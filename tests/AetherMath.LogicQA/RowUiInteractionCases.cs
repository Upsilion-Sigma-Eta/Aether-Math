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
using AetherMath.Layout;

namespace AetherMath.LogicQA;

internal static class RowUiInteractionCases
{
    private const string CaseId = "ROW-UI";
    private static MainWindow? _window;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "실제 App/MainWindow 바인딩·행 버튼·undo/redo와 두 크기 셸 및 긴 문서 caret 접근성의 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId != CaseId)
        {
            Console.Error.WriteLine("알 수 없는 행 UI headless 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("행 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
        startInfo.ArgumentList.Add("--row-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("행 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("행 UI headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"행 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("행 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
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

        AssertEditorAndCommands(window, viewModel);
        RowShellVisualCases.CaptureAndVerify(window, "shell-minimum");

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1360×880 재배치 후");
        RowShellVisualCases.CaptureAndVerify(window, "shell-default");

        var session = viewModel.Session;
        var addButton = Find<Button>(window, "AddRowButton");
        var duplicateButton = Find<Button>(window, "DuplicateRowButton");
        var deleteButton = Find<Button>(window, "DeleteRowButton");
        var moveUpButton = Find<Button>(window, "MoveRowUpButton");
        var moveDownButton = Find<Button>(window, "MoveRowDownButton");
        var originalRows = session.Workspace.Groups[0].Rows.Select(row => row.Id).ToArray();
        var initialWorkspace = Qa.Fingerprint(session.Workspace);

        Qa.Check(!Find<Button>(window, "MoveRowUpButton").IsEffectivelyEnabled,
            "첫 행에서 위 이동 버튼이 enabled");
        Qa.Check(addButton.IsEffectivelyEnabled && duplicateButton.IsEffectivelyEnabled &&
                 deleteButton.IsEffectivelyEnabled && moveDownButton.IsEffectivelyEnabled,
            "초기 행 도구 CanExecute와 Button enabled 배선 불일치");
        AssertIndicator(window, viewModel);
        AssertRowToolTips(window, viewModel, deleteEnabled: true, upEnabled: false, downEnabled: true);

        await ClickAsync(window, addButton);
        Qa.Equal(3, session.Workspace.Groups[0].Rows.Length, "실제 Add 버튼의 행 수");
        Qa.Equal(2, viewModel.ActiveRowNumber, "실제 Add 버튼의 활성 행 바인딩");
        Qa.Equal(3, viewModel.ActiveRowCount, "실제 Add 버튼의 행 수 바인딩");
        AssertIndicator(window, viewModel);
        var addedText = session.Workspace.Groups[0].Rows[1].Root as TextNode;
        Qa.Check(addedText is { IsEmptySlot: true, Text.Length: 0 }, "Add 버튼의 필수 빈 슬롯");
        Qa.Equal(new MathCaret(addedText!.Id, 0), session.Caret, "Add 버튼 뒤 caret");
        Qa.Check(window.FindControl<MathEditorControl>("MathEditorSurface")!.IsFocused,
            "행 Add 후 headless managed editor focus 복원");

        await ClickAsync(window, duplicateButton);
        Qa.Equal(4, session.Workspace.Groups[0].Rows.Length, "실제 Duplicate 버튼의 행 수");
        Qa.Equal(3, viewModel.ActiveRowNumber, "실제 Duplicate 버튼의 활성 행");
        AssertIndicator(window, viewModel);
        var duplicateId = session.Workspace.Groups[0].Rows[2].Id;
        Qa.Check(duplicateId != session.Workspace.Groups[0].Rows[1].Id, "중복 버튼이 새 row ID를 만들지 않음");

        await ClickAsync(window, moveUpButton);
        Qa.Equal(2, viewModel.ActiveRowNumber, "실제 Move Up 버튼의 활성 행");
        AssertIndicator(window, viewModel);
        Qa.Equal(duplicateId, session.Workspace.Groups[0].Rows[1].Id, "실제 Move Up 버튼의 행 교환");
        await ClickAsync(window, moveDownButton);
        Qa.Equal(3, viewModel.ActiveRowNumber, "실제 Move Down 버튼의 활성 행");
        AssertIndicator(window, viewModel);
        Qa.Equal(duplicateId, session.Workspace.Groups[0].Rows[2].Id, "실제 Move Down 버튼의 행 복귀");

        var rowsBeforeDelete = session.Workspace.Groups[0].Rows.Length;
        await ClickAsync(window, deleteButton);
        Qa.Equal(rowsBeforeDelete - 1, session.Workspace.Groups[0].Rows.Length, "실제 Delete 버튼의 행 수");
        Qa.Equal(3, viewModel.ActiveRowNumber, "행 삭제 뒤 다음 행 활성화");
        Qa.Check(window.FindControl<MathEditorControl>("MathEditorSurface")!.IsFocused,
            "행 Delete 후 headless managed editor focus 복원");
        Qa.Check(!string.Equals(initialWorkspace, Qa.Fingerprint(session.Workspace), StringComparison.Ordinal),
            "실제 row command가 workspace에 반영되지 않음");

        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        Qa.Equal(rowsBeforeDelete, session.Workspace.Groups[0].Rows.Length, "실제 Ctrl+Z가 행 삭제를 undo");
        Qa.Equal(3, viewModel.ActiveRowNumber, "Ctrl+Z 뒤 복원된 활성 행");
        SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
        Qa.Equal(rowsBeforeDelete - 1, session.Workspace.Groups[0].Rows.Length, "실제 Ctrl+Y가 행 삭제를 redo");
        AssertIndicator(window, viewModel);
        Qa.Check(originalRows.All(id => session.Workspace.Groups[0].Rows.Any(row => row.Id == id)),
            "행 도구 조작 중 원래 행 ID 손실");

        await ClickAsync(window, deleteButton);
        Qa.Equal(2, session.Workspace.Groups[0].Rows.Length, "두 번째 삭제 후 두 행");
        await ClickAsync(window, deleteButton);
        Qa.Equal(1, session.Workspace.Groups[0].Rows.Length, "그룹 마지막 행까지 삭제");
        AssertRowToolTips(window, viewModel, deleteEnabled: false, upEnabled: false, downEnabled: false);
        AssertIndicator(window, viewModel);

        // 실제 UI 경로로 충분히 긴 문서를 만든 다음 맨 아래 긴 행 끝 caret가 viewport 안에 보이는지 확인한다.
        for (var index = 0; index < 17; index++)
            await ClickAsync(window, addButton);
        Qa.Check(session.Workspace.Groups[0].Rows.Length >= 18, "긴 문서 행 수가 충분하지 않음");
        Qa.Equal(session.Workspace.Groups[0].Rows.Length, viewModel.ActiveRowNumber,
            "반복 추가 뒤 맨 아래 행이 활성화되지 않음");
        Qa.Check(window.FindControl<MathEditorControl>("MathEditorSurface")!.IsFocused,
            "반복 추가 뒤 headless editor focus 누락");

        var longText = new string('x', 512);
        window.KeyTextInput(longText);
        await DrainLayoutAsync(window);
        Qa.Check(session.Workspace.Groups[0].Rows[^1].Root is TextNode lastText && lastText.Text == longText,
            "headless text input이 맨 아래 빈 슬롯에 입력되지 않음");
        Qa.Equal(longText.Length, session.Caret.TextElementOffset, "긴 행 끝의 활성 caret offset");
        var viewer = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var longLayout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(),
            new MathLayoutOptions(FontSize: session.Workspace.Style.FontSize));
        Qa.Check(longLayout.Width > viewer.Viewport.Width,
            $"테스트 텍스트가 실제 viewport보다 길지 않습니다: layout={longLayout.Width:R}, viewport={viewer.Viewport.Width:R}.");

        var rowsBeforeClone = session.Workspace.Groups[0].Rows.Length;
        await ClickAsync(window, duplicateButton);
        Qa.Equal(rowsBeforeClone + 1, session.Workspace.Groups[0].Rows.Length, "긴 행 복제 뒤 row 수");
        Qa.Equal(longText.Length, session.Caret.TextElementOffset, "긴 행 복제 뒤 caret offset 보존");
        Qa.Equal(longText, Qa.Leaf(session, session.Caret.NodeId).Text, "긴 행 복제 뒤 caret 대상 text");
        AssertActiveCaretVisible(window, session);
        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        Qa.Equal(rowsBeforeClone, session.Workspace.Groups[0].Rows.Length, "긴 행 복제 Ctrl+Z");
        AssertActiveCaretVisible(window, session);
        SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
        Qa.Equal(rowsBeforeClone + 1, session.Workspace.Groups[0].Rows.Length, "긴 행 복제 Ctrl+Y");
        AssertActiveCaretVisible(window, session);

        await ClickAsync(window, Find<Button>(window, "UndoButton"));
        Qa.Equal(rowsBeforeClone, session.Workspace.Groups[0].Rows.Length, "toolbar Undo가 긴 행 복제를 취소");
        AssertActiveCaretVisible(window, session);
        await ClickAsync(window, Find<Button>(window, "RedoButton"));
        Qa.Equal(rowsBeforeClone + 1, session.Workspace.Groups[0].Rows.Length, "toolbar Redo가 긴 행 복제를 복원");
        AssertActiveCaretVisible(window, session);
    }

    private static void AssertEditorAndCommands(MainWindow window, MainWindowViewModel viewModel)
    {
        var bindings = new (string Name, ICommand Command)[]
        {
            ("AddRowButton", viewModel.AddRowCommand),
            ("DuplicateRowButton", viewModel.DuplicateRowCommand),
            ("DeleteRowButton", viewModel.DeleteRowCommand),
            ("MoveRowUpButton", viewModel.MoveRowUpCommand),
            ("MoveRowDownButton", viewModel.MoveRowDownCommand)
        };
        foreach (var (name, command) in bindings)
        {
            var button = Find<Button>(window, name);
            Qa.Check(ReferenceEquals(button.Command, command), $"{name}가 실제 ViewModel command에 연결되지 않음");
        Qa.Equal(name, AutomationProperties.GetAutomationId(button) ?? string.Empty, $"{name} AutomationId");
            Qa.Equal(command.CanExecute(null), button.IsEffectivelyEnabled, $"{name} CanExecute/IsEffectivelyEnabled");
        }

        var indicator = Find<Border>(window, "ActiveRowIndicator");
        Qa.Equal("ActiveRowIndicator", AutomationProperties.GetAutomationId(indicator) ?? string.Empty, "활성 행 표시 AutomationId");
        Qa.Equal(1, viewModel.ActiveRowNumber, "실제 MainWindow 초기 활성 행 binding");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Equal("MathEditorSurface", AutomationProperties.GetAutomationId(editor) ?? string.Empty, "MathEditorSurface AutomationId");
        Qa.Check(ReferenceEquals(editor.Session, viewModel.Session), "MainWindow editor와 ViewModel Session 불일치");
    }

    private static void AssertIndicator(MainWindow window, MainWindowViewModel viewModel)
    {
        var indicator = Find<Border>(window, "ActiveRowIndicator");
        var numbers = indicator.GetVisualDescendants().OfType<TextBlock>()
            .Select(textBlock => textBlock.Text)
            .Where(text => int.TryParse(text, out _))
            .ToArray();
        var expected = new[]
        {
            viewModel.ActiveGroupNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            viewModel.ActiveRowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            viewModel.ActiveRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        Qa.Check(numbers.SequenceEqual(expected),
            $"활성 행 표시의 실제 text binding 불일치: 기대=[{string.Join(",", expected)}], 실제=[{string.Join(",", numbers)}].");
    }

    private static void AssertRowToolTips(
        MainWindow window, MainWindowViewModel viewModel, bool deleteEnabled, bool upEnabled, bool downEnabled)
    {
        var delete = Find<Button>(window, "DeleteRowButton");
        var up = Find<Button>(window, "MoveRowUpButton");
        var down = Find<Button>(window, "MoveRowDownButton");
        Qa.Equal(deleteEnabled, delete.IsEffectivelyEnabled, "삭제 버튼 경계 effectively enabled");
        Qa.Equal(upEnabled, up.IsEffectivelyEnabled, "위 이동 버튼 경계 effectively enabled");
        Qa.Equal(downEnabled, down.IsEffectivelyEnabled, "아래 이동 버튼 경계 effectively enabled");
        Qa.Equal(viewModel.DeleteRowToolTip, ToolTip.GetTip(delete)?.ToString(), "삭제 disabled tooltip");
        Qa.Equal(viewModel.MoveRowUpToolTip, ToolTip.GetTip(up)?.ToString(), "위 이동 disabled tooltip");
        Qa.Equal(viewModel.MoveRowDownToolTip, ToolTip.GetTip(down)?.ToString(), "아래 이동 disabled tooltip");
        Qa.Check(ToolTip.GetShowOnDisabled(delete) && ToolTip.GetShowOnDisabled(up) &&
                 ToolTip.GetShowOnDisabled(down),
            "disabled 행 도구 tooltip을 비활성 상태에서 표시하지 않음");
    }

    private static async Task ClickAsync(Window window, Button button)
    {
        Qa.Check(button.IsEffectivelyEnabled, "비활성 행 버튼 합성 클릭 시도");
        var origin = button.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "행 버튼 중심의 창 좌표를 가져오지 못함");
        var point = origin!.Value + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static void SendShortcut(Window window, Key key, PhysicalKey physicalKey, string symbol)
    {
        window.KeyPress(key, RawInputModifiers.Control, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertActiveCaretVisible(Window window, EditingSession session)
    {
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        var viewer = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(),
            new MathLayoutOptions(FontSize: session.Workspace.Style.FontSize));
        var node = layout.Nodes.Single(node => node.NodeId == session.Caret.NodeId);
        var stop = node.CaretStops.Single(stop => stop.TextElementOffset == session.Caret.TextElementOffset);
        var top = editor.TranslatePoint(new Point(stop.Position.X, node.Bounds.Y), viewer);
        var bottom = editor.TranslatePoint(new Point(stop.Position.X, node.Bounds.Bottom), viewer);
        Qa.Check(top is Point && bottom is Point, "DrawCaret의 실제 위·아래 경계를 ScrollViewer 좌표로 변환하지 못함");
        var caretTop = top!.Value;
        var caretBottom = bottom!.Value;
        var viewport = new Rect(0, 0, viewer.Viewport.Width, viewer.Viewport.Height);
        const double caretStrokeWidth = 1.4;
        const double antialiasSlack = 0.2;
        var caretBounds = new Rect(caretTop.X - (caretStrokeWidth / 2 + antialiasSlack),
            Math.Min(caretTop.Y, caretBottom.Y), caretStrokeWidth + antialiasSlack * 2,
            Math.Abs(caretBottom.Y - caretTop.Y));
        Qa.Check(viewport.Contains(caretBounds.TopLeft) && viewport.Contains(caretBounds.BottomRight),
            $"맨 아래 긴 행 끝 caret가 편집 viewport 밖에 있습니다. caret={caretBounds}; viewport={viewport}; offset={viewer.Offset}.");
    }

    private static async Task DrainLayoutAsync(Window window)
    {
        for (var i = 0; i < 5; i++)
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
        if (window is null)
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
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");
}
