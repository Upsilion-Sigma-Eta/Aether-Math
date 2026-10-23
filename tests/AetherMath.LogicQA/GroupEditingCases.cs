using System.Globalization;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class GroupEditingCases
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    public static IEnumerable<QaCase> All()
    {
        yield return new("GROUP-ADD", "현재 그룹 뒤에 속성을 상속한 필수 빈 그룹 추가와 단일 undo/redo", AddGroupAfterCurrent);
        yield return new("GROUP-ACTIVE", "교차 그룹 구조 선택이 선택 그룹을 모든 명령의 대상으로 지정", SelectedStructureChoosesActiveGroup);
        yield return new("GROUP-DUPLICATE", "여러 행·깊은 구조·Unicode와 그룹 전체 ID/선택 매핑 복제", DuplicateGroupAndMapAllRows);
        yield return new("GROUP-DUP-CROSS", "교차 그룹 구조 선택 복제의 fallback과 undo/redo 복원", DuplicateCrossGroupStructureSelection);
        yield return new("GROUP-DELETE", "그룹 삭제 뒤 다음 또는 이전 그룹 첫 텍스트로 이동", DeleteChoosesAdjacentGroup);
        yield return new("GROUP-MOVE", "인접 그룹만 교환하고 행 순서·caret·선택을 보존", MoveGroupsPreservesEditPosition);
        yield return new("GROUP-NOOP", "최소 그룹 수와 이동 경계에서 전체 상태 무변경", RejectGroupBoundariesWithoutMutation);
        yield return new("GROUP-IO", "그룹 저장 재열기·LaTeX/SVG 순서와 필수 빈 출력 거절", SaveAndExportGroups);
        yield return new("GROUP-PASTE", "그룹 이동·삭제 중 대기 중인 붙여넣기 폐기와 재시도", GroupEditInvalidatesPendingPaste);
    }

    private static void AddGroupAfterCurrent()
    {
        var beforeGroup = Group([Row(Qa.Sequence(Qa.Text("before=1")))], AlignmentMode.None, 9.25);
        var targetText = Qa.Text("middle=2");
        var target = Group([Row(Qa.Sequence(targetText)), Row(Qa.Text("middle=3"))], AlignmentMode.Equals, 42.5);
        var afterGroup = Group([Row(Qa.Sequence(Qa.Text("after=4")))], AlignmentMode.None, 71);
        var session = new EditingSession(Workspace(beforeGroup, target, afterGroup));
        Qa.Select(session, targetText.Id, 7, 1);
        var before = Snapshot(session);
        session.MarkSaved(session.CaptureSnapshot());
        before = Snapshot(session);
        Qa.Equal(0, before.Revision, "caret/selection 설정이 revision을 증가");

        Qa.Check(session.CanAddGroupAfterCurrent && session.AddGroupAfterCurrent(), "활성 그룹 뒤 추가 가능/성공");
        Qa.Equal(4, session.GroupCount, "추가 뒤 그룹 수");
        var groups = session.Workspace.Groups;
        Qa.Equal(beforeGroup.Id, groups[0].Id, "이전 그룹 위치 보존");
        Qa.Equal(target.Id, groups[1].Id, "대상 그룹 위치 보존");
        Qa.Equal(afterGroup.Id, groups[3].Id, "뒤 그룹 위치 보존");
        var added = groups[2];
        Qa.Check(added.Id != target.Id && added.Id != beforeGroup.Id && added.Id != afterGroup.Id,
            "추가 그룹 ID가 기존 그룹과 충돌");
        Qa.Equal(target.Alignment, added.Alignment, "추가 그룹 정렬 속성 상속");
        Qa.Equal(target.RowSpacing, added.RowSpacing, "추가 그룹 행 간격 상속");
        Qa.Equal(1, added.Rows.Length, "새 그룹의 초기 행 수");
        Qa.Check(added.Rows[0].Id != target.Rows[0].Id, "추가 그룹 row ID 재사용");
        var empty = added.Rows[0].Root as TextNode;
        Qa.Check(empty is { Text.Length: 0, IsEmptySlot: true }, "새 그룹의 필수 빈 Text 슬롯 누락");
        Qa.Check(empty!.Id != ((SequenceNode)target.Rows[0].Root).Children[0].Id,
            "새 그룹의 빈 슬롯 ID 재사용");
        Qa.Equal(new MathCaret(empty.Id, 0), session.Caret, "새 빈 그룹 caret 0");
        Qa.Check(session.Selection is null && !session.IsNodeSelected, "추가 뒤 이전 선택 잔존");
        Qa.Equal(3, session.ActiveGroupNumber, "추가 뒤 활성 그룹 번호");
        Qa.Equal(before.WorkspaceStyle, session.Workspace.Style, "추가 뒤 작업 스타일 보존");
        Qa.Equal(1, session.Workspace.SchemaVersion, "그룹 추가가 schema version을 변경");
        Qa.Check(session.IsDirty && session.CanUndo && !session.CanRedo, "그룹 추가 dirty/history 상태");
        AssertGlobalIdsUnique(session.Workspace, "그룹 추가 ID 고유성");

        var after = Snapshot(session);
        session.Undo();
        AssertUndone(before, after, Snapshot(session), "그룹 추가 undo snapshot");
        Qa.Check(!session.IsDirty && session.CanRedo, "추가 undo가 저장 상태를 복원하지 않음");
        session.Redo();
        AssertRedone(after, Snapshot(session), "그룹 추가 redo snapshot");
        Qa.Check(session.IsDirty, "추가 redo가 dirty 상태를 복원하지 않음");
    }

    private static void SelectedStructureChoosesActiveGroup()
    {
        var caretText = Qa.Text("caret-group");
        var first = Group([Row(Qa.Sequence(caretText))], AlignmentMode.None, 12);
        var targetFirst = Row(Qa.Text("target-first"));
        var numerator = Qa.Text("𝑥🙂");
        var denominator = Qa.Text("e\u0301");
        var fraction = new FractionNode(Guid.NewGuid(), numerator, denominator);
        var targetSecond = Row(Qa.Sequence(Qa.Text("f="), fraction));
        var target = Group([targetFirst, targetSecond], AlignmentMode.Equals, 37);
        var last = Group([Row(Qa.Text("last-group"))], AlignmentMode.None, 55);
        var workspace = Workspace(first, target, last);
        var addSession = new EditingSession(workspace);
        Qa.Caret(addSession, caretText.Id, 5);
        Qa.Check(addSession.TrySelectNode(fraction.Id), "그룹 추가 사례의 교차 그룹 구조 선택 실패");
        var addBefore = Snapshot(addSession);
        Qa.Check(addBefore.ActiveGroupNumber == 2 && addSession.AddGroupAfterCurrent(),
            "교차 그룹 선택 대상 뒤 추가 실패");
        EqualSequence(new[] { first.Id, target.Id, addSession.Workspace.Groups[2].Id, last.Id },
            addSession.Workspace.Groups.Select(group => group.Id), "전체 선택 그룹 뒤에 새 그룹이 삽입되지 않음");
        Qa.Equal(target.Alignment, addSession.Workspace.Groups[2].Alignment, "선택 그룹의 정렬 속성을 추가 그룹이 상속하지 않음");
        Qa.Equal(target.RowSpacing, addSession.Workspace.Groups[2].RowSpacing, "선택 그룹의 간격을 추가 그룹이 상속하지 않음");
        Qa.Equal(3, addSession.ActiveGroupNumber, "교차 그룹 선택 뒤 추가 그룹의 활성 번호");
        var addAfter = Snapshot(addSession);
        addSession.Undo();
        AssertUndone(addBefore, addAfter, Snapshot(addSession), "선택 그룹 뒤 추가 undo");
        addSession.Redo();
        AssertRedone(addAfter, Snapshot(addSession), "선택 그룹 뒤 추가 redo");

        var session = new EditingSession(workspace);
        Qa.Caret(session, caretText.Id, 5);
        Qa.Check(session.TrySelectNode(fraction.Id), "다른 그룹 구조 전체 선택 실패");

        Qa.Equal(new MathCaret(caretText.Id, 5), session.Caret, "교차 그룹 선택이 원 caret를 이동");
        Qa.Equal(2, session.ActiveGroupNumber, "선택 구조가 포함된 그룹을 활성 그룹으로 해석하지 않음");
        Qa.Equal(2, session.ActiveRowNumber, "선택 구조의 실제 행 위치");
        var before = Snapshot(session);
        Qa.Check(session.CanMoveCurrentGroupUp && session.MoveCurrentGroupUp(), "선택 구조 그룹 위 이동 실패");
        Qa.Equal(target.Id, session.Workspace.Groups[0].Id, "선택된 그룹을 이동하지 않음");
        Qa.Equal(first.Id, session.Workspace.Groups[1].Id, "caret 그룹이 선택 그룹을 대신해 이동");
        Qa.Equal(last.Id, session.Workspace.Groups[2].Id, "인접 그룹 경계 밖 변경");
        Qa.Equal(targetSecond.Id, session.Workspace.Groups[0].Rows[1].Id, "그룹 이동 중 내부 행 순서 변경");
        Qa.Equal(before.Caret, session.Caret, "교차 그룹 이동 caret 보존");
        Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, "교차 그룹 이동 선택 ID 보존");
        Qa.Check(session.IsNodeSelected, "교차 그룹 이동 뒤 구조 선택 flag 소실");
        Qa.Equal(1, session.ActiveGroupNumber, "선택 그룹 이동 후 활성 번호 재계산");

        var after = Snapshot(session);
        session.Undo();
        AssertUndone(before, after, Snapshot(session), "선택 그룹 이동 undo");
        session.Redo();
        AssertRedone(after, Snapshot(session), "선택 그룹 이동 redo");
    }

    private static void DuplicateGroupAndMapAllRows()
    {
        var (deepGroup, caretText, unicode, matrix) = CreateDeepGroup();
        var outside = Group([Row(Qa.Text("outside"))], AlignmentMode.None, 61);

        // 역방향 Unicode 부분 선택은 복제 그룹의 대응 Text ID와 같은 문자소 offset을 유지해야 한다.
        var partialSession = new EditingSession(Workspace(deepGroup, outside));
        Qa.Select(partialSession, unicode.Id, 3, 1);
        var partialBefore = Snapshot(partialSession);
        partialSession.MarkSaved(partialSession.CaptureSnapshot());
        partialBefore = Snapshot(partialSession);
        Qa.Check(partialSession.DuplicateCurrentGroup(), "역방향 부분 선택 그룹 복제 실패");
        var partialClone = partialSession.Workspace.Groups[1];
        var partialMap = MapCloneGroup(deepGroup, partialClone);
        AssertCloneGroup(deepGroup, partialClone, partialMap, "깊은 그룹 복제");
        Qa.Equal(new MathCaret(partialMap[unicode.Id], 1), partialSession.Caret,
            "역방향 Unicode 선택 clone caret 매핑");
        Qa.Equal(new MathSelection(new(partialMap[unicode.Id], 3), new(partialMap[unicode.Id], 1)),
            partialSession.Selection, "역방향 Unicode selection anchor/focus 매핑");
        Qa.Equal<Guid?>(partialMap[unicode.Id], partialSession.SelectedNodeId, "부분 선택 selected ID 매핑");
        Qa.Check(!partialSession.IsNodeSelected, "부분 선택이 전체 구조 선택으로 변환");
        Qa.Equal(outside.Id, partialSession.Workspace.Groups[2].Id, "복제 뒤 비대상 그룹 변경");
        Qa.Equal(partialSession.Workspace.Style, Workspace(deepGroup, outside).Style, "복제 뒤 작업 스타일 변경");
        AssertGlobalIdsUnique(partialSession.Workspace, "깊은 복제 전체 ID 고유성");

        var partialAfter = Snapshot(partialSession);
        partialSession.Undo();
        AssertUndone(partialBefore, partialAfter, Snapshot(partialSession), "역방향 부분 선택 복제 undo");
        partialSession.Redo();
        AssertRedone(partialAfter, Snapshot(partialSession), "역방향 부분 선택 복제 redo");

        // caret는 같은 그룹 다른 행에, 구조 선택은 두 번째 행에 둔다. caret도 그룹 전체 ID map으로 이동해야 한다.
        var mappedSession = new EditingSession(Workspace(deepGroup, outside));
        Qa.Caret(mappedSession, caretText.Id, 6);
        Qa.Check(mappedSession.TrySelectNode(matrix.Id), "다른 행의 Matrix 전체 선택 실패");
        Qa.Equal(1, mappedSession.ActiveGroupNumber, "전체 선택 대상 그룹 활성화");
        var mappedBefore = Snapshot(mappedSession);
        Qa.Check(mappedSession.DuplicateCurrentGroup(), "같은 그룹 교차 행 caret/구조 선택 복제 실패");
        var clone = mappedSession.Workspace.Groups[1];
        var map = MapCloneGroup(deepGroup, clone);
        AssertCloneGroup(deepGroup, clone, map, "교차 행 선택 그룹 복제");
        Qa.Equal(new MathCaret(map[caretText.Id], 6), mappedSession.Caret,
            "동일 그룹 다른 행 caret가 fallback 대신 원 대응 clone으로 매핑되지 않음");
        Qa.Check(mappedSession.IsNodeSelected, "복제 뒤 전체 구조 선택 flag 소실");
        Qa.Equal<Guid?>(map[matrix.Id], mappedSession.SelectedNodeId, "선택 구조가 복제 ID로 매핑되지 않음");
        Qa.Check(!map.ContainsValue(caretText.Id) && !map.ContainsValue(matrix.Id), "clone ID map이 원본 ID를 가리킴");
        AssertGlobalIdsUnique(mappedSession.Workspace, "교차 행 복제 전체 ID 고유성");
        var mappedAfter = Snapshot(mappedSession);
        mappedSession.Undo();
        AssertUndone(mappedBefore, mappedAfter, Snapshot(mappedSession), "교차 행 caret 복제 undo");
        mappedSession.Redo();
        AssertRedone(mappedAfter, Snapshot(mappedSession), "교차 행 caret 복제 redo");
    }

    private static void DuplicateCrossGroupStructureSelection()
    {
        var (selectedGroup, _, _, selectedMatrix) = CreateDeepGroup();
        var outsideCaret = Qa.Text("external🙂");
        var outsideGroup = Group([Row(Qa.Sequence(Qa.Text("z="), outsideCaret))], AlignmentMode.None, 50);
        var session = new EditingSession(Workspace(selectedGroup, outsideGroup));
        Qa.Caret(session, outsideCaret.Id, 4);
        Qa.Check(session.TrySelectNode(selectedMatrix.Id), "다른 그룹 caret와 선택 그룹 구조 설정 실패");
        Qa.Equal(1, session.ActiveGroupNumber, "whole-node selection이 caret 그룹 대신 선택 그룹을 활성화하지 않음");
        var before = Snapshot(session);

        Qa.Check(session.DuplicateCurrentGroup(), "교차 그룹 구조 선택 복제 실패");
        var clone = session.Workspace.Groups[1];
        var map = MapCloneGroup(selectedGroup, clone);
        var clonedMatrix = Qa.Node(session, map[selectedMatrix.Id]);
        var firstText = Qa.Walk(clonedMatrix).OfType<TextNode>().First();
        Qa.Equal(new MathCaret(firstText.Id, 0), session.Caret,
            "외부 그룹 caret가 복제된 selected subtree 첫 Text 0으로 fallback되지 않음");
        Qa.Check(session.IsNodeSelected, "복제 뒤 전체 구조 선택 소실");
        Qa.Equal<Guid?>(map[selectedMatrix.Id], session.SelectedNodeId, "교차 그룹 선택 구조가 clone ID로 매핑되지 않음");
        Qa.Equal(outsideGroup.Id, session.Workspace.Groups[2].Id, "cross-group 복제 중 외부 그룹 위치/ID 변경");
        AssertGlobalIdsUnique(session.Workspace, "교차 그룹 복제 전체 ID 고유성");
        var after = Snapshot(session);

        session.Undo();
        AssertUndone(before, after, Snapshot(session), "cross-group clone undo 원 caret+선택 조합");
        session.Redo();
        AssertRedone(after, Snapshot(session), "cross-group clone redo fallback+선택 조합");
    }

    private static void DeleteChoosesAdjacentGroup()
    {
        var firstText = Qa.Text("first=1");
        var first = Group([Row(Qa.Sequence(firstText))], AlignmentMode.None, 13);
        var selectedTarget = new FractionNode(Guid.NewGuid(), Qa.Text("remove=2"), Qa.Text("remove=3"));
        var target = Group([Row(Qa.Sequence(Qa.Text("prefix"), selectedTarget)), Row(Qa.Text("remove=4"))],
            AlignmentMode.Equals, 31);
        var nextText = Qa.Text("next=4");
        var next = Group([Row(Qa.Sequence(nextText)), Row(Qa.Text("next=5"))], AlignmentMode.None, 47);
        var lastText = Qa.Text("last=6");
        var last = Group([Row(Qa.Sequence(lastText))], AlignmentMode.Equals, 19);
        var session = new EditingSession(Workspace(first, target, next, last));
        Qa.Caret(session, firstText.Id, 3);
        Qa.Check(session.TrySelectNode(selectedTarget.Id), "그룹 삭제 사례의 교차 그룹 구조 선택 실패");
        var before = Snapshot(session);
        Qa.Equal(2, session.ActiveGroupNumber, "교차 그룹 선택 대상이 삭제 그룹으로 활성화되지 않음");
        session.MarkSaved(session.CaptureSnapshot());
        before = Snapshot(session);

        Qa.Check(session.CanDeleteCurrentGroup && session.DeleteCurrentGroup(), "중간 그룹 삭제 실패");
        EqualSequence(new[] { first.Id, next.Id, last.Id }, session.Workspace.Groups.Select(group => group.Id),
            "중간 그룹 삭제 후 순서");
        Qa.Equal(new MathCaret(nextText.Id, 0), session.Caret, "삭제 후 다음 그룹 첫 행 첫 Text caret");
        Qa.Equal(2, session.ActiveGroupNumber, "삭제 후 다음 그룹 활성 번호");
        var afterMiddle = Snapshot(session);
        session.Undo();
        AssertUndone(before, afterMiddle, Snapshot(session), "중간 그룹 삭제 undo 및 저장 상태");
        session.Redo();
        AssertRedone(afterMiddle, Snapshot(session), "중간 그룹 삭제 redo");

        Qa.Caret(session, lastText.Id, 3);
        var beforeLast = Snapshot(session);
        Qa.Check(session.DeleteCurrentGroup(), "마지막 그룹 삭제 실패");
        EqualSequence(new[] { first.Id, next.Id }, session.Workspace.Groups.Select(group => group.Id),
            "마지막 그룹 삭제 후 순서");
        Qa.Equal(new MathCaret(nextText.Id, 0), session.Caret, "마지막 그룹 삭제 후 이전 그룹 첫 행 첫 Text caret");
        var afterLast = Snapshot(session);
        session.Undo();
        AssertUndone(beforeLast, afterLast, Snapshot(session), "마지막 그룹 삭제 undo");
        session.Redo();
        AssertRedone(afterLast, Snapshot(session), "마지막 그룹 삭제 redo");
    }

    private static void MoveGroupsPreservesEditPosition()
    {
        var groupA = Group([Row(Qa.Text("A"))], AlignmentMode.None, 12);
        var targetText = Qa.Text("target🙂e\u0301");
        var groupB = Group([Row(Qa.Text("B0")), Row(Qa.Sequence(Qa.Text("prefix"), targetText))],
            AlignmentMode.Equals, 25.5);
        var groupC = Group([Row(Qa.Text("C0")), Row(Qa.Text("C1")), Row(Qa.Text("C2"))],
            AlignmentMode.None, 47.25);
        var groupD = Group([Row(Qa.Text("D"))], AlignmentMode.Equals, 66);
        var session = new EditingSession(Workspace(groupA, groupB, groupC, groupD));
        Qa.Select(session, targetText.Id, 8, 2);
        var before = Snapshot(session);
        var rowsBefore = groupB.Rows.Select(row => row.Id).ToArray();

        Qa.Check(session.CanMoveCurrentGroupUp && session.MoveCurrentGroupUp(), "중간 그룹 위 이동");
        EqualSequence(new[] { groupB.Id, groupA.Id, groupC.Id, groupD.Id },
            session.Workspace.Groups.Select(group => group.Id), "그룹 위 이동은 바로 이웃만 교환");
        EqualSequence(rowsBefore, session.Workspace.Groups[0].Rows.Select(row => row.Id), "그룹 위 이동 중 내부 행 순서 보존");
        Qa.Equal(before.Caret, session.Caret, "그룹 위 이동 caret 보존");
        Qa.Equal(before.Selection, session.Selection, "그룹 위 이동 역방향 부분 선택 보존");
        Qa.Equal(before.SelectedNodeId, session.SelectedNodeId, "그룹 위 이동 선택 ID 보존");
        Qa.Equal(1, session.ActiveGroupNumber, "그룹 위 이동 후 활성 번호");
        var afterUp = Snapshot(session);
        session.Undo();
        AssertUndone(before, afterUp, Snapshot(session), "그룹 위 이동 undo");
        session.Redo();
        AssertRedone(afterUp, Snapshot(session), "그룹 위 이동 redo");

        Qa.Check(session.CanMoveCurrentGroupDown && session.MoveCurrentGroupDown(), "그룹 아래 이동");
        EqualSequence(new[] { groupA.Id, groupB.Id, groupC.Id, groupD.Id },
            session.Workspace.Groups.Select(group => group.Id), "아래 이동 뒤 원래 그룹 순서");
        EqualSequence(rowsBefore, session.Workspace.Groups[1].Rows.Select(row => row.Id), "아래 이동 중 내부 행 순서 보존");
        Qa.Equal(before.Caret, session.Caret, "그룹 아래 이동 caret 보존");
        Qa.Equal(before.Selection, session.Selection, "그룹 아래 이동 selection 보존");
    }

    private static void RejectGroupBoundariesWithoutMutation()
    {
        var onlyText = Qa.Text("alone");
        var single = new EditingSession(Workspace(Group([Row(onlyText)])));
        Qa.Caret(single, onlyText.Id, 5);
        Qa.Check(single.InsertText("!"), "no-op history 준비용 변경 실패");
        single.Undo();
        Qa.Check(single.CanRedo && !single.IsDirty, "single-group no-op 준비 상태에 redo history 없음");
        var singleBefore = Snapshot(single);
        Qa.Check(!single.CanDeleteCurrentGroup && !single.DeleteCurrentGroup(), "마지막 그룹 삭제가 허용됨");
        Qa.Check(!single.CanMoveCurrentGroupUp && !single.MoveCurrentGroupUp(), "유일 그룹 위 이동이 허용됨");
        Qa.Check(!single.CanMoveCurrentGroupDown && !single.MoveCurrentGroupDown(), "유일 그룹 아래 이동이 허용됨");
        EqualSnapshot(singleBefore, Snapshot(single), "유일 그룹 경계 전체 무변경");

        var firstText = Qa.Text("first");
        var middleText = Qa.Text("middle");
        var lastText = Qa.Text("last");
        var groups = new[]
        {
            Group([Row(firstText)], AlignmentMode.None, 10),
            Group([Row(middleText)], AlignmentMode.Equals, 20),
            Group([Row(lastText)], AlignmentMode.None, 30)
        };
        var multi = new EditingSession(Workspace(groups));
        Qa.Caret(multi, middleText.Id, 3);
        Qa.Check(multi.InsertText("!"), "경계 no-op history 준비용 변경 실패");
        multi.Undo();
        Qa.Check(multi.CanRedo, "경계 no-op 준비 상태에 redo history 없음");

        Qa.Caret(multi, firstText.Id, 0);
        var firstBefore = Snapshot(multi);
        Qa.Check(!multi.CanMoveCurrentGroupUp && !multi.MoveCurrentGroupUp(), "첫 그룹 위 이동 경계");
        EqualSnapshot(firstBefore, Snapshot(multi), "첫 그룹 위 이동 무변경");

        Qa.Caret(multi, lastText.Id, 2);
        var lastBefore = Snapshot(multi);
        Qa.Check(!multi.CanMoveCurrentGroupDown && !multi.MoveCurrentGroupDown(), "마지막 그룹 아래 이동 경계");
        EqualSnapshot(lastBefore, Snapshot(multi), "마지막 그룹 아래 이동 무변경");
        Qa.Check(multi.CanMoveCurrentGroupUp && multi.CanDeleteCurrentGroup,
            "다른 그룹의 경계 때문에 활성 그룹 명령 상태가 잘못 계산됨");
    }

    private static async Task SaveAndExportGroups()
    {
        var firstText = Qa.Text("A=1");
        var first = Group([Row(Qa.Sequence(firstText)), Row(Qa.Sequence(Qa.Text("long=2")))], AlignmentMode.Equals, 24);
        var second = Group([Row(Qa.Sequence(Qa.Text("B=3")))], AlignmentMode.None, 11);
        var third = Group([Row(Qa.Sequence(Qa.Text("C=4"))), Row(Qa.Sequence(Qa.Text("D=5")))],
            AlignmentMode.Equals, 18);
        var session = new EditingSession(Workspace(first, second, third));
        var startSnapshot = session.CaptureSnapshot();
        session.MarkSaved(startSnapshot);
        Qa.Caret(session, firstText.Id, 3);
        Qa.Check(session.AddGroupAfterCurrent(), "그룹 출력 사례의 빈 그룹 추가");
        Qa.Equal(first.Alignment, session.Workspace.Groups[1].Alignment, "출력 사례 새 그룹 정렬 상속");
        var incomplete = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics());
        Qa.Check(incomplete.Diagnostics.Any(diagnostic => diagnostic.Code == LayoutDiagnosticCode.EmptySlot),
            "필수 빈 그룹 슬롯의 layout 진단 누락");
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(incomplete),
            "필수 빈 그룹이 SVG 출력에서 거부되지 않음");

        var addedGroupId = session.Workspace.Groups[1].Id;
        Qa.Check(session.InsertText("NEW=6"), "추가 그룹 슬롯 채우기");
        Qa.Check(!((TextNode)session.Workspace.Groups[1].Rows[0].Root).IsEmptySlot,
            "내용 입력 뒤에도 새 그룹 슬롯이 빈 상태로 남음");
        Qa.Check(session.MoveCurrentGroupDown(), "채운 그룹의 아래 이동");
        EqualSequence(new[] { first.Id, second.Id, addedGroupId, third.Id },
            session.Workspace.Groups.Select(group => group.Id), "출력 fixture의 실제 그룹 이동 순서");

        var expectedTexts = new[] { "A=1", "long=2", "B=3", "NEW=6", "C=4", "D=5" };
        var latex = LatexSerializer.ToLatex(session.Workspace);
        var tokens = new[]
        {
            @"{\displaystyle A}&={\displaystyle 1}",
            @"{\displaystyle long}&={\displaystyle 2}",
            @"{\displaystyle B=3}",
            @"{\displaystyle NEW=6}",
            @"{\displaystyle C}&={\displaystyle 4}",
            @"{\displaystyle D}&={\displaystyle 5}"
        };
        var positions = tokens.Select(token => latex.IndexOf(token, StringComparison.Ordinal)).ToArray();
        Qa.Check(positions.All(position => position >= 0) && positions.Zip(positions.Skip(1), (a, b) => b > a).All(increases => increases),
            "LaTeX 그룹/행 순서 또는 등호 정렬 순서 손실: " + latex);
        Qa.Check(latex.Contains("\\qquad", StringComparison.Ordinal), "LaTeX 그룹 간 구분 기호 누락");

        var layout = MathLayoutEngine.Layout(session.Workspace, new AvaloniaMathTextMetrics());
        EqualSequence(session.Workspace.Groups.SelectMany(group => group.Rows).Select(row => row.Id),
            layout.LogicalRows.Select(row => row.RowId), "Layout 논리 행 ID/그룹 순서");
        Qa.Check(layout.LogicalRows.Zip(layout.LogicalRows.Skip(1), (a, b) => b.Baseline > a.Baseline).All(increases => increases),
            "여러 그룹의 논리 행 baseline이 아래 방향으로 증가하지 않음");
        var svg = new SvgExportService().ToSvg(layout);
        var svgRows = XDocument.Parse(svg).Descendants()
            .Where(element => element.Name.LocalName == "text")
            .GroupBy(element => (string?)element.Attribute("y")
                ?? throw new InvalidOperationException("SVG text run에 baseline y가 없습니다."))
            .Select(group => new
            {
                Baseline = double.Parse(group.Key, CultureInfo.InvariantCulture),
                Text = string.Concat(group.Select(element => element.Value))
            })
            .ToArray();
        EqualSequence(expectedTexts, svgRows.Select(row => row.Text),
            "SVG 그룹·논리 행 텍스트 순서 (style-split run은 baseline별 결합)");
        Qa.Check(svgRows.Zip(svgRows.Skip(1), (a, b) => b.Baseline > a.Baseline).All(increases => increases),
            "SVG 그룹 baseline 순서가 Layout 논리 순서와 다름");

        using var directory = new TempDirectory();
        var path = directory.File("group-roundtrip.aethermath");
        var snapshot = session.CaptureSnapshot();
        await new WorkspaceFileService().SaveAsync(path, snapshot);
        session.MarkSaved(snapshot);
        Qa.Check(!session.IsDirty, "그룹 저장 뒤 dirty 상태");
        var reopened = await new WorkspaceFileService().OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(session.Workspace), Qa.Fingerprint(reopened), "그룹 ID·속성·행 순서 저장 재열기");
        EqualSequence(expectedTexts, reopened.Groups.SelectMany(group => group.Rows)
            .Select(row => string.Concat(Qa.Walk(row.Root).OfType<TextNode>().Select(text => text.Text))),
            "재열기 후 그룹/행 텍스트 순서");
        Qa.Equal(1, reopened.SchemaVersion, "그룹 편집이 workspace schema를 변경");
        Qa.Equal(addedGroupId, reopened.Groups[2].Id, "실제 이동한 그룹 ID 저장 재열기");
    }

    private static async Task GroupEditInvalidatesPendingPaste()
    {
        var firstText = Qa.Text("first");
        var moveTarget = Qa.Text("target");
        var lastText = Qa.Text("last");
        var moveSession = new EditingSession(Workspace(
            Group([Row(firstText)], AlignmentMode.None, 12),
            Group([Row(moveTarget)], AlignmentMode.Equals, 25),
            Group([Row(lastText)], AlignmentMode.None, 40)));
        Qa.Caret(moveSession, moveTarget.Id, 3);
        var moveControl = new MathEditorControl { Session = moveSession };
        moveControl.SetVisualTreeAttachmentState(attached: true);
        try
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            moveControl.ClipboardTextReaderOverride = _ =>
            {
                started.TrySetResult(true);
                return read.Task;
            };
            var pending = moveControl.PasteFromClipboardAsync();
            await started.Task.WaitAsync(WaitLimit);
            Qa.Check(moveSession.MoveCurrentGroupDown(), "paste 대기 중 그룹 이동");
            var afterMove = Snapshot(moveSession);
            read.SetResult("STALE");
            Qa.Equal(ClipboardPasteResult.StaleTarget, await pending.WaitAsync(WaitLimit),
                "그룹 이동 중 대기 paste stale 결과");
            EqualSnapshot(afterMove, Snapshot(moveSession), "그룹 이동 뒤 늦은 paste가 상태를 변경");
            Qa.Equal("target", Qa.Leaf(moveSession, moveTarget.Id).Text, "그룹 이동 뒤 늦은 paste가 텍스트에 적용됨");

            moveControl.ClipboardTextReaderOverride = _ => Task.FromResult<string?>("!");
            Qa.Equal(ClipboardPasteResult.Applied, await moveControl.PasteFromClipboardAsync().WaitAsync(WaitLimit),
                "stale group move 뒤 재시도");
            Qa.Equal("tar!get", Qa.Leaf(moveSession, moveTarget.Id).Text, "그룹 이동 뒤 재시도 결과");
        }
        finally
        {
            moveControl.SetVisualTreeAttachmentState(attached: false);
        }

        var deleteTarget = Qa.Text("remove-me");
        var neighborText = Qa.Text("neighbor");
        var deleteSession = new EditingSession(Workspace(
            Group([Row(Qa.Text("before"))], AlignmentMode.None, 11),
            Group([Row(deleteTarget)], AlignmentMode.Equals, 24),
            Group([Row(Qa.Sequence(neighborText))], AlignmentMode.None, 39)));
        Qa.Caret(deleteSession, deleteTarget.Id, 4);
        var deleteControl = new MathEditorControl { Session = deleteSession };
        deleteControl.SetVisualTreeAttachmentState(attached: true);
        try
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            deleteControl.ClipboardTextReaderOverride = _ =>
            {
                started.TrySetResult(true);
                return read.Task;
            };
            var pending = deleteControl.PasteFromClipboardAsync();
            await started.Task.WaitAsync(WaitLimit);
            Qa.Check(deleteSession.DeleteCurrentGroup(), "paste 대기 중 활성 그룹 삭제");
            var afterDelete = Snapshot(deleteSession);
            read.SetResult("STALE");
            Qa.Equal(ClipboardPasteResult.StaleTarget, await pending.WaitAsync(WaitLimit),
                "그룹 삭제 중 대기 paste stale 결과");
            EqualSnapshot(afterDelete, Snapshot(deleteSession), "그룹 삭제 뒤 늦은 paste가 상태를 변경");
            Qa.Check(!Qa.Nodes(deleteSession.Workspace).OfType<TextNode>().Any(text => text.Id == deleteTarget.Id),
                "삭제 그룹 target이 workspace에 남음");
            Qa.Equal(new MathCaret(neighborText.Id, 0), deleteSession.Caret, "그룹 삭제 뒤 neighbor caret");

            deleteControl.ClipboardTextReaderOverride = _ => Task.FromResult<string?>("!");
            Qa.Equal(ClipboardPasteResult.Applied, await deleteControl.PasteFromClipboardAsync().WaitAsync(WaitLimit),
                "stale group delete 뒤 재시도");
            Qa.Equal("!neighbor", Qa.Leaf(deleteSession, neighborText.Id).Text, "그룹 삭제 뒤 재시도 결과");
        }
        finally
        {
            deleteControl.SetVisualTreeAttachmentState(attached: false);
        }
    }

    private static (EquationGroup Group, TextNode CaretText, TextNode UnicodeText, MatrixNode Matrix) CreateDeepGroup()
    {
        var caretText = Qa.Text("caret🙂row");
        var unicode = Qa.Text("e\u0301🙂𝑥");
        var empty = Qa.Text(string.Empty, isEmptySlot: true);
        var matrix = new MatrixNode(Guid.NewGuid(), 2, 2,
        [
            Qa.Text("α"), empty,
            unicode, Qa.Text("4")
        ]);
        var script = new ScriptNode(Guid.NewGuid(),
            new RadicalNode(Guid.NewGuid(), new FractionNode(Guid.NewGuid(),
                new SequenceNode(Guid.NewGuid(), [Qa.Text("a"), Qa.Text("b")]), Qa.Text("c"))),
            Qa.Text("i"), new SequenceNode(Guid.NewGuid(), [Qa.Text("n"), matrix]));
        var fraction = new FractionNode(Guid.NewGuid(),
            new SequenceNode(Guid.NewGuid(), [Qa.Text("prefix"), script]),
            new RadicalNode(Guid.NewGuid(), new SequenceNode(Guid.NewGuid(), [Qa.Text("tail"), Qa.Text("🙂")])));
        var group = Group(
            [Row(new SequenceNode(Guid.NewGuid(), [caretText, Qa.Text("-row0")])),
             Row(new SequenceNode(Guid.NewGuid(), [Qa.Text("f="), fraction]))],
            AlignmentMode.Equals, 32.75);
        return (group, caretText, unicode, matrix);
    }

    private static EquationRow Row(MathNode root) => new(Guid.NewGuid(), root);

    private static EquationGroup Group(EquationRow[] rows,
        AlignmentMode alignment = AlignmentMode.Equals, double rowSpacing = 26) =>
        new(Guid.NewGuid(), [.. rows], alignment, rowSpacing);

    private static EquationWorkspace Workspace(params EquationGroup[] groups) => new(
        Guid.NewGuid(), 1, "그룹 편집 QA 𝑥 e\u0301 한글🙂", [.. groups], new MathStyle(31, "Cambria Math", "#24364B"));

    private static EditSnapshot Snapshot(EditingSession session) => new(
        Qa.Fingerprint(session.Workspace), session.Revision, session.Caret, session.Selection,
        session.SelectedNodeId, session.IsNodeSelected, session.ActiveGroupNumber, session.ActiveRowNumber,
        session.GroupCount, session.CanUndo, session.CanRedo, session.IsDirty, session.Workspace.Style);

    private static void EqualSnapshot(EditSnapshot expected, EditSnapshot actual, string label) =>
        Qa.Equal(expected, actual, label);

    private static Dictionary<Guid, Guid> MapCloneGroup(EquationGroup original, EquationGroup clone)
    {
        Qa.Check(original.Id != clone.Id, "복제 그룹 ID 충돌");
        Qa.Equal(original.Rows.Length, clone.Rows.Length, "복제 그룹 row 수");
        Qa.Equal(original.Alignment, clone.Alignment, "복제 그룹 alignment 보존");
        Qa.Equal(original.RowSpacing, clone.RowSpacing, "복제 그룹 spacing 보존");
        var map = new Dictionary<Guid, Guid>();
        for (var index = 0; index < original.Rows.Length; index++)
        {
            Qa.Check(original.Rows[index].Id != clone.Rows[index].Id, "복제 row ID 충돌");
            AddNodePairs(original.Rows[index].Root, clone.Rows[index].Root, map);
        }
        return map;
    }

    private static void AssertCloneGroup(EquationGroup original, EquationGroup clone,
        IReadOnlyDictionary<Guid, Guid> map, string label)
    {
        Qa.Equal(original.Rows.Length, clone.Rows.Length, label + " row 개수");
        Qa.Equal(original.Alignment, clone.Alignment, label + " 정렬");
        Qa.Equal(original.RowSpacing, clone.RowSpacing, label + " 행 간격");
        for (var index = 0; index < original.Rows.Length; index++)
        {
            Qa.Equal(original.Rows[index].Root.GetType(), clone.Rows[index].Root.GetType(), label + " root 종류");
            var sourceIds = Qa.Walk(original.Rows[index].Root).Select(node => node.Id).ToHashSet();
            var clonedIds = Qa.Walk(clone.Rows[index].Root).Select(node => node.Id).ToHashSet();
            Qa.Equal(sourceIds.Count, clonedIds.Count, label + " row subtree node 수");
            Qa.Check(sourceIds.All(id => map.ContainsKey(id)), label + " group-wide ID map 누락");
            Qa.Check(!sourceIds.Overlaps(clonedIds), label + " 원본 node ID 재사용");
        }
    }

    private static void AddNodePairs(MathNode original, MathNode clone, Dictionary<Guid, Guid> map)
    {
        Qa.Equal(original.GetType(), clone.GetType(), "복제 node 종류");
        Qa.Check(original.Id != clone.Id, "복제 node ID 충돌");
        map.Add(original.Id, clone.Id);
        switch (original, clone)
        {
            case (TextNode oldText, TextNode newText):
                Qa.Equal(oldText.Text, newText.Text, "복제 text/Unicode 의미");
                Qa.Equal(oldText.IsEmptySlot, newText.IsEmptySlot, "복제 필수 빈 슬롯 flag");
                break;
            case (SequenceNode oldSequence, SequenceNode newSequence):
                Qa.Equal(oldSequence.Children.Length, newSequence.Children.Length, "복제 sequence child count");
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
                Qa.Equal(oldScript.Subscript is null, newScript.Subscript is null, "복제 subscript presence");
                Qa.Equal(oldScript.Superscript is null, newScript.Superscript is null, "복제 superscript presence");
                AddNodePairs(oldScript.Base, newScript.Base, map);
                if (oldScript.Subscript is not null) AddNodePairs(oldScript.Subscript, newScript.Subscript!, map);
                if (oldScript.Superscript is not null) AddNodePairs(oldScript.Superscript, newScript.Superscript!, map);
                break;
            case (MatrixNode oldMatrix, MatrixNode newMatrix):
                Qa.Equal(oldMatrix.Rows, newMatrix.Rows, "복제 matrix rows");
                Qa.Equal(oldMatrix.Columns, newMatrix.Columns, "복제 matrix columns");
                Qa.Equal(oldMatrix.Cells.Length, newMatrix.Cells.Length, "복제 matrix cells");
                for (var i = 0; i < oldMatrix.Cells.Length; i++)
                    AddNodePairs(oldMatrix.Cells[i], newMatrix.Cells[i], map);
                break;
            case (IntegralNode oldIntegral, IntegralNode newIntegral):
                Qa.Equal(oldIntegral.LowerLimit is null, newIntegral.LowerLimit is null, "복제 integral 하한 presence");
                Qa.Equal(oldIntegral.UpperLimit is null, newIntegral.UpperLimit is null, "복제 integral 상한 presence");
                AddNodePairs(oldIntegral.Integrand, newIntegral.Integrand, map);
                if (oldIntegral.LowerLimit is not null) AddNodePairs(oldIntegral.LowerLimit, newIntegral.LowerLimit!, map);
                if (oldIntegral.UpperLimit is not null) AddNodePairs(oldIntegral.UpperLimit, newIntegral.UpperLimit!, map);
                AddNodePairs(oldIntegral.DifferentialVariable, newIntegral.DifferentialVariable, map);
                break;
            case (SummationNode oldSum, SummationNode newSum):
                Qa.Equal(oldSum.LowerLimit is null, newSum.LowerLimit is null, "복제 sum 하한 presence");
                Qa.Equal(oldSum.UpperLimit is null, newSum.UpperLimit is null, "복제 sum 상한 presence");
                AddNodePairs(oldSum.Body, newSum.Body, map);
                if (oldSum.LowerLimit is not null) AddNodePairs(oldSum.LowerLimit, newSum.LowerLimit!, map);
                if (oldSum.UpperLimit is not null) AddNodePairs(oldSum.UpperLimit, newSum.UpperLimit!, map);
                break;
            case (LimitNode oldLimit, LimitNode newLimit):
                AddNodePairs(oldLimit.Approach, newLimit.Approach, map);
                AddNodePairs(oldLimit.Body, newLimit.Body, map);
                break;
            case (BracketsNode oldBrackets, BracketsNode newBrackets):
                Qa.Equal(oldBrackets.Kind, newBrackets.Kind, "복제 bracket kind");
                AddNodePairs(oldBrackets.Content, newBrackets.Content, map);
                break;
            case (CasesNode oldCases, CasesNode newCases):
                Qa.Equal(oldCases.Rows.Length, newCases.Rows.Length, "복제 cases 행 수");
                for (var i = 0; i < oldCases.Rows.Length; i++)
                {
                    AddNodePairs(oldCases.Rows[i].Expression, newCases.Rows[i].Expression, map);
                    AddNodePairs(oldCases.Rows[i].Condition, newCases.Rows[i].Condition, map);
                }
                break;
            default:
                throw new InvalidOperationException("지원하지 않는 복제 node 조합");
        }
    }

    private static void AssertGlobalIdsUnique(EquationWorkspace workspace, string label)
    {
        var ids = new List<Guid> { workspace.Id };
        foreach (var group in workspace.Groups)
        {
            ids.Add(group.Id);
            foreach (var row in group.Rows)
            {
                ids.Add(row.Id);
                ids.AddRange(Qa.Walk(row.Root).Select(node => node.Id));
            }
        }
        Qa.Equal(ids.Count, ids.Distinct().Count(), label);
    }

    private static void AssertUndone(EditSnapshot before, EditSnapshot afterOperation,
        EditSnapshot actual, string label)
    {
        EqualRestoredState(before, actual, label);
        Qa.Equal(before.Revision + 1, afterOperation.Revision, label + " 성공 동작은 단일 Commit");
        Qa.Equal(afterOperation.Revision + 1, actual.Revision, label + " revision 증가");
        Qa.Equal(before.CanUndo, actual.CanUndo, label + " undo history 복원");
        Qa.Check(actual.CanRedo, label + " redo history가 없음");
    }

    private static void AssertRedone(EditSnapshot afterOperation, EditSnapshot actual, string label)
    {
        EqualRestoredState(afterOperation, actual, label);
        Qa.Equal(afterOperation.Revision + 2, actual.Revision, label + " undo/redo revision 증가");
        Qa.Check(actual.CanUndo, label + " undo history가 없음");
        Qa.Check(!actual.CanRedo, label + " redo history가 남음");
    }

    private static void EqualRestoredState(EditSnapshot expected, EditSnapshot actual, string label)
    {
        Qa.Equal(expected.Workspace, actual.Workspace, label + " workspace");
        Qa.Equal(expected.Caret, actual.Caret, label + " caret");
        Qa.Equal(expected.Selection, actual.Selection, label + " selection");
        Qa.Equal(expected.SelectedNodeId, actual.SelectedNodeId, label + " selected node ID");
        Qa.Equal(expected.IsNodeSelected, actual.IsNodeSelected, label + " whole-node flag");
        Qa.Equal(expected.ActiveGroupNumber, actual.ActiveGroupNumber, label + " active group");
        Qa.Equal(expected.ActiveRowNumber, actual.ActiveRowNumber, label + " active row");
        Qa.Equal(expected.GroupCount, actual.GroupCount, label + " group count");
        Qa.Equal(expected.IsDirty, actual.IsDirty, label + " dirty state");
        Qa.Equal(expected.WorkspaceStyle, actual.WorkspaceStyle, label + " workspace style");
    }

    private static void EqualSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();
        Qa.Check(expectedArray.SequenceEqual(actualArray),
            $"{label}: 기대=[{string.Join(",", expectedArray)}]; 실제=[{string.Join(",", actualArray)}]");
    }

    private sealed record EditSnapshot(string Workspace, long Revision, MathCaret Caret, MathSelection? Selection,
        Guid? SelectedNodeId, bool IsNodeSelected, int ActiveGroupNumber, int ActiveRowNumber,
        int GroupCount, bool CanUndo, bool CanRedo, bool IsDirty, MathStyle WorkspaceStyle);
}
