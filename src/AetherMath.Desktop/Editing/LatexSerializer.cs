using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AetherMath.Core;

namespace AetherMath.Editing;

public enum LatexStyleField
{
    FontSize,
    FontFamily,
    Foreground
}

public sealed record LatexStyleDiagnostic(LatexStyleField Field, Guid OwnerId,
    MathPropertyScope Scope, string Message);

public sealed record LatexLayoutDiagnostic(Guid GroupId, double RowSpacing, string Message);

public sealed record LatexTextDiagnostic(Guid NodeId, MathTextRole Role, int Utf16Index,
    int CodePoint, string Reason)
{
    public string CodePointLabel => $"U+{CodePoint:X4}";
}

public static class LatexSerializer
{
    public static ImmutableArray<LatexTextDiagnostic> GetTextDiagnostics(EquationWorkspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var diagnostics = ImmutableArray.CreateBuilder<LatexTextDiagnostic>();
        foreach (var group in workspace.Groups)
        foreach (var row in group.Rows)
            Visit(row.Root);
        return diagnostics.ToImmutable();

        void Visit(MathNode node)
        {
            if (node is TextNode text)
            {
                var utf16Index = 0;
                foreach (var rune in text.Text.EnumerateRunes())
                {
                    if (rune.Value is <= 0x1F or >= 0x7F and <= 0x9F)
                        diagnostics.Add(new LatexTextDiagnostic(text.Id, text.Role, utf16Index, rune.Value,
                            "제어 문자는 LaTeX 외부 재사용 범위에서 지원하지 않습니다. 원문 code point는 생성 코드에 유지됩니다."));
                    utf16Index += rune.Utf16SequenceLength;
                }
            }
            foreach (var child in MathTree.ChildrenOf(node))
                Visit(child);
        }
    }

    public static ImmutableArray<Guid> GetIncompleteSlotNodeIds(EquationWorkspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var nodeIds = ImmutableArray.CreateBuilder<Guid>();
        foreach (var group in workspace.Groups)
        foreach (var row in group.Rows)
            Visit(row.Root);
        return nodeIds.ToImmutable();

        void Visit(MathNode node)
        {
            if (node is TextNode { IsEmptySlot: true, Text.Length: 0 } text)
                nodeIds.Add(text.Id);
            foreach (var child in MathTree.ChildrenOf(node))
                Visit(child);
        }
    }

    public static ImmutableArray<LatexStyleDiagnostic> GetStyleDiagnostics(EquationWorkspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var diagnostics = ImmutableArray.CreateBuilder<LatexStyleDiagnostic>();
        AddStyle(workspace.Id, MathPropertyScope.WorkspaceDefault, new MathStyleOverride(
            workspace.Style.FontSize, workspace.Style.FontFamily, workspace.Style.Foreground));
        foreach (var group in workspace.Groups)
        {
            AddStyle(group.Id, MathPropertyScope.CurrentGroup, group.StyleOverride);
            foreach (var row in group.Rows)
            {
                AddStyle(row.Id, MathPropertyScope.CurrentRow, row.StyleOverride);
                Visit(row.Root);
            }
        }
        return diagnostics.ToImmutable();

        void Visit(MathNode node)
        {
            AddStyle(node.Id, MathPropertyScope.SelectionNode, node.StyleOverride);
            foreach (var child in MathTree.ChildrenOf(node))
                Visit(child);
        }

        void AddStyle(Guid ownerId, MathPropertyScope scope, MathStyleOverride? style)
        {
            if (style is null)
                return;
            if (style.FontSize is not null)
                diagnostics.Add(new LatexStyleDiagnostic(LatexStyleField.FontSize, ownerId, scope,
                    "글자 크기는 현재 LaTeX 출력에서 표현되지 않습니다."));
            if (style.FontFamily is not null)
                diagnostics.Add(new LatexStyleDiagnostic(LatexStyleField.FontFamily, ownerId, scope,
                    "수학 글꼴은 현재 LaTeX 출력에서 표현되지 않습니다."));
            if (style.Foreground is not null)
                diagnostics.Add(new LatexStyleDiagnostic(LatexStyleField.Foreground, ownerId, scope,
                    "수식 색상은 현재 LaTeX 출력에서 표현되지 않습니다."));
        }
    }

    public static ImmutableArray<LatexLayoutDiagnostic> GetLayoutDiagnostics(EquationWorkspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var diagnostics = ImmutableArray.CreateBuilder<LatexLayoutDiagnostic>();
        foreach (var group in workspace.Groups)
        {
            if (group.Rows.Length < 2)
                continue;
            diagnostics.Add(new LatexLayoutDiagnostic(group.Id, group.RowSpacing,
                $"LaTeX 코드에는 이 그룹의 논리 행 사이 추가 간격({group.RowSpacing.ToString("R", CultureInfo.InvariantCulture)} px)이 반영되지 않습니다."));
        }
        return diagnostics.ToImmutable();
    }

    public static string ToLatex(EquationWorkspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var groups = new List<string>(workspace.Groups.Length);
        var workspaceStyle = workspace.Style.TypesettingStyle;
        var workspaceStyleCommand = true;
        foreach (var group in workspace.Groups)
        {
            var rows = new List<string>(group.Rows.Length);
            var groupStyle = group.StyleOverride?.TypesettingStyle ?? workspaceStyle;
            var groupStyleCommand = workspaceStyleCommand || group.StyleOverride?.TypesettingStyle is not null;
            foreach (var row in group.Rows)
            {
                var builder = new StringBuilder();
                var alignmentPending = group.Alignment == AlignmentMode.Equals && group.Rows.Length > 1;
                var rowStyle = row.StyleOverride?.TypesettingStyle ?? groupStyle;
                var rowStyleCommand = groupStyleCommand || row.StyleOverride?.TypesettingStyle is not null;
                AppendNode(builder, row.Root, topLevel: true, ref alignmentPending, rowStyle, rowStyleCommand);
                rows.Add(builder.ToString());
            }

            groups.Add(rows.Count == 1
                ? rows[0]
                : $"\\begin{{aligned}}{string.Join(" \\\\", rows)}\\end{{aligned}}");
        }

        return string.Join(" \\qquad ", groups);
    }

    private static void AppendNode(StringBuilder builder, MathNode node, bool topLevel,
        ref bool alignmentPending, MathTypesettingStyle inheritedStyle, bool emitStyle)
    {
        var style = inheritedStyle;
        if (node.StyleOverride?.TypesettingStyle is MathTypesettingStyle nodeStyle)
        {
            style = nodeStyle;
            emitStyle = true;
        }

        if (node is SequenceNode sequence)
        {
            foreach (var child in sequence.Children)
                AppendNode(builder, child, topLevel, ref alignmentPending, style, emitStyle);
            return;
        }
        if (node is TextNode text)
        {
            AppendText(builder, text, topLevel, ref alignmentPending, style, emitStyle);
            return;
        }
        if (emitStyle)
            builder.Append("{\\").Append(ToLatexStyleCommand(style)).Append(' ');

        switch (node)
        {
            case MathSpaceNode:
                builder.Append("\\,");
                break;
            case FractionNode fraction:
            {
                builder.Append(fraction.StyleOverride?.TypesettingStyle == MathTypesettingStyle.Display
                    ? "\\dfrac{"
                    : "\\frac{");
                var childStyle = ToFractionChildStyle(style);
                AppendNode(builder, fraction.Numerator, topLevel: false, ref alignmentPending,
                    childStyle, emitStyle: true);
                builder.Append("}{");
                AppendNode(builder, fraction.Denominator, topLevel: false, ref alignmentPending,
                    childStyle, emitStyle: true);
                builder.Append('}');
                break;
            }
            case RadicalNode radical:
                builder.Append("\\sqrt{");
                AppendNode(builder, radical.Radicand, topLevel: false, ref alignmentPending, style, emitStyle: false);
                builder.Append('}');
                break;
            case ScriptNode script:
                builder.Append('{');
                AppendNode(builder, script.Base, topLevel: false, ref alignmentPending, style, emitStyle: false);
                builder.Append('}');
                if (script.Subscript is not null)
                {
                    builder.Append("_{");
                    AppendNode(builder, script.Subscript, topLevel: false, ref alignmentPending,
                        ToScriptChildStyle(style), emitStyle: true);
                    builder.Append('}');
                }
                if (script.Superscript is not null)
                {
                    builder.Append("^{");
                    AppendNode(builder, script.Superscript, topLevel: false, ref alignmentPending,
                        ToScriptChildStyle(style), emitStyle: true);
                    builder.Append('}');
                }
                break;
            case MatrixNode matrix:
                builder.Append("\\begin{bmatrix}");
                for (var row = 0; row < matrix.Rows; row++)
                {
                    if (row > 0)
                        builder.Append("\\\\");
                    for (var column = 0; column < matrix.Columns; column++)
                    {
                        if (column > 0)
                            builder.Append('&');
                        AppendNode(builder, matrix.Cells[row * matrix.Columns + column], topLevel: false,
                            ref alignmentPending, ToCompactStyle(style), emitStyle: true);
                    }
                }
                builder.Append("\\end{bmatrix}");
                break;
            case IntegralNode integral:
                builder.Append("\\int\\nolimits");
                AppendBound(builder, '_', integral.LowerLimit, ref alignmentPending, style);
                AppendBound(builder, '^', integral.UpperLimit, ref alignmentPending, style);
                builder.Append('{');
                AppendNode(builder, integral.Integrand, topLevel: false, ref alignmentPending, style, emitStyle: false);
                builder.Append("}\\,\\mathrm{d}{");
                AppendNode(builder, integral.DifferentialVariable, topLevel: false, ref alignmentPending, style, emitStyle: false);
                builder.Append('}');
                break;
            case SummationNode summation:
                builder.Append(style == MathTypesettingStyle.Display ? "\\sum\\limits" : "\\sum\\nolimits");
                AppendBound(builder, '_', summation.LowerLimit, ref alignmentPending, style);
                AppendBound(builder, '^', summation.UpperLimit, ref alignmentPending, style);
                builder.Append('{');
                AppendNode(builder, summation.Body, topLevel: false, ref alignmentPending, style, emitStyle: false);
                builder.Append('}');
                break;
            case LimitNode limit:
                builder.Append(style == MathTypesettingStyle.Display ? "\\lim\\limits_{" : "\\lim\\nolimits_{");
                AppendNode(builder, limit.Approach, topLevel: false, ref alignmentPending,
                    ToScriptChildStyle(style), emitStyle: true);
                builder.Append('}');
                builder.Append('{');
                AppendNode(builder, limit.Body, topLevel: false, ref alignmentPending, style, emitStyle: false);
                builder.Append('}');
                break;
            case BracketsNode brackets:
            {
                var (left, right) = brackets.Kind switch
                {
                    MathBracketKind.Parentheses => ("\\left(", "\\right)"),
                    MathBracketKind.Square => ("\\left[", "\\right]"),
                    MathBracketKind.Curly => ("\\left\\{", "\\right\\}"),
                    MathBracketKind.Angle => ("\\left\\langle", "\\right\\rangle{}"),
                    _ => throw new InvalidOperationException("괄호 종류가 올바르지 않습니다.")
                };
                builder.Append(left);
                var angle = brackets.Kind == MathBracketKind.Angle;
                if (angle)
                    builder.Append('{');
                AppendNode(builder, brackets.Content, topLevel: false, ref alignmentPending, style, emitStyle: false);
                if (angle)
                    builder.Append('}');
                builder.Append(right);
                break;
            }
            case CasesNode cases:
                builder.Append("\\begin{cases}");
                for (var index = 0; index < cases.Rows.Length; index++)
                {
                    if (index > 0)
                        builder.Append("\\\\");
                    AppendNode(builder, cases.Rows[index].Expression, topLevel: false,
                        ref alignmentPending, ToCompactStyle(style), emitStyle: true);
                    builder.Append('&');
                    AppendNode(builder, cases.Rows[index].Condition, topLevel: false,
                        ref alignmentPending, ToCompactStyle(style), emitStyle: true);
                }
                builder.Append("\\end{cases}");
                break;
            default:
                throw new InvalidOperationException($"지원하지 않는 수식 노드입니다: {node.GetType().Name}");
        }

        if (emitStyle)
            builder.Append('}');
    }

    private static void AppendBound(StringBuilder builder, char marker, MathNode? node,
        ref bool alignmentPending, MathTypesettingStyle parentStyle)
    {
        if (node is null)
            return;
        builder.Append(marker).Append('{');
        AppendNode(builder, node, topLevel: false, ref alignmentPending,
            ToScriptChildStyle(parentStyle), emitStyle: true);
        builder.Append('}');
    }

    private static void AppendText(StringBuilder builder, TextNode node, bool topLevel,
        ref bool alignmentPending, MathTypesettingStyle style, bool emitStyle)
    {
        var text = node.Text;
        if (node.Role != MathTextRole.Automatic)
        {
            if (topLevel && alignmentPending && node.Role == MathTextRole.MathSymbol)
            {
                var equals = FindStandaloneEqualityIndex(text);
                if (equals >= 0)
                {
                    AppendRoleRun(builder, text.AsSpan(0, equals), node.Role, style, emitStyle);
                    builder.Append('&').Append('=');
                    AppendRoleRun(builder, text.AsSpan(equals + 1), node.Role, style, emitStyle);
                    alignmentPending = false;
                    return;
                }
            }

            AppendRoleRun(builder, text.AsSpan(), node.Role, style, emitStyle);
            return;
        }

        if (!emitStyle)
        {
            AppendTextWithoutStyle(builder, text, topLevel, ref alignmentPending);
            return;
        }

        if (topLevel && alignmentPending)
        {
            var equals = FindStandaloneEqualityIndex(text);
            if (equals >= 0)
            {
                AppendStyledText(builder, text.AsSpan(0, equals), style);
                builder.Append('&').Append('=');
                AppendStyledText(builder, text.AsSpan(equals + 1), style);
                alignmentPending = false;
                return;
            }
        }

        AppendStyledText(builder, text.AsSpan(), style);
    }

    private static void AppendRoleRun(StringBuilder builder, ReadOnlySpan<char> text, MathTextRole role,
        MathTypesettingStyle style, bool emitStyle)
    {
        if (emitStyle)
            builder.Append("{\\").Append(ToLatexStyleCommand(style)).Append(' ');

        var command = role switch
        {
            MathTextRole.Identifier => "mathit",
            MathTextRole.MathSymbol or MathTextRole.Number or MathTextRole.Unit => "mathrm",
            MathTextRole.PlainText or MathTextRole.Condition => "text",
            _ => throw new InvalidOperationException("텍스트 역할이 올바르지 않습니다.")
        };
        builder.Append('\\').Append(command).Append('{');
        if (role is MathTextRole.PlainText or MathTextRole.Condition)
            AppendTextEscaped(builder, text);
        else
            AppendRoleMathEscaped(builder, text);
        builder.Append('}');

        if (emitStyle)
            builder.Append('}');
    }

    private static void AppendRoleMathEscaped(StringBuilder builder, ReadOnlySpan<char> text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == ' ')
            {
                builder.Append("\\ ");
                continue;
            }
            if (character == '^')
            {
                builder.Append("\\text{\\textasciicircum{}}");
                continue;
            }
            if (character == '~')
            {
                builder.Append("\\text{\\textasciitilde{}}");
                continue;
            }
            AppendEscaped(builder, text.Slice(index, 1));
        }
    }

    private static void AppendTextEscaped(StringBuilder builder, ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case ' ': builder.Append("\\ "); break;
                case '#': builder.Append("\\#"); break;
                case '$': builder.Append("\\$"); break;
                case '%': builder.Append("\\%"); break;
                case '&': builder.Append("\\&"); break;
                case '_': builder.Append("\\_"); break;
                case '{': builder.Append("\\{"); break;
                case '}': builder.Append("\\}"); break;
                case '\\': builder.Append("\\textbackslash{}"); break;
                case '^': builder.Append("\\textasciicircum{}"); break;
                case '~': builder.Append("\\textasciitilde{}"); break;
                default: builder.Append(character); break;
            }
        }
    }

    private static void AppendTextWithoutStyle(StringBuilder builder, string text, bool topLevel,
        ref bool alignmentPending)
    {
        if (topLevel && alignmentPending)
        {
            var equals = FindStandaloneEqualityIndex(text);
            if (equals >= 0)
            {
                AppendEscaped(builder, text.AsSpan(0, equals));
                builder.Append('&').Append('=');
                AppendEscaped(builder, text.AsSpan(equals + 1));
                alignmentPending = false;
                return;
            }
        }
        AppendEscaped(builder, text.AsSpan());
    }

    private static int FindStandaloneEqualityIndex(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var end = index + 1 == starts.Length ? text.Length : starts[index + 1];
            if (text.AsSpan(start, end - start).SequenceEqual("="))
                return start;
        }
        return -1;
    }

    private static void AppendStyledText(StringBuilder builder, ReadOnlySpan<char> text, MathTypesettingStyle style)
    {
        builder.Append("{\\").Append(ToLatexStyleCommand(style)).Append(' ');
        AppendEscaped(builder, text);
        builder.Append('}');
    }

    private static string ToLatexStyleCommand(MathTypesettingStyle style) => style switch
    {
        MathTypesettingStyle.Display => "displaystyle",
        MathTypesettingStyle.Text => "textstyle",
        MathTypesettingStyle.Script => "scriptstyle",
        MathTypesettingStyle.ScriptScript => "scriptscriptstyle",
        _ => throw new InvalidOperationException("수학 조판 스타일이 올바르지 않습니다.")
    };

    private static MathTypesettingStyle ToFractionChildStyle(MathTypesettingStyle parentStyle) => parentStyle switch
    {
        MathTypesettingStyle.Display => MathTypesettingStyle.Text,
        MathTypesettingStyle.Text => MathTypesettingStyle.Script,
        MathTypesettingStyle.Script => MathTypesettingStyle.ScriptScript,
        MathTypesettingStyle.ScriptScript => MathTypesettingStyle.ScriptScript,
        _ => MathTypesettingStyle.Text
    };

    private static MathTypesettingStyle ToScriptChildStyle(MathTypesettingStyle parentStyle) => parentStyle switch
    {
        MathTypesettingStyle.Display or MathTypesettingStyle.Text => MathTypesettingStyle.Script,
        MathTypesettingStyle.Script or MathTypesettingStyle.ScriptScript => MathTypesettingStyle.ScriptScript,
        _ => MathTypesettingStyle.Script
    };

    private static MathTypesettingStyle ToCompactStyle(MathTypesettingStyle parentStyle) =>
        parentStyle == MathTypesettingStyle.Display ? MathTypesettingStyle.Text : parentStyle;

    private static void AppendEscaped(StringBuilder builder, ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '#': builder.Append("\\#"); break;
                case '$': builder.Append("\\$"); break;
                case '%': builder.Append("\\%"); break;
                case '&': builder.Append("\\&"); break;
                case '_': builder.Append("\\_"); break;
                case '{': builder.Append("\\{"); break;
                case '}': builder.Append("\\}"); break;
                case '\\': builder.Append("\\backslash{}"); break;
                case '^': builder.Append("\\^"); break;
                case '~': builder.Append("\\~"); break;
                default: builder.Append(ch); break;
            }
        }
    }
}
