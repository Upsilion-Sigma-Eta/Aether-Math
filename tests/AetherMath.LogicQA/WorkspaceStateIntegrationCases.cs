using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Nodes;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class WorkspaceStateIntegrationCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("VM-STATE-INIT", "저장 기반 초기화와 상태 서비스를 쓰지 않는 기존 VM 격리", InitializesStateAndKeepsDefaultVmIsolated);
        yield return new("AUTO-RECOVERY-TICK", "실제 자동 tick의 dirty snapshot·ID·스타일과 편집 상태 보존", AutomaticTickPreservesDirtySnapshot);
        yield return new("AUTO-RECOVERY-CLEANUP", "성공 저장 뒤 복구본 정리 및 저장 실패 때 원본 보존", SuccessfulSaveCleansRecoveryAndFailurePreservesIt);
        yield return new("AUTO-RECOVERY-STALE", "정지·세션 교체와 겹친 복구 저장 drain 및 늦은 결과 차단", StopAndReplaceDrainRecoveryWrites);
        yield return new("RECOVERY-RESTORE", "재시작 복구 후보의 명시 복원과 새 dirty Session 생성", RestoresSelectedRecoveryAfterRestart);
        yield return new("RECOVERY-DIRTY-GUARD", "dirty 작업에서 복원 Cancel·Save·Discard 확인", ProtectsDirtyWorkspaceDuringRestore);
        yield return new("RECENT-SUCCESS", "성공한 Save/Open만 recent 목록에 기록", RecordsOnlySuccessfulWorkspaceUse);
        yield return new("RECENT-MISSING-RELINK", "누락·metadata 상태, 명시 경로 재연결, pin·remove와 원본 보존", HandlesMissingMetadataRelinkAndMutation);
    }

    private static async Task InitializesStateAndKeepsDefaultVmIsolated()
    {
        var defaultVm = new MainWindowViewModel();
        await defaultVm.InitializeWorkspaceStateAsync();
        Qa.Check(defaultVm.IsWorkspaceStateReady, "state service 없는 VM 초기화 완료");
        Qa.Equal<Guid?>(null, defaultVm.ActiveRecoveryId, "기본 VM의 recovery ID");
        Qa.Equal(0, defaultVm.RecentWorkspaces.Length, "기본 VM recent 목록");
        Qa.Equal(0, defaultVm.RecoveryCandidates.Length, "기본 VM recovery 후보");
        Qa.Equal(WorkspaceRecoveryFlushStatus.Disabled,
            (await defaultVm.FlushRecoveryAsync()).Status, "기본 VM은 recovery I/O를 비활성화");
        await defaultVm.StopRecoveryAsync();

        using var directory = new TempDirectory();
        var root = directory.File("state");
        var store = new WorkspaceStateService(root);
        var recentPath = directory.File("previous.aethermath");
        var recentWorkspace = CreateWorkspace("최근 수식 β=𝑥");
        await new WorkspaceFileService().SaveAsync(recentPath,
            new EditingSession(recentWorkspace).CaptureSnapshot());
        await store.RecordSuccessfulUseAsync(recentPath, recentWorkspace.Name, DateTimeOffset.UtcNow);

        var recoveryWorkspace = CreateWorkspace("복구 후보 ∫x² dx");
        var recoverySnapshot = new EditingSession(recoveryWorkspace).CaptureSnapshot();
        var recoveryId = Guid.NewGuid();
        Qa.Equal(RecoveryWriteStatus.Written,
            (await store.SaveRecoveryAsync(new WorkspaceRecovery(
                recoveryId, recoverySnapshot, DateTimeOffset.UtcNow, recentPath))).Status,
            "VM 초기화용 recovery 후보 준비");

        await using var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), store, TimeSpan.FromHours(1)));
        var vm = scope.ViewModel;
        Qa.Check(vm.IsWorkspaceStateReady, "저장 service 초기화 readiness");
        Qa.Equal(Path.GetFullPath(recentPath), vm.RecentWorkspaces.Single().FullPath,
            "초기 recent 목록 로드");
        Qa.Equal(recoveryId, vm.RecoveryCandidates.Single().RecoveryId,
            "초기 recovery 목록 로드");
        vm.SelectedRecovery = vm.RecoveryCandidates.Single();
        Qa.Equal(recoveryId, vm.SelectedRecovery?.RecoveryId,
            "명시한 recovery 후보 선택 상태");
        var activeRecoveryId = vm.ActiveRecoveryId;
        await vm.InitializeWorkspaceStateAsync();
        Qa.Equal(1, vm.RecentWorkspaces.Length, "초기화 재호출 recent 중복");
        Qa.Equal(1, vm.RecoveryCandidates.Count(item => item.RecoveryId == recoveryId),
            "초기화 재호출 recovery 중복");
        Qa.Equal(activeRecoveryId, vm.ActiveRecoveryId, "초기화 재호출이 활성 recovery ID를 교체");
    }

    private static async Task AutomaticTickPreservesDirtySnapshot()
    {
        using var directory = new TempDirectory();
        var store = new ControlledWorkspaceStateService(new WorkspaceStateService(directory.File("state")));
        var vm = new MainWindowViewModel(new WorkspaceFileService(), store, TimeSpan.FromMilliseconds(25));
        MakeDirty(vm, "자동 복구 e\u0301 한글🙂");
        var activeId = vm.ActiveRecoveryId ?? throw new InvalidOperationException("활성 RecoveryId 없음");
        var before = Capture(vm);
        var firstSequence = vm.RecoveryFlushSequence;
        var tickCompleted = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.RecoveryFlushSequence) &&
                vm.RecoveryFlushSequence > firstSequence)
                tickCompleted.TrySetResult(vm.RecoveryFlushSequence);
        };
        vm.PropertyChanged += handler;
        try
        {
            await vm.InitializeWorkspaceStateAsync();
            var sequence = await tickCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Qa.Check(sequence > firstSequence, "자동 timer가 RecoveryFlushSequence를 올리지 않음");
            Qa.Equal(WorkspaceRecoveryFlushStatus.Written, vm.LastRecoveryFlushResult?.Status,
                "자동 recovery tick 결과");
            Qa.Check(vm.LastRecoveryFlushAtUtc?.Offset == TimeSpan.Zero,
                "자동 recovery 완료 시각 UTC");

            var read = await store.ReadRecoveryAsync(activeId);
            Qa.Equal(RecoveryReadStatus.Found, read.Status, "자동 tick recovery 실파일");
            var recovery = read.Recovery ?? throw new InvalidOperationException("자동 tick recovery 내용 누락");
            Qa.Equal(before.Snapshot.Revision, recovery.Snapshot.Revision, "recovery revision");
            Qa.Equal(before.Snapshot.StateId, recovery.Snapshot.StateId, "recovery StateId");
            Qa.Equal(Qa.Fingerprint(before.Snapshot.Workspace),
                Qa.Fingerprint(recovery.Snapshot.Workspace), "Unicode·구조·style 및 원본 ID snapshot");
            AssertUnchanged(before, Capture(vm), "자동 복구가 편집/session 상태를 변경");
            Qa.Check(vm.Session.IsDirty && vm.IsRecoverySaveRequired,
                "자동 복구가 dirty/saved marker를 보존하지 않음");
        }
        finally
        {
            vm.PropertyChanged -= handler;
            await vm.StopRecoveryAsync();
        }
    }

    private static async Task SuccessfulSaveCleansRecoveryAndFailurePreservesIt()
    {
        using var directory = new TempDirectory();
        var cleanStore = new WorkspaceStateService(directory.File("clean-state"));
        await using (var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), cleanStore, TimeSpan.FromHours(1))))
        {
            var vm = scope.ViewModel;
            MakeDirty(vm, "저장 후 정리");
            var recoveryId = vm.ActiveRecoveryId!.Value;
            Qa.Equal(WorkspaceRecoveryFlushStatus.Written, (await vm.FlushRecoveryAsync()).Status,
                "저장 전 복구본 생성");
            Qa.Equal(RecoveryReadStatus.Found, (await cleanStore.ReadRecoveryAsync(recoveryId)).Status,
                "저장 전 복구본 존재");

            var savedPath = directory.File("saved.aethermath");
            await vm.SaveToPathAsync(savedPath);
            Qa.Check(!vm.Session.IsDirty, "정상 저장 뒤 Session clean");
            Qa.Check(!vm.IsRecoverySaveRequired, "정상 저장 뒤 recovery 필요 상태");
            Qa.Equal(RecoveryReadStatus.NotFound, (await cleanStore.ReadRecoveryAsync(recoveryId)).Status,
                "성공 저장 뒤 해당 활성 recovery 정리");
            Qa.Equal(Qa.Fingerprint(vm.Session.Workspace),
                Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(savedPath)),
                "저장 성공 파일 내용");
        }

        var futureStore = new WorkspaceStateService(directory.File("future-recovery-state"));
        await using (var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), futureStore, TimeSpan.FromHours(1))))
        {
            var vm = scope.ViewModel;
            MakeDirty(vm, "미지원 복구본은 사용자 선택 전 보존");
            var recoveryId = vm.ActiveRecoveryId!.Value;
            Qa.Equal(WorkspaceRecoveryFlushStatus.Written, (await vm.FlushRecoveryAsync()).Status,
                "future version fixture 복구본 생성");
            var recoveryPath = RecoveryPath(futureStore, recoveryId);
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(recoveryPath)) ??
                throw new InvalidOperationException("Recovery envelope JSON 없음");
            envelope["version"] = 999;
            await File.WriteAllTextAsync(recoveryPath, envelope.ToJsonString());
            var futureBytesBeforeSave = await File.ReadAllBytesAsync(recoveryPath);
            Qa.Equal(RecoveryReadStatus.Unsupported,
                (await futureStore.ReadRecoveryAsync(recoveryId)).Status,
                "future recovery fixture가 unsupported로 진단되지 않음");

            var savedPath = directory.File("save-with-future-recovery.aethermath");
            await vm.SaveToPathAsync(savedPath);
            Qa.Check(!vm.Session.IsDirty && PathEquals(vm.CurrentPath!, savedPath),
                "future recovery 보존 때문에 사용자 Save가 실패");
            var futureBytesAfterSave = await File.ReadAllBytesAsync(recoveryPath);
            Qa.Check(futureBytesBeforeSave.SequenceEqual(futureBytesAfterSave),
                "성공한 사용자 Save가 읽을 수 없는 future recovery를 삭제/덮어씀");
            Qa.Equal(RecoveryReadStatus.Unsupported,
                (await futureStore.ReadRecoveryAsync(recoveryId)).Status,
                "성공 Save 뒤 future recovery 진단이 유지되지 않음");
            Qa.Check(vm.RecoveryDiagnostics.Any(item =>
                    item.Code == WorkspaceStateDiagnosticCodes.RecoveryUnsupportedVersion),
                "성공 Save 뒤 future recovery 진단을 목록에 유지하지 않음");
        }

        var failStore = new WorkspaceStateService(directory.File("failed-state"));
        var failingFiles = new FailingWorkspaceFileService();
        await using (var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(failingFiles, failStore, TimeSpan.FromHours(1))))
        {
            var vm = scope.ViewModel;
            MakeDirty(vm, "실패 시 복구본 보존");
            var recoveryId = vm.ActiveRecoveryId!.Value;
            Qa.Equal(WorkspaceRecoveryFlushStatus.Written, (await vm.FlushRecoveryAsync()).Status,
                "실패 branch 복구본 생성");
            var path = RecoveryPath(failStore, recoveryId);
            var originalBytes = await File.ReadAllBytesAsync(path);
            await vm.SaveToPathAsync(directory.File("save-fails.aethermath"));
            Qa.Check(vm.Session.IsDirty && vm.IsRecoverySaveRequired,
                "실패한 Save가 dirty/recovery 필요 상태를 지움");
            var bytesAfterFailedSave = await File.ReadAllBytesAsync(path);
            Qa.Check(originalBytes.SequenceEqual(bytesAfterFailedSave),
                "실패한 Save가 기존 recovery bytes를 덮어씀");
            Qa.Equal(RecoveryReadStatus.Found, (await failStore.ReadRecoveryAsync(recoveryId)).Status,
                "실패한 Save 뒤 recovery를 보존");
        }
    }

    private static async Task StopAndReplaceDrainRecoveryWrites()
    {
        using var directory = new TempDirectory();
        var store = new ControlledWorkspaceStateService(new WorkspaceStateService(directory.File("state")));
        var vm = new MainWindowViewModel(new WorkspaceFileService(), store, TimeSpan.FromHours(1));
        MakeDirty(vm, "정지 중 recovery");
        await vm.InitializeWorkspaceStateAsync();
        var gate = new StateOperationGate();
        store.GateNextRecoveryWrite(gate);
        var activeId = vm.ActiveRecoveryId!.Value;
        var sequenceBeforeStop = vm.RecoveryFlushSequence;
        var lastResultBeforeStop = vm.LastRecoveryFlushResult;
        var flush = vm.FlushRecoveryAsync();
        await gate.WaitUntilStartedAsync();
        var stop = vm.StopRecoveryAsync();
        try
        {
            Qa.Check(!stop.IsCompleted, "StopRecovery가 진행 중 저장을 drain하지 않고 반환");
        }
        finally
        {
            gate.Release();
        }
        var result = await flush.WaitAsync(TimeSpan.FromSeconds(10));
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        Qa.Equal(WorkspaceRecoveryFlushStatus.Stale, result.Status,
            "정지 경합 recovery 결과: " + result.Status);
        Qa.Equal(1, store.CompletedRecoveryWriteCalls, "Stop이 진행 중 복구 저장을 drain하지 않음");
        Qa.Equal(sequenceBeforeStop, vm.RecoveryFlushSequence, "stale stop 결과가 완료 알림을 발행함");
        Qa.Equal(lastResultBeforeStop, vm.LastRecoveryFlushResult, "stale stop 결과가 마지막 결과를 덮어씀");
        Qa.Equal(RecoveryReadStatus.Found, (await store.ReadRecoveryAsync(activeId)).Status,
            "Stop이 이미 시작된 쓰기를 완료 전에 버림");
        var callsAfterDrain = store.RecoveryWriteCalls;
        Qa.Equal(WorkspaceRecoveryFlushStatus.Disabled, (await vm.FlushRecoveryAsync()).Status,
            "정지 뒤 새 recovery tick 거부");
        Qa.Equal(callsAfterDrain, store.RecoveryWriteCalls, "정지 뒤 새 service write 시작");
        Qa.Check(activeId != Guid.Empty, "fixture recovery ID");

        // 실제 timer tick은 Stop의 연결 token으로 서비스 경계를 취소하고 결과 알림을 내지 않는다.
        var timerStore = new ControlledWorkspaceStateService(
            new WorkspaceStateService(directory.File("timer-stop-state")));
        var timerVm = new MainWindowViewModel(new WorkspaceFileService(), timerStore,
            TimeSpan.FromMilliseconds(35));
        MakeDirty(timerVm, "timer tick 정지 취소");
        var timerGate = new StateOperationGate();
        timerStore.GateNextRecoveryWrite(timerGate);
        var timerSequenceBefore = timerVm.RecoveryFlushSequence;
        var timerLastBefore = timerVm.LastRecoveryFlushResult;
        await timerVm.InitializeWorkspaceStateAsync();
        await timerGate.WaitUntilStartedAsync();
        var timerStop = timerVm.StopRecoveryAsync();
        try
        {
            await timerStop.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            timerGate.Release();
        }
        await timerStop.WaitAsync(TimeSpan.FromSeconds(10));
        await timerGate.WaitUntilFinishedAsync();
        Qa.Check(timerGate.WasCanceled, "Stop이 진행 중 실제 timer write token을 취소하지 않음");
        Qa.Equal(1, timerStore.RecoveryWriteCalls, "timer tick이 서비스 write 경계에 도달하지 않음");
        Qa.Equal(0, timerStore.CompletedRecoveryWriteCalls, "취소된 timer write가 inner service를 완료함");
        Qa.Equal(timerSequenceBefore, timerVm.RecoveryFlushSequence,
            "Stop으로 취소된 timer가 완료 sequence를 올림");
        Qa.Equal(timerLastBefore, timerVm.LastRecoveryFlushResult,
            "Stop으로 취소된 timer가 LastResult를 바꿈");
        var timerRecoveryId = timerVm.ActiveRecoveryId!.Value;
        Qa.Equal(RecoveryReadStatus.NotFound,
            (await timerStore.ReadRecoveryAsync(timerRecoveryId)).Status,
            "취소된 timer tick이 실제 recovery 파일을 남김");
        var timerCallsAfterStop = timerStore.RecoveryWriteCalls;
        Qa.Equal(WorkspaceRecoveryFlushStatus.Disabled, (await timerVm.FlushRecoveryAsync()).Status,
            "Stop 뒤 새 Flush가 비활성 결과를 반환하지 않음");
        Qa.Equal(timerCallsAfterStop, timerStore.RecoveryWriteCalls,
            "Stop 뒤 새 Flush가 service I/O를 시작");

        // 세션 전환은 이전 복구 ID를 retire하고, 이미 대기 중인 tick의 완료가 새 VM 상태를 덮지 않아야 한다.
        var replacementStore = new ControlledWorkspaceStateService(new WorkspaceStateService(directory.File("replace-state")));
        var replacementVm = new MainWindowViewModel(new WorkspaceFileService(), replacementStore, TimeSpan.FromHours(1));
        MakeDirty(replacementVm, "전환 전 상태");
        await replacementVm.InitializeWorkspaceStateAsync();
        var oldRecoveryId = replacementVm.ActiveRecoveryId!.Value;
        var replacementGate = new StateOperationGate(ignoreCancellation: true);
        replacementStore.GateNextRecoveryWrite(replacementGate);
        var staleFlush = replacementVm.FlushRecoveryAsync();
        await replacementGate.WaitUntilStartedAsync();
        Guid? newRecoveryId;
        try
        {
            Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired,
                replacementVm.RequestNewWorkspace(), "대기 tick 중 dirty New 확인");
            var oldSession = replacementVm.Session;
            Qa.Equal(WorkspaceActionResolutionResult.Completed,
                replacementVm.DiscardPendingWorkspaceAction(), "대기 tick 중 명시 discard");
            Qa.Check(!ReferenceEquals(oldSession, replacementVm.Session), "discard가 Session을 교체하지 않음");
            newRecoveryId = replacementVm.ActiveRecoveryId;
        }
        finally
        {
            replacementGate.Release();
        }
        await replacementGate.WaitUntilFinishedAsync();
        var staleResult = await staleFlush.WaitAsync(TimeSpan.FromSeconds(10));
        await replacementVm.StopRecoveryAsync();
        Qa.Equal(WorkspaceRecoveryFlushStatus.Stale, staleResult.Status,
            "세션 교체 뒤 이전 tick 결과가 stale 처리되지 않음");
        Qa.Check(newRecoveryId is not null && newRecoveryId != oldRecoveryId,
            "새 Session이 새 RecoveryId를 얻지 않음");
        Qa.Equal(RecoveryReadStatus.NotFound,
            (await replacementStore.ReadRecoveryAsync(oldRecoveryId)).Status,
            "세션 교체 후 이전 recovery의 늦은 쓰기/잔류");
        Qa.Equal(1, replacementStore.CompletedRecoveryWriteCalls,
            "취소를 무시한 wrapper가 실제 늦은 복구 쓰기를 완료하지 않음");
        Qa.Check(!replacementVm.Session.IsDirty && !replacementVm.IsRecoverySaveRequired,
            "오래된 tick이 새 빈 Session의 저장 상태를 오염");

        // Initialization이 recent read에서 대기하는 동안 Stop하면 완료 경합이 timer를 되살리면 안 된다.
        var initRaceStore = new ControlledWorkspaceStateService(
            new WorkspaceStateService(directory.File("init-stop-state")));
        var initRaceVm = new MainWindowViewModel(new WorkspaceFileService(), initRaceStore,
            TimeSpan.FromMilliseconds(40));
        MakeDirty(initRaceVm, "초기화와 정지 경합");
        var recentReadGate = new StateOperationGate();
        initRaceStore.GateNextRecentRead(recentReadGate);
        var initialize = initRaceVm.InitializeWorkspaceStateAsync();
        await recentReadGate.WaitUntilStartedAsync();
        var stopDuringInit = initRaceVm.StopRecoveryAsync();
        try
        {
            Qa.Check(!stopDuringInit.IsCompleted, "Stop이 초기화 중인 state read를 drain하지 않음");
        }
        finally
        {
            recentReadGate.Release();
        }
        await Task.WhenAll(initialize, stopDuringInit).WaitAsync(TimeSpan.FromSeconds(10));
        await recentReadGate.WaitUntilFinishedAsync();
        var writesAfterInitStop = initRaceStore.RecoveryWriteCalls;
        var sequenceAfterInitStop = initRaceVm.RecoveryFlushSequence;
        Qa.Equal(WorkspaceRecoveryFlushStatus.Disabled, (await initRaceVm.FlushRecoveryAsync()).Status,
            "Stop과 겹친 Initialize 완료가 timer를 다시 시작함");
        Qa.Equal(writesAfterInitStop, initRaceStore.RecoveryWriteCalls,
            "Stop 뒤 Flush가 state service write를 시작");
        Qa.Equal(sequenceAfterInitStop, initRaceVm.RecoveryFlushSequence,
            "Stop 뒤 Flush가 완료 알림을 발행");

        var resumedTick = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler resumedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.RecoveryFlushSequence) &&
                initRaceVm.RecoveryFlushSequence > sequenceAfterInitStop)
                resumedTick.TrySetResult(initRaceVm.RecoveryFlushSequence);
        };
        initRaceVm.PropertyChanged += resumedHandler;
        try
        {
            await initRaceVm.InitializeWorkspaceStateAsync();
            var resumedSequence = await resumedTick.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Qa.Check(resumedSequence > sequenceAfterInitStop,
                "명시적 재초기화가 recovery timer를 다시 시작하지 않음");
            Qa.Equal(WorkspaceRecoveryFlushStatus.Written, initRaceVm.LastRecoveryFlushResult?.Status,
                "재초기화 후 자동 recovery 결과");
        }
        finally
        {
            initRaceVm.PropertyChanged -= resumedHandler;
            await initRaceVm.StopRecoveryAsync();
        }
    }

    private static async Task RestoresSelectedRecoveryAfterRestart()
    {
        using var directory = new TempDirectory();
        var store = new WorkspaceStateService(directory.File("state"));
        var originalWorkspace = CreateWorkspace("재시작 복구 ∑ₙ xₙ");
        var originalSnapshot = new EditingSession(originalWorkspace).CaptureSnapshot();
        var recoveryId = Guid.NewGuid();
        Qa.Equal(RecoveryWriteStatus.Written,
            (await store.SaveRecoveryAsync(new WorkspaceRecovery(
                recoveryId, originalSnapshot, DateTimeOffset.UtcNow, directory.File("original.aethermath")))).Status,
            "재시작 복구 fixture");

        await using var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), store, TimeSpan.FromHours(1)));
        var vm = scope.ViewModel;
        Qa.Equal(recoveryId, vm.RecoveryCandidates.Single().RecoveryId, "재시작 시 복구 후보 표시");
        vm.SelectedRecovery = vm.RecoveryCandidates.Single();
        Qa.Equal(recoveryId, vm.SelectedRecovery?.RecoveryId, "복구 후보 명시 선택");
        var oldSession = vm.Session;
        var restored = await vm.RestoreRecoveryAsync(recoveryId);
        Qa.Equal(RecoveryRestoreRequestStatus.Restored, restored.Status, "선택한 복구본 명시 복원");
        Qa.Check(!ReferenceEquals(oldSession, vm.Session), "복원 후 새 EditingSession 생성");
        Qa.Equal(originalWorkspace.Id, vm.Session.Workspace.Id, "복구 workspace ID 보존");
        Qa.Equal(Qa.Fingerprint(originalWorkspace), Qa.Fingerprint(vm.Session.Workspace),
            "복구 AST/모든 노드 ID/style 보존");
        Qa.Check(vm.Session.IsDirty, "복원 직후 새 Session이 dirty 상태가 아님");
        Qa.Equal<string?>(null, vm.CurrentPath, "복원된 작업에 원본 파일 경로를 자동 적용");
        Qa.Check(vm.ActiveRecoveryId is not null && vm.ActiveRecoveryId != recoveryId,
            "복원 후 새 active RecoveryId");
        Qa.Equal(RecoveryReadStatus.Found, (await store.ReadRecoveryAsync(recoveryId)).Status,
            "명시 복원이 원본 recovery를 자동 삭제");

        var ownStore = new WorkspaceStateService(directory.File("own-recovery-state"));
        await using var ownScope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), ownStore, TimeSpan.FromHours(1)));
        var ownVm = ownScope.ViewModel;
        MakeDirty(ownVm, "현재 작업의 복구 후보 자체를 복원");
        var ownRecoveryId = ownVm.ActiveRecoveryId!.Value;
        Qa.Equal(WorkspaceRecoveryFlushStatus.Written, (await ownVm.FlushRecoveryAsync()).Status,
            "현재 Session의 recovery 후보 기록");
        var ownRecoveryPath = RecoveryPath(ownStore, ownRecoveryId);
        var ownRecoveryBytes = await File.ReadAllBytesAsync(ownRecoveryPath);
        var ownCandidate = ownVm.RecoveryCandidates.Single(item => item.RecoveryId == ownRecoveryId);
        ownVm.SelectedRecovery = ownCandidate;
        Qa.Equal(RecoveryRestoreRequestStatus.ConfirmationRequired,
            (await ownVm.RestoreRecoveryAsync(ownRecoveryId)).Status,
            "dirty Session의 자기 recovery 복원은 확인을 요구하지 않음");
        Qa.Equal(WorkspaceActionResolutionResult.Completed, ownVm.DiscardPendingWorkspaceAction(),
            "자기 recovery를 선택한 뒤 Discard 복원이 실패");
        Qa.Check(ownVm.Session.IsDirty && ownVm.CurrentPath is null &&
                 ownVm.ActiveRecoveryId is Guid currentId && currentId != ownRecoveryId,
            "자기 recovery 복원 뒤 새 dirty Session/ID/path 규칙이 깨짐");
        await ownVm.StopRecoveryAsync();
        var ownRecoveryBytesAfterRestore = await File.ReadAllBytesAsync(ownRecoveryPath);
        Qa.Check(ownRecoveryBytes.SequenceEqual(ownRecoveryBytesAfterRestore),
            "복원 출처인 기존 ActiveRecoveryId 파일을 자동 cleanup이 삭제/덮어씀");
        Qa.Equal(RecoveryReadStatus.Found, (await ownStore.ReadRecoveryAsync(ownRecoveryId)).Status,
            "자기 recovery 복원 뒤 원본 후보가 보존되지 않음");

        var ownSaveStore = new WorkspaceStateService(directory.File("own-recovery-save-state"));
        await using var ownSaveScope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), ownSaveStore, TimeSpan.FromHours(1)));
        var ownSaveVm = ownSaveScope.ViewModel;
        MakeDirty(ownSaveVm, "저장 경유 자기 recovery 후보");
        var ownSaveRecoveryId = ownSaveVm.ActiveRecoveryId!.Value;
        Qa.Equal(WorkspaceRecoveryFlushStatus.Written,
            (await ownSaveVm.FlushRecoveryAsync()).Status,
            "저장 경유 자기 recovery 후보 기록");
        var ownSaveRecoveryPath = RecoveryPath(ownSaveStore, ownSaveRecoveryId);
        var ownSaveRecoveryBytes = await File.ReadAllBytesAsync(ownSaveRecoveryPath);
        var currentLeaf = Qa.Nodes(ownSaveVm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(ownSaveVm.Session, currentLeaf.Id, currentLeaf.Text.Length);
        ownSaveVm.Session.InsertText(" 저장 전 최신 편집");
        var latestCurrentSnapshot = ownSaveVm.Session.CaptureSnapshot();
        var ownSaveCandidate = ownSaveVm.RecoveryCandidates.Single(item =>
            item.RecoveryId == ownSaveRecoveryId);
        ownSaveVm.SelectedRecovery = ownSaveCandidate;
        Qa.Equal(RecoveryRestoreRequestStatus.ConfirmationRequired,
            (await ownSaveVm.RestoreRecoveryAsync(ownSaveRecoveryId)).Status,
            "저장 경유 자기 recovery Restore 확인");
        var pendingFlush = await ownSaveVm.FlushRecoveryAsync();
        Qa.Equal(WorkspaceRecoveryFlushStatus.Skipped, pendingFlush.Status,
            "선택된 자기 recovery를 보존해야 하는 pending Restore 중 Flush가 기록함");
        Qa.Check(pendingFlush.Diagnostic is not null,
            "pending Restore 중 동일 recovery ID 보호 진단 누락");
        var ownSaveRecoveryBytesWhilePending = await File.ReadAllBytesAsync(ownSaveRecoveryPath);
        Qa.Check(ownSaveRecoveryBytes.SequenceEqual(ownSaveRecoveryBytesWhilePending),
            "pending Restore 중 timer/manual Flush가 선택 원본 recovery를 덮어씀");
        Qa.Equal(WorkspaceActionResolutionResult.Canceled,
            ownSaveVm.CancelPendingWorkspaceAction(), "pending 자기 recovery Restore 취소");
        Qa.Equal(WorkspaceRecoveryFlushStatus.Written,
            (await ownSaveVm.FlushRecoveryAsync()).Status,
            "Restore 취소 뒤 정상 recovery 기록이 재개되지 않음");
        var ownSaveRecoveryBytesBeforeFinalRestore = await File.ReadAllBytesAsync(ownSaveRecoveryPath);
        ownSaveVm.SelectedRecovery = ownSaveVm.RecoveryCandidates.Single(item =>
            item.RecoveryId == ownSaveRecoveryId);
        Qa.Equal(RecoveryRestoreRequestStatus.ConfirmationRequired,
            (await ownSaveVm.RestoreRecoveryAsync(ownSaveRecoveryId)).Status,
            "취소 뒤 자기 recovery 재요청 확인");
        var savedCurrentPath = directory.File("save-before-own-recovery-restore.aethermath");
        Qa.Equal(WorkspaceActionResolutionResult.Completed,
            await ownSaveVm.SavePendingWorkspaceActionToPathAsync(savedCurrentPath),
            "자기 recovery 복원 전 현재 작업 Save");
        Qa.Equal(Qa.Fingerprint(latestCurrentSnapshot.Workspace),
            Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(savedCurrentPath)),
            "복원 전 현재 작업의 최신 편집을 저장하지 않음");
        Qa.Check(ownSaveVm.Session.IsDirty && ownSaveVm.CurrentPath is null &&
                 ownSaveVm.ActiveRecoveryId is Guid rotatedId && rotatedId != ownSaveRecoveryId,
            "Save 경유 자기 recovery 복원 뒤 새 dirty Session/ID/path 규칙이 깨짐");
        await ownSaveVm.StopRecoveryAsync();
        var ownSaveRecoveryBytesAfterRestore = await File.ReadAllBytesAsync(ownSaveRecoveryPath);
        Qa.Check(ownSaveRecoveryBytesBeforeFinalRestore.SequenceEqual(ownSaveRecoveryBytesAfterRestore),
            "Save 경유 자기 recovery 복원 중 출처 파일을 자동 cleanup이 삭제/덮어씀");
        Qa.Equal(RecoveryReadStatus.Found,
            (await ownSaveStore.ReadRecoveryAsync(ownSaveRecoveryId)).Status,
            "Save 경유 자기 recovery 복원 뒤 원본 후보가 보존되지 않음");
    }

    private static async Task ProtectsDirtyWorkspaceDuringRestore()
    {
        using var directory = new TempDirectory();
        var recoveryStore = new WorkspaceStateService(directory.File("state"));
        var recoveredWorkspace = CreateWorkspace("확인 대상 복구본");
        var recoveryId = Guid.NewGuid();
        await recoveryStore.SaveRecoveryAsync(new WorkspaceRecovery(
            recoveryId, new EditingSession(recoveredWorkspace).CaptureSnapshot(), DateTimeOffset.UtcNow, null));

        await using (var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), recoveryStore, TimeSpan.FromHours(1))))
        {
            var vm = scope.ViewModel;
            await MakeCurrentDirtyAsync(vm, directory.File("before-cancel.aethermath"));
            var beforeCancel = Capture(vm);
            Qa.Equal(RecoveryRestoreRequestStatus.ConfirmationRequired,
                (await vm.RestoreRecoveryAsync(recoveryId)).Status, "dirty 복원 확인 필요");
            Qa.Equal(WorkspaceActionKind.RestoreRecovery, vm.PendingWorkspaceAction, "dirty 복원 pending kind");
            Qa.Equal(WorkspaceActionResolutionResult.Canceled,
                vm.CancelPendingWorkspaceAction(), "복원 취소");
            AssertUnchanged(beforeCancel, Capture(vm), "복원 취소가 현재 작업을 변경");
            Qa.Equal(string.Empty, vm.PendingWorkspaceActionError,
                "복원 Cancel 뒤 불필요한 error");
        }

        await using (var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), recoveryStore, TimeSpan.FromHours(1))))
        {
            var vm = scope.ViewModel;
            var savePath = directory.File("save-before-restore.aethermath");
            await MakeCurrentDirtyAsync(vm, savePath);
            var currentBeforeRestore = vm.Session.CaptureSnapshot();
            Qa.Equal(RecoveryRestoreRequestStatus.ConfirmationRequired,
                (await vm.RestoreRecoveryAsync(recoveryId)).Status, "Save branch 복원 확인");
            Qa.Equal(WorkspaceActionResolutionResult.Completed,
                await vm.SavePendingWorkspaceActionToPathAsync(savePath), "dirty 복원 전 저장 후 복원");
            Qa.Equal(Qa.Fingerprint(currentBeforeRestore.Workspace),
                Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(savePath)),
                "복원 전 현재 작업 저장 snapshot");
            Qa.Equal(Qa.Fingerprint(recoveredWorkspace), Qa.Fingerprint(vm.Session.Workspace),
                "Save 후 대상 recovery 복원");
            Qa.Check(vm.Session.IsDirty, "Save 후 복원된 새 Session이 clean");
            Qa.Equal<string?>(null, vm.CurrentPath, "Save 경유 복원 뒤 현재 경로");
        }

        await using (var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), recoveryStore, TimeSpan.FromHours(1))))
        {
            var vm = scope.ViewModel;
            await MakeCurrentDirtyAsync(vm, directory.File("discard-current.aethermath"));
            Qa.Equal(RecoveryRestoreRequestStatus.ConfirmationRequired,
                (await vm.RestoreRecoveryAsync(recoveryId)).Status, "Discard branch 복원 확인");
            Qa.Equal(WorkspaceActionResolutionResult.Completed,
                vm.DiscardPendingWorkspaceAction(), "dirty 현재 작업 버리고 복원");
            Qa.Equal(Qa.Fingerprint(recoveredWorkspace), Qa.Fingerprint(vm.Session.Workspace),
                "Discard 후 recovery 내용");
            Qa.Check(vm.Session.IsDirty && vm.CurrentPath is null,
                "Discard 후 복원 작업은 dirty·경로 없음이어야 함");
        }
    }

    private static async Task RecordsOnlySuccessfulWorkspaceUse()
    {
        using var directory = new TempDirectory();
        var store = new WorkspaceStateService(directory.File("state"));
        var files = new FailingWorkspaceFileService();
        await using var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(files, store, TimeSpan.FromHours(1)));
        var vm = scope.ViewModel;
        var savePath = directory.File("success-save.aethermath");
        files.AllowWrites = true;
        await vm.SaveToPathAsync(savePath);
        Qa.Check(vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, savePath)),
            "성공한 Save가 recent를 기록하지 않음");

        var openPath = directory.File("success-open.aethermath");
        var openWorkspace = CreateWorkspace("최근 목록을 통한 열기 𝑦");
        await new WorkspaceFileService().SaveAsync(openPath, new EditingSession(openWorkspace).CaptureSnapshot());
        Qa.Equal(WorkspaceActionRequestResult.Completed,
            await vm.OpenRecentWorkspaceAsync(openPath), "recent 경로 열기 결과");
        Qa.Check(vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, openPath)),
            "성공한 Open이 recent를 기록하지 않음");

        files.AllowWrites = false;
        var dirtyLeaf = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, dirtyLeaf.Id, dirtyLeaf.Text.Length);
        vm.Session.InsertText(" dirty");
        var failedSavePath = directory.File("failed-save.aethermath");
        await vm.SaveToPathAsync(failedSavePath);
        Qa.Check(!vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, failedSavePath)),
            "실패한 Save가 recent를 기록");

        var invalidPath = directory.File("bad-open.aethermath");
        await File.WriteAllTextAsync(invalidPath, "{broken");
        Qa.Equal(WorkspaceActionRequestResult.Failed, await vm.OpenRecentWorkspaceAsync(invalidPath),
            "손상 파일 recent Open 결과");
        Qa.Check(!vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, invalidPath)),
            "실패한 Open이 recent를 기록");

        var canceledPath = directory.File("canceled-open.aethermath");
        var canceledWorkspace = CreateWorkspace("취소 뒤에도 열지 않을 파일");
        await new WorkspaceFileService().SaveAsync(canceledPath,
            new EditingSession(canceledWorkspace).CaptureSnapshot());
        var beforeCanceledOpen = Capture(vm);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Qa.Equal(WorkspaceActionRequestResult.Canceled,
            await vm.OpenFromPathAsync(canceledPath, canceled.Token), "취소한 Open 결과");
        AssertUnchanged(beforeCanceledOpen, Capture(vm), "취소한 Open이 현재 Session/path를 변경");
        Qa.Check(!vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, canceledPath)),
            "취소한 Open이 recent를 기록");

        var selectedRecent = vm.RecentWorkspaces.Single(entry => PathEquals(entry.FullPath, openPath));
        var beforePreview = Capture(vm);
        vm.SelectedRecentWorkspace = selectedRecent;
        Qa.Check(vm.SelectedRecentWorkspacePreview.Contains(openPath, StringComparison.Ordinal),
            "recent 선택 미리보기에 경로가 표시되지 않음");
        AssertUnchanged(beforePreview, Capture(vm), "recent 미리보기 선택이 수식 편집 상태를 변경");
    }

    private static async Task HandlesMissingMetadataRelinkAndMutation()
    {
        using var directory = new TempDirectory();
        var root = directory.File("state");
        var store = new WorkspaceStateService(root);
        var originalPath = directory.File("original.aethermath");
        var originalWorkspace = CreateWorkspace("원래 파일 보존");
        await new WorkspaceFileService().SaveAsync(originalPath,
            new EditingSession(originalWorkspace).CaptureSnapshot());
        var originalBytes = await File.ReadAllBytesAsync(originalPath);

        var missingPath = directory.File("missing.aethermath");
        await File.WriteAllTextAsync(missingPath, "temporary");
        await store.RecordSuccessfulUseAsync(missingPath, "누락 대상", DateTimeOffset.UtcNow);
        File.Delete(missingPath);
        await store.RecordSuccessfulUseAsync(originalPath, "원본", DateTimeOffset.UtcNow);
        var metadataUnavailablePath = directory.File("directory-metadata");
        Directory.CreateDirectory(metadataUnavailablePath);
        await store.RecordSuccessfulUseAsync(metadataUnavailablePath, "metadata 진단", DateTimeOffset.UtcNow);

        await using var scope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), store, TimeSpan.FromHours(1)));
        var vm = scope.ViewModel;
        var missing = vm.RecentWorkspaces.Single(entry => PathEquals(entry.FullPath, missingPath));
        Qa.Equal(RecentWorkspaceFileStatus.Missing, missing.FileStatus, "누락 recent 상태");
        Qa.Equal<DateTimeOffset?>(null, missing.FileModifiedAtUtc, "누락 파일 수정 시각");
        var metadata = vm.RecentWorkspaces.Single(entry => PathEquals(entry.FullPath, metadataUnavailablePath));
        Qa.Equal(RecentWorkspaceFileStatus.MetadataUnavailable, metadata.FileStatus, "metadata 실패 상태");
        Qa.Check(metadata.FileDiagnostic is not null, "metadata 실패 개별 진단 누락");
        Qa.Check(vm.IsWorkspaceStateReady && vm.WorkspaceStateError.Length == 0,
            "metadata 실패가 recent 목록 초기화를 전역 실패 처리");

        Qa.Equal(RecentWorkspaceMutationStatus.Applied,
            await vm.PinRecentWorkspaceAsync(originalPath), "recent pin");
        Qa.Check(vm.RecentWorkspaces.Single(entry => PathEquals(entry.FullPath, originalPath)).IsPinned,
            "pin 결과가 VM 목록에 반영되지 않음");
        Qa.Equal(RecentWorkspaceMutationStatus.Applied,
            await vm.UnpinRecentWorkspaceAsync(originalPath), "recent unpin");

        var replacementPath = directory.File("replacement.aethermath");
        var replacementWorkspace = CreateWorkspace("대체 위치");
        await new WorkspaceFileService().SaveAsync(replacementPath,
            new EditingSession(replacementWorkspace).CaptureSnapshot());
        var replacementBytes = await File.ReadAllBytesAsync(replacementPath);
        Qa.Equal(WorkspaceActionRequestResult.Completed,
            await vm.RelinkRecentWorkspaceAsync(missingPath, replacementPath), "누락 recent 명시 재연결");
        Qa.Check(!vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, missingPath)),
            "재연결 뒤 이전 누락 경로 잔존");
        Qa.Check(vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, replacementPath) &&
            entry.FileStatus == RecentWorkspaceFileStatus.Available), "재연결 경로/상태 미반영");
        var originalBytesAfterRelink = await File.ReadAllBytesAsync(originalPath);
        Qa.Check(originalBytes.SequenceEqual(originalBytesAfterRelink),
            "recent 재연결이 별도 원본 파일을 수정");
        var replacementBytesAfterRelink = await File.ReadAllBytesAsync(replacementPath);
        Qa.Check(replacementBytes.SequenceEqual(replacementBytesAfterRelink),
            "recent 재연결이 선택한 대체 파일을 수정");

        Qa.Equal(RecentWorkspaceMutationStatus.Applied,
            await vm.RemoveRecentWorkspaceAsync(metadataUnavailablePath), "metadata-error recent 제거");
        Qa.Check(!vm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, metadataUnavailablePath)),
            "recent 제거가 표시 목록에 반영되지 않음");
        Qa.Check(Directory.Exists(metadataUnavailablePath), "recent 제거가 사용자 디렉터리를 삭제");

        // Index 잠금 오류는 문서 저장 성공 상태와 분리해 서비스 진단만 갱신해야 한다.
        var lockedRoot = directory.File("metadata-error-state");
        var lockedService = new WorkspaceStateService(lockedRoot);
        var lockedVm = new MainWindowViewModel(new WorkspaceFileService(), lockedService,
            TimeSpan.FromHours(1));
        await using var lockedScope = await StateVmScope.CreateAsync(lockedVm);
        var lockPath = Path.Combine(lockedRoot, WorkspaceStateService.RootLockFileName);
        await using (var stateLock = new FileStream(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None))
        {
            var documentPath = directory.File("document-save-with-state-lock.aethermath");
            await lockedVm.SaveToPathAsync(documentPath);
            Qa.Check(!lockedVm.Session.IsDirty, "recent metadata lock이 문서 Save를 실패시킴");
            Qa.Equal(Path.GetFullPath(documentPath), lockedVm.CurrentPath,
                "recent metadata lock이 저장 경로를 덮어씀");
            Qa.Equal("저장됨", lockedVm.StatusText, "recent metadata 오류가 문서 저장 상태를 오염");
            Qa.Check(lockedVm.WorkspaceStateError.Length > 0,
                "recent index lock 오류가 별도 진단되지 않음");
            Qa.Equal(Qa.Fingerprint(lockedVm.Session.Workspace),
                Qa.Fingerprint(await new WorkspaceFileService().OpenAsync(documentPath)),
                "recent index 오류가 실제 저장 파일을 손상");
        }
        await lockedVm.RefreshWorkspaceStateAsync();
        Qa.Check(lockedVm.WorkspaceStateError.Length == 0,
            "잠금 해제 후 recent index 재시도에서 진단이 초기화되지 않음");

        var blockedStore = new ControlledWorkspaceStateService(
            new WorkspaceStateService(directory.File("relink-write-blocked-state")));
        var oldRecentPath = directory.File("old-recent.aethermath");
        var oldRecentWorkspace = CreateWorkspace("기존 recent 경로");
        await new WorkspaceFileService().SaveAsync(oldRecentPath,
            new EditingSession(oldRecentWorkspace).CaptureSnapshot());
        await blockedStore.RecordSuccessfulUseAsync(oldRecentPath, oldRecentWorkspace.Name, DateTimeOffset.UtcNow);
        var newRecentPath = directory.File("new-recent.aethermath");
        var newRecentWorkspace = CreateWorkspace("기록 실패할 대체 경로");
        await new WorkspaceFileService().SaveAsync(newRecentPath,
            new EditingSession(newRecentWorkspace).CaptureSnapshot());
        var oldRecentBytes = await File.ReadAllBytesAsync(oldRecentPath);
        var newRecentBytes = await File.ReadAllBytesAsync(newRecentPath);
        blockedStore.BlockSuccessfulUseFor(newRecentPath);
        await using var blockedScope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), blockedStore, TimeSpan.FromHours(1)));
        var blockedVm = blockedScope.ViewModel;
        Qa.Equal(WorkspaceActionRequestResult.Completed,
            await blockedVm.RelinkRecentWorkspaceAsync(oldRecentPath, newRecentPath),
            "replacement document open 자체가 실패");
        Qa.Check(blockedVm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, oldRecentPath)),
            "replacement recent 기록이 WriteBlocked인데 기존 경로를 먼저 제거");
        Qa.Check(!blockedVm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, newRecentPath)),
            "WriteBlocked replacement 경로를 recent 목록에 성공으로 표시");
        Qa.Equal(0, blockedStore.RemoveRecentCallCount(oldRecentPath),
            "replacement 기록 실패 후 old recent 제거 API를 호출");
        Qa.Check(blockedVm.WorkspaceStateError.Length > 0,
            "replacement recent WriteBlocked 진단 누락");
        var oldRecentBytesAfterRelink = await File.ReadAllBytesAsync(oldRecentPath);
        var newRecentBytesAfterRelink = await File.ReadAllBytesAsync(newRecentPath);
        Qa.Check(oldRecentBytes.SequenceEqual(oldRecentBytesAfterRelink) &&
                 newRecentBytes.SequenceEqual(newRecentBytesAfterRelink),
            "WriteBlocked relink가 기존 문서 파일을 수정");

        var lateStore = new ControlledWorkspaceStateService(
            new WorkspaceStateService(directory.File("late-recent-diagnostic-state")));
        var lateOldPath = directory.File("late-old-recent.aethermath");
        var lateOldWorkspace = CreateWorkspace("늦은 진단의 기존 항목");
        await new WorkspaceFileService().SaveAsync(lateOldPath,
            new EditingSession(lateOldWorkspace).CaptureSnapshot());
        await lateStore.RecordSuccessfulUseAsync(lateOldPath, lateOldWorkspace.Name, DateTimeOffset.UtcNow);
        var lateReplacementPath = directory.File("late-replacement.aethermath");
        var lateReplacementWorkspace = CreateWorkspace("늦게 실패할 recent 기록");
        await new WorkspaceFileService().SaveAsync(lateReplacementPath,
            new EditingSession(lateReplacementWorkspace).CaptureSnapshot());
        lateStore.BlockSuccessfulUseFor(lateReplacementPath);
        await using var lateScope = await StateVmScope.CreateAsync(
            new MainWindowViewModel(new WorkspaceFileService(), lateStore, TimeSpan.FromHours(1)));
        var lateVm = lateScope.ViewModel;
        var recordGate = new StateOperationGate(ignoreCancellation: true);
        lateStore.GateNextRecentWrite(recordGate);
        var lateRelink = lateVm.RelinkRecentWorkspaceAsync(lateOldPath, lateReplacementPath);
        await recordGate.WaitUntilStartedAsync();
        var refreshGate = new StateOperationGate(ignoreCancellation: true);
        lateStore.GateNextRecentRead(refreshGate);
        var newerRefresh = lateVm.RefreshWorkspaceStateAsync();
        recordGate.Release();
        try
        {
            await refreshGate.WaitUntilStartedAsync();
            Qa.Equal(WorkspaceActionRequestResult.Completed, await lateRelink,
                "최근 기록이 늦게 WriteBlocked여도 문서 열기가 실패");
            await recordGate.WaitUntilFinishedAsync();
            Qa.Equal(string.Empty, lateVm.WorkspaceStateError,
                "새 refresh가 진행 중인데 오래된 recent 기록 진단이 최신 상태에 반영됨");
            Qa.Check(lateVm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, lateOldPath)),
                "새 refresh가 진행 중인 동안 늦은 WriteBlocked 결과가 목록을 덮음");
        }
        finally
        {
            refreshGate.Release();
        }
        await newerRefresh.WaitAsync(TimeSpan.FromSeconds(10));
        await refreshGate.WaitUntilFinishedAsync();
        Qa.Equal(string.Empty, lateVm.WorkspaceStateError,
            "최신 recent refresh 뒤 오래된 진단이 복원됨");
        Qa.Check(lateVm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, lateOldPath)) &&
                 !lateVm.RecentWorkspaces.Any(entry => PathEquals(entry.FullPath, lateReplacementPath)),
            "최신 recent snapshot의 old/new 경로가 잘못 적용됨");
    }

    private static EquationWorkspace CreateWorkspace(string name)
    {
        var workspace = Qa.Workspace(Qa.Text("x=1"), Qa.Text("한글 e\u0301🙂"));
        return workspace with
        {
            Name = name,
            Style = new MathStyle(37, "Cambria Math", "#123456")
        };
    }

    private static void MakeDirty(MainWindowViewModel vm, string value)
    {
        var source = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, source.Id, source.Text.Length);
        vm.Session.InsertText(value);
        Qa.Check(vm.Session.InsertFraction().HasValue, "dirty fixture 분수 삽입");
        var slot = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().Last();
        Qa.Caret(vm.Session, slot.Id, 0);
        vm.Session.InsertText("λₙ🙂");
    }

    private static async Task MakeCurrentDirtyAsync(MainWindowViewModel vm, string savePath)
    {
        await vm.SaveToPathAsync(savePath);
        var source = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().First();
        Qa.Caret(vm.Session, source.Id, source.Text.Length);
        vm.Session.InsertText(" 현재 작업의 미저장 변경");
    }

    private static CapturedVmState Capture(MainWindowViewModel vm)
    {
        var snapshot = vm.Session.CaptureSnapshot();
        return new CapturedVmState(vm.Session, Qa.Fingerprint(snapshot.Workspace), snapshot,
            vm.Session.Caret, vm.Session.Selection, vm.Session.SelectedNodeId, vm.Session.IsNodeSelected,
            vm.Session.IsDirty, vm.Session.CanUndo, vm.Session.CanRedo, vm.CurrentPath, vm.StatusText);
    }

    private static void AssertUnchanged(CapturedVmState expected, CapturedVmState actual, string label)
    {
        Qa.Check(ReferenceEquals(expected.Session, actual.Session), label + ": Session identity");
        Qa.Equal(expected.WorkspaceFingerprint, actual.WorkspaceFingerprint, label + ": workspace");
        Qa.Equal(expected.Snapshot.Revision, actual.Snapshot.Revision, label + ": revision");
        Qa.Equal(expected.Snapshot.StateId, actual.Snapshot.StateId, label + ": state ID");
        Qa.Equal(expected.Caret, actual.Caret, label + ": caret");
        Qa.Equal(expected.Selection, actual.Selection, label + ": selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + ": selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + ": whole-node selection");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + ": dirty");
        Qa.Equal(expected.CanUndo, actual.CanUndo, label + ": undo");
        Qa.Equal(expected.CanRedo, actual.CanRedo, label + ": redo");
        Qa.Equal(expected.CurrentPath, actual.CurrentPath, label + ": path");
        Qa.Equal(expected.StatusText, actual.StatusText, label + ": status");
    }

    private static string RecoveryPath(WorkspaceStateService service, Guid id) =>
        Path.Combine(service.RootDirectory, WorkspaceStateService.RecoveryDirectoryName, $"{id:D}.json");

    private static bool PathEquals(string left, string right) =>
        StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private sealed record CapturedVmState(
        EditingSession Session,
        string WorkspaceFingerprint,
        WorkspaceSnapshot Snapshot,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        bool IsDirty,
        bool CanUndo,
        bool CanRedo,
        string? CurrentPath,
        string StatusText);

    private sealed class StateVmScope(MainWindowViewModel viewModel) : IAsyncDisposable
    {
        public MainWindowViewModel ViewModel { get; } = viewModel;
        public static async Task<StateVmScope> CreateAsync(MainWindowViewModel viewModel)
        {
            await viewModel.InitializeWorkspaceStateAsync();
            return new StateVmScope(viewModel);
        }
        public async ValueTask DisposeAsync() => await ViewModel.StopRecoveryAsync();
    }

    private sealed class FailingWorkspaceFileService : IWorkspaceFileService
    {
        private readonly WorkspaceFileService _inner = new();
        public bool AllowWrites { get; set; }
        public Task<EquationWorkspace> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(path, cancellationToken);
        public Task SaveAsync(string path, WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default) =>
            AllowWrites ? _inner.SaveAsync(path, snapshot, cancellationToken)
                : Task.FromException(new IOException("QA가 만든 저장 실패"));
    }

    private sealed class ControlledWorkspaceStateService(IWorkspaceStateService inner) : IWorkspaceStateService
    {
        private readonly ConcurrentQueue<StateOperationGate> _recoveryGates = new();
        private readonly ConcurrentQueue<StateOperationGate> _recentReadGates = new();
        private readonly ConcurrentQueue<StateOperationGate> _recentWriteGates = new();
        private readonly ConcurrentDictionary<string, byte> _blockedRecentRecords = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _removeRecentCalls = new(StringComparer.OrdinalIgnoreCase);
        private int _recoveryWriteCalls;
        private int _completedRecoveryWriteCalls;
        public int RecoveryWriteCalls => Volatile.Read(ref _recoveryWriteCalls);
        public int CompletedRecoveryWriteCalls => Volatile.Read(ref _completedRecoveryWriteCalls);
        public string RootDirectory => inner.RootDirectory;
        public void GateNextRecoveryWrite(StateOperationGate gate) => _recoveryGates.Enqueue(gate);
        public void GateNextRecentRead(StateOperationGate gate) => _recentReadGates.Enqueue(gate);
        public void GateNextRecentWrite(StateOperationGate gate) => _recentWriteGates.Enqueue(gate);
        public void BlockSuccessfulUseFor(string path) => _blockedRecentRecords[Path.GetFullPath(path)] = 0;
        public int RemoveRecentCallCount(string path) =>
            _removeRecentCalls.TryGetValue(Path.GetFullPath(path), out var count) ? count : 0;
        public async Task<RecentWorkspaceIndexResult> ReadRecentWorkspacesAsync(
            CancellationToken cancellationToken = default)
        {
            _recentReadGates.TryDequeue(out var gate);
            try
            {
                if (gate is not null)
                    await gate.WaitAsync(cancellationToken);
                return await inner.ReadRecentWorkspacesAsync(cancellationToken);
            }
            finally
            {
                gate?.Finish();
            }
        }
        public async Task<RecentWorkspaceMutationResult> RecordSuccessfulUseAsync(string path, string displayName,
            DateTimeOffset usedAtUtc, CancellationToken cancellationToken = default)
        {
            _recentWriteGates.TryDequeue(out var gate);
            try
            {
                if (gate is not null)
                    await gate.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (_blockedRecentRecords.ContainsKey(Path.GetFullPath(path)))
                {
                    var snapshot = await inner.ReadRecentWorkspacesAsync(cancellationToken);
                    return new RecentWorkspaceMutationResult(RecentWorkspaceMutationStatus.WriteBlocked, snapshot,
                        new PersistenceDiagnostic("QARecentWriteBlocked", "QA에서 replacement 기록을 차단했습니다."));
                }
                return await inner.RecordSuccessfulUseAsync(path, displayName, usedAtUtc, cancellationToken);
            }
            finally
            {
                gate?.Finish();
            }
        }
        public Task<RecentWorkspaceMutationResult> SetPinnedAsync(string path, bool isPinned,
            CancellationToken cancellationToken = default) => inner.SetPinnedAsync(path, isPinned, cancellationToken);
        public Task<RecentWorkspaceMutationResult> RemoveRecentAsync(string path,
            CancellationToken cancellationToken = default)
        {
            _removeRecentCalls.AddOrUpdate(Path.GetFullPath(path), 1, static (_, count) => count + 1);
            return inner.RemoveRecentAsync(path, cancellationToken);
        }
        public async Task<RecoveryWriteResult> SaveRecoveryAsync(WorkspaceRecovery recovery,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _recoveryWriteCalls);
            _recoveryGates.TryDequeue(out var gate);
            try
            {
                if (gate is not null)
                    await gate.WaitAsync(gate.IgnoreCancellation ? CancellationToken.None : cancellationToken);
                var writeToken = gate?.IgnoreCancellation == true ? CancellationToken.None : cancellationToken;
                var result = await inner.SaveRecoveryAsync(recovery, writeToken);
                Interlocked.Increment(ref _completedRecoveryWriteCalls);
                return result;
            }
            finally
            {
                gate?.Finish();
            }
        }
        public Task<RecoveryListResult> ListRecoveriesAsync(CancellationToken cancellationToken = default) =>
            inner.ListRecoveriesAsync(cancellationToken);
        public Task<RecoveryReadResult> ReadRecoveryAsync(Guid recoveryId, CancellationToken cancellationToken = default) =>
            inner.ReadRecoveryAsync(recoveryId, cancellationToken);
        public Task<bool> DeleteRecoveryAsync(Guid recoveryId, CancellationToken cancellationToken = default) =>
            inner.DeleteRecoveryAsync(recoveryId, cancellationToken);
    }

    private sealed class StateOperationGate(bool ignoreCancellation = false)
    {
        public bool IgnoreCancellation { get; } = ignoreCancellation;
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _wasCanceled;
        public bool WasCanceled => Volatile.Read(ref _wasCanceled) != 0;
        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            try
            {
                if (IgnoreCancellation)
                    await _released.Task;
                else
                    await _released.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _wasCanceled, 1);
                throw;
            }
        }
        public Task WaitUntilStartedAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public Task WaitUntilFinishedAsync() => _finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Release() => _released.TrySetResult();
        public void Finish() => _finished.TrySetResult();
    }
}
