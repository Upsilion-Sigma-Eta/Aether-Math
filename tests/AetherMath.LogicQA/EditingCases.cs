using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class EditingCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("SEL-01", "실제 명령으로 2단 분수 삽입 후 내부 분모 부분 교체·undo", NestedInsertion);
        yield return new("SEL-02", "근호·밑/위/아래첨자·행렬 모든 슬롯 독립 교체", SlotIsolation);
        yield return new("SEL-03", "정방향/역방향 선택의 동일한 교체", SelectionDirection);
        yield return new("SEL-INVALID", "잘못된 ID/오프셋/교차 슬롯 선택의 무변경 거절", InvalidSelection);
        yield return new("DEL-01", "슬롯 첫 Backspace/끝 Delete 안전 경계", DeleteBoundaries);
        yield return new("DEL-02", "구조 직전 Delete/직후 Backspace는 중첩 내용을 보존", AdjacentStructureBoundary);
        yield return new("DEL-03", "명시선택 subtree 삭제·이웃/부모 보존·undo 선택 복구", DeleteStructure);
        yield return new("UNI-01", "보조평면·결합·ZWJ·한글 텍스트 요소 삭제/undo", UnicodeElements);
        yield return new("UNI-04", "확정 한글 문자열 한 번 입력은 undo 한 단계", CommittedText);
        yield return new("UND-01", "입력 undo/redo의 모델·커서·선택·dirty 복원", UndoRestore);
        yield return new("UND-02", "undo 뒤 새 입력은 redo 분기를 폐기", RedoBranch);
        yield return new("UND-03", "선택과 빈 입력/경계 삭제는 이력을 만들지 않음", NoOpHistory);
        yield return new("UND-04", "저장 state 복귀와 지연 저장 snapshot dirty", SavedState);
        yield return new("TPL-01", "네 구조 템플릿의 감싸기/빈 슬롯/ID 고유성", Templates);
        yield return new("DEL-TEXT", "부분선택 명시 삭제와 undo 선택 복구", DeleteSelectedText);
        yield return new("LATEX-NESTED-SCRIPT", "중첩 첨자 밑은 독립 내부식 전체를 묶어 합성 의미 보존", NestedScriptLatex);
    }

    private static void NestedInsertion()
    {
        var original = Qa.Text("left+right");
        var session = new EditingSession(Qa.Workspace(original));
        Qa.Select(session, original.Id, 0, 4);
        var outerId = session.InsertFraction();
        Qa.Check(outerId.HasValue, "바깥 분수 삽입 실패");
        var outer = (FractionNode)Qa.Node(session, outerId!.Value);
        var outerDenominator = Qa.Walk(outer.Denominator).OfType<TextNode>().Single();
        Qa.Caret(session, outerDenominator.Id, 0);
        Qa.Check(session.InsertText("abcde"), "분모 입력 실패");
        Qa.Select(session, outerDenominator.Id, 1, 4);
        var innerId = session.InsertFraction();
        Qa.Check(innerId.HasValue, "안쪽 분수 삽입 실패");
        var inner = (FractionNode)Qa.Node(session, innerId!.Value);
        Qa.Check(Qa.Walk(((FractionNode)Qa.Node(session, outerId.Value)).Denominator).Any(n => n.Id == innerId), "실제 2단 중첩 없음");
        var leaf = Qa.Walk(inner.Denominator).OfType<TextNode>().Single();
        Qa.Caret(session, leaf.Id, 0);
        session.InsertText("UVWXY");
        var before = session.Workspace;
        Qa.Select(session, leaf.Id, 1, 4);
        var selection = session.Selection;
        var caret = session.Caret;
        Qa.Check(session.InsertText("𝑥"), "안쪽 분모 부분 입력 실패");
        Qa.OnlyTextChanged(before, session.Workspace, leaf.Id, "U𝑥Y");
        var changed = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(Qa.Fingerprint(before), Qa.Fingerprint(session.Workspace), "중첩 편집 undo 구조/ID");
        Qa.Equal(caret, session.Caret, "중첩 undo caret");
        Qa.Equal(selection, session.Selection, "중첩 undo selection");
        session.Redo();
        Qa.Equal(changed, Qa.Fingerprint(session.Workspace), "중첩 redo");
    }

    private static void SlotIsolation()
    {
        var leaves = Enumerable.Range(0, 9).Select(i => Qa.Text("abc" + i)).ToArray();
        var root = Qa.Sequence(new RadicalNode(Guid.NewGuid(), leaves[0]),
            new ScriptNode(Guid.NewGuid(), leaves[1], leaves[2], leaves[3]),
            new MatrixNode(Guid.NewGuid(), 2, 2, [leaves[4], leaves[5], leaves[6], leaves[7]]), leaves[8]);
        var workspace = Qa.Workspace(root);
        foreach (var leaf in leaves.Take(8))
        {
            var session = new EditingSession(workspace);
            Qa.Select(session, leaf.Id, 1, 3);
            Qa.Check(session.InsertText("한"), "슬롯 교체 실패");
            Qa.OnlyTextChanged(workspace, session.Workspace, leaf.Id, "a한" + leaf.Text[^1]);
        }
    }

    private static void SelectionDirection()
    {
        var leaf = Qa.Text("Ae\u0301𝑥한Z");
        var workspace = Qa.Workspace(leaf);
        var a = new EditingSession(workspace);
        var b = new EditingSession(workspace);
        Qa.Select(a, leaf.Id, 1, 4);
        Qa.Select(b, leaf.Id, 4, 1);
        a.InsertText("🙂"); b.InsertText("🙂");
        Qa.Equal("A🙂Z", Qa.Leaf(a, leaf.Id).Text, "선택 치환 결과");
        Qa.Equal(Qa.Fingerprint(a.Workspace), Qa.Fingerprint(b.Workspace), "역방향 선택 내용");
        Qa.Equal(new MathCaret(leaf.Id, 2), a.Caret, "치환 caret");
        Qa.Equal(a.Caret, b.Caret, "역방향 선택 caret");
    }

    private static void InvalidSelection()
    {
        var a = Qa.Text("𝑥"); var b = Qa.Text("e\u0301");
        var session = new EditingSession(Qa.Workspace(Qa.Sequence(a, b)));
        var before = Qa.Fingerprint(session.Workspace);
        var caret = session.Caret;
        var revision = session.Revision;
        foreach (var invalid in new[] { new MathCaret(Guid.NewGuid(), 0), new MathCaret(a.Id, -1), new MathCaret(a.Id, 2) })
            Qa.Check(!session.TrySetCaret(invalid), "잘못된 caret 수락: " + invalid);
        Qa.Check(!session.TrySetSelection(new(new(a.Id, 0), new(b.Id, 1))), "교차 슬롯 선택을 조용히 수락함");
        Qa.Check(!session.TrySetSelection(new(new(a.Id, 0), new(a.Id, 2))), "문자소 밖 선택 수락");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "거절 후 모델");
        Qa.Equal(caret, session.Caret, "거절 후 caret");
        Qa.Equal(revision, session.Revision, "거절 revision");
        Qa.Check(!session.IsDirty && !session.CanUndo, "거절이 dirty/history를 생성함");
    }

    private static void DeleteBoundaries()
    {
        var session = EditingSession.CreateDefault();
        var before = Qa.Fingerprint(session.Workspace);
        foreach (var leaf in Qa.Nodes(session.Workspace).OfType<TextNode>().ToArray())
        {
            Qa.Caret(session, leaf.Id, 0);
            session.Backspace();
            Qa.Equal(before, Qa.Fingerprint(session.Workspace), "슬롯 첫 Backspace의 구조 보존");
            Qa.Caret(session, leaf.Id, Qa.Elements(leaf.Text));
            session.DeleteForward();
            Qa.Equal(before, Qa.Fingerprint(session.Workspace), "슬롯 끝 Delete의 구조 보존");
        }
        Qa.Check(!session.CanUndo && !session.IsDirty, "경계 삭제가 dirty/history를 생성함");
    }

    private static void AdjacentStructureBoundary()
    {
        var left = Qa.Text("L"); var right = Qa.Text("R");
        var nested = new FractionNode(Guid.NewGuid(), Qa.Text("top"),
            new FractionNode(Guid.NewGuid(), Qa.Text("inner top"), Qa.Text("inner bottom")));
        var session = new EditingSession(Qa.Workspace(Qa.Sequence(left, nested, right)));
        var before = Qa.Fingerprint(session.Workspace); var revision = session.Revision;
        Qa.Caret(session, left.Id, 1);
        Qa.Check(!session.DeleteForward(), "구조 바로 앞 Delete는 no-op이어야 함");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "구조 앞 Delete 보존");
        Qa.Caret(session, right.Id, 0);
        Qa.Check(!session.Backspace(), "구조 바로 뒤 Backspace는 no-op이어야 함");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "구조 뒤 Backspace 보존");
        Qa.Equal(revision, session.Revision, "구조 경계 revision");
        Qa.Check(!session.IsDirty && !session.CanUndo, "구조 경계 dirty/history");
    }

    private static void DeleteStructure()
    {
        var inner = new FractionNode(Guid.NewGuid(), Qa.Text("inner top"),
            new RadicalNode(Guid.NewGuid(), Qa.Text("inner bottom")));
        var outer = new FractionNode(Guid.NewGuid(), Qa.Text("outer top"), inner);
        var left = Qa.Text("L"); var right = Qa.Text("R");
        var original = Qa.Workspace(Qa.Sequence(left, outer, right));
        var session = new EditingSession(original);
        Qa.Check(session.TrySelectNode(inner.Id), "구조 명시선택 실패");
        Qa.Equal<Guid?>(inner.Id, session.SelectedNodeId, "전체 구조 선택 ID");
        var caret = session.Caret; var selection = session.Selection;
        Qa.Check(session.DeleteSelection(), "명시선택 구조 삭제 실패");
        var changedOuter = (FractionNode)Qa.Node(session, outer.Id);
        Qa.Equal(Qa.NodeFingerprint(outer.Numerator), Qa.NodeFingerprint(changedOuter.Numerator), "바깥 분자 보존");
        Qa.Equal(Qa.NodeFingerprint(left), Qa.NodeFingerprint(Qa.Node(session, left.Id)), "왼쪽 이웃 보존");
        Qa.Equal(Qa.NodeFingerprint(right), Qa.NodeFingerprint(Qa.Node(session, right.Id)), "오른쪽 이웃 보존");
        Qa.Check(changedOuter.Denominator is TextNode { Text.Length: 0 }, "삭제한 subtree 슬롯이 빈 텍스트로 보존되지 않음");
        var deletedIds = Qa.Walk(inner).Select(n => n.Id).ToHashSet();
        Qa.Check(!Qa.Nodes(session.Workspace).Any(n => deletedIds.Contains(n.Id)), "삭제 subtree 잔존");
        Qa.Check(!Qa.Nodes(original).Any(n => n.Id == changedOuter.Denominator.Id), "삭제 대체 슬롯은 새 ID여야 함");
        Qa.Equal(new MathCaret(changedOuter.Denominator.Id, 0), session.Caret, "삭제 후 caret");
        var changed = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(Qa.Fingerprint(original), Qa.Fingerprint(session.Workspace), "구조 삭제 undo 전체 ID/내용");
        Qa.Equal<Guid?>(inner.Id, session.SelectedNodeId, "undo 전체 노드 선택 복구");
        Qa.Equal(caret, session.Caret, "undo 구조 caret"); Qa.Equal(selection, session.Selection, "undo 구조 text 선택 상태");
        session.Redo(); Qa.Equal(changed, Qa.Fingerprint(session.Workspace), "구조 삭제 redo 새 슬롯 ID");
    }

    private static void UnicodeElements()
    {
        string[] elements = ["𝑥", "🙂", "e\u0301", "한", "한", "👨‍👩‍👧‍👦", "✈️", "👍🏽"];
        foreach (var element in elements)
        {
            Qa.Equal(1, Qa.Elements(element), "시험 문자소 수");
            foreach (bool backspace in new[] { true, false })
            {
                var leaf = Qa.Text("L" + element + "R");
                var session = new EditingSession(Qa.Workspace(leaf));
                Qa.Caret(session, leaf.Id, backspace ? 2 : 1);
                Qa.Check(backspace ? session.Backspace() : session.DeleteForward(), "문자소 삭제 실패");
                Qa.Equal("LR", Qa.Leaf(session, leaf.Id).Text, "문자소 원자적 삭제: " + element);
                Qa.Equal(new MathCaret(leaf.Id, 1), session.Caret, "문자소 삭제 caret");
                session.Undo();
                Qa.Equal(leaf.Text, Qa.Leaf(session, leaf.Id).Text, "Unicode undo 원문");
                session.Redo();
                Qa.Equal("LR", Qa.Leaf(session, leaf.Id).Text, "Unicode redo");
            }
        }
        var separate = Qa.Text("eZ");
        var combining = new EditingSession(Qa.Workspace(separate));
        Qa.Caret(combining, separate.Id, 1);
        combining.InsertText("\u0301");
        Qa.Equal("e\u0301Z", Qa.Leaf(combining, separate.Id).Text, "별도 결합문자 원문 보존");
        Qa.Equal(new MathCaret(separate.Id, 1), combining.Caret, "기존 문자와 결합한 뒤 caret");
        combining.Backspace();
        Qa.Equal("Z", Qa.Leaf(combining, separate.Id).Text, "결합 뒤 첫 삭제가 전체 문자소 제거");
    }

    private static void CommittedText()
    {
        var leaf = Qa.Text("");
        var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Caret(session, leaf.Id, 0);
        session.InsertText("한글 조건 𝑥 ≥ 0");
        Qa.Equal("한글 조건 𝑥 ≥ 0", Qa.Leaf(session, leaf.Id).Text, "확정 문자열");
        session.Undo();
        Qa.Equal("", Qa.Leaf(session, leaf.Id).Text, "한 번 undo");
        Qa.Check(!session.CanUndo, "한 입력이 여러 트랜잭션으로 쪼개짐");
    }

    private static void UndoRestore()
    {
        var leaf = Qa.Text("abcd");
        var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Select(session, leaf.Id, 1, 3);
        var selection = session.Selection; var caret = session.Caret;
        var original = Qa.Fingerprint(session.Workspace);
        session.InsertText("X");
        var changed = Qa.Fingerprint(session.Workspace); var afterCaret = session.Caret;
        Qa.Check(session.IsDirty && session.CanUndo, "편집 history/dirty 없음");
        session.Undo();
        Qa.Equal(original, Qa.Fingerprint(session.Workspace), "undo 모델");
        Qa.Equal(caret, session.Caret, "undo caret"); Qa.Equal(selection, session.Selection, "undo selection");
        Qa.Check(!session.IsDirty && session.CanRedo, "초기 저장 state undo dirty");
        session.Redo();
        Qa.Equal(changed, Qa.Fingerprint(session.Workspace), "redo 모델");
        Qa.Equal(afterCaret, session.Caret, "redo caret");
        Qa.Check(session.Selection is null && session.IsDirty, "redo 편집 상태");
    }

    private static void RedoBranch()
    {
        var leaf = Qa.Text("a"); var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Caret(session, leaf.Id, 1); session.InsertText("b"); session.InsertText("c");
        session.Undo(); session.InsertText("d");
        Qa.Check(!session.CanRedo, "새 편집 후 redo 잔존");
        session.Redo(); Qa.Equal("abd", Qa.Leaf(session, leaf.Id).Text, "분기 뒤 redo 무변경");
    }

    private static void NoOpHistory()
    {
        var leaf = Qa.Text("abc"); var session = new EditingSession(Qa.Workspace(leaf));
        var revision = session.Revision;
        Qa.Caret(session, leaf.Id, 0); session.Backspace();
        Qa.Caret(session, leaf.Id, 3); session.DeleteForward();
        Qa.Select(session, leaf.Id, 1, 2);
        Qa.Caret(session, leaf.Id, 1);
        session.InsertText("");
        Qa.Equal("abc", Qa.Leaf(session, leaf.Id).Text, "빈 입력");
        Qa.Equal(revision, session.Revision, "무변경 revision");
        Qa.Check(!session.IsDirty && !session.CanUndo, "무변경 이력");
    }

    private static void SavedState()
    {
        var leaf = Qa.Text("a"); var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Caret(session, leaf.Id, 1); session.InsertText("b");
        var snapshot = session.CaptureSnapshot(); session.MarkSaved(snapshot);
        Qa.Check(!session.IsDirty, "성공 저장 dirty");
        session.InsertText("c"); Qa.Check(session.IsDirty, "저장 뒤 입력 dirty");
        session.Undo(); Qa.Check(!session.IsDirty, "저장 state로 undo dirty 해제");
        session.Redo(); Qa.Check(session.IsDirty, "redo dirty");
        session.MarkSaved(snapshot);
        Qa.Check(session.IsDirty, "이전 snapshot 저장 완료가 새 편집을 저장 처리함");
        session.Undo(); Qa.Check(!session.IsDirty, "지연 저장 snapshot으로 undo");
    }

    private static void Templates()
    {
        foreach (var kind in new[] { "fraction", "root", "matrix", "script" })
        {
            var leaf = Qa.Text("ABCDE"); var session = new EditingSession(Qa.Workspace(leaf));
            Qa.Select(session, leaf.Id, 1, 4);
            var before = Qa.Fingerprint(session.Workspace); var selection = session.Selection;
            Guid? id = kind switch
            {
                "fraction" => session.InsertFraction(), "root" => session.InsertRoot(),
                "matrix" => session.InsertMatrix(), _ => session.InsertSuperscript()
            };
            Qa.Check(id.HasValue, kind + " 삽입 실패");
            var inserted = Qa.Node(session, id!.Value);
            var texts = Qa.Walk(inserted).OfType<TextNode>().ToArray();
            Qa.Check(texts.Any(n => n.Text == "BCD"), kind + " 선택 텍스트 손실");
            Qa.Equal("ABCDE", string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(n => n.Text)), kind + " 감싸기 앞/뒤 보존");
            var ids = Qa.Nodes(session.Workspace).Select(n => n.Id).ToArray();
            Qa.Equal(ids.Length, ids.Distinct().Count(), kind + " 노드 ID 중복");
            Qa.Check(Qa.Walk(inserted).Any(n => n.Id == session.Caret.NodeId), kind + " 새 슬롯 caret");
            if (inserted is MatrixNode matrix) { Qa.Equal(2, matrix.Rows, "행렬 행"); Qa.Equal(2, matrix.Columns, "행렬 열"); Qa.Equal(4, matrix.Cells.Length, "행렬 셀"); }
            if (inserted is FractionNode) Qa.Check(texts.Any(n => n.Text == ""), "분모 빈 슬롯 없음");
            session.Undo(); Qa.Equal(before, Qa.Fingerprint(session.Workspace), kind + " undo");
            Qa.Equal(selection, session.Selection, kind + " undo 선택");
            session.Redo(); Qa.Check(Qa.Nodes(session.Workspace).Any(n => n.Id == id), kind + " redo 안정 ID");
        }
    }

    private static void DeleteSelectedText()
    {
        var leaf = Qa.Text("abcde"); var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Select(session, leaf.Id, 4, 1); var selection = session.Selection;
        session.DeleteSelection(); Qa.Equal("ae", Qa.Leaf(session, leaf.Id).Text, "선택 삭제");
        Qa.Equal(new MathCaret(leaf.Id, 1), session.Caret, "선택 삭제 caret");
        session.Undo(); Qa.Equal(leaf.Text, Qa.Leaf(session, leaf.Id).Text, "삭제 undo");
        Qa.Equal(selection, session.Selection, "삭제 undo 선택");
    }

    private static void NestedScriptLatex()
    {
        var inner = new ScriptNode(Guid.NewGuid(), Qa.Text("x"), null, Qa.Text("2"));
        var power = Qa.Text("3");
        var outer = new ScriptNode(Guid.NewGuid(), inner, null, power);
        var actual = LatexSerializer.ToLatex(Qa.Workspace(outer));
        Qa.Equal(@"{\displaystyle {{x}^{{\scriptstyle 2}}}^{{\scriptstyle 3}}}", actual,
            "중첩 첨자에서 inner base와 Script 지수 그룹이 실제 문맥에 맞게 분리됨");
    }
}
