using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

internal static class LatexHighlightUiInteractionCases
{
    private const string CaseId = "LATEX-HIGHLIGHT-UI";
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "LaTeX 한 표면의 색상 강조·선택·스크롤·전체 복사 경계를 Headless에서 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 LaTeX 강조 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("LaTeX 강조 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
                throw new InvalidOperationException("dotnet host의 QA 어셈블리 경로를 찾지 못했습니다.");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        startInfo.ArgumentList.Add("--latex-highlight-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("LaTeX 강조 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("LaTeX 강조 UI child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"LaTeX 강조 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("LaTeX 강조 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        using var temporaryDirectory = new TempDirectory();
        var service = new WorkspaceFileService();
        var fixturePath = temporaryDirectory.File("latex-highlight.aethermath");
        var fixture = CreateHighlightWorkspace();
        await service.SaveAsync(fixturePath, new EditingSession(fixture.Workspace).CaptureSnapshot());
        Qa.Check(File.Exists(fixturePath), "LaTeX 강조 fixture가 명시 TempDirectory에 저장되지 않았습니다.");

        var viewModel = new MainWindowViewModel(service);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(fixturePath),
            "명시 경로에서 LaTeX 강조 fixture 열기");
        var window = new MainWindow(viewModel) { Width = 1060, Height = 700, CanResize = true };
        _ownedWindow = window;
        EnsureStubHandle(window, "Show 전");
        window.Show();
        EnsureStubHandle(window, "Show 후");
        await DrainLayoutAsync(window);

        var session = viewModel.Session;
        var expander = Find<Expander>(window, "LatexCodeExpander");
        var surface = Find<LatexCodeSurface>(window, "LatexCodeView");
        var codeScroll = surface.CodeScrollViewer;
        var textSurface = surface.TextSurface;
        var toggle = Find<ToggleButton>(window, "LatexHighlightToggle");
        var copyButton = Find<Button>(window, "CopyLatexButton");
        var editorViewport = Find<ScrollViewer>(window, "MathEditorScrollViewer");

        Qa.Check(viewModel.IsLatexHighlightingEnabled && toggle.IsChecked == true,
            "LaTeX 구문 강조 기본값 또는 실제 ToggleButton binding이 켜져 있지 않습니다.");
        Qa.Equal("LatexHighlightToggle", AutomationProperties.GetAutomationId(toggle) ?? string.Empty,
            "구문 강조 전환 AutomationId");
        Qa.Equal("LaTeX 구문 강조 표시", AutomationProperties.GetName(toggle) ?? string.Empty,
            "구문 강조 전환 접근성 이름");
        Qa.Equal("LatexCodeView", AutomationProperties.GetAutomationId(surface) ?? string.Empty,
            "단일 LaTeX 코드 표면 AutomationId");
        Qa.Check(ReferenceEquals(surface.Syntax, viewModel.LatexSyntax) &&
                 surface.SourceText == viewModel.LatexCode && surface.HasCompleteTokenCoverage,
            "초기 코드 표면이 하나의 최신 syntax snapshot과 원문을 사용하지 않습니다.");
        Qa.Check(surface.IsReadOnly,
            "선택 가능한 코드 표면이 읽기 전용이 아닙니다.");
        Qa.Check(viewModel.LatexCode.Contains("e\u0301", StringComparison.Ordinal) &&
                 viewModel.LatexCode.Contains("1\u0301", StringComparison.Ordinal) &&
                 viewModel.LatexCode.Contains("𝑥", StringComparison.Ordinal) &&
                 viewModel.LatexCode.Contains("한글", StringComparison.Ordinal) &&
                 viewModel.LatexCode.Contains("👩‍🔬", StringComparison.Ordinal),
            "실제 생성 코드 fixture에서 문자+조합 부호·숫자+조합 부호·수학 문자·한글·ZWJ 이모지 원문을 모두 확인할 수 없습니다.");
        Qa.Check(editorViewport.Viewport.Height >= 257,
            $"접힌 LaTeX 코드 패널에서 최소 중앙 편집 viewport를 보존하지 못했습니다: {editorViewport.Viewport.Height:R}.");

        var firstBaseline = CaptureSession(session);
        Qa.Check(viewModel.CanCopyLatex, "정상 LaTeX fixture의 전체 코드 복사가 시작부터 비활성입니다.");
        var fakeCopies = new List<string>();
        window.LatexClipboardWriterOverride = text =>
        {
            fakeCopies.Add(text);
            return Task.CompletedTask;
        };

        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-collapsed-minimum");
        window.Width = 1360;
        window.Height = 880;
        await DrainLayoutAsync(window);
        Qa.Check(editorViewport.Viewport.Height >= 437,
            $"접힌 LaTeX 코드 패널에서 기본 중앙 편집 viewport를 보존하지 못했습니다: {editorViewport.Viewport.Height:R}.");
        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-collapsed-default");

        window.Width = 1060;
        window.Height = 700;
        expander.IsExpanded = true;
        await DrainLayoutAsync(window);
        Qa.Check(expander.IsExpanded, "코드 표면을 펼치지 못했습니다.");
        Qa.Check(surface.IsReadOnly && textSurface.IsEffectivelyVisible,
            "펼친 선택 가능 코드 표면이 읽기 전용/실제 표시 상태가 아닙니다.");
        Qa.Check(!surface.GetVisualDescendants().OfType<TextBox>().Any(),
            "코드 표면 안에 숨은 TextBox 대체 원문이 남아 있습니다.");
        var selectableSurfaces = surface.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
        Qa.Equal(1, selectableSurfaces.Length, "실제 부착된 단일 선택 가능 코드 표면 수");
        Qa.Check(ReferenceEquals(textSurface, selectableSurfaces[0]),
            "검증한 selectable text surface가 코드 표면의 유일한 원문 선택 surface가 아닙니다.");
        Qa.Check(codeScroll.Extent.Height > codeScroll.Viewport.Height + 1,
            $"펼친 긴 코드 fixture에서 실제 내부 세로 스크롤이 생기지 않았습니다: viewport={codeScroll.Viewport.Height:R}, extent={codeScroll.Extent.Height:R}.");
        var highlightedRuns = textSurface.Inlines?.OfType<Run>().ToArray()
            ?? throw new InvalidOperationException("강조 상태의 코드 InlineCollection이 없습니다.");
        Qa.Equal(viewModel.LatexSyntax.Tokens.Length, highlightedRuns.Length,
            "초기 강조 상태의 토큰 Run 개수");
        var highlightedRunTexts = highlightedRuns.Select(run => run.Text ?? string.Empty).ToArray();
        var highlightedBrushes = highlightedRuns.Select(run => run.Foreground).ToArray();
        Qa.Check(highlightedBrushes.All(IsVisibleSolidBrush),
            "강조 상태의 plain/colored 토큰 중 전경색이 비어 있거나 투명한 Run이 있습니다.");
        var plainRunIndices = viewModel.LatexSyntax.Tokens
            .Select((token, index) => (token, index))
            .Where(item => item.token.Kind is LatexTokenKind.Text or LatexTokenKind.Whitespace)
            .Select(item => item.index)
            .ToArray();
        var coloredRunIndices = viewModel.LatexSyntax.Tokens
            .Select((token, index) => (token, index))
            .Where(item => item.token.Kind is LatexTokenKind.ControlWord or LatexTokenKind.ControlSymbol or
                LatexTokenKind.Brace or LatexTokenKind.Alignment or LatexTokenKind.Script or
                LatexTokenKind.Number or LatexTokenKind.Comment)
            .Select(item => item.index)
            .ToArray();
        Qa.Check(plainRunIndices.Length > 0 && plainRunIndices.All(index => IsDefaultInk(highlightedBrushes[index])) &&
                 coloredRunIndices.Length > 0 && coloredRunIndices.Any(index => !IsDefaultInk(highlightedBrushes[index])),
            "강조 상태가 plain 토큰에 기본 잉크색을 주거나 색상 토큰을 구별하지 않습니다.");
        Qa.Equal(firstBaseline.WorkspaceFingerprint, Qa.Fingerprint(session.Workspace),
            "코드 표면 열기가 수식을 바꾸지 않음");
        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-expanded-minimum-top");

        var maxScroll = Math.Max(0, codeScroll.Extent.Height - codeScroll.Viewport.Height);
        Qa.Check(maxScroll > 1, "원문 코드 표면의 실제 스크롤 범위가 없습니다.");
        codeScroll.Offset = new Vector(codeScroll.Offset.X, maxScroll);
        await DrainLayoutAsync(window);
        Qa.Check(codeScroll.Offset.Y > 0,
            $"내부 코드 스크롤을 아래로 옮기지 못했습니다: max={maxScroll:R}, actual={codeScroll.Offset.Y:R}.");

        var source = surface.SourceText;
        var combiningStart = source.LastIndexOf("e\u0301", StringComparison.Ordinal);
        Qa.Check(combiningStart >= 0, "LaTeX 생성 원문에서 조합 부호 표본을 찾지 못했습니다.");
        surface.SelectionStart = combiningStart;
        surface.SelectionEnd = combiningStart + 2;
        await DrainLayoutAsync(window);
        Qa.Equal("e\u0301", surface.SelectedText, "원문 표면의 실제 combining grapheme 선택");
        var selectionStart = surface.SelectionStart;
        var selectionEnd = surface.SelectionEnd;
        var scrollBeforeToggle = codeScroll.Offset;
        var desiredBeforeToggle = textSurface.DesiredSize;
        var textBoundsBeforeToggle = textSurface.Bounds.Size;
        var sameSourceSnapshot = viewModel.LatexSyntax;
        var toggleSessionBaseline = CaptureSession(session);

        await ClickToggleAsync(window, toggle, expander, expectedEnabled: false, "강조 끄기");
        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-off-combining-minimum-bottom");
        var unhighlightedRuns = textSurface.Inlines?.OfType<Run>().ToArray()
            ?? throw new InvalidOperationException("강조 해제 후 코드 InlineCollection이 없습니다.");
        Qa.Equal(highlightedRuns.Length, unhighlightedRuns.Length,
            "강조 해제 후 토큰 Run 분할 보존");
        Qa.Check(highlightedRunTexts.SequenceEqual(unhighlightedRuns.Select(run => run.Text ?? string.Empty)) &&
                 highlightedRuns.Select((run, index) => ReferenceEquals(run, unhighlightedRuns[index])).All(value => value),
            "강조 전환이 같은 source의 Run 분할 또는 실제 Run 인스턴스를 재구성했습니다.");
        Qa.Check(unhighlightedRuns.All(run => IsVisibleSolidBrush(run.Foreground) && IsDefaultInk(run.Foreground)),
            "강조 해제 후 plain/colored Run 모두 불투명한 기본 원문 색상으로 돌아오지 않았습니다.");
        Qa.Check(ReferenceEquals(sameSourceSnapshot, viewModel.LatexSyntax), "강조 끄기가 immutable source/token snapshot을 바꿨습니다.");
        Qa.Equal(source, surface.SourceText, "강조 끄기 원문 보존");
        Qa.Equal(selectionStart, surface.SelectionStart, "강조 끄기 selection 시작 보존");
        Qa.Equal(selectionEnd, surface.SelectionEnd, "강조 끄기 selection 끝 보존");
        Qa.Equal("e\u0301", surface.SelectedText, "강조 끄기 combining selection 보존");
        AssertNear(scrollBeforeToggle.X, codeScroll.Offset.X, "강조 끄기 horizontal scroll 보존");
        AssertNear(scrollBeforeToggle.Y, codeScroll.Offset.Y, "강조 끄기 vertical scroll 보존");
        AssertSizeNear(desiredBeforeToggle, textSurface.DesiredSize, "강조 끄기 실제 glyph measure 보존");
        AssertSizeNear(textBoundsBeforeToggle, textSurface.Bounds.Size, "강조 끄기 실제 TextSurface allocation 보존");
        AssertSessionUnchanged(toggleSessionBaseline, session, "강조 끄기");
        surface.SelectionEnd = combiningStart;
        surface.SelectionStart = combiningStart + 2;
        await DrainLayoutAsync(window);
        var reverseStart = surface.SelectionStart;
        var reverseEnd = surface.SelectionEnd;
        Qa.Check(reverseStart > reverseEnd, "역방향 코드 선택 fixture를 설정하지 못했습니다.");
        codeScroll.Offset = new Vector(codeScroll.Offset.X, maxScroll);
        await DrainLayoutAsync(window);
        var reverseScroll = codeScroll.Offset;
        var reverseMeasure = textSurface.DesiredSize;
        var reverseBounds = textSurface.Bounds.Size;
        var toggleOnBaseline = CaptureSession(session);
        await ClickToggleAsync(window, toggle, expander, expectedEnabled: true, "강조 켜기");
        var restoredRuns = textSurface.Inlines?.OfType<Run>().ToArray()
            ?? throw new InvalidOperationException("강조 재적용 후 코드 InlineCollection이 없습니다.");
        Qa.Equal(highlightedRuns.Length, restoredRuns.Length,
            "강조 재적용 후 토큰 Run 분할 보존");
        Qa.Check(highlightedRunTexts.SequenceEqual(restoredRuns.Select(run => run.Text ?? string.Empty)) &&
                 highlightedRuns.Select((run, index) => ReferenceEquals(run, restoredRuns[index])).All(value => value),
            "강조 재적용이 같은 source의 Run 분할 또는 실제 Run 인스턴스를 재구성했습니다.");
        Qa.Check(highlightedBrushes.Select((brush, index) => ReferenceEquals(brush, restoredRuns[index].Foreground)).All(value => value),
            "강조 재적용 후 토큰별 색상 brush가 원래 상태로 복원되지 않았습니다.");
        Qa.Equal(reverseStart, surface.SelectionStart, "강조 켜기 역방향 selection 시작 보존");
        Qa.Equal(reverseEnd, surface.SelectionEnd, "강조 켜기 역방향 selection 끝 보존");
        Qa.Equal("e\u0301", surface.SelectedText, "강조 켜기 역방향 combining selection 원문");
        AssertNear(reverseScroll.Y, codeScroll.Offset.Y, "강조 켜기 역방향 선택의 scroll 보존");
        AssertSizeNear(reverseMeasure, textSurface.DesiredSize, "강조 켜기 실제 glyph measure 보존");
        AssertSizeNear(reverseBounds, textSurface.Bounds.Size, "강조 켜기 실제 TextSurface allocation 보존");
        Qa.Check(ReferenceEquals(sameSourceSnapshot, surface.Syntax), "강조 켜기가 원문 syntax snapshot을 바꿨습니다.");
        AssertSessionUnchanged(toggleOnBaseline, session, "강조 켜기");
        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-on-reverse-selection-minimum-bottom");

        window.Width = 1360;
        window.Height = 880;
        await DrainLayoutAsync(window);
        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-on-default-bottom");
        window.Width = 1060;
        window.Height = 700;
        await DrainLayoutAsync(window);

        var editText = Qa.Nodes(session.Workspace).OfType<TextNode>()
            .First(node => !node.IsEmptySlot && node.Text.Contains("e\u0301", StringComparison.Ordinal));
        var oldSource = viewModel.LatexCode;
        Qa.Caret(session, editText.Id, Qa.Elements(editText.Text));
        Qa.Check(session.InsertText("끝"), "원문 변경 선택 초기화 fixture 편집 실패");
        await WaitUntilAsync(() => surface.SourceText == viewModel.LatexCode &&
                                   ReferenceEquals(surface.Syntax, viewModel.LatexSyntax),
            "실제 편집 후 새 syntax snapshot binding");
        await DrainLayoutAsync(window);
        Qa.Check(!string.Equals(oldSource, viewModel.LatexCode, StringComparison.Ordinal),
            "실제 Session 편집이 LaTeX 원문을 바꾸지 않았습니다.");
        Qa.Equal(viewModel.LatexCode, surface.SourceText, "새 원문 snapshot과 실제 표면 일치");
        Qa.Equal(0, surface.SelectionStart, "원문 변경 시 선택 시작 초기화");
        Qa.Equal(0, surface.SelectionEnd, "원문 변경 시 선택 끝 초기화");
        AssertNear(0, codeScroll.Offset.Y, "원문 변경 시 실제 내부 scroll 초기화");
        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-source-changed-reset-minimum");

        var updatedSource = viewModel.LatexCode;
        var selectedStart = updatedSource.IndexOf("e\u0301", StringComparison.Ordinal);
        Qa.Check(selectedStart >= 0, "수정된 원문에서 부분 복사 선택 표본이 없습니다.");
        surface.SelectionStart = selectedStart;
        surface.SelectionEnd = selectedStart + 2;
        await DrainLayoutAsync(window);
        var previewSnapshot = viewModel.OutputPreview.PreviewSnapshot;
        var previewState = viewModel.OutputPreview.State;
        var copyBaseline = CaptureSession(session);
        await ClickCopyAsync(window, copyButton, expander, "전체 코드 복사");
        await WaitUntilAsync(() => !viewModel.IsCopyingLatex, "전체 LaTeX 코드 fake 복사 완료");
        Qa.Equal(1, fakeCopies.Count, "fake writer가 전체 복사에서 한 번만 호출됨");
        Qa.Equal(updatedSource, fakeCopies[0], "부분 선택 중에도 전체 원문 snapshot을 전달함");
        Qa.Equal(updatedSource, viewModel.LatexCode, "복사 후 serializer source 유지");
        Qa.Check(expander.IsExpanded, "헤더 복사 버튼이 Expander를 우발적으로 접지 않았습니다.");
        AssertSessionUnchanged(copyBaseline, session, "fake 전체 코드 복사");
        Qa.Check(ReferenceEquals(previewSnapshot, viewModel.OutputPreview.PreviewSnapshot) &&
                 previewState == viewModel.OutputPreview.State && !viewModel.IsOutputPreviewVisible,
            "fake 전체 코드 복사가 preview snapshot/state/visibility를 바꿨습니다.");
        RowShellVisualCases.CaptureLatexHighlightAndVerify(window, "highlight-copy-partial-selection-minimum");
    }

    private static (EquationWorkspace Workspace, Guid TextId) CreateHighlightWorkspace()
    {
        var parts = Enumerable.Range(0, 56)
            .Select(index => Qa.Text($"α_{index} + 1.25 + e\u0301 + 1\u0301 + 𝑥 + 한글 + 😀 + 👩‍🔬 = y\\\\"))
            .Cast<MathNode>()
            .ToArray();
        var first = (TextNode)parts[0];
        return (Qa.Workspace(Qa.Sequence(parts)), first.Id);
    }

    private static async Task ClickToggleAsync(MainWindow window, ToggleButton toggle, Expander expander,
        bool expectedEnabled, string phase)
    {
        var wasExpanded = expander.IsExpanded;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buttonBounds = BoundsInWindow(toggle, window);
            var windowBounds = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
            Qa.Check(Contains(windowBounds, buttonBounds), $"{phase}: 강조 전환이 창 viewport에 온전히 보이지 않습니다.");
            var point = Center(buttonBounds);
            window.MouseMove(point, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, phase + " hover 후");
            var settled = BoundsInWindow(toggle, window);
            Qa.Check(Contains(windowBounds, settled), $"{phase}: hover 뒤 강조 전환이 잘렸습니다.");
            var settledPoint = Center(settled);
            if (Math.Abs(point.X - settledPoint.X) > 0.5 || Math.Abs(point.Y - settledPoint.Y) > 0.5)
                continue;

            window.MouseDown(settledPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(settledPoint, MouseButton.Left, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            var viewModel = window.DataContext as MainWindowViewModel
                ?? throw new InvalidOperationException("강조 전환 창의 실제 ViewModel을 찾지 못했습니다.");
            Qa.Equal(expectedEnabled, viewModel.IsLatexHighlightingEnabled, phase + " VM 상태");
            Qa.Equal(expectedEnabled, toggle.IsChecked == true, phase + " 실제 TwoWay Toggle 상태");
            Qa.Equal(wasExpanded, expander.IsExpanded, phase + " Expander 접힘 상태 보존");
            return;
        }
        throw new InvalidOperationException(phase + ": hover 뒤 강조 전환 좌표가 안정되지 않았습니다.");
    }

    private static async Task ClickCopyAsync(MainWindow window, Button button, Expander expander, string phase)
    {
        var wasExpanded = expander.IsExpanded;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var bounds = BoundsInWindow(button, window);
            var windowBounds = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
            Qa.Check(Contains(windowBounds, bounds), phase + ": 복사 버튼이 창 안에 온전히 보이지 않습니다.");
            var point = Center(bounds);
            window.MouseMove(point, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, phase + " hover 후");
            var settled = BoundsInWindow(button, window);
            Qa.Check(Contains(windowBounds, settled), phase + ": hover 뒤 복사 버튼이 잘렸습니다.");
            var settledPoint = Center(settled);
            if (Math.Abs(point.X - settledPoint.X) > 0.5 || Math.Abs(point.Y - settledPoint.Y) > 0.5)
                continue;
            Qa.Check(button.IsEffectivelyEnabled, phase + ": 복사 버튼이 비활성입니다.");
            window.MouseDown(settledPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(settledPoint, MouseButton.Left, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            Qa.Equal(wasExpanded, expander.IsExpanded, phase + ": 복사 버튼이 Expander를 우발 전환하지 않음");
            return;
        }
        throw new InvalidOperationException(phase + ": hover 뒤 복사 버튼 좌표가 안정되지 않았습니다.");
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

    private static Rect BoundsInWindow(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("실제 창 기준 코드 표면 좌표를 얻지 못했습니다: " + control.Name);
        return new Rect(origin, control.Bounds.Size);
    }

    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static bool IsVisibleSolidBrush(IBrush? brush) =>
        brush is ISolidColorBrush solid && solid.Color.A > 0 && solid.Opacity > 0;

    private static bool IsDefaultInk(IBrush? brush) =>
        brush is ISolidColorBrush solid && solid.Color == Color.Parse("#21496F") && solid.Opacity > 0;

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

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
        Qa.Equal(expected.StateId, current.StateId, label + " StateId");
        Qa.Equal(expected.Revision, current.Revision, label + " revision");
        Qa.Equal(expected.Caret, current.Caret, label + " caret");
        Qa.Equal(expected.Selection, current.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, current.SelectedNodeId, label + " selected node");
        Qa.Equal(expected.IsNodeSelected, current.IsNodeSelected, label + " whole node selection");
        Qa.Equal(expected.IsDirty, current.IsDirty, label + " dirty");
        Qa.Equal(expected.CanUndo, current.CanUndo, label + " undo");
        Qa.Equal(expected.CanRedo, current.CanRedo, label + " redo");
    }

    private static void AssertNear(double expected, double actual, string label) =>
        Qa.Near(expected, actual, label, 0.5);

    private static void AssertSizeNear(Size expected, Size actual, string label)
    {
        AssertNear(expected.Width, actual.Width, label + " width");
        AssertNear(expected.Height, actual.Height, label + " height");
    }

    private static void EnsureStubHandle(Window window, string phase)
    {
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is not null && handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            $"{phase}: Headless 창 STUB/0 handle을 확인하지 못했습니다.");
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
        EnsureStubHandle(window, "소유 LaTeX 강조 Headless 창 닫기 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "소유 LaTeX 강조 Headless 창을 닫지 못했습니다.");
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
