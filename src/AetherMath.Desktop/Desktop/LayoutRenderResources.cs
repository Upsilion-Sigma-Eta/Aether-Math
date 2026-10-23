using System.Collections.Immutable;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AetherMath.Layout;

namespace AetherMath.Desktop;

internal abstract record PreparedPrimitive;
internal sealed record PreparedGlyph(TextLayout Layout, MathPoint BaselineOrigin) : PreparedPrimitive;
internal sealed record PreparedLine(Pen Pen, MathPoint Start, MathPoint End) : PreparedPrimitive;
internal sealed record PreparedPolyline(StreamGeometry Geometry, Pen Pen) : PreparedPrimitive;

internal sealed class LayoutRenderResources : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public LayoutRenderResources(LayoutResult layout)
    {
        var prepared = ImmutableArray.CreateBuilder<PreparedPrimitive>(layout.Primitives.Length);
        var brushes = new Dictionary<string, IBrush>(StringComparer.Ordinal);
        var pens = new Dictionary<(string Color, double Thickness), Pen>();

        IBrush GetBrush(string color)
        {
            if (!brushes.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(Color.Parse(color));
                brushes.Add(color, brush);
            }
            return brush;
        }

        Pen GetPen(string color, double thickness)
        {
            var key = (color, thickness);
            if (!pens.TryGetValue(key, out var pen))
            {
                pen = new Pen(GetBrush(color), thickness);
                pens.Add(key, pen);
            }
            return pen;
        }

        foreach (var primitive in layout.Primitives)
        {
            switch (primitive)
            {
                case GlyphPrimitive glyph:
                {
                    var typeface = new Typeface(MathFontFamilyResolver.Resolve(glyph.FontFamily),
                        glyph.Italic ? FontStyle.Italic : FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
                    var textLayout = new TextLayout(glyph.Text, typeface, glyph.FontSize, GetBrush(glyph.Fill),
                        textWrapping: TextWrapping.NoWrap,
                        flowDirection: FlowDirection.LeftToRight,
                        maxWidth: double.PositiveInfinity,
                        maxHeight: double.PositiveInfinity);
                    _disposables.Add(textLayout);
                    prepared.Add(new PreparedGlyph(textLayout, glyph.BaselineOrigin));
                    break;
                }
                case LinePrimitive line:
                    prepared.Add(new PreparedLine(GetPen(line.Stroke, line.StrokeWidth), line.Start, line.End));
                    break;
                case PolylinePrimitive polyline:
                {
                    var geometry = new StreamGeometry();
                    if (!polyline.Points.IsDefaultOrEmpty)
                    {
                        using var geometryContext = geometry.Open();
                        var first = polyline.Points[0];
                        geometryContext.BeginFigure(new Point(first.X, first.Y), isFilled: false);
                        for (var index = 1; index < polyline.Points.Length; index++)
                        {
                            var point = polyline.Points[index];
                            geometryContext.LineTo(new Point(point.X, point.Y));
                        }
                        geometryContext.EndFigure(polyline.Closed);
                    }
                    prepared.Add(new PreparedPolyline(geometry, GetPen(polyline.Stroke, polyline.StrokeWidth)));
                    break;
                }
            }
        }

        Primitives = prepared.MoveToImmutable();
    }

    public ImmutableArray<PreparedPrimitive> Primitives { get; }

    public void Draw(DrawingContext context)
    {
        foreach (var primitive in Primitives)
        {
            switch (primitive)
            {
                case PreparedGlyph glyph:
                    glyph.Layout.Draw(context, new Point(glyph.BaselineOrigin.X,
                        glyph.BaselineOrigin.Y - glyph.Layout.Baseline));
                    break;
                case PreparedLine line:
                    context.DrawLine(line.Pen, new Point(line.Start.X, line.Start.Y), new Point(line.End.X, line.End.Y));
                    break;
                case PreparedPolyline polyline:
                    context.DrawGeometry(null, polyline.Pen, polyline.Geometry);
                    break;
            }
        }
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _disposables.Clear();
    }
}
