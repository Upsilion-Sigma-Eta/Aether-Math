using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class TypesettingUiInteractionCases
{
    private const string CaseId = "TYPESETTING-UI";
    private const string ArtifactScope = "p2-typesetting";
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "기준 조판 스타일의 실제 바인딩, 네 필드 적용·초기화·실행 취소, stale 차단과 같은 수식 렌더 비교를 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 조판 스타일 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("조판 스타일 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
        startInfo.ArgumentList.Add("--typesetting-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("조판 스타일 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("조판 스타일 UI Headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"조판 스타일 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("조판 스타일 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var viewModel = new MainWindowViewModel();
        var session = viewModel.Session;
        using var fixtureDirectory = new TempDirectory();
        var fixturePath = await PrepareOperatorComparisonFixtureAsync(viewModel, fixtureDirectory);
        var originalStyle = session.Workspace.Style;
        var equationFingerprint = FingerprintEquation(session);
        Qa.Check(Qa.Nodes(session.Workspace).OfType<FractionNode>().Any(),
            "동일 수식 조판 표본에 분수가 없습니다.");
        Qa.Check(Qa.Nodes(session.Workspace).OfType<ScriptNode>().Any(),
            "동일 수식 조판 표본에 첨자가 없습니다.");
        Qa.Check(Qa.Nodes(session.Workspace).OfType<SummationNode>().Any(),
            "동일 수식 조판 표본에 채워진 합이 없습니다.");
        Qa.Check(Qa.Nodes(session.Workspace).OfType<IntegralNode>().Any(),
            "동일 수식 조판 표본에 채워진 적분이 없습니다.");
        Qa.Check(Qa.Nodes(session.Workspace).OfType<LimitNode>().Any(),
            "동일 수식 조판 표본에 채워진 극한이 없습니다.");
        Qa.Check(File.Exists(fixturePath), "조판 비교용 실제 작업 파일이 임시 디렉터리에 저장되지 않았습니다.");

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
        Qa.Check(ReferenceEquals(editor.Session, session), "조판 UI가 현재 Session과 다른 editor Session을 사용합니다.");
        AssertBindings(window, viewModel);

        await CapturePanelEdgesAsync(window, "typesetting-initial-minimum");
        await BringTypesettingCardIntoViewAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "typesetting-detail-display-minimum", ArtifactScope);

        var scopeBox = Find<ComboBox>(window, "PropertyScopeComboBox");
        scopeBox.SelectedValue = MathPropertyScope.WorkspaceDefault;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.WorkspaceDefault, viewModel.Properties.SelectedScope,
            "작업 기본값 범위의 실제 ComboBox 양방향 바인딩");
        AssertTargetSummaryMatches(window, viewModel, "작업 기본값 범위 전환");
        var styleBox = Find<ComboBox>(window, "TypesettingStyleComboBox");
        var noChangeBaseline = CaptureSession(session);
        var noChangeChoice = viewModel.Properties.TypesettingStyleChoices.Single(choice => choice.Value is null);
        styleBox.SelectedItem = noChangeChoice;
        await DrainLayoutAsync(window);
        Qa.Equal<MathTypesettingStyle?>(null, viewModel.Properties.DraftTypesettingStyle,
            "변경 안 함 선택의 DraftTypesettingStyle 전달");
        Qa.Check(ReferenceEquals(noChangeChoice, viewModel.Properties.SelectedTypesettingStyleChoice) &&
                 ReferenceEquals(noChangeChoice, styleBox.SelectedItem),
            "nullable 스타일 값 대신 canonical SelectedItem으로 변경 안 함 선택이 표시되지 않습니다.");
        Qa.Check(!viewModel.Properties.CanApply && !Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "조판 스타일 변경 안 함 선택만으로 적용이 활성화되었습니다.");
        AssertSessionUnchanged(noChangeBaseline, session, "조판 스타일 변경 안 함 선택");
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        await DrainLayoutAsync(window);
        Qa.Equal(originalStyle.TypesettingStyle, viewModel.Properties.DraftTypesettingStyle,
            "현재 조판 기준 다시 불러오기");
        AssertTargetSummaryMatches(window, viewModel, "작업 기본값 범위 초안 되돌리기");
        AssertSessionUnchanged(noChangeBaseline, session, "조판 스타일 초안 되돌리기");

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "기본 창 크기 재배치 후");
        await CapturePanelEdgesAsync(window, "typesetting-initial-default");

        scopeBox.SelectedValue = MathPropertyScope.WorkspaceDefault;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.WorkspaceDefault, viewModel.Properties.SelectedScope,
            "동일 수식 비교 전 작업 기본값 범위");
        AssertTargetSummaryMatches(window, viewModel, "동일 수식 비교 전 범위 재선택");
        var artifactDirectory = CreateArtifactDirectory("equation-style-comparison");
        var captures = new List<EquationCapture>();
        var displayCapture = await CaptureEquationAsync(window, MathTypesettingStyle.Display,
            equationFingerprint, artifactDirectory);
        captures.Add(displayCapture);

        foreach (var style in new[]
                 {
                     MathTypesettingStyle.Text,
                     MathTypesettingStyle.Script,
                     MathTypesettingStyle.ScriptScript
                 })
        {
            EnsureStubHandle(window, style + " 조판 초안 선택 전");
            var before = CaptureSession(session);
            await SelectTypesettingStyleAsync(window, viewModel, style);
            Qa.Check(viewModel.Properties.CanApply, style + " 조판 스타일 초안이 적용 가능하지 않습니다.");
            AssertSessionUnchanged(before, session, style + " 조판 기준 초안 입력");

            await ClickButtonAsync(window, "ApplyPropertiesButton");
            await DrainLayoutAsync(window);
            Qa.Equal(before.Revision + 1, session.Revision, style + " 조판 기준 한 번 적용");
            Qa.Equal(originalStyle with { TypesettingStyle = style }, session.Workspace.Style,
                style + " 작업 기본값의 조판 기준만 변경");
            AssertSelectionPreserved(before, session, style + " 조판 기준 적용");
            Qa.Check(ReferenceEquals(session, viewModel.Session) && ReferenceEquals(session, editor.Session),
                style + " 조판 기준 적용이 Session을 교체했습니다.");

            var capture = await CaptureEquationAsync(window, style, equationFingerprint, artifactDirectory);
            captures.Add(capture);
            if (style == MathTypesettingStyle.Text)
            {
                await BringTypesettingCardIntoViewAsync(window);
                RowShellVisualCases.CaptureAndVerify(window, "typesetting-detail-text-default", ArtifactScope);
            }

            await ClickButtonAsync(window, "UndoButton");
            await DrainLayoutAsync(window);
            Qa.Equal(before.Workspace, Qa.Fingerprint(session.Workspace), style + " 조판 적용 undo");
            Qa.Equal(before.Revision + 2, session.Revision, style + " 조판 적용 단일 undo revision");
            AssertSelectionPreserved(before, session, style + " 조판 적용 undo");
            Qa.Equal(originalStyle.TypesettingStyle, viewModel.Properties.DraftTypesettingStyle,
                style + " undo 뒤 초안이 현재 조판 기준으로 다시 읽힘");
        }

        Qa.Check(captures.All(capture => capture.EquationFingerprint == equationFingerprint),
            "조판 비교 중 수식 구조 또는 노드 ID가 변경되었습니다.");
        Qa.Equal(4, captures.Select(capture => capture.PngSha256).Distinct(StringComparer.Ordinal).Count(),
            "같은 수식에서 네 조판 기준의 실제 편집기 렌더가 서로 구분되지 않습니다.");
        WriteComparisonEvidence(artifactDirectory, captures, window, session, originalStyle,
            Path.GetFileName(fixturePath));

        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        scopeBox.SelectedValue = MathPropertyScope.CurrentRow;
        await DrainLayoutAsync(window);
        var originalText = Qa.Nodes(session.Workspace).OfType<TextNode>()
            .FirstOrDefault(text => Qa.Elements(text.Text) >= 2)
            ?? throw new InvalidOperationException("네 필드 적용 표본에 부분 선택 가능한 텍스트가 없습니다.");
        Qa.Select(session, originalText.Id, 0, 1);
        scopeBox.SelectedValue = MathPropertyScope.SelectionNode;
        await DrainLayoutAsync(window);
        AssertTargetSummaryMatches(window, viewModel, "부분 텍스트 선택 및 선택 범위 전환");
        Qa.Check(viewModel.Properties.SelectionNotice.Contains("일부 문자를 선택했습니다", StringComparison.Ordinal) &&
                 viewModel.Properties.SelectionNotice.Contains("텍스트 전체에 스타일을 적용합니다", StringComparison.Ordinal),
            "선택 범위에서 일부 문자를 선택했을 때 스타일 적용 대상이 텍스트 전체임을 알리지 않습니다.");
        scopeBox.SelectedValue = MathPropertyScope.CurrentRow;
        await DrainLayoutAsync(window);
        AssertTargetSummaryMatches(window, viewModel, "행 범위 복귀");

        var row = session.Workspace.Groups[0].Rows[0];
        var rowId = row.Id;
        var combinedBefore = CaptureSession(session);
        await ReplaceTextAsync(window, Find<TextBox>(window, "FontSizeDraftTextBox"), "34");
        await ReplaceTextAsync(window, Find<TextBox>(window, "FontFamilyDraftTextBox"), "Arial");
        await ReplaceTextAsync(window, Find<TextBox>(window, "ForegroundDraftTextBox"), "#80445566");
        await SelectTypesettingStyleAsync(window, viewModel, MathTypesettingStyle.Script);
        Qa.Check(viewModel.Properties.CanApply, "네 필드를 함께 편집한 Properties 초안이 적용 가능하지 않습니다.");
        AssertSessionUnchanged(combinedBefore, session, "네 필드 초안 입력");
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        await DrainLayoutAsync(window);
        var expectedOverride = new MathStyleOverride(34, "Arial", "#80445566")
        {
            TypesettingStyle = MathTypesettingStyle.Script
        };
        var appliedRow = session.Workspace.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.Id == rowId);
        Qa.Equal(combinedBefore.Revision + 1, session.Revision, "네 필드 동시 적용 단일 revision");
        Qa.Equal(expectedOverride, appliedRow.StyleOverride, "행 범위 네 필드 override 동시 적용");
        AssertSelectionPreserved(combinedBefore, session, "네 필드 동시 적용");
        Qa.Check(session.CanUndo && !session.CanRedo, "네 필드 동시 적용이 단일 undo 단계가 아닙니다.");
        await BringTypesettingCardIntoViewAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "typesetting-detail-row-script-minimum", ArtifactScope);

        await ClickButtonAsync(window, "ResetPropertiesOverrideButton");
        await DrainLayoutAsync(window);
        var resetRow = session.Workspace.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.Id == rowId);
        Qa.Equal<MathStyleOverride?>(null, resetRow.StyleOverride, "행 상속 초기화가 네 명시 필드를 모두 제거");
        Qa.Equal(combinedBefore.Revision + 2, session.Revision, "네 필드 override 초기화 단일 revision");
        AssertSelectionPreserved(combinedBefore, session, "네 필드 override 초기화");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Equal(expectedOverride,
            session.Workspace.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.Id == rowId).StyleOverride,
            "네 필드 override 초기화 undo");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Equal<MathStyleOverride?>(null,
            session.Workspace.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.Id == rowId).StyleOverride,
            "네 필드 동시 적용 undo");
        AssertSelectionPreserved(combinedBefore, session, "네 필드 적용/초기화 전체 undo");

        scopeBox.SelectedValue = MathPropertyScope.CurrentRow;
        await DrainLayoutAsync(window);
        await SelectTypesettingStyleAsync(window, viewModel, MathTypesettingStyle.Text);
        var staleBaseline = CaptureSession(session);
        var secondRowText = Qa.Nodes(session.Workspace).OfType<TextNode>().Last();
        Qa.Check(session.TrySetCaret(new MathCaret(secondRowText.Id, 0)), "stale fixture의 다른 행 caret 설정 실패");
        await DrainLayoutAsync(window);
        AssertTargetSummaryMatches(window, viewModel, "활성 행 caret 변경");
        Qa.Equal(session.ActiveRowCount, session.ActiveRowNumber,
            "stale fixture caret가 추가 표본의 마지막 행으로 이동하지 않았습니다.");
        Qa.Check(viewModel.Properties.IsDraftStale && !viewModel.Properties.CanApply &&
                 viewModel.Properties.DraftTypesettingStyle == MathTypesettingStyle.Text,
            "수정 중 조판 초안이 target 변경 뒤 보존되면서 stale 적용을 차단하지 않습니다.");
        Qa.Equal(staleBaseline.Revision, session.Revision, "stale fixture의 caret 이동에서 문서 revision 불변");
        SetScrollEdge(window, Find<ScrollViewer>(window, "PropertiesScrollViewer"), top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "typesetting-stale-minimum-bottom", ArtifactScope);
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        await DrainLayoutAsync(window);
        AssertTargetSummaryMatches(window, viewModel, "stale 초안 되돌리기 후 대상 갱신");
        Qa.Check(!viewModel.Properties.IsDraftStale && !viewModel.Properties.CanApply,
            "초안 되돌리기가 stale 차단을 해제하고 pristine 적용 비활성 상태로 돌아오지 않았습니다.");
        Qa.Equal(viewModel.Properties.EffectiveTypesettingStyle, viewModel.Properties.DraftTypesettingStyle,
            "stale 이후 초안 되돌리기가 현재 대상 조판 기준을 읽지 않았습니다.");
        Qa.Equal(secondRowText.Id, session.Caret.NodeId, "stale 초안 되돌리기에서 현재 선택·caret 보존");
        Qa.Equal(staleBaseline.Workspace, Qa.Fingerprint(session.Workspace), "stale 초안 탐색과 되돌리기 문서 불변");
        Qa.Check(ReferenceEquals(session, viewModel.Session) && ReferenceEquals(session, editor.Session),
            "조판 UI 시나리오 중 실제 Session identity가 바뀌었습니다.");

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        await CapturePanelEdgesAsync(window, "typesetting-final-default");
    }

    private static void AssertBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        EnsureStubHandle(window, "조판 스타일 바인딩 확인 전");
        var properties = viewModel.Properties;
        var combo = Find<ComboBox>(window, "TypesettingStyleComboBox");
        var itemsSource = combo.ItemsSource
            ?? throw new InvalidOperationException("조판 스타일 ComboBox에 실제 선택지가 연결되지 않았습니다.");
        var choices = itemsSource.Cast<MathTypesettingStyleChoice>().ToArray();
        Qa.Equal(5, choices.Length, "조판 스타일 선택지 개수");
        Qa.Equal<MathTypesettingStyle?>(null, choices[0].Value, "첫 번째 조판 선택지는 변경 안 함");
        Qa.Equal(MathTypesettingStyle.Display, choices[1].Value, "독립 수식(Display) 선택지 순서");
        Qa.Equal(MathTypesettingStyle.Text, choices[2].Value, "텍스트 크기(Text) 선택지 순서");
        Qa.Equal(MathTypesettingStyle.Script, choices[3].Value, "첨자(Script) 선택지 순서");
        Qa.Equal(MathTypesettingStyle.ScriptScript, choices[4].Value, "작은 첨자(ScriptScript) 선택지 순서");
        Qa.Check(choices.All(choice => !string.IsNullOrWhiteSpace(choice.DisplayName) &&
                                       !string.IsNullOrWhiteSpace(choice.Description)),
            "조판 스타일 선택지 이름 또는 설명이 비어 있습니다.");
        Qa.Equal("상속", MathTypesettingStyleDisplayConverter.Instance.Convert(
                null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)?.ToString() ?? string.Empty,
            "명시값 null은 변경 안 함 초안이 아니라 상속 상태로 표시");
        foreach (var choice in choices.Skip(1))
        {
            var effectiveLabel = MathTypesettingStyleDisplayConverter.Instance.Convert(
                choice.Value, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)?.ToString() ?? string.Empty;
            Qa.Equal(choice.DisplayName, effectiveLabel,
                "현재/명시값 표시와 조판 선택지 용어 일치: " + choice.Value);
        }
        Qa.Check(choices.Skip(1).All(choice =>
                choice.DisplayName.Contains("(", StringComparison.Ordinal) &&
                choice.DisplayName.Contains(")", StringComparison.Ordinal)),
            "조판 선택지가 한국어 의미와 표준 스타일명을 함께 표시하지 않습니다.");
        Qa.Check(ReferenceEquals(properties.SelectedTypesettingStyleChoice, combo.SelectedItem),
            "기준 조판 초안의 canonical SelectedItem 양방향 바인딩");
        Qa.Check(ReferenceEquals(Find<Button>(window, "ApplyPropertiesButton").Command, properties.ApplyCommand),
            "적용 버튼이 실제 Properties.ApplyCommand를 사용하지 않습니다.");
        Qa.Check(ReferenceEquals(Find<Button>(window, "ResetPropertiesDraftButton").Command, properties.ResetDraftCommand),
            "초안 되돌리기 버튼이 실제 Properties.ResetDraftCommand를 사용하지 않습니다.");
        Qa.Check(ReferenceEquals(Find<Button>(window, "ResetPropertiesOverrideButton").Command, properties.ResetOverrideCommand),
            "상속 초기화 버튼이 실제 Properties.ResetOverrideCommand를 사용하지 않습니다.");
        Qa.Check(Find<TextBlock>(window, "TypesettingStyleEffectiveValue").Text?.Contains("독립 수식 (Display)", StringComparison.Ordinal) == true,
            "현재 조판 스타일을 선택지와 같은 사용자용 한국어 이름으로 표시하지 않습니다.");
        Qa.Check(Find<TextBlock>(window, "TypesettingStyleContextNotice").Text?.Contains("자동", StringComparison.Ordinal) == true,
            "구조 자식의 조판 전이가 자동임을 안내하지 않습니다.");
        Qa.Check(Find<TextBlock>(window, "PropertiesResetOverrideNotice").Text?.Contains("조판 기준", StringComparison.Ordinal) == true,
            "상속 초기화가 조판 기준 명시값도 제거한다는 안내가 없습니다.");
        Qa.Check(ToolTip.GetTip(Find<Button>(window, "ApplyPropertiesButton"))?.ToString()
                     ?.Contains("실행 취소", StringComparison.Ordinal) == true,
            "Properties 적용 안내가 undo 가능한 속성 변경을 설명하지 않습니다.");
    }

    private static void AssertTargetSummaryMatches(MainWindow window, MainWindowViewModel viewModel, string phase)
    {
        var displayed = Find<TextBlock>(window, "PropertyTargetSummary").Text;
        Qa.Check(string.Equals(displayed, viewModel.Properties.TargetSummary, StringComparison.Ordinal),
            phase + "에서 화면 대상 요약이 VM.TargetSummary와 동기화되지 않았습니다. " +
            $"화면='{displayed}', VM='{viewModel.Properties.TargetSummary}'");
    }

    private static async Task SelectTypesettingStyleAsync(
        MainWindow window, MainWindowViewModel viewModel, MathTypesettingStyle style)
    {
        var combo = Find<ComboBox>(window, "TypesettingStyleComboBox");
        await BringControlIntoViewAsync(window, combo);
        EnsureFullyVisibleOrCapture(window, combo, "조판 스타일 ComboBox 선택 전");
        var choice = viewModel.Properties.TypesettingStyleChoices.Single(item => item.Value == style);
        combo.SelectedItem = choice;
        await DrainLayoutAsync(window);
        Qa.Equal<MathTypesettingStyle?>(style, viewModel.Properties.DraftTypesettingStyle,
            "조판 ComboBox → DraftTypesettingStyle 양방향 바인딩: " + style);
        Qa.Check(ReferenceEquals(choice, combo.SelectedItem) &&
                 ReferenceEquals(choice, viewModel.Properties.SelectedTypesettingStyleChoice),
            "DraftTypesettingStyle → canonical SelectedItem 표시값: " + style);
        Qa.Check(!string.IsNullOrWhiteSpace(choice.Description), style + " 구조 전이 설명이 없습니다.");
    }

    private static async Task CapturePanelEdgesAsync(MainWindow window, string prefix)
    {
        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        SetScrollEdge(window, scroll, top: true);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, prefix + "-top", ArtifactScope);
        SetScrollEdge(window, scroll, top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, prefix + "-bottom", ArtifactScope);
    }

    private static async Task BringTypesettingCardIntoViewAsync(MainWindow window)
    {
        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        var controls = new Control[]
        {
            Find<TextBlock>(window, "TypesettingStyleEffectiveValue"),
            Find<TextBlock>(window, "TypesettingStyleExplicitValue"),
            Find<TextBlock>(window, "TypesettingStyleSourceLabel"),
            Find<ComboBox>(window, "TypesettingStyleComboBox"),
            Find<TextBlock>(window, "TypesettingStyleContextNotice")
        };
        var positions = controls.Select(control => control.TranslatePoint(new Point(0, 0), scroll)
                ?? throw new InvalidOperationException("조판 카드 요소의 실제 스크롤 좌표를 얻지 못했습니다."))
            .ToArray();
        var top = positions.Min(point => point.Y);
        var bottom = controls.Select((control, index) => positions[index].Y + control.Bounds.Height).Max();
        Qa.Check(bottom - top <= scroll.Viewport.Height + 1,
            $"조판 스타일 상세가 최소 패널 viewport보다 높습니다: card={bottom - top:R}, viewport={scroll.Viewport.Height:R}.");
        var shift = (top + bottom - scroll.Viewport.Height) / 2;
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + shift, 0, maximum));
        await DrainLayoutAsync(window);
        foreach (var control in controls)
            EnsureFullyVisibleOrCapture(window, control, "조판 카드 노출 확인");
    }

    private static async Task<EquationCapture> CaptureEquationAsync(
        MainWindow window, MathTypesettingStyle style, string equationFingerprint, string directory)
    {
        EnsureStubHandle(window, style + " 실제 편집기 렌더 전");
        await DrainLayoutAsync(window);
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        if (window.DataContext is not MainWindowViewModel viewModel || !ReferenceEquals(editor.Session, viewModel.Session))
            throw new InvalidOperationException("조판 비교 PNG가 현재 실제 MathEditorControl Session에서 생성되지 않습니다.");
        editor.BringCaretIntoView();
        await DrainLayoutAsync(window);
        Qa.Equal(style, viewModel.Session.Workspace.Style.TypesettingStyle,
            style + " 비교 시 실제 workspace 조판 기준");
        var editorBounds = editor.Bounds;
        var width = Math.Max(1, (int)Math.Ceiling(editorBounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(editorBounds.Height));
        Qa.Check(width >= 120 && height >= 100, "실제 editor 렌더 영역이 너무 작습니다.");
        var editorOrigin = editor.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("실제 편집기 창 기준 좌표를 얻지 못했습니다.");

        var pngPath = Path.Combine(directory, $"equation-{style.ToString().ToLowerInvariant()}.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            using (var drawingContext = bitmap.CreateDrawingContext())
            {
                drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                editor.Render(drawingContext);
            }
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var bytes = await File.ReadAllBytesAsync(pngPath);
        Qa.Check(bytes.Length > 64 && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            style + " 조판 비교 렌더가 유효한 PNG가 아닙니다.");
        return new EquationCapture(style.ToString(), Path.GetFileName(pngPath),
            Convert.ToHexString(SHA256.HashData(bytes)), width, height,
            new BoundsEvidence(editorOrigin.X, editorOrigin.Y, editorBounds.Width, editorBounds.Height),
            equationFingerprint, "MathEditorControl.Render의 실제 Session 기반 오프스크린 검토 이미지이며 제품 내보내기가 아닙니다.");
    }

    private static void WriteComparisonEvidence(string directory, IReadOnlyList<EquationCapture> captures,
        MainWindow window, EditingSession session, MathStyle baselineStyle, string fixtureFileName)
    {
        var evidence = new
        {
            caseId = CaseId,
            evidencePurpose = "같은 실제 수식의 조판 기준 비교용 오프스크린 캡처이며 제품 내보내기 출력이 아닙니다.",
            windowSize = new { window.Width, window.Height },
            baselineWorkspaceStyle = baselineStyle,
            fixtureFileName,
            fixtureStorage = "비교 표본은 전용 TempDirectory의 .aethermath 작업 파일로 저장되며 이미지는 검토용 오프스크린 렌더입니다.",
            expressionFingerprint = FingerprintEquation(session),
            fixtureStructures = new
            {
                fractions = Qa.Nodes(session.Workspace).OfType<FractionNode>().Count(),
                scripts = Qa.Nodes(session.Workspace).OfType<ScriptNode>().Count(),
                summations = Qa.Nodes(session.Workspace).OfType<SummationNode>().Count(),
                integrals = Qa.Nodes(session.Workspace).OfType<IntegralNode>().Count(),
                limits = Qa.Nodes(session.Workspace).OfType<LimitNode>().Count()
            },
            expressionRows = session.Workspace.Groups.SelectMany(group => group.Rows).Count(),
            captures
        };
        var path = Path.Combine(directory, "equation-typesetting-comparison.json");
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"TYPESETTING-UI artifact {path}");
    }

    private static string FingerprintEquation(EditingSession session) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(session.Workspace.Groups)));

    private static async Task<string> PrepareOperatorComparisonFixtureAsync(
        MainWindowViewModel viewModel, TempDirectory fixtureDirectory)
    {
        var session = viewModel.Session;
        var firstText = Qa.Nodes(session.Workspace).OfType<TextNode>().First();
        Qa.Check(session.TrySetCaret(new MathCaret(firstText.Id, 0)),
            "조판 표본의 첫 행을 활성화할 caret를 설정하지 못했습니다.");

        AddPopulatedOperatorRow(session, MathTemplateKind.Summation, node =>
        {
            var sum = node as SummationNode
                ?? throw new InvalidOperationException("합 템플릿이 SummationNode를 생성하지 않았습니다.");
            FillFixtureSlot(session, sum.LowerLimit, "i=1", "합 하한");
            FillFixtureSlot(session, sum.UpperLimit, "n", "합 상한");
            FillFixtureSlot(session, sum.Body, "a_i", "합 본문");
        });

        AddPopulatedOperatorRow(session, MathTemplateKind.Integral, node =>
        {
            var integral = node as IntegralNode
                ?? throw new InvalidOperationException("적분 템플릿이 IntegralNode를 생성하지 않았습니다.");
            FillFixtureSlot(session, integral.LowerLimit, "0", "적분 하한");
            FillFixtureSlot(session, integral.UpperLimit, "1", "적분 상한");
            FillFixtureSlot(session, integral.Integrand, "f(x)", "적분 피적분식");
            FillFixtureSlot(session, integral.DifferentialVariable, "x", "적분 변수");
        });

        AddPopulatedOperatorRow(session, MathTemplateKind.Limit, node =>
        {
            var limit = node as LimitNode
                ?? throw new InvalidOperationException("극한 템플릿이 LimitNode를 생성하지 않았습니다.");
            FillFixtureSlot(session, limit.Approach, "x→0", "극한 접근식");
            FillFixtureSlot(session, limit.Body, "g(x)", "극한 본문");
        });

        var path = fixtureDirectory.File("typesetting-operator-comparison.aethermath");
        await viewModel.SaveToPathAsync(path);
        Qa.Check(File.Exists(path) && new FileInfo(path).Length > 0,
            "공개 Session 편집으로 만든 조판 표본 작업 파일을 저장하지 못했습니다.");
        return path;
    }

    private static void AddPopulatedOperatorRow(EditingSession session, MathTemplateKind kind, Action<MathNode> populate)
    {
        Qa.Check(session.AddRowAfterCurrent(), kind + " 표본을 둘 행을 추가하지 못했습니다.");
        var templateId = session.InsertTemplate(kind)
            ?? throw new InvalidOperationException(kind + " 공개 Session 템플릿 삽입에 실패했습니다.");
        populate(Qa.Node(session, templateId));
    }

    private static void FillFixtureSlot(EditingSession session, MathNode? node, string value, string label)
    {
        var slot = node as TextNode
            ?? throw new InvalidOperationException(label + "이 편집 가능한 텍스트 슬롯이 아닙니다.");
        Qa.Check(session.TrySetCaret(new MathCaret(slot.Id, 0)), label + " caret를 설정하지 못했습니다.");
        Qa.Check(session.InsertText(value), label + " 값을 공개 Session API로 입력하지 못했습니다.");
        Qa.Equal(value, Qa.Leaf(session, slot.Id).Text, label + " 표본 문자열");
    }

    private static string CreateArtifactDirectory(string name)
    {
        var repositoryRoot = FindRepositoryRoot();
        var directory = Path.Combine(repositoryRoot, "doc", "verification", "artifacts", ArtifactScope,
            $"{name}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task ReplaceTextAsync(MainWindow window, TextBox textBox, string text)
    {
        EnsureStubHandle(window, "조판 속성 초안 입력 전");
        await BringControlIntoViewAsync(window, textBox);
        EnsureFullyVisibleOrCapture(window, textBox, "조판 속성 입력 전");
        textBox.Focus();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "조판 속성 초점 이동 후");
        EnsureFullyVisibleOrCapture(window, textBox, "조판 속성 입력 직전");
        textBox.SelectAll();
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        if (text.Length > 0)
            window.KeyTextInput(text);
        await DrainLayoutAsync(window);
        Qa.Equal(text, textBox.Text ?? string.Empty, "합성 키 입력 뒤 속성 초안 내용");
    }

    private static async Task ClickButtonAsync(MainWindow window, string name) =>
        await ClickControlAsync(window, Find<Button>(window, name));

    private static async Task ClickControlAsync(MainWindow window, Control control)
    {
        EnsureStubHandle(window, "조판 UI 합성 클릭 전");
        Qa.Check(control.IsEffectivelyEnabled, "비활성 조판 속성 control에 합성 클릭을 시도했습니다: " + control.Name);
        await BringControlIntoViewAsync(window, control);
        EnsureFullyVisibleOrCapture(window, control, "조판 합성 클릭 전");

        Point? stablePoint = null;
        var pointer = ControlCenterInWindow(window, control);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            window.MouseMove(pointer, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "조판 합성 pointer 입력 중");
            EnsureFullyVisibleOrCapture(window, control, "조판 hover 뒤 클릭 좌표 확인", pointer);
            var refreshed = ControlCenterInWindow(window, control);
            if (Distance(pointer, refreshed) <= 0.5)
            {
                stablePoint = refreshed;
                break;
            }
            pointer = refreshed;
        }

        var clickPoint = stablePoint
            ?? throw CreateVisibilityFailureException(
                window, control, "조판 hover 뒤 클릭 좌표가 안정되지 않았습니다", pointer);
        EnsureFullyVisibleOrCapture(window, control, "조판 MouseDown 직전", clickPoint);
        window.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(clickPoint, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static async Task BringControlIntoViewAsync(MainWindow window, Control control)
    {
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var localOrigin = control.TranslatePoint(new Point(0, 0), scroll)
                    ?? throw CreateVisibilityFailureException(
                        window, control, "조판 ScrollViewer 내부 대상 좌표를 얻지 못했습니다");

                var viewportHeight = scroll.Viewport.Height;
                var height = control.Bounds.Height;
                var margin = Math.Min(8, Math.Max(0, (viewportHeight - height) / 2));
                var top = localOrigin.Y;
                var bottom = top + height;
                if (top >= margin - 0.5 && bottom <= viewportHeight - margin + 0.5)
                    break;

                // 세로 방향만 중앙 또는 최대 8 DIP 안쪽으로 이동합니다. 가로 위치와 strict gate는 유지합니다.
                var desiredTop = height + margin * 2 <= viewportHeight
                    ? Math.Max(margin, (viewportHeight - height) / 2)
                    : 0;
                var maximum = Math.Max(0, scroll.Extent.Height - viewportHeight);
                var desiredOffset = Math.Clamp(scroll.Offset.Y + top - desiredTop, 0, maximum);
                if (Math.Abs(desiredOffset - scroll.Offset.Y) <= 0.5)
                    break;

                scroll.Offset = new Vector(scroll.Offset.X, desiredOffset);
                await DrainLayoutAsync(window);
            }
        }
    }

    private static void EnsureFullyVisibleOrCapture(
        MainWindow window, Control control, string phase, Point? pointer = null)
    {
        if (!IsFullyVisible(window, control))
            ThrowVisibilityFailure(window, control, phase, pointer);
    }

    private static void ThrowVisibilityFailure(
        MainWindow window, Control control, string phase, Point? pointer = null)
    {
        throw CreateVisibilityFailureException(window, control, phase, pointer);
    }

    private static InvalidOperationException CreateVisibilityFailureException(
        MainWindow window, Control control, string phase, Point? pointer = null)
    {
        EnsureStubHandle(window, phase + " 실패 증거 저장 전");
        var diagnostic = DescribeVisibility(window, control, phase, pointer);
        var evidencePath = CaptureVisibilityFailureEvidence(window, control, phase, pointer, diagnostic);
        return new InvalidOperationException(
            $"{phase}: {control.Name}이 실제 viewport에서 완전히 보이지 않습니다. {diagnostic} 증거={evidencePath}");
    }

    private static string CaptureVisibilityFailureEvidence(
        MainWindow window, Control control, string phase, Point? pointer, string diagnostic)
    {
        var handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("실패 증거 렌더 전에 Headless handle이 없습니다.");
        Qa.Check(handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "실패 증거 렌더는 STUB/0 창에서만 허용됩니다.");

        var directory = CreateArtifactDirectory("typesetting-visibility-failure");
        var pngFile = "window.png";
        var pngPath = Path.Combine(directory, pngFile);
        var width = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height));
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var metadata = new
        {
            caseId = CaseId,
            phase,
            target = control.Name,
            targetType = control.GetType().FullName,
            targetBounds = GetWindowBounds(window, control),
            pointer = pointer is Point point ? new { x = point.X, y = point.Y } : null,
            clientSize = new { width = window.ClientSize.Width, height = window.ClientSize.Height },
            scrollAncestors = control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll => new
            {
                name = scroll.Name,
                viewport = new { width = scroll.Viewport.Width, height = scroll.Viewport.Height },
                offset = new { x = scroll.Offset.X, y = scroll.Offset.Y },
                extent = new { width = scroll.Extent.Width, height = scroll.Extent.Height },
                boundsInWindow = GetWindowBounds(window, scroll),
                targetOriginInViewport = GetOrigin(control, scroll)
            }).ToArray(),
            handle = new { descriptor = handle.HandleDescriptor, value = "0x0", isStubZero = true },
            png = pngFile,
            diagnostic
        };
        var jsonPath = Path.Combine(directory, "visibility-failure.json");
        File.WriteAllText(jsonPath,
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.Error.WriteLine($"TYPESETTING-UI visibility evidence {jsonPath}");
        return directory;
    }

    private static string DescribeVisibility(MainWindow window, Control control, string phase, Point? pointer)
    {
        var target = GetWindowBounds(window, control);
        var scrolls = control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll =>
            $"{scroll.Name ?? scroll.GetType().Name}[viewport={scroll.Viewport.Width:R}x{scroll.Viewport.Height:R}," +
            $"offset={scroll.Offset.X:R},{scroll.Offset.Y:R},extent={scroll.Extent.Width:R}x{scroll.Extent.Height:R}," +
            $"bounds={GetWindowBounds(window, scroll)},targetLocal={GetOrigin(control, scroll)}]");
        var pointerText = pointer is Point point ? $"({point.X:R},{point.Y:R})" : "없음";
        return $"phase={phase}; target={target}; client={window.ClientSize.Width:R}x{window.ClientSize.Height:R}; " +
               $"pointer={pointerText}; scroll=[{string.Join("; ", scrolls)}]";
    }

    private static object? GetWindowBounds(MainWindow window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        return origin is Point point
            ? new { x = point.X, y = point.Y, width = control.Bounds.Width, height = control.Bounds.Height }
            : null;
    }

    private static object? GetOrigin(Control control, Control relativeTo)
    {
        var origin = control.TranslatePoint(new Point(0, 0), relativeTo);
        return origin is Point point ? new { x = point.X, y = point.Y } : null;
    }

    private static Point ControlCenterInWindow(MainWindow window, Control control)
    {
        var point = control.TranslatePoint(new Point(0, 0), window)
            ?? throw CreateVisibilityFailureException(window, control, "조판 합성 클릭 대상 창 좌표 산출 실패");
        return point + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }

    private static double Distance(Point left, Point right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
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

    private static void SetScrollEdge(MainWindow window, ScrollViewer scroll, bool top)
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

    private static void AssertSelectionPreserved(SessionState expected, EditingSession actual, string label)
    {
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " 부분 선택");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " 선택 노드");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " 전체 구조 선택 상태");
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
        EnsureStubHandle(window, "소유 조판 Headless 창 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "조판 UI가 소유한 Headless 창을 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "조판 UI child 종료 뒤 실제/native 창 handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AetherMath.sln")))
                return current.FullName;
        throw new DirectoryNotFoundException("AetherMath.sln을 기준으로 저장소 루트를 찾지 못했습니다.");
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

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

    private sealed record BoundsEvidence(double X, double Y, double Width, double Height);

    private sealed record EquationCapture(
        string TypesettingStyle,
        string PngFile,
        string PngSha256,
        int PixelWidth,
        int PixelHeight,
        BoundsEvidence EditorBoundsInWindow,
        string EquationFingerprint,
        string Purpose);
}
