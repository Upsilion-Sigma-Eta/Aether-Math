using System.Collections.Immutable;

namespace AetherMath.Core;

internal static class MathTree
{
    public static bool TryFindNode(EquationWorkspace workspace, Guid id, out MathNode? node,
        out ImmutableArray<Guid> ancestors)
    {
        foreach (var group in workspace.Groups)
        {
            foreach (var row in group.Rows)
            {
                var path = new List<Guid>();
                if (TryFind(row.Root, id, path, out node))
                {
                    ancestors = path.ToImmutableArray();
                    return true;
                }
            }
        }

        node = null;
        ancestors = ImmutableArray<Guid>.Empty;
        return false;
    }

    public static TextNode? FindTextNode(EquationWorkspace workspace, Guid id)
    {
        foreach (var group in workspace.Groups)
        foreach (var row in group.Rows)
            if (TryFindText(row.Root, id, out var found))
                return found;
        return null;
    }

    public static bool TryFindContainingRow(EquationWorkspace workspace, Guid nodeId,
        out int groupIndex, out int rowIndex, out EquationGroup? group, out EquationRow? row)
    {
        for (var currentGroupIndex = 0; currentGroupIndex < workspace.Groups.Length; currentGroupIndex++)
        {
            var currentGroup = workspace.Groups[currentGroupIndex];
            for (var currentRowIndex = 0; currentRowIndex < currentGroup.Rows.Length; currentRowIndex++)
            {
                var currentRow = currentGroup.Rows[currentRowIndex];
                if (!TryFind(currentRow.Root, nodeId, new List<Guid>(), out _))
                    continue;

                groupIndex = currentGroupIndex;
                rowIndex = currentRowIndex;
                group = currentGroup;
                row = currentRow;
                return true;
            }
        }

        groupIndex = -1;
        rowIndex = -1;
        group = null;
        row = null;
        return false;
    }

    public static MathNode CloneWithNewIds(MathNode node, IDictionary<Guid, Guid> idMap)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(idMap);

        var newId = Guid.NewGuid();
        idMap.Add(node.Id, newId);
        return node switch
        {
            TextNode text => text with { Id = newId },
            MathSpaceNode space => space with { Id = newId },
            SequenceNode sequence => sequence with
            {
                Id = newId,
                Children = sequence.Children.Select(child => CloneWithNewIds(child, idMap)).ToImmutableArray()
            },
            FractionNode fraction => fraction with
            {
                Id = newId,
                Numerator = CloneWithNewIds(fraction.Numerator, idMap),
                Denominator = CloneWithNewIds(fraction.Denominator, idMap)
            },
            RadicalNode radical => radical with
            {
                Id = newId,
                Radicand = CloneWithNewIds(radical.Radicand, idMap)
            },
            ScriptNode script => script with
            {
                Id = newId,
                Base = CloneWithNewIds(script.Base, idMap),
                Subscript = script.Subscript is null ? null : CloneWithNewIds(script.Subscript, idMap),
                Superscript = script.Superscript is null ? null : CloneWithNewIds(script.Superscript, idMap)
            },
            MatrixNode matrix => matrix with
            {
                Id = newId,
                Cells = matrix.Cells.Select(cell => CloneWithNewIds(cell, idMap)).ToImmutableArray()
            },
            IntegralNode integral => integral with
            {
                Id = newId,
                Integrand = CloneWithNewIds(integral.Integrand, idMap),
                LowerLimit = integral.LowerLimit is null ? null : CloneWithNewIds(integral.LowerLimit, idMap),
                UpperLimit = integral.UpperLimit is null ? null : CloneWithNewIds(integral.UpperLimit, idMap),
                DifferentialVariable = CloneWithNewIds(integral.DifferentialVariable, idMap)
            },
            SummationNode summation => summation with
            {
                Id = newId,
                Body = CloneWithNewIds(summation.Body, idMap),
                LowerLimit = summation.LowerLimit is null ? null : CloneWithNewIds(summation.LowerLimit, idMap),
                UpperLimit = summation.UpperLimit is null ? null : CloneWithNewIds(summation.UpperLimit, idMap)
            },
            LimitNode limit => limit with
            {
                Id = newId,
                Approach = CloneWithNewIds(limit.Approach, idMap),
                Body = CloneWithNewIds(limit.Body, idMap)
            },
            BracketsNode brackets => brackets with
            {
                Id = newId,
                Content = CloneWithNewIds(brackets.Content, idMap)
            },
            CasesNode cases => cases with
            {
                Id = newId,
                Rows = cases.Rows.Select(row => row with
                {
                    Expression = CloneWithNewIds(row.Expression, idMap),
                    Condition = CloneWithNewIds(row.Condition, idMap)
                }).ToImmutableArray()
            },
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };
    }

    public static MathNode? FindNearestStructure(EquationWorkspace workspace, Guid id, bool includeSelf)
    {
        if (!TryFindNode(workspace, id, out var node, out var ancestors))
            return null;

        if (includeSelf && IsStructure(node!))
            return node;

        for (var i = ancestors.Length - 1; i >= 0; i--)
        {
            if (TryFindNode(workspace, ancestors[i], out var ancestor, out _) && IsStructure(ancestor!))
                return ancestor;
        }

        return null;
    }

    public static MathNodeKind KindOf(MathNode node) => node switch
    {
        TextNode => MathNodeKind.Text,
        SequenceNode => MathNodeKind.Sequence,
        FractionNode => MathNodeKind.Fraction,
        RadicalNode => MathNodeKind.Radical,
        ScriptNode => MathNodeKind.Script,
        MatrixNode => MathNodeKind.Matrix,
        IntegralNode => MathNodeKind.Integral,
        SummationNode => MathNodeKind.Summation,
        LimitNode => MathNodeKind.Limit,
        BracketsNode => MathNodeKind.Brackets,
        CasesNode => MathNodeKind.Cases,
        MathSpaceNode => MathNodeKind.MathSpace,
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };

    public static bool IsStructure(MathNode node) => node is FractionNode or RadicalNode or ScriptNode or MatrixNode or
        IntegralNode or SummationNode or LimitNode or BracketsNode or CasesNode;

    public static bool HasClearableContent(MathNode node) => node switch
    {
        TextNode text => text.Text.Length > 0,
        MathSpaceNode => true,
        SequenceNode sequence => sequence.Children.Any(HasClearableContent),
        _ => true
    };

    public static bool TryGetContainingSequence(EquationWorkspace workspace, Guid nodeId,
        out SequenceNode? sequence, out int childIndex)
    {
        sequence = null;
        childIndex = -1;
        if (!TryFindNode(workspace, nodeId, out _, out var ancestors) || ancestors.IsDefaultOrEmpty)
            return false;
        if (!TryFindNode(workspace, ancestors[^1], out var parent, out _) || parent is not SequenceNode parentSequence)
            return false;
        var index = -1;
        for (var currentIndex = 0; currentIndex < parentSequence.Children.Length; currentIndex++)
        {
            if (parentSequence.Children[currentIndex].Id != nodeId)
                continue;
            index = currentIndex;
            break;
        }
        if (index < 0)
            return false;
        sequence = parentSequence;
        childIndex = index;
        return true;
    }

    public static TextNode? FindAdjacentTextToMathSpace(EquationWorkspace workspace, Guid spaceId, bool forward)
    {
        if (!TryFindNode(workspace, spaceId, out var node, out _) || node is not MathSpaceNode ||
            !TryGetContainingSequence(workspace, spaceId, out var sequence, out var childIndex) || sequence is null)
            return null;

        for (var index = childIndex + (forward ? 1 : -1);
             index >= 0 && index < sequence.Children.Length;
             index += forward ? 1 : -1)
        {
            var child = sequence.Children[index];
            if (child is MathSpaceNode)
                continue;
            return FindEdgeText(child, forward);
        }
        return null;
    }

    private static TextNode? FindEdgeText(MathNode node, bool forward)
    {
        if (node is TextNode text)
            return text;

        var children = ChildrenOf(node);
        if (forward)
        {
            foreach (var child in children)
            {
                var found = FindEdgeText(child, forward: true);
                if (found is not null)
                    return found;
            }
            return null;
        }

        foreach (var child in children.Reverse())
        {
            var found = FindEdgeText(child, forward: false);
            if (found is not null)
                return found;
        }
        return null;
    }

    public static EquationWorkspace ReplaceNodeWithSequenceChildren(EquationWorkspace workspace, Guid nodeId,
        ImmutableArray<MathNode> replacementChildren, out bool changed)
    {
        if (replacementChildren.IsDefaultOrEmpty || !TryFindNode(workspace, nodeId, out _, out var ancestors))
        {
            changed = false;
            return workspace;
        }

        if (TryGetContainingSequence(workspace, nodeId, out var parent, out var childIndex) && parent is not null)
        {
            var children = ImmutableArray.CreateBuilder<MathNode>(parent.Children.Length - 1 + replacementChildren.Length);
            for (var index = 0; index < parent.Children.Length; index++)
            {
                if (index == childIndex)
                    children.AddRange(replacementChildren);
                else
                    children.Add(parent.Children[index]);
            }
            return ReplaceNode(workspace, parent.Id, parent with { Children = children.ToImmutable() }, out changed);
        }

        return ReplaceNode(workspace, nodeId, new SequenceNode(Guid.NewGuid(), replacementChildren), out changed);
    }

    public static EquationWorkspace RemoveMathSpace(EquationWorkspace workspace, Guid nodeId, out bool changed,
        out TextNode? emptySequenceAnchor)
    {
        emptySequenceAnchor = null;
        if (!TryFindNode(workspace, nodeId, out var node, out _) || node is not MathSpaceNode ||
            !TryGetContainingSequence(workspace, nodeId, out var parent, out var childIndex) || parent is null)
        {
            changed = false;
            return workspace;
        }

        var children = parent.Children.RemoveAt(childIndex);
        if (children.IsEmpty)
        {
            // A Sequence must retain a required empty Text anchor when its last spacer is removed.
            // Keep the Sequence identity and style; ordinary spacer removal with siblings adds nothing.
            var anchor = new TextNode(Guid.NewGuid(), string.Empty, IsEmptySlot: true);
            emptySequenceAnchor = anchor;
            children = ImmutableArray.Create<MathNode>(anchor);
        }
        return ReplaceNode(workspace, parent.Id, parent with { Children = children }, out changed);
    }

    public static EquationWorkspace ReplaceNode(EquationWorkspace workspace, Guid id, MathNode replacement, out bool changed)
    {
        for (var groupIndex = 0; groupIndex < workspace.Groups.Length; groupIndex++)
        {
            var group = workspace.Groups[groupIndex];
            for (var rowIndex = 0; rowIndex < group.Rows.Length; rowIndex++)
            {
                var row = group.Rows[rowIndex];
                var updatedRoot = ReplaceNode(row.Root, id, replacement, out var rowChanged);
                if (!rowChanged)
                    continue;

                var rows = group.Rows.ToBuilder();
                rows[rowIndex] = row with { Root = updatedRoot };
                var groups = workspace.Groups.ToBuilder();
                groups[groupIndex] = group with { Rows = rows.ToImmutable() };
                changed = true;
                return workspace with { Groups = groups.ToImmutable() };
            }
        }

        changed = false;
        return workspace;
    }

    public static IEnumerable<MathNode> ChildrenOf(MathNode node)
    {
        switch (node)
        {
            case SequenceNode sequence:
                foreach (var child in sequence.Children)
                    yield return child;
                break;
            case MathSpaceNode:
                break;
            case FractionNode fraction:
                yield return fraction.Numerator;
                yield return fraction.Denominator;
                break;
            case RadicalNode radical:
                yield return radical.Radicand;
                break;
            case ScriptNode script:
                yield return script.Base;
                if (script.Subscript is not null)
                    yield return script.Subscript;
                if (script.Superscript is not null)
                    yield return script.Superscript;
                break;
            case MatrixNode matrix:
                foreach (var cell in matrix.Cells)
                    yield return cell;
                break;
            case IntegralNode integral:
                if (integral.LowerLimit is not null)
                    yield return integral.LowerLimit;
                if (integral.UpperLimit is not null)
                    yield return integral.UpperLimit;
                yield return integral.Integrand;
                yield return integral.DifferentialVariable;
                break;
            case SummationNode summation:
                if (summation.LowerLimit is not null)
                    yield return summation.LowerLimit;
                if (summation.UpperLimit is not null)
                    yield return summation.UpperLimit;
                yield return summation.Body;
                break;
            case LimitNode limit:
                yield return limit.Approach;
                yield return limit.Body;
                break;
            case BracketsNode brackets:
                yield return brackets.Content;
                break;
            case CasesNode cases:
                foreach (var row in cases.Rows)
                {
                    yield return row.Expression;
                    yield return row.Condition;
                }
                break;
        }
    }

    public static bool IsSemanticallyEmpty(MathNode node)
    {
        if (node is TextNode text)
            return text.Text.Length == 0;
        if (node is MathSpaceNode)
            return true;
        if (node is not SequenceNode sequence)
            return false;
        foreach (var child in sequence.Children)
            if (!IsSemanticallyEmpty(child))
                return false;
        return true;
    }

    private static bool TryFind(MathNode current, Guid id, List<Guid> path, out MathNode? found)
    {
        if (current.Id == id)
        {
            found = current;
            return true;
        }

        path.Add(current.Id);
        foreach (var child in ChildrenOf(current))
        {
            if (TryFind(child, id, path, out found))
                return true;
        }
        path.RemoveAt(path.Count - 1);
        found = null;
        return false;
    }

    private static bool TryFindText(MathNode current, Guid id, out TextNode? found)
    {
        if (current is TextNode text)
        {
            found = text.Id == id ? text : null;
            return found is not null;
        }

        foreach (var child in ChildrenOf(current))
            if (TryFindText(child, id, out found))
                return true;

        found = null;
        return false;
    }

    private static MathNode ReplaceNode(MathNode current, Guid id, MathNode replacement, out bool changed)
    {
        if (current.Id == id)
        {
            changed = true;
            return replacement;
        }

        switch (current)
        {
            case SequenceNode sequence:
                for (var index = 0; index < sequence.Children.Length; index++)
                {
                    var updated = ReplaceNode(sequence.Children[index], id, replacement, out changed);
                    if (!changed)
                        continue;
                    var children = sequence.Children.ToBuilder();
                    children[index] = updated;
                    return sequence with { Children = children.ToImmutable() };
                }
                changed = false;
                return sequence;
            case FractionNode fraction:
            {
                var numerator = ReplaceNode(fraction.Numerator, id, replacement, out changed);
                if (changed)
                    return fraction with { Numerator = numerator };
                var denominator = ReplaceNode(fraction.Denominator, id, replacement, out changed);
                return changed ? fraction with { Denominator = denominator } : fraction;
            }
            case RadicalNode radical:
            {
                var radicand = ReplaceNode(radical.Radicand, id, replacement, out changed);
                return changed ? radical with { Radicand = radicand } : radical;
            }
            case ScriptNode script:
            {
                var @base = ReplaceNode(script.Base, id, replacement, out changed);
                if (changed)
                    return script with { Base = @base };
                if (script.Subscript is not null)
                {
                    var subscript = ReplaceNode(script.Subscript, id, replacement, out changed);
                    if (changed)
                        return script with { Subscript = subscript };
                }
                if (script.Superscript is not null)
                {
                    var superscript = ReplaceNode(script.Superscript, id, replacement, out changed);
                    if (changed)
                        return script with { Superscript = superscript };
                }
                return script;
            }
            case MatrixNode matrix:
                for (var index = 0; index < matrix.Cells.Length; index++)
                {
                    var updated = ReplaceNode(matrix.Cells[index], id, replacement, out changed);
                    if (!changed)
                        continue;
                    var cells = matrix.Cells.ToBuilder();
                    cells[index] = updated;
                    return matrix with { Cells = cells.ToImmutable() };
                }
                changed = false;
                return matrix;
            case IntegralNode integral:
            {
                var integrand = ReplaceNode(integral.Integrand, id, replacement, out changed);
                if (changed)
                    return integral with { Integrand = integrand };
                if (integral.LowerLimit is not null)
                {
                    var lower = ReplaceNode(integral.LowerLimit, id, replacement, out changed);
                    if (changed)
                        return integral with { LowerLimit = lower };
                }
                if (integral.UpperLimit is not null)
                {
                    var upper = ReplaceNode(integral.UpperLimit, id, replacement, out changed);
                    if (changed)
                        return integral with { UpperLimit = upper };
                }
                var differential = ReplaceNode(integral.DifferentialVariable, id, replacement, out changed);
                return changed ? integral with { DifferentialVariable = differential } : integral;
            }
            case SummationNode summation:
            {
                var body = ReplaceNode(summation.Body, id, replacement, out changed);
                if (changed)
                    return summation with { Body = body };
                if (summation.LowerLimit is not null)
                {
                    var lower = ReplaceNode(summation.LowerLimit, id, replacement, out changed);
                    if (changed)
                        return summation with { LowerLimit = lower };
                }
                if (summation.UpperLimit is not null)
                {
                    var upper = ReplaceNode(summation.UpperLimit, id, replacement, out changed);
                    if (changed)
                        return summation with { UpperLimit = upper };
                }
                return summation;
            }
            case LimitNode limit:
            {
                var approach = ReplaceNode(limit.Approach, id, replacement, out changed);
                if (changed)
                    return limit with { Approach = approach };
                var body = ReplaceNode(limit.Body, id, replacement, out changed);
                return changed ? limit with { Body = body } : limit;
            }
            case BracketsNode brackets:
            {
                var content = ReplaceNode(brackets.Content, id, replacement, out changed);
                return changed ? brackets with { Content = content } : brackets;
            }
            case CasesNode cases:
                for (var index = 0; index < cases.Rows.Length; index++)
                {
                    var row = cases.Rows[index];
                    var expression = ReplaceNode(row.Expression, id, replacement, out changed);
                    if (changed)
                    {
                        var rows = cases.Rows.ToBuilder();
                        rows[index] = row with { Expression = expression };
                        return cases with { Rows = rows.ToImmutable() };
                    }
                    var condition = ReplaceNode(row.Condition, id, replacement, out changed);
                    if (changed)
                    {
                        var rows = cases.Rows.ToBuilder();
                        rows[index] = row with { Condition = condition };
                        return cases with { Rows = rows.ToImmutable() };
                    }
                }
                changed = false;
                return cases;
            default:
                changed = false;
                return current;
        }
    }

}
