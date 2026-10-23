using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace AetherMath.Editing;

public sealed record UnicodeScalarEntry(
    int CodePoint,
    string CodePointLabel,
    string PreviewText,
    bool PreviewIsPlaceholder,
    UnicodeCategory UnicodeCategory,
    bool CanInsert,
    string? DisabledReason)
{
    public string CategoryNameKo => UnicodeCategory switch
    {
        UnicodeCategory.UppercaseLetter => "대문자",
        UnicodeCategory.LowercaseLetter => "소문자",
        UnicodeCategory.TitlecaseLetter => "제목 대문자",
        UnicodeCategory.ModifierLetter => "수정 문자",
        UnicodeCategory.OtherLetter => "기타 문자",
        UnicodeCategory.NonSpacingMark => "비간격 결합 문자",
        UnicodeCategory.SpacingCombiningMark => "간격 결합 문자",
        UnicodeCategory.EnclosingMark => "둘러싸는 결합 문자",
        UnicodeCategory.DecimalDigitNumber => "십진 숫자",
        UnicodeCategory.LetterNumber => "문자형 숫자",
        UnicodeCategory.OtherNumber => "기타 숫자",
        UnicodeCategory.SpaceSeparator => "공백",
        UnicodeCategory.LineSeparator => "줄 구분자",
        UnicodeCategory.ParagraphSeparator => "문단 구분자",
        UnicodeCategory.Control => "제어 문자",
        UnicodeCategory.Format => "서식 문자",
        UnicodeCategory.Surrogate => "서로게이트",
        UnicodeCategory.PrivateUse => "사용자 정의 영역",
        UnicodeCategory.ConnectorPunctuation => "연결 문장 부호",
        UnicodeCategory.DashPunctuation => "대시 문장 부호",
        UnicodeCategory.OpenPunctuation => "여는 문장 부호",
        UnicodeCategory.ClosePunctuation => "닫는 문장 부호",
        UnicodeCategory.InitialQuotePunctuation => "여는 인용 부호",
        UnicodeCategory.FinalQuotePunctuation => "닫는 인용 부호",
        UnicodeCategory.OtherPunctuation => "기타 문장 부호",
        UnicodeCategory.MathSymbol => "수학 기호",
        UnicodeCategory.CurrencySymbol => "통화 기호",
        UnicodeCategory.ModifierSymbol => "수정 기호",
        UnicodeCategory.OtherSymbol => "기타 기호",
        UnicodeCategory.OtherNotAssigned => "미할당",
        _ => "기타"
    };
}

public sealed record UnicodeScalarPage(
    int PageIndex,
    ImmutableArray<UnicodeScalarEntry> Entries);

public sealed record UnicodeQuickRange(
    string Id,
    string NameKo,
    string NameEn,
    int StartCodePoint,
    int EndCodePoint);

public enum UnicodeLookupStatus
{
    Empty,
    Found,
    Invalid
}

public sealed record UnicodeLookupResult(
    UnicodeLookupStatus Status,
    UnicodeScalarEntry? Entry,
    string? Diagnostic);

public static class UnicodeScalarBrowser
{
    public const int PageSize = 128;
    public const int ScalarCount = 0x110000 - 0x800;
    public const int PageCount = ScalarCount / PageSize;

    public static ImmutableArray<UnicodeQuickRange> QuickRanges { get; } =
    [
        new("unicode.range.math", "수학 기호", "Mathematical operators", 0x2200, 0x22FF),
        new("unicode.range.greek", "그리스 문자", "Greek and Coptic", 0x0370, 0x03FF),
        new("unicode.range.hangul", "한글 음절", "Hangul syllables", 0xAC00, 0xD7A3)
    ];

    private static readonly ImmutableDictionary<string, UnicodeQuickRange> QuickRangesById =
        QuickRanges.ToImmutableDictionary(range => range.Id, StringComparer.Ordinal);

    public static UnicodeScalarPage GetPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var firstScalarIndex = pageIndex * PageSize;
        var entries = ImmutableArray.CreateBuilder<UnicodeScalarEntry>(PageSize);
        for (var offset = 0; offset < PageSize; offset++)
        {
            var scalarIndex = firstScalarIndex + offset;
            var codePoint = ScalarIndexToCodePoint(scalarIndex);
            if (!TryCreateEntry(codePoint, out var entry) || entry is null)
                throw new InvalidOperationException("Unicode scalar 페이지의 계산 결과가 올바르지 않습니다.");
            entries.Add(entry);
        }

        return new UnicodeScalarPage(pageIndex, entries.MoveToImmutable());
    }

    public static bool TryCreateEntry(int codePoint, out UnicodeScalarEntry? entry)
    {
        entry = null;
        if (!Rune.TryCreate(codePoint, out var rune))
            return false;

        var category = Rune.GetUnicodeCategory(rune);
        var (canInsert, reason) = GetInsertability(category);
        var preview = GetPreview(rune, category, canInsert, out var isPlaceholder);
        var label = $"U+{codePoint:X4}";
        entry = new UnicodeScalarEntry(codePoint, label, preview, isPlaceholder,
            category, canInsert, reason);
        return true;
    }

    public static UnicodeLookupResult Lookup(string? query)
    {
        var value = query?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return new UnicodeLookupResult(UnicodeLookupStatus.Empty, null, null);

        if (!value.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            return Invalid("U+로 시작하는 코드 포인트를 입력하세요.");

        var digits = value.AsSpan(2);
        if (digits.Length is < 1 or > 6 || !ContainsOnlyAsciiHexDigits(digits))
            return Invalid("U+ 뒤에 1~6자리 16진수만 입력할 수 있습니다.");

        if (!int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) ||
            codePoint > 0x10FFFF)
            return Invalid("Unicode 코드 포인트 범위는 U+0000부터 U+10FFFF까지입니다.");

        if (codePoint is >= 0xD800 and <= 0xDFFF)
            return Invalid("U+D800~U+DFFF는 Unicode scalar가 아닌 서로게이트 범위입니다.");

        if (!TryCreateEntry(codePoint, out var entry) || entry is null)
            return Invalid("입력한 값은 유효한 Unicode scalar가 아닙니다.");

        return new UnicodeLookupResult(UnicodeLookupStatus.Found, entry, null);
    }

    public static bool TryFindQuickRange(string? stableId, out UnicodeQuickRange? range)
    {
        if (stableId is not null && QuickRangesById.TryGetValue(stableId, out var found))
        {
            range = found;
            return true;
        }

        range = null;
        return false;
    }

    public static int GetPageIndexForCodePoint(int codePoint)
    {
        if (!Rune.TryCreate(codePoint, out _))
            throw new ArgumentOutOfRangeException(nameof(codePoint), "유효한 Unicode scalar를 지정해야 합니다.");

        var scalarIndex = codePoint < 0xD800 ? codePoint : codePoint - 0x800;
        return scalarIndex / PageSize;
    }

    private static int ScalarIndexToCodePoint(int scalarIndex) =>
        scalarIndex < 0xD800 ? scalarIndex : scalarIndex + 0x800;

    private static bool ContainsOnlyAsciiHexDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f') and
                not (>= 'A' and <= 'F'))
                return false;
        }
        return true;
    }

    private static UnicodeLookupResult Invalid(string diagnostic) =>
        new(UnicodeLookupStatus.Invalid, null, diagnostic);

    private static (bool CanInsert, string? Reason) GetInsertability(UnicodeCategory category) => category switch
    {
        UnicodeCategory.Control => (false, "제어 문자는 수식에 직접 삽입할 수 없습니다."),
        UnicodeCategory.Format => (false, "서식 문자는 단독으로 표시하거나 삽입할 수 없습니다."),
        UnicodeCategory.OtherNotAssigned => (false, "현재 Unicode 데이터에서 할당되지 않은 코드 포인트입니다."),
        UnicodeCategory.PrivateUse => (false, "사용자 정의 영역 문자는 공통 글리프를 보장할 수 없습니다."),
        UnicodeCategory.SpaceSeparator => (false, "공백 구분자는 화면상 표시가 분명하지 않아 직접 삽입할 수 없습니다."),
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark =>
            (false, "결합 문자는 기본 문자 없이 단독으로 삽입할 수 없습니다."),
        UnicodeCategory.LineSeparator => (false, "줄 구분자는 수식 텍스트로 삽입할 수 없습니다."),
        UnicodeCategory.ParagraphSeparator => (false, "문단 구분자는 수식 텍스트로 삽입할 수 없습니다."),
        _ => (true, null)
    };

    private static string GetPreview(Rune rune, UnicodeCategory category, bool canInsert,
        out bool isPlaceholder)
    {
        isPlaceholder = true;
        if (category == UnicodeCategory.SpaceSeparator)
            return rune.Value == 0x20 ? "␠" : "⟦공백⟧";

        if (canInsert)
        {
            isPlaceholder = false;
            return rune.ToString();
        }

        return category switch
        {
            UnicodeCategory.Control => "⟦제어 문자⟧",
            UnicodeCategory.Format => "⟦서식 문자⟧",
            UnicodeCategory.OtherNotAssigned => "⟦미할당⟧",
            UnicodeCategory.PrivateUse => "⟦사용자 정의 영역⟧",
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => "⟦결합 문자⟧",
            UnicodeCategory.LineSeparator => "⟦줄 구분자⟧",
            UnicodeCategory.ParagraphSeparator => "⟦문단 구분자⟧",
            _ => "⟦표시할 수 없음⟧"
        };
    }
}
