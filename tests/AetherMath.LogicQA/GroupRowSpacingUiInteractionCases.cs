using System.Collections.Immutable;
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
using AetherMath.Layout;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class GroupRowSpacingUiInteractionCases
{
    private const string CaseId = "GROUP-ROW-SPACING-UI";
    private const string ArtifactScope = "p2-group-row-spacing";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "현재 그룹 내부 행 간격의 실제 binding·geometry·history와 LaTeX 비보존 진단을 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 그룹 행 간격 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("그룹 행 간격 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
            return 2;
        }

        var fixtureDirectory = new TempDirectory();
        try
        {
            await RunWindowChecksAsync(fixtureDirectory);
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
                await CloseOwnedWindowAsync(_ownedWindow);
            }
            finally
            {
                _ownedWindow = null;
                fixtureDirectory.Dispose();
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
                throw new InvalidOperationException("dotnet host의 QA 어셈블리 경로를 찾지 못했습니다.");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        startInfo.ArgumentList.Add("--group-row-spacing-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("그룹 행 간격 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("그룹 행 간격 UI child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"그룹 행 간격 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("그룹 행 간격 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync(TempDirectory fixtureDirectory)
    {
        var fixture = CreateFixture();
        var fileService = new WorkspaceFileService();
        var fixturePath = fixtureDirectory.File("group-row-spacing-ui.aethermath");
        var seedSession = new EditingSession(fixture.Workspace);
        seedSession.MarkSaved(seedSession.CaptureSnapshot());
        await fileService.SaveAsync(fixturePath, seedSession.CaptureSnapshot());
        Qa.Check(File.Exists(fixturePath) && new FileInfo(fixturePath).Length > 0,
            "실제 UI fixture를 격리 임시 디렉터리에 저장하지 못했습니다.");

        var viewModel = new MainWindowViewModel(fileService);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(fixturePath),
            "명시 경로로 그룹 행 간격 fixture 열기");
        var session = viewModel.Session;
        Qa.Caret(session, fixture.TargetFirstTextId, 0);
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Check(!session.IsDirty, "초기 그룹 행 간격 fixture가 clean 저장 상태가 아닙니다.");

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
        EnsureStubHandle(window, "초기 layout 후");

        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        var scopeCombo = Find<ComboBox>(window, "PropertyScopeComboBox");
        var propertiesScroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        Qa.Check(ReferenceEquals(session, viewModel.Session) && ReferenceEquals(session, editor.Session),
            "실제 MainWindow/MathEditorControl이 fixture Session을 공유하지 않습니다.");
        AssertScopeChoices(scopeCombo, viewModel);
        await SelectScopeAsync(window, scopeCombo, viewModel, MathPropertyScope.CurrentGroup);
        AssertEditableGroupBindings(window, viewModel, fixture.TargetGroupId, expectedSpacing: 18);

        await CaptureShellEdgesAsync(window, propertiesScroll, "group-spacing-initial-minimum");
        await ResizeAsync(window, 1360, 880);
        await CaptureShellEdgesAsync(window, propertiesScroll, "group-spacing-initial-default");
        await ResizeAsync(window, 1060, 700);

        await BringGroupDetailIntoViewAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-detail-minimum", ArtifactScope);

        var formulaFingerprint = FingerprintFormula(session);
        var baselineState = CaptureSession(session);
        var baselineLayout = CreateLayout(session);
        AssertInternalGap(baselineLayout, fixture.TargetRows, 18, "초기 활성 그룹 내부 간격");
        AssertBoundaryGap(baselineLayout, fixture.TargetRows[^1], fixture.NeighborRows[0], 28,
            "초기 인접 그룹 경계 간격");
        Qa.Equal(18d, session.Workspace.Groups.Single(group => group.Id == fixture.TargetGroupId).RowSpacing,
            "활성 대상 그룹 저장 간격");

        var comparisonDirectory = CreateArtifactDirectory("equation-spacing-comparison");
        var beforeCapture = CaptureFormula(window, "group-row-spacing-before", comparisonDirectory, formulaFingerprint);

        var draftBox = Find<TextBox>(window, "GroupRowSpacingDraftTextBox");
        await ReplaceTextAsync(window, draftBox, "513");
        AssertSessionUnchanged(baselineState, session, "잘못된 행 간격 초안 입력");
        Qa.Check(!string.IsNullOrWhiteSpace(viewModel.Properties.GroupRowSpacingValidationError) &&
                 !viewModel.Properties.CanApply && !Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "범위 초과 입력의 오류/Apply 차단이 실제 VM과 버튼에 반영되지 않습니다.");
        await BringControlIntoViewAsync(window, Find<TextBlock>(window, "GroupRowSpacingValidationError"));
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-invalid-minimum", ArtifactScope);
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        await DrainLayoutAsync(window);
        Qa.Equal(18d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            viewModel.Properties.DraftGroupRowSpacingText, "초안 되돌리기가 현재 그룹 값을 다시 읽음");
        AssertSessionUnchanged(baselineState, session, "잘못된 초안 되돌리기");

        await ReplaceTextAsync(window, draftBox, "68");
        Qa.Equal("68", viewModel.Properties.DraftGroupRowSpacingText, "TextBox의 행 간격 TwoWay 초안");
        Qa.Check(viewModel.Properties.CanApply && Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "유효한 그룹 행 간격 초안에서 Apply가 활성화되지 않습니다.");
        AssertSessionUnchanged(baselineState, session, "유효한 행 간격 초안 입력");
        Qa.Near(18, GetInternalGap(CreateLayout(session), fixture.TargetRows), "Apply 전 모델/geometry 초안 불변");

        await ClickButtonAsync(window, "ApplyPropertiesButton");
        await DrainLayoutAsync(window);
        var appliedGroup = session.Workspace.Groups.Single(group => group.Id == fixture.TargetGroupId);
        Qa.Equal(68d, appliedGroup.RowSpacing, "CurrentGroup 행 간격 Apply 결과");
        Qa.Equal(baselineState.Revision + 1, session.Revision, "행 간격 Apply 단일 revision");
        Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo,
            "행 간격 Apply dirty/undo history");
        Qa.Equal(formulaFingerprint, FingerprintFormula(session), "간격 Apply가 수식 AST/ID를 변경");
        var appliedLayout = CreateLayout(session);
        AssertInternalGap(appliedLayout, fixture.TargetRows, 68, "Apply 후 대상 그룹 내부 geometry");
        AssertBoundaryGap(appliedLayout, fixture.TargetRows[^1], fixture.NeighborRows[0], 28,
            "Apply 후 인접 그룹 geometry는 기본 간격 유지");
        Qa.Equal(23d, session.Workspace.Groups.Single(group => group.Id == fixture.SecondNeighborGroupId).RowSpacing,
            "대상이 아닌 이웃 그룹 내부 간격 보존");
        Qa.Equal(31d, session.Workspace.Groups.Single(group => group.Id == fixture.LastMultiRowGroupId).RowSpacing,
            "대상이 아닌 후속 그룹 내부 간격 보존");
        await BringGroupDetailIntoViewAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-applied-minimum", ArtifactScope);
        var afterCapture = CaptureFormula(window, "group-row-spacing-after", comparisonDirectory, formulaFingerprint);
        Qa.Check(!string.Equals(beforeCapture.Sha256, afterCapture.Sha256, StringComparison.Ordinal),
            "행 간격 변화가 실제 MathEditorControl 오프스크린 렌더에 나타나지 않습니다.");
        WriteFormulaEvidence(comparisonDirectory, fixture, beforeCapture, afterCapture, baselineState, session);

        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(baselineState.WorkspaceFingerprint, Qa.Fingerprint(session.Workspace), "행 간격 toolbar Undo workspace 복원");
        Qa.Equal(baselineState.Revision + 2, session.Revision, "행 간격 Apply Undo revision");
        Qa.Check(!session.IsDirty && session.CanRedo && !session.CanUndo,
            "행 간격 Undo 이후 history가 단일 적용을 되돌리지 않습니다.");
        AssertInternalGap(CreateLayout(session), fixture.TargetRows, 18, "Undo 뒤 대상 geometry");

        await ClickButtonAsync(window, "RedoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(68d, session.Workspace.Groups.Single(group => group.Id == fixture.TargetGroupId).RowSpacing,
            "행 간격 toolbar Redo 복원");
        Qa.Equal(baselineState.Revision + 3, session.Revision, "행 간격 Apply Redo revision");
        Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo,
            "행 간격 Redo history 상태");
        AssertInternalGap(CreateLayout(session), fixture.TargetRows, 68, "Redo 뒤 대상 geometry");

        await SelectScopeAsync(window, scopeCombo, viewModel, MathPropertyScope.CurrentGroup);
        await ReplaceTextAsync(window, draftBox, "88");
        var staleBaseline = CaptureSession(session);
        Qa.Caret(session, fixture.NeighborFirstTextId, 0);
        await DrainLayoutAsync(window);
        Qa.Check(viewModel.Properties.IsDraftStale && !viewModel.Properties.CanApply &&
                 viewModel.Properties.DraftGroupRowSpacingText == "88",
            "다른 그룹으로 이동한 뒤 수정된 초안 보존과 stale Apply 차단이 동작하지 않습니다.");
        Qa.Equal(staleBaseline.Revision, session.Revision, "stale 대상 이동은 문서 revision을 바꾸지 않습니다.");
        Qa.Equal(staleBaseline.WorkspaceFingerprint, Qa.Fingerprint(session.Workspace),
            "stale 대상 이동이 그룹/수식 데이터를 변경하지 않습니다.");
        await BringControlIntoViewAsync(window, Find<TextBlock>(window, "GroupRowSpacingNotice"));
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-stale-minimum", ArtifactScope);
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.Properties.IsDraftStale && !viewModel.Properties.CanApply,
            "stale 초안 되돌리기가 새 그룹 baseline으로 돌아오지 않았습니다.");
        Qa.Equal("24", viewModel.Properties.DraftGroupRowSpacingText,
            "stale 초안 되돌리기가 활성 이웃 그룹의 저장값을 다시 읽음");
        Qa.Equal(fixture.NeighborFirstTextId, session.Caret.NodeId, "stale 초안 복구가 활성 caret를 바꿈");
        Qa.Equal(staleBaseline.WorkspaceFingerprint, Qa.Fingerprint(session.Workspace),
            "stale 초안 되돌리기는 문서 데이터를 변경하지 않습니다.");

        foreach (var (scope, label) in new[]
                 {
                     (MathPropertyScope.CurrentRow, "row"),
                     (MathPropertyScope.SelectionNode, "selection"),
                     (MathPropertyScope.WorkspaceDefault, "workspace")
                 })
        {
            await SelectScopeAsync(window, scopeCombo, viewModel, scope);
            var disabledReason = Find<TextBlock>(window, "GroupRowSpacingDisabledReason");
            Qa.Check(!draftBox.IsEffectivelyEnabled && disabledReason.IsEffectivelyVisible &&
                     !string.IsNullOrWhiteSpace(disabledReason.Text) && ToolTip.GetShowOnDisabled(draftBox),
                $"{scope} 범위에서 간격 입력이 비활성화되고 이유가 노출되지 않습니다.");
            await BringControlIntoViewAsync(window, disabledReason);
            RowShellVisualCases.CaptureAndVerify(window, "group-spacing-disabled-" + label, ArtifactScope);
            var state = CaptureSession(session);
            await ReplaceTextAsync(window, draftBox, "99", expectEnabled: false);
            AssertSessionUnchanged(state, session, $"{scope} 비활성 간격 입력");
        }

        Qa.Caret(session, fixture.SingleRowTextId, 0);
        await DrainLayoutAsync(window);
        await SelectScopeAsync(window, scopeCombo, viewModel, MathPropertyScope.CurrentGroup);
        Qa.Equal(13d, viewModel.Properties.CurrentGroupRowSpacing, "단일 행 그룹 저장 간격");
        Qa.Check(draftBox.IsEffectivelyEnabled &&
                 viewModel.Properties.GroupRowSpacingNotice.Contains("화면상 변화가 없습니다", StringComparison.Ordinal) &&
                 string.IsNullOrWhiteSpace(viewModel.Properties.GroupRowSpacingLatexDiagnostic),
            "단일 행 그룹의 편집 가능성/현재 효과/LaTeX 진단 경계가 맞지 않습니다.");
        await BringGroupDetailIntoViewAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-single-row-minimum", ArtifactScope);

        Qa.Caret(session, fixture.TargetFirstTextId, 0);
        await DrainLayoutAsync(window);
        await SelectScopeAsync(window, scopeCombo, viewModel, MathPropertyScope.CurrentGroup);
        await ResizeAsync(window, 1360, 880);
        await BringGroupDetailIntoViewAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-detail-final-default", ArtifactScope);

        var beforeExpandedState = CaptureSession(session);
        var collapsedViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer").Viewport.Height;
        Qa.Check(collapsedViewport >= 257,
            $"코드 패널 접힘 상태의 중앙 편집 viewport가 257px 미만입니다: {collapsedViewport:R}.");
        var expander = Find<Expander>(window, "LatexCodeExpander");
        expander.IsExpanded = true;
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "LaTeX 행 간격 진단 펼친 뒤");
        Qa.Check(viewModel.CanExportLatex && viewModel.LatexLayoutDiagnostics.Length >= 4,
            "완성 multi-row 표본에서 비차단 행 간격 진단이 여러 그룹별로 생성되지 않았습니다.");
        Qa.Check(viewModel.LatexCodeDiagnostic.Contains("행 간격", StringComparison.Ordinal),
            "LaTeX 진단 요약에 간격 fidelity advisory가 포함되지 않았습니다.");
        RowShellVisualCases.CaptureGroupRowSpacingLatexAndVerify(window,
            "group-spacing-layout-diagnostic-default-top");
        RowShellVisualCases.CaptureGroupRowSpacingLatexAndVerify(window,
            "group-spacing-layout-diagnostic-default-first");
        RowShellVisualCases.CaptureGroupRowSpacingLatexAndVerify(window,
            "group-spacing-layout-diagnostic-default-bottom");
        AssertSessionUnchanged(beforeExpandedState, session, "LaTeX 행 간격 진단/스크롤 표시");

        expander.IsExpanded = false;
        await DrainLayoutAsync(window);
        var restoredViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer").Viewport.Height;
        Qa.Check(restoredViewport >= 257,
            $"코드 패널을 접은 뒤 중앙 편집 viewport의 257px 기준을 회복하지 못했습니다: {restoredViewport:R}.");
        Qa.Near(collapsedViewport, restoredViewport, "코드 패널 접기 뒤 편집 viewport 복귀", 1.5);
        AssertSessionUnchanged(beforeExpandedState, session, "LaTeX 코드 접기 후 Session 불변");
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-code-collapsed-restored-default", ArtifactScope);

        await ResizeAsync(window, 1060, 700);
        var minimumCollapsedViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer").Viewport.Height;
        Qa.Check(minimumCollapsedViewport >= 257,
            $"최소 창의 접힌 편집 viewport가 257px 미만입니다: {minimumCollapsedViewport:R}.");
        var minimumExpandedState = CaptureSession(session);
        expander.IsExpanded = true;
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "최소 창에서 LaTeX 행 간격 진단을 펼친 뒤");
        Qa.Check(viewModel.CanExportLatex && viewModel.LatexLayoutDiagnostics.Length >= 4,
            "최소 창 expanded 진단 fixture가 비차단 layout advisory를 유지하지 않습니다.");
        Qa.Check(viewModel.LatexCodeDiagnostic.Contains("행 간격", StringComparison.Ordinal),
            "최소 창 expanded 상태의 LaTeX 요약에 간격 advisory가 없습니다.");
        RowShellVisualCases.CaptureGroupRowSpacingLatexAndVerify(window,
            "group-spacing-layout-diagnostic-minimum-top");
        RowShellVisualCases.CaptureGroupRowSpacingLatexAndVerify(window,
            "group-spacing-layout-diagnostic-minimum-first");
        RowShellVisualCases.CaptureGroupRowSpacingLatexAndVerify(window,
            "group-spacing-layout-diagnostic-minimum-bottom");
        AssertSessionUnchanged(minimumExpandedState, session, "최소 창 LaTeX 진단/스크롤 표시");
        expander.IsExpanded = false;
        await DrainLayoutAsync(window);
        var minimumRestoredViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer").Viewport.Height;
        Qa.Check(minimumRestoredViewport >= 257,
            $"최소 창에서 코드 패널을 접은 뒤 편집 viewport가 257px 기준을 회복하지 못했습니다: {minimumRestoredViewport:R}.");
        Qa.Near(minimumCollapsedViewport, minimumRestoredViewport,
            "최소 창 코드 패널 접기 뒤 편집 viewport 복귀", 1.5);
        AssertSessionUnchanged(minimumExpandedState, session, "최소 창 LaTeX 코드 접기 후 Session 불변");
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-code-collapsed-restored-minimum", ArtifactScope);

        await BringGroupDetailIntoViewAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "group-spacing-detail-final-minimum", ArtifactScope);
        Qa.Check(ReferenceEquals(session, viewModel.Session) && ReferenceEquals(session, editor.Session),
            "그룹 행 간격 UI 시나리오가 실제 Session 또는 editor를 교체했습니다.");
    }

    private static Fixture CreateFixture()
    {
        var target = CreateGroup(18, "a", "b", "c");
        var neighbor = CreateGroup(24, "d", "e");
        var secondNeighbor = CreateGroup(23, "u", "v");
        var lastMultiRow = CreateGroup(31, "m", "n");
        var single = CreateGroup(13, "solo");
        var groups = ImmutableArray.Create(target.Group, neighbor.Group, secondNeighbor.Group,
            lastMultiRow.Group, single.Group);
        var workspace = new EquationWorkspace(Guid.NewGuid(), 1, "그룹 행 간격 Headless fixture", groups,
            new MathStyle(30, "Cambria Math", "#25364D"));
        return new Fixture(workspace, target.Group.Id, target.Group.Rows, neighbor.Group.Rows,
            secondNeighbor.Group.Id, lastMultiRow.Group.Id, target.FirstTextId,
            neighbor.FirstTextId, single.FirstTextId);
    }

    private static (EquationGroup Group, Guid FirstTextId) CreateGroup(double spacing, params string[] values)
    {
        var rows = values.Select(value =>
        {
            var root = Qa.Sequence(Qa.Text(value), Qa.Text("="), Qa.Text("1"));
            return new EquationRow(Guid.NewGuid(), root);
        }).ToImmutableArray();
        var group = new EquationGroup(Guid.NewGuid(), rows, AlignmentMode.Equals, spacing);
        var firstTextId = ((SequenceNode)rows[0].Root).Children.OfType<TextNode>().First().Id;
        return (group, firstTextId);
    }

    private static void AssertScopeChoices(ComboBox combo, MainWindowViewModel viewModel)
    {
        var choices = combo.Items.Cast<MathPropertyScopeChoice>().ToArray();
        Qa.Equal(viewModel.Properties.ScopeChoices.Length, choices.Length, "속성 scope ComboBox 항목 수");
        Qa.Check(choices.Select(choice => choice.Scope).SequenceEqual(viewModel.Properties.ScopeChoices.Select(choice => choice.Scope)),
            "속성 scope ComboBox 순서가 고정 VM choices와 다릅니다.");
        Qa.Check(ReferenceEquals(viewModel.Properties.ScopeChoices.Single(choice => choice.Scope == MathPropertyScope.CurrentGroup),
                choices.Single(choice => choice.Scope == MathPropertyScope.CurrentGroup)),
            "속성 scope ComboBox가 실제 CurrentGroup choice를 표시하지 않습니다.");
    }

    private static void AssertEditableGroupBindings(
        MainWindow window, MainWindowViewModel viewModel, Guid expectedGroupId, double expectedSpacing)
    {
        var group = viewModel.Session.Workspace.Groups.Single(item => item.Id == expectedGroupId);
        var properties = viewModel.Properties;
        var input = Find<TextBox>(window, "GroupRowSpacingDraftTextBox");
        var currentValue = Find<TextBlock>(window, "GroupRowSpacingCurrentValue");
        var meaning = Find<TextBlock>(window, "GroupRowSpacingMeaningNotice");
        var meaningText = meaning.Text ?? string.Empty;
        Qa.Check(properties.CanEditGroupRowSpacing && input.IsEffectivelyEnabled,
            "활성 CurrentGroup 간격 입력이 실제 UI에서 비활성입니다.");
        Qa.Equal(expectedSpacing, group.RowSpacing, "fixture 그룹의 기대 저장값");
        Qa.Equal(expectedSpacing, properties.CurrentGroupRowSpacing, "CurrentGroup 저장값 API");
        Qa.Equal(expectedSpacing.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            properties.DraftGroupRowSpacingText, "CurrentGroup draft invariant R 표시");
        Qa.Check(currentValue.IsEffectivelyVisible && currentValue.Text?.Contains(
                     expectedSpacing.ToString("R", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) == true,
            "현재 그룹 저장 간격 TextBlock이 VM 값과 동기화되지 않았습니다.");
        Qa.Check(meaning.IsEffectivelyVisible && meaningText.Contains("앞 행 수식과 다음 행 수식", StringComparison.Ordinal) &&
                 meaningText.Contains("그룹 사이 간격", StringComparison.Ordinal) &&
                 meaningText.Contains("상속 초기화", StringComparison.Ordinal),
            "행 간격 UI가 그룹 경계 차이와 상속 초기화 비대상을 설명하지 않습니다.");
        Qa.Check(ToolTip.GetShowOnDisabled(input) && input.IsEffectivelyEnabled == properties.CanEditGroupRowSpacing,
            "행 간격 입력의 비활성 tooltip 또는 CanEdit binding 상태가 맞지 않습니다.");
        Qa.Check(string.IsNullOrWhiteSpace(properties.GroupRowSpacingLatexDiagnostic) == (group.Rows.Length <= 1),
            "현재 multi-row 그룹 LaTeX fidelity advisory 표시 조건이 맞지 않습니다.");
    }

    private static async Task SelectScopeAsync(
        MainWindow window, ComboBox combo, MainWindowViewModel viewModel, MathPropertyScope scope)
    {
        EnsureStubHandle(window, "scope 선택 전");
        await BringControlIntoViewAsync(window, combo);
        EnsureFullyVisibleOrCapture(window, combo, "scope 선택 전 실제 viewport 확인");
        combo.SelectedValue = scope;
        await DrainLayoutAsync(window);
        Qa.Equal(scope, viewModel.Properties.SelectedScope, "scope ComboBox → Properties VM two-way: " + scope);
        Qa.Equal(scope, combo.SelectedValue, "Properties VM scope가 실제 ComboBox 선택으로 표시: " + scope);
    }

    private static async Task CaptureShellEdgesAsync(MainWindow window, ScrollViewer scroll, string prefix)
    {
        SetScrollEdge(window, scroll, top: true);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, prefix + "-top", ArtifactScope);
        SetScrollEdge(window, scroll, top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, prefix + "-bottom", ArtifactScope);
    }

    private static async Task ResizeAsync(MainWindow window, double width, double height)
    {
        EnsureStubHandle(window, "Headless 창 resize 전");
        window.Width = width;
        window.Height = height;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, $"{width:R}×{height:R} 재배치 후");
        Qa.Near(width, window.ClientSize.Width, "Headless client width", 1.5);
        Qa.Near(height, window.ClientSize.Height, "Headless client height", 1.5);
    }

    private static async Task BringGroupDetailIntoViewAsync(MainWindow window)
    {
        var controls = new Control[]
        {
            Find<Border>(window, "GroupRowSpacingCard"),
            Find<TextBlock>(window, "GroupRowSpacingCurrentValue"),
            Find<TextBlock>(window, "GroupRowSpacingMeaningNotice"),
            Find<TextBox>(window, "GroupRowSpacingDraftTextBox"),
            Find<TextBlock>(window, "GroupRowSpacingNotice")
        };
        await BringControlsIntoViewAsync(window, controls);
        foreach (var control in controls)
            EnsureFullyVisibleOrCapture(window, control, "행 간격 상세 viewport 확인");
    }

    private static async Task BringControlIntoViewAsync(MainWindow window, Control control)
    {
        await BringControlsIntoViewAsync(window, [control]);
        EnsureFullyVisibleOrCapture(window, control, "대상 control 실제 viewport 확인");
    }

    private static async Task BringControlsIntoViewAsync(MainWindow window, IReadOnlyCollection<Control> controls)
    {
        if (controls.Count == 0)
            return;

        var candidateScrolls = controls.First().GetVisualAncestors().OfType<ScrollViewer>().ToArray();
        foreach (var scroll in candidateScrolls.Where(scroll => controls.All(control =>
                     control.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, scroll)))))
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var origins = controls.Select(control => control.TranslatePoint(new Point(0, 0), scroll)
                    ?? throw CreateVisibilityFailureException(window, control,
                        "ScrollViewer 상대 좌표를 얻지 못했습니다.")).ToArray();
                var top = origins.Min(origin => origin.Y);
                var bottom = controls.Select((control, index) => origins[index].Y + control.Bounds.Height).Max();
                var height = bottom - top;
                const double margin = 8;
                if (top >= margin && bottom <= scroll.Viewport.Height - margin)
                    break;

                var desiredTop = height + margin * 2 <= scroll.Viewport.Height
                    ? Math.Max(margin, (scroll.Viewport.Height - height) / 2)
                    : 0;
                var maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                var nextOffset = Math.Clamp(scroll.Offset.Y + top - desiredTop, 0, maxOffset);
                if (Math.Abs(nextOffset - scroll.Offset.Y) < 0.5)
                    break;
                scroll.Offset = new Vector(scroll.Offset.X, nextOffset);
                await DrainLayoutAsync(window);
            }
        }
    }

    private static async Task ReplaceTextAsync(
        MainWindow window, TextBox textBox, string text, bool expectEnabled = true)
    {
        EnsureStubHandle(window, "행 간격 합성 키 입력 전");
        if (expectEnabled)
        {
            Qa.Check(textBox.IsEffectivelyEnabled, "비활성 행 간격 입력에 키 입력을 시도했습니다.");
            await BringControlIntoViewAsync(window, textBox);
        }
        else
        {
            Qa.Check(!textBox.IsEffectivelyEnabled, "비활성 범위의 행 간격 입력이 활성화되어 있습니다.");
            return;
        }

        if (text.Length == 0)
            return;
        textBox.Focus();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "행 간격 TextBox focus 후");
        EnsureFullyVisibleOrCapture(window, textBox, "행 간격 TextBox 키 입력 직전");
        textBox.SelectAll();
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        window.KeyTextInput(text);
        await DrainLayoutAsync(window);
        Qa.Equal(text, textBox.Text ?? string.Empty, "합성 입력 후 행 간격 draft 표시");
    }

    private static async Task ClickButtonAsync(MainWindow window, string name) =>
        await ClickControlAsync(window, Find<Button>(window, name));

    private static async Task ClickControlAsync(MainWindow window, Control control)
    {
        EnsureStubHandle(window, "행 간격 UI 합성 클릭 전");
        Qa.Check(control.IsEffectivelyEnabled, "비활성 행 간격 UI control 클릭을 거절했습니다: " + control.Name);
        await BringControlIntoViewAsync(window, control);
        EnsureFullyVisibleOrCapture(window, control, "행 간격 합성 클릭 전");

        Point? stablePoint = null;
        var pointer = ControlCenterInWindow(window, control);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            window.MouseMove(pointer, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "행 간격 합성 pointer 입력 중");
            EnsureFullyVisibleOrCapture(window, control, "hover 후 행 간격 클릭 위치 확인", pointer);
            var refreshed = ControlCenterInWindow(window, control);
            if (Distance(pointer, refreshed) <= 0.5)
            {
                stablePoint = refreshed;
                break;
            }
            pointer = refreshed;
        }

        var clickPoint = stablePoint
            ?? throw CreateVisibilityFailureException(window, control, "hover 뒤 행 간격 클릭 좌표가 안정되지 않았습니다.");
        EnsureFullyVisibleOrCapture(window, control, "MouseDown 직전 행 간격 control 확인", clickPoint);
        window.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(clickPoint, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "행 간격 합성 클릭 완료 후");
    }

    private static void EnsureFullyVisibleOrCapture(
        MainWindow window, Control control, string phase, Point? pointer = null)
    {
        if (IsFullyVisible(window, control))
            return;
        throw CreateVisibilityFailureException(window, control, phase, pointer);
    }

    private static InvalidOperationException CreateVisibilityFailureException(
        MainWindow window, Control control, string phase, Point? pointer = null)
    {
        EnsureStubHandle(window, phase + " 증거 캡처 전");
        var directory = CreateArtifactDirectory("visibility-failure");
        var pngPath = Path.Combine(directory, "main-window.png");
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
            client = new { width = window.ClientSize.Width, height = window.ClientSize.Height },
            handle = new { descriptor = "STUB", value = "0x0", isStubZero = true },
            scrollAncestors = control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll => new
            {
                name = scroll.Name,
                viewport = new { width = scroll.Viewport.Width, height = scroll.Viewport.Height },
                offset = new { x = scroll.Offset.X, y = scroll.Offset.Y },
                extent = new { width = scroll.Extent.Width, height = scroll.Extent.Height },
                bounds = GetWindowBounds(window, scroll),
                targetOrigin = GetOrigin(control, scroll)
            }).ToArray(),
            png = Path.GetFileName(pngPath)
        };
        var jsonPath = Path.Combine(directory, "visibility-failure.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(metadata, JsonOptions), new UTF8Encoding(false));
        return new InvalidOperationException(
            $"{phase}: {control.Name}이 실제 viewport에서 완전히 보이지 않습니다. " +
            $"target={JsonSerializer.Serialize(metadata.targetBounds)}; " +
            $"scroll={JsonSerializer.Serialize(metadata.scrollAncestors)}; 증거={jsonPath}");
    }

    private static bool IsFullyVisible(Window window, Control control)
    {
        if (!control.IsEffectivelyVisible)
            return false;
        var origin = control.TranslatePoint(new Point(0, 0), window);
        if (origin is not Point point)
            return false;
        var bounds = new Rect(point, control.Bounds.Size);
        if (!Contains(new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height), bounds))
            return false;
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var scrollOrigin = scroll.TranslatePoint(new Point(0, 0), window);
            if (scrollOrigin is not Point originInWindow ||
                !Contains(new Rect(originInWindow, scroll.Viewport), bounds))
                return false;
        }
        return true;
    }

    private static Point ControlCenterInWindow(MainWindow window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw CreateVisibilityFailureException(window, control, "행 간격 클릭 좌표 계산 실패");
        return origin + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }

    private static Point? GetOrigin(Control control, Control relativeTo)
    {
        var point = control.TranslatePoint(new Point(0, 0), relativeTo);
        return point is Point value ? value : null;
    }

    private static Rect GetWindowBounds(Window window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("control bounds의 창 좌표를 얻지 못했습니다: " + control.Name);
        return new Rect(origin, control.Bounds.Size);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static double Distance(Point left, Point right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void SetScrollEdge(MainWindow window, ScrollViewer scroll, bool top)
    {
        EnsureStubHandle(window, "속성 panel scroll 이동 전");
        var maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, top ? 0 : maxOffset);
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
            $"{phase}: 소유 Headless 창의 STUB/0 handle이 아닙니다.");
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
        EnsureStubHandle(window, "소유 MainWindow 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "UI child가 소유한 Headless MainWindow를 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "소유 창 종료 후 native handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static LayoutResult CreateLayout(EditingSession session) =>
        MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(),
            new MathLayoutOptions(FontSize: session.Workspace.Style.FontSize));

    private static double GetInternalGap(LayoutResult layout, ImmutableArray<EquationRow> rows)
    {
        var first = layout.LogicalRows.Single(item => item.RowId == rows[0].Id);
        var second = layout.LogicalRows.Single(item => item.RowId == rows[1].Id);
        return second.Bounds.Y - first.Bounds.Bottom;
    }

    private static void AssertInternalGap(
        LayoutResult layout, ImmutableArray<EquationRow> rows, double expected, string label)
    {
        for (var index = 0; index < rows.Length - 1; index++)
        {
            var upper = layout.LogicalRows.Single(item => item.RowId == rows[index].Id);
            var lower = layout.LogicalRows.Single(item => item.RowId == rows[index + 1].Id);
            Qa.Near(expected, lower.Bounds.Y - upper.Bounds.Bottom, $"{label} #{index + 1}");
        }
    }

    private static void AssertBoundaryGap(LayoutResult layout, EquationRow upper, EquationRow lower, double expected, string label)
    {
        var upperLayout = layout.LogicalRows.Single(item => item.RowId == upper.Id);
        var lowerLayout = layout.LogicalRows.Single(item => item.RowId == lower.Id);
        Qa.Near(expected, lowerLayout.Bounds.Y - upperLayout.Bounds.Bottom, label);
    }

    private static string FingerprintFormula(EditingSession session)
    {
        var payload = session.Workspace.Groups.Select(group => new
        {
            group.Id,
            Rows = group.Rows.Select(row => new { row.Id, Root = Qa.NodeFingerprint(row.Root) }).ToArray()
        }).ToArray();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
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

    private static void WriteFormulaEvidence(
        string directory, Fixture fixture, FormulaCapture before, FormulaCapture after,
        SessionState baseline, EditingSession session)
    {
        var evidence = new
        {
            caseId = CaseId,
            purpose = "동일 수식의 행 간격 적용 전후 실제 MathEditorControl 오프스크린 검토 이미지입니다. 제품 내보내기 파일이 아닙니다.",
            formulaFingerprintBeforeApply = before.FormulaFingerprint,
            formulaFingerprintAfterApply = after.FormulaFingerprint,
            sameFormulaIdsAndContent = before.FormulaFingerprint == after.FormulaFingerprint,
            revisionBeforeApply = baseline.Revision,
            revisionAfterApply = after.Revision,
            revisionAfterApplyAndBeforeUndoRedo = session.Revision,
            targetGroupId = fixture.TargetGroupId,
            targetRowIds = fixture.TargetRows.Select(row => row.Id).ToArray(),
            initialSpacing = 18d,
            appliedSpacing = 68d,
            before,
            after
        };
        var path = Path.Combine(directory, "equation-spacing-comparison.json");
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, JsonOptions), new UTF8Encoding(false));
        Console.WriteLine($"GROUP-ROW-SPACING-UI formula evidence {path}");
    }

    private static FormulaCapture CaptureFormula(
        MainWindow window, string label, string directory, string expectedFormulaFingerprint)
    {
        EnsureStubHandle(window, "수식 비교 렌더 전");
        Dispatcher.UIThread.RunJobs();
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        if (window.DataContext is not MainWindowViewModel viewModel || !ReferenceEquals(editor.Session, viewModel.Session))
            throw new InvalidOperationException("수식 비교가 실제 현재 MathEditorControl Session을 사용하지 않습니다.");
        var formulaFingerprint = FingerprintFormula(viewModel.Session);
        Qa.Equal(expectedFormulaFingerprint, formulaFingerprint, label + " 수식 AST/ID fingerprint");
        var bounds = editor.Bounds;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        Qa.Check(width >= 120 && height >= 100, "실제 수식 비교 viewport가 너무 작습니다.");
        var path = Path.Combine(directory, label + ".png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            using (var context = bitmap.CreateDrawingContext())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                editor.Render(context);
            }
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }
        var bytes = File.ReadAllBytes(path);
        Qa.Check(bytes.Length > 64 && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), label + " 비교 PNG가 유효하지 않습니다.");
        return new FormulaCapture(label, Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes)),
            width, height, formulaFingerprint, viewModel.Session.Revision,
            "실제 MathEditorControl.Render 오프스크린 PNG이며 제품 SVG/내보내기가 아닙니다.");
    }

    private static string CreateArtifactDirectory(string name)
    {
        var repositoryRoot = FindRepositoryRoot();
        var directory = Path.Combine(repositoryRoot, "doc", "verification", "artifacts", ArtifactScope,
            $"{name}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AetherMath.sln")))
                return current.FullName;
        throw new DirectoryNotFoundException("AetherMath.sln으로 저장소 루트를 찾지 못했습니다.");
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

    private sealed record Fixture(
        EquationWorkspace Workspace,
        Guid TargetGroupId,
        ImmutableArray<EquationRow> TargetRows,
        ImmutableArray<EquationRow> NeighborRows,
        Guid SecondNeighborGroupId,
        Guid LastMultiRowGroupId,
        Guid TargetFirstTextId,
        Guid NeighborFirstTextId,
        Guid SingleRowTextId);

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

    private sealed record FormulaCapture(
        string Label,
        string File,
        string Sha256,
        int PixelWidth,
        int PixelHeight,
        string FormulaFingerprint,
        long Revision,
        string Purpose);
}
