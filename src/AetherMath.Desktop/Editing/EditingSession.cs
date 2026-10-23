using System.Collections.Immutable;
using System.ComponentModel;
using AetherMath.Core;

namespace AetherMath.Editing;

public readonly record struct MathCaret(Guid NodeId, int TextElementOffset);
public sealed record MathSelection(MathCaret Anchor, MathCaret Focus);
public sealed record WorkspaceSnapshot(long Revision, Guid StateId, EquationWorkspace Workspace);

public enum MathTemplateKind
{
    Fraction,
    Radical,
    Superscript,
    Subscript,
    Integral,
    Summation,
    Limit,
    Matrix,
    Brackets,
    Cases
}

public enum MathClearScope
{
    Selection,
    CurrentRow,
    CurrentGroup,
    Workspace
}

public enum MathPropertyScope
{
    SelectionNode,
    CurrentRow,
    CurrentGroup,
    WorkspaceDefault
}

public enum MathPropertyChangeStatus
{
    Applied,
    NoChange,
    StaleTarget,
    InvalidValue,
    UnsupportedScope
}

public sealed record MathPropertyChangeResult(MathPropertyChangeStatus Status, string? Message = null);

public sealed record MathStylePatch(double? FontSize = null, string? FontFamily = null, string? Foreground = null);

public sealed record MathPropertiesPatch(MathStylePatch Style, MathTypesettingStyle? TypesettingStyle = null,
    MathTextRole? TextRole = null, double? GroupRowSpacing = null, MathBracketKind? BracketKind = null);

public sealed record MathPropertyTargetSnapshot(
    MathPropertyScope Scope,
    Guid SessionToken,
    long Revision,
    Guid WorkspaceId,
    Guid? GroupId,
    Guid? RowId,
    Guid? TargetNodeId,
    MathCaret Caret,
    MathSelection? Selection,
    Guid? SelectedNodeId,
    bool IsNodeSelected);

public enum MathStyleSourceKind
{
    WorkspaceDefault,
    Group,
    Row,
    Node
}

public sealed record MathStyleOrigin(MathStyleSourceKind Kind, Guid OwnerId, int? GroupNumber, int? RowNumber);

public sealed record ResolvedStyleValue<T>(T Value, MathStyleOrigin Origin);

public sealed record ResolvedMathStyle(
    ResolvedStyleValue<double> FontSize,
    ResolvedStyleValue<string> FontFamily,
    ResolvedStyleValue<string> Foreground,
    ResolvedStyleValue<MathTypesettingStyle> TypesettingStyle,
    MathStyleOverride? TargetOverride);

public sealed class EditingSession : INotifyPropertyChanged
{
    private readonly Stack<EditState> _undo = new();
    private readonly Stack<EditState> _redo = new();
    private readonly Guid _sessionToken = Guid.NewGuid();
    private EquationWorkspace _workspace;
    private MathCaret _caret;
    private MathSelection? _selection;
    private Guid? _selectedNodeId;
    private bool _isNodeSelected;
    private ActiveRowLocation? _activeRowLocation;
    private long _revision;
    private Guid _stateId = Guid.NewGuid();
    private Guid _savedStateId;

    public EditingSession(EquationWorkspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        _workspace = workspace;
        var firstText = FindFirstTextNode(workspace) ??
            throw new InvalidDataException("수식 작업에 편집 가능한 텍스트 슬롯이 없습니다.");
        _caret = new MathCaret(firstText.Id, 0);
        _selectedNodeId = firstText.Id;
        _activeRowLocation = FindActiveRowLocation();
        _savedStateId = _stateId;
    }

    public static EditingSession CreateDefault() => new(CreateDefaultWorkspace());

    public static EditingSession CreateBlank() => new(CreateBlankWorkspace());

    public static EditingSession CreateRecovered(EquationWorkspace workspace)
    {
        var session = new EditingSession(workspace);
        session._savedStateId = Guid.Empty;
        return session;
    }

    public EquationWorkspace Workspace => _workspace;
    public MathCaret Caret => _caret;
    public MathSelection? Selection => _selection;
    public Guid? SelectedNodeId => _selectedNodeId;
    public bool IsNodeSelected => _isNodeSelected;
    public long Revision => _revision;
    public bool IsDirty => _stateId != _savedStateId;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int GroupCount => _workspace.Groups.Length;
    public int ActiveGroupNumber => _activeRowLocation is ActiveRowLocation location ? location.GroupIndex + 1 : 0;
    public int ActiveRowNumber => _activeRowLocation is ActiveRowLocation location ? location.RowIndex + 1 : 0;
    public int ActiveRowCount => _activeRowLocation is ActiveRowLocation location ? location.Group.Rows.Length : 0;
    public bool CanAddGroupAfterCurrent => TryGetActiveRow(out _);
    public bool CanDuplicateCurrentGroup => TryGetActiveRow(out _);
    public bool CanDeleteCurrentGroup => GroupCount > 1 && TryGetActiveRow(out _);
    public bool CanMoveCurrentGroupUp => TryGetActiveRow(out var location) && location.GroupIndex > 0;
    public bool CanMoveCurrentGroupDown =>
        TryGetActiveRow(out var location) && location.GroupIndex < GroupCount - 1;
    public bool CanAddRowAfterCurrent => TryGetActiveRow(out _);
    public bool CanDuplicateCurrentRow => TryGetActiveRow(out _);
    public bool CanDeleteCurrentRow => TryGetActiveRow(out var location) && location.Group.Rows.Length > 1;
    public bool CanMoveCurrentRowUp => TryGetActiveRow(out var location) && location.RowIndex > 0;
    public bool CanMoveCurrentRowDown =>
        TryGetActiveRow(out var location) && location.RowIndex < location.Group.Rows.Length - 1;
    public bool CanInsertMathSpace => TryGetMathSpaceSource(out _, out _, out _);
    public string MathSpaceInsertDisabledReason => CanInsertMathSpace
        ? string.Empty
        : "텍스트 커서나 같은 텍스트의 부분 선택에서만 공백을 삽입할 수 있습니다.";

    public Guid SessionToken => _sessionToken;

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? ActiveRowChanged;

    public bool TryCaptureMathPropertyTarget(MathPropertyScope scope, out MathPropertyTargetSnapshot? snapshot)
    {
        snapshot = null;
        if (!Enum.IsDefined(typeof(MathPropertyScope), scope))
            return false;

        Guid? groupId = null;
        Guid? rowId = null;
        Guid? targetNodeId = null;
        if (scope != MathPropertyScope.WorkspaceDefault)
        {
            if (!TryGetActiveRow(out var active))
                return false;
            groupId = active.Group.Id;
            rowId = active.Row.Id;
            if (scope == MathPropertyScope.SelectionNode)
            {
                targetNodeId = _isNodeSelected ? _selectedNodeId : _selection?.Anchor.NodeId ?? _caret.NodeId;
                if (targetNodeId is not Guid id || !MathTree.TryFindNode(_workspace, id, out _, out _))
                    return false;
            }
            else if (scope == MathPropertyScope.CurrentGroup)
            {
                rowId = null;
            }
            else if (scope == MathPropertyScope.CurrentRow)
            {
                targetNodeId = null;
            }
        }

        snapshot = new MathPropertyTargetSnapshot(scope, _sessionToken, _revision, _workspace.Id,
            groupId, rowId, targetNodeId, _caret, _selection, _selectedNodeId, _isNodeSelected);
        return true;
    }

    public ResolvedMathStyle? ResolveMathStyle(MathPropertyTargetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsCurrentMathPropertyTarget(snapshot))
            return null;

        var size = _workspace.Style.FontSize;
        var family = _workspace.Style.FontFamily;
        var foreground = _workspace.Style.Foreground;
        var typesettingStyle = _workspace.Style.TypesettingStyle;
        var workspaceOrigin = new MathStyleOrigin(MathStyleSourceKind.WorkspaceDefault, _workspace.Id, null, null);
        var sizeOrigin = workspaceOrigin;
        var familyOrigin = workspaceOrigin;
        var foregroundOrigin = workspaceOrigin;
        var typesettingStyleOrigin = workspaceOrigin;
        var groupIndex = -1;
        var rowIndex = -1;
        EquationGroup? group = null;
        EquationRow? row = null;

        if (snapshot.GroupId is Guid groupId)
        {
            groupIndex = FindGroupIndex(groupId);
            if (groupIndex < 0)
                return null;
            group = _workspace.Groups[groupIndex];
            ApplyOverride(group.StyleOverride,
                new MathStyleOrigin(MathStyleSourceKind.Group, group.Id, groupIndex + 1, null),
                ref size, ref family, ref foreground, ref typesettingStyle,
                ref sizeOrigin, ref familyOrigin, ref foregroundOrigin, ref typesettingStyleOrigin);
        }

        if (snapshot.RowId is Guid rowId)
        {
            if (group is null)
                return null;
            rowIndex = FindRowIndex(group, rowId);
            if (rowIndex < 0)
                return null;
            row = group.Rows[rowIndex];
            ApplyOverride(row.StyleOverride,
                new MathStyleOrigin(MathStyleSourceKind.Row, row.Id, groupIndex + 1, rowIndex + 1),
                ref size, ref family, ref foreground, ref typesettingStyle,
                ref sizeOrigin, ref familyOrigin, ref foregroundOrigin, ref typesettingStyleOrigin);
        }

        MathStyleOverride? targetOverride = snapshot.Scope switch
        {
            MathPropertyScope.WorkspaceDefault => null,
            MathPropertyScope.CurrentGroup => group?.StyleOverride,
            MathPropertyScope.CurrentRow => row?.StyleOverride,
            MathPropertyScope.SelectionNode => null,
            _ => null
        };

        if (snapshot.Scope == MathPropertyScope.SelectionNode && snapshot.TargetNodeId is Guid targetNodeId)
        {
            if (row is null || !MathTree.TryFindNode(_workspace, targetNodeId, out var targetNode, out var ancestors))
                return null;

            foreach (var ancestorId in ancestors)
            {
                if (!MathTree.TryFindNode(_workspace, ancestorId, out var ancestor, out _) || ancestor is null)
                    return null;
                ApplyOverride(ancestor.StyleOverride,
                    new MathStyleOrigin(MathStyleSourceKind.Node, ancestor.Id, groupIndex + 1, rowIndex + 1),
                    ref size, ref family, ref foreground, ref typesettingStyle,
                    ref sizeOrigin, ref familyOrigin, ref foregroundOrigin, ref typesettingStyleOrigin);
            }

            targetOverride = targetNode!.StyleOverride;
            ApplyOverride(targetOverride,
                new MathStyleOrigin(MathStyleSourceKind.Node, targetNode.Id, groupIndex + 1, rowIndex + 1),
                ref size, ref family, ref foreground, ref typesettingStyle,
                ref sizeOrigin, ref familyOrigin, ref foregroundOrigin, ref typesettingStyleOrigin);
        }

        return new ResolvedMathStyle(
            new ResolvedStyleValue<double>(size, sizeOrigin),
            new ResolvedStyleValue<string>(family, familyOrigin),
            new ResolvedStyleValue<string>(foreground, foregroundOrigin),
            new ResolvedStyleValue<MathTypesettingStyle>(typesettingStyle, typesettingStyleOrigin),
            targetOverride);
    }

    public MathPropertyChangeResult ApplyMathStyle(MathPropertyTargetSnapshot snapshot, MathStylePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return ApplyMathProperties(snapshot, new MathPropertiesPatch(patch));
    }

    public MathPropertyChangeResult ApplyMathProperties(MathPropertyTargetSnapshot snapshot, MathPropertiesPatch patch)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(patch);
        if (!IsCurrentMathPropertyTarget(snapshot))
            return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상이 변경되었습니다.");
        if (patch.BracketKind is MathBracketKind bracketKindValue &&
            !Enum.IsDefined(typeof(MathBracketKind), bracketKindValue))
            return new MathPropertyChangeResult(MathPropertyChangeStatus.InvalidValue, "속성 값이 올바르지 않습니다.");
        if (patch.GroupRowSpacing is not null && snapshot.Scope != MathPropertyScope.CurrentGroup)
            return new MathPropertyChangeResult(MathPropertyChangeStatus.UnsupportedScope,
                "그룹 내부 행 간격은 현재 그룹에만 적용할 수 있습니다.");
        if (patch.Style is null || !IsValidMathStylePatch(patch.Style) ||
            (patch.TypesettingStyle is MathTypesettingStyle typesettingStyle &&
             !Enum.IsDefined(typeof(MathTypesettingStyle), typesettingStyle)) ||
            (patch.TextRole is MathTextRole textRole &&
             !Enum.IsDefined(typeof(MathTextRole), textRole)) ||
            (patch.GroupRowSpacing is double spacing &&
             (!double.IsFinite(spacing) || spacing is < 0 or > 512)))
            return new MathPropertyChangeResult(MathPropertyChangeStatus.InvalidValue, "속성 값이 올바르지 않습니다.");

        BracketsNode? bracketTarget = null;
        if (patch.BracketKind is not null)
        {
            if (snapshot.Scope != MathPropertyScope.SelectionNode || !snapshot.IsNodeSelected ||
                snapshot.SelectedNodeId is not Guid selectedNodeId || snapshot.TargetNodeId != selectedNodeId ||
                !MathTree.TryFindNode(_workspace, selectedNodeId, out var selectedNode, out _) ||
                selectedNode is not BracketsNode selectedBrackets)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.UnsupportedScope,
                    "괄호 종류는 전체 선택한 괄호에서만 변경할 수 있습니다.");
            bracketTarget = selectedBrackets;
        }

        TextNode? roleTarget = null;
        if (patch.TextRole is not null)
        {
            if (snapshot.Scope != MathPropertyScope.SelectionNode || snapshot.TargetNodeId is not Guid roleNodeId)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.UnsupportedScope,
                    "텍스트 역할은 선택한 텍스트 노드에만 적용할 수 있습니다.");
            roleTarget = MathTree.FindTextNode(_workspace, roleNodeId);
            if (roleTarget is null)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.UnsupportedScope,
                    "텍스트 역할은 선택한 텍스트 노드에만 적용할 수 있습니다.");
        }

        if (snapshot.Scope == MathPropertyScope.WorkspaceDefault)
        {
            var oldStyle = _workspace.Style;
            var nextStyle = new MathStyle(patch.Style.FontSize ?? oldStyle.FontSize,
                patch.Style.FontFamily ?? oldStyle.FontFamily, patch.Style.Foreground ?? oldStyle.Foreground)
            {
                TypesettingStyle = patch.TypesettingStyle ?? oldStyle.TypesettingStyle
            };
            if (nextStyle == oldStyle)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.NoChange);
            Commit(_workspace with { Style = nextStyle }, _caret, _selection, _selectedNodeId, _isNodeSelected);
            return new MathPropertyChangeResult(MathPropertyChangeStatus.Applied);
        }

        if (!TryGetOverride(snapshot, out var currentOverride))
            return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");

        MathStyleOverride? nextOverride = new MathStyleOverride(
            patch.Style.FontSize ?? currentOverride?.FontSize,
            patch.Style.FontFamily ?? currentOverride?.FontFamily,
            patch.Style.Foreground ?? currentOverride?.Foreground)
        {
            TypesettingStyle = patch.TypesettingStyle ?? currentOverride?.TypesettingStyle
        };
        if (nextOverride is { FontSize: null, FontFamily: null, Foreground: null, TypesettingStyle: null })
            nextOverride = null;
        var overrideChanged = !Equals(currentOverride, nextOverride);
        var roleChanged = roleTarget is not null && patch.TextRole is MathTextRole requestedRole &&
                          roleTarget.Role != requestedRole;
        var bracketKindChanged = bracketTarget is not null && patch.BracketKind is MathBracketKind requestedBracketKind &&
                                 bracketTarget.Kind != requestedBracketKind;
        if (!overrideChanged && !roleChanged && !bracketKindChanged && patch.GroupRowSpacing is null)
            return new MathPropertyChangeResult(MathPropertyChangeStatus.NoChange);

        var updatedWorkspace = _workspace;
        if (overrideChanged)
        {
            if (!TrySetOverride(snapshot, nextOverride, out var overrideWorkspace))
                return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");
            updatedWorkspace = overrideWorkspace!;
        }
        var rowSpacingChanged = false;
        if (patch.GroupRowSpacing is double requestedRowSpacing)
        {
            if (snapshot.Scope != MathPropertyScope.CurrentGroup || snapshot.GroupId is not Guid groupId ||
                !TrySetGroupRowSpacing(updatedWorkspace, groupId, requestedRowSpacing,
                    out var workspaceWithSpacing, out rowSpacingChanged))
                return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget,
                    "그룹 행 간격 적용 대상을 찾을 수 없습니다.");
            updatedWorkspace = workspaceWithSpacing!;
        }
        if (roleChanged && roleTarget is not null && patch.TextRole is MathTextRole requestedTextRole)
        {
            var currentRoleTarget = MathTree.FindTextNode(updatedWorkspace, roleTarget.Id);
            if (currentRoleTarget is null)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");
            updatedWorkspace = MathTree.ReplaceNode(updatedWorkspace, roleTarget.Id,
                currentRoleTarget with { Role = requestedTextRole }, out var roleReplaced);
            if (!roleReplaced)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");
        }

        if (bracketKindChanged && bracketTarget is not null && patch.BracketKind is MathBracketKind requestedKind)
        {
            if (!MathTree.TryFindNode(updatedWorkspace, bracketTarget.Id, out var currentNode, out _) ||
                currentNode is not BracketsNode currentBrackets)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");
            updatedWorkspace = MathTree.ReplaceNode(updatedWorkspace, bracketTarget.Id,
                currentBrackets with { Kind = requestedKind }, out var bracketReplaced);
            if (!bracketReplaced)
                return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");
        }

        if (!overrideChanged && !roleChanged && !rowSpacingChanged && !bracketKindChanged)
            return new MathPropertyChangeResult(MathPropertyChangeStatus.NoChange);

        Commit(updatedWorkspace, _caret, _selection, _selectedNodeId, _isNodeSelected);
        return new MathPropertyChangeResult(MathPropertyChangeStatus.Applied);
    }

    private static bool TrySetGroupRowSpacing(EquationWorkspace workspace, Guid groupId, double rowSpacing,
        out EquationWorkspace? updatedWorkspace, out bool changed)
    {
        updatedWorkspace = null;
        changed = false;
        var groupIndex = -1;
        for (var index = 0; index < workspace.Groups.Length; index++)
        {
            if (workspace.Groups[index].Id != groupId)
                continue;
            groupIndex = index;
            break;
        }
        if (groupIndex < 0)
            return false;

        var group = workspace.Groups[groupIndex];
        if (group.RowSpacing.Equals(rowSpacing))
        {
            updatedWorkspace = workspace;
            return true;
        }

        var groups = workspace.Groups.ToBuilder();
        groups[groupIndex] = group with { RowSpacing = rowSpacing };
        updatedWorkspace = workspace with { Groups = groups.ToImmutable() };
        changed = true;
        return true;
    }

    public MathPropertyChangeResult ResetMathStyleOverride(MathPropertyTargetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsCurrentMathPropertyTarget(snapshot))
            return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상이 변경되었습니다.");
        if (snapshot.Scope == MathPropertyScope.WorkspaceDefault)
            return new MathPropertyChangeResult(MathPropertyChangeStatus.UnsupportedScope,
                "작업 기본값은 상속 초기화 대상이 아닙니다.");
        if (!TryGetOverride(snapshot, out var currentOverride))
            return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");
        if (currentOverride is null)
            return new MathPropertyChangeResult(MathPropertyChangeStatus.NoChange);
        if (!TrySetOverride(snapshot, null, out var updatedWorkspace))
            return new MathPropertyChangeResult(MathPropertyChangeStatus.StaleTarget, "적용 대상을 찾을 수 없습니다.");

        Commit(updatedWorkspace!, _caret, _selection, _selectedNodeId, _isNodeSelected);
        return new MathPropertyChangeResult(MathPropertyChangeStatus.Applied);
    }

    public bool TrySetCaret(MathCaret caret)
    {
        var text = MathTree.FindTextNode(_workspace, caret.NodeId);
        if (text is null || caret.TextElementOffset < 0 || caret.TextElementOffset > TextElementOffsets.Count(text.Text))
            return false;

        if (_caret == caret && _selection is null && !_isNodeSelected)
            return true;

        _caret = caret;
        _selection = null;
        _selectedNodeId = caret.NodeId;
        _isNodeSelected = false;
        NotifySelectionChanged();
        return true;
    }

    public bool TrySetSelection(MathSelection selection)
    {
        if (selection.Anchor.NodeId != selection.Focus.NodeId)
            return false;
        if (selection.Anchor == selection.Focus)
            return TrySetCaret(selection.Focus);

        var text = MathTree.FindTextNode(_workspace, selection.Anchor.NodeId);
        if (text is null ||
            selection.Anchor.TextElementOffset < 0 || selection.Focus.TextElementOffset < 0 ||
            selection.Anchor.TextElementOffset > TextElementOffsets.Count(text.Text) ||
            selection.Focus.TextElementOffset > TextElementOffsets.Count(text.Text))
            return false;

        if (_selection == selection && !_isNodeSelected)
            return true;

        _selection = selection;
        _caret = selection.Focus;
        _selectedNodeId = selection.Focus.NodeId;
        _isNodeSelected = false;
        NotifySelectionChanged();
        return true;
    }

    public bool TrySelectNode(Guid nodeId)
    {
        if (!MathTree.TryFindNode(_workspace, nodeId, out _, out _))
            return false;

        if (_isNodeSelected && _selectedNodeId == nodeId)
            return true;

        _selectedNodeId = nodeId;
        _selection = null;
        _isNodeSelected = true;
        NotifySelectionChanged();
        return true;
    }

    public bool TrySelectParentStructure()
    {
        var selectedId = _selectedNodeId ?? _caret.NodeId;
        var parent = MathTree.FindNearestStructure(_workspace, selectedId, includeSelf: false);
        return parent is not null && TrySelectNode(parent.Id);
    }

    internal bool TryMoveFromSelectedMathSpace(bool forward)
    {
        if (!_isNodeSelected || _selectedNodeId is not Guid selectedId ||
            !MathTree.TryFindNode(_workspace, selectedId, out var selected, out _) ||
            selected is not MathSpaceNode)
            return false;

        var text = MathTree.FindAdjacentTextToMathSpace(_workspace, selectedId, forward);
        if (text is null)
            return false;
        var offset = forward ? 0 : TextElementOffsets.Count(text.Text);
        return TrySetCaret(new MathCaret(text.Id, offset));
    }

    public bool AddRowAfterCurrent()
    {
        if (!TryGetActiveRow(out var location))
            return false;

        var emptySlot = EmptySlot();
        var row = new EquationRow(Guid.NewGuid(), emptySlot);
        var rows = location.Group.Rows.ToBuilder();
        rows.Insert(location.RowIndex + 1, row);
        var workspace = ReplaceGroupRows(location.GroupIndex, rows.ToImmutable());
        Commit(workspace, new MathCaret(emptySlot.Id, 0), null, emptySlot.Id, isNodeSelected: false);
        return true;
    }

    public bool AddGroupAfterCurrent()
    {
        if (!TryGetActiveRow(out var location))
            return false;

        var emptySlot = EmptySlot();
        var row = new EquationRow(Guid.NewGuid(), emptySlot);
        var group = new EquationGroup(Guid.NewGuid(), ImmutableArray.Create(row),
            location.Group.Alignment, location.Group.RowSpacing);
        var groups = _workspace.Groups.ToBuilder();
        groups.Insert(location.GroupIndex + 1, group);
        var workspace = _workspace with { Groups = groups.ToImmutable() };
        Commit(workspace, new MathCaret(emptySlot.Id, 0), null, emptySlot.Id, isNodeSelected: false);
        return true;
    }

    public bool DuplicateCurrentGroup()
    {
        if (!TryGetActiveRow(out var location))
            return false;

        var idMap = new Dictionary<Guid, Guid>();
        var clonedRows = ImmutableArray.CreateBuilder<EquationRow>(location.Group.Rows.Length);
        foreach (var row in location.Group.Rows)
            clonedRows.Add(row with { Id = Guid.NewGuid(), Root = MathTree.CloneWithNewIds(row.Root, idMap) });
        var clonedRowArray = clonedRows.ToImmutable();

        var clonedGroup = location.Group with
        {
            Id = Guid.NewGuid(),
            Rows = clonedRowArray
        };
        var groups = _workspace.Groups.ToBuilder();
        groups.Insert(location.GroupIndex + 1, clonedGroup);
        var workspace = _workspace with { Groups = groups.ToImmutable() };

        if (!TryMapCaret(_caret, idMap, out var caret))
        {
            if (!_isNodeSelected || _selectedNodeId is not Guid selectedId ||
                !idMap.TryGetValue(selectedId, out var clonedSelectedId) ||
                !MathTree.TryFindNode(workspace, clonedSelectedId, out var clonedSelectedNode, out _))
                return false;

            MathTree.TryFindContainingRow(workspace, clonedSelectedId, out _, out _, out _,
                out var selectedRow);
            var fallbackText = FindFirstTextNode(clonedSelectedNode!) ??
                (selectedRow is null ? null : FindFirstTextNode(selectedRow.Root));
            if (fallbackText is null)
                return false;
            caret = new MathCaret(fallbackText.Id, 0);
        }

        MathSelection? selection = null;
        if (_selection is MathSelection currentSelection)
        {
            if (!TryMapCaret(currentSelection.Anchor, idMap, out var anchor) ||
                !TryMapCaret(currentSelection.Focus, idMap, out var focus))
                return false;
            selection = new MathSelection(anchor, focus);
        }

        Guid? selectedNodeId = null;
        if (_selectedNodeId is Guid currentSelectedNodeId)
        {
            if (!idMap.TryGetValue(currentSelectedNodeId, out var clonedSelectedNodeId))
                return false;
            selectedNodeId = clonedSelectedNodeId;
        }
        else if (_isNodeSelected)
        {
            return false;
        }
        else
        {
            selectedNodeId = caret.NodeId;
        }

        Commit(workspace, caret, selection, selectedNodeId, _isNodeSelected);
        return true;
    }

    public bool DeleteCurrentGroup()
    {
        if (!TryGetActiveRow(out var location) || GroupCount <= 1)
            return false;

        var neighborIndex = location.GroupIndex < GroupCount - 1
            ? location.GroupIndex + 1
            : location.GroupIndex - 1;
        var neighbor = _workspace.Groups[neighborIndex];
        var firstRow = neighbor.Rows[0];
        var firstText = FindFirstTextNode(firstRow.Root);
        if (firstText is null)
            return false;

        var groups = _workspace.Groups.ToBuilder();
        groups.RemoveAt(location.GroupIndex);
        var workspace = _workspace with { Groups = groups.ToImmutable() };
        Commit(workspace, new MathCaret(firstText.Id, 0), null, firstText.Id, isNodeSelected: false);
        return true;
    }

    public bool MoveCurrentGroupUp() => MoveCurrentGroup(-1);

    public bool MoveCurrentGroupDown() => MoveCurrentGroup(1);

    public bool DuplicateCurrentRow()
    {
        if (!TryGetActiveRow(out var location))
            return false;

        var idMap = new Dictionary<Guid, Guid>();
        var clonedRoot = MathTree.CloneWithNewIds(location.Row.Root, idMap);
        var clonedRow = location.Row with { Id = Guid.NewGuid(), Root = clonedRoot };
        var rows = location.Group.Rows.ToBuilder();
        rows.Insert(location.RowIndex + 1, clonedRow);
        var workspace = ReplaceGroupRows(location.GroupIndex, rows.ToImmutable());

        if (!TryMapCaret(_caret, idMap, out var caret))
        {
            if (!_isNodeSelected || _selectedNodeId is not Guid selectedId ||
                !idMap.TryGetValue(selectedId, out var clonedSelectedId) ||
                !MathTree.TryFindNode(workspace, clonedSelectedId, out var clonedSelectedNode, out _))
                return false;

            var fallbackText = FindFirstTextNode(clonedSelectedNode!) ?? FindFirstTextNode(clonedRoot);
            if (fallbackText is null)
                return false;
            caret = new MathCaret(fallbackText.Id, 0);
        }

        MathSelection? selection = null;
        if (_selection is MathSelection currentSelection)
        {
            if (!TryMapCaret(currentSelection.Anchor, idMap, out var anchor) ||
                !TryMapCaret(currentSelection.Focus, idMap, out var focus))
                return false;
            selection = new MathSelection(anchor, focus);
        }

        Guid? selectedNodeId = null;
        if (_selectedNodeId is Guid currentSelectedNodeId)
        {
            if (!idMap.TryGetValue(currentSelectedNodeId, out var clonedSelectedNodeId))
                return false;
            selectedNodeId = clonedSelectedNodeId;
        }
        else if (_isNodeSelected)
        {
            return false;
        }
        else
        {
            selectedNodeId = caret.NodeId;
        }

        Commit(workspace, caret, selection, selectedNodeId, _isNodeSelected);
        return true;
    }

    public bool DeleteCurrentRow()
    {
        if (!TryGetActiveRow(out var location) || location.Group.Rows.Length <= 1)
            return false;

        var neighborIndex = location.RowIndex < location.Group.Rows.Length - 1
            ? location.RowIndex + 1
            : location.RowIndex - 1;
        var neighbor = location.Group.Rows[neighborIndex];
        var firstText = FindFirstTextNode(neighbor.Root);
        if (firstText is null)
            return false;

        var rows = location.Group.Rows.ToBuilder();
        rows.RemoveAt(location.RowIndex);
        var workspace = ReplaceGroupRows(location.GroupIndex, rows.ToImmutable());
        Commit(workspace, new MathCaret(firstText.Id, 0), null, firstText.Id, isNodeSelected: false);
        return true;
    }

    public bool MoveCurrentRowUp() => MoveCurrentRow(-1);

    public bool MoveCurrentRowDown() => MoveCurrentRow(1);

    public bool InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return false;

        if (_isNodeSelected && _selectedNodeId is Guid selectedId)
        {
            if (!MathTree.TryFindNode(_workspace, selectedId, out _, out _))
                return false;
            var selectedOverride = MathTree.TryFindNode(_workspace, selectedId, out var selectedNode, out _)
                ? selectedNode!.StyleOverride
                : null;
            var selectedTextRole = selectedNode is TextNode selectedText
                ? selectedText.Role
                : MathTextRole.Automatic;
            var replacement = new TextNode(Guid.NewGuid(), text)
            {
                StyleOverride = selectedOverride,
                Role = selectedTextRole
            };
            var updated = MathTree.ReplaceNode(_workspace, selectedId, replacement, out var changed);
            if (!changed)
                return false;
            var caret = new MathCaret(replacement.Id, TextElementOffsets.Count(text));
            Commit(updated, caret, null, replacement.Id, isNodeSelected: false);
            return true;
        }

        var nodeId = _selection?.Anchor.NodeId ?? _caret.NodeId;
        var oldText = MathTree.FindTextNode(_workspace, nodeId);
        if (oldText is null)
            return false;

        int startIndex;
        int endIndex;
        if (_selection is MathSelection selection)
        {
            var startOffset = Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            var endOffset = Math.Max(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            if (startOffset == endOffset)
                startIndex = endIndex = TextElementOffsets.ToUtf16Index(oldText.Text, startOffset);
            else
            {
                startIndex = TextElementOffsets.ToUtf16Index(oldText.Text, startOffset);
                endIndex = TextElementOffsets.ToUtf16Index(oldText.Text, endOffset);
            }
        }
        else
        {
            startIndex = endIndex = TextElementOffsets.ToUtf16Index(oldText.Text, _caret.TextElementOffset);
        }

        var updatedText = ReplaceRange(oldText.Text, startIndex, endIndex, text);
        var insertionEnd = startIndex + text.Length;
        var newTextNode = oldText with { Text = updatedText, IsEmptySlot = updatedText.Length == 0 };
        var workspace = MathTree.ReplaceNode(_workspace, oldText.Id, newTextNode, out var replaced);
        if (!replaced)
            return false;

        var caretOffset = TextElementOffsets.FromUtf16InsertionPoint(updatedText, insertionEnd);
        Commit(workspace, new MathCaret(oldText.Id, caretOffset), null, oldText.Id, isNodeSelected: false);
        return true;
    }

    public Guid? InsertMathSpace(MathSpaceKind kind)
    {
        if (!Enum.IsDefined(typeof(MathSpaceKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!TryGetMathSpaceSource(out var source, out var startIndex, out var endIndex) || source is null)
            return null;

        var prefix = source.Text[..startIndex];
        var suffix = source.Text[endIndex..];
        var space = new MathSpaceNode(Guid.NewGuid(), kind) { StyleOverride = source.StyleOverride };
        var pieces = ImmutableArray.CreateBuilder<MathNode>(3);
        TextNode caretAnchor;

        if (source.IsEmptySlot || (source.Text.Length > 0 && prefix.Length == 0 && suffix.Length == 0))
        {
            caretAnchor = source with { Text = string.Empty, IsEmptySlot = true };
            pieces.Add(space);
            pieces.Add(caretAnchor);
        }
        else if (source.Text.Length == 0)
        {
            // An unmarked empty text node is an internal caret anchor created after a
            // trailing spacer. Repeated insertion must not turn it into a required-slot marker.
            caretAnchor = source with { IsEmptySlot = false };
            pieces.Add(space);
            pieces.Add(caretAnchor);
        }
        else
        {
            if (prefix.Length > 0)
                pieces.Add(source with { Text = prefix, IsEmptySlot = false });

            pieces.Add(space);
            if (suffix.Length > 0)
            {
                caretAnchor = prefix.Length == 0
                    ? source with { Text = suffix, IsEmptySlot = false }
                    : new TextNode(Guid.NewGuid(), suffix)
                    {
                        Role = source.Role,
                        StyleOverride = source.StyleOverride
                    };
                pieces.Add(caretAnchor);
            }
            else
            {
                caretAnchor = new TextNode(Guid.NewGuid(), string.Empty)
                {
                    Role = source.Role,
                    StyleOverride = source.StyleOverride
                };
                pieces.Add(caretAnchor);
            }
        }

        var workspace = MathTree.ReplaceNodeWithSequenceChildren(_workspace, source.Id,
            pieces.ToImmutable(), out var changed);
        if (!changed)
            return null;
        if (caretAnchor.IsEmptySlot)
            workspace = NormalizeEmptySequenceTextAnchors(workspace, caretAnchor);
        Commit(workspace, new MathCaret(caretAnchor.Id, 0), null, caretAnchor.Id, isNodeSelected: false);
        return space.Id;
    }

    public bool DeleteSelection()
    {
        if (_isNodeSelected && _selectedNodeId is Guid selectedId &&
            MathTree.TryFindNode(_workspace, selectedId, out var selectedNode, out _))
        {
            if (selectedNode is MathSpaceNode)
            {
                var precedingText = MathTree.FindAdjacentTextToMathSpace(_workspace, selectedId, forward: false);
                var adjacent = precedingText ??
                    MathTree.FindAdjacentTextToMathSpace(_workspace, selectedId, forward: true);
                var workspaceWithoutSpace = MathTree.RemoveMathSpace(_workspace, selectedId,
                    out var removedSpace, out var emptySequenceAnchor);
                if (!removedSpace)
                    return false;
                var caret = adjacent is not null
                    ? new MathCaret(adjacent.Id, precedingText is not null
                        ? TextElementOffsets.Count(adjacent.Text)
                        : 0)
                    : emptySequenceAnchor is not null
                        ? new MathCaret(emptySequenceAnchor.Id, 0)
                        : _caret;
                var caretNode = MathTree.FindTextNode(workspaceWithoutSpace, caret.NodeId);
                if (caretNode is null)
                    return false;
                Commit(workspaceWithoutSpace, caret, null, caret.NodeId, isNodeSelected: false);
                return true;
            }

            var emptySlot = new TextNode(Guid.NewGuid(), string.Empty, IsEmptySlot: true)
            {
                Role = selectedNode is TextNode selectedText ? selectedText.Role : MathTextRole.Automatic
            };
            var workspace = MathTree.ReplaceNode(_workspace, selectedId, emptySlot, out var changed);
            if (!changed)
                return false;
            workspace = NormalizeEmptySequenceTextAnchors(workspace, emptySlot);
            Commit(workspace, new MathCaret(emptySlot.Id, 0), null, emptySlot.Id, isNodeSelected: false);
            return true;
        }

        if (_selection is not MathSelection selection)
            return false;

        var node = MathTree.FindTextNode(_workspace, selection.Anchor.NodeId);
        if (node is null)
            return false;
        var startOffset = Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
        var endOffset = Math.Max(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
        if (startOffset == endOffset)
            return false;

        var startIndex = TextElementOffsets.ToUtf16Index(node.Text, startOffset);
        var endIndex = TextElementOffsets.ToUtf16Index(node.Text, endOffset);
        var value = ReplaceRange(node.Text, startIndex, endIndex, string.Empty);
        var replacement = node with { Text = value, IsEmptySlot = value.Length == 0 };
        var workspaceAfter = MathTree.ReplaceNode(_workspace, node.Id, replacement, out var textChanged);
        if (!textChanged)
            return false;
        if (value.Length == 0)
            workspaceAfter = NormalizeEmptySequenceTextAnchors(workspaceAfter, replacement);

        var caretOffset = TextElementOffsets.FromUtf16InsertionPoint(value, startIndex);
        Commit(workspaceAfter, new MathCaret(node.Id, caretOffset), null, node.Id, isNodeSelected: false);
        return true;
    }

    public bool CanClear(MathClearScope scope)
    {
        if (!Enum.IsDefined(typeof(MathClearScope), scope))
            return false;

        if (scope == MathClearScope.Selection)
        {
            if (_isNodeSelected && _selectedNodeId is Guid selectedId &&
                MathTree.TryFindNode(_workspace, selectedId, out var selectedNode, out _))
                return selectedNode is not null && MathTree.HasClearableContent(selectedNode);

            if (_selection is not MathSelection selection || selection.Anchor.NodeId != selection.Focus.NodeId)
                return false;

            var startOffset = Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            var endOffset = Math.Max(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            return startOffset < endOffset && MathTree.FindTextNode(_workspace, selection.Anchor.NodeId) is not null;
        }

        return TryGetClearRows(scope, out var rows) && rows.Any(row => MathTree.HasClearableContent(row.Root));
    }

    public bool Clear(MathClearScope scope)
    {
        if (!CanClear(scope))
            return false;

        if (scope == MathClearScope.Selection)
            return DeleteSelection();

        if (!TryGetActiveRow(out var activeRow) || !TryGetClearRows(scope, out var rows))
            return false;

        var targetRowIds = rows.Select(row => row.Id).ToHashSet();
        var groups = _workspace.Groups.ToBuilder();
        TextNode? activeEmptyRoot = null;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var updatedRows = group.Rows.ToBuilder();
            var groupChanged = false;
            for (var rowIndex = 0; rowIndex < updatedRows.Count; rowIndex++)
            {
                var row = updatedRows[rowIndex];
                if (!targetRowIds.Contains(row.Id))
                    continue;

                var emptyRoot = EmptySlot();
                updatedRows[rowIndex] = row with { Root = emptyRoot };
                if (row.Id == activeRow.Row.Id)
                    activeEmptyRoot = emptyRoot;
                groupChanged = true;
            }

            if (groupChanged)
                groups[groupIndex] = group with { Rows = updatedRows.ToImmutable() };
        }

        if (activeEmptyRoot is null)
            return false;

        var workspace = _workspace with { Groups = groups.ToImmutable() };
        Commit(workspace, new MathCaret(activeEmptyRoot.Id, 0), null, activeEmptyRoot.Id, isNodeSelected: false);
        return true;
    }

    private bool TryGetClearRows(MathClearScope scope, out ImmutableArray<EquationRow> rows)
    {
        rows = ImmutableArray<EquationRow>.Empty;
        if (!TryGetActiveRow(out var location))
            return false;

        switch (scope)
        {
            case MathClearScope.CurrentRow:
                rows = ImmutableArray.Create(location.Row);
                return true;
            case MathClearScope.CurrentGroup:
                rows = location.Group.Rows;
                return true;
            case MathClearScope.Workspace:
                rows = _workspace.Groups.SelectMany(group => group.Rows).ToImmutableArray();
                return true;
            default:
                return false;
        }
    }

    public bool Backspace()
    {
        if (_isNodeSelected || _selection is not null)
            return DeleteSelection();

        var node = MathTree.FindTextNode(_workspace, _caret.NodeId);
        if (node is null)
            return false;
        if (_caret.TextElementOffset == 0)
            return TryRemoveAdjacentMathSpace(node, preceding: true);

        var startOffset = _caret.TextElementOffset - 1;
        var start = TextElementOffsets.ToUtf16Index(node.Text, startOffset);
        var end = TextElementOffsets.ToUtf16Index(node.Text, _caret.TextElementOffset);
        return DeleteTextRange(node, start, end, startOffset);
    }

    public bool DeleteForward()
    {
        if (_isNodeSelected || _selection is not null)
            return DeleteSelection();

        var node = MathTree.FindTextNode(_workspace, _caret.NodeId);
        if (node is null)
            return false;
        if (_caret.TextElementOffset >= TextElementOffsets.Count(node.Text))
            return TryRemoveAdjacentMathSpace(node, preceding: false);

        var start = TextElementOffsets.ToUtf16Index(node.Text, _caret.TextElementOffset);
        var end = TextElementOffsets.ToUtf16Index(node.Text, _caret.TextElementOffset + 1);
        return DeleteTextRange(node, start, end, _caret.TextElementOffset);
    }

    public Guid? InsertFraction() => InsertTemplate(MathTemplateKind.Fraction);
    public Guid? InsertRoot() => InsertTemplate(MathTemplateKind.Radical);
    public Guid? InsertMatrix() => InsertTemplate(MathTemplateKind.Matrix);
    public Guid? InsertSuperscript() => InsertTemplate(MathTemplateKind.Superscript);

    public Guid? InsertTemplate(MathTemplateKind kind)
    {
        if (!Enum.IsDefined(typeof(MathTemplateKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        return InsertTemplateCore(kind);
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Push(CurrentState());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Push(CurrentState());
        Restore(_redo.Pop());
    }

    public WorkspaceSnapshot CaptureSnapshot() => new(_revision, _stateId, _workspace);

    public void MarkSaved(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Workspace.Id != _workspace.Id || snapshot.StateId == Guid.Empty)
            throw new InvalidOperationException("저장 스냅샷이 현재 작업 세션에 속하지 않습니다.");
        _savedStateId = snapshot.StateId;
        OnPropertyChanged(nameof(IsDirty));
    }

    private bool DeleteTextRange(TextNode node, int start, int end, int caretOffset)
    {
        var value = ReplaceRange(node.Text, start, end, string.Empty);
        var replacement = node with { Text = value, IsEmptySlot = value.Length == 0 };
        var workspace = MathTree.ReplaceNode(_workspace, node.Id, replacement, out var changed);
        if (!changed)
            return false;
        if (value.Length == 0)
            workspace = NormalizeEmptySequenceTextAnchors(workspace, replacement);
        Commit(workspace, new MathCaret(node.Id, caretOffset), null, node.Id, isNodeSelected: false);
        return true;
    }

    private static EquationWorkspace NormalizeEmptySequenceTextAnchors(EquationWorkspace workspace,
        TextNode emptyMarker)
    {
        if (!emptyMarker.IsEmptySlot || emptyMarker.Text.Length != 0)
            return workspace;

        var currentWorkspace = workspace;
        var currentNodeId = emptyMarker.Id;
        while (MathTree.TryGetContainingSequence(currentWorkspace, currentNodeId,
                   out var sequence, out var childIndex) && sequence is not null)
        {
            if (childIndex < 0 || childIndex >= sequence.Children.Length ||
                sequence.Children[childIndex].Id != currentNodeId ||
                !MathTree.IsSemanticallyEmpty(sequence.Children[childIndex]) ||
                !MathTree.IsSemanticallyEmpty(sequence))
                break;

            var normalizedSequence = NormalizeEmptyTextMarkers(sequence, out var changed);
            if (changed)
            {
                currentWorkspace = MathTree.ReplaceNode(currentWorkspace, sequence.Id,
                    normalizedSequence, out var replaced);
                if (!replaced)
                    break;
            }

            currentNodeId = sequence.Id;
        }
        return currentWorkspace;
    }

    private static SequenceNode NormalizeEmptyTextMarkers(SequenceNode sequence, out bool changed)
    {
        if (sequence.Children.IsEmpty)
        {
            changed = true;
            return sequence with
            {
                Children = ImmutableArray.Create<MathNode>(
                    new TextNode(Guid.NewGuid(), string.Empty, IsEmptySlot: true))
            };
        }

        var children = sequence.Children.ToBuilder();
        changed = false;
        for (var index = 0; index < children.Count; index++)
        {
            switch (children[index])
            {
                case TextNode { Text.Length: 0, IsEmptySlot: false } anchor:
                    // Preserve every ID, role, style, and position; only mark the empty anchor.
                    children[index] = anchor with { IsEmptySlot = true };
                    changed = true;
                    break;
                case SequenceNode nested when MathTree.IsSemanticallyEmpty(nested):
                {
                    var normalizedNested = NormalizeEmptyTextMarkers(nested, out var nestedChanged);
                    if (nestedChanged)
                    {
                        children[index] = normalizedNested;
                        changed = true;
                    }
                    break;
                }
            }
        }
        return changed ? sequence with { Children = children.ToImmutable() } : sequence;
    }

    private bool TryRemoveAdjacentMathSpace(TextNode text, bool preceding)
    {
        if (!MathTree.TryGetContainingSequence(_workspace, text.Id, out var sequence, out var textIndex) ||
            sequence is null)
            return false;
        var spaceIndex = textIndex + (preceding ? -1 : 1);
        if (spaceIndex < 0 || spaceIndex >= sequence.Children.Length ||
            sequence.Children[spaceIndex] is not MathSpaceNode space)
            return false;

        var workspace = MathTree.RemoveMathSpace(_workspace, space.Id, out var changed, out _);
        if (!changed)
            return false;
        Commit(workspace, _caret, null, text.Id, isNodeSelected: false);
        return true;
    }

    private bool TryGetMathSpaceSource(out TextNode? source, out int startIndex, out int endIndex)
    {
        source = null;
        startIndex = 0;
        endIndex = 0;
        if (_isNodeSelected)
            return false;

        var nodeId = _selection?.Anchor.NodeId ?? _caret.NodeId;
        source = MathTree.FindTextNode(_workspace, nodeId);
        if (source is null)
            return false;

        if (_selection is MathSelection currentSelection &&
            (currentSelection.Anchor.NodeId != currentSelection.Focus.NodeId ||
             currentSelection.Focus.NodeId != source.Id))
        {
            source = null;
            return false;
        }
        if (_selection is null && _caret.NodeId != source.Id)
        {
            source = null;
            return false;
        }

        var startOffset = _selection is MathSelection selection
            ? Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset)
            : _caret.TextElementOffset;
        var endOffset = _selection is MathSelection endSelection
            ? Math.Max(endSelection.Anchor.TextElementOffset, endSelection.Focus.TextElementOffset)
            : _caret.TextElementOffset;
        if (startOffset < 0 || endOffset < startOffset || endOffset > TextElementOffsets.Count(source.Text))
        {
            source = null;
            return false;
        }
        startIndex = TextElementOffsets.ToUtf16Index(source.Text, startOffset);
        endIndex = TextElementOffsets.ToUtf16Index(source.Text, endOffset);
        return true;
    }

    private bool MoveCurrentRow(int direction)
    {
        if (!TryGetActiveRow(out var location))
            return false;
        var targetIndex = location.RowIndex + direction;
        if (targetIndex < 0 || targetIndex >= location.Group.Rows.Length)
            return false;

        var rows = location.Group.Rows.ToBuilder();
        var current = rows[location.RowIndex];
        rows[location.RowIndex] = rows[targetIndex];
        rows[targetIndex] = current;
        var workspace = ReplaceGroupRows(location.GroupIndex, rows.ToImmutable());
        Commit(workspace, _caret, _selection, _selectedNodeId, _isNodeSelected);
        return true;
    }

    private bool MoveCurrentGroup(int direction)
    {
        if (!TryGetActiveRow(out var location))
            return false;
        var targetIndex = location.GroupIndex + direction;
        if (targetIndex < 0 || targetIndex >= GroupCount)
            return false;

        var groups = _workspace.Groups.ToBuilder();
        var current = groups[location.GroupIndex];
        groups[location.GroupIndex] = groups[targetIndex];
        groups[targetIndex] = current;
        var workspace = _workspace with { Groups = groups.ToImmutable() };
        Commit(workspace, _caret, _selection, _selectedNodeId, _isNodeSelected);
        return true;
    }

    private bool TryGetActiveRow(out ActiveRowLocation location)
    {
        if (_activeRowLocation is ActiveRowLocation activeLocation)
        {
            location = activeLocation;
            return true;
        }

        location = new ActiveRowLocation(-1, -1, null!, null!);
        return false;
    }

    private ActiveRowLocation? FindActiveRowLocation()
    {
        var nodeId = _isNodeSelected ? _selectedNodeId : _caret.NodeId;
        if (nodeId is Guid activeNodeId &&
            MathTree.TryFindContainingRow(_workspace, activeNodeId,
                out var groupIndex, out var rowIndex, out var group, out var row) &&
            group is not null && row is not null)
            return new ActiveRowLocation(groupIndex, rowIndex, group, row);

        return null;
    }

    private EquationWorkspace ReplaceGroupRows(int groupIndex, ImmutableArray<EquationRow> rows)
    {
        var groups = _workspace.Groups.ToBuilder();
        groups[groupIndex] = groups[groupIndex] with { Rows = rows };
        return _workspace with { Groups = groups.ToImmutable() };
    }

    private static bool TryMapCaret(MathCaret source, IReadOnlyDictionary<Guid, Guid> idMap, out MathCaret mapped)
    {
        if (idMap.TryGetValue(source.NodeId, out var nodeId))
        {
            mapped = new MathCaret(nodeId, source.TextElementOffset);
            return true;
        }

        mapped = default;
        return false;
    }

    private static string ReplaceRange(string source, int start, int end, string replacement)
    {
        var state = (Source: source, Start: start, End: end, Replacement: replacement);
        var length = source.Length - (end - start) + replacement.Length;
        return string.Create(length, state, static (destination, value) =>
        {
            value.Source.AsSpan(0, value.Start).CopyTo(destination);
            value.Replacement.AsSpan().CopyTo(destination[value.Start..]);
            value.Source.AsSpan(value.End).CopyTo(destination[(value.Start + value.Replacement.Length)..]);
        });
    }

    private bool IsCurrentMathPropertyTarget(MathPropertyTargetSnapshot snapshot) =>
        snapshot.SessionToken == _sessionToken &&
        TryCaptureMathPropertyTarget(snapshot.Scope, out var current) &&
        Equals(snapshot, current);

    private bool TryGetOverride(MathPropertyTargetSnapshot snapshot, out MathStyleOverride? styleOverride)
    {
        styleOverride = null;
        if (snapshot.Scope == MathPropertyScope.WorkspaceDefault)
            return false;
        if (snapshot.GroupId is not Guid groupId)
            return false;
        var groupIndex = FindGroupIndex(groupId);
        if (groupIndex < 0)
            return false;

        var group = _workspace.Groups[groupIndex];
        switch (snapshot.Scope)
        {
            case MathPropertyScope.CurrentGroup:
                styleOverride = group.StyleOverride;
                return true;
            case MathPropertyScope.CurrentRow:
                if (snapshot.RowId is not Guid rowId)
                    return false;
                var rowIndex = FindRowIndex(group, rowId);
                if (rowIndex < 0)
                    return false;
                styleOverride = group.Rows[rowIndex].StyleOverride;
                return true;
            case MathPropertyScope.SelectionNode:
                if (snapshot.TargetNodeId is not Guid nodeId ||
                    !MathTree.TryFindNode(_workspace, nodeId, out var node, out _))
                    return false;
                styleOverride = node!.StyleOverride;
                return true;
            default:
                return false;
        }
    }

    private bool TrySetOverride(MathPropertyTargetSnapshot snapshot, MathStyleOverride? styleOverride,
        out EquationWorkspace? updatedWorkspace)
    {
        updatedWorkspace = null;
        if (snapshot.Scope == MathPropertyScope.WorkspaceDefault || snapshot.GroupId is not Guid groupId)
            return false;
        var groupIndex = FindGroupIndex(groupId);
        if (groupIndex < 0)
            return false;

        var groups = _workspace.Groups.ToBuilder();
        var group = groups[groupIndex];
        switch (snapshot.Scope)
        {
            case MathPropertyScope.CurrentGroup:
                groups[groupIndex] = group with { StyleOverride = styleOverride };
                break;
            case MathPropertyScope.CurrentRow:
                if (snapshot.RowId is not Guid rowId)
                    return false;
                var rowIndex = FindRowIndex(group, rowId);
                if (rowIndex < 0)
                    return false;
                var rows = group.Rows.ToBuilder();
                rows[rowIndex] = rows[rowIndex] with { StyleOverride = styleOverride };
                groups[groupIndex] = group with { Rows = rows.ToImmutable() };
                break;
            case MathPropertyScope.SelectionNode:
                if (snapshot.TargetNodeId is not Guid nodeId ||
                    !MathTree.TryFindNode(_workspace, nodeId, out var node, out _))
                    return false;
                var replacement = MathTree.ReplaceNode(_workspace, nodeId,
                    node! with { StyleOverride = styleOverride }, out var changed);
                if (!changed)
                    return false;
                updatedWorkspace = replacement;
                return true;
            default:
                return false;
        }

        updatedWorkspace = _workspace with { Groups = groups.ToImmutable() };
        return true;
    }

    private int FindGroupIndex(Guid groupId)
    {
        for (var index = 0; index < _workspace.Groups.Length; index++)
            if (_workspace.Groups[index].Id == groupId)
                return index;
        return -1;
    }

    private static int FindRowIndex(EquationGroup group, Guid rowId)
    {
        for (var index = 0; index < group.Rows.Length; index++)
            if (group.Rows[index].Id == rowId)
                return index;
        return -1;
    }

    private static bool IsValidMathStylePatch(MathStylePatch patch) =>
        (patch.FontSize is null || double.IsFinite(patch.FontSize.Value) && patch.FontSize.Value is >= 1 and <= 512) &&
        (patch.FontFamily is null || !string.IsNullOrWhiteSpace(patch.FontFamily)) &&
        (patch.Foreground is null || WorkspaceValidator.IsHexColor(patch.Foreground));

    private static void ApplyOverride(MathStyleOverride? styleOverride, MathStyleOrigin origin,
        ref double fontSize, ref string fontFamily, ref string foreground,
        ref MathTypesettingStyle typesettingStyle,
        ref MathStyleOrigin fontSizeOrigin, ref MathStyleOrigin fontFamilyOrigin,
        ref MathStyleOrigin foregroundOrigin, ref MathStyleOrigin typesettingStyleOrigin)
    {
        if (styleOverride is null)
            return;
        if (styleOverride.FontSize is double size)
        {
            fontSize = size;
            fontSizeOrigin = origin;
        }
        if (styleOverride.FontFamily is string family)
        {
            fontFamily = family;
            fontFamilyOrigin = origin;
        }
        if (styleOverride.Foreground is string color)
        {
            foreground = color;
            foregroundOrigin = origin;
        }
        if (styleOverride.TypesettingStyle is MathTypesettingStyle preset)
        {
            typesettingStyle = preset;
            typesettingStyleOrigin = origin;
        }
    }

    private Guid? InsertTemplateCore(MathTemplateKind kind)
    {
        MathNode? content = null;
        MathNode? replacement = null;
        MathCaret focus;
        Guid targetId;

        if (_isNodeSelected && _selectedNodeId is Guid selectedId &&
            MathTree.TryFindNode(_workspace, selectedId, out var selected, out _))
        {
            content = selected;
            targetId = selectedId;
            replacement = CreateTemplate(kind, content, out focus);
        }
        else if (_selection is MathSelection selection && selection.Anchor.NodeId == selection.Focus.NodeId)
        {
            var source = MathTree.FindTextNode(_workspace, selection.Anchor.NodeId);
            if (source is null)
                return null;
            var startOffset = Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            var endOffset = Math.Max(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            if (startOffset == endOffset)
                return InsertTemplateAtCaret(kind);

            var startIndex = TextElementOffsets.ToUtf16Index(source.Text, startOffset);
            var endIndex = TextElementOffsets.ToUtf16Index(source.Text, endOffset);
            var selectedText = source.Text[startIndex..endIndex];
            content = new TextNode(source.Id, selectedText)
            {
                StyleOverride = source.StyleOverride,
                Role = source.Role
            };
            var wrapper = CreateTemplate(kind, content, out focus);
            var prefix = source.Text[..startIndex];
            var suffix = source.Text[endIndex..];
            var pieces = ImmutableArray.CreateBuilder<MathNode>(3);
            if (prefix.Length > 0)
                pieces.Add(new TextNode(Guid.NewGuid(), prefix)
                {
                    StyleOverride = source.StyleOverride,
                    Role = source.Role
                });
            pieces.Add(wrapper);
            if (suffix.Length > 0)
                pieces.Add(new TextNode(Guid.NewGuid(), suffix)
                {
                    StyleOverride = source.StyleOverride,
                    Role = source.Role
                });
            replacement = pieces.Count == 1 ? wrapper : new SequenceNode(Guid.NewGuid(), pieces.ToImmutable());
            targetId = source.Id;
        }
        else
        {
            return InsertTemplateAtCaret(kind);
        }

        var workspace = MathTree.ReplaceNode(_workspace, targetId, replacement!, out var changed);
        if (!changed)
            return null;
        Commit(workspace, focus, null, focus.NodeId, isNodeSelected: false);
        return replacement is SequenceNode sequence
            ? sequence.Children.First(MathTree.IsStructure).Id
            : replacement.Id;
    }

    private Guid? InsertTemplateAtCaret(MathTemplateKind kind)
    {
        var source = MathTree.FindTextNode(_workspace, _caret.NodeId);
        if (source is null)
            return null;

        if (source.Text.Length == 0)
        {
            if (kind is MathTemplateKind.Subscript or MathTemplateKind.Integral or MathTemplateKind.Summation or
                MathTemplateKind.Limit or MathTemplateKind.Brackets or MathTemplateKind.Cases)
            {
                var emptyContent = source with { IsEmptySlot = true };
                var emptyWrapper = CreateTemplate(kind, emptyContent, out var emptyContentFocus);
                var emptyFocus = kind is MathTemplateKind.Subscript or MathTemplateKind.Cases
                    ? CaretAtStart(emptyContent)
                    : emptyContentFocus;
                var emptyWorkspace = MathTree.ReplaceNode(_workspace, source.Id, emptyWrapper, out var emptyChanged);
                if (!emptyChanged)
                    return null;
                Commit(emptyWorkspace, emptyFocus, null, emptyFocus.NodeId, isNodeSelected: false);
                return emptyWrapper.Id;
            }

            var content = source with { IsEmptySlot = true };
            var wrapper = CreateTemplate(kind, content, out var contentFocus);
            var updated = MathTree.ReplaceNode(_workspace, source.Id, wrapper, out var contentChanged);
            if (!contentChanged)
                return null;
            Commit(updated, contentFocus, null, contentFocus.NodeId, isNodeSelected: false);
            return wrapper.Id;
        }

        var split = TextElementOffsets.ToUtf16Index(source.Text, _caret.TextElementOffset);
        var prefix = source.Text[..split];
        var suffix = source.Text[split..];
        var wrapperNoContent = CreateTemplate(kind, content: null, out var focus);
        var pieces = ImmutableArray.CreateBuilder<MathNode>(3);
        if (prefix.Length > 0)
            pieces.Add(new TextNode(source.Id, prefix)
            {
                StyleOverride = source.StyleOverride,
                Role = source.Role
            });
        pieces.Add(wrapperNoContent);
        if (suffix.Length > 0)
            pieces.Add(new TextNode(prefix.Length == 0 ? source.Id : Guid.NewGuid(), suffix)
            {
                StyleOverride = source.StyleOverride,
                Role = source.Role
            });

        MathNode replacement = pieces.Count == 1
            ? wrapperNoContent
            : new SequenceNode(Guid.NewGuid(), pieces.ToImmutable());
        var workspace = MathTree.ReplaceNode(_workspace, source.Id, replacement, out var changed);
        if (!changed)
            return null;
        Commit(workspace, focus, null, focus.NodeId, isNodeSelected: false);
        return wrapperNoContent.Id;
    }

    private static MathNode CreateTemplate(MathTemplateKind kind, MathNode? content, out MathCaret focus)
    {
        var hasContent = content is not null;
        switch (kind)
        {
            case MathTemplateKind.Fraction:
            {
                var numerator = content ?? EmptySlot();
                var denominator = EmptySlot();
                focus = hasContent ? CaretAtStart(denominator) : CaretAtStart(numerator);
                return new FractionNode(Guid.NewGuid(), numerator, denominator);
            }
            case MathTemplateKind.Radical:
            {
                var radicand = content ?? EmptySlot();
                focus = hasContent ? CaretAtEndOfLastText(radicand) : CaretAtStart(radicand);
                return new RadicalNode(Guid.NewGuid(), radicand);
            }
            case MathTemplateKind.Superscript:
            {
                var @base = content ?? EmptySlot();
                var superscript = EmptySlot();
                focus = CaretAtStart(superscript);
                return new ScriptNode(Guid.NewGuid(), @base, Subscript: null, Superscript: superscript);
            }
            case MathTemplateKind.Subscript:
            {
                var @base = content ?? EmptySlot();
                var subscript = EmptySlot();
                focus = hasContent ? CaretAtStart(subscript) : CaretAtStart(@base);
                return new ScriptNode(Guid.NewGuid(), @base, Subscript: subscript, Superscript: null);
            }
            case MathTemplateKind.Matrix:
            {
                var cells = ImmutableArray.CreateBuilder<MathNode>(4);
                cells.Add(content ?? EmptySlot());
                cells.Add(EmptySlot());
                cells.Add(EmptySlot());
                cells.Add(EmptySlot());
                focus = CaretAtStart(hasContent ? cells[1] : cells[0]);
                return new MatrixNode(Guid.NewGuid(), 2, 2, cells.MoveToImmutable());
            }
            case MathTemplateKind.Integral:
            {
                var lower = EmptySlot();
                var upper = EmptySlot();
                var integrand = content ?? EmptySlot();
                var differential = EmptySlot();
                focus = CaretAtStart(lower);
                return new IntegralNode(Guid.NewGuid(), integrand, lower, upper, differential);
            }
            case MathTemplateKind.Summation:
            {
                var lower = EmptySlot();
                var upper = EmptySlot();
                var body = content ?? EmptySlot();
                focus = CaretAtStart(lower);
                return new SummationNode(Guid.NewGuid(), body, lower, upper);
            }
            case MathTemplateKind.Limit:
            {
                var approach = EmptySlot();
                var body = content ?? EmptySlot();
                focus = CaretAtStart(approach);
                return new LimitNode(Guid.NewGuid(), approach, body);
            }
            case MathTemplateKind.Brackets:
            {
                var inner = content ?? EmptySlot();
                focus = hasContent ? CaretAtEndOfLastText(inner) : CaretAtStart(inner);
                return new BracketsNode(Guid.NewGuid(), MathBracketKind.Parentheses, inner);
            }
            case MathTemplateKind.Cases:
            {
                var firstExpression = content ?? EmptySlot();
                var firstCondition = EmptySlot();
                var secondExpression = EmptySlot();
                var secondCondition = EmptySlot();
                focus = CaretAtStart(hasContent ? firstCondition : firstExpression);
                return new CasesNode(Guid.NewGuid(), ImmutableArray.Create(
                    new CaseRow(firstExpression, firstCondition),
                    new CaseRow(secondExpression, secondCondition)));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static TextNode EmptySlot() => new(Guid.NewGuid(), string.Empty, IsEmptySlot: true);

    private static MathCaret CaretAtStart(MathNode node) =>
        new(FindFirstTextNode(node)?.Id ?? node.Id, 0);

    private static MathCaret CaretAtEndOfLastText(MathNode node)
    {
        var text = FindLastTextNode(node);
        return text is null
            ? new MathCaret(node.Id, 0)
            : new MathCaret(text.Id, TextElementOffsets.Count(text.Text));
    }

    private static TextNode? FindFirstTextNode(MathNode node)
    {
        if (node is TextNode text)
            return text;
        foreach (var child in MathTree.ChildrenOf(node))
        {
            var found = FindFirstTextNode(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static TextNode? FindLastTextNode(MathNode node)
    {
        if (node is TextNode text)
            return text;
        TextNode? result = null;
        foreach (var child in MathTree.ChildrenOf(node))
            result = FindLastTextNode(child) ?? result;
        return result;
    }

    private static TextNode? FindFirstTextNode(EquationWorkspace workspace)
    {
        foreach (var group in workspace.Groups)
        foreach (var row in group.Rows)
        {
            var result = FindFirstTextNode(row.Root);
            if (result is not null)
                return result;
        }
        return null;
    }

    private static TextNode? FindFirstTextNode(IEnumerable<MathNode> nodes)
    {
        foreach (var node in nodes)
        {
            var text = FindFirstTextNode(node);
            if (text is not null)
                return text;
        }
        return null;
    }

    private void Commit(EquationWorkspace workspace, MathCaret caret, MathSelection? selection,
        Guid? selectedNodeId, bool isNodeSelected)
    {
        _undo.Push(CurrentState());
        _redo.Clear();
        _workspace = workspace;
        _caret = caret;
        _selection = selection;
        _selectedNodeId = selectedNodeId;
        _isNodeSelected = isNodeSelected;
        _stateId = Guid.NewGuid();
        _revision++;
        NotifySessionChanged();
    }

    private EditState CurrentState() => new(_workspace, _caret, _selection, _selectedNodeId, _isNodeSelected, _stateId);

    private void Restore(EditState state)
    {
        _workspace = state.Workspace;
        _caret = state.Caret;
        _selection = state.Selection;
        _selectedNodeId = state.SelectedNodeId;
        _isNodeSelected = state.IsNodeSelected;
        _stateId = state.StateId;
        _revision++;
        NotifySessionChanged();
    }

    private void NotifySessionChanged()
    {
        RefreshActiveRowLocation();
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(GroupCount));
        OnPropertyChanged(nameof(Caret));
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectedNodeId));
        OnPropertyChanged(nameof(IsNodeSelected));
        OnPropertyChanged(nameof(Revision));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        NotifyMathSpaceState();
        NotifyActiveRowChanged();
    }
    private void NotifySelectionChanged()
    {
        RefreshActiveRowLocation();
        OnPropertyChanged(nameof(Caret));
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectedNodeId));
        OnPropertyChanged(nameof(IsNodeSelected));
        NotifyMathSpaceState();
        NotifyActiveRowChanged();
    }

    private void NotifyMathSpaceState()
    {
        OnPropertyChanged(nameof(CanInsertMathSpace));
        OnPropertyChanged(nameof(MathSpaceInsertDisabledReason));
    }

    private void NotifyActiveRowChanged()
    {
        OnPropertyChanged(nameof(ActiveGroupNumber));
        OnPropertyChanged(nameof(ActiveRowNumber));
        OnPropertyChanged(nameof(ActiveRowCount));
        OnPropertyChanged(nameof(CanAddGroupAfterCurrent));
        OnPropertyChanged(nameof(CanDuplicateCurrentGroup));
        OnPropertyChanged(nameof(CanDeleteCurrentGroup));
        OnPropertyChanged(nameof(CanMoveCurrentGroupUp));
        OnPropertyChanged(nameof(CanMoveCurrentGroupDown));
        OnPropertyChanged(nameof(CanAddRowAfterCurrent));
        OnPropertyChanged(nameof(CanDuplicateCurrentRow));
        OnPropertyChanged(nameof(CanDeleteCurrentRow));
        OnPropertyChanged(nameof(CanMoveCurrentRowUp));
        OnPropertyChanged(nameof(CanMoveCurrentRowDown));
        ActiveRowChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshActiveRowLocation() => _activeRowLocation = FindActiveRowLocation();

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static EquationWorkspace CreateDefaultWorkspace()
    {
        var firstRow = new EquationRow(Guid.NewGuid(), new SequenceNode(Guid.NewGuid(), ImmutableArray.Create<MathNode>(
            new TextNode(Guid.NewGuid(), "x"),
            new TextNode(Guid.NewGuid(), " = "),
            new FractionNode(Guid.NewGuid(),
                new TextNode(Guid.NewGuid(), "a+b"),
                new TextNode(Guid.NewGuid(), "c+d")),
            new TextNode(Guid.NewGuid(), " + "),
            new RadicalNode(Guid.NewGuid(), new SequenceNode(Guid.NewGuid(), ImmutableArray.Create<MathNode>(
                new ScriptNode(Guid.NewGuid(), new TextNode(Guid.NewGuid(), "y"), null,
                    new TextNode(Guid.NewGuid(), "2")),
                new TextNode(Guid.NewGuid(), "+1")))))));

        var matrix = new MatrixNode(Guid.NewGuid(), 2, 2, ImmutableArray.Create<MathNode>(
            new TextNode(Guid.NewGuid(), "1"),
            new TextNode(Guid.NewGuid(), "2"),
            new TextNode(Guid.NewGuid(), "3"),
            new TextNode(Guid.NewGuid(), "4")));
        var secondRow = new EquationRow(Guid.NewGuid(), new SequenceNode(Guid.NewGuid(), ImmutableArray.Create<MathNode>(
            new TextNode(Guid.NewGuid(), "y"),
            new TextNode(Guid.NewGuid(), " = "),
            matrix)));

        return new EquationWorkspace(
            Guid.NewGuid(),
            SchemaVersion: 1,
            Name: "새 수식 작업",
            Groups: ImmutableArray.Create(new EquationGroup(
                Guid.NewGuid(), ImmutableArray.Create(firstRow, secondRow), AlignmentMode.Equals, RowSpacing: 28)),
            Style: new MathStyle(30, "Cambria Math", "#25364D"));
    }

    private static EquationWorkspace CreateBlankWorkspace()
    {
        var slot = new TextNode(Guid.NewGuid(), string.Empty, IsEmptySlot: true);
        var row = new EquationRow(Guid.NewGuid(),
            new SequenceNode(Guid.NewGuid(), ImmutableArray.Create<MathNode>(slot)));
        var group = new EquationGroup(Guid.NewGuid(), ImmutableArray.Create(row), AlignmentMode.None, RowSpacing: 28);
        return new EquationWorkspace(Guid.NewGuid(), SchemaVersion: 1, Name: "새 수식 작업",
            Groups: ImmutableArray.Create(group), Style: new MathStyle(30, "Cambria Math", "#25364D"));
    }

    private readonly record struct EditState(
        EquationWorkspace Workspace,
        MathCaret Caret,
        MathSelection? Selection,
        Guid? SelectedNodeId,
        bool IsNodeSelected,
        Guid StateId);

    private readonly record struct ActiveRowLocation(
        int GroupIndex,
        int RowIndex,
        EquationGroup Group,
        EquationRow Row);

}
