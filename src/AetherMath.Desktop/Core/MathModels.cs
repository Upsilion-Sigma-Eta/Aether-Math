using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace AetherMath.Core;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(TextNode), "text")]
[JsonDerivedType(typeof(SequenceNode), "sequence")]
[JsonDerivedType(typeof(FractionNode), "fraction")]
[JsonDerivedType(typeof(RadicalNode), "radical")]
[JsonDerivedType(typeof(ScriptNode), "script")]
[JsonDerivedType(typeof(MatrixNode), "matrix")]
[JsonDerivedType(typeof(IntegralNode), "integral")]
[JsonDerivedType(typeof(SummationNode), "summation")]
[JsonDerivedType(typeof(LimitNode), "limit")]
[JsonDerivedType(typeof(BracketsNode), "brackets")]
[JsonDerivedType(typeof(CasesNode), "cases")]
[JsonDerivedType(typeof(MathSpaceNode), "mathSpace")]
public abstract record MathNode(Guid Id)
{
    private MathStyleOverride? _styleOverride;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MathStyleOverride? StyleOverride
    {
        get => _styleOverride;
        init => _styleOverride = value is { FontSize: null, FontFamily: null, Foreground: null, TypesettingStyle: null } ? null : value;
    }
}

public sealed record TextNode(Guid Id, string Text, bool IsEmptySlot = false) : MathNode(Id)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MathTextRole Role { get; init; } = MathTextRole.Automatic;
}
public sealed record MathSpaceNode(Guid Id, MathSpaceKind Kind) : MathNode(Id);
public sealed record SequenceNode(Guid Id, ImmutableArray<MathNode> Children) : MathNode(Id);
public sealed record FractionNode(Guid Id, MathNode Numerator, MathNode Denominator) : MathNode(Id);
public sealed record RadicalNode(Guid Id, MathNode Radicand) : MathNode(Id);
public sealed record ScriptNode(Guid Id, MathNode Base, MathNode? Subscript, MathNode? Superscript) : MathNode(Id);
public sealed record MatrixNode(Guid Id, int Rows, int Columns, ImmutableArray<MathNode> Cells) : MathNode(Id);
public sealed record IntegralNode(Guid Id, MathNode Integrand, MathNode? LowerLimit, MathNode? UpperLimit,
    MathNode DifferentialVariable) : MathNode(Id);
public sealed record SummationNode(Guid Id, MathNode Body, MathNode? LowerLimit, MathNode? UpperLimit) : MathNode(Id);
public sealed record LimitNode(Guid Id, MathNode Approach, MathNode Body) : MathNode(Id);
public sealed record BracketsNode(Guid Id, MathBracketKind Kind, MathNode Content) : MathNode(Id);
public sealed record CasesNode(Guid Id, ImmutableArray<CaseRow> Rows) : MathNode(Id);
public sealed record CaseRow(MathNode Expression, MathNode Condition);

public enum MathBracketKind
{
    Parentheses,
    Square,
    Curly,
    Angle
}

public enum MathNodeKind
{
    Text,
    Sequence,
    Fraction,
    Radical,
    Script,
    Matrix,
    Integral,
    Summation,
    Limit,
    Brackets,
    Cases,
    MathSpace
}

public enum MathSpaceKind
{
    Thin
}

public enum MathTextRole
{
    Automatic,
    Identifier,
    MathSymbol,
    Number,
    PlainText,
    Unit,
    Condition
}

public enum AlignmentMode
{
    None,
    Equals
}

public enum MathTypesettingStyle
{
    Display,
    Text,
    Script,
    ScriptScript
}

public sealed record EquationRow(Guid Id, MathNode Root)
{
    private MathStyleOverride? _styleOverride;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MathStyleOverride? StyleOverride
    {
        get => _styleOverride;
        init => _styleOverride = value is { FontSize: null, FontFamily: null, Foreground: null, TypesettingStyle: null } ? null : value;
    }
}

public sealed record EquationGroup(
    Guid Id,
    ImmutableArray<EquationRow> Rows,
    AlignmentMode Alignment,
    double RowSpacing)
{
    private MathStyleOverride? _styleOverride;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MathStyleOverride? StyleOverride
    {
        get => _styleOverride;
        init => _styleOverride = value is { FontSize: null, FontFamily: null, Foreground: null, TypesettingStyle: null } ? null : value;
    }
}

public sealed record MathStyle(double FontSize, string FontFamily, string Foreground)
{
    public MathTypesettingStyle TypesettingStyle { get; init; } = MathTypesettingStyle.Display;
}

public sealed record MathStyleOverride(double? FontSize = null, string? FontFamily = null, string? Foreground = null)
{
    public MathTypesettingStyle? TypesettingStyle { get; init; }
}

public sealed record EquationWorkspace(
    Guid Id,
    int SchemaVersion,
    string Name,
    ImmutableArray<EquationGroup> Groups,
    MathStyle Style);

internal static class WorkspaceValidator
{
    public static void Validate(EquationWorkspace? workspace)
    {
        if (workspace is null)
            throw new InvalidDataException("수식 작업 데이터가 비어 있습니다.");

        if (workspace.SchemaVersion != 1)
            throw new InvalidDataException($"지원하지 않는 수식 파일 스키마입니다: {workspace.SchemaVersion}");

        if (string.IsNullOrWhiteSpace(workspace.Name) || workspace.Style is null ||
            !double.IsFinite(workspace.Style.FontSize) || workspace.Style.FontSize <= 0 ||
            string.IsNullOrWhiteSpace(workspace.Style.FontFamily) || !IsHexColor(workspace.Style.Foreground) ||
            !Enum.IsDefined(typeof(MathTypesettingStyle), workspace.Style.TypesettingStyle))
            throw new InvalidDataException("수식 작업 이름 또는 기본 스타일이 올바르지 않습니다.");

        if (workspace.Groups.IsDefaultOrEmpty)
            throw new InvalidDataException("수식 작업에는 적어도 하나의 그룹이 필요합니다.");

        var ids = new HashSet<Guid>();
        AddId(workspace.Id, ids);
        foreach (var group in workspace.Groups)
        {
            if (group is null || group.Rows.IsDefaultOrEmpty || !double.IsFinite(group.RowSpacing) ||
                group.RowSpacing < 0 || !Enum.IsDefined(typeof(AlignmentMode), group.Alignment))
                throw new InvalidDataException("수식 그룹 또는 논리 행 구성이 올바르지 않습니다.");

            ValidateStyleOverride(group.StyleOverride);

            AddId(group.Id, ids);
            foreach (var row in group.Rows)
            {
                if (row is null || row.Root is null)
                    throw new InvalidDataException("수식 행에 루트 노드가 없습니다.");

                ValidateStyleOverride(row.StyleOverride);
                AddId(row.Id, ids);
                ValidateNode(row.Root, ids);
                ValidateRequiredContent(row.Root);
                ValidateRequiredSlots(row.Root);
            }
        }
    }

    private static void ValidateNode(MathNode node, HashSet<Guid> ids)
    {
        if (node is null)
            throw new InvalidDataException("수식 구조에 null 노드가 있습니다.");

        ValidateStyleOverride(node.StyleOverride);
        AddId(node.Id, ids);
        switch (node)
        {
            case TextNode text:
                if (text.Text is null || (text.IsEmptySlot && text.Text.Length != 0) ||
                    !Enum.IsDefined(typeof(MathTextRole), text.Role))
                    throw new InvalidDataException("텍스트 역할 또는 빈 슬롯 표시와 수식 문자열이 올바르지 않습니다.");
                break;
            case MathSpaceNode space:
                if (!Enum.IsDefined(typeof(MathSpaceKind), space.Kind))
                    throw new InvalidDataException("수학 공백 종류가 올바르지 않습니다.");
                break;
            case SequenceNode sequence:
                if (sequence.Children.IsDefault)
                    throw new InvalidDataException("수식 시퀀스 자식 목록이 초기화되지 않았습니다.");
                foreach (var child in sequence.Children)
                    ValidateNode(child, ids);
                break;
            case FractionNode fraction:
                ValidateNode(fraction.Numerator, ids);
                ValidateNode(fraction.Denominator, ids);
                break;
            case RadicalNode radical:
                ValidateNode(radical.Radicand, ids);
                break;
            case ScriptNode script:
                ValidateNode(script.Base, ids);
                if (script.Subscript is not null)
                    ValidateNode(script.Subscript, ids);
                if (script.Superscript is not null)
                    ValidateNode(script.Superscript, ids);
                if (script.Subscript is null && script.Superscript is null)
                    throw new InvalidDataException("첨자 구조에는 위첨자 또는 아래첨자가 필요합니다.");
                break;
            case MatrixNode matrix:
                if (matrix.Rows <= 0 || matrix.Columns <= 0 || matrix.Rows > int.MaxValue / matrix.Columns ||
                    matrix.Cells.IsDefault || matrix.Cells.Length != matrix.Rows * matrix.Columns)
                    throw new InvalidDataException("행렬의 크기와 셀 수가 일치하지 않습니다.");
                foreach (var cell in matrix.Cells)
                    ValidateNode(cell, ids);
                break;
            case IntegralNode integral:
                ValidateNode(integral.Integrand, ids);
                if (integral.LowerLimit is not null)
                    ValidateNode(integral.LowerLimit, ids);
                if (integral.UpperLimit is not null)
                    ValidateNode(integral.UpperLimit, ids);
                ValidateNode(integral.DifferentialVariable, ids);
                break;
            case SummationNode summation:
                ValidateNode(summation.Body, ids);
                if (summation.LowerLimit is not null)
                    ValidateNode(summation.LowerLimit, ids);
                if (summation.UpperLimit is not null)
                    ValidateNode(summation.UpperLimit, ids);
                break;
            case LimitNode limit:
                ValidateNode(limit.Approach, ids);
                ValidateNode(limit.Body, ids);
                break;
            case BracketsNode brackets:
                if (!Enum.IsDefined(typeof(MathBracketKind), brackets.Kind))
                    throw new InvalidDataException("괄호 종류가 올바르지 않습니다.");
                ValidateNode(brackets.Content, ids);
                break;
            case CasesNode cases:
                if (cases.Rows.IsDefaultOrEmpty)
                    throw new InvalidDataException("경우 나누기에는 적어도 한 행이 필요합니다.");
                foreach (var row in cases.Rows)
                {
                    if (row is null || row.Expression is null || row.Condition is null)
                        throw new InvalidDataException("경우 나누기 행에는 식과 조건이 모두 필요합니다.");
                    ValidateNode(row.Expression, ids);
                    ValidateNode(row.Condition, ids);
                }
                break;
            default:
                throw new InvalidDataException($"지원하지 않는 수식 노드 형식입니다: {node.GetType().Name}");
        }
    }
    private static void ValidateRequiredSlots(MathNode node)
    {
        switch (node)
        {
            case SequenceNode sequence:
                foreach (var child in sequence.Children)
                    ValidateRequiredSlots(child);
                break;
            case FractionNode fraction:
                ValidateRequiredContent(fraction.Numerator);
                ValidateRequiredContent(fraction.Denominator);
                ValidateRequiredSlots(fraction.Numerator);
                ValidateRequiredSlots(fraction.Denominator);
                break;
            case RadicalNode radical:
                ValidateRequiredContent(radical.Radicand);
                ValidateRequiredSlots(radical.Radicand);
                break;
            case ScriptNode script:
                ValidateRequiredContent(script.Base);
                ValidateRequiredSlots(script.Base);
                if (script.Subscript is not null)
                {
                    ValidateRequiredContent(script.Subscript);
                    ValidateRequiredSlots(script.Subscript);
                }
                if (script.Superscript is not null)
                {
                    ValidateRequiredContent(script.Superscript);
                    ValidateRequiredSlots(script.Superscript);
                }
                break;
            case MatrixNode matrix:
                foreach (var cell in matrix.Cells)
                {
                    ValidateRequiredContent(cell);
                    ValidateRequiredSlots(cell);
                }
                break;
            case IntegralNode integral:
                ValidateRequiredChild(integral.Integrand);
                ValidateRequiredChild(integral.DifferentialVariable);
                if (integral.LowerLimit is not null)
                    ValidateRequiredChild(integral.LowerLimit);
                if (integral.UpperLimit is not null)
                    ValidateRequiredChild(integral.UpperLimit);
                break;
            case SummationNode summation:
                ValidateRequiredChild(summation.Body);
                if (summation.LowerLimit is not null)
                    ValidateRequiredChild(summation.LowerLimit);
                if (summation.UpperLimit is not null)
                    ValidateRequiredChild(summation.UpperLimit);
                break;
            case LimitNode limit:
                ValidateRequiredChild(limit.Approach);
                ValidateRequiredChild(limit.Body);
                break;
            case BracketsNode brackets:
                ValidateRequiredChild(brackets.Content);
                break;
            case CasesNode cases:
                foreach (var row in cases.Rows)
                {
                    ValidateRequiredChild(row.Expression);
                    ValidateRequiredChild(row.Condition);
                }
                break;
        }
    }

    private static void ValidateRequiredChild(MathNode node)
    {
        ValidateRequiredContent(node);
        ValidateRequiredSlots(node);
    }

    private static void ValidateRequiredContent(MathNode node, bool emptyMarkerAvailable = false)
    {
        if (!MathTree.IsSemanticallyEmpty(node))
            return;
        switch (node)
        {
            case TextNode text when text.IsEmptySlot:
                return;
            case MathSpaceNode when emptyMarkerAvailable:
                return;
            case SequenceNode sequence when !sequence.Children.IsEmpty:
                var markerAvailable = emptyMarkerAvailable || ContainsEmptySlotMarker(sequence);
                if (!markerAvailable)
                    throw new InvalidDataException("필수 수식 슬롯에는 비어 있음 표시가 있는 텍스트 앵커가 필요합니다.");
                foreach (var child in sequence.Children)
                    ValidateRequiredContent(child, markerAvailable);
                return;
            default:
                throw new InvalidDataException("필수 수식 슬롯의 빈 상태 표시가 누락되었거나 슬롯 구조가 비어 있습니다.");
        }
    }

    private static bool ContainsEmptySlotMarker(MathNode node) => node switch
    {
        TextNode { IsEmptySlot: true, Text.Length: 0 } => true,
        SequenceNode sequence => sequence.Children.Any(ContainsEmptySlotMarker),
        _ => false
    };


    private static void AddId(Guid id, HashSet<Guid> ids)
    {
        if (id == Guid.Empty || !ids.Add(id))
            throw new InvalidDataException("수식 모델의 ID가 비어 있거나 중복되었습니다.");
    }
    internal static bool IsHexColor(string value)
    {
        if ((value.Length != 7 && value.Length != 9) || value[0] != '#')
            return false;
        for (var index = 1; index < value.Length; index++)
            if (!Uri.IsHexDigit(value[index]))
                return false;
        return true;
    }

    private static void ValidateStyleOverride(MathStyleOverride? styleOverride)
    {
        if (styleOverride is null)
            return;

        if (styleOverride.FontSize is double fontSize &&
            (!double.IsFinite(fontSize) || fontSize is < 1 or > 512))
            throw new InvalidDataException("명시 글자 크기는 1~512 논리 픽셀의 유한한 값이어야 합니다.");
        if (styleOverride.FontFamily is not null && string.IsNullOrWhiteSpace(styleOverride.FontFamily))
            throw new InvalidDataException("명시 수학 글꼴 이름이 비어 있습니다.");
        if (styleOverride.Foreground is not null && !IsHexColor(styleOverride.Foreground))
            throw new InvalidDataException("명시 수식 색상은 #RRGGBB 또는 #AARRGGBB 형식이어야 합니다.");
        if (styleOverride.TypesettingStyle is MathTypesettingStyle typesettingStyle &&
            !Enum.IsDefined(typeof(MathTypesettingStyle), typesettingStyle))
            throw new InvalidDataException("명시 수학 조판 스타일이 올바르지 않습니다.");
    }
}
