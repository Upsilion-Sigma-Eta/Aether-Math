using System.Collections.Immutable;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class StructureClearCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("STRUCT-PROJECTION", "구조 트리의 문서 순서·실제 ID·슬롯 순서 투영", ProjectionMatchesWorkspace);
        yield return new("STRUCT-SELECT-PURITY", "트리 선택과 표면 caret/부분선택 동기화의 편집 상태 순수성", SelectionAndSessionReplacementStayInSync);
        yield return new("CLEAR-SELECTION", "정·역 Unicode 부분선택 및 전체 구조 선택만 비우기", ClearSelectionOnly);
        yield return new("CLEAR-ROW", "교차 행 선택에서 선택 노드 행만 비우고 행 식별자 보존", ClearSelectedRowOnly);
        yield return new("CLEAR-GROUP", "교차 그룹 선택에서 선택 노드 그룹만 비우기", ClearSelectedGroupOnly);
        yield return new("CLEAR-WORKSPACE", "작업 전체 비우기의 행·그룹·스타일 보존과 활성 caret", ClearWorkspaceRoots);
        yield return new("CLEAR-NOOP", "무효 범위·빈 범위는 완전 무변경, 빈 구조는 비우기 가능", EmptyAndInvalidScopesAreNoOps);
        yield return new("CLEAR-HISTORY", "단일 Commit·undo/redo·고정 범위 선택과 SVG 출력 경계", ClearHistoryAndViewModelContract);
    }

    private static void ProjectionMatchesWorkspace()
    {
        var matrix = new MatrixNode(Guid.NewGuid(), 2, 2,
        [Qa.Text("α"), Qa.Text(string.Empty, true), Qa.Text("👩‍🔬"), Qa.Text("4")]);
        var fraction = new FractionNode(Guid.NewGuid(), new SequenceNode(Guid.NewGuid(), [Qa.Text("분자"), matrix]),
            new RadicalNode(Guid.NewGuid(), Qa.Text("분모")));
        var script = new ScriptNode(Guid.NewGuid(), Qa.Text("밑"), Qa.Text("아래"), Qa.Text("위"));
        var integral = new IntegralNode(Guid.NewGuid(), Qa.Text("피적분"), Qa.Text("하한"), Qa.Text("상한"), Qa.Text("d"));
        var sum = new SummationNode(Guid.NewGuid(), Qa.Text("합"), Qa.Text("i=0"), Qa.Text("n"));
        var limit = new LimitNode(Guid.NewGuid(), Qa.Text("x→0"), Qa.Text("극한"));
        var brackets = new BracketsNode(Guid.NewGuid(), MathBracketKind.Angle, Qa.Text("안"));
        var cases = new CasesNode(Guid.NewGuid(), ImmutableArray.Create(
            new CaseRow(Qa.Text("식1"), Qa.Text("조건1")),
            new CaseRow(Qa.Text("식2"), Qa.Text("조건2"))));
        var firstRoot = Qa.Sequence(fraction, script, integral, sum, limit, brackets, cases);
        var secondRoot = new RadicalNode(Guid.NewGuid(), Qa.Text("다른 행"));
        var group1 = Group([Row(firstRoot), Row(secondRoot)], AlignmentMode.Equals, 23.5);
        var group2 = Group([Row(new BracketsNode(Guid.NewGuid(), MathBracketKind.Curly, Qa.Text("그룹2")))], AlignmentMode.None, 41);
        var workspace = Workspace(group1, group2);
        var session = new EditingSession(workspace);
        var panel = new StructurePanelViewModel(session);

        var expectedRoots = workspace.Groups.SelectMany((group, groupIndex) => group.Rows.Select((row, rowIndex) =>
            (row.Root, Group: groupIndex + 1, Row: rowIndex + 1))).ToArray();
        Qa.Equal(expectedRoots.Length, panel.Items.Length, "최상위 item은 행별 실제 root 수와 일치");
        for (var i = 0; i < expectedRoots.Length; i++)
        {
            var (node, groupNumber, rowNumber) = expectedRoots[i];
            var item = panel.Items[i];
            Qa.Equal(node.Id, item.Id, "root Item.Id가 node ID와 일치");
            Qa.Equal(node.Id, item.MathNodeId, "root MathNodeId가 실제 node와 일치");
            Qa.Equal(Kind(node), item.Kind, "root Kind가 실제 AST 종류와 일치");
            Qa.Equal(groupNumber, item.GroupNumber, "root 그룹 위치");
            Qa.Equal(rowNumber, item.RowNumber, "root 행 위치");
            Qa.Check(item.Label.StartsWith($"그룹 {groupNumber} · {rowNumber}행 ·", StringComparison.Ordinal),
                "root Label에 그룹/행 위치가 없음: " + item.Label);
        }

        var flattened = Flatten(panel.Items).ToArray();
        var expectedNodes = Qa.Nodes(workspace).ToArray();
        Qa.Equal(expectedNodes.Length, flattened.Length, "투영 전체 node 개수");
        Qa.Check(flattened.Select(item => item.MathNodeId).SequenceEqual(expectedNodes.Select(node => node.Id)),
            "투영 item의 preorder가 실제 workspace node preorder와 다름");
        Qa.Equal(flattened.Length, flattened.Select(item => item.Id).Distinct().Count(), "투영 ID가 중복됨");
        Qa.Equal(flattened.Length, flattened.Select(item => item.MathNodeId).Distinct().Count(), "MathNodeId가 중복됨");
        var nodeById = expectedNodes.ToDictionary(node => node.Id);
        foreach (var item in flattened)
        {
            Qa.Equal(item.MathNodeId, item.Id, "모든 투영 item의 Id/MathNodeId 일치");
            Qa.Equal(Kind(nodeById[item.MathNodeId]), item.Kind, "투영 Kind와 실제 node 종류");
            Qa.Equal(item.GroupNumber, item.Children.FirstOrDefault()?.GroupNumber ?? item.GroupNumber,
                "부모·자식 그룹 위치 전파");
            Qa.Equal(item.RowNumber, item.Children.FirstOrDefault()?.RowNumber ?? item.RowNumber,
                "부모·자식 행 위치 전파");
        }

        var fractionItem = Find(panel.Items, fraction.Id);
        Qa.Equal(2, fractionItem.Children.Length, "분수 슬롯 개수");
        Qa.Check(fractionItem.Children[0].Label.StartsWith("분자 ·", StringComparison.Ordinal), "첫 분수 슬롯 역할 순서");
        Qa.Check(fractionItem.Children[1].Label.StartsWith("분모 ·", StringComparison.Ordinal), "둘째 분수 슬롯 역할 순서");
        var matrixItem = Find(panel.Items, matrix.Id);
        Qa.Check(matrixItem.Children.Select(child => child.Label.Split('·')[0].Trim())
                .SequenceEqual(new[] { "셀 1,1", "셀 1,2", "셀 2,1", "셀 2,2" }),
            "행렬 슬롯의 row-major 인덱스 순서");
        var casesItem = Find(panel.Items, cases.Id);
        Qa.Check(casesItem.Children.Select(child => child.Label.Split('·')[0].Trim())
                .SequenceEqual(new[] { "경우 1 식", "경우 1 조건", "경우 2 식", "경우 2 조건" }),
            "경우 노드의 행 우선 식/조건 순서");
        Qa.Check(Find(panel.Items, matrix.Cells[1].Id).Label.Contains("빈 텍스트 슬롯", StringComparison.Ordinal),
            "빈 구조 슬롯도 트리에서 빠지지 않음");
    }

    private static void SelectionAndSessionReplacementStayInSync()
    {
        var first = Qa.Text("A🙂e\u0301Z");
        var second = Qa.Text("분모");
        var fraction = new FractionNode(Guid.NewGuid(), Qa.Text("분자"), second);
        var root = Qa.Sequence(first, fraction);
        var workspace = Workspace(Group([Row(root), Row(Qa.Text("다음 행"))]));
        var session = new EditingSession(workspace);
        var panel = new StructurePanelViewModel(session);
        var beforeRevision = session.Revision;
        var beforeWorkspace = Qa.Fingerprint(session.Workspace);
        var beforeStateId = session.CaptureSnapshot().StateId;
        int revealCount = 0;
        Guid? lastReveal = null;
        panel.RevealNodeRequested += (_, args) => { revealCount++; lastReveal = args.NodeId; };

        Qa.Check(panel.SelectNode(fraction.Id), "트리에서 실제 구조 ID 선택 실패");
        Qa.Equal<Guid?>(fraction.Id, panel.SelectedNodeId, "tree selected node ID");
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == fraction.Id, "tree 선택이 Session 전체 구조 선택에 반영되지 않음");
        Qa.Equal(beforeRevision, session.Revision, "순수 트리 선택이 revision을 바꿈");
        Qa.Equal(beforeStateId, session.CaptureSnapshot().StateId, "순수 트리 선택이 history state ID를 바꿈");
        Qa.Equal(beforeWorkspace, Qa.Fingerprint(session.Workspace), "순수 트리 선택이 AST를 바꿈");
        Qa.Check(!session.IsDirty && !session.CanUndo && !session.CanRedo, "순수 트리 선택이 dirty/history를 바꿈");
        Qa.Equal(1, revealCount, "구조 트리 선택 reveal 이벤트 수");
        Qa.Equal<Guid?>(fraction.Id, lastReveal, "reveal node ID");
        Qa.Check(panel.SelectNode(fraction.Id), "같은 트리 항목 재선택 실패");
        Qa.Equal(2, revealCount, "같은 항목 재선택의 reveal 이벤트 누락");
        var beforeInvalidTreeSelection = Capture(session);
        Qa.Check(!panel.SelectNode(Guid.NewGuid()), "트리에 없는 ID 선택이 허용됨");
        AssertUnchanged(beforeInvalidTreeSelection, Capture(session), "없는 구조 ID 선택의 전체 상태");
        Qa.Equal(2, revealCount, "없는 구조 ID 선택에서 reveal 이벤트 발생");
        panel.SelectedItem = null;
        Qa.Equal<Guid?>(fraction.Id, panel.SelectedNodeId, "TwoWay null 재진입이 트리 선택을 지움");

        Qa.Check(session.TrySetCaret(new MathCaret(first.Id, 1)), "표면 caret 설정");
        Qa.Equal<Guid?>(first.Id, panel.SelectedNodeId, "표면 caret 변경이 트리 selected item에 반영되지 않음");
        Qa.Check(!session.IsNodeSelected, "caret 동기화가 전체 구조 선택을 남김");
        var revision = session.Revision;
        var stateId = session.CaptureSnapshot().StateId;
        var dirty = session.IsDirty;
        Qa.Check(session.TrySetSelection(new(new(first.Id, 1), new(first.Id, 3))), "정방향 문자소 부분선택 설정");
        Qa.Equal<Guid?>(first.Id, panel.SelectedNodeId, "정방향 부분선택 후 tree 대상");
        Qa.Check(!session.IsNodeSelected, "정방향 부분선택이 전체 구조 선택으로 반전");
        Qa.Equal(new MathSelection(new(first.Id, 1), new(first.Id, 3)), session.Selection, "정방향 부분선택 anchor/focus");
        Qa.Equal(revision, session.Revision, "부분선택이 revision을 바꿈");
        Qa.Equal(stateId, session.CaptureSnapshot().StateId, "부분선택이 state ID를 바꿈");
        Qa.Equal(dirty, session.IsDirty, "부분선택이 dirty를 바꿈");
        var forwardSelection = session.Selection;
        var forwardCaret = session.Caret;
        var forwardRevision = session.Revision;
        var forwardStateId = session.CaptureSnapshot().StateId;
        var forwardWorkspace = Qa.Fingerprint(session.Workspace);
        var echoedItem = panel.SelectedItem;
        panel.SelectedItem = echoedItem;
        Qa.Equal(forwardSelection, session.Selection, "동일 SelectedItem echo가 정방향 부분선택을 바꿈");
        Qa.Equal(forwardCaret, session.Caret, "동일 SelectedItem echo가 caret를 바꿈");
        Qa.Check(!session.IsNodeSelected, "동일 SelectedItem echo가 부분선택을 전체 구조 선택으로 반전");
        Qa.Equal(forwardRevision, session.Revision, "동일 SelectedItem echo가 revision을 바꿈");
        Qa.Equal(forwardStateId, session.CaptureSnapshot().StateId, "동일 SelectedItem echo가 history state를 바꿈");
        Qa.Equal(forwardWorkspace, Qa.Fingerprint(session.Workspace), "동일 SelectedItem echo가 workspace를 바꿈");
        panel.SelectedItem = Find(panel.Items, fraction.Id);
        Qa.Equal(forwardSelection, session.Selection, "다른 SelectedItem presentation echo가 정방향 부분선택을 바꿈");
        Qa.Equal(forwardCaret, session.Caret, "다른 SelectedItem presentation echo가 caret를 바꿈");
        Qa.Equal<Guid?>(first.Id, panel.SelectedNodeId, "다른 presentation echo가 표면 caret 대상의 tree selection을 덮음");
        Qa.Check(!session.IsNodeSelected, "다른 SelectedItem echo가 부분선택을 전체 구조 선택으로 반전");
        Qa.Equal(forwardRevision, session.Revision, "다른 SelectedItem echo가 revision을 바꿈");
        Qa.Check(session.TrySetSelection(new(new(first.Id, 3), new(first.Id, 1))), "역방향 문자소 부분선택 설정");
        Qa.Equal(new MathSelection(new(first.Id, 3), new(first.Id, 1)), session.Selection, "역방향 anchor/focus가 보존됨");
        Qa.Check(!session.IsNodeSelected, "역방향 부분선택이 전체 구조 선택으로 반전");
        Qa.Equal<Guid?>(first.Id, panel.SelectedNodeId, "역방향 부분선택 후 tree 대상");
        var reverseSelection = session.Selection;
        panel.SelectedItem = panel.SelectedItem;
        Qa.Equal(reverseSelection, session.Selection, "동일 SelectedItem echo가 역방향 부분선택을 바꿈");
        Qa.Check(!session.IsNodeSelected, "역방향 동일 item echo가 전체 구조 선택으로 반전");
        panel.SelectedItem = null;
        Qa.Equal(reverseSelection, session.Selection, "null SelectedItem echo가 역방향 부분선택을 지움");

        int focusRequests = 0;
        var viewModel = new MainWindowViewModel();
        var stablePanel = viewModel.StructurePanel;
        viewModel.EditorFocusRequested += (_, _) => focusRequests++;
        var oldSession = viewModel.Session;
        var panelNotifications = new List<string>();
        stablePanel.PropertyChanged += (_, args) => panelNotifications.Add(args.PropertyName ?? string.Empty);
        Qa.Equal(WorkspaceActionRequestResult.Completed, viewModel.RequestNewWorkspace(), "깨끗한 작업의 새 작업 즉시 전환");
        Qa.Equal(1, focusRequests, "새 작업 전환의 정상 editor focus 요청");
        Qa.Check(ReferenceEquals(stablePanel, viewModel.StructurePanel), "Session 교체가 StructurePanel 인스턴스를 갈아 끼움");
        Qa.Check(!ReferenceEquals(oldSession, viewModel.Session), "새 작업에서 Session이 교체되지 않음");
        var itemsNotificationIndex = panelNotifications.IndexOf(nameof(StructurePanelViewModel.Items));
        var selectedNotificationIndex = panelNotifications.IndexOf(nameof(StructurePanelViewModel.SelectedItem));
        Qa.Check(itemsNotificationIndex >= 0 && selectedNotificationIndex > itemsNotificationIndex,
            "Session 교체 projection에서 Items 알림 전에 selection 복원이 일어남");
        AssertProjectionEqualsSession(stablePanel, viewModel.Session);
        var insertedId = viewModel.Session.InsertTemplate(MathTemplateKind.Fraction);
        Qa.Check(insertedId.HasValue, "교체 Session에 구조 삽입");
        Qa.Check(Find(stablePanel.Items, insertedId!.Value) is not null, "동일 StructurePanel이 새 Session의 Workspace 변경을 구독하지 않음");
        Qa.Check(stablePanel.SelectNode(insertedId.Value), "교체 Session의 신규 구조 트리 선택");
        Qa.Equal(1, focusRequests, "트리 reveal/select가 추가 editor focus를 요청함");
        Qa.Check(viewModel.Session.IsNodeSelected && viewModel.Session.SelectedNodeId == insertedId,
            "새 Session의 트리 선택이 새 Session에 반영되지 않음");
    }

    private static void ClearSelectionOnly()
    {
        const string source = "A🙂e\u0301𝑥Z";
        const string remaining = "AZ";
        foreach (var reverse in new[] { false, true })
        {
            var text = Qa.Text(source);
            var session = new EditingSession(Workspace(Group([Row(Qa.Sequence(Qa.Text("왼쪽"), text, Qa.Text("오른쪽")))])));
            Qa.Caret(session, text.Id, 4);
            Qa.Select(session, text.Id, reverse ? 4 : 1, reverse ? 1 : 4);
            session.MarkSaved(session.CaptureSnapshot());
            var before = Capture(session);
            Qa.Check(session.CanClear(MathClearScope.Selection), "Unicode 부분선택 clear가 불가능으로 표시됨");
            Qa.Check(session.Clear(MathClearScope.Selection), "Unicode 부분선택 clear 실패");
            Qa.Equal(remaining, Qa.Leaf(session, text.Id).Text, "선택한 grapheme만 삭제");
            Qa.Equal(new MathCaret(text.Id, 1), session.Caret, "부분선택 clear의 caret 위치");
            Qa.Check(session.Selection is null && !session.IsNodeSelected, "부분선택 clear 후 선택 잔존");
            Qa.Check(session.IsDirty && session.Revision == before.Revision + 1, "부분선택 clear가 단일 Commit이 아님");
            var after = Capture(session);
            AssertUndoRedo(session, before, after, "부분선택 clear " + (reverse ? "역방향" : "정방향"));
        }

        var left = Qa.Text("L");
        var selected = new FractionNode(Guid.NewGuid(), Qa.Text("분자"), new RadicalNode(Guid.NewGuid(), Qa.Text("분모")));
        var right = Qa.Text("R");
        var session2 = new EditingSession(Workspace(Group([Row(Qa.Sequence(left, selected, right))])));
        Qa.Check(session2.TrySelectNode(selected.Id), "전체 구조 선택 설정");
        session2.MarkSaved(session2.CaptureSnapshot());
        var beforeStructure = Capture(session2);
        Qa.Check(session2.Clear(MathClearScope.Selection), "전체 구조 선택 clear 실패");
        var replaced = (SequenceNode)session2.Workspace.Groups[0].Rows[0].Root;
        Qa.Equal(left.Id, replaced.Children[0].Id, "전체 구조 clear 왼쪽 이웃 보존");
        Qa.Check(replaced.Children[1] is TextNode { Text.Length: 0, IsEmptySlot: true }, "전체 구조 clear가 기존 DeleteSelection placeholder 계약을 따르지 않음");
        Qa.Equal(right.Id, replaced.Children[2].Id, "전체 구조 clear 오른쪽 이웃 보존");
        Qa.Check(!session2.IsNodeSelected && session2.Selection is null, "전체 구조 clear 후 선택 상태");
        Qa.Equal(new MathCaret(replaced.Children[1].Id, 0), session2.Caret, "전체 구조 clear 후 caret");
        var afterStructure = Capture(session2);
        AssertUndoRedo(session2, beforeStructure, afterStructure, "전체 구조 선택 clear");
    }

    private static void ClearSelectedRowOnly()
    {
        var caretText = Qa.Text("caret-group1-row1");
        var unaffected = new FractionNode(Guid.NewGuid(), Qa.Text("A"), Qa.Text("B"));
        var selectedFraction = new FractionNode(Guid.NewGuid(), Qa.Text("𝑥🙂"), new RadicalNode(Guid.NewGuid(), Qa.Text("e\u0301")));
        var group1 = Group([Row(Qa.Sequence(caretText, unaffected)), Row(Qa.Text("other row"))], AlignmentMode.Equals, 31.5);
        var targetRow = Row(Qa.Sequence(Qa.Text("f="), selectedFraction));
        var targetGroup = Group([Row(Qa.Text("target neighbor")), targetRow, Row(Qa.Text("tail"))], AlignmentMode.None, 47.25);
        var outsideGroup = Group([Row(Qa.Text("outside"))], AlignmentMode.Equals, 9);
        var workspace = Workspace(group1, targetGroup, outsideGroup);
        var session = new EditingSession(workspace);
        Qa.Caret(session, caretText.Id, 3);
        Qa.Check(session.TrySelectNode(selectedFraction.Id), "다른 행의 구조 선택");
        session.MarkSaved(session.CaptureSnapshot());
        var before = Capture(session);

        Qa.Check(session.Clear(MathClearScope.CurrentRow), "선택 구조의 행 clear 실패");
        var groups = session.Workspace.Groups;
        Qa.Equal(group1.Id, groups[0].Id, "대상 아닌 그룹 순서/ID");
        Qa.Equal(targetGroup.Id, groups[1].Id, "대상 그룹 ID");
        Qa.Equal(outsideGroup.Id, groups[2].Id, "후속 그룹 ID");
        Qa.Equal(group1.Rows[0].Id, groups[0].Rows[0].Id, "caret 행 ID 보존");
        Qa.Equal(Qa.NodeFingerprint(group1.Rows[0].Root), Qa.NodeFingerprint(groups[0].Rows[0].Root), "caret 행은 비워지지 않음");
        Qa.Equal(3, groups[1].Rows.Length, "같은 그룹의 비대상 행 수");
        Qa.Equal(targetGroup.Rows[0].Id, groups[1].Rows[0].Id, "대상 앞 행 ID 유지");
        Qa.Equal(targetRow.Id, groups[1].Rows[1].Id, "대상 행 ID 유지");
        Qa.Equal(targetGroup.Rows[2].Id, groups[1].Rows[2].Id, "대상 뒤 행 ID 유지");
        Qa.Check(groups[1].Rows[1].Root is TextNode { Text.Length: 0, IsEmptySlot: true }, "clear가 선택 구조가 든 행의 root만 빈 슬롯으로 바꾸지 않음");
        Qa.Equal(Qa.NodeFingerprint(targetGroup.Rows[0].Root), Qa.NodeFingerprint(groups[1].Rows[0].Root), "대상 앞 행 내용 보존");
        Qa.Equal(Qa.NodeFingerprint(targetGroup.Rows[2].Root), Qa.NodeFingerprint(groups[1].Rows[2].Root), "대상 뒤 행 내용 보존");
        Qa.Equal(new MathCaret(groups[1].Rows[1].Root.Id, 0), session.Caret, "clear 후 선택 구조 행의 새 root caret");
        Qa.Check(session.Selection is null && !session.IsNodeSelected, "row clear 후 선택 해제");
        Qa.Equal(targetGroup.Alignment, groups[1].Alignment, "row clear 그룹 정렬 보존");
        Qa.Equal(targetGroup.RowSpacing, groups[1].RowSpacing, "row clear 그룹 간격 보존");
        Qa.Equal(workspace.Style, session.Workspace.Style, "row clear 스타일 보존");
        var after = Capture(session);
        Qa.Equal(before.Revision + 1, after.Revision, "row clear가 단일 Commit");
        AssertUndoRedo(session, before, after, "교차 행 선택 row clear");
    }

    private static void ClearSelectedGroupOnly()
    {
        var caretText = Qa.Text("caret-in-first-group");
        var group1 = Group([Row(Qa.Sequence(caretText)), Row(Qa.Text("first group row2"))], AlignmentMode.None, 17);
        var selectedText = Qa.Text("선택 구조");
        var selectedNode = new BracketsNode(Guid.NewGuid(), MathBracketKind.Angle, selectedText);
        var group2 = Group([Row(Qa.Text("second group row1")), Row(Qa.Sequence(Qa.Text("a="), selectedNode)), Row(Qa.Text("second group row3"))], AlignmentMode.Equals, 52);
        var group3 = Group([Row(Qa.Text("must stay"))], AlignmentMode.None, 63);
        var workspace = Workspace(group1, group2, group3);
        var session = new EditingSession(workspace);
        Qa.Caret(session, caretText.Id, 4);
        Qa.Check(session.TrySelectNode(selectedNode.Id), "다른 그룹의 구조 선택");
        session.MarkSaved(session.CaptureSnapshot());
        var before = Capture(session);

        Qa.Check(session.Clear(MathClearScope.CurrentGroup), "선택 구조의 그룹 clear 실패");
        var groups = session.Workspace.Groups;
        Qa.Equal(3, groups.Length, "group clear 그룹 개수");
        Qa.Equal(group1.Id, groups[0].Id, "비대상 이전 그룹 ID");
        Qa.Equal(group2.Id, groups[1].Id, "대상 그룹 ID 유지");
        Qa.Equal(group3.Id, groups[2].Id, "비대상 다음 그룹 ID");
        Qa.Equal(2, groups[0].Rows.Length, "비대상 이전 그룹 행 수");
        Qa.Equal(3, groups[1].Rows.Length, "clear가 행을 삭제하지 않음");
        Qa.Check(group2.Rows.Select(row => row.Id).SequenceEqual(groups[1].Rows.Select(row => row.Id)), "대상 그룹 행 ID/order");
        Qa.Check(groups[1].Rows.All(row => row.Root is TextNode { Text.Length: 0, IsEmptySlot: true }), "대상 그룹의 모든 root가 필수 빈 슬롯으로 바뀌지 않음");
        Qa.Equal(Qa.NodeFingerprint(group1.Rows[0].Root), Qa.NodeFingerprint(groups[0].Rows[0].Root), "caret 그룹은 보존되어야 함");
        Qa.Equal(Qa.NodeFingerprint(group3.Rows[0].Root), Qa.NodeFingerprint(groups[2].Rows[0].Root), "후속 그룹은 보존되어야 함");
        Qa.Equal(group2.Alignment, groups[1].Alignment, "group clear 정렬 모드 보존");
        Qa.Equal(group2.RowSpacing, groups[1].RowSpacing, "group clear 행 간격 보존");
        Qa.Equal(new MathCaret(groups[1].Rows[1].Root.Id, 0), session.Caret, "활성 선택 행의 새 root caret");
        Qa.Check(session.Selection is null && !session.IsNodeSelected, "group clear 후 selection 해제");
        var after = Capture(session);
        Qa.Equal(before.Revision + 1, after.Revision, "group clear 단일 Commit");
        AssertUndoRedo(session, before, after, "교차 그룹 선택 group clear");
    }

    private static void ClearWorkspaceRoots()
    {
        var caretText = Qa.Text("outside active");
        var activeRoot = new FractionNode(Guid.NewGuid(), Qa.Text("A"), Qa.Text("B"));
        var group1 = Group([Row(Qa.Sequence(caretText, Qa.Text("neighbor")))] , AlignmentMode.None, 12);
        var group2 = Group([Row(Qa.Text("before")), Row(activeRoot), Row(new CasesNode(Guid.NewGuid(), ImmutableArray.Create(
            new CaseRow(Qa.Text("x"), Qa.Text("x>0")), new CaseRow(Qa.Text("0"), Qa.Text("other")))))], AlignmentMode.Equals, 38);
        var workspace = Workspace(group1, group2);
        var session = new EditingSession(workspace);
        Qa.Caret(session, caretText.Id, 2);
        Qa.Check(session.TrySelectNode(activeRoot.Id), "전체 작업 범위의 활성 선택 설정");
        session.MarkSaved(session.CaptureSnapshot());
        var before = Capture(session);

        Qa.Check(session.Clear(MathClearScope.Workspace), "전체 작업 clear 실패");
        Qa.Equal(workspace.Groups.Length, session.Workspace.Groups.Length, "전체 clear 그룹 수");
        var newRoots = session.Workspace.Groups.SelectMany(group => group.Rows).ToArray();
        var oldRows = workspace.Groups.SelectMany(group => group.Rows).ToArray();
        Qa.Equal(oldRows.Length, newRoots.Length, "전체 clear 행 수");
        for (var i = 0; i < oldRows.Length; i++)
        {
            Qa.Equal(oldRows[i].Id, newRoots[i].Id, "전체 clear 행 ID/order");
            Qa.Check(newRoots[i].Root is TextNode { Text.Length: 0, IsEmptySlot: true }, "전체 clear가 한 행을 필수 빈 텍스트로 바꾸지 않음");
            Qa.Check(newRoots[i].Root.Id != oldRows[i].Root.Id, "전체 clear root가 새 ID가 아님");
        }
        for (var i = 0; i < workspace.Groups.Length; i++)
        {
            Qa.Equal(workspace.Groups[i].Id, session.Workspace.Groups[i].Id, "전체 clear 그룹 ID/order");
            Qa.Equal(workspace.Groups[i].Alignment, session.Workspace.Groups[i].Alignment, "전체 clear alignment 보존");
            Qa.Equal(workspace.Groups[i].RowSpacing, session.Workspace.Groups[i].RowSpacing, "전체 clear spacing 보존");
        }
        Qa.Equal(workspace.Style, session.Workspace.Style, "전체 clear style 보존");
        Qa.Equal(new MathCaret(newRoots[2].Root.Id, 0), session.Caret, "활성 선택 행의 새 root caret");
        Qa.Check(session.Selection is null && !session.IsNodeSelected, "전체 clear 후 선택 해제");
        var after = Capture(session);
        Qa.Equal(before.Revision + 1, after.Revision, "전체 clear 단일 Commit");
        AssertUndoRedo(session, before, after, "전체 작업 clear");
    }

    private static void EmptyAndInvalidScopesAreNoOps()
    {
        var emptyText = Qa.Text(string.Empty, true);
        var emptySequence = new SequenceNode(Guid.NewGuid(), [emptyText, new SequenceNode(Guid.NewGuid(), [Qa.Text(string.Empty)])]);
        AssertNoOpForContentlessRoot(emptyText, "빈 TextNode");
        AssertNoOpForContentlessRoot(emptySequence, "재귀로 빈 SequenceNode");

        var populated = Qa.Text("x");
        var session = new EditingSession(Workspace(Group([Row(populated)])));
        Qa.Caret(session, populated.Id, 0);
        session.MarkSaved(session.CaptureSnapshot());
        var before = Capture(session);
        var invalidScope = (MathClearScope)int.MaxValue;
        Qa.Check(!session.CanClear(invalidScope), "미정의 clear scope를 허용 가능으로 표시");
        Qa.Check(!session.Clear(invalidScope), "미정의 clear scope 실행");
        AssertUnchanged(before, Capture(session), "잘못된 scope의 전체 상태");
        Qa.Check(!session.CanClear(MathClearScope.Selection), "빈 선택을 CanClear=true로 표시");
        Qa.Check(!session.Clear(MathClearScope.Selection), "빈 선택을 clear 실행");
        foreach (var scope in new[] { MathClearScope.CurrentRow, MathClearScope.CurrentGroup, MathClearScope.Workspace })
            Qa.Check(session.CanClear(scope), "내용이 있는 범위인데 CanClear=false: " + scope);
        AssertUnchanged(before, Capture(session), "내용 없는 선택/범위의 전체 상태");

        var emptyStructure = new FractionNode(Guid.NewGuid(), Qa.Text(string.Empty, true),
            new MatrixNode(Guid.NewGuid(), 1, 2, [Qa.Text(string.Empty, true), Qa.Text(string.Empty, true)]));
        var structural = new EditingSession(Workspace(Group([Row(emptyStructure)])));
        var structuralBefore = Capture(structural);
        Qa.Check(structural.CanClear(MathClearScope.CurrentRow), "빈 슬롯만 가진 구조를 내용 없음으로 잘못 분류");
        Qa.Check(structural.Clear(MathClearScope.CurrentRow), "빈 슬롯만 있는 유효 구조 clear 실패");
        Qa.Check(structural.Workspace.Groups[0].Rows[0].Root is TextNode { IsEmptySlot: true }, "빈 구조 clear 후 필수 빈 root 누락");
        Qa.Equal(structuralBefore.Revision + 1, structural.Revision, "빈 구조 clear commit");
    }

    private static async Task ClearHistoryAndViewModelContract()
    {
        var vm = new MainWindowViewModel();
        Qa.Equal(WorkspaceActionRequestResult.Completed, vm.RequestNewWorkspace(), "기본 샘플에서 빈 작업으로 초기화");
        var panel = vm.StructurePanel;
        Qa.Equal(MathClearScope.CurrentRow, vm.SelectedClearScope, "기본 clear scope");
        Qa.Check(vm.ClearScopeChoices.Select(choice => choice.Scope).SequenceEqual(Enum.GetValues<MathClearScope>()),
            "clear scope 선택지 종류/순서");
        Qa.Check(!vm.CanClearActiveScope && !vm.ClearActiveScopeCommand.CanExecute(null),
            "빈 초기 작업에서 clear command가 활성화됨");
        Qa.Check(!string.IsNullOrWhiteSpace(vm.ClearScopeDisabledReason), "비활성 사유가 비어 있음");
        Qa.Equal(1, vm.ClearScopeAffectedRowCount, "초기 현재 행의 영향 행 수");

        var oldSession = vm.Session;
        var templateId = oldSession.InsertTemplate(MathTemplateKind.Cases);
        Qa.Check(templateId.HasValue && Find(panel.Items, templateId.Value) is not null,
            "ViewModel의 tree projection이 실제 편집 결과를 반영하지 않음");
        Qa.Check(oldSession.AddRowAfterCurrent(), "clear scope 영향 행 수를 위한 두 번째 행 추가");
        var cases = (CasesNode)Qa.Node(oldSession, templateId!.Value);
        var firstExpression = Qa.Walk(cases.Rows[0].Expression).OfType<TextNode>().First();
        Qa.Caret(oldSession, firstExpression.Id, 0);
        Qa.Check(oldSession.InsertText("x"), "clear history fixture 채우기");
        oldSession.MarkSaved(oldSession.CaptureSnapshot());
        var originalWorkspace = Qa.Fingerprint(oldSession.Workspace);
        var originalStateId = oldSession.CaptureSnapshot().StateId;
        var choices = vm.ClearScopeChoices;
        vm.SelectedClearScope = MathClearScope.Workspace;
        Qa.Equal(MathClearScope.Workspace, vm.SelectedClearScope, "workspace 범위 선택");
        Qa.Check(vm.ClearScopeDescription.Contains("전체 작업", StringComparison.Ordinal), "workspace clear 설명에 활성 범위가 표시되지 않음");
        Qa.Equal(2, vm.ClearScopeAffectedRowCount, "workspace clear 영향 행 수");
        Qa.Equal(choices, vm.ClearScopeChoices, "고정 clear choices collection 교체");
        Qa.Caret(oldSession, firstExpression.Id, 1);
        Qa.Equal(MathClearScope.Workspace, vm.SelectedClearScope, "caret/revision 변경 뒤 선택된 scope 초기화");
        Qa.Equal(choices, vm.ClearScopeChoices, "caret 변경 시 clear choices 재생성");
        Qa.Check(panel.SelectNode(templateId.Value), "구조 tree 선택으로 scope 재계산");
        Qa.Equal(MathClearScope.Workspace, vm.SelectedClearScope, "tree selection 변경 뒤 scope 초기화");
        Qa.Equal(choices, vm.ClearScopeChoices, "selection 변경 시 clear choices 재생성");
        vm.SelectedClearScope = MathClearScope.CurrentRow;
        Qa.Check(vm.CanClearActiveScope && vm.ClearActiveScopeCommand.CanExecute(null), "내용 구조가 있는데 clear command 비활성");
        Qa.Check(vm.ClearScopeDescription.Contains("현재 행", StringComparison.Ordinal), "현재 행 clear 설명 누락");
        Qa.Equal(1, vm.ClearScopeAffectedRowCount, "현재 행 clear 영향 행 수");
        var focusRequests = 0;
        vm.EditorFocusRequested += (_, _) => focusRequests++;
        var revisionBeforeCommandClear = vm.Session.Revision;
        vm.ClearActiveScopeCommand.Execute(null);
        Qa.Equal(revisionBeforeCommandClear + 1, vm.Session.Revision, "root ViewModel clear command가 단일 Commit을 적용하지 않음");
        Qa.Equal(1, focusRequests, "성공 clear 뒤 editor focus 요청 횟수");
        Qa.Equal(MathClearScope.CurrentRow, vm.SelectedClearScope, "clear command 뒤 scope 변경");
        Qa.Equal(choices, vm.ClearScopeChoices, "clear command 실행 중 선택지 collection 변경");
        Qa.Check(!vm.CanClearActiveScope && !vm.ClearActiveScopeCommand.CanExecute(null), "clear 후 빈 활성 행 command가 계속 활성");
        Qa.Check(!string.IsNullOrWhiteSpace(vm.ClearScopeDisabledReason), "clear 후 빈 범위 비활성 사유 누락");
        Qa.Check(vm.Session.Workspace.Groups[0].Rows[0].Root is TextNode { Text.Length: 0, IsEmptySlot: true },
            "ViewModel clear command가 선택 scope를 실행하지 않음");
        Qa.Equal<Guid?>(vm.Session.Caret.NodeId, panel.SelectedNodeId, "clear 이후 구조 트리 caret 선택 동기화");
        Qa.Check(vm.Session.CanUndo && vm.Session.IsDirty, "ViewModel clear가 단일 history/dirty를 만들지 않음");
        var cleared = Capture(vm.Session);

        vm.UndoCommand.Execute(null);
        Qa.Equal(originalWorkspace, Qa.Fingerprint(vm.Session.Workspace), "VM undo clear workspace");
        Qa.Equal(originalStateId, vm.Session.CaptureSnapshot().StateId, "VM undo clear saved state ID");
        Qa.Check(vm.Session.IsNodeSelected && vm.Session.SelectedNodeId == templateId,
            "undo 뒤 원래 전체 구조 선택이 복원되지 않음");
        Qa.Equal(vm.Session.SelectedNodeId, panel.SelectedNodeId, "undo 후 tree selection이 복원 구조에 동기화되지 않음");
        Qa.Equal(MathClearScope.CurrentRow, vm.SelectedClearScope, "undo/projection rebuild 뒤 선택 scope 변경");
        Qa.Equal(choices, vm.ClearScopeChoices, "undo/projection rebuild 뒤 clear choices 교체");
        Qa.Check(vm.Session.IsDirty == false && vm.Session.CanRedo, "VM undo saved state/history");
        Qa.Check(Find(panel.Items, templateId!.Value) is not null, "undo 후 projection에서 원 구조가 사라짐");
        vm.RedoCommand.Execute(null);
        Qa.Equal(cleared.Workspace, Qa.Fingerprint(vm.Session.Workspace), "VM redo clear workspace");
        Qa.Equal<Guid?>(vm.Session.Caret.NodeId, panel.SelectedNodeId, "redo 후 tree selection 동기화");
        Qa.Equal(MathClearScope.CurrentRow, vm.SelectedClearScope, "redo/projection rebuild 뒤 선택 scope 변경");

        vm.SelectedClearScope = MathClearScope.Workspace;
        Qa.Equal(MathClearScope.Workspace, vm.SelectedClearScope, "유효한 root scope 선택");
        Qa.Equal(choices, vm.ClearScopeChoices, "undo/redo·projection rebuild 후 fixed scope choices identity");
        var currentWorkspace = Qa.Fingerprint(vm.Session.Workspace);
        vm.SelectedClearScope = (MathClearScope)999;
        Qa.Equal(MathClearScope.Workspace, vm.SelectedClearScope, "잘못된 root scope 값이 기존 선택을 덮어씀");
        Qa.Equal(currentWorkspace, Qa.Fingerprint(vm.Session.Workspace), "scope 선택 전환이 모델을 바꿈");

        var outputText = Qa.Text("clear output fixture");
        var outputSession = new EditingSession(Workspace(Group([Row(outputText)])));
        outputSession.MarkSaved(outputSession.CaptureSnapshot());
        Qa.Check(outputSession.Clear(MathClearScope.CurrentRow), "SVG 거절 fixture clear");
        var incompleteLayout = MathLayoutEngine.Layout(outputSession.Workspace, new AvaloniaMathTextMetrics());
        Qa.Check(incompleteLayout.Diagnostics.Any(diagnostic => diagnostic.Code == LayoutDiagnosticCode.EmptySlot),
            "clear 뒤 필수 빈 슬롯 진단이 layout에 없음");
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(incompleteLayout),
            "필수 빈 슬롯이 있는 clear 결과의 SVG 출력 거절 누락");
        outputSession.Undo();
        var completeLayout = MathLayoutEngine.Layout(outputSession.Workspace, new AvaloniaMathTextMetrics());
        var svgService = new SvgExportService();
        var svg = svgService.ToSvg(completeLayout);
        Qa.Equal("clear output fixture", ReadSvgTextInDocumentOrder(svg), "undo 뒤 완성 수식 SVG text-run 의미/순서");
        using var directory = new TempDirectory();
        var svgPath = directory.File("clear-undo-restored.svg");
        await svgService.ExportAsync(svgPath, completeLayout);
        var savedSvg = await File.ReadAllTextAsync(svgPath);
        Qa.Equal("clear output fixture", ReadSvgTextInDocumentOrder(savedSvg), "undo 뒤 SVG 파일의 text-run 의미/순서");
    }

    private static string ReadSvgTextInDocumentOrder(string svg)
    {
        var document = XDocument.Parse(svg);
        return string.Concat(document.Descendants()
            .Where(element => element.Name.LocalName == "text")
            .Select(element => element.Value));
    }

    private static void AssertNoOpForContentlessRoot(MathNode root, string label)
    {
        var session = new EditingSession(Workspace(Group([Row(root)])));
        session.MarkSaved(session.CaptureSnapshot());
        var before = Capture(session);
        foreach (var scope in Enum.GetValues<MathClearScope>())
        {
            Qa.Check(!session.CanClear(scope), label + " 내용 없는 상태 CanClear: " + scope);
            Qa.Check(!session.Clear(scope), label + " 내용 없는 상태 Clear: " + scope);
            AssertUnchanged(before, Capture(session), label + " no-op " + scope);
        }
    }

    private static void AssertUndoRedo(EditingSession session, SessionState before, SessionState after, string label)
    {
        Qa.Check(session.CanUndo && !session.CanRedo, label + " 성공 후 history 방향");
        session.Undo();
        var undone = Capture(session);
        Qa.Equal(before.Workspace, undone.Workspace, label + " undo workspace");
        Qa.Equal(before.StateId, undone.StateId, label + " undo saved state ID");
        Qa.Equal(before.Caret, undone.Caret, label + " undo caret");
        Qa.Equal(before.Selection, undone.Selection, label + " undo partial selection");
        Qa.Equal(before.SelectedNodeId, undone.SelectedNodeId, label + " undo selected node");
        Qa.Equal(before.IsNodeSelected, undone.IsNodeSelected, label + " undo node-selection flag");
        Qa.Equal(before.IsDirty, undone.IsDirty, label + " undo dirty state");
        Qa.Equal(after.Revision + 1, undone.Revision, label + " undo revision increment");
        Qa.Check(undone.CanRedo && !undone.CanUndo, label + " undo history direction");

        session.Redo();
        var redone = Capture(session);
        Qa.Equal(after.Workspace, redone.Workspace, label + " redo workspace");
        Qa.Equal(after.StateId, redone.StateId, label + " redo state ID");
        Qa.Equal(after.Caret, redone.Caret, label + " redo caret");
        Qa.Equal(after.Selection, redone.Selection, label + " redo selection");
        Qa.Equal(after.SelectedNodeId, redone.SelectedNodeId, label + " redo selected node");
        Qa.Equal(after.IsNodeSelected, redone.IsNodeSelected, label + " redo node-selection flag");
        Qa.Equal(after.Revision + 2, redone.Revision, label + " redo revision increment");
        Qa.Check(redone.CanUndo && !redone.CanRedo, label + " redo history direction");
        Qa.Equal(after.IsDirty, redone.IsDirty, label + " redo dirty");
    }

    private static void AssertUnchanged(SessionState expected, SessionState actual, string label) =>
        Qa.Equal(expected, actual, label);

    private static SessionState Capture(EditingSession session)
    {
        var snapshot = session.CaptureSnapshot();
        return new SessionState(Qa.Fingerprint(session.Workspace), snapshot.Revision, snapshot.StateId,
            session.Caret, session.Selection, session.SelectedNodeId, session.IsNodeSelected,
            session.CanUndo, session.CanRedo, session.IsDirty);
    }

    private static void AssertProjectionEqualsSession(StructurePanelViewModel panel, EditingSession session)
    {
        var expected = Qa.Nodes(session.Workspace).Select(node => node.Id);
        var actual = Flatten(panel.Items).Select(item => item.MathNodeId);
        Qa.Check(expected.SequenceEqual(actual), "Session 교체 뒤 StructurePanel projection이 새 AST와 다름");
        Qa.Equal<Guid?>(session.Caret.NodeId, panel.SelectedNodeId, "Session 교체 뒤 selection 복원");
    }

    private static IEnumerable<StructurePanelItem> Flatten(IEnumerable<StructurePanelItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children))
                yield return child;
        }
    }

    private static StructurePanelItem Find(IEnumerable<StructurePanelItem> items, Guid id) =>
        Flatten(items).Single(item => item.MathNodeId == id);

    private static MathNodeKind Kind(MathNode node) => node switch
    {
        TextNode => MathNodeKind.Text,
        SequenceNode => MathNodeKind.Sequence,
        FractionNode => MathNodeKind.Fraction,
        RadicalNode => MathNodeKind.Radical,
        ScriptNode => MathNodeKind.Script,
        MatrixNode => MathNodeKind.Matrix,
        IntegralNode => MathNodeKind.Integral,
        SummationNode => MathNodeKind.Summation,
        LimitNode => MathNodeKind.Limit,
        BracketsNode => MathNodeKind.Brackets,
        CasesNode => MathNodeKind.Cases,
        _ => throw new InvalidOperationException("알 수 없는 node 종류: " + node.GetType().Name)
    };

    private static EquationRow Row(MathNode root) => new(Guid.NewGuid(), root);

    private static EquationGroup Group(EquationRow[] rows,
        AlignmentMode alignment = AlignmentMode.Equals, double rowSpacing = 26) =>
        new(Guid.NewGuid(), [.. rows], alignment, rowSpacing);

    private static EquationWorkspace Workspace(params EquationGroup[] groups) => new(
        Guid.NewGuid(), 1, "구조 비우기 QA 𝑥 e\u0301 한글🙂", [.. groups], new MathStyle(31, "Cambria Math", "#24364B"));

    private sealed record SessionState(string Workspace, long Revision, Guid StateId, MathCaret Caret,
        MathSelection? Selection, Guid? SelectedNodeId, bool IsNodeSelected, bool CanUndo, bool CanRedo, bool IsDirty);
}
