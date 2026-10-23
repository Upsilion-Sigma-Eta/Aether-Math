using System.Text;
using Avalonia.Media;

namespace AetherMath.Desktop;

public enum MathGlyphCoverageStatus
{
    Present,
    Missing,
    Unknown
}

public sealed record MathGlyphCoverageResult(
    MathGlyphCoverageStatus Status,
    string FontFamily,
    string Reason);

public interface IMathGlyphCoverageProbe
{
    MathGlyphCoverageResult Probe(string text, string fontFamily);
}

public sealed class AvaloniaMathGlyphCoverageProbe : IMathGlyphCoverageProbe
{
    public MathGlyphCoverageResult Probe(string text, string fontFamily)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(fontFamily))
            return Unknown(fontFamily, "글리프를 확인할 문자 또는 글꼴 정보가 없습니다.");

        var runes = text.EnumerateRunes().ToArray();

        try
        {
            MathFontFamilyResolver.EnsureRegistered();
            var typeface = new Typeface(MathFontFamilyResolver.Resolve(fontFamily), FontStyle.Normal,
                FontWeight.Normal, FontStretch.Normal);
            if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface) || glyphTypeface is null)
                return Unknown(fontFamily, "요청한 기본 글꼴의 글리프 정보를 확인할 수 없습니다.");

            var actualFamily = glyphTypeface.FamilyName;
            var platformFamily = glyphTypeface.PlatformTypeface.FamilyName;
            if (!string.Equals(actualFamily, fontFamily, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(platformFamily, fontFamily, StringComparison.OrdinalIgnoreCase))
                return new MathGlyphCoverageResult(MathGlyphCoverageStatus.Unknown, actualFamily,
                    $"요청 글꼴 '{fontFamily}'와 반환 face '{actualFamily}' (플랫폼 '{platformFamily}')가 일치하지 않아 그 글꼴의 문자표로 판정하지 않았습니다.");

            if (runes.Length != 1)
                return new MathGlyphCoverageResult(MathGlyphCoverageStatus.Unknown, actualFamily,
                    "복합 문자열은 기본 글꼴 정보가 있어도 shaping·ZWJ 결합·fallback 결과를 문자표만으로 판정할 수 없습니다.");

            var glyphIndex = glyphTypeface.CharacterToGlyphMap.GetGlyph(runes[0].Value);
            return glyphIndex == 0
                ? new MathGlyphCoverageResult(MathGlyphCoverageStatus.Missing, actualFamily,
                    "요청한 글꼴의 문자표에 이 코드 포인트가 없습니다. 원문은 그대로 삽입됩니다.")
                : new MathGlyphCoverageResult(MathGlyphCoverageStatus.Present, actualFamily,
                    "요청한 글꼴의 문자표에 코드 포인트가 있습니다. 실제 shaping·fallback 표시를 보장하지는 않습니다.");
        }
        catch (Exception exception)
        {
            return Unknown(fontFamily, $"글리프 정보를 확인할 수 없습니다: {exception.Message}");
        }
    }

    private static MathGlyphCoverageResult Unknown(string fontFamily, string reason) =>
        new(MathGlyphCoverageStatus.Unknown, fontFamily ?? string.Empty, reason);
}
