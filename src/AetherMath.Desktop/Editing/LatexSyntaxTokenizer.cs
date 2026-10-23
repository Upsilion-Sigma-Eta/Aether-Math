using System.Collections.Immutable;
using System.Text;

namespace AetherMath.Editing;

public enum LatexTokenKind
{
    Text,
    Whitespace,
    ControlWord,
    ControlSymbol,
    Brace,
    Alignment,
    Script,
    Number,
    Comment
}

public readonly record struct LatexTokenRange(int Start, int Length, LatexTokenKind Kind);

public sealed record LatexSyntaxSnapshot(string Source, ImmutableArray<LatexTokenRange> Tokens);

/// <summary>
/// Creates display-only token ranges over the exact source string. This is not a TeX parser.
/// </summary>
public static class LatexSyntaxTokenizer
{
    public static ImmutableArray<LatexTokenRange> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0)
            return ImmutableArray<LatexTokenRange>.Empty;

        var ranges = ImmutableArray.CreateBuilder<LatexTokenRange>();
        var position = 0;

        while (position < source.Length)
        {
            var start = position;
            LatexTokenKind kind;
            var current = source[position];

            if (current == '\\')
            {
                if (position + 1 < source.Length && IsAsciiLetter(source[position + 1]))
                {
                    kind = LatexTokenKind.ControlWord;
                    position += 2;
                    while (position < source.Length && IsAsciiLetter(source[position]))
                        position++;
                }
                else
                {
                    kind = LatexTokenKind.ControlSymbol;
                    position++;
                    if (position < source.Length)
                        position += GetScalarLength(source, position);
                }
            }
            else if (current == '%')
            {
                kind = LatexTokenKind.Comment;
                position++;
                while (position < source.Length && source[position] is not '\r' and not '\n')
                    position += GetScalarLength(source, position);
            }
            else if (current is '{' or '}')
            {
                kind = LatexTokenKind.Brace;
                position++;
            }
            else if (current == '&')
            {
                kind = LatexTokenKind.Alignment;
                position++;
            }
            else if (current is '^' or '_')
            {
                kind = LatexTokenKind.Script;
                position++;
            }
            else if (IsAsciiDigit(current))
            {
                kind = LatexTokenKind.Number;
                position++;
                while (position < source.Length && IsAsciiDigit(source[position]))
                    position++;
            }
            else if (IsWhitespaceScalar(source, position))
            {
                kind = LatexTokenKind.Whitespace;
                position += GetScalarLength(source, position);
                while (position < source.Length && IsWhitespaceScalar(source, position))
                    position += GetScalarLength(source, position);
            }
            else
            {
                kind = LatexTokenKind.Text;
                position += GetScalarLength(source, position);
                while (position < source.Length && IsPlainTextScalar(source, position))
                    position += GetScalarLength(source, position);
            }

            AppendRange(ranges, start, position - start, kind);
        }

        return ranges.ToImmutable();
    }

    private static void AppendRange(ImmutableArray<LatexTokenRange>.Builder ranges,
        int start, int length, LatexTokenKind kind)
    {
        ranges.Add(new LatexTokenRange(start, length, kind));
    }

    private static bool IsPlainTextScalar(string source, int position)
    {
        var character = source[position];
        if (character is '\\' or '%' or '{' or '}' or '&' or '^' or '_'
            || IsAsciiDigit(character))
            return false;
        return !IsWhitespaceScalar(source, position);
    }

    private static bool IsWhitespaceScalar(string source, int position)
    {
        var status = Rune.DecodeFromUtf16(source.AsSpan(position), out var rune, out _);
        return status == System.Buffers.OperationStatus.Done
            ? Rune.IsWhiteSpace(rune)
            : char.IsWhiteSpace(source[position]);
    }

    private static int GetScalarLength(string source, int position)
    {
        var status = Rune.DecodeFromUtf16(source.AsSpan(position), out _, out var consumed);
        return status == System.Buffers.OperationStatus.Done ? consumed : 1;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';
}
