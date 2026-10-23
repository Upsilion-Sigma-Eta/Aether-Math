using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class ClipboardCases
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    public static IEnumerable<QaCase> All()
    {
        yield return new("CLIP-01", "제어 reader로 붙여넣기 후 한 단계 undo", ApplyAndUndo);
        yield return new("CLIP-02", "clipboard 없음·빈 결과·읽기 실패의 모델/이력 보존", ReadOutcomes);
        yield return new("CLIP-03", "읽기 중 Session 교체와 현재 편집 대상 변경 결과 폐기", StaleSessionAndTarget);
        yield return new("CLIP-04", "같은 Session의 detach·재부착과 늦은 응답 폐기·재시도", DetachAndReattach);
        yield return new("CLIP-05", "연속 paste FIFO 및 사용자 편집·Session 변경 시 대기열 폐기", FifoAndQueuedSession);
        yield return new("CLIP-06", "caret 왕복은 대기 paste를 무효화하고 저장 상태만 바뀌면 유지", GenerationAndSavedState);
    }

    private static MathEditorControl AttachedControl(EditingSession session)
    {
        var control = new MathEditorControl { Session = session };
        control.SetVisualTreeAttachmentState(attached: true);
        return control;
    }

    private static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertUnchanged(EditingSession session, EditSnapshot snapshot, string label) =>
        Qa.Equal(snapshot, Snapshot(session), label);

    private static EditSnapshot Snapshot(EditingSession session) => new(
        Qa.Fingerprint(session.Workspace), session.Revision, session.Caret, session.Selection,
        session.SelectedNodeId, session.IsNodeSelected, session.CanUndo, session.CanRedo, session.IsDirty);

    private static async Task ApplyAndUndo()
    {
        var text = Qa.Text("x");
        var session = new EditingSession(Qa.Workspace(text));
        Qa.Caret(session, text.Id, 1);
        var control = AttachedControl(session);
        control.ClipboardTextReaderOverride = _ => Task.FromResult<string?>("𝑥🙂");

        var result = await control.PasteFromClipboardAsync().WaitAsync(WaitLimit);

        Qa.Equal(ClipboardPasteResult.Applied, result, "붙여넣기 결과");
        Qa.Equal("x𝑥🙂", Qa.Leaf(session, text.Id).Text, "Unicode 붙여넣기 원문");
        Qa.Equal(new MathCaret(text.Id, 3), session.Caret, "붙여넣기 후 문자소 caret");
        Qa.Check(session.CanUndo && session.IsDirty, "붙여넣기 이력/dirty 없음");
        session.Undo();
        Qa.Equal("x", Qa.Leaf(session, text.Id).Text, "undo가 붙여넣기 트랜잭션 하나를 복원");
        Qa.Equal(new MathCaret(text.Id, 1), session.Caret, "붙여넣기 undo caret 복원");
        Qa.Check(!session.CanUndo && !session.IsDirty, "붙여넣기 undo 이후 추가 이력/dirty");
        control.SetVisualTreeAttachmentState(attached: false);
    }

    private static async Task ReadOutcomes()
    {
        var text = Qa.Text("stable");
        var session = new EditingSession(Qa.Workspace(text));
        Qa.Caret(session, text.Id, 3);
        var control = AttachedControl(session);
        var before = Snapshot(session);

        control.ClipboardTextReaderOverride = null;
        Qa.Equal(ClipboardPasteResult.NoClipboard,
            await control.PasteFromClipboardAsync().WaitAsync(WaitLimit), "TopLevel 없는 clipboard 결과");
        AssertUnchanged(session, before, "clipboard 없음");

        control.ClipboardTextReaderOverride = _ => Task.FromResult<string?>(null);
        Qa.Equal(ClipboardPasteResult.Empty,
            await control.PasteFromClipboardAsync().WaitAsync(WaitLimit), "빈 clipboard 결과");
        AssertUnchanged(session, before, "빈 clipboard");

        control.ClipboardTextReaderOverride = _ => Task.FromException<string?>(new IOException("reader 실패"));
        Qa.Equal(ClipboardPasteResult.ReadFailed,
            await control.PasteFromClipboardAsync().WaitAsync(WaitLimit), "reader 오류 결과");
        AssertUnchanged(session, before, "reader 오류");

        control.ClipboardTextReaderOverride = _ => Task.FromResult<string?>("R");
        Qa.Equal(ClipboardPasteResult.Applied,
            await control.PasteFromClipboardAsync().WaitAsync(WaitLimit), "reader 오류 뒤 재시도 결과");
        Qa.Equal("staRble", Qa.Leaf(session, text.Id).Text, "reader 오류 뒤 재시도 내용");
        session.Undo();
        Qa.Equal(before.Workspace, Qa.Fingerprint(session.Workspace), "reader 오류 뒤 재시도 undo 모델");
        Qa.Equal(new MathCaret(text.Id, 3), session.Caret, "reader 오류 뒤 재시도 undo caret");
        Qa.Check(!session.CanUndo && !session.IsDirty, "reader 오류 뒤 재시도 undo가 추가 history/dirty를 남김");

        var beforeNoSession = Snapshot(session);
        control.Session = null;
        var oldReaderCalls = 0;
        control.ClipboardTextReaderOverride = _ =>
        {
            oldReaderCalls++;
            return Task.FromResult<string?>("예상 밖");
        };
        Qa.Equal(ClipboardPasteResult.NoSession,
            await control.PasteFromClipboardAsync().WaitAsync(WaitLimit), "Session 없는 결과");
        Qa.Equal(0, oldReaderCalls, "Session 없이 reader 실행");
        AssertUnchanged(session, beforeNoSession, "Session 없음");
        control.SetVisualTreeAttachmentState(attached: false);
    }

    private static async Task StaleSessionAndTarget()
    {
        var oldText = Qa.Text("old");
        var oldSession = new EditingSession(Qa.Workspace(oldText));
        Qa.Caret(oldSession, oldText.Id, 1);
        var control = AttachedControl(oldSession);
        var readStarted = Signal<bool>();
        var read = Signal<string?>();
        control.ClipboardTextReaderOverride = _ =>
        {
            readStarted.TrySetResult(true);
            return read.Task;
        };
        var pending = control.PasteFromClipboardAsync();
        await readStarted.Task.WaitAsync(WaitLimit);

        var newText = Qa.Text("new");
        var newSession = new EditingSession(Qa.Workspace(newText));
        var oldBefore = Snapshot(oldSession);
        var newBefore = Snapshot(newSession);
        control.Session = newSession;
        read.SetResult("stale");
        Qa.Equal(ClipboardPasteResult.Canceled, await pending.WaitAsync(WaitLimit), "교체된 Session의 reader 취소 결과");
        AssertUnchanged(oldSession, oldBefore, "오래된 Session");
        AssertUnchanged(newSession, newBefore, "새 Session");
        Qa.Check(ReferenceEquals(newSession, control.Session), "오래된 paste가 control Session을 바꿈");
        control.SetVisualTreeAttachmentState(attached: false);

        await AssertStaleTarget("caret 이동", (session, target) =>
            Qa.Check(session.TrySetCaret(new MathCaret(target.Id, 2)), "caret 이동 실패"),
            (session, target) => { });

        await AssertStaleTarget("텍스트 선택 변경", (session, target) =>
            Qa.Check(session.TrySetSelection(new MathSelection(new(target.Id, 0), new(target.Id, 2))), "selection 변경 실패"),
            (session, target) => { });

        await AssertStaleTarget("선택 노드 변경", (session, target) =>
            Qa.Check(session.TrySelectNode(target.Id), "노드 선택 변경 실패"),
            (session, target) => { });

        await AssertStaleTarget("수식 revision 변경", (session, target) =>
        {
            Qa.Caret(session, target.Id, Qa.Elements(target.Text));
            Qa.Check(session.InsertText("local"), "경쟁 편집 실패");
        }, (session, target) =>
        {
            session.Undo();
            Qa.Equal("abc", Qa.Leaf(session, target.Id).Text, "stale paste가 경쟁 편집 undo를 추가하지 않음");
            Qa.Check(!session.CanUndo, "경쟁 편집 한 번 undo 뒤 잔여 paste 이력");
        });
    }

    private static async Task AssertStaleTarget(string label,
        Action<EditingSession, TextNode> mutate,
        Action<EditingSession, TextNode> afterStale,
        bool expectStateRestored = false)
    {
        var target = Qa.Text("abc");
        var session = new EditingSession(Qa.Workspace(target));
        Qa.Caret(session, target.Id, 1);
        var control = AttachedControl(session);
        var initialState = Snapshot(session);
        var readStarted = Signal<bool>();
        var read = Signal<string?>();
        control.ClipboardTextReaderOverride = _ =>
        {
            readStarted.TrySetResult(true);
            return read.Task;
        };
        var pending = control.PasteFromClipboardAsync();
        await readStarted.Task.WaitAsync(WaitLimit);
        mutate(session, target);
        var afterMutation = Snapshot(session);
        if (expectStateRestored)
            Qa.Equal(initialState, afterMutation, label + "이 끝난 뒤 편집 상태가 원래 값과 같음");
        read.SetResult("stale");

        Qa.Equal(ClipboardPasteResult.StaleTarget, await pending.WaitAsync(WaitLimit), label + " 결과");
        AssertUnchanged(session, afterMutation, label + " 상태 보존");
        afterStale(session, target);
        control.SetVisualTreeAttachmentState(attached: false);
    }

    private static async Task DetachAndReattach()
    {
        var text = Qa.Text("keep");
        var session = new EditingSession(Qa.Workspace(text));
        Qa.Caret(session, text.Id, 2);
        var control = AttachedControl(session);
        var tokenObserved = Signal<CancellationToken>();
        var lateRead = Signal<string?>();
        var readCount = 0;
        control.ClipboardTextReaderOverride = token =>
        {
            Interlocked.Increment(ref readCount);
            tokenObserved.TrySetResult(token);
            return lateRead.Task; // 취소를 일부러 무시하는 clipboard backend를 재현한다.
        };
        var before = Snapshot(session);
        var pending = control.PasteFromClipboardAsync();
        var token = await tokenObserved.Task.WaitAsync(WaitLimit);
        var queued = control.PasteFromClipboardAsync();

        control.SetVisualTreeAttachmentState(attached: false);
        Qa.Check(token.IsCancellationRequested, "detach가 진행 중 reader token을 취소하지 않음");
        control.SetVisualTreeAttachmentState(attached: true);

        Qa.Equal(ClipboardPasteResult.Canceled, await pending.WaitAsync(WaitLimit), "attach epoch 변경 결과");
        Qa.Equal(ClipboardPasteResult.StaleSession, await queued.WaitAsync(WaitLimit), "attach epoch 이전 큐 작업 결과");
        AssertUnchanged(session, before, "detach/재부착 이후 Session");
        lateRead.SetResult("도착이 늦은 stale 텍스트");
        await Task.Yield();
        AssertUnchanged(session, before, "취소를 무시한 늦은 reader 응답");
        Qa.Equal(1, Volatile.Read(ref readCount), "이전 attach 큐에서 reader 재호출");

        control.ClipboardTextReaderOverride = _ => Task.FromResult<string?>("R");
        Qa.Equal(ClipboardPasteResult.Applied,
            await control.PasteFromClipboardAsync().WaitAsync(WaitLimit), "재부착 뒤 새 paste 재시도 결과");
        Qa.Equal("keRep", Qa.Leaf(session, text.Id).Text, "재부착 뒤 새 paste 재시도 내용");
        control.SetVisualTreeAttachmentState(attached: false);
    }

    private static async Task FifoAndQueuedSession()
    {
        var text = Qa.Text("x");
        var session = new EditingSession(Qa.Workspace(text));
        Qa.Caret(session, text.Id, 1);
        var control = AttachedControl(session);
        var firstStarted = Signal<bool>();
        var secondStarted = Signal<bool>();
        var firstRead = Signal<string?>();
        var secondRead = Signal<string?>();
        var readNumber = 0;
        var secondStartedBeforeFirstReadCompleted = 0;
        control.ClipboardTextReaderOverride = _ => Interlocked.Increment(ref readNumber) switch
        {
            1 => Start(firstStarted, firstRead.Task),
            2 => StartSecond(secondStarted, secondRead.Task, firstRead,
                () => Interlocked.Exchange(ref secondStartedBeforeFirstReadCompleted, 1)),
            _ => Task.FromResult<string?>("unexpected")
        };

        var first = control.PasteFromClipboardAsync();
        await firstStarted.Task.WaitAsync(WaitLimit);
        var second = control.PasteFromClipboardAsync();

        firstRead.SetResult("A");
        Qa.Equal(ClipboardPasteResult.Applied, await first.WaitAsync(WaitLimit), "첫 paste");
        await secondStarted.Task.WaitAsync(WaitLimit);
        Qa.Equal(0, Volatile.Read(ref secondStartedBeforeFirstReadCompleted), "두 번째 reader가 첫 read 완료 전에 시작");
        secondRead.SetResult("B");
        Qa.Equal(ClipboardPasteResult.Applied, await second.WaitAsync(WaitLimit), "두 번째 paste");
        Qa.Equal("xAB", Qa.Leaf(session, text.Id).Text, "FIFO 순서와 앞 paste 이후 caret");
        session.Undo(); Qa.Equal("xA", Qa.Leaf(session, text.Id).Text, "두 번째 paste undo");
        session.Undo(); Qa.Equal("x", Qa.Leaf(session, text.Id).Text, "첫 paste undo");
        control.SetVisualTreeAttachmentState(attached: false);

        var queuedText = Qa.Text("old");
        var queuedSession = new EditingSession(Qa.Workspace(queuedText));
        var queuedControl = AttachedControl(queuedSession);
        var queuedFirstStarted = Signal<bool>();
        var queuedFirstRead = Signal<string?>();
        var queuedReadCount = 0;
        queuedControl.ClipboardTextReaderOverride = _ =>
        {
            Interlocked.Increment(ref queuedReadCount);
            queuedFirstStarted.TrySetResult(true);
            return queuedFirstRead.Task;
        };
        var pendingFirst = queuedControl.PasteFromClipboardAsync();
        await queuedFirstStarted.Task.WaitAsync(WaitLimit);
        var pendingSecond = queuedControl.PasteFromClipboardAsync();
        var replacementText = Qa.Text("new");
        var replacement = new EditingSession(Qa.Workspace(replacementText));
        var replacementBefore = Snapshot(replacement);
        queuedControl.Session = replacement;
        queuedFirstRead.SetResult("stale");

        Qa.Equal(ClipboardPasteResult.Canceled, await pendingFirst.WaitAsync(WaitLimit), "기존 작업 첫 paste 취소");
        Qa.Equal(ClipboardPasteResult.StaleSession, await pendingSecond.WaitAsync(WaitLimit), "enqueue 후 Session 변경된 대기 paste");
        Qa.Equal(1, Volatile.Read(ref queuedReadCount), "대기 중 Session 교체 뒤 reader 재호출");
        AssertUnchanged(replacement, replacementBefore, "교체 Session으로 대기 paste 누출");
        queuedControl.SetVisualTreeAttachmentState(attached: false);

        await AssertQueuedTargetInvalidation("caret를 옮겼다가 원래 위치로 복원", session =>
        {
            Qa.Check(session.TrySetCaret(new MathCaret(session.Caret.NodeId, 2)), "임시 caret 이동 실패");
            Qa.Check(session.TrySetCaret(new MathCaret(session.Caret.NodeId, 1)), "caret 원위치 복귀 실패");
        }, "abc", expectStateRestored: true);

        await AssertQueuedTargetInvalidation("대기 중 사용자 내용 편집", session =>
        {
            Qa.Check(session.InsertText("USER"), "대기 중 사용자 내용 편집 실패");
        }, "aUSERbc");
    }

    private static async Task AssertQueuedTargetInvalidation(
        string label, Action<EditingSession> mutate, string expectedText, bool expectStateRestored = false)
    {
        var text = Qa.Text("abc");
        var session = new EditingSession(Qa.Workspace(text));
        Qa.Caret(session, text.Id, 1);
        var initialState = Snapshot(session);
        var control = AttachedControl(session);
        var readStarted = Signal<bool>();
        var read = Signal<string?>();
        var readCount = 0;
        control.ClipboardTextReaderOverride = _ =>
        {
            Interlocked.Increment(ref readCount);
            readStarted.TrySetResult(true);
            return read.Task;
        };

        var first = control.PasteFromClipboardAsync();
        await readStarted.Task.WaitAsync(WaitLimit);
        var second = control.PasteFromClipboardAsync();
        mutate(session);
        var afterUserChange = Snapshot(session);
        if (expectStateRestored)
            Qa.Equal(initialState, afterUserChange, label + " 이후 동일 상태 복귀");
        read.SetResult("stale");

        Qa.Equal(ClipboardPasteResult.StaleTarget, await first.WaitAsync(WaitLimit), label + " 첫 요청 결과");
        Qa.Equal(ClipboardPasteResult.StaleTarget, await second.WaitAsync(WaitLimit), label + " 대기 요청 결과");
        AssertUnchanged(session, afterUserChange, label + " 모델·선택·이력 보존");
        Qa.Equal(expectedText, string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(node => node.Text)),
            label + " 사용자 변경 보존");
        Qa.Equal(1, Volatile.Read(ref readCount), label + " 무효화된 대기 요청의 reader 재호출");
        control.SetVisualTreeAttachmentState(attached: false);
    }

    private static async Task GenerationAndSavedState()
    {
        await AssertStaleTarget("caret를 옮겼다가 원래 위치로 복원", (session, target) =>
        {
            Qa.Check(session.TrySetCaret(new MathCaret(target.Id, 2)), "임시 caret 이동 실패");
            Qa.Check(session.TrySetCaret(new MathCaret(target.Id, 1)), "caret 원위치 복귀 실패");
        }, (session, target) => { }, expectStateRestored: true);

        var text = Qa.Text("abc");
        var session = new EditingSession(Qa.Workspace(text));
        Qa.Caret(session, text.Id, 1);
        Qa.Check(session.InsertText("!"), "MarkSaved 시험의 사전 dirty 편집 실패");
        var control = AttachedControl(session);
        var readStarted = Signal<bool>();
        var read = Signal<string?>();
        control.ClipboardTextReaderOverride = _ =>
        {
            readStarted.TrySetResult(true);
            return read.Task;
        };
        var pending = control.PasteFromClipboardAsync();
        await readStarted.Task.WaitAsync(WaitLimit);

        Qa.Check(session.IsDirty, "MarkSaved 이전 dirty 상태 누락");
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Check(!session.IsDirty, "MarkSaved가 dirty 상태를 갱신하지 않음");
        read.SetResult("P");

        Qa.Equal(ClipboardPasteResult.Applied, await pending.WaitAsync(WaitLimit), "MarkSaved 뒤 paste 결과");
        Qa.Equal("a!Pbc", Qa.Leaf(session, text.Id).Text, "MarkSaved 뒤 paste 내용");
        control.SetVisualTreeAttachmentState(attached: false);
    }

    private static Task<string?> Start(TaskCompletionSource<bool> started, Task<string?> result)
    {
        started.TrySetResult(true);
        return result;
    }

    private static Task<string?> StartSecond(
        TaskCompletionSource<bool> started,
        Task<string?> result,
        TaskCompletionSource<string?> firstRead,
        Action markStartedEarly)
    {
        if (!firstRead.Task.IsCompleted)
            markStartedEarly();
        started.TrySetResult(true);
        return result;
    }

    private readonly record struct EditSnapshot(
        string Workspace,
        long Revision,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        bool CanUndo,
        bool CanRedo,
        bool IsDirty);
}
