using System.Collections.Immutable;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AetherMath.Layout;

namespace AetherMath.Desktop;

public sealed class AvaloniaMathTextMetrics : IMathTextMetrics
{
    public AvaloniaMathTextMetrics() => MathFontFamilyResolver.EnsureRegistered();
    public TextMeasurement Measure(string text, string fontFamily, double fontSize, bool italic)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        if (text.Length == 0)
            return new TextMeasurement(0, fontSize * 0.72, fontSize * 0.24, ImmutableArray.Create(0d));

        var typeface = new Typeface(MathFontFamilyResolver.Resolve(fontFamily), italic ? FontStyle.Italic : FontStyle.Normal,
            FontWeight.Normal, FontStretch.Normal);
        using var layout = new TextLayout(text, typeface, fontSize, Brushes.Transparent,
            textWrapping: TextWrapping.NoWrap,
            flowDirection: FlowDirection.LeftToRight,
            maxWidth: double.PositiveInfinity,
            maxHeight: double.PositiveInfinity);

        var starts = StringInfo.ParseCombiningCharacters(text);
        var caretX = ImmutableArray.CreateBuilder<double>(starts.Length + 1);
        for (var index = 0; index <= starts.Length; index++)
        {
            var utf16Position = index == starts.Length ? text.Length : starts[index];
            caretX.Add(layout.HitTestTextPosition(utf16Position).X);
        }

        return new TextMeasurement(layout.WidthIncludingTrailingWhitespace, layout.Baseline,
            Math.Max(0, layout.Height - layout.Baseline), caretX.MoveToImmutable());
    }
}
