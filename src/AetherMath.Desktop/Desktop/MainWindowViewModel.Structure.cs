using System.Collections.Immutable;
using System.Windows.Input;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Desktop;

public sealed record ClearScopeChoice(
    MathClearScope Scope,
    string DisplayName,
    string Description);

public sealed partial class MainWindowViewModel
{
    private StructurePanelViewModel _structurePanel = null!;
    private RelayCommand _clearActiveScopeCommand = null!;
    private MathClearScope _selectedClearScope = MathClearScope.CurrentRow;
    private static readonly ImmutableArray<ClearScopeChoice> ScopeChoices = ImmutableArray.Create(
        new ClearScopeChoice(MathClearScope.Selection, "선택 영역", "선택한 텍스트 또는 구조를 비웁니다."),
        new ClearScopeChoice(MathClearScope.CurrentRow, "현재 행", "현재 행의 수식 내용을 비웁니다."),
        new ClearScopeChoice(MathClearScope.CurrentGroup, "현재 그룹", "현재 그룹의 모든 행 내용을 비웁니다."),
        new ClearScopeChoice(MathClearScope.Workspace, "전체 작업", "작업의 모든 행 내용을 비웁니다."));
    private ClearScopeState _clearScopeState;

    public StructurePanelViewModel StructurePanel => _structurePanel;
    public ImmutableArray<ClearScopeChoice> ClearScopeChoices => ScopeChoices;
    public ICommand ClearActiveScopeCommand => _clearActiveScopeCommand;

    public MathClearScope SelectedClearScope
    {
        get => _selectedClearScope;
        set
        {
            if (!Enum.IsDefined(typeof(MathClearScope), value) || _selectedClearScope == value)
                return;

            _selectedClearScope = value;
            OnPropertyChanged(nameof(SelectedClearScope));
            NotifyClearScopeState();
        }
    }

    public bool CanClearActiveScope => _clearScopeState.CanClear;
    public string ClearScopeDescription => _clearScopeState.Description;
    public int ClearScopeAffectedRowCount => _clearScopeState.AffectedRowCount;
    public string ClearScopeDisabledReason => _clearScopeState.DisabledReason;

    private void ClearActiveScope()
    {
        if (Session.Clear(_selectedClearScope))
            EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyClearScopeState()
    {
        _clearScopeState = BuildClearScopeState(_selectedClearScope);
        NotifySelectedClearScopeProperties();
    }

    private void NotifySelectedClearScopeProperties()
    {
        OnPropertyChanged(nameof(CanClearActiveScope));
        OnPropertyChanged(nameof(ClearScopeDescription));
        OnPropertyChanged(nameof(ClearScopeAffectedRowCount));
        OnPropertyChanged(nameof(ClearScopeDisabledReason));
        _clearActiveScopeCommand?.NotifyCanExecuteChanged();
    }

    private ClearScopeState BuildClearScopeState(MathClearScope scope)
    {
        var workspace = Session.Workspace;
        var totalRows = workspace.Groups.Sum(group => group.Rows.Length);
        var hasActiveRow = TryGetActiveStructureLocation(out var groupIndex, out var rowIndex,
            out var activeGroup, out _);
        var selectionRowCount = HasSelectionTarget() && TryGetActiveStructureLocation(out _, out _, out _, out _)
            ? 1
            : 0;
        var groupRows = hasActiveRow ? activeGroup!.Rows.Length : 0;
        var groupNumber = hasActiveRow ? groupIndex + 1 : 0;
        var rowNumber = hasActiveRow ? rowIndex + 1 : 0;
        var selectionDescription = Session.IsNodeSelected ? "선택한 구조 비우기" : "선택한 텍스트 비우기";
        var description = scope switch
        {
            MathClearScope.Selection => selectionDescription,
            MathClearScope.CurrentRow => hasActiveRow
                ? $"현재 행 내용 비우기 (그룹 {groupNumber} · {rowNumber}행)"
                : "현재 행 내용 비우기",
            MathClearScope.CurrentGroup => hasActiveRow
                ? $"그룹 {groupNumber}의 내용 비우기 ({groupRows}행)"
                : "현재 그룹 내용 비우기",
            MathClearScope.Workspace => $"전체 작업 내용 비우기 ({totalRows}행)",
            _ => string.Empty
        };
        var affectedRows = scope switch
        {
            MathClearScope.Selection => selectionRowCount,
            MathClearScope.CurrentRow => hasActiveRow ? 1 : 0,
            MathClearScope.CurrentGroup => groupRows,
            MathClearScope.Workspace => totalRows,
            _ => 0
        };
        var canClear = Session.CanClear(scope);
        return new ClearScopeState(description, affectedRows, canClear,
            canClear ? string.Empty : GetClearDisabledReason(scope));
    }

    private string GetClearDisabledReason(MathClearScope scope)
    {
        if (scope == MathClearScope.Selection)
        {
            if (Session.IsNodeSelected && Session.SelectedNodeId is Guid selectedId &&
                MathTree.TryFindNode(Session.Workspace, selectedId, out var selectedNode, out _) &&
                selectedNode is not null && !MathTree.HasClearableContent(selectedNode))
                return "선택한 구조에 지울 내용이 없습니다.";

            return "비울 텍스트나 전체 구조를 선택하세요.";
        }

        if (!TryGetActiveStructureLocation(out _, out _, out _, out _))
            return "현재 편집 위치를 확인할 수 없습니다.";
        return "범위에 지울 내용이 없습니다.";
    }

    private bool HasSelectionTarget()
    {
        if (Session.IsNodeSelected && Session.SelectedNodeId is Guid selectedId &&
            MathTree.TryFindNode(Session.Workspace, selectedId, out _, out _))
            return true;

        if (Session.Selection is not MathSelection selection || selection.Anchor.NodeId != selection.Focus.NodeId)
            return false;

        return selection.Anchor.TextElementOffset != selection.Focus.TextElementOffset;
    }

    private bool TryGetActiveStructureLocation(out int groupIndex, out int rowIndex,
        out EquationGroup? group, out EquationRow? row)
    {
        var nodeId = Session.IsNodeSelected ? Session.SelectedNodeId : Session.Caret.NodeId;
        if (nodeId is Guid id)
            return MathTree.TryFindContainingRow(Session.Workspace, id,
                out groupIndex, out rowIndex, out group, out row);

        groupIndex = -1;
        rowIndex = -1;
        group = null;
        row = null;
        return false;
    }

    private readonly record struct ClearScopeState(
        string Description,
        int AffectedRowCount,
        bool CanClear,
        string DisabledReason);
}
