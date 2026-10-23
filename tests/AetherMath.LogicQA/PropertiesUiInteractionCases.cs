using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class PropertiesUiInteractionCases
{
    private const string CaseId = "PROPERTIES-UI";
    private const string ArtifactScope = "p2-properties";
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "속성 패널 4개 범위의 실제 바인딩, 초안·stale·적용·상속 초기화와 셸 레이아웃 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 속성 패널 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("속성 패널 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
        startInfo.ArgumentList.Add("--properties-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("속성 패널 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("속성 패널 UI Headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"속성 패널 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("속성 패널 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var viewModel = new MainWindowViewModel();
        var session = viewModel.Session;
        var targetText = Qa.Nodes(session.Workspace).OfType<TextNode>()
            .FirstOrDefault(text => Qa.Elements(text.Text) >= 2)
            ?? throw new InvalidOperationException("속성 UI fixture에 부분 선택 가능한 TextNode가 없습니다.");
        Qa.Select(session, targetText.Id, 0, 1);
        session.MarkSaved(session.CaptureSnapshot());

        var window = new MainWindow(viewModel)
        {
            Width = 1060,
            Height = 700,
            CanResize = true
        };
        _ownedWindow = window;
        EnsureStubHandle(window, "Show 전");
        window.Show();
        EnsureStubHandle(window, "Show 후");
        await DrainLayoutAsync(window);

        var inspector = Find<TabControl>(window, "EquationInspector");
        var propertiesTab = Find<TabItem>(window, "PropertiesTab");
        inspector.SelectedItem = propertiesTab;
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "속성 탭 초기화 후");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Check(ReferenceEquals(editor.Session, session), "속성 UI가 현재 Session과 다른 editor Session을 사용합니다.");
        Qa.Check(viewModel.Properties.SelectionNotice.Contains("텍스트", StringComparison.Ordinal) &&
                 viewModel.Properties.SelectionNotice.Contains("전체", StringComparison.Ordinal),
            "부분 텍스트 선택에서도 적용 대상이 해당 텍스트 전체임을 알리지 않습니다.");
        AssertUserFacingText(viewModel.Properties.SelectionNotice, "부분 텍스트 선택 안내", session.Workspace);

        AssertBindings(window, viewModel);
        await VerifyScopeTwoWayAndBrowseDoesNotEditAsync(window, viewModel);

        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        SetScrollEdge(window, scroll, top: true);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-initial-minimum-top", ArtifactScope);
        SetScrollEdge(window, scroll, top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-initial-minimum-bottom", ArtifactScope);

        await VerifyInvalidDraftAsync(window, viewModel);
        await VerifyStaleDraftAsync(window, viewModel, targetText.Id);
        await VerifyApplyResetUndoAsync(window, viewModel, targetText.Id);
        await VerifyWorkspaceDefaultUnsupportedResetAsync(window, viewModel);

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "기본 크기 재배치 후");
        SetScrollEdge(window, scroll, top: true);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-source-default-top", ArtifactScope);
        SetScrollEdge(window, scroll, top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-source-default-bottom", ArtifactScope);
        Qa.Check(ReferenceEquals(session, viewModel.Session) && ReferenceEquals(session, editor.Session),
            "속성 패널 탐색 또는 크기 변경이 Session을 교체했습니다.");
    }

    private static void AssertBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        EnsureStubHandle(window, "속성 command binding 검증 전");
        var properties = viewModel.Properties;
        Qa.Equal(4, properties.ScopeChoices.Length, "속성 적용 범위 선택지 개수");
        Qa.Equal(MathPropertyScope.SelectionNode, properties.ScopeChoices[0].Scope, "첫 번째 속성 범위");
        Qa.Equal(MathPropertyScope.CurrentRow, properties.ScopeChoices[1].Scope, "두 번째 속성 범위");
        Qa.Equal(MathPropertyScope.CurrentGroup, properties.ScopeChoices[2].Scope, "세 번째 속성 범위");
        Qa.Equal(MathPropertyScope.WorkspaceDefault, properties.ScopeChoices[3].Scope, "네 번째 속성 범위");

        var scopeBox = Find<ComboBox>(window, "PropertyScopeComboBox");
        Qa.Check(scopeBox.ItemsSource is not null, "속성 scope ComboBox에 실제 항목이 연결되지 않았습니다.");
        Qa.Equal(properties.SelectedScope, (MathPropertyScope)(scopeBox.SelectedValue
            ?? throw new InvalidOperationException("초기 속성 scope가 선택되지 않았습니다.")),
            "초기 scope ComboBox 양방향 바인딩");
        Qa.Check(!string.IsNullOrWhiteSpace(properties.ScopeChoices[0].Description),
            "속성 scope의 설명이 비어 있습니다.");
        Qa.Check(Find<TextBlock>(window, "PropertyBlankMeansUnchangedNotice").Text?.Contains("빈칸", StringComparison.Ordinal) == true,
            "빈 draft 필드가 변경하지 않음을 알리는 안내가 없습니다.");

        var apply = Find<Button>(window, "ApplyPropertiesButton");
        var resetDraft = Find<Button>(window, "ResetPropertiesDraftButton");
        var resetOverride = Find<Button>(window, "ResetPropertiesOverrideButton");
        Qa.Check(ReferenceEquals(apply.Command, properties.ApplyCommand), "적용 버튼이 실제 Properties.ApplyCommand를 사용하지 않습니다.");
        Qa.Check(ReferenceEquals(resetDraft.Command, properties.ResetDraftCommand), "초안 되돌리기 버튼 binding이 다릅니다.");
        Qa.Check(ReferenceEquals(resetOverride.Command, properties.ResetOverrideCommand), "상속 초기화 버튼 binding이 다릅니다.");
        Qa.Equal(properties.CanApply, apply.IsEffectivelyEnabled, "초기 적용 가능 상태");
        Qa.Equal(properties.CanResetOverride, resetOverride.IsEffectivelyEnabled, "초기 상속 초기화 가능 상태");
        Qa.Check(ToolTip.GetShowOnDisabled(apply) && ToolTip.GetShowOnDisabled(resetOverride),
            "비활성 속성 동작의 이유/설명을 disabled 상태에서도 표시해야 합니다.");
        Qa.Equal(viewModel.Session.CanUndo, Find<Button>(window, "UndoButton").IsEffectivelyEnabled,
            "속성 UI 초기화 뒤 undo 표시 상태");
    }

    private static async Task VerifyScopeTwoWayAndBrowseDoesNotEditAsync(
        MainWindow window, MainWindowViewModel viewModel)
    {
        var session = viewModel.Session;
        var baseline = CaptureSession(session);
        var scopeBox = Find<ComboBox>(window, "PropertyScopeComboBox");
        foreach (var choice in viewModel.Properties.ScopeChoices)
        {
            EnsureStubHandle(window, "속성 범위 선택 전");
            scopeBox.SelectedValue = choice.Scope;
            await DrainLayoutAsync(window);
            Qa.Equal(choice.Scope, viewModel.Properties.SelectedScope,
                "실제 ComboBox.SelectedValue에서 Properties.SelectedScope로의 양방향 binding");
            Qa.Equal(choice.Scope, (MathPropertyScope)(scopeBox.SelectedValue
                ?? throw new InvalidOperationException("속성 scope 선택이 null로 초기화되었습니다.")),
                "Properties scope 선택에서 ComboBox 표시값으로의 binding");
            Qa.Check(!string.IsNullOrWhiteSpace(viewModel.Properties.TargetSummary),
                "선택한 범위의 실제 대상 요약이 비어 있습니다: " + choice.Scope);
            AssertUserFacingText(viewModel.Properties.TargetSummary, "속성 적용 대상 " + choice.Scope, session.Workspace);
            Qa.Check(!string.IsNullOrWhiteSpace(viewModel.Properties.FontSizeSourceLabel) &&
                     !string.IsNullOrWhiteSpace(viewModel.Properties.FontFamilySourceLabel) &&
                     !string.IsNullOrWhiteSpace(viewModel.Properties.ForegroundSourceLabel),
                "선택한 범위의 세 필드 상속 출처가 표시되지 않습니다: " + choice.Scope);
            Qa.Equal(viewModel.Properties.EffectiveFontSize.ToString("0.########",
                    System.Globalization.CultureInfo.InvariantCulture),
                viewModel.Properties.DraftFontSizeText,
                "수정하지 않은 draft가 pristine scope 변경 뒤 effective 크기를 다시 읽지 않음: " + choice.Scope);
            Qa.Equal(viewModel.Properties.EffectiveFontFamily, viewModel.Properties.DraftFontFamily,
                "수정하지 않은 draft가 pristine scope 변경 뒤 effective 글꼴을 다시 읽지 않음: " + choice.Scope);
            Qa.Equal(viewModel.Properties.EffectiveForeground, viewModel.Properties.DraftForeground,
                "수정하지 않은 draft가 pristine scope 변경 뒤 effective 색을 다시 읽지 않음: " + choice.Scope);
            Qa.Check(!viewModel.Properties.CanApply,
                "pristine scope 변경 뒤 손대지 않은 draft가 Apply 가능 상태: " + choice.Scope);
            if (choice.Scope == MathPropertyScope.WorkspaceDefault)
            {
                Qa.Equal<double?>(session.Workspace.Style.FontSize, viewModel.Properties.ExplicitFontSize,
                    "WorkspaceDefault 크기를 상속/null로 표시");
                Qa.Equal<string?>(session.Workspace.Style.FontFamily, viewModel.Properties.ExplicitFontFamily,
                    "WorkspaceDefault 글꼴을 상속/null로 표시");
                Qa.Equal<string?>(session.Workspace.Style.Foreground, viewModel.Properties.ExplicitForeground,
                    "WorkspaceDefault 색을 상속/null로 표시");
            }
            AssertUserFacingText(viewModel.Properties.FontSizeSourceLabel, "글자 크기 출처 " + choice.Scope, session.Workspace);
            AssertUserFacingText(viewModel.Properties.FontFamilySourceLabel, "수학 글꼴 출처 " + choice.Scope, session.Workspace);
            AssertUserFacingText(viewModel.Properties.ForegroundSourceLabel, "수식 색 출처 " + choice.Scope, session.Workspace);
            AssertSessionUnchanged(baseline, session, "속성 범위 browse " + choice.Scope);
        }

        scopeBox.SelectedValue = MathPropertyScope.SelectionNode;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.SelectionNode, viewModel.Properties.SelectedScope,
            "편집 검증 전 SelectionNode 범위 복구");
        Qa.Equal(baseline.Selection, session.Selection, "scope 순환 뒤 부분 선택 유지");
    }

    private static async Task VerifyInvalidDraftAsync(MainWindow window, MainWindowViewModel viewModel)
    {
        var session = viewModel.Session;
        var baseline = CaptureSession(session);
        var sizeBox = Find<TextBox>(window, "FontSizeDraftTextBox");
        var colorBox = Find<TextBox>(window, "ForegroundDraftTextBox");
        await ReplaceTextAsync(window, sizeBox, "513");
        await ReplaceTextAsync(window, colorBox, "#GG0000");
        Qa.Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "FontSizeValidationError").Text),
            "범위 밖 글자 크기 초안에 검증 오류가 없습니다.");
        Qa.Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "ForegroundValidationError").Text),
            "잘못된 색상 초안에 검증 오류가 없습니다.");
        Qa.Check(!viewModel.Properties.CanApply && !Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "잘못된 속성 초안이 Apply를 비활성화하지 않았습니다.");
        AssertSessionUnchanged(baseline, session, "잘못된 초안 입력");
        SetScrollEdge(window, Find<ScrollViewer>(window, "PropertiesScrollViewer"), top: true);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-invalid-minimum-top", ArtifactScope);

        await ReplaceTextAsync(window, sizeBox, "1e300");
        Qa.Check(!viewModel.Properties.CanApply && !Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "1e300 크기 초안이 유효한 속성 적용으로 취급됩니다.");
        AssertSessionUnchanged(baseline, session, "1e300 크기 초안");
        EnsureStubHandle(window, "1e300 속성 draft 입력 뒤");

        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        Qa.Check(!viewModel.Properties.IsDraftStale && !viewModel.Properties.CanApply,
            "초안 되돌리기가 invalid 초안 오류/변경 상태를 정리하지 못했습니다.");
        AssertSessionUnchanged(baseline, session, "invalid 초안 되돌리기");

        await ReplaceTextAsync(window, sizeBox, string.Empty);
        await ReplaceTextAsync(window, Find<TextBox>(window, "FontFamilyDraftTextBox"), string.Empty);
        await ReplaceTextAsync(window, colorBox, string.Empty);
        Qa.Check(!viewModel.Properties.CanApply && !Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "세 속성 초안을 모두 비웠을 때 Apply가 비활성화되지 않았습니다.");
        AssertSessionUnchanged(baseline, session, "빈 속성 초안 입력");
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
    }

    private static async Task VerifyStaleDraftAsync(MainWindow window, MainWindowViewModel viewModel, Guid originalTextId)
    {
        var session = viewModel.Session;
        var sizeBox = Find<TextBox>(window, "FontSizeDraftTextBox");
        await ReplaceTextAsync(window, sizeBox, "37");
        var preservedText = viewModel.Properties.DraftFontSizeText;
        var changedText = Qa.Nodes(session.Workspace).OfType<TextNode>()
            .FirstOrDefault(text => text.Id != originalTextId && Qa.Elements(text.Text) > 0)
            ?? throw new InvalidOperationException("stale 초안 검증용 다른 활성 TextNode가 없습니다.");
        Qa.Check(session.TrySetCaret(new MathCaret(changedText.Id, 0)), "다른 target으로 stale 검증 caret 변경 실패");
        await DrainLayoutAsync(window);
        Qa.Check(viewModel.Properties.IsDraftStale && !viewModel.Properties.CanApply,
            "편집 중인 속성 초안이 target 변경 후 stale/적용 차단 상태가 되지 않았습니다.");
        Qa.Equal(preservedText, viewModel.Properties.DraftFontSizeText, "target 변경 뒤 수정 중 draft 문자열 보존");
        Qa.Equal(preservedText, Find<TextBox>(window, "FontSizeDraftTextBox").Text ?? string.Empty,
            "stale 화면 TextBox 값 보존");
        Qa.Check(!Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "stale 초안에 Apply 버튼이 실제로 활성화되어 있습니다.");
        SetScrollEdge(window, Find<ScrollViewer>(window, "PropertiesScrollViewer"), top: true);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-stale-minimum-top", ArtifactScope);
        SetScrollEdge(window, Find<ScrollViewer>(window, "PropertiesScrollViewer"), top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-stale-minimum-bottom", ArtifactScope);
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        Qa.Check(!viewModel.Properties.IsDraftStale, "명시적 초안 되돌리기 뒤 stale 상태가 남았습니다.");
        Qa.Equal(viewModel.Properties.EffectiveFontSize.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
            viewModel.Properties.DraftFontSizeText, "초안 되돌리기가 현재 target 크기를 다시 읽음");
        Qa.Equal(changedText.Id, session.Caret.NodeId, "초안 재불러오기가 현재 target selection을 바꾸지 않음");
    }

    private static async Task VerifyApplyResetUndoAsync(MainWindow window, MainWindowViewModel viewModel, Guid nodeId)
    {
        var session = viewModel.Session;
        _ = Qa.Leaf(session, nodeId);
        Qa.Select(session, nodeId, 0, 1);
        await DrainLayoutAsync(window);
        var beforeDraft = CaptureSession(session);

        await ReplaceTextAsync(window, Find<TextBox>(window, "FontSizeDraftTextBox"), "42");
        await ReplaceTextAsync(window, Find<TextBox>(window, "FontFamilyDraftTextBox"), "Arial");
        await ReplaceTextAsync(window, Find<TextBox>(window, "ForegroundDraftTextBox"), "#80A1B2C3");
        Qa.Equal("42", viewModel.Properties.DraftFontSizeText, "font-size TextBox→VM draft binding");
        Qa.Equal("Arial", viewModel.Properties.DraftFontFamily, "font-family TextBox→VM draft binding");
        Qa.Equal("#80A1B2C3", viewModel.Properties.DraftForeground, "foreground TextBox→VM draft binding");
        Qa.Check(viewModel.Properties.CanApply && Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "올바른 변경 초안에서 실제 Apply 버튼이 활성화되지 않았습니다.");
        AssertSessionUnchanged(beforeDraft, session, "속성 draft typing은 session 불변");

        await ClickButtonAsync(window, "ApplyPropertiesButton");
        Qa.Equal(beforeDraft.Revision + 1, session.Revision, "Properties Apply가 한 번의 commit을 생성");
        var appliedText = Qa.Leaf(session, nodeId);
        Qa.Equal(new MathStyleOverride(42, "Arial", "#80A1B2C3"), appliedText.StyleOverride,
            "선택 TextNode 전체에 세 속성 적용 및 alpha 색 보존");
        Qa.Equal(beforeDraft.Caret, session.Caret, "Properties Apply의 caret 보존");
        Qa.Equal(beforeDraft.Selection, session.Selection, "Properties Apply의 부분 선택 보존");
        Qa.Equal(beforeDraft.SelectedNodeId, session.SelectedNodeId, "Properties Apply의 active node 보존");
        Qa.Equal(beforeDraft.IsNodeSelected, session.IsNodeSelected, "Properties Apply의 선택 모드 보존");
        Qa.Check(session.CanUndo && !session.CanRedo, "Properties Apply 뒤 undo history가 한 단계가 아닙니다.");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Equal(beforeDraft.Workspace, Qa.Fingerprint(session.Workspace), "속성 Apply undo가 원래 override를 복원");
        Qa.Equal(beforeDraft.Caret, session.Caret, "Properties undo caret 보존");
        Qa.Equal(beforeDraft.Selection, session.Selection, "Properties undo selection 보존");
        Qa.Equal(beforeDraft.SelectedNodeId, session.SelectedNodeId, "Properties undo selected node 보존");
        Qa.Equal(beforeDraft.IsNodeSelected, session.IsNodeSelected, "Properties undo selection mode 보존");
        Qa.Check(session.CanRedo, "Properties undo 뒤 redo가 활성화되지 않았습니다.");

        var rowBox = Find<ComboBox>(window, "PropertyScopeComboBox");
        rowBox.SelectedValue = MathPropertyScope.CurrentRow;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.CurrentRow, viewModel.Properties.SelectedScope, "row scope UI binding");
        var beforeRowDraft = CaptureSession(session);
        await ReplaceTextAsync(window, Find<TextBox>(window, "ForegroundDraftTextBox"), "#80FF2040");
        AssertSessionUnchanged(beforeRowDraft, session, "row scope draft 입력");
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        Qa.Equal(beforeRowDraft.Revision + 1, session.Revision, "row scope 적용 단일 commit");
        Qa.Equal(new MathStyleOverride(null, null, "#80FF2040"), session.Workspace.Groups[0].Rows[0].StyleOverride,
            "row scope 실제 EquationRow에 foreground만 override하고 null patch 필드를 보존");
        Qa.Check(viewModel.Properties.CanResetOverride &&
                 Find<Button>(window, "ResetPropertiesOverrideButton").IsEffectivelyEnabled,
            "명시 row override가 상속 초기화를 활성화하지 않았습니다.");
        await ClickButtonAsync(window, "ResetPropertiesOverrideButton");
        Qa.Equal<MathStyleOverride?>(null, session.Workspace.Groups[0].Rows[0].StyleOverride,
            "상속 초기화가 선택 scope의 모든 명시 스타일을 제거");
        Qa.Check(session.CanUndo, "상속 초기화가 undo 가능한 commit을 만들지 않았습니다.");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Equal(new MathStyleOverride(null, null, "#80FF2040"), session.Workspace.Groups[0].Rows[0].StyleOverride,
            "상속 초기화 undo가 명시 스타일을 복원");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Equal<MathStyleOverride?>(null, session.Workspace.Groups[0].Rows[0].StyleOverride,
            "row 속성 적용 undo가 원래 명시값을 복원");

        var groupBox = Find<ComboBox>(window, "PropertyScopeComboBox");
        groupBox.SelectedValue = MathPropertyScope.CurrentGroup;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.CurrentGroup, viewModel.Properties.SelectedScope, "group scope UI binding");
        if (!session.TryCaptureMathPropertyTarget(MathPropertyScope.CurrentGroup, out var groupTarget) ||
            groupTarget?.GroupId is not Guid groupId)
            throw new InvalidOperationException("속성 UI에서 현재 그룹 적용 대상을 캡처하지 못했습니다.");
        var beforeGroupDraft = CaptureSession(session);
        await ReplaceTextAsync(window, Find<TextBox>(window, "FontSizeDraftTextBox"), "35");
        AssertSessionUnchanged(beforeGroupDraft, session, "group scope draft 입력");
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        Qa.Equal(beforeGroupDraft.Revision + 1, session.Revision, "group scope 적용 단일 commit");
        Qa.Equal(new MathStyleOverride(35, null, null),
            session.Workspace.Groups.Single(group => group.Id == groupId).StyleOverride,
            "group scope 실제 EquationGroup override");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Equal<MathStyleOverride?>(null,
            session.Workspace.Groups.Single(group => group.Id == groupId).StyleOverride,
            "group scope 적용 undo");

        var workspaceBox = Find<ComboBox>(window, "PropertyScopeComboBox");
        workspaceBox.SelectedValue = MathPropertyScope.WorkspaceDefault;
        await DrainLayoutAsync(window);
        var beforeWorkspaceDraft = CaptureSession(session);
        var oldWorkspaceStyle = session.Workspace.Style;
        await ReplaceTextAsync(window, Find<TextBox>(window, "ForegroundDraftTextBox"), "#FF445566");
        AssertSessionUnchanged(beforeWorkspaceDraft, session, "workspace default draft 입력");
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        Qa.Equal(beforeWorkspaceDraft.Revision + 1, session.Revision,
            "workspace default scope 적용 단일 commit");
        Qa.Equal(oldWorkspaceStyle with { Foreground = "#FF445566" }, session.Workspace.Style,
            "workspace 기본 색만 변경하고 null patch 필드 보존");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Equal(oldWorkspaceStyle, session.Workspace.Style, "workspace default 적용 undo");
        Qa.Equal(nodeId, Qa.Leaf(session, nodeId).Id,
            "속성 Apply/Undo가 실제 TextNode 식별자를 변경했습니다.");
    }

    private static async Task VerifyWorkspaceDefaultUnsupportedResetAsync(MainWindow window,
        MainWindowViewModel viewModel)
    {
        var session = viewModel.Session;
        var baseline = CaptureSession(session);
        var scopeBox = Find<ComboBox>(window, "PropertyScopeComboBox");
        scopeBox.SelectedValue = MathPropertyScope.WorkspaceDefault;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.WorkspaceDefault, viewModel.Properties.SelectedScope,
            "작업 기본값 scope 선택");
        Qa.Check(!viewModel.Properties.CanResetOverride &&
                 !Find<Button>(window, "ResetPropertiesOverrideButton").IsEffectivelyEnabled,
            "작업 기본값에서 상속 초기화가 비활성화되어 있지 않습니다.");
        Qa.Check(!string.IsNullOrWhiteSpace(viewModel.Properties.ResetOverrideDisabledReason) &&
                 !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "ResetPropertiesOverrideDisabledReason").Text),
            "작업 기본값의 상속 초기화 비활성 이유가 화면에 없습니다.");
        Qa.Equal(viewModel.Properties.ResetOverrideDisabledReason,
            ToolTip.GetTip(Find<Button>(window, "ResetPropertiesOverrideButton"))?.ToString(),
            "작업 기본값 상속 초기화의 실제 tooltip 사유");
        AssertSessionUnchanged(baseline, session, "작업 기본값 scope 탐색");

        SetScrollEdge(window, Find<ScrollViewer>(window, "PropertiesScrollViewer"), top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "properties-source-default-minimum-bottom", ArtifactScope);
    }

    private static async Task ReplaceTextAsync(Window window, TextBox textBox, string text)
    {
        EnsureStubHandle(window, "속성 draft 입력 전");
        await BringControlIntoViewAsync(window, textBox);
        Qa.Check(IsFullyVisible(window, textBox), "속성 입력 대상이 실제 viewport에서 잘립니다: " + textBox.Name);
        textBox.Focus();
        textBox.SelectAll();
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        if (text.Length > 0)
            window.KeyTextInput(text);
        await DrainLayoutAsync(window);
        Qa.Equal(text, textBox.Text ?? string.Empty, "합성 키 입력 뒤 속성 TextBox 내용");
    }

    private static async Task ClickButtonAsync(MainWindow window, string name) =>
        await ClickControlAsync(window, Find<Button>(window, name));

    private static async Task ClickControlAsync(MainWindow window, Control control)
    {
        EnsureStubHandle(window, "속성 UI 합성 클릭 전");
        Qa.Check(control.IsEffectivelyEnabled, "비활성 속성 control에 합성 클릭을 시도했습니다: " + control.Name);
        await BringControlIntoViewAsync(window, control);
        var origin = control.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "속성 합성 클릭 대상의 창 좌표를 얻지 못했습니다: " + control.Name);
        Qa.Check(IsFullyVisible(window, control), "속성 합성 클릭 대상이 스크롤 viewport 안에 완전히 표시되지 않습니다: " + control.Name);
        var point = origin!.Value + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "속성 합성 pointer 입력 중");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static async Task BringControlIntoViewAsync(Window window, Control control)
    {
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var origin = control.TranslatePoint(new Point(0, 0), scroll);
            Qa.Check(origin is Point, "속성 스크롤 내부 대상 좌표를 얻지 못했습니다: " + control.Name);
            var top = origin!.Value.Y;
            var bottom = top + control.Bounds.Height;
            var offset = scroll.Offset.Y;
            if (top < 0)
                offset += top;
            else if (bottom > scroll.Viewport.Height)
                offset += bottom - scroll.Viewport.Height;
            else
                continue;
            var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(offset, 0, maximum));
            await DrainLayoutAsync(window);
        }
    }

    private static bool IsFullyVisible(Window window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        if (origin is not Point)
            return false;
        var bounds = new Rect(origin!.Value, control.Bounds.Size);
        var client = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
        if (!Contains(client, bounds))
            return false;
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var scrollOrigin = scroll.TranslatePoint(new Point(0, 0), window);
            if (scrollOrigin is not Point || !Contains(new Rect(scrollOrigin!.Value, scroll.Viewport), bounds))
                return false;
        }
        return control.IsEffectivelyVisible;
    }

    private static void SetScrollEdge(Window window, ScrollViewer scroll, bool top)
    {
        EnsureStubHandle(window, "속성 ScrollViewer 이동 전");
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, top ? 0 : maximum);
    }

    private static SessionState CaptureSession(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(session.Workspace), snapshot.StateId, snapshot.Revision,
            session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
            session.IsDirty, session.CanUndo, session.CanRedo);
    }

    private static void AssertSessionUnchanged(SessionState expected, EditingSession actual, string label)
    {
        var current = CaptureSession(actual);
        Qa.Equal(expected.Workspace, current.Workspace, label + " workspace");
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
            $"{phase}: 실제 창 입력 전에 Headless STUB/0 handle이어야 합니다.");
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
        EnsureStubHandle(window, "소유 속성 Headless 창 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "속성 UI가 소유한 Headless 창을 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "속성 UI child 종료 뒤 실제/native 창 handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static void AssertUserFacingText(string value, string label, EquationWorkspace fixture)
    {
        var fixtureIds = fixture.Groups.Select(group => group.Id)
            .Concat(fixture.Groups.SelectMany(group => group.Rows.Select(row => row.Id)))
            .Concat(Qa.Nodes(fixture).Select(node => node.Id))
            .Append(fixture.Id)
            .Distinct();
        var containsFixtureId = fixtureIds.Any(id =>
        {
            var compact = id.ToString("N");
            return value.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
                   value.Contains(compact, StringComparison.OrdinalIgnoreCase) ||
                   value.Contains(compact[..8], StringComparison.OrdinalIgnoreCase);
        });
        Qa.Check(!value.Contains("TextNode", StringComparison.Ordinal) && !containsFixtureId,
            label + "에 내부 타입명 또는 Guid 조각이 노출됩니다: " + value);
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

    private sealed record SessionState(
        string Workspace,
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
