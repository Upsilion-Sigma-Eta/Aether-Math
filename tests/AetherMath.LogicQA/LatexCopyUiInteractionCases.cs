using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class LatexCopyUiInteractionCases
{
    private const string CaseId = "LATEX-COPY-UI";
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "전체 LaTeX 복사의 실제 UI 바인딩·fake sink·문서 상태 보존을 Headless에서 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 LaTeX 복사 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("LaTeX 복사 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
            await CloseOwnedWindowAsync(_ownedWindow);
            _ownedWindow = null;
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
        startInfo.ArgumentList.Add("--latex-copy-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("LaTeX 복사 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("LaTeX 복사 UI Headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"LaTeX 복사 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("LaTeX 복사 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        using var temporaryDirectory = new TempDirectory();
        var service = new WorkspaceFileService();
        var validPath = temporaryDirectory.File("latex-copy-valid.aethermath");
        var blockedPath = temporaryDirectory.File("latex-copy-blocked.aethermath");
        var validWorkspace = CreateValidWorkspace();
        var blockedWorkspace = CreateBlockedWorkspace();
        await service.SaveAsync(validPath, new EditingSession(validWorkspace).CaptureSnapshot());
        await service.SaveAsync(blockedPath, new EditingSession(blockedWorkspace).CaptureSnapshot());
        Qa.Check(File.Exists(validPath) && File.Exists(blockedPath),
            "LaTeX 복사 UI fixture를 명시 TempDirectory에 저장하지 못했습니다.");

        var viewModel = new MainWindowViewModel(service);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(validPath),
            "명시 경로로 정상 LaTeX UI fixture 열기");
        var window = new MainWindow(viewModel) { Width = 1060, Height = 700, CanResize = true };
        _ownedWindow = window;
        EnsureStubHandle(window, "정상 fixture Show 전");
        window.Show();
        EnsureStubHandle(window, "정상 fixture Show 후");
        await DrainLayoutAsync(window);

        var session = viewModel.Session;
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Check(ReferenceEquals(editor.Session, session), "복사 UI가 현재 Session이 아닌 편집기를 표시합니다.");
        Qa.Check(viewModel.CanExportLatex && viewModel.LatexLayoutDiagnostics.Length > 0 && viewModel.CanCopyLatex,
            "정상 복사 fixture의 layout advisory가 차단 진단으로 취급되거나 복사가 비활성입니다.");
        Qa.Check(!viewModel.IsOutputPreviewVisible && viewModel.OutputPreview.State == MathOutputPreviewState.Empty,
            "복사 UI 초기 상태가 출력 미리보기 상태를 변경했습니다.");

        var expander = Find<Expander>(window, "LatexCodeExpander");
        var codeView = Find<LatexCodeSurface>(window, "LatexCodeView");
        var copyButton = Find<Button>(window, "CopyLatexButton");
        var copiedTexts = new List<string>();
        window.LatexClipboardWriterOverride = text =>
        {
            copiedTexts.Add(text);
            return Task.CompletedTask;
        };
        Qa.Check(window.LatexClipboardWriterOverride is not null,
            "Headless에서 복사 동작 전에 fake clipboard sink가 주입되지 않았습니다.");

        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-initial-collapsed-minimum");
        window.Width = 1360;
        window.Height = 880;
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-initial-collapsed-default");
        window.Width = 1060;
        window.Height = 700;
        await DrainLayoutAsync(window);

        expander.IsExpanded = true;
        await DrainLayoutAsync(window);
        Qa.Check(codeView.IsReadOnly && codeView.SourceText == viewModel.LatexCode &&
                 !string.IsNullOrWhiteSpace(codeView.SourceText),
            "LaTeX 원문은 전체 코드가 표시되는 읽기 전용 선택 표면이 아닙니다.");
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-expanded-minimum");

        var source = viewModel.LatexCode;
        Qa.Check(source.Length > 2, "정상 fixture의 LaTeX 코드가 부분 선택 검사를 하기에는 너무 짧습니다.");
        codeView.SelectionStart = 0;
        codeView.SelectionEnd = Math.Min(3, source.Length);
        Qa.Check(codeView.SelectionEnd > codeView.SelectionStart,
            "복사 직전에 LaTeX 원문 일부를 실제 선택 표면에서 만들지 못했습니다.");
        var successBaseline = CaptureSession(session);
        var successPreview = viewModel.OutputPreview.PreviewSnapshot;
        var successPreviewState = viewModel.OutputPreview.State;
        await ClickCopyButtonAsync(window, "latex-copy-success");
        await WaitUntilAsync(() => !viewModel.IsCopyingLatex, "정상 전체 코드 복사 완료");
        await DrainLayoutAsync(window);
        Qa.Equal(1, copiedTexts.Count, "성공 복사가 writer를 정확히 한 번 호출함");
        Qa.Equal(source, copiedTexts[0], "부분 선택 중에도 전체 LaTeX 원문을 전달함");
        Qa.Equal("LaTeX 코드를 복사했습니다.", viewModel.LatexCopyMessage, "성공 상태 문구");
        Qa.Check(expander.IsExpanded, "복사 버튼 클릭이 펼친 코드 패널을 우발적으로 접었습니다.");
        AssertSessionUnchanged(successBaseline, session, "성공 복사");
        AssertPreviewUnchanged(viewModel, successPreview, successPreviewState, "성공 복사");
        Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer").Offset = default;
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-success-expanded-minimum");

        expander.IsExpanded = false;
        await DrainLayoutAsync(window);
        var collapsedBaseline = CaptureSession(session);
        await ClickCopyButtonAsync(window, "latex-copy-collapsed-header");
        await WaitUntilAsync(() => !viewModel.IsCopyingLatex, "접힌 헤더 복사 완료");
        Qa.Check(!expander.IsExpanded, "접힌 LaTeX 코드 헤더의 복사 버튼이 패널을 펼쳤습니다.");
        Qa.Equal(2, copiedTexts.Count, "접힌 헤더 복사 writer 호출 수");
        Qa.Equal(source, copiedTexts[1], "접힌 헤더에서도 전체 LaTeX 원문을 전달함");
        AssertSessionUnchanged(collapsedBaseline, session, "접힌 헤더 복사");
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-success-collapsed-minimum");

        expander.IsExpanded = true;
        await DrainLayoutAsync(window);
        var failureBaseline = CaptureSession(session);
        var failurePreview = viewModel.OutputPreview.PreviewSnapshot;
        var failurePreviewState = viewModel.OutputPreview.State;
        var failedWriterCalls = 0;
        window.LatexClipboardWriterOverride = _ =>
        {
            failedWriterCalls++;
            return Task.FromException(new IOException("fake clipboard write failure"));
        };
        await ClickCopyButtonAsync(window, "latex-copy-failure");
        await WaitUntilAsync(() => !viewModel.IsCopyingLatex, "실패 상태 복구");
        await DrainLayoutAsync(window);
        Qa.Equal(1, failedWriterCalls, "실패 writer 호출 횟수");
        Qa.Check(viewModel.LatexCopyMessage.Contains("복사에 실패", StringComparison.Ordinal) &&
                 viewModel.LatexCopyMessage.Contains("fake clipboard write failure", StringComparison.Ordinal),
            "writer 실패가 복사 결과 상태 문구로 표시되지 않습니다.");
        Qa.Check(copyButton.IsEffectivelyEnabled, "일시적인 writer 실패 뒤 재시도 버튼이 비활성입니다.");
        AssertSessionUnchanged(failureBaseline, session, "writer 실패");
        AssertPreviewUnchanged(viewModel, failurePreview, failurePreviewState, "writer 실패");
        Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer").Offset = default;
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-failure-expanded-minimum");

        copiedTexts.Clear();
        window.LatexClipboardWriterOverride = text =>
        {
            copiedTexts.Add(text);
            return Task.CompletedTask;
        };
        var retryBaseline = CaptureSession(session);
        await ClickCopyButtonAsync(window, "latex-copy-retry");
        await WaitUntilAsync(() => !viewModel.IsCopyingLatex, "복사 재시도 완료");
        await DrainLayoutAsync(window);
        Qa.Equal(1, copiedTexts.Count, "실패 이후 재시도 writer 호출 수");
        Qa.Equal(source, copiedTexts[0], "실패 이후 재시도 원문");
        Qa.Equal("LaTeX 코드를 복사했습니다.", viewModel.LatexCopyMessage, "재시도 성공 상태 문구");
        AssertSessionUnchanged(retryBaseline, session, "복사 재시도");
        AssertPreviewUnchanged(viewModel, failurePreview, failurePreviewState, "복사 재시도");
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-retry-expanded-minimum");

        var pendingStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.LatexClipboardWriterOverride = async text =>
        {
            pendingStarted.TrySetResult(text);
            await releasePending.Task;
        };
        await ClickCopyButtonAsync(window, "latex-copy-busy-start");
        var busySource = await pendingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Qa.Check(viewModel.IsCopyingLatex && !viewModel.CanCopyLatex && !copyButton.IsEffectivelyEnabled,
            "복사가 진행되는 동안 busy 상태와 버튼 비활성화가 표시되지 않습니다.");
        Qa.Equal("복사 중…", Find<TextBlock>(window, "CopyLatexButtonLabel").Text ?? string.Empty,
            "복사 진행 중 버튼 레이블");
        var busyWriterCalls = 0;
        var busyResult = await viewModel.CopyLatexAsync(_ =>
        {
            busyWriterCalls++;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Busy, busyResult.Status, "동시 호출 busy 결과");
        Qa.Equal(0, busyWriterCalls, "busy 재진입이 clipboard sink를 호출하지 않음");
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-busy-expanded-minimum");
        releasePending.TrySetResult(true);
        await WaitUntilAsync(() => !viewModel.IsCopyingLatex, "busy 이후 첫 복사 완료");
        await DrainLayoutAsync(window);
        Qa.Equal("전체 코드 복사", Find<TextBlock>(window, "CopyLatexButtonLabel").Text ?? string.Empty,
            "복사 완료 뒤 버튼 레이블 복구");
        Qa.Equal(source, busySource, "busy 검사 중 첫 writer에 고정 전달된 원문");
        Qa.Equal("LaTeX 코드를 복사했습니다.", viewModel.LatexCopyMessage, "busy 이후 첫 복사의 완료 상태");

        var previousStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrevious = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.LatexClipboardWriterOverride = async text =>
        {
            previousStarted.TrySetResult(text);
            await releasePrevious.Task;
        };
        await ClickCopyButtonAsync(window, "latex-copy-previous-snapshot-start");
        var capturedPreviousSource = await previousStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Qa.Equal(source, capturedPreviousSource, "writer가 당시 전체 LaTeX 원문을 고정함");
        Qa.Caret(session, FixtureTextId(session), 1);
        Qa.Check(session.InsertText("x"), "복사 중 현재 수식 편집으로 이전 snapshot 경계 생성");
        await DrainLayoutAsync(window);
        var editedSource = viewModel.LatexCode;
        Qa.Check(!string.Equals(source, editedSource, StringComparison.Ordinal),
            "복사 완료 대기 중 문서 편집이 LaTeX 코드를 변경하지 않았습니다.");
        var concurrentEditBaseline = CaptureSession(session);
        releasePrevious.TrySetResult(true);
        await WaitUntilAsync(() => !viewModel.IsCopyingLatex, "이전 snapshot 복사 완료");
        await DrainLayoutAsync(window);
        Qa.Equal(source, capturedPreviousSource, "이전 snapshot writer에 전달된 전체 원문");
        Qa.Check(viewModel.LatexCopyMessage.Contains("이전 수식", StringComparison.Ordinal),
            "편집 중 완료된 복사가 이전 snapshot 결과로 표시되지 않습니다.");
        AssertSessionUnchanged(concurrentEditBaseline, session, "이전 snapshot 복사 완료 후 동시 편집 상태");
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-previous-snapshot-expanded-minimum");

        var unavailableResult = await viewModel.CopyLatexAsync(null);
        Qa.Equal(LatexCopyStatus.Unavailable, unavailableResult.Status, "writer가 없는 복사 API 결과");
        Qa.Check(viewModel.LatexCopyMessage.Contains("사용할 수 없습니다", StringComparison.Ordinal),
            "clipboard writer가 없을 때 사용 불가 문구가 표시되지 않습니다.");
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-unavailable-expanded-minimum");
        await CloseOwnedWindowAsync(window);
        _ownedWindow = null;

        await RunBlockedCopyChecksAsync(service, blockedPath);
    }

    private static async Task RunBlockedCopyChecksAsync(WorkspaceFileService service, string blockedPath)
    {
        var viewModel = new MainWindowViewModel(service);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(blockedPath),
            "명시 경로로 LaTeX 차단 진단 fixture 열기");
        var window = new MainWindow(viewModel) { Width = 1060, Height = 700, CanResize = true };
        _ownedWindow = window;
        EnsureStubHandle(window, "차단 fixture Show 전");
        window.Show();
        EnsureStubHandle(window, "차단 fixture Show 후");
        await DrainLayoutAsync(window);

        var expander = Find<Expander>(window, "LatexCodeExpander");
        var button = Find<Button>(window, "CopyLatexButton");
        var sinkCalls = 0;
        window.LatexClipboardWriterOverride = _ =>
        {
            sinkCalls++;
            return Task.CompletedTask;
        };
        expander.IsExpanded = true;
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.CanExportLatex && !viewModel.CanCopyLatex && !button.IsEffectivelyEnabled &&
                 !string.IsNullOrWhiteSpace(viewModel.LatexCodeDiagnostic) &&
                 viewModel.LatexCopyDisabledReason.Contains(viewModel.LatexCodeDiagnostic, StringComparison.Ordinal),
            "차단형 LaTeX 진단이 코드 복사 버튼 비활성 사유에 연결되지 않습니다.");
        var blockedResult = await viewModel.CopyLatexAsync(text =>
        {
            sinkCalls++;
            return Task.CompletedTask;
        });
        Qa.Equal(LatexCopyStatus.Blocked, blockedResult.Status, "미지원 텍스트가 있는 원문의 복사 차단 결과");
        Qa.Equal(0, sinkCalls, "차단된 LaTeX 코드에서 clipboard sink를 호출하지 않음");
        Qa.Equal(viewModel.LatexCodeDiagnostic, viewModel.LatexCopyMessage,
            "복사 차단 상태가 기존 LaTeX 진단과 같은 의미를 전달함");
        Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer").Offset = default;
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-blocked-expanded-minimum");
    }

    private static async Task ClickCopyButtonAsync(MainWindow window, string phase)
    {
        EnsureStubHandle(window, phase + " 합성 입력 전");
        Qa.Check(window.LatexClipboardWriterOverride is not null,
            phase + ": Headless 복사 전에 fake sink가 주입되지 않았습니다.");
        var button = Find<Button>(window, "CopyLatexButton");
        var expander = Find<Expander>(window, "LatexCodeExpander");
        var wasExpanded = expander.IsExpanded;
        if (!button.IsEffectivelyEnabled)
        {
            RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-click-disabled-evidence");
            throw new InvalidOperationException(phase + ": 비활성 복사 버튼에 합성 클릭할 수 없습니다.");
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buttonBounds = BoundsInWindow(button, window);
            var windowBounds = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
            if (!Contains(windowBounds, buttonBounds))
            {
                RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-click-clipped-evidence");
                throw new InvalidOperationException(
                    $"{phase}: 복사 버튼이 창 viewport에서 잘렸습니다: button={buttonBounds}, window={windowBounds}.");
            }

            var point = new Point(buttonBounds.X + buttonBounds.Width / 2, buttonBounds.Y + buttonBounds.Height / 2);
            window.MouseMove(point, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, phase + " pointer hover 후");
            var settledBounds = BoundsInWindow(button, window);
            if (!Contains(windowBounds, settledBounds))
            {
                RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-click-hover-clipped-evidence");
                throw new InvalidOperationException(
                    $"{phase}: hover/layout 이후 복사 버튼이 잘렸습니다: button={settledBounds}, window={windowBounds}.");
            }

            var settledPoint = new Point(settledBounds.X + settledBounds.Width / 2,
                settledBounds.Y + settledBounds.Height / 2);
            if (Math.Abs(point.X - settledPoint.X) > 0.5 || Math.Abs(point.Y - settledPoint.Y) > 0.5)
            {
                if (attempt == 3)
                {
                    RowShellVisualCases.CaptureLatexCopyAndVerify(window, "latex-copy-click-unstable-evidence");
                    throw new InvalidOperationException(phase + ": hover 뒤 복사 버튼의 실제 좌표가 안정되지 않았습니다.");
                }
                continue;
            }

            if (!button.IsEffectivelyEnabled)
                throw new InvalidOperationException(phase + ": hover 중 복사 버튼이 비활성화됐습니다.");
            window.MouseDown(settledPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(settledPoint, MouseButton.Left, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            Qa.Equal(wasExpanded, expander.IsExpanded,
                phase + ": 복사 버튼 클릭이 코드 패널의 접힘 상태를 변경하지 않음");
            return;
        }

        throw new InvalidOperationException(phase + ": 복사 버튼을 안정된 위치에서 누르지 못했습니다.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string label)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return;
            await Task.Delay(5);
        }
        throw new TimeoutException(label + " 상태가 제한 시간 안에 도달하지 않았습니다.");
    }

    private static async Task DrainLayoutAsync(Window window)
    {
        for (var iteration = 0; iteration < 5; iteration++)
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
            $"{phase}: Headless 창 STUB/0 handle을 확인하지 못했습니다.");
    }

    private static Rect BoundsInWindow(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("복사 control의 창 기준 좌표를 얻지 못했습니다: " + control.Name);
        return new Rect(origin, control.Bounds.Size);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

    private static Guid FixtureTextId(EditingSession session) =>
        Qa.Nodes(session.Workspace).OfType<TextNode>().First(text => text.Text.StartsWith("left", StringComparison.Ordinal)).Id;

    private static void AssertSessionUnchanged(SessionState expected, EditingSession actual, string label)
    {
        var current = CaptureSession(actual);
        Qa.Equal(expected.WorkspaceFingerprint, current.WorkspaceFingerprint, label + " workspace");
        Qa.Equal(expected.StateId, current.StateId, label + " state ID");
        Qa.Equal(expected.Revision, current.Revision, label + " revision");
        Qa.Equal(expected.Caret, current.Caret, label + " caret");
        Qa.Equal(expected.Selection, current.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, current.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, current.IsNodeSelected, label + " whole-node selection");
        Qa.Equal(expected.IsDirty, current.IsDirty, label + " dirty state");
        Qa.Equal(expected.CanUndo, current.CanUndo, label + " undo state");
        Qa.Equal(expected.CanRedo, current.CanRedo, label + " redo state");
    }

    private static SessionState CaptureSession(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(session.Workspace), snapshot.StateId, snapshot.Revision,
            session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
            session.IsDirty, session.CanUndo, session.CanRedo);
    }

    private static void AssertPreviewUnchanged(MainWindowViewModel viewModel,
        MathOutputPreviewSnapshot? expectedSnapshot, MathOutputPreviewState expectedState, string label)
    {
        Qa.Check(ReferenceEquals(expectedSnapshot, viewModel.OutputPreview.PreviewSnapshot),
            label + " output preview snapshot");
        Qa.Equal(expectedState, viewModel.OutputPreview.State, label + " output preview state");
        Qa.Check(!viewModel.IsOutputPreviewVisible, label + " output preview visibility");
    }

    private static EquationWorkspace CreateValidWorkspace()
    {
        var first = Qa.Sequence(Qa.Text("left"), Qa.Text(" = "), Qa.Text("right"));
        var second = Qa.Sequence(Qa.Text("up"), Qa.Text(" = "), Qa.Text("down"));
        var group = new EquationGroup(Guid.NewGuid(),
            [new EquationRow(Guid.NewGuid(), first), new EquationRow(Guid.NewGuid(), second)],
            AlignmentMode.Equals, RowSpacing: 40);
        return Qa.Workspace(first) with { Groups = [group] };
    }

    private static EquationWorkspace CreateBlockedWorkspace()
    {
        var unsupportedText = Qa.Text("bad\u0001text") with { Role = MathTextRole.PlainText };
        return Qa.Workspace(Qa.Sequence(unsupportedText, Qa.Text("+"), Qa.Text("1")));
    }

    private static async Task CloseOwnedWindowAsync(MainWindow? window)
    {
        if (window is null)
            return;
        if (window.DataContext is MainWindowViewModel viewModel)
        {
            if (viewModel.IsPendingWorkspaceActionVisible)
                viewModel.CancelPendingWorkspaceAction();
            viewModel.Session.MarkSaved(viewModel.Session.CaptureSnapshot());
        }
        EnsureStubHandle(window, "소유 LaTeX 복사 Headless 창 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "소유 LaTeX 복사 Headless 창을 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "Headless 창 종료 뒤 native handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private sealed record SessionState(
        string WorkspaceFingerprint,
        Guid StateId,
        long Revision,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        bool IsDirty,
        bool CanUndo,
        bool CanRedo);
}
