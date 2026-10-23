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

internal static class GroupUiInteractionCases
{
    private const string CaseId = "GROUP-UI";
    private static MainWindow? _window;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "실제 App/MainWindow 그룹·행 도구 바인딩, 합성 편집, undo/redo 및 긴 작업영역의 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId != CaseId)
        {
            Console.Error.WriteLine("알 수 없는 그룹 UI headless 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("그룹 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
        startInfo.ArgumentList.Add("--group-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("그룹 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("그룹 UI headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"그룹 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("그룹 UI child가 stderr를 기록했습니다:\n" + stderr);
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

        var session = viewModel.Session;
        Qa.Equal(1, session.GroupCount, "기본 workspace 그룹 수");
        Qa.Equal(2, session.Workspace.Groups[0].Rows.Length, "기본 workspace 첫 그룹 행 수");
        Qa.Equal(1, viewModel.ActiveGroupNumber, "기본 workspace 활성 그룹 번호");
        Qa.Equal(1, viewModel.ActiveRowNumber, "기본 workspace 활성 행 번호");
        AssertEditorAndCommands(window, viewModel);
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: false, upEnabled: false, downEnabled: false);
        AssertRowToolTips(window, viewModel, deleteEnabled: true, upEnabled: false, downEnabled: true);
        RowShellVisualCases.CaptureAndVerify(window, "group-shell-initial-minimum", "p1-group-editing");

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1360×880 재배치 후");
        RowShellVisualCases.CaptureAndVerify(window, "group-shell-initial-default", "p1-group-editing");

        var originalGroupId = session.Workspace.Groups[0].Id;
        var originalRowIds = session.Workspace.Groups[0].Rows.Select(row => row.Id).ToArray();
        await ClickAsync(window, Find<Button>(window, "AddGroupButton"));
        Qa.Equal(2, session.GroupCount, "그룹 추가 뒤 그룹 수");
        Qa.Equal(2, viewModel.ActiveGroupNumber, "그룹 추가 뒤 활성 그룹");
        Qa.Equal(originalGroupId, session.Workspace.Groups[0].Id, "그룹 추가가 앞 그룹 위치를 변경함");
        Qa.Check(originalRowIds.SequenceEqual(session.Workspace.Groups[0].Rows.Select(row => row.Id)),
            "그룹 추가가 원래 그룹의 행 ID를 변경함");
        var addedGroupId = session.Workspace.Groups[1].Id;
        Qa.Check(addedGroupId != originalGroupId, "그룹 추가가 새 그룹 ID를 만들지 않음");
        Qa.Equal(1, session.Workspace.Groups[1].Rows.Length, "새 그룹의 초기 행 수");
        Qa.Check(session.Workspace.Groups[1].Rows[0].Root is TextNode { IsEmptySlot: true, Text.Length: 0 },
            "새 그룹의 필수 빈 슬롯이 생성되지 않음");
        Qa.Check(Find<MathEditorControl>(window, "MathEditorSurface").IsFocused,
            "그룹 추가 후 managed editor focus가 복원되지 않음");
        AssertEditorAndCommands(window, viewModel);
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: true, upEnabled: true, downEnabled: false);
        AssertRowToolTips(window, viewModel, deleteEnabled: false, upEnabled: false, downEnabled: false);

        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        await DrainLayoutAsync(window);
        Qa.Equal(1, session.GroupCount, "Ctrl+Z가 그룹 추가를 취소하지 않음");
        Qa.Equal(originalGroupId, session.Workspace.Groups[0].Id, "그룹 추가 undo 뒤 원 그룹 ID");
        Qa.Check(session.CanRedo, "그룹 추가 Ctrl+Z 뒤 redo가 없음");
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: false, upEnabled: false, downEnabled: false);
        SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
        await DrainLayoutAsync(window);
        Qa.Equal(2, session.GroupCount, "Ctrl+Y가 그룹 추가를 복원하지 않음");
        Qa.Equal(addedGroupId, session.Workspace.Groups[1].Id, "그룹 추가 redo 뒤 새 그룹 ID");
        AssertIndicators(window, viewModel);

        await ClickAsync(window, Find<Button>(window, "MoveGroupUpButton"));
        Qa.Equal(1, viewModel.ActiveGroupNumber, "그룹 위 이동 뒤 활성 그룹 번호");
        Qa.Check(session.Workspace.Groups.Select(group => group.Id).SequenceEqual([addedGroupId, originalGroupId]),
            "그룹 위 이동이 인접 그룹 순서를 바꾸지 않음");
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: true, upEnabled: false, downEnabled: true);

        await ClickAsync(window, Find<Button>(window, "UndoButton"));
        Qa.Check(session.Workspace.Groups.Select(group => group.Id).SequenceEqual([originalGroupId, addedGroupId]),
            "toolbar Undo가 그룹 이동을 되돌리지 않음");
        Qa.Equal(2, viewModel.ActiveGroupNumber, "toolbar Undo 뒤 활성 그룹");
        AssertIndicators(window, viewModel);
        await ClickAsync(window, Find<Button>(window, "RedoButton"));
        Qa.Check(session.Workspace.Groups.Select(group => group.Id).SequenceEqual([addedGroupId, originalGroupId]),
            "toolbar Redo가 그룹 이동을 복원하지 않음");
        Qa.Equal(1, viewModel.ActiveGroupNumber, "toolbar Redo 뒤 활성 그룹");
        AssertIndicators(window, viewModel);

        await ClickAsync(window, Find<Button>(window, "DuplicateGroupButton"));
        Qa.Equal(3, session.GroupCount, "그룹 복제 뒤 그룹 수");
        Qa.Equal(2, viewModel.ActiveGroupNumber, "그룹 복제 뒤 활성 그룹 번호");
        var clonedGroupId = session.Workspace.Groups[1].Id;
        Qa.Check(clonedGroupId != addedGroupId, "복제 그룹이 원 그룹 ID를 재사용함");
        Qa.Equal(session.Workspace.Groups[0].Rows.Length, session.Workspace.Groups[1].Rows.Length,
            "복제 그룹의 행 수가 원 그룹과 다름");
        Qa.Check(session.Workspace.Groups[0].Rows.Select(row => row.Id)
                .Intersect(session.Workspace.Groups[1].Rows.Select(row => row.Id)).Any() == false,
            "복제 그룹이 원 그룹의 행 ID를 재사용함");
        AssertEditorAndCommands(window, viewModel);
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: true, upEnabled: true, downEnabled: true);

        await ClickAsync(window, Find<Button>(window, "MoveGroupDownButton"));
        Qa.Equal(3, viewModel.ActiveGroupNumber, "그룹 아래 이동 뒤 활성 그룹 번호");
        Qa.Check(session.Workspace.Groups.Select(group => group.Id).SequenceEqual([addedGroupId, originalGroupId, clonedGroupId]),
            "그룹 아래 이동이 활성 그룹 ID를 보존하지 않음");
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: true, upEnabled: true, downEnabled: false);

        var inactiveFirst = session.Workspace.Groups[0];
        var inactiveSecond = session.Workspace.Groups[1];
        Qa.Equal(1, session.Workspace.Groups[2].Rows.Length, "활성 복제 그룹의 행 수");
        await ClickAsync(window, Find<Button>(window, "AddRowButton"));
        Qa.Equal(2, session.Workspace.Groups[2].Rows.Length, "행 추가가 활성 그룹에 반영되지 않음");
        Qa.Check(ReferenceEquals(inactiveFirst, session.Workspace.Groups[0]) &&
                 ReferenceEquals(inactiveSecond, session.Workspace.Groups[1]),
            "활성 그룹 행 추가가 비활성 그룹을 수정함");
        Qa.Equal(3, viewModel.ActiveGroupNumber, "행 도구 사용 뒤 활성 그룹이 변경됨");
        Qa.Equal(2, viewModel.ActiveRowNumber, "그룹 복제의 행 도구 추가 뒤 활성 행");
        AssertIndicators(window, viewModel);
        AssertRowToolTips(window, viewModel, deleteEnabled: true, upEnabled: true, downEnabled: false);
        Qa.Check(Find<MathEditorControl>(window, "MathEditorSurface").IsFocused,
            "활성 그룹의 행 추가 후 managed editor focus가 복원되지 않음");

        await ClickAsync(window, Find<Button>(window, "DeleteGroupButton"));
        Qa.Equal(2, session.GroupCount, "그룹 삭제 뒤 그룹 수");
        Qa.Check(session.Workspace.Groups.All(group => group.Id != clonedGroupId), "그룹 삭제 뒤 복제 그룹이 남음");
        Qa.Equal(2, viewModel.ActiveGroupNumber, "마지막 그룹 삭제 뒤 이웃 그룹 활성화");
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: true, upEnabled: true, downEnabled: false);

        await ClickAsync(window, Find<Button>(window, "DeleteGroupButton"));
        Qa.Equal(1, session.GroupCount, "마지막 그룹 삭제를 거절하지 않음");
        Qa.Equal(1, viewModel.ActiveGroupNumber, "그룹 삭제 뒤 단일 그룹의 활성 번호");
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: false, upEnabled: false, downEnabled: false);

        var addGroupButton = Find<Button>(window, "AddGroupButton");
        for (var index = 0; index < 16; index++)
            await ClickAsync(window, addGroupButton);
        Qa.Check(session.GroupCount >= 17, "긴 작업영역 검증에 필요한 그룹 수가 부족함");
        Qa.Equal(session.GroupCount, viewModel.ActiveGroupNumber, "반복 그룹 추가 뒤 마지막 그룹이 활성화되지 않음");
        Qa.Equal(1, viewModel.ActiveRowNumber, "새 마지막 그룹의 활성 행 번호");
        AssertEditorAndCommands(window, viewModel);
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: true, upEnabled: true, downEnabled: false);
        Qa.Check(Find<MathEditorControl>(window, "MathEditorSurface").IsFocused,
            "반복 그룹 추가 뒤 managed editor focus가 복원되지 않음");

        var longText = new string('x', 512);
        window.KeyTextInput(longText);
        await DrainLayoutAsync(window);
        var sourceGroup = session.Workspace.Groups[^1];
        Qa.Check(sourceGroup.Rows[0].Root is TextNode sourceText && sourceText.Text == longText,
            "headless 입력이 마지막 그룹의 빈 슬롯에 반영되지 않음");
        Qa.Equal(longText.Length, session.Caret.TextElementOffset, "긴 행 끝 caret offset");
        var viewer = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var longLayout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(),
            new MathLayoutOptions(FontSize: session.Workspace.Style.FontSize));
        Qa.Check(longLayout.Width > viewer.Viewport.Width,
            $"테스트 수식이 viewport보다 길지 않습니다: layout={longLayout.Width:R}, viewport={viewer.Viewport.Width:R}.");

        var groupCountBeforeClone = session.GroupCount;
        var sourceGroupId = sourceGroup.Id;
        var sourceRowId = sourceGroup.Rows[0].Id;
        await ClickAsync(window, Find<Button>(window, "DuplicateGroupButton"));
        await DrainLayoutAsync(window);
        Qa.Equal(groupCountBeforeClone + 1, session.GroupCount, "긴 행 그룹 복제 뒤 그룹 수");
        Qa.Equal(session.GroupCount, viewModel.ActiveGroupNumber, "긴 행 그룹 복제 뒤 복제본 활성화");
        var clonedLongGroup = session.Workspace.Groups[^1];
        Qa.Check(clonedLongGroup.Id != sourceGroupId, "긴 행 그룹 복제가 새 그룹 ID를 만들지 않음");
        Qa.Check(clonedLongGroup.Rows[0].Id != sourceRowId, "긴 행 그룹 복제가 새 행 ID를 만들지 않음");
        Qa.Check(clonedLongGroup.Rows[0].Root is TextNode clonedText && clonedText.Text == longText,
            "긴 행 그룹 복제에서 텍스트가 보존되지 않음");
        Qa.Equal(longText.Length, session.Caret.TextElementOffset, "긴 행 그룹 복제 뒤 caret offset");
        Qa.Equal(longText, Qa.Leaf(session, session.Caret.NodeId).Text, "긴 행 그룹 복제 뒤 caret 대상");
        Qa.Check(Find<MathEditorControl>(window, "MathEditorSurface").IsFocused,
            "긴 행 그룹 복제 뒤 managed editor focus가 복원되지 않음");
        AssertEditorAndCommands(window, viewModel);
        AssertIndicators(window, viewModel);
        AssertGroupToolTips(window, viewModel, deleteEnabled: true, upEnabled: true, downEnabled: false);
        AssertActiveCaretVisible(window, session);
        RowShellVisualCases.CaptureAndVerify(window, "group-shell-many-default", "p1-group-editing");

        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1060×700 재배치 후");

        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        await DrainLayoutAsync(window);
        Qa.Equal(groupCountBeforeClone, session.GroupCount, "1060×700에서 긴 그룹 복제 Ctrl+Z");
        Qa.Equal(sourceGroupId, session.Workspace.Groups[^1].Id, "긴 그룹 undo 뒤 마지막 원본 그룹");
        Qa.Equal(longText.Length, session.Caret.TextElementOffset, "긴 그룹 undo 뒤 caret offset");
        Qa.Equal(longText, Qa.Leaf(session, session.Caret.NodeId).Text, "긴 그룹 undo 뒤 caret 대상");
        AssertIndicators(window, viewModel);
        AssertActiveCaretVisible(window, session);

        SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
        await DrainLayoutAsync(window);
        Qa.Equal(groupCountBeforeClone + 1, session.GroupCount, "1060×700에서 긴 그룹 복제 Ctrl+Y");
        Qa.Equal(clonedLongGroup.Id, session.Workspace.Groups[^1].Id, "긴 그룹 redo 뒤 복제 그룹 ID");
        Qa.Equal(longText.Length, session.Caret.TextElementOffset, "긴 그룹 redo 뒤 caret offset");
        Qa.Equal(longText, Qa.Leaf(session, session.Caret.NodeId).Text, "긴 그룹 redo 뒤 caret 대상");
        AssertIndicators(window, viewModel);
        AssertActiveCaretVisible(window, session);
        RowShellVisualCases.CaptureAndVerify(window, "group-shell-many-minimum", "p1-group-editing");
    }

    private static void AssertEditorAndCommands(MainWindow window, MainWindowViewModel viewModel)
    {
        var bindings = new (string Name, ICommand Command)[]
        {
            ("AddGroupButton", viewModel.AddGroupCommand),
            ("DuplicateGroupButton", viewModel.DuplicateGroupCommand),
            ("DeleteGroupButton", viewModel.DeleteGroupCommand),
            ("MoveGroupUpButton", viewModel.MoveGroupUpCommand),
            ("MoveGroupDownButton", viewModel.MoveGroupDownCommand),
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
            Qa.Equal(command.CanExecute(null), button.IsEffectivelyEnabled,
                $"{name} CanExecute/IsEffectivelyEnabled");
        }

        var groupIndicator = Find<Border>(window, "ActiveGroupIndicator");
        Qa.Equal("ActiveGroupIndicator", AutomationProperties.GetAutomationId(groupIndicator) ?? string.Empty,
            "활성 그룹 표시 AutomationId");
        var rowIndicator = Find<Border>(window, "ActiveRowIndicator");
        Qa.Equal("ActiveRowIndicator", AutomationProperties.GetAutomationId(rowIndicator) ?? string.Empty,
            "활성 행 표시 AutomationId");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Equal("MathEditorSurface", AutomationProperties.GetAutomationId(editor) ?? string.Empty,
            "MathEditorSurface AutomationId");
        Qa.Check(ReferenceEquals(editor.Session, viewModel.Session), "MainWindow editor와 ViewModel Session 불일치");
    }

    private static void AssertIndicators(MainWindow window, MainWindowViewModel viewModel)
    {
        var groupIndicator = Find<Border>(window, "ActiveGroupIndicator");
        var groupNumbers = NumericText(groupIndicator);
        var expectedGroupNumbers = new[]
        {
            viewModel.ActiveGroupNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            viewModel.GroupCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        Qa.Check(groupNumbers.SequenceEqual(expectedGroupNumbers),
            $"활성 그룹 표시 숫자 순서 불일치: 기대=[{string.Join(",", expectedGroupNumbers)}], 실제=[{string.Join(",", groupNumbers)}].");

        var rowIndicator = Find<Border>(window, "ActiveRowIndicator");
        var rowNumbers = NumericText(rowIndicator);
        var expectedRowNumbers = new[]
        {
            viewModel.ActiveGroupNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            viewModel.ActiveRowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            viewModel.ActiveRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        Qa.Check(rowNumbers.SequenceEqual(expectedRowNumbers),
            $"활성 행 표시 숫자 순서 불일치: 기대=[{string.Join(",", expectedRowNumbers)}], 실제=[{string.Join(",", rowNumbers)}].");
    }

    private static string[] NumericText(Control indicator) => indicator.GetVisualDescendants()
        .OfType<TextBlock>()
        .Select(textBlock => textBlock.Text ?? string.Empty)
        .Where(text => int.TryParse(text, out _))
        .ToArray();

    private static void AssertGroupToolTips(
        MainWindow window, MainWindowViewModel viewModel, bool deleteEnabled, bool upEnabled, bool downEnabled)
    {
        var delete = Find<Button>(window, "DeleteGroupButton");
        var up = Find<Button>(window, "MoveGroupUpButton");
        var down = Find<Button>(window, "MoveGroupDownButton");
        Qa.Equal(deleteEnabled, delete.IsEffectivelyEnabled, "그룹 삭제 경계 effectively enabled");
        Qa.Equal(upEnabled, up.IsEffectivelyEnabled, "그룹 위 이동 경계 effectively enabled");
        Qa.Equal(downEnabled, down.IsEffectivelyEnabled, "그룹 아래 이동 경계 effectively enabled");
        Qa.Equal(viewModel.DeleteGroupToolTip, ToolTip.GetTip(delete)?.ToString(), "그룹 삭제 tooltip binding");
        Qa.Equal(viewModel.MoveGroupUpToolTip, ToolTip.GetTip(up)?.ToString(), "그룹 위 이동 tooltip binding");
        Qa.Equal(viewModel.MoveGroupDownToolTip, ToolTip.GetTip(down)?.ToString(), "그룹 아래 이동 tooltip binding");
        Qa.Check(ToolTip.GetShowOnDisabled(delete) && ToolTip.GetShowOnDisabled(up) &&
                 ToolTip.GetShowOnDisabled(down),
            "disabled 그룹 도구의 tooltip이 비활성 상태에서 표시되지 않음");
        if (!deleteEnabled)
            Qa.Check(viewModel.DeleteGroupToolTip.Contains("마지막", StringComparison.Ordinal),
                "그룹 삭제 비활성 사유가 표시되지 않음");
        if (!upEnabled)
            Qa.Check(viewModel.MoveGroupUpToolTip.Contains("첫 번째", StringComparison.Ordinal),
                "그룹 위 이동 비활성 사유가 표시되지 않음");
        if (!downEnabled)
            Qa.Check(viewModel.MoveGroupDownToolTip.Contains("마지막", StringComparison.Ordinal),
                "그룹 아래 이동 비활성 사유가 표시되지 않음");
    }

    private static void AssertRowToolTips(
        MainWindow window, MainWindowViewModel viewModel, bool deleteEnabled, bool upEnabled, bool downEnabled)
    {
        var delete = Find<Button>(window, "DeleteRowButton");
        var up = Find<Button>(window, "MoveRowUpButton");
        var down = Find<Button>(window, "MoveRowDownButton");
        Qa.Equal(deleteEnabled, delete.IsEffectivelyEnabled, "행 삭제 effectively enabled");
        Qa.Equal(upEnabled, up.IsEffectivelyEnabled, "행 위 이동 effectively enabled");
        Qa.Equal(downEnabled, down.IsEffectivelyEnabled, "행 아래 이동 effectively enabled");
        Qa.Equal(viewModel.DeleteRowToolTip, ToolTip.GetTip(delete)?.ToString(), "행 삭제 tooltip binding");
        Qa.Equal(viewModel.MoveRowUpToolTip, ToolTip.GetTip(up)?.ToString(), "행 위 이동 tooltip binding");
        Qa.Equal(viewModel.MoveRowDownToolTip, ToolTip.GetTip(down)?.ToString(), "행 아래 이동 tooltip binding");
        Qa.Check(ToolTip.GetShowOnDisabled(delete) && ToolTip.GetShowOnDisabled(up) &&
                 ToolTip.GetShowOnDisabled(down),
            "disabled 행 도구의 tooltip이 비활성 상태에서 표시되지 않음");
    }

    private static async Task ClickAsync(Window window, Button button)
    {
        Qa.Check(button.IsEffectivelyEnabled, "비활성 그룹/행 버튼 합성 클릭 시도");
        var origin = button.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "그룹/행 버튼 중심의 창 좌표를 가져오지 못함");
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
        Qa.Check(top is Point && bottom is Point,
            "DrawCaret의 실제 위·아래 경계를 ScrollViewer 좌표로 변환하지 못함");
        var caretTop = top!.Value;
        var caretBottom = bottom!.Value;
        var viewport = new Rect(0, 0, viewer.Viewport.Width, viewer.Viewport.Height);
        const double caretStrokeWidth = 1.4;
        const double antialiasSlack = 0.2;
        var caretBounds = new Rect(caretTop.X - (caretStrokeWidth / 2 + antialiasSlack),
            Math.Min(caretTop.Y, caretBottom.Y), caretStrokeWidth + antialiasSlack * 2,
            Math.Abs(caretBottom.Y - caretTop.Y));
        Qa.Check(viewport.Contains(caretBounds.TopLeft) && viewport.Contains(caretBounds.BottomRight),
            $"맨 아래 긴 행 끝 DrawCaret가 viewport 밖에 있습니다. caret={caretBounds}; viewport={viewport}; offset={viewer.Offset}.");
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
