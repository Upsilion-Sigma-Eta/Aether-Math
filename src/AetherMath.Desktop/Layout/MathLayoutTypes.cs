using System.Collections.Immutable;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Layout;

public readonly record struct MathPoint(double X, double Y);

public readonly record struct MathRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(MathPoint point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    public MathRect Inflate(double amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
}

public sealed record TextMeasurement(
    double Width,
    double Ascent,
    double Descent,
    ImmutableArray<double> CaretX);

public interface IMathTextMetrics
{
    TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic);
}

public sealed record MathLayoutOptions(
    double AvailableWidth = 1200,
    double FontSize = 30,
    double Padding = 24,
    double RowSpacing = 28);

public abstract record RenderPrimitive;

public sealed record GlyphPrimitive(
    Guid NodeId,
    string Text,
    MathPoint BaselineOrigin,
    double FontSize,
    string FontFamily,
    string Fill,
    bool Italic) : RenderPrimitive;

public sealed record LinePrimitive(
    MathPoint Start,
    MathPoint End,
    double StrokeWidth,
    string Stroke) : RenderPrimitive;

public sealed record PolylinePrimitive(
    ImmutableArray<MathPoint> Points,
    bool Closed,
    double StrokeWidth,
    string Stroke) : RenderPrimitive;

public sealed record TextCaretStop(int TextElementOffset, MathPoint Position);

public sealed record NodeLayout(
    Guid NodeId,
    MathNodeKind Kind,
    ImmutableArray<Guid> AncestorIds,
    MathRect Bounds,
    double Baseline,
    ImmutableArray<TextCaretStop> CaretStops);

public sealed record LogicalRowLayout(
    Guid RowId,
    int Index,
    MathRect Bounds,
    double Baseline,
    double? AlignmentX);

public sealed record VisualRowLayout(int LogicalRowIndex, MathRect Bounds, double Baseline);

public enum LayoutDiagnosticCode
{
    EmptySlot
}

public sealed record LayoutDiagnostic(LayoutDiagnosticCode Code, Guid NodeId, string Message);

public sealed class LayoutResult
{
    private readonly ImmutableDictionary<Guid, NodeLayout> _byId;

    internal LayoutResult(
        double width,
        double height,
        ImmutableArray<RenderPrimitive> primitives,
        ImmutableArray<NodeLayout> nodes,
        ImmutableArray<LogicalRowLayout> logicalRows,
        ImmutableArray<VisualRowLayout> visualRows,
        ImmutableArray<LayoutDiagnostic> diagnostics)
    {
        Width = width;
        Height = height;
        Primitives = primitives;
        Nodes = nodes;
        LogicalRows = logicalRows;
        VisualRows = visualRows;
        Diagnostics = diagnostics;
        _byId = nodes.ToImmutableDictionary(node => node.NodeId);
    }

    public double Width { get; }
    public double Height { get; }
    public ImmutableArray<RenderPrimitive> Primitives { get; }
    public ImmutableArray<NodeLayout> Nodes { get; }
    public ImmutableArray<LogicalRowLayout> LogicalRows { get; }
    public ImmutableArray<VisualRowLayout> VisualRows { get; }
    public ImmutableArray<LayoutDiagnostic> Diagnostics { get; }

    public MathCaret? HitTest(MathPoint point)
    {
        var hitId = HitTestNode(point);
        if (hitId is null || !_byId.TryGetValue(hitId.Value, out var hit))
            return null;

        NodeLayout? bestText = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var node in Nodes)
        {
            if (node.Kind != MathNodeKind.Text || node.CaretStops.IsDefaultOrEmpty)
                continue;
            if (hit.Kind == MathNodeKind.Text ? node.NodeId != hit.NodeId : !node.AncestorIds.Contains(hit.NodeId))
                continue;
            var distance = DistanceSquared(node.Bounds, point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestText = node;
            }
        }

        if (bestText is null)
            return null;

        var bestStop = bestText.CaretStops[0];
        var bestStopDistance = double.PositiveInfinity;
        foreach (var stop in bestText.CaretStops)
        {
            var dx = stop.Position.X - point.X;
            var dy = stop.Position.Y - point.Y;
            var distance = dx * dx + dy * dy;
            if (distance < bestStopDistance)
            {
                bestStopDistance = distance;
                bestStop = stop;
            }
        }
        return new MathCaret(bestText.NodeId, bestStop.TextElementOffset);
    }

    public Guid? HitTestNode(MathPoint point)
    {
        NodeLayout? best = null;
        foreach (var node in Nodes)
        {
            if (!node.Bounds.Contains(point))
                continue;
            if (best is null || node.AncestorIds.Length > best.AncestorIds.Length ||
                (node.AncestorIds.Length == best.AncestorIds.Length && node.Bounds.Width * node.Bounds.Height < best.Bounds.Width * best.Bounds.Height))
                best = node;
        }
        return best?.NodeId;
    }

    public Guid? HitTestSelectionTarget(MathPoint point)
    {
        var hitId = HitTestNode(point);
        if (hitId is null || !_byId.TryGetValue(hitId.Value, out var hit))
            return null;

        if (hit.Kind == MathNodeKind.MathSpace)
            return hit.NodeId;

        if (IsSelectableStructure(hit.Kind))
            return hit.NodeId;

        for (var i = hit.AncestorIds.Length - 1; i >= 0; i--)
        {
            if (_byId.TryGetValue(hit.AncestorIds[i], out var ancestor) && IsSelectableStructure(ancestor.Kind))
                return ancestor.NodeId;
        }

        if (hit.Kind == MathNodeKind.Text)
            return hit.NodeId;
        return HitTest(point)?.NodeId;
    }

    public MathRect? GetBounds(Guid nodeId) => _byId.TryGetValue(nodeId, out var node) ? node.Bounds : null;

    private static bool IsSelectableStructure(MathNodeKind kind) =>
        kind is MathNodeKind.Fraction or MathNodeKind.Radical or MathNodeKind.Script or MathNodeKind.Matrix or
            MathNodeKind.Integral or MathNodeKind.Summation or MathNodeKind.Limit or MathNodeKind.Brackets or MathNodeKind.Cases;

    private static double DistanceSquared(MathRect rect, MathPoint point)
    {
        var dx = point.X < rect.X ? rect.X - point.X : point.X > rect.Right ? point.X - rect.Right : 0;
        var dy = point.Y < rect.Y ? rect.Y - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
        return dx * dx + dy * dy;
    }
}
