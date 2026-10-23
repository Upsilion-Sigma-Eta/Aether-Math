using System.Collections.Immutable;
using System.Text;

namespace AetherMath.Editing;

public enum MathSymbolCategory
{
    Algebra,
    Calculus,
    LinearAlgebra,
    GreekLetters,
    Logic,
    Arrows,
    Structures
}

public enum SymbolPaletteMode
{
    Symbols,
    Emoji,
    Unicode
}

public sealed record MathPaletteCategoryInfo(
    MathSymbolCategory Category,
    string NameKo,
    string NameEn);

public sealed record MathPaletteEntry(
    string Id,
    MathSymbolCategory Category,
    string NameKo,
    string NameEn,
    ImmutableArray<string> Aliases,
    string PreviewText,
    string? Text,
    MathTemplateKind? TemplateKind,
    string CodePointLabel,
    string CodePoints);

public sealed record CuratedEmojiEntry(
    string Id,
    string NameKo,
    string NameEn,
    ImmutableArray<string> Aliases,
    string PreviewText,
    string Text,
    string CodePointLabel,
    string CodePoints);

public static class MathSymbolCatalog
{
    public static ImmutableArray<MathPaletteCategoryInfo> Categories { get; } =
    [
        new(MathSymbolCategory.Algebra, "대수", "Algebra"),
        new(MathSymbolCategory.Calculus, "미적분", "Calculus"),
        new(MathSymbolCategory.LinearAlgebra, "선형대수", "Linear Algebra"),
        new(MathSymbolCategory.GreekLetters, "그리스 문자", "Greek Letters"),
        new(MathSymbolCategory.Logic, "논리", "Logic"),
        new(MathSymbolCategory.Arrows, "화살표", "Arrows"),
        new(MathSymbolCategory.Structures, "구조 템플릿", "Structures")
    ];

    public static ImmutableArray<MathPaletteEntry> Entries { get; } = CreateEntries();

    private static readonly ImmutableDictionary<string, MathPaletteEntry> ById =
        Entries.ToImmutableDictionary(entry => entry.Id, StringComparer.Ordinal);

    public static ImmutableArray<MathPaletteEntry> ForCategory(MathSymbolCategory category) =>
        Enum.IsDefined(typeof(MathSymbolCategory), category)
            ? Entries.Where(entry => entry.Category == category).ToImmutableArray()
            : ImmutableArray<MathPaletteEntry>.Empty;

    public static MathPaletteEntry? Find(string? stableId) =>
        stableId is not null && ById.TryGetValue(stableId, out var entry) ? entry : null;

    private static ImmutableArray<MathPaletteEntry> CreateEntries()
    {
        var entries = ImmutableArray.CreateBuilder<MathPaletteEntry>();

        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.plus", "더하기", "Addition", "+", "plus", "sum", "합");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.minus", "빼기", "Subtraction", "−", "minus", "subtract", "빼기 기호");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.multiply", "곱하기", "Multiplication", "×", "times", "multiply", "곱셈");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.divide", "나누기", "Division", "÷", "divide", "division", "나눗셈");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.equals", "같음", "Equals", "=", "equals", "equal");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.not-equal", "같지 않음", "Not equal", "≠", "not equal", "inequality");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.less-than", "보다 작음", "Less than", "<", "less than", "lt");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.greater-than", "보다 큼", "Greater than", ">", "greater than", "gt");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.less-than-or-equal", "작거나 같음", "Less than or equal", "≤", "less than or equal", "lte");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.greater-than-or-equal", "크거나 같음", "Greater than or equal", "≥", "greater than or equal", "gte");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.approximately-equal", "거의 같음", "Approximately equal", "≈", "approx", "approximately");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.equivalent", "동치", "Equivalent", "≡", "equivalent", "congruent");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.proportional", "비례", "Proportional to", "∝", "proportional");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.plus-minus", "플러스마이너스", "Plus-minus", "±", "plus-minus", "plus or minus");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.infinity", "무한대", "Infinity", "∞", "infinity");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.real-numbers", "실수", "Real numbers", "ℝ", "real", "real numbers");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.complex-numbers", "복소수", "Complex numbers", "ℂ", "complex", "complex numbers");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.natural-numbers", "자연수", "Natural numbers", "ℕ", "natural", "natural numbers");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.integers", "정수", "Integers", "ℤ", "integer", "integers");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.rational-numbers", "유리수", "Rational numbers", "ℚ", "rational", "rational numbers");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.empty-set", "공집합", "Empty set", "∅", "empty set", "null set");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.element-of", "원소", "Element of", "∈", "element", "belongs to", "in");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.not-element-of", "원소가 아님", "Not an element of", "∉", "not element", "not belongs to");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.subset", "부분집합", "Subset", "⊂", "subset");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.subset-equal", "부분집합 또는 같음", "Subset or equal", "⊆", "subset equal");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.union", "합집합", "Union", "∪", "union");
        AddText(entries, MathSymbolCategory.Algebra, "symbol.algebra.intersection", "교집합", "Intersection", "∩", "intersection");

        AddText(entries, MathSymbolCategory.Calculus, "symbol.calculus.partial", "편미분", "Partial differential", "∂", "partial", "partial derivative");
        AddText(entries, MathSymbolCategory.Calculus, "symbol.calculus.nabla", "나블라", "Nabla", "∇", "gradient", "del");
        AddText(entries, MathSymbolCategory.Calculus, "symbol.calculus.contour-integral", "폐곡선 적분", "Contour integral", "∮", "contour integral", "closed integral");
        AddText(entries, MathSymbolCategory.Calculus, "symbol.calculus.double-integral", "중적분", "Double integral", "∬", "double integral");
        AddText(entries, MathSymbolCategory.Calculus, "symbol.calculus.triple-integral", "삼중 적분", "Triple integral", "∭", "triple integral");
        AddText(entries, MathSymbolCategory.Calculus, "symbol.calculus.therefore", "그러므로", "Therefore", "∴", "therefore");
        AddText(entries, MathSymbolCategory.Calculus, "symbol.calculus.because", "왜냐하면", "Because", "∵", "because");

        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.dot-product", "내적 점", "Dot product", "·", "dot product", "middle dot");
        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.cross-product", "외적", "Cross product", "×", "cross product", "vector product");
        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.tensor-product", "텐서곱", "Tensor product", "⊗", "tensor product");
        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.direct-sum", "직합", "Direct sum", "⊕", "direct sum");
        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.perpendicular", "수직", "Perpendicular", "⊥", "perpendicular", "orthogonal");
        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.parallel", "평행", "Parallel", "∥", "parallel");
        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.norm", "노름", "Norm bars", "‖", "norm", "double bar");
        AddText(entries, MathSymbolCategory.LinearAlgebra, "symbol.linear-algebra.vector-arrow", "벡터 방향", "Vector direction", "⇀", "right harpoon", "vector arrow");

        AddGreek(entries, "alpha", "알파", "Alpha", "α", "Α");
        AddGreek(entries, "beta", "베타", "Beta", "β", "Β");
        AddGreek(entries, "gamma", "감마", "Gamma", "γ", "Γ");
        AddGreek(entries, "delta", "델타", "Delta", "δ", "Δ");
        AddGreek(entries, "epsilon", "엡실론", "Epsilon", "ε", "Ε");
        AddGreek(entries, "zeta", "제타", "Zeta", "ζ", "Ζ");
        AddGreek(entries, "eta", "에타", "Eta", "η", "Η");
        AddGreek(entries, "theta", "세타", "Theta", "θ", "Θ");
        AddGreek(entries, "iota", "이오타", "Iota", "ι", "Ι");
        AddGreek(entries, "kappa", "카파", "Kappa", "κ", "Κ");
        AddGreek(entries, "lambda", "람다", "Lambda", "λ", "Λ");
        AddGreek(entries, "mu", "뮤", "Mu", "μ", "Μ");
        AddGreek(entries, "nu", "뉴", "Nu", "ν", "Ν");
        AddGreek(entries, "xi", "크시", "Xi", "ξ", "Ξ");
        AddGreek(entries, "omicron", "오미크론", "Omicron", "ο", "Ο");
        AddGreek(entries, "pi", "파이", "Pi", "π", "Π");
        AddGreek(entries, "rho", "로", "Rho", "ρ", "Ρ");
        AddGreek(entries, "sigma", "시그마", "Sigma", "σ", "Σ");
        AddGreek(entries, "final-sigma", "어말 시그마", "Final sigma", "ς", null);
        AddGreek(entries, "tau", "타우", "Tau", "τ", "Τ");
        AddGreek(entries, "upsilon", "윕실론", "Upsilon", "υ", "Υ");
        AddGreek(entries, "phi", "파이", "Phi", "φ", "Φ");
        AddGreek(entries, "chi", "카이", "Chi", "χ", "Χ");
        AddGreek(entries, "psi", "프사이", "Psi", "ψ", "Ψ");
        AddGreek(entries, "omega", "오메가", "Omega", "ω", "Ω");

        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.not", "논리 부정", "Logical not", "¬", "not", "negation");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.and", "논리곱", "Logical and", "∧", "and", "conjunction");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.or", "논리합", "Logical or", "∨", "or", "disjunction");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.xor", "배타적 논리합", "Exclusive or", "⊻", "xor");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.implies", "함의", "Implies", "⇒", "implies", "therefore");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.iff", "필요충분조건", "If and only if", "⇔", "iff", "biconditional");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.forall", "모든", "For all", "∀", "forall", "universal quantifier");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.exists", "존재", "There exists", "∃", "exists", "existential quantifier");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.true", "참", "True", "⊤", "true", "top");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.false", "거짓", "False", "⊥", "false", "bottom");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.proves", "증명 가능", "Proves", "⊢", "proves", "turnstile");
        AddText(entries, MathSymbolCategory.Logic, "symbol.logic.models", "모델", "Models", "⊨", "models", "entails");

        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.left", "왼쪽", "Left arrow", "←", "left arrow");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.right", "오른쪽", "Right arrow", "→", "right arrow");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.left-right", "양방향", "Left-right arrow", "↔", "two-way arrow");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.long-left", "긴 왼쪽 화살표", "Long left arrow", "⟵", "long left");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.long-right", "긴 오른쪽 화살표", "Long right arrow", "⟶", "long right");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.up", "위쪽", "Up arrow", "↑", "up arrow");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.down", "아래쪽", "Down arrow", "↓", "down arrow");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.north-east", "오른쪽 위", "North-east arrow", "↗", "northeast");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.south-east", "오른쪽 아래", "South-east arrow", "↘", "southeast");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.maps-to", "사상", "Maps to", "↦", "maps to");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.hook-right", "갈고리 오른쪽", "Right hook arrow", "↪", "hook right");
        AddText(entries, MathSymbolCategory.Arrows, "symbol.arrow.counterclockwise", "시계 반대 방향", "Counterclockwise arrow", "↶", "counterclockwise");

        AddTemplate(entries, "structure.fraction", "분수", "Fraction", "a/b", MathTemplateKind.Fraction, "분수 템플릿");
        AddTemplate(entries, "structure.radical", "근호", "Radical", "√□", MathTemplateKind.Radical, "제곱근");
        AddTemplate(entries, "structure.superscript", "위첨자", "Superscript", "xⁿ", MathTemplateKind.Superscript, "지수");
        AddTemplate(entries, "structure.subscript", "아래첨자", "Subscript", "xₙ", MathTemplateKind.Subscript, "첨자");
        AddTemplate(entries, "structure.integral", "적분", "Integral", "∫ᵇₐ f dx", MathTemplateKind.Integral, "정적분");
        AddTemplate(entries, "structure.summation", "합", "Summation", "∑ᵇₐ aₙ", MathTemplateKind.Summation, "시그마");
        AddTemplate(entries, "structure.limit", "극한", "Limit", "limₓ→ₐ f(x)", MathTemplateKind.Limit, "극한 템플릿");
        AddTemplate(entries, "structure.matrix", "행렬", "Matrix", "[a b; c d]", MathTemplateKind.Matrix, "행렬 템플릿");
        AddTemplate(entries, "structure.brackets", "괄호", "Brackets", "(□)", MathTemplateKind.Brackets, "괄호 템플릿");
        AddTemplate(entries, "structure.cases", "경우 나누기", "Cases", "{ a; b", MathTemplateKind.Cases, "조건별 식");

        return entries.ToImmutable();
    }

    private static void AddGreek(ImmutableArray<MathPaletteEntry>.Builder entries,
        string id, string nameKo, string nameEn, string lower, string? upper)
    {
        AddText(entries, MathSymbolCategory.GreekLetters, $"symbol.greek.{id}", nameKo, nameEn,
            lower, id, nameKo, $"소문자 {nameKo}");
        if (upper is not null)
            AddText(entries, MathSymbolCategory.GreekLetters, $"symbol.greek.{id}-uppercase",
                $"대문자 {nameKo}", $"Uppercase {nameEn}", upper, $"capital {id}", $"uppercase {id}", nameKo);
    }

    private static void AddText(ImmutableArray<MathPaletteEntry>.Builder entries,
        MathSymbolCategory category, string id, string nameKo, string nameEn,
        string text, params string[] aliases)
    {
        var codePoints = FormatCodePoints(text);
        entries.Add(new MathPaletteEntry(id, category, nameKo, nameEn, aliases.ToImmutableArray(),
            text, text, null, codePoints, codePoints));
    }

    private static void AddTemplate(ImmutableArray<MathPaletteEntry>.Builder entries,
        string id, string nameKo, string nameEn, string preview,
        MathTemplateKind kind, params string[] aliases) =>
        entries.Add(new MathPaletteEntry(id, MathSymbolCategory.Structures, nameKo, nameEn,
            aliases.ToImmutableArray(), preview, null, kind, string.Empty, string.Empty));

    internal static string FormatCodePoints(string text)
    {
        var codePoints = text.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}").ToImmutableArray();
        return string.Join(" ", codePoints);
    }
}

public static class CuratedEmojiCatalog
{
    public static ImmutableArray<CuratedEmojiEntry> Entries { get; } = CreateEntries();

    private static readonly ImmutableDictionary<string, CuratedEmojiEntry> ById =
        Entries.ToImmutableDictionary(entry => entry.Id, StringComparer.Ordinal);

    public static CuratedEmojiEntry? Find(string? stableId) =>
        stableId is not null && ById.TryGetValue(stableId, out var entry) ? entry : null;

    private static ImmutableArray<CuratedEmojiEntry> CreateEntries()
    {
        var entries = ImmutableArray.CreateBuilder<CuratedEmojiEntry>();
        Add(entries, "emoji.grinning-face", "활짝 웃는 얼굴", "Grinning face", "😀", "smile", "happy", "grin");
        Add(entries, "emoji.thinking-face", "생각하는 얼굴", "Thinking face", "🤔", "think", "hmm");
        Add(entries, "emoji.heart-red", "빨간 하트", "Red heart", "❤️", "heart", "love");
        Add(entries, "emoji.thumbs-up", "엄지 올리기", "Thumbs up", "👍", "like", "approve");
        Add(entries, "emoji.thumbs-up-medium-skin", "중간 피부색 엄지", "Thumbs up, medium skin tone", "👍🏽", "skin tone", "medium skin tone");
        Add(entries, "emoji.woman-technologist", "여성 기술자", "Woman technologist", "👩‍💻", "woman coder", "developer", "zwj");
        Add(entries, "emoji.man-technologist", "남성 기술자", "Man technologist", "👨‍💻", "man coder", "developer", "zwj");
        Add(entries, "emoji.family", "가족", "Family", "👨‍👩‍👧‍👦", "family", "zwj");
        Add(entries, "emoji.rainbow-flag", "무지개 깃발", "Rainbow flag", "🏳️‍🌈", "pride", "flag", "zwj");
        Add(entries, "emoji.technologist-dark-skin", "짙은 피부색 기술자", "Technologist, dark skin tone", "🧑🏿‍💻", "coder", "dark skin tone", "zwj");
        Add(entries, "emoji.waving-hand", "흔드는 손", "Waving hand", "👋", "hello", "wave");
        Add(entries, "emoji.waving-hand-light-skin", "밝은 피부색 손 흔들기", "Waving hand, light skin tone", "👋🏻", "hello", "light skin tone");
        Add(entries, "emoji.rocket", "로켓", "Rocket", "🚀", "launch", "space");
        Add(entries, "emoji.sparkles", "반짝임", "Sparkles", "✨", "sparkle", "magic");
        Add(entries, "emoji.pencil", "연필", "Pencil", "✏️", "write", "edit");
        return entries.ToImmutable();
    }

    private static void Add(ImmutableArray<CuratedEmojiEntry>.Builder entries,
        string id, string nameKo, string nameEn, string text, params string[] aliases)
    {
        var codePoints = MathSymbolCatalog.FormatCodePoints(text);
        entries.Add(new CuratedEmojiEntry(id, nameKo, nameEn, aliases.ToImmutableArray(),
            text, text, codePoints, codePoints));
    }
}
