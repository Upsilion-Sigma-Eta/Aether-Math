using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using AetherMath.Desktop;

namespace AetherMath.LogicQA;

internal static class LifecycleUiInteractionCases
{
    private const string CaseId = "LIFECYCLE-UI";
    private static MainWindow? _window;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "실제 App/MainWindow에서 dirty 작업 보호, 저장 오류 재시도, 취소·버리기 및 두 크기 확인 overlay를 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId != CaseId)
        {
            Console.Error.WriteLine("알 수 없는 수명 보호 UI headless 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("수명 보호 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
            return 2;
        }

        try
        {
            await RunWindowChecksAsync();
            Console.WriteLine("PASS " + CaseId);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {CaseId}\n{exception}");
            return 1;
        }
        finally
        {
            try
            {
                CloseOwnedWindow(_window);
            }
            finally
            {
                _window = null;
            }
        }
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
        startInfo.ArgumentList.Add("--lifecycle-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("수명 보호 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("수명 보호 UI headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"수명 보호 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("수명 보호 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        using var directory = new TempDirectory();
        var sourcePath = directory.File("current.aethermath");
        using var targetDirectory = new ShortTempDirectory();
        var targetFileName = new string('t', 149) + ".aethermath";
        Qa.Equal(160, targetFileName.Length, "긴 대상 파일 이름 길이");
        var targetPath = targetDirectory.File(targetFileName);
        var atomicTemporaryPath = Path.Combine(targetDirectory.Path,
            $".{targetFileName}.{Guid.Empty:N}.tmp");
        Qa.Check(Path.GetFullPath(targetPath).Length < 260 &&
                 Path.GetFullPath(atomicTemporaryPath).Length < 260,
            "160자 대상 파일 또는 atomic-save 임시 경로가 Windows MAX_PATH를 넘습니다.");

        var viewModel = new MainWindowViewModel();
        var initialSession = viewModel.Session;
        var initialWorkspaceId = initialSession.Workspace.Id;
        await viewModel.SaveToPathAsync(sourcePath);
        Qa.Check(File.Exists(sourcePath), "TempDirectory 원본 경로가 초기 저장되지 않았습니다.");
        Qa.Equal(Path.GetFullPath(sourcePath), viewModel.CurrentPath ?? string.Empty,
            "초기 명시 경로 저장 뒤 CurrentPath");

        var targetViewModel = new MainWindowViewModel();
        var targetWorkspaceId = targetViewModel.Session.Workspace.Id;
        await targetViewModel.SaveToPathAsync(targetPath);
        Qa.Check(File.Exists(targetPath), "열기 대상 fixture를 TempDirectory에 저장하지 못했습니다.");

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
        await DrainLayoutAsync(window);

        Qa.Check(ReferenceEquals(window.DataContext, viewModel), "MainWindow와 ViewModel DataContext가 다릅니다.");
        Qa.Check(ReferenceEquals(Find<MathEditorControl>(window, "MathEditorSurface").Session, viewModel.Session),
            "편집 컨트롤과 ViewModel Session이 다릅니다.");
        AssertHeaderBindings(window, viewModel);
        RowShellVisualCases.CaptureAndVerify(window, "lifecycle-initial-minimum", "p1-lifecycle-protection");

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "기본 크기 재배치 후");
        RowShellVisualCases.CaptureAndVerify(window, "lifecycle-initial-default", "p1-lifecycle-protection");

        var initialPath = viewModel.CurrentPath;
        await ClickAsync(window, Find<Button>(window, "AddRowButton"));
        var newCancelSession = viewModel.Session;
        var newCancelFingerprint = Qa.Fingerprint(newCancelSession.Workspace);
        var newCancelRevision = newCancelSession.Revision;
        var newCancelHistory = (newCancelSession.CanUndo, newCancelSession.CanRedo);
        Qa.Check(newCancelSession.IsDirty, "행 추가 뒤 취소 시나리오가 dirty가 되지 않았습니다.");
        Qa.Check(ReferenceEquals(newCancelSession, initialSession) && initialWorkspaceId == newCancelSession.Workspace.Id,
            "새 작업 취소 시나리오에서 기존 세션을 유지하지 못했습니다.");

        await ClickAsync(window, Find<Button>(window, "NewWorkspaceButton"));
        AssertPendingUi(window, viewModel, WorkspaceActionKind.NewWorkspace);
        Qa.Check(ReferenceEquals(viewModel.Session, newCancelSession), "새 작업 확인 전 원 세션이 교체됐습니다.");
        Qa.Equal(newCancelFingerprint, Qa.Fingerprint(viewModel.Session.Workspace), "새 작업 확인 전 원본 workspace");
        Qa.Check(Find<Button>(window, "SavePendingWorkspaceActionButton").IsFocused,
            "확인 overlay가 저장 버튼으로 managed focus를 시작하지 않았습니다.");

        SendKey(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.None, "\t");
        Qa.Check(Find<Button>(window, "DiscardPendingWorkspaceActionButton").IsFocused,
            "확인 overlay Tab 순환이 저장→버리기 순서로 이동하지 않았습니다.");
        SendKey(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift, "\t");
        Qa.Check(Find<Button>(window, "SavePendingWorkspaceActionButton").IsFocused,
            "확인 overlay Shift+Tab 순환이 버리기→저장 순서로 돌아오지 않았습니다.");

        var undoButton = Find<Button>(window, "UndoButton");
        Qa.Check(undoButton.IsEffectivelyEnabled, "확인 overlay 뒤편 입력 차단 검증의 Undo 버튼이 비활성입니다.");
        var revisionBeforeBlockedClick = viewModel.Session.Revision;
        await ClickAsync(window, undoButton);
        Qa.Equal(revisionBeforeBlockedClick, viewModel.Session.Revision,
            "확인 overlay가 배경 Undo 합성 클릭을 차단하지 않았습니다.");
        Qa.Check(viewModel.IsPendingWorkspaceActionVisible, "배경 클릭이 pending confirmation을 닫았습니다.");
        var fingerprintBeforeBlockedShortcuts = Qa.Fingerprint(viewModel.Session.Workspace);
        var historyBeforeBlockedShortcuts = (viewModel.Session.CanUndo, viewModel.Session.CanRedo);
        SendKey(window, Key.Z, PhysicalKey.Z, RawInputModifiers.Control, "z");
        SendKey(window, Key.Y, PhysicalKey.Y, RawInputModifiers.Control, "y");
        Qa.Equal(revisionBeforeBlockedClick, viewModel.Session.Revision,
            "modal overlay 중 Ctrl+Z/Y가 배경 editor를 변경했습니다.");
        Qa.Equal(fingerprintBeforeBlockedShortcuts, Qa.Fingerprint(viewModel.Session.Workspace),
            "modal overlay 중 Ctrl+Z/Y 뒤 workspace 보존");
        Qa.Equal(historyBeforeBlockedShortcuts, (viewModel.Session.CanUndo, viewModel.Session.CanRedo),
            "modal overlay 중 Ctrl+Z/Y 뒤 undo/redo history 보존");
        Qa.Check(viewModel.IsPendingWorkspaceActionVisible, "modal overlay 중 Ctrl+Z/Y가 confirmation을 닫았습니다.");

        RowShellVisualCases.CaptureAndVerify(window, "lifecycle-confirmation-default", "p1-lifecycle-protection");
        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "확인 overlay 최소 크기 재배치 후");
        RowShellVisualCases.CaptureAndVerify(window, "lifecycle-confirmation-minimum", "p1-lifecycle-protection");
        await ClickAsync(window, Find<Button>(window, "CancelPendingWorkspaceActionButton"));
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.IsPendingWorkspaceActionVisible, "새 작업 취소 버튼이 overlay를 닫지 않았습니다.");
        Qa.Check(ReferenceEquals(viewModel.Session, newCancelSession), "새 작업 취소가 원 세션 참조를 바꿨습니다.");
        Qa.Equal(newCancelFingerprint, Qa.Fingerprint(viewModel.Session.Workspace), "새 작업 취소의 전체 workspace 보존");
        Qa.Equal(newCancelRevision, viewModel.Session.Revision, "새 작업 취소의 revision 보존");
        Qa.Equal(newCancelHistory, (viewModel.Session.CanUndo, viewModel.Session.CanRedo), "새 작업 취소의 undo history 보존");
        Qa.Equal(initialPath, viewModel.CurrentPath, "새 작업 취소의 현재 경로 보존");
        Qa.Check(Find<MathEditorControl>(window, "MathEditorSurface").IsFocused,
            "취소 뒤 managed focus가 기존 수식 편집기로 복원되지 않았습니다.");

        var openCancelSession = viewModel.Session;
        var openCancelFingerprint = Qa.Fingerprint(openCancelSession.Workspace);
        var openCancelPath = viewModel.CurrentPath;
        var openCancelResult = await viewModel.OpenFromPathAsync(targetPath);
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, openCancelResult,
            "dirty open 취소 시나리오에서 확인 요청 결과");
        AssertPendingUi(window, viewModel, WorkspaceActionKind.OpenWorkspace);
        Qa.Equal(targetFileName, viewModel.PendingWorkspaceActionTarget, "긴 Open 대상 표시 문자열");
        RowShellVisualCases.CaptureAndVerify(window, "lifecycle-open-long-target-minimum", "p1-lifecycle-protection");
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "긴 Open 대상 기본 크기 재배치 후");
        RowShellVisualCases.CaptureAndVerify(window, "lifecycle-open-long-target-default", "p1-lifecycle-protection");
        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        SendKey(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None, "\u001b");
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.IsPendingWorkspaceActionVisible, "ESC가 dirty open confirmation을 취소하지 않았습니다.");
        Qa.Check(ReferenceEquals(viewModel.Session, openCancelSession), "dirty open 취소가 원 세션 참조를 바꿨습니다.");
        Qa.Equal(openCancelFingerprint, Qa.Fingerprint(viewModel.Session.Workspace), "dirty open 취소의 전체 workspace 보존");
        Qa.Equal(openCancelPath, viewModel.CurrentPath, "dirty open 취소의 현재 경로 보존");

        var openSaveSession = viewModel.Session;
        var openResult = await viewModel.OpenFromPathAsync(targetPath);
        Qa.Equal(WorkspaceActionRequestResult.ConfirmationRequired, openResult, "dirty open 저장 시나리오에서 확인 요청 결과");
        AssertPendingUi(window, viewModel, WorkspaceActionKind.OpenWorkspace);
        await using (var pathLock = new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await ClickAsync(window, Find<Button>(window, "SavePendingWorkspaceActionButton"));
            await WaitUntilAsync(() => !viewModel.IsPendingWorkspaceActionBusy &&
                !string.IsNullOrWhiteSpace(viewModel.PendingWorkspaceActionError), "잠금 경로 저장 실패 표시");
            Qa.Check(viewModel.IsPendingWorkspaceActionVisible, "저장 실패 뒤 pending guard가 유지되지 않았습니다.");
            Qa.Check(ReferenceEquals(viewModel.Session, openSaveSession), "저장 실패 뒤 원 세션이 교체됐습니다.");
            Qa.Check(viewModel.Session.IsDirty, "저장 실패 뒤 원 세션이 dirty로 유지되지 않았습니다.");
            Qa.Equal(Path.GetFullPath(sourcePath), viewModel.CurrentPath ?? string.Empty, "저장 실패 뒤 원 경로 보존");
            Qa.Equal(viewModel.PendingWorkspaceActionError,
                Find<TextBlock>(window, "PendingWorkspaceActionError").Text ?? string.Empty,
                "저장 오류 overlay 표시");
            RowShellVisualCases.CaptureAndVerify(window, "lifecycle-save-error-minimum", "p1-lifecycle-protection");
        }

        await ClickAsync(window, Find<Button>(window, "SavePendingWorkspaceActionButton"));
        await WaitUntilAsync(() => !viewModel.IsPendingWorkspaceActionVisible &&
            viewModel.Session.Workspace.Id == targetWorkspaceId &&
            viewModel.CurrentPath == Path.GetFullPath(targetPath), "dirty open 저장 후 대상 작업으로 전환");
        Qa.Check(!ReferenceEquals(viewModel.Session, openSaveSession), "저장 성공 후 Open이 원 세션을 유지했습니다.");
        Qa.Equal(targetWorkspaceId, viewModel.Session.Workspace.Id, "저장 성공 후 Open 대상 workspace ID");
        Qa.Check(!viewModel.Session.IsDirty, "저장 성공 후 열린 대상 workspace가 dirty입니다.");
        Qa.Equal(Path.GetFullPath(targetPath), viewModel.CurrentPath ?? string.Empty, "저장 성공 후 Open 경로");

        await ClickAsync(window, Find<Button>(window, "AddRowButton"));
        var discardSession = viewModel.Session;
        Qa.Check(discardSession.IsDirty, "새 작업 discard 시나리오가 dirty가 되지 않았습니다.");
        await ClickAsync(window, Find<Button>(window, "NewWorkspaceButton"));
        AssertPendingUi(window, viewModel, WorkspaceActionKind.NewWorkspace);
        await ClickAsync(window, Find<Button>(window, "DiscardPendingWorkspaceActionButton"));
        Qa.Check(!viewModel.IsPendingWorkspaceActionVisible, "변경 버리기 뒤 확인 overlay가 남았습니다.");
        Qa.Check(!ReferenceEquals(viewModel.Session, discardSession), "변경 버리기가 새 세션을 만들지 않았습니다.");
        Qa.Check(!viewModel.Session.IsDirty, "변경 버리기 후 새 세션이 dirty입니다.");
        Qa.Equal(1, viewModel.Session.Workspace.Groups.Length, "변경 버리기 후 새 workspace 그룹 수");
        Qa.Equal(1, viewModel.Session.Workspace.Groups[0].Rows.Length, "변경 버리기 후 새 workspace 행 수");
        Qa.Equal<string?>(null, viewModel.CurrentPath, "새 workspace가 이전 파일 경로를 유지했습니다.");

        await ClickAsync(window, Find<Button>(window, "AddRowButton"));
        var closeCancelSession = viewModel.Session;
        var closeCancelFingerprint = Qa.Fingerprint(closeCancelSession.Workspace);
        var closeCancelPath = viewModel.CurrentPath;
        window.Close();
        EnsureStubHandle(window, "dirty Close 취소 확인 진입");
        await DrainLayoutAsync(window);
        AssertPendingUi(window, viewModel, WorkspaceActionKind.CloseWindow);
        Qa.Check(window.IsVisible, "dirty Close 요청이 확인 없이 창을 닫았습니다.");
        await ClickAsync(window, Find<Button>(window, "CancelPendingWorkspaceActionButton"));
        await DrainLayoutAsync(window);
        Qa.Check(window.IsVisible, "Close 취소가 현재 창을 유지하지 않았습니다.");
        Qa.Check(ReferenceEquals(viewModel.Session, closeCancelSession), "Close 취소가 기존 세션을 교체했습니다.");
        Qa.Equal(closeCancelFingerprint, Qa.Fingerprint(viewModel.Session.Workspace), "Close 취소의 전체 workspace 보존");
        Qa.Equal(closeCancelPath, viewModel.CurrentPath, "Close 취소의 현재 경로 보존");

        window.Close();
        await DrainLayoutAsync(window);
        AssertPendingUi(window, viewModel, WorkspaceActionKind.CloseWindow);
        await ClickAsync(window, Find<Button>(window, "DiscardPendingWorkspaceActionButton"));
        await WaitUntilAsync(() => !window.IsVisible, "dirty Close discard 뒤 창 닫힘");
    }

    private static void AssertHeaderBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        var newButton = Find<Button>(window, "NewWorkspaceButton");
        var bindings = new (Button Button, ICommand Command, string Id, string Name)[]
        {
            (newButton, viewModel.NewWorkspaceCommand, "NewWorkspaceButton", "새 수식 작업 만들기"),
            (Find<Button>(window, "OpenWorkspaceButton"), viewModel.OpenCommand, "OpenWorkspaceButton", "수식 파일 열기"),
            (Find<Button>(window, "SaveWorkspaceButton"), viewModel.SaveCommand, "SaveWorkspaceButton", "수식 원본 저장"),
            (Find<Button>(window, "UndoButton"), viewModel.UndoCommand, "UndoButton", "실행 취소"),
            (Find<Button>(window, "RedoButton"), viewModel.RedoCommand, "RedoButton", "다시 실행"),
            (Find<Button>(window, "ExportSvgButton"), viewModel.ShowOutputPreviewCommand, "ExportSvgButton", "SVG 출력 미리보기 열기")
        };

        foreach (var (button, command, automationId, automationName) in bindings)
        {
            Qa.Equal(automationId, AutomationProperties.GetAutomationId(button) ?? string.Empty,
                automationId + " AutomationId");
            Qa.Equal(automationName, AutomationProperties.GetName(button) ?? string.Empty,
                $"{button.Name} 한국어 AutomationName");
            Qa.Check(ReferenceEquals(button.Command, command), $"{button.Name}이 실제 VM command에 연결되지 않았습니다.");
        }
    }

    private static void AssertPendingUi(MainWindow window, MainWindowViewModel viewModel, WorkspaceActionKind expectedKind)
    {
        Qa.Check(viewModel.IsPendingWorkspaceActionVisible, "요청 확인 overlay가 표시되지 않았습니다.");
        Qa.Equal(expectedKind, viewModel.PendingWorkspaceAction, "확인 대상 종류");
        Qa.Check(Find<Grid>(window, "PendingWorkspaceActionOverlay").IsVisible,
            "확인 overlay가 visual tree에서 표시되지 않았습니다.");
        Qa.Equal(viewModel.PendingWorkspaceActionTitle,
            Find<TextBlock>(window, "PendingWorkspaceActionTitle").Text ?? string.Empty, "확인 overlay 제목");
        Qa.Equal(viewModel.PendingWorkspaceActionMessage,
            Find<TextBlock>(window, "PendingWorkspaceActionMessage").Text ?? string.Empty, "확인 overlay 설명");
        Qa.Equal(viewModel.PendingWorkspaceActionTarget,
            Find<TextBlock>(window, "PendingWorkspaceActionTarget").Text ?? string.Empty, "확인 overlay 요청 대상");

        var buttons = new (string Id, string Name, ICommand Command, Button Button)[]
        {
            ("SavePendingWorkspaceActionButton", "저장 후 계속", viewModel.SavePendingWorkspaceActionCommand,
                Find<Button>(window, "SavePendingWorkspaceActionButton")),
            ("DiscardPendingWorkspaceActionButton", "변경 버리기", viewModel.DiscardPendingWorkspaceActionCommand,
                Find<Button>(window, "DiscardPendingWorkspaceActionButton")),
            ("CancelPendingWorkspaceActionButton", "계속 편집", viewModel.CancelPendingWorkspaceActionCommand,
                Find<Button>(window, "CancelPendingWorkspaceActionButton"))
        };
        foreach (var (id, name, command, button) in buttons)
        {
            Qa.Equal(id, AutomationProperties.GetAutomationId(button) ?? string.Empty, id + " AutomationId");
            Qa.Equal(name, AutomationProperties.GetName(button) ?? string.Empty, id + " 한국어 AutomationName");
            Qa.Check(ReferenceEquals(command, button.Command), id + " ViewModel command 연결");
            Qa.Equal(command.CanExecute(null), button.IsEffectivelyEnabled, id + " CanExecute/실효 활성 상태");
        }

        Qa.Check(ToolTip.GetTip(Find<Button>(window, "SavePendingWorkspaceActionButton")) is not null &&
                 ToolTip.GetTip(Find<Button>(window, "DiscardPendingWorkspaceActionButton")) is not null &&
                 ToolTip.GetTip(Find<Button>(window, "CancelPendingWorkspaceActionButton")) is not null,
            "수명 보호 버튼에 설명 tooltip이 없습니다.");
    }

    private static async Task ClickAsync(Window window, Button button)
    {
        Qa.Check(button.IsEffectivelyEnabled, "비활성 lifecycle 버튼 합성 클릭 시도: " + button.Name);
        var origin = button.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "lifecycle 버튼의 창 좌표를 가져오지 못했습니다: " + button.Name);
        var point = origin!.Value + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static void SendKey(Window window, Key key, PhysicalKey physicalKey, RawInputModifiers modifiers, string symbol)
    {
        window.KeyPress(key, modifiers, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
        Qa.Check(condition(), "시간 제한 안에 완료되지 않았습니다: " + label);
    }

    private static async Task DrainLayoutAsync(Window window)
    {
        for (var index = 0; index < 5; index++)
        {
            Dispatcher.UIThread.RunJobs();
            window.InvalidateMeasure();
            window.InvalidateArrange();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void EnsureStubHandle(Window window, string phase)
    {
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is not null && handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            $"{phase}: STUB/0 headless handle을 확인하지 못해 UI 동작을 중단했습니다.");
    }

    private static void CloseOwnedWindow(MainWindow? window)
    {
        if (window is null || !window.IsVisible)
            return;

        if (window.DataContext is MainWindowViewModel viewModel && viewModel.IsPendingWorkspaceActionVisible)
            viewModel.CancelPendingWorkspaceAction();

        window.Close();
        if (window.DataContext is MainWindowViewModel closingViewModel &&
            closingViewModel.IsPendingWorkspaceActionVisible &&
            closingViewModel.PendingWorkspaceAction == WorkspaceActionKind.CloseWindow)
            closingViewModel.DiscardPendingWorkspaceAction();
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name} 컨트롤을 찾지 못했습니다.");

    private sealed class ShortTempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "am-" + Guid.NewGuid().ToString("N")[..8]);

        public ShortTempDirectory() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
