using System.Diagnostics;
using System.Reflection;
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

namespace AetherMath.LogicQA;

internal static class StructureClearUiInteractionCases
{
    private const string CaseId = "STRUCTURE-CLEAR-UI";
    private const string ArtifactScope = "p2-structure-clear";
    private static MainWindow? _ownedWindow;

    public static IEnumerable<QaCase> All()
    {
        yield return new(CaseId,
            "실제 구조 트리 선택·접힌 경로 노출·네 범위 비우기와 실행 취소의 Headless UI 검증",
            RunHeadlessProcessAsync);
    }

    public static async Task<int> RunHeadlessChildAsync(string caseId)
    {
        if (caseId != CaseId)
        {
            Console.Error.WriteLine("알 수 없는 구조/비우기 UI 시나리오 ID: " + caseId);
            return 2;
        }

        if (Application.Current is not App)
        {
            Console.Error.WriteLine("구조/비우기 UI child가 실제 AetherMath App 구성으로 시작되지 않았습니다.");
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
                await CloseOwnedWindowAsync(_ownedWindow);
            }
            finally
            {
                _ownedWindow = null;
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
        startInfo.ArgumentList.Add("--structure-clear-ui-headless");
        startInfo.ArgumentList.Add(CaseId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("구조/비우기 UI headless child를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("구조/비우기 UI headless child가 제한 시간 안에 끝나지 않았습니다.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !stdout.Contains("PASS " + CaseId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"구조/비우기 UI child 실패(exit={process.ExitCode}). stdout:\n{stdout}\nstderr:\n{stderr}");
        if (!string.IsNullOrWhiteSpace(stderr))
            throw new InvalidOperationException("구조/비우기 UI child가 stderr를 기록했습니다:\n" + stderr);
        Console.Write(stdout);
    }

    private static async Task RunWindowChecksAsync()
    {
        var viewModel = new MainWindowViewModel();
        var preselectedNodeId = viewModel.Session.Caret.NodeId;
        Qa.Check(viewModel.Session.TrySelectNode(preselectedNodeId), "창 생성 전 전체 구조 선택 설정 실패");
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
        var structureTab = Find<TabItem>(window, "StructureInspectorTab");
        inspector.SelectedItem = structureTab;
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "구조 탭 초기 노출 후");
        Qa.Equal("전체 구조 선택", Find<TextBlock>(window, "StructureSelectionModeText").Text,
            "창 생성 시 기존 전체 구조 선택 모드 초기 표시");
        Qa.Check(viewModel.Session.TrySetCaret(new MathCaret(preselectedNodeId, 0)),
            "초기 입력 위치로 복귀 실패");
        await DrainLayoutAsync(window);
        AssertWindowAndBindings(window, viewModel);
        RowShellVisualCases.CaptureAndVerify(window, "structure-initial-minimum", ArtifactScope);
        var initialRoot = viewModel.Session.Workspace.Groups[0].Rows[0].Root;
        await ClickNodeAsync(window, initialRoot.Id);
        Qa.Check(viewModel.Session.IsNodeSelected && viewModel.Session.SelectedNodeId == initialRoot.Id,
            "행 root pointer 선택이 실제 root NodeId를 선택하지 않았습니다.");
        var rootProjection = viewModel.StructurePanel.Items.Single(item => item.MathNodeId == initialRoot.Id);
        foreach (var child in rootProjection.Children)
            Qa.Check(!FindTreeItem(Find<TreeView>(window, "StructureTree"), child.MathNodeId).IsSelected,
                "root 선택 강조가 실제 선택되지 않은 자식 노드까지 번졌습니다.");
        RowShellVisualCases.CaptureAndVerify(window, "structure-root-selection-minimum", ArtifactScope);
        Qa.Check(viewModel.Session.TrySetCaret(new MathCaret(preselectedNodeId, 0)),
            "fixture 작성 전 표면 입력 위치 복구 실패");
        await DrainLayoutAsync(window);

        var session = viewModel.Session;
        var originalGroupId = session.Workspace.Groups[0].Id;
        var originalRowIds = session.Workspace.Groups[0].Rows.Select(row => row.Id).ToArray();
        await ClickButtonAsync(window, "AddGroupButton");
        window.KeyTextInput("G2R1");
        await DrainLayoutAsync(window);
        await ClickButtonAsync(window, "AddRowButton");
        window.KeyTextInput("G2R2");
        await DrainLayoutAsync(window);
        Qa.Equal(2, session.GroupCount, "Headless 구조 fixture 그룹 수");
        Qa.Equal(4, session.Workspace.Groups.Sum(group => group.Rows.Length), "Headless 구조 fixture 행 수");
        Qa.Equal(originalGroupId, session.Workspace.Groups[0].Id, "fixture 작성 중 원 그룹 ID");
        Qa.Check(originalRowIds.SequenceEqual(session.Workspace.Groups[0].Rows.Select(row => row.Id)),
            "fixture 작성이 원래 그룹 행 ID를 바꿨습니다.");

        var deepSeed = FindText(session, "G2R2");
        Qa.Check(session.TrySetCaret(new MathCaret(deepSeed.Id, 0)), "깊은 트리 caret 설정 실패");
        var wrappedNodeId = deepSeed.Id;
        for (var index = 0; index < 12; index++)
        {
            Qa.Check(session.TrySelectNode(wrappedNodeId), $"깊은 구조 fixture {index + 1}단계 선택 실패");
            wrappedNodeId = session.InsertTemplate(MathTemplateKind.Fraction)
                ?? throw new InvalidOperationException($"깊은 구조 fixture {index + 1}단계 삽입 실패");
        }
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "깊은 구조 fixture 후");
        AssertProjection(window, viewModel);

        RowShellVisualCases.CaptureAndVerify(window, "structure-populated-minimum", ArtifactScope);
        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1360×880 재배치 후");
        RowShellVisualCases.CaptureAndVerify(window, "structure-populated-default", ArtifactScope);

        await VerifySurfaceProjectionAndTreeSelectionAsync(window, viewModel);
        await VerifyCollapsedPathRevealOnInspectorReturnAsync(window, viewModel);
        await VerifyClearScopesAndUndoAsync(window, viewModel);

        window.Width = 1060;
        window.Height = 700;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "1060×700 최종 재배치 후");
        await SelectDeepCaretAndRevealAsync(window, viewModel);
        RowShellVisualCases.CaptureAndVerify(window, "structure-deep-minimum", ArtifactScope);

        window.Width = 1360;
        window.Height = 880;
        window.InvalidateMeasure();
        window.InvalidateArrange();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "최종 1360×880 재배치 후");
        await SelectDeepCaretAndRevealAsync(window, viewModel);
        RowShellVisualCases.CaptureAndVerify(window, "structure-deep-default", ArtifactScope);
        var deepLeaf = FindText(viewModel.Session, "G2R2");
        await ClickNodeAsync(window, deepLeaf.Id);
        Qa.Check(viewModel.Session.IsNodeSelected && viewModel.Session.SelectedNodeId == deepLeaf.Id,
            "깊은 leaf pointer 선택이 실제 leaf NodeId를 선택하지 않았습니다.");
        RowShellVisualCases.CaptureAndVerify(window, "structure-leaf-selection-default", ArtifactScope);
    }

    private static void AssertWindowAndBindings(MainWindow window, MainWindowViewModel viewModel)
    {
        EnsureStubHandle(window, "구조 UI 배선 검증 전");
        var tree = Find<TreeView>(window, "StructureTree");
        Qa.Equal("StructureTree", AutomationProperties.GetAutomationId(tree) ?? string.Empty,
            "실제 구조 트리 AutomationId");
        Qa.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            ScrollViewer.GetHorizontalScrollBarVisibility(tree), "깊은 구조용 가로 스크롤 설정");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        Qa.Check(ReferenceEquals(editor.Session, viewModel.Session),
            "구조 트리 UI가 별도 복제 Session에 연결되었습니다.");

        var clearButton = Find<Button>(window, "ClearActiveScopeButton");
        Qa.Check(ReferenceEquals(clearButton.Command, viewModel.ClearActiveScopeCommand),
            "범위 비우기 버튼이 실제 ViewModel command에 연결되지 않았습니다.");
        Qa.Equal(viewModel.CanClearActiveScope, clearButton.IsEffectivelyEnabled,
            "비우기 버튼의 실제 활성화 상태와 CanClearActiveScope가 다릅니다.");
        Qa.Check(ToolTip.GetShowOnDisabled(clearButton), "비활성 비우기 사유 tooltip 설정이 없습니다.");
        Qa.Equal(MathClearScope.CurrentRow, viewModel.SelectedClearScope, "초기 비우기 범위");
        var initialScopeValue = Find<ComboBox>(window, "StructureClearScopeComboBox").SelectedValue;
        Qa.Check(initialScopeValue is MathClearScope,
            "초기 비우기 ComboBox가 enum 값을 선택하지 않았습니다.");
        Qa.Equal(MathClearScope.CurrentRow, (MathClearScope)initialScopeValue!, "초기 비우기 범위 ComboBox 배선");
    }

    private static async Task VerifySurfaceProjectionAndTreeSelectionAsync(
        MainWindow window, MainWindowViewModel viewModel)
    {
        EnsureStubHandle(window, "표면 선택 투영 전");
        var session = viewModel.Session;
        var tree = Find<TreeView>(window, "StructureTree");
        var editor = Find<MathEditorControl>(window, "MathEditorSurface");
        var text = FindText(session, "a+b");

        Qa.Check(session.TrySetCaret(new MathCaret(text.Id, 1)), "표면 caret fixture 설정 실패");
        var initial = CaptureBrowseState(session);
        await DrainLayoutAsync(window);
        Qa.Check(!session.IsNodeSelected && session.Selection is null,
            "표면 caret 이동이 전체 구조 선택으로 변경되었습니다.");
        Qa.Equal<Guid?>(text.Id, viewModel.StructurePanel.SelectedNodeId,
            "표면 caret의 구조 트리 NodeId projection");
        Qa.Equal("표면 입력 위치", Find<TextBlock>(window, "StructureSelectionModeText").Text,
            "표면 caret 상태 문구");
        AssertBrowseStateUnchanged(initial, session, "caret projection");

        Qa.Check(session.TrySetSelection(new MathSelection(new MathCaret(text.Id, 0), new MathCaret(text.Id, 1))),
            "표면 부분 선택 fixture 설정 실패");
        var selectionBefore = CaptureBrowseState(session);
        await DrainLayoutAsync(window);
        Qa.Check(!session.IsNodeSelected && session.Selection is not null,
            "표면 문자소 선택이 전체 구조 선택으로 변환되었습니다.");
        Qa.Equal<Guid?>(text.Id, viewModel.StructurePanel.SelectedNodeId,
            "부분 선택의 구조 트리 NodeId projection");
        Qa.Equal("표면 부분 선택", Find<TextBlock>(window, "StructureSelectionModeText").Text,
            "부분 선택 상태 문구");
        AssertBrowseStateUnchanged(selectionBefore, session, "partial selection projection");
        Qa.Check(ReferenceEquals(editor.Session, session), "부분 선택 projection이 editor Session을 갈아 끼웠습니다.");

        var fraction = Qa.Nodes(session.Workspace).OfType<FractionNode>().First();
        var beforeTreeChoice = CaptureBrowseState(session);
        await ClickNodeAsync(window, fraction.Id);
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == fraction.Id,
            "트리 pointer 선택이 실제 Fraction NodeId를 전체 구조로 선택하지 않았습니다.");
        Qa.Equal(beforeTreeChoice.Revision, session.Revision, "트리 탐색이 편집 revision을 증가시켰습니다.");
        Qa.Equal(beforeTreeChoice.StateId, session.CaptureSnapshot().StateId,
            "트리 탐색이 저장 state ID를 변경했습니다.");
        Qa.Equal(beforeTreeChoice.Workspace, Qa.Fingerprint(session.Workspace),
            "트리 탐색이 수식 workspace를 수정했습니다.");
        Qa.Equal(beforeTreeChoice.Caret, session.Caret, "트리 구조 선택이 표면 caret를 이동했습니다.");
        var selectedFraction = viewModel.StructurePanel.SelectedItem
            ?? throw new InvalidOperationException("분수 선택 후 트리 projection이 없습니다.");
        Qa.Equal(1, selectedFraction.GroupNumber, "선택 노드의 그룹 문맥");
        Qa.Equal(1, selectedFraction.RowNumber, "선택 노드의 행 문맥");
        var activePath = Find<TextBlock>(window, "StructureActivePath").Text ?? string.Empty;
        Qa.Check(activePath.Contains(selectedFraction.Label, StringComparison.Ordinal) &&
                 activePath.Contains("분수", StringComparison.Ordinal),
            "선택 경로에 실제 node 종류가 나타나지 않습니다.");

        var numerator = Qa.Walk(fraction.Numerator).OfType<TextNode>().First(node => node.Text.Length > 0);
        Qa.Check(session.TrySetCaret(new MathCaret(numerator.Id, 0)), "같은 노드 재선택용 caret fixture 실패");
        await DrainLayoutAsync(window);
        Qa.Check(!session.IsNodeSelected && viewModel.StructurePanel.SelectedNodeId == numerator.Id,
            "표면 caret 모드로 돌아간 뒤 트리 선택이 caret leaf와 동기화되지 않았습니다.");
        await ClickNodeAsync(window, numerator.Id);
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == numerator.Id,
            "이미 선택 표시된 같은 노드의 pointer 재클릭이 명시 선택으로 처리되지 않았습니다.");

        await ClickNodeAsync(window, fraction.Id);
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == fraction.Id,
            "Enter 검사 전에 비-leaf Fraction 노드를 선택하지 못했습니다.");
        await DrainLayoutAsync(window);
        var fractionContainer = FindTreeItem(tree, fraction.Id);
        fractionContainer.Focus();
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "구조 트리 Enter 합성 입력 전");
        var fractionExpandedBeforeEnter = fractionContainer.IsExpanded;
        var fractionRevealRequests = 0;
        EventHandler<StructureNodeRevealRequestedEventArgs> onFractionReveal = (_, args) =>
        {
            if (args.NodeId == fraction.Id)
                fractionRevealRequests++;
        };
        viewModel.StructurePanel.RevealNodeRequested += onFractionReveal;
        try
        {
            SendKey(window, Key.Enter, PhysicalKey.Enter, "\r");
            await DrainLayoutAsync(window);
        }
        finally
        {
            viewModel.StructurePanel.RevealNodeRequested -= onFractionReveal;
        }
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == fraction.Id,
            "TreeView 비-leaf Enter가 TreeViewItem 처리 후에도 같은 NodeId를 명시 선택하지 않았습니다.");
        Qa.Equal(fractionExpandedBeforeEnter, fractionContainer.IsExpanded,
            "비-leaf Enter가 TreeView 기본 확장/축소 동작으로 node 표시를 변경했습니다.");
        Qa.Equal(1, fractionRevealRequests,
            "비-leaf Enter가 같은 NodeId의 명시 재선택을 정확히 한 번 알리지 않았습니다.");
        AssertFocusWithinTree(window);

        Qa.Check(session.TrySetCaret(new MathCaret(numerator.Id, 0)), "방향키 검사 caret 복귀 실패");
        await DrainLayoutAsync(window);
        var keyboardStartId = viewModel.StructurePanel.SelectedNodeId
            ?? throw new InvalidOperationException("방향키 검사 시작 선택이 없습니다.");
        var visibleTreeItems = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(item => item.DataContext is StructurePanelItem)
            .Select(item => (Container: item, Node: (StructurePanelItem)item.DataContext!))
            .ToArray();
        var keyboardStartIndex = Array.FindIndex(visibleTreeItems,
            pair => pair.Node.MathNodeId == keyboardStartId);
        Qa.Check(keyboardStartIndex >= 0 && keyboardStartIndex + 1 < visibleTreeItems.Length,
            "TreeView 방향키 다음 항목을 계산할 수 없습니다.");
        var expectedDownNodeId = visibleTreeItems[keyboardStartIndex + 1].Node.MathNodeId;
        FindTreeItem(tree, numerator.Id).Focus();
        SendKey(window, Key.Down, PhysicalKey.ArrowDown, string.Empty);
        await DrainLayoutAsync(window);
        Qa.Equal<Guid?>(expectedDownNodeId, viewModel.StructurePanel.SelectedNodeId,
            "TreeView 방향키가 다음 실제 구조 항목으로 이동");
        Qa.Check(viewModel.StructurePanel.SelectedNodeId != keyboardStartId,
            "TreeView 방향키가 시작 NodeId에 머물렀습니다.");
        Qa.Check(!editor.IsFocused, "트리 방향키 탐색이 focus를 수식 편집기로 빼앗았습니다.");
        AssertFocusWithinTree(window);
        AssertProjection(window, viewModel);
    }

    private static async Task VerifyCollapsedPathRevealOnInspectorReturnAsync(
        MainWindow window, MainWindowViewModel viewModel)
    {
        var session = viewModel.Session;
        var inspector = Find<TabControl>(window, "EquationInspector");
        var tree = Find<TreeView>(window, "StructureTree");
        var deepText = Qa.Nodes(session.Workspace).OfType<TextNode>()
            .First(text => text.Text == "G2R2");
        var path = FindProjectionPath(viewModel.StructurePanel.Items, deepText.Id);
        Qa.Check(path.Count >= 8, $"깊은 구조 fixture가 충분히 중첩되지 않았습니다: {path.Count}개 노드.");

        var collapsedAncestor = FindTreeItem(tree, path[0].MathNodeId);
        collapsedAncestor.IsExpanded = false;
        inspector.SelectedIndex = 0;
        await DrainLayoutAsync(window);
        Qa.Check(session.TrySetCaret(new MathCaret(deepText.Id, 2)), "다른 inspector 탭에서 깊은 표면 caret 설정 실패");
        await DrainLayoutAsync(window);
        Qa.Equal<Guid?>(deepText.Id, viewModel.StructurePanel.SelectedNodeId,
            "구조 탭이 보이지 않을 때도 표면 NodeId projection을 보존해야 합니다.");

        inspector.SelectedItem = Find<TabItem>(window, "StructureInspectorTab");
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "접힌 깊은 선택 경로를 다시 연 뒤");
        AssertDeepNodeVisible(window, viewModel, deepText.Id);
        var treeScroll = FindTreeScrollViewer(tree);
        Qa.Check(treeScroll.Extent.Width > treeScroll.Viewport.Width + 1,
            $"깊은 트리에서 가로 스크롤 가능 범위가 생성되지 않았습니다: extent={treeScroll.Extent.Width:R}, viewport={treeScroll.Viewport.Width:R}.");
        Qa.Equal("표면 입력 위치", Find<TextBlock>(window, "StructureSelectionModeText").Text,
            "다른 탭에서 돌아온 표면 caret 선택 모드");
        AssertProjection(window, viewModel);
    }

    private static async Task VerifyClearScopesAndUndoAsync(MainWindow window, MainWindowViewModel viewModel)
    {
        var session = viewModel.Session;
        var originalWorkspace = Qa.Fingerprint(session.Workspace);
        var originalGroup1 = Qa.Fingerprint(session.Workspace with
        {
            Groups = [session.Workspace.Groups[0]]
        });
        var originalGroup2 = Qa.Fingerprint(session.Workspace with
        {
            Groups = [session.Workspace.Groups[1]]
        });
        var fraction = Qa.Nodes(session.Workspace).OfType<FractionNode>().First();

        Qa.Check(session.TrySelectNode(fraction.Id), "선택 범위 비우기 구조 선택 설정 실패");
        await DrainLayoutAsync(window);
        await SelectClearScopeAsync(window, viewModel, MathClearScope.Selection);
        var selectionBeforeClear = Qa.Fingerprint(session.Workspace);
        await ClickClearAsync(window, viewModel);
        Qa.Check(Qa.Fingerprint(session.Workspace) != selectionBeforeClear,
            "선택 영역 비우기 button이 수식을 변경하지 않았습니다.");
        Qa.Check(session.CanUndo && !Find<Grid>(window, "PendingWorkspaceActionOverlay").IsVisible,
            "선택 범위 비우기가 undo 가능한 단일 편집 없이 승인 modal을 띄웠습니다.");
        await ClickUndoAsync(window, session);
        Qa.Equal(selectionBeforeClear, Qa.Fingerprint(session.Workspace), "선택 영역 비우기 undo");

        var row2Text = FindText(session, "G2R2");
        Qa.Check(session.TrySelectNode(row2Text.Id), "현재 행 비우기 활성 행 선택 실패");
        await DrainLayoutAsync(window);
        await SelectClearScopeAsync(window, viewModel, MathClearScope.CurrentRow);
        Qa.Equal(1, viewModel.ClearScopeAffectedRowCount, "현재 행 영향 행 수");
        var currentRowBefore = Qa.Fingerprint(session.Workspace);
        var group2Id = session.Workspace.Groups[1].Id;
        var group2RowIds = session.Workspace.Groups[1].Rows.Select(row => row.Id).ToArray();
        await ClickClearAsync(window, viewModel);
        Qa.Check(session.Workspace.Groups[1].Rows[1].Root is TextNode { Text.Length: 0, IsEmptySlot: true },
            "현재 행 범위에서 대상 행 root가 빈 필수 슬롯으로 교체되지 않았습니다.");
        Qa.Equal(group2Id, session.Workspace.Groups[1].Id, "현재 행 clear 그룹 ID 보존");
        Qa.Check(group2RowIds.SequenceEqual(session.Workspace.Groups[1].Rows.Select(row => row.Id)),
            "현재 행 clear가 행 ID 또는 순서를 바꿨습니다.");
        await ClickUndoAsync(window, session);
        Qa.Equal(currentRowBefore, Qa.Fingerprint(session.Workspace), "현재 행 clear undo");

        var group2Text = FindText(session, "G2R1");
        Qa.Check(session.TrySelectNode(group2Text.Id), "현재 그룹 clear 활성 그룹 선택 실패");
        await DrainLayoutAsync(window);
        await SelectClearScopeAsync(window, viewModel, MathClearScope.CurrentGroup);
        Qa.Equal(2, viewModel.ClearScopeAffectedRowCount, "현재 그룹 영향 행 수");
        Qa.Equal(MathClearScope.CurrentGroup, viewModel.SelectedClearScope,
            "범위 선택 전에 현재 그룹 scope가 저장되지 않았습니다.");
        Qa.Check(session.TrySetCaret(new MathCaret(FindText(session, "x").Id, 0)),
            "다른 그룹으로 이동하는 scope 유지 caret 설정 실패");
        await DrainLayoutAsync(window);
        Qa.Equal(MathClearScope.CurrentGroup, viewModel.SelectedClearScope,
            "활성 행을 바꾼 뒤 선택한 비우기 scope가 초기화되었습니다.");
        Qa.Check(session.TrySelectNode(group2Text.Id), "그룹 clear 대상 그룹으로 복귀 실패");
        await DrainLayoutAsync(window);
        var currentGroupBefore = Qa.Fingerprint(session.Workspace);
        await ClickClearAsync(window, viewModel);
        Qa.Equal(originalGroup1, Qa.Fingerprint(session.Workspace with { Groups = [session.Workspace.Groups[0]] }),
            "현재 그룹 clear가 비대상 그룹의 내용/ID를 바꿨습니다.");
        Qa.Equal(group2Id, session.Workspace.Groups[1].Id, "현재 그룹 clear 그룹 ID 보존");
        Qa.Check(group2RowIds.SequenceEqual(session.Workspace.Groups[1].Rows.Select(row => row.Id)),
            "현재 그룹 clear 행 ID/순서 보존");
        Qa.Check(session.Workspace.Groups[1].Rows.All(row => row.Root is TextNode { Text.Length: 0, IsEmptySlot: true }),
            "현재 그룹 clear가 그룹의 모든 행 내용을 비우지 않았습니다.");
        await ClickUndoAsync(window, session);
        Qa.Equal(currentGroupBefore, Qa.Fingerprint(session.Workspace), "현재 그룹 clear undo");

        Qa.Check(session.TrySelectNode(fraction.Id), "전체 작업 범위 clear 선택 설정 실패");
        await DrainLayoutAsync(window);
        await SelectClearScopeAsync(window, viewModel, MathClearScope.Workspace);
        Qa.Equal(4, viewModel.ClearScopeAffectedRowCount, "전체 작업 영향 행 수");
        var workspaceBeforeClear = Qa.Fingerprint(session.Workspace);
        var groupIds = session.Workspace.Groups.Select(group => group.Id).ToArray();
        var rowIds = session.Workspace.Groups.Select(group => group.Rows.Select(row => row.Id).ToArray()).ToArray();
        var style = session.Workspace.Style;
        await ClickClearAsync(window, viewModel);
        Qa.Check(session.Workspace.Groups.All(group => group.Rows.All(row =>
                row.Root is TextNode { Text.Length: 0, IsEmptySlot: true })),
            "전체 작업 clear가 모든 행 root를 빈 필수 슬롯으로 만들지 않았습니다.");
        Qa.Check(groupIds.SequenceEqual(session.Workspace.Groups.Select(group => group.Id)),
            "전체 작업 clear가 그룹 ID/순서를 변경했습니다.");
        Qa.Check(rowIds.Zip(session.Workspace.Groups).All(pair =>
                pair.First.SequenceEqual(pair.Second.Rows.Select(row => row.Id))),
            "전체 작업 clear가 행 ID/순서를 변경했습니다.");
        Qa.Equal(style, session.Workspace.Style, "전체 작업 clear에서 스타일 보존");
        Qa.Check(!viewModel.CanClearActiveScope && !Find<Button>(window, "ClearActiveScopeButton").IsEffectivelyEnabled,
            "빈 전체 작업에서 비우기 버튼이 비활성화되지 않았습니다.");
        Qa.Equal(viewModel.ClearScopeDisabledReason,
            ToolTip.GetTip(Find<Button>(window, "ClearActiveScopeButton"))?.ToString(),
            "빈 범위의 비활성 사유 tooltip");
        Qa.Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window, "StructureClearDisabledReason").Text),
            "빈 범위의 비활성 이유가 화면 텍스트로 표시되지 않습니다.");
        Qa.Check(!Find<Grid>(window, "PendingWorkspaceActionOverlay").IsVisible,
            "실행 취소 가능한 전체 작업 clear가 불필요한 승인 modal을 열었습니다.");
        await ClickUndoAsync(window, session);
        Qa.Equal(workspaceBeforeClear, Qa.Fingerprint(session.Workspace), "전체 작업 clear undo");
        Qa.Equal(originalWorkspace, Qa.Fingerprint(session.Workspace), "네 범위 clear/undo 뒤 전체 원본 구조 복원");
        Qa.Equal(originalGroup2, Qa.Fingerprint(session.Workspace with { Groups = [session.Workspace.Groups[1]] }),
            "그룹 clear/undo가 원 그룹 구조를 복원하지 않았습니다.");
        AssertProjection(window, viewModel);
    }

    private static async Task SelectDeepCaretAndRevealAsync(MainWindow window, MainWindowViewModel viewModel)
    {
        var session = viewModel.Session;
        var deepText = FindText(session, "G2R2");
        Qa.Check(session.TrySetCaret(new MathCaret(deepText.Id, 2)), "깊은 caret 재노출 설정 실패");
        await DrainLayoutAsync(window);
        AssertDeepNodeVisible(window, viewModel, deepText.Id);
        var treeScroll = FindTreeScrollViewer(Find<TreeView>(window, "StructureTree"));
        Qa.Check(treeScroll.Extent.Width > treeScroll.Viewport.Width + 1,
            "깊은 구조에서 수평 스크롤을 사용할 수 없습니다.");
    }

    private static async Task SelectClearScopeAsync(
        MainWindow window, MainWindowViewModel viewModel, MathClearScope scope)
    {
        EnsureStubHandle(window, "clear scope 변경 전");
        var combo = Find<ComboBox>(window, "StructureClearScopeComboBox");
        combo.SelectedValue = scope;
        await DrainLayoutAsync(window);
        Qa.Equal(scope, viewModel.SelectedClearScope, "UI ComboBox 변경이 ViewModel clear scope를 갱신");
        var comboValue = combo.SelectedValue;
        Qa.Check(comboValue is MathClearScope, "clear scope ComboBox가 enum 값을 선택하지 않았습니다.");
        Qa.Equal(scope, (MathClearScope)comboValue!, "clear scope ComboBox 표시");
    }

    private static async Task ClickClearAsync(MainWindow window, MainWindowViewModel viewModel)
    {
        EnsureStubHandle(window, "Clear command 클릭 전");
        var button = Find<Button>(window, "ClearActiveScopeButton");
        Qa.Check(ReferenceEquals(button.Command, viewModel.ClearActiveScopeCommand), "Clear button command 참조가 다릅니다.");
        Qa.Check(button.IsEffectivelyEnabled && viewModel.CanClearActiveScope,
            $"실행할 범위가 비활성화되어 있습니다: {viewModel.ClearScopeDisabledReason}");
        await ClickControlAsync(window, button);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "Clear command 클릭 후");
        Qa.Check(viewModel.Session.CanUndo, "범위 clear 결과가 실행 취소 history에 들어가지 않았습니다.");
        Qa.Check(!Find<Grid>(window, "PendingWorkspaceActionOverlay").IsVisible,
            "범위 clear 뒤 수명 확인 overlay가 열렸습니다.");
        AssertProjection(window, viewModel);
    }

    private static async Task ClickUndoAsync(MainWindow window, EditingSession session)
    {
        EnsureStubHandle(window, "clear undo 클릭 전");
        Qa.Check(session.CanUndo, "clear undo를 실행할 history가 없습니다.");
        await ClickButtonAsync(window, "UndoButton");
        Qa.Check(session.CanRedo, "clear undo 뒤 redo가 활성화되지 않았습니다.");
        AssertProjection(window, (MainWindowViewModel)window.DataContext!);
    }

    private static async Task ClickButtonAsync(MainWindow window, string name) =>
        await ClickControlAsync(window, Find<Button>(window, name));

    private static async Task ClickNodeAsync(MainWindow window, Guid nodeId)
    {
        var tree = Find<TreeView>(window, "StructureTree");
        var header = FindStructureHeader(tree, nodeId);
        header.BringIntoView();
        await DrainLayoutAsync(window);
        var headerOrigin = header.TranslatePoint(new Point(0, 0), tree);
        Qa.Check(headerOrigin is Point, "합성 클릭 대상 구조 header의 Tree 좌표를 얻지 못했습니다.");
        var treeViewport = new Rect(0, 0, tree.Bounds.Width, tree.Bounds.Height);
        var clickHeaderBounds = new Rect(headerOrigin!.Value, header.Bounds.Size);
        Qa.Check(treeViewport.Contains(clickHeaderBounds.TopLeft) &&
                 treeViewport.Contains(clickHeaderBounds.BottomRight),
            "합성 클릭 대상 구조 header 전체가 Tree viewport 안에 들어오지 않습니다.");
        EnsureStubHandle(window, "구조 노드 합성 pointer 입력 전");
        await ClickControlAsync(window, header);
    }

    private static async Task ClickControlAsync(MainWindow window, Control target)
    {
        EnsureStubHandle(window, "합성 pointer 입력 전");
        if (target is Button button)
            Qa.Check(button.IsEffectivelyEnabled, "비활성 UI control 합성 클릭 시도: " + AutomationProperties.GetAutomationId(button));
        Qa.Check(target.IsEffectivelyVisible, "합성 입력 대상이 visual tree에서 보이지 않습니다.");
        var origin = target.TranslatePoint(new Point(0, 0), window);
        Qa.Check(origin is Point, "합성 입력 대상의 창 좌표를 얻지 못했습니다.");
        var point = origin!.Value + new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
        Qa.Check(point.X >= 0 && point.Y >= 0 && point.X < window.ClientSize.Width && point.Y < window.ClientSize.Height,
            "합성 입력 대상 중심이 창 경계 밖입니다: " + point);
        window.MouseMove(point, RawInputModifiers.None);
        await DrainLayoutAsync(window);
        EnsureStubHandle(window, "합성 pointer 입력 중");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainLayoutAsync(window);
    }

    private static void SendKey(MainWindow window, Key key, PhysicalKey physicalKey, string symbol)
    {
        EnsureStubHandle(window, "합성 키 입력 전");
        window.KeyPress(key, RawInputModifiers.None, physicalKey, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertDeepNodeVisible(MainWindow window, MainWindowViewModel viewModel, Guid nodeId)
    {
        var tree = Find<TreeView>(window, "StructureTree");
        Qa.Equal<Guid?>(nodeId, viewModel.StructurePanel.SelectedNodeId, "깊은 표면 NodeId의 Tree projection");
        var path = FindProjectionPath(viewModel.StructurePanel.Items, nodeId);
        Qa.Check(path.Count >= 8, $"깊은 경로가 너무 얕습니다: {path.Count}개 노드.");
        foreach (var ancestor in path.Take(path.Count - 1))
            Qa.Check(FindTreeItem(tree, ancestor.MathNodeId).IsExpanded,
                "현재 선택된 깊은 노드의 조상이 닫혀 있습니다: " + ancestor.Label);

        var header = FindStructureHeader(tree, nodeId);
        var scrollViewer = FindTreeScrollViewer(tree);
        var origin = header.TranslatePoint(new Point(0, 0), scrollViewer);
        Qa.Check(origin is Point, "깊은 노드 header를 실제 ScrollViewer viewport 좌표로 옮기지 못했습니다.");
        var headerBounds = new Rect(origin!.Value, header.Bounds.Size);
        var viewport = new Rect(0, 0, scrollViewer.Viewport.Width, scrollViewer.Viewport.Height);
        Qa.Check(viewport.Contains(headerBounds.TopLeft) && viewport.Contains(headerBounds.BottomRight),
            $"깊은 노드 header가 스크롤바를 제외한 실제 viewport 안에 완전히 노출되지 않았습니다: " +
            $"header={headerBounds}, viewport={scrollViewer.Viewport}, extent={scrollViewer.Extent}, offset={scrollViewer.Offset}.");
        var session = viewModel.Session;
        if (session.Caret.NodeId == nodeId && !session.IsNodeSelected)
            Qa.Equal("표면 입력 위치", Find<TextBlock>(window, "StructureSelectionModeText").Text,
                "깊은 표면 caret 표시 방식");
    }

    private static void AssertProjection(MainWindow window, MainWindowViewModel viewModel)
    {
        var session = viewModel.Session;
        var panel = viewModel.StructurePanel;
        var expectedSelectedNode = session.IsNodeSelected ? session.SelectedNodeId : session.Caret.NodeId;
        Qa.Equal(expectedSelectedNode, panel.SelectedNodeId, "구조 트리 projection의 활성 NodeId");
        Qa.Equal(session.Workspace.Groups.Sum(group => group.Rows.Length), panel.Items.Length,
            "구조 트리 최상위 root 수");
        foreach (var root in panel.Items)
            AssertNodeIds(root);
        var selectedItem = panel.SelectedItem
            ?? throw new InvalidOperationException("현재 Session 선택에 대응하는 트리 item이 없습니다.");
        var selectedContainer = FindTreeItem(Find<TreeView>(window, "StructureTree"), selectedItem.MathNodeId);
        Qa.Check(selectedContainer.IsSelected, "현재 Session NodeId의 TreeViewItem이 selected 상태가 아닙니다.");
        var activePath = Find<TextBlock>(window, "StructureActivePath").Text ?? string.Empty;
        Qa.Check(activePath.Contains(selectedItem.Label, StringComparison.Ordinal),
            "선택 경로가 실제 활성 구조 항목을 포함하지 않습니다.");
        var mode = session.IsNodeSelected ? "전체 구조 선택" : session.Selection is not null ? "표면 부분 선택" : "표면 입력 위치";
        Qa.Equal(mode, Find<TextBlock>(window, "StructureSelectionModeText").Text, "트리와 수식 선택 모드 설명");
    }

    private static void AssertNodeIds(StructurePanelItem item)
    {
        Qa.Equal(item.Id, item.MathNodeId, "Tree item ID와 실제 MathNode ID");
        foreach (var child in item.Children)
            AssertNodeIds(child);
    }

    private static BrowseState CaptureBrowseState(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new BrowseState(Qa.Fingerprint(session.Workspace), snapshot.Revision, snapshot.StateId,
            session.Caret, session.CanUndo, session.CanRedo, session.IsDirty);
    }

    private static void AssertBrowseStateUnchanged(BrowseState before, EditingSession session, string label)
    {
        var after = CaptureBrowseState(session);
        Qa.Equal(before.Workspace, after.Workspace, label + " workspace");
        Qa.Equal(before.Revision, after.Revision, label + " revision");
        Qa.Equal(before.StateId, after.StateId, label + " state ID");
        Qa.Equal(before.Caret, after.Caret, label + " caret");
        Qa.Equal(before.CanUndo, after.CanUndo, label + " undo history");
        Qa.Equal(before.CanRedo, after.CanRedo, label + " redo history");
        Qa.Equal(before.IsDirty, after.IsDirty, label + " dirty state");
    }

    private static TextNode FindText(EditingSession session, string exactText) =>
        Qa.Nodes(session.Workspace).OfType<TextNode>().Single(text => text.Text == exactText);

    private static IReadOnlyList<StructurePanelItem> FindProjectionPath(
        IEnumerable<StructurePanelItem> items, Guid nodeId)
    {
        foreach (var item in items)
        {
            if (item.MathNodeId == nodeId)
                return [item];
            var childPath = FindProjectionPath(item.Children, nodeId);
            if (childPath.Count > 0)
                return new[] { item }.Concat(childPath).ToArray();
        }
        return Array.Empty<StructurePanelItem>();
    }

    private static TreeViewItem FindTreeItem(TreeView tree, Guid nodeId) =>
        tree.GetVisualDescendants().OfType<TreeViewItem>()
            .FirstOrDefault(candidate => candidate.DataContext is StructurePanelItem item && item.MathNodeId == nodeId)
        ?? throw new InvalidOperationException("TreeViewItem container를 찾지 못했습니다: " + nodeId);

    private static Border FindStructureHeader(TreeView tree, Guid nodeId) =>
        tree.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(candidate => candidate.Classes.Contains("structure-tree-entry") &&
                                         candidate.DataContext is StructurePanelItem item && item.MathNodeId == nodeId)
        ?? throw new InvalidOperationException("수식 노드 header를 찾지 못했습니다: " + nodeId);

    private static ScrollViewer FindTreeScrollViewer(TreeView tree) =>
        tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()
        ?? throw new InvalidOperationException("StructureTree 내부 ScrollViewer를 찾지 못했습니다.");

    private static void AssertFocusWithinTree(MainWindow window)
    {
        var tree = Find<TreeView>(window, "StructureTree");
        var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
        Qa.Check(focused is Control control &&
                 (ReferenceEquals(control, tree) || control.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, tree))),
            "방향키 뒤 키보드 focus가 구조 트리 밖으로 이동했습니다.");
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
            $"{phase}: 실제/native 창 handle이 아닌 Headless STUB/0가 필요합니다.");
    }

    private static async Task CloseOwnedWindowAsync(MainWindow? window)
    {
        if (window is null)
            return;

        var viewModel = window.DataContext as MainWindowViewModel;
        if (viewModel is not null)
        {
            if (viewModel.IsPendingWorkspaceActionVisible)
                viewModel.CancelPendingWorkspaceAction();
            viewModel.Session.MarkSaved(viewModel.Session.CaptureSnapshot());
        }

        EnsureStubHandle(window, "소유 Headless 창 닫기 전");
        window.Close();
        await DrainLayoutAsync(window);
        var handle = window.TryGetPlatformHandle();
        Qa.Check(handle is null || handle.HandleDescriptor == "STUB" && handle.Handle == IntPtr.Zero,
            "소유 창 정리 뒤 STUB/0 외 실제/native handle이 남았습니다.");
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? throw new InvalidOperationException($"실제 MainWindow visual tree에서 {name}을 찾지 못했습니다.");

    private readonly record struct BrowseState(
        string Workspace,
        long Revision,
        Guid StateId,
        MathCaret Caret,
        bool CanUndo,
        bool CanRedo,
        bool IsDirty);
}
