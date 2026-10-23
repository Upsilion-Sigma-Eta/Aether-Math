using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class DfracPinUiInteractionCases
{
    private const string CaseId = "DFRAC-PIN-UI";
    private const string ArtifactScope = "p2-dfrac-pin";
    private static MainWindow? _ownedWindow;
    private static TempDirectory? _ownedFixtureDirectory;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "분수의 Display pin 의도, 통합 적용·실행 취소, 범위 제한과 LaTeX dfrac 안내를 Headless 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 분수 조판 pin UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("분수 조판 pin UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
        startInfo.ArgumentList.Add("--dfrac-pin-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("분수 조판 pin UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("분수 조판 pin UI Headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"분수 조판 pin UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("분수 조판 pin UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var fixtureDirectory = new TempDirectory();
        _ownedFixtureDirectory = fixtureDirectory;
        var workspaceFiles = new WorkspaceFileService();
        var numerator = Qa.Text("x+1") with { Role = MathTextRole.Identifier };
        var denominator = Qa.Text("n");
        var fraction = new FractionNode(Guid.NewGuid(), numerator, denominator);
        var otherText = Qa.Text("+ c");
        var root = Qa.Sequence(Qa.Text("S = "), fraction, otherText);
        var workspace = Qa.Workspace(root);
        Qa.Equal(MathTypesettingStyle.Display, workspace.Style.TypesettingStyle,
            "분수 pin fixture의 기본 조판 preset");
        var fixturePath = fixtureDirectory.File("dfrac-pin-workspace.aethermath");
        await workspaceFiles.SaveAsync(fixturePath, new EditingSession(workspace).CaptureSnapshot());
        Qa.Check(File.Exists(fixturePath), "분수 pin UI fixture가 격리 TempDirectory에 저장되지 않았습니다.");

        var viewModel = new MainWindowViewModel(workspaceFiles);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(fixturePath),
            "명시 TempDirectory 경로로 분수 pin 작업 열기");
        var session = viewModel.Session;
        Qa.Check(session.TrySelectNode(fraction.Id), "분수 pin fixture의 FractionNode를 명시 선택하지 못했습니다.");
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Check(!session.IsDirty, "분수 pin fixture가 저장된 기준 상태가 아닙니다.");

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
        Qa.Check(ReferenceEquals(editor.Session, session) && ReferenceEquals(viewModel.Session, session),
            "분수 pin UI가 원 Session과 실제 MathEditorControl을 교체했습니다.");

        var properties = viewModel.Properties;
        var scopeCombo = Find<ComboBox>(window, "PropertyScopeComboBox");
        scopeCombo.SelectedValue = MathPropertyScope.SelectionNode;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.SelectionNode, properties.SelectedScope,
            "속성 scope ComboBox의 실제 양방향 SelectionNode 바인딩");
        Qa.Check(properties.IsFractionSelection && properties.CanPinTypesettingStyle &&
                 !properties.DraftPinTypesettingStyle && properties.ExplicitTypesettingStyle is null,
            "상속 Display 분수의 pin 시작 상태가 잘못되었습니다.");
        Qa.Check(properties.FractionDisplayNotice.Contains("\\frac", StringComparison.Ordinal) &&
                 properties.FractionDisplayNotice.Contains("\\dfrac", StringComparison.Ordinal),
            "상속 Display 분수의 \\frac/명시 pin \\dfrac 안내가 표시되지 않습니다.");

        var minimumBaseline = CaptureSession(session);
        await CapturePanelEdgesAsync(window, "dfrac-pin-initial-minimum");
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "기본 크기 재배치 후");
        await CapturePanelEdgesAsync(window, "dfrac-pin-initial-default");
        AssertSessionUnchanged(minimumBaseline, session, "최소/기본 창 캡처");

        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);

        var styleCombo = Find<ComboBox>(window, "TypesettingStyleComboBox");
        var applyButton = Find<Button>(window, "ApplyPropertiesButton");
        var pinCheckBox = Find<CheckBox>(window, "TypesettingPinCheckBox");
        var pinNotice = Find<TextBlock>(window, "TypesettingPinNotice");
        var pinDisabledReason = Find<TextBlock>(window, "TypesettingPinDisabledReason");
        var fractionNotice = Find<TextBlock>(window, "FractionDisplayNotice");
        var displayChoice = properties.TypesettingStyleChoices.Single(choice => choice.Value == MathTypesettingStyle.Display);
        var samePresetBaseline = CaptureSession(session);
        styleCombo.SelectedItem = displayChoice;
        await DrainLayoutAsync(window);
        Qa.Equal<MathTypesettingStyle?>(MathTypesettingStyle.Display, properties.DraftTypesettingStyle,
            "현재 값과 같은 Display preset ComboBox 선택 전달");
        Qa.Check(ReferenceEquals(displayChoice, properties.SelectedTypesettingStyleChoice) &&
                 ReferenceEquals(displayChoice, styleCombo.SelectedItem),
            "Display 조판 선택이 고정 choice 객체로 표시되지 않습니다.");
        Qa.Check(!properties.DraftPinTypesettingStyle && !properties.CanApply && !applyButton.IsEffectivelyEnabled,
            "같은 상속 Display preset 선택만으로 명시 적용이 준비되었습니다.");
        AssertSessionUnchanged(samePresetBaseline, session, "같은 Display preset 선택 no-op");
        Qa.Check(pinNotice.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(pinNotice.Text),
            "pin 초안의 명시 저장 의미가 설명되지 않습니다.");

        await BringPinCardIntoViewAsync(window, includeFractionNotice: true);
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-detail-minimum", ArtifactScope);
        var pinDraftBaseline = CaptureSession(session);
        await ClickControlAsync(window, pinCheckBox);
        Qa.Check(properties.DraftPinTypesettingStyle && properties.CanApply && applyButton.IsEffectivelyEnabled,
            "pin checkbox 입력이 명시 적용 초안과 기존 Apply 활성화로 전달되지 않습니다.");
        Qa.Check(properties.FractionDisplayNotice.Contains("적용", StringComparison.Ordinal) &&
                 properties.FractionDisplayNotice.Contains("\\dfrac", StringComparison.Ordinal),
            "pin 초안의 분수-specific dfrac 결과가 안내되지 않습니다.");
        Qa.Check(properties.IsFractionSelection && properties.ExplicitTypesettingStyle is null &&
                 properties.DraftPinTypesettingStyle,
            "pin 초안이 상속 분수의 명시 Display 적용 예정 상태를 유지하지 않습니다.");
        Qa.Check(string.Equals(fractionNotice.Text, properties.FractionDisplayNotice, StringComparison.Ordinal) &&
                 properties.FractionDisplayNotice.Contains("amsmath", StringComparison.Ordinal),
            "pin 초안의 분수 안내가 실제 컨트롤과 동기화되거나 amsmath 의미를 설명하지 않습니다.");
        AssertSessionUnchanged(pinDraftBaseline, session, "pin 의도 checkbox 초안");
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-draft-minimum", ArtifactScope);

        await ClickControlAsync(window, applyButton);
        await DrainLayoutAsync(window);
        Qa.Equal(pinDraftBaseline.Revision + 1, session.Revision, "pin + 현재 preset의 단일 통합 Apply");
        var pinnedFraction = Qa.Node(session, fraction.Id) as FractionNode
            ?? throw new InvalidOperationException("pin Apply 뒤 FractionNode 식별자가 보존되지 않았습니다.");
        Qa.Equal<MathTypesettingStyle?>(MathTypesettingStyle.Display, pinnedFraction.StyleOverride?.TypesettingStyle,
            "pin Apply가 현재 Display preset을 FractionNode에 명시 저장하지 않았습니다.");
        Qa.Check(viewModel.LatexCode.Contains(@"\dfrac", StringComparison.Ordinal),
            "FractionNode 자체의 명시 Display pin이 LaTeX \\dfrac 출력을 만들지 않았습니다.");
        AssertSelectionPreserved(pinDraftBaseline, session, "pin Apply Fraction 선택 보존");
        Qa.Check(ReferenceEquals(editor.Session, session) && ReferenceEquals(viewModel.Session, session),
            "pin Apply가 editor 또는 Session 인스턴스를 교체했습니다.");
        Qa.Check(properties.ExplicitTypesettingStyle == MathTypesettingStyle.Display &&
                 properties.FractionDisplayNotice.Contains("명시", StringComparison.Ordinal) &&
                 fractionNotice.IsEffectivelyVisible,
            "적용 후 명시 Display / Fraction 전용 dfrac 안내가 동기화되지 않았습니다.");
        await BringPinCardIntoViewAsync(window, includeFractionNotice: true);
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-applied-minimum", ArtifactScope);

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        await BringPinCardIntoViewAsync(window, includeFractionNotice: true);
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-applied-default", ArtifactScope);
        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);

        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        var undoneFraction = Qa.Node(session, fraction.Id) as FractionNode
            ?? throw new InvalidOperationException("pin 실행 취소 뒤 FractionNode가 보존되지 않았습니다.");
        Qa.Equal<MathTypesettingStyle?>(null, undoneFraction.StyleOverride?.TypesettingStyle,
            "pin Apply의 실행 취소가 local Display override를 제거하지 않았습니다.");
        Qa.Check(viewModel.LatexCode.Contains(@"\frac", StringComparison.Ordinal) &&
                 !viewModel.LatexCode.Contains(@"\dfrac", StringComparison.Ordinal),
            "pin 실행 취소 뒤 LaTeX가 inherited \\frac 상태로 복구되지 않았습니다.");
        Qa.Check(!properties.DraftPinTypesettingStyle && !properties.CanApply,
            "pin 실행 취소 뒤 UI가 이전 pin intent를 다시 적용 대상으로 남겼습니다.");
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-undo-minimum", ArtifactScope);

        await ClickControlAsync(window, pinCheckBox);
        await ClickControlAsync(window, applyButton);
        await DrainLayoutAsync(window);
        Qa.Equal<MathTypesettingStyle?>(MathTypesettingStyle.Display,
            (Qa.Node(session, fraction.Id) as FractionNode)?.StyleOverride?.TypesettingStyle,
            "상속 초기화 검증 전 다시 pin 적용");
        var resetBaseline = CaptureSession(session);
        var numeratorRole = Qa.Leaf(session, numerator.Id).Role;
        Qa.Equal(MathTextRole.Identifier, numeratorRole,
            "상속 초기화 fixture가 검증 가능한 명시 텍스트 역할을 사용하지 않습니다.");
        await ClickButtonAsync(window, "ResetPropertiesOverrideButton");
        await DrainLayoutAsync(window);
        var resetFraction = Qa.Node(session, fraction.Id) as FractionNode
            ?? throw new InvalidOperationException("스타일 상속 초기화 뒤 FractionNode가 보존되지 않았습니다.");
        Qa.Equal<MathStyleOverride?>(null, resetFraction.StyleOverride,
            "상속 초기화가 분수의 네 조판 스타일 override 전체를 제거하지 않았습니다.");
        Qa.Equal(numeratorRole, Qa.Leaf(session, numerator.Id).Role,
            "조판 스타일 상속 초기화가 하위 텍스트 역할을 변경했습니다.");
        Qa.Equal(resetBaseline.Revision + 1, session.Revision, "pin 상속 초기화의 단일 undo 단계");
        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal<MathTypesettingStyle?>(MathTypesettingStyle.Display,
            (Qa.Node(session, fraction.Id) as FractionNode)?.StyleOverride?.TypesettingStyle,
            "pin 상속 초기화 undo가 명시 Display preset을 복원하지 않았습니다.");
        await ClickButtonAsync(window, "UndoButton");
        await DrainLayoutAsync(window);
        Qa.Equal<MathTypesettingStyle?>(null,
            (Qa.Node(session, fraction.Id) as FractionNode)?.StyleOverride?.TypesettingStyle,
            "초기화 전 pin Apply undo가 원래 inherited 상태로 복구되지 않았습니다.");

        Qa.Check(session.TrySelectNode(fraction.Id), "stale pin fixture의 분수 선택");
        await DrainLayoutAsync(window);
        var staleBaseline = CaptureSession(session);
        await ClickControlAsync(window, pinCheckBox);
        Qa.Check(properties.DraftPinTypesettingStyle && properties.CanApply,
            "stale 검증을 위한 pin intent 초안을 만들지 못했습니다.");
        Qa.Check(session.TrySelectNode(otherText.Id), "pin 초안 대상 변경용 sibling TextNode 선택");
        await DrainLayoutAsync(window);
        Qa.Check(properties.IsDraftStale && properties.DraftPinTypesettingStyle && !properties.CanApply &&
                 !applyButton.IsEffectivelyEnabled && !string.IsNullOrWhiteSpace(properties.StaleReason),
            "대상이 바뀐 pin 초안이 보존되면서 적용 차단/stale 사유로 표시되지 않습니다.");
        var unchangedFraction = Qa.Node(session, fraction.Id) as FractionNode
            ?? throw new InvalidOperationException("stale 검증에서 원 분수 노드를 찾지 못했습니다.");
        Qa.Equal<MathTypesettingStyle?>(null, unchangedFraction.StyleOverride?.TypesettingStyle,
            "stale pin draft가 원 분수에 명시 조판 스타일을 저장했습니다.");
        SetScrollEdge(window, Find<ScrollViewer>(window, "PropertiesScrollViewer"), top: false);
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-stale-bottom", ArtifactScope);
        await ClickButtonAsync(window, "ResetPropertiesDraftButton");
        await DrainLayoutAsync(window);
        Qa.Check(!properties.IsDraftStale && !properties.DraftPinTypesettingStyle && !properties.CanApply,
            "초안 되돌리기가 stale pin 의도를 남겨 두었습니다.");
        Qa.Equal(staleBaseline.Workspace, Qa.Fingerprint(session.Workspace),
            "stale 검증 선택 이동이 Session 문서 내용을 변경했습니다.");

        var workspaceScopeBaseline = CaptureSession(session);
        scopeCombo.SelectedValue = MathPropertyScope.WorkspaceDefault;
        await DrainLayoutAsync(window);
        Qa.Equal(MathPropertyScope.WorkspaceDefault, properties.SelectedScope,
            "작업 기본값 scope의 양방향 binding");
        Qa.Check(!properties.CanPinTypesettingStyle && !pinCheckBox.IsEffectivelyEnabled &&
                 !string.IsNullOrWhiteSpace(properties.TypesettingPinDisabledReason) &&
                 pinDisabledReason.IsEffectivelyVisible && !properties.IsFractionSelection &&
                 !fractionNotice.IsEffectivelyVisible,
            "작업 기본값에서 pin이 비활성화되거나 분수 안내가 숨겨지지 않았습니다.");
        Qa.Check(properties.TypesettingPinDisabledReason.Contains("작업 기본값", StringComparison.Ordinal),
            "작업 기본값 pin 비활성 사유가 실제 범위를 설명하지 않습니다.");
        Qa.Check(!properties.DraftPinTypesettingStyle,
            "작업 기본값에서 비활성 pin checkbox가 draft를 변경했습니다.");
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-workspace-disabled-minimum", ArtifactScope);

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        await BringPinCardIntoViewAsync(window, includeFractionNotice: false);
        RowShellVisualCases.CaptureAndVerify(window, "dfrac-pin-workspace-disabled-default", ArtifactScope);
        AssertSessionUnchanged(workspaceScopeBaseline, session, "작업 기본값 pin 비활성 상태");
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

    private static async Task BringPinCardIntoViewAsync(MainWindow window, bool includeFractionNotice)
    {
        var scroll = Find<ScrollViewer>(window, "PropertiesScrollViewer");
        var controls = new List<Control>
        {
            Find<ComboBox>(window, "TypesettingStyleComboBox"),
            Find<CheckBox>(window, "TypesettingPinCheckBox"),
            Find<TextBlock>(window, "TypesettingPinNotice")
        };
        var disabledReason = Find<TextBlock>(window, "TypesettingPinDisabledReason");
        if (disabledReason.IsEffectivelyVisible)
            controls.Add(disabledReason);
        if (includeFractionNotice && Find<TextBlock>(window, "FractionDisplayNotice").IsEffectivelyVisible)
            controls.Add(Find<TextBlock>(window, "FractionDisplayNotice"));
        var positions = controls.Select(control => control.TranslatePoint(new Point(0, 0), scroll)
                ?? throw new InvalidOperationException("pin 카드 요소의 실제 scroll 좌표를 얻지 못했습니다."))
            .ToArray();
        var top = positions.Min(point => point.Y);
        var bottom = controls.Select((control, index) => positions[index].Y + control.Bounds.Height).Max();
        Qa.Check(bottom - top <= scroll.Viewport.Height + 1,
            $"조판 pin 상세가 최소 속성 viewport보다 높습니다: card={bottom - top:R}, viewport={scroll.Viewport.Height:R}.");
        var shift = (top + bottom - scroll.Viewport.Height) / 2;
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + shift, 0, maximum));
        await DrainLayoutAsync(window);
        foreach (var control in controls)
            Qa.Check(IsFullyVisible(window, control), "pin 카드 전체를 볼 수 없습니다: " + control.Name);
    }

    private static async Task ClickButtonAsync(MainWindow window, string name) =>
        await ClickControlAsync(window, Find<Button>(window, name));

    private static async Task ClickControlAsync(MainWindow window, Control control)
    {
        EnsureStubHandle(window, "분수 조판 pin UI 합성 클릭 전");
        Qa.Check(control.IsEffectivelyEnabled, "비활성 pin UI control에 합성 클릭을 시도했습니다: " + control.Name);
        await BringControlIntoViewAsync(window, control);
        var point = GetWindowCenter(control, window);
        var settled = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            EnsureClickTargetVisibleOrCapture(window, control, "분수 pin 합성 클릭 직전", point, verticalMargin: 8);
            window.MouseMove(point, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "분수 pin UI hover 후");
            EnsureClickTargetVisibleOrCapture(window, control, "hover 배치 뒤 분수 pin UI 위치 확인", point,
                verticalMargin: 8);
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
            throw CreateClickFailureException(window, control, "pointer hover 뒤 클릭 좌표가 안정되지 않음", point, 8);
        EnsureClickTargetVisibleOrCapture(window, control, "MouseDown 직전 분수 pin UI 좌표 확인", point,
            verticalMargin: 8);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static async Task BringControlIntoViewAsync(Window window, Control control)
    {
        const double verticalMargin = 8;
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var origin = control.TranslatePoint(new Point(0, 0), scroll);
            Qa.Check(origin is Point, "pin UI scroll 내부 control 좌표를 얻지 못했습니다: " + control.Name);
            var top = origin!.Value.Y;
            var bottom = top + control.Bounds.Height;
            var viewportHeight = scroll.Viewport.Height;
            var margin = Math.Min(verticalMargin, Math.Max(0, (viewportHeight - control.Bounds.Height) / 2));
            if (top >= margin && bottom <= viewportHeight - margin)
                continue;
            var desiredTop = control.Bounds.Height <= viewportHeight - 2 * margin
                ? Math.Max(margin, (viewportHeight - control.Bounds.Height) / 2)
                : 0;
            var offset = scroll.Offset.Y + top - desiredTop;
            var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(offset, 0, maximum));
            await DrainLayoutAsync(window);
        }
    }

    private static bool IsFullyVisible(Window window, Control control, double verticalMargin = 0)
    {
        if (!control.IsEffectivelyVisible)
            return false;
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
            if (scrollOrigin is not Point)
                return false;
            var viewport = new Rect(scrollOrigin!.Value, scroll.Viewport);
            var horizontallyContained = bounds.Left >= viewport.Left - 0.5 &&
                                        bounds.Right <= viewport.Right + 0.5;
            var verticallyContained = bounds.Top >= viewport.Top + verticalMargin - 0.5 &&
                                      bounds.Bottom <= viewport.Bottom - verticalMargin + 0.5;
            if (!horizontallyContained || !verticallyContained)
                return false;
        }
        return true;
    }

    private static void EnsureClickTargetVisibleOrCapture(MainWindow window, Control control, string phase,
        Point? pointerPosition, double verticalMargin)
    {
        if (IsFullyVisible(window, control, verticalMargin))
            return;
        throw CreateClickFailureException(window, control, phase, pointerPosition, verticalMargin);
    }

    private static InvalidOperationException CreateClickFailureException(MainWindow window, Control control,
        string phase, Point? pointerPosition, double verticalMargin)
    {
        var diagnostics = DescribeBounds(window, control) +
                          $"; verticalMargin={verticalMargin:R}; pointer={FormatPoint(pointerPosition)}";
        var directory = CaptureClickFailureEvidence(window, control, phase, diagnostics, pointerPosition,
            verticalMargin);
        return new InvalidOperationException(
            $"{phase}: 분수 pin 합성 클릭 대상이 실제 viewport 안에 완전히 표시되지 않습니다. " +
            diagnostics + $"\n실패 증거 폴더: {directory}");
    }

    private static string CaptureClickFailureEvidence(MainWindow window, Control control, string phase,
        string diagnostics, Point? pointerPosition, double verticalMargin)
    {
        EnsureStubHandle(window, "분수 pin 합성 클릭 가시성 실패 증거 저장 전");
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "doc", "verification", "artifacts", ArtifactScope,
            $"click-visibility-failure-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var width = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height));
        var pngPath = Path.Combine(directory, "main-window.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("분수 pin 실패 증거에 Headless handle이 없습니다.");
        Qa.Check(handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "분수 pin 실패 증거는 STUB/0 Headless 창에서만 저장할 수 있습니다.");
        var controlOrigin = control.TranslatePoint(new Point(0, 0), window);
        var scrollEvidence = control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll => new
        {
            Name = scroll.Name,
            Viewport = new { scroll.Viewport.Width, scroll.Viewport.Height },
            OriginInWindow = scroll.TranslatePoint(new Point(0, 0), window),
            scroll.Offset,
            Extent = new { scroll.Extent.Width, scroll.Extent.Height },
            HorizontalVisibility = ScrollViewer.GetHorizontalScrollBarVisibility(scroll).ToString(),
            VerticalVisibility = ScrollViewer.GetVerticalScrollBarVisibility(scroll).ToString(),
            TargetOriginInViewport = control.TranslatePoint(new Point(0, 0), scroll)
        }).ToArray();
        var evidence = new
        {
            CaseId,
            phase,
            Control = control.Name,
            ControlBounds = new { control.Bounds.X, control.Bounds.Y, control.Bounds.Width, control.Bounds.Height },
            ControlOriginInWindow = controlOrigin,
            WindowClientSize = new { window.ClientSize.Width, window.ClientSize.Height },
            VerticalMargin = verticalMargin,
            Pointer = FormatPoint(pointerPosition),
            HandleDescriptor = handle.HandleDescriptor,
            Handle = handle.Handle.ToInt64(),
            Diagnostics = diagnostics,
            Png = Path.GetFileName(pngPath),
            ScrollViewers = scrollEvidence
        };
        File.WriteAllText(Path.Combine(directory, "bounds.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return directory;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AetherMath.sln")))
                return current.FullName;
        throw new DirectoryNotFoundException("AetherMath.sln 기준 저장소 루트를 찾지 못했습니다.");
    }

    private static Point GetWindowCenter(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("분수 pin 합성 클릭 대상의 창 좌표를 얻지 못했습니다: " + control.Name);
        return origin + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }

    private static string FormatPoint(Point? point) => point is Point value
        ? $"({value.X:R},{value.Y:R})"
        : "없음";

    private static string DescribeBounds(Window window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window);
        var scrolls = control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll =>
        {
            var scrollOrigin = scroll.TranslatePoint(new Point(0, 0), window);
            var targetOrigin = control.TranslatePoint(new Point(0, 0), scroll);
            return $"viewport={scroll.Viewport}, origin={scrollOrigin}, targetInViewport={targetOrigin}, " +
                   $"offset={scroll.Offset}, extent={scroll.Extent}";
        });
        return $"name={control.Name}, bounds={control.Bounds}, origin={origin}, " + string.Join("; ", scrolls);
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
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " whole-node selection");
    }

    private static async Task DrainLayoutAsync(Window window)
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
        EnsureStubHandle(window, "소유 분수 pin Headless 창 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "분수 pin UI가 소유한 Headless 창을 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "분수 pin UI child 종료 뒤 실제/native 창 handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(MainWindow window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException("XAML control을 찾지 못했습니다: " + name);

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
