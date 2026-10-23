using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace AetherMath.Desktop;

internal static class MathFontFamilyResolver
{
    private static readonly object RegistrationGate = new();
    private static FontManager? _registeredManager;
    private static readonly FontFamily CambriaMathFamily =
        new(CambriaMathTypefaceCollection.FontFamilyUri);

    public static void EnsureRegistered()
    {
        lock (RegistrationGate)
        {
            var manager = FontManager.Current;
            if (ReferenceEquals(manager, _registeredManager))
                return;

            // Avalonia 12.1.4 on Windows can return Cambria Math's native Oblique platform face
            // for an Italic request, then cache it under the GlyphTypeface's Normal metadata key.
            // Prime the manager-owned Normal entry before any window XAML asks for Cambria Math.
            // This protects the app path where the view model creates metrics before MainWindow
            // loads XAML; earlier external SystemFonts lookups are outside this guarantee.
            _ = manager.SystemFonts.TryGetGlyphTypeface(CambriaMathTypefaceCollection.FamilyName, FontStyle.Normal,
                FontWeight.Normal, FontStretch.Normal, out _);

            manager.AddFontCollection(new CambriaMathTypefaceCollection());
            _registeredManager = manager;
        }
    }

    public static FontFamily Resolve(string familyName) =>
        string.Equals(familyName, CambriaMathTypefaceCollection.FamilyName, StringComparison.OrdinalIgnoreCase)
            ? CambriaMathFamily
            : new FontFamily(familyName);
}

internal sealed class CambriaMathTypefaceCollection : FontCollectionBase
{
    internal const string FamilyName = "Cambria Math";
    internal const string FontFamilyUri = "fonts:AetherMathCambria#Cambria Math";
    private static readonly Uri CollectionKey = new("fonts:AetherMathCambria", UriKind.Absolute);

    public CambriaMathTypefaceCollection() => AddFontFamily(new FontFamily(FamilyName));

    public override Uri Key => CollectionKey;

    public override bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight,
        FontStretch stretch, [NotNullWhen(true)] out GlyphTypeface? glyphTypeface)
    {
        glyphTypeface = null;
        if (!string.Equals(familyName, FamilyName, StringComparison.OrdinalIgnoreCase))
            return false;

        var systemFonts = FontManager.Current.SystemFonts;
        if (style == FontStyle.Italic &&
            systemFonts.TryGetGlyphTypeface(FamilyName, FontStyle.Oblique, weight, stretch,
                out var nativeOblique) &&
            nativeOblique is not null &&
            string.Equals(nativeOblique.FamilyName, FamilyName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(nativeOblique.PlatformTypeface.FamilyName, FamilyName, StringComparison.OrdinalIgnoreCase) &&
            (nativeOblique.Style == FontStyle.Oblique ||
             nativeOblique.PlatformTypeface.Style == FontStyle.Oblique ||
             (nativeOblique.FontSimulations & FontSimulations.Oblique) != 0 ||
             (nativeOblique.PlatformTypeface.FontSimulations & FontSimulations.Oblique) != 0))
        {
            // Keep the italic request's meaning while using the already-resolved Cambria Math
            // Oblique face. Reuse the SystemFonts-owned instance; do not clone, cache, or dispose it.
            glyphTypeface = nativeOblique;
            return true;
        }

        return systemFonts.TryGetGlyphTypeface(FamilyName, style, weight, stretch, out glyphTypeface);
    }

    public override bool TryGetFamilyTypefaces(string familyName,
        [NotNullWhen(true)] out IReadOnlyList<Typeface>? familyTypefaces)
    {
        familyTypefaces = null;
        if (!string.Equals(familyName, FamilyName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!FontManager.Current.SystemFonts.TryGetFamilyTypefaces(FamilyName, out var systemTypefaces))
            return false;

        var family = MathFontFamilyResolver.Resolve(FamilyName);
        familyTypefaces = systemTypefaces
            .Select(typeface => new Typeface(family, typeface.Style, typeface.Weight, typeface.Stretch))
            .ToArray();
        return true;
    }
}
