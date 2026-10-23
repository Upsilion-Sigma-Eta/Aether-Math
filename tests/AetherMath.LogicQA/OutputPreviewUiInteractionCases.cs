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
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;

namespace AetherMath.LogicQA;

internal static class OutputPreviewUiInteractionCases
{
    private const string CaseId = "OUTPUT-PREVIEW-UI";
    private static MainWindow? _window;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "전체 작업 SVG 미리보기의 출력 모드·고정 snapshot·탭 복귀를 실제 App Headless에서 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId != CaseId)
        {
            Console.Error.WriteLine("알 수 없는 출력 미리보기 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("출력 미리보기 child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
            await CloseOwnedWindowAsync(_window);
            _window = null;
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
        startInfo.ArgumentList.Add("--output-preview-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("출력 미리보기 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("출력 미리보기 UI child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"출력 미리보기 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("출력 미리보기 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "AetherMath-output-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var viewModel = new MainWindowViewModel();
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

            var originalSession = viewModel.Session;
            var editorSurface = Find<MathEditorControl>(window, "MathEditorSurface");
            var editorScroll = Find<ScrollViewer>(window, "MathEditorScrollViewer");
            var maxEditorOffset = new Vector(
                Math.Max(0, editorScroll.Extent.Width - editorScroll.Viewport.Width),
                Math.Max(0, editorScroll.Extent.Height - editorScroll.Viewport.Height));
            Qa.Check(maxEditorOffset.X > 1 || maxEditorOffset.Y > 1,
                "최소 창의 편집 수식 표면에서 스크롤 가능한 범위를 만들지 못했습니다.");
            var requestedEditorOffset = new Vector(
                maxEditorOffset.X > 1 ? Math.Min(14, maxEditorOffset.X) : 0,
                maxEditorOffset.Y > 1 ? Math.Min(14, maxEditorOffset.Y) : 0);
            if (requestedEditorOffset != default)
            {
                editorScroll.Offset = requestedEditorOffset;
                await DrainLayoutAsync(window);
            }
            var originalOffset = editorScroll.Offset;
            var offsetProbeWasNonzero = originalOffset != default;
            Qa.Check(offsetProbeWasNonzero,
                $"스크롤 가능한 편집 콘텐츠에 실제 nonzero offset을 만들지 못했습니다: max={maxEditorOffset}; actual={originalOffset}.");
            Console.WriteLine($"INFO editorScrollOffsetProbe={originalOffset}; max={maxEditorOffset}; nonzero={offsetProbeWasNonzero}");
            var initialSessionState = CaptureSessionState(originalSession);
            var topSvgButton = Find<Button>(window, "ExportSvgButton");
            var outputTab = Find<TabItem>(window, "OutputPreviewTab");
            var modeCombo = Find<ComboBox>(window, "OutputPreviewModeComboBox");
            var refreshButton = Find<Button>(window, "OutputPreviewRefreshButton");
            var saveButton = Find<Button>(window, "OutputPreviewSaveSvgButton");
            var previewCanvas = Find<MathOutputPreviewControl>(window, "OutputPreviewCanvas");
            var inspector = Find<TabControl>(window, "EquationInspector");

            Qa.Check(ReferenceEquals(topSvgButton.Command, viewModel.ShowOutputPreviewCommand),
                "상단 SVG 진입 버튼이 출력 미리보기 명령과 연결되지 않았습니다.");
            Qa.Check(ReferenceEquals(refreshButton.Command, viewModel.OutputPreview.RefreshPreviewCommand),
                "미리보기 갱신 버튼이 preview child 명령에 연결되지 않았습니다.");
            Qa.Check(ReferenceEquals(saveButton.Command, viewModel.SaveOutputPreviewSvgCommand),
                "SVG 저장 버튼이 picker command에 연결되지 않았습니다.");
            Qa.Check(modeCombo.Items.Cast<MathOutputModeChoice>().SequenceEqual(viewModel.OutputPreview.ModeChoices),
                "출력 모드 선택지가 child의 고정 choices와 일치하지 않습니다.");
            Qa.Check(!previewCanvas.Focusable && !previewCanvas.IsHitTestVisible,
                "출력 미리보기 control이 입력을 받거나 keyboard focus를 탈취합니다.");

            await ClickAsync(window, topSvgButton);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "상단 SVG 미리보기 진입 뒤");
            Qa.Check(viewModel.IsOutputPreviewVisible && ReferenceEquals(inspector.SelectedItem, outputTab),
                "상단 SVG 버튼이 출력 inspector tab과 중앙 preview surface를 열지 않았습니다.");
            Qa.Check(ReferenceEquals(viewModel.Session, originalSession) && !editorSurface.IsEffectivelyVisible,
                "출력 보기 진입이 편집 Session을 교체하거나 편집 surface를 계속 표시합니다.");
            Qa.Equal(MathOutputPreviewState.Ready, viewModel.OutputPreview.State,
                "완성된 기본 작업의 첫 출력 미리보기 상태");
            Qa.Check(viewModel.OutputPreview.PreviewSnapshot is not null &&
                     ReferenceEquals(previewCanvas.Layout, viewModel.OutputPreview.PreviewSnapshot.Layout),
                "화면이 child snapshot과 다른 layout 또는 빈 surface를 표시합니다.");
            AssertSessionState(initialSessionState, originalSession, "첫 미리보기 생성 뒤 원본 작업 불변");
            RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "output-ready-minimum");

            var propertiesTab = Find<TabItem>(window, "PropertiesTab");
            await ClickInspectorTabAsync(window, propertiesTab);
            Qa.Check(!viewModel.IsOutputPreviewVisible && editorSurface.IsEffectivelyVisible && !editorSurface.IsFocused,
                "일반 inspector 전환이 편집 화면을 복원하거나 editor focus를 탈취하지 않았습니다.");
            Qa.Equal(originalOffset, editorScroll.Offset, "출력 탭에서 나갈 때 편집 scroll offset 보존");
            var structureTab = Find<TabItem>(window, "StructureInspectorTab");
            var libraryTab = Find<TabItem>(window, "WorkspaceLibraryTab");
            await ClickInspectorTabAsync(window, structureTab);
            Qa.Check(!viewModel.IsOutputPreviewVisible && structureTab.IsSelected && !editorSurface.IsFocused,
                "구조 탭 제목 클릭이 편집 focus를 탈취하거나 출력 mode를 유지합니다.");
            var offsetAfterStructureReveal = editorScroll.Offset;
            await ClickInspectorTabAsync(window, libraryTab);
            Qa.Check(!viewModel.IsOutputPreviewVisible && libraryTab.IsSelected && !editorSurface.IsFocused,
                "작업 탭 제목 클릭이 편집 focus를 탈취하거나 출력 mode를 유지합니다.");
            Qa.Equal(offsetAfterStructureReveal, editorScroll.Offset, "일반 작업 탭 전환에서 편집 scroll offset 보존");
            await ClickInspectorTabAsync(window, propertiesTab);
            Qa.Check(!viewModel.IsOutputPreviewVisible && propertiesTab.IsSelected && !editorSurface.IsFocused,
                "속성 탭 제목 클릭이 편집 focus를 탈취하거나 출력 mode를 유지합니다.");
            Qa.Equal(offsetAfterStructureReveal, editorScroll.Offset, "속성 탭 복귀에서 편집 scroll offset 보존");
            await ClickInspectorTabAsync(window, outputTab);
            Qa.Check(viewModel.IsOutputPreviewVisible && !editorSurface.IsEffectivelyVisible && !editorSurface.IsFocused,
                "출력 inspector 탭 합성 클릭이 읽기 전용 mode와 focus 경계를 유지하지 못했습니다.");
            Qa.Equal(offsetAfterStructureReveal, editorScroll.Offset, "출력 탭 재진입 때 편집 scroll offset 보존");
            Qa.Check(editorScroll.Offset != default, "실제 nonzero 편집 offset이 출력 탭 왕복에서 0으로 초기화되었습니다.");
            AssertSessionState(initialSessionState, originalSession, "일반 inspector 탭 왕복 뒤 원본 작업 불변");

            window.Width = 1360;
            window.Height = 880;
            window.InvalidateMeasure();
            window.InvalidateArrange();
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "1360×880 출력 재배치 후");
            RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "output-ready-default");
            AssertSessionState(initialSessionState, originalSession, "창 크기 변경 뒤 원본 작업 불변");

            var displayChoice = viewModel.OutputPreview.ModeChoices.Single(choice => choice.Mode == MathOutputMode.Display);
            await SelectModeAsync(window, modeCombo, displayChoice);
            Qa.Equal(MathOutputPreviewState.Stale, viewModel.OutputPreview.State,
                "Display 모드 선택이 기존 snapshot을 stale로 표시하지 않음");
            Qa.Check(!saveButton.IsEffectivelyEnabled && viewModel.OutputPreview.CanExportSvg == false,
                "stale Display 미리보기의 SVG 저장이 비활성화되지 않았습니다.");
            await ClickAsync(window, refreshButton);
            Qa.Equal(MathOutputPreviewState.Ready, viewModel.OutputPreview.State, "Display 출력 갱신 상태");
            Qa.Equal(MathOutputMode.Display, viewModel.OutputPreview.PreviewSnapshot?.Mode, "Display snapshot mode");
            Qa.Check(ReferenceEquals(previewCanvas.Layout, viewModel.OutputPreview.PreviewSnapshot?.Layout),
                "Display 화면이 고정 preview snapshot layout을 사용하지 않습니다.");
            AssertSessionState(initialSessionState, originalSession, "Display 미리보기와 갱신 뒤 원본 작업 불변");
            RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "output-display-default");

            var inlineChoice = viewModel.OutputPreview.ModeChoices.Single(choice => choice.Mode == MathOutputMode.Inline);
            await SelectModeAsync(window, modeCombo, inlineChoice);
            Qa.Equal(MathOutputPreviewState.Stale, viewModel.OutputPreview.State,
                "Inline 모드 선택이 기존 snapshot을 stale로 표시하지 않음");
            await ClickAsync(window, refreshButton);
            Qa.Equal(MathOutputPreviewState.Ready, viewModel.OutputPreview.State, "Inline 출력 갱신 상태");
            Qa.Equal(MathOutputMode.Inline, viewModel.OutputPreview.PreviewSnapshot?.Mode, "Inline snapshot mode");
            AssertSessionState(initialSessionState, originalSession, "Inline 미리보기와 갱신 뒤 원본 작업 불변");
            RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "output-inline-default");

            var svgPath = Path.Combine(temporaryRoot, "preview-inline.svg");
            var export = await viewModel.OutputPreview.ExportSvgToPathAsync(svgPath);
            Qa.Equal(SvgPreviewExportStatus.Saved, export.Status, "picker-free 고정 preview SVG 저장 결과");
            Qa.Check(File.Exists(svgPath) && (await File.ReadAllTextAsync(svgPath)).Contains("<svg", StringComparison.OrdinalIgnoreCase),
                "picker-free SVG 저장 파일이 생성되지 않았습니다.");
            AssertSessionState(initialSessionState, originalSession, "SVG 출력 저장 뒤 원본 작업 불변");

            viewModel.AddRowCommand.Execute(null);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "출력 mode에서 작업 변경 후");
            Qa.Equal(MathOutputPreviewState.Stale, viewModel.OutputPreview.State,
                "편집 Session 변경이 현재 출력 snapshot을 stale로 만들지 않았습니다.");
            Qa.Check(viewModel.IsOutputPreviewVisible && !editorSurface.IsEffectivelyVisible && !editorSurface.IsFocused,
                "출력 mode에서 편집 명령의 EditorFocusRequested가 숨은 editor를 focus했습니다.");
            Qa.Check(!saveButton.IsEffectivelyEnabled && !viewModel.OutputPreview.CanExportSvg,
                "편집 후 stale SVG 출력이 저장 가능 상태를 유지합니다.");
            window.Width = 1060;
            window.Height = 700;
            window.InvalidateMeasure();
            window.InvalidateArrange();
            await DrainLayoutAsync(window);
            Qa.Check(Find<TextBlock>(window, "OutputPreviewStaleReason").IsEffectivelyVisible,
                "stale 안내가 출력 mode에서 보이지 않습니다.");
            RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "output-stale-minimum");

            await ClickAsync(window, refreshButton);
            await DrainLayoutAsync(window);
            Qa.Equal(MathOutputPreviewState.Incomplete, viewModel.OutputPreview.State,
                "빈 행이 있는 편집 후 갱신된 미리보기의 미완성 상태");
            Qa.Check(!viewModel.OutputPreview.CanExportSvg && !saveButton.IsEffectivelyEnabled,
                "필수 빈 슬롯이 있는데 SVG 저장이 활성화되었습니다.");
            Qa.Check(Find<TextBlock>(window, "OutputPreviewDiagnostic").IsEffectivelyVisible,
                "불완전 출력 진단이 화면에 표시되지 않습니다.");
            RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "output-incomplete-minimum");

            var sessionAfterEdit = viewModel.Session;
            var stateAfterEdit = CaptureSessionState(sessionAfterEdit);
            await ClickAsync(window, Find<Button>(window, "OutputPreviewReturnToEditorButton"));
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "명시 편집 복귀 후");
            Qa.Check(!viewModel.IsOutputPreviewVisible && !OutputPreviewSurfaceVisible(window) && editorSurface.IsEffectivelyVisible,
                "편집 복귀 동작이 editor/preview surface visibility를 복원하지 않았습니다.");
            Qa.Check(ReferenceEquals(viewModel.Session, sessionAfterEdit) && ReferenceEquals(Find<MathEditorControl>(window, "MathEditorSurface"), editorSurface),
                "미리보기 왕복 중 편집 Session 또는 MathEditorControl instance가 교체되었습니다.");
            Qa.Check(ReferenceEquals(inspector.SelectedItem, Find<TabItem>(window, "PropertiesTab")),
                "명시 편집 복귀가 마지막 non-output inspector tab을 복원하지 않았습니다.");
            Qa.Check(editorSurface.IsFocused, "명시 편집 복귀에서만 editor focus가 복원되어야 합니다.");
            AssertSessionState(stateAfterEdit, sessionAfterEdit, "명시 편집 복귀 후 같은 Session 상태 유지");
            AssertCaretVisible(window, sessionAfterEdit);

            await ClickInspectorTabAsync(window, outputTab);
            Qa.Check(viewModel.IsOutputPreviewVisible && ReferenceEquals(inspector.SelectedItem, outputTab),
                "출력 inspector tab 직접 선택이 preview mode로 동기화되지 않았습니다.");
            Qa.Check(!editorSurface.IsFocused,
                "단순 inspector 탭 전환이 편집 surface로 keyboard focus를 강제로 옮겼습니다.");
            AssertSessionState(stateAfterEdit, sessionAfterEdit, "출력 tab 재진입 뒤 같은 작업 상태 유지");
            await ClickInspectorTabAsync(window, propertiesTab);
            Qa.Check(!viewModel.IsOutputPreviewVisible && editorSurface.IsEffectivelyVisible,
                "출력 tab을 떠날 때 편집 surface가 복원되지 않았습니다.");
            Qa.Check(!editorSurface.IsFocused,
                "단순 inspector tab 전환이 editor focus를 탈취했습니다.");
        }
        finally
        {
            try
            {
                await CloseOwnedWindowAsync(_window);
            }
            finally
            {
                _window = null;
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static async Task SelectModeAsync(MainWindow window, ComboBox comboBox, MathOutputModeChoice choice)
    {
        comboBox.SelectedItem = choice;
        await DrainLayoutAsync(window);
        var viewModel = (MainWindowViewModel)window.DataContext!;
        Qa.Equal(choice.Mode, viewModel.OutputPreview.SelectedMode,
            "출력 조판 모드 선택의 UI→ViewModel binding");
        Qa.Check(ReferenceEquals(comboBox.SelectedItem, viewModel.OutputPreview.SelectedModeChoice),
            "출력 조판 모드 ComboBox의 선택이 stable choice proxy와 동기화되지 않았습니다.");
    }

    private static async Task ClickInspectorTabAsync(MainWindow window, TabItem tab)
    {
        EnsureStubHandle(window, "Inspector 탭 클릭 전");
        var headerText = tab.Header?.ToString()
            ?? throw new InvalidOperationException("Inspector 탭 header가 문자열이 아닙니다.");
        var header = tab.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.Text == headerText && text.IsEffectivelyVisible)
            ?? throw new InvalidOperationException($"'{headerText}' 탭의 실제 클릭 가능한 제목을 찾지 못했습니다.");
        Qa.Check(tab.IsEffectivelyEnabled && header.Bounds.Width > 0 && header.Bounds.Height > 0,
            $"'{headerText}' 탭 제목이 표시되지 않거나 비활성입니다.");
        var origin = header.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, $"'{headerText}' 탭 제목의 창 좌표를 가져오지 못했습니다.");
        var point = origin!.Value + new Point(header.Bounds.Width / 2, header.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "Inspector 탭 합성 클릭 직전");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "Inspector 탭 합성 클릭 뒤");
        Qa.Check(tab.IsSelected, $"'{headerText}' 탭 제목 클릭으로 해당 Inspector 탭이 선택되지 않았습니다.");
    }

    private static void AssertCaretVisible(Window window, EditingSession session)
    {
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        var viewer = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(),
            new MathLayoutOptions(FontSize: session.Workspace.Style.FontSize));
        var node = layout.Nodes.Single(node => node.NodeId == session.Caret.NodeId);
        var stop = node.CaretStops.Single(stop => stop.TextElementOffset == session.Caret.TextElementOffset);
        var top = editor.TranslatePoint(new Point(stop.Position.X, node.Bounds.Y), viewer);
        var bottom = editor.TranslatePoint(new Point(stop.Position.X, node.Bounds.Bottom), viewer);
        Qa.Check(top is Point && bottom is Point, "명시적 복귀 후 caret 경계를 편집 viewport로 변환하지 못했습니다.");
        var topPoint = top!.Value;
        var bottomPoint = bottom!.Value;
        var viewport = new Rect(0, 0, viewer.Viewport.Width, viewer.Viewport.Height);
        const double caretStrokeWidth = 1.4;
        const double antialiasSlack = 0.2;
        var caretBounds = new Rect(topPoint.X - (caretStrokeWidth / 2 + antialiasSlack),
            Math.Min(topPoint.Y, bottomPoint.Y), caretStrokeWidth + antialiasSlack * 2,
            Math.Abs(bottomPoint.Y - topPoint.Y));
        Qa.Check(viewport.Contains(caretBounds.TopLeft) && viewport.Contains(caretBounds.BottomRight),
            $"명시적 편집 복귀 후 활성 caret가 실제 viewport 밖에 있습니다: caret={caretBounds}; viewport={viewport}; offset={viewer.Offset}.");
    }

    private static async Task ClickAsync(Window window, Button button)
    {
        Qa.Check(button.IsEffectivelyEnabled, "비활성 출력 미리보기 UI 버튼 합성 클릭 시도");
        var origin = button.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "출력 미리보기 버튼의 창 좌표를 가져오지 못했습니다.");
        var point = origin!.Value + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static SessionState CaptureSessionState(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(snapshot.Workspace), snapshot.Revision, snapshot.StateId,
            session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
            session.IsDirty, session.CanUndo, session.CanRedo);
    }

    private static void AssertSessionState(SessionState expected, EditingSession session, string label)
    {
        var actual = CaptureSessionState(session);
        Qa.Equal(expected, actual, label);
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
            $"{phase}: STUB/0 headless handle을 확인하지 못해 출력 미리보기 UI 동작을 중단했습니다.");
    }

    private static async Task CloseOwnedWindowAsync(MainWindow? window)
    {
        if (window is null)
            return;

        if (window.DataContext is not MainWindowViewModel viewModel)
            throw new InvalidOperationException("출력 미리보기 child가 소유한 MainWindowViewModel을 찾지 못했습니다.");

        await viewModel.StopRecoveryAsync();
        if (window.IsVisible)
            window.Close();
        if (viewModel.IsPendingWorkspaceActionVisible &&
            viewModel.PendingWorkspaceAction == WorkspaceActionKind.CloseWindow)
            viewModel.DiscardPendingWorkspaceAction();

        for (var attempt = 0; attempt < 30 && window.IsVisible; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(5);
        }

        if (window.IsVisible)
            throw new InvalidOperationException("headless child 소유 창이 종료 승인 뒤에도 열려 있습니다.");

        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || (handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero),
            "종료된 출력 미리보기 child 창에서 native platform handle이 발견되었습니다.");
        await viewModel.StopRecoveryAsync();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool OutputPreviewSurfaceVisible(MainWindow window) =>
        Find<Border>(window, "OutputPreviewSurface").IsVisible;

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name} 컨트롤을 찾지 못했습니다.");

    private sealed record SessionState(
        string WorkspaceFingerprint,
        long Revision,
        Guid StateId,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        bool IsDirty,
        bool CanUndo,
        bool CanRedo);
}
