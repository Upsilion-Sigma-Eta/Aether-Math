using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AetherMath.Core;
using AetherMath.Desktop;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Rendering;
using Avalonia.Media;

namespace AetherMath.LogicQA;

internal static class LayoutSvgCases
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static LayoutResult Layout(EquationWorkspace workspace, double width = 1200) =>
        MathLayoutEngine.Layout(workspace, new AvaloniaMathTextMetrics(), new MathLayoutOptions(AvailableWidth: width));

    public static IEnumerable<QaCase> All()
    {
        yield return new("LAY-01", "최상위 등호 정렬과 논리 행 순서", Alignment);
        yield return new("LAY-02", "실제 폰트 측정의 중첩 bounds/baseline/caret stop", MeasuredGeometry);
        yield return new("LAY-03", "반복/폭변경 레이아웃의 결정성·원본/선택 보존", LayoutPurity);
        yield return new("SEL-04", "실제 레이아웃 caret hit-test로 내부 슬롯만 편집", HitTestEditing);
        yield return new("SVG-01", "모든 SVG primitive와 공통 LayoutResult 수치 비교", SvgGeometry);
        yield return new("SVG-02", "XML escape·Unicode 원문·편집 표시 배제", SvgTextAndSelection);
        yield return new("SVG-FILE", "SVG 실제 파일 출력/교체/취소 안전성", SvgFile);
        yield return new("SVG-04", "빈 슬롯의 노드별 미완성 진단과 출력 파일 보존", EmptySlotExport);
        yield return new("TPL-SVG", "시작/끝/선택 감싸기 템플릿 완성 후 접합노드 오탐 없이 출력", CompletedTemplates);
        yield return new("SLOT-INVARIANT", "필수 슬롯 flag 불일치로 미완성 출력 우회 금지", InvalidSlotFlag);
        yield return new("STRUCT-SELECT", "실제 hit 구조대상/상위선택은 비dirty, 삭제 undo는 전체선택 복구", StructureSelection);
    }

    private static EquationWorkspace Sample()
    {
        var denominator = new FractionNode(Guid.NewGuid(), Qa.Text("𝑥+e\u0301"),
            new RadicalNode(Guid.NewGuid(), Qa.Text("c+d")));
        var fraction = new FractionNode(Guid.NewGuid(), Qa.Text("a+b"), denominator);
        var script = new ScriptNode(Guid.NewGuid(), Qa.Text("y"), Qa.Text("i"), Qa.Text("2"));
        return Qa.Workspace(Qa.Sequence(Qa.Text("x"), Qa.Text("="), fraction, Qa.Text("+"), script),
            Qa.Sequence(Qa.Text("longer left"), Qa.Text("="), new MatrixNode(Guid.NewGuid(), 2, 2,
                [Qa.Text("11"), Qa.Text("22"), Qa.Text("33"), Qa.Text("44")])));
    }

    private static void Alignment()
    {
        var workspace = Sample(); var result = Layout(workspace);
        var rows = workspace.Groups.SelectMany(g => g.Rows).ToArray();
        Qa.Equal(rows.Length, result.LogicalRows.Length, "논리 행 수");
        Qa.Equal(rows.Length, result.VisualRows.Length, "P0 임의 줄바꿈 없음");
        double? x = null;
        for (int index = 0; index < rows.Length; index++)
        {
            var row = result.LogicalRows[index];
            Qa.Equal(rows[index].Id, row.RowId, "행 ID/순서"); Qa.Equal(index, row.Index, "행 index");
            var alignmentX = row.AlignmentX ?? throw new InvalidOperationException("등호 정렬점 없음");
            var commonX = x ?? alignmentX;
            x = commonX;
            Qa.Near(commonX, alignmentX, "등호 공통 X");
            var equalsId = Qa.Walk(rows[index].Root).OfType<TextNode>().Single(n => n.Text == "=").Id;
            var equalsGlyph = result.Primitives.OfType<GlyphPrimitive>().Single(g => g.NodeId == equalsId);
            Qa.Near(alignmentX, equalsGlyph.BaselineOrigin.X, "실제 등호 glyph X");
            if (index > 0) Qa.Check(row.Bounds.Y >= result.LogicalRows[index - 1].Bounds.Y + result.LogicalRows[index - 1].Bounds.Height, "논리 행 겹침");
        }
    }

    private static void MeasuredGeometry()
    {
        var workspace = Sample(); var result = Layout(workspace);
        var metrics = new AvaloniaMathTextMetrics();
        Qa.Check(double.IsFinite(result.Width) && result.Width > 0 && double.IsFinite(result.Height) && result.Height > 0, "출력 크기");
        foreach (var model in Qa.Nodes(workspace))
        {
            var node = result.Nodes.Single(n => n.NodeId == model.Id);
            var bounds = node.Bounds;
            Qa.Check(double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height), "유한 bounds " + model.Id);
            Qa.Check(bounds.Width >= 0 && bounds.Height > 0, "유효 bounds " + model.Id);
            Qa.Check(bounds.X >= -0.001 && bounds.Y >= -0.001 && bounds.X + bounds.Width <= result.Width + 0.001 && bounds.Y + bounds.Height <= result.Height + 0.001, "출력 경계 밖 노드 " + model.Id);
            Qa.Check(double.IsFinite(node.Baseline), "유한 baseline");
            if (model is not TextNode text) continue;
            Qa.Equal(Qa.Elements(text.Text) + 1, node.CaretStops.Length, "문자소별 caret stop 수 " + text.Text);
            var glyphs = result.Primitives.OfType<GlyphPrimitive>().Where(g => g.NodeId == text.Id).ToArray();
            Qa.Equal(text.Text, string.Concat(glyphs.Select(g => g.Text)), "node별 glyph run 원문 보존");
            int elementOffset = 0;
            foreach (var glyph in glyphs)
            {
                int runElements = Qa.Elements(glyph.Text);
                Qa.Near(node.Baseline, glyph.BaselineOrigin.Y, "glyph run 공통 node baseline");
                var measurement = metrics.Measure(glyph.Text, glyph.FontFamily, glyph.FontSize, glyph.Italic);
                Qa.Equal(runElements + 1, measurement.CaretX.Length, "run 실측 caret 수");
                var actualTypeface = new Typeface(glyph.FontFamily, glyph.Italic ? FontStyle.Italic : FontStyle.Normal);
                var actualText = new FormattedText(glyph.Text, Invariant, FlowDirection.LeftToRight,
                    actualTypeface, glyph.FontSize, Brushes.Black);
                Qa.Near(actualText.WidthIncludingTrailingWhitespace,
                    node.CaretStops[elementOffset + runElements].Position.X - node.CaretStops[elementOffset].Position.X,
                    "실제 glyph run Typeface(Italic 포함)의 advance/caret 정합성 " + glyph.Text);
                for (int i = 0; i <= runElements; i++)
                {
                    var stop = node.CaretStops[elementOffset + i];
                    Qa.Equal(elementOffset + i, stop.TextElementOffset, "run의 전체 노드 caret offset");
                    Qa.Near(glyph.BaselineOrigin.X + measurement.CaretX[i], stop.Position.X,
                        "실측 run caret X " + glyph.Text + "/" + i);
                    Qa.Near(node.Baseline, stop.Position.Y, "run caret 공통 baseline");
                }
                elementOffset += runElements;
            }
            Qa.Equal(Qa.Elements(text.Text), elementOffset, "glyph run의 전체 문자소 수 보존");
        }
        foreach (var glyph in result.Primitives.OfType<GlyphPrimitive>())
            Qa.Check(glyph.FontSize > 0 && double.IsFinite(glyph.FontSize), "glyph font size");
        GeometryAssertions.Structures(workspace, result);
    }

    private static void LayoutPurity()
    {
        var session = new EditingSession(Sample());
        var leaf = Qa.Nodes(session.Workspace).OfType<TextNode>().First(n => n.Text == "c+d");
        Qa.Select(session, leaf.Id, 1, 2);
        var selection = session.Selection; var caret = session.Caret; var before = Qa.Fingerprint(session.Workspace); var revision = session.Revision;
        var wide = Layout(session.Workspace); var repeated = Layout(session.Workspace);
        Qa.Equal(JsonSerializer.Serialize(wide), JsonSerializer.Serialize(repeated), "동일 설정 결정성");
        var narrow = Layout(session.Workspace, 80);
        Qa.Equal(wide.LogicalRows.Length, narrow.LogicalRows.Length, "폭변경 논리 행 수");
        Qa.Equal(wide.VisualRows.Length, narrow.VisualRows.Length, "P0 폭변경 행 분할 없음");
        Qa.Check(narrow.Width >= narrow.Nodes.Max(n => n.Bounds.X + n.Bounds.Width), "좁은 폭 넘침이 잘림");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "레이아웃 원본 불변");
        Qa.Equal(selection, session.Selection, "레이아웃 선택 불변"); Qa.Equal(caret, session.Caret, "레이아웃 caret 불변");
        Qa.Equal(revision, session.Revision, "레이아웃 revision"); Qa.Check(!session.IsDirty && !session.CanUndo, "레이아웃 history");
    }

    private static void HitTestEditing()
    {
        var workspace = Sample(); var layout = Layout(workspace);
        var targets = Qa.Nodes(workspace).OfType<TextNode>().Where(n => n.Text is "c+d" or "2" or "11" or "22" or "33" or "44").ToArray();
        foreach (var target in targets)
        {
            var node = layout.Nodes.Single(n => n.NodeId == target.Id);
            foreach (var stop in node.CaretStops)
            {
                var hit = layout.HitTest(stop.Position);
                Qa.Check(hit.HasValue, "실제 caret 좌표 hit-test 실패 " + target.Text);
                Qa.Equal(new MathCaret(target.Id, stop.TextElementOffset), hit!.Value, "원본 ID/문자소 hit-test");
            }
            var session = new EditingSession(workspace);
            var clicked = layout.HitTest(node.CaretStops[1].Position);
            Qa.Check(clicked.HasValue && session.TrySetCaret(clicked.Value), "클릭 caret 수락 실패");
            session.InsertText("한");
            Qa.OnlyTextChanged(workspace, session.Workspace, target.Id, target.Text.Insert(1, "한"));
            session.Undo(); Qa.Equal(Qa.Fingerprint(workspace), Qa.Fingerprint(session.Workspace), "클릭 편집 undo");
        }
        Qa.Check(layout.HitTest(new(-1000, -1000)) is null, "비수식 외부 좌표 hit-test");
    }

    private static void SvgGeometry()
    {
        var layout = Layout(Sample());
        AssertSvg(layout, new SvgExportService().ToSvg(layout));
    }

    private static void AssertSvg(LayoutResult layout, string svg)
    {
        var document = XDocument.Parse(svg); var root = document.Root!;
        Qa.Equal("svg", root.Name.LocalName, "SVG root");
        Qa.Equal("http://www.w3.org/2000/svg", root.Name.NamespaceName, "SVG namespace");
        Number(layout.Width, root, "width"); Number(layout.Height, root, "height");
        var viewBox = Numbers(Required(root, "viewBox"));
        Qa.Equal(4, viewBox.Length, "viewBox 항목 수"); Qa.Near(0, viewBox[0], "viewBox x"); Qa.Near(0, viewBox[1], "viewBox y");
        Qa.Near(layout.Width, viewBox[2], "viewBox width"); Qa.Near(layout.Height, viewBox[3], "viewBox height");
        var elements = root.Descendants().Where(e => e.Name.LocalName is "text" or "line" or "polyline" or "polygon").ToArray();
        Qa.Equal(layout.Primitives.Length, elements.Length, "그리기 요소 수/편집 표시 배제");
        for (int i = 0; i < layout.Primitives.Length; i++)
        {
            var element = elements[i];
            switch (layout.Primitives[i])
            {
                case GlyphPrimitive glyph:
                    Qa.Equal("text", element.Name.LocalName, "glyph 요소");
                    Qa.Equal(glyph.Text, element.Value, "XML 해제된 원문");
                    Number(glyph.BaselineOrigin.X, element, "x"); Number(glyph.BaselineOrigin.Y, element, "y");
                    Number(glyph.FontSize, element, "font-size");
                    Qa.Equal(glyph.FontFamily, Required(element, "font-family"), "SVG font family");
                    Qa.Equal(glyph.Fill, Required(element, "fill"), "SVG glyph fill");
                    Qa.Equal(glyph.Italic ? "italic" : "normal", (string?)element.Attribute("font-style") ?? "normal", "SVG Italic 보존");
                    break;
                case LinePrimitive line:
                    Qa.Equal("line", element.Name.LocalName, "선 요소");
                    Number(line.Start.X, element, "x1"); Number(line.Start.Y, element, "y1");
                    Number(line.End.X, element, "x2"); Number(line.End.Y, element, "y2");
                    Number(line.StrokeWidth, element, "stroke-width"); Qa.Equal(line.Stroke, Required(element, "stroke"), "선 stroke");
                    break;
                case PolylinePrimitive polyline:
                    Qa.Check(element.Name.LocalName is "polyline" or "polygon", "도형 요소");
                    Qa.Equal(polyline.Closed, element.Name.LocalName == "polygon", "도형 닫힘");
                    var points = Numbers(Required(element, "points"));
                    Qa.Equal(polyline.Points.Length * 2, points.Length, "도형 좌표 수");
                    for (int p = 0; p < polyline.Points.Length; p++)
                    { Qa.Near(polyline.Points[p].X, points[p * 2], "도형 x"); Qa.Near(polyline.Points[p].Y, points[p * 2 + 1], "도형 y"); }
                    Number(polyline.StrokeWidth, element, "stroke-width"); Qa.Equal(polyline.Stroke, Required(element, "stroke"), "도형 stroke");
                    break;
                default: throw new InvalidOperationException("검증하지 않은 primitive: " + layout.Primitives[i].GetType().Name);
            }
        }
        Qa.Check(!root.Descendants().Any(e => e.Name.LocalName is "image" or "foreignObject" or "script"), "SVG 비벡터/실행 요소");
    }

    private static string Required(XElement element, string attribute) =>
        (string?)element.Attribute(attribute) ?? throw new InvalidOperationException("SVG 필수 속성 없음: " + attribute);
    private static void Number(double expected, XElement element, string attribute) =>
        Qa.Near(expected, double.Parse(Required(element, attribute), Invariant), "SVG " + attribute);
    private static double[] Numbers(string text) => Regex.Matches(text, @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?")
        .Select(m => double.Parse(m.Value, Invariant)).ToArray();

    private static void SvgTextAndSelection()
    {
        var leaf = Qa.Text("<>&\"' 𝑥 e\u0301 한글 🙂"); var session = new EditingSession(Qa.Workspace(leaf));
        Qa.Select(session, leaf.Id, 1, 4);
        var before = Qa.Fingerprint(session.Workspace); var selection = session.Selection; var revision = session.Revision;
        var result = Layout(session.Workspace); var exporter = new SvgExportService();
        var xml = exporter.ToSvg(result); AssertSvg(result, xml);
        Qa.Equal(leaf.Text, string.Concat(XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value)), "특수/Unicode XML run 원문 보존");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "SVG 원본"); Qa.Equal(selection, session.Selection, "SVG 선택");
        Qa.Equal(revision, session.Revision, "SVG revision"); Qa.Check(!session.CanUndo && !session.IsDirty, "SVG history/dirty");
    }

    private static async Task SvgFile()
    {
        using var directory = new TempDirectory(); var exporter = new SvgExportService(); var layout = Layout(Sample());
        var path = directory.File("수식.svg"); await File.WriteAllTextAsync(path, "previous");
        await exporter.ExportAsync(path, layout);
        AssertSvg(layout, await File.ReadAllTextAsync(path));
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Qa.FailsAsync(() => exporter.ExportAsync(path, layout, cancel.Token), "SVG 취소", typeof(OperationCanceledException));
        var bytesAfterCancelledExport = await File.ReadAllBytesAsync(path);
        Qa.Check(bytes.SequenceEqual(bytesAfterCancelledExport), "SVG 취소 파일 보존");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Qa.FileFailsAsync(() => exporter.ExportAsync(path, layout), "SVG 잠금 덮어쓰기");
        var bytesAfterFailedExport = await File.ReadAllBytesAsync(path);
        Qa.Check(bytes.SequenceEqual(bytesAfterFailedExport), "SVG 실패 기존 바이트");
        Qa.Equal(1, Directory.GetFiles(directory.Path).Length, "SVG 임시파일 정리");
    }

    private static async Task EmptySlotExport()
    {
        var empty = Qa.Text("");
        var workspace = Qa.Workspace(new FractionNode(Guid.NewGuid(), Qa.Text("x"), empty));
        var before = Qa.Fingerprint(workspace); var layout = Layout(workspace);
        Qa.Check(layout.Diagnostics.Any(d => d.Code == LayoutDiagnosticCode.EmptySlot && d.NodeId == empty.Id && !string.IsNullOrWhiteSpace(d.Message)), "빈 슬롯 노드별 진단 없음");
        var exporter = new SvgExportService();
        var exception = Qa.Throws<MathExportException>(() => exporter.ToSvg(layout), "미완성 SVG 문자열");
        Qa.Check(exception.Diagnostics.Any(d => d.NodeId == empty.Id && d.Code == LayoutDiagnosticCode.EmptySlot), "소비자 예외의 원인 노드 누락");
        using var directory = new TempDirectory();
        var path = directory.File("preserve.svg"); File.WriteAllText(path, "기존 완성 출력");
        await Qa.FailsAsync(() => exporter.ExportAsync(path, layout), "미완성 SVG 파일", typeof(MathExportException));
        Qa.Equal("기존 완성 출력", File.ReadAllText(path), "미완성 출력이 기존 파일 손상");
        var missing = directory.File("must-not-exist.svg");
        await Qa.FailsAsync(() => exporter.ExportAsync(missing, layout), "미완성 새 출력", typeof(MathExportException));
        Qa.Check(!File.Exists(missing), "미완성 출력이 완료 파일을 생성함");
        Qa.Equal(1, Directory.GetFiles(directory.Path).Length, "미완성 출력 임시파일");
        Qa.Equal(before, Qa.Fingerprint(workspace), "미완성 출력 원본 보존");
        var session = new EditingSession(workspace); Qa.Caret(session, empty.Id, 0); session.InsertText("y");
        var complete = Layout(session.Workspace);
        Qa.Check(complete.Diagnostics.IsEmpty, "빈 슬롯 완성 후 진단 잔존");
        AssertSvg(complete, exporter.ToSvg(complete));
        Qa.Select(session, empty.Id, 0, 1); session.DeleteSelection();
        var emptied = Layout(session.Workspace);
        Qa.Check(Qa.Leaf(session, empty.Id).IsEmptySlot, "필수 슬롯의 마지막 텍스트 삭제 후 flag 누락");
        Qa.Throws<MathExportException>(() => exporter.ToSvg(emptied), "텍스트 삭제로 다시 비어 있는 필수 슬롯");
        session.Undo();
        var restored = Layout(session.Workspace);
        Qa.Check(restored.Diagnostics.IsEmpty, "삭제 undo 후 완성 상태 미복구");
        AssertSvg(restored, exporter.ToSvg(restored));
    }

    private static void StructureSelection()
    {
        var session = new EditingSession(Sample());
        var leaf = Qa.Nodes(session.Workspace).OfType<TextNode>().Single(n => n.Text == "c+d");
        var radical = Qa.Nodes(session.Workspace).OfType<RadicalNode>().Single(n => Qa.Walk(n).Any(child => child.Id == leaf.Id));
        var before = Qa.Fingerprint(session.Workspace); var revision = session.Revision;
        Qa.Caret(session, leaf.Id, 1);
        Qa.Check(!session.IsNodeSelected && session.TrySelectParentStructure(), "상위 구조 선택 실패");
        Qa.Check(session.IsNodeSelected, "명시 구조선택 상태 없음");
        Qa.Equal<Guid?>(radical.Id, session.SelectedNodeId, "가장 가까운 상위 근호 선택");
        Qa.Caret(session, leaf.Id, 1); Qa.Check(!session.IsNodeSelected, "caret 복귀가 전체선택을 해제하지 않음");
        var layout = Layout(session.Workspace);
        var point = layout.Nodes.Single(n => n.NodeId == leaf.Id).CaretStops[1].Position;
        Qa.Equal<Guid?>(radical.Id, layout.HitTestSelectionTarget(point), "실제 hit 좌표의 가장 가까운 구조 대상");
        Qa.Check(session.TrySelectNode(layout.HitTestSelectionTarget(point)!.Value), "hit 구조 전체선택 실패");
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "선택은 원본 불변");
        Qa.Equal(revision, session.Revision, "선택은 revision 불변");
        Qa.Check(!session.IsDirty && !session.CanUndo, "구조선택이 dirty/history 생성");
        Qa.Check(session.DeleteSelection(), "hit 선택 구조 삭제 실패");
        Qa.Check(!session.IsNodeSelected, "삭제 후 whole-node 선택 잔존");
        session.Undo();
        Qa.Equal(before, Qa.Fingerprint(session.Workspace), "hit 구조삭제 undo 원본/ID");
        Qa.Check(session.IsNodeSelected, "삭제 undo 전체선택 상태 복구 실패");
        Qa.Equal<Guid?>(radical.Id, session.SelectedNodeId, "삭제 undo 전체선택 대상");
        Qa.Check(!session.IsDirty, "삭제 undo 초기 저장 state dirty");
    }

    private static void CompletedTemplates()
    {
        foreach (var mode in new[] { "start", "end", "wrap" })
        {
            var leaf = Qa.Text("AB"); var session = new EditingSession(Qa.Workspace(leaf));
            if (mode == "wrap") Qa.Select(session, leaf.Id, 0, 2);
            else Qa.Caret(session, leaf.Id, mode == "start" ? 0 : 2);
            var id = session.InsertFraction(); Qa.Check(id.HasValue, mode + " 분수 삽입");
            var required = Qa.Nodes(session.Workspace).OfType<TextNode>().Where(n => n.IsEmptySlot && n.Text.Length == 0).ToArray();
            Qa.Check(required.Length > 0, mode + " 필수 빈 슬롯 없음");
            foreach (var slot in required)
            {
                Qa.Caret(session, slot.Id, 0); session.InsertText("filled");
                Qa.Check(!Qa.Leaf(session, slot.Id).IsEmptySlot, mode + " 채운 슬롯 flag 잔존");
            }
            Qa.Check(!Qa.Nodes(session.Workspace).OfType<TextNode>().Any(n => n.Text.Length == 0), mode + " 불필요한 빈 접합 TextNode 생성");
            Qa.Check(Qa.Nodes(session.Workspace).OfType<TextNode>().Any(n => n.Text == "AB"), mode + " 기존 텍스트 손실");
            var layout = Layout(session.Workspace);
            Qa.Check(layout.Diagnostics.IsEmpty, mode + " 완성된 수식의 빈 슬롯 오탐");
            GeometryAssertions.Structures(session.Workspace, layout);
            AssertSvg(layout, new SvgExportService().ToSvg(layout));
        }
    }

    private static async Task InvalidSlotFlag()
    {
        var bad = Qa.Text("", false);
        MathNode[] invalid =
        [
            new FractionNode(Guid.NewGuid(), Qa.Text("x"), bad),
            new RadicalNode(Guid.NewGuid(), bad),
            new ScriptNode(Guid.NewGuid(), Qa.Text("x"), null, bad),
            new MatrixNode(Guid.NewGuid(), 2, 2, [Qa.Text("1"), bad, Qa.Text("3"), Qa.Text("4")])
        ];
        foreach (var root in invalid)
            await Qa.FailsAsync(() =>
            {
                new SvgExportService().ToSvg(Layout(Qa.Workspace(root)));
                return Task.CompletedTask;
            }, "필수 빈 슬롯 flag 우회 " + root.GetType().Name, typeof(InvalidDataException), typeof(MathExportException));
    }
}
