using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.LogicQA;

internal static class TextRoleCases
{
    public static IEnumerable<QaCase> All()
    {
        yield return new("TEXT-ROLE-SCHEMA", "schema v1 역할 왕복·legacy 기본값·복구 왕복·손상 원본 보존", SchemaAndRecovery);
        yield return new("TEXT-ROLE-LAYOUT", "역할별 italic과 명시 역할 shaping run·caret 폭", LayoutAndShaping);
        yield return new("TEXT-ROLE-LATEX", "역할 macro·공백/예약 문자 escaping·Unicode 및 제어 진단", LatexRolesAndDiagnostics);
        yield return new("TEXT-ROLE-ALIGN-SVG", "역할 한정 등호 정렬과 같은 layout의 SVG 소비", RoleAlignmentAndSvg);
        yield return new("TEXT-ROLE-APPLY", "다섯 필드 원자 Apply·stale/no-op/범위 거절·reset 보존", ApplyAndReset);
        yield return new("TEXT-ROLE-EDIT-PRESERVE", "문자 편집·분할·붙이기 경로와 부분선택 감싸기의 역할 보존", EditAndWrapPreservation);
        yield return new("TEXT-ROLE-TEMPLATE-CLONE", "빈 템플릿 내용·복제·undo에서 역할·ID 보존", TemplateAndClonePreservation);
        yield return new("TEXT-ROLE-VM-DIAGNOSTIC", "속성 패널 범위 안내와 MainVM의 진단/외부 재사용 차단", ViewModelDiagnostics);
    }

    private static async Task SchemaAndRecovery()
    {
        using var directory = new TempDirectory();
        var service = new WorkspaceFileService();
        var identifier = Qa.Text("x_value") with { Role = MathTextRole.Identifier };
        var unit = Qa.Text("m/s") with { Role = MathTextRole.Unit };
        var workspace = Workspace([Group([Row(Qa.Sequence(identifier, unit))])]);
        var session = new EditingSession(workspace);
        var path = directory.File("roles.aethermath");
        await service.SaveAsync(path, session.CaptureSnapshot());
        var bytes = await File.ReadAllBytesAsync(path);
        var json = JsonNode.Parse(bytes)!.AsObject();
        var roleValues = FindTextJsonNodes(json).Select(node => node["role"]?.GetValue<int>()).ToArray();
        Qa.Check(roleValues.Contains((int)MathTextRole.Identifier) && roleValues.Contains((int)MathTextRole.Unit),
            "schema v1 JSON에 명시 역할이 기록되지 않음");
        var reopened = await service.OpenAsync(path);
        Qa.Equal(Qa.Fingerprint(workspace), Qa.Fingerprint(reopened), "파일 저장/열기 역할·ID 왕복");

        var legacy = JsonNode.Parse(bytes)!.AsObject();
        RemovePropertyRecursively(legacy, "role");
        await File.WriteAllTextAsync(path, legacy.ToJsonString());
        var legacyWorkspace = await service.OpenAsync(path);
        Qa.Check(Qa.Nodes(legacyWorkspace).OfType<TextNode>().All(node => node.Role == MathTextRole.Automatic),
            "schema v1에서 Role 누락을 Automatic으로 복원하지 않음");

        var recoveryService = new WorkspaceStateService(directory.Path);
        var recoveryId = Guid.NewGuid();
        var recovery = new WorkspaceRecovery(recoveryId, session.CaptureSnapshot(), DateTimeOffset.UtcNow, path);
        var write = await recoveryService.SaveRecoveryAsync(recovery);
        Qa.Equal(RecoveryWriteStatus.Written, write.Status, "역할이 있는 복구 스냅샷 저장");
        var read = await recoveryService.ReadRecoveryAsync(recoveryId);
        Qa.Equal(RecoveryReadStatus.Found, read.Status, "역할이 있는 복구 스냅샷 읽기");
        Qa.Equal(Qa.Fingerprint(workspace), Qa.Fingerprint(read.Recovery!.Snapshot.Workspace),
            "복구 envelope의 역할·ID 왕복");

        var invalid = JsonNode.Parse(bytes)!.AsObject();
        FindTextJsonNodes(invalid).First()["role"] = 999;
        var invalidBytes = System.Text.Encoding.UTF8.GetBytes(invalid.ToJsonString());
        await File.WriteAllBytesAsync(path, invalidBytes);
        var storedInvalidBytes = await File.ReadAllBytesAsync(path);
        await Qa.FailsAsync(() => service.OpenAsync(path), "정의되지 않은 역할 JSON 거절",
            typeof(InvalidDataException), typeof(System.Text.Json.JsonException));
        var afterRejectedRead = await File.ReadAllBytesAsync(path);
        Qa.Check(storedInvalidBytes.SequenceEqual(afterRejectedRead), "잘못된 역할 파일 원본 bytes가 읽기 거절 중 변경됨");
    }

    private static void LayoutAndShaping()
    {
        var explicitlyUpright = Qa.Text("A1V") with { Role = MathTextRole.Number };
        var automatic = Qa.Text("A1V");
        var identifier = Qa.Text("ab") with { Role = MathTextRole.Identifier };
        var mathSymbol = Qa.Text("=+") with { Role = MathTextRole.MathSymbol };
        var plainText = Qa.Text("Hi") with { Role = MathTextRole.PlainText };
        var unit = Qa.Text("kg") with { Role = MathTextRole.Unit };
        var condition = Qa.Text("x>0") with { Role = MathTextRole.Condition };
        var roots = new MathNode[] { explicitlyUpright, automatic, identifier, mathSymbol, plainText, unit, condition };
        var metrics = new ShapingMetrics();
        var layout = MathLayoutEngine.Layout(Workspace([Group(roots.Select(Row).ToArray())]), metrics);
        var glyphs = layout.Primitives.OfType<GlyphPrimitive>().ToArray();

        var numberRuns = glyphs.Where(glyph => glyph.NodeId == explicitlyUpright.Id).ToArray();
        Qa.Equal(1, numberRuns.Length, "명시 Number 문자열이 heuristic 문자별 run으로 분리됨");
        Qa.Equal("A1V", numberRuns[0].Text, "명시 역할 전체 shaping run 원문");
        Qa.Check(!numberRuns[0].Italic, "명시 Number glyph가 upright가 아님");
        Qa.Check(metrics.Calls.Any(call => call.Text == "A1V" && !call.Italic),
            "비가산 AV 유사 측정이 명시 역할 전체 run으로 전달되지 않음");
        Qa.Near(23, Node(layout, explicitlyUpright.Id).Bounds.Width,
            "명시 역할 전체 run의 kerning 폭이 node/caret geometry에 반영되지 않음");
        Qa.Equal(4, Node(layout, explicitlyUpright.Id).CaretStops.Length, "세 문자 역할 caret stop 수");
        Qa.Near(Node(layout, explicitlyUpright.Id).Bounds.Right,
            Node(layout, explicitlyUpright.Id).CaretStops[^1].Position.X,
            "shaping run 끝 caret와 node 폭 불일치");

        var automaticRuns = glyphs.Where(glyph => glyph.NodeId == automatic.Id).ToArray();
        Qa.Equal("A|1|V", string.Join('|', automaticRuns.Select(glyph => glyph.Text)),
            "Automatic의 기존 문자별 italic run 분할 보존");
        Qa.Check(automaticRuns[0].Italic && !automaticRuns[1].Italic && automaticRuns[2].Italic,
            "Automatic 문자별 italic heuristic 변경");
        foreach (var text in roots.OfType<TextNode>().Where(node =>
                     node.Role is not (MathTextRole.Automatic or MathTextRole.Identifier)))
        {
            var nodeGlyphs = glyphs.Where(glyph => glyph.NodeId == text.Id).ToArray();
            Qa.Check(nodeGlyphs.Length > 0 && nodeGlyphs.All(glyph => !glyph.Italic),
                "명시 upright 역할의 glyph 상태 " + text.Role);
            Qa.Equal(Qa.Elements(text.Text) + 1, Node(layout, text.Id).CaretStops.Length,
                "역할별 Unicode 문자소 caret stop 수 " + text.Role);
        }
        Qa.Check(glyphs.Where(glyph => glyph.NodeId == identifier.Id).All(glyph => glyph.Italic),
            "Identifier 역할이 italic이 아님");
    }

    private static void LatexRolesAndDiagnostics()
    {
        AssertLatex("x", MathTextRole.Automatic, "{\\displaystyle x}", "Automatic byte-compatible 기본 직렬화");
        AssertLatex("x_i", MathTextRole.Identifier, "{\\displaystyle \\mathit{x\\_i}}", "Identifier mathit 및 예약 문자");
        AssertLatex("x+1", MathTextRole.MathSymbol, "{\\displaystyle \\mathrm{x+1}}", "MathSymbol mathrm");
        AssertLatex("a b^~\\", MathTextRole.MathSymbol,
            "{\\displaystyle \\mathrm{a\\ b\\text{\\textasciicircum{}}\\text{\\textasciitilde{}}\\backslash{}}}",
            "MathSymbol 공백·위첨자·물결·역슬래시 escaping");
        AssertLatex("12", MathTextRole.Number, "{\\displaystyle \\mathrm{12}}", "Number mathrm");
        AssertLatex("m/s", MathTextRole.Unit, "{\\displaystyle \\mathrm{m/s}}", "Unit mathrm");
        AssertLatex("a b  #$%&_{}\\^~한", MathTextRole.PlainText,
            "{\\displaystyle \\text{a\\ b\\ \\ \\#\\$\\%\\&\\_\\{\\}\\textbackslash{}\\textasciicircum{}\\textasciitilde{}한}}",
            "PlainText amsmath text macro, 연속 공백·예약 문자·Unicode 보존");
        AssertLatex("if x>0", MathTextRole.Condition,
            "{\\displaystyle \\text{if\\ x>0}}", "Condition text macro");

        const string controlText = "A😀\u0001B\u0085C\u007F\r\n\tZ";
        var controlled = Qa.Text(controlText) with { Role = MathTextRole.PlainText };
        var workspace = Workspace([Group([Row(controlled)])]);
        var code = LatexSerializer.ToLatex(workspace);
        Qa.Check(code.Contains(controlText, StringComparison.Ordinal), "제어 문자가 LaTeX code 생성에서 제거/치환됨");
        var diagnostics = LatexSerializer.GetTextDiagnostics(workspace);
        Qa.Equal(6, diagnostics.Length, "C0/C1/CR/LF/TAB 제어 code point 진단 개수");
        var expected = new[] { (3, 0x01), (5, 0x85), (7, 0x7F), (8, 0x0D), (9, 0x0A), (10, 0x09) };
        for (var index = 0; index < expected.Length; index++)
        {
            Qa.Equal(controlled.Id, diagnostics[index].NodeId, "진단 node ID");
            Qa.Equal(MathTextRole.PlainText, diagnostics[index].Role, "진단 role");
            Qa.Equal(expected[index].Item1, diagnostics[index].Utf16Index, "진단 UTF16 위치");
            Qa.Equal(expected[index].Item2, diagnostics[index].CodePoint, "진단 code point");
            Qa.Equal($"U+{expected[index].Item2:X4}", diagnostics[index].CodePointLabel, "진단 codepoint 라벨");
            Qa.Check(!string.IsNullOrWhiteSpace(diagnostics[index].Reason), "진단 사유 누락");
        }

        var incomplete = Qa.Text(string.Empty, isEmptySlot: true) with { Role = MathTextRole.Condition };
        var incompleteWorkspace = Workspace([Group([Row(new FractionNode(Guid.NewGuid(), controlled, incomplete))])]);
        Qa.Equal(1, LatexSerializer.GetIncompleteSlotNodeIds(incompleteWorkspace).Length,
            "필수 빈 슬롯 ID 목록과 텍스트 진단 분리");
        Qa.Check(LatexSerializer.GetTextDiagnostics(incompleteWorkspace).Any(diagnostic => diagnostic.NodeId == controlled.Id),
            "중첩 구조에서 제어 진단 누락");
    }

    private static void RoleAlignmentAndSvg()
    {
        var plainEquals = Qa.Text("literal=") with { Role = MathTextRole.PlainText };
        var alignedSymbol = Qa.Text("x=1") with { Role = MathTextRole.MathSymbol };
        var nestedSequence = Qa.Sequence(alignedSymbol);
        var firstRoot = Qa.Sequence(plainEquals, nestedSequence);
        var unalignedPlain = Qa.Text("other=3") with { Role = MathTextRole.PlainText };
        var hiddenEquals = Qa.Text("a=b") with { Role = MathTextRole.MathSymbol };
        var fraction = new FractionNode(Guid.NewGuid(), hiddenEquals, Qa.Text("d"));
        var aligned = Group([Row(firstRoot), Row(unalignedPlain), Row(fraction)]) with
        {
            Alignment = AlignmentMode.Equals
        };
        var workspace = Workspace([aligned]);
        var before = Qa.Fingerprint(workspace);
        var layout = MathLayoutEngine.Layout(workspace, new ShapingMetrics());
        var rows = layout.LogicalRows;
        Qa.Check(rows[0].AlignmentX is not null, "수평 Sequence 안 실제 MathSymbol 등호를 찾지 못함");
        Qa.Equal<double?>(null, rows[1].AlignmentX, "PlainText 등호가 정렬점으로 사용됨");
        Qa.Equal<double?>(null, rows[2].AlignmentX, "분수 내부 등호가 행 정렬점으로 전파됨");
        var symbolLayout = Node(layout, alignedSymbol.Id);
        Qa.Near(rows[0].AlignmentX!.Value, symbolLayout.CaretStops[1].Position.X,
            "nested sequence 절대 X 정렬 관계");

        var latex = LatexSerializer.ToLatex(workspace);
        Qa.Equal(1, CountOccurrences(latex, "&="), "MathSymbol의 최상위 등호만 aligned marker로 변환");
        Qa.Check(latex.Contains("\\mathrm{x}}&={\\displaystyle \\mathrm{1}}", StringComparison.Ordinal),
            "중첩 Sequence MathSymbol LaTeX 정렬 token 위치");
        Qa.Check(latex.Contains("\\text{literal=}", StringComparison.Ordinal) &&
                 latex.Contains("\\text{other=3}", StringComparison.Ordinal),
            "PlainText 등호 원문이 text macro 안에 남아 있음");
        Qa.Check(latex.Contains("\\mathrm{a=b}", StringComparison.Ordinal),
            "분수 내부 수학 등호가 role macro에서 보존됨");

        var automaticCombining = Qa.Text("=\u0338");
        var symbolCombining = Qa.Text("=\u0338") with { Role = MathTextRole.MathSymbol };
        var automaticVariation = Qa.Text("=\uFE0F");
        var symbolVariation = Qa.Text("=\uFE0F") with { Role = MathTextRole.MathSymbol };
        var sequenceAutomaticText = Qa.Text("=\u0338");
        var rootSequence = Qa.Sequence(sequenceAutomaticText);
        var automaticAfterCombining = Qa.Text("=\u0338=x=y");
        var symbolAfterCombining = Qa.Text("=\u0338=x=y") with { Role = MathTextRole.MathSymbol };
        var automaticUnstyledChild = Qa.Text("=\u0338");
        var nestedWithoutStyle = Qa.Sequence(new RadicalNode(Guid.NewGuid(), Qa.Sequence(automaticUnstyledChild)));
        var automaticSequenceCombined = Qa.Text("=\u0338");
        var automaticSequenceStandalone = Qa.Text("z=x");
        var automaticSequenceAfterCombining = Qa.Sequence(automaticSequenceCombined, automaticSequenceStandalone);
        var symbolSequenceCombined = Qa.Text("=\u0338") with { Role = MathTextRole.MathSymbol };
        var symbolSequenceStandalone = Qa.Text("z=x") with { Role = MathTextRole.MathSymbol };
        var symbolSequenceAfterCombining = Qa.Sequence(symbolSequenceCombined, symbolSequenceStandalone);
        var nestedUnstyledCombined = Qa.Text("=\u0338");
        var nestedUnstyledStandalone = Qa.Text("z=x");
        var nestedUnstyledThenStandalone = Qa.Sequence(
            new RadicalNode(Guid.NewGuid(), Qa.Sequence(nestedUnstyledCombined)), nestedUnstyledStandalone);
        var graphemeGroup = Group([
            Row(automaticCombining), Row(symbolCombining), Row(automaticVariation), Row(symbolVariation), Row(rootSequence),
            Row(automaticAfterCombining), Row(symbolAfterCombining), Row(nestedWithoutStyle),
            Row(automaticSequenceAfterCombining), Row(symbolSequenceAfterCombining), Row(nestedUnstyledThenStandalone)
        ]) with { Alignment = AlignmentMode.Equals };
        var graphemeWorkspace = Workspace([graphemeGroup]);
        var graphemeLayout = MathLayoutEngine.Layout(graphemeWorkspace, new ShapingMetrics());
        var graphemeRows = graphemeLayout.LogicalRows;
        Qa.Equal(11, graphemeRows.Length, "조합 등호 경계 fixture의 실제 행 수");
        Qa.Equal(1, Qa.Elements(automaticCombining.Text), "combining overlay 등호는 단일 grapheme");
        Qa.Equal(1, Qa.Elements(automaticVariation.Text), "variation selector 등호는 단일 grapheme");
        Qa.Equal(1, Qa.Elements(automaticSequenceCombined.Text), "Sequence 첫 Automatic combining 등호는 단일 grapheme");
        Qa.Equal(1, Qa.Elements(symbolSequenceCombined.Text), "Sequence 첫 MathSymbol combining 등호는 단일 grapheme");
        Qa.Equal(1, Qa.Elements(nestedUnstyledCombined.Text), "구조 안 no-style combining 등호는 단일 grapheme");
        foreach (var index in new[] { 0, 1, 2, 3, 4, 7 })
            Qa.Equal<double?>(null, graphemeRows[index].AlignmentX,
                "조합 등호만 있거나 구조 내부인 행에 alignment anchor가 생김: " + index);
        foreach (var (index, text) in new[]
                 {
                     (5, automaticAfterCombining),
                     (6, symbolAfterCombining)
                 })
        {
            Qa.Check(graphemeRows[index].AlignmentX is not null,
                "조합 grapheme 뒤의 실제 standalone 등호 anchor 누락: " + index);
            Qa.Equal(Qa.Elements(text.Text) + 1, Node(graphemeLayout, text.Id).CaretStops.Length,
                "combining mark가 grapheme caret 경계를 분리: " + index);
            Qa.Near(Node(graphemeLayout, text.Id).CaretStops[1].Position.X,
                graphemeRows[index].AlignmentX!.Value,
                "anchor가 결합 등호가 아니라 첫 standalone 등호 뒤에 놓임: " + index);
        }
        foreach (var (rowIndex, text) in new[]
                 {
                     (8, automaticSequenceStandalone),
                     (9, symbolSequenceStandalone),
                     (10, nestedUnstyledStandalone)
                 })
        {
            Qa.Check(graphemeRows[rowIndex].AlignmentX is not null,
                "첫 Sequence child의 combining grapheme가 pending anchor를 소비함: " + rowIndex);
            Qa.Equal(Qa.Elements(text.Text) + 1, Node(graphemeLayout, text.Id).CaretStops.Length,
                "후속 child의 caret grapheme 경계: " + rowIndex);
            Qa.Near(Node(graphemeLayout, text.Id).CaretStops[1].Position.X,
                graphemeRows[rowIndex].AlignmentX!.Value,
                "다음 유효 child의 첫 standalone 등호 anchor: " + rowIndex);
        }

        var graphemeLatex = LatexSerializer.ToLatex(graphemeWorkspace);
        var expectedGraphemeLatex = "\\begin{aligned}" +
            "{\\displaystyle =\u0338} \\\\" +
            "{\\displaystyle \\mathrm{=\u0338}} \\\\" +
            "{\\displaystyle =\uFE0F} \\\\" +
            "{\\displaystyle \\mathrm{=\uFE0F}} \\\\" +
            "{\\displaystyle =\u0338} \\\\" +
            "{\\displaystyle =\u0338}&={\\displaystyle x=y} \\\\" +
            "{\\displaystyle \\mathrm{=\u0338}}&={\\displaystyle \\mathrm{x=y}} \\\\" +
            "{\\displaystyle \\sqrt{=\u0338}} \\\\" +
            "{\\displaystyle =\u0338}{\\displaystyle z}&={\\displaystyle x} \\\\" +
            "{\\displaystyle \\mathrm{=\u0338}}{\\displaystyle \\mathrm{z}}&={\\displaystyle \\mathrm{x}} \\\\" +
            "{\\displaystyle \\sqrt{=\u0338}}{\\displaystyle z}&={\\displaystyle x}\\end{aligned}";
        Qa.Equal(expectedGraphemeLatex, graphemeLatex,
            "Automatic·MathSymbol 조합 등호 원문 보존 및 다음 유효 child의 첫 등호만 marker; root Text/Sequence styled 경로와 구조 내부 no-style 원문 보존");
        Qa.Equal(5, CountOccurrences(graphemeLatex, "&="),
            "조합 grapheme 내부 equals를 marker로 소비하지 않고 각 행의 첫 실제 등호만 정렬");

        var svg = XDocument.Parse(new SvgExportService().ToSvg(layout));
        XNamespace ns = "http://www.w3.org/2000/svg";
        var svgText = svg.Descendants(ns + "text").ToArray();
        var primitives = layout.Primitives.OfType<GlyphPrimitive>().ToArray();
        Qa.Equal(string.Join('|', primitives.Select(glyph => glyph.Text)),
            string.Join('|', svgText.Select(element => element.Value)), "동일 LayoutResult의 SVG 글리프 순서/문자열");
        Qa.Check(svgText.Any(element => element.Value == "x=1" &&
            ((string?)element.Attribute("font-style") ?? "normal") == "normal"),
            "MathSymbol의 upright glyph 상태가 SVG 소비자에 전달되지 않음");
        Qa.Equal(before, Qa.Fingerprint(workspace), "정렬/layout/SVG가 AST·role을 수정");
    }

    private static void ApplyAndReset()
    {
        var target = Qa.Text("abcdef");
        var sibling = Qa.Text("q") with { Role = MathTextRole.Unit };
        var fraction = new FractionNode(Guid.NewGuid(), target, sibling);
        var session = new EditingSession(Workspace([Group([Row(fraction)])]));
        Qa.Select(session, target.Id, 1, 4);
        session.MarkSaved(session.CaptureSnapshot());
        var before = Qa.Fingerprint(session.Workspace);
        var caret = session.Caret;
        var selection = session.Selection;
        var stateId = session.CaptureSnapshot().StateId;
        var revision = session.Revision;
        var snapshot = Capture(session, MathPropertyScope.SelectionNode);
        var patch = new MathPropertiesPatch(new MathStylePatch(29, "Role Face", "#80445566"),
            MathTypesettingStyle.Text, MathTextRole.Identifier);
        var applied = session.ApplyMathProperties(snapshot, patch);
        Qa.Equal(MathPropertyChangeStatus.Applied, applied.Status, "5필드 combined role/style Apply");
        Qa.Equal(revision + 1, session.Revision, "5필드 Apply가 단일 commit이 아님");
        Qa.Equal(caret, session.Caret, "role Apply caret 보존");
        Qa.Equal(selection, session.Selection, "부분 텍스트 role Apply selection 보존");
        var changedLeaf = Qa.Leaf(session, target.Id);
        Qa.Equal(MathTextRole.Identifier, changedLeaf.Role, "부분 선택에서 전체 TextNode 역할 변경");
        Qa.Equal(new MathStyleOverride(29, "Role Face", "#80445566")
            { TypesettingStyle = MathTypesettingStyle.Text }, changedLeaf.StyleOverride,
            "role과 네 style field 원자 저장");
        Qa.Equal(MathTextRole.Unit, Qa.Leaf(session, sibling.Id).Role, "역할 변경이 sibling에 전파됨");
        var after = Qa.Fingerprint(session.Workspace);

        var same = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode), patch);
        Qa.Equal(MathPropertyChangeStatus.NoChange, same.Status, "동일 저장 role/style patch no-op");
        var invalid = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(new MathStylePatch(), TextRole: (MathTextRole)999));
        Qa.Equal(MathPropertyChangeStatus.InvalidValue, invalid.Status, "정의되지 않은 역할 enum 거절");
        var currentRow = Capture(session, MathPropertyScope.CurrentRow);
        var unsupported = session.ApplyMathProperties(currentRow,
            new MathPropertiesPatch(new MathStylePatch(), TextRole: MathTextRole.Number));
        Qa.Equal(MathPropertyChangeStatus.UnsupportedScope, unsupported.Status, "행 범위 role 변경 거절");
        Qa.Equal(after, Qa.Fingerprint(session.Workspace), "무효/unsupported role patch가 구조를 바꿈");
        Qa.Equal(revision + 1, session.Revision, "무효/unsupported role patch가 history/revision을 바꿈");

        var stale = Capture(session, MathPropertyScope.SelectionNode);
        Qa.Caret(session, target.Id, 6);
        Qa.Check(session.InsertText("!"), "stale target 유발 편집");
        var staleResult = session.ApplyMathProperties(stale,
            new MathPropertiesPatch(new MathStylePatch(Foreground: "#123456"), TextRole: MathTextRole.Number));
        Qa.Equal(MathPropertyChangeStatus.StaleTarget, staleResult.Status, "revision 변경 뒤 오래된 target 적용");
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, target.Id).Role, "stale patch role 반영");

        var resetTarget = Capture(session, MathPropertyScope.SelectionNode);
        var resetRevision = session.Revision;
        var reset = session.ResetMathStyleOverride(resetTarget);
        Qa.Equal(MathPropertyChangeStatus.Applied, reset.Status, "style override reset");
        Qa.Equal(resetRevision + 1, session.Revision, "style reset 단일 commit");
        Qa.Equal<MathStyleOverride?>(null, Qa.Leaf(session, target.Id).StyleOverride, "style reset이 네 필드를 모두 지우지 않음");
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, target.Id).Role, "4필드 style reset이 TextRole을 지움");
        session.Undo();
        Qa.Equal("Role Face", Qa.Leaf(session, target.Id).StyleOverride?.FontFamily, "reset undo style 복구");
        session.Undo();
        Qa.Equal("abcdef", Qa.Leaf(session, target.Id).Text, "텍스트 편집 undo 이전 내용");
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(session, target.Id).Role, "텍스트 편집 undo가 역할을 잃음");
        session.Undo();
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "combined role/style Apply undo 전체 상태");
        Qa.Equal(stateId, session.CaptureSnapshot().StateId, "역할 Apply undo가 저장 StateId를 복구하지 않음");
        Qa.Check(!session.IsDirty, "역할 Apply undo dirty 상태");
        session.Redo();
        Qa.Equal(after, Qa.Fingerprint(session.Workspace), "combined role/style Apply redo 전체 상태");
        var automaticResult = session.ApplyMathProperties(Capture(session, MathPropertyScope.SelectionNode),
            new MathPropertiesPatch(new MathStylePatch(), TextRole: MathTextRole.Automatic));
        Qa.Equal(MathPropertyChangeStatus.Applied, automaticResult.Status, "Automatic 역할 복귀 적용");
        Qa.Equal(MathTextRole.Automatic, Qa.Leaf(session, target.Id).Role,
            "Automatic 선택이 명시 role을 초기화");
        Qa.Equal(new MathStyleOverride(29, "Role Face", "#80445566")
            { TypesettingStyle = MathTypesettingStyle.Text }, Qa.Leaf(session, target.Id).StyleOverride,
            "Automatic 역할 복귀가 독립 style override를 지움");
    }

    private static async Task EditAndWrapPreservation()
    {
        var original = Qa.Text("left+right") with { Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(Foreground: "#AA224466") };
        var session = new EditingSession(Workspace([Group([Row(original)])]));
        Qa.Select(session, original.Id, 5, 10);
        var before = Qa.Fingerprint(session.Workspace);
        Qa.Check(session.InsertText("unit"), "부분 선택 대체 입력");
        Qa.Equal("left+unit", Qa.Leaf(session, original.Id).Text, "부분 선택 원문/이웃 텍스트 유지");
        Qa.Equal(MathTextRole.Condition, Qa.Leaf(session, original.Id).Role, "문자 대체가 역할을 초기화");
        Qa.Equal(original.StyleOverride, Qa.Leaf(session, original.Id).StyleOverride, "문자 대체가 explicit style을 초기화");
        session.Undo();
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "부분 입력 undo role/ID");

        Qa.Select(session, original.Id, 5, 10);
        var selectedBefore = Qa.Fingerprint(session.Workspace);
        var wrapperId = session.InsertFraction();
        Qa.Check(wrapperId.HasValue, "role text 부분 선택 분수 wrapping");
        var wrapped = (FractionNode)Qa.Node(session, wrapperId!.Value);
        Qa.Equal(original.Id, wrapped.Numerator.Id, "감싼 선택 content ID 보존");
        Qa.Equal(MathTextRole.Condition, ((TextNode)wrapped.Numerator).Role, "감싼 선택 content role 보존");
        Qa.Equal(original.StyleOverride, wrapped.Numerator.StyleOverride, "감싼 선택 content explicit style 보존");
        var wrappedTexts = Qa.Walk(wrapped).OfType<TextNode>().ToArray();
        Qa.Check(wrappedTexts.Where(node => node.Id != original.Id).All(node => node.Role == MathTextRole.Automatic),
            "새 prefix/suffix/template slot에 선택 역할이 잘못 복제됨");
        Qa.Equal("left+right", string.Concat(Qa.Nodes(session.Workspace).OfType<TextNode>().Select(node => node.Text)),
            "감싸기 앞/선택/뒤 문자열 손실");
        var wrappedFingerprint = Qa.Fingerprint(session.Workspace);
        session.Undo();
        Qa.Equal(selectedBefore, Qa.Fingerprint(session.Workspace), "부분 감싸기 undo role/style/ID");
        session.Redo();
        Qa.Equal(wrappedFingerprint, Qa.Fingerprint(session.Workspace), "부분 감싸기 redo role/style/ID");

        var splitSource = Qa.Text("A1V") with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(Foreground: "#AA224466")
        };
        var splitSession = new EditingSession(Workspace([Group([Row(splitSource)])]));
        Qa.Caret(splitSession, splitSource.Id, 1);
        var template = splitSession.InsertTemplate(MathTemplateKind.Subscript);
        Qa.Check(template.HasValue, "caret 분할 구조 삽입");
        var templateId = template ?? throw new InvalidOperationException("caret 분할 구조 삽입 결과가 null입니다.");
        var splitRoot = (SequenceNode)splitSession.Workspace.Groups.Single().Rows.Single().Root;
        var prefix = splitRoot.Children.OfType<TextNode>().Single(node => node.Id == splitSource.Id);
        var suffix = splitRoot.Children.OfType<TextNode>().Single(node => node.Id != splitSource.Id);
        Qa.Equal("A", prefix.Text, "분할 template 기존 ID가 prefix에 보존됨");
        Qa.Equal("1V", suffix.Text, "분할 template suffix 문자열 보존");
        foreach (var piece in new[] { prefix, suffix })
        {
            Qa.Equal(MathTextRole.Condition, piece.Role, "caret template 분할 주변 역할 보존");
            Qa.Equal(splitSource.StyleOverride, piece.StyleOverride, "caret template 분할 주변 style 보존");
        }
        var splitWrapper = Qa.Node(splitSession, templateId);
        Qa.Equal<MathStyleOverride?>(null, splitWrapper.StyleOverride, "분할 template wrapper에 주변 style이 복사됨");
        Qa.Check(Qa.Walk(splitWrapper).OfType<TextNode>().All(node =>
                node.Role == MathTextRole.Automatic && node.StyleOverride is null),
            "분할 template의 새 슬롯에 주변 역할/style이 잘못 복사됨");
        Qa.Equal("A1V", string.Concat(splitRoot.Children.SelectMany(Qa.Walk)
            .OfType<TextNode>().Where(node => !node.IsEmptySlot).Select(node => node.Text)),
            "분할 template 주변 원문 순서 보존");

        var pasteSource = Qa.Text("left+right") with
        {
            Role = MathTextRole.Condition,
            StyleOverride = new MathStyleOverride(Foreground: "#AA224466")
        };
        var pasteSession = new EditingSession(Workspace([Group([Row(pasteSource)])]));
        var pasteBeforeFingerprint = Qa.Fingerprint(pasteSession.Workspace);
        Qa.Caret(pasteSession, pasteSource.Id, 5);
        var pasteControl = new MathEditorControl { Session = pasteSession };
        pasteControl.SetVisualTreeAttachmentState(attached: true);
        try
        {
            pasteControl.ClipboardTextReaderOverride = _ => Task.FromResult<string?>("paste");
            var pasteResult = await pasteControl.PasteFromClipboardAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Qa.Equal(ClipboardPasteResult.Applied, pasteResult, "제어 reader를 통한 붙여넣기 결과");
            Qa.Equal("left+pasteright", Qa.Leaf(pasteSession, pasteSource.Id).Text,
                "붙여넣기 원문과 주변 문자 보존");
            Qa.Equal(MathTextRole.Condition, Qa.Leaf(pasteSession, pasteSource.Id).Role,
                "붙여넣기가 TextRole을 초기화");
            Qa.Equal(pasteSource.StyleOverride, Qa.Leaf(pasteSession, pasteSource.Id).StyleOverride,
                "붙여넣기가 기존 명시 style을 초기화");
            var pastedFingerprint = Qa.Fingerprint(pasteSession.Workspace);
            pasteSession.Undo();
            Qa.Equal(pasteBeforeFingerprint, Qa.Fingerprint(pasteSession.Workspace),
                "붙여넣기 undo가 role/style/ID 원문을 복원");
            pasteSession.Redo();
            Qa.Equal(pastedFingerprint, Qa.Fingerprint(pasteSession.Workspace),
                "붙여넣기 redo가 role/style/ID를 복원");
        }
        finally
        {
            pasteControl.SetVisualTreeAttachmentState(attached: false);
        }
    }

    private static void TemplateAndClonePreservation()
    {
        var kinds = new[]
        {
            MathTemplateKind.Subscript, MathTemplateKind.Integral, MathTemplateKind.Summation,
            MathTemplateKind.Limit, MathTemplateKind.Brackets, MathTemplateKind.Cases
        };
        foreach (var kind in kinds)
        {
            var source = Qa.Text(string.Empty, isEmptySlot: true) with
            {
                Role = MathTextRole.Unit,
                StyleOverride = new MathStyleOverride(FontSize: 21, FontFamily: "Slot Face", Foreground: "#224466")
            };
            var session = new EditingSession(Workspace([Group([Row(source)])]));
            var before = Qa.Fingerprint(session.Workspace);
            var wrapperId = session.InsertTemplate(kind);
            Qa.Check(wrapperId.HasValue, kind + " 빈 슬롯 template 삽입");
            var wrapper = Qa.Node(session, wrapperId!.Value);
            var content = Qa.Walk(wrapper).OfType<TextNode>().Single(node => node.Id == source.Id);
            Qa.Equal(MathTextRole.Unit, content.Role, kind + " 기존 빈 content role 보존");
            Qa.Equal(source.StyleOverride, content.StyleOverride, kind + " 기존 빈 content style 보존");
            Qa.Equal<MathStyleOverride?>(null, wrapper.StyleOverride, kind + " content style이 새 wrapper로 잘못 이동");
            Qa.Check(Qa.Walk(wrapper).OfType<TextNode>().Where(node => node.Id != source.Id)
                .All(node => node.Role == MathTextRole.Automatic), kind + " 새 빈 슬롯 역할 초기값");
            Qa.Check(Qa.Walk(wrapper).OfType<TextNode>().Any(node => node.Id == session.Caret.NodeId),
                kind + " 기존 focus 목적지의 유효 텍스트 slot 보존");

            var beforeClone = Qa.Fingerprint(session.Workspace);
            var originalRoleIds = Qa.Walk(wrapper).OfType<TextNode>().Where(node => node.Role == MathTextRole.Unit)
                .Select(node => node.Id).ToHashSet();
            Qa.Check(session.DuplicateCurrentRow(), kind + " 역할이 있는 행 복제");
            var cloneRoleNodes = Qa.Nodes(session.Workspace).OfType<TextNode>()
                .Where(node => node.Role == MathTextRole.Unit && !originalRoleIds.Contains(node.Id)).ToArray();
            Qa.Equal(1, cloneRoleNodes.Length, kind + " clone이 역할을 새 ID에 보존");
            Qa.Equal(source.StyleOverride, cloneRoleNodes[0].StyleOverride, kind + " clone explicit style 보존");
            var clonedFingerprint = Qa.Fingerprint(session.Workspace);
            session.Undo();
            Qa.Equal(beforeClone, Qa.Fingerprint(session.Workspace), kind + " 행 복제 undo 역할/ID");
            session.Redo();
            Qa.Equal(clonedFingerprint, Qa.Fingerprint(session.Workspace), kind + " 행 복제 redo 역할/ID");
            session.Undo();
            session.Undo();
            Qa.Equal(before, Qa.Fingerprint(session.Workspace), kind + " template/clone history 원상복귀");
        }
    }

    private static void ViewModelDiagnostics()
    {
        var vm = new MainWindowViewModel();
        Qa.Equal(WorkspaceActionRequestResult.Completed, vm.RequestNewWorkspace(), "미완성 진단 fixture 빈 작업 생성");
        var empty = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().Single(node => node.IsEmptySlot);
        Qa.Check(!vm.CanExportLatex && vm.LatexIncompleteSlotNodeIds.Contains(empty.Id),
            "필수 빈 슬롯만으로 external LaTeX reuse 차단 및 ID 진단");
        Qa.Check(vm.LatexTextDiagnostics.IsEmpty && vm.LatexCodeDiagnostic.Contains("미완성", StringComparison.Ordinal),
            "빈 슬롯 안내와 text control 진단 구분");
        Qa.Check(!string.IsNullOrWhiteSpace(vm.LatexCode), "미완성 작업의 코드 미리보기까지 생성 차단");

        Qa.Check(vm.Session.InsertText("A\u0001B"), "MainVM 제어문자 진단 fixture 입력");
        Qa.Check(!vm.CanExportLatex && vm.LatexIncompleteSlotNodeIds.IsEmpty,
            "완성 슬롯에서 제어 문자 진단만으로 external reuse 차단");
        var diagnostic = vm.LatexTextDiagnostics.Single();
        Qa.Equal(MathTextRole.Automatic, diagnostic.Role, "MainVM LatexTextDiagnostics role 연결");
        Qa.Equal(1, diagnostic.Utf16Index, "MainVM LatexTextDiagnostics UTF16 offset 연결");
        Qa.Check(vm.LatexCodeDiagnostic.Contains("제어 문자", StringComparison.Ordinal),
            "제어문자 외부 재사용 진단 표시");

        var text = Qa.Nodes(vm.Session.Workspace).OfType<TextNode>().Single(node => node.Text == "A\u0001B");
        Qa.Caret(vm.Session, text.Id, 0);
        vm.Properties.SelectedScope = MathPropertyScope.SelectionNode;
        vm.Properties.DraftTextRole = MathTextRole.Identifier;
        Qa.Check(vm.Properties.CanApply, "선택한 TextNode role draft 적용 가능");
        vm.Properties.ApplyCommand.Execute(null);
        Qa.Equal(MathTextRole.Identifier, Qa.Leaf(vm.Session, text.Id).Role, "Properties combined role Apply 경로");
        var fractionId = vm.Session.InsertFraction();
        Qa.Check(fractionId.HasValue, "unsupported structure scope fixture");
        Qa.Check(vm.Session.TrySelectNode(fractionId!.Value), "구조 노드 선택");
        vm.Properties.SelectedScope = MathPropertyScope.SelectionNode;
        Qa.Check(!vm.Properties.CanEditTextRole && !string.IsNullOrWhiteSpace(vm.Properties.TextRoleDisabledReason),
            "구조 노드 대상에 role 편집 가능 상태/사유");
    }

    private static void AssertLatex(string text, MathTextRole role, string expected, string label)
    {
        var leaf = Qa.Text(text) with { Role = role };
        Qa.Equal(expected, LatexSerializer.ToLatex(Workspace([Group([Row(leaf)])])), label);
    }

    private static IEnumerable<JsonObject> FindTextJsonNodes(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["$kind"]?.GetValue<string>() == "text")
                yield return obj;
            foreach (var property in obj.ToArray())
                foreach (var child in FindTextJsonNodes(property.Value))
                    yield return child;
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                foreach (var child in FindTextJsonNodes(item))
                    yield return child;
        }
    }

    private static void RemovePropertyRecursively(JsonNode? node, string name)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(name);
            foreach (var property in obj.ToArray())
                RemovePropertyRecursively(property.Value, name);
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                RemovePropertyRecursively(item, name);
        }
    }

    private static MathPropertyTargetSnapshot Capture(EditingSession session, MathPropertyScope scope)
    {
        Qa.Check(session.TryCaptureMathPropertyTarget(scope, out var target) && target is not null,
            "속성 대상 캡처: " + scope);
        return target!;
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var position = 0;
        while ((position = value.IndexOf(token, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += token.Length;
        }
        return count;
    }

    private static NodeLayout Node(LayoutResult layout, Guid id) => layout.Nodes.Single(node => node.NodeId == id);
    private static EquationRow Row(MathNode root) => new(Guid.NewGuid(), root);
    private static EquationGroup Group(EquationRow[] rows) => new(Guid.NewGuid(), [.. rows], AlignmentMode.None, 20);
    private static EquationWorkspace Workspace(EquationGroup[] groups) => new(Guid.NewGuid(), 1, "텍스트 역할 QA",
        [.. groups], new MathStyle(30, "Test Face", "#203040"));

    private sealed class ShapingMetrics : IMathTextMetrics
    {
        public List<(string Text, bool Italic)> Calls { get; } = [];

        public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
        {
            Calls.Add((text, italic));
            var elements = Qa.Elements(text);
            if (text == "A1V")
                return new TextMeasurement(23, 18, 6, [0, 7, 13, 23]);
            var advance = fontSize * (italic ? 0.53 : 0.50);
            var carets = ImmutableArray.CreateBuilder<double>(elements + 1);
            for (var index = 0; index <= elements; index++)
                carets.Add(index * advance);
            return new TextMeasurement(elements * advance, fontSize * 0.72, fontSize * 0.23,
                carets.MoveToImmutable());
        }
    }
}
