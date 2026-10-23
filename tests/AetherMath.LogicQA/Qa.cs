using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.LogicQA;

internal static class Qa
{
    public static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string label) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{label}: 기대={expected}; 실제={actual}");

    public static void Near(double expected, double actual, string label, double tolerance = 0.001)
    {
        Check(double.IsFinite(expected) && double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            $"{label}: 기대={expected:R}; 실제={actual:R}; 허용오차={tolerance}");
    }

    public static async Task<Exception> FailsAsync(Func<Task> action, string label, params Type[] expectedTypes)
    {
        try { await action(); }
        catch (Exception exception) when (expectedTypes.Any(t => t.IsInstanceOfType(exception))) { return exception; }
        throw new InvalidOperationException(label + ": 기대한 실패가 발생하지 않았습니다.");
    }

    public static TException Throws<TException>(Action action, string label) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException(label + ": " + typeof(TException).Name + " 예외가 발생하지 않았습니다.");
    }

    public static Task<Exception> FileFailsAsync(Func<Task> action, string label) =>
        FailsAsync(action, label, typeof(IOException), typeof(UnauthorizedAccessException));

    public static TextNode Text(string value, bool? isEmptySlot = null) => new(Guid.NewGuid(), value, isEmptySlot ?? value.Length == 0);
    public static SequenceNode Sequence(params MathNode[] children) => new(Guid.NewGuid(), [.. children]);
    public static EquationWorkspace Workspace(params MathNode[] rows) => new(
        Guid.NewGuid(), 1, "논리 QA 𝑥 e\u0301 한글",
        [new EquationGroup(Guid.NewGuid(), [.. rows.Select(n => new EquationRow(Guid.NewGuid(), n))], AlignmentMode.Equals, 28)],
        new MathStyle(30, "Cambria Math", "#25364D"));

    public static IEnumerable<MathNode> Walk(MathNode node)
    {
        yield return node;
        IEnumerable<MathNode> children = node switch
        {
            TextNode => [],
            MathSpaceNode => [],
            SequenceNode sequence => sequence.Children,
            FractionNode fraction => [fraction.Numerator, fraction.Denominator],
            RadicalNode radical => [radical.Radicand],
            ScriptNode script => new[] { script.Base, script.Subscript, script.Superscript }.OfType<MathNode>(),
            MatrixNode matrix => matrix.Cells,
            IntegralNode integral => new[] { integral.LowerLimit, integral.UpperLimit, integral.Integrand, integral.DifferentialVariable }.OfType<MathNode>(),
            SummationNode summation => new[] { summation.LowerLimit, summation.UpperLimit, summation.Body }.OfType<MathNode>(),
            LimitNode limit => [limit.Approach, limit.Body],
            BracketsNode brackets => [brackets.Content],
            CasesNode cases => cases.Rows.SelectMany(row => new[] { row.Expression, row.Condition }.OfType<MathNode>()),
            _ => throw new InvalidOperationException("미검증 노드 형식: " + node.GetType().Name)
        };
        foreach (var child in children)
            foreach (var descendant in Walk(child)) yield return descendant;
    }

    public static IEnumerable<MathNode> Nodes(EquationWorkspace workspace) => workspace.Groups
        .SelectMany(g => g.Rows).SelectMany(r => Walk(r.Root));
    public static MathNode Node(EditingSession session, Guid id) => Nodes(session.Workspace).Single(n => n.Id == id);
    public static TextNode Leaf(EditingSession session, Guid id) => (TextNode)Node(session, id);
    public static int Elements(string text) => StringInfo.ParseCombiningCharacters(text).Length;
    public static void Caret(EditingSession session, Guid id, int offset) =>
        Check(session.TrySetCaret(new MathCaret(id, offset)), $"caret 설정 실패: {id}/{offset}");
    public static void Select(EditingSession session, Guid id, int from, int to) =>
        Check(session.TrySetSelection(new MathSelection(new(id, from), new(id, to))), $"선택 설정 실패: {id}/{from}..{to}");

    // 모델의 전체 의미·식별자를 직렬화한다. 제품 소스나 UI 문자열을 검사하지 않는다.
    public static string Fingerprint(EquationWorkspace workspace) => JsonSerializer.Serialize(new
    {
        workspace.Id, workspace.SchemaVersion, workspace.Name,
        Style = new
        {
            workspace.Style.FontSize,
            workspace.Style.FontFamily,
            workspace.Style.Foreground,
            workspace.Style.TypesettingStyle
        },
        Groups = workspace.Groups.Select(g => new
        {
            g.Id, g.Alignment, g.RowSpacing, StyleOverride = OverrideShape(g.StyleOverride),
            Rows = g.Rows.Select(r => new
            {
                r.Id, StyleOverride = OverrideShape(r.StyleOverride), Root = NodeShape(r.Root)
            })
        })
    });

    private static object? OverrideShape(MathStyleOverride? styleOverride) => styleOverride is null ? null : new
    {
        styleOverride.FontSize,
        styleOverride.FontFamily,
        styleOverride.Foreground,
        styleOverride.TypesettingStyle
    };

    public static string NodeFingerprint(MathNode node) => JsonSerializer.Serialize(NodeShape(node));
    private static object NodeShape(MathNode node) => node switch
    {
        TextNode n => new { Kind = "text", n.Id, n.Text, n.IsEmptySlot, n.Role, StyleOverride = OverrideShape(n.StyleOverride) },
        MathSpaceNode n => new { Kind = "mathSpace", n.Id, SpaceKind = n.Kind, StyleOverride = OverrideShape(n.StyleOverride) },
        SequenceNode n => new { Kind = "sequence", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Children = n.Children.Select(NodeShape).ToArray() },
        FractionNode n => new { Kind = "fraction", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Numerator = NodeShape(n.Numerator), Denominator = NodeShape(n.Denominator) },
        RadicalNode n => new { Kind = "radical", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Radicand = NodeShape(n.Radicand) },
        ScriptNode n => new { Kind = "script", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Base = NodeShape(n.Base), Subscript = n.Subscript is null ? null : NodeShape(n.Subscript), Superscript = n.Superscript is null ? null : NodeShape(n.Superscript) },
        MatrixNode n => new { Kind = "matrix", n.Id, StyleOverride = OverrideShape(n.StyleOverride), n.Rows, n.Columns, Cells = n.Cells.Select(NodeShape).ToArray() },
        IntegralNode n => new { Kind = "integral", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Integrand = NodeShape(n.Integrand), LowerLimit = n.LowerLimit is null ? null : NodeShape(n.LowerLimit), UpperLimit = n.UpperLimit is null ? null : NodeShape(n.UpperLimit), DifferentialVariable = NodeShape(n.DifferentialVariable) },
        SummationNode n => new { Kind = "summation", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Body = NodeShape(n.Body), LowerLimit = n.LowerLimit is null ? null : NodeShape(n.LowerLimit), UpperLimit = n.UpperLimit is null ? null : NodeShape(n.UpperLimit) },
        LimitNode n => new { Kind = "limit", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Approach = NodeShape(n.Approach), Body = NodeShape(n.Body) },
        BracketsNode n => new { Kind = "brackets", n.Id, StyleOverride = OverrideShape(n.StyleOverride), BracketKind = n.Kind, Content = NodeShape(n.Content) },
        CasesNode n => new { Kind = "cases", n.Id, StyleOverride = OverrideShape(n.StyleOverride), Rows = n.Rows.Select(row => new { Expression = NodeShape(row.Expression), Condition = NodeShape(row.Condition) }).ToArray() },
        _ => throw new InvalidOperationException("미검증 노드 형식: " + node.GetType().Name)
    };

    public static void OnlyTextChanged(EquationWorkspace before, EquationWorkspace after, Guid textId, string expected)
    {
        var oldNodes = Nodes(before).ToDictionary(n => n.Id);
        var newNodes = Nodes(after).ToDictionary(n => n.Id);
        Equal(string.Join(',', oldNodes.Keys.Order()), string.Join(',', newNodes.Keys.Order()), "안정 ID 집합");
        foreach (var pair in oldNodes)
        {
            var actual = newNodes[pair.Key];
            Equal(pair.Value.GetType(), actual.GetType(), "노드 종류");
            if (pair.Value is TextNode text)
            {
                Equal(pair.Key == textId ? expected : text.Text, ((TextNode)actual).Text, "텍스트/이웃 보존 " + pair.Key);
                Equal(pair.Key == textId && expected.Length > 0 ? false : text.IsEmptySlot, ((TextNode)actual).IsEmptySlot, "빈 슬롯 상태 " + pair.Key);
            }
        }
        // 대상 leaf 외 필드를 원본으로 되돌렸을 때 전체 구조가 같아야 한다.
        Equal(Fingerprint(before), Fingerprint(Replace(after, (TextNode)oldNodes[textId])), "조상/슬롯/그룹 보존");
    }

    private static EquationWorkspace Replace(EquationWorkspace workspace, TextNode original) => workspace with
    {
        Groups = [.. workspace.Groups.Select(g => g with { Rows = [.. g.Rows.Select(r => r with { Root = ReplaceNode(r.Root, original) })] })]
    };
    private static MathNode ReplaceNode(MathNode node, TextNode original) => node switch
    {
        TextNode n => n.Id == original.Id ? original : n,
        MathSpaceNode n => n,
        SequenceNode n => n with { Children = [.. n.Children.Select(c => ReplaceNode(c, original))] },
        FractionNode n => n with { Numerator = ReplaceNode(n.Numerator, original), Denominator = ReplaceNode(n.Denominator, original) },
        RadicalNode n => n with { Radicand = ReplaceNode(n.Radicand, original) },
        ScriptNode n => n with { Base = ReplaceNode(n.Base, original), Subscript = n.Subscript is null ? null : ReplaceNode(n.Subscript, original), Superscript = n.Superscript is null ? null : ReplaceNode(n.Superscript, original) },
        MatrixNode n => n with { Cells = [.. n.Cells.Select(c => ReplaceNode(c, original))] },
        IntegralNode n => n with { Integrand = ReplaceNode(n.Integrand, original), LowerLimit = n.LowerLimit is null ? null : ReplaceNode(n.LowerLimit, original), UpperLimit = n.UpperLimit is null ? null : ReplaceNode(n.UpperLimit, original), DifferentialVariable = ReplaceNode(n.DifferentialVariable, original) },
        SummationNode n => n with { Body = ReplaceNode(n.Body, original), LowerLimit = n.LowerLimit is null ? null : ReplaceNode(n.LowerLimit, original), UpperLimit = n.UpperLimit is null ? null : ReplaceNode(n.UpperLimit, original) },
        LimitNode n => n with { Approach = ReplaceNode(n.Approach, original), Body = ReplaceNode(n.Body, original) },
        BracketsNode n => n with { Content = ReplaceNode(n.Content, original) },
        CasesNode n => n with { Rows = [.. n.Rows.Select(row => row with { Expression = ReplaceNode(row.Expression, original), Condition = ReplaceNode(row.Condition, original) })] },
        _ => throw new InvalidOperationException("미검증 노드 형식")
    };
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AetherMath-LogicQA-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, true);
}
