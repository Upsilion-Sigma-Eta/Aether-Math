using System.Collections.Immutable;
using System.Globalization;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Layout;

public static class MathLayoutEngine
{
    private const double ScriptScale = 0.71;

    public static LayoutResult Layout(EquationWorkspace workspace, IMathTextMetrics metrics, MathLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        WorkspaceValidator.Validate(workspace);
        options ??= new MathLayoutOptions();
        if (!double.IsFinite(options.FontSize) || options.FontSize <= 0 ||
            !double.IsFinite(options.Padding) || options.Padding < 0 ||
            !double.IsFinite(options.RowSpacing) || options.RowSpacing < 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        var diagnostics = CollectDiagnostics(workspace);
        var emptySlots = diagnostics.Select(diagnostic => diagnostic.NodeId).ToHashSet();
        var primitives = ImmutableArray.CreateBuilder<RenderPrimitive>();
        var nodeLayouts = ImmutableArray.CreateBuilder<NodeLayout>();
        var logicalRows = ImmutableArray.CreateBuilder<LogicalRowLayout>();
        var visualRows = ImmutableArray.CreateBuilder<VisualRowLayout>();
        var currentY = options.Padding;
        var maxRight = options.Padding;
        var logicalIndex = 0;

        foreach (var group in workspace.Groups)
        {
            var groupContext = MathLayoutContext.FromStyle(workspace.Style).ApplyOverride(group.StyleOverride);
            var measuredRows = new NodeMetric[group.Rows.Length];
            var equalityOffsets = new double?[group.Rows.Length];
            var sharedAlignment = 0d;
            for (var index = 0; index < group.Rows.Length; index++)
            {
                var rowContext = groupContext.ApplyOverride(group.Rows[index].StyleOverride);
                measuredRows[index] = MeasureNode(group.Rows[index].Root, rowContext, metrics, emptySlots);
                equalityOffsets[index] = group.Alignment == AlignmentMode.Equals
                    ? FindTopLevelEqualityOffset(measuredRows[index])
                    : null;
                if (equalityOffsets[index] is double offset)
                    sharedAlignment = Math.Max(sharedAlignment, offset);
            }

            for (var index = 0; index < group.Rows.Length; index++)
            {
                var metric = measuredRows[index];
                var alignmentOffset = equalityOffsets[index];
                var rowX = options.Padding + (alignmentOffset is double aligned ? sharedAlignment - aligned : 0);
                var baseline = currentY + metric.Ascent;
                var rowBounds = new MathRect(rowX, currentY, metric.Width, metric.Ascent + metric.Descent);
                double? alignmentX = alignmentOffset is double equalsOffset ? rowX + equalsOffset : null;
                logicalRows.Add(new LogicalRowLayout(group.Rows[index].Id, logicalIndex, rowBounds, baseline, alignmentX));
                visualRows.Add(new VisualRowLayout(logicalIndex, rowBounds, baseline));
                DrawNode(metric, rowX, baseline, ImmutableArray<Guid>.Empty, workspace.Style, primitives, nodeLayouts);
                maxRight = Math.Max(maxRight, rowX + metric.Width);
                currentY += metric.Ascent + metric.Descent;
                if (index + 1 < group.Rows.Length)
                    currentY += group.RowSpacing;
                logicalIndex++;
            }

            currentY += options.RowSpacing;
        }

        var width = Math.Max(1, maxRight + options.Padding);
        var height = Math.Max(1, currentY - options.RowSpacing + options.Padding);
        return new LayoutResult(width, height, primitives.ToImmutable(), nodeLayouts.ToImmutable(),
            logicalRows.ToImmutable(), visualRows.ToImmutable(), diagnostics);
    }

    private static NodeMetric MeasureNode(MathNode node, MathLayoutContext inheritedContext,
        IMathTextMetrics metrics, HashSet<Guid> emptySlots)
    {
        var context = inheritedContext.ApplyOverride(node.StyleOverride);
        var style = context.ToMathStyle();
        var fontSize = context.FontSize;
        switch (node)
        {
            case TextNode text:
                return MeasureTextNode(text, fontSize, style, metrics, emptySlots.Contains(text.Id));
            case MathSpaceNode:
                return new NodeMetric(node, style, fontSize, fontSize / 6, fontSize * 0.72, fontSize * 0.24);
            case SequenceNode sequence:
            {
                var children = ImmutableArray.CreateBuilder<NodeMetric>(sequence.Children.Length);
                var xs = ImmutableArray.CreateBuilder<double>(sequence.Children.Length);
                var baselines = ImmutableArray.CreateBuilder<double>(sequence.Children.Length);
                var width = 0d;
                var ascent = fontSize * 0.72;
                var descent = fontSize * 0.24;
                NodeMetric? previous = null;
                foreach (var child in sequence.Children)
                {
                    var childMetric = MeasureNode(child, context, metrics, emptySlots);
                    if (previous is not null)
                        width += SequenceGap(previous, childMetric, fontSize);
                    xs.Add(width);
                    baselines.Add(0);
                    children.Add(childMetric);
                    width += childMetric.Width;
                    ascent = Math.Max(ascent, childMetric.Ascent);
                    descent = Math.Max(descent, childMetric.Descent);
                    previous = childMetric;
                }
                return new NodeMetric(node, style, fontSize, width, ascent, descent,
                    children.MoveToImmutable(), xs.MoveToImmutable(), baselines.MoveToImmutable());
            }
            case FractionNode fraction:
            {
                var fractionChildContext = context.ForFractionChild();
                var numerator = MeasureNode(fraction.Numerator, fractionChildContext, metrics, emptySlots);
                var denominator = MeasureNode(fraction.Denominator, fractionChildContext, metrics, emptySlots);
                var padding = fontSize * 0.18;
                var ruleThickness = Math.Max(1, fontSize * 0.045);
                var gap = fontSize * 0.12;
                var axisOffset = -fontSize * 0.20;
                var numeratorBaseline = axisOffset - ruleThickness / 2 - gap - numerator.Descent;
                var denominatorBaseline = axisOffset + ruleThickness / 2 + gap + denominator.Ascent;
                var width = Math.Max(numerator.Width, denominator.Width) + padding * 2;
                var ascent = Math.Max(-numeratorBaseline + numerator.Ascent, -axisOffset + ruleThickness / 2);
                var descent = Math.Max(denominatorBaseline + denominator.Descent, axisOffset + ruleThickness / 2);
                var numeratorX = (width - numerator.Width) / 2;
                var denominatorX = (width - denominator.Width) / 2;
                return new NodeMetric(node, style, fontSize, width, ascent, descent,
                    ImmutableArray.Create(numerator, denominator),
                    ImmutableArray.Create(numeratorX, denominatorX),
                    ImmutableArray.Create(numeratorBaseline, denominatorBaseline),
                    shapeOffsetY: axisOffset, shapeThickness: ruleThickness);
            }
            case RadicalNode radical:
            {
                var radicand = MeasureNode(radical.Radicand, context, metrics, emptySlots);
                var radicalWidth = fontSize * 0.72;
                var rightPadding = fontSize * 0.10;
                var ascent = Math.Max(radicand.Ascent + fontSize * 0.16, fontSize * 0.85);
                var descent = Math.Max(radicand.Descent, fontSize * 0.12);
                return new NodeMetric(node, style, fontSize, radicalWidth + radicand.Width + rightPadding, ascent, descent,
                    ImmutableArray.Create(radicand), ImmutableArray.Create(radicalWidth), ImmutableArray.Create(0d),
                    shapeThickness: Math.Max(1, fontSize * 0.055));
            }
            case ScriptNode script:
            {
                var scriptContext = context.ToScriptChild();
                var @base = MeasureNode(script.Base, context, metrics, emptySlots);
                var sub = script.Subscript is null ? null : MeasureNode(script.Subscript, scriptContext, metrics, emptySlots);
                var sup = script.Superscript is null ? null : MeasureNode(script.Superscript, scriptContext, metrics, emptySlots);
                var gap = fontSize * 0.04;
                var scriptWidth = Math.Max(sub?.Width ?? 0, sup?.Width ?? 0);
                var width = @base.Width + (scriptWidth > 0 ? gap + scriptWidth : 0);
                var supBaseline = -fontSize * 0.43;
                var subBaseline = fontSize * 0.28;
                if (sup is not null && sub is not null)
                    supBaseline = Math.Min(supBaseline,
                        subBaseline - sub.Ascent - gap - sup.Descent);
                var ascent = @base.Ascent;
                var descent = @base.Descent;
                if (sub is not null)
                {
                    ascent = Math.Max(ascent, sub.Ascent - subBaseline);
                    descent = Math.Max(descent, subBaseline + sub.Descent);
                }
                if (sup is not null)
                {
                    ascent = Math.Max(ascent, sup.Ascent - supBaseline);
                    descent = Math.Max(descent, supBaseline + sup.Descent);
                }
                var children = ImmutableArray.CreateBuilder<NodeMetric>(1 + (sub is null ? 0 : 1) + (sup is null ? 0 : 1));
                var xs = ImmutableArray.CreateBuilder<double>(children.Capacity);
                var ys = ImmutableArray.CreateBuilder<double>(children.Capacity);
                children.Add(@base);
                xs.Add(0);
                ys.Add(0);
                if (sub is not null)
                {
                    children.Add(sub);
                    xs.Add(@base.Width + gap);
                    ys.Add(subBaseline);
                }
                if (sup is not null)
                {
                    children.Add(sup);
                    xs.Add(@base.Width + gap);
                    ys.Add(supBaseline);
                }
                return new NodeMetric(node, style, fontSize, width, ascent, descent,
                    children.MoveToImmutable(), xs.MoveToImmutable(), ys.MoveToImmutable());
            }
            case MatrixNode matrix:
            {
                var childContext = context.Compact();
                var cells = ImmutableArray.CreateBuilder<NodeMetric>(matrix.Cells.Length);
                var columnWidths = new double[matrix.Columns];
                var rowAscent = new double[matrix.Rows];
                var rowDescent = new double[matrix.Rows];
                for (var row = 0; row < matrix.Rows; row++)
                for (var column = 0; column < matrix.Columns; column++)
                {
                    var cell = MeasureNode(matrix.Cells[row * matrix.Columns + column], childContext, metrics, emptySlots);
                    cells.Add(cell);
                    columnWidths[column] = Math.Max(columnWidths[column], cell.Width);
                    rowAscent[row] = Math.Max(rowAscent[row], cell.Ascent);
                    rowDescent[row] = Math.Max(rowDescent[row], cell.Descent);
                }
                var columnGap = fontSize * 0.22;
                var rowGap = fontSize * 0.16;
                var braceWidth = fontSize * 0.20;
                var horizontalPadding = fontSize * 0.16;
                var contentWidth = columnWidths.Sum() + columnGap * Math.Max(0, matrix.Columns - 1);
                var contentHeight = rowAscent.Sum() + rowDescent.Sum() + rowGap * Math.Max(0, matrix.Rows - 1);
                var width = contentWidth + horizontalPadding * 2 + braceWidth * 2;
                var ascent = contentHeight / 2 + fontSize * 0.04;
                var descent = contentHeight / 2 + fontSize * 0.04;
                var xs = ImmutableArray.CreateBuilder<double>(matrix.Cells.Length);
                var ys = ImmutableArray.CreateBuilder<double>(matrix.Cells.Length);
                var rowBaseline = -contentHeight / 2;
                for (var row = 0; row < matrix.Rows; row++)
                {
                    rowBaseline += rowAscent[row];
                    var cellX = braceWidth + horizontalPadding;
                    for (var column = 0; column < matrix.Columns; column++)
                    {
                        var cell = cells[row * matrix.Columns + column];
                        xs.Add(cellX + (columnWidths[column] - cell.Width) / 2);
                        ys.Add(rowBaseline);
                        cellX += columnWidths[column] + columnGap;
                    }
                    rowBaseline += rowDescent[row] + rowGap;
                }
                return new NodeMetric(node, style, fontSize, width, ascent, descent,
                    cells.MoveToImmutable(), xs.MoveToImmutable(), ys.MoveToImmutable(),
                    shapeThickness: Math.Max(1, fontSize * 0.045));
            }
            case IntegralNode integral:
            {
                var limitContext = context.ToScriptChild();
                var operatorSize = context.IsDisplayStyle ? fontSize * 1.55 : fontSize;
                var lower = integral.LowerLimit is null ? null : MeasureNode(integral.LowerLimit, limitContext, metrics, emptySlots);
                var upper = integral.UpperLimit is null ? null : MeasureNode(integral.UpperLimit, limitContext, metrics, emptySlots);
                var integrand = MeasureNode(integral.Integrand, context, metrics, emptySlots);
                var differential = MeasureNode(integral.DifferentialVariable, context, metrics, emptySlots);
                var op = MeasureGlyph("∫", operatorSize, style, metrics, italic: false);
                var gap = fontSize * 0.16;
                var dGap = fontSize * 0.10;
                var opBaseline = -fontSize * 0.03;
                var hasLimits = lower is not null || upper is not null;
                var limitColumnWidth = Math.Max(lower?.Width ?? 0, upper?.Width ?? 0);
                var limitX = op.Measurement.Width + (hasLimits ? gap : 0);
                var operatorColumnWidth = op.Measurement.Width + (hasLimits ? gap + limitColumnWidth : 0);
                var lowerBaseline = lower is null ? 0 : opBaseline + fontSize * 0.24;
                var upperBaseline = upper is null ? 0 : opBaseline - fontSize * 0.36;
                if (upper is not null && lower is not null)
                    upperBaseline = Math.Min(upperBaseline,
                        lowerBaseline - lower.Ascent - gap * 0.35 - upper.Descent);
                var opX = 0d;
                var integrandX = operatorColumnWidth + gap;
                var dGlyph = MeasureGlyph("d", fontSize, style, metrics, italic: false);
                var dX = integrandX + integrand.Width + dGap;
                var differentialX = dX + dGlyph.Measurement.Width + dGap * 0.45;
                var width = differentialX + differential.Width;
                var ascent = Math.Max(Math.Max(integrand.Ascent, differential.Ascent), op.Measurement.Ascent - opBaseline);
                var descent = Math.Max(Math.Max(integrand.Descent, differential.Descent), op.Measurement.Descent + opBaseline);
                ascent = Math.Max(ascent, dGlyph.Measurement.Ascent);
                descent = Math.Max(descent, dGlyph.Measurement.Descent);
                if (lower is not null)
                {
                    ascent = Math.Max(ascent, lower.Ascent - lowerBaseline);
                    descent = Math.Max(descent, lowerBaseline + lower.Descent);
                }
                if (upper is not null)
                {
                    ascent = Math.Max(ascent, upper.Ascent - upperBaseline);
                    descent = Math.Max(descent, upperBaseline + upper.Descent);
                }
                var children = ImmutableArray.CreateBuilder<NodeMetric>(4);
                var xs = ImmutableArray.CreateBuilder<double>(4);
                var ys = ImmutableArray.CreateBuilder<double>(4);
                if (lower is not null)
                {
                    children.Add(lower);
                    xs.Add(limitX);
                    ys.Add(lowerBaseline);
                }
                if (upper is not null)
                {
                    children.Add(upper);
                    xs.Add(limitX);
                    ys.Add(upperBaseline);
                }
                children.Add(integrand);
                xs.Add(integrandX);
                ys.Add(0);
                children.Add(differential);
                xs.Add(differentialX);
                ys.Add(0);
                var glyphs = ImmutableArray.Create(op with { X = opX, Baseline = opBaseline },
                    dGlyph with { X = dX, Baseline = 0 });
                return new NodeMetric(node, style, fontSize, width, ascent, descent,
                    children.ToImmutable(), xs.ToImmutable(), ys.ToImmutable(), glyphs: glyphs);
            }
            case SummationNode summation:
            {
                var limitContext = context.ToScriptChild();
                var lower = summation.LowerLimit is null ? null : MeasureNode(summation.LowerLimit, limitContext, metrics, emptySlots);
                var upper = summation.UpperLimit is null ? null : MeasureNode(summation.UpperLimit, limitContext, metrics, emptySlots);
                var body = MeasureNode(summation.Body, context, metrics, emptySlots);
                var operatorSize = context.IsDisplayStyle ? fontSize * 1.45 : fontSize;
                var op = MeasureGlyph("∑", operatorSize, style, metrics, italic: false);
                var gap = fontSize * 0.16;
                var opBaseline = -fontSize * 0.03;
                var opTop = opBaseline - op.Measurement.Ascent;
                var opBottom = opBaseline + op.Measurement.Descent;
                var ascent = Math.Max(body.Ascent, op.Measurement.Ascent - opBaseline);
                var descent = Math.Max(body.Descent, op.Measurement.Descent + opBaseline);
                var children = ImmutableArray.CreateBuilder<NodeMetric>(3);
                var xs = ImmutableArray.CreateBuilder<double>(3);
                var ys = ImmutableArray.CreateBuilder<double>(3);
                double bodyX;
                double opX;
                if (context.IsDisplayStyle)
                {
                    var operatorColumnWidth = Math.Max(op.Measurement.Width, Math.Max(lower?.Width ?? 0, upper?.Width ?? 0));
                    opX = (operatorColumnWidth - op.Measurement.Width) / 2;
                    var lowerBaseline = lower is null ? 0 : opBottom + gap + lower.Ascent;
                    var upperBaseline = upper is null ? 0 : opTop - gap - upper.Descent;
                    if (lower is not null && upper is not null)
                        upperBaseline = Math.Min(upperBaseline,
                            lowerBaseline - lower.Ascent - gap * 0.35 - upper.Descent);
                    if (lower is not null)
                    {
                        children.Add(lower);
                        xs.Add((operatorColumnWidth - lower.Width) / 2);
                        ys.Add(lowerBaseline);
                        ascent = Math.Max(ascent, lower.Ascent - lowerBaseline);
                        descent = Math.Max(descent, lowerBaseline + lower.Descent);
                    }
                    if (upper is not null)
                    {
                        children.Add(upper);
                        xs.Add((operatorColumnWidth - upper.Width) / 2);
                        ys.Add(upperBaseline);
                        ascent = Math.Max(ascent, upper.Ascent - upperBaseline);
                        descent = Math.Max(descent, upperBaseline + upper.Descent);
                    }
                    bodyX = operatorColumnWidth + gap;
                }
                else
                {
                    var hasLimits = lower is not null || upper is not null;
                    var limitColumnWidth = Math.Max(lower?.Width ?? 0, upper?.Width ?? 0);
                    var limitX = op.Measurement.Width + (hasLimits ? gap : 0);
                    var upperBaseline = upper is null ? 0 : opBaseline - fontSize * 0.36;
                    var lowerBaseline = lower is null ? 0 : opBaseline + fontSize * 0.24;
                    if (upper is not null && lower is not null)
                        upperBaseline = Math.Min(upperBaseline,
                            lowerBaseline - lower.Ascent - gap * 0.35 - upper.Descent);
                    opX = 0;
                    if (lower is not null)
                    {
                        children.Add(lower);
                        xs.Add(limitX);
                        ys.Add(lowerBaseline);
                        ascent = Math.Max(ascent, lower.Ascent - lowerBaseline);
                        descent = Math.Max(descent, lowerBaseline + lower.Descent);
                    }
                    if (upper is not null)
                    {
                        children.Add(upper);
                        xs.Add(limitX);
                        ys.Add(upperBaseline);
                        ascent = Math.Max(ascent, upper.Ascent - upperBaseline);
                        descent = Math.Max(descent, upperBaseline + upper.Descent);
                    }
                    bodyX = op.Measurement.Width + (hasLimits ? gap + limitColumnWidth : 0) + gap;
                }
                children.Add(body);
                xs.Add(bodyX);
                ys.Add(0);
                return new NodeMetric(node, style, fontSize, bodyX + body.Width, ascent, descent,
                    children.ToImmutable(), xs.ToImmutable(), ys.ToImmutable(),
                    glyphs: ImmutableArray.Create(op with { X = opX, Baseline = opBaseline }));
            }
            case LimitNode limit:
            {
                var operatorSize = fontSize * 0.90;
                var approach = MeasureNode(limit.Approach, context.ToScriptChild(), metrics, emptySlots);
                var body = MeasureNode(limit.Body, context, metrics, emptySlots);
                var op = MeasureGlyph("lim", operatorSize, style, metrics, italic: false);
                var gap = fontSize * 0.16;
                var ascent = Math.Max(body.Ascent, op.Measurement.Ascent);
                var descent = Math.Max(body.Descent, op.Measurement.Descent);
                var children = ImmutableArray.CreateBuilder<NodeMetric>(2);
                var xs = ImmutableArray.CreateBuilder<double>(2);
                var ys = ImmutableArray.CreateBuilder<double>(2);
                double operatorX;
                double bodyX;
                if (context.IsDisplayStyle)
                {
                    var lowerBaseline = op.Measurement.Descent + gap + approach.Ascent;
                    var operatorWidth = Math.Max(op.Measurement.Width, approach.Width);
                    operatorX = (operatorWidth - op.Measurement.Width) / 2;
                    children.Add(approach);
                    xs.Add((operatorWidth - approach.Width) / 2);
                    ys.Add(lowerBaseline);
                    descent = Math.Max(body.Descent, lowerBaseline + approach.Descent);
                    bodyX = operatorWidth + gap;
                }
                else
                {
                    var approachX = op.Measurement.Width + gap;
                    var approachBaseline = fontSize * 0.22;
                    operatorX = 0;
                    children.Add(approach);
                    xs.Add(approachX);
                    ys.Add(approachBaseline);
                    ascent = Math.Max(ascent, approach.Ascent - approachBaseline);
                    descent = Math.Max(body.Descent, approachBaseline + approach.Descent);
                    bodyX = approachX + approach.Width + gap;
                }
                children.Add(body);
                xs.Add(bodyX);
                ys.Add(0);
                return new NodeMetric(node, style, fontSize, bodyX + body.Width, ascent, descent,
                    children.ToImmutable(), xs.ToImmutable(), ys.ToImmutable(),
                    glyphs: ImmutableArray.Create(op with { X = operatorX, Baseline = 0 }));
            }
            case BracketsNode brackets:
            {
                var content = MeasureNode(brackets.Content, context, metrics, emptySlots);
                var shapeThickness = Math.Max(1, fontSize * 0.045);
                var delimiterWidth = Math.Max(fontSize * 0.42, shapeThickness * 2 + 2);
                var contentGap = Math.Max(fontSize * 0.10, shapeThickness + 1);
                var verticalPadding = Math.Max(fontSize * 0.08, shapeThickness);
                var width = content.Width + (delimiterWidth + contentGap) * 2;
                var ascent = Math.Max(content.Ascent + verticalPadding, fontSize * 0.78);
                var descent = Math.Max(content.Descent + verticalPadding, fontSize * 0.22);
                return new NodeMetric(node, style, fontSize, width, ascent, descent,
                    ImmutableArray.Create(content), ImmutableArray.Create(delimiterWidth + contentGap), ImmutableArray.Create(0d),
                    shapeThickness: shapeThickness);
            }
            case CasesNode cases:
            {
                var childContext = context.Compact();
                var children = ImmutableArray.CreateBuilder<NodeMetric>(cases.Rows.Length * 2);
                var expressionWidth = 0d;
                var conditionWidth = 0d;
                var rowAscents = new double[cases.Rows.Length];
                var rowDescents = new double[cases.Rows.Length];
                for (var index = 0; index < cases.Rows.Length; index++)
                {
                    var expression = MeasureNode(cases.Rows[index].Expression, childContext, metrics, emptySlots);
                    var condition = MeasureNode(cases.Rows[index].Condition, childContext, metrics, emptySlots);
                    children.Add(expression);
                    children.Add(condition);
                    expressionWidth = Math.Max(expressionWidth, expression.Width);
                    conditionWidth = Math.Max(conditionWidth, condition.Width);
                    rowAscents[index] = Math.Max(expression.Ascent, condition.Ascent);
                    rowDescents[index] = Math.Max(expression.Descent, condition.Descent);
                }
                var shapeThickness = Math.Max(1, fontSize * 0.045);
                var braceWidth = Math.Max(fontSize * 0.42, shapeThickness * 2 + 2);
                var padding = Math.Max(fontSize * 0.10, shapeThickness + 1);
                var columnGap = fontSize * 0.22;
                var rowGap = fontSize * 0.14;
                var contentHeight = rowAscents.Sum() + rowDescents.Sum() + rowGap * (cases.Rows.Length - 1);
                var verticalPadding = Math.Max(fontSize * 0.06, shapeThickness);
                var ascent = contentHeight / 2 + verticalPadding;
                var descent = contentHeight / 2 + verticalPadding;
                var xs = ImmutableArray.CreateBuilder<double>(children.Count);
                var ys = ImmutableArray.CreateBuilder<double>(children.Count);
                var rowBaseline = -contentHeight / 2;
                for (var index = 0; index < cases.Rows.Length; index++)
                {
                    rowBaseline += rowAscents[index];
                    xs.Add(braceWidth + padding + (expressionWidth - children[index * 2].Width) / 2);
                    ys.Add(rowBaseline);
                    xs.Add(braceWidth + padding + expressionWidth + columnGap +
                        (conditionWidth - children[index * 2 + 1].Width) / 2);
                    ys.Add(rowBaseline);
                    rowBaseline += rowDescents[index] + rowGap;
                }
                var width = braceWidth + padding + expressionWidth + columnGap + conditionWidth + padding;
                return new NodeMetric(node, style, fontSize, width, ascent, descent,
                    children.MoveToImmutable(), xs.MoveToImmutable(), ys.MoveToImmutable(),
                    shapeThickness: shapeThickness);
            }
            default:
                throw new InvalidOperationException($"지원하지 않는 수식 노드입니다: {node.GetType().Name}");
        }
    }

    private static NodeMetric MeasureTextNode(TextNode node, double fontSize, MathStyle style,
        IMathTextMetrics metrics, bool requiredEmpty)
    {
        var starts = StringInfo.ParseCombiningCharacters(node.Text);
        if (starts.Length == 0)
        {
            var width = requiredEmpty ? fontSize * 0.52 : 0;
            var ascent = fontSize * 0.72;
            var descent = fontSize * 0.24;
            return new NodeMetric(node, style, fontSize, width, ascent, descent,
                textRuns: ImmutableArray<TextRunMetric>.Empty,
                caretX: ImmutableArray.Create(0d));
        }

        var runs = ImmutableArray.CreateBuilder<TextRunMetric>();
        var caretX = new double[starts.Length + 1];
        var x = 0d;
        var ascentMax = fontSize * 0.72;
        var descentMax = fontSize * 0.24;
        var elementStart = 0;
        while (elementStart < starts.Length)
        {
            var italic = GetTextElementItalic(node, starts, elementStart);
            var elementEnd = elementStart + 1;
            while (elementEnd < starts.Length && GetTextElementItalic(node, starts, elementEnd) == italic)
                elementEnd++;
            var charStart = starts[elementStart];
            var charEnd = elementEnd == starts.Length ? node.Text.Length : starts[elementEnd];
            var runText = node.Text[charStart..charEnd];
            var measurement = metrics.Measure(runText, style.FontFamily, fontSize, italic);
            if (measurement.CaretX.Length != elementEnd - elementStart + 1)
                throw new InvalidOperationException("글꼴 측정기가 Unicode text-element caret 위치를 반환하지 않았습니다.");
            for (var caretIndex = 0; caretIndex < measurement.CaretX.Length; caretIndex++)
                caretX[elementStart + caretIndex] = x + measurement.CaretX[caretIndex];
            runs.Add(new TextRunMetric(runText, x, fontSize, style.FontFamily, style.Foreground,
                italic, elementStart, measurement));
            x += measurement.Width;
            ascentMax = Math.Max(ascentMax, measurement.Ascent);
            descentMax = Math.Max(descentMax, measurement.Descent);
            elementStart = elementEnd;
        }

        return new NodeMetric(node, style, fontSize, x, ascentMax, descentMax,
            textRuns: runs.ToImmutable(), caretX: caretX.ToImmutableArray());
    }

    private readonly record struct MathLayoutContext(
        MathStyle Style,
        double BaseFontSize,
        int SizeAnchorLevel,
        int ScriptLevel,
        bool IsDisplayStyle)
    {
        public double FontSize
        {
            get
            {
                var levels = Math.Max(0, ScriptLevel - SizeAnchorLevel);
                var scaled = BaseFontSize * Math.Pow(ScriptScale, levels);
                if (double.IsNaN(scaled) || scaled <= 1)
                    return 1;
                if (!double.IsFinite(scaled))
                    throw new InvalidOperationException("수학 글자 크기 계산 결과가 유한하지 않습니다.");
                return scaled;
            }
        }

        public static MathLayoutContext FromStyle(MathStyle style)
        {
            var level = LevelFor(style.TypesettingStyle);
            return new MathLayoutContext(style, style.FontSize, 0, level,
                style.TypesettingStyle == MathTypesettingStyle.Display);
        }

        public MathLayoutContext ApplyOverride(MathStyleOverride? styleOverride)
        {
            if (styleOverride is null)
                return this;

            var context = this;
            var style = Style;
            if (styleOverride.TypesettingStyle is MathTypesettingStyle preset)
            {
                var level = LevelFor(preset);
                if (level < context.SizeAnchorLevel)
                    context = context with { BaseFontSize = context.FontSize, SizeAnchorLevel = level };
                context = context with { ScriptLevel = level, IsDisplayStyle = preset == MathTypesettingStyle.Display };
            }
            if (styleOverride.FontSize is double fontSize)
                context = context with { BaseFontSize = fontSize, SizeAnchorLevel = context.ScriptLevel };
            if (styleOverride.FontFamily is string family)
                style = style with { FontFamily = family };
            if (styleOverride.Foreground is string foreground)
                style = style with { Foreground = foreground };
            return context with { Style = style };
        }

        public MathLayoutContext Compact() => this with { IsDisplayStyle = false };

        public MathLayoutContext ToScriptChild() => this with
        {
            ScriptLevel = ScriptLevel == int.MaxValue ? int.MaxValue : ScriptLevel + 1,
            IsDisplayStyle = false
        };

        public MathLayoutContext ForFractionChild()
        {
            if (!IsDisplayStyle)
                return ToScriptChild();
            var fontSize = FontSize;
            return this with { BaseFontSize = fontSize, SizeAnchorLevel = 0, ScriptLevel = 0, IsDisplayStyle = false };
        }

        public MathStyle ToMathStyle() => Style with
        {
            FontSize = FontSize,
            TypesettingStyle = CurrentPreset
        };

        private MathTypesettingStyle CurrentPreset => IsDisplayStyle && ScriptLevel == 0
            ? MathTypesettingStyle.Display
            : ScriptLevel switch
            {
                <= 0 => MathTypesettingStyle.Text,
                1 => MathTypesettingStyle.Script,
                _ => MathTypesettingStyle.ScriptScript
            };

        private static int LevelFor(MathTypesettingStyle preset) => preset switch
        {
            MathTypesettingStyle.Display or MathTypesettingStyle.Text => 0,
            MathTypesettingStyle.Script => 1,
            MathTypesettingStyle.ScriptScript => 2,
            _ => 0
        };
    }

    private static GlyphMetric MeasureGlyph(string text, double fontSize, MathStyle style,
        IMathTextMetrics metrics, bool italic)
    {
        var measurement = metrics.Measure(text, style.FontFamily, fontSize, italic);
        return new GlyphMetric(text, 0, 0, fontSize, italic, measurement);
    }

    private static bool GetTextElementItalic(TextNode node, int[] starts, int index) => node.Role switch
    {
        MathTextRole.Automatic => IsItalicTextElement(node.Text, starts, index),
        MathTextRole.Identifier => true,
        _ => false
    };

    private static bool IsItalicTextElement(string text, int[] starts, int index)
    {
        var start = starts[index];
        var end = index + 1 == starts.Length ? text.Length : starts[index + 1];
        for (var i = start; i < end; i++)
            if (char.IsLetter(text[i]))
                return true;
        return false;
    }

    private static double SequenceGap(NodeMetric previous, NodeMetric next, double fontSize)
    {
        if (previous.Node is MathSpaceNode || next.Node is MathSpaceNode)
            return 0;
        var previousText = previous.Node as TextNode;
        var nextText = next.Node as TextNode;
        if ((previousText?.Text.EndsWith(' ') ?? false) || (nextText?.Text.StartsWith(' ') ?? false))
            return 0;
        var previousIsStructural = previous.Node is FractionNode or RadicalNode or MatrixNode or IntegralNode or
            SummationNode or LimitNode or BracketsNode or CasesNode;
        var nextIsStructural = next.Node is FractionNode or RadicalNode or MatrixNode or IntegralNode or
            SummationNode or LimitNode or BracketsNode or CasesNode;
        return previousIsStructural || nextIsStructural ? fontSize * 0.10 : fontSize * 0.06;
    }

    private static double? FindTopLevelEqualityOffset(NodeMetric metric)
    {
        return FindTopLevelEqualityOffset(metric, 0);
    }

    private static double? FindTopLevelEqualityOffset(NodeMetric metric, double originX)
    {
        if (metric.Node is TextNode text && IsMathEqualityRole(text.Role))
        {
            var local = FindEqualityInText(text.Text, metric.CaretX);
            return local is double offset ? originX + offset : null;
        }
        if (metric.Node is not SequenceNode sequence)
            return null;

        for (var index = 0; index < sequence.Children.Length; index++)
        {
            var nested = FindTopLevelEqualityOffset(metric.Children[index], originX + metric.ChildX[index]);
            if (nested is double offset)
                return offset;
        }
        return null;
    }

    private static bool IsMathEqualityRole(MathTextRole role) =>
        role is MathTextRole.Automatic or MathTextRole.MathSymbol;

    private static double? FindEqualityInText(string text, ImmutableArray<double> caretX)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        for (var index = 0; index < starts.Length; index++)
        {
            var end = index + 1 == starts.Length ? text.Length : starts[index + 1];
            if (text.AsSpan(starts[index], end - starts[index]).SequenceEqual("="))
                return caretX[index];
        }
        return null;
    }

    private static void DrawNode(NodeMetric metric, double x, double baseline, ImmutableArray<Guid> ancestors,
        MathStyle inheritedStyle, ImmutableArray<RenderPrimitive>.Builder primitives, ImmutableArray<NodeLayout>.Builder nodes)
    {
        var style = metric.Style;
        var bounds = new MathRect(x, baseline - metric.Ascent, metric.Width, metric.Ascent + metric.Descent);
        var caretStops = ImmutableArray<TextCaretStop>.Empty;
        if (metric.Node is TextNode && !metric.CaretX.IsDefault)
        {
            var caretBuilder = ImmutableArray.CreateBuilder<TextCaretStop>(metric.CaretX.Length);
            for (var i = 0; i < metric.CaretX.Length; i++)
                caretBuilder.Add(new TextCaretStop(i, new MathPoint(x + metric.CaretX[i], baseline)));
            caretStops = caretBuilder.MoveToImmutable();
        }
        nodes.Add(new NodeLayout(metric.Node.Id, MathTree.KindOf(metric.Node), ancestors, bounds, baseline, caretStops));

        foreach (var glyph in metric.Glyphs)
            primitives.Add(new GlyphPrimitive(metric.Node.Id, glyph.Text,
                new MathPoint(x + glyph.X, baseline + glyph.Baseline), glyph.FontSize,
                style.FontFamily, style.Foreground, glyph.Italic));

        if (metric.Node is TextNode)
        {
            foreach (var run in metric.TextRuns)
                primitives.Add(new GlyphPrimitive(metric.Node.Id, run.Text,
                    new MathPoint(x + run.X, baseline), run.FontSize, run.FontFamily, run.Fill, run.Italic));
            return;
        }

        var childAncestors = ancestors.Add(metric.Node.Id);
        switch (metric.Node)
        {
            case SequenceNode:
                for (var i = 0; i < metric.Children.Length; i++)
                    DrawNode(metric.Children[i], x + metric.ChildX[i], baseline + metric.ChildBaseline[i],
                        childAncestors, style, primitives, nodes);
                break;
            case FractionNode:
                DrawNode(metric.Children[0], x + metric.ChildX[0], baseline + metric.ChildBaseline[0],
                    childAncestors, style, primitives, nodes);
                DrawNode(metric.Children[1], x + metric.ChildX[1], baseline + metric.ChildBaseline[1],
                    childAncestors, style, primitives, nodes);
                primitives.Add(new LinePrimitive(
                    new MathPoint(x + metric.ShapeThickness, baseline + metric.ShapeOffsetY),
                    new MathPoint(x + metric.Width - metric.ShapeThickness, baseline + metric.ShapeOffsetY),
                    metric.ShapeThickness, style.Foreground));
                break;
            case RadicalNode:
            {
                var rootWidth = metric.ChildX[0];
                var topY = baseline - metric.Ascent + metric.FontSize * 0.04;
                var points = ImmutableArray.Create(
                    new MathPoint(x + metric.FontSize * 0.02, baseline - metric.FontSize * 0.02),
                    new MathPoint(x + metric.FontSize * 0.22, baseline - metric.FontSize * 0.02),
                    new MathPoint(x + metric.FontSize * 0.36, baseline + metric.FontSize * 0.18),
                    new MathPoint(x + rootWidth * 0.78, topY),
                    new MathPoint(x + metric.Width, topY));
                primitives.Add(new PolylinePrimitive(points, Closed: false, metric.ShapeThickness, style.Foreground));
                DrawNode(metric.Children[0], x + metric.ChildX[0], baseline + metric.ChildBaseline[0],
                    childAncestors, style, primitives, nodes);
                break;
            }
            case ScriptNode:
                for (var i = 0; i < metric.Children.Length; i++)
                    DrawNode(metric.Children[i], x + metric.ChildX[i], baseline + metric.ChildBaseline[i],
                        childAncestors, style, primitives, nodes);
                break;
            case MatrixNode matrix:
            {
                var brace = metric.FontSize * 0.20;
                var top = baseline - metric.Ascent + metric.FontSize * 0.04;
                var bottom = baseline + metric.Descent - metric.FontSize * 0.04;
                var left = x + brace;
                var right = x + metric.Width - brace;
                primitives.Add(new PolylinePrimitive(ImmutableArray.Create(
                    new MathPoint(left + brace * 0.65, top), new MathPoint(left, top),
                    new MathPoint(left, bottom), new MathPoint(left + brace * 0.65, bottom)),
                    Closed: false, metric.ShapeThickness, style.Foreground));
                primitives.Add(new PolylinePrimitive(ImmutableArray.Create(
                    new MathPoint(right - brace * 0.65, top), new MathPoint(right, top),
                    new MathPoint(right, bottom), new MathPoint(right - brace * 0.65, bottom)),
                    Closed: false, metric.ShapeThickness, style.Foreground));
                for (var i = 0; i < matrix.Cells.Length; i++)
                    DrawNode(metric.Children[i], x + metric.ChildX[i], baseline + metric.ChildBaseline[i],
                        childAncestors, style, primitives, nodes);
                break;
            }
            case IntegralNode:
            case SummationNode:
            case LimitNode:
                for (var i = 0; i < metric.Children.Length; i++)
                    DrawNode(metric.Children[i], x + metric.ChildX[i], baseline + metric.ChildBaseline[i],
                        childAncestors, style, primitives, nodes);
                break;
            case BracketsNode brackets:
            {
                var contentX = metric.ChildX[0];
                var contentWidth = metric.Children[0].Width;
                var delimiterWidth = Math.Max(metric.FontSize * 0.42, metric.ShapeThickness * 2 + 2);
                var contentGap = Math.Max(metric.FontSize * 0.10, metric.ShapeThickness + 1);
                var halfStroke = metric.ShapeThickness / 2;
                var top = baseline - metric.Ascent + halfStroke;
                var bottom = baseline + metric.Descent - halfStroke;
                var rightColumnX = x + delimiterWidth + contentGap + contentWidth + contentGap;
                var leftPoints = CreateDelimiterPoints(brackets.Kind, x, top, bottom,
                    delimiterWidth, metric.ShapeThickness, rightSide: false);
                var rightPoints = CreateDelimiterPoints(brackets.Kind, rightColumnX, top, bottom,
                    delimiterWidth, metric.ShapeThickness, rightSide: true);
                primitives.Add(new PolylinePrimitive(leftPoints, Closed: false, metric.ShapeThickness, style.Foreground));
                primitives.Add(new PolylinePrimitive(rightPoints, Closed: false, metric.ShapeThickness, style.Foreground));
                DrawNode(metric.Children[0], x + contentX, baseline + metric.ChildBaseline[0],
                    childAncestors, style, primitives, nodes);
                break;
            }
            case CasesNode:
            {
                var braceWidth = Math.Max(metric.FontSize * 0.42, metric.ShapeThickness * 2 + 2);
                var halfStroke = metric.ShapeThickness / 2;
                var top = baseline - metric.Ascent + halfStroke;
                var bottom = baseline + metric.Descent - halfStroke;
                var points = CreateDelimiterPoints(MathBracketKind.Curly, x, top, bottom,
                    braceWidth, metric.ShapeThickness, rightSide: false);
                primitives.Add(new PolylinePrimitive(points, Closed: false, metric.ShapeThickness, style.Foreground));
                for (var i = 0; i < metric.Children.Length; i++)
                    DrawNode(metric.Children[i], x + metric.ChildX[i], baseline + metric.ChildBaseline[i],
                        childAncestors, style, primitives, nodes);
                break;
            }
        }

    }

    private static ImmutableArray<MathPoint> CreateDelimiterPoints(MathBracketKind kind, double columnX,
        double top, double bottom, double columnWidth, double strokeWidth, bool rightSide)
    {
        var halfStroke = strokeWidth / 2;
        var innerWidth = Math.Max(0, columnWidth - strokeWidth);
        var innerHeight = Math.Max(0, bottom - top);
        var startX = columnX + halfStroke;
        var startY = top;
        (double X, double Y)[] normalized = kind switch
        {
            MathBracketKind.Parentheses =>
            [
                (0.82, 0.00), (0.58, 0.10), (0.37, 0.25), (0.20, 0.40),
                (0.14, 0.50), (0.20, 0.60), (0.37, 0.75), (0.58, 0.90), (0.82, 1.00)
            ],
            MathBracketKind.Square =>
            [
                (0.88, 0.00), (0.14, 0.00), (0.14, 1.00), (0.88, 1.00)
            ],
            MathBracketKind.Curly =>
            [
                (0.84, 0.00), (0.57, 0.08), (0.52, 0.23), (0.40, 0.36),
                (0.20, 0.44), (0.12, 0.50), (0.20, 0.56), (0.40, 0.64),
                (0.52, 0.77), (0.57, 0.92), (0.84, 1.00)
            ],
            MathBracketKind.Angle =>
            [
                (0.84, 0.00), (0.16, 0.50), (0.84, 1.00)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        return normalized.Select(point =>
        {
            var fractionX = rightSide ? 1 - point.X : point.X;
            return new MathPoint(startX + innerWidth * fractionX,
                startY + innerHeight * point.Y);
        }).ToImmutableArray();
    }

    private static ImmutableArray<LayoutDiagnostic> CollectDiagnostics(EquationWorkspace workspace)
    {
        var diagnostics = ImmutableArray.CreateBuilder<LayoutDiagnostic>();
        var diagnosed = new HashSet<Guid>();
        foreach (var group in workspace.Groups)
        foreach (var row in group.Rows)
        {
            if (MathTree.IsSemanticallyEmpty(row.Root))
                Add(row.Root.Id, "수식 행이 비어 있습니다.");
            Visit(row.Root);
        }
        return diagnostics.ToImmutable();

        void Add(Guid id, string message)
        {
            if (diagnosed.Add(id))
                diagnostics.Add(new LayoutDiagnostic(LayoutDiagnosticCode.EmptySlot, id, message));
        }

        void Visit(MathNode node)
        {
            if (node is TextNode { IsEmptySlot: true, Text.Length: 0 })
                Add(node.Id, "수식 편집 슬롯이 비어 있습니다.");

            switch (node)
            {
                case SequenceNode sequence:
                    foreach (var child in sequence.Children)
                        Visit(child);
                    break;
                case FractionNode fraction:
                    AddIfEmpty(fraction.Numerator, "분수 분자가 비어 있습니다.");
                    AddIfEmpty(fraction.Denominator, "분수 분모가 비어 있습니다.");
                    Visit(fraction.Numerator);
                    Visit(fraction.Denominator);
                    break;
                case RadicalNode radical:
                    AddIfEmpty(radical.Radicand, "근호 내부가 비어 있습니다.");
                    Visit(radical.Radicand);
                    break;
                case ScriptNode script:
                    AddIfEmpty(script.Base, "첨자 밑이 비어 있습니다.");
                    Visit(script.Base);
                    if (script.Subscript is not null)
                    {
                        AddIfEmpty(script.Subscript, "아래첨자가 비어 있습니다.");
                        Visit(script.Subscript);
                    }
                    if (script.Superscript is not null)
                    {
                        AddIfEmpty(script.Superscript, "위첨자가 비어 있습니다.");
                        Visit(script.Superscript);
                    }
                    break;
                case MatrixNode matrix:
                    foreach (var cell in matrix.Cells)
                    {
                        AddIfEmpty(cell, "행렬 셀이 비어 있습니다.");
                        Visit(cell);
                    }
                    break;
                case IntegralNode integral:
                    if (integral.LowerLimit is not null)
                    {
                        AddIfEmpty(integral.LowerLimit, "적분 하한이 비어 있습니다.");
                        Visit(integral.LowerLimit);
                    }
                    if (integral.UpperLimit is not null)
                    {
                        AddIfEmpty(integral.UpperLimit, "적분 상한이 비어 있습니다.");
                        Visit(integral.UpperLimit);
                    }
                    AddIfEmpty(integral.Integrand, "적분식이 비어 있습니다.");
                    Visit(integral.Integrand);
                    AddIfEmpty(integral.DifferentialVariable, "적분 변수가 비어 있습니다.");
                    Visit(integral.DifferentialVariable);
                    break;
                case SummationNode summation:
                    if (summation.LowerLimit is not null)
                    {
                        AddIfEmpty(summation.LowerLimit, "합의 하한이 비어 있습니다.");
                        Visit(summation.LowerLimit);
                    }
                    if (summation.UpperLimit is not null)
                    {
                        AddIfEmpty(summation.UpperLimit, "합의 상한이 비어 있습니다.");
                        Visit(summation.UpperLimit);
                    }
                    AddIfEmpty(summation.Body, "합의 식이 비어 있습니다.");
                    Visit(summation.Body);
                    break;
                case LimitNode limit:
                    AddIfEmpty(limit.Approach, "극한 접근식이 비어 있습니다.");
                    AddIfEmpty(limit.Body, "극한의 식이 비어 있습니다.");
                    Visit(limit.Approach);
                    Visit(limit.Body);
                    break;
                case BracketsNode brackets:
                    AddIfEmpty(brackets.Content, "괄호 내부가 비어 있습니다.");
                    Visit(brackets.Content);
                    break;
                case CasesNode cases:
                    foreach (var caseRow in cases.Rows)
                    {
                        AddIfEmpty(caseRow.Expression, "경우 나누기 식이 비어 있습니다.");
                        AddIfEmpty(caseRow.Condition, "경우 나누기 조건이 비어 있습니다.");
                        Visit(caseRow.Expression);
                        Visit(caseRow.Condition);
                    }
                    break;
            }
        }

        void AddIfEmpty(MathNode node, string message)
        {
            if (MathTree.IsSemanticallyEmpty(node))
                Add(node.Id, message);
        }
    }


    private sealed class NodeMetric
    {
        public NodeMetric(MathNode node, MathStyle style, double fontSize, double width, double ascent, double descent,
            ImmutableArray<NodeMetric> children = default,
            ImmutableArray<double> childX = default,
            ImmutableArray<double> childBaseline = default,
            ImmutableArray<TextRunMetric> textRuns = default,
            ImmutableArray<double> caretX = default,
            double shapeOffsetY = 0,
            double shapeThickness = 0,
            ImmutableArray<GlyphMetric> glyphs = default)
        {
            Node = node;
            Style = style;
            FontSize = fontSize;
            Width = width;
            Ascent = ascent;
            Descent = descent;
            Children = children.IsDefault ? ImmutableArray<NodeMetric>.Empty : children;
            ChildX = childX.IsDefault ? ImmutableArray<double>.Empty : childX;
            ChildBaseline = childBaseline.IsDefault ? ImmutableArray<double>.Empty : childBaseline;
            TextRuns = textRuns.IsDefault ? ImmutableArray<TextRunMetric>.Empty : textRuns;
            CaretX = caretX;
            ShapeOffsetY = shapeOffsetY;
            ShapeThickness = shapeThickness;
            Glyphs = glyphs.IsDefault ? ImmutableArray<GlyphMetric>.Empty : glyphs;
        }

        public MathNode Node { get; }
        public MathStyle Style { get; }
        public double FontSize { get; }
        public double Width { get; }
        public double Ascent { get; }
        public double Descent { get; }
        public ImmutableArray<NodeMetric> Children { get; }
        public ImmutableArray<double> ChildX { get; }
        public ImmutableArray<double> ChildBaseline { get; }
        public ImmutableArray<TextRunMetric> TextRuns { get; }
        public ImmutableArray<double> CaretX { get; }
        public double ShapeOffsetY { get; }
        public double ShapeThickness { get; }
        public ImmutableArray<GlyphMetric> Glyphs { get; }
    }

    private sealed record TextRunMetric(
        string Text,
        double X,
        double FontSize,
        string FontFamily,
        string Fill,
        bool Italic,
        int TextElementStart,
        TextMeasurement Measurement);

    private sealed record GlyphMetric(string Text, double X, double Baseline, double FontSize,
        bool Italic, TextMeasurement Measurement);
}
