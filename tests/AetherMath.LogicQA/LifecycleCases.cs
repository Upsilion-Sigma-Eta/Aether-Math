using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Persistence;
using System.ComponentModel;
using System.Collections.Concurrent;

namespace AetherMath.LogicQA;

internal static class LifecycleCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("LIFECYCLE-CLEAN", "깨끗한 New/Open/Close와 빈 작업의 식별자·필수 슬롯", CleanNavigation);
        yield return new("LIFECYCLE-NEW", "dirty New의 취소·버리기·저장 후 전환", DirtyNew);
        yield return new("LIFECYCLE-OPEN", "dirty Open의 취소·저장 후 대상 파일 전환", DirtyOpen);
        yield return new("LIFECYCLE-CLOSE", "dirty Close 확인과 승인 토큰의 일회성·무효화", CloseApproval);
        yield return new("LIFECYCLE-FAIL", "pending 저장 실패·취소 뒤 상태 보존과 재시도", FailedSaveRetry);
        yield return new("LIFECYCLE-SAVE-EDIT", "저장 중 편집 및 취소 뒤 늦은 저장 완료 폐기", SaveDuringEditAndCancel);
        yield return new("LIFECYCLE-SAVE-UNDO", "지연 일반 저장 중 Undo 뒤 New/Open/Close 보호와 이전 snapshot 저장", SaveUndoNavigation);
        yield return new("LIFECYCLE-SAVE-CANCELLED-WRITE", "취소 뒤 늦게 성공한 저장 보호와 실패·실제 취소 ticket 정리", CancelledSaveThatWritesLate);
        yield return new("LIFECYCLE-ASYNC-STALE", "새 요청 뒤 늦게 끝난 Open과 close token 경합", StaleOpenAndCloseToken);
        yield return new("LIFECYCLE-REENTRY", "pending 동안 중복 요청·해결 거부와 단일 전환", RejectReentry);
    }

    private static async Task CleanNavigation()
    {
        using var directory = new TempDirectory();
        var vm = new MainWindowViewModel();
        var originalSession = vm.Session;
        var originalWorkspaceId = vm.Session.Workspace.Id;
        Qa.Equal(WorkspaceActionRequestResult.Completed, vm.RequestNewWorkspace(), "깨끗한 New 결과");
        Qa.Check(!ReferenceEquals(originalSession, vm.Session), "깨끗한 New가 Session을 바꾸지 않음");
        Qa.Check(originalWorkspaceId != vm.Session.Workspace.Id, "새 작업이 Workspace ID를 재사용함");
        AssertBlankWorkspace(vm.Session.Workspace);
        Qa.Equal<string?>(null, vm.CurrentPath, "깨끗한 New의 경로 초기화");
        Qa.Check(!vm.Session.IsDirty && !vm.Session.CanUndo && !vm.Session.CanRedo, "새 작업의 history/저장 기준 초기화");

        var target = directory.File("clean-open.aethermath");
        var targetWorkspace = Qa.Workspace(Qa.Text("열린 파일 𝑥🙂"));
        await new WorkspaceFileService().SaveAsync(target, new EditingSession(targetWorkspace).CaptureSnapshot());
        Qa.Equal(WorkspaceActionRequestResult.Completed, await vm.OpenFromPathAsync(target), "깨끗한 Open 결과");
        Qa.Equal(Qa.Fingerprint(targetWorkspace), Qa.Fingerprint(vm.Session.Workspace), "깨끗한 Open 데이터");
        Qa.Equal(target, vm.CurrentPath, "깨끗한 Open 경로");
        Qa.Check(!vm.Session.IsDirty && !vm.IsPendingWorkspaceActionVisible, "깨끗한 Open 저장/pending 상태");
        Qa.Check(vm.SaveCommand.CanExecute(null), "성공한 Open 뒤 저장 command가 이전 Open busy에 막힘");
        var openedLeaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, openedLeaf.Id, 0); vm.Session.InsertText("저장 재개 확인");
        var postOpenSavePath = directory.File("post-open-save.aethermath");
        await vm.SaveToPathAsync(postOpenSavePath);
        Qa.Check(!vm.Session.IsDirty, "성공한 Open 뒤 저장이 saved marker를 반영하지 못함");
        Qa.Equal(Qa.Fingerprint(vm.Session.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(postOpenSavePath)), "Open 뒤 실제 Save 결과");
        Qa.Equal(postOpenSavePath, vm.CurrentPath, "Open 뒤 실제 Save 현재 경로");

        var tokens = new List<Guid>();
        var closeSignal = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CloseRequested += (_, args) => { tokens.Add(args.ApprovalToken); closeSignal.TrySetResult(args.ApprovalToken); };
        Qa.Equal(WorkspaceActionRequestResult.Completed, vm.RequestClose(), "깨끗한 Close 결과");
        var cleanCloseToken = await closeSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Qa.Equal(1, tokens.Count, "깨끗한 Close 승인 통지 횟수");
        Qa.Check(vm.TryConsumeCloseApproval(cleanCloseToken), "깨끗한 Close 승인 소비");
        Qa.Check(!vm.TryConsumeCloseApproval(cleanCloseToken), "Close 승인이 두 번 소비됨");
    }

    private static async Task DirtyNew()
    {
        using var directory = new TempDirectory();
        var vm = new MainWindowViewModel();
        var currentPath = directory.File("current.aethermath");
        await vm.SaveToPathAsync(currentPath);
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0);
        vm.Session.InsertText("dirty e\u0301🙂");
        Qa.Select(vm.Session, leaf.Id, 1, 3);
        var beforeCancel = CaptureCore(vm);
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, vm.RequestNewWorkspace(), "dirty New 확인 요구");
        Qa.Equal(WorkspaceActionKind.NewWorkspace, vm.PendingWorkspaceAction, "dirty New pending 종류");
        Qa.Check(vm.IsPendingWorkspaceActionVisible, "dirty New 확인 UI 상태 누락");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, vm.CancelPendingWorkspaceAction(), "dirty New 취소");
        AssertCore(beforeCancel, CaptureCore(vm), "취소한 New의 전체 편집 상태");
        Qa.Check(!vm.IsPendingWorkspaceActionVisible, "New 취소 뒤 pending 유지");

        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, vm.RequestNewWorkspace(), "두 번째 dirty New 확인");
        Qa.Equal(WorkspaceActionResolutionResult.Completed, vm.DiscardPendingWorkspaceAction(), "dirty New 버리기");
        AssertBlankWorkspace(vm.Session.Workspace);
        Qa.Check(!ReferenceEquals(beforeCancel.Session, vm.Session), "버린 New가 새 Session을 만들지 않음");
        Qa.Equal<string?>(null, vm.CurrentPath, "버린 New 뒤 이전 경로 유지");
        Qa.Check(!vm.Session.IsDirty && !vm.Session.CanUndo && !vm.Session.CanRedo, "버린 New의 빈 작업 상태");

        var saveVm = new MainWindowViewModel();
        var saveLeaf = Qa.Nodes(saveVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(saveVm.Session, saveLeaf.Id, 0);
        saveVm.Session.InsertText("저장 후 새 작업");
        var savedSnapshot = saveVm.Session.CaptureSnapshot();
        var savedPath = directory.File("before-new.aethermath");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, saveVm.RequestNewWorkspace(), "저장 경유 New 확인");
        Qa.Equal(WorkspaceActionResolutionResult.Completed,
            await saveVm.SavePendingWorkspaceActionToPathAsync(savedPath), "저장 뒤 New 전환");
        Qa.Equal(Qa.Fingerprint(savedSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(savedPath)), "New 전환 전 저장된 원본");
        AssertBlankWorkspace(saveVm.Session.Workspace);
        Qa.Equal<string?>(null, saveVm.CurrentPath, "New 전환 후 경로 지움");
        Qa.Check(!saveVm.IsPendingWorkspaceActionVisible, "저장 후 New pending 종료");
    }

    private static async Task DirtyOpen()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService();
        var targetPath = directory.File("target.aethermath");
        var targetWorkspace = Qa.Workspace(Qa.Text("대상 β=𝑥"));
        await service.SaveAsync(targetPath, new EditingSession(targetWorkspace).CaptureSnapshot());

        var vm = new MainWindowViewModel();
        var originalPath = directory.File("active.aethermath");
        await vm.SaveToPathAsync(originalPath);
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0);
        vm.Session.InsertText("미저장 원본");
        Qa.Select(vm.Session, leaf.Id, 0, 2);
        var original = CaptureCore(vm);

        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired,
            await vm.OpenFromPathAsync(targetPath), "dirty Open 확인 요구");
        Qa.Equal(WorkspaceActionKind.OpenWorkspace, vm.PendingWorkspaceAction, "dirty Open pending 종류");
        AssertCore(original, CaptureCore(vm), "Open 확인 중 원 세션·경로·history 보존");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, vm.CancelPendingWorkspaceAction(), "dirty Open 취소");
        AssertCore(original, CaptureCore(vm), "Open 취소 뒤 기존 편집 상태");

        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired,
            await vm.OpenFromPathAsync(targetPath), "저장 경유 Open 확인");
        Qa.Equal(WorkspaceActionResolutionResult.Completed,
            await vm.SavePendingWorkspaceActionToPathAsync(originalPath), "dirty Open 저장 후 전환");
        Qa.Equal(Qa.Fingerprint(targetWorkspace), Qa.Fingerprint(vm.Session.Workspace), "저장 후 Open 대상 데이터");
        Qa.Equal(targetPath, vm.CurrentPath, "저장 후 Open 대상 경로");
        Qa.Check(!vm.Session.IsDirty && !vm.IsPendingWorkspaceActionVisible, "저장 후 Open의 clean/pending 상태");
        Qa.Equal(Qa.Fingerprint(original.Session.Workspace),
            Qa.Fingerprint(await service.OpenAsync(originalPath)), "Open 전 기존 작업이 지정 경로에 저장됨");

        var sameTargetVm = new MainWindowViewModel();
        var sameTargetLeaf = Qa.Nodes(sameTargetVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(sameTargetVm.Session, sameTargetLeaf.Id, 0);
        sameTargetVm.Session.InsertText("현재 snapshot을 열기 대상에 저장");
        var sameTargetSnapshot = sameTargetVm.Session.CaptureSnapshot();
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired,
            await sameTargetVm.OpenFromPathAsync(targetPath), "같은 경로 Open 확인");
        Qa.Equal(WorkspaceActionResolutionResult.Completed,
            await sameTargetVm.SavePendingWorkspaceActionToPathAsync(targetPath), "Open 대상 경로에 현재 snapshot 저장");
        Qa.Equal(Qa.Fingerprint(sameTargetSnapshot.Workspace), Qa.Fingerprint(sameTargetVm.Session.Workspace),
            "같은 경로 저장 후 이전 파일 후보가 아닌 새 snapshot 활성화");
        Qa.Equal(Qa.Fingerprint(sameTargetSnapshot.Workspace),
            Qa.Fingerprint(await service.OpenAsync(targetPath)), "같은 경로에 저장한 snapshot이 최종 파일");
        Qa.Check(!sameTargetVm.Session.IsDirty && !sameTargetVm.IsPendingWorkspaceActionVisible,
            "같은 경로 Save/Open 뒤 clean 상태");
    }

    private static Task CloseApproval()
    {
        var vm = new MainWindowViewModel();
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0);
        vm.Session.InsertText("닫기 확인");
        var before = CaptureCore(vm);
        var approvals = new List<Guid>();
        vm.CloseRequested += (_, args) => approvals.Add(args.ApprovalToken);

        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, vm.RequestClose(), "dirty Close 확인 요구");
        Qa.Equal(WorkspaceActionKind.CloseWindow, vm.PendingWorkspaceAction, "Close pending 종류");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, vm.CancelPendingWorkspaceAction(), "dirty Close 취소");
        AssertCore(before, CaptureCore(vm), "Close 취소 후 상태 보존");
        Qa.Equal(0, approvals.Count, "취소된 Close가 승인 요청을 보냄");

        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, vm.RequestClose(), "dirty Close 재확인");
        Qa.Equal(WorkspaceActionResolutionResult.Completed, vm.DiscardPendingWorkspaceAction(), "dirty Close 버리기");
        Qa.Equal(1, approvals.Count, "Discard 뒤 Close 승인 이벤트");
        Qa.Check(vm.TryConsumeCloseApproval(approvals[^1]), "Discard close token 소비");
        Qa.Check(!vm.TryConsumeCloseApproval(approvals[^1]), "Discard close token 재사용 허용");

        var editVm = new MainWindowViewModel();
        var editTokens = new List<Guid>(); editVm.CloseRequested += (_, args) => editTokens.Add(args.ApprovalToken);
        Qa.Equal(WorkspaceActionRequestResult.Completed, editVm.RequestClose(), "clean close token 발급");
        var editLeaf = Qa.Nodes(editVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(editVm.Session, editLeaf.Id, 0); editVm.Session.InsertText("token stale");
        Qa.Check(!editVm.TryConsumeCloseApproval(editTokens.Single()), "revision 변경 뒤 close token 소비됨");

        var navigationVm = new MainWindowViewModel();
        var navigationTokens = new List<Guid>(); navigationVm.CloseRequested += (_, args) => navigationTokens.Add(args.ApprovalToken);
        Qa.Equal(WorkspaceActionRequestResult.Completed, navigationVm.RequestClose(), "새 요청 이전 close token");
        Qa.Equal(WorkspaceActionRequestResult.Completed, navigationVm.RequestNewWorkspace(), "새 navigation 요청");
        Qa.Check(!navigationVm.TryConsumeCloseApproval(navigationTokens.Single()), "새 navigation이 이전 close token을 무효화하지 않음");
        return Task.CompletedTask;
    }

    private static async Task FailedSaveRetry()
    {
        using var directory = new TempDirectory();
        var files = new ControlledWorkspaceFileService();
        var vm = new MainWindowViewModel(files);
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0); vm.Session.InsertText("저장 실패 경계");
        var original = CaptureCore(vm);
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, vm.RequestNewWorkspace(), "실패 경유 New 확인");
        var path = directory.File("preserve.aethermath");
        await File.WriteAllTextAsync(path, "원본 바이트");
        var bytes = await File.ReadAllBytesAsync(path);
        files.FailNextSave(new IOException("QA에서 주입한 저장 실패"));
        Qa.Equal(WorkspaceActionResolutionResult.Failed,
            await vm.SavePendingWorkspaceActionToPathAsync(path), "pending Save 실패 결과");
        AssertCore(original, CaptureCore(vm), "실패한 Save 뒤 원 세션·경로·history");
        Qa.Equal(WorkspaceActionKind.NewWorkspace, vm.PendingWorkspaceAction, "실패 후 pending 유지");
        Qa.Check(!string.IsNullOrWhiteSpace(vm.PendingWorkspaceActionError), "실패 이유가 pending에 표시되지 않음");
        var bytesAfterFailedSave = await File.ReadAllBytesAsync(path);
        Qa.Check(bytes.SequenceEqual(bytesAfterFailedSave), "실패한 Save가 대상 파일을 덮어씀");

        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            Qa.Equal(WorkspaceActionResolutionResult.Canceled,
                await vm.SavePendingWorkspaceActionToPathAsync(path, canceled.Token), "취소된 pending Save 결과");
        }
        AssertCore(original, CaptureCore(vm), "취소된 Save 뒤 원 세션·경로·history");
        Qa.Equal(WorkspaceActionKind.NewWorkspace, vm.PendingWorkspaceAction, "취소 뒤 pending 유지");
        var bytesAfterCanceledSave = await File.ReadAllBytesAsync(path);
        Qa.Check(bytes.SequenceEqual(bytesAfterCanceledSave), "취소된 Save가 대상 파일을 변경");

        Qa.Equal(WorkspaceActionResolutionResult.Completed,
            await vm.SavePendingWorkspaceActionToPathAsync(path), "pending Save 재시도");
        AssertBlankWorkspace(vm.Session.Workspace);
        Qa.Equal<string?>(null, vm.CurrentPath, "Save 후 New 전환의 현재경로");
        var loaded = await new WorkspaceFileService().OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(original.Session.Workspace), Qa.Fingerprint(loaded), "재시도 파일은 전환 전 snapshot");
    }

    private static async Task SaveDuringEditAndCancel()
    {
        using var directory = new TempDirectory();

        // 일반 Save(A)와 pending Close의 Save(B)는 동일 저장 큐에서 A→B 순서를 지켜야 한다.
        var queueFiles = new ControlledWorkspaceFileService();
        var queueVm = new MainWindowViewModel(queueFiles);
        var queuePath = directory.File("ordered-saves.aethermath");
        var saveAGate = new OperationGate(); queueFiles.GateSave(queuePath, saveAGate);
        var saveA = queueVm.SaveToPathAsync(queuePath);
        await saveAGate.WaitUntilStartedAsync();
        Qa.Equal("저장 중", queueVm.StatusText, "일반 Save(A) 진행 상태 표시");
        var queueLeaf = Qa.Nodes(queueVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(queueVm.Session, queueLeaf.Id, 0); queueVm.Session.InsertText("B snapshot");
        var snapshotB = queueVm.Session.CaptureSnapshot();
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, queueVm.RequestClose(), "A 저장 중 dirty Close 확인");
        var closeApprovals = new List<Guid>(); queueVm.CloseRequested += (_, args) => closeApprovals.Add(args.ApprovalToken);
        var saveBGate = new OperationGate(); queueFiles.GateSave(queuePath, saveBGate);
        var saveB = queueVm.SavePendingWorkspaceActionToPathAsync(queuePath);
        Qa.Check(queueVm.IsPendingWorkspaceActionBusy, "pending Save(B) busy 미설정");
        Qa.Check(!saveBGate.HasStarted, "일반 Save(A)가 끝나기 전에 pending Save(B)가 파일 서비스에 진입");
        Qa.Equal(0, closeApprovals.Count, "Save(B) 완료 전 Close 승인 발급");
        saveAGate.Release();
        await saveA;
        await saveBGate.WaitUntilStartedAsync();
        Qa.Equal("저장 중", queueVm.StatusText, "pending Save(B) 진행 상태 표시");
        Qa.Equal(0, closeApprovals.Count, "Save(B) 진행 중 Close 승인 발급");
        saveBGate.Release();
        Qa.Equal(WorkspaceActionResolutionResult.Completed, await saveB, "직렬 저장 뒤 Close action");
        Qa.Equal(Qa.Fingerprint(snapshotB.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(queuePath)), "같은 경로 최종 파일은 B snapshot");
        Qa.Equal(queuePath, queueVm.CurrentPath, "직렬 저장 뒤 현재 경로");
        Qa.Check(!queueVm.Session.IsDirty, "B snapshot 저장 뒤 dirty 상태");
        Qa.Equal(1, closeApprovals.Count, "B 저장 후 Close 승인 1회");
        Qa.Check(queueVm.TryConsumeCloseApproval(closeApprovals.Single()), "직렬 저장 후 Close 승인 소비");
        Qa.Equal(2, queueFiles.SaveCallCount, "A/B 직렬 저장에서 실제 file-service 호출 수");

        var queuedFiles = new ControlledWorkspaceFileService();
        var queuedVm = new MainWindowViewModel(queuedFiles);
        var pathA = directory.File("queued-a.aethermath");
        var pathB = directory.File("queued-canceled-b.aethermath");
        var pathC = directory.File("queued-c.aethermath");
        var gateA = new OperationGate(); queuedFiles.GateSave(pathA, gateA);
        var queuedA = queuedVm.SaveToPathAsync(pathA);
        await gateA.WaitUntilStartedAsync();
        var queuedLeaf = Qa.Nodes(queuedVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(queuedVm.Session, queuedLeaf.Id, 0); queuedVm.Session.InsertText("queued B");
        var queuedSnapshot = queuedVm.Session.CaptureSnapshot();
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, queuedVm.RequestNewWorkspace(), "queued save New 확인");
        var queuedB = queuedVm.SavePendingWorkspaceActionToPathAsync(pathB);
        Qa.Check(queuedVm.IsPendingWorkspaceActionBusy, "queued B가 busy 상태가 아님");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, queuedVm.CancelPendingWorkspaceAction(), "선행 A 뒤 대기 중인 B 취소");
        Qa.Check(!queuedVm.IsPendingWorkspaceActionBusy, "취소 직후 pending busy가 해제되지 않음");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, queuedVm.RequestNewWorkspace(), "취소 뒤 새 pending 요청 가능");
        var gateC = new OperationGate(); queuedFiles.GateSave(pathC, gateC);
        var queuedC = queuedVm.SavePendingWorkspaceActionToPathAsync(pathC);
        Qa.Check(queuedVm.IsPendingWorkspaceActionBusy, "새 pending Save(C)가 busy가 아님");
        Qa.Check(!gateC.HasStarted, "A가 끝나기 전에 tail Save(C)가 파일 서비스에 진입");
        gateA.Release();
        await queuedA;
        Qa.Equal(WorkspaceActionResolutionResult.Stale, await queuedB, "취소된 queued Save(B) continuation");
        await gateC.WaitUntilStartedAsync();
        Qa.Check(!File.Exists(pathB), "취소된 queued Save(B)가 파일을 씀");
        gateC.Release();
        Qa.Equal(WorkspaceActionResolutionResult.Completed, await queuedC, "취소된 B slot drain 뒤 C 완료");
        Qa.Equal(Qa.Fingerprint(queuedSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(pathC)), "tail C가 현재 B 상태를 저장");
        Qa.Equal(2, queuedFiles.SaveCallCount, "취소된 B queue slot이 file service를 호출함");
        AssertBlankWorkspace(queuedVm.Session.Workspace);
        Qa.Equal<string?>(null, queuedVm.CurrentPath, "C 뒤 새 작업의 경로");

        var files = new ControlledWorkspaceFileService();
        var vm = new MainWindowViewModel(files);
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0); vm.Session.InsertText("저장 snapshot");
        var startingSnapshot = vm.Session.CaptureSnapshot();
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, vm.RequestNewWorkspace(), "save edit New 확인");

        var path = directory.File("snapshot.aethermath");
        var saveGate = new OperationGate(); files.GateSave(path, saveGate);
        var saveTask = vm.SavePendingWorkspaceActionToPathAsync(path);
        await saveGate.WaitUntilStartedAsync();
        Qa.Check(vm.IsPendingWorkspaceActionBusy, "저장 중 busy flag가 설정되지 않음");
        Qa.Equal("저장 중", vm.StatusText, "pending Save 진행 상태 표시");
        var saveBusyState = CaptureCore(vm);
        Qa.Equal(WorkspaceActionResolutionResult.Rejected,
            await vm.SavePendingWorkspaceActionToPathAsync(path), "동시 두 번째 pending Save");
        Qa.Equal(WorkspaceActionResolutionResult.Rejected, vm.DiscardPendingWorkspaceAction(), "저장 중 Discard 거부");
        Qa.Equal(WorkspaceActionRequestResult.Rejected, vm.RequestClose(), "저장 중 Close 재진입 거부");
        AssertCore(saveBusyState, CaptureCore(vm), "저장 중 거부된 요청의 상태");

        vm.Session.InsertText(" 편집 중");
        Qa.Equal("저장 중", vm.StatusText, "저장 중 편집이 진행 상태 표시를 지움");
        saveGate.Release();
        Qa.Equal(WorkspaceActionResolutionResult.Pending, await saveTask, "저장 snapshot 이후 편집의 pending 결과");
        Qa.Check(vm.StatusText != "저장 중", "저장 완료 뒤 진행 상태가 계속 표시됨");
        Qa.Equal(startingSnapshot.Workspace.Id, vm.Session.Workspace.Id, "저장 중 편집이 작업 ID 변경");
        Qa.Check(vm.Session.IsDirty && vm.IsPendingWorkspaceActionVisible, "저장 중 새 편집이 dirty/pending으로 남지 않음");
        Qa.Equal(WorkspaceActionKind.NewWorkspace, vm.PendingWorkspaceAction, "새 편집이 pending New를 보존하지 않음");
        Qa.Equal(path, vm.CurrentPath, "완료된 snapshot 저장 경로");
        Qa.Equal(Qa.Fingerprint(startingSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(path)), "파일이 저장 시작 시점 snapshot을 포함");
        Qa.Equal(WorkspaceActionResolutionResult.Completed, vm.DiscardPendingWorkspaceAction(), "새 편집 남긴 뒤 New 버리기");
        AssertBlankWorkspace(vm.Session.Workspace);

        var cancelFiles = new ControlledWorkspaceFileService();
        var cancelVm = new MainWindowViewModel(cancelFiles);
        var cancelLeaf = Qa.Nodes(cancelVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(cancelVm.Session, cancelLeaf.Id, 0); cancelVm.Session.InsertText("취소할 저장");
        var statusBefore = cancelVm.StatusText;
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, cancelVm.RequestNewWorkspace(), "취소 경유 New 확인");
        var canceledPath = directory.File("canceled.aethermath");
        var cancelGate = new OperationGate(); cancelFiles.GateSave(canceledPath, cancelGate);
        var canceledSave = cancelVm.SavePendingWorkspaceActionToPathAsync(canceledPath);
        await cancelGate.WaitUntilStartedAsync();
        Qa.Equal("저장 중", cancelVm.StatusText, "취소 전 저장 진행 상태 표시");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, cancelVm.CancelPendingWorkspaceAction(), "busy Save 중 사용자 취소");
        Qa.Check(!cancelVm.IsPendingWorkspaceActionVisible, "busy Save 취소 뒤 pending 남음");
        Qa.Check(!cancelVm.IsPendingWorkspaceActionBusy, "busy Save 취소 직후 busy가 해제되지 않음");
        Qa.Equal(statusBefore, cancelVm.StatusText, "busy Save 취소 뒤 dirty 상태 문구 복원");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, cancelVm.RequestNewWorkspace(), "취소 직후 새 dirty 요청 거부");
        Qa.Equal(WorkspaceActionKind.NewWorkspace, cancelVm.PendingWorkspaceAction, "취소 뒤 새 요청 pending 종류");
        var afterCancel = CaptureCore(cancelVm);
        cancelGate.Release();
        Qa.Equal(WorkspaceActionResolutionResult.Stale, await canceledSave, "취소된 Save의 늦은 완료");
        AssertCore(afterCancel, CaptureCore(cancelVm), "취소 이후 늦은 Save의 상태 영향");
        Qa.Check(ReferenceEquals(afterCancel.Session, cancelVm.Session) && cancelVm.Session.IsDirty, "늦은 Save가 세션/dirty 전환");
        Qa.Equal<string?>(null, cancelVm.CurrentPath, "늦은 Save가 현재 경로 갱신");
        Qa.Equal(statusBefore, cancelVm.StatusText, "늦은 Save가 취소 후 status 갱신");
        Qa.Check(!File.Exists(canceledPath), "취소된 Save가 차단 이후 파일을 씀");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, cancelVm.CancelPendingWorkspaceAction(), "새 pending 정리");
    }

    private static async Task StaleOpenAndCloseToken()
    {
        using var directory = new TempDirectory();
        var target = directory.File("delayed.aethermath");
        await new WorkspaceFileService().SaveAsync(target,
            new EditingSession(Qa.Workspace(Qa.Text("delayed open"))).CaptureSnapshot());

        var files = new ControlledWorkspaceFileService();
        var vm = new MainWindowViewModel(files);
        var gate = new OperationGate(); files.GateNextOpen(gate);
        var openTask = vm.OpenFromPathAsync(target);
        await gate.WaitUntilStartedAsync();
        Qa.Equal(WorkspaceActionRequestResult.Completed, vm.RequestNewWorkspace(), "지연 Open 중 깨끗한 New");
        var newState = CaptureCore(vm);
        gate.Release();
        Qa.Equal(WorkspaceActionRequestResult.Stale, await openTask, "New 뒤 늦은 Open 결과");
        AssertCore(newState, CaptureCore(vm), "stale Open이 새 작업을 덮지 않음");
        Qa.Equal<string?>(null, vm.CurrentPath, "stale Open이 CurrentPath 갱신");
        AssertBlankWorkspace(vm.Session.Workspace);

        var closeFiles = new ControlledWorkspaceFileService();
        var closeVm = new MainWindowViewModel(closeFiles);
        var closeGate = new OperationGate(); closeFiles.GateNextOpen(closeGate);
        var lateOpen = closeVm.OpenFromPathAsync(target);
        await closeGate.WaitUntilStartedAsync();
        var approvals = new List<Guid>();
        closeVm.CloseRequested += (_, args) => approvals.Add(args.ApprovalToken);
        var drainFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        closeVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsPendingWorkspaceActionBusy) &&
                !closeVm.IsPendingWorkspaceActionBusy)
                drainFinished.TrySetResult();
        };
        Qa.Equal(WorkspaceActionRequestResult.Completed, closeVm.RequestClose(), "지연 Open 중 clean Close");
        Qa.Check(closeVm.IsPendingWorkspaceActionBusy, "clean Close가 I/O drain 동안 busy를 표시하지 않음");
        Qa.Equal(0, approvals.Count, "I/O drain 전에 Close 승인이 발급됨");
        var closeLeaf = Qa.Nodes(closeVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(closeVm.Session, closeLeaf.Id, 0); closeVm.Session.InsertText("drain 중 사용자 편집");
        var closeState = CaptureCore(closeVm);
        closeGate.Release();
        Qa.Equal(WorkspaceActionRequestResult.Stale, await lateOpen, "Close 뒤 늦은 Open 결과");
        await drainFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        AssertCore(closeState, CaptureCore(closeVm), "stale Open이 Close 승인 상태에 영향");
        Qa.Check(closeVm.Session.IsDirty, "I/O drain 중 편집이 저장/dirty 해제됨");
        Qa.Equal(0, approvals.Count, "I/O drain 중 revision 변경 뒤 Close 승인 발급");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, closeVm.RequestClose(), "drain 중 편집 후 dirty Close 재요청");
        Qa.Equal(WorkspaceActionResolutionResult.Completed, closeVm.DiscardPendingWorkspaceAction(), "drain 뒤 dirty Close 승인");
        Qa.Equal(1, approvals.Count, "dirty Close 재요청 뒤 승인 이벤트 횟수");
        Qa.Check(closeVm.TryConsumeCloseApproval(approvals.Single()), "drain 뒤 재요청한 Close token 소비");

        var revisionVm = new MainWindowViewModel();
        var revisionTokens = new List<Guid>(); revisionVm.CloseRequested += (_, args) => revisionTokens.Add(args.ApprovalToken);
        Qa.Equal(WorkspaceActionRequestResult.Completed, revisionVm.RequestClose(), "revision 경계 clean close");
        var text = Qa.Nodes(revisionVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(revisionVm.Session, text.Id, 0); revisionVm.Session.InsertText("revision changes");
        Qa.Check(!revisionVm.TryConsumeCloseApproval(revisionTokens.Single()), "편집 이후 close token이 유효함");
    }

    private static async Task SaveUndoNavigation()
    {
        using var directory = new TempDirectory();

        // New 취소는 원 세션을 보존한다. 재요청 후 현재(old) snapshot을 A 뒤에 저장해야 한다.
        var newCase = await CreateSavedOriginalAsync(directory, "undo-new.aethermath");
        var newSaveGate = new OperationGate(); newCase.Files.GateSave(newCase.Path, newSaveGate);
        var (newSaveTask, newSnapshotA) = await EditAndStartDelayedSaveAsync(newCase, newSaveGate);
        newCase.Vm.Session.Undo();
        AssertRestoredSavedOriginal(newCase, "New 전 Undo");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, newCase.Vm.RequestNewWorkspace(),
            "A 저장 대기 중 clean Undo 후 New 확인");
        Qa.Equal(WorkspaceActionKind.NewWorkspace, newCase.Vm.PendingWorkspaceAction, "Undo/New pending 종류");
        var newBeforeCancel = CaptureCore(newCase.Vm);
        var newStatusBeforeCancel = newCase.Vm.StatusText;
        AssertSaveProtectionStatus(newCase.Vm, "Undo 뒤 New 확인 상태");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, newCase.Vm.CancelPendingWorkspaceAction(), "Undo/New 확인 취소");
        AssertCore(newBeforeCancel, CaptureCore(newCase.Vm), "Undo/New 취소가 원 세션을 보존");
        Qa.Equal(newStatusBeforeCancel, newCase.Vm.StatusText, "Undo/New 취소가 보호 상태 문구를 보존");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, newCase.Vm.RequestNewWorkspace(),
            "취소 후 남은 저장 ticket이 New 재요청을 계속 보호");
        var newSaveGateB = new OperationGate(); newCase.Files.GateSave(newCase.Path, newSaveGateB);
        var newSaveOldTask = newCase.Vm.SavePendingWorkspaceActionToPathAsync(newCase.Path);
        Qa.Check(!newSaveGateB.HasStarted, "New용 이전 snapshot 저장이 A보다 먼저 시작");
        newSaveGate.Release();
        await newSaveTask;
        await newSaveGateB.WaitUntilStartedAsync();
        Qa.Equal(Qa.Fingerprint(newSnapshotA.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(newCase.Path)), "첫 번째 지연 저장이 A를 실제 기록");
        newSaveGateB.Release();
        Qa.Equal(WorkspaceActionResolutionResult.Completed, await newSaveOldTask, "New 전에 이전 snapshot 저장 후 전환");
        Qa.Equal(Qa.Fingerprint(newCase.OldSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(newCase.Path)), "New 전 FIFO 저장의 최종 파일은 old");
        AssertBlankWorkspace(newCase.Vm.Session.Workspace);

        // Open은 같은 파일 큐에서 A 다음에 읽고, 후보를 적용하기 전에 확인을 요구한다.
        var openCase = await CreateSavedOriginalAsync(directory, "undo-open-current.aethermath");
        var targetPath = directory.File("undo-open-target.aethermath");
        var targetWorkspace = Qa.Workspace(Qa.Text("대상 수식 β=𝑥"));
        await new WorkspaceFileService().SaveAsync(targetPath, new EditingSession(targetWorkspace).CaptureSnapshot());
        var openSaveGate = new OperationGate(); openCase.Files.GateSave(openCase.Path, openSaveGate);
        var (openSaveTask, _) = await EditAndStartDelayedSaveAsync(openCase, openSaveGate);
        openCase.Vm.Session.Undo();
        AssertRestoredSavedOriginal(openCase, "Open 전 Undo");
        var openTask = openCase.Vm.OpenFromPathAsync(targetPath);
        Qa.Check(!openTask.IsCompleted, "Open이 선행 일반 Save gate를 건너뜀");
        openSaveGate.Release();
        await openSaveTask;
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, await openTask,
            "A 저장 뒤 Undo 복구 중 유효 Open의 확인 요구");
        Qa.Equal(WorkspaceActionKind.OpenWorkspace, openCase.Vm.PendingWorkspaceAction, "Undo/Open pending 종류");
        AssertSaveProtectionStatus(openCase.Vm, "Undo/Open 확인 상태");
        var openBeforeCancel = CaptureCore(openCase.Vm);
        var openStatusBeforeCancel = openCase.Vm.StatusText;
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, openCase.Vm.CancelPendingWorkspaceAction(), "Undo/Open 확인 취소");
        AssertCore(openBeforeCancel, CaptureCore(openCase.Vm), "Undo/Open 취소가 원 수식·경로를 보존");
        Qa.Equal(openStatusBeforeCancel, openCase.Vm.StatusText, "Undo/Open 취소가 상태 문구를 보존");
        Qa.Equal(openCase.Path, openCase.Vm.CurrentPath, "취소한 Open이 현재 경로를 바꿈");
        Qa.Equal(Qa.Fingerprint(openCase.OldSnapshot.Workspace),
            Qa.Fingerprint(openCase.Vm.Session.Workspace), "취소한 Open이 대상 수식을 활성화");

        // Close 취소는 보존하고, 재요청의 Save(old)는 A 뒤에 완료된 뒤 토큰을 한 번 발급한다.
        var closeCase = await CreateSavedOriginalAsync(directory, "undo-close.aethermath");
        var closeSaveGateA = new OperationGate(); closeCase.Files.GateSave(closeCase.Path, closeSaveGateA);
        var (closeSaveTaskA, closeSnapshotA) = await EditAndStartDelayedSaveAsync(closeCase, closeSaveGateA);
        closeCase.Vm.Session.Undo();
        AssertRestoredSavedOriginal(closeCase, "Close 전 Undo");
        var closeApprovals = new List<Guid>();
        closeCase.Vm.CloseRequested += (_, args) => closeApprovals.Add(args.ApprovalToken);
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, closeCase.Vm.RequestClose(),
            "A 저장 대기 중 clean Undo 뒤 Close 확인");
        Qa.Equal(WorkspaceActionKind.CloseWindow, closeCase.Vm.PendingWorkspaceAction, "Undo/Close pending 종류");
        var closeBeforeCancel = CaptureCore(closeCase.Vm);
        var closeStatusBeforeCancel = closeCase.Vm.StatusText;
        AssertSaveProtectionStatus(closeCase.Vm, "Undo 뒤 Close 확인 상태");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, closeCase.Vm.CancelPendingWorkspaceAction(), "Undo/Close 취소");
        AssertCore(closeBeforeCancel, CaptureCore(closeCase.Vm), "Undo/Close 취소가 원 세션을 보존");
        Qa.Equal(closeStatusBeforeCancel, closeCase.Vm.StatusText, "Undo/Close 취소가 보호 상태 문구를 보존");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, closeCase.Vm.RequestClose(),
            "취소 후 저장 ticket이 Close 재요청을 계속 보호");

        var closeSaveGateB = new OperationGate(); closeCase.Files.GateSave(closeCase.Path, closeSaveGateB);
        var closeSaveOldTask = closeCase.Vm.SavePendingWorkspaceActionToPathAsync(closeCase.Path);
        Qa.Check(!closeSaveGateB.HasStarted, "Close용 이전 snapshot 저장이 A보다 먼저 시작");
        Qa.Equal(0, closeApprovals.Count, "A/B 저장이 끝나기 전 Close token 발급");
        closeSaveGateA.Release();
        await closeSaveTaskA;
        await closeSaveGateB.WaitUntilStartedAsync();
        Qa.Equal(Qa.Fingerprint(closeSnapshotA.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(closeCase.Path)), "Close 앞의 A 저장이 실제 반영");
        Qa.Equal(0, closeApprovals.Count, "이전 snapshot 저장 진행 중 Close token 발급");
        closeSaveGateB.Release();
        Qa.Equal(WorkspaceActionResolutionResult.Completed, await closeSaveOldTask, "FIFO Save(old) 뒤 Close 승인 흐름");
        Qa.Equal(Qa.Fingerprint(closeCase.OldSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(closeCase.Path)), "Close 직전 최종 파일은 old");
        Qa.Equal(1, closeApprovals.Count, "Save(old) 뒤 Close token 발급 횟수");
        Qa.Check(closeCase.Vm.TryConsumeCloseApproval(closeApprovals.Single()), "Save(old) 뒤 Close token 소비");
        Qa.Check(!closeCase.Vm.TryConsumeCloseApproval(closeApprovals.Single()), "Close token을 두 번 소비할 수 있음");
    }

    private static async Task CancelledSaveThatWritesLate()
    {
        using var directory = new TempDirectory();

        var lateCase = await CreateSavedOriginalAsync(directory, "late-write-current.aethermath");
        var saveAsPath = directory.File("late-write-candidate.aethermath");
        EditAtEnd(lateCase, "A");
        var snapshotA = lateCase.Vm.Session.CaptureSnapshot();
        using var cancellation = new CancellationTokenSource();
        var ignoredGate = new OperationGate();
        lateCase.Files.GateSave(saveAsPath, ignoredGate, ignoreCancellation: true);
        var canceledCallerSave = lateCase.Vm.SaveToPathAsync(saveAsPath, cancellation.Token);
        await ignoredGate.WaitUntilStartedAsync();
        Qa.Equal("저장 중", lateCase.Vm.StatusText, "취소 무시 service의 저장 시작 상태");
        cancellation.Cancel();
        await canceledCallerSave;
        Qa.Check(!ignoredGate.HasFinished, "취소 무시 service가 gate 해제 전에 종료됨");
        Qa.Check(!File.Exists(saveAsPath), "취소 직후 gate 미해제인데 실제 파일이 생김");
        Qa.Equal(lateCase.Path, lateCase.Vm.CurrentPath, "caller 취소 직후 현재 경로가 Save As 대상으로 바뀜");

        lateCase.Vm.Session.Undo();
        AssertRestoredSavedOriginal(lateCase, "취소 무시 save 뒤 Undo");
        AssertSaveProtectionStatus(lateCase.Vm, "취소된 caller의 실제 I/O 대기 보호 상태");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, lateCase.Vm.RequestNewWorkspace(),
            "caller 취소 후 in-flight write 가능성이 New를 보호");
        var firstRequestState = CaptureCore(lateCase.Vm);
        var firstRequestStatus = lateCase.Vm.StatusText;
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, lateCase.Vm.CancelPendingWorkspaceAction(),
            "취소된 caller 뒤 New 확인 취소");
        AssertCore(firstRequestState, CaptureCore(lateCase.Vm), "첫 New 취소가 저장 상태를 보존");
        Qa.Equal(firstRequestStatus, lateCase.Vm.StatusText, "첫 New 취소가 보호 문구를 보존");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, lateCase.Vm.RequestNewWorkspace(),
            "guard 취소 뒤 ticket 보호가 유지됨");
        var lateWriteState = CaptureCore(lateCase.Vm);
        var lateWriteStatus = lateCase.Vm.StatusText;
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, lateCase.Vm.CancelPendingWorkspaceAction(),
            "늦은 write 대기 중 두 번째 New 취소");

        ignoredGate.Release();
        await ignoredGate.WaitUntilFinishedAsync();
        Qa.Equal(Qa.Fingerprint(snapshotA.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(saveAsPath)), "취소 무시 service가 A를 실제 파일에 기록");
        AssertCore(lateWriteState, CaptureCore(lateCase.Vm), "취소된 caller의 늦은 성공이 saved marker·session·path를 바꿈");
        Qa.Equal(lateWriteStatus, lateCase.Vm.StatusText, "취소된 caller의 늦은 성공이 StatusText를 덮음");
        Qa.Equal(lateCase.Path, lateCase.Vm.CurrentPath, "늦은 Save As 완료가 CurrentPath를 덮음");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, lateCase.Vm.RequestNewWorkspace(),
            "실제 늦은 write 이후에도 New 보호가 유지됨");
        Qa.Equal(WorkspaceActionResolutionResult.Completed,
            await lateCase.Vm.SavePendingWorkspaceActionToPathAsync(saveAsPath), "old snapshot으로 늦게 쓴 후보 파일 복구 후 New");
        Qa.Equal(Qa.Fingerprint(lateCase.OldSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(saveAsPath)), "취소 무시 write 뒤 최종 후보 파일은 old");
        AssertBlankWorkspace(lateCase.Vm.Session.Workspace);

        // 실제 cancellation은 service 자체가 쓰기 전에 취소를 관찰하므로 ticket을 남기지 않는다.
        var realCancelCase = await CreateSavedOriginalAsync(directory, "real-cancel.aethermath");
        EditAtEnd(realCancelCase, "cancel");
        var realCancelGate = new OperationGate(); realCancelCase.Files.GateSave(realCancelCase.Path, realCancelGate);
        using (var realCancellation = new CancellationTokenSource())
        {
            var realCanceledSave = realCancelCase.Vm.SaveToPathAsync(realCancelCase.Path, realCancellation.Token);
            await realCancelGate.WaitUntilStartedAsync();
            realCancelCase.Vm.Session.Undo();
            Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, realCancelCase.Vm.RequestNewWorkspace(),
                "실제 취소 전 진행 중 ticket이 New를 보호");
            Qa.Equal(WorkspaceActionResolutionResult.Canceled, realCancelCase.Vm.CancelPendingWorkspaceAction(),
                "실제 취소 전 confirmation 정리");
            realCancellation.Cancel();
            realCancelGate.Release();
            await realCanceledSave;
            await realCancelGate.WaitUntilFinishedAsync();
        }
        Qa.Equal(Qa.Fingerprint(realCancelCase.OldSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(realCancelCase.Path)), "실제 취소는 원본 파일을 바꾸지 않음");
        Qa.Equal(WorkspaceActionRequestResult.Completed,
            await realCancelCase.Vm.OpenFromPathAsync(realCancelCase.Path),
            "실제 취소 ticket 정리 뒤 같은 파일 Open barrier");
        Qa.Equal(WorkspaceActionRequestResult.Completed, realCancelCase.Vm.RequestNewWorkspace(),
            "실제 취소된 ticket이 정리되어 clean New가 바로 진행");

        // 실패한 write도 ticket을 제거하고, clean 동일 StateId 저장은 navigation 확인이 아니라 drain만 요구한다.
        var failureCase = await CreateSavedOriginalAsync(directory, "failed-ticket.aethermath");
        EditAtEnd(failureCase, "failure");
        failureCase.Files.FailNextSave(new IOException("QA에서 주입한 일반 저장 실패"));
        var failureGate = new OperationGate(); failureCase.Files.GateSave(failureCase.Path, failureGate);
        var failureSave = failureCase.Vm.SaveToPathAsync(failureCase.Path);
        await failureGate.WaitUntilStartedAsync();
        failureCase.Vm.Session.Undo();
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, failureCase.Vm.RequestNewWorkspace(),
            "실패가 결정되기 전 ticket이 New를 보호");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled, failureCase.Vm.CancelPendingWorkspaceAction(),
            "실패 시험의 confirmation 취소");
        failureGate.Release();
        await failureSave;
        await failureGate.WaitUntilFinishedAsync();
        Qa.Equal(Qa.Fingerprint(failureCase.OldSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(failureCase.Path)), "저장 실패가 원본 파일을 보존");
        Qa.Equal(WorkspaceActionRequestResult.Completed,
            await failureCase.Vm.OpenFromPathAsync(failureCase.Path), "실패 ticket 정리 뒤 같은 파일 Open barrier");
        Qa.Equal(WorkspaceActionRequestResult.Completed, failureCase.Vm.RequestNewWorkspace(),
            "실패한 ticket이 제거되어 clean New가 바로 진행");

        using var cleanDirectory = new TempDirectory();
        var cleanFiles = new ControlledWorkspaceFileService();
        var cleanVm = new MainWindowViewModel(cleanFiles);
        var cleanPath = cleanDirectory.File("same-clean-state.aethermath");
        var cleanSnapshot = cleanVm.Session.CaptureSnapshot();
        var cleanGate = new OperationGate(); cleanFiles.GateSave(cleanPath, cleanGate);
        var cleanSave = cleanVm.SaveToPathAsync(cleanPath);
        await cleanGate.WaitUntilStartedAsync();
        var cleanApprovals = new List<Guid>();
        var closeEvent = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        cleanVm.CloseRequested += (_, args) => { cleanApprovals.Add(args.ApprovalToken); closeEvent.TrySetResult(args.ApprovalToken); };
        Qa.Equal(WorkspaceActionRequestResult.Completed, cleanVm.RequestClose(),
            "현재 clean StateId와 같은 지연 저장은 추가 확인 없이 Close drain 진행");
        Qa.Check(cleanVm.IsPendingWorkspaceActionBusy, "clean 동일 StateId Close가 drain 상태를 표시하지 않음");
        Qa.Equal(0, cleanApprovals.Count, "clean 동일 StateId Close가 선행 write 전에 승인됨");
        cleanGate.Release();
        await cleanSave;
        await cleanGate.WaitUntilFinishedAsync();
        var cleanToken = await closeEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Qa.Equal(1, cleanApprovals.Count, "clean same-StateId drain 뒤 Close token 횟수");
        Qa.Check(cleanVm.TryConsumeCloseApproval(cleanToken), "clean same-StateId drain token 소비");
        Qa.Check(!cleanVm.TryConsumeCloseApproval(cleanToken), "clean same-StateId drain token 중복 소비");
        Qa.Equal(Qa.Fingerprint(cleanSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(cleanPath)), "clean same-StateId 지연 저장 파일");
    }

    private static async Task<SavedOriginal> CreateSavedOriginalAsync(TempDirectory directory, string fileName)
    {
        var files = new ControlledWorkspaceFileService();
        var vm = new MainWindowViewModel(files);
        var path = directory.File(fileName);
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0);
        vm.Session.InsertText("old-" + Path.GetFileNameWithoutExtension(fileName));
        var oldSnapshot = vm.Session.CaptureSnapshot();
        await vm.SaveToPathAsync(path);
        Qa.Check(!vm.Session.IsDirty, "fixture 원본 저장 뒤 clean 상태");
        Qa.Equal(path, vm.CurrentPath, "fixture 원본 저장 경로");
        return new SavedOriginal(files, vm, path, leaf.Id, oldSnapshot);
    }

    private static async Task<(Task SaveTask, WorkspaceSnapshot SnapshotA)> EditAndStartDelayedSaveAsync(
        SavedOriginal saved, OperationGate gate)
    {
        EditAtEnd(saved, "A");
        var snapshotA = saved.Vm.Session.CaptureSnapshot();
        var saveTask = saved.Vm.SaveToPathAsync(saved.Path);
        await gate.WaitUntilStartedAsync();
        return (saveTask, snapshotA);
    }

    private static void EditAtEnd(SavedOriginal saved, string value)
    {
        var leaf = MathTree.FindTextNode(saved.Vm.Session.Workspace, saved.LeafId)
            ?? throw new InvalidOperationException("fixture text node를 찾지 못함");
        Qa.Caret(saved.Vm.Session, leaf.Id, leaf.Text.Length);
        saved.Vm.Session.InsertText(value);
    }

    private static void AssertRestoredSavedOriginal(SavedOriginal saved, string label)
    {
        Qa.Check(!saved.Vm.Session.IsDirty, $"{label}: Undo가 clean StateId를 복구하지 못함");
        Qa.Equal(Qa.Fingerprint(saved.OldSnapshot.Workspace), Qa.Fingerprint(saved.Vm.Session.Workspace),
            $"{label}: 원본 workspace 복구");
        Qa.Equal(saved.Path, saved.Vm.CurrentPath, $"{label}: 원본 path 보존");
    }

    private static void AssertSaveProtectionStatus(MainWindowViewModel vm, string label) =>
        Qa.Check(vm.StatusText is "변경됨" or "저장 확인 필요", $"{label}: ‘{vm.StatusText}’는 저장 ticket 보호를 나타내지 않음");

    private sealed record SavedOriginal(ControlledWorkspaceFileService Files, MainWindowViewModel Vm,
        string Path, Guid LeafId, WorkspaceSnapshot OldSnapshot);

    private static async Task RejectReentry()
    {
        using var directory = new TempDirectory();
        var files = new ControlledWorkspaceFileService();
        var vm = new MainWindowViewModel(files);
        var leaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, leaf.Id, 0); vm.Session.InsertText("한 번만 승인");
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, vm.RequestNewWorkspace(), "첫 navigation 승인");
        var pendingState = CaptureCore(vm);
        var opensBefore = files.OpenCallCount;
        Qa.Equal(WorkspaceActionRequestResult.Rejected, vm.RequestNewWorkspace(), "중복 New 거부");
        Qa.Equal(WorkspaceActionRequestResult.Rejected, vm.RequestClose(), "pending 중 Close 거부");
        Qa.Equal(WorkspaceActionRequestResult.Rejected,
            await vm.OpenFromPathAsync(directory.File("never-opened.aethermath")), "pending 중 Open 거부");
        Qa.Equal(opensBefore, files.OpenCallCount, "거부된 Open이 파일 서비스 호출");
        AssertCore(pendingState, CaptureCore(vm), "거부된 중복 요청이 pending/원본 상태 변경");

        Qa.Equal(WorkspaceActionResolutionResult.Completed, vm.DiscardPendingWorkspaceAction(), "승인된 단일 Discard");
        Qa.Equal(WorkspaceActionResolutionResult.Rejected, vm.DiscardPendingWorkspaceAction(), "두 번째 Discard 승인");
        Qa.Equal(WorkspaceActionResolutionResult.Rejected,
            await vm.SavePendingWorkspaceActionToPathAsync(directory.File("rejected-save.aethermath")), "pending이 없는 두 번째 Save");
        Qa.Equal(WorkspaceActionResolutionResult.Rejected, vm.CancelPendingWorkspaceAction(), "pending이 없는 두 번째 Cancel");
        AssertBlankWorkspace(vm.Session.Workspace);
    }

    private static void AssertBlankWorkspace(EquationWorkspace workspace)
    {
        Qa.Equal(1, workspace.Groups.Length, "새 작업 그룹 최소 개수");
        Qa.Equal(1, workspace.Groups[0].Rows.Length, "새 작업 행 최소 개수");
        var text = Qa.Nodes(workspace).OfType<TextNode>().Single();
        Qa.Equal(string.Empty, text.Text, "새 작업 빈 슬롯 문자열");
        Qa.Check(text.IsEmptySlot, "새 작업 필수 빈 슬롯 flag");
        Qa.Equal(30d, workspace.Style.FontSize, "새 작업 기본 style");

        var ids = new List<Guid> { workspace.Id };
        foreach (var group in workspace.Groups)
        {
            ids.Add(group.Id);
            foreach (var row in group.Rows)
            {
                ids.Add(row.Id);
                ids.AddRange(Qa.Walk(row.Root).Select(node => node.Id));
            }
        }
        Qa.Equal(ids.Count, ids.Distinct().Count(), "새 작업의 workspace/group/row/node ID 고유성");
    }

    private static CoreState CaptureCore(MainWindowViewModel vm) => new(
        vm.Session, Qa.Fingerprint(vm.Session.Workspace), vm.Session.Revision,
        vm.Session.Caret, vm.Session.Selection, vm.Session.SelectedNodeId, vm.Session.IsNodeSelected,
        vm.Session.IsDirty, vm.Session.CanUndo, vm.Session.CanRedo, vm.CurrentPath);

    private static void AssertCore(CoreState expected, CoreState actual, string label) =>
        Qa.Equal(expected, actual, label);

    private sealed record CoreState(EditingSession Session, string Workspace, long Revision,
        MathCaret Caret, MathSelection? Selection, Guid? SelectedNodeId, bool IsNodeSelected,
        bool IsDirty, bool CanUndo, bool CanRedo, string? CurrentPath);

    private sealed class ControlledWorkspaceFileService : IWorkspaceFileService
    {
        private readonly WorkspaceFileService _inner = new();
        private OperationGate? _nextOpenGate;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<SaveGatePlan>> _saveGates = new(StringComparer.OrdinalIgnoreCase);
        private Exception? _nextSaveFailure;

        public int OpenCallCount { get; private set; }
        public int SaveCallCount => Volatile.Read(ref _saveCallCount);
        private int _saveCallCount;

        public void GateNextOpen(OperationGate gate) => _nextOpenGate = gate;
        public OperationGate GateSave(string path, OperationGate gate, bool ignoreCancellation = false)
        {
            _saveGates.GetOrAdd(Path.GetFullPath(path), _ => new ConcurrentQueue<SaveGatePlan>())
                .Enqueue(new SaveGatePlan(gate, ignoreCancellation));
            return gate;
        }
        public void FailNextSave(Exception exception) => _nextSaveFailure = exception;

        public async Task<EquationWorkspace> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            OpenCallCount++;
            var gate = Interlocked.Exchange(ref _nextOpenGate, null);
            if (gate is not null)
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await _inner.OpenAsync(path, cancellationToken).ConfigureAwait(false);
        }

        public async Task SaveAsync(string path, WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _saveCallCount);
            var fullPath = Path.GetFullPath(path);
            SaveGatePlan? plan = null;
            if (_saveGates.TryGetValue(fullPath, out var gates))
                gates.TryDequeue(out plan);
            try
            {
                if (plan is not null)
                    await plan.Gate.WaitAsync(plan.IgnoreCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
                if (plan?.IgnoreCancellation != true)
                    cancellationToken.ThrowIfCancellationRequested();
                var failure = Interlocked.Exchange(ref _nextSaveFailure, null);
                if (failure is not null)
                    throw failure;
                await _inner.SaveAsync(fullPath, snapshot,
                    plan?.IgnoreCancellation == true ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                plan?.Gate.Finish();
            }
        }

        private sealed record SaveGatePlan(OperationGate Gate, bool IgnoreCancellation);
    }

    private sealed class OperationGate
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _released.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public bool HasStarted => _started.Task.IsCompleted;
        public bool HasFinished => _finished.Task.IsCompleted;
        public Task WaitUntilStartedAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public Task WaitUntilFinishedAsync() => _finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Release() => _released.TrySetResult();
        public void Finish() => _finished.TrySetResult();
    }
}
