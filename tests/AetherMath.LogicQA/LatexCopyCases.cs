using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class LatexCopyCases
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    public static IEnumerable<QaCase> All()
    {
        yield return new("LATEX-COPY-SUCCESS", "성공 복사와 편집 상태·경로·미리보기 보존", SuccessAndPurity);
        yield return new("LATEX-COPY-GATES", "차단 진단·행 간격 안내·sink 사용 불가 경계", BlockingAdvisoryAndUnavailable);
        yield return new("LATEX-COPY-BUSY", "복사 대기 중 재진입 차단과 이전 메시지 정리", BusyReentry);
        yield return new("LATEX-COPY-SNAPSHOT", "호출 시점 코드와 편집·Undo·세션 교체·선택 이동 경계", FrozenSnapshotAndNavigation);
        yield return new("LATEX-COPY-FAIL-CANCEL", "동기·비동기 실패와 취소 후 재시도", FailureCancellationAndRetry);
        yield return new("LATEX-COPY-NOTIFY-ORDER", "코드·진단 상태 알림의 원자성 및 재진입 복사 차단", NotificationPublicationAndReentry);
    }

    private static async Task SuccessAndPurity()
    {
        using var directory = new TempDirectory();
        var vm = new MainWindowViewModel(new WorkspaceFileService());
        var path = directory.File("copy-source.aethermath");
        await vm.SaveToPathAsync(path);
        var source = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First(node => node.Text.Length > 0);
        Qa.Select(vm.Session, source.Id, 0, 1);
        vm.IsOutputPreviewVisible = true;
        var before = Capture(vm);
        var calls = 0;
        string? received = null;

        var result = await vm.CopyLatexAsync(text =>
        {
            calls++;
            received = text;
            return Task.CompletedTask;
        });

        Qa.Equal(LatexCopyStatus.Copied, result.Status, "정상 sink 복사 결과");
        Qa.Equal(1, calls, "정상 sink 호출 횟수");
        Qa.Equal(before.LatexCode, received, "sink에 전달된 전체 호출 시점 LaTeX");
        AssertUnchanged(before, Capture(vm), "정상 복사 뒤 편집 상태");
        Qa.Check(vm.LatexCopyMessage.Contains("복사", StringComparison.Ordinal), "성공 결과 메시지 누락");
        Qa.Check(!string.Equals(vm.LatexCopyMessage, vm.LatexCodeDiagnostic, StringComparison.Ordinal),
            "복사 결과 메시지가 코드 진단과 같은 상태로 덮임");
        Qa.Check(vm.CanCopyLatex && !vm.IsCopyingLatex, "성공 뒤 복사 가능 상태 복원");
    }

    private static async Task BlockingAdvisoryAndUnavailable()
    {
        var advisoryVm = new MainWindowViewModel();
        Qa.Check(!advisoryVm.LatexLayoutDiagnostics.IsEmpty, "기본 다중 행 fixture의 행 간격 안내가 없음");
        Qa.Check(advisoryVm.CanExportLatex && advisoryVm.CanCopyLatex,
            "비차단 행 간격 안내만으로 복사를 막음");
        Qa.Check(advisoryVm.LatexCodeDiagnostic.Contains("행 간격", StringComparison.Ordinal),
            "행 간격 안내와 복사 결과 경계용 진단이 없음");
        var advisoryCalls = new List<string>();
        var advisoryResult = await advisoryVm.CopyLatexAsync(text =>
        {
            advisoryCalls.Add(text);
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Copied, advisoryResult.Status, "행 간격 안내가 있는 코드 복사");
        Qa.Equal(1, advisoryCalls.Count, "행 간격 안내 sink 호출 횟수");
        Qa.Equal(advisoryVm.LatexCode, advisoryCalls[0], "행 간격 안내 fixture의 전달 코드");

        var unavailableVm = new MainWindowViewModel();
        var unavailableBefore = Capture(unavailableVm);
        var unavailable = await unavailableVm.CopyLatexAsync(null);
        Qa.Equal(LatexCopyStatus.Unavailable, unavailable.Status, "null sink 결과");
        AssertUnchanged(unavailableBefore, Capture(unavailableVm), "null sink 뒤 편집 상태");
        Qa.Check(!string.IsNullOrWhiteSpace(unavailableVm.LatexCopyMessage), "null sink 결과 안내 누락");

        var controlledVm = new MainWindowViewModel();
        var controlledText = Qa.Nodes(controlledVm.Session.Workspace).OfType<TextNode>()
            .First(node => node.Text.Length > 0);
        Qa.Caret(controlledVm.Session, controlledText.Id, controlledText.Text.Length);
        Qa.Check(controlledVm.Session.InsertText("\u0001"), "제어 문자 차단 fixture 입력");
        Qa.Check(!controlledVm.CanExportLatex && !controlledVm.CanCopyLatex &&
                 !controlledVm.LatexTextDiagnostics.IsEmpty,
            "제어 문자 진단 상태가 복사 가능을 차단하지 않음");
        var blockedCalls = 0;
        var controlledBefore = Capture(controlledVm);
        var controlledBlocked = await controlledVm.CopyLatexAsync(_ =>
        {
            blockedCalls++;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Blocked, controlledBlocked.Status, "제어 문자 코드 차단 결과");
        Qa.Equal(0, blockedCalls, "제어 문자 차단에서 sink가 호출됨");
        AssertUnchanged(controlledBefore, Capture(controlledVm), "제어 문자 차단 뒤 편집 상태");

        var incompleteVm = new MainWindowViewModel();
        Qa.Equal(WorkspaceActionRequestResult.Completed, incompleteVm.RequestNewWorkspace(), "빈 작업 차단 fixture 전환");
        Qa.Check(!incompleteVm.CanExportLatex && !incompleteVm.LatexIncompleteSlotNodeIds.IsEmpty,
            "필수 빈 슬롯 진단이 복사 차단 fixture에 없음");
        var incompleteCalls = 0;
        var incomplete = await incompleteVm.CopyLatexAsync(_ =>
        {
            incompleteCalls++;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Blocked, incomplete.Status, "필수 빈 슬롯 코드 차단 결과");
        Qa.Equal(0, incompleteCalls, "빈 슬롯 차단에서 sink가 호출됨");
    }

    private static async Task BusyReentry()
    {
        var vm = new MainWindowViewModel();
        var seed = await vm.CopyLatexAsync(_ => Task.CompletedTask);
        Qa.Equal(LatexCopyStatus.Copied, seed.Status, "busy fixture의 이전 성공 복사");
        Qa.Check(!string.IsNullOrWhiteSpace(vm.LatexCopyMessage), "busy fixture 이전 복사 메시지 누락");

        var gate = new CopyGate();
        var before = Capture(vm);
        var firstCopy = vm.CopyLatexAsync(gate.WriteAsync);
        await gate.WaitUntilStartedAsync();
        Qa.Check(vm.IsCopyingLatex && !vm.CanCopyLatex, "대기 중 복사 상태/CanCopy 갱신 실패");
        Qa.Equal(string.Empty, vm.LatexCopyMessage, "새 복사 시작 때 이전 결과 메시지가 지워지지 않음");
        Qa.Check(vm.LatexCopyDisabledReason.Contains("진행", StringComparison.Ordinal),
            "busy 비활성 사유가 진행 상태를 나타내지 않음");

        var secondCalls = 0;
        var busy = await vm.CopyLatexAsync(_ =>
        {
            secondCalls++;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Busy, busy.Status, "busy 재진입 결과");
        Qa.Equal(0, secondCalls, "busy 재진입에서 두 번째 sink 호출");
        Qa.Equal(1, gate.Calls, "busy 재진입에서 첫 sink 호출 수 변경");
        Qa.Equal(string.Empty, vm.LatexCopyMessage, "busy 재진입이 현재 상태 메시지를 덮음");
        AssertUnchanged(before, Capture(vm), "busy 재진입 뒤 편집 상태");

        gate.Release();
        var completed = await firstCopy.WaitAsync(WaitLimit);
        Qa.Equal(LatexCopyStatus.Copied, completed.Status, "기존 busy 복사 완료 결과");
        Qa.Equal(1, gate.Calls, "기존 busy 복사 sink 호출 횟수");
        AssertUnchanged(before, Capture(vm), "대기 복사 완료 뒤 편집 상태");
        Qa.Check(!vm.IsCopyingLatex && vm.CanCopyLatex, "busy 완료 뒤 CanCopy 복구");
    }

    private static async Task FrozenSnapshotAndNavigation()
    {
        var vm = new MainWindowViewModel();
        var session = vm.Session;
        var originalSource = vm.LatexCode;
        var originalState = Capture(vm);

        var navigationGate = new CopyGate();
        var navigationCopy = vm.CopyLatexAsync(navigationGate.WriteAsync);
        await navigationGate.WaitUntilStartedAsync();
        var anotherText = Qa.Nodes(session.Workspace).OfType<TextNode>()
            .First(node => node.Id != session.Caret.NodeId && node.Text.Length > 0);
        Qa.Caret(session, anotherText.Id, 0);
        var navigated = Capture(vm);
        Qa.Equal(originalState.Revision, navigated.Revision, "선택 이동만으로 revision 변경");
        Qa.Equal(originalState.StateId, navigated.StateId, "선택 이동만으로 state ID 변경");
        navigationGate.Release();
        var navigationResult = await navigationCopy.WaitAsync(WaitLimit);
        Qa.Equal(LatexCopyStatus.Copied, navigationResult.Status, "선택 이동만 한 복사 결과");
        Qa.Equal(originalSource, navigationGate.Sources.Single(), "선택 이동 중 복사 코드");
        AssertUnchanged(navigated, Capture(vm), "선택 이동 중 복사 완료 뒤 상태");

        var beforeEdit = Capture(vm);
        var editGate = new CopyGate();
        var editCopy = vm.CopyLatexAsync(editGate.WriteAsync);
        await editGate.WaitUntilStartedAsync();
        var editText = Qa.Nodes(session.Workspace).OfType<TextNode>()
            .First(node => !node.IsEmptySlot && node.Text.Length > 0);
        Qa.Caret(session, editText.Id, editText.Text.Length);
        Qa.Check(session.InsertText("!"), "복사 대기 중 edit fixture 변경");
        session.Undo();
        var editedThenUndone = Capture(vm);
        Qa.Equal(beforeEdit.StateId, editedThenUndone.StateId, "복사 대기 중 edit+undo가 원 state ID로 복귀하지 않음");
        Qa.Check(editedThenUndone.Revision > beforeEdit.Revision, "복사 대기 중 edit+undo revision이 변하지 않음");
        Qa.Equal(beforeEdit.WorkspaceFingerprint, editedThenUndone.WorkspaceFingerprint,
            "복사 대기 중 edit+undo가 원 AST를 복원하지 않음");
        editGate.Release();
        var editedResult = await editCopy.WaitAsync(WaitLimit);
        Qa.Equal(LatexCopyStatus.CopiedPreviousSnapshot, editedResult.Status,
            "revision이 바뀐 edit+undo를 최신 snapshot으로 오인");
        Qa.Equal(beforeEdit.LatexCode, editGate.Sources.Single(), "edit+undo 대기 중 전송 코드가 요청 snapshot과 다름");
        AssertUnchanged(editedThenUndone, Capture(vm), "edit+undo 복사 완료 뒤 현재 상태");

        var oldSession = vm.Session;
        var beforeReplacement = Capture(vm);
        var replacementGate = new CopyGate();
        var replacementCopy = vm.CopyLatexAsync(replacementGate.WriteAsync);
        await replacementGate.WaitUntilStartedAsync();
        Qa.Equal(WorkspaceActionRequestResult.Completed, vm.RequestNewWorkspace(), "복사 대기 중 새 작업 전환");
        Qa.Check(!ReferenceEquals(oldSession, vm.Session), "복사 대기 중 새 작업이 세션을 교체하지 않음");
        var replacementState = Capture(vm);
        replacementGate.Release();
        var replacementResult = await replacementCopy.WaitAsync(WaitLimit);
        Qa.Equal(LatexCopyStatus.CopiedPreviousSnapshot, replacementResult.Status,
            "Session 교체 후 원 snapshot 복사 결과");
        Qa.Equal(beforeReplacement.LatexCode, replacementGate.Sources.Single(), "Session 교체 중 전송된 원 코드");
        AssertUnchanged(replacementState, Capture(vm), "Session 교체 뒤 복사 완료가 새 작업을 변경");
    }

    private static async Task FailureCancellationAndRetry()
    {
        var vm = new MainWindowViewModel();
        var before = Capture(vm);
        var syncFailureCalls = 0;
        var syncFailure = await vm.CopyLatexAsync(_ =>
        {
            syncFailureCalls++;
            throw new IOException("sink sync failure");
        });
        Qa.Equal(LatexCopyStatus.Failed, syncFailure.Status, "동기 sink 예외 결과");
        Qa.Equal(1, syncFailureCalls, "동기 sink 예외 호출 횟수");
        AssertUnchanged(before, Capture(vm), "동기 실패 뒤 편집 상태");

        var asyncFailureCalls = 0;
        var asyncFailure = await vm.CopyLatexAsync(_ =>
        {
            asyncFailureCalls++;
            return Task.FromException(new IOException("sink async failure"));
        });
        Qa.Equal(LatexCopyStatus.Failed, asyncFailure.Status, "비동기 sink 예외 결과");
        Qa.Equal(1, asyncFailureCalls, "비동기 sink 예외 호출 횟수");
        AssertUnchanged(before, Capture(vm), "비동기 실패 뒤 편집 상태");

        var syncCancellation = await vm.CopyLatexAsync(_ => throw new OperationCanceledException("sink sync cancel"));
        Qa.Equal(LatexCopyStatus.Canceled, syncCancellation.Status, "동기 sink 취소 결과");
        AssertUnchanged(before, Capture(vm), "동기 취소 뒤 편집 상태");

        var asyncCancellation = await vm.CopyLatexAsync(_ => Task.FromException(new OperationCanceledException("sink async cancel")));
        Qa.Equal(LatexCopyStatus.Canceled, asyncCancellation.Status, "비동기 sink 취소 결과");
        AssertUnchanged(before, Capture(vm), "비동기 취소 뒤 편집 상태");

        var retryCalls = 0;
        var retry = await vm.CopyLatexAsync(text =>
        {
            retryCalls++;
            Qa.Equal(before.LatexCode, text, "실패/취소 뒤 재시도 코드");
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Copied, retry.Status, "실패/취소 뒤 재시도 결과");
        Qa.Equal(1, retryCalls, "재시도 sink 호출 횟수");
        AssertUnchanged(before, Capture(vm), "재시도 뒤 편집 상태");
        Qa.Check(!vm.IsCopyingLatex && vm.CanCopyLatex, "실패/취소/재시도 뒤 복사 가능 상태 복원");
    }

    private static async Task NotificationPublicationAndReentry()
    {
        var vm = new MainWindowViewModel();
        var sinkSources = new List<string>();
        var publicationCopies = new List<(bool CanExport, string Source, Task<LatexCopyResult> Task)>();
        Task<LatexCopyResult>? sessionNotificationCopy = null;
        var publicationEvents = 0;
        var sessionNotificationCalls = 0;

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.Session))
            {
                Qa.Check(!vm.CanCopyLatex,
                    "새 Session 알림에서 현재 코드 캐시가 교체 전인데도 복사가 허용됨");
                Qa.Check(vm.LatexCopyDisabledReason.Contains("갱신", StringComparison.Ordinal),
                    "새 Session 알림에서 코드 갱신 중 비활성 사유 누락");
                sessionNotificationCopy = vm.CopyLatexAsync(_ =>
                {
                    sessionNotificationCalls++;
                    return Task.CompletedTask;
                });
                return;
            }

            if (args.PropertyName is not (nameof(MainWindowViewModel.LatexCode) or
                nameof(MainWindowViewModel.LatexTextDiagnostics) or
                nameof(MainWindowViewModel.LatexLayoutDiagnostics) or
                nameof(MainWindowViewModel.LatexIncompleteSlotNodeIds) or
                nameof(MainWindowViewModel.CanExportLatex) or
                nameof(MainWindowViewModel.LatexCodeDiagnostic)))
                return;

            AssertPublishedLatexState(vm, args.PropertyName ?? "(null)");
            if (args.PropertyName != nameof(MainWindowViewModel.LatexCode))
                return;

            publicationEvents++;
            var expectedCanExport = vm.LatexTextDiagnostics.IsEmpty && vm.LatexIncompleteSlotNodeIds.IsEmpty;
            var expectedSource = vm.LatexCode;
            var copyTask = vm.CopyLatexAsync(text =>
            {
                sinkSources.Add(text);
                return Task.CompletedTask;
            });
            publicationCopies.Add((expectedCanExport, expectedSource, copyTask));
        };

        var sourceText = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>()
            .First(node => !node.IsEmptySlot && node.Text.Length > 0);
        Qa.Caret(vm.Session, sourceText.Id, sourceText.Text.Length);
        Qa.Check(vm.Session.InsertText("\u0001"), "알림 경계 제어 문자 삽입");
        var controlled = Qa.Leaf(vm.Session, sourceText.Id);
        Qa.Select(vm.Session, controlled.Id, controlled.Text.Length - 1, controlled.Text.Length);
        Qa.Check(vm.Session.DeleteSelection(), "알림 경계 제어 문자 삭제");
        Qa.Check(vm.LatexCopyMessage.Contains("복사", StringComparison.Ordinal),
            "같은 Commit의 늦은 Revision 알림이 성공 복사 메시지를 지움");
        vm.Session.MarkSaved(vm.Session.CaptureSnapshot());
        Qa.Equal(WorkspaceActionRequestResult.Completed, vm.RequestNewWorkspace(), "Session 교체 알림 fixture");
        var blankSlot = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().Single(node => node.IsEmptySlot);
        Qa.Caret(vm.Session, blankSlot.Id, 0);
        Qa.Check(vm.Session.InsertText("ready"), "필수 슬롯 알림 해제 입력");
        Qa.Check(vm.LatexCopyMessage.Contains("복사", StringComparison.Ordinal),
            "복구 Commit의 늦은 Revision 알림이 성공 복사 메시지를 지움");

        Qa.Check(sessionNotificationCopy is not null, "Session 변경 알림 중 복사 재진입이 발생하지 않음");
        var sessionResult = await sessionNotificationCopy!;
        Qa.Equal(LatexCopyStatus.Blocked, sessionResult.Status, "코드 publish 전 Session 변경 복사 결과");
        Qa.Equal(0, sessionNotificationCalls, "코드 publish 전 Session 변경에서 stale sink 호출");
        Qa.Equal(4, publicationEvents, "유효/제어/빈 슬롯/복구 코드 상태 알림 수");
        Qa.Equal(2, publicationCopies.Count(item => item.CanExport), "허용 상태 코드 갱신 수");
        Qa.Equal(2, publicationCopies.Count(item => !item.CanExport), "차단 상태 코드 갱신 수");

        foreach (var item in publicationCopies)
        {
            var result = await item.Task;
            Qa.Equal(item.CanExport ? LatexCopyStatus.Copied : LatexCopyStatus.Blocked,
                result.Status, "코드 알림 중 재진입 복사 결과");
        }
        Qa.Equal(2, sinkSources.Count, "차단된 상태에서 reentrant sink 호출");
        var expectedSources = publicationCopies.Where(item => item.CanExport).Select(item => item.Source).ToArray();
        Qa.Check(expectedSources.Length == 2 && !string.Equals(expectedSources[0], expectedSources[1], StringComparison.Ordinal),
            "서로 다른 허용 코드 publish의 호출 시점 source fixture가 구별되지 않음");
        Qa.Check(sinkSources.SequenceEqual(expectedSources), "허용 코드 알림의 sink가 각 호출 시점 소스와 불일치");
        Qa.Check(vm.CanExportLatex && vm.CanCopyLatex, "마지막 유효 코드 publish 뒤 복사 상태");
    }

    private static void AssertPublishedLatexState(MainWindowViewModel vm, string propertyName)
    {
        var workspace = vm.Session.Workspace;
        Qa.Equal(LatexSerializer.ToLatex(workspace), vm.LatexCode,
            propertyName + " 알림 중 실제 Workspace와 코드가 불일치");
        var textDiagnostics = LatexSerializer.GetTextDiagnostics(workspace);
        var layoutDiagnostics = LatexSerializer.GetLayoutDiagnostics(workspace);
        var incompleteSlots = LatexSerializer.GetIncompleteSlotNodeIds(workspace);
        Qa.Check(textDiagnostics.SequenceEqual(vm.LatexTextDiagnostics), propertyName + " text diagnostics 원자 publish");
        Qa.Check(layoutDiagnostics.SequenceEqual(vm.LatexLayoutDiagnostics), propertyName + " layout diagnostics 원자 publish");
        Qa.Check(incompleteSlots.SequenceEqual(vm.LatexIncompleteSlotNodeIds), propertyName + " slot diagnostics 원자 publish");
        var canExport = textDiagnostics.IsEmpty && incompleteSlots.IsEmpty;
        Qa.Equal(canExport, vm.CanExportLatex, propertyName + " CanExportLatex 원자 publish");
        Qa.Equal(canExport, vm.CanCopyLatex, propertyName + " CanCopyLatex 원자 publish");
        if (canExport)
            Qa.Equal(string.Empty, vm.LatexCopyDisabledReason, propertyName + " 허용 상태의 비활성 사유");
        else
            Qa.Equal(vm.LatexCodeDiagnostic, vm.LatexCopyDisabledReason,
                propertyName + " 차단 상태의 비활성 사유와 코드 진단 불일치");
        if (!canExport)
            Qa.Check(!string.IsNullOrWhiteSpace(vm.LatexCodeDiagnostic), propertyName + " 차단 진단 누락");
    }

    private static VmState Capture(MainWindowViewModel vm)
    {
        var snapshot = vm.Session.CaptureSnapshot();
        return new VmState(
            vm.Session,
            Qa.Fingerprint(snapshot.Workspace),
            snapshot.Revision,
            snapshot.StateId,
            vm.Session.Caret,
            vm.Session.Selection,
            vm.Session.SelectedNodeId,
            vm.Session.IsNodeSelected,
            vm.Session.IsDirty,
            vm.Session.CanUndo,
            vm.Session.CanRedo,
            vm.CurrentPath,
            vm.LatexCode,
            vm.LatexCodeDiagnostic,
            vm.CanExportLatex,
            DiagnosticFingerprint(vm.LatexTextDiagnostics),
            DiagnosticFingerprint(vm.LatexLayoutDiagnostics),
            string.Join(',', vm.LatexIncompleteSlotNodeIds),
            vm.IsOutputPreviewVisible,
            vm.OutputPreview.State,
            vm.OutputPreview.SelectedMode,
            vm.OutputPreview.PreviewSnapshot,
            vm.OutputPreview.Diagnostic,
            vm.OutputPreview.LastExportSummary,
            vm.OutputPreview.IsExportBusy);
    }

    private static string DiagnosticFingerprint<T>(IEnumerable<T> values) =>
        string.Join("|", values.Select(value => System.Text.Json.JsonSerializer.Serialize(value)));

    private static void AssertUnchanged(VmState expected, VmState actual, string label)
    {
        Qa.Check(ReferenceEquals(expected.Session, actual.Session), label + " Session identity");
        Qa.Equal(expected.WorkspaceFingerprint, actual.WorkspaceFingerprint, label + " workspace");
        Qa.Equal(expected.Revision, actual.Revision, label + " revision");
        Qa.Equal(expected.StateId, actual.StateId, label + " StateId");
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " whole-node selection");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + " dirty");
        Qa.Equal(expected.CanUndo, actual.CanUndo, label + " undo");
        Qa.Equal(expected.CanRedo, actual.CanRedo, label + " redo");
        Qa.Equal(expected.CurrentPath, actual.CurrentPath, label + " current path");
        Qa.Equal(expected.LatexCode, actual.LatexCode, label + " LaTeX source");
        Qa.Equal(expected.LatexCodeDiagnostic, actual.LatexCodeDiagnostic, label + " code diagnostic");
        Qa.Equal(expected.CanExportLatex, actual.CanExportLatex, label + " export gate");
        Qa.Equal(expected.TextDiagnostics, actual.TextDiagnostics, label + " text diagnostics");
        Qa.Equal(expected.LayoutDiagnostics, actual.LayoutDiagnostics, label + " layout diagnostics");
        Qa.Equal(expected.IncompleteSlots, actual.IncompleteSlots, label + " incomplete slots");
        Qa.Equal(expected.OutputPreviewVisible, actual.OutputPreviewVisible, label + " preview visibility");
        Qa.Equal(expected.PreviewState, actual.PreviewState, label + " preview state");
        Qa.Equal(expected.PreviewMode, actual.PreviewMode, label + " preview mode");
        Qa.Check(ReferenceEquals(expected.PreviewSnapshot, actual.PreviewSnapshot), label + " preview snapshot identity");
        Qa.Equal(expected.PreviewDiagnostic, actual.PreviewDiagnostic, label + " preview diagnostic");
        Qa.Equal(expected.LastExportSummary, actual.LastExportSummary, label + " preview export summary");
        Qa.Equal(expected.IsExportBusy, actual.IsExportBusy, label + " preview busy");
    }

    private sealed record VmState(
        EditingSession Session,
        string WorkspaceFingerprint,
        long Revision,
        Guid StateId,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        bool IsDirty,
        bool CanUndo,
        bool CanRedo,
        string? CurrentPath,
        string LatexCode,
        string LatexCodeDiagnostic,
        bool CanExportLatex,
        string TextDiagnostics,
        string LayoutDiagnostics,
        string IncompleteSlots,
        bool OutputPreviewVisible,
        MathOutputPreviewState PreviewState,
        MathOutputMode PreviewMode,
        MathOutputPreviewSnapshot? PreviewSnapshot,
        string PreviewDiagnostic,
        string LastExportSummary,
        bool IsExportBusy);

    private sealed class CopyGate
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _sources = [];

        public int Calls { get; private set; }
        public IReadOnlyList<string> Sources => _sources;

        public Task WriteAsync(string source)
        {
            Calls++;
            _sources.Add(source);
            _started.TrySetResult();
            return _released.Task;
        }

        public Task WaitUntilStartedAsync() => _started.Task.WaitAsync(WaitLimit);
        public void Release() => _released.TrySetResult();
    }
}
