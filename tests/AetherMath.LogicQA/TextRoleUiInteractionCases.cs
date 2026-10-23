using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class TextRoleUiInteractionCases
{
    private const string CaseId = "TEXT-ROLE-UI";
    private const string ArtifactScope = "p2-text-role";
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "수식 텍스트 역할의 실제 바인딩·범위·통합 적용/실행 취소와 LaTeX 진단 패널을 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 텍스트 역할 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("텍스트 역할 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
        startInfo.ArgumentList.Add("--text-role-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("텍스트 역할 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("텍스트 역할 UI Headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"텍스트 역할 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("텍스트 역할 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        using var fixtureDirectory = new TempDirectory();
        var service = new WorkspaceFileService();
        var fixture = CreateRoleFixture();
        var fixturePath = fixtureDirectory.File("text-role-workspace.aethermath");
        await service.SaveAsync(fixturePath, new EditingSession(fixture.Workspace).CaptureSnapshot());
        Qa.Check(File.Exists(fixturePath), "텍스트 역할 실제 UI fixture를 TempDirectory에 저장하지 못했습니다.");

        var viewModel = new MainWindowViewModel(service);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(fixturePath),
            "명시 경로로 UI fixture 열기");
        var session = viewModel.Session;
        Qa.Caret(session, fixture.AutomaticTextId, 1);
        Qa.Select(session, fixture.AutomaticTextId, 0, 1);
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Check(!session.IsDirty, "초기 텍스트 역할 fixture가 저장된 기준 상태가 아닙니다.");

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
        EnsureStubHandle(window, "속성 탭 표시 후");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Check(ReferenceEquals(editor.Session, session), "텍스트 역할 UI가 현재 Session과 다른 편집기를 사용합니다.");
        AssertRoleBindings(window, viewModel);
        Qa.Check(viewModel.Properties.SelectionNotice.Contains("텍스트", StringComparison.Ordinal) &&
                 viewModel.Properties.SelectionNotice.Contains("전체", StringComparison.Ordinal),
            "부분 문자 선택의 적용 범위가 텍스트 전체라고 안내되지 않습니다.");
        Qa.Check(viewModel.Properties.TextRoleScopeNotice.Contains("일부 문자", StringComparison.Ordinal) &&
                 viewModel.Properties.TextRoleScopeNotice.Contains("전체", StringComparison.Ordinal),
            "텍스트 역할 UI가 부분 선택에도 텍스트 전체를 대상으로 한다고 안내하지 않습니다.");
        Qa.Check(viewModel.CanExportLatex && viewModel.LatexTextDiagnostics.IsEmpty &&
                 viewModel.LatexIncompleteSlotNodeIds.IsEmpty,
            "정상 텍스트 역할 fixture가 LaTeX 차단 진단을 잘못 표시합니다.");

        await CapturePanelEdgesAsync(window, "properties-initial-minimum");
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "기본 크기 재배치 후");
        await CapturePanelEdgesAsync(window, "properties-initial-default");

        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);

        var roleCombo = Find<ComboBox>(window, "TextRoleComboBox");
        var scopeCombo = Find<ComboBox>(window, "PropertyScopeComboBox");
        var currentValue = Find<TextBlock>(window, "TextRoleCurrentValue");
        var scopeNotice = Find<TextBlock>(window, "TextRoleScopeNotice");
        var disabledReason = Find<TextBlock>(window, "TextRoleDisabledReason");
        var semanticsNotice = Find<TextBlock>(window, "TextRoleSemanticsNotice");
        Qa.Check(!string.IsNullOrWhiteSpace(semanticsNotice.Text) &&
                 semanticsNotice.Text.Contains("문자를 바꾸지 않고", StringComparison.Ordinal),
            "텍스트 역할이 문자열이 아닌 표시 의미라는 안내가 없습니다.");

        await SelectScopeAsync(window, viewModel, scopeCombo, MathPropertyScope.CurrentRow);
        Qa.Check(!viewModel.Properties.CanEditTextRole && !roleCombo.IsEffectivelyEnabled,
            "행 범위에서 텍스트 역할 편집이 비활성화되지 않았습니다.");
        Qa.Check(!currentValue.IsEffectivelyVisible && !scopeNotice.IsEffectivelyVisible &&
                 disabledReason.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(disabledReason.Text),
            "지원하지 않는 행 범위에 자동 기본값을 역할처럼 보이거나 비활성 사유를 표시하지 않습니다.");
        await CaptureRoleDetailAsync(window, "text-role-unsupported-row-minimum");

        await SelectScopeAsync(window, viewModel, scopeCombo, MathPropertyScope.SelectionNode);
        Qa.Check(session.TrySelectNode(fixture.SequenceId), "구조 노드를 UI 역할 비지원 fixture로 선택하지 못했습니다.");
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.Properties.CanEditTextRole && !roleCombo.IsEffectivelyEnabled,
            "전체 구조 선택에서 텍스트 역할 편집이 비활성화되지 않았습니다.");
        Qa.Check(!currentValue.IsEffectivelyVisible && disabledReason.IsEffectivelyVisible &&
                 disabledReason.Text?.Contains("텍스트", StringComparison.Ordinal) == true,
            "구조 노드가 역할을 가진 것처럼 표시되거나 실제 비활성 사유가 없습니다.");
        await CaptureRoleDetailAsync(window, "text-role-unsupported-structure-minimum");

        Qa.Select(session, fixture.AutomaticTextId, 0, 1);
        await DrainLayoutAsync(window);
        Qa.Check(viewModel.Properties.CanEditTextRole && roleCombo.IsEffectivelyEnabled,
            "부분 선택에서 텍스트 역할 편집이 다시 활성화되지 않았습니다.");

        var staleBaseline = CaptureSession(session);
        await SelectRoleAsync(window, viewModel, roleCombo, MathTextRole.Number);
        Qa.Check(session.InsertText("!"), "stale 검증을 위한 실제 수식 텍스트 편집");
        await DrainLayoutAsync(window);
        Qa.Equal(staleBaseline.Revision + 1, session.Revision, "텍스트 편집에 따른 stale revision");
        Qa.Equal("!1V", Qa.Leaf(session, fixture.AutomaticTextId).Text,
            "stale 검증 중 원본 Session의 실제 텍스트 편집");
        Qa.Equal(MathTextRole.Automatic, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "stale 상태에서 미적용 역할 초안이 저장 역할을 바꾸지 않음");
        Qa.Check(viewModel.Properties.IsDraftStale &&
                 viewModel.Properties.DraftTextRole == MathTextRole.Number &&
                 !string.IsNullOrWhiteSpace(viewModel.Properties.StaleReason) &&
                 !viewModel.Properties.CanApply &&
                 !Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled &&
                 Find<Button>(window, "ResetPropertiesDraftButton").IsEffectivelyEnabled,
            "대상이 바뀐 역할 초안이 보존된 채 적용 차단·초안 되돌리기로 표시되지 않습니다.");
        var propertiesScroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        var bottomOffset = Math.Max(0, propertiesScroll.Extent.Height - propertiesScroll.Viewport.Height);
        propertiesScroll.Offset = new Vector(propertiesScroll.Offset.X, bottomOffset);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureTextRoleAndVerify(window, "properties-stale-minimum-bottom");
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.Properties.IsDraftStale &&
                 viewModel.Properties.DraftTextRole is null &&
                 viewModel.Properties.SelectedTextRoleChoice?.Value is null,
            "초안 되돌리기가 현재 대상을 다시 불러오고 역할 변경을 지우지 않았습니다.");
        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(staleBaseline.Workspace, Qa.Fingerprint(session.Workspace),
            "stale 검증 편집 실행 취소가 원래 수식 내용을 복구하지 않았습니다.");
        Qa.Equal(MathTextRole.Automatic, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "stale 검증 편집 실행 취소 뒤 역할 원본 복구");
        Qa.Select(session, fixture.AutomaticTextId, 0, 1);
        await DrainLayoutAsync(window);

        var initialSession = CaptureSession(session);
        var comparisonDirectory = CreateArtifactDirectory("text-role-comparison");
        var captures = new List<RoleCapture>
        {
            await CaptureEditorRoleAsync(window, fixture.AutomaticTextId, "Automatic", comparisonDirectory)
        };

        await SelectRoleAsync(window, viewModel, roleCombo, MathTextRole.Number);
        AssertSessionUnchanged(initialSession, session, "Number 역할 초안 선택");
        Qa.Check(viewModel.Properties.CanApply && Find<Button>(window, "ApplyPropertiesButton").IsEffectivelyEnabled,
            "Number 역할 초안이 명시 Apply 전에는 적용 가능하지 않습니다.");
        await CaptureRoleDetailAsync(window, "text-role-supported-number-minimum");
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        await DrainLayoutAsync(window);
        Qa.Equal(initialSession.Revision + 1, session.Revision, "Number 역할의 단일 통합 Apply");
        Qa.Equal(MathTextRole.Number, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "실제 Apply 뒤 Number 역할 저장");
        AssertSelectionPreserved(initialSession, session, "Number 역할 Apply 선택 보존");
        Qa.Check(ReferenceEquals(editor.Session, session) && ReferenceEquals(viewModel.Session, session),
            "텍스트 역할 Apply가 현재 Session 인스턴스를 교체했습니다.");
        captures.Add(await CaptureEditorRoleAsync(window, fixture.AutomaticTextId, "Number", comparisonDirectory));

        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathTextRole.Automatic, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "Number 역할 Apply 실행 취소");
        Qa.Equal(initialSession.Workspace, Qa.Fingerprint(session.Workspace), "Number 역할 undo가 다른 수식 구조를 변경");
        AssertSelectionPreserved(initialSession, session, "Number 역할 undo 선택 보존");

        await SelectRoleAsync(window, viewModel, roleCombo, MathTextRole.Identifier);
        await ReplaceTextAsync(window, Find<TextBox>(window, "FontSizeDraftTextBox"), "34");
        var combinedBaseline = CaptureSession(session);
        Qa.Check(viewModel.Properties.CanApply, "역할+크기 초안을 한 번에 적용할 수 없습니다.");
        AssertSessionUnchanged(combinedBaseline, session, "역할+크기 초안 편집");
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        await DrainLayoutAsync(window);
        Qa.Equal(combinedBaseline.Revision + 1, session.Revision, "역할과 기존 크기 필드의 통합 Apply revision");
        var identifierAfterApply = Qa.Leaf(session, fixture.AutomaticTextId);
        Qa.Equal(MathTextRole.Identifier, identifierAfterApply.Role, "통합 Apply가 Identifier 역할을 저장하지 않음");
        Qa.Near(34, identifierAfterApply.StyleOverride?.FontSize ?? double.NaN,
            "통합 Apply가 기존 글자 크기 override를 저장하지 않음");
        AssertSelectionPreserved(combinedBaseline, session, "통합 Apply 선택 보존");
        Qa.Check(session.CanUndo, "통합 Apply가 실행 취소 가능하지 않습니다.");
        await ClickButtonAsync(window, "ResetPropertiesOverrideButton");
        await DrainLayoutAsync(window);
        var roleAfterStyleReset = Qa.Leaf(session, fixture.AutomaticTextId);
        Qa.Equal(MathTextRole.Identifier, roleAfterStyleReset.Role,
            "4개 스타일 override 초기화가 텍스트 역할까지 제거했습니다.");
        Qa.Equal<MathStyleOverride?>(null, roleAfterStyleReset.StyleOverride,
            "상속 초기화가 대상의 스타일 override를 제거하지 않았습니다.");
        captures.Add(await CaptureEditorRoleAsync(window, fixture.AutomaticTextId, "Identifier", comparisonDirectory));

        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "상속 초기화 undo가 텍스트 역할을 손실했습니다.");
        Qa.Near(34, Qa.Leaf(session, fixture.AutomaticTextId).StyleOverride?.FontSize ?? double.NaN,
            "상속 초기화 undo가 이전 명시 글자 크기를 복원하지 않았습니다.");
        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathTextRole.Automatic, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "역할+크기 통합 Apply undo가 Automatic으로 돌아가지 않았습니다.");
        Qa.Equal<MathStyleOverride?>(null, Qa.Leaf(session, fixture.AutomaticTextId).StyleOverride,
            "역할+크기 통합 Apply undo가 원래 상속 상태를 복구하지 않았습니다.");
        Qa.Equal(initialSession.Workspace, Qa.Fingerprint(session.Workspace),
            "역할+크기 apply/reset undo 전체가 원본 fixture를 정확히 복구하지 않았습니다.");
        captures.Add(await CaptureEditorRoleAsync(window, fixture.AutomaticTextId, "AutomaticAfterUndo", comparisonDirectory));

        Qa.Check(captures.Where(capture => capture.Role is "Automatic" or "Number" or "Identifier")
                .Select(capture => capture.PngSha256).Distinct(StringComparer.Ordinal).Count() == 3,
            "동일 TextNode의 Automatic·Number·Identifier 실제 편집기 렌더가 구분되지 않습니다.");
        Qa.Check(captures.Where(capture => capture.Role.StartsWith("Automatic", StringComparison.Ordinal))
                .Select(capture => capture.PngSha256).Distinct(StringComparer.Ordinal).Count() == 1,
            "undo 뒤 Automatic 렌더가 초기 Automatic과 다릅니다.");
        WriteRoleComparisonEvidence(comparisonDirectory, captures, fixture.AutomaticTextId, session);

        await SelectRoleAsync(window, viewModel, roleCombo, MathTextRole.Identifier);
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "명시 Automatic 복귀 경계 준비");
        await SelectRoleAsync(window, viewModel, roleCombo, MathTextRole.Automatic);
        Qa.Check(viewModel.Properties.DraftTextRole == MathTextRole.Automatic && viewModel.Properties.CanApply,
            "현재 명시 Identifier에서 Automatic 선택을 명시 역할 변경으로 인식하지 않습니다.");
        await ClickButtonAsync(window, "ApplyPropertiesButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathTextRole.Automatic, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "명시 Automatic 선택이 저장 역할을 기본값으로 복귀시키지 않았습니다.");
        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "명시 Automatic 복귀가 단일 undo 단계가 아닙니다.");
        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathTextRole.Automatic, Qa.Leaf(session, fixture.AutomaticTextId).Role,
            "Automatic 준비용 Identifier Apply undo");
        session.MarkSaved(session.CaptureSnapshot());

        var controlFixture = CreateDiagnosticFixture();
        var diagnosticPath = fixtureDirectory.File("text-role-latex-diagnostics.aethermath");
        await service.SaveAsync(diagnosticPath, new EditingSession(controlFixture.Workspace).CaptureSnapshot());
        var editorBeforeReplacement = editor;
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(diagnosticPath),
            "제어 문자·빈 슬롯 진단 fixture를 명시 경로로 열기");
        await DrainLayoutAsync(window);
        session = viewModel.Session;
        Qa.Check(ReferenceEquals(editorBeforeReplacement, Find<MathEditorControl>(window, "MathEditorSurface")) &&
                 ReferenceEquals(editor.Session, session),
            "진단 fixture 열기가 MainWindow의 실제 편집기 control을 교체했습니다.");
        Qa.Caret(session, controlFixture.ControlTextId, 0);
        Qa.Select(session, controlFixture.ControlTextId, 0, 1);
        await DrainLayoutAsync(window);
        var expander = Find<Expander>(window, "LatexCodeExpander");
        var editorViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var collapsedSession = CaptureSession(session);
        var collapsedViewportHeight = editorViewport.Viewport.Height;
        var collapsedExpanderBounds = TryBoundsOf(expander, window);
        expander.IsExpanded = true;
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "LaTeX 진단 패널 펼친 뒤");
        var expandedEvidence = await CaptureLatexDiagnosticEvidenceAsync(window, viewModel, fixtureDirectory.Path,
            collapsedViewportHeight, collapsedExpanderBounds, collapsedSession);

        expander.IsExpanded = false;
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "LaTeX 진단 패널을 접은 뒤");
        var collapsedAgainViewportHeight = editorViewport.Viewport.Height;
        await CaptureRoleDetailAsync(window, "text-role-latex-diagnostic-minimum-collapsed");

        Qa.Check(!viewModel.CanExportLatex && viewModel.LatexTextDiagnostics.Length == 1 &&
                 viewModel.LatexTextDiagnostics[0].NodeId == controlFixture.ControlTextId &&
                 viewModel.LatexTextDiagnostics[0].Role == MathTextRole.PlainText &&
                 viewModel.LatexTextDiagnostics[0].CodePoint == 0x0001 &&
                 viewModel.LatexIncompleteSlotNodeIds.Length == 1 &&
                 viewModel.LatexIncompleteSlotNodeIds.Contains(controlFixture.EmptySlotId),
            "제어 문자와 필수 빈 슬롯이 LaTeX 차단 상태에 각각 반영되지 않았습니다.");
        Qa.Check(viewModel.LatexCodeDiagnostic.Contains("제어 문자", StringComparison.Ordinal) &&
                 viewModel.LatexCodeDiagnostic.Contains("미완성", StringComparison.Ordinal),
            "LaTeX 차단 배너 사유에 제어 문자와 필수 슬롯 상태가 모두 설명되지 않았습니다.");
        AssertLatexDiagnosticEvidence(window, viewModel, expandedEvidence);
        Qa.Check(collapsedViewportHeight >= 257 && collapsedAgainViewportHeight >= 257 &&
                 Math.Abs(collapsedAgainViewportHeight - collapsedViewportHeight) <= 1,
            $"LaTeX 패널을 접은 뒤 collapsed 최소 viewport가 257 DIP로 회복되지 않았습니다: " +
            $"처음={collapsedViewportHeight:R}, 접은 뒤={collapsedAgainViewportHeight:R}.");
        AssertSessionUnchanged(collapsedSession, session, "LaTeX 패널 펼침/접힘 전체 Session 불변");
    }

    private static void AssertRoleBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        var combo = Find<ComboBox>(window, "TextRoleComboBox");
        var properties = viewModel.Properties;
        var choices = properties.TextRoleChoices;
        Qa.Equal(8, choices.Length, "텍스트 역할 선택지 개수");
        Qa.Equal<MathTextRole?>(null, choices[0].Value, "첫 번째 텍스트 역할 항목은 변경 안 함");
        Qa.Equal(MathTextRole.Automatic, choices[1].Value, "Automatic 역할 선택 순서");
        Qa.Check(choices.All(choice => !string.IsNullOrWhiteSpace(choice.DisplayName) &&
                                       !string.IsNullOrWhiteSpace(choice.Description)),
            "역할 선택지의 한국어 이름 또는 설명이 비어 있습니다.");
        Qa.Check(combo.ItemsSource is IEnumerable<MathTextRoleChoice> items &&
                 items.SequenceEqual(choices), "텍스트 역할 ComboBox 항목이 VM의 canonical 선택지와 일치하지 않습니다.");
        Qa.Check(ReferenceEquals(combo.SelectedItem, properties.SelectedTextRoleChoice) &&
                 properties.SelectedTextRoleChoice?.Value is null,
            "Automatic 기준의 미변경 초안이 canonical SelectedItem으로 표시되지 않습니다.");
        Qa.Check(ReferenceEquals(Find<Button>(window, "ApplyPropertiesButton").Command, properties.ApplyCommand),
            "텍스트 역할이 기존 통합 Apply 명령에 연결되지 않았습니다.");
        Qa.Check(ReferenceEquals(Find<Button>(window, "ResetPropertiesOverrideButton").Command, properties.ResetOverrideCommand),
            "텍스트 역할 속성 패널이 기존 4필드 상속 초기화 명령을 바꾸었습니다.");
        Qa.Check(Find<TextBlock>(window, "TextRoleSemanticsNotice").IsEffectivelyVisible,
            "텍스트 역할 의미 안내가 표시되지 않습니다.");
    }

    private static async Task SelectScopeAsync(MainWindow window, MainWindowViewModel viewModel,
        ComboBox scopeCombo, MathPropertyScope scope)
    {
        EnsureStubHandle(window, "속성 범위 선택 전");
        await BringControlIntoViewAsync(window, scopeCombo);
        Qa.Check(IsFullyVisible(window, scopeCombo), "속성 범위 ComboBox가 viewport에 보이지 않습니다.");
        scopeCombo.SelectedValue = scope;
        await DrainLayoutAsync(window);
        Qa.Equal(scope, viewModel.Properties.SelectedScope, "PropertyScopeComboBox → VM 양방향 바인딩");
    }

    private static async Task SelectRoleAsync(MainWindow window, MainWindowViewModel viewModel,
        ComboBox roleCombo, MathTextRole role)
    {
        EnsureStubHandle(window, "텍스트 역할 선택 전");
        await BringControlIntoViewAsync(window, roleCombo);
        Qa.Check(IsFullyVisible(window, roleCombo), "텍스트 역할 ComboBox가 실제 viewport에서 잘립니다.");
        var choice = viewModel.Properties.TextRoleChoices.Single(item => item.Value == role);
        var before = CaptureSession(viewModel.Session);
        roleCombo.SelectedItem = choice;
        await DrainLayoutAsync(window);
        Qa.Equal<MathTextRole?>(role, viewModel.Properties.DraftTextRole,
            "텍스트 역할 ComboBox → DraftTextRole 양방향 바인딩: " + role);
        Qa.Check(ReferenceEquals(choice, roleCombo.SelectedItem) &&
                 ReferenceEquals(choice, viewModel.Properties.SelectedTextRoleChoice),
            "텍스트 역할 draft → canonical SelectedItem 표시값: " + role);
        Qa.Check(!string.IsNullOrWhiteSpace(choice.Description), role + " 선택지 설명이 없습니다.");
        AssertSessionUnchanged(before, viewModel.Session, "텍스트 역할 초안 선택 " + role);
    }

    private static async Task ReplaceTextAsync(MainWindow window, TextBox textBox, string text)
    {
        EnsureStubHandle(window, "속성 초안 입력 전");
        await BringControlIntoViewAsync(window, textBox);
        Qa.Check(IsFullyVisible(window, textBox), "속성 입력 대상이 실제 viewport에서 잘립니다: " + textBox.Name);
        textBox.Focus();
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
        EnsureStubHandle(window, "Headless 합성 클릭 전");
        Qa.Check(control.IsEffectivelyEnabled, "비활성 control에 합성 클릭을 시도했습니다: " + control.Name);
        await BringControlIntoViewAsync(window, control);
        EnsureClickTargetVisibleOrCapture(window, control, "합성 클릭 직전");
        var point = GetWindowCenter(control, window);
        var lastSentPoint = point;
        var settled = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            window.MouseMove(point, RawInputModifiers.None);
            lastSentPoint = point;
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "Headless 합성 pointer 입력 중");
            EnsureClickTargetVisibleOrCapture(window, control, "pointer 이동 뒤 layout 정리 후", point);
            var updatedPoint = GetWindowCenter(control, window);
            if (Math.Abs(updatedPoint.X - point.X) <= 0.5 && Math.Abs(updatedPoint.Y - point.Y) <= 0.5)
            {
                point = updatedPoint;
                settled = true;
                break;
            }
            point = updatedPoint;
        }
        if (!settled)
            throw CreateClickFailureException(window, control, "pointer hover 배치가 안정되지 않음", lastSentPoint);
        EnsureClickTargetVisibleOrCapture(window, control, "MouseDown 직전 최종 좌표 확인", point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static async Task CaptureRoleDetailAsync(MainWindow window, string label)
    {
        EnsureStubHandle(window, "텍스트 역할 상세 캡처 전");
        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        var targetNames = new List<string>
        {
            "TextRoleComboBox", "TextRoleSemanticsNotice"
        };
        if (label.Contains("unsupported", StringComparison.Ordinal))
            targetNames.Add("TextRoleDisabledReason");
        else
            targetNames.AddRange(["TextRoleCurrentValue", "TextRoleScopeNotice"]);
        var controls = targetNames.Select(name => Find<Control>(window, name)).ToArray();
        var origins = controls.Select(control => control.TranslatePoint(new Point(0, 0), scroll)
            ?? throw new InvalidOperationException("텍스트 역할 상세 control의 스크롤 좌표를 얻지 못했습니다: " + control.Name)).ToArray();
        var top = origins.Min(point => point.Y);
        var bottom = controls.Select((control, index) => origins[index].Y + control.Bounds.Height).Max();
        Qa.Check(bottom - top <= scroll.Viewport.Height + 1,
            $"텍스트 역할 상세가 속성 viewport보다 높습니다: 상세={bottom - top:R}, viewport={scroll.Viewport.Height:R}.");
        var shift = (top + bottom - scroll.Viewport.Height) / 2;
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + shift, 0, maximum));
        await DrainLayoutAsync(window);
        foreach (var control in controls)
            Qa.Check(IsFullyVisible(window, control), "텍스트 역할 상세 control 전체를 볼 수 없습니다: " + control.Name);
        RowShellVisualCases.CaptureTextRoleAndVerify(window, label);
    }

    private static async Task CapturePanelEdgesAsync(MainWindow window, string prefix)
    {
        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        scroll.Offset = new Vector(scroll.Offset.X, 0);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureTextRoleAndVerify(window, prefix + "-top");
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, maximum);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureTextRoleAndVerify(window, prefix + "-bottom");
    }

    private static async Task BringControlIntoViewAsync(Window window, Control control)
    {
        const double viewportMargin = 8;
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var origin = control.TranslatePoint(new Point(0, 0), scroll);
            Qa.Check(origin is Point, "스크롤 내부 control 좌표를 얻지 못했습니다: " + control.Name);
            var top = origin!.Value.Y;
            var bottom = top + control.Bounds.Height;
            var viewportHeight = scroll.Viewport.Height;
            var bottomMargin = Math.Min(viewportMargin, Math.Max(0, (viewportHeight - control.Bounds.Height) / 2));
            var topMargin = bottomMargin;
            if (top >= topMargin && bottom <= viewportHeight - bottomMargin)
                continue;

            var desiredTop = control.Bounds.Height <= viewportHeight - (topMargin + bottomMargin)
                ? Math.Max(topMargin, (viewportHeight - control.Bounds.Height) / 2)
                : 0;
            var offset = scroll.Offset.Y + top - desiredTop;
            var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(offset, 0, maximum));
            await DrainLayoutAsync(window);
        }
    }

    private static void EnsureClickTargetVisibleOrCapture(MainWindow window, Control control, string phase,
        Point? pointerPosition = null)
    {
        if (IsFullyVisible(window, control))
            return;

        throw CreateClickFailureException(window, control, phase, pointerPosition);
    }

    private static InvalidOperationException CreateClickFailureException(MainWindow window, Control control,
        string phase, Point? pointerPosition)
    {
        var label = "properties-click-" + SafeArtifactName(control.Name ?? control.GetType().Name);
        var pointer = pointerPosition is Point point
            ? $"({point.X:R},{point.Y:R})"
            : "미이동";
        var diagnostics = DescribeControlVisibility(window, control) + $" pointer={pointer}";
        var evidenceDirectory = CaptureClickFailureEvidence(window, label, phase, diagnostics, pointer);
        var shellCaptureError = string.Empty;
        try
        {
            RowShellVisualCases.CaptureTextRoleAndVerify(window, label);
        }
        catch (Exception exception)
        {
            shellCaptureError = exception.ToString();
        }

        return new InvalidOperationException(
            $"{phase}: 합성 클릭 대상의 실제 viewport 가시성 또는 안정된 좌표를 확인하지 못했습니다. " +
            diagnostics + $"\n실패 증거 폴더: {evidenceDirectory}" +
            (shellCaptureError.Length == 0 ? string.Empty : "\n공용 셸 캡처 추가 진단:\n" + shellCaptureError));
    }

    private static Point GetWindowCenter(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("합성 클릭 대상의 창 좌표를 얻지 못했습니다: " + control.Name);
        return new Point(origin.X + control.Bounds.Width / 2, origin.Y + control.Bounds.Height / 2);
    }

    private static string DescribeControlVisibility(Window window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        var controlBounds = origin is Point point
            ? new Rect(point, control.Bounds.Size).ToString()
            : "창 좌표 없음";
        var scrollDiagnostics = control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll =>
        {
            var scrollOrigin = scroll.TranslatePoint(new Point(0, 0), window);
            var localTarget = control.TranslatePoint(new Point(0, 0), scroll);
            var windowOriginText = scrollOrigin is Point windowOrigin
                ? $"({windowOrigin.X:R},{windowOrigin.Y:R})"
                : "없음";
            var targetOriginText = localTarget is Point targetOrigin
                ? $"({targetOrigin.X:R},{targetOrigin.Y:R})"
                : "없음";
            return $"{scroll.Name ?? nameof(ScrollViewer)}: " +
                   $"viewport=({scroll.Viewport.Width:R},{scroll.Viewport.Height:R}), " +
                   $"offset=({scroll.Offset.X:R},{scroll.Offset.Y:R}), " +
                   $"extent=({scroll.Extent.Width:R},{scroll.Extent.Height:R}), " +
                   $"horizontal={ScrollViewer.GetHorizontalScrollBarVisibility(scroll)}, " +
                   $"vertical={ScrollViewer.GetVerticalScrollBarVisibility(scroll)}, " +
                   $"windowOrigin={windowOriginText}, targetInViewport={targetOriginText}, " +
                   $"targetSize=({control.Bounds.Width:R},{control.Bounds.Height:R})";
        });
        return $"control={control.Name ?? control.GetType().Name}, visible={control.IsEffectivelyVisible}, " +
               $"bounds={controlBounds}, client=({window.ClientSize.Width:R},{window.ClientSize.Height:R}), " +
               "scrolls=[" + string.Join("; ", scrollDiagnostics) + "]";
    }

    private static string CaptureClickFailureEvidence(MainWindow window,
        string label, string phase, string diagnostics, string pointerPosition)
    {
        EnsureStubHandle(window, "합성 클릭 가시성 실패 증거 저장 전");
        var directory = CreateArtifactDirectory("click-visibility-failure");
        var width = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height));
        var pngPath = Path.Combine(directory, "main-window.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var platformHandle = window.TryGetPlatformHandle();
        var evidence = new
        {
            caseId = CaseId,
            label,
            phase,
            evidencePurpose = "Headless 합성 클릭의 strict viewport 실패 당시 전체 창과 실제 control/ScrollViewer bounds 진단이며 제품 화면 출력물이 아닙니다.",
            handleDescriptor = platformHandle?.HandleDescriptor,
            handle = platformHandle?.Handle.ToInt64(),
            pointerPosition,
            diagnostics,
            png = Path.GetFileName(pngPath)
        };
        File.WriteAllText(Path.Combine(directory, "bounds.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return directory;
    }

    private static string SafeArtifactName(string value)
    {
        var safe = new string(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
            ? character
            : '-').ToArray());
        return safe.Length == 0 ? "control" : safe;
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

    private static async Task<RoleCapture> CaptureEditorRoleAsync(
        MainWindow window, Guid nodeId, string roleName, string directory)
    {
        EnsureStubHandle(window, roleName + " 편집기 오프스크린 렌더 전");
        await DrainLayoutAsync(window);
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        if (window.DataContext is not MainWindowViewModel viewModel ||
            !ReferenceEquals(editor.Session, viewModel.Session))
            throw new InvalidOperationException("텍스트 역할 비교 렌더가 현재 실제 편집기 Session을 사용하지 않습니다.");
        var leaf = Qa.Leaf(viewModel.Session, nodeId);
        Qa.Equal("A1V", leaf.Text, roleName + " 비교의 안정 텍스트 원문");
        var bounds = editor.Bounds;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        Qa.Check(width >= 120 && height >= 100, "실제 수식 편집 렌더 영역이 너무 작습니다.");
        var origin = editor.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("실제 편집기 창 좌표를 얻지 못했습니다.");
        var fileName = $"equation-{roleName}.png";
        var path = Path.Combine(directory, fileName);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            using (var drawingContext = bitmap.CreateDrawingContext())
            {
                drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                editor.Render(drawingContext);
            }
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }
        var bytes = await File.ReadAllBytesAsync(path);
        Qa.Check(bytes.Length > 64 && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            roleName + " 실제 편집기 오프스크린 렌더가 유효한 PNG가 아닙니다.");
        return new RoleCapture(roleName, fileName, Convert.ToHexString(SHA256.HashData(bytes)),
            width, height, new BoundsEvidence(origin.X, origin.Y, bounds.Width, bounds.Height),
            nodeId, leaf.Text, leaf.Role.ToString(), Qa.Fingerprint(viewModel.Session.Workspace),
            "현재 MathEditorControl.Render와 실제 Session을 사용한 흰 배경 비교용 이미지이며 제품 내보내기가 아닙니다.");
    }

    private static void WriteRoleComparisonEvidence(string directory, IReadOnlyList<RoleCapture> captures,
        Guid targetNodeId, EditingSession session)
    {
        var evidence = new
        {
            caseId = CaseId,
            evidencePurpose = "같은 실제 TextNode의 Automatic·Number·Identifier 화면 차이 검토용 오프스크린 이미지이며 제품 내보내기가 아닙니다.",
            targetNodeId,
            targetText = Qa.Leaf(session, targetNodeId).Text,
            captures
        };
        var path = Path.Combine(directory, "text-role-comparison.json");
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"TEXT-ROLE-UI 비교 증거 {path}");
    }

    private static async Task<LatexDiagnosticEvidence> CaptureLatexDiagnosticEvidenceAsync(
        MainWindow window, MainWindowViewModel viewModel, string tempRoot,
        double collapsedViewportHeight, BoundsEvidence? collapsedExpanderBounds, SessionState collapsedSession)
    {
        var directory = CreateArtifactDirectory("latex-diagnostic");
        var width = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height));
        EnsureStubHandle(window, "LaTeX 진단 셸 오프스크린 캡처 전");
        var platformHandle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("LaTeX 진단 캡처에 Headless platform handle이 없습니다.");
        var handleDescriptor = platformHandle.HandleDescriptor
            ?? throw new InvalidOperationException("LaTeX 진단 캡처의 platform handle descriptor가 없습니다.");
        var handleValue = platformHandle.Handle.ToInt64();

        var expander = Find<Expander>(window, "LatexCodeExpander");
        var banner = Find<Border>(window, "LatexCodeDiagnosticBanner");
        var layoutBanner = Find<Border>(window, "LatexLayoutDiagnosticBanner");
        var bannerText = Find<TextBlock>(window, "LatexCodeDiagnosticText");
        var note = Find<TextBlock>(window, "LatexAmsmathNotice");
        var source = Find<LatexCodeSurface>(window, "LatexCodeView");
        var diagnosticsViewer = Find<ScrollViewer>(window, "LatexDiagnosticsScrollViewer");
        var layoutList = Find<ItemsControl>(window, "LatexLayoutDiagnosticsList");
        var editorViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var codePanelGrid = source.GetVisualAncestors().OfType<Grid>()
            .FirstOrDefault(grid => Math.Abs(grid.MaxHeight - 160) <= 0.01);
        diagnosticsViewer.Offset = new Vector(diagnosticsViewer.Offset.X, 0);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "LaTeX 진단 상단 표시 정리 후");
        const string topCaptureFile = "latex-diagnostic-shell.png";
        SaveWholeWindowPng(window, width, height, Path.Combine(directory, topCaptureFile));
        var diagnosticTextBoundsTop = TryBoundsRelativeTo(bannerText, diagnosticsViewer);
        var diagnosticViewportBounds = TryBoundsOf(diagnosticsViewer, window);
        var diagnosticViewportHeight = diagnosticsViewer.Viewport.Height;
        var diagnosticExtentHeight = diagnosticsViewer.Extent.Height;
        var diagnosticOffsetTop = diagnosticsViewer.Offset.Y;
        var diagnosticsScrollBarVisibility = ScrollViewer.GetVerticalScrollBarVisibility(diagnosticsViewer);

        var maximumDiagnosticOffset = Math.Max(0, diagnosticExtentHeight - diagnosticViewportHeight);
        var textTop = bannerText.TranslatePoint(new Point(0, 0), diagnosticsViewer)
            ?? throw new InvalidOperationException("LaTeX 진단 문구의 스크롤 상대 좌표를 얻지 못했습니다.");
        const double diagnosticTextMargin = 8;
        var availableVerticalGap = Math.Max(0, diagnosticViewportHeight - bannerText.Bounds.Height);
        var adaptiveTextMargin = Math.Min(diagnosticTextMargin, availableVerticalGap / 2);
        var desiredTextTop = bannerText.Bounds.Height <= diagnosticViewportHeight
            ? Math.Max(adaptiveTextMargin, availableVerticalGap / 2)
            : diagnosticViewportHeight - bannerText.Bounds.Height - diagnosticTextMargin;
        var messageEndOffset = Math.Clamp(diagnosticsViewer.Offset.Y + textTop.Y - desiredTextTop, 0,
            maximumDiagnosticOffset);
        diagnosticsViewer.Offset = new Vector(diagnosticsViewer.Offset.X, messageEndOffset);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "LaTeX 진단 문구 끝 접근 상태");
        const string messageEndCaptureFile = "latex-diagnostic-message-end.png";
        SaveWholeWindowPng(window, width, height, Path.Combine(directory, messageEndCaptureFile));
        var diagnosticTextBoundsMessageEnd = TryBoundsRelativeTo(bannerText, diagnosticsViewer);
        var diagnosticOffsetMessageEnd = diagnosticsViewer.Offset.Y;

        diagnosticsViewer.Offset = new Vector(diagnosticsViewer.Offset.X, maximumDiagnosticOffset);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "LaTeX 진단 목록 말미 접근 상태");
        const string listEndCaptureFile = "latex-diagnostic-list-end.png";
        SaveWholeWindowPng(window, width, height, Path.Combine(directory, listEndCaptureFile));
        var layoutTailText = layoutList.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => !string.IsNullOrWhiteSpace(text.Text)).LastOrDefault();
        var layoutTailBoundsListEnd = layoutTailText is null ? null : TryBoundsRelativeTo(layoutTailText, diagnosticsViewer);
        var diagnosticTextBoundsListEnd = TryBoundsRelativeTo(bannerText, diagnosticsViewer);

        var windowBounds = new BoundsEvidence(0, 0, width, height);
        var expandedExpanderBounds = TryBoundsOf(expander, window);
        var bannerBounds = TryBoundsOf(banner, window);
        var diagnosticTextBounds = TryBoundsOf(bannerText, window);
        var noticeBounds = TryBoundsOf(note, window);
        var sourceBounds = TryBoundsOf(source, window);
        var codePanelBounds = codePanelGrid is null ? null : TryBoundsOf(codePanelGrid, window);
        var editorViewportBounds = TryBoundsOf(editorViewport, window);
        var expanderGrowth = collapsedExpanderBounds is not null && expandedExpanderBounds is not null
            ? expandedExpanderBounds.Height - collapsedExpanderBounds.Height
            : (double?)null;
        var expandedViewportHeight = editorViewport.Viewport.Height;
        var viewportLoss = collapsedViewportHeight - expandedViewportHeight;
        var layoutDeltaError = expanderGrowth is double growth ? Math.Abs(growth - viewportLoss) : (double?)null;
        var sourceAndDiagnosticHeight = diagnosticsViewer.Bounds.Height + source.Bounds.Height;
        var sessionState = CaptureSession(viewModel.Session);
        var textDiagnostics = viewModel.LatexTextDiagnostics
            .Select(item => new LatexTextDiagnosticEvidence(item.NodeId, item.Role.ToString(), item.Utf16Index,
                item.CodePoint, item.Reason, item.CodePointLabel))
            .ToArray();
        var evidence = new LatexDiagnosticEvidence(
            CaseId,
            directory,
            handleDescriptor,
            handleValue,
            windowBounds,
            collapsedViewportHeight,
            expandedViewportHeight,
            collapsedExpanderBounds,
            expandedExpanderBounds,
            expanderGrowth,
            viewportLoss,
            layoutDeltaError,
            expander.IsExpanded,
            expander.IsEffectivelyVisible,
            banner.IsEffectivelyVisible,
            layoutBanner.IsEffectivelyVisible,
            bannerText.IsEffectivelyVisible,
            note.IsEffectivelyVisible,
            source.IsReadOnly,
            ScrollViewer.GetVerticalScrollBarVisibility(source.CodeScrollViewer),
            diagnosticsScrollBarVisibility,
            diagnosticViewportBounds,
            diagnosticViewportHeight,
            diagnosticExtentHeight,
            diagnosticOffsetTop,
            diagnosticOffsetMessageEnd,
            diagnosticsViewer.Offset.Y,
            diagnosticTextBoundsTop,
            diagnosticTextBoundsMessageEnd,
            diagnosticTextBoundsListEnd,
            layoutTailBoundsListEnd,
            topCaptureFile,
            messageEndCaptureFile,
            listEndCaptureFile,
            source.SourceText,
            sourceAndDiagnosticHeight,
            codePanelGrid?.MaxHeight,
            bannerText.Text,
            note.Text,
            ToolTip.GetTip(note)?.ToString(),
            viewModel.LatexCode,
            viewModel.LatexCodeDiagnostic,
            viewModel.CanExportLatex,
            textDiagnostics,
            viewModel.LatexIncompleteSlotNodeIds.ToArray(),
            expandedExpanderBounds,
            codePanelBounds,
            bannerBounds,
            diagnosticTextBounds,
            noticeBounds,
            sourceBounds,
            editorViewportBounds,
            sessionState,
            collapsedSession);
        File.WriteAllText(Path.Combine(directory, "latex-diagnostic-shell.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        diagnosticsViewer.Offset = new Vector(diagnosticsViewer.Offset.X, 0);
        await DrainLayoutAsync(window);
        Console.WriteLine($"TEXT-ROLE-UI LaTeX 증거 {directory}");
        return evidence;
    }

    private static void SaveWholeWindowPng(Window window, int width, int height, string path)
    {
        EnsureStubHandle(window, "LaTeX 진단 전체 창 PNG 저장 전");
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
    }

    private static BoundsEvidence? TryBoundsRelativeTo(Control control, Control relativeTo)
    {
        var origin = control.TranslatePoint(new Point(0, 0), relativeTo);
        return origin is Point point
            ? new BoundsEvidence(point.X, point.Y, control.Bounds.Width, control.Bounds.Height)
            : null;
    }

    private static void AssertLatexDiagnosticEvidence(
        MainWindow window, MainWindowViewModel viewModel, LatexDiagnosticEvidence evidence)
    {
        EnsureStubHandle(window, "LaTeX expanded 진단 evidence 검증 전");
        var windowBounds = ToRect(evidence.WindowBounds);
        var amsmathTooltip = evidence.AmsmathTooltip ?? string.Empty;
        var diagnostic = evidence.Diagnostic ?? string.Empty;
        Qa.Check(evidence.CaseId == CaseId && evidence.HandleDescriptor == "STUB" && evidence.Handle == 0,
            $"LaTeX JSON evidence의 Headless 경계가 STUB/0이 아닙니다: " +
            $"case={evidence.CaseId}, descriptor={evidence.HandleDescriptor}, handle={evidence.Handle}.");
        Qa.Check(evidence.ExpanderExpanded && evidence.ExpanderVisible,
            "전용 진단 캡처가 LaTeX 패널을 펼친 상태에서 이루어지지 않았습니다.");
        Qa.Check(evidence.DiagnosticBannerVisible && evidence.DiagnosticTextVisible &&
                 !string.IsNullOrWhiteSpace(evidence.DiagnosticText),
            "차단 진단 banner가 펼친 LaTeX 패널에 표시되지 않습니다.");
        Qa.Check(evidence.LayoutBannerVisible && evidence.CanExportLatex == false &&
                 evidence.Diagnostic?.Contains("행 간격", StringComparison.Ordinal) == true,
            "비차단 행 간격 안내가 차단형 LaTeX 진단과 별도 상태로 표시되지 않습니다.");
        Qa.Check(evidence.AmsmathNoticeVisible && evidence.AmsmathNotice?.Contains("amsmath", StringComparison.Ordinal) == true &&
                 amsmathTooltip.Contains("amsmath", StringComparison.Ordinal) &&
                 amsmathTooltip.Contains("리터럴", StringComparison.Ordinal),
            "일반 텍스트·조건과 리터럴 표기의 amsmath 의존 안내가 코드 패널에 보이지 않습니다.");
        Qa.Check(evidence.SourceIsReadOnly && !string.IsNullOrWhiteSpace(evidence.SourceText) &&
                 evidence.SourceText == evidence.LatexCode && evidence.LatexCode.Contains('\u0001'),
            "LaTeX source가 읽기 전용 원문 표시를 유지하지 않습니다.");
        Qa.Check(evidence.CanExportLatex == false && evidence.TextDiagnostics.Length == 1 &&
                 evidence.TextDiagnostics[0].CodePoint == 0x0001 && evidence.IncompleteSlotNodeIds.Length == 1,
            "제어 문자·필수 빈 슬롯의 LaTeX 차단 상태가 evidence에 반영되지 않았습니다.");
        Qa.Check(diagnostic.Contains("제어 문자", StringComparison.Ordinal) &&
                 diagnostic.Contains("미완성", StringComparison.Ordinal),
            "LaTeX 차단 배너 사유에 제어 문자와 필수 슬롯 상태가 모두 설명되지 않습니다.");
        Qa.Check(evidence.ExpandedExpanderBounds is not null && evidence.CodePanelBounds is not null &&
                 evidence.BannerBounds is not null && evidence.DiagnosticTextBounds is not null &&
                 evidence.AmsmathNoticeBounds is not null && evidence.SourceBounds is not null &&
                 evidence.EditorViewportBounds is not null && evidence.DiagnosticsViewportBounds is not null &&
                 evidence.DiagnosticTextBoundsTop is not null &&
                 evidence.DiagnosticTextBoundsMessageEnd is not null && evidence.LayoutTailBoundsListEnd is not null,
            "LaTeX expanded 진단 control 중 하나 이상에서 실제 bounds를 얻지 못했습니다.");
        var expanderBounds = ToRect(evidence.ExpandedExpanderBounds!);
        var codePanelBounds = ToRect(evidence.CodePanelBounds!);
        var bannerBounds = ToRect(evidence.BannerBounds!);
        var diagnosticTextBounds = ToRect(evidence.DiagnosticTextBounds!);
        var noticeBounds = ToRect(evidence.AmsmathNoticeBounds!);
        var sourceBounds = ToRect(evidence.SourceBounds!);
        var editorBounds = ToRect(evidence.EditorViewportBounds!);
        var diagnosticsViewportBounds = ToRect(evidence.DiagnosticsViewportBounds!);
        Qa.Check(Contains(windowBounds, expanderBounds) && Contains(windowBounds, codePanelBounds) &&
                 Contains(windowBounds, noticeBounds) && Contains(windowBounds, sourceBounds) &&
                 Contains(windowBounds, diagnosticsViewportBounds) && Contains(windowBounds, editorBounds),
            "LaTeX expanded 진단 control 또는 편집 viewport가 창 경계 밖으로 잘립니다.");
        Qa.Check(Contains(expanderBounds, codePanelBounds) && Contains(bannerBounds, diagnosticTextBounds) &&
                 Contains(codePanelBounds, diagnosticsViewportBounds) && Contains(codePanelBounds, sourceBounds) &&
                 !Overlaps(expanderBounds, editorBounds),
            "LaTeX 코드 패널 내부 bounds가 맞지 않거나 편집 viewport와 겹칩니다.");
        Qa.Check(evidence.SourceBounds!.Height >= 90 &&
                 evidence.SourceScrollBarVisibility == ScrollBarVisibility.Auto,
            "LaTeX source가 최소 90 DIP를 확보하거나 자체 세로 스크롤을 유지하지 않습니다.");
        Qa.Check(evidence.CodePanelMaxHeight is double maxHeight && Math.Abs(maxHeight - 160) <= 0.01 &&
                 evidence.CodePanelBounds!.Height <= 161 && evidence.SourceAndDiagnosticHeight <= 161,
            $"LaTeX 진단과 source가 기존 160 DIP 코드 영역 상한 안에 배치되지 않았습니다: " +
            $"panel={evidence.CodePanelBounds!.Height:R}, source+diagnostic={evidence.SourceAndDiagnosticHeight:R}, " +
            $"MaxHeight={evidence.CodePanelMaxHeight:R}.");
        Qa.Check(evidence.DiagnosticsScrollBarVisibility == ScrollBarVisibility.Auto &&
                 evidence.DiagnosticsExtentHeight > evidence.DiagnosticsViewportHeight + 1 &&
                 evidence.DiagnosticsOffsetTop <= 0.5 &&
                 evidence.DiagnosticsOffsetListEnd >= evidence.DiagnosticsExtentHeight - evidence.DiagnosticsViewportHeight - 0.5,
            $"LaTeX 진단 전문이 실제 스크롤 영역을 넘치거나 끝까지 접근할 수 없습니다: " +
            $"viewport={evidence.DiagnosticsViewportHeight:R}, extent={evidence.DiagnosticsExtentHeight:R}, " +
            $"offsets={evidence.DiagnosticsOffsetTop:R}/{evidence.DiagnosticsOffsetMessageEnd:R}/{evidence.DiagnosticsOffsetListEnd:R}.");
        var diagnosticViewportLocal = new Rect(0, 0, diagnosticsViewportBounds.Width,
            evidence.DiagnosticsViewportHeight);
        var diagnosticTopBounds = ToRect(evidence.DiagnosticTextBoundsTop!);
        var diagnosticMessageEndBounds = ToRect(evidence.DiagnosticTextBoundsMessageEnd!);
        var layoutTailBounds = ToRect(evidence.LayoutTailBoundsListEnd!);
        var diagnosticAtTopReadable = diagnosticTopBounds.Top >= -0.5 &&
                                      diagnosticTopBounds.Top < evidence.DiagnosticsViewportHeight &&
                                      diagnosticTopBounds.Bottom > 0;
        var diagnosticAtMessageEndReadable = diagnosticMessageEndBounds.Height <=
            evidence.DiagnosticsViewportHeight + 0.5
            ? Contains(diagnosticViewportLocal, diagnosticMessageEndBounds)
            : diagnosticMessageEndBounds.Top < evidence.DiagnosticsViewportHeight &&
              diagnosticMessageEndBounds.Bottom <= evidence.DiagnosticsViewportHeight + 0.5 &&
              diagnosticMessageEndBounds.Bottom > 0;
        Qa.Check(diagnosticAtTopReadable &&
                 diagnosticAtMessageEndReadable && Contains(diagnosticViewportLocal, layoutTailBounds),
            $"LaTeX 차단 진단의 시작/말미 또는 layout advisory의 끝까지 실제 스크롤로 접근할 수 없습니다: " +
            $"top={diagnosticTopBounds}, messageEnd={diagnosticMessageEndBounds}, tail={layoutTailBounds}, " +
            $"viewport={diagnosticViewportLocal}.");
        Qa.Check(evidence.CollapsedExpanderBounds is not null &&
                 evidence.ExpanderGrowth is double growth && growth > 0 &&
                 evidence.ViewportLoss >= 0 && evidence.LayoutDeltaError is double deltaError && deltaError <= 2,
            $"collapsed→expanded viewport 감소가 Expander의 실제 증가분과 일치하지 않습니다: " +
            $"Expander 증가={evidence.ExpanderGrowth:R}, viewport 감소={evidence.ViewportLoss:R}, " +
            $"차이={evidence.LayoutDeltaError:R} DIP.");
        Qa.Check(evidence.SessionState.Workspace == Qa.Fingerprint(viewModel.Session.Workspace),
            "expanded 진단 증거와 현재 Session의 수식 지문이 다릅니다.");
        Qa.Equal(evidence.CollapsedSessionState, evidence.SessionState,
            "collapsed→expanded 진단 표시가 전체 Session 상태를 변경했습니다.");
    }

    private static BoundsEvidence BoundsOf(Control control, Window window)
    {
        return TryBoundsOf(control, window)
            ?? throw new InvalidOperationException("LaTeX 증거 control의 창 좌표를 얻지 못했습니다: " + control.Name);
    }

    private static BoundsEvidence? TryBoundsOf(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        return origin is Point point
            ? new BoundsEvidence(point.X, point.Y, control.Bounds.Width, control.Bounds.Height)
            : null;
    }

    private static Rect ToRect(BoundsEvidence bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static bool Overlaps(Rect first, Rect second) =>
        first.Left < second.Right && first.Right > second.Left &&
        first.Top < second.Bottom && first.Bottom > second.Top;

    private static (EquationWorkspace Workspace, Guid SequenceId, Guid AutomaticTextId) CreateRoleFixture()
    {
        var automatic = Qa.Text("A1V");
        var equals = Qa.Text(" = ") with { Role = MathTextRole.MathSymbol };
        var number = Qa.Text("12") with { Role = MathTextRole.Number };
        var unit = Qa.Text(" m") with { Role = MathTextRole.Unit };
        var condition = Qa.Text(", if x>0") with { Role = MathTextRole.Condition };
        var root = Qa.Sequence(automatic, equals, number, unit, condition);
        return (Qa.Workspace(root), root.Id, automatic.Id);
    }

    private static (EquationWorkspace Workspace, Guid ControlTextId, Guid EmptySlotId) CreateDiagnosticFixture()
    {
        var controlText = Qa.Text("A\u0001B") with { Role = MathTextRole.PlainText };
        var emptySlot = Qa.Text(string.Empty, isEmptySlot: true);
        var fraction = new FractionNode(Guid.NewGuid(), Qa.Text("n"), emptySlot);
        var workspace = Qa.Workspace(Qa.Sequence(controlText, fraction));
        var layoutRows = ImmutableArray.Create(
            new EquationRow(Guid.NewGuid(), Qa.Sequence(Qa.Text("u"), Qa.Text("="), Qa.Text("0"))),
            new EquationRow(Guid.NewGuid(), Qa.Sequence(Qa.Text("v"), Qa.Text("="), Qa.Text("1"))));
        var layoutGroup = new EquationGroup(Guid.NewGuid(), layoutRows, AlignmentMode.Equals, RowSpacing: 41);
        return (workspace with { Groups = workspace.Groups.Add(layoutGroup) }, controlText.Id, emptySlot.Id);
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
        Qa.Equal(expected.Selection, current.Selection, label + " 부분 선택");
        Qa.Equal(expected.SelectedNodeId, current.SelectedNodeId, label + " 선택 노드");
        Qa.Equal(expected.IsNodeSelected, current.IsNodeSelected, label + " 전체 노드 선택");
        Qa.Equal(expected.IsDirty, current.IsDirty, label + " dirty 상태");
        Qa.Equal(expected.CanUndo, current.CanUndo, label + " undo 상태");
        Qa.Equal(expected.CanRedo, current.CanRedo, label + " redo 상태");
    }

    private static void AssertSelectionPreserved(SessionState expected, EditingSession actual, string label)
    {
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " 부분 선택");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " 선택 노드");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " 전체 노드 선택");
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
            $"{phase}: Headless 전용 창의 STUB/0 handle을 확인하지 못했습니다.");
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
        EnsureStubHandle(window, "소유 Headless 창 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "텍스트 역할 UI가 소유한 Headless 창을 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "소유 Headless 창 종료 뒤 실제/native window handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static Control FindControl(MainWindow window, string name) =>
        window.FindControl<Control>(name) ?? throw new InvalidOperationException("XAML control을 찾지 못했습니다: " + name);

    private static T Find<T>(MainWindow window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException("XAML control을 찾지 못했습니다: " + name);

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private sealed record LatexDiagnosticEvidence(
        string CaseId,
        string Directory,
        string HandleDescriptor,
        long Handle,
        BoundsEvidence WindowBounds,
        double CollapsedViewportHeight,
        double ExpandedViewportHeight,
        BoundsEvidence? CollapsedExpanderBounds,
        BoundsEvidence? ExpandedExpanderBounds,
        double? ExpanderGrowth,
        double ViewportLoss,
        double? LayoutDeltaError,
        bool ExpanderExpanded,
        bool ExpanderVisible,
        bool DiagnosticBannerVisible,
        bool LayoutBannerVisible,
        bool DiagnosticTextVisible,
        bool AmsmathNoticeVisible,
        bool SourceIsReadOnly,
        ScrollBarVisibility SourceScrollBarVisibility,
        ScrollBarVisibility DiagnosticsScrollBarVisibility,
        BoundsEvidence? DiagnosticsViewportBounds,
        double DiagnosticsViewportHeight,
        double DiagnosticsExtentHeight,
        double DiagnosticsOffsetTop,
        double DiagnosticsOffsetMessageEnd,
        double DiagnosticsOffsetListEnd,
        BoundsEvidence? DiagnosticTextBoundsTop,
        BoundsEvidence? DiagnosticTextBoundsMessageEnd,
        BoundsEvidence? DiagnosticTextBoundsListEnd,
        BoundsEvidence? LayoutTailBoundsListEnd,
        string TopCaptureFile,
        string MessageEndCaptureFile,
        string ListEndCaptureFile,
        string? SourceText,
        double SourceAndDiagnosticHeight,
        double? CodePanelMaxHeight,
        string? DiagnosticText,
        string? AmsmathNotice,
        string? AmsmathTooltip,
        string LatexCode,
        string Diagnostic,
        bool CanExportLatex,
        LatexTextDiagnosticEvidence[] TextDiagnostics,
        Guid[] IncompleteSlotNodeIds,
        BoundsEvidence? ExpanderBounds,
        BoundsEvidence? CodePanelBounds,
        BoundsEvidence? BannerBounds,
        BoundsEvidence? DiagnosticTextBounds,
        BoundsEvidence? AmsmathNoticeBounds,
        BoundsEvidence? SourceBounds,
        BoundsEvidence? EditorViewportBounds,
        SessionState SessionState,
        SessionState CollapsedSessionState);

    private sealed record LatexTextDiagnosticEvidence(Guid NodeId, string Role, int Utf16Index,
        int CodePoint, string Reason, string CodePointLabel);

    private static string CreateArtifactDirectory(string name)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "doc", "verification", "artifacts", ArtifactScope,
            $"{name}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AetherMath.sln")))
                return current.FullName;
        throw new DirectoryNotFoundException("AetherMath.sln 기준 저장소 루트를 찾지 못했습니다.");
    }

    private sealed record SessionState(string Workspace, Guid StateId, long Revision,
        MathCaret? Caret, MathSelection? Selection, Guid? SelectedNodeId, bool IsNodeSelected,
        bool IsDirty, bool CanUndo, bool CanRedo);

    private sealed record BoundsEvidence(double X, double Y, double Width, double Height);

    private sealed record RoleCapture(string Role, string FileName, string PngSha256,
        int PixelWidth, int PixelHeight, BoundsEvidence EditorBounds, Guid NodeId,
        string NodeText, string StoredRole, string WorkspaceFingerprint, string Purpose);
}
