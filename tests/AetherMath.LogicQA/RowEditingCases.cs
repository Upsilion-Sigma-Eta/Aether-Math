using System.Globalization;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class RowEditingCases
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    public static IEnumerable<QaCase> All()
    {
        yield return new("ROW-ADD", "활성 행 뒤 필수 빈 슬롯 추가와 단일 undo/redo", AddAfterCurrent);
        yield return new("ROW-ACTIVE", "교차 행 구조 선택이 선택 구조의 행을 편집 대상으로 지정", SelectedStructureChoosesActiveRow);
        yield return new("ROW-DUPLICATE", "깊은 행 복제의 ID·의미·역방향 Unicode 선택 매핑", DuplicateDeepRowAndMapSelection);
        yield return new("ROW-DUP-CROSS", "교차 행 구조 선택 복제의 caret fallback과 undo 복원", DuplicateCrossRowStructureSelection);
        yield return new("ROW-DELETE", "행 삭제 후 다음 또는 이전 행의 첫 텍스트로 이동", DeleteChoosesAdjacentRow);
        yield return new("ROW-MOVE", "그룹 내부 인접 행 이동과 모든 편집 위치 보존", MoveRowsPreservesEditPosition);
        yield return new("ROW-NOOP", "그룹 마지막 행과 이동 경계의 완전 무변경 거절", RejectRowBoundariesWithoutMutation);
        yield return new("ROW-IO", "행 history·저장 재열기·LaTeX/SVG 순서 및 빈 슬롯 출력 거절", SaveAndExportRows);
        yield return new("ROW-PASTE", "행 이동 중 대기 중인 붙여넣기를 폐기하고 재시도 허용", RowEditInvalidatesPendingPaste);
    }

    private static void AddAfterCurrent()
    {
        var first = Row(Qa.Sequence(Qa.Text("A=x")));
        var targetText = Qa.Text("B=y");
        var target = Row(Qa.Sequence(targetText));
        var last = Row(Qa.Text("C=z"));
        var otherGroupRow = Row(Qa.Text("outside"));
        var primary = Group([first, target, last], AlignmentMode.Equals, 17.5);
        var secondary = Group([otherGroupRow], AlignmentMode.None, 41);
        var workspace = Workspace(primary, secondary);
        var session = new EditingSession(workspace);
        Qa.Caret(session, targetText.Id, 2);
        var before = Snapshot(session);
        var saved = session.CaptureSnapshot();
        session.MarkSaved(saved);

        Qa.Check(session.CanAddRowAfterCurrent, "활성 행 추가 가능 상태");
        Qa.Check(session.AddRowAfterCurrent(), "활성 행 뒤 추가 실패");

        var rows = session.Workspace.Groups[0].Rows;
        Qa.Equal(4, rows.Length, "추가 뒤 같은 그룹 행 수");
        Qa.Equal(first.Id, rows[0].Id, "앞 행 위치 보존");
        Qa.Equal(target.Id, rows[1].Id, "대상 행 위치 보존");
        Qa.Equal(last.Id, rows[3].Id, "뒤 행 위치 보존");
        var inserted = rows[2];
        var empty = (TextNode)inserted.Root;
        Qa.Check(inserted.Id != target.Id && empty.Id != targetText.Id, "추가 row/text ID를 새로 만들지 않음");
        Qa.Equal(string.Empty, empty.Text, "새 슬롯의 초기 텍스트");
        Qa.Check(empty.IsEmptySlot, "새 행이 필수 빈 슬롯으로 표시되지 않음");
        Qa.Equal(new MathCaret(empty.Id, 0), session.Caret, "새 행 caret 0");
        Qa.Check(session.Selection is null && !session.IsNodeSelected, "추가 후 이전 선택 잔존");
        Qa.Equal(1, session.ActiveGroupNumber, "추가 후 활성 그룹");
        Qa.Equal(3, session.ActiveRowNumber, "추가 후 활성 행");
        Qa.Equal(4, session.ActiveRowCount, "추가 후 현재 그룹 행 수");
        Qa.Equal(secondary.Rows[0].Id, session.Workspace.Groups[1].Rows[0].Id, "다른 그룹 변경");
        Qa.Equal(primary.Alignment, session.Workspace.Groups[0].Alignment, "추가 후 정렬 속성");
        Qa.Equal(primary.RowSpacing, session.Workspace.Groups[0].RowSpacing, "추가 후 간격 속성");
        Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo, "추가의 dirty/history 상태");

        var after = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(before.Workspace, Qa.Fingerprint(session.Workspace), "추가 undo workspace");
        Qa.Equal(before.Caret, session.Caret, "추가 undo caret");
        Qa.Equal(before.Selection, session.Selection, "추가 undo 선택");
        Qa.Check(!session.IsDirty && session.CanRedo, "추가 undo saved snapshot 상태");
        session.Redo();
        Qa.Equal(after, Qa.Fingerprint(session.Workspace), "추가 redo workspace");
        Qa.Equal(new MathCaret(empty.Id, 0), session.Caret, "추가 redo caret");
        Qa.Check(session.IsDirty, "추가 redo dirty 상태");
    }

    private static void SelectedStructureChoosesActiveRow()
    {
        var caretNode = Qa.Text("caret-row");
        var rowA = Row(Qa.Sequence(caretNode));
        var numerator = Qa.Text("𝑥🙂");
        var denominator = Qa.Text("e\u0301");
        var fraction = new FractionNode(Guid.NewGuid(), numerator, denominator);
        var rowB = Row(Qa.Sequence(Qa.Text("B="), fraction));
        var rowC = Row(Qa.Text("C"));
        var group = Group([rowA, rowB, rowC]);
        var session = new EditingSession(Workspace(group));
        Qa.Caret(session, caretNode.Id, 2);
        Qa.Check(session.TrySelectNode(fraction.Id), "다른 행 분수 구조 선택 실패");

        Qa.Equal(new MathCaret(caretNode.Id, 2), session.Caret, "구조 선택이 원 caret를 이동함");
        Qa.Equal(2, session.ActiveRowNumber, "선택 구조가 포함된 행을 활성 행으로 해석하지 않음");
        Qa.Check(session.AddRowAfterCurrent(), "교차 행 구조 선택 상태에서 행 추가 실패");
        var rows = session.Workspace.Groups[0].Rows;
        Qa.Equal(rowA.Id, rows[0].Id, "비활성 caret 행 위치 변경");
        Qa.Equal(rowB.Id, rows[1].Id, "선택 대상 행 위치 변경");
        Qa.Check(rows[2].Id != rowB.Id && rows[2].Root is TextNode { IsEmptySlot: true },
            "새 행이 selected-node 행 뒤에 삽입되지 않음");
        Qa.Equal(rowC.Id, rows[3].Id, "선택 대상 뒤쪽 행 위치 변경");
        Qa.Equal(new MathCaret(((TextNode)rows[2].Root).Id, 0), session.Caret, "선택 대상 뒤 새 행 caret");
        session.Undo();
        Qa.Equal(new MathCaret(caretNode.Id, 2), session.Caret, "교차 행 추가 undo 원 caret");
        Qa.Check(session.IsNodeSelected, "교차 행 추가 undo 구조 선택 복원 실패");
        Qa.Equal<Guid?>(fraction.Id, session.SelectedNodeId, "교차 행 추가 undo 선택 ID");
    }

    private static void DuplicateDeepRowAndMapSelection()
    {
        var unicode = Qa.Text("e\u0301🙂𝑥");
        var matrix = new MatrixNode(Guid.NewGuid(), 2, 2,
        [
            Qa.Text("α"), Qa.Text(string.Empty, isEmptySlot: true),
            Qa.Text("👩‍🔬"), Qa.Text("4")
        ]);
        var script = new ScriptNode(Guid.NewGuid(),
            new RadicalNode(Guid.NewGuid(), new FractionNode(Guid.NewGuid(), Qa.Text("a+b"), Qa.Text("c+d"))),
            Qa.Text("i"), Qa.Sequence(Qa.Text("n"), matrix));
        var fraction = new FractionNode(Guid.NewGuid(),
            new SequenceNode(Guid.NewGuid(), [Qa.Text("head"), unicode]),
            new RadicalNode(Guid.NewGuid(), new SequenceNode(Guid.NewGuid(), [script, Qa.Text("tail")] )));
        var target = Row(Qa.Sequence(Qa.Text("x="), fraction));
        var trailing = Row(Qa.Text("outside"));
        var group = Group([target, trailing], AlignmentMode.Equals, 33.25);
        var otherGroup = Group([Row(Qa.Text("unrelated"))], AlignmentMode.None, 8);
        var workspace = Workspace(group, otherGroup);
        var session = new EditingSession(workspace);
        Qa.Select(session, unicode.Id, 3, 1); // 역방향 선택, é·emoji·수학용 기호의 문자소 offset
        var before = Snapshot(session);

        Qa.Check(session.DuplicateCurrentRow(), "깊은 행 복제 실패");
        var rows = session.Workspace.Groups[0].Rows;
        Qa.Equal(3, rows.Length, "복제 후 행 수");
        var clone = rows[1];
        Qa.Check(clone.Id != target.Id, "복제 row ID 충돌");

        var idMap = MapCloneTree(target.Root, clone.Root);
        var oldIds = Qa.Nodes(workspace).Select(n => n.Id).ToHashSet();
        var cloneIds = Qa.Walk(clone.Root).Select(n => n.Id).ToArray();
        Qa.Equal(cloneIds.Length, cloneIds.Distinct().Count(), "복제 subtree 내부 node ID 중복");
        Qa.Check(!cloneIds.Any(oldIds.Contains), "복제 node ID가 원본 ID를 재사용함");
        Qa.Equal(new MathCaret(idMap[unicode.Id], 1), session.Caret, "복제 caret ID/문자소 offset 매핑");
        Qa.Equal(new MathSelection(new(idMap[unicode.Id], 3), new(idMap[unicode.Id], 1)),
            session.Selection, "역방향 부분 선택 anchor/focus 매핑");
        Qa.Equal<Guid?>(idMap[unicode.Id], session.SelectedNodeId, "복제 selectedNodeId 매핑");
        Qa.Check(!session.IsNodeSelected, "부분 텍스트 선택이 전체 구조 선택으로 바뀜");
        Qa.Equal(group.Alignment, session.Workspace.Groups[0].Alignment, "복제 후 정렬 모드");
        Qa.Equal(group.RowSpacing, session.Workspace.Groups[0].RowSpacing, "복제 후 행 간격");
        Qa.Equal(workspace.Style, session.Workspace.Style, "복제 후 workspace style");
        Qa.Equal(workspace.Groups[1], session.Workspace.Groups[1], "복제 후 다른 그룹 불변");
        _ = new EditingSession(session.Workspace); // 전역 workspace/group/row/node ID와 슬롯 invariant 재검증

        var afterWorkspace = Qa.Fingerprint(session.Workspace);
        var afterCaret = session.Caret;
        var afterSelection = session.Selection;
        session.Undo();
        Qa.Equal(before.Workspace, Qa.Fingerprint(session.Workspace), "깊은 복제 undo workspace");
        Qa.Equal(before.Caret, session.Caret, "깊은 복제 undo caret");
        Qa.Equal(before.Selection, session.Selection, "깊은 복제 undo selection");
        Qa.Check(!session.IsDirty && session.CanRedo, "깊은 복제 undo saved state/history");
        session.Redo();
        Qa.Equal(afterWorkspace, Qa.Fingerprint(session.Workspace), "깊은 복제 redo workspace");
        Qa.Equal(afterCaret, session.Caret, "깊은 복제 redo caret");
        Qa.Equal(afterSelection, session.Selection, "깊은 복제 redo selection");
        Qa.Check(session.IsDirty, "깊은 복제 redo dirty 상태");
    }

    private static void DuplicateCrossRowStructureSelection()
    {
        var caretText = Qa.Text("caret row");
        var numerator = Qa.Text("𝑥🙂");
        var denominator = new RadicalNode(Guid.NewGuid(), Qa.Sequence(Qa.Text("e\u0301"), Qa.Text("2")));
        var selectedFraction = new FractionNode(Guid.NewGuid(), numerator, denominator);
        var rowA = Row(Qa.Sequence(caretText));
        var rowB = Row(Qa.Sequence(Qa.Text("f="), selectedFraction));
        var trailing = Row(Qa.Text("after"));
        var session = new EditingSession(Workspace(Group([rowA, rowB, trailing])));
        Qa.Caret(session, caretText.Id, 4);
        Qa.Check(session.TrySelectNode(selectedFraction.Id), "교차 행 구조 선택");
        var before = Snapshot(session);

        Qa.Equal(2, session.ActiveRowNumber, "구조 선택 대상 행 번호");
        Qa.Check(session.DuplicateCurrentRow(), "교차 행 구조 선택 복제");
        var cloneRow = session.Workspace.Groups[0].Rows[2];
        var map = MapCloneTree(rowB.Root, cloneRow.Root);
        var cloneFractionId = map[selectedFraction.Id];
        var clonedFraction = Qa.Node(session, cloneFractionId);
        var firstText = Qa.Walk(clonedFraction).OfType<TextNode>().First();

        Qa.Equal(new MathCaret(firstText.Id, 0), session.Caret, "복제 선택구조 밖 원 caret의 clone 내부 fallback");
        Qa.Check(session.IsNodeSelected, "복제 뒤 전체 구조 선택 소실");
        Qa.Equal<Guid?>(cloneFractionId, session.SelectedNodeId, "선택 구조가 원본 ID에 남음");
        Qa.Equal(rowA.Id, session.Workspace.Groups[0].Rows[0].Id, "선택한 구조가 아닌 원 caret 행이 clone 대상으로 변경");
        var after = Snapshot(session);

        session.Undo();
        Qa.Equal(before.Workspace, Qa.Fingerprint(session.Workspace), "교차 행 clone undo workspace");
        Qa.Equal(before.Caret, session.Caret, "교차 행 clone undo 원 caret");
        Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, "교차 행 clone undo 선택 ID");
        Qa.Equal(before.IsNodeSelected, session.IsNodeSelected, "교차 행 clone undo 구조 선택 flag");
        session.Redo();
        Qa.Equal(after.Workspace, Qa.Fingerprint(session.Workspace), "교차 행 clone redo workspace");
        Qa.Equal(after.Caret, session.Caret, "교차 행 clone redo fallback caret");
        Qa.Equal(after.SelectedNodeId, session.SelectedNodeId, "교차 행 clone redo selection ID");
    }

    private static void DeleteChoosesAdjacentRow()
    {
        var firstText = Qa.Text("first");
        var middleText = Qa.Text("middle");
        var nextText = Qa.Text("next");
        var rows = new[] { Row(Qa.Sequence(firstText)), Row(Qa.Sequence(middleText)), Row(Qa.Sequence(nextText)) };
        var session = new EditingSession(Workspace(Group(rows)));
        Qa.Caret(session, middleText.Id, 3);
        var before = Snapshot(session);

        Qa.Check(session.CanDeleteCurrentRow && session.DeleteCurrentRow(), "중간 행 삭제");
        EqualSequence(new[] { rows[0].Id, rows[2].Id }, session.Workspace.Groups[0].Rows.Select(r => r.Id), "중간 행 제거·순서");
        Qa.Equal(new MathCaret(nextText.Id, 0), session.Caret, "삭제 뒤 다음 행 첫 텍스트 caret");
        var afterMiddleDelete = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(before.Workspace, Qa.Fingerprint(session.Workspace), "중간 행 삭제 undo workspace");
        Qa.Equal(before.Caret, session.Caret, "중간 행 삭제 undo caret");
        session.Redo();
        Qa.Equal(afterMiddleDelete, Qa.Fingerprint(session.Workspace), "중간 행 삭제 redo workspace");

        Qa.Caret(session, nextText.Id, 2);
        Qa.Check(session.DeleteCurrentRow(), "마지막 행 삭제");
        Qa.Equal(rows[0].Id, session.Workspace.Groups[0].Rows[^1].Id, "마지막 행 삭제 후 이전 행 유지");
        Qa.Equal(new MathCaret(firstText.Id, 0), session.Caret, "마지막 행 삭제 뒤 이전 행 첫 텍스트 caret");
    }

    private static void MoveRowsPreservesEditPosition()
    {
        var left = Qa.Text("left");
        var target = Qa.Text("target🙂e\u0301");
        var right = Qa.Text("right");
        var otherGroupText = Qa.Text("separate");
        var rows = new[] { Row(left), Row(Qa.Sequence(Qa.Text("prefix"), target)), Row(right) };
        var other = Row(otherGroupText);
        var session = new EditingSession(Workspace(Group(rows), Group([other], AlignmentMode.None, 52)));
        Qa.Select(session, target.Id, 8, 2);
        var before = Snapshot(session);

        Qa.Check(session.CanMoveCurrentRowUp && session.MoveCurrentRowUp(), "행 위 이동");
        EqualSequence(new[] { rows[1].Id, rows[0].Id, rows[2].Id },
            session.Workspace.Groups[0].Rows.Select(r => r.Id), "위 이동은 같은 그룹의 바로 이웃만 교환");
        Qa.Equal(other.Id, session.Workspace.Groups[1].Rows[0].Id, "위 이동이 다른 그룹을 변경");
        Qa.Equal(before.Caret, session.Caret, "위 이동 caret 보존");
        Qa.Equal(before.Selection, session.Selection, "위 이동 부분 선택 보존");
        Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, "위 이동 선택 대상 보존");
        var afterUp = Snapshot(session);
        session.Undo();
        Qa.Equal(before.Workspace, Qa.Fingerprint(session.Workspace), "위 이동 undo workspace");
        Qa.Equal(before.Caret, session.Caret, "위 이동 undo caret");
        Qa.Equal(before.Selection, session.Selection, "위 이동 undo selection");
        session.Redo();
        Qa.Equal(afterUp.Workspace, Qa.Fingerprint(session.Workspace), "위 이동 redo workspace");
        Qa.Equal(afterUp.Caret, session.Caret, "위 이동 redo caret");
        Qa.Equal(afterUp.Selection, session.Selection, "위 이동 redo selection");

        Qa.Check(session.MoveCurrentRowDown(), "복귀 방향 아래 이동");
        EqualSequence(new[] { rows[0].Id, rows[1].Id, rows[2].Id },
            session.Workspace.Groups[0].Rows.Select(r => r.Id), "아래 이동 순서");
        Qa.Equal(before.Caret, session.Caret, "아래 이동 caret 보존");
        Qa.Equal(before.Selection, session.Selection, "아래 이동 selection 보존");
    }

    private static void RejectRowBoundariesWithoutMutation()
    {
        var only = Qa.Text("only");
        var a = Row(Qa.Text("a"));
        var b = Row(Qa.Text("b"));
        var singleGroup = Group([Row(only)]);
        var secondGroup = Group([a, b]);
        var session = new EditingSession(Workspace(singleGroup, secondGroup));

        var onlyBefore = Snapshot(session);
        Qa.Check(!session.CanDeleteCurrentRow, "단일 행 그룹 삭제 CanExecute가 true");
        Qa.Check(!session.DeleteCurrentRow(), "그룹 유일 행 삭제가 허용됨");
        Qa.Check(!session.MoveCurrentRowUp() && !session.MoveCurrentRowDown(), "단일 행 그룹 경계 이동이 허용됨");
        EqualSnapshot(onlyBefore, Snapshot(session), "단일 행 그룹 no-op");

        Qa.Caret(session, ((TextNode)a.Root).Id, 1);
        var firstBefore = Snapshot(session);
        Qa.Check(!session.CanMoveCurrentRowUp && !session.MoveCurrentRowUp(), "첫 행 위 이동 경계");
        EqualSnapshot(firstBefore, Snapshot(session), "첫 행 위 이동 무변경");

        Qa.Caret(session, ((TextNode)b.Root).Id, 0);
        var lastBefore = Snapshot(session);
        Qa.Check(!session.CanMoveCurrentRowDown && !session.MoveCurrentRowDown(), "마지막 행 아래 이동 경계");
        EqualSnapshot(lastBefore, Snapshot(session), "마지막 행 아래 이동 무변경");
        Qa.Check(session.CanMoveCurrentRowUp, "다른 그룹의 첫 행 때문에 인접 행 CanExecute가 잘못 계산됨");
    }

    private static async Task SaveAndExportRows()
    {
        var rowA = Row(Qa.Sequence(Qa.Text("A=x")));
        var rowB = Row(Qa.Sequence(Qa.Text("long=y")));
        var thirdGroupRow = Row(Qa.Text("u=1"));
        var session = new EditingSession(Workspace(
            Group([rowA, rowB], AlignmentMode.Equals, 24),
            Group([thirdGroupRow], AlignmentMode.None, 11)));
        var savedAtStart = session.CaptureSnapshot();
        session.MarkSaved(savedAtStart);
        var rowAText = ((SequenceNode)rowA.Root).Children.OfType<TextNode>().Single();
        Qa.Caret(session, rowAText.Id, 3);

        Qa.Check(session.AddRowAfterCurrent(), "출력 사례의 새 행 추가");
        var insertedRow = session.Workspace.Groups[0].Rows[1];
        var insertedRowId = insertedRow.Id;
        var insertedLeaf = (TextNode)insertedRow.Root;
        Qa.Check(insertedLeaf.IsEmptySlot, "출력 사례 새 행 필수 슬롯 누락");
        var incomplete = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics());
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(incomplete), "행 추가 후 미완성 필수 슬롯 SVG 출력 거절");
        Qa.Check(session.InsertText("new=z"), "새 행 내용 채우기");
        Qa.Check(session.MoveCurrentRowDown(), "새 행 아래 이동");
        Qa.Check(session.IsDirty, "행 추가/편집 dirty 상태 누락");

        var group = session.Workspace.Groups[0];
        EqualSequence(new[] { rowA.Id, rowB.Id, insertedRowId }, group.Rows.Select(r => r.Id), "행 move 전후 logical ordering");
        var latex = LatexSerializer.ToLatex(session.Workspace);
        var firstIndex = latex.IndexOf(@"{\displaystyle A}&={\displaystyle x}", StringComparison.Ordinal);
        var secondIndex = latex.IndexOf(@"{\displaystyle long}&={\displaystyle y}", StringComparison.Ordinal);
        var addedIndex = latex.IndexOf(@"{\displaystyle new}&={\displaystyle z}", StringComparison.Ordinal);
        Qa.Check(firstIndex >= 0 && firstIndex < secondIndex && secondIndex < addedIndex,
            "LaTeX 행 순서 또는 등호 기준 alignment 손실: " + latex);
        Qa.Check(latex.Contains(" \\\\", StringComparison.Ordinal), "LaTeX 다중 행 구분 기호 누락: " + latex);

        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics());
        EqualSequence(session.Workspace.Groups.SelectMany(g => g.Rows).Select(r => r.Id),
            layout.LogicalRows.Select(r => r.RowId), "layout 논리 행 순서/ID");
        var svg = new SvgExportService().ToSvg(layout);
        var svgDocument = XDocument.Parse(svg);
        var svgRows = svgDocument.Descendants()
            .Where(element => element.Name.LocalName == "text")
            .GroupBy(element => (string?)element.Attribute("y")
                ?? throw new InvalidOperationException("SVG text run에 baseline y가 없습니다."))
            .Select(group => new
            {
                Baseline = double.Parse(group.Key, CultureInfo.InvariantCulture),
                Text = string.Concat(group.Select(element => element.Value))
            })
            .ToArray();
        EqualSequence(new[] { "A=x", "long=y", "new=z", "u=1" },
            svgRows.Select(row => row.Text), "SVG logical row text order (style-split runs joined by baseline)");
        Qa.Check(svgRows.Zip(svgRows.Skip(1), (previous, next) => next.Baseline > previous.Baseline).All(increases => increases),
            "SVG 논리 행 baseline이 아래 방향으로 증가하지 않음: " + string.Join(",", svgRows.Select(row => row.Baseline.ToString("R", CultureInfo.InvariantCulture))));
        var alignmentXs = layout.LogicalRows.Take(3).Select(r => r.AlignmentX).ToArray();
        Qa.Check(alignmentXs.All(x => x is not null), "등호 정렬 그룹의 행 alignment 좌표 누락");
        Qa.Near(alignmentXs[0]!.Value, alignmentXs[1]!.Value, "첫 두 행 등호 정렬");
        Qa.Near(alignmentXs[0]!.Value, alignmentXs[2]!.Value, "세 행 등호 정렬");

        using var directory = new TempDirectory();
        var path = directory.File("row-roundtrip.aethermath");
        var snapshot = session.CaptureSnapshot();
        await new WorkspaceFileService().SaveAsync(path, snapshot);
        session.MarkSaved(snapshot);
        Qa.Check(!session.IsDirty, "저장 snapshot dirty 해제");
        var reopened = await new WorkspaceFileService().OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(reopened), "행 ID/순서·스타일·그룹 저장 재열기");

        var savedSession = new EditingSession(reopened);
        var rowText = Qa.Nodes(reopened).OfType<TextNode>().First(n => n.Text == "A=x");
        Qa.Caret(savedSession, rowText.Id, 1);
        var oldSnapshot = savedSession.CaptureSnapshot();
        Qa.Check(savedSession.AddRowAfterCurrent(), "저장 snapshot 의미 사례에서 행 추가");
        savedSession.MarkSaved(oldSnapshot);
        Qa.Check(savedSession.IsDirty, "이전 snapshot MarkSaved가 현재 행 변경을 clean 처리");
        savedSession.Undo();
        Qa.Check(!savedSession.IsDirty, "undo가 저장된 StateId로 돌아와도 dirty 유지");
        savedSession.Redo();
        Qa.Check(savedSession.IsDirty, "redo가 저장 이후 StateId로 돌아오지 않음");
    }

    private static async Task RowEditInvalidatesPendingPaste()
    {
        var first = Qa.Text("first");
        var target = Qa.Text("target");
        var last = Qa.Text("last");
        var session = new EditingSession(Workspace(Group([Row(first), Row(target), Row(last)])));
        Qa.Caret(session, target.Id, 2);
        var control = new MathEditorControl { Session = session };
        control.SetVisualTreeAttachmentState(attached: true);
        var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.ClipboardTextReaderOverride = _ =>
        {
            readStarted.TrySetResult(true);
            return read.Task;
        };

        var pending = control.PasteFromClipboardAsync();
        await readStarted.Task.WaitAsync(WaitLimit);
        var beforeMove = Snapshot(session);
        Qa.Check(session.MoveCurrentRowDown(), "paste 대기 중 활성 행 이동");
        var afterMove = Snapshot(session);
        Qa.Check(afterMove.Workspace != beforeMove.Workspace, "paste 경쟁 행 이동이 workspace를 바꾸지 않음");
        read.SetResult("STALE");
        Qa.Equal(ClipboardPasteResult.StaleTarget, await pending.WaitAsync(WaitLimit), "행 이동 중인 paste stale 결과");
        EqualSnapshot(afterMove, Snapshot(session), "행 이동 뒤 늦은 paste의 상태 보존");
        Qa.Equal("target", Qa.Leaf(session, target.Id).Text, "행 이동 뒤 늦은 paste가 내용에 적용됨");

        control.ClipboardTextReaderOverride = _ => Task.FromResult<string?>("!");
        Qa.Equal(ClipboardPasteResult.Applied,
            await control.PasteFromClipboardAsync().WaitAsync(WaitLimit), "stale row paste 뒤 재시도");
        Qa.Equal("ta!rget", Qa.Leaf(session, target.Id).Text, "stale row paste 뒤 재시도 텍스트");
        control.SetVisualTreeAttachmentState(attached: false);
    }

    private static EquationRow Row(MathNode root) => new(Guid.NewGuid(), root);

    private static EquationGroup Group(EquationRow[] rows,
        AlignmentMode alignment = AlignmentMode.Equals, double rowSpacing = 26) =>
        new(Guid.NewGuid(), [.. rows], alignment, rowSpacing);

    private static EquationWorkspace Workspace(params EquationGroup[] groups) => new(
        Guid.NewGuid(), 1, "행 편집 QA 𝑥 e\u0301 한글🙂", [.. groups], new MathStyle(31, "Cambria Math", "#24364B"));

    private static EditSnapshot Snapshot(EditingSession session) => new(
        Qa.Fingerprint(session.Workspace), session.Revision, session.Caret, session.Selection,
        session.SelectedNodeId, session.IsNodeSelected, session.CanUndo, session.CanRedo, session.IsDirty);

    private static void EqualSnapshot(EditSnapshot expected, EditSnapshot actual, string label) =>
        Qa.Equal(expected, actual, label);

    private static void EqualSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();
        Qa.Check(expectedArray.SequenceEqual(actualArray),
            $"{label}: 기대=[{string.Join(",", expectedArray)}]; 실제=[{string.Join(",", actualArray)}]");
    }

    private static Dictionary<Guid, Guid> MapCloneTree(MathNode original, MathNode clone)
    {
        var map = new Dictionary<Guid, Guid>();
        AddNodePairs(original, clone, map);
        return map;
    }

    private static void AddNodePairs(MathNode original, MathNode clone, Dictionary<Guid, Guid> map)
    {
        Qa.Equal(original.GetType(), clone.GetType(), "clone 노드 형식");
        Qa.Check(original.Id != clone.Id, "clone 노드가 원본 ID를 재사용");
        map.Add(original.Id, clone.Id);
        switch (original, clone)
        {
            case (TextNode oldText, TextNode newText):
                Qa.Equal(oldText.Text, newText.Text, "clone Unicode/text 원문");
                Qa.Equal(oldText.IsEmptySlot, newText.IsEmptySlot, "clone 필수 슬롯 flag");
                break;
            case (SequenceNode oldSequence, SequenceNode newSequence):
                Qa.Equal(oldSequence.Children.Length, newSequence.Children.Length, "clone sequence child count");
                for (var i = 0; i < oldSequence.Children.Length; i++)
                    AddNodePairs(oldSequence.Children[i], newSequence.Children[i], map);
                break;
            case (FractionNode oldFraction, FractionNode newFraction):
                AddNodePairs(oldFraction.Numerator, newFraction.Numerator, map);
                AddNodePairs(oldFraction.Denominator, newFraction.Denominator, map);
                break;
            case (RadicalNode oldRadical, RadicalNode newRadical):
                AddNodePairs(oldRadical.Radicand, newRadical.Radicand, map);
                break;
            case (ScriptNode oldScript, ScriptNode newScript):
                Qa.Equal(oldScript.Subscript is null, newScript.Subscript is null, "clone subscript presence");
                Qa.Equal(oldScript.Superscript is null, newScript.Superscript is null, "clone superscript presence");
                AddNodePairs(oldScript.Base, newScript.Base, map);
                if (oldScript.Subscript is not null) AddNodePairs(oldScript.Subscript, newScript.Subscript!, map);
                if (oldScript.Superscript is not null) AddNodePairs(oldScript.Superscript, newScript.Superscript!, map);
                break;
            case (MatrixNode oldMatrix, MatrixNode newMatrix):
                Qa.Equal(oldMatrix.Rows, newMatrix.Rows, "clone matrix rows");
                Qa.Equal(oldMatrix.Columns, newMatrix.Columns, "clone matrix columns");
                Qa.Equal(oldMatrix.Cells.Length, newMatrix.Cells.Length, "clone matrix cell count");
                for (var i = 0; i < oldMatrix.Cells.Length; i++)
                    AddNodePairs(oldMatrix.Cells[i], newMatrix.Cells[i], map);
                break;
            case (IntegralNode oldIntegral, IntegralNode newIntegral):
                Qa.Equal(oldIntegral.LowerLimit is null, newIntegral.LowerLimit is null, "clone integral lower-bound presence");
                Qa.Equal(oldIntegral.UpperLimit is null, newIntegral.UpperLimit is null, "clone integral upper-bound presence");
                AddNodePairs(oldIntegral.Integrand, newIntegral.Integrand, map);
                if (oldIntegral.LowerLimit is not null) AddNodePairs(oldIntegral.LowerLimit, newIntegral.LowerLimit!, map);
                if (oldIntegral.UpperLimit is not null) AddNodePairs(oldIntegral.UpperLimit, newIntegral.UpperLimit!, map);
                AddNodePairs(oldIntegral.DifferentialVariable, newIntegral.DifferentialVariable, map);
                break;
            case (SummationNode oldSum, SummationNode newSum):
                Qa.Equal(oldSum.LowerLimit is null, newSum.LowerLimit is null, "clone sum lower-bound presence");
                Qa.Equal(oldSum.UpperLimit is null, newSum.UpperLimit is null, "clone sum upper-bound presence");
                AddNodePairs(oldSum.Body, newSum.Body, map);
                if (oldSum.LowerLimit is not null) AddNodePairs(oldSum.LowerLimit, newSum.LowerLimit!, map);
                if (oldSum.UpperLimit is not null) AddNodePairs(oldSum.UpperLimit, newSum.UpperLimit!, map);
                break;
            case (LimitNode oldLimit, LimitNode newLimit):
                AddNodePairs(oldLimit.Approach, newLimit.Approach, map);
                AddNodePairs(oldLimit.Body, newLimit.Body, map);
                break;
            case (BracketsNode oldBrackets, BracketsNode newBrackets):
                Qa.Equal(oldBrackets.Kind, newBrackets.Kind, "clone bracket kind");
                AddNodePairs(oldBrackets.Content, newBrackets.Content, map);
                break;
            case (CasesNode oldCases, CasesNode newCases):
                Qa.Equal(oldCases.Rows.Length, newCases.Rows.Length, "clone cases row count");
                for (var i = 0; i < oldCases.Rows.Length; i++)
                {
                    AddNodePairs(oldCases.Rows[i].Expression, newCases.Rows[i].Expression, map);
                    AddNodePairs(oldCases.Rows[i].Condition, newCases.Rows[i].Condition, map);
                }
                break;
            default:
                throw new InvalidOperationException("검증되지 않은 clone 노드 조합");
        }
    }

    private sealed record EditSnapshot(string Workspace, long Revision, MathCaret Caret, MathSelection? Selection,
        Guid? SelectedNodeId, bool IsNodeSelected, bool CanUndo, bool CanRedo, bool IsDirty);
}
