using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class TemplateEditingCases
{
    private static readonly MathTemplateKind[] TemplateKinds =
    [
        MathTemplateKind.Fraction, MathTemplateKind.Radical,
        MathTemplateKind.Superscript, MathTemplateKind.Subscript,
        MathTemplateKind.Integral, MathTemplateKind.Summation,
        MathTemplateKind.Limit, MathTemplateKind.Matrix,
        MathTemplateKind.Brackets, MathTemplateKind.Cases
    ];

    public static IEnumerable<QaCase> All()
    {
        yield return new("TPL-CATALOG", "공개 10종 템플릿 API와 기존 네 삽입 명령 호환", CatalogAndLegacyWrappers);
        yield return new("TPL-SHAPES", "새 여섯 구조의 초기 모양·필수 빈 슬롯·caret", NewTemplateShapes);
        yield return new("TPL-WRAP-TEXT", "정방향·역방향 Unicode 부분선택 감싸기와 주변 텍스트 보존", WrapPartialText);
        yield return new("TPL-WRAP-NODE", "전체 구조 선택 감싸기·선택 대상/undo 매핑", WrapSelectedStructure);
        yield return new("TPL-BOUNDS", "적분·합 optional 상하한과 필수 내용의 구분", OptionalBounds);
        yield return new("TPL-BRACKETS-CASES", "괄호 네 종류와 두 식/조건 행의 독립 편집", BracketsAndCases);
        yield return new("TPL-NAV", "중첩 10종 MathTree 자식 순서·문자소 caret 경계", NestedTreeOrderAndGraphemes);
        yield return new("TPL-HISTORY", "새 노드 깊은 복제 ID·selection·delete·undo/redo", CloneDeleteAndHistory);
        yield return new("TPL-JSON", "구버전 노드 JSON 읽기와 10종 신규 AST 저장 재열기", JsonCompatibilityAndRoundTrip);
        yield return new("TPL-JSON-REJECT", "새 노드의 null·빈 행·잘못된 enum·중복 ID 거절과 원본 보존", RejectInvalidJson);
        yield return new("TPL-LATEX", "10종 LaTeX 구조 의미와 null 상하한 생략", LatexSemantics);
        yield return new("TPL-LAYOUT-SVG", "새 구조 bounds·glyph·같은 LayoutResult SVG 및 미완성 출력 거절", LayoutAndSvgParity);
    }

    private static void CatalogAndLegacyWrappers()
    {
        var values = Enum.GetValues<MathTemplateKind>();
        Qa.Equal(10, values.Length, "공개 템플릿 종류 개수");
        Qa.Equal(10, values.Distinct().Count(), "공개 템플릿 종류 중복");
        Qa.Check(TemplateKinds.ToHashSet().SetEquals(values), "공개 enum 값이 계획의 10종과 다름");

        foreach (var kind in TemplateKinds)
        {
            var session = BlankSession();
            var id = session.InsertTemplate(kind);
            Qa.Check(id.HasValue, kind + " 공용 삽입 API 결과 ID 없음");
            var node = Qa.Node(session, id!.Value);
            Qa.Equal(ExpectedNodeType(kind), node.GetType(), kind + " 생성 노드 종류");
            Qa.Check(Qa.Walk(node).Any(n => n.Id == session.Caret.NodeId), kind + " 새 템플릿 caret 대상 누락");
            Qa.Check(session.IsDirty && session.CanUndo, kind + " 삽입 단일 편집 history 누락");
            _ = new EditingSession(session.Workspace); // 모든 새 구조를 공용 workspace validator가 수용하는지 확인
        }

        var wrappers = new (string Name, Func<EditingSession, Guid?> Call, Type Expected)[]
        {
            ("InsertFraction", session => session.InsertFraction(), typeof(FractionNode)),
            ("InsertRoot", session => session.InsertRoot(), typeof(RadicalNode)),
            ("InsertSuperscript", session => session.InsertSuperscript(), typeof(ScriptNode)),
            ("InsertMatrix", session => session.InsertMatrix(), typeof(MatrixNode))
        };
        foreach (var (name, call, expected) in wrappers)
        {
            var session = BlankSession();
            var id = call(session);
            Qa.Check(id.HasValue, name + " 기존 호환 명령이 결과 ID를 반환하지 않음");
            Qa.Equal(expected, Qa.Node(session, id!.Value).GetType(), name + " 기존 생성 의미 변경");
        }

        var invalid = Qa.Throws<ArgumentOutOfRangeException>(
            () => BlankSession().InsertTemplate((MathTemplateKind)int.MaxValue), "알 수 없는 공개 템플릿 값");
        Qa.Equal("kind", invalid.ParamName, "잘못된 템플릿 인자 식별");
    }

    private static void NewTemplateShapes()
    {
        var subscriptSession = BlankSession();
        var script = (ScriptNode)Inserted(subscriptSession, MathTemplateKind.Subscript);
        Qa.Check(script.Subscript is TextNode { IsEmptySlot: true } && script.Superscript is null,
            "아래첨자가 위첨자와 다른 nullable AST 모양이 아님");
        CheckEmpty(script.Base, "아래첨자 밑");
        AssertCaretAt(subscriptSession, script.Base, "아래첨자 첫 슬롯");

        var integralSession = BlankSession();
        var integral = (IntegralNode)Inserted(integralSession, MathTemplateKind.Integral);
        CheckEmpty(integral.LowerLimit, "적분 하한");
        CheckEmpty(integral.UpperLimit, "적분 상한");
        CheckEmpty(integral.Integrand, "적분 피적분식");
        CheckEmpty(integral.DifferentialVariable, "적분 미분 변수");
        AssertCaretAt(integralSession, integral.LowerLimit, "적분 시작 슬롯");

        var sumSession = BlankSession();
        var sum = (SummationNode)Inserted(sumSession, MathTemplateKind.Summation);
        CheckEmpty(sum.LowerLimit, "합 하한");
        CheckEmpty(sum.UpperLimit, "합 상한");
        CheckEmpty(sum.Body, "합 본문");
        AssertCaretAt(sumSession, sum.LowerLimit, "합 시작 슬롯");

        var limitSession = BlankSession();
        var limit = (LimitNode)Inserted(limitSession, MathTemplateKind.Limit);
        CheckEmpty(limit.Approach, "극한 접근식");
        CheckEmpty(limit.Body, "극한 본문");
        AssertCaretAt(limitSession, limit.Approach, "극한 첫 슬롯");

        var bracketsSession = BlankSession();
        var brackets = (BracketsNode)Inserted(bracketsSession, MathTemplateKind.Brackets);
        Qa.Equal(MathBracketKind.Parentheses, brackets.Kind, "괄호 템플릿 기본 종류");
        CheckEmpty(brackets.Content, "괄호 내용");
        AssertCaretAt(bracketsSession, brackets.Content, "괄호 내용 caret");

        var casesSession = BlankSession();
        var cases = (CasesNode)Inserted(casesSession, MathTemplateKind.Cases);
        Qa.Equal(2, cases.Rows.Length, "경우 나누기 초기 행 수");
        foreach (var (row, index) in cases.Rows.Select((row, index) => (row, index)))
        {
            CheckEmpty(row.Expression, $"경우 나누기 {index + 1}행 식");
            CheckEmpty(row.Condition, $"경우 나누기 {index + 1}행 조건");
        }
        AssertCaretAt(casesSession, cases.Rows[0].Expression, "경우 나누기 첫 식 caret");
    }

    private static void WrapPartialText()
    {
        const string source = "A🙂e\u0301𝑥Z";
        const string selected = "🙂e\u0301𝑥";
        foreach (var kind in TemplateKinds)
        foreach (var reverse in new[] { false, true })
        {
            var leaf = Qa.Text(source);
            var session = new EditingSession(Qa.Workspace(leaf));
            Qa.Select(session, leaf.Id, reverse ? 4 : 1, reverse ? 1 : 4);
            var insertedId = session.InsertTemplate(kind);
            Qa.Check(insertedId.HasValue, $"{kind} {(reverse ? "역" : "정")}방향 선택 감싸기 실패");
            var wrapper = Qa.Node(session, insertedId!.Value);
            Qa.Equal(ExpectedNodeType(kind), wrapper.GetType(), kind + " 감싼 결과 종류");
            Qa.Equal(selected, string.Concat(Qa.Walk(wrapper).OfType<TextNode>().Select(text => text.Text)),
                kind + " 선택된 문자소가 template content에서 바뀌거나 순서가 달라짐");
            Qa.Equal(source, string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text)),
                kind + " 선택 앞뒤의 Unicode 문자가 손실/복제됨");
            var allIds = Qa.Nodes(session.Workspace).Select(node => node.Id).ToArray();
            Qa.Equal(allIds.Length, allIds.Distinct().Count(), kind + " 감싸기 뒤 ID 중복");
            Qa.Check(session.Selection is null && !session.IsNodeSelected, kind + " 삽입 후 이전 선택 잔존");
            Qa.Check(Qa.Walk(wrapper).OfType<TextNode>().Any(text => text.Id == session.Caret.NodeId),
                kind + " caret가 감싼 구조 안에 없음");
        }
    }

    private static void WrapSelectedStructure()
    {
        foreach (var kind in TemplateKinds)
        {
            var target = new FractionNode(Guid.NewGuid(), Qa.Text("분자𝑥"),
                new RadicalNode(Guid.NewGuid(), Qa.Text("분모e\u0301")));
            var left = Qa.Text("L|");
            var right = Qa.Text("|R");
            var session = new EditingSession(Qa.Workspace(Qa.Sequence(left, target, right)));
            Qa.Caret(session, target.Denominator is RadicalNode root ? Qa.Walk(root).OfType<TextNode>().Single().Id : target.Id, 1);
            Qa.Check(session.TrySelectNode(target.Id), "기존 분수 구조 전체 선택");
            var before = Qa.Fingerprint(session.Workspace);
            var oldCaret = session.Caret;
            var wrapperId = session.InsertTemplate(kind);
            Qa.Check(wrapperId.HasValue, kind + " 전체 구조 선택 감싸기 실패");
            var wrapper = Qa.Node(session, wrapperId!.Value);
            Qa.Check(Qa.Walk(wrapper).Any(node => node.Id == target.Id), kind + " 감싼 구조가 기존 선택 노드를 보존하지 않음");
            Qa.Equal("L|분자𝑥분모e\u0301|R",
                string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(text => text.Text)),
                kind + " 전체 선택 감싸기의 주변 텍스트/기존 하위식 보존");
            Qa.Check(Qa.Nodes(session.Workspace).Select(node => node.Id).Distinct().Count() == Qa.Nodes(session.Workspace).Count(),
                kind + " 전체 구조 감싸기 ID 중복");
            Qa.Check(!session.IsNodeSelected && session.Selection is null, kind + " 전체선택 상태가 삽입 뒤 남음");
            var after = Qa.Fingerprint(session.Workspace);
            session.Undo();
            Qa.Equal(before, Qa.Fingerprint(session.Workspace), kind + " 감싸기 undo 모델/ID");
            Qa.Equal(oldCaret, session.Caret, kind + " 감싸기 undo 원 caret");
            Qa.Check(session.IsNodeSelected && session.SelectedNodeId == target.Id, kind + " 감싸기 undo 전체선택 복구");
            session.Redo();
            Qa.Equal(after, Qa.Fingerprint(session.Workspace), kind + " 감싸기 redo");
        }
    }

    private static async Task OptionalBounds()
    {
        var integral = new IntegralNode(Guid.NewGuid(), Qa.Text("f(x)"), null, null, Qa.Text("x"));
        var sum = new SummationNode(Guid.NewGuid(), Qa.Text("an"), null, null);
        var unbounded = Qa.Workspace(Qa.Sequence(integral, Qa.Text("+"), sum));
        var unboundedLatex = LatexSerializer.ToLatex(unbounded);
        Qa.Check(unboundedLatex.Contains("\\int", StringComparison.Ordinal), "무경계 적분 연산자 누락");
        Qa.Check(unboundedLatex.Contains("\\sum", StringComparison.Ordinal), "무경계 합 연산자 누락");
        Qa.Check(unboundedLatex.Contains("f(x)", StringComparison.Ordinal) && unboundedLatex.Contains("\\,\\mathrm{d}{x}", StringComparison.Ordinal),
            "적분 본문 또는 미분변수 출력 누락");
        Qa.Check(unboundedLatex.Contains("an", StringComparison.Ordinal), "무경계 합 본문 누락");
        Qa.Check(!unboundedLatex.Contains("_{", StringComparison.Ordinal) && !unboundedLatex.Contains("^{", StringComparison.Ordinal),
            "null 상하한을 가짜 빈 첨자로 출력함");

        var lowerOnly = new IntegralNode(Guid.NewGuid(), Qa.Text("g"), Qa.Text("t=0"), null, Qa.Text("t"));
        var upperOnly = new SummationNode(Guid.NewGuid(), Qa.Text("b_k"), null, Qa.Text("N"));
        var partialBounds = LatexSerializer.ToLatex(Qa.Workspace(Qa.Sequence(lowerOnly, upperOnly)));
        Qa.Check(partialBounds.Contains(@"\int\nolimits_{{\scriptstyle t=0}}{g}\,\mathrm{d}{t}", StringComparison.Ordinal),
            "적분 하한만 Script bound group으로 출력되고 상한은 생략됨");
        Qa.Check(partialBounds.Contains(@"\sum\limits^{{\scriptstyle N}}{b\_k}", StringComparison.Ordinal),
            "합 상한만 Script bound group으로 출력되고 하한은 생략됨");
        Qa.Check(!partialBounds.Contains(@"^{{\scriptstyle t=0}}", StringComparison.Ordinal) &&
            !partialBounds.Contains(@"_{{\scriptstyle N}}", StringComparison.Ordinal),
            "누락된 optional bound를 반대쪽에 잘못 배치함");

        using var directory = new TempDirectory();
        var path = directory.File("optional-bounds.aethermath");
        var service = new WorkspaceFileService();
        await service.SaveAsync(path, new EditingSession(unbounded).CaptureSnapshot());
        var reopened = await service.OpenAsync(path);
        var reopenedIntegral = Qa.Nodes(reopened).OfType<IntegralNode>().Single();
        var reopenedSum = Qa.Nodes(reopened).OfType<SummationNode>().Single();
        Qa.Check(reopenedIntegral.LowerLimit is null && reopenedIntegral.UpperLimit is null,
            "JSON 왕복에서 integral null optional bounds가 빈 슬롯과 혼동됨");
        Qa.Check(reopenedSum.LowerLimit is null && reopenedSum.UpperLimit is null,
            "JSON 왕복에서 summation null optional bounds가 빈 슬롯과 혼동됨");
        Qa.Equal(Qa.Fingerprint(unbounded), Qa.Fingerprint(reopened), "optional bound JSON 왕복 의미");
    }

    private static void BracketsAndCases()
    {
        var expected = new[]
        {
            MathBracketKind.Parentheses, MathBracketKind.Square,
            MathBracketKind.Curly, MathBracketKind.Angle
        };
        var bracketLatex = new List<string>();
        foreach (var kind in expected)
        {
            var brackets = new BracketsNode(Guid.NewGuid(), kind, Qa.Text("u+v"));
            var session = new EditingSession(Qa.Workspace(brackets));
            Qa.Equal(kind, ((BracketsNode)Qa.Node(session, brackets.Id)).Kind, "괄호 종류 저장 " + kind);
            var latex = LatexSerializer.ToLatex(session.Workspace);
            Qa.Check(latex.Contains("u+v", StringComparison.Ordinal), "괄호 내용 손실 " + kind);
            Qa.Check(latex.Contains("\\left", StringComparison.Ordinal) && latex.Contains("\\right", StringComparison.Ordinal),
                "크기 조절 괄호 토큰 누락 " + kind);
            bracketLatex.Add(latex);
        }
        Qa.Equal(4, bracketLatex.Distinct(StringComparer.Ordinal).Count(), "괄호 네 종류가 같은 출력으로 축약됨");

        var casesSession = BlankSession();
        var cases = (CasesNode)Inserted(casesSession, MathTemplateKind.Cases);
        var rowOne = cases.Rows[0];
        var rowTwo = cases.Rows[1];
        var slots = new[] { rowOne.Expression, rowOne.Condition, rowTwo.Expression, rowTwo.Condition }
            .Cast<TextNode>().ToArray();
        var values = new[] { "α=0", "x<0", "β=1", "x≥0" };
        for (var index = 0; index < slots.Length; index++)
        {
            Qa.Caret(casesSession, slots[index].Id, 0);
            Qa.Check(casesSession.InsertText(values[index]), "경우 나누기 slot 입력 " + index);
            for (var other = 0; other < slots.Length; other++)
                Qa.Equal(other <= index ? values[other] : string.Empty,
                    Qa.Leaf(casesSession, slots[other].Id).Text, "경우 나누기 다른 식/조건이 교차 변경됨");
        }
        var serialized = LatexSerializer.ToLatex(casesSession.Workspace);
        Qa.Check(serialized.Contains("\\begin{cases}", StringComparison.Ordinal) &&
            serialized.Contains("\\end{cases}", StringComparison.Ordinal), "경우 나누기 환경 경계");
        var order = new[] { "α=0", "x<0", "β=1", "x≥0" };
        var positions = order.Select(value => serialized.IndexOf(value, StringComparison.Ordinal)).ToArray();
        Qa.Check(positions.All(position => position >= 0) && positions.Zip(positions.Skip(1), (a, b) => a < b).All(x => x),
            "경우 나누기의 행 또는 expression/condition 출력 순서 변경: " + serialized);
        Qa.Check(serialized.Contains(@"{\textstyle α=0}&{\textstyle x<0}", StringComparison.Ordinal) &&
            serialized.Contains(@"{\textstyle β=1}&{\textstyle x≥0}", StringComparison.Ordinal),
            "행별 식·조건의 Text scope group 분리 손실");
    }

    private static void NestedTreeOrderAndGraphemes()
    {
        var lower = Qa.Text("i=0"); var upper = Qa.Text("n");
        var integrand = Qa.Text("f(x)"); var differential = Qa.Text("x");
        var integral = new IntegralNode(Guid.NewGuid(), integrand, lower, upper, differential);
        EqualIds([lower.Id, upper.Id, integrand.Id, differential.Id], MathTreeTextLeaves(integral).Select(n => n.Id),
            "제품 MathTree 적분 child 순서(UI의 Tab 검사는 TEMPLATE-UI에서 별도 실행)");

        var sumLower = Qa.Text("k=1"); var sumUpper = Qa.Text("N"); var sumBody = Qa.Text("a_k");
        var sum = new SummationNode(Guid.NewGuid(), sumBody, sumLower, sumUpper);
        EqualIds([sumLower.Id, sumUpper.Id, sumBody.Id], MathTreeTextLeaves(sum).Select(n => n.Id),
            "제품 MathTree 합 child 순서");

        var approach = Qa.Text("x→0"); var body = Qa.Text("f(x)");
        var limit = new LimitNode(Guid.NewGuid(), approach, body);
        EqualIds([approach.Id, body.Id], MathTreeTextLeaves(limit).Select(n => n.Id),
            "제품 MathTree 극한 child 순서");

        var caseNodes = new CasesNode(Guid.NewGuid(),
        [
            new CaseRow(Qa.Text("a"), Qa.Text("x<0")),
            new CaseRow(Qa.Text("b"), Qa.Text("x≥0"))
        ]);
        EqualIds([caseNodes.Rows[0].Expression.Id, caseNodes.Rows[0].Condition.Id,
                caseNodes.Rows[1].Expression.Id, caseNodes.Rows[1].Condition.Id],
            MathTreeTextLeaves(caseNodes).Select(n => n.Id), "제품 MathTree 경우 나누기 child 행 우선 순서");

        var all = AllTemplatesNode();
        var actualKinds = Qa.Walk(all).Where(n => n is not TextNode and not SequenceNode)
            .Select(n => n.GetType()).ToHashSet();
        var expectedKinds = new[]
        {
            typeof(FractionNode), typeof(RadicalNode), typeof(ScriptNode), typeof(MatrixNode),
            typeof(IntegralNode), typeof(SummationNode), typeof(LimitNode), typeof(BracketsNode), typeof(CasesNode)
        };
        foreach (var type in expectedKinds)
            Qa.Check(actualKinds.Contains(type), "중첩 fixture에서 새 템플릿 종류 누락: " + type.Name);
        var fixtureIntegral = Qa.Walk(all).OfType<IntegralNode>().Single();
        var workspace = Qa.Workspace(all);
        var session = new EditingSession(workspace);
        var unicode = Qa.Nodes(session.Workspace).OfType<TextNode>().Single(n => n.Text == "e\u0301🙂𝑥");
        Qa.Equal(3, Qa.Elements(unicode.Text), "Unicode fixture 문자소 수");
        Qa.Check(session.TrySetCaret(new MathCaret(unicode.Id, 3)), "마지막 Unicode 문자소 경계 caret");
        Qa.Check(!session.TrySetCaret(new MathCaret(unicode.Id, 4)), "UTF-16 index를 문자소 offset으로 오인");
        var layout = MathLayoutEngine.Layout(workspace, new AvaloniaMathTextMetrics());
        Qa.Check(layout.Nodes.Single(n => n.NodeId == fixtureIntegral.Id).CaretStops.IsEmpty,
            "구조 node에 TextNode caret stops를 잘못 생성함");
        Qa.Equal(Qa.Elements(unicode.Text) + 1,
            layout.Nodes.Single(n => n.NodeId == unicode.Id).CaretStops.Length, "Unicode grapheme caret stop 수");
    }

    private static void CloneDeleteAndHistory()
    {
        var root = AllTemplatesNode();
        var trailing = Qa.Text("tail");
        var session = new EditingSession(Qa.Workspace(root, trailing));
        var originalRowId = session.Workspace.Groups[0].Rows[0].Id;
        var unicode = Qa.Nodes(session.Workspace).OfType<TextNode>().Single(n => n.Text == "e\u0301🙂𝑥");
        Qa.Select(session, unicode.Id, 3, 1);
        var originalWorkspace = Qa.Fingerprint(session.Workspace);
        var originalCaret = session.Caret;
        var originalSelection = session.Selection;
        Qa.Check(session.DuplicateCurrentRow(), "새 템플릿 포함 행 clone 실패");
        var cloneRow = session.Workspace.Groups[0].Rows[1];
        Qa.Check(cloneRow.Id != originalRowId, "복제 row ID 재사용");
        var idMap = MapCloneTree(root, cloneRow.Root);
        var allIds = Qa.Nodes(session.Workspace).Select(n => n.Id).ToArray();
        Qa.Equal(allIds.Length, allIds.Distinct().Count(), "새 템플릿 복제 전체 ID 중복");
        Qa.Equal(new MathCaret(idMap[unicode.Id], originalCaret.TextElementOffset), session.Caret,
            "복제한 새 템플릿 caret ID/문자소 offset");
        Qa.Equal(new MathSelection(new(idMap[unicode.Id], 3), new(idMap[unicode.Id], 1)), session.Selection,
            "복제한 새 템플릿 역방향 selection 매핑");
        _ = new EditingSession(session.Workspace);
        var clonedWorkspace = Qa.Fingerprint(session.Workspace);
        var clonedCaret = session.Caret;
        var clonedSelection = session.Selection;
        session.Undo();
        Qa.Equal(originalWorkspace, Qa.Fingerprint(session.Workspace), "새 템플릿 clone undo workspace");
        Qa.Equal(originalCaret, session.Caret, "새 템플릿 clone undo caret");
        Qa.Equal(originalSelection, session.Selection, "새 템플릿 clone undo selection");
        session.Redo();
        Qa.Equal(clonedWorkspace, Qa.Fingerprint(session.Workspace), "새 템플릿 clone redo workspace");
        Qa.Equal(clonedCaret, session.Caret, "새 템플릿 clone redo caret");
        Qa.Equal(clonedSelection, session.Selection, "새 템플릿 clone redo selection");

        var clonedCases = Qa.Walk(cloneRow.Root).OfType<CasesNode>().Single();
        Qa.Check(session.TrySelectNode(clonedCases.Id), "복제 Cases 구조 선택");
        var beforeDelete = Qa.Fingerprint(session.Workspace);
        var deleteCaret = session.Caret;
        Qa.Check(session.DeleteSelection(), "복제 Cases 구조 삭제");
        Qa.Check(!Qa.Nodes(session.Workspace).Any(n => n.Id == clonedCases.Id), "삭제된 Cases ID가 남음");
        var afterDelete = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(beforeDelete, Qa.Fingerprint(session.Workspace), "Cases 삭제 undo subtree/ID 복원");
        Qa.Check(session.IsNodeSelected && session.SelectedNodeId == clonedCases.Id,
            "Cases 삭제 undo 전체 구조 선택 복구");
        Qa.Equal(deleteCaret, session.Caret, "Cases 삭제 undo caret 복원");
        session.Redo();
        Qa.Equal(afterDelete, Qa.Fingerprint(session.Workspace), "Cases 삭제 redo");
    }

    private static async Task JsonCompatibilityAndRoundTrip()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService();
        const string oldFixture = """
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "schemaVersion": 1,
          "name": "legacy old-node fixture",
          "groups": [
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "rows": [
                {
                  "id": "33333333-3333-3333-3333-333333333333",
                  "root": {
                    "$kind": "sequence",
                    "id": "44444444-4444-4444-4444-444444444444",
                    "children": [
                      { "$kind": "text", "id": "55555555-5555-5555-5555-555555555555", "text": "x=", "isEmptySlot": false },
                      {
                        "$kind": "fraction",
                        "id": "66666666-6666-6666-6666-666666666666",
                        "numerator": { "$kind": "text", "id": "77777777-7777-7777-7777-777777777777", "text": "a", "isEmptySlot": false },
                        "denominator": {
                          "$kind": "script",
                          "id": "88888888-8888-8888-8888-888888888888",
                          "base": { "$kind": "text", "id": "99999999-9999-9999-9999-999999999999", "text": "b", "isEmptySlot": false },
                          "subscript": { "$kind": "text", "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "text": "i", "isEmptySlot": false },
                          "superscript": null
                        }
                      }
                    ]
                  }
                }
              ],
              "alignment": 0,
              "rowSpacing": 22.0
            }
          ],
          "style": { "fontSize": 30.0, "fontFamily": "Cambria Math", "foreground": "#25364D" }
        }
        """;
        var oldPath = directory.File("legacy.aethermath");
        await File.WriteAllTextAsync(oldPath, oldFixture);
        var oldWorkspace = await service.OpenAsync(oldPath);
        Qa.Equal(1, oldWorkspace.SchemaVersion, "기존 workspace schema 버전");
        Qa.Equal("legacy old-node fixture", oldWorkspace.Name, "구버전 fixture 이름");
        Qa.Equal(1, Qa.Nodes(oldWorkspace).OfType<FractionNode>().Count(), "구버전 fraction 판독");
        Qa.Equal(1, Qa.Nodes(oldWorkspace).OfType<ScriptNode>().Count(), "구버전 script 판독");
        var oldSession = new EditingSession(oldWorkspace);
        var oldCopy = directory.File("legacy-resaved.aethermath");
        await service.SaveAsync(oldCopy, oldSession.CaptureSnapshot());
        Qa.Equal(Qa.Fingerprint(oldWorkspace), Qa.Fingerprint(await service.OpenAsync(oldCopy)), "구버전 노드 재저장 후 ID/의미");

        var fullWorkspace = Qa.Workspace(Qa.Sequence(
            AllTemplatesNode(),
            new IntegralNode(Guid.NewGuid(), Qa.Text("f"), null, null, Qa.Text("x")),
            new SummationNode(Guid.NewGuid(), Qa.Text("a"), null, null)));
        var fullPath = directory.File("all-template-nodes.aethermath");
        await service.SaveAsync(fullPath, new EditingSession(fullWorkspace).CaptureSnapshot());
        var json = await File.ReadAllTextAsync(fullPath);
        foreach (var discriminator in new[] { "integral", "summation", "limit", "brackets", "cases" })
            Qa.Check(json.Contains($"\"$kind\": \"{discriminator}\"", StringComparison.Ordinal), "JSON 다형성 discriminator 누락: " + discriminator);
        var reopened = await service.OpenAsync(fullPath);
        Qa.Equal(Qa.Fingerprint(fullWorkspace), Qa.Fingerprint(reopened), "새 템플릿 전체 JSON 왕복");
        Qa.Equal(1, reopened.SchemaVersion, "새 discriminator 추가에도 기존 schema v1 유지");
        Qa.Check(Qa.Nodes(reopened).OfType<IntegralNode>().Single(node => node.LowerLimit is not null).UpperLimit is not null,
            "템플릿 fixture의 명시 bound 손실");
        Qa.Check(Qa.Nodes(reopened).OfType<BracketsNode>().Single().Kind == MathBracketKind.Parentheses,
            "괄호 종류 JSON 왕복 손실");
        Qa.Equal(2, Qa.Nodes(reopened).OfType<CasesNode>().Single().Rows.Length, "Cases row count JSON 왕복");

        var recoveryStore = new WorkspaceStateService(directory.File("state-root"));
        var recoveryId = Guid.NewGuid();
        var recoverySnapshot = new EditingSession(fullWorkspace).CaptureSnapshot();
        var recoveryWrite = await recoveryStore.SaveRecoveryAsync(new WorkspaceRecovery(
            recoveryId, recoverySnapshot, DateTimeOffset.UtcNow, oldPath));
        Qa.Equal(RecoveryWriteStatus.Written, recoveryWrite.Status, "10종/nullable bound recovery envelope 저장");
        var recoveryRead = await recoveryStore.ReadRecoveryAsync(recoveryId);
        Qa.Equal(RecoveryReadStatus.Found, recoveryRead.Status, "10종 recovery envelope 재열기");
        Qa.Check(recoveryRead.Recovery is not null, "recovery envelope가 Found인데 내용이 없음");
        Qa.Equal(Qa.Fingerprint(fullWorkspace), Qa.Fingerprint(recoveryRead.Recovery!.Snapshot.Workspace),
            "recovery envelope의 10종 AST/ID/style/null-bound 보존");
        Qa.Check(Qa.Nodes(recoveryRead.Recovery.Snapshot.Workspace).OfType<IntegralNode>().Any(node =>
                node.LowerLimit is null && node.UpperLimit is null) &&
            Qa.Nodes(recoveryRead.Recovery.Snapshot.Workspace).OfType<SummationNode>().Any(node =>
                node.LowerLimit is null && node.UpperLimit is null),
            "recovery envelope에서 nullable bound가 빈 필수 슬롯으로 변환됨");
    }

    private static async Task RejectInvalidJson()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService();
        var validPath = directory.File("valid-all-templates.aethermath");
        await service.SaveAsync(validPath, new EditingSession(Qa.Workspace(AllTemplatesNode())).CaptureSnapshot());
        var validText = await File.ReadAllTextAsync(validPath);
        var mutations = new (string Name, Action<JsonNode> Apply)[]
        {
            ("cases-missing-rows", root => FindKind(root, "cases").Remove("rows")),
            ("cases-null-rows", root => FindKind(root, "cases")["rows"] = null),
            ("cases-empty-rows", root => FindKind(root, "cases")["rows"] = new JsonArray()),
            ("cases-null-row", root => ((JsonArray)FindKind(root, "cases")["rows"]!)[0] = null),
            ("cases-null-expression", root => ((JsonArray)FindKind(root, "cases")["rows"]!)[0]!["expression"] = null),
            ("cases-null-condition", root => ((JsonArray)FindKind(root, "cases")["rows"]!)[0]!["condition"] = null),
            ("integral-null-integrand", root => FindKind(root, "integral")["integrand"] = null),
            ("integral-null-differential", root => FindKind(root, "integral")["differentialVariable"] = null),
            ("sum-null-body", root => FindKind(root, "summation")["body"] = null),
            ("limit-null-approach", root => FindKind(root, "limit")["approach"] = null),
            ("limit-null-body", root => FindKind(root, "limit")["body"] = null),
            ("brackets-null-content", root => FindKind(root, "brackets")["content"] = null),
            ("brackets-invalid-enum", root => FindKind(root, "brackets")["kind"] = int.MaxValue),
            ("duplicate-child-id", root =>
            {
                var cases = FindKind(root, "cases");
                var first = (JsonObject)((JsonArray)cases["rows"]!)[0]!["expression"]!;
                var second = (JsonObject)((JsonArray)cases["rows"]!)[0]!["condition"]!;
                second["id"] = first["id"]!.DeepClone();
            }),
            ("unknown-new-discriminator", root => FindKind(root, "limit")["$kind"] = "future-template")
        };

        foreach (var mutation in mutations)
        {
            var json = JsonNode.Parse(validText)!;
            mutation.Apply(json);
            var path = directory.File(mutation.Name + ".aethermath");
            await File.WriteAllTextAsync(path, json.ToJsonString());
            var originalBytes = await File.ReadAllBytesAsync(path);
            await Qa.FailsAsync(() => service.OpenAsync(path), mutation.Name,
                typeof(JsonException), typeof(InvalidDataException));
            var bytesAfterRejectedOpen = await File.ReadAllBytesAsync(path);
            Qa.Check(originalBytes.SequenceEqual(bytesAfterRejectedOpen), mutation.Name + " 거절이 원본 파일 바이트를 변경함");
        }

        var defaultRows = new CasesNode(Guid.NewGuid(), default);
        Qa.Throws<InvalidDataException>(() => new EditingSession(Qa.Workspace(defaultRows)),
            "Cases default row collection 모델 거절");

        var nullRoot = directory.File("null-root.aethermath");
        await File.WriteAllTextAsync(nullRoot, "null");
        var nullBytes = await File.ReadAllBytesAsync(nullRoot);
        await Qa.FailsAsync(() => service.OpenAsync(nullRoot), "JSON null workspace root",
            typeof(JsonException), typeof(InvalidDataException));
        var bytesAfterNull = await File.ReadAllBytesAsync(nullRoot);
        Qa.Check(nullBytes.SequenceEqual(bytesAfterNull), "null root 파일 원본 바이트 변경");
    }

    private static void LatexSemantics()
    {
        var workspace = Qa.Workspace(AllTemplatesNode());
        var latex = LatexSerializer.ToLatex(workspace);
        foreach (var token in new[]
        {
            "\\frac{", "\\sqrt{", "_{", "^{",
            @"{\displaystyle \int\nolimits_{{\scriptstyle i=0}}^{{\scriptstyle n}}{f(x)}\,\mathrm{d}{dx}}",
            @"{\displaystyle \sum\limits_{{\scriptstyle k=1}}^{{\scriptstyle N}}{a\_k}}",
            @"{\displaystyle \lim\limits_{{\scriptstyle x→0}}{g(x)}}",
            "\\begin{bmatrix}", "\\left(", "\\right)", "\\begin{cases}", "\\end{cases}"
        })
            Qa.Check(latex.Contains(token, StringComparison.Ordinal), "템플릿 serializer 의미 표기 누락: " + token + " / " + latex);
        foreach (var text in new[] { "fracN", "fracD", "radicand", "sub\\_i", "sup\\_2", "i=0", "n", "f(x)", "dx", "k=1", "N", "a\\_k", "x→0", "g(x)", "matrix22", "u+v", "caseA", "x<0", "caseB", "x≥0" })
            Qa.Check(latex.Contains(text, StringComparison.Ordinal), "LaTeX serializer에서 수식 텍스트/행이 손실됨: " + text);

        var noBounds = Qa.Workspace(Qa.Sequence(
            new IntegralNode(Guid.NewGuid(), Qa.Text("f"), null, null, Qa.Text("x")),
            new SummationNode(Guid.NewGuid(), Qa.Text("a"), null, null)));
        var noBoundsLatex = LatexSerializer.ToLatex(noBounds);
        Qa.Check(noBoundsLatex.Contains("\\int", StringComparison.Ordinal) &&
            noBoundsLatex.Contains("\\sum", StringComparison.Ordinal), "null bound serializer 연산자 손실");
        Qa.Check(!noBoundsLatex.Contains("_{", StringComparison.Ordinal) && !noBoundsLatex.Contains("^{", StringComparison.Ordinal),
            "null bound가 빈 상/하한 command로 오출력됨");
        Qa.Check(noBoundsLatex.Contains("{f}", StringComparison.Ordinal) && noBoundsLatex.Contains("\\,\\mathrm{d}{x}", StringComparison.Ordinal) &&
            noBoundsLatex.Contains("a", StringComparison.Ordinal), "null bound에서 필수 내용이 생략됨");

        var angleAdjacent = LatexSerializer.ToLatex(Qa.Workspace(Qa.Sequence(
            Qa.Text("A"), new BracketsNode(Guid.NewGuid(), MathBracketKind.Angle, Qa.Text("x")), Qa.Text("z"))));
        Qa.Check(angleAdjacent.Contains("\\left\\langle{x}\\right\\rangle{}", StringComparison.Ordinal),
            "Angle bracket의 control-word 종료 경계가 명시되지 않음: " + angleAdjacent);
        Qa.Check(!angleAdjacent.Contains("\\langlex", StringComparison.Ordinal) &&
            !angleAdjacent.Contains("\\ranglez", StringComparison.Ordinal),
            "Angle bracket 명령이 인접 영문자와 하나의 control word로 합쳐짐: " + angleAdjacent);
    }

    private static async Task LayoutAndSvgParity()
    {
        var complete = Qa.Workspace(AllTemplatesNode());
        var layout = MathLayoutEngine.Layout(complete, new AvaloniaMathTextMetrics());
        Qa.Check(layout.Diagnostics.IsEmpty, "완성된 새 템플릿의 미완성 진단");
        Qa.Equal(Qa.Nodes(complete).Count(), layout.Nodes.Length, "10종 AST의 각 node bounds 생성");
        foreach (var node in Qa.Nodes(complete))
        {
            var bounds = layout.Nodes.Single(result => result.NodeId == node.Id);
            Qa.Check(bounds.Bounds.Width >= 0 && bounds.Bounds.Height > 0 &&
                bounds.Bounds.X >= -0.001 && bounds.Bounds.Y >= -0.001 &&
                bounds.Bounds.Right <= layout.Width + 0.001 && bounds.Bounds.Bottom <= layout.Height + 0.001,
                "새 template node layout bounds 범위: " + node.GetType().Name + "/" + node.Id);
            if (node is TextNode text)
                Qa.Equal(Qa.Elements(text.Text) + 1, bounds.CaretStops.Length, "새 template Unicode/text caret stop");
        }
        var glyphs = layout.Primitives.OfType<GlyphPrimitive>().ToArray();
        foreach (var operatorGlyph in new[] { "∫", "∑", "lim" })
            Qa.Check(glyphs.Any(glyph => glyph.Text == operatorGlyph), "실제 layout operator glyph 누락: " + operatorGlyph);
        Qa.Check(layout.Primitives.OfType<PolylinePrimitive>().Count() >= 6,
            "새 괄호/행렬/경우나누기 구조 도형 primitive 누락");
        AssertGlyphsWithinOwner(layout, glyphs);

        var stress = Qa.Workspace(Qa.Sequence(
            new IntegralNode(Guid.NewGuid(), Qa.Text("x"),
                new FractionNode(Guid.NewGuid(), Qa.Text(new string('n', 72)), Qa.Text("k=0")),
                new RadicalNode(Guid.NewGuid(), Qa.Text(new string('m', 68))), Qa.Text("t")),
            new SummationNode(Guid.NewGuid(), Qa.Text("body"),
                new FractionNode(Guid.NewGuid(), Qa.Text(new string('i', 76)), Qa.Text("q")),
                Qa.Text(new string('N', 73))),
            new LimitNode(Guid.NewGuid(), new FractionNode(Guid.NewGuid(), Qa.Text(new string('x', 70)), Qa.Text("n")), Qa.Text("limitBody")),
            new BracketsNode(Guid.NewGuid(), MathBracketKind.Parentheses, Qa.Text("x")),
            new BracketsNode(Guid.NewGuid(), MathBracketKind.Square,
                new FractionNode(Guid.NewGuid(),
                    new RadicalNode(Guid.NewGuid(), new FractionNode(Guid.NewGuid(), Qa.Text("a"), Qa.Text("b"))),
                    new FractionNode(Guid.NewGuid(), Qa.Text("c"), new RadicalNode(Guid.NewGuid(), Qa.Text("d"))))),
            new BracketsNode(Guid.NewGuid(), MathBracketKind.Curly,
                new MatrixNode(Guid.NewGuid(), 4, 2,
                    [Qa.Text("m11"), Qa.Text("m12"), Qa.Text("m21"), Qa.Text("m22"),
                     Qa.Text("m31"), Qa.Text("m32"), Qa.Text("m41"), Qa.Text("m42")])),
            new BracketsNode(Guid.NewGuid(), MathBracketKind.Angle, Qa.Text("z")),
            new CasesNode(Guid.NewGuid(),
            [
                new CaseRow(Qa.Text("caseA"), Qa.Text("x<0")),
                new CaseRow(Qa.Text("caseB"), Qa.Text("x≥0"))
            ])));
        var stressLayout = MathLayoutEngine.Layout(stress, new AvaloniaMathTextMetrics());
        AssertChildrenInsideAndSeparated(stress, stressLayout);
        AssertBracketsAndCasesInsideReservedColumns(stress, stressLayout);
        var stressBrackets = Qa.Nodes(stress).OfType<BracketsNode>().ToDictionary(node => node.Kind);
        var smallContentHeight = stressLayout.Nodes.Single(node => node.NodeId == stressBrackets[MathBracketKind.Parentheses].Content.Id).Bounds.Height;
        var fractionContentHeight = stressLayout.Nodes.Single(node => node.NodeId == stressBrackets[MathBracketKind.Square].Content.Id).Bounds.Height;
        var matrixContentHeight = stressLayout.Nodes.Single(node => node.NodeId == stressBrackets[MathBracketKind.Curly].Content.Id).Bounds.Height;
        Qa.Check(smallContentHeight < fractionContentHeight && smallContentHeight < matrixContentHeight,
            $"괄호 높이 fixture가 작은 식/큰 분수/행렬을 구분하지 못함: small={smallContentHeight:R}, fraction={fractionContentHeight:R}, matrix={matrixContentHeight:R}");
        AssertGlyphsWithinOwner(stressLayout, stressLayout.Primitives.OfType<GlyphPrimitive>().ToArray());

        var svg = new SvgExportService().ToSvg(layout); // 화면과 출력이 공유하는 같은 LayoutResult를 전달
        var document = XDocument.Parse(svg);
        var elements = document.Root!.Descendants().Where(element =>
            element.Name.LocalName is "text" or "line" or "polyline" or "polygon").ToArray();
        Qa.Equal(layout.Primitives.Length, elements.Length, "SVG primitive 수가 전달된 LayoutResult와 다름");
        for (var index = 0; index < layout.Primitives.Length; index++)
        {
            var primitive = layout.Primitives[index];
            var element = elements[index];
            switch (primitive)
            {
                case GlyphPrimitive glyph:
                    Qa.Equal("text", element.Name.LocalName, "SVG primitive 순서의 glyph");
                    Qa.Equal(glyph.Text, element.Value, "SVG glyph text");
                    Qa.Near(glyph.BaselineOrigin.X, Number(element, "x"), "공유 LayoutResult glyph x");
                    Qa.Near(glyph.BaselineOrigin.Y, Number(element, "y"), "공유 LayoutResult glyph baseline");
                    break;
                case LinePrimitive line:
                    Qa.Equal("line", element.Name.LocalName, "SVG primitive 순서의 line");
                    Qa.Near(line.Start.X, Number(element, "x1"), "공유 LayoutResult line x1");
                    Qa.Near(line.Start.Y, Number(element, "y1"), "공유 LayoutResult line y1");
                    Qa.Near(line.End.X, Number(element, "x2"), "공유 LayoutResult line x2");
                    Qa.Near(line.End.Y, Number(element, "y2"), "공유 LayoutResult line y2");
                    break;
                case PolylinePrimitive polyline:
                    Qa.Check(element.Name.LocalName is "polyline" or "polygon", "SVG primitive 순서의 polyline");
                    Qa.Equal(polyline.Closed, element.Name.LocalName == "polygon", "SVG polyline 닫힘 여부");
                    var points = element.Attribute("points")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    Qa.Equal(polyline.Points.Length, points.Length, "SVG shape 좌표 수");
                    for (var point = 0; point < points.Length; point++)
                    {
                        var pair = points[point].Split(',');
                        Qa.Near(polyline.Points[point].X, double.Parse(pair[0], System.Globalization.CultureInfo.InvariantCulture), "SVG polyline x");
                        Qa.Near(polyline.Points[point].Y, double.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture), "SVG polyline y");
                    }
                    break;
            }
        }

        var incompleteSession = BlankSession();
        Qa.Check(incompleteSession.InsertTemplate(MathTemplateKind.Cases).HasValue, "미완성 출력용 Cases 삽입");
        var incompleteLayout = MathLayoutEngine.Layout(incompleteSession.Workspace, new AvaloniaMathTextMetrics());
        Qa.Check(!incompleteLayout.Diagnostics.IsEmpty, "필수 빈 template slots에 대한 진단이 없음");
        using var directory = new TempDirectory();
        var preservedPath = directory.File("existing.svg");
        File.WriteAllText(preservedPath, "이전 완료 출력");
        var before = File.ReadAllBytes(preservedPath);
        Qa.Throws<MathExportException>(() => new SvgExportService().ToSvg(incompleteLayout), "필수 빈 슬롯 SVG 출력 경계");
        await Qa.FailsAsync(() => new SvgExportService().ExportAsync(preservedPath, incompleteLayout),
            "미완성 template 기존 SVG 출력 파일 보존", typeof(MathExportException));
        var after = File.ReadAllBytes(preservedPath);
        Qa.Check(before.SequenceEqual(after), "미완성 수식 진단이 기존 출력물을 변경함");
    }

    private static void AssertGlyphsWithinOwner(LayoutResult layout, IReadOnlyCollection<GlyphPrimitive> glyphs)
    {
        var metrics = new AvaloniaMathTextMetrics();
        foreach (var glyph in glyphs)
        {
            var owner = layout.Nodes.Single(node => node.NodeId == glyph.NodeId).Bounds;
            var measured = metrics.Measure(glyph.Text, glyph.FontFamily, glyph.FontSize, glyph.Italic);
            var left = glyph.BaselineOrigin.X;
            var top = glyph.BaselineOrigin.Y - measured.Ascent;
            var right = left + measured.Width;
            var bottom = glyph.BaselineOrigin.Y + measured.Descent;
            Qa.Check(left >= owner.X - 0.75 && top >= owner.Y - 0.75 &&
                right <= owner.Right + 0.75 && bottom <= owner.Bottom + 0.75,
                $"glyph가 owner NodeLayout bounds 밖: node={glyph.NodeId}, glyph={glyph.Text}, glyphRect={left:R},{top:R},{right:R},{bottom:R}, owner={owner}");
        }
    }

    private static void AssertChildrenInsideAndSeparated(EquationWorkspace workspace, LayoutResult layout)
    {
        NodeLayout Get(MathNode node) => layout.Nodes.Single(item => item.NodeId == node.Id);
        static bool Intersects(MathRect a, MathRect b) =>
            a.X < b.Right - 0.5 && b.X < a.Right - 0.5 && a.Y < b.Bottom - 0.5 && b.Y < a.Bottom - 0.5;
        MathRect GlyphBounds(GlyphPrimitive glyph)
        {
            var measure = new AvaloniaMathTextMetrics().Measure(glyph.Text, glyph.FontFamily, glyph.FontSize, glyph.Italic);
            return new MathRect(glyph.BaselineOrigin.X, glyph.BaselineOrigin.Y - measure.Ascent,
                measure.Width, measure.Ascent + measure.Descent);
        }
        MathRect OperatorBounds(MathNode owner, string text) => GlyphBounds(layout.Primitives.OfType<GlyphPrimitive>()
            .Single(glyph => glyph.NodeId == owner.Id && glyph.Text == text));
        static void Inside(MathRect outer, MathRect inner, string label) => Qa.Check(
            inner.X >= outer.X - 0.5 && inner.Y >= outer.Y - 0.5 &&
            inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5,
            $"{label}: 자식 bounds가 부모 밖으로 나감. parent={outer}; child={inner}");

        foreach (var node in Qa.Nodes(workspace))
        {
            switch (node)
            {
                case IntegralNode integral:
                {
                    var owner = Get(integral).Bounds;
                    var integrand = Get(integral.Integrand).Bounds;
                    var differential = Get(integral.DifferentialVariable).Bounds;
                    var op = OperatorBounds(integral, "∫");
                    Inside(owner, integrand, "Integral Integrand");
                    Inside(owner, differential, "Integral differential variable");
                    Qa.Check(integrand.X >= op.Right - 0.5, "적분 본문이 연산자 glyph 왼쪽으로 밀림");
                    if (integral.LowerLimit is not null)
                    {
                        var lower = Get(integral.LowerLimit).Bounds;
                        Inside(owner, lower, "Integral lower limit");
                        Qa.Check(!Intersects(lower, integrand) && !Intersects(lower, differential) && !Intersects(lower, op),
                            "적분 하한이 연산자/본문/변수와 겹침");
                    }
                    if (integral.UpperLimit is not null)
                    {
                        var upper = Get(integral.UpperLimit).Bounds;
                        Inside(owner, upper, "Integral upper limit");
                        Qa.Check(!Intersects(upper, integrand) && !Intersects(upper, differential) && !Intersects(upper, op),
                            "적분 상한이 연산자/본문/변수와 겹침");
                    }
                    break;
                }
                case SummationNode summation:
                {
                    var owner = Get(summation).Bounds;
                    var body = Get(summation.Body).Bounds;
                    var op = OperatorBounds(summation, "∑");
                    Inside(owner, body, "Summation body");
                    Qa.Check(body.X >= op.Right - 0.5, "합 본문이 연산자 glyph 왼쪽으로 밀림");
                    if (summation.LowerLimit is not null)
                    {
                        var lower = Get(summation.LowerLimit).Bounds;
                        Inside(owner, lower, "Summation lower limit");
                        Qa.Check(!Intersects(lower, body) && !Intersects(lower, op), "합 하한이 연산자/본문과 겹침");
                    }
                    if (summation.UpperLimit is not null)
                    {
                        var upper = Get(summation.UpperLimit).Bounds;
                        Inside(owner, upper, "Summation upper limit");
                        Qa.Check(!Intersects(upper, body) && !Intersects(upper, op), "합 상한이 연산자/본문과 겹침");
                    }
                    break;
                }
                case LimitNode limit:
                {
                    var owner = Get(limit).Bounds;
                    var approach = Get(limit.Approach).Bounds;
                    var body = Get(limit.Body).Bounds;
                    var op = OperatorBounds(limit, "lim");
                    Inside(owner, approach, "Limit approach");
                    Inside(owner, body, "Limit body");
                    Qa.Check(!Intersects(approach, op), "극한 접근식이 lim 연산자 glyph와 겹침");
                    Qa.Check(body.X >= op.Right - 0.5, "극한 본문이 lim 연산자 glyph 왼쪽으로 밀림");
                    Qa.Check(body.X >= approach.Right - 0.5, "극한 접근식과 본문이 겹치거나 본문이 접근식 앞에 놓임");
                    break;
                }
            }
        }
    }

    private static void AssertBracketsAndCasesInsideReservedColumns(EquationWorkspace workspace, LayoutResult layout)
    {
        const double tolerance = 0.5;
        NodeLayout Get(MathNode node) => layout.Nodes.Single(item => item.NodeId == node.Id);
        static MathRect StrokeBounds(PolylinePrimitive shape)
        {
            Qa.Check(!shape.Points.IsDefaultOrEmpty, "괄호/brace 도형 primitive에 점이 없음");
            var halfStroke = shape.StrokeWidth / 2;
            var left = shape.Points.Min(point => point.X) - halfStroke;
            var top = shape.Points.Min(point => point.Y) - halfStroke;
            var right = shape.Points.Max(point => point.X) + halfStroke;
            var bottom = shape.Points.Max(point => point.Y) + halfStroke;
            return new MathRect(left, top, right - left, bottom - top);
        }
        static double CenterX(MathRect bounds) => bounds.X + bounds.Width / 2;
        static double CenterY(MathRect bounds) => bounds.Y + bounds.Height / 2;
        bool IsCenteredInOwner(MathRect shape, MathRect owner) =>
            CenterX(shape) >= owner.X - tolerance && CenterX(shape) <= owner.Right + tolerance &&
            CenterY(shape) >= owner.Y - tolerance && CenterY(shape) <= owner.Bottom + tolerance;
        void Inside(MathRect outer, MathRect inner, string label) => Qa.Check(
            inner.X >= outer.X - tolerance && inner.Y >= outer.Y - tolerance &&
            inner.Right <= outer.Right + tolerance && inner.Bottom <= outer.Bottom + tolerance,
            $"{label}: 선 두께를 포함한 도형 bounds가 owner 밖으로 나감; owner={outer}; shape={inner}");

        var shapes = layout.Primitives.OfType<PolylinePrimitive>()
            .Select(shape => (Shape: shape, Bounds: StrokeBounds(shape)))
            .ToArray();
        foreach (var brackets in Qa.Nodes(workspace).OfType<BracketsNode>())
        {
            var owner = Get(brackets).Bounds;
            var content = Get(brackets.Content).Bounds;
            Qa.Check(content.X > owner.X && content.Right < owner.Right,
                $"{brackets.Kind}괄호의 자식 내용 양쪽에 delimiter 예약 열이 없음: owner={owner}; content={content}");
            Inside(owner, content, brackets.Kind + " 괄호 자식 내용");

            var left = shapes.Where(item => IsCenteredInOwner(item.Bounds, owner) && CenterX(item.Bounds) < content.X).ToArray();
            var right = shapes.Where(item => IsCenteredInOwner(item.Bounds, owner) && CenterX(item.Bounds) > content.Right).ToArray();
            Qa.Equal(1, left.Length, brackets.Kind + " 왼쪽 delimiter가 예약 영역에서 식별되지 않음");
            Qa.Equal(1, right.Length, brackets.Kind + " 오른쪽 delimiter가 예약 영역에서 식별되지 않음");
            Inside(owner, left[0].Bounds, brackets.Kind + " 왼쪽 delimiter 예약 열");
            Inside(owner, right[0].Bounds, brackets.Kind + " 오른쪽 delimiter 예약 열");
            Qa.Check(left[0].Bounds.Right <= content.X + tolerance &&
                right[0].Bounds.X >= content.Right - tolerance,
                $"{brackets.Kind} delimiter 선 두께가 자식 내용 예약 영역을 침범함: left={left[0].Bounds}; content={content}; right={right[0].Bounds}");
            Qa.Check(left[0].Bounds.Y <= content.Y + tolerance &&
                right[0].Bounds.Y <= content.Y + tolerance &&
                left[0].Bounds.Bottom >= content.Bottom - tolerance &&
                right[0].Bounds.Bottom >= content.Bottom - tolerance,
                $"{brackets.Kind} 괄호가 작은 내용만 감싸고 전체 자식 높이를 둘러싸지 않음: left={left[0].Bounds}; content={content}; right={right[0].Bounds}");
        }

        foreach (var cases in Qa.Nodes(workspace).OfType<CasesNode>())
        {
            var owner = Get(cases).Bounds;
            var rowChildren = cases.Rows.SelectMany(row => new[] { row.Expression, row.Condition })
                .Select(Get).ToArray();
            var firstChildX = rowChildren.Min(child => child.Bounds.X);
            var contentTop = rowChildren.Min(child => child.Bounds.Y);
            var contentBottom = rowChildren.Max(child => child.Bounds.Bottom);
            Qa.Check(firstChildX > owner.X, "Cases 좌측 brace 예약 열이 없음");
            var braces = shapes.Where(item => IsCenteredInOwner(item.Bounds, owner) && CenterX(item.Bounds) < firstChildX).ToArray();
            Qa.Equal(1, braces.Length, "Cases 좌측 brace가 예약 영역에서 식별되지 않음");
            Inside(owner, braces[0].Bounds, "Cases brace 예약 열");
            Qa.Check(braces[0].Bounds.Right <= firstChildX + tolerance,
                $"Cases brace 선 두께가 첫 식 bounds를 침범함: brace={braces[0].Bounds}; firstChildX={firstChildX:R}");
            Qa.Check(braces[0].Bounds.Y <= contentTop + tolerance &&
                braces[0].Bounds.Bottom >= contentBottom - tolerance,
                $"Cases brace가 두 행의 자식 bounds 높이를 감싸지 않음: brace={braces[0].Bounds}; children={contentTop:R}..{contentBottom:R}");
        }
    }

    private static EditingSession BlankSession()
    {
        var empty = Qa.Text(string.Empty, isEmptySlot: true);
        return new EditingSession(Qa.Workspace(empty));
    }

    private static MathNode Inserted(EditingSession session, MathTemplateKind kind)
    {
        var id = session.InsertTemplate(kind);
        Qa.Check(id.HasValue, kind + " template 삽입 실패");
        return Qa.Node(session, id!.Value);
    }

    private static Type ExpectedNodeType(MathTemplateKind kind) => kind switch
    {
        MathTemplateKind.Fraction => typeof(FractionNode),
        MathTemplateKind.Radical => typeof(RadicalNode),
        MathTemplateKind.Superscript or MathTemplateKind.Subscript => typeof(ScriptNode),
        MathTemplateKind.Integral => typeof(IntegralNode),
        MathTemplateKind.Summation => typeof(SummationNode),
        MathTemplateKind.Limit => typeof(LimitNode),
        MathTemplateKind.Matrix => typeof(MatrixNode),
        MathTemplateKind.Brackets => typeof(BracketsNode),
        MathTemplateKind.Cases => typeof(CasesNode),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void CheckEmpty(MathNode? node, string label)
    {
        Qa.Check(node is TextNode { Text.Length: 0, IsEmptySlot: true }, label + "이 필수 빈 슬롯 노드가 아님");
    }

    private static void AssertCaretAt(EditingSession session, MathNode? node, string label)
    {
        var text = Qa.Walk(node ?? throw new InvalidOperationException(label + " node가 null임")).OfType<TextNode>().First();
        Qa.Equal(new MathCaret(text.Id, 0), session.Caret, label + "");
    }

    private static void EqualIds(IEnumerable<Guid> expected, IEnumerable<Guid> actual, string label)
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();
        Qa.Check(expectedArray.SequenceEqual(actualArray),
            $"{label}: 기대=[{string.Join(",", expectedArray)}], 실제=[{string.Join(",", actualArray)}]");
    }

    private static IEnumerable<TextNode> MathTreeTextLeaves(MathNode root)
    {
        foreach (var child in MathTree.ChildrenOf(root))
        {
            if (child is TextNode text)
                yield return text;
            else
                foreach (var nested in MathTreeTextLeaves(child))
                    yield return nested;
        }
    }

    private static MathNode AllTemplatesNode()
    {
        var fraction = new FractionNode(Guid.NewGuid(), Qa.Text("fracN"), Qa.Text("fracD"));
        var radical = new RadicalNode(Guid.NewGuid(), Qa.Text("radicand"));
        var script = new ScriptNode(Guid.NewGuid(), Qa.Text("base"), Qa.Text("sub_i"), Qa.Text("sup_2"));
        var matrix = new MatrixNode(Guid.NewGuid(), 2, 2,
            [Qa.Text("matrix11"), Qa.Text("matrix12"), Qa.Text("matrix21"), Qa.Text("matrix22")]);
        var integral = new IntegralNode(Guid.NewGuid(), Qa.Text("f(x)"), Qa.Text("i=0"), Qa.Text("n"), Qa.Text("dx"));
        var sum = new SummationNode(Guid.NewGuid(), Qa.Text("a_k"), Qa.Text("k=1"), Qa.Text("N"));
        var limit = new LimitNode(Guid.NewGuid(), Qa.Text("x→0"), Qa.Text("g(x)"));
        var brackets = new BracketsNode(Guid.NewGuid(), MathBracketKind.Parentheses, Qa.Text("u+v"));
        var cases = new CasesNode(Guid.NewGuid(),
        [
            new CaseRow(Qa.Text("caseA"), Qa.Text("x<0")),
            new CaseRow(Qa.Text("caseB"), Qa.Text("x≥0"))
        ]);
        return Qa.Sequence(fraction, radical, script, matrix, integral, sum, limit, brackets, cases,
            Qa.Text("e\u0301🙂𝑥"));
    }

    private static JsonObject FindKind(JsonNode root, string kind) =>
        Objects(root).First(value => value["$kind"]?.GetValue<string>() == kind);

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var property in obj)
                foreach (var child in Objects(property.Value)) yield return child;
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                foreach (var child in Objects(item)) yield return child;
        }
    }

    private static double Number(XElement element, string name) =>
        double.Parse(element.Attribute(name)!.Value, System.Globalization.CultureInfo.InvariantCulture);

    private static Dictionary<Guid, Guid> MapCloneTree(MathNode original, MathNode clone)
    {
        var map = new Dictionary<Guid, Guid>();
        AddNodePairs(original, clone, map);
        return map;
    }

    private static void AddNodePairs(MathNode original, MathNode clone, Dictionary<Guid, Guid> map)
    {
        Qa.Equal(original.GetType(), clone.GetType(), "template clone node 종류");
        Qa.Check(original.Id != clone.Id, "template clone가 원본 ID 재사용");
        map.Add(original.Id, clone.Id);
        switch (original, clone)
        {
            case (TextNode oldText, TextNode newText):
                Qa.Equal(oldText.Text, newText.Text, "template clone Unicode/text 원문");
                Qa.Equal(oldText.IsEmptySlot, newText.IsEmptySlot, "template clone empty-slot flag");
                break;
            case (SequenceNode oldNode, SequenceNode newNode):
                Qa.Equal(oldNode.Children.Length, newNode.Children.Length, "template clone sequence 길이");
                for (var index = 0; index < oldNode.Children.Length; index++) AddNodePairs(oldNode.Children[index], newNode.Children[index], map);
                break;
            case (FractionNode oldNode, FractionNode newNode):
                AddNodePairs(oldNode.Numerator, newNode.Numerator, map); AddNodePairs(oldNode.Denominator, newNode.Denominator, map); break;
            case (RadicalNode oldNode, RadicalNode newNode):
                AddNodePairs(oldNode.Radicand, newNode.Radicand, map); break;
            case (ScriptNode oldNode, ScriptNode newNode):
                Qa.Equal(oldNode.Subscript is null, newNode.Subscript is null, "template clone subscript optionality");
                Qa.Equal(oldNode.Superscript is null, newNode.Superscript is null, "template clone superscript optionality");
                AddNodePairs(oldNode.Base, newNode.Base, map);
                if (oldNode.Subscript is not null) AddNodePairs(oldNode.Subscript, newNode.Subscript!, map);
                if (oldNode.Superscript is not null) AddNodePairs(oldNode.Superscript, newNode.Superscript!, map);
                break;
            case (MatrixNode oldNode, MatrixNode newNode):
                Qa.Equal(oldNode.Rows, newNode.Rows, "template clone matrix rows"); Qa.Equal(oldNode.Columns, newNode.Columns, "template clone matrix columns");
                for (var index = 0; index < oldNode.Cells.Length; index++) AddNodePairs(oldNode.Cells[index], newNode.Cells[index], map);
                break;
            case (IntegralNode oldNode, IntegralNode newNode):
                Qa.Equal(oldNode.LowerLimit is null, newNode.LowerLimit is null, "template clone integral lower");
                Qa.Equal(oldNode.UpperLimit is null, newNode.UpperLimit is null, "template clone integral upper");
                AddNodePairs(oldNode.Integrand, newNode.Integrand, map);
                if (oldNode.LowerLimit is not null) AddNodePairs(oldNode.LowerLimit, newNode.LowerLimit!, map);
                if (oldNode.UpperLimit is not null) AddNodePairs(oldNode.UpperLimit, newNode.UpperLimit!, map);
                AddNodePairs(oldNode.DifferentialVariable, newNode.DifferentialVariable, map);
                break;
            case (SummationNode oldNode, SummationNode newNode):
                Qa.Equal(oldNode.LowerLimit is null, newNode.LowerLimit is null, "template clone sum lower");
                Qa.Equal(oldNode.UpperLimit is null, newNode.UpperLimit is null, "template clone sum upper");
                AddNodePairs(oldNode.Body, newNode.Body, map);
                if (oldNode.LowerLimit is not null) AddNodePairs(oldNode.LowerLimit, newNode.LowerLimit!, map);
                if (oldNode.UpperLimit is not null) AddNodePairs(oldNode.UpperLimit, newNode.UpperLimit!, map);
                break;
            case (LimitNode oldNode, LimitNode newNode):
                AddNodePairs(oldNode.Approach, newNode.Approach, map); AddNodePairs(oldNode.Body, newNode.Body, map); break;
            case (BracketsNode oldNode, BracketsNode newNode):
                Qa.Equal(oldNode.Kind, newNode.Kind, "template clone bracket kind"); AddNodePairs(oldNode.Content, newNode.Content, map); break;
            case (CasesNode oldNode, CasesNode newNode):
                Qa.Equal(oldNode.Rows.Length, newNode.Rows.Length, "template clone cases rows");
                for (var index = 0; index < oldNode.Rows.Length; index++)
                {
                    AddNodePairs(oldNode.Rows[index].Expression, newNode.Rows[index].Expression, map);
                    AddNodePairs(oldNode.Rows[index].Condition, newNode.Rows[index].Condition, map);
                }
                break;
            default:
                throw new InvalidOperationException("검증되지 않은 template clone 노드 조합");
        }
    }
}
