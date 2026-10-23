using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;

namespace AetherMath.LogicQA;

internal static class ThinMathSpaceUiInteractionCases
{
    private const string CaseId = "THIN-MATH-SPACE-UI";
    private const string ArtifactScope = "p2-thin-math-space";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly MethodInfo EnsureEditorLayoutMethod = typeof(MathEditorControl).GetMethod(
        "EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(MathEditorControl).FullName, "EnsureLayout");
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "얇은 수학 공백의 삽입·선택·삭제·간격·SVG 경계를 실제 App Headless에서 확인",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (!string.Equals(caseId, CaseId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("알 수 없는 얇은 수학 공백 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("얇은 수학 공백 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
            return 2;
        }

        using var fixtureDirectory = new TempDirectory();
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
        startInfo.ArgumentList.Add("--thin-math-space-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("얇은 수학 공백 UI Headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("얇은 수학 공백 UI child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"얇은 수학 공백 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("얇은 수학 공백 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync(TempDirectory fixtureDirectory)
    {
        var fileService = new WorkspaceFileService();
        var completeFixture = CreateCompleteFixture();
        var completeStyle = completeFixture.SourceStyleOverride
            ?? throw new InvalidOperationException("완성 수식 fixture에 명시 스타일이 없습니다.");
        var completePath = fixtureDirectory.File("thin-space-complete.aethermath");
        await SaveFixtureAsync(fileService, completePath, completeFixture.Workspace);
        var (viewModel, window) = await OpenFixtureWindowAsync(fileService, completePath, 1060, 700);
        var session = viewModel.Session;
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        var toolsScroll = Find<ScrollViewer>(window, "TemplateToolsScrollViewer");
        var thinButton = Find<Button>(window, "InsertThinSpaceButton");
        var disabledReason = Find<TextBlock>(window, "ThinSpaceDisabledReason");
        var thinNotice = Find<TextBlock>(window, "ThinSpaceNotice");
        var helpText = Find<TextBlock>(window, "ThinSpaceHelpText");

        Qa.Check(ReferenceEquals(session, editor.Session), "Headless 편집기와 실제 Workspace Session이 다릅니다.");
        Qa.Check(ReferenceEquals(thinButton.Command, viewModel.InsertThinSpaceCommand),
            "얇은 수학 공백 버튼이 VM의 실제 삽입 명령에 연결되지 않았습니다.");
        Qa.Equal("얇은 수학 공백 삽입", AutomationProperties.GetName(thinButton) ?? string.Empty,
            "얇은 수학 공백 버튼 접근성 이름");
        Qa.Equal("현재 입력 위치에 얇은 수학 공백을 넣습니다. Unicode U+2009 문자 삽입과는 별개입니다.",
            ToolTip.GetTip(thinButton)?.ToString() ?? string.Empty, "얇은 수학 공백 버튼 도움말");
        var helpContent = helpText.Text
            ?? throw new InvalidOperationException("얇은 수학 공백 도움말 텍스트가 null입니다.");
        Qa.Check(helpText.TextWrapping == Avalonia.Media.TextWrapping.Wrap &&
                 helpContent.Contains("U+2009", StringComparison.Ordinal) &&
                 helpContent.Contains("Ctrl+클릭", StringComparison.Ordinal) &&
                 helpContent.Contains("구조 트리", StringComparison.Ordinal) &&
                 helpContent.Contains("삭제", StringComparison.Ordinal),
            "사용자 안내가 Unicode 문자와의 차이 또는 선택·삭제 경로를 설명하지 않습니다.");
        Qa.Check(!string.IsNullOrWhiteSpace(thinNotice.Text), "VM의 얇은 수학 공백 상태 안내가 비어 있습니다.");

        Qa.Caret(session, completeFixture.SourceTextId, 1);
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Check(!session.IsDirty && !session.CanUndo && viewModel.CanInsertThinSpace &&
                 thinButton.IsEffectivelyEnabled,
            "일반 Text caret에서 삽입 버튼이 활성화되지 않았습니다.");

        await CaptureStructureShellAsync(window, toolsScroll, "thin-space-structure-initial-minimum");
        await ResizeAsync(window, 1360, 880);
        await CaptureStructureShellAsync(window, toolsScroll, "thin-space-structure-initial-default");
        await ResizeAsync(window, 1060, 700);

        var unmodified = CaptureSession(session);
        Qa.Check(session.TrySelectNode(completeFixture.SourceTextId), "전체 TextNode 선택 fixture 설정 실패");
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.CanInsertThinSpace && !thinButton.IsEffectivelyEnabled &&
                 !thinButton.Command!.CanExecute(null) && !string.IsNullOrWhiteSpace(viewModel.ThinSpaceDisabledReason),
            "전체 구조 선택에서 얇은 수학 공백 삽입이 차단되거나 사유가 없습니다.");
        Qa.Check(disabledReason.IsEffectivelyVisible &&
                 disabledReason.Text == viewModel.ThinSpaceDisabledReason,
            "비활성 사유가 실제 구조 도구 UI에 표시되지 않습니다.");
        AssertDocumentHistoryUnchanged(unmodified, session, "전체 TextNode 선택");
        await SetScrollEdgeAsync(window, toolsScroll, top: false);
        SaveWindowEvidence(window, "thin-space-disabled-minimum-bottom", viewModel,
            new { state = "whole-node-selection", reason = viewModel.ThinSpaceDisabledReason });
        RowShellVisualCases.CaptureAndVerify(window, "thin-space-structure-disabled-minimum-bottom", ArtifactScope);

        Qa.Select(session, completeFixture.SourceTextId, 0, 1);
        await DrainLayoutAsync(window);
        Qa.Check(viewModel.CanInsertThinSpace && thinButton.IsEffectivelyEnabled,
            "같은 TextNode의 부분 선택에서는 얇은 수학 공백을 삽입할 수 있어야 합니다.");
        Qa.Caret(session, completeFixture.SourceTextId, 1);
        await DrainLayoutAsync(window);
        Qa.Check(viewModel.CanInsertThinSpace && thinButton.IsEffectivelyEnabled &&
                 !disabledReason.IsEffectivelyVisible,
            "일반 Text caret 복귀 뒤 삽입 버튼 또는 비활성 사유 표시가 갱신되지 않았습니다.");

        var baseline = CaptureSession(session);
        Qa.Check(!baseline.CanUndo, "격리 fixture의 삽입 전 undo history가 비어 있지 않습니다.");
        await ClickControlAsync(window, thinButton);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "수학 공백 버튼 클릭 후");

        var insertedSpace = FindThinSpace(session, MathSpaceKind.Thin);
        var splitTexts = Qa.Nodes(session.Workspace).OfType<TextNode>().ToArray();
        Qa.Check(splitTexts.Length == 2 && string.Concat(splitTexts.Select(text => text.Text)) == "ab",
            "공백 삽입 중 원본 TextNode의 문자가 손실되거나 복제되었습니다.");
        Qa.Check(splitTexts.All(text => text.Role == completeFixture.SourceRole &&
                                       text.StyleOverride == completeStyle),
            "공백 삽입 중 분할된 원본 텍스트의 역할 또는 명시 스타일이 바뀌었습니다.");
        Qa.Check(splitTexts.Any(text => text.Id == completeFixture.SourceTextId),
            "공백 삽입이 원본 텍스트의 기존 ID를 보존하지 않았습니다.");
        Qa.Equal(MathSpaceKind.Thin, insertedSpace.Kind, "삽입된 MathSpace 종류");
        Qa.Equal(baseline.Revision + 1, session.Revision, "실제 버튼 삽입은 한 revision만 생성");
        Qa.Check(session.CaptureSnapshot().StateId != baseline.StateId && session.CanUndo && !session.CanRedo,
            "실제 버튼 삽입이 단일 undoable Session commit을 만들지 않았습니다.");
        AssertSingleThinSpaceBetweenText(session, insertedSpace.Id, completeStyle.FontSize ?? 30);

        editor.Focus();
        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        Qa.Check(!Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Any(node => node.Id == insertedSpace.Id) &&
                 session.CanRedo,
            "얇은 공백 버튼 삽입을 한 번 Undo했을 때 해당 수학 공백이 제거되지 않았습니다.");
        Qa.Equal("ab", string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text)),
            "얇은 공백 삽입 Undo 뒤 원본 문자열 복구");
        SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
        Qa.Check(ReferenceEquals(session, viewModel.Session) && session.CanUndo && !session.CanRedo &&
                 Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Any(node => node.Id == insertedSpace.Id),
            "얇은 공백 삽입 Redo가 같은 Session과 수학 공백 ID를 복구하지 않았습니다.");
        AssertSingleThinSpaceBetweenText(session, insertedSpace.Id, completeStyle.FontSize ?? 30);
        var insertedBaseline = CaptureSession(session);

        var editorLayout = GetEditorLayout(editor)
            ?? throw new InvalidOperationException("편집기 MathSpace layout을 얻지 못했습니다.");
        var editorSpaceLayout = editorLayout.Nodes.Single(node => node.NodeId == insertedSpace.Id);
        var primitiveCountForSpace = editorLayout.Primitives.OfType<GlyphPrimitive>()
            .Count(primitive => primitive.NodeId == insertedSpace.Id);
        Qa.Equal(0, primitiveCountForSpace, "얇은 공백은 편집기에서도 glyph primitive를 만들지 않음");
        Qa.Check(editorSpaceLayout.Bounds.Width > 0,
            "얇은 수학 공백이 선택 가능한 양의 layout 폭을 갖지 않습니다.");
        WriteThinSpaceLayoutEvidence("thin-space-inserted-layout", viewModel, insertedSpace.Id,
            editorLayout, editorSpaceLayout, primitiveCountForSpace);

        var structureTab = Find<TabItem>(window, "StructureInspectorTab");
        await ClickInspectorTabAsync(window, structureTab);
        await ClickStructureNodeAsync(window, insertedSpace.Id);
        Qa.Equal<Guid?>(insertedSpace.Id, session.SelectedNodeId, "구조 트리에서 선택한 공백 NodeId");
        Qa.Check(session.IsNodeSelected && viewModel.StructurePanel.SelectedNodeId == insertedSpace.Id,
            "구조 트리 공백 선택이 실제 Session의 전체 구조 선택으로 연결되지 않았습니다.");
        Qa.Check(viewModel.StructurePanel.SelectedItem?.Label.Contains("얇은 수학 공백", StringComparison.Ordinal) == true,
            "구조 트리에서 선택된 공백에 식별 가능한 이름이 없습니다.");
        await SetScrollEdgeAsync(window, toolsScroll, top: false);
        SaveWindowEvidence(window, "thin-space-tree-selection-minimum-bottom", viewModel,
            new { selectedSpaceId = insertedSpace.Id, isNodeSelected = session.IsNodeSelected });
        RowShellVisualCases.CaptureAndVerify(window, "thin-space-structure-tree-selection-minimum-bottom", ArtifactScope);

        Qa.Caret(session, splitTexts[0].Id, 1);
        await DrainLayoutAsync(window);
        var spaceBoundsBeforePointer = GetEditorLayout(editor)?.Nodes.Single(node => node.NodeId == insertedSpace.Id).Bounds
            ?? throw new InvalidOperationException("Ctrl+클릭 대상 MathSpace bounds를 찾지 못했습니다.");
        var controlClickEvidence = await ControlClickMathNodeAsync(
            window, editor, insertedSpace.Id, spaceBoundsBeforePointer);
        SaveControlClickEvidence(window, viewModel, insertedSpace.Id, controlClickEvidence);
        AssertControlClickReachedMathEditor(controlClickEvidence, insertedSpace.Id);
        Qa.Equal<Guid?>(insertedSpace.Id, session.SelectedNodeId, "편집기 Ctrl+클릭으로 선택한 공백 NodeId");
        Qa.Check(session.IsNodeSelected, "편집기 Ctrl+클릭이 공백의 전체 선택 overlay를 활성화하지 않았습니다.");
        var selectedLayout = GetEditorLayout(editor)
            ?? throw new InvalidOperationException("Ctrl+클릭 뒤 선택 overlay layout을 얻지 못했습니다.");
        Qa.Check(selectedLayout.GetBounds(insertedSpace.Id) is not null && session.IsNodeSelected,
            "편집기 선택 overlay가 실제 공백 NodeLayout bounds에 대응하지 않습니다.");
        SaveWindowEvidence(window, "thin-space-control-click-overlay-minimum-bottom", viewModel,
            new { selectedSpaceId = insertedSpace.Id, selectedBounds = Format(spaceBoundsBeforePointer), overlay = session.IsNodeSelected });
        await SetScrollEdgeAsync(window, toolsScroll, top: false);
        RowShellVisualCases.CaptureAndVerify(window, "thin-space-structure-control-click-overlay-minimum-bottom", ArtifactScope);

        SendKey(window, Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, string.Empty);
        await DrainLayoutAsync(window);
        Qa.Check(!session.IsNodeSelected && session.Caret.NodeId != insertedSpace.Id &&
                 Qa.Node(session, session.Caret.NodeId) is TextNode,
            "공백 전체 선택에서 오른쪽 방향키가 인접 텍스트 caret으로 벗어나지 못했습니다.");
        var afterDirection = CaptureSession(session);
        AssertDocumentHistoryUnchanged(insertedBaseline, session, "공백 선택에서 방향키 탈출");
        Qa.Equal(insertedBaseline.Revision, afterDirection.Revision, "방향키 이동은 삽입 revision을 바꾸지 않음");

        await ClickStructureNodeAsync(window, insertedSpace.Id);
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == insertedSpace.Id,
            "Tab 경계 검사 전에 구조 트리에서 공백을 다시 선택하지 못했습니다.");
        editor.Focus();
        await DrainLayoutAsync(window);
        var tabBaseline = CaptureSession(session);
        SendKey(window, Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
        await DrainLayoutAsync(window);
        Qa.Check(!session.IsNodeSelected && session.Caret.NodeId == splitTexts[1].Id &&
                 session.Caret.TextElementOffset == 0,
            "공백 전체 선택에서 Tab이 뒤쪽 텍스트 시작 caret으로 탈출하지 못했습니다.");
        AssertDocumentHistoryUnchanged(tabBaseline, session, "공백 선택에서 Tab 탈출");

        await ClickStructureNodeAsync(window, insertedSpace.Id);
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == insertedSpace.Id,
            "Shift+Tab 경계 검사 전에 구조 트리에서 공백을 다시 선택하지 못했습니다.");
        editor.Focus();
        await DrainLayoutAsync(window);
        var shiftTabBaseline = CaptureSession(session);
        SendKey(window, Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
        await DrainLayoutAsync(window);
        Qa.Check(!session.IsNodeSelected && session.Caret.NodeId == splitTexts[0].Id &&
                 session.Caret.TextElementOffset == TextElementOffsets.Count(splitTexts[0].Text),
            "공백 전체 선택에서 Shift+Tab이 앞쪽 텍스트 끝 caret으로 탈출하지 못했습니다.");
        AssertDocumentHistoryUnchanged(shiftTabBaseline, session, "공백 선택에서 Shift+Tab 탈출");

        await ClickStructureNodeAsync(window, insertedSpace.Id);
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == insertedSpace.Id,
            "삭제 전 구조 트리에서 공백을 다시 명시 선택하지 못했습니다.");
        editor.Focus();
        await DrainLayoutAsync(window);
        SendKey(window, Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, string.Empty);
        await DrainLayoutAsync(window);
        Qa.Check(!Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Any(node => node.Id == insertedSpace.Id),
            "구조 선택 후 Delete 입력으로 얇은 수학 공백이 제거되지 않았습니다.");
        Qa.Check(!session.IsNodeSelected && Qa.Node(session, session.Caret.NodeId) is TextNode,
            "공백 삭제 뒤 편집 caret이 텍스트로 복귀하지 않았습니다.");
        var caretAfterDelete = session.Caret;
        SendKey(window, Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, string.Empty);
        await DrainLayoutAsync(window);
        Qa.Check(session.Caret != caretAfterDelete && Qa.Node(session, session.Caret.NodeId) is TextNode,
            "공백 삭제 뒤 방향키가 인접 텍스트 편집 위치로 이동하지 않았습니다.");

        var remainingSpaceCount = Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Count();
        var textBeforeUnicode = string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text));
        var currentText = Qa.Node(session, session.Caret.NodeId) as TextNode
            ?? throw new InvalidOperationException("Unicode 얇은 공백 입력 caret이 TextNode에 없습니다.");
        Qa.Caret(session, currentText.Id, TextElementOffsets.Count(currentText.Text));
        editor.Focus();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "Unicode U+2009 합성 텍스트 입력 전");
        window.KeyTextInput("\u2009");
        await DrainLayoutAsync(window);
        var textAfterUnicode = string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text));
        Qa.Equal(textBeforeUnicode + "\u2009", textAfterUnicode,
            "Unicode U+2009 입력은 TextNode 문자열에 들어감");
        Qa.Equal(remainingSpaceCount, Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Count(),
            "Unicode U+2009 입력은 MathSpaceNode 삽입과 구분됨");

        SendShortcut(window, Key.Z, PhysicalKey.Z, "z");
        Qa.Equal(textBeforeUnicode, string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text)),
            "Unicode U+2009 텍스트 입력 undo");
        SendShortcut(window, Key.Y, PhysicalKey.Y, "y");
        Qa.Equal(textBeforeUnicode + "\u2009",
            string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text)),
            "Unicode U+2009 텍스트 입력 redo");

        Qa.Caret(session, currentText.Id, 1);
        await DrainLayoutAsync(window);
        var spaceButtonAfterDeletion = Find<Button>(window, "InsertThinSpaceButton");
        Qa.Check(viewModel.CanInsertThinSpace && spaceButtonAfterDeletion.IsEffectivelyEnabled,
            "Unicode 입력 후 일반 텍스트 caret에서 공백 명령이 다시 활성화되지 않았습니다.");
        var unicodeTextBeforeMathCommand = string.Concat(
            Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text));
        await ClickControlAsync(window, spaceButtonAfterDeletion);
        await DrainLayoutAsync(window);
        var previewSpace = FindThinSpace(session, MathSpaceKind.Thin);
        Qa.Check(previewSpace.Id != insertedSpace.Id &&
                 Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Any(node => node.Id == previewSpace.Id),
            "삭제 후 미리보기 검증용 새 수학 공백이 별도 ID로 삽입되지 않았습니다.");
        Qa.Check(!Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Any(node => node.Id == insertedSpace.Id),
            "삭제한 첫 수학 공백 ID가 미리보기용 재삽입 뒤 다시 살아났습니다.");
        var unicodeAndSpaceText = string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text));
        Qa.Equal(unicodeTextBeforeMathCommand, unicodeAndSpaceText,
            "별도 수학 공백 명령은 U+2009가 포함된 기존 TextNode 문자열을 바꾸지 않음");
        Qa.Check(unicodeAndSpaceText.Contains('\u2009') && previewSpace.StyleOverride == completeStyle,
            "미리보기용 간격 명령이 기존 U+2009 문자 또는 원본 스타일 정보를 손상했습니다.");
        AssertSingleThinSpaceBetweenText(session, previewSpace.Id,
            completeStyle.FontSize ?? 30);
        await ClickControlAsync(window, Find<Button>(window, "ExportSvgButton"));
        await DrainLayoutAsync(window);
        Qa.Equal(MathOutputPreviewState.Ready, viewModel.OutputPreview.State,
            "완성된 수식과 얇은 공백의 SVG preview 상태");
        var previewCanvas = Find<MathOutputPreviewControl>(window, "OutputPreviewCanvas");
        var previewLayout = viewModel.OutputPreview.PreviewSnapshot?.Layout
            ?? throw new InvalidOperationException("수학 공백 preview snapshot layout이 없습니다.");
        Qa.Check(ReferenceEquals(previewCanvas.Layout, previewLayout),
            "제품 readonly preview surface가 child의 frozen layout을 그대로 사용하지 않습니다.");
        var previewSpaceLayout = previewLayout.Nodes.Single(node => node.NodeId == previewSpace.Id);
        Qa.Equal(0, previewLayout.Primitives.OfType<GlyphPrimitive>()
                .Count(primitive => primitive.NodeId == previewSpace.Id),
            "제품 output preview에서 공백 NodeId에 대한 glyph primitive가 없음");
        Qa.Check(!previewCanvas.Focusable && !previewCanvas.IsHitTestVisible,
            "제품 output preview가 편집 입력/포커스를 받습니다.");
        WritePreviewSpaceEvidence("thin-space-preview-minimum", viewModel, previewSpace.Id,
            previewLayout, previewSpaceLayout);
        SaveWindowEvidence(window, "thin-space-preview-minimum", viewModel,
            new { previewState = viewModel.OutputPreview.State.ToString(), spaceNodeId = previewSpace.Id,
                previewBounds = Format(previewSpaceLayout.Bounds), associatedGlyphs = 0 });
        RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "thin-space-preview-minimum", ArtifactScope);

        await ResizeAsync(window, 1360, 880);
        Qa.Check(ReferenceEquals(viewModel.Session, session) && ReferenceEquals(editor.Session, session),
            "출력 창 크기 조절이 원래 Session 또는 editor 인스턴스를 교체했습니다.");
        RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "thin-space-preview-default", ArtifactScope);
        await ClickControlAsync(window, Find<Button>(window, "OutputPreviewReturnToEditorButton"));
        await DrainLayoutAsync(window);
        Qa.Check(!viewModel.IsOutputPreviewVisible && editor.IsEffectivelyVisible &&
                 ReferenceEquals(editor.Session, session),
            "출력 미리보기 복귀가 같은 편집 Session/control을 보존하지 못했습니다.");
        Qa.Equal(0, Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Count(node => node.Id == insertedSpace.Id),
            "삽입한 공백은 실제 Delete 뒤에 workspace에서 제거되어야 합니다.");
        Qa.Check(Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Any(node => node.Id == previewSpace.Id),
            "미리보기용으로 다시 삽입한 공백이 탭 복귀 중 보존되지 않았습니다.");

        session.MarkSaved(session.CaptureSnapshot());
        await CloseOwnedWindowAsync(window);
        _ownedWindow = null;

        await RunRequiredSlotChecksAsync(fileService, fixtureDirectory);
    }

    private static async Task RunRequiredSlotChecksAsync(WorkspaceFileService fileService, TempDirectory fixtureDirectory)
    {
        var blankFixture = CreateRequiredSlotFixture();
        var blankPath = fixtureDirectory.File("thin-space-required-slot.aethermath");
        await SaveFixtureAsync(fileService, blankPath, blankFixture.Workspace);
        var (viewModel, window) = await OpenFixtureWindowAsync(fileService, blankPath, 1060, 700);
        var session = viewModel.Session;
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Caret(session, blankFixture.SourceTextId, 0);
        session.MarkSaved(session.CaptureSnapshot());
        Qa.Check(viewModel.CanInsertThinSpace && Find<Button>(window, "InsertThinSpaceButton").IsEffectivelyEnabled,
            "필수 빈 텍스트 슬롯 caret에서 얇은 수학 공백 삽입이 허용되지 않습니다.");
        await CaptureStructureShellAsync(window, Find<ScrollViewer>(window, "TemplateToolsScrollViewer"),
            "thin-space-structure-required-slot-minimum");
        await ClickControlAsync(window, Find<Button>(window, "InsertThinSpaceButton"));
        await DrainLayoutAsync(window);
        var space = FindThinSpace(session, MathSpaceKind.Thin);
        var fraction = session.Workspace.Groups.Single().Rows.Single().Root as FractionNode
            ?? throw new InvalidOperationException("필수 분수 슬롯 fixture의 행 root가 FractionNode가 아닙니다.");
        Qa.Check(DescendantsAndSelf(fraction.Numerator).OfType<TextNode>().Any(text =>
                     text.Id == blankFixture.SourceTextId && text.IsEmptySlot && text.Text.Length == 0),
            "필수 빈 슬롯에 얇은 공백을 삽입한 뒤에도 원래 빈 marker가 보존되지 않았습니다.");
        Qa.Check(blankFixture.DenominatorTextId is Guid denominatorId &&
                 DescendantsAndSelf(fraction.Denominator).OfType<TextNode>().Any(text =>
                     text.Id == denominatorId && text.Text == "b"),
            "필수 분수 슬롯에 공백을 삽입하는 동안 채워진 분모가 보존되지 않았습니다.");
        var editorLayout = GetEditorLayout(editor)
            ?? throw new InvalidOperationException("필수 빈 슬롯 layout을 얻지 못했습니다.");
        Qa.Check(editorLayout.Diagnostics.Any(diagnostic => diagnostic.Code == LayoutDiagnosticCode.EmptySlot &&
                                                            diagnostic.NodeId == blankFixture.SourceTextId),
            "필수 빈 슬롯 marker의 레이아웃 진단이 남지 않았습니다.");
        var incompleteSlotNodeIds = LatexSerializer.GetIncompleteSlotNodeIds(session.Workspace);
        Qa.Check(incompleteSlotNodeIds.Contains(blankFixture.SourceTextId),
            "LaTeX serializer가 빈 분자 marker의 필수 슬롯 ID를 보고하지 않았습니다.");
        Qa.Check(!viewModel.CanExportLatex && viewModel.LatexIncompleteSlotNodeIds.Contains(blankFixture.SourceTextId),
            "빈 분자 marker가 남아 있는데도 LaTeX 내보내기 제한과 VM 진단 ID가 반영되지 않았습니다.");

        await ClickControlAsync(window, Find<Button>(window, "ExportSvgButton"));
        await DrainLayoutAsync(window);
        Qa.Equal(MathOutputPreviewState.Incomplete, viewModel.OutputPreview.State,
            "필수 슬롯이 남아 있는 얇은 공백 수식의 SVG preview 상태");
        Qa.Check(!viewModel.OutputPreview.CanExportSvg &&
                 viewModel.OutputPreview.PreviewSnapshot?.Layout.Diagnostics.Any(diagnostic =>
                     diagnostic.Code == LayoutDiagnosticCode.EmptySlot && diagnostic.NodeId == blankFixture.SourceTextId) == true,
            "미완성 슬롯이 preview에 남거나 SVG 저장을 차단하지 않았습니다.");
        var outputPath = fixtureDirectory.File("thin-space-incomplete.svg");
        var exportResult = await viewModel.OutputPreview.ExportSvgToPathAsync(outputPath);
        Qa.Equal(SvgPreviewExportStatus.Incomplete, exportResult.Status,
            "필수 빈 슬롯이 있는 output의 picker-free SVG 저장 결과");
        Qa.Check(!File.Exists(outputPath), "필수 빈 슬롯이 있는 미완성 SVG 파일이 실제로 기록되었습니다.");
        var preview = viewModel.OutputPreview.PreviewSnapshot?.Layout
            ?? throw new InvalidOperationException("미완성 output preview layout이 없습니다.");
        Qa.Equal(0, preview.Primitives.OfType<GlyphPrimitive>().Count(primitive => primitive.NodeId == space.Id),
            "미완성 output에서 공백 노드는 여전히 glyph를 만들지 않음");
        WritePreviewSpaceEvidence("thin-space-incomplete-minimum", viewModel, space.Id,
            preview, preview.Nodes.Single(node => node.NodeId == space.Id));
        SaveWindowEvidence(window, "thin-space-incomplete-minimum", viewModel,
            new { previewState = viewModel.OutputPreview.State.ToString(), canExport = viewModel.OutputPreview.CanExportSvg,
                exportStatus = exportResult.Status.ToString(), emptySlot = blankFixture.SourceTextId,
                spaceNodeId = space.Id, associatedGlyphs = 0 });
        RowShellVisualCases.CaptureOutputPreviewAndVerify(window, "thin-space-incomplete-minimum", ArtifactScope);
        Qa.Check(ReferenceEquals(editor.Session, session) && ReferenceEquals(viewModel.OutputPreview.PreviewSnapshot?.Layout, preview),
            "불완전 preview가 원본 Session 또는 snapshot layout을 교체했습니다.");

        session.MarkSaved(session.CaptureSnapshot());
        await CloseOwnedWindowAsync(window);
        _ownedWindow = null;
    }

    private static async Task<(MainWindowViewModel ViewModel, MainWindow Window)> OpenFixtureWindowAsync(
        IWorkspaceFileService fileService, string path, double width, double height)
    {
        var viewModel = new MainWindowViewModel(fileService);
        Qa.Equal(WorkspaceActionRequestResult.Completed, await viewModel.OpenFromPathAsync(path),
            "명시적인 임시 파일 경로로 수학 공백 fixture 열기");
        var window = new MainWindow(viewModel) { Width = width, Height = height, CanResize = true };
        _ownedWindow = window;
        EnsureStubHandle(window, "Show 전");
        window.Show();
        EnsureStubHandle(window, "Show 후");
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "초기 레이아웃 후");
        Qa.Check(Find<TabItem>(window, "StructureToolsTab").IsSelected,
            "새 UI 창의 기본 왼쪽 도구 모드가 구조가 아닙니다.");
        Qa.Near(width, window.ClientSize.Width, "Headless 창 실제 가로 크기", 1.5);
        Qa.Near(height, window.ClientSize.Height, "Headless 창 실제 세로 크기", 1.5);
        return (viewModel, window);
    }

    private static async Task SaveFixtureAsync(IWorkspaceFileService fileService, string path, EquationWorkspace workspace)
    {
        var seed = new EditingSession(workspace);
        seed.MarkSaved(seed.CaptureSnapshot());
        await fileService.SaveAsync(path, seed.CaptureSnapshot());
        Qa.Check(File.Exists(path) && new FileInfo(path).Length > 0,
            "얇은 수학 공백 UI fixture가 격리 임시 디렉터리에 저장되지 않았습니다.");
    }

    private static Fixture CreateCompleteFixture()
    {
        var textId = Guid.NewGuid();
        var style = new MathStyleOverride(FontSize: 24, FontFamily: "Cambria Math", Foreground: "#315477");
        var role = MathTextRole.Identifier;
        var source = new TextNode(textId, "ab") { Role = role, StyleOverride = style };
        var root = new SequenceNode(Guid.NewGuid(), [source]);
        var row = new EquationRow(Guid.NewGuid(), root);
        var group = new EquationGroup(Guid.NewGuid(), [row], AlignmentMode.None, RowSpacing: 28);
        var workspace = new EquationWorkspace(Guid.NewGuid(), 1, "수학 공백 UI fixture", [group],
            new MathStyle(30, "Cambria Math", "#25364D"));
        return new Fixture(workspace, textId, role, style);
    }

    private static Fixture CreateRequiredSlotFixture()
    {
        var emptyTextId = Guid.NewGuid();
        var empty = new TextNode(emptyTextId, string.Empty, IsEmptySlot: true);
        var denominator = new TextNode(Guid.NewGuid(), "b");
        var root = new FractionNode(Guid.NewGuid(), empty, denominator);
        var row = new EquationRow(Guid.NewGuid(), root);
        var group = new EquationGroup(Guid.NewGuid(), [row], AlignmentMode.None, RowSpacing: 28);
        var workspace = new EquationWorkspace(Guid.NewGuid(), 1, "빈 분자 슬롯 수학 공백 fixture", [group],
            new MathStyle(30, "Cambria Math", "#25364D"));
        return new Fixture(workspace, emptyTextId, MathTextRole.Automatic, null, denominator.Id);
    }

    private static IEnumerable<MathNode> DescendantsAndSelf(MathNode node)
    {
        yield return node;
        foreach (var child in MathTree.ChildrenOf(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static MathSpaceNode FindThinSpace(EditingSession session, MathSpaceKind kind) =>
        Qa.Nodes(session.Workspace).OfType<MathSpaceNode>().Single(node => node.Kind == kind);

    private static void AssertSingleThinSpaceBetweenText(EditingSession session, Guid spaceId, double fontSize)
    {
        var rowRoot = session.Workspace.Groups.Single().Rows.Single().Root as SequenceNode
            ?? throw new InvalidOperationException("fixture root가 SequenceNode가 아닙니다.");
        var rowChildren = rowRoot.Children.ToArray();
        var spaceIndex = Array.FindIndex(rowChildren, node => node.Id == spaceId);
        Qa.Check(spaceIndex > 0 && spaceIndex < rowChildren.Length - 1 &&
                 rowChildren[spaceIndex - 1] is TextNode && rowChildren[spaceIndex + 1] is TextNode,
            "실제 MathSpaceNode가 분할된 TextNode 둘 사이에 들어가지 않았습니다.");
        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics(),
            new MathLayoutOptions(FontSize: session.Workspace.Style.FontSize));
        var spaceBounds = layout.GetBounds(spaceId)
            ?? throw new InvalidOperationException("얇은 공백 NodeId의 layout bounds가 없습니다.");
        var leftBounds = layout.GetBounds(rowChildren[spaceIndex - 1].Id)
            ?? throw new InvalidOperationException("공백 앞 텍스트 bounds가 없습니다.");
        var rightBounds = layout.GetBounds(rowChildren[spaceIndex + 1].Id)
            ?? throw new InvalidOperationException("공백 뒤 텍스트 bounds가 없습니다.");
        Qa.Near(fontSize / 6, spaceBounds.Width, "얇은 수학 공백 폭 = effective fontSize/6", 0.15);
        Qa.Near(0, spaceBounds.X - leftBounds.Right, "공백 앞 자동 SequenceGap 억제", 0.15);
        Qa.Near(0, rightBounds.X - spaceBounds.Right, "공백 뒤 자동 SequenceGap 억제", 0.15);
        Qa.Check(!layout.Primitives.OfType<GlyphPrimitive>().Any(primitive => primitive.NodeId == spaceId),
            "glyph가 없는 공백 노드에 GlyphPrimitive가 만들어졌습니다.");
    }

    private static void WriteThinSpaceLayoutEvidence(
        string label, MainWindowViewModel viewModel, Guid spaceId, LayoutResult layout,
        NodeLayout spaceLayout, int glyphPrimitiveCount)
    {
        var sequence = viewModel.Session.Workspace.Groups.Single().Rows.Single().Root as SequenceNode
            ?? throw new InvalidOperationException("간격 증거 대상 root가 SequenceNode가 아닙니다.");
        var sequenceChildren = sequence.Children.ToArray();
        var index = Array.FindIndex(sequenceChildren, node => node.Id == spaceId);
        Qa.Check(index > 0 && index < sequenceChildren.Length - 1,
            "공백 layout 증거에서 인접 텍스트 항목을 찾지 못했습니다.");
        var left = layout.Nodes.Single(node => node.NodeId == sequenceChildren[index - 1].Id);
        var right = layout.Nodes.Single(node => node.NodeId == sequenceChildren[index + 1].Id);
        var directory = CreateArtifactDirectory(label);
        var evidence = new
        {
            caseId = CaseId,
            label,
            purpose = "편집 수식 layout에서 비가시 MathSpace bounds와 glyph 없는 제품 preview를 비교하는 수치 증거입니다. 제품 내보내기 파일이 아닙니다.",
            sessionRevision = viewModel.Session.Revision,
            selectedNodeId = viewModel.Session.SelectedNodeId,
            isNodeSelected = viewModel.Session.IsNodeSelected,
            mathSpaceNodeId = spaceId,
            mathSpaceKind = ((MathSpaceNode)Qa.Node(viewModel.Session, spaceId)).Kind.ToString(),
            mathSpaceBounds = Format(spaceLayout.Bounds),
            effectiveFontSize = ((MathSpaceNode)Qa.Node(viewModel.Session, spaceId)).StyleOverride?.FontSize ??
                                viewModel.Session.Workspace.Style.FontSize,
            expectedWidthDip = (((MathSpaceNode)Qa.Node(viewModel.Session, spaceId)).StyleOverride?.FontSize ??
                                viewModel.Session.Workspace.Style.FontSize) / 6,
            leftTextBounds = Format(left.Bounds),
            rightTextBounds = Format(right.Bounds),
            leftGapDip = spaceLayout.Bounds.X - left.Bounds.Right,
            rightGapDip = right.Bounds.X - spaceLayout.Bounds.Right,
            glyphPrimitiveCountForMathSpace = glyphPrimitiveCount,
            layoutPrimitiveCount = layout.Primitives.Length,
            nodeCount = layout.Nodes.Length
        };
        File.WriteAllText(Path.Combine(directory, "thin-space-layout.json"),
            JsonSerializer.Serialize(evidence, JsonOptions), new UTF8Encoding(false));
    }

    private static void WritePreviewSpaceEvidence(
        string label, MainWindowViewModel viewModel, Guid spaceId, LayoutResult layout, NodeLayout spaceLayout)
    {
        var directory = CreateArtifactDirectory(label);
        var evidence = new
        {
            caseId = CaseId,
            label,
            purpose = "제품 preview의 immutable layout에는 공백 bounds가 있지만 그 NodeId에 glyph가 없음을 기록합니다.",
            state = viewModel.OutputPreview.State.ToString(),
            snapshotRevision = viewModel.OutputPreview.PreviewSnapshot?.Revision,
            currentRevision = viewModel.Session.Revision,
            selectedNodeId = viewModel.Session.SelectedNodeId,
            wholeNodeSelection = viewModel.Session.IsNodeSelected,
            mathSpaceNodeId = spaceId,
            previewNodeBounds = Format(spaceLayout.Bounds),
            glyphPrimitiveCountForMathSpace = layout.Primitives.OfType<GlyphPrimitive>()
                .Count(primitive => primitive.NodeId == spaceId),
            totalPrimitiveCount = layout.Primitives.Length,
            exportCanProceed = viewModel.OutputPreview.CanExportSvg
        };
        File.WriteAllText(Path.Combine(directory, "thin-space-preview.json"),
            JsonSerializer.Serialize(evidence, JsonOptions), new UTF8Encoding(false));
    }

    private static string SaveWindowEvidence(MainWindow window, string label, MainWindowViewModel viewModel,
        object scenario)
    {
        EnsureStubHandle(window, label + " 창 증거 저장 전");
        Dispatcher.UIThread.RunJobs();
        var directory = CreateArtifactDirectory(label);
        var pngPath = Path.Combine(directory, "main-window.png");
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width)),
            Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height)));
        using (var bitmap = new RenderTargetBitmap(size, new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Headless 증거 창의 platform handle이 없습니다.");
        var metadata = new
        {
            caseId = CaseId,
            label,
            scenario,
            window = new { width = window.ClientSize.Width, height = window.ClientSize.Height },
            handle = new { descriptor = handle.HandleDescriptor, value = "0x" + handle.Handle.ToInt64().ToString("X"), isStubZero = handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero },
            sessionRevision = viewModel.Session.Revision,
            stateId = viewModel.Session.CaptureSnapshot().StateId,
            dirty = viewModel.Session.IsDirty,
            caret = viewModel.Session.Caret,
            selection = viewModel.Session.Selection,
            selectedNodeId = viewModel.Session.SelectedNodeId,
            isNodeSelected = viewModel.Session.IsNodeSelected,
            workspaceFingerprint = Qa.Fingerprint(viewModel.Session.Workspace),
            mathSpaceIds = Qa.Nodes(viewModel.Session.Workspace).OfType<MathSpaceNode>().Select(node => node.Id).ToArray(),
            outputState = viewModel.OutputPreview.State.ToString(),
            outputRevision = viewModel.OutputPreview.PreviewSnapshot?.Revision,
            png = Path.GetFileName(pngPath)
        };
        Qa.Check(metadata.handle.isStubZero, label + " 캡처 창이 STUB/0이 아닙니다.");
        File.WriteAllText(Path.Combine(directory, "state.json"),
            JsonSerializer.Serialize(metadata, JsonOptions), new UTF8Encoding(false));
        return directory;
    }

    private static string CreateArtifactDirectory(string label)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "doc", "verification", "artifacts", ArtifactScope,
            $"{label}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task CaptureStructureShellAsync(MainWindow window, ScrollViewer scroll, string prefix)
    {
        await SetScrollEdgeAsync(window, scroll, top: true);
        SaveWindowEvidence(window, prefix + "-top", (MainWindowViewModel)window.DataContext!,
            new { edge = "top", offset = scroll.Offset.Y });
        RowShellVisualCases.CaptureAndVerify(window, prefix + "-top", ArtifactScope);
        await SetScrollEdgeAsync(window, scroll, top: false);
        SaveWindowEvidence(window, prefix + "-bottom", (MainWindowViewModel)window.DataContext!,
            new { edge = "bottom", offset = scroll.Offset.Y });
        RowShellVisualCases.CaptureAndVerify(window, prefix + "-bottom", ArtifactScope);
    }

    private static async Task ResizeAsync(MainWindow window, double width, double height)
    {
        EnsureStubHandle(window, "Headless 창 크기 변경 전");
        window.Width = width;
        window.Height = height;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "Headless 창 크기 변경 후");
        Qa.Near(width, window.ClientSize.Width, "Headless 실제 창 가로 크기", 1.5);
        Qa.Near(height, window.ClientSize.Height, "Headless 실제 창 세로 크기", 1.5);
    }

    private static async Task SetScrollEdgeAsync(MainWindow window, ScrollViewer scroll, bool top)
    {
        EnsureStubHandle(window, "구조 도구 ScrollViewer 이동 전");
        var maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, top ? 0 : maxOffset);
        await DrainLayoutAsync(window);
        var actualMax = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        Qa.Check(top ? scroll.Offset.Y <= 0.5 : scroll.Offset.Y >= actualMax - 0.5,
            $"구조 도구 스크롤을 {(top ? "상단" : "하단")}으로 옮기지 못했습니다: offset={scroll.Offset.Y:R}, max={actualMax:R}.");
    }

    private static async Task ClickStructureNodeAsync(MainWindow window, Guid nodeId)
    {
        var tree = Find<TreeView>(window, "StructureTree");
        var item = FindTreeItem(tree, nodeId);
        var header = FindStructureHeader(tree, nodeId);
        header.BringIntoView();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "구조 트리 공백 항목 노출 후");
        Qa.Check(IsFullyVisible(window, header),
            "얇은 수학 공백 구조 header가 실제 Tree viewport에서 완전히 보이지 않습니다: " + Format(BoundsInWindow(window, header)));
        await ClickControlAsync(window, header);
        Qa.Check(item.IsSelected, "얇은 수학 공백 header 클릭으로 실제 TreeViewItem이 선택되지 않았습니다.");
    }

    private static async Task<ControlClickDiagnostic> ControlClickMathNodeAsync(
        MainWindow window, MathEditorControl editor, Guid expectedNodeId, MathRect nodeBounds)
    {
        EnsureStubHandle(window, "편집기 공백 Ctrl+클릭 전");
        var session = editor.Session as EditingSession
            ?? throw new InvalidOperationException("Ctrl+클릭 진단에 실제 편집 Session이 없습니다.");
        var viewport = Find<ScrollViewer>(window, "MathEditorScrollViewer");
        var origin = editor.TranslatePoint(new Point(nodeBounds.X, nodeBounds.Y), window)
            ?? throw new InvalidOperationException("MathSpace layout 좌표를 창 기준으로 변환하지 못했습니다.");
        var point = origin + new Point(nodeBounds.Width / 2, nodeBounds.Height / 2);
        var client = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
        Qa.Check(client.Contains(point), $"얇은 공백 Ctrl+클릭 좌표가 창 경계를 벗어났습니다: {point}; {client}.");
        var initialWindowPoint = point;
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "편집기 공백 Ctrl+클릭 hover 후");
        var refreshedLayout = GetEditorLayout(editor)
            ?? throw new InvalidOperationException("hover 후 편집기 layout을 얻지 못했습니다.");
        var refreshedBounds = refreshedLayout.GetBounds(expectedNodeId)
            ?? throw new InvalidOperationException("hover 후 MathSpace bounds가 없습니다.");
        var refreshedOrigin = editor.TranslatePoint(new Point(refreshedBounds.X, refreshedBounds.Y), window)
            ?? throw new InvalidOperationException("hover 후 MathSpace 좌표를 창으로 변환하지 못했습니다.");
        var refreshedPoint = refreshedOrigin + new Point(refreshedBounds.Width / 2, refreshedBounds.Height / 2);
        Qa.Check(Distance(point, refreshedPoint) <= 0.5 && client.Contains(refreshedPoint),
            "Ctrl+클릭 hover 동안 MathSpace click 위치가 이동하거나 창 밖으로 나갔습니다.");

        var editorPoint = window.TranslatePoint(refreshedPoint, editor)
            ?? throw new InvalidOperationException("Ctrl+클릭 창 좌표를 editor 기준으로 변환하지 못했습니다.");
        var mathPoint = new MathPoint(editorPoint.X, editorPoint.Y);
        var editorWindowBounds = BoundsInWindow(window, editor);
        var viewportWindowBounds = BoundsInWindow(window, viewport);
        var hitTestWindowTarget = window.InputHitTest(refreshedPoint);
        var hitTestEditorTarget = editor.InputHitTest(editorPoint);
        var pointerEvents = new List<PointerPressedObservation>();
        EventHandler<PointerPressedEventArgs> pointerObserver = (_, eventArgs) =>
        {
            var eventWindowPoint = eventArgs.GetPosition(window);
            var eventEditorPoint = eventArgs.GetPosition(editor);
            pointerEvents.Add(new PointerPressedObservation(
                DescribeInputTarget(eventArgs.Source),
                eventArgs.KeyModifiers.ToString(),
                ToPointRecord(eventWindowPoint),
                ToPointRecord(eventEditorPoint),
                eventArgs.Handled,
                session.SelectedNodeId?.ToString("D"),
                session.IsNodeSelected,
                editor.IsFocused));
        };
        window.AddHandler(
            InputElement.PointerPressedEvent,
            pointerObserver,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        var selectedNodeIdBefore = session.SelectedNodeId?.ToString("D");
        var isNodeSelectedBefore = session.IsNodeSelected;
        try
        {
            window.MouseDown(refreshedPoint, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(refreshedPoint, MouseButton.Left, RawInputModifiers.Control);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "편집기 공백 Ctrl+클릭 완료 후");
        }
        finally
        {
            window.RemoveHandler(InputElement.PointerPressedEvent, pointerObserver);
        }

        return new ControlClickDiagnostic(
            expectedNodeId.ToString("D"),
            RawInputModifiers.Control.ToString(),
            selectedNodeIdBefore,
            isNodeSelectedBefore,
            ToPointRecord(initialWindowPoint),
            ToPointRecord(refreshedPoint),
            ToPointRecord(editorPoint),
            ToRectRecord(new Rect(origin, new Size(nodeBounds.Width, nodeBounds.Height))),
            ToRectRecord(new Rect(refreshedOrigin, new Size(refreshedBounds.Width, refreshedBounds.Height))),
            ToRectRecord(editorWindowBounds),
            ToRectRecord(viewportWindowBounds),
            new VectorRecord(viewport.Offset.X, viewport.Offset.Y),
            new SizeRecord(viewport.Viewport.Width, viewport.Viewport.Height),
            new SizeRecord(viewport.Extent.Width, viewport.Extent.Height),
            editorWindowBounds.Contains(refreshedPoint),
            viewportWindowBounds.Contains(refreshedPoint),
            DescribeInputTarget(hitTestWindowTarget),
            DescribeInputTarget(hitTestEditorTarget),
            refreshedLayout.HitTestNode(mathPoint)?.ToString("D"),
            refreshedLayout.HitTestSelectionTarget(mathPoint)?.ToString("D"),
            session.SelectedNodeId?.ToString("D"),
            session.IsNodeSelected,
            pointerEvents.ToArray());
    }

    private static void SaveControlClickEvidence(
        MainWindow window,
        MainWindowViewModel viewModel,
        Guid expectedNodeId,
        ControlClickDiagnostic diagnostic)
    {
        EnsureStubHandle(window, "Ctrl+클릭 진단 증거 저장 전");
        Dispatcher.UIThread.RunJobs();
        var directory = CreateArtifactDirectory("thin-space-control-click-diagnostic");
        var pngPath = Path.Combine(directory, "main-window.png");
        var width = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height));
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            bitmap.Render(window);
            using var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, PngBitmapEncoderOptions.Default);
        }

        var handle = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Ctrl+클릭 진단 증거 창의 platform handle이 없습니다.");
        var metadata = new
        {
            caseId = CaseId,
            label = "thin-space-control-click-diagnostic",
            expectedNodeId = expectedNodeId.ToString("D"),
            diagnostic,
            window = new { width = window.ClientSize.Width, height = window.ClientSize.Height },
            handle = new
            {
                descriptor = handle.HandleDescriptor,
                value = "0x" + handle.Handle.ToInt64().ToString("X"),
                isStubZero = handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero
            },
            currentState = new
            {
                sessionRevision = viewModel.Session.Revision,
                stateId = viewModel.Session.CaptureSnapshot().StateId,
                dirty = viewModel.Session.IsDirty,
                caret = viewModel.Session.Caret,
                selection = viewModel.Session.Selection,
                selectedNodeId = viewModel.Session.SelectedNodeId,
                isNodeSelected = viewModel.Session.IsNodeSelected,
                editorFocused = Find<MathEditorControl>(window, "MathEditorSurface").IsFocused
            },
            png = Path.GetFileName(pngPath)
        };
        File.WriteAllText(Path.Combine(directory, "pointer-diagnostic.json"),
            JsonSerializer.Serialize(metadata, JsonOptions), new UTF8Encoding(false));
        Qa.Check(metadata.handle.isStubZero, "Ctrl+클릭 진단 캡처 창이 STUB/0이 아닙니다.");
    }

    private static void AssertControlClickReachedMathEditor(
        ControlClickDiagnostic diagnostic,
        Guid expectedNodeId)
    {
        var expectedId = expectedNodeId.ToString("D");
        var expectedType = typeof(MathEditorControl).FullName;

        Qa.Check(diagnostic.WindowInputHitTestTarget.Type == expectedType &&
                 diagnostic.WindowInputHitTestTarget.Name == "MathEditorSurface",
            $"창 InputHitTest가 실제 MathEditorSurface를 가리키지 않습니다: " +
            $"{diagnostic.WindowInputHitTestTarget.Type}/{diagnostic.WindowInputHitTestTarget.Name}.");
        Qa.Check(diagnostic.EditorInputHitTestTarget.Type == expectedType &&
                 diagnostic.EditorInputHitTestTarget.Name == "MathEditorSurface",
            $"편집기 InputHitTest가 실제 MathEditorSurface를 가리키지 않습니다: " +
            $"{diagnostic.EditorInputHitTestTarget.Type}/{diagnostic.EditorInputHitTestTarget.Name}.");
        Qa.Equal<string?>(expectedId, diagnostic.ModelHitTestNodeId,
            "실제 편집기 좌표의 모델 HitTestNode가 얇은 공백 ID를 반환");
        Qa.Equal<string?>(expectedId, diagnostic.ModelHitTestSelectionTargetId,
            "실제 편집기 좌표의 모델 HitTestSelectionTarget이 얇은 공백 ID를 반환");

        Qa.Check(diagnostic.PointerPressedEvents.Any(pointerEvent =>
                pointerEvent.EventSource.Type == expectedType &&
                pointerEvent.EventSource.Name == "MathEditorSurface" &&
                pointerEvent.KeyModifiers == RawInputModifiers.Control.ToString() &&
                pointerEvent.Handled &&
                pointerEvent.SelectedNodeIdAfterRoute == expectedId &&
                pointerEvent.IsNodeSelectedAfterRoute),
            "합성 Ctrl+클릭 PointerPressed가 MathEditorSurface에서 Control modifier와 함께 처리되어 " +
            $"공백 {expectedId}를 구조 선택하지 않았습니다.");
        Qa.Equal<string?>(expectedId, diagnostic.SelectedNodeIdAfter,
            "합성 Ctrl+클릭 처리 뒤 선택된 공백 ID");
        Qa.Check(diagnostic.IsNodeSelectedAfter,
            "합성 Ctrl+클릭 처리 뒤 전체 수학 공백 선택 상태");
    }

    private static InputTargetRecord DescribeInputTarget(object? target)
    {
        if (target is Control control)
            return new InputTargetRecord(target.GetType().FullName, control.Name,
                AutomationProperties.GetAutomationId(control));
        return new InputTargetRecord(target?.GetType().FullName, null, null);
    }

    private static PointRecord ToPointRecord(Point point) => new(point.X, point.Y);

    private static RectRecord ToRectRecord(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private sealed record ControlClickDiagnostic(
        string ExpectedNodeId,
        string InjectedRawInputModifiers,
        string? SelectedNodeIdBefore,
        bool IsNodeSelectedBefore,
        PointRecord InitialWindowPoint,
        PointRecord ClickWindowPoint,
        PointRecord ClickEditorPoint,
        RectRecord InitialTargetBoundsInWindow,
        RectRecord RefreshedTargetBoundsInWindow,
        RectRecord EditorBoundsInWindow,
        RectRecord ViewportBoundsInWindow,
        VectorRecord ViewportOffset,
        SizeRecord ViewportSize,
        SizeRecord ExtentSize,
        bool ClickInsideEditorBounds,
        bool ClickInsideViewportBounds,
        InputTargetRecord WindowInputHitTestTarget,
        InputTargetRecord EditorInputHitTestTarget,
        string? ModelHitTestNodeId,
        string? ModelHitTestSelectionTargetId,
        string? SelectedNodeIdAfter,
        bool IsNodeSelectedAfter,
        PointerPressedObservation[] PointerPressedEvents);

    private sealed record PointerPressedObservation(
        InputTargetRecord EventSource,
        string KeyModifiers,
        PointRecord WindowPosition,
        PointRecord EditorPosition,
        bool Handled,
        string? SelectedNodeIdAfterRoute,
        bool IsNodeSelectedAfterRoute,
        bool EditorFocusedAfterRoute);

    private sealed record InputTargetRecord(string? Type, string? Name, string? AutomationId);
    private sealed record PointRecord(double X, double Y);
    private sealed record RectRecord(double X, double Y, double Width, double Height);
    private sealed record VectorRecord(double X, double Y);
    private sealed record SizeRecord(double Width, double Height);

    private static async Task ClickInspectorTabAsync(MainWindow window, TabItem tab)
    {
        EnsureStubHandle(window, "Inspector 탭 클릭 전");
        var headerText = tab.Header?.ToString()
            ?? throw new InvalidOperationException("Inspector 탭 header가 문자열이 아닙니다.");
        var header = tab.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.Text == headerText && text.IsEffectivelyVisible)
            ?? throw new InvalidOperationException($"'{headerText}' Inspector 탭의 실제 제목을 찾지 못했습니다.");
        Qa.Check(tab.IsEffectivelyEnabled, $"'{headerText}' Inspector 탭이 비활성입니다.");
        await ClickControlAsync(window, header);
        Qa.Check(tab.IsSelected, $"'{headerText}' Inspector 탭을 실제 합성 클릭으로 선택하지 못했습니다.");
    }

    private static async Task ClickControlAsync(MainWindow window, Control target)
    {
        EnsureStubHandle(window, "얇은 공백 UI 합성 클릭 전");
        Qa.Check(target.IsEffectivelyEnabled && target.IsEffectivelyVisible,
            "비활성 또는 비표시 control을 합성 클릭하려 했습니다: " + target.Name);
        await BringControlIntoViewAsync(window, target);
        EnsureFullyVisible(window, target, "합성 클릭 직전 control");

        var point = ControlCenterInWindow(window, target);
        Point? stablePoint = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            window.MouseMove(point, RawInputModifiers.None);
            await DrainLayoutAsync(window);
            EnsureStubHandle(window, "얇은 공백 UI 합성 hover 중");
            EnsureFullyVisible(window, target, "hover 후 실제 클릭 대상");
            var refreshed = ControlCenterInWindow(window, target);
            if (Distance(point, refreshed) <= 0.5)
            {
                stablePoint = refreshed;
                break;
            }
            point = refreshed;
        }
        var clickPoint = stablePoint
            ?? throw new InvalidOperationException("hover 뒤 얇은 공백 UI 클릭 좌표가 안정되지 않았습니다.");
        EnsureFullyVisible(window, target, "MouseDown 직전 실제 control");
        window.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(clickPoint, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "얇은 공백 UI 합성 클릭 후");
    }

    private static async Task BringControlIntoViewAsync(MainWindow window, Control target)
    {
        foreach (var scroll in target.GetVisualAncestors().OfType<ScrollViewer>())
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var origin = target.TranslatePoint(new Point(0, 0), scroll)
                    ?? throw new InvalidOperationException("스크롤 내부 control 좌표를 얻지 못했습니다: " + target.Name);
                var top = origin.Y;
                var bottom = top + target.Bounds.Height;
                var margin = Math.Min(8, Math.Max(0, (scroll.Viewport.Height - target.Bounds.Height) / 2));
                if (top >= margin && bottom <= scroll.Viewport.Height - margin)
                    break;
                var desiredTop = target.Bounds.Height + margin * 2 <= scroll.Viewport.Height
                    ? Math.Max(margin, (scroll.Viewport.Height - target.Bounds.Height) / 2)
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

    private static void EnsureFullyVisible(MainWindow window, Control target, string label)
    {
        var bounds = BoundsInWindow(window, target);
        var client = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
        Qa.Check(target.IsEffectivelyVisible && Contains(client, bounds),
            $"{label}: control 전체가 Headless 창 안에 있지 않습니다: {target.Name}={bounds}, client={client}.");
        foreach (var scroll in target.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var origin = scroll.TranslatePoint(new Point(0, 0), window)
                ?? throw new InvalidOperationException($"{label}: ScrollViewer 창 기준 위치를 얻지 못했습니다: {scroll.Name}.");
            var viewport = new Rect(origin, scroll.Viewport);
            Qa.Check(Contains(viewport, bounds),
                $"{label}: {target.Name}가 ScrollViewer viewport 밖으로 잘립니다: target={bounds}, viewport={viewport}, offset={scroll.Offset}, extent={scroll.Extent}.");
        }
    }

    private static bool IsFullyVisible(Window window, Control target)
    {
        if (!target.IsEffectivelyVisible)
            return false;
        var bounds = BoundsInWindow(window, target);
        if (!Contains(new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height), bounds))
            return false;
        foreach (var scroll in target.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var origin = scroll.TranslatePoint(new Point(0, 0), window);
            if (origin is not Point point || !Contains(new Rect(point, scroll.Viewport), bounds))
                return false;
        }
        return true;
    }

    private static void SendKey(Window window, Key key, RawInputModifiers modifiers, PhysicalKey physicalKey, string symbol)
    {
        EnsureStubHandle(window, "얇은 공백 합성 키 입력 전");
        window.KeyPress(key, modifiers, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SendShortcut(Window window, Key key, PhysicalKey physicalKey, string symbol)
    {
        EnsureStubHandle(window, "얇은 공백 undo/redo 합성 입력 전");
        window.KeyPress(key, RawInputModifiers.Control, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static SessionState CaptureSession(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(session.Workspace), snapshot.StateId, snapshot.Revision,
            session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
            session.IsDirty, session.CanUndo, session.CanRedo);
    }

    private static void AssertDocumentHistoryUnchanged(SessionState expected, EditingSession actual, string label)
    {
        var current = CaptureSession(actual);
        Qa.Equal(expected.WorkspaceFingerprint, current.WorkspaceFingerprint, label + " workspace");
        Qa.Equal(expected.StateId, current.StateId, label + " state ID");
        Qa.Equal(expected.Revision, current.Revision, label + " revision");
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
            $"{phase}: Headless window이 STUB/0이 아닙니다.");
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
        EnsureStubHandle(window, "소유 Headless 창 종료 전");
        window.Close();
        for (var attempt = 0; window.IsVisible && attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Qa.Check(!window.IsVisible, "소유 Headless MainWindow를 닫지 못했습니다.");
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "Headless 창 종료 뒤 STUB가 아닌 native handle이 남았습니다.");
        Dispatcher.UIThread.RunJobs();
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static Point ControlCenterInWindow(Window window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("control 중심 좌표를 창 기준으로 변환하지 못했습니다: " + control.Name);
        return origin + new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
    }

    private static Rect BoundsInWindow(Window window, Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("control bounds를 창 기준으로 변환하지 못했습니다: " + control.Name);
        return new Rect(origin, control.Bounds.Size);
    }

    private static TreeViewItem FindTreeItem(TreeView tree, Guid nodeId) =>
        tree.GetVisualDescendants().OfType<TreeViewItem>()
            .FirstOrDefault(item => item.DataContext is StructurePanelItem entry && entry.MathNodeId == nodeId)
        ?? throw new InvalidOperationException("구조 트리에서 얇은 수학 공백 item을 찾지 못했습니다: " + nodeId);

    private static Border FindStructureHeader(TreeView tree, Guid nodeId) =>
        tree.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("structure-tree-entry") &&
                                      border.DataContext is StructurePanelItem item && item.MathNodeId == nodeId)
        ?? throw new InvalidOperationException("얇은 수학 공백의 실제 구조 header를 찾지 못했습니다: " + nodeId);

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name} 컨트롤을 찾지 못했습니다.");

    private static LayoutResult? GetEditorLayout(MathEditorControl editor)
    {
        var result = EnsureEditorLayoutMethod.Invoke(editor, null);
        if (result is null)
            return null;
        return result as LayoutResult
            ?? throw new InvalidOperationException(
                $"MathEditorControl.EnsureLayout이 예상하지 않은 결과 형식 {result.GetType().FullName}을 반환했습니다.");
    }

    private static string Format(Rect rect) =>
        $"x={rect.X:R}; y={rect.Y:R}; width={rect.Width:R}; height={rect.Height:R}";

    private static string Format(MathRect rect) =>
        $"x={rect.X:R}; y={rect.Y:R}; width={rect.Width:R}; height={rect.Height:R}";

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { new DirectoryInfo(Environment.CurrentDirectory), new DirectoryInfo(AppContext.BaseDirectory) })
        {
            for (var current = start; current is not null; current = current.Parent)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "doc", "verification", "artifacts")))
                    return current.FullName;
            }
        }
        throw new DirectoryNotFoundException("얇은 수학 공백 artifact를 저장할 저장소 루트를 찾지 못했습니다.");
    }

    private sealed record Fixture(EquationWorkspace Workspace, Guid SourceTextId, MathTextRole SourceRole,
        MathStyleOverride? SourceStyleOverride, Guid? DenominatorTextId = null);

    private sealed record SessionState(string WorkspaceFingerprint, Guid StateId, long Revision,
        MathCaret Caret, MathSelection? Selection, Guid? SelectedNodeId, bool IsNodeSelected,
        bool IsDirty, bool CanUndo, bool CanRedo);
}
