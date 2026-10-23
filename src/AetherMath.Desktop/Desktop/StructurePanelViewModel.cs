using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Desktop;

public sealed record StructurePanelItem(
    MathNodeKind Kind,
    Guid Id,
    Guid MathNodeId,
    string Label,
    int GroupNumber,
    int RowNumber,
    ImmutableArray<StructurePanelItem> Children);

public sealed class StructureNodeRevealRequestedEventArgs(Guid nodeId) : EventArgs
{
    public Guid NodeId { get; } = nodeId;
}

public sealed class StructurePanelViewModel : INotifyPropertyChanged
{
    private EditingSession _session;
    private ImmutableArray<StructurePanelItem> _items = ImmutableArray<StructurePanelItem>.Empty;
    private Dictionary<Guid, StructurePanelItem> _itemsById = new();
    private Dictionary<Guid, ImmutableArray<Guid>> _pathsById = new();
    private StructurePanelItem? _selectedItem;
    private string _activePath = string.Empty;

    public StructurePanelViewModel(EditingSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.PropertyChanged += OnSessionPropertyChanged;
        RebuildProjection();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<StructureNodeRevealRequestedEventArgs>? RevealNodeRequested;

    public ImmutableArray<StructurePanelItem> Items => _items;

    public StructurePanelItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is null)
                return;

            // Two-way bindings echo presentation updates through this setter. Only the explicit
            // SelectNode action may change the Session's whole-node selection state.
        }
    }

    public Guid? SelectedNodeId => _selectedItem?.MathNodeId;
    public string ActivePath => _activePath;

    public bool SelectNode(Guid nodeId)
    {
        if (!_itemsById.TryGetValue(nodeId, out var item) || !_session.TrySelectNode(nodeId))
            return false;

        SetSelectedItem(item);
        RevealNodeRequested?.Invoke(this, new StructureNodeRevealRequestedEventArgs(nodeId));
        return true;
    }

    internal void SetSession(EditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ReferenceEquals(_session, session))
        {
            RebuildProjection();
            return;
        }

        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session = session;
        _session.PropertyChanged += OnSessionPropertyChanged;
        RebuildProjection();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditingSession.Workspace))
        {
            RebuildProjection();
            return;
        }

        if (e.PropertyName is nameof(EditingSession.Caret) or nameof(EditingSession.Selection) or
            nameof(EditingSession.SelectedNodeId) or nameof(EditingSession.IsNodeSelected))
            SynchronizeSelectionFromSession();
    }

    private void RebuildProjection()
    {
        var itemsById = new Dictionary<Guid, StructurePanelItem>();
        var pathsById = new Dictionary<Guid, ImmutableArray<Guid>>();
        var roots = ImmutableArray.CreateBuilder<StructurePanelItem>();
        var workspace = _session.Workspace;

        for (var groupIndex = 0; groupIndex < workspace.Groups.Length; groupIndex++)
        {
            var group = workspace.Groups[groupIndex];
            for (var rowIndex = 0; rowIndex < group.Rows.Length; rowIndex++)
            {
                var root = group.Rows[rowIndex].Root;
                var label = $"그룹 {groupIndex + 1} · {rowIndex + 1}행 · {DescribeNode(root)}";
                roots.Add(BuildItem(root, label, groupIndex + 1, rowIndex + 1,
                    ImmutableArray<Guid>.Empty, itemsById, pathsById));
            }
        }

        _items = roots.ToImmutable();
        _itemsById = itemsById;
        _pathsById = pathsById;
        OnPropertyChanged(nameof(Items));
        SynchronizeSelectionFromSession();
    }

    private static StructurePanelItem BuildItem(MathNode node, string label, int groupNumber, int rowNumber,
        ImmutableArray<Guid> parentPath, Dictionary<Guid, StructurePanelItem> itemsById,
        Dictionary<Guid, ImmutableArray<Guid>> pathsById)
    {
        var children = ImmutableArray.CreateBuilder<StructurePanelItem>();
        foreach (var (child, slotName) in GetChildrenWithSlots(node))
        {
            var childLabel = $"{slotName} · {DescribeNode(child)}";
            children.Add(BuildItem(child, childLabel, groupNumber, rowNumber,
                parentPath.Add(node.Id), itemsById, pathsById));
        }

        var item = new StructurePanelItem(GetNodeKind(node), node.Id, node.Id, label,
            groupNumber, rowNumber, children.ToImmutable());
        itemsById.Add(node.Id, item);
        pathsById.Add(node.Id, parentPath.Add(node.Id));
        return item;
    }

    private static IEnumerable<(MathNode Node, string Slot)> GetChildrenWithSlots(MathNode node)
    {
        switch (node)
        {
            case SequenceNode sequence:
                for (var index = 0; index < sequence.Children.Length; index++)
                    yield return (sequence.Children[index], $"항목 {index + 1}");
                break;
            case FractionNode fraction:
                yield return (fraction.Numerator, "분자");
                yield return (fraction.Denominator, "분모");
                break;
            case RadicalNode radical:
                yield return (radical.Radicand, "근호 내부");
                break;
            case ScriptNode script:
                yield return (script.Base, "밑");
                if (script.Subscript is not null)
                    yield return (script.Subscript, "아래첨자");
                if (script.Superscript is not null)
                    yield return (script.Superscript, "위첨자");
                break;
            case MatrixNode matrix:
                for (var index = 0; index < matrix.Cells.Length; index++)
                    yield return (matrix.Cells[index], $"셀 {index / matrix.Columns + 1},{index % matrix.Columns + 1}");
                break;
            case IntegralNode integral:
                if (integral.LowerLimit is not null)
                    yield return (integral.LowerLimit, "하한");
                if (integral.UpperLimit is not null)
                    yield return (integral.UpperLimit, "상한");
                yield return (integral.Integrand, "피적분식");
                yield return (integral.DifferentialVariable, "미분 변수");
                break;
            case SummationNode summation:
                if (summation.LowerLimit is not null)
                    yield return (summation.LowerLimit, "하한");
                if (summation.UpperLimit is not null)
                    yield return (summation.UpperLimit, "상한");
                yield return (summation.Body, "합의 식");
                break;
            case LimitNode limit:
                yield return (limit.Approach, "접근식");
                yield return (limit.Body, "극한식");
                break;
            case BracketsNode brackets:
                yield return (brackets.Content, $"{BracketName(brackets.Kind)} 괄호 안");
                break;
            case CasesNode cases:
                for (var index = 0; index < cases.Rows.Length; index++)
                {
                    yield return (cases.Rows[index].Expression, $"경우 {index + 1} 식");
                    yield return (cases.Rows[index].Condition, $"경우 {index + 1} 조건");
                }
                break;
        }
    }

    private static MathNodeKind GetNodeKind(MathNode node) => node switch
    {
        TextNode => MathNodeKind.Text,
        MathSpaceNode => MathNodeKind.MathSpace,
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
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };

    private static string DescribeNode(MathNode node) => node switch
    {
        TextNode { IsEmptySlot: true } => "빈 텍스트 슬롯",
        TextNode { Text.Length: 0 } => "빈 텍스트",
        TextNode text => $"텍스트: {PreviewText(text.Text)}",
        MathSpaceNode => "얇은 수학 공백",
        SequenceNode => "수식 구문",
        FractionNode => "분수",
        RadicalNode => "근호",
        ScriptNode => "첨자",
        MatrixNode matrix => $"행렬 {matrix.Rows}×{matrix.Columns}",
        IntegralNode => "적분",
        SummationNode => "합",
        LimitNode => "극한",
        BracketsNode brackets => $"{BracketName(brackets.Kind)} 괄호",
        CasesNode cases => $"경우 나누기 {cases.Rows.Length}행",
        _ => "수식 구조"
    };

    private static string PreviewText(string text)
    {
        var runes = text.EnumerateRunes().Take(25).ToArray();
        var visible = string.Concat(runes.Take(24).Select(rune => rune.Value switch
        {
            '\n' => "↵",
            '\r' => "↵",
            '\t' => "⇥",
            ' ' => "␠",
            _ => rune.ToString()
        }));
        return runes.Length > 24 ? $"{visible}…" : visible;
    }

    private static string BracketName(MathBracketKind kind) => kind switch
    {
        MathBracketKind.Parentheses => "소괄호",
        MathBracketKind.Square => "대괄호",
        MathBracketKind.Curly => "중괄호",
        MathBracketKind.Angle => "꺾쇠",
        _ => "괄호"
    };

    private void SynchronizeSelectionFromSession()
    {
        var nodeId = _session.IsNodeSelected ? _session.SelectedNodeId : _session.Caret.NodeId;
        if (nodeId is Guid id && _itemsById.TryGetValue(id, out var item))
            SetSelectedItem(item);
        else
            SetSelectedItem(null);
    }

    private void SetSelectedItem(StructurePanelItem? value)
    {
        var selectedChanged = !ReferenceEquals(_selectedItem, value);
        var selectedId = value?.MathNodeId;
        var path = selectedId is Guid id && _pathsById.TryGetValue(id, out var pathIds)
            ? string.Join(" › ", pathIds.Select(pathId => _itemsById[pathId].Label))
            : string.Empty;
        var pathChanged = !string.Equals(_activePath, path, StringComparison.Ordinal);

        _selectedItem = value;
        _activePath = path;
        if (selectedChanged)
        {
            OnPropertyChanged(nameof(SelectedItem));
            OnPropertyChanged(nameof(SelectedNodeId));
        }
        if (pathChanged)
            OnPropertyChanged(nameof(ActivePath));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
