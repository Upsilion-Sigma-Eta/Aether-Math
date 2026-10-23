using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
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

internal static class BracketKindUiInteractionCases
{
    private const string CaseId = "BRACKET-KIND-UI";
    private const string ArtifactScope = "p2-bracket-kind";
    private static MainWindow? _ownedWindow;
    private static TempDirectory? _ownedFixtureDirectory;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "괄호 모양의 실제 선택 바인딩, 단일 적용·실행 취소, stale 차단과 속성 범위를 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 괄호 모양 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("괄호 모양 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
            _ownedFixtureDirectory?.Dispose();
            _ownedFixtureDirectory = null;
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
        startInfo.ArgumentList.Add("--bracket-kind-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("괄호 모양 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("괄호 모양 UI Headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"괄호 모양 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("괄호 모양 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var fixtureDirectory = new TempDirectory();
        _ownedFixtureDirectory = fixtureDirectory;
        var workspaceFiles = new WorkspaceFileService();
        var contentLeft = Qa.Text("xy");
        var contentRight = Qa.Text("+z");
        var content = Qa.Sequence(contentLeft, contentRight);
        var originalOverride = new MathStyleOverride(FontSize: 34, Foreground: "#345678");
        var brackets = new BracketsNode(Guid.NewGuid(), MathBracketKind.Parentheses, content)
        {
            StyleOverride = originalOverride
        };
        var siblingText = Qa.Text("+w");
        var root = Qa.Sequence(Qa.Text("f="), brackets, siblingText);
        var workspace = Qa.Workspace(root);
        var fixturePath = fixtureDirectory.File("bracket-kind-workspace.aethermath");
        await workspaceFiles.SaveAsync(fixturePath, new EditingSession(workspace).CaptureSnapshot());
        Qa.Check(File.Exists(fixturePath), "괄호 종류 fixture가 격리 TempDirectory에 저장되지 않았습니다.");

        var viewModel = new MainWindowViewModel(workspaceFiles);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(fixturePath),
            "명시 TempDirectory 경로로 괄호 종류 작업 열기");
        var session = viewModel.Session;
        Qa.Check(session.TrySelectNode(brackets.Id), "괄호 종류 fixture의 BracketsNode를 명시 선택하지 못했습니다.");
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Check(!session.IsDirty, "괄호 종류 fixture가 저장된 기준 상태가 아닙니다.");

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

        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        var inspector = Find<TabControl>(window, "EquationInspector");
        var propertiesTab = Find<TabItem>(window, "PropertiesTab");
        inspector.SelectedItem = propertiesTab;
        await DrainLayoutAsync(window);
        Qa.Check(ReferenceEquals(editor.Session, session) && ReferenceEquals(viewModel.Session, session),
            "괄호 종류 UI가 실제 MathEditorControl 또는 Session 인스턴스를 교체했습니다.");

        var properties = viewModel.Properties;
        Qa.Check(properties.CanEditBracketKind && properties.CurrentBracketKind == MathBracketKind.Parentheses,
            "전체 괄호 선택의 현재 모양 또는 편집 가능 상태가 표시되지 않습니다.");
        Qa.Equal<MathBracketKind?>(null, properties.DraftBracketKind, "초기 괄호 모양 draft는 변경 안 함");
        Qa.Check(!properties.CanApply, "변경되지 않은 괄호 draft에서 Apply가 활성화되었습니다.");
        Qa.Check(properties.BracketKindNotice.Contains("내용", StringComparison.Ordinal) &&
                 properties.BracketKindNotice.Contains("하위", StringComparison.Ordinal),
            "괄호 모양 변경이 내용과 하위 구조를 보존한다는 안내가 없습니다.");

        var minimumBaseline = CaptureSession(session);
        await CapturePanelEdgesAsync(window, "bracket-kind-initial-minimum");
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "기본 크기 재배치 후");
        await CapturePanelEdgesAsync(window, "bracket-kind-initial-default");
        await CaptureBracketDetailAsync(window, "bracket-kind-detail-parentheses-default");
        AssertSessionUnchanged(minimumBaseline, session, "최소·기본 창 캡처");
        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);

        var combo = Find<ComboBox>(window, "BracketKindComboBox");
        var apply = Find<Button>(window, "ApplyPropertiesButton");
        var expectedKinds = new MathBracketKind?[]
        {
            null, MathBracketKind.Parentheses, MathBracketKind.Square, MathBracketKind.Curly, MathBracketKind.Angle
        };
        Qa.Check(properties.BracketKindChoices.Select(choice => choice.Value).SequenceEqual(expectedKinds),
            "괄호 종류 choices의 변경 안 함/네 종류 순서가 계약과 다릅니다.");
        await CaptureBracketDetailAsync(window, "bracket-kind-detail-parentheses-minimum");

        var sameChoice = FindChoice(properties, MathBracketKind.Parentheses);
        await SelectBracketChoiceAsync(window, sameChoice);
        Qa.Equal<MathBracketKind?>(MathBracketKind.Parentheses, properties.DraftBracketKind,
            "현재 종류를 다시 고른 실제 ComboBox 선택");
        Qa.Check(ReferenceEquals(sameChoice, properties.SelectedBracketKindChoice) &&
                 ReferenceEquals(sameChoice, combo.SelectedItem) && !properties.CanApply &&
                 !apply.IsEffectivelyEnabled,
            "현재 괄호 종류의 재선택이 stable choice/no-op으로 표시되지 않습니다.");
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        Qa.Equal<MathBracketKind?>(null, properties.DraftBracketKind,
            "초안 되돌리기가 괄호 모양을 변경 안 함 상태로 되돌리지 않았습니다.");

        var expectedContentFingerprint = Qa.NodeFingerprint(content);
        var appliedKinds = new[] { MathBracketKind.Square, MathBracketKind.Curly, MathBracketKind.Angle };
        foreach (var kind in appliedKinds)
        {
            var beforeDraft = CaptureSession(session);
            await SelectBracketChoiceAsync(window, FindChoice(properties, kind));
            Qa.Equal(kind, properties.DraftBracketKind, "괄호 모양 ComboBox의 실제 TwoWay 초안 " + kind);
            Qa.Check(properties.CanApply && apply.IsEffectivelyEnabled,
                "변경된 괄호 종류에서 기존 Apply가 활성화되지 않습니다: " + kind);
            AssertSessionUnchanged(beforeDraft, session, "괄호 종류 초안 " + kind);

            await ClickControlAsync(window, apply);
            await DrainLayoutAsync(window);
            Qa.Equal(beforeDraft.Revision + 1, session.Revision, "괄호 종류 단일 Apply revision " + kind);
            var changedBrackets = GetBrackets(session, brackets.Id);
            Qa.Equal(kind, changedBrackets.Kind, "실제 BracketsNode 적용 종류 " + kind);
            Qa.Equal(expectedContentFingerprint, Qa.NodeFingerprint(changedBrackets.Content),
                "괄호 종류 적용 시 내용/하위 구조 ID 보존 " + kind);
            Qa.Equal(originalOverride, changedBrackets.StyleOverride,
                "괄호 모양 변경 시 기존 조판 스타일 보존 " + kind);
            Qa.Check(properties.DraftBracketKind is null && !properties.CanApply,
                "성공 Apply 뒤 괄호 종류 draft가 초기화되지 않았습니다: " + kind);
            AssertSelectionPreserved(beforeDraft, session, "괄호 종류 Apply 선택 보존 " + kind);
            Qa.Check(ReferenceEquals(editor.Session, session) && ReferenceEquals(viewModel.Session, session),
                "괄호 종류 Apply가 editor/Session 인스턴스를 교체했습니다.");

            await CaptureBracketDetailAsync(window, "bracket-kind-detail-" + kind.ToString().ToLowerInvariant() + "-minimum");

            if (kind == MathBracketKind.Square)
            {
                await ClickButtonAsync(window, "UndoButton");
                await DrainLayoutAsync(window);
                Qa.Equal(MathBracketKind.Parentheses, GetBrackets(session, brackets.Id).Kind,
                    "괄호 종류 Apply undo가 원래 Parentheses를 복원하지 않았습니다.");
                Qa.Equal(originalOverride, GetBrackets(session, brackets.Id).StyleOverride,
                    "괄호 종류 undo가 조판 스타일을 바꿨습니다.");
                await ClickButtonAsync(window, "RedoButton");
                await DrainLayoutAsync(window);
                Qa.Equal(MathBracketKind.Square, GetBrackets(session, brackets.Id).Kind,
                    "괄호 종류 Apply redo가 Square를 복원하지 않았습니다.");
            }
        }

        var beforeStyleReset = CaptureSession(session);
        Qa.Check(properties.CanResetOverride && Find<Button>(window, "ResetPropertiesOverrideButton").IsEffectivelyEnabled,
            "fixture의 BracketsNode 명시 스타일을 초기화할 수 없습니다.");
        await ClickButtonAsync(window, "ResetPropertiesOverrideButton");
        await DrainLayoutAsync(window);
        var styleResetBrackets = GetBrackets(session, brackets.Id);
        Qa.Equal(beforeStyleReset.Revision + 1, session.Revision, "괄호 스타일 초기화의 단일 undo 단계");
        Qa.Equal(MathBracketKind.Angle, styleResetBrackets.Kind,
            "스타일 상속 초기화가 괄호 종류를 함께 초기화했습니다.");
        Qa.Equal<MathStyleOverride?>(null, styleResetBrackets.StyleOverride,
            "스타일 상속 초기화가 BracketsNode 명시 스타일을 제거하지 않았습니다.");
        await CaptureBracketDetailAsync(window, "bracket-kind-detail-style-reset-minimum");
        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathBracketKind.Angle, GetBrackets(session, brackets.Id).Kind,
            "스타일 초기화 undo가 괄호 종류를 바꿨습니다.");
        Qa.Equal(originalOverride, GetBrackets(session, brackets.Id).StyleOverride,
            "스타일 초기화 undo가 원래 스타일을 복원하지 않았습니다.");
        await ClickButtonAsync(window, "RedoButton");
        await DrainLayoutAsync(window);
        Qa.Equal(MathBracketKind.Angle, GetBrackets(session, brackets.Id).Kind,
            "스타일 초기화 redo가 괄호 종류를 바꿨습니다.");

        var staleBaseline = CaptureSession(session);
        await SelectBracketChoiceAsync(window, FindChoice(properties, MathBracketKind.Curly));
        Qa.Check(properties.CanApply && properties.DraftBracketKind == MathBracketKind.Curly,
            "stale 괄호 확인 전에 변경 초안을 만들지 못했습니다.");
        Qa.Check(session.TrySelectNode(contentLeft.Id), "stale 대상을 내부 텍스트로 바꾸지 못했습니다.");
        await DrainLayoutAsync(window);
        Qa.Check(properties.IsDraftStale && properties.DraftBracketKind == MathBracketKind.Curly &&
                 !properties.CanApply && !apply.IsEffectivelyEnabled && !properties.CanEditBracketKind,
            "괄호 초안이 대상 변경 뒤 보존되면서 stale 적용 차단을 유지하지 못합니다.");
        Qa.Equal(MathBracketKind.Angle, GetBrackets(session, brackets.Id).Kind,
            "stale 괄호 초안이 원래 BracketsNode를 바꿨습니다.");
        Qa.Equal(staleBaseline.Workspace, Qa.Fingerprint(session.Workspace),
            "stale 괄호 초안 대상 이동이 문서 내용을 바꿨습니다.");
        await CaptureBracketUnsupportedAsync(window, "bracket-kind-stale-minimum");
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        await DrainLayoutAsync(window);
        Qa.Check(!properties.IsDraftStale && properties.DraftBracketKind is null &&
                 !properties.CanEditBracketKind && !properties.CanApply,
            "초안 되돌리기가 stale 괄호 종류 선택을 제거하거나 현재 내부 텍스트 상태를 다시 읽지 않았습니다.");

        Qa.Check(session.TrySetCaret(new MathCaret(contentLeft.Id, 1)), "괄호 내부 텍스트 caret 위치를 설정하지 못했습니다.");
        await DrainLayoutAsync(window);
        Qa.Check(!properties.CanEditBracketKind && properties.CurrentBracketKind is null &&
                 !string.IsNullOrWhiteSpace(properties.BracketKindDisabledReason),
            "괄호 내부 caret를 전체 괄호 선택처럼 취급했습니다.");
        await CaptureBracketUnsupportedAsync(window, "bracket-kind-unsupported-interior-minimum");
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        await CaptureBracketUnsupportedAsync(window, "bracket-kind-unsupported-interior-default");
        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);

        var beforePartialSelection = CaptureSession(session);
        var partialSelection = new MathSelection(
            new MathCaret(contentLeft.Id, 0), new MathCaret(contentLeft.Id, 1));
        Qa.Check(session.TrySetSelection(partialSelection),
            "괄호 내부 텍스트의 부분 문자 선택을 설정하지 못했습니다.");
        await DrainLayoutAsync(window);
        var afterPartialSelection = CaptureSession(session);
        Qa.Equal(beforePartialSelection.Workspace, afterPartialSelection.Workspace,
            "괄호 내부 부분 선택이 문서 내용을 바꿨습니다.");
        Qa.Equal(beforePartialSelection.StateId, afterPartialSelection.StateId,
            "괄호 내부 부분 선택이 문서 상태 ID를 바꿨습니다.");
        Qa.Equal(beforePartialSelection.Revision, afterPartialSelection.Revision,
            "괄호 내부 부분 선택이 revision을 바꿨습니다.");
        Qa.Equal(beforePartialSelection.IsDirty, afterPartialSelection.IsDirty,
            "괄호 내부 부분 선택이 dirty 상태를 바꿨습니다.");
        Qa.Equal(beforePartialSelection.CanUndo, afterPartialSelection.CanUndo,
            "괄호 내부 부분 선택이 undo 가능 상태를 바꿨습니다.");
        Qa.Equal(beforePartialSelection.CanRedo, afterPartialSelection.CanRedo,
            "괄호 내부 부분 선택이 redo 가능 상태를 바꿨습니다.");
        Qa.Equal(partialSelection, afterPartialSelection.Selection,
            "괄호 내부 부분 선택의 anchor/focus가 보존되지 않았습니다.");
        Qa.Check(!afterPartialSelection.IsNodeSelected,
            "괄호 내부 부분 문자 선택이 전체 수식 구조 선택으로 취급됐습니다.");
        Qa.Check(!properties.CanEditBracketKind && properties.CurrentBracketKind is null &&
                 !string.IsNullOrWhiteSpace(properties.BracketKindDisabledReason),
            "괄호 내부 부분 문자 선택에서 종류 편집이 활성화되거나 비활성 사유가 없습니다.");
        await CaptureBracketUnsupportedAsync(window, "bracket-kind-unsupported-partial-selection-minimum");
        AssertSessionUnchanged(afterPartialSelection, session, "부분 선택 괄호 상세 캡처");

        var scopeCombo = Find<ComboBox>(window, "PropertyScopeComboBox");
        foreach (var scope in new[]
                 { MathPropertyScope.CurrentRow, MathPropertyScope.CurrentGroup, MathPropertyScope.WorkspaceDefault })
        {
            var beforeScope = CaptureSession(session);
            await BringControlIntoViewAsync(window, scopeCombo);
            Qa.Check(IsFullyVisible(window, scopeCombo), "속성 scope 선택기가 실제 viewport 밖에 있습니다.");
            scopeCombo.SelectedValue = scope;
            await DrainLayoutAsync(window);
            Qa.Equal(scope, properties.SelectedScope, "실제 scope ComboBox의 양방향 binding " + scope);
            Qa.Check(!properties.CanEditBracketKind && !combo.IsEffectivelyEnabled &&
                     !string.IsNullOrWhiteSpace(properties.BracketKindDisabledReason),
                "괄호 모양이 SelectionNode 이외 scope에서 활성화되었습니다: " + scope);
            AssertSessionUnchanged(beforeScope, session, "괄호 종류 비활성 scope " + scope);
        }
        scopeCombo.SelectedValue = MathPropertyScope.SelectionNode;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.SelectionNode, properties.SelectedScope, "SelectionNode scope 복귀");
        Qa.Check(!properties.CanEditBracketKind && !combo.IsEffectivelyEnabled,
            "내부 text caret 상태에서 SelectionNode scope 복귀 후 괄호 편집이 활성화되었습니다.");
        await CaptureBracketUnsupportedAsync(window, "bracket-kind-unsupported-selection-minimum");
    }

    private static async Task CapturePanelEdgesAsync(MainWindow window, string prefix)
    {
        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        SetScrollEdge(scroll, top: true);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureBracketKindAndVerify(window, prefix + "-top");
        SetScrollEdge(scroll, top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureBracketKindAndVerify(window, prefix + "-bottom");
    }

    private static async Task CaptureBracketDetailAsync(MainWindow window, string label)
    {
        await BringBracketControlsIntoViewAsync(window, includeDisabledReason: false);
        RowShellVisualCases.CaptureBracketKindAndVerify(window, label);
    }

    private static async Task CaptureBracketUnsupportedAsync(MainWindow window, string label)
    {
        await BringBracketControlsIntoViewAsync(window, includeDisabledReason: true);
        RowShellVisualCases.CaptureBracketKindAndVerify(window, label);
    }

    private static async Task BringBracketControlsIntoViewAsync(MainWindow window, bool includeDisabledReason)
    {
        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        var controls = new List<Control>
        {
            Find<Border>(window, "BracketKindCard"),
            Find<ComboBox>(window, "BracketKindComboBox")
        };
        var currentValue = Find<TextBlock>(window, "CurrentBracketKindValue");
        var notice = Find<TextBlock>(window, "BracketKindNotice");
        var disabledReason = Find<TextBlock>(window, "BracketKindDisabledReason");
        if (currentValue.IsEffectivelyVisible)
            controls.Add(currentValue);
        if (notice.IsEffectivelyVisible)
            controls.Add(notice);
        if (includeDisabledReason && disabledReason.IsEffectivelyVisible)
            controls.Add(disabledReason);

        var positions = controls.Select(control => control.TranslatePoint(new Point(0, 0), scroll)
                ?? throw new InvalidOperationException("괄호 모양 카드의 실제 ScrollViewer 좌표를 얻지 못했습니다."))
            .ToArray();
        var top = positions.Min(point => point.Y);
        var bottom = controls.Select((control, index) => positions[index].Y + control.Bounds.Height).Max();
        Qa.Check(bottom - top <= scroll.Viewport.Height + 1,
            $"괄호 모양 카드의 핵심 컨트롤이 최소 Properties viewport보다 높습니다: card={bottom - top:R}, viewport={scroll.Viewport.Height:R}.");
        var shift = top + (bottom - top - scroll.Viewport.Height) / 2;
        var targetOffset = scroll.Offset.Y + shift;
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(targetOffset, 0, maximum));
        await DrainLayoutAsync(window);
        foreach (var control in controls)
            Qa.Check(IsFullyVisible(window, control),
                "괄호 모양 카드의 실제 컨트롤이 Properties viewport에 완전히 들어오지 않습니다: " + control.Name);
    }

    private static async Task SelectBracketChoiceAsync(MainWindow window, MathBracketKindChoice choice)
    {
        var combo = Find<ComboBox>(window, "BracketKindComboBox");
        EnsureStubHandle(window, "괄호 종류 실제 ComboBox 선택 전");
        await BringControlIntoViewAsync(window, combo);
        Qa.Check(IsFullyVisible(window, combo) && combo.IsEffectivelyEnabled,
            "괄호 종류 선택기가 실제 viewport에서 자르거나 비활성입니다.");
        combo.SelectedItem = choice;
        await DrainLayoutAsync(window);
        var viewModel = FindViewModel(window);
        Qa.Check(ReferenceEquals(choice, combo.SelectedItem) &&
                 ReferenceEquals(choice, viewModel.Properties.SelectedBracketKindChoice),
            "실제 괄호 종류 ComboBox와 stable choice proxy가 동기화되지 않습니다.");
    }

    private static MathBracketKindChoice FindChoice(MathPropertiesPanelViewModel properties, MathBracketKind? value) =>
        properties.BracketKindChoices.Single(choice => choice.Value == value);

    private static BracketsNode GetBrackets(EditingSession session, Guid id) =>
        Qa.Node(session, id) as BracketsNode
        ?? throw new InvalidOperationException("현재 Session에서 같은 BracketsNode ID를 찾지 못했습니다: " + id);

    private static MainWindowViewModel FindViewModel(MainWindow window) =>
        window.DataContext as MainWindowViewModel
        ?? throw new InvalidOperationException("괄호 모양 창에 MainWindowViewModel이 연결되지 않았습니다.");

    private static async Task ClickButtonAsync(MainWindow window, string name) =>
        await ClickControlAsync(window, Find<Button>(window, name));

    private static async Task ClickControlAsync(MainWindow window, Control control)
    {
        EnsureStubHandle(window, "괄호 모양 UI 합성 클릭 전");
        Qa.Check(control.IsEffectivelyEnabled, "비활성 control에 합성 클릭을 시도했습니다: " + control.Name);
        await BringControlIntoViewAsync(window, control);
        var point = GetWindowCenter(control, window);
        var settled = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Qa.Check(IsFullyVisible(window, control, verticalMargin: 8),
                "괄호 모양 UI 클릭 전에 대상이 실제 viewport에서 잘립니다: " + control.Name);
            window.MouseMove(point, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "괄호 모양 UI hover 후");
            Qa.Check(IsFullyVisible(window, control, verticalMargin: 8),
                "hover 후 괄호 모양 UI 클릭 대상이 실제 viewport에서 잘립니다: " + control.Name);
            var updated = GetWindowCenter(control, window);
            if (Math.Abs(updated.X - point.X) <= 0.5 && Math.Abs(updated.Y - point.Y) <= 0.5)
            {
                point = updated;
                settled = true;
                break;
            }
            point = updated;
        }
        Qa.Check(settled, "괄호 모양 UI hover 뒤 실제 클릭 좌표가 안정되지 않았습니다: " + control.Name);
        Qa.Check(IsFullyVisible(window, control, verticalMargin: 8),
            "MouseDown 직전 괄호 모양 UI 클릭 대상이 실제 viewport에서 잘립니다: " + control.Name);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static async Task BringControlIntoViewAsync(MainWindow window, Control control)
    {
        const double verticalMargin = 8;
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var origin = control.TranslatePoint(new Point(0, 0), scroll)
                ?? throw new InvalidOperationException("괄호 모양 UI 대상의 ScrollViewer 좌표를 얻지 못했습니다: " + control.Name);
            var top = origin.Y;
            var bottom = top + control.Bounds.Height;
            var margin = Math.Min(verticalMargin, Math.Max(0, (scroll.Viewport.Height - control.Bounds.Height) / 2));
            if (top >= margin && bottom <= scroll.Viewport.Height - margin)
                continue;
            var desiredTop = control.Bounds.Height <= scroll.Viewport.Height - margin * 2
                ? Math.Max(margin, (scroll.Viewport.Height - control.Bounds.Height) / 2)
                : 0;
            var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X,
                Math.Clamp(scroll.Offset.Y + top - desiredTop, 0, maximum));
            await DrainLayoutAsync(window);
        }
    }

    private static bool IsFullyVisible(MainWindow window, Control control, double verticalMargin = 0)
    {
        if (!control.IsEffectivelyVisible)
            return false;
        var origin = control.TranslatePoint(new Point(0, 0), window);
        if (origin is null)
            return false;
        var bounds = new Rect(origin.Value, control.Bounds.Size);
        if (!Contains(new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height), bounds))
            return false;
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var scrollOrigin = scroll.TranslatePoint(new Point(0, 0), window);
            if (scrollOrigin is null)
                return false;
            var viewport = new Rect(scrollOrigin.Value, scroll.Viewport);
            if (bounds.Left < viewport.Left - 0.5 || bounds.Right > viewport.Right + 0.5 ||
                bounds.Top < viewport.Top + verticalMargin - 0.5 ||
                bounds.Bottom > viewport.Bottom - verticalMargin + 0.5)
                return false;
        }
        return true;
    }

    private static Point GetWindowCenter(Control control, MainWindow window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("괄호 모양 UI control의 창 좌표를 얻지 못했습니다: " + control.Name);
        return origin + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }

    private static void SetScrollEdge(ScrollViewer scroll, bool top)
    {
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
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " whole-node selection");
    }

    private static async Task DrainLayoutAsync(MainWindow window)
    {
        for (var attempt = 0; attempt < 3; attempt++)
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
            $"{phase}: Headless STUB/0 handle을 확인하지 못했습니다.");
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 괄호 모양 UI visual tree에서 {name} 컨트롤을 찾지 못했습니다.");

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
        EnsureStubHandle(window, "소유 괄호 모양 Headless 창 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "괄호 모양 UI가 소유한 Headless 창을 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "괄호 모양 UI child 종료 뒤 실제/native 창 handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

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
}
