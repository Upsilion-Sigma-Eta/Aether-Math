using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class LibraryUiInteractionCases
{
    private const string CaseId = "LIBRARY-UI";
    private const string RecoveredWorkspaceName = "복구할 명시 작업";
    private static MainWindow? _window;
    private static MainWindowViewModel? _viewModel;
    private static TempDirectory? _tempDirectory;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "실제 App의 최근 작업·복구본 작업 탭, 명시 동작과 두 크기 셸을 Headless로 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId != CaseId)
        {
            Console.Error.WriteLine("알 수 없는 최근 작업 UI headless 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("최근 작업 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
            return 2;
        }

        var exitCode = 0;
        try
        {
            _tempDirectory = new TempDirectory();
            await RunWindowChecksAsync(_tempDirectory);
            Console.WriteLine("PASS " + CaseId);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {CaseId}\n{exception}");
            exitCode = 1;
        }

        try
        {
            await CleanupOwnedWindowAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Headless 최근 작업 창 정리 실패: " + exception);
            exitCode = 1;
        }

        try
        {
            _tempDirectory?.Dispose();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("격리 TempDirectory 정리 실패: " + exception);
            exitCode = 1;
        }
        finally
        {
            _tempDirectory = null;
            _viewModel = null;
            _window = null;
        }

        return exitCode;
    }

    private static async Task RunHeadlessProcessAsync()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("현재 프로세스 실행 경로가 없습니다.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("dotnet host의 QA 어셈블리 경로를 찾을 수 없습니다.");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        startInfo.ArgumentList.Add("--library-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("최근 작업 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("최근 작업 UI headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"최근 작업 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("최근 작업 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync(TempDirectory temporary)
    {
        var stateRoot = Path.Combine(temporary.Path, "isolated-state");
        var stateService = new WorkspaceStateService(stateRoot);
        var fileService = new WorkspaceFileService();
        var targetPath = temporary.File("recent-target.aethermath");
        var removablePath = temporary.File("recent-removable.aethermath");
        var recoveredOriginalPath = temporary.File("recovery-original.aethermath");
        var targetWorkspace = MakeWorkspace("최근 대상", "target = β");
        var removableWorkspace = MakeWorkspace("목록 제거 대상", "remove = 1");
        var originalWorkspace = MakeWorkspace("복구 원본 보존", "original = 4");
        await fileService.SaveAsync(targetPath, new EditingSession(targetWorkspace).CaptureSnapshot());
        await fileService.SaveAsync(removablePath, new EditingSession(removableWorkspace).CaptureSnapshot());
        await fileService.SaveAsync(recoveredOriginalPath, new EditingSession(originalWorkspace).CaptureSnapshot());
        var originalFileBytes = await File.ReadAllBytesAsync(recoveredOriginalPath);

        var extension = WorkspaceFileService.Extension;
        var stemLength = Math.Clamp(210 - temporary.Path.Length - extension.Length - 1, 110, 165);
        var longDisplayName = new string('긴', 160);
        var missingPath = temporary.File(new string('m', stemLength) + extension);
        Qa.Check(missingPath.Length < 260,
            $"긴 경로 UI 사례가 Windows 경로 한도를 넘었습니다: {missingPath.Length}자.");
        var usedAt = DateTimeOffset.UtcNow;
        await stateService.RecordSuccessfulUseAsync(targetPath, "최근 대상", usedAt.AddMinutes(-2));
        await stateService.RecordSuccessfulUseAsync(removablePath, "목록 제거 대상", usedAt.AddMinutes(-1));
        await stateService.RecordSuccessfulUseAsync(missingPath, longDisplayName, usedAt);

        var recoveryId = Guid.NewGuid();
        var recoverySession = new EditingSession(MakeWorkspace(RecoveredWorkspaceName, "recovered = ∑x"));
        Qa.Check(recoverySession.AddRowAfterCurrent(), "복구 fixture에 dirty revision을 만들지 못했습니다.");
        var recovery = new WorkspaceRecovery(recoveryId, recoverySession.CaptureSnapshot(), usedAt, recoveredOriginalPath);
        var write = await stateService.SaveRecoveryAsync(recovery);
        Qa.Equal(RecoveryWriteStatus.Written, write.Status, "headless 복구 fixture 저장");

        var corruptRecoveryId = Guid.NewGuid();
        var corruptRecoveryPath = Path.Combine(stateRoot, WorkspaceStateService.RecoveryDirectoryName,
            $"{corruptRecoveryId:D}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptRecoveryPath)!);
        await File.WriteAllTextAsync(corruptRecoveryPath, "{ not valid recovery json }");

        var viewModel = new MainWindowViewModel(fileService, stateService, TimeSpan.FromHours(1));
        _viewModel = viewModel;
        var window = new MainWindow(viewModel)
        {
            Width = 1060,
            Height = 700,
            CanResize = true
        };
        _window = window;
        EnsureStubHandle(window, "Show 전");
        window.Show();
        EnsureStubHandle(window, "Show 후");
        await WaitUntilAsync(() => viewModel.IsWorkspaceStateReady, window, "작업 상태 초기화");
        await WaitUntilAsync(() => !viewModel.IsWorkspaceStateBusy, window, "작업 상태 초기화 큐 drain");
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "초기화 후");

        var libraryTab = Find<TabItem>(window, "WorkspaceLibraryTab");
        libraryTab.IsSelected = true;
        var libraryScroll = Find<ScrollViewer>(window, "WorkspaceLibraryScrollViewer");
        libraryScroll.Offset = new Vector(0, 0);
        await DrainLayoutAsync(window);
        AssertCommandBindings(window, viewModel);

        Qa.Equal(3, viewModel.RecentWorkspaces.Length, "초기 최근 작업 수");
        Qa.Check(viewModel.RecentWorkspaceCanWrite, "임시 상태 저장소가 쓰기 가능으로 초기화되지 않음");
        Qa.Check(viewModel.RecentWorkspaceDiagnostic is null, "유효한 최근 목록이 진단 오류를 가짐");
        var missingEntry = viewModel.RecentWorkspaces.Single(entry => entry.FullPath == missingPath);
        Qa.Check(missingEntry.IsMissing && missingEntry.FileStatus == RecentWorkspaceFileStatus.Missing,
            "누락 파일이 최근 작업 목록에서 missing 상태로 표시되지 않음");
        Qa.Check(viewModel.RecentWorkspaces.Single(entry => entry.FullPath == targetPath).FileModifiedAtUtc is not null,
            "유효 파일의 수정 시각이 작업 목록에서 빠짐");
        Qa.Equal(1, viewModel.RecoveryDiagnostics.Length, "손상 복구본 진단 수");
        Qa.Equal(corruptRecoveryPath, viewModel.RecoveryDiagnostics[0].Path,
            "손상 복구본 진단 경로");
        Qa.Check(viewModel.RecoveryCandidates.Any(candidate => candidate.RecoveryId == recoveryId),
            "유효 복구본이 손상 항목과 함께 목록에 보존되지 않음");

        var recentList = Find<ListBox>(window, "RecentWorkspacesList");
        var startingSession = viewModel.Session;
        var startingFingerprint = Qa.Fingerprint(startingSession.Workspace);
        var startingCaret = startingSession.Caret;
        var startingSelection = startingSession.Selection;
        var startingSelectedNode = startingSession.SelectedNodeId;
        var startingPath = viewModel.CurrentPath;
        recentList.SelectedItem = missingEntry;
        await DrainLayoutAsync(window);
        Qa.Check(ReferenceEquals(startingSession, viewModel.Session), "최근 작업 선택이 복사본 Session을 만들었습니다.");
        Qa.Equal(startingFingerprint, Qa.Fingerprint(viewModel.Session.Workspace), "최근 작업 선택 뒤 수식 변경");
        Qa.Equal(startingCaret, viewModel.Session.Caret, "최근 작업 선택 뒤 caret 유지");
        Qa.Equal(startingSelection, viewModel.Session.Selection, "최근 작업 선택 뒤 selection 유지");
        Qa.Equal(startingSelectedNode, viewModel.Session.SelectedNodeId, "최근 작업 선택 뒤 구조 선택 유지");
        Qa.Equal(startingPath, viewModel.CurrentPath, "최근 작업 선택 뒤 현재 경로 유지");
        Qa.Check(viewModel.SelectedRecentWorkspacePreview.Contains(missingPath, StringComparison.Ordinal),
            "최근 작업 미리보기에 전체 경로가 없습니다.");
        Qa.Check(viewModel.SelectedRecentWorkspacePreview.Contains("찾을 수 없음", StringComparison.Ordinal),
            "최근 작업 미리보기에 누락 상태가 없습니다.");
        Qa.Check(Find<Border>(window, "RecentWorkspacePreview").IsVisible,
            "목록 선택 뒤 preview pane이 보이지 않습니다.");
        var longPathText = recentList.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.Text == missingPath);
        Qa.Check(longPathText is not null && longPathText.TextWrapping == Avalonia.Media.TextWrapping.Wrap,
            "긴 파일 경로가 실제 목록 템플릿에서 줄바꿈되지 않습니다.");
        Qa.Check(longPathText!.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis,
            "긴 파일 경로가 두 줄 ellipsis로 제한되지 않습니다.");
        var previewNameText = Find<TextBlock>(window, "SelectedRecentWorkspaceNameText");
        Qa.Equal(longDisplayName, previewNameText.Text ?? string.Empty, "선택한 긴 작업명 표시");
        Qa.Check(previewNameText.MaxLines == 1 &&
                 previewNameText.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis,
            "긴 작업명이 미리보기에서 한 줄 ellipsis로 제한되지 않습니다.");
        var previewPathText = Find<TextBlock>(window, "SelectedRecentWorkspacePathText");
        Qa.Equal(missingPath, previewPathText.Text ?? string.Empty, "선택한 최근 작업 전체 경로 표시");
        Qa.Check(previewPathText.TextWrapping == Avalonia.Media.TextWrapping.Wrap &&
                 previewPathText.MaxLines is >= 2 and <= 3 &&
                 previewPathText.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis,
            "선택한 최근 작업 경로가 제한된 여러 줄로 표시되지 않습니다.");
        var previewStatusText = Find<TextBlock>(window, "SelectedRecentWorkspaceStatusText");
        Qa.Check(previewStatusText.Text?.Contains("찾을 수 없음", StringComparison.Ordinal) == true,
            "선택한 최근 작업의 누락 상태가 별도 줄에 표시되지 않습니다.");
        Qa.Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "SelectedRecentWorkspaceModifiedText").Text),
            "선택한 최근 작업의 수정 시각 안내가 비어 있습니다.");

        RowShellVisualCases.CaptureAndVerify(window, "library-initial", "p1-recovery-recent");
        var previewBorder = Find<Border>(window, "RecentWorkspacePreview");
        await BringIntoViewAsync(window, libraryScroll, previewBorder);
        var previewViewportBounds = BoundsInWindow(libraryScroll, window);
        var previewBounds = BoundsInWindow(previewBorder, window);
        Qa.Check(Contains(previewViewportBounds, previewBounds),
            "긴 경로 미리보기를 전용 캡처 viewport 안에 모두 표시하지 못했습니다.");
        var previewNameBounds = BoundsInWindow(previewNameText, window);
        var previewPathBounds = BoundsInWindow(previewPathText, window);
        var previewStatusBounds = BoundsInWindow(previewStatusText, window);
        var previewModifiedBounds = BoundsInWindow(
            Find<TextBlock>(window, "SelectedRecentWorkspaceModifiedText"), window);
        foreach (var metadataBounds in new[]
                 { previewNameBounds, previewPathBounds, previewStatusBounds, previewModifiedBounds })
            Qa.Check(Contains(previewBounds, metadataBounds) && Contains(previewViewportBounds, metadataBounds),
                $"긴 이름이 미리보기 metadata를 viewport 밖으로 밀었습니다: {metadataBounds}.");
        Qa.Check(previewNameBounds.Top < previewPathBounds.Top &&
                 previewPathBounds.Top < previewStatusBounds.Top &&
                 previewStatusBounds.Top < previewModifiedBounds.Top,
            "최근 작업 이름·경로·상태·수정 시각 순서가 예상과 다릅니다.");
        RowShellVisualCases.CaptureAndVerify(window, "library-preview", "p1-recovery-recent");
        libraryScroll.Offset = new Vector(0, 0);
        await DrainLayoutAsync(window);
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1360×880 재배치 후");
        RowShellVisualCases.CaptureAndVerify(window, "library-default", "p1-recovery-recent");

        await BringIntoViewAsync(window, libraryScroll, Find<WrapPanel>(window, "RecentWorkspaceActions"));
        AssertActionButtonsFullyVisible(window, libraryScroll,
            "OpenRecentWorkspaceButton", "PinRecentWorkspaceButton", "UnpinRecentWorkspaceButton",
            "RelinkRecentWorkspaceButton", "RemoveRecentWorkspaceButton");
        await ClickAsync(window, Find<Button>(window, "PinRecentWorkspaceButton"));
        await WaitUntilAsync(() => !viewModel.IsWorkspaceStateBusy &&
                                   viewModel.RecentWorkspaces.Single(entry => entry.FullPath == missingPath).IsPinned,
            window, "최근 작업 고정");
        Qa.Check(File.Exists(missingPath) is false, "고정 동작이 누락 원본 파일을 생성했습니다.");

        await ClickAsync(window, Find<Button>(window, "UnpinRecentWorkspaceButton"));
        await WaitUntilAsync(() => !viewModel.IsWorkspaceStateBusy &&
                                   !viewModel.RecentWorkspaces.Single(entry => entry.FullPath == missingPath).IsPinned,
            window, "최근 작업 고정 해제");

        var relinkResult = await viewModel.RelinkRecentWorkspaceAsync(missingPath, targetPath);
        Qa.Equal(WorkspaceActionRequestResult.Completed, relinkResult,
            "picker-free 누락 작업 다시 찾기 결과");
        Qa.Check(viewModel.RecentWorkspaces.All(entry => entry.FullPath != missingPath),
            "다시 찾기 성공 뒤 누락 경로가 최근 목록에 남았습니다.");
        Qa.Check(File.Exists(targetPath), "다시 찾기가 원본 경로를 덮어쓰거나 삭제했습니다.");

        var removableEntry = viewModel.RecentWorkspaces.Single(entry => entry.FullPath == removablePath);
        recentList.SelectedItem = removableEntry;
        await DrainLayoutAsync(window);
        await BringIntoViewAsync(window, libraryScroll, Find<WrapPanel>(window, "RecentWorkspaceActions"));
        AssertActionButtonsFullyVisible(window, libraryScroll,
            "OpenRecentWorkspaceButton", "PinRecentWorkspaceButton", "UnpinRecentWorkspaceButton",
            "RelinkRecentWorkspaceButton", "RemoveRecentWorkspaceButton");
        await ClickAsync(window, Find<Button>(window, "RemoveRecentWorkspaceButton"));
        await WaitUntilAsync(() => !viewModel.IsWorkspaceStateBusy &&
                                   viewModel.RecentWorkspaces.All(entry => entry.FullPath != removablePath),
            window, "최근 목록 항목 제거");
        Qa.Check(File.Exists(removablePath), "최근 목록 제거가 원본 수식 파일을 지웠습니다.");

        var targetEntry = viewModel.RecentWorkspaces.Single(entry => entry.FullPath == targetPath);
        var currentSession = viewModel.Session;
        Qa.Check(currentSession.AddRowAfterCurrent(), "Open Cancel을 위해 현재 Session을 dirty로 만들지 못했습니다.");
        var dirtyFingerprint = Qa.Fingerprint(currentSession.Workspace);
        var dirtyCaret = currentSession.Caret;
        var dirtySelection = currentSession.Selection;
        recentList.SelectedItem = targetEntry;
        await DrainLayoutAsync(window);
        Qa.Check(ReferenceEquals(currentSession, viewModel.Session), "최근 항목 선택이 dirty Session을 교체했습니다.");
        Qa.Equal(dirtyFingerprint, Qa.Fingerprint(currentSession.Workspace), "최근 미리보기 선택 뒤 dirty 수식 유지");
        Qa.Equal(dirtyCaret, currentSession.Caret, "최근 미리보기 선택 뒤 dirty caret 유지");
        Qa.Equal(dirtySelection, currentSession.Selection, "최근 미리보기 선택 뒤 dirty selection 유지");
        await BringIntoViewAsync(window, libraryScroll, Find<WrapPanel>(window, "RecentWorkspaceActions"));
        await ClickAsync(window, Find<Button>(window, "OpenRecentWorkspaceButton"));
        await WaitUntilAsync(() => viewModel.IsPendingWorkspaceActionVisible, window, "최근 작업 열기 확인");
        Qa.Equal(WorkspaceActionKind.OpenWorkspace, viewModel.PendingWorkspaceAction,
            "최근 작업 열기의 dirty guard 종류");
        RowShellVisualCases.CaptureAndVerify(window, "recent-open-guard", "p1-recovery-recent");
        await ClickAsync(window, Find<Button>(window, "CancelPendingWorkspaceActionButton"));
        await WaitUntilAsync(() => !viewModel.IsPendingWorkspaceActionVisible, window, "최근 작업 열기 취소");
        Qa.Check(ReferenceEquals(currentSession, viewModel.Session), "열기 취소가 원 Session을 교체했습니다.");
        Qa.Equal(dirtyFingerprint, Qa.Fingerprint(viewModel.Session.Workspace), "열기 취소 뒤 수식 보존");
        Qa.Equal(dirtyCaret, viewModel.Session.Caret, "열기 취소 뒤 caret 보존");
        Qa.Equal(dirtySelection, viewModel.Session.Selection, "열기 취소 뒤 selection 보존");

        var recoveryList = Find<ListBox>(window, "RecoveryCandidatesList");
        var recoverySummary = viewModel.RecoveryCandidates.Single(candidate => candidate.RecoveryId == recoveryId);
        recoveryList.SelectedItem = recoverySummary;
        await DrainLayoutAsync(window);
        var separateSaveNotice = Find<TextBlock>(window, "RecoverySeparateSaveNoticeText");
        var separateSaveNoticeText = separateSaveNotice.Text ?? string.Empty;
        Qa.Check(separateSaveNoticeText.Contains("원본과 별개", StringComparison.Ordinal) &&
                 separateSaveNoticeText.Contains("새 경로", StringComparison.Ordinal),
            "복구본을 원본과 별개로 열고 새 경로에 저장한다는 안내가 없습니다.");
        await BringIntoViewAsync(window, libraryScroll, Find<WrapPanel>(window, "RecoveryActions"));
        AssertActionButtonsFullyVisible(window, libraryScroll, "RestoreRecoveryButton", "DeleteRecoveryButton");
        await ClickAsync(window, Find<Button>(window, "RestoreRecoveryButton"));
        await WaitUntilAsync(() => viewModel.IsPendingWorkspaceActionVisible, window, "복구본 복원 저장 확인");
        Qa.Equal(WorkspaceActionKind.RestoreRecovery, viewModel.PendingWorkspaceAction,
            "dirty 복구 요청의 guard 종류");
        await ClickAsync(window, Find<Button>(window, "DiscardPendingWorkspaceActionButton"));
        await WaitUntilAsync(() => !viewModel.IsPendingWorkspaceActionVisible &&
                                   viewModel.WorkspaceName == RecoveredWorkspaceName, window,
            "복구본 discard 이후 복원");
        Qa.Equal<string?>(null, viewModel.CurrentPath, "복구본 복원 경로 독립성");
        Qa.Check(viewModel.Session.IsDirty && viewModel.IsRecoverySaveRequired,
            "복원한 작업이 새 경로 저장 필요 상태로 열리지 않았습니다.");
        Qa.Equal(RecoveredWorkspaceName, viewModel.Session.Workspace.Name, "명시 선택한 복구 작업 이름");
        var originalFileBytesAfterRestore = await File.ReadAllBytesAsync(recoveredOriginalPath);
        Qa.Check(originalFileBytes.AsSpan().SequenceEqual(originalFileBytesAfterRestore),
            "복구본 복원 중 원본 파일이 변경되었습니다.");
        await BringIntoViewAsync(window, libraryScroll, Find<WrapPanel>(window, "RecoveryActions"));
        RowShellVisualCases.CaptureAndVerify(window, "recovery-restored", "p1-recovery-recent");

        recoveryList.SelectedItem = viewModel.RecoveryCandidates.Single(candidate => candidate.RecoveryId == recoveryId);
        await DrainLayoutAsync(window);
        await BringIntoViewAsync(window, libraryScroll, Find<WrapPanel>(window, "RecoveryActions"));
        await ClickAsync(window, Find<Button>(window, "DeleteRecoveryButton"));
        var confirmation = Find<Border>(window, "RecoveryDeleteConfirmation");
        await WaitUntilAsync(() => confirmation.IsVisible, window, "복구본 삭제 확인 대화상자");
        Qa.Equal<Guid?>(recoveryId, viewModel.PendingRecoveryDeletionId, "삭제 확인 캡처 복구 ID");
        Qa.Equal(RecoveredWorkspaceName, viewModel.PendingRecoveryDeletionName, "삭제 확인 캡처 작업명");
        recoveryList.SelectedItem = null;
        await DrainLayoutAsync(window);
        Qa.Equal<Guid?>(recoveryId, viewModel.PendingRecoveryDeletionId,
            "선택 변경이 삭제 확인의 대상 ID를 바꿨습니다.");
        Qa.Equal(RecoveredWorkspaceName, viewModel.PendingRecoveryDeletionName,
            "선택 변경이 삭제 확인의 대상 이름을 바꿨습니다.");
        await BringIntoViewAsync(window, libraryScroll, confirmation);
        AssertActionButtonsFullyVisible(window, libraryScroll,
            "ConfirmDeleteRecoveryButton", "CancelDeleteRecoveryButton");
        RowShellVisualCases.CaptureAndVerify(window, "recovery-delete-confirmation", "p1-recovery-recent");
        await ClickAsync(window, Find<Button>(window, "CancelDeleteRecoveryButton"));
        await WaitUntilAsync(() => !confirmation.IsVisible, window, "복구본 삭제 취소");
        Qa.Check(viewModel.RecoveryCandidates.Any(candidate => candidate.RecoveryId == recoveryId),
            "삭제 확인 취소가 복구본을 보존하지 않았습니다.");
        Qa.Check(File.Exists(Path.Combine(stateRoot, WorkspaceStateService.RecoveryDirectoryName,
                $"{recoveryId:D}.json")), "삭제 취소 뒤 복구 파일이 보존되지 않았습니다.");

        viewModel.SelectedRecovery = viewModel.RecoveryCandidates.Single(candidate => candidate.RecoveryId == recoveryId);
        await DrainLayoutAsync(window);
        await ClickAsync(window, Find<Button>(window, "DeleteRecoveryButton"));
        await WaitUntilAsync(() => confirmation.IsVisible, window, "복구본 삭제 재확인");
        await BringIntoViewAsync(window, libraryScroll, confirmation);
        await ClickAsync(window, Find<Button>(window, "ConfirmDeleteRecoveryButton"));
        await WaitUntilAsync(() => !viewModel.IsWorkspaceStateBusy &&
                                   !confirmation.IsVisible &&
                                   viewModel.RecoveryCandidates.All(candidate => candidate.RecoveryId != recoveryId),
            window, "복구본 삭제 확인");
        Qa.Check(!File.Exists(Path.Combine(stateRoot, WorkspaceStateService.RecoveryDirectoryName,
                $"{recoveryId:D}.json")), "복구본 삭제 확인 뒤 복구 파일이 남았습니다.");
        Qa.Check(File.Exists(corruptRecoveryPath), "손상 진단 항목을 별도 제거 확인 없이 자동 삭제했습니다.");
        Qa.Check(viewModel.RecoveryDiagnostics.Any(item => item.Path == corruptRecoveryPath),
            "손상 복구본 원본 보존 후 진단이 유지되지 않았습니다.");
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "recovery-diagnostic-preserved", "p1-recovery-recent");

        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "복구 흐름 뒤 최소 크기 재배치");
        var minimumViewport = Find<ScrollViewer>(window, "WorkspaceLibraryScrollViewer");
        minimumViewport.Offset = new Vector(0, 0);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "library-final-minimum", "p1-recovery-recent");
    }

    private static void AssertCommandBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        var bindings = new (string Name, ICommand Command)[]
        {
            ("RefreshWorkspaceStateButton", viewModel.RefreshWorkspaceStateCommand),
            ("OpenRecentWorkspaceButton", viewModel.OpenRecentWorkspaceCommand),
            ("PinRecentWorkspaceButton", viewModel.PinRecentWorkspaceCommand),
            ("UnpinRecentWorkspaceButton", viewModel.UnpinRecentWorkspaceCommand),
            ("RelinkRecentWorkspaceButton", viewModel.RelinkRecentWorkspaceCommand),
            ("RemoveRecentWorkspaceButton", viewModel.RemoveRecentWorkspaceCommand),
            ("RestoreRecoveryButton", viewModel.RestoreRecoveryCommand),
            ("DeleteRecoveryButton", viewModel.DeleteRecoveryCommand),
            ("ConfirmDeleteRecoveryButton", viewModel.ConfirmDeleteRecoveryCommand),
            ("CancelDeleteRecoveryButton", viewModel.CancelDeleteRecoveryCommand)
        };
        foreach (var (name, command) in bindings)
        {
            var button = Find<Button>(window, name);
            Qa.Check(ReferenceEquals(button.Command, command), $"{name}가 ViewModel 명령에 연결되지 않았습니다.");
            Qa.Equal(name, AutomationProperties.GetAutomationId(button) ?? string.Empty,
                $"{name} AutomationId");
        }

        var recoveryNotice = Find<TextBlock>(window, "RecoverySeparateSaveNoticeText");
        Qa.Check(recoveryNotice.IsVisible,
            "복구본을 여는 동작 근처에 별도 저장 안내가 항상 표시되지 않습니다.");
    }

    private static async Task BringIntoViewAsync(MainWindow window, ScrollViewer scrollViewer, Control control)
    {
        await DrainLayoutAsync(window);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var origin = control.TranslatePoint(new Point(0, 0), scrollViewer);
            Qa.Check(origin is Point, "작업 도구의 ScrollViewer 기준 좌표를 계산하지 못했습니다.");
            var top = origin!.Value.Y;
            var bottom = top + control.Bounds.Height;
            var nextOffset = scrollViewer.Offset.Y;
            if (top < 0)
                nextOffset += top;
            else if (bottom > scrollViewer.Viewport.Height)
                nextOffset += bottom - scrollViewer.Viewport.Height;
            else
                return;

            var maximumOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Clamp(nextOffset, 0, maximumOffset));
            await DrainLayoutAsync(window);
        }
    }

    private static void AssertActionButtonsFullyVisible(MainWindow window, ScrollViewer scrollViewer,
        params string[] names)
    {
        var viewport = BoundsInWindow(scrollViewer, window);
        var bounds = names.Select(name => BoundsInWindow(Find<Button>(window, name), window)).ToArray();
        for (var index = 0; index < names.Length; index++)
        {
            Qa.Check(Contains(viewport, bounds[index]),
                $"{names[index]}가 BringIntoView 후 viewport 전체 안에 없습니다: {bounds[index]} / {viewport}.");
            for (var other = index + 1; other < names.Length; other++)
                Qa.Check(!Overlaps(bounds[index], bounds[other]),
                    $"최근 작업 도구 버튼끼리 겹칩니다: {names[index]} / {names[other]}.");
        }
    }

    private static async Task ClickAsync(MainWindow window, Button button)
    {
        Qa.Check(button.IsEffectivelyEnabled, "비활성 작업 탭 버튼을 합성 클릭하려 했습니다.");
        var origin = button.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "작업 버튼의 창 기준 좌표를 얻지 못했습니다.");
        var point = origin!.Value + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, MainWindow window, string label)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return;
            await DrainLayoutAsync(window);
            await Task.Delay(10);
        }
        throw new TimeoutException($"{label} 상태가 제한 시간 안에 도달하지 않았습니다.");
    }

    private static async Task DrainLayoutAsync(MainWindow window)
    {
        for (var iteration = 0; iteration < 4; iteration++)
        {
            Dispatcher.UIThread.RunJobs();
            window.InvalidateMeasure();
            window.InvalidateArrange();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task CleanupOwnedWindowAsync()
    {
        var window = _window;
        var viewModel = _viewModel;
        if (viewModel is not null)
        {
            await viewModel.StopRecoveryAsync();
            if (viewModel.IsPendingWorkspaceActionVisible)
                viewModel.CancelPendingWorkspaceAction();
            if (window?.IsVisible == true)
            {
                var closeRequest = viewModel.RequestClose();
                if (closeRequest == WorkspaceActionRequestResult.ConfirmationRequired)
                    viewModel.DiscardPendingWorkspaceAction();
            }
        }

        if (window is not null && window.IsVisible)
            await WaitUntilAsync(() => !window.IsVisible, window, "headless 작업 창 닫기 및 recovery drain");
        if (window is not null)
        {
            if (viewModel is not null)
                await viewModel.StopRecoveryAsync();
            EnsureClosedHandleIsSafe(window);
        }
        Dispatcher.UIThread.RunJobs();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void EnsureStubHandle(Window window, string phase)
    {
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is not null && handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            $"{phase}: STUB/0 headless handle을 확인하지 못했습니다.");
    }

    private static void EnsureClosedHandleIsSafe(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "닫힌 소유 headless 창에 native platform handle이 남았습니다.");
    }

    private static Rect BoundsInWindow(Control control, Control window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "작업 UI 요소의 창 기준 좌표를 얻지 못했습니다.");
        return new Rect(origin!.Value, control.Bounds.Size);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static bool Overlaps(Rect first, Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right > left + 0.5 && bottom > top + 0.5;
    }

    private static EquationWorkspace MakeWorkspace(string name, string expression) =>
        Qa.Workspace(Qa.Text(expression)) with { Name = name };

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name} 컨트롤을 찾지 못했습니다.");
}
