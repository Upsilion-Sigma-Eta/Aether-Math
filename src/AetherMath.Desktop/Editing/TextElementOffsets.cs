using System.Globalization;

namespace AetherMath.Editing;

internal static class TextElementOffsets
{
    public static int Count(string text) => StringInfo.ParseCombiningCharacters(text).Length;

    public static int ToUtf16Index(string text, int textElementOffset)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        if (textElementOffset < 0 || textElementOffset > starts.Length)
            throw new ArgumentOutOfRangeException(nameof(textElementOffset));
        return textElementOffset == starts.Length ? text.Length : starts[textElementOffset];
    }

    public static int FromUtf16InsertionPoint(string text, int utf16Index)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var count = 0;
        while (count < starts.Length && starts[count] < utf16Index)
            count++;
        return count;
    }

    public static string GetElement(string text, int textElementOffset)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        if ((uint)textElementOffset >= (uint)starts.Length)
            throw new ArgumentOutOfRangeException(nameof(textElementOffset));
        var start = starts[textElementOffset];
        var end = textElementOffset + 1 == starts.Length ? text.Length : starts[textElementOffset + 1];
        return text[start..end];
    }
}
