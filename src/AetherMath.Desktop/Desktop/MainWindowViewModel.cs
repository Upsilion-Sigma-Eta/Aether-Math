using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AetherMath.Core;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Persistence;
using AetherMath.Rendering;

namespace AetherMath.Desktop;

public enum WorkspaceActionKind
{
    NewWorkspace,
    OpenWorkspace,
    CloseWindow,
    RestoreRecovery
}

public enum WorkspaceActionRequestResult
{
    Completed,
    ConfirmationRequired,
    Rejected,
    Failed,
    Canceled,
    Stale
}

public enum WorkspaceActionResolutionResult
{
    Completed,
    Pending,
    Rejected,
    Canceled,
    Failed,
    Stale
}

public enum RecoveryRestoreRequestStatus
{
    Restored,
    ConfirmationRequired,
    NotFound,
    Corrupt,
    Unsupported,
    Rejected,
    Canceled,
    Failed,
    Stale
}

public sealed record RecoveryRestoreRequestResult(
    RecoveryRestoreRequestStatus Status,
    PersistenceDiagnostic? Diagnostic = null);

public enum WorkspaceRecoveryFlushStatus
{
    Written,
    Removed,
    Skipped,
    Disabled,
    Canceled,
    Stale,
    WriteBlocked,
    Failed
}

public sealed record WorkspaceRecoveryFlushResult(
    WorkspaceRecoveryFlushStatus Status,
    PersistenceDiagnostic? Diagnostic = null);

public sealed class CloseRequestedEventArgs(Guid approvalToken) : EventArgs
{
    public Guid ApprovalToken { get; } = approvalToken;
}

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceFileService _workspaceFiles;
    private readonly IWorkspaceStateService? _workspaceState;
    private readonly TimeSpan _autoRecoveryInterval;
    private readonly object _fileOperationQueueLock = new();
    private Task _fileOperationQueueTail = Task.CompletedTask;
    private readonly object _saveTicketLock = new();
    private readonly List<SaveOperationTicket> _saveTickets = new();
    private long _saveTicketSequence;
    private readonly object _recoveryQueueLock = new();
    private Task _recoveryQueueTail = Task.CompletedTask;
    private readonly object _recoveryTimerLock = new();
    private CancellationTokenSource? _recoveryTimerCancellation;
    private Task? _recoveryTimerTask;
    private CancellationTokenSource _sessionRecoveryCancellation = new();
    private long _recoveryGeneration;
    private long _recoveryTimerGeneration;
    private long _recoveryFlushSequence;
    private long _workspaceStateLifecycleGeneration;
    private long _workspaceStateRefreshGeneration;
    private long _recentSnapshotGeneration;
    private long _recentRelinkRequestGeneration;
    private long _recoveryListSnapshotGeneration;
    private int _workspaceStateBusyCount;
    private int _workspaceStateStopCount;
    private bool _workspaceStateDraining;
    private bool _recoveryStopped = true;
    private readonly ISvgExportService _svgExporter;
    private readonly AvaloniaMathTextMetrics _textMetrics = new();
    private readonly MathOutputPreviewViewModel _outputPreview;
    private readonly RelayCommand _showOutputPreviewCommand;
    private readonly AsyncCommand _saveOutputPreviewSvgCommand;
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly RelayCommand _addRowCommand;
    private readonly RelayCommand _duplicateRowCommand;
    private readonly RelayCommand _deleteRowCommand;
    private readonly RelayCommand _moveRowUpCommand;
    private readonly RelayCommand _moveRowDownCommand;
    private readonly RelayCommand _addGroupCommand;
    private readonly RelayCommand _duplicateGroupCommand;
    private readonly RelayCommand _deleteGroupCommand;
    private readonly RelayCommand _moveGroupUpCommand;
    private readonly RelayCommand _moveGroupDownCommand;
    private readonly RelayCommand _newWorkspaceCommand;
    private readonly RelayCommand _insertThinSpaceCommand;
    private readonly SymbolPaletteViewModel _symbolPalette;
    private readonly AsyncCommand _saveCommand;
    private readonly AsyncCommand _openCommand;
    private readonly AsyncCommand _savePendingWorkspaceActionCommand;
    private readonly RelayCommand _discardPendingWorkspaceActionCommand;
    private readonly RelayCommand _cancelPendingWorkspaceActionCommand;
    private readonly AsyncCommand _refreshWorkspaceStateCommand;
    private readonly AsyncCommand _openRecentWorkspaceCommand;
    private readonly AsyncCommand _pinRecentWorkspaceCommand;
    private readonly AsyncCommand _unpinRecentWorkspaceCommand;
    private readonly AsyncCommand _removeRecentWorkspaceCommand;
    private readonly AsyncCommand _relinkRecentWorkspaceCommand;
    private readonly AsyncCommand _restoreRecoveryCommand;
    private readonly RelayCommand _deleteRecoveryCommand;
    private readonly AsyncCommand _confirmDeleteRecoveryCommand;
    private readonly RelayCommand _cancelDeleteRecoveryCommand;
    private EditingSession _session;
    private Window? _window;
    private string _workspaceName;
    private string _statusText = "변경 없음";
    private string _latexCode;
    private LatexSyntaxSnapshot _latexSyntax = new(string.Empty, ImmutableArray<LatexTokenRange>.Empty);
    private bool _isLatexHighlightingEnabled = true;
    private EquationWorkspace? _latexCodeWorkspace;
    private ImmutableArray<LatexTextDiagnostic> _latexTextDiagnostics = ImmutableArray<LatexTextDiagnostic>.Empty;
    private ImmutableArray<LatexLayoutDiagnostic> _latexLayoutDiagnostics = ImmutableArray<LatexLayoutDiagnostic>.Empty;
    private ImmutableArray<Guid> _latexIncompleteSlotNodeIds = ImmutableArray<Guid>.Empty;
    private bool _canExportLatex;
    private string _latexCodeDiagnostic = string.Empty;
    private string _selectedNodeDescription;
    private string _structureSummary;
    private string? _paletteFontFamily;
    private string? _currentPath;
    private PendingWorkspaceActionState? _pendingWorkspaceAction;
    private CloseApproval? _closeApproval;
    private CancellationTokenSource? _pendingSaveCancellation;
    private bool _isPendingWorkspaceActionBusy;
    private bool _isCloseDrainPending;
    private bool _isWorkspaceOpenPending;
    private string? _pendingWorkspaceActionError;
    private long _navigationGeneration;
    private long _pendingResolutionGeneration;
    private long _saveRequestGeneration;
    private long _workspaceOpenPendingGeneration;
    private EditingSession? _statusSavingSession;
    private long _statusSavingNavigationGeneration;
    private long _statusSavingRequestGeneration;
    private ImmutableArray<RecentWorkspaceEntry> _recentWorkspaces = ImmutableArray<RecentWorkspaceEntry>.Empty;
    private RecentWorkspaceEntry? _selectedRecentWorkspace;
    private PersistenceDiagnostic? _recentWorkspaceDiagnostic;
    private bool _recentWorkspaceCanWrite = true;
    private ImmutableArray<RecoverySummary> _recoveryCandidates = ImmutableArray<RecoverySummary>.Empty;
    private RecoverySummary? _selectedRecovery;
    private ImmutableArray<PersistenceDiagnostic> _recoveryDiagnostics = ImmutableArray<PersistenceDiagnostic>.Empty;
    private bool _isWorkspaceStateLoading;
    private bool _isWorkspaceStateBusy;
    private bool _isWorkspaceStateReady;
    private string _workspaceStateError = string.Empty;
    private Guid? _activeRecoveryId;
    private Guid? _restoreRecoveryReservationId;
    private long _restoreRecoveryReservationGeneration;
    private string? _recoveryOriginalPath;
    private WorkspaceRecoveryFlushResult? _lastRecoveryFlushResult;
    private DateTimeOffset? _lastRecoveryFlushAtUtc;
    private bool _isRecoveryDeleteConfirmationVisible;
    private bool _isOutputPreviewVisible;
    private Guid? _pendingRecoveryDeletionId;
    private string _pendingRecoveryDeletionName = string.Empty;

    public MainWindowViewModel() : this(new WorkspaceFileService(), null, null, null, true)
    {
    }

    public MainWindowViewModel(IWorkspaceFileService workspaceFiles) : this(workspaceFiles, null, null, null, true)
    {
    }

    public MainWindowViewModel(IWorkspaceFileService workspaceFiles, ISvgExportService svgExportService) :
        this(workspaceFiles, null, null, svgExportService, true)
    {
    }

    public MainWindowViewModel(IWorkspaceStateService workspaceStateService,
        TimeSpan? autoRecoveryInterval = null) :
        this(new WorkspaceFileService(), workspaceStateService, autoRecoveryInterval, null, true)
    {
    }

    public MainWindowViewModel(IWorkspaceFileService workspaceFiles, IWorkspaceStateService workspaceStateService,
        TimeSpan? autoRecoveryInterval = null) :
        this(workspaceFiles, workspaceStateService, autoRecoveryInterval, null, true)
    {
    }

    private MainWindowViewModel(IWorkspaceFileService workspaceFiles, IWorkspaceStateService? workspaceStateService,
        TimeSpan? autoRecoveryInterval, ISvgExportService? svgExportService, bool initialize)
    {
        _workspaceFiles = workspaceFiles ?? throw new ArgumentNullException(nameof(workspaceFiles));
        _svgExporter = svgExportService ?? new SvgExportService();
        _workspaceState = workspaceStateService;
        _autoRecoveryInterval = autoRecoveryInterval ?? TimeSpan.FromSeconds(10);
        if (workspaceStateService is not null && _autoRecoveryInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoRecoveryInterval));
        _isWorkspaceStateReady = workspaceStateService is null;
        _recentWorkspaceCanWrite = workspaceStateService is not null;
        _session = EditingSession.CreateDefault();
        _outputPreview = new MathOutputPreviewViewModel(_session, _textMetrics, _svgExporter);
        _outputPreview.PropertyChanged += OnOutputPreviewPropertyChanged;
        _structurePanel = new StructurePanelViewModel(_session);
        _activeRecoveryId = workspaceStateService is null ? null : Guid.NewGuid();
        _paletteFontFamily = _session.Workspace.Style.FontFamily;
        _workspaceName = _session.Workspace.Name;
        _latexCode = string.Empty;
        UpdateLatexCodeState(_session.Workspace);
        _selectedNodeDescription = DescribeSelection(_session);
        _structureSummary = BuildStructureSummary(_session.Workspace);
        _session.PropertyChanged += OnSessionPropertyChanged;
        _session.ActiveRowChanged += OnActiveRowChanged;
        _symbolPalette = new SymbolPaletteViewModel(
            InsertPaletteText,
            InsertPaletteTemplate,
            () => Session.Workspace.Style.FontFamily,
            new AvaloniaMathGlyphCoverageProbe());
        _properties = new MathPropertiesPanelViewModel(_session, _textMetrics,
            new AvaloniaMathGlyphCoverageProbe());

        _undoCommand = new RelayCommand(UndoFromCommand, () => Session.CanUndo);
        _redoCommand = new RelayCommand(RedoFromCommand, () => Session.CanRedo);
        _addRowCommand = new RelayCommand(() => RunRowCommand(Session.AddRowAfterCurrent),
            () => Session.CanAddRowAfterCurrent);
        _duplicateRowCommand = new RelayCommand(() => RunRowCommand(Session.DuplicateCurrentRow),
            () => Session.CanDuplicateCurrentRow);
        _deleteRowCommand = new RelayCommand(() => RunRowCommand(Session.DeleteCurrentRow),
            () => Session.CanDeleteCurrentRow);
        _moveRowUpCommand = new RelayCommand(() => RunRowCommand(Session.MoveCurrentRowUp),
            () => Session.CanMoveCurrentRowUp);
        _moveRowDownCommand = new RelayCommand(() => RunRowCommand(Session.MoveCurrentRowDown),
            () => Session.CanMoveCurrentRowDown);
        _addGroupCommand = new RelayCommand(() => RunGroupCommand(Session.AddGroupAfterCurrent),
            () => Session.CanAddGroupAfterCurrent);
        _duplicateGroupCommand = new RelayCommand(() => RunGroupCommand(Session.DuplicateCurrentGroup),
            () => Session.CanDuplicateCurrentGroup);
        _deleteGroupCommand = new RelayCommand(() => RunGroupCommand(Session.DeleteCurrentGroup),
            () => Session.CanDeleteCurrentGroup);
        _moveGroupUpCommand = new RelayCommand(() => RunGroupCommand(Session.MoveCurrentGroupUp),
            () => Session.CanMoveCurrentGroupUp);
        _moveGroupDownCommand = new RelayCommand(() => RunGroupCommand(Session.MoveCurrentGroupDown),
            () => Session.CanMoveCurrentGroupDown);
        _newWorkspaceCommand = new RelayCommand(() => RequestNewWorkspace(), CanStartWorkspaceAction);
        _clearActiveScopeCommand = new RelayCommand(ClearActiveScope, () => CanClearActiveScope);
        _insertThinSpaceCommand = new RelayCommand(
            () => InsertTemplate(() => Session.InsertMathSpace(MathSpaceKind.Thin)),
            () => CanInsertThinSpace);
        InsertFractionCommand = new RelayCommand(() => InsertTemplate(Session.InsertFraction));
        InsertRootCommand = new RelayCommand(() => InsertTemplate(Session.InsertRoot));
        InsertMatrixCommand = new RelayCommand(() => InsertTemplate(Session.InsertMatrix));
        InsertSuperscriptCommand = new RelayCommand(() => InsertTemplate(Session.InsertSuperscript));
        InsertSubscriptCommand = new RelayCommand(() => InsertTemplate(() => Session.InsertTemplate(MathTemplateKind.Subscript)));
        InsertIntegralCommand = new RelayCommand(() => InsertTemplate(() => Session.InsertTemplate(MathTemplateKind.Integral)));
        InsertSummationCommand = new RelayCommand(() => InsertTemplate(() => Session.InsertTemplate(MathTemplateKind.Summation)));
        InsertLimitCommand = new RelayCommand(() => InsertTemplate(() => Session.InsertTemplate(MathTemplateKind.Limit)));
        InsertBracketsCommand = new RelayCommand(() => InsertTemplate(() => Session.InsertTemplate(MathTemplateKind.Brackets)));
        InsertCasesCommand = new RelayCommand(() => InsertTemplate(() => Session.InsertTemplate(MathTemplateKind.Cases)));
        UndoCommand = _undoCommand;
        RedoCommand = _redoCommand;
        AddRowCommand = _addRowCommand;
        DuplicateRowCommand = _duplicateRowCommand;
        DeleteRowCommand = _deleteRowCommand;
        MoveRowUpCommand = _moveRowUpCommand;
        MoveRowDownCommand = _moveRowDownCommand;
        AddGroupCommand = _addGroupCommand;
        DuplicateGroupCommand = _duplicateGroupCommand;
        DeleteGroupCommand = _deleteGroupCommand;
        MoveGroupUpCommand = _moveGroupUpCommand;
        MoveGroupDownCommand = _moveGroupDownCommand;
        _saveCommand = new AsyncCommand(SaveFromCommandAsync, CanSaveCurrentWorkspace);
        _openCommand = new AsyncCommand(OpenFromCommandAsync, CanStartWorkspaceAction);
        _savePendingWorkspaceActionCommand = new AsyncCommand(SavePendingWorkspaceActionFromCommandAsync,
            () => _pendingWorkspaceAction is not null && !_isPendingWorkspaceActionBusy);
        _discardPendingWorkspaceActionCommand = new RelayCommand(() => DiscardPendingWorkspaceAction(),
            () => _pendingWorkspaceAction is not null && !_isPendingWorkspaceActionBusy);
        _cancelPendingWorkspaceActionCommand = new RelayCommand(() => CancelPendingWorkspaceAction(),
            () => _pendingWorkspaceAction is not null);
        _refreshWorkspaceStateCommand = new AsyncCommand(() => RefreshWorkspaceStateAsync(),
            () => _workspaceState is not null && !_isWorkspaceStateBusy);
        _openRecentWorkspaceCommand = new AsyncCommand(OpenSelectedRecentWorkspaceAsync,
            () => _selectedRecentWorkspace is not null && CanStartWorkspaceAction());
        _pinRecentWorkspaceCommand = new AsyncCommand(PinSelectedRecentWorkspaceAsync,
            () => _selectedRecentWorkspace is not null && _workspaceState is not null && !_isWorkspaceStateBusy);
        _unpinRecentWorkspaceCommand = new AsyncCommand(UnpinSelectedRecentWorkspaceAsync,
            () => _selectedRecentWorkspace is not null && _workspaceState is not null && !_isWorkspaceStateBusy);
        _removeRecentWorkspaceCommand = new AsyncCommand(RemoveSelectedRecentWorkspaceAsync,
            () => _selectedRecentWorkspace is not null && _workspaceState is not null && !_isWorkspaceStateBusy);
        _relinkRecentWorkspaceCommand = new AsyncCommand(RelinkSelectedRecentWorkspaceFromCommandAsync,
            () => _selectedRecentWorkspace is not null && CanStartWorkspaceAction());
        _restoreRecoveryCommand = new AsyncCommand(RestoreSelectedRecoveryAsync,
            () => _selectedRecovery is not null && CanStartWorkspaceAction());
        _deleteRecoveryCommand = new RelayCommand(RequestDeleteSelectedRecovery,
            () => _selectedRecovery is not null && !_isWorkspaceStateBusy && !_isRecoveryDeleteConfirmationVisible);
        _confirmDeleteRecoveryCommand = new AsyncCommand(ConfirmDeleteRecoveryFromCommandAsync,
            () => _pendingRecoveryDeletionId is not null && !_isWorkspaceStateBusy);
        _cancelDeleteRecoveryCommand = new RelayCommand(CancelRecoveryDeleteConfirmation,
            () => _isRecoveryDeleteConfirmationVisible);
        _showOutputPreviewCommand = new RelayCommand(ShowOutputPreview);
        _saveOutputPreviewSvgCommand = new AsyncCommand(SaveOutputPreviewSvgFromCommandAsync,
            () => _window is not null && _outputPreview.CanExportSvg);
        NewWorkspaceCommand = _newWorkspaceCommand;
        SaveCommand = _saveCommand;
        OpenCommand = _openCommand;
        SavePendingWorkspaceActionCommand = _savePendingWorkspaceActionCommand;
        DiscardPendingWorkspaceActionCommand = _discardPendingWorkspaceActionCommand;
        CancelPendingWorkspaceActionCommand = _cancelPendingWorkspaceActionCommand;
        RefreshWorkspaceStateCommand = _refreshWorkspaceStateCommand;
        OpenRecentWorkspaceCommand = _openRecentWorkspaceCommand;
        PinRecentWorkspaceCommand = _pinRecentWorkspaceCommand;
        UnpinRecentWorkspaceCommand = _unpinRecentWorkspaceCommand;
        RemoveRecentWorkspaceCommand = _removeRecentWorkspaceCommand;
        RelinkRecentWorkspaceCommand = _relinkRecentWorkspaceCommand;
        RestoreRecoveryCommand = _restoreRecoveryCommand;
        DeleteRecoveryCommand = _deleteRecoveryCommand;
        ConfirmDeleteRecoveryCommand = _confirmDeleteRecoveryCommand;
        CancelDeleteRecoveryCommand = _cancelDeleteRecoveryCommand;
        ExportSvgCommand = new AsyncCommand(ExportSvgFromCommandAsync);
        ShowOutputPreviewCommand = _showOutputPreviewCommand;
        SaveOutputPreviewSvgCommand = _saveOutputPreviewSvgCommand;
        NotifyClearScopeState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? EditorFocusRequested;
    public event EventHandler<CloseRequestedEventArgs>? CloseRequested;

    public EditingSession Session => _session;
    public SymbolPaletteViewModel SymbolPalette => _symbolPalette;
    public MathOutputPreviewViewModel OutputPreview => _outputPreview;
    public bool IsOutputPreviewVisible
    {
        get => _isOutputPreviewVisible;
        set
        {
            if (_isOutputPreviewVisible == value)
                return;
            _isOutputPreviewVisible = value;
            OnPropertyChanged(nameof(IsOutputPreviewVisible));
        }
    }
    public string WorkspaceName => _workspaceName;
    public string StatusText => _statusText;
    public string LatexCode => _latexCode;
    public LatexSyntaxSnapshot LatexSyntax => _latexSyntax;
    public bool IsLatexHighlightingEnabled
    {
        get => _isLatexHighlightingEnabled;
        set
        {
            if (_isLatexHighlightingEnabled == value)
                return;
            _isLatexHighlightingEnabled = value;
            OnPropertyChanged(nameof(IsLatexHighlightingEnabled));
        }
    }
    public ImmutableArray<LatexTextDiagnostic> LatexTextDiagnostics => _latexTextDiagnostics;
    public ImmutableArray<LatexLayoutDiagnostic> LatexLayoutDiagnostics => _latexLayoutDiagnostics;
    public ImmutableArray<Guid> LatexIncompleteSlotNodeIds => _latexIncompleteSlotNodeIds;
    public bool CanExportLatex => _canExportLatex;
    public string LatexCodeDiagnostic => _latexCodeDiagnostic;
    public string SelectedNodeDescription => _selectedNodeDescription;
    public string StructureSummary => _structureSummary;
    public string? CurrentPath => _currentPath;
    public ImmutableArray<RecentWorkspaceEntry> RecentWorkspaces => _recentWorkspaces;
    public RecentWorkspaceEntry? SelectedRecentWorkspace
    {
        get => _selectedRecentWorkspace;
        set
        {
            if (ReferenceEquals(_selectedRecentWorkspace, value))
                return;
            _selectedRecentWorkspace = value;
            OnPropertyChanged(nameof(SelectedRecentWorkspace));
            OnPropertyChanged(nameof(SelectedRecentWorkspacePreview));
            OnPropertyChanged(nameof(SelectedRecentWorkspaceStatus));
            OnPropertyChanged(nameof(SelectedRecentWorkspaceModifiedText));
            NotifyWorkspaceStateCommands();
        }
    }
    public string SelectedRecentWorkspacePreview => _selectedRecentWorkspace is null
        ? string.Empty
        : BuildRecentWorkspacePreview(_selectedRecentWorkspace);
    public string SelectedRecentWorkspaceStatus => _selectedRecentWorkspace?.FileStatus switch
    {
        RecentWorkspaceFileStatus.Missing => "파일을 찾을 수 없음",
        RecentWorkspaceFileStatus.MetadataUnavailable => "파일 정보를 확인할 수 없음",
        RecentWorkspaceFileStatus.Available => "사용 가능",
        _ => string.Empty
    };
    public string SelectedRecentWorkspaceModifiedText => _selectedRecentWorkspace is null
        ? string.Empty
        : _selectedRecentWorkspace.FileModifiedAtUtc?.ToLocalTime().ToString("g") ?? "수정 시각 없음";
    public PersistenceDiagnostic? RecentWorkspaceDiagnostic => _recentWorkspaceDiagnostic;
    public bool RecentWorkspaceCanWrite => _recentWorkspaceCanWrite;
    public ImmutableArray<RecoverySummary> RecoveryCandidates => _recoveryCandidates;
    public RecoverySummary? SelectedRecovery
    {
        get => _selectedRecovery;
        set
        {
            if (ReferenceEquals(_selectedRecovery, value))
                return;
            _selectedRecovery = value;
            OnPropertyChanged(nameof(SelectedRecovery));
            NotifyWorkspaceStateCommands();
        }
    }
    public ImmutableArray<PersistenceDiagnostic> RecoveryDiagnostics => _recoveryDiagnostics;
    public bool IsWorkspaceStateLoading => _isWorkspaceStateLoading;
    public bool IsWorkspaceStateBusy => _isWorkspaceStateBusy;
    public bool IsWorkspaceStateReady => _isWorkspaceStateReady;
    public string WorkspaceStateError => _workspaceStateError;
    public Guid? ActiveRecoveryId => _activeRecoveryId;
    public bool IsRecoverySaveRequired => Session.IsDirty || HasConflictingSaveTicket(Session);
    public WorkspaceRecoveryFlushResult? LastRecoveryFlushResult => _lastRecoveryFlushResult;
    public DateTimeOffset? LastRecoveryFlushAtUtc => _lastRecoveryFlushAtUtc;
    public long RecoveryFlushSequence => _recoveryFlushSequence;
    public bool IsRecoveryDeleteConfirmationVisible => _isRecoveryDeleteConfirmationVisible;
    public Guid? PendingRecoveryDeletionId => _pendingRecoveryDeletionId;
    public string PendingRecoveryDeletionName => _pendingRecoveryDeletionName;
    public int GroupCount => Session.GroupCount;
    public WorkspaceActionKind? PendingWorkspaceAction => _pendingWorkspaceAction?.Kind;
    public bool IsPendingWorkspaceActionVisible => _pendingWorkspaceAction is not null;
    public bool IsPendingWorkspaceActionBusy => _isPendingWorkspaceActionBusy;
    public string PendingWorkspaceActionTitle => _pendingWorkspaceAction?.Kind switch
    {
        WorkspaceActionKind.NewWorkspace => "새 작업 만들기",
        WorkspaceActionKind.OpenWorkspace => "다른 작업 열기",
        WorkspaceActionKind.CloseWindow => "작업 종료",
        WorkspaceActionKind.RestoreRecovery => "복구본 복원",
        _ => string.Empty
    };
    public string PendingWorkspaceActionMessage
    {
        get
        {
            if (_pendingWorkspaceAction is not { } pending)
                return string.Empty;
            if (!pending.OriginalSession.IsDirty && HasConflictingSaveTicket(pending.OriginalSession))
                return "완료 중이거나 확인되지 않은 저장 결과가 현재 작업과 다를 수 있습니다. 현재 작업을 저장하거나 버리고 계속할 수 있습니다.";

            return pending.Kind switch
            {
                WorkspaceActionKind.NewWorkspace => "저장되지 않은 변경사항이 있습니다. 현재 작업을 저장하거나 버리고 새 작업을 만들 수 있습니다.",
                WorkspaceActionKind.OpenWorkspace => "저장되지 않은 변경사항이 있습니다. 현재 작업을 저장하거나 버리고 선택한 작업을 열 수 있습니다.",
                WorkspaceActionKind.CloseWindow => "저장되지 않은 변경사항이 있습니다. 현재 작업을 저장하거나 버리고 종료할 수 있습니다.",
                WorkspaceActionKind.RestoreRecovery => "저장되지 않은 변경사항이 있습니다. 현재 작업을 저장하거나 버리고 선택한 복구본을 복원할 수 있습니다.",
                _ => string.Empty
            };
        }
    }
    public string PendingWorkspaceActionTarget => _pendingWorkspaceAction?.Kind switch
    {
        WorkspaceActionKind.NewWorkspace => "새 수식 작업",
        WorkspaceActionKind.OpenWorkspace => _pendingWorkspaceAction.OpenPath is string path
            ? Path.GetFileName(path)
            : "선택한 수식 작업",
        WorkspaceActionKind.CloseWindow => "AetherMath 종료",
        WorkspaceActionKind.RestoreRecovery => _pendingWorkspaceAction.RecoverySummary?.WorkspaceName ?? "선택한 복구본",
        _ => string.Empty
    };
    public string PendingWorkspaceActionError => _pendingWorkspaceActionError ?? string.Empty;
    public int ActiveGroupNumber => Session.ActiveGroupNumber;
    public int ActiveRowNumber => Session.ActiveRowNumber;
    public int ActiveRowCount => Session.ActiveRowCount;
    public string DeleteRowToolTip => Session.CanDeleteCurrentRow
        ? "현재 행 삭제"
        : "현재 그룹의 마지막 행은 삭제할 수 없습니다.";
    public string MoveRowUpToolTip => Session.CanMoveCurrentRowUp
        ? "현재 행을 같은 그룹의 한 행 위로 이동"
        : "현재 그룹의 첫 번째 행은 위로 이동할 수 없습니다.";
    public string MoveRowDownToolTip => Session.CanMoveCurrentRowDown
        ? "현재 행을 같은 그룹의 한 행 아래로 이동"
        : "현재 그룹의 마지막 행은 아래로 이동할 수 없습니다.";
    public string DeleteGroupToolTip => Session.CanDeleteCurrentGroup
        ? "현재 수식 그룹 삭제"
        : "마지막 수식 그룹은 삭제할 수 없습니다.";
    public string MoveGroupUpToolTip => Session.CanMoveCurrentGroupUp
        ? "현재 그룹을 한 그룹 위로 이동"
        : "첫 번째 수식 그룹은 위로 이동할 수 없습니다.";
    public string MoveGroupDownToolTip => Session.CanMoveCurrentGroupDown
        ? "현재 그룹을 한 그룹 아래로 이동"
        : "마지막 수식 그룹은 아래로 이동할 수 없습니다.";
    public bool CanInsertThinSpace => Session.CanInsertMathSpace;
    public string ThinSpaceDisabledReason => Session.MathSpaceInsertDisabledReason;
    public string ThinSpaceNotice => "현재 조판 글자 크기의 1/6 간격을 넣고 LaTeX에서는 \\, 명령으로 출력합니다.";
    public ICommand InsertFractionCommand { get; }
    public ICommand InsertRootCommand { get; }
    public ICommand InsertMatrixCommand { get; }
    public ICommand InsertSuperscriptCommand { get; }
    public ICommand InsertSubscriptCommand { get; }
    public ICommand InsertIntegralCommand { get; }
    public ICommand InsertSummationCommand { get; }
    public ICommand InsertLimitCommand { get; }
    public ICommand InsertBracketsCommand { get; }
    public ICommand InsertCasesCommand { get; }
    public ICommand InsertThinSpaceCommand => _insertThinSpaceCommand;
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand AddRowCommand { get; }
    public ICommand DuplicateRowCommand { get; }
    public ICommand DeleteRowCommand { get; }
    public ICommand MoveRowUpCommand { get; }
    public ICommand MoveRowDownCommand { get; }
    public ICommand AddGroupCommand { get; }
    public ICommand DuplicateGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand MoveGroupUpCommand { get; }
    public ICommand MoveGroupDownCommand { get; }
    public ICommand NewWorkspaceCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SavePendingWorkspaceActionCommand { get; }
    public ICommand DiscardPendingWorkspaceActionCommand { get; }
    public ICommand CancelPendingWorkspaceActionCommand { get; }
    public ICommand ExportSvgCommand { get; }
    public ICommand ShowOutputPreviewCommand { get; }
    public ICommand SaveOutputPreviewSvgCommand { get; }
    public ICommand RefreshWorkspaceStateCommand { get; }
    public ICommand OpenRecentWorkspaceCommand { get; }
    public ICommand PinRecentWorkspaceCommand { get; }
    public ICommand UnpinRecentWorkspaceCommand { get; }
    public ICommand RemoveRecentWorkspaceCommand { get; }
    public ICommand RelinkRecentWorkspaceCommand { get; }
    public ICommand RestoreRecoveryCommand { get; }
    public ICommand DeleteRecoveryCommand { get; }
    public ICommand ConfirmDeleteRecoveryCommand { get; }
    public ICommand CancelDeleteRecoveryCommand { get; }

    public async Task InitializeWorkspaceStateAsync(CancellationToken cancellationToken = default)
    {
        if (_workspaceState is null)
        {
            SetWorkspaceStateReady(true);
            return;
        }

        long initializeGeneration;
        lock (_recoveryTimerLock)
            initializeGeneration = ++_workspaceStateLifecycleGeneration;

        await RefreshWorkspaceStateAsync(cancellationToken).ConfigureAwait(true);
        lock (_recoveryTimerLock)
        {
            if (initializeGeneration != _workspaceStateLifecycleGeneration || cancellationToken.IsCancellationRequested)
                return;
            _recoveryStopped = false;
        }
        StartRecoveryTimer(initializeGeneration);
    }

    public async Task RefreshWorkspaceStateAsync(CancellationToken cancellationToken = default)
    {
        if (_workspaceState is null)
        {
            SetWorkspaceStateReady(true);
            return;
        }

        long refreshGeneration;
        long recentGeneration;
        long recoveryListGeneration;
        long lifecycleGeneration;
        lock (_recoveryTimerLock)
        {
            lifecycleGeneration = _workspaceStateLifecycleGeneration;
            refreshGeneration = ++_workspaceStateRefreshGeneration;
            recentGeneration = ++_recentSnapshotGeneration;
            recoveryListGeneration = ++_recoveryListSnapshotGeneration;
        }
        SetWorkspaceStateLoading(true);
        EnterWorkspaceStateBusy();
        try
        {
            var service = _workspaceState;
            var result = await EnqueueWorkspaceStateOperationAsync(async () =>
            {
                var recentTask = service.ReadRecentWorkspacesAsync(cancellationToken);
                var recoveryTask = service.ListRecoveriesAsync(cancellationToken);
                await Task.WhenAll(recentTask, recoveryTask).ConfigureAwait(false);
                return (Recent: await recentTask.ConfigureAwait(false),
                    Recovery: await recoveryTask.ConfigureAwait(false));
            }, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(true);

            if (recentGeneration == Volatile.Read(ref _recentSnapshotGeneration) &&
                lifecycleGeneration == Volatile.Read(ref _workspaceStateLifecycleGeneration))
                ApplyRecentWorkspaceSnapshot(result.Recent);
            if (recoveryListGeneration == Volatile.Read(ref _recoveryListSnapshotGeneration) &&
                lifecycleGeneration == Volatile.Read(ref _workspaceStateLifecycleGeneration))
                ApplyRecoveryList(result.Recovery);
            if (refreshGeneration == Volatile.Read(ref _workspaceStateRefreshGeneration))
                SetWorkspaceStateError(string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (refreshGeneration == Volatile.Read(ref _workspaceStateRefreshGeneration))
                SetWorkspaceStateError(exception.Message);
        }
        finally
        {
            if (refreshGeneration == Volatile.Read(ref _workspaceStateRefreshGeneration))
            {
                SetWorkspaceStateLoading(false);
                SetWorkspaceStateReady(true);
            }
            LeaveWorkspaceStateBusy();
        }
    }

    public Task<WorkspaceRecoveryFlushResult> FlushRecoveryAsync(CancellationToken cancellationToken = default)
    {
        if (_workspaceState is null || _activeRecoveryId is null)
            return Task.FromResult(new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Disabled));
        if (_recoveryStopped)
            return Task.FromResult(new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Disabled));

        var operationGeneration = _recoveryGeneration;
        return FlushRecoveryCoreAsync(operationGeneration, cancellationToken, isTimerTick: false);
    }

    public async Task StopRecoveryAsync()
    {
        CancellationTokenSource? timerCancellation;
        Task? timerTask;
        lock (_recoveryTimerLock)
        {
            _recoveryStopped = true;
            _workspaceStateDraining = true;
            _workspaceStateStopCount++;
            _workspaceStateLifecycleGeneration++;
            _workspaceStateRefreshGeneration++;
            _recentSnapshotGeneration++;
            _recoveryListSnapshotGeneration++;
            timerCancellation = _recoveryTimerCancellation;
            timerTask = _recoveryTimerTask;
            _recoveryTimerCancellation = null;
            _recoveryTimerTask = null;
            _recoveryGeneration++;
        }
        SetWorkspaceStateLoading(false);

        if (timerCancellation is not null)
        {
            timerCancellation.Cancel();
            if (timerTask is not null)
            {
                try
                {
                    await timerTask.ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                }
            }
            timerCancellation.Dispose();
        }

        while (true)
        {
            Task tail;
            lock (_recoveryTimerLock)
            lock (_recoveryQueueLock)
                tail = _recoveryQueueTail;
            await tail.ConfigureAwait(true);
            lock (_recoveryTimerLock)
            lock (_recoveryQueueLock)
            {
                if (ReferenceEquals(tail, _recoveryQueueTail))
                {
                    _workspaceStateStopCount = Math.Max(0, _workspaceStateStopCount - 1);
                    _workspaceStateDraining = _workspaceStateStopCount > 0;
                    break;
                }
            }
        }
    }

    public async Task<WorkspaceActionRequestResult> OpenRecentWorkspaceAsync(
        string fullPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return WorkspaceActionRequestResult.Rejected;
        return await OpenFromPathAsync(fullPath, cancellationToken).ConfigureAwait(true);
    }

    public async Task<WorkspaceActionRequestResult> RelinkRecentWorkspaceAsync(
        string originalFullPath, string replacementFullPath, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _recentRelinkRequestGeneration);
        if (string.IsNullOrWhiteSpace(originalFullPath) || string.IsNullOrWhiteSpace(replacementFullPath))
            return WorkspaceActionRequestResult.Rejected;

        var originalPath = Path.GetFullPath(originalFullPath);
        var replacementPath = Path.GetFullPath(replacementFullPath);
        if (!CanStartWorkspaceAction())
            return WorkspaceActionRequestResult.Rejected;

        var generation = BeginNavigationRequest();
        var originalSession = Session;
        return await OpenFromPathForRequestAsync(replacementPath, cancellationToken, generation, originalSession,
            originalPath).ConfigureAwait(true);
    }

    public Task<RecentWorkspaceMutationStatus> PinRecentWorkspaceAsync(
        string fullPath, CancellationToken cancellationToken = default) =>
        SetRecentWorkspacePinnedAsync(fullPath, isPinned: true, cancellationToken);

    public Task<RecentWorkspaceMutationStatus> UnpinRecentWorkspaceAsync(
        string fullPath, CancellationToken cancellationToken = default) =>
        SetRecentWorkspacePinnedAsync(fullPath, isPinned: false, cancellationToken);

    public async Task<RecentWorkspaceMutationStatus> RemoveRecentWorkspaceAsync(
        string fullPath, CancellationToken cancellationToken = default)
    {
        if (_workspaceState is null)
            return RecentWorkspaceMutationStatus.NotFound;

        var (snapshotGeneration, lifecycleGeneration) = BeginRecentSnapshotRequest();
        EnterWorkspaceStateBusy();
        try
        {
            var mutation = await EnqueueWorkspaceStateOperationAsync(
                () => _workspaceState.RemoveRecentAsync(fullPath, cancellationToken), cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(true);
            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
                ApplyRecentWorkspaceMutation(mutation);
            return mutation.Status;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
                SetWorkspaceStateError(exception.Message);
            return RecentWorkspaceMutationStatus.WriteBlocked;
        }
        finally
        {
            LeaveWorkspaceStateBusy();
        }
    }

    public async Task<RecoveryRestoreRequestResult> RestoreRecoveryAsync(
        Guid recoveryId, CancellationToken cancellationToken = default)
    {
        if (_workspaceState is null)
            return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Rejected);
        if (recoveryId == Guid.Empty || _recoveryStopped || !CanStartWorkspaceAction())
            return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Rejected);

        var stateLifecycleGeneration = _workspaceStateLifecycleGeneration;
        var generation = BeginNavigationRequest();
        var originalSession = Session;
        lock (_recoveryTimerLock)
        {
            _restoreRecoveryReservationId = recoveryId;
            _restoreRecoveryReservationGeneration = generation;
        }
        EnterWorkspaceStateBusy();
        try
        {
            var read = await EnqueueWorkspaceStateOperationAsync(
                () => _workspaceState.ReadRecoveryAsync(recoveryId, cancellationToken), cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(true);

            if (stateLifecycleGeneration != _workspaceStateLifecycleGeneration)
                return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Stale);
            if (cancellationToken.IsCancellationRequested)
                return IsCurrentNavigation(generation, originalSession)
                    ? new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Canceled)
                    : new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Stale);
            if (!IsCurrentNavigation(generation, originalSession))
                return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Stale);

            if (read.Status != RecoveryReadStatus.Found || read.Recovery is null)
            {
                SetWorkspaceStateError(read.Diagnostic?.Message ?? "선택한 복구본을 읽을 수 없습니다.");
                return new RecoveryRestoreRequestResult(read.Status switch
                {
                    RecoveryReadStatus.NotFound => RecoveryRestoreRequestStatus.NotFound,
                    RecoveryReadStatus.Corrupt => RecoveryRestoreRequestStatus.Corrupt,
                    RecoveryReadStatus.Unsupported => RecoveryRestoreRequestStatus.Unsupported,
                    _ => RecoveryRestoreRequestStatus.Failed
                }, read.Diagnostic);
            }

            var recovery = read.Recovery;
            WorkspaceValidator.Validate(recovery.Snapshot.Workspace);
            var summary = _recoveryCandidates.FirstOrDefault(candidate => candidate.RecoveryId == recoveryId) ??
                new RecoverySummary(recoveryId, recovery.Snapshot.Workspace.Id,
                    recovery.Snapshot.Workspace.Name, recovery.CapturedAtUtc, recovery.OriginalPath,
                    recovery.Snapshot.Revision);
            if (NeedsWorkspaceConfirmation(originalSession))
            {
                SetPendingWorkspaceAction(new PendingWorkspaceActionState(
                    WorkspaceActionKind.RestoreRecovery, originalSession, generation,
                    recovery: recovery, recoverySummary: summary));
                return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.ConfirmationRequired,
                    read.Diagnostic);
            }

            ApplyRecoveredWorkspace(recovery);
            return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Restored, read.Diagnostic);
        }
        catch (OperationCanceledException)
        {
            if (stateLifecycleGeneration != _workspaceStateLifecycleGeneration)
                return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Stale);
            return IsCurrentNavigation(generation, originalSession)
                ? new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Canceled)
                : new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Stale);
        }
        catch (Exception exception)
        {
            if (stateLifecycleGeneration != _workspaceStateLifecycleGeneration)
                return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Stale);
            if (!IsCurrentNavigation(generation, originalSession))
                return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Stale);
            SetWorkspaceStateError(exception.Message);
            return new RecoveryRestoreRequestResult(RecoveryRestoreRequestStatus.Failed,
                new PersistenceDiagnostic("RecoveryRestoreFailed", exception.Message));
        }
        finally
        {
            lock (_recoveryTimerLock)
            {
                if (_restoreRecoveryReservationGeneration == generation)
                {
                    _restoreRecoveryReservationId = null;
                    _restoreRecoveryReservationGeneration = 0;
                }
            }
            LeaveWorkspaceStateBusy();
        }
    }

    public async Task<bool> DeleteRecoveryAsync(Guid recoveryId, CancellationToken cancellationToken = default)
    {
        var service = _workspaceState;
        if (service is null || recoveryId == Guid.Empty)
            return false;

        var lifecycleGeneration = _workspaceStateLifecycleGeneration;
        EnterWorkspaceStateBusy();
        try
        {
            var deleted = await EnqueueWorkspaceStateOperationAsync(
                () => service.DeleteRecoveryAsync(recoveryId, cancellationToken), cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(true);
            if (deleted && lifecycleGeneration == _workspaceStateLifecycleGeneration)
                await RefreshRecoveryCandidatesAsync(cancellationToken).ConfigureAwait(true);
            return deleted;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (lifecycleGeneration == _workspaceStateLifecycleGeneration)
                SetWorkspaceStateError(exception.Message);
            throw;
        }
        finally
        {
            LeaveWorkspaceStateBusy();
        }
    }

    private async Task<RecentWorkspaceMutationStatus> SetRecentWorkspacePinnedAsync(
        string fullPath, bool isPinned, CancellationToken cancellationToken)
    {
        if (_workspaceState is null)
            return RecentWorkspaceMutationStatus.NotFound;

        var (snapshotGeneration, lifecycleGeneration) = BeginRecentSnapshotRequest();
        EnterWorkspaceStateBusy();
        try
        {
            var mutation = await EnqueueWorkspaceStateOperationAsync(
                () => _workspaceState.SetPinnedAsync(fullPath, isPinned, cancellationToken), cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(true);
            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
                ApplyRecentWorkspaceMutation(mutation);
            return mutation.Status;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
                SetWorkspaceStateError(exception.Message);
            return RecentWorkspaceMutationStatus.WriteBlocked;
        }
        finally
        {
            LeaveWorkspaceStateBusy();
        }
    }

    private async Task OpenSelectedRecentWorkspaceAsync()
    {
        var selected = _selectedRecentWorkspace;
        if (selected is null)
            return;
        await OpenRecentWorkspaceAsync(selected.FullPath).ConfigureAwait(true);
    }

    private async Task PinSelectedRecentWorkspaceAsync()
    {
        var selected = _selectedRecentWorkspace;
        if (selected is not null)
            await PinRecentWorkspaceAsync(selected.FullPath).ConfigureAwait(true);
    }

    private async Task UnpinSelectedRecentWorkspaceAsync()
    {
        var selected = _selectedRecentWorkspace;
        if (selected is not null)
            await UnpinRecentWorkspaceAsync(selected.FullPath).ConfigureAwait(true);
    }

    private async Task RemoveSelectedRecentWorkspaceAsync()
    {
        var selected = _selectedRecentWorkspace;
        if (selected is not null)
            await RemoveRecentWorkspaceAsync(selected.FullPath).ConfigureAwait(true);
    }

    private async Task RelinkSelectedRecentWorkspaceFromCommandAsync()
    {
        var selected = _selectedRecentWorkspace;
        var window = _window;
        if (selected is null || window is null || !CanStartWorkspaceAction())
            return;

        var relinkGeneration = Interlocked.Increment(ref _recentRelinkRequestGeneration);
        var originalPath = selected.FullPath;
        var originalSession = Session;
        var navigationGeneration = _navigationGeneration;
        var lifecycleGeneration = _workspaceStateLifecycleGeneration;
        bool IsRequestCurrent() =>
            relinkGeneration == Volatile.Read(ref _recentRelinkRequestGeneration) &&
            ReferenceEquals(window, _window) && ReferenceEquals(originalSession, Session) &&
            navigationGeneration == _navigationGeneration && lifecycleGeneration == _workspaceStateLifecycleGeneration &&
            string.Equals(originalPath, _selectedRecentWorkspace?.FullPath, StringComparison.OrdinalIgnoreCase);

        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "누락된 작업 파일 다시 찾기",
                AllowMultiple = false
            }).ConfigureAwait(true);
            if (!IsRequestCurrent())
                return;
            if (files.Count == 0)
                return;

            var replacementPath = files[0].TryGetLocalPath();
            if (replacementPath is null)
            {
                if (IsRequestCurrent())
                    SetWorkspaceStateError("선택한 항목에서 로컬 파일 경로를 얻을 수 없습니다.");
                return;
            }

            if (IsRequestCurrent())
                await RelinkRecentWorkspaceAsync(originalPath, replacementPath).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (IsRequestCurrent())
                SetWorkspaceStateError(exception.Message);
        }
    }

    private async Task RestoreSelectedRecoveryAsync()
    {
        if (_selectedRecovery is RecoverySummary selected)
            await RestoreRecoveryAsync(selected.RecoveryId).ConfigureAwait(true);
    }

    private void RequestDeleteSelectedRecovery()
    {
        if (_selectedRecovery is not RecoverySummary selected || _isWorkspaceStateBusy ||
            _isRecoveryDeleteConfirmationVisible)
            return;

        _pendingRecoveryDeletionId = selected.RecoveryId;
        _pendingRecoveryDeletionName = selected.WorkspaceName;
        _isRecoveryDeleteConfirmationVisible = true;
        OnPropertyChanged(nameof(PendingRecoveryDeletionId));
        OnPropertyChanged(nameof(PendingRecoveryDeletionName));
        OnPropertyChanged(nameof(IsRecoveryDeleteConfirmationVisible));
        NotifyWorkspaceStateCommands();
    }

    private async Task ConfirmDeleteRecoveryFromCommandAsync()
    {
        if (_pendingRecoveryDeletionId is not Guid recoveryId)
            return;
        var lifecycleGeneration = _workspaceStateLifecycleGeneration;
        try
        {
            await DeleteRecoveryAsync(recoveryId).ConfigureAwait(true);
            if (lifecycleGeneration != _workspaceStateLifecycleGeneration)
                return;
            CancelRecoveryDeleteConfirmation();
        }
        catch (Exception exception)
        {
            if (lifecycleGeneration == _workspaceStateLifecycleGeneration)
                SetWorkspaceStateError(exception.Message);
        }
    }

    private void CancelRecoveryDeleteConfirmation()
    {
        if (!_isRecoveryDeleteConfirmationVisible && _pendingRecoveryDeletionId is null)
            return;
        _pendingRecoveryDeletionId = null;
        _pendingRecoveryDeletionName = string.Empty;
        _isRecoveryDeleteConfirmationVisible = false;
        OnPropertyChanged(nameof(PendingRecoveryDeletionId));
        OnPropertyChanged(nameof(PendingRecoveryDeletionName));
        OnPropertyChanged(nameof(IsRecoveryDeleteConfirmationVisible));
        NotifyWorkspaceStateCommands();
    }

    private async Task<WorkspaceRecoveryFlushResult> FlushRecoveryCoreAsync(
        long operationGeneration, CancellationToken cancellationToken, bool isTimerTick)
    {
        var service = _workspaceState;
        var session = Session;
        var recoveryId = _activeRecoveryId;
        if (service is null || recoveryId is null)
            return PublishRecoveryFlushResult(new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Disabled));
        if (!IsRecoveryContextCurrent(operationGeneration, session, recoveryId.Value))
            return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sessionRecoveryCancellation.Token);
        var operationToken = linkedCancellation.Token;
        try
        {
            operationToken.ThrowIfCancellationRequested();
            if (IsRecoveryIdProtectedByPendingRestore(recoveryId.Value))
            {
                var diagnostic = new PersistenceDiagnostic("RecoveryRestorePending",
                    "선택한 복구본이 복원 확인 대기 중이므로 원본을 보존했습니다.");
                SetWorkspaceStateError(diagnostic.Message);
                return PublishRecoveryFlushResult(
                    new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Skipped, diagnostic));
            }

            WorkspaceRecoveryFlushResult result;
            if (session.IsDirty || NeedsWorkspaceConfirmation(session))
            {
                var snapshot = session.CaptureSnapshot();
                var recovery = new WorkspaceRecovery(recoveryId.Value, snapshot, DateTimeOffset.UtcNow,
                    _recoveryOriginalPath ?? _currentPath);
                var queuedWrite = TryEnqueueRecoveryOperation(operationGeneration,
                    async () =>
                    {
                        if (IsRecoveryIdProtectedByPendingRestore(recoveryId.Value))
                            return (Skipped: true, Result: (RecoveryWriteResult?)null);
                        return (Skipped: false,
                            Result: (RecoveryWriteResult?)await service.SaveRecoveryAsync(recovery, operationToken)
                                .ConfigureAwait(false));
                    }, operationToken);
                if (queuedWrite is null)
                    return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale);
                var write = await queuedWrite.ConfigureAwait(true);
                if (!IsRecoveryContextCurrent(operationGeneration, session, recoveryId.Value))
                    return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale,
                        write.Result?.Diagnostic);

                if (write.Skipped)
                {
                    var diagnostic = new PersistenceDiagnostic("RecoveryRestorePending",
                        "선택한 복구본이 복원 확인 대기 중이므로 원본을 보존했습니다.");
                    result = new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Skipped, diagnostic);
                }
                else
                {
                    var writeResult = write.Result!;
                    result = writeResult.Status == RecoveryWriteStatus.Written
                        ? new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Written, writeResult.Diagnostic)
                        : new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.WriteBlocked, writeResult.Diagnostic);
                    if (writeResult.Status == RecoveryWriteStatus.WriteBlocked)
                        SetWorkspaceStateError(writeResult.Diagnostic?.Message ?? "복구본을 저장할 수 없습니다.");
                }
            }
            else
            {
                var queuedCleanup = TryEnqueueRecoveryOperation(operationGeneration,
                    () => DeleteRecoveryIfValidAsync(service, recoveryId.Value, operationToken), operationToken);
                if (queuedCleanup is null)
                    return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale);
                var cleanup = await queuedCleanup.ConfigureAwait(true);
                if (!IsRecoveryContextCurrent(operationGeneration, session, recoveryId.Value))
                    return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale);
                result = new WorkspaceRecoveryFlushResult(cleanup.Removed
                    ? WorkspaceRecoveryFlushStatus.Removed
                    : WorkspaceRecoveryFlushStatus.Skipped, cleanup.Diagnostic);
            }

            await RefreshRecoveryCandidatesAsync(CancellationToken.None).ConfigureAwait(true);
            if (!IsRecoveryContextCurrent(operationGeneration, session, recoveryId.Value))
                return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale, result.Diagnostic);
            SetWorkspaceStateError(result.Status == WorkspaceRecoveryFlushStatus.Written ||
                                   result.Status == WorkspaceRecoveryFlushStatus.Removed
                ? string.Empty
                : result.Diagnostic?.Message ?? string.Empty);
            return PublishRecoveryFlushResult(result);
        }
        catch (OperationCanceledException)
        {
            if (!IsRecoveryContextCurrent(operationGeneration, session, recoveryId.Value))
                return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale);
            var canceled = new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Canceled);
            if (!isTimerTick)
                return PublishRecoveryFlushResult(canceled);
            return PublishRecoveryFlushResult(canceled);
        }
        catch (Exception exception)
        {
            if (!IsRecoveryContextCurrent(operationGeneration, session, recoveryId.Value))
                return new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Stale);
            var diagnostic = new PersistenceDiagnostic("RecoveryWriteFailed", exception.Message);
            SetWorkspaceStateError(exception.Message);
            return PublishRecoveryFlushResult(
                new WorkspaceRecoveryFlushResult(WorkspaceRecoveryFlushStatus.Failed, diagnostic));
        }
    }

    private void StartRecoveryTimer(long lifecycleGeneration)
    {
        if (_workspaceState is null)
            return;

        lock (_recoveryTimerLock)
        {
            if (_recoveryStopped || lifecycleGeneration != _workspaceStateLifecycleGeneration ||
                _recoveryTimerCancellation is not null)
                return;
            var cancellation = new CancellationTokenSource();
            var timerGeneration = ++_recoveryTimerGeneration;
            _recoveryTimerCancellation = cancellation;
            _recoveryTimerTask = RunRecoveryTimerAsync(timerGeneration, cancellation.Token);
        }
    }

    private async Task RunRecoveryTimerAsync(long timerGeneration, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_autoRecoveryInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                if (!IsRecoveryTimerCurrent(timerGeneration))
                    return;
                await FlushRecoveryCoreAsync(_recoveryGeneration, cancellationToken, isTimerTick: true)
                    .ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsRecoveryTimerCurrent(timerGeneration))
            {
                SetWorkspaceStateError(exception.Message);
                PublishRecoveryFlushResult(new WorkspaceRecoveryFlushResult(
                    WorkspaceRecoveryFlushStatus.Failed,
                    new PersistenceDiagnostic("RecoveryTimerFailed", exception.Message)));
            }
        }
    }

    private bool IsRecoveryTimerCurrent(long timerGeneration)
    {
        lock (_recoveryTimerLock)
            return timerGeneration == _recoveryTimerGeneration && _recoveryTimerCancellation is not null;
    }

    private bool IsRecoveryContextCurrent(long generation, EditingSession session, Guid recoveryId) =>
        generation == _recoveryGeneration && ReferenceEquals(session, Session) &&
        _activeRecoveryId == recoveryId;

    private bool IsRecoveryStopped
    {
        get
        {
            lock (_recoveryTimerLock)
                return _recoveryStopped;
        }
    }

    private bool IsRecoveryIdProtectedByPendingRestore(Guid recoveryId)
    {
        lock (_recoveryTimerLock)
            return _restoreRecoveryReservationId == recoveryId ||
                   _pendingWorkspaceAction is
                       { Kind: WorkspaceActionKind.RestoreRecovery, Recovery: { } recovery } &&
                   recovery.RecoveryId == recoveryId;
    }

    private WorkspaceRecoveryFlushResult PublishRecoveryFlushResult(WorkspaceRecoveryFlushResult result)
    {
        _lastRecoveryFlushResult = result;
        _lastRecoveryFlushAtUtc = DateTimeOffset.UtcNow;
        _recoveryFlushSequence++;
        OnPropertyChanged(nameof(LastRecoveryFlushResult));
        OnPropertyChanged(nameof(LastRecoveryFlushAtUtc));
        OnPropertyChanged(nameof(RecoveryFlushSequence));
        return result;
    }

    private Task<T> EnqueueWorkspaceStateOperationAsync<T>(Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_recoveryTimerLock)
        {
            if (_workspaceStateDraining)
                throw new OperationCanceledException("상태 저장소가 종료 중입니다.", cancellationToken);
            lock (_recoveryQueueLock)
            {
                var preceding = _recoveryQueueTail;
                var queued = Task.Run(async () =>
                {
                    await preceding.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return await operation().ConfigureAwait(false);
                }, CancellationToken.None);
                _recoveryQueueTail = ObserveWorkspaceStateOperationAsync(queued);
                return queued;
            }
        }
    }

    private Task<T>? TryEnqueueRecoveryOperation<T>(long recoveryGeneration, Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        lock (_recoveryTimerLock)
        {
            if (_recoveryStopped || recoveryGeneration != _recoveryGeneration)
                return null;
            return EnqueueWorkspaceStateOperationAsync(operation, cancellationToken);
        }
    }

    private static async Task ObserveWorkspaceStateOperationAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private Task CaptureWorkspaceStateQueueTail()
    {
        lock (_recoveryQueueLock)
            return _recoveryQueueTail;
    }

    private async Task RefreshRecoveryCandidatesAsync(CancellationToken cancellationToken)
    {
        var service = _workspaceState;
        if (service is null)
            return;
        long lifecycleGeneration;
        long recoveryGeneration;
        long snapshotGeneration;
        lock (_recoveryTimerLock)
        {
            if (_recoveryStopped || _workspaceStateDraining)
                return;
            lifecycleGeneration = _workspaceStateLifecycleGeneration;
            recoveryGeneration = _recoveryGeneration;
            snapshotGeneration = ++_recoveryListSnapshotGeneration;
        }
        var queuedRead = TryEnqueueRecoveryOperation(recoveryGeneration,
            () => service.ListRecoveriesAsync(cancellationToken), cancellationToken);
        if (queuedRead is null)
            return;
        var result = await queuedRead.WaitAsync(cancellationToken).ConfigureAwait(true);
        if (!_recoveryStopped && lifecycleGeneration == _workspaceStateLifecycleGeneration &&
            recoveryGeneration == _recoveryGeneration && snapshotGeneration == _recoveryListSnapshotGeneration)
            ApplyRecoveryList(result);
    }

    private async Task RefreshRecoveryCandidatesIfCurrentAsync(long generation)
    {
        if (_workspaceState is null || generation != _recoveryGeneration)
            return;
        try
        {
            await RefreshRecoveryCandidatesAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (generation == _recoveryGeneration)
                SetWorkspaceStateError(exception.Message);
        }
    }

    private void ApplyRecentWorkspaceSnapshot(RecentWorkspaceIndexResult result)
    {
        var previousSelection = _selectedRecentWorkspace?.FullPath;
        var restoredSelection = previousSelection is null
            ? null
            : result.Entries.FirstOrDefault(entry => PathsEqual(entry.FullPath, previousSelection));
        _recentWorkspaces = result.Entries;
        _recentWorkspaceCanWrite = result.CanWrite;
        _recentWorkspaceDiagnostic = result.Diagnostic;
        OnPropertyChanged(nameof(RecentWorkspaces));
        OnPropertyChanged(nameof(RecentWorkspaceCanWrite));
        OnPropertyChanged(nameof(RecentWorkspaceDiagnostic));
        // ItemsSource 변경 중 TwoWay SelectedItem 바인딩이 null을 동기적으로 기록할 수 있다.
        // 컬렉션 교체 알림이 끝난 뒤 선택을 복원한다.
        _selectedRecentWorkspace = restoredSelection;
        OnPropertyChanged(nameof(SelectedRecentWorkspace));
        OnPropertyChanged(nameof(SelectedRecentWorkspacePreview));
        OnPropertyChanged(nameof(SelectedRecentWorkspaceStatus));
        OnPropertyChanged(nameof(SelectedRecentWorkspaceModifiedText));
        NotifyWorkspaceStateCommands();
    }

    private void ApplyRecentWorkspaceMutation(RecentWorkspaceMutationResult result)
    {
        ApplyRecentWorkspaceSnapshot(result.Snapshot);
        if (result.Diagnostic is not null)
            SetWorkspaceStateError(result.Diagnostic.Message);
        else if (result.Status is RecentWorkspaceMutationStatus.Applied or RecentWorkspaceMutationStatus.Unchanged)
            SetWorkspaceStateError(string.Empty);
    }

    private void ApplyRecoveryList(RecoveryListResult result)
    {
        var selectedId = _selectedRecovery?.RecoveryId;
        var restoredSelection = selectedId is Guid id
            ? result.Recoveries.FirstOrDefault(recovery => recovery.RecoveryId == id)
            : null;
        _recoveryCandidates = result.Recoveries;
        _recoveryDiagnostics = result.Diagnostics;
        OnPropertyChanged(nameof(RecoveryCandidates));
        OnPropertyChanged(nameof(RecoveryDiagnostics));
        // 복구 목록도 ItemsSource/TwoWay 바인딩의 재진입 중 선택을 유지한다.
        _selectedRecovery = restoredSelection;
        OnPropertyChanged(nameof(SelectedRecovery));
        NotifyWorkspaceStateCommands();
    }

    private void EnterWorkspaceStateBusy()
    {
        _workspaceStateBusyCount++;
        SetWorkspaceStateBusy(_workspaceStateBusyCount > 0);
    }

    private void LeaveWorkspaceStateBusy()
    {
        _workspaceStateBusyCount = Math.Max(0, _workspaceStateBusyCount - 1);
        SetWorkspaceStateBusy(_workspaceStateBusyCount > 0);
    }

    private void SetWorkspaceStateBusy(bool value)
    {
        if (_isWorkspaceStateBusy == value)
            return;
        _isWorkspaceStateBusy = value;
        OnPropertyChanged(nameof(IsWorkspaceStateBusy));
        NotifyWorkspaceStateCommands();
    }

    private void SetWorkspaceStateLoading(bool value)
    {
        if (_isWorkspaceStateLoading == value)
            return;
        _isWorkspaceStateLoading = value;
        OnPropertyChanged(nameof(IsWorkspaceStateLoading));
    }

    private void SetWorkspaceStateReady(bool value)
    {
        if (_isWorkspaceStateReady == value)
            return;
        _isWorkspaceStateReady = value;
        OnPropertyChanged(nameof(IsWorkspaceStateReady));
    }

    private void SetWorkspaceStateError(string value)
    {
        if (_workspaceStateError == value)
            return;
        _workspaceStateError = value;
        OnPropertyChanged(nameof(WorkspaceStateError));
    }

    private void SetRecoveryOriginalPath(string? value)
    {
        if (_recoveryOriginalPath == value)
            return;
        _recoveryOriginalPath = value;
    }

    private static async Task<RecoveryCleanupResult> DeleteRecoveryIfValidAsync(
        IWorkspaceStateService service, Guid recoveryId, CancellationToken cancellationToken)
    {
        var read = await service.ReadRecoveryAsync(recoveryId, cancellationToken).ConfigureAwait(false);
        if (read.Status != RecoveryReadStatus.Found || read.Recovery is null ||
            read.Recovery.RecoveryId != recoveryId)
            return new RecoveryCleanupResult(false, read.Diagnostic);

        var removed = await service.DeleteRecoveryAsync(recoveryId, cancellationToken).ConfigureAwait(false);
        return new RecoveryCleanupResult(removed, null);
    }

    private void NotifyWorkspaceStateCommands()
    {
        _refreshWorkspaceStateCommand?.NotifyCanExecuteChanged();
        _openRecentWorkspaceCommand?.NotifyCanExecuteChanged();
        _pinRecentWorkspaceCommand?.NotifyCanExecuteChanged();
        _unpinRecentWorkspaceCommand?.NotifyCanExecuteChanged();
        _removeRecentWorkspaceCommand?.NotifyCanExecuteChanged();
        _relinkRecentWorkspaceCommand?.NotifyCanExecuteChanged();
        _restoreRecoveryCommand?.NotifyCanExecuteChanged();
        _deleteRecoveryCommand?.NotifyCanExecuteChanged();
        _confirmDeleteRecoveryCommand?.NotifyCanExecuteChanged();
        _cancelDeleteRecoveryCommand?.NotifyCanExecuteChanged();
    }

    private static string BuildRecentWorkspacePreview(RecentWorkspaceEntry entry)
    {
        var status = entry.FileStatus switch
        {
            RecentWorkspaceFileStatus.Missing => "파일을 찾을 수 없음",
            RecentWorkspaceFileStatus.MetadataUnavailable => "파일 정보를 확인할 수 없음",
            _ => "사용 가능"
        };
        var modified = entry.FileModifiedAtUtc?.ToLocalTime().ToString("g") ?? "수정 시각 없음";
        return $"{entry.DisplayName}{Environment.NewLine}{entry.FullPath}{Environment.NewLine}{status} · 수정 {modified}";
    }

    private async Task<RecentWorkspaceMutationStatus> RecordSuccessfulRecentUseBestEffortAsync(
        string path, string displayName)
    {
        var service = _workspaceState;
        if (service is null)
            return RecentWorkspaceMutationStatus.NotFound;

        var (snapshotGeneration, lifecycleGeneration) = BeginRecentSnapshotRequest();
        try
        {
            var mutation = await EnqueueWorkspaceStateOperationAsync(
                () => service.RecordSuccessfulUseAsync(path, displayName, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(true);
            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
                ApplyRecentWorkspaceMutation(mutation);
            return mutation.Status;
        }
        catch (Exception exception)
        {
            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
                SetWorkspaceStateError($"최근 작업 기록 실패: {exception.Message}");
            return RecentWorkspaceMutationStatus.WriteBlocked;
        }
    }

    private void QueueSuccessfulOpenUse(string path, string displayName, string? removeOldPath)
    {
        _ = CompleteSuccessfulOpenUseAsync(path, displayName, removeOldPath);
    }

    private async Task CompleteSuccessfulOpenUseAsync(string path, string displayName, string? removeOldPath)
    {
        await RecordSuccessfulOpenUseBestEffortAsync(path, displayName, removeOldPath).ConfigureAwait(true);
    }

    private async Task<RecentWorkspaceMutationStatus> RecordSuccessfulOpenUseBestEffortAsync(
        string path, string displayName, string? removeOldPath)
    {
        var service = _workspaceState;
        if (service is null)
            return RecentWorkspaceMutationStatus.NotFound;

        var (snapshotGeneration, lifecycleGeneration) = BeginRecentSnapshotRequest();
        try
        {
            var result = await EnqueueWorkspaceStateOperationAsync(async () =>
            {
                var recorded = await service.RecordSuccessfulUseAsync(path, displayName, DateTimeOffset.UtcNow)
                    .ConfigureAwait(false);
                var latest = recorded.Snapshot;
                var status = recorded.Status;
                var diagnostic = recorded.Diagnostic;
                if (removeOldPath is not null && !PathsEqual(path, removeOldPath) &&
                    recorded.Status is RecentWorkspaceMutationStatus.Applied or RecentWorkspaceMutationStatus.Unchanged)
                {
                    var removed = await service.RemoveRecentAsync(removeOldPath).ConfigureAwait(false);
                    latest = removed.Snapshot;
                    if (removed.Status == RecentWorkspaceMutationStatus.WriteBlocked)
                    {
                        status = removed.Status;
                        diagnostic = removed.Diagnostic;
                    }
                }
                return (Status: status, Diagnostic: diagnostic, Snapshot: latest);
            }, CancellationToken.None).ConfigureAwait(true);

            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
            {
                ApplyRecentWorkspaceSnapshot(result.Snapshot);
                SetWorkspaceStateError(result.Diagnostic?.Message ?? string.Empty);
            }
            return result.Status;
        }
        catch (Exception exception)
        {
            if (IsCurrentRecentSnapshotRequest(snapshotGeneration, lifecycleGeneration))
                SetWorkspaceStateError($"최근 작업 기록 실패: {exception.Message}");
            return RecentWorkspaceMutationStatus.WriteBlocked;
        }
    }

    private (long SnapshotGeneration, long LifecycleGeneration) BeginRecentSnapshotRequest()
    {
        lock (_recoveryTimerLock)
            return (++_recentSnapshotGeneration, _workspaceStateLifecycleGeneration);
    }

    private bool IsCurrentRecentSnapshotRequest(long snapshotGeneration, long lifecycleGeneration) =>
        snapshotGeneration == Volatile.Read(ref _recentSnapshotGeneration) &&
        lifecycleGeneration == Volatile.Read(ref _workspaceStateLifecycleGeneration);

    private async Task DeleteRecoveryAfterSuccessfulSaveAsync(EditingSession session,
        Guid? preserveRecoveryId = null)
    {
        var service = _workspaceState;
        if (service is null || !ReferenceEquals(session, Session) || session.IsDirty ||
            _activeRecoveryId is not Guid recoveryId || recoveryId == preserveRecoveryId || _recoveryStopped)
            return;

        long recoveryGeneration;
        long lifecycleGeneration;
        long listGeneration;
        lock (_recoveryTimerLock)
        {
            if (_recoveryStopped || _workspaceStateDraining)
                return;
            recoveryGeneration = _recoveryGeneration;
            lifecycleGeneration = _workspaceStateLifecycleGeneration;
            listGeneration = ++_recoveryListSnapshotGeneration;
        }
        try
        {
            var queuedCleanup = TryEnqueueRecoveryOperation(recoveryGeneration, async () =>
            {
                var cleanup = await DeleteRecoveryIfValidAsync(service, recoveryId, CancellationToken.None)
                    .ConfigureAwait(false);
                return (Cleanup: cleanup,
                    List: await service.ListRecoveriesAsync().ConfigureAwait(false));
            }, CancellationToken.None);
            if (queuedCleanup is null)
                return;
            var cleanupResult = await queuedCleanup.ConfigureAwait(true);
            if (!IsRecoveryStopped && lifecycleGeneration == _workspaceStateLifecycleGeneration &&
                recoveryGeneration == _recoveryGeneration && ReferenceEquals(session, Session) &&
                _activeRecoveryId == recoveryId)
            {
                if (listGeneration == _recoveryListSnapshotGeneration)
                    ApplyRecoveryList(cleanupResult.List);
                if (cleanupResult.Cleanup.Diagnostic is not null)
                    SetWorkspaceStateError(cleanupResult.Cleanup.Diagnostic.Message);
            }
        }
        catch (Exception exception)
        {
            if (!IsRecoveryStopped && lifecycleGeneration == _workspaceStateLifecycleGeneration &&
                recoveryGeneration == _recoveryGeneration && ReferenceEquals(session, Session))
                SetWorkspaceStateError($"복구본 정리 실패: {exception.Message}");
        }
    }

    private void RotateRecoveryIdentity(Guid? preserveRecoveryId = null)
    {
        var service = _workspaceState;
        Guid? oldRecoveryId;
        CancellationTokenSource oldCancellation;
        long generation;
        long listGeneration = 0;
        Task<(RecoveryCleanupResult Cleanup, RecoveryListResult List)>? cleanup = null;
        lock (_recoveryTimerLock)
        {
            oldRecoveryId = _activeRecoveryId;
            oldCancellation = _sessionRecoveryCancellation;
            oldCancellation.Cancel();
            _sessionRecoveryCancellation = new CancellationTokenSource();
            generation = ++_recoveryGeneration;
            _activeRecoveryId = service is null ? null : Guid.NewGuid();
            _recoveryOriginalPath = _currentPath;

            if (!_recoveryStopped && service is not null && oldRecoveryId is Guid recoveryId &&
                recoveryId != preserveRecoveryId)
            {
                listGeneration = ++_recoveryListSnapshotGeneration;
                cleanup = EnqueueWorkspaceStateOperationAsync(async () =>
                {
                    var result = await DeleteRecoveryIfValidAsync(service, recoveryId, CancellationToken.None)
                        .ConfigureAwait(false);
                    return (Cleanup: result,
                        List: await service.ListRecoveriesAsync().ConfigureAwait(false));
                }, CancellationToken.None);
            }
        }
        OnPropertyChanged(nameof(ActiveRecoveryId));
        OnPropertyChanged(nameof(IsRecoverySaveRequired));

        if (cleanup is null)
        {
            oldCancellation.Dispose();
            return;
        }
        _ = CompleteRetiredRecoveryCleanupAsync(cleanup, oldCancellation, generation, listGeneration);
    }

    private async Task CompleteRetiredRecoveryCleanupAsync(
        Task<(RecoveryCleanupResult Cleanup, RecoveryListResult List)> cleanup,
        CancellationTokenSource retiredCancellation, long generation,
        long listGeneration)
    {
        try
        {
            var result = await cleanup.ConfigureAwait(true);
            if (generation == _recoveryGeneration && listGeneration == _recoveryListSnapshotGeneration)
            {
                ApplyRecoveryList(result.List);
                if (result.Cleanup.Diagnostic is not null)
                    SetWorkspaceStateError(result.Cleanup.Diagnostic.Message);
            }
        }
        catch (Exception exception)
        {
            if (generation == _recoveryGeneration && listGeneration == _recoveryListSnapshotGeneration)
                SetWorkspaceStateError($"이전 복구본 정리 실패: {exception.Message}");
        }
        finally
        {
            retiredCancellation.Dispose();
        }
    }

    private void ApplyRecoveredWorkspace(WorkspaceRecovery recovery)
    {
        ReplaceSession(EditingSession.CreateRecovered(recovery.Snapshot.Workspace), recovery.RecoveryId);
        SetCurrentPath(null);
        SetRecoveryOriginalPath(recovery.OriginalPath);
        SetStatus("변경됨");
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void QueueRecoveryDeletionForCurrentSession()
    {
        if (_workspaceState is null || _activeRecoveryId is null)
            return;
        RotateRecoveryIdentity();
    }

    public async Task SaveToPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_pendingWorkspaceAction is not null || _isPendingWorkspaceActionBusy ||
            _isCloseDrainPending || _isWorkspaceOpenPending || _closeApproval is not null)
            return;

        var session = Session;
        var navigationGeneration = _navigationGeneration;
        var saveRequestGeneration = ++_saveRequestGeneration;
        _statusSavingSession = session;
        _statusSavingNavigationGeneration = navigationGeneration;
        _statusSavingRequestGeneration = saveRequestGeneration;
        SetStatus("저장 중");
        QueuedSaveOperation? saveOperation = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var snapshot = session.CaptureSnapshot();
            saveOperation = EnqueueSaveOperation(session, snapshot,
                token => _workspaceFiles.SaveAsync(fullPath, snapshot, token), cancellationToken);
            await saveOperation.Value.Operation.WaitAsync(cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
                return;
            if (!IsCurrentSaveRequest(saveRequestGeneration, navigationGeneration, session))
                return;
            AcknowledgeSaveTicketsThrough(saveOperation.Value.Ticket);
            ClearRegularSaveStatus(saveRequestGeneration);
            session.MarkSaved(snapshot);
            SetCurrentPath(fullPath);
            SetStatus(session.IsDirty ? "변경됨" : "저장됨");
            await RecordSuccessfulRecentUseBestEffortAsync(fullPath, snapshot.Workspace.Name)
                .ConfigureAwait(true);
            if (!session.IsDirty && ReferenceEquals(session, Session))
                await DeleteRecoveryAfterSuccessfulSaveAsync(session).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            RemoveSaveTicketIfOperationFailed(saveOperation);
            if (IsCurrentSaveRequest(saveRequestGeneration, navigationGeneration, session))
            {
                ClearRegularSaveStatus(saveRequestGeneration);
                SetStatus(GetWorkspaceStatus(session));
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            RemoveSaveTicketIfOperationFailed(saveOperation);
            if (IsCurrentSaveRequest(saveRequestGeneration, navigationGeneration, session))
            {
                ClearRegularSaveStatus(saveRequestGeneration);
                SetStatus($"저장 실패: {exception.Message}");
            }
        }
        finally
        {
            if (IsCurrentSaveRequest(saveRequestGeneration, navigationGeneration, session) &&
                ClearRegularSaveStatus(saveRequestGeneration) && StatusText == "저장 중")
                SetStatus(GetWorkspaceStatus(session));
        }
    }

    public WorkspaceActionRequestResult RequestNewWorkspace()
    {
        if (!CanStartWorkspaceAction())
            return WorkspaceActionRequestResult.Rejected;

        var generation = BeginNavigationRequest();
        var session = Session;
        if (NeedsWorkspaceConfirmation(session))
        {
            SetPendingWorkspaceAction(new PendingWorkspaceActionState(
                WorkspaceActionKind.NewWorkspace, session, generation));
            return WorkspaceActionRequestResult.ConfirmationRequired;
        }

        ReplaceSession(EditingSession.CreateBlank());
        SetCurrentPath(null);
        SetStatus("변경 없음");
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
        return WorkspaceActionRequestResult.Completed;
    }

    public async Task<WorkspaceActionRequestResult> OpenFromPathAsync(
        string path, CancellationToken cancellationToken = default)
    {
        if (!CanStartWorkspaceAction())
            return WorkspaceActionRequestResult.Rejected;

        var generation = BeginNavigationRequest();
        var originalSession = Session;
        return await OpenFromPathForRequestAsync(path, cancellationToken, generation, originalSession)
            .ConfigureAwait(true);
    }

    private async Task<WorkspaceActionRequestResult> OpenFromPathForRequestAsync(
        string path, CancellationToken cancellationToken, long generation, EditingSession originalSession,
        string? removeRecentPath = null)
    {
        if (!IsCurrentNavigation(generation, originalSession))
            return WorkspaceActionRequestResult.Stale;

        SetWorkspaceOpenPending(true, generation);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
            var workspace = await EnqueueFileOperationAsync(
                () => _workspaceFiles.OpenAsync(fullPath, cancellationToken), cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
                return IsCurrentNavigation(generation, originalSession)
                    ? WorkspaceActionRequestResult.Canceled
                    : WorkspaceActionRequestResult.Stale;

            if (!IsCurrentNavigation(generation, originalSession))
                return WorkspaceActionRequestResult.Stale;

            WorkspaceValidator.Validate(workspace);
            if (NeedsWorkspaceConfirmation(originalSession))
            {
                var pendingAction = new PendingWorkspaceActionState(
                    WorkspaceActionKind.OpenWorkspace, originalSession, generation, fullPath, workspace);
                pendingAction.RelinkOldPath = removeRecentPath;
                SetPendingWorkspaceAction(pendingAction);
                return WorkspaceActionRequestResult.ConfirmationRequired;
            }

            ReplaceSession(new EditingSession(workspace));
            SetCurrentPath(fullPath);
            SetStatus("변경 없음");
            EditorFocusRequested?.Invoke(this, EventArgs.Empty);
            await RecordSuccessfulOpenUseBestEffortAsync(fullPath, workspace.Name, removeRecentPath)
                .ConfigureAwait(true);
            return WorkspaceActionRequestResult.Completed;
        }
        catch (OperationCanceledException)
        {
            return IsCurrentNavigation(generation, originalSession)
                ? WorkspaceActionRequestResult.Canceled
                : WorkspaceActionRequestResult.Stale;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            if (!IsCurrentNavigation(generation, originalSession))
                return WorkspaceActionRequestResult.Stale;
            SetStatus($"열기 실패: {exception.Message}");
            return WorkspaceActionRequestResult.Failed;
        }
        finally
        {
            ClearWorkspaceOpenPending(generation);
        }
    }

    public WorkspaceActionRequestResult RequestClose()
    {
        if (!CanStartWorkspaceAction())
            return WorkspaceActionRequestResult.Rejected;

        var generation = BeginNavigationRequest();
        var session = Session;
        if (NeedsWorkspaceConfirmation(session))
        {
            SetPendingWorkspaceAction(new PendingWorkspaceActionState(
                WorkspaceActionKind.CloseWindow, session, generation));
            return WorkspaceActionRequestResult.ConfirmationRequired;
        }

        var queueTail = CaptureFileOperationQueueTail();
        if (queueTail.IsCompleted)
        {
            IssueCloseApproval(session, generation);
        }
        else
        {
            _isCloseDrainPending = true;
            var operationGeneration = ++_pendingResolutionGeneration;
            SetPendingWorkspaceActionBusy(true);
            SetStatus("저장 작업을 마친 뒤 종료합니다.");
            _ = IssueCloseApprovalAfterFileQueueAsync(
                queueTail, session, session.Revision, generation, operationGeneration);
        }
        return WorkspaceActionRequestResult.Completed;
    }

    public bool TryConsumeCloseApproval(Guid approvalToken)
    {
        if (_closeApproval is not CloseApproval approval || approval.Token != approvalToken)
            return false;

        _closeApproval = null;
        var isValid = ReferenceEquals(approval.Session, Session) &&
            approval.Revision == Session.Revision &&
            approval.NavigationGeneration == _navigationGeneration &&
            _pendingWorkspaceAction is null &&
            !_isPendingWorkspaceActionBusy && !_isCloseDrainPending;
        if (isValid)
            RetireSaveTicketsForSession(approval.Session);
        return isValid;
    }

    public WorkspaceActionResolutionResult DiscardPendingWorkspaceAction()
    {
        var pending = _pendingWorkspaceAction;
        if (pending is null)
            return WorkspaceActionResolutionResult.Rejected;
        if (_isPendingWorkspaceActionBusy)
            return WorkspaceActionResolutionResult.Rejected;
        if (!IsPendingActionCurrent(pending))
        {
            BeginNavigationRequest();
            ClearPendingWorkspaceAction();
            return WorkspaceActionResolutionResult.Stale;
        }

        var queueTail = CaptureFileOperationQueueTail();
        if (!queueTail.IsCompleted)
        {
            var operationGeneration = ++_pendingResolutionGeneration;
            var navigationGeneration = _navigationGeneration;
            var session = Session;
            var revision = session.Revision;
            SetPendingWorkspaceActionBusy(true);
            _ = CompleteDiscardAfterFileQueueAsync(
                queueTail, pending, session, revision, navigationGeneration, operationGeneration);
            return WorkspaceActionResolutionResult.Pending;
        }

        var generation = BeginNavigationRequest();
        ClearPendingWorkspaceAction();
        return CompletePendingWorkspaceAction(pending, generation);
    }

    public WorkspaceActionResolutionResult CancelPendingWorkspaceAction()
    {
        if (_pendingWorkspaceAction is null)
            return WorkspaceActionResolutionResult.Rejected;

        BeginNavigationRequest();
        _pendingResolutionGeneration++;
        _pendingSaveCancellation?.Cancel();
        _pendingSaveCancellation = null;
        ClearPendingWorkspaceAction();
        SetPendingWorkspaceActionBusy(false);
        SetStatus(GetWorkspaceStatus(Session));
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
        return WorkspaceActionResolutionResult.Canceled;
    }

    public async Task<WorkspaceActionResolutionResult> SavePendingWorkspaceActionToPathAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var pending = _pendingWorkspaceAction;
        if (pending is null || _isPendingWorkspaceActionBusy)
            return WorkspaceActionResolutionResult.Rejected;
        if (!IsPendingActionCurrent(pending))
        {
            BeginNavigationRequest();
            ClearPendingWorkspaceAction();
            return WorkspaceActionResolutionResult.Stale;
        }

        var session = Session;
        var navigationGeneration = _navigationGeneration;
        var snapshot = session.CaptureSnapshot();
        var operationGeneration = ++_pendingResolutionGeneration;
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operationToken = operationCancellation.Token;
        _pendingSaveCancellation = operationCancellation;
        SetPendingWorkspaceActionBusy(true);
        SetStatus("저장 중");
        QueuedSaveOperation? saveOperation = null;
        try
        {
            SetPendingWorkspaceActionError(null);
            var fullPath = Path.GetFullPath(path);
            saveOperation = EnqueueSaveOperation(session, snapshot,
                token => _workspaceFiles.SaveAsync(fullPath, snapshot, token), operationToken);
            await saveOperation.Value.Operation.WaitAsync(operationToken).ConfigureAwait(true);

            if (operationToken.IsCancellationRequested)
            {
                var isCurrent = IsPendingSaveCurrent(pending, session, navigationGeneration, operationGeneration);
                if (isCurrent)
                    SetStatus(GetWorkspaceStatus(session));
                return isCurrent
                    ? WorkspaceActionResolutionResult.Canceled
                    : WorkspaceActionResolutionResult.Stale;
            }

            if (!IsPendingSaveCurrent(pending, session, navigationGeneration, operationGeneration))
                return WorkspaceActionResolutionResult.Stale;

            AcknowledgeSaveTicketsThrough(saveOperation.Value.Ticket);
            session.MarkSaved(snapshot);
            SetCurrentPath(fullPath);
            SetStatus(session.IsDirty ? "변경됨" : "저장됨");
            await RecordSuccessfulRecentUseBestEffortAsync(fullPath, snapshot.Workspace.Name)
                .ConfigureAwait(true);
            if (!session.IsDirty && ReferenceEquals(session, Session))
                await DeleteRecoveryAfterSuccessfulSaveAsync(session, pending.Recovery?.RecoveryId)
                    .ConfigureAwait(true);
            if (!IsPendingSaveCurrent(pending, session, navigationGeneration, operationGeneration))
                return WorkspaceActionResolutionResult.Stale;

            if (pending.Kind == WorkspaceActionKind.OpenWorkspace &&
                pending.OpenPath is not null && PathsEqual(fullPath, pending.OpenPath))
            {
                pending.OpenWorkspace = snapshot.Workspace;
            }

            if (session.Revision != snapshot.Revision || session.IsDirty)
                return WorkspaceActionResolutionResult.Pending;

            var generation = BeginNavigationRequest();
            ClearPendingWorkspaceAction();
            SetPendingWorkspaceActionBusy(false);
            return CompletePendingWorkspaceAction(pending, generation);
        }
        catch (OperationCanceledException)
        {
            RemoveSaveTicketIfOperationFailed(saveOperation);
            var isCurrent = IsPendingSaveCurrent(pending, session, navigationGeneration, operationGeneration);
            if (isCurrent)
                SetStatus(GetWorkspaceStatus(session));
            return isCurrent ? WorkspaceActionResolutionResult.Canceled : WorkspaceActionResolutionResult.Stale;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            RemoveSaveTicketIfOperationFailed(saveOperation);
            if (!IsPendingSaveCurrent(pending, session, navigationGeneration, operationGeneration))
                return WorkspaceActionResolutionResult.Stale;
            SetPendingWorkspaceActionError(exception.Message);
            SetStatus($"저장 실패: {exception.Message}");
            return WorkspaceActionResolutionResult.Failed;
        }
        finally
        {
            if (_pendingResolutionGeneration == operationGeneration)
            {
                _pendingSaveCancellation = null;
                SetPendingWorkspaceActionBusy(false);
            }
        }
    }

    public async Task ExportSvgToPathAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspace = Session.Workspace;
            var layout = MathLayoutEngine.Layout(workspace, _textMetrics,
                new MathLayoutOptions(FontSize: workspace.Style.FontSize));
            await _svgExporter.ExportAsync(path, layout, cancellationToken).ConfigureAwait(true);
            SetStatus("SVG 내보내기 완료");
        }
        catch (MathExportException exception)
        {
            SetStatus($"SVG 출력 실패: {exception.Message}");
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            SetStatus($"SVG 출력 실패: {exception.Message}");
        }
    }

    internal void AttachWindow(Window window)
    {
        _window = window;
        _saveOutputPreviewSvgCommand.NotifyCanExecuteChanged();
    }

    internal void DetachWindow(Window window)
    {
        if (ReferenceEquals(_window, window))
        {
            _window = null;
            _saveOutputPreviewSvgCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SaveFromCommandAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentPath))
        {
            await SaveToPathAsync(CurrentPath).ConfigureAwait(true);
            return;
        }

        if (_window is null)
        {
            SetStatus("저장 실패: 파일 대화상자를 사용할 수 없습니다.");
            return;
        }

        var session = Session;
        var navigationGeneration = _navigationGeneration;

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "수식 원본 저장",
            SuggestedFileName = MakeSuggestedFileName(WorkspaceName, WorkspaceFileService.Extension),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("AetherMath 수식") { Patterns = new[] { "*.aethermath" } }
            }
        });
        if (file is null)
            return;

        if (!IsCurrentNavigation(navigationGeneration, session))
            return;

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            if (IsCurrentNavigation(navigationGeneration, session))
                SetStatus("저장 실패: 로컬 파일 시스템 경로를 얻을 수 없습니다.");
            return;
        }
        await SaveToPathAsync(path).ConfigureAwait(true);
    }

    private async Task SavePendingWorkspaceActionFromCommandAsync()
    {
        var pending = _pendingWorkspaceAction;
        if (pending is null || _isPendingWorkspaceActionBusy)
            return;

        if (!string.IsNullOrWhiteSpace(CurrentPath))
        {
            await SavePendingWorkspaceActionToPathAsync(CurrentPath).ConfigureAwait(true);
            return;
        }

        if (_window is null)
        {
            SetPendingWorkspaceActionError("저장 실패: 파일 대화상자를 사용할 수 없습니다.");
            return;
        }

        var session = Session;
        var navigationGeneration = _navigationGeneration;

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "수식 원본 저장",
            SuggestedFileName = MakeSuggestedFileName(WorkspaceName, WorkspaceFileService.Extension),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("AetherMath 수식") { Patterns = new[] { "*.aethermath" } }
            }
        });
        if (file is null)
            return;

        if (!IsPendingActionCurrent(pending) || !ReferenceEquals(session, Session) ||
            navigationGeneration != _navigationGeneration)
            return;

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            if (IsPendingActionCurrent(pending))
                SetPendingWorkspaceActionError("저장 실패: 로컬 파일 시스템 경로를 얻을 수 없습니다.");
            return;
        }

        await SavePendingWorkspaceActionToPathAsync(path).ConfigureAwait(true);
    }

    private async Task OpenFromCommandAsync()
    {
        if (!CanStartWorkspaceAction())
            return;
        var navigationGeneration = BeginNavigationRequest();
        var session = Session;
        if (_window is null)
        {
            if (IsCurrentNavigation(navigationGeneration, session))
                SetStatus("열기 실패: 파일 대화상자를 사용할 수 없습니다.");
            return;
        }

        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "수식 파일 열기",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("AetherMath 수식") { Patterns = new[] { "*.aethermath" } }
            }
        });
        if (!IsCurrentNavigation(navigationGeneration, session))
            return;
        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (path is null)
        {
            if (IsCurrentNavigation(navigationGeneration, session))
                SetStatus("열기 실패: 로컬 파일 시스템 경로를 얻을 수 없습니다.");
            return;
        }
        await OpenFromPathForRequestAsync(path, CancellationToken.None, navigationGeneration, session)
            .ConfigureAwait(true);
    }

    private WorkspaceActionResolutionResult CompletePendingWorkspaceAction(
        PendingWorkspaceActionState pending, long navigationGeneration)
    {
        if (!IsCurrentNavigation(navigationGeneration, pending.OriginalSession))
            return WorkspaceActionResolutionResult.Stale;

        switch (pending.Kind)
        {
            case WorkspaceActionKind.NewWorkspace:
                ReplaceSession(EditingSession.CreateBlank());
                SetCurrentPath(null);
                SetStatus("변경 없음");
                EditorFocusRequested?.Invoke(this, EventArgs.Empty);
                return WorkspaceActionResolutionResult.Completed;
            case WorkspaceActionKind.OpenWorkspace:
                if (pending.OpenWorkspace is null || pending.OpenPath is null)
                    return WorkspaceActionResolutionResult.Stale;
                ReplaceSession(new EditingSession(pending.OpenWorkspace));
                SetCurrentPath(pending.OpenPath);
                SetStatus("변경 없음");
                EditorFocusRequested?.Invoke(this, EventArgs.Empty);
                QueueSuccessfulOpenUse(pending.OpenPath, pending.OpenWorkspace.Name, pending.RelinkOldPath);
                return WorkspaceActionResolutionResult.Completed;
            case WorkspaceActionKind.RestoreRecovery:
                if (pending.Recovery is null)
                    return WorkspaceActionResolutionResult.Stale;
                ApplyRecoveredWorkspace(pending.Recovery);
                return WorkspaceActionResolutionResult.Completed;
            case WorkspaceActionKind.CloseWindow:
                QueueRecoveryDeletionForCurrentSession();
                IssueCloseApproval(pending.OriginalSession, navigationGeneration);
                return WorkspaceActionResolutionResult.Completed;
            default:
                return WorkspaceActionResolutionResult.Stale;
        }
    }

    private bool NeedsWorkspaceConfirmation(EditingSession session) =>
        session.IsDirty || HasConflictingSaveTicket(session);

    private bool HasConflictingSaveTicket(EditingSession session)
    {
        var currentStateId = session.CaptureSnapshot().StateId;
        lock (_saveTicketLock)
            return _saveTickets.Any(ticket => !ticket.IsRetired &&
                ReferenceEquals(ticket.Session, session) && ticket.StateId != currentStateId);
    }

    private string GetWorkspaceStatus(EditingSession session)
    {
        if (session.IsDirty)
            return "변경됨";
        return HasConflictingSaveTicket(session) ? "저장 확인 필요" : "변경 없음";
    }

    private bool CanStartWorkspaceAction() =>
        _pendingWorkspaceAction is null && !_isPendingWorkspaceActionBusy && !_isCloseDrainPending;

    private bool CanSaveCurrentWorkspace() =>
        _pendingWorkspaceAction is null && !_isPendingWorkspaceActionBusy && !_isCloseDrainPending &&
        !_isWorkspaceOpenPending && _closeApproval is null;

    private long BeginNavigationRequest()
    {
        if (_statusSavingSession is not null)
        {
            ClearRegularSaveStatus(_statusSavingRequestGeneration);
            if (StatusText == "저장 중")
                SetStatus(GetWorkspaceStatus(Session));
        }
        _navigationGeneration++;
        if (_closeApproval is not null)
        {
            _closeApproval = null;
            _saveCommand.NotifyCanExecuteChanged();
        }
        SetWorkspaceOpenPending(false);
        return _navigationGeneration;
    }

    private bool IsCurrentNavigation(long generation, EditingSession originalSession) =>
        generation == _navigationGeneration && ReferenceEquals(originalSession, Session);

    private bool IsCurrentSaveRequest(long saveRequestGeneration, long navigationGeneration,
        EditingSession originalSession) =>
        saveRequestGeneration == _saveRequestGeneration &&
        IsCurrentNavigation(navigationGeneration, originalSession) &&
        _pendingWorkspaceAction is null && !_isPendingWorkspaceActionBusy && !_isCloseDrainPending;

    private bool IsCurrentRegularSaveStatus() =>
        _statusSavingSession is not null &&
        _statusSavingRequestGeneration == _saveRequestGeneration &&
        _statusSavingNavigationGeneration == _navigationGeneration &&
        ReferenceEquals(_statusSavingSession, Session);

    private bool ClearRegularSaveStatus(long saveRequestGeneration)
    {
        if (_statusSavingRequestGeneration != saveRequestGeneration)
            return false;
        _statusSavingSession = null;
        _statusSavingNavigationGeneration = 0;
        _statusSavingRequestGeneration = 0;
        return true;
    }

    private bool IsPendingActionCurrent(PendingWorkspaceActionState pending) =>
        ReferenceEquals(_pendingWorkspaceAction, pending) &&
        ReferenceEquals(pending.OriginalSession, Session) &&
        pending.NavigationGeneration == _navigationGeneration;

    private bool IsPendingSaveCurrent(PendingWorkspaceActionState pending, EditingSession session,
        long navigationGeneration, long operationGeneration) =>
        IsPendingActionCurrent(pending) &&
        ReferenceEquals(session, Session) &&
        navigationGeneration == _navigationGeneration &&
        operationGeneration == _pendingResolutionGeneration &&
        _isPendingWorkspaceActionBusy;

    private void IssueCloseApproval(EditingSession session, long navigationGeneration)
    {
        if (!ReferenceEquals(session, Session) || navigationGeneration != _navigationGeneration ||
            _pendingWorkspaceAction is not null || _isPendingWorkspaceActionBusy ||
            _isCloseDrainPending || _isWorkspaceOpenPending)
            return;

        var approval = new CloseApproval(Guid.NewGuid(), session, session.Revision, navigationGeneration);
        _closeApproval = approval;
        _saveCommand.NotifyCanExecuteChanged();
        CloseRequested?.Invoke(this, new CloseRequestedEventArgs(approval.Token));
    }

    private async Task IssueCloseApprovalAfterFileQueueAsync(Task queueTail, EditingSession session,
        long revision, long navigationGeneration, long operationGeneration)
    {
        await queueTail.ConfigureAwait(true);
        if (!_isCloseDrainPending || operationGeneration != _pendingResolutionGeneration)
            return;

        _isCloseDrainPending = false;
        SetPendingWorkspaceActionBusy(false);
        if (!ReferenceEquals(session, Session) ||
            navigationGeneration != _navigationGeneration || _pendingWorkspaceAction is not null)
        {
            if (ReferenceEquals(session, Session) && _pendingWorkspaceAction is null)
                SetStatus(GetWorkspaceStatus(session));
            return;
        }

        if (revision != session.Revision)
        {
            SetStatus(GetWorkspaceStatus(session));
            return;
        }

        if (NeedsWorkspaceConfirmation(session))
        {
            SetPendingWorkspaceAction(new PendingWorkspaceActionState(
                WorkspaceActionKind.CloseWindow, session, navigationGeneration));
            return;
        }

        IssueCloseApproval(session, navigationGeneration);
    }

    private async Task CompleteDiscardAfterFileQueueAsync(Task queueTail,
        PendingWorkspaceActionState pending, EditingSession session, long revision,
        long navigationGeneration, long operationGeneration)
    {
        await queueTail.ConfigureAwait(true);
        if (operationGeneration != _pendingResolutionGeneration)
            return;

        if (!ReferenceEquals(_pendingWorkspaceAction, pending) ||
            !ReferenceEquals(session, Session) || navigationGeneration != _navigationGeneration ||
            revision != session.Revision)
        {
            if (ReferenceEquals(_pendingWorkspaceAction, pending) &&
                ReferenceEquals(session, Session) && navigationGeneration == _navigationGeneration)
            {
                SetPendingWorkspaceActionBusy(false);
                SetStatus(GetWorkspaceStatus(session));
            }
            return;
        }

        var generation = BeginNavigationRequest();
        ClearPendingWorkspaceAction();
        SetPendingWorkspaceActionBusy(false);
        CompletePendingWorkspaceAction(pending, generation);
    }

    private Task<T> EnqueueFileOperationAsync<T>(Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_fileOperationQueueLock)
            return EnqueueFileOperationNoLock(operation, cancellationToken);
    }

    private Task<T> EnqueueFileOperationNoLock<T>(Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var preceding = _fileOperationQueueTail;
        var queued = Task.Run(async () =>
        {
            await preceding.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await operation().ConfigureAwait(false);
        }, CancellationToken.None);
        _fileOperationQueueTail = ObserveFileOperationAsync(queued);
        return queued;
    }

    private QueuedSaveOperation EnqueueSaveOperation(EditingSession session,
        WorkspaceSnapshot snapshot, Func<CancellationToken, Task> save,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(save);
        SaveOperationTicket ticket;
        Task<bool> operation;
        lock (_fileOperationQueueLock)
        {
            ticket = new SaveOperationTicket(++_saveTicketSequence, session, snapshot.StateId);
            lock (_saveTicketLock)
                _saveTickets.Add(ticket);
            try
            {
                var preceding = _fileOperationQueueTail;
                operation = Task.Run(async () =>
                {
                    try
                    {
                        await preceding.ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        await save(cancellationToken).ConfigureAwait(false);
                        CompleteSuccessfulSaveTicket(ticket);
                        return true;
                    }
                    catch
                    {
                        RemoveSaveTicket(ticket);
                        throw;
                    }
                }, CancellationToken.None);
                _fileOperationQueueTail = ObserveFileOperationAsync(operation);
            }
            catch
            {
                RemoveSaveTicket(ticket);
                throw;
            }
        }

        return new QueuedSaveOperation(ticket, operation);
    }

    private void CompleteSuccessfulSaveTicket(SaveOperationTicket ticket)
    {
        lock (_saveTicketLock)
        {
            if (!_saveTickets.Contains(ticket))
                return;
            ticket.IsCompleted = true;
            if (ticket.IsRetired)
                _saveTickets.Remove(ticket);
        }
    }

    private void AcknowledgeSaveTicketsThrough(SaveOperationTicket completedTicket)
    {
        lock (_saveTicketLock)
        {
            foreach (var ticket in _saveTickets.Where(ticket =>
                         ReferenceEquals(ticket.Session, completedTicket.Session) &&
                         ticket.Sequence <= completedTicket.Sequence).ToArray())
            {
                ticket.IsRetired = true;
                ticket.IsCompleted = true;
                ticket.Session = null;
                _saveTickets.Remove(ticket);
            }
        }
    }

    private void RetireSaveTicketsForSession(EditingSession session)
    {
        lock (_saveTicketLock)
        {
            foreach (var ticket in _saveTickets.Where(ticket => ReferenceEquals(ticket.Session, session)).ToArray())
            {
                ticket.IsRetired = true;
                ticket.Session = null;
                if (ticket.IsCompleted)
                    _saveTickets.Remove(ticket);
            }
        }
    }

    private void RemoveSaveTicket(SaveOperationTicket ticket)
    {
        lock (_saveTicketLock)
            _saveTickets.Remove(ticket);
    }

    private void RemoveSaveTicketIfOperationFailed(QueuedSaveOperation? saveOperation)
    {
        if (saveOperation is { } operation &&
            (operation.Operation.IsCanceled || operation.Operation.IsFaulted))
            RemoveSaveTicket(operation.Ticket);
    }

    private static async Task ObserveFileOperationAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
            // Keep the FIFO tail usable after one failed or canceled operation.
        }
    }

    private Task CaptureFileOperationQueueTail()
    {
        lock (_fileOperationQueueLock)
            return _fileOperationQueueTail;
    }

    private void SetWorkspaceOpenPending(bool value, long generation = 0)
    {
        if (value)
            _workspaceOpenPendingGeneration = generation;
        if (_isWorkspaceOpenPending == value)
            return;
        _isWorkspaceOpenPending = value;
        _saveCommand.NotifyCanExecuteChanged();
    }

    private void ClearWorkspaceOpenPending(long generation)
    {
        if (_isWorkspaceOpenPending && _workspaceOpenPendingGeneration == generation)
            SetWorkspaceOpenPending(false);
    }

    private void SetPendingWorkspaceAction(PendingWorkspaceActionState? pending)
    {
        lock (_recoveryTimerLock)
            _pendingWorkspaceAction = pending;
        _pendingWorkspaceActionError = null;
        NotifyPendingWorkspaceActionState();
    }

    private void ClearPendingWorkspaceAction()
    {
        if (_pendingWorkspaceAction is null && _pendingWorkspaceActionError is null)
            return;
        lock (_recoveryTimerLock)
            _pendingWorkspaceAction = null;
        _pendingWorkspaceActionError = null;
        NotifyPendingWorkspaceActionState();
    }

    private void SetPendingWorkspaceActionError(string? value)
    {
        if (_pendingWorkspaceActionError == value)
            return;
        _pendingWorkspaceActionError = value;
        OnPropertyChanged(nameof(PendingWorkspaceActionError));
    }

    private void SetPendingWorkspaceActionBusy(bool value)
    {
        if (_isPendingWorkspaceActionBusy == value)
            return;
        _isPendingWorkspaceActionBusy = value;
        OnPropertyChanged(nameof(IsPendingWorkspaceActionBusy));
        _savePendingWorkspaceActionCommand.NotifyCanExecuteChanged();
        _discardPendingWorkspaceActionCommand.NotifyCanExecuteChanged();
        _cancelPendingWorkspaceActionCommand.NotifyCanExecuteChanged();
        _newWorkspaceCommand.NotifyCanExecuteChanged();
        _openCommand.NotifyCanExecuteChanged();
        _saveCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPendingWorkspaceActionState()
    {
        OnPropertyChanged(nameof(PendingWorkspaceAction));
        OnPropertyChanged(nameof(IsPendingWorkspaceActionVisible));
        OnPropertyChanged(nameof(PendingWorkspaceActionTitle));
        OnPropertyChanged(nameof(PendingWorkspaceActionMessage));
        OnPropertyChanged(nameof(PendingWorkspaceActionTarget));
        OnPropertyChanged(nameof(PendingWorkspaceActionError));
        _savePendingWorkspaceActionCommand.NotifyCanExecuteChanged();
        _discardPendingWorkspaceActionCommand.NotifyCanExecuteChanged();
        _cancelPendingWorkspaceActionCommand.NotifyCanExecuteChanged();
        _newWorkspaceCommand.NotifyCanExecuteChanged();
        _openCommand.NotifyCanExecuteChanged();
        _saveCommand.NotifyCanExecuteChanged();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private async Task ExportSvgFromCommandAsync()
    {
        if (_window is null)
        {
            SetStatus("SVG 출력 실패: 파일 대화상자를 사용할 수 없습니다.");
            return;
        }

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "수식 SVG 내보내기",
            SuggestedFileName = MakeSuggestedFileName(WorkspaceName, ".svg"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SVG 벡터 이미지") { Patterns = new[] { "*.svg" }, MimeTypes = new[] { "image/svg+xml" } }
            }
        });
        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            SetStatus("SVG 출력 실패: 로컬 파일 시스템 경로를 얻을 수 없습니다.");
            return;
        }
        await ExportSvgToPathAsync(path).ConfigureAwait(true);
    }

    private void ShowOutputPreview()
    {
        IsOutputPreviewVisible = true;
        if (_outputPreview.State == MathOutputPreviewState.Empty)
            _outputPreview.RefreshPreview();
    }

    private async Task SaveOutputPreviewSvgFromCommandAsync()
    {
        var window = _window;
        var expectedSnapshot = _outputPreview.PreviewSnapshot;
        if (window is null || expectedSnapshot is null || !_outputPreview.CanExportSvg)
            return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "미리보기 SVG 저장",
            SuggestedFileName = MakeSuggestedFileName(WorkspaceName, ".svg"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SVG 벡터 이미지")
                {
                    Patterns = new[] { "*.svg" },
                    MimeTypes = new[] { "image/svg+xml" }
                }
            }
        });
        if (file is null || !ReferenceEquals(window, _window))
            return;

        var path = file.TryGetLocalPath();
        if (path is null)
            return;

        await _outputPreview.ExportSvgToPathAsync(path, expectedSnapshot).ConfigureAwait(true);
    }

    private void OnOutputPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _saveOutputPreviewSvgCommand.NotifyCanExecuteChanged();

    private void InsertTemplate(Func<Guid?> insert)
    {
        if (insert() is not null)
            EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool InsertPaletteText(string text)
    {
        if (!Session.InsertText(text))
            return false;
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool InsertPaletteTemplate(MathTemplateKind kind)
    {
        if (Session.InsertTemplate(kind) is null)
            return false;
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RunRowCommand(Func<bool> edit)
    {
        if (edit())
            EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RunGroupCommand(Func<bool> edit)
    {
        if (edit())
            EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyRowCommandState()
    {
        OnPropertyChanged(nameof(ActiveGroupNumber));
        OnPropertyChanged(nameof(ActiveRowNumber));
        OnPropertyChanged(nameof(ActiveRowCount));
        OnPropertyChanged(nameof(DeleteRowToolTip));
        OnPropertyChanged(nameof(MoveRowUpToolTip));
        OnPropertyChanged(nameof(MoveRowDownToolTip));
        _addRowCommand.NotifyCanExecuteChanged();
        _duplicateRowCommand.NotifyCanExecuteChanged();
        _deleteRowCommand.NotifyCanExecuteChanged();
        _moveRowUpCommand.NotifyCanExecuteChanged();
        _moveRowDownCommand.NotifyCanExecuteChanged();
    }

    private void NotifyThinSpaceCommandState()
    {
        OnPropertyChanged(nameof(CanInsertThinSpace));
        OnPropertyChanged(nameof(ThinSpaceDisabledReason));
        _insertThinSpaceCommand.NotifyCanExecuteChanged();
    }

    private void NotifyGroupCommandState()
    {
        OnPropertyChanged(nameof(GroupCount));
        OnPropertyChanged(nameof(DeleteGroupToolTip));
        OnPropertyChanged(nameof(MoveGroupUpToolTip));
        OnPropertyChanged(nameof(MoveGroupDownToolTip));
        _addGroupCommand.NotifyCanExecuteChanged();
        _duplicateGroupCommand.NotifyCanExecuteChanged();
        _deleteGroupCommand.NotifyCanExecuteChanged();
        _moveGroupUpCommand.NotifyCanExecuteChanged();
        _moveGroupDownCommand.NotifyCanExecuteChanged();
    }

    private void OnActiveRowChanged(object? sender, EventArgs e)
    {
        NotifyRowCommandState();
        NotifyGroupCommandState();
        NotifyClearScopeState();
    }

    private void UndoFromCommand()
    {
        if (!Session.CanUndo)
            return;
        Session.Undo();
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RedoFromCommand()
    {
        if (!Session.CanRedo)
            return;
        Session.Redo();
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceSession(EditingSession session, Guid? preserveRecoveryId = null)
    {
        _closeApproval = null;
        RetireSaveTicketsForSession(_session);
        RotateRecoveryIdentity(preserveRecoveryId);
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session.ActiveRowChanged -= OnActiveRowChanged;
        _session = session;
        ClearLatexCopyMessage();
        NotifyLatexCopyState();
        _outputPreview.SetSession(session);
        _structurePanel.SetSession(session);
        _properties.SetSession(session);
        _session.PropertyChanged += OnSessionPropertyChanged;
        _session.ActiveRowChanged += OnActiveRowChanged;
        OnPropertyChanged(nameof(Session));
        UpdateDerivedProperties();
        NotifyThinSpaceCommandState();
        NotifyRowCommandState();
        NotifyGroupCommandState();
        NotifyClearScopeState();
        _undoCommand.NotifyCanExecuteChanged();
        _redoCommand.NotifyCanExecuteChanged();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditingSession.Workspace))
        {
            ClearLatexCopyMessageIfDocumentChanged();
            NotifyLatexCopyState();
        }

        if (e.PropertyName is nameof(EditingSession.Workspace) or nameof(EditingSession.Caret) or
            nameof(EditingSession.Selection) or nameof(EditingSession.SelectedNodeId) or
            nameof(EditingSession.IsNodeSelected) or nameof(EditingSession.CanInsertMathSpace) or
            nameof(EditingSession.MathSpaceInsertDisabledReason))
            NotifyThinSpaceCommandState();

        if (e.PropertyName == nameof(EditingSession.Revision))
        {
            ClearLatexCopyMessageIfDocumentChanged();
            _closeApproval = null;
            _saveCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsRecoverySaveRequired));
        }

        if (e.PropertyName == nameof(EditingSession.Workspace))
        {
            UpdateDerivedProperties();
        }
        else if (e.PropertyName is nameof(EditingSession.Caret) or nameof(EditingSession.Selection) or
                 nameof(EditingSession.SelectedNodeId) or nameof(EditingSession.IsNodeSelected))
        {
            SetSelectedNodeDescription(DescribeSelection(Session));
        }

        if (e.PropertyName == nameof(EditingSession.IsDirty))
        {
            OnPropertyChanged(nameof(IsRecoverySaveRequired));
            if (!IsCurrentRegularSaveStatus() &&
                !(_isPendingWorkspaceActionBusy && _pendingWorkspaceAction is not null))
                SetStatus(GetWorkspaceStatus(Session));
        }

        if (e.PropertyName is nameof(EditingSession.CanUndo) or nameof(EditingSession.CanRedo))
        {
            _undoCommand.NotifyCanExecuteChanged();
            _redoCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateDerivedProperties()
    {
        SetWorkspaceName(Session.Workspace.Name);
        UpdateLatexCodeState(Session.Workspace);
        SetStructureSummary(BuildStructureSummary(Session.Workspace));
        SetSelectedNodeDescription(DescribeSelection(Session));
        var fontFamily = Session.Workspace.Style.FontFamily;
        if (!string.Equals(_paletteFontFamily, fontFamily, StringComparison.Ordinal))
        {
            _paletteFontFamily = fontFamily;
            _symbolPalette.RefreshSelectedGlyphCoverage();
        }
    }

    private static string DescribeSelection(EditingSession session)
    {
        if (session.SelectedNodeId is not Guid selectedId ||
            !MathTree.TryFindNode(session.Workspace, selectedId, out var selected, out var ancestors))
            return "수식의 빈 편집 슬롯을 선택하세요.";

        if (session.IsNodeSelected)
        {
            var label = selected switch
            {
                FractionNode => "분수",
                RadicalNode => "근호",
                ScriptNode => "첨자",
                MatrixNode => "행렬",
                IntegralNode => "적분",
                SummationNode => "합",
                LimitNode => "극한",
                BracketsNode brackets => $"{BracketName(brackets.Kind)} 괄호",
                CasesNode => "경우 나누기",
                MathSpaceNode => "얇은 수학 공백",
                SequenceNode => "수식 구문",
                TextNode text => $"텍스트 선택: {text.Text}",
                _ => "수식 구조"
            };
            return selected is TextNode ? label : $"구조 선택: {label}";
        }

        if (session.Selection is MathSelection selection && selection.Anchor.NodeId == selection.Focus.NodeId &&
            MathTree.FindTextNode(session.Workspace, selection.Anchor.NodeId) is TextNode selectedText)
        {
            var start = Math.Min(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            var end = Math.Max(selection.Anchor.TextElementOffset, selection.Focus.TextElementOffset);
            if (start != end)
            {
                var startIndex = TextElementOffsets.ToUtf16Index(selectedText.Text, start);
                var endIndex = TextElementOffsets.ToUtf16Index(selectedText.Text, end);
                return $"텍스트 선택: {selectedText.Text[startIndex..endIndex]}";
            }
        }

        foreach (var ancestorId in ancestors.Reverse())
        {
            if (!MathTree.TryFindNode(session.Workspace, ancestorId, out var ancestor, out _))
                continue;
            var description = DescribeChildSlot(ancestor!, selectedId);
            if (description is not null)
                return description;
        }
        return session.IsNodeSelected ? "구조 선택" : "수식 텍스트 편집";
    }

    private static string? DescribeChildSlot(MathNode parent, Guid childId)
    {
        bool Has(MathNode node) => Contains(node, childId);

        return parent switch
        {
            FractionNode fraction when Has(fraction.Numerator) => "분수 분자",
            FractionNode fraction when Has(fraction.Denominator) => "분수 분모",
            RadicalNode radical when Has(radical.Radicand) => "근호 내부",
            ScriptNode script when Has(script.Base) => "첨자 밑",
            ScriptNode script when script.Subscript is not null && Has(script.Subscript) => "아래첨자",
            ScriptNode script when script.Superscript is not null && Has(script.Superscript) => "위첨자",
            IntegralNode integral when integral.LowerLimit is not null && Has(integral.LowerLimit) => "적분 하한",
            IntegralNode integral when integral.UpperLimit is not null && Has(integral.UpperLimit) => "적분 상한",
            IntegralNode integral when Has(integral.Integrand) => "적분식",
            IntegralNode integral when Has(integral.DifferentialVariable) => "적분 변수",
            SummationNode summation when summation.LowerLimit is not null && Has(summation.LowerLimit) => "합 하한",
            SummationNode summation when summation.UpperLimit is not null && Has(summation.UpperLimit) => "합 상한",
            SummationNode summation when Has(summation.Body) => "합의 식",
            LimitNode limit when Has(limit.Approach) => "극한 접근식",
            LimitNode limit when Has(limit.Body) => "극한의 식",
            BracketsNode brackets when Has(brackets.Content) => "괄호 내부",
            CasesNode cases => CasesSlotDescription(cases, childId),
            MatrixNode matrix => MatrixSlotDescription(matrix, childId),
            _ => null
        };
    }

    private static string? MatrixSlotDescription(MatrixNode matrix, Guid childId)
    {
        for (var index = 0; index < matrix.Cells.Length; index++)
        {
            if (Contains(matrix.Cells[index], childId))
                return $"행렬 {index / matrix.Columns + 1}행 {index % matrix.Columns + 1}열";
        }
        return null;
    }

    private static string? CasesSlotDescription(CasesNode cases, Guid childId)
    {
        for (var index = 0; index < cases.Rows.Length; index++)
        {
            if (Contains(cases.Rows[index].Expression, childId))
                return $"경우 나누기 {index + 1}행 식";
            if (Contains(cases.Rows[index].Condition, childId))
                return $"경우 나누기 {index + 1}행 조건";
        }
        return null;
    }

    private static string BracketName(MathBracketKind kind) => kind switch
    {
        MathBracketKind.Parentheses => "소괄호",
        MathBracketKind.Square => "대괄호",
        MathBracketKind.Curly => "중괄호",
        MathBracketKind.Angle => "꺾쇠",
        _ => "괄호"
    };

    private static bool Contains(MathNode node, Guid id)
    {
        if (node.Id == id)
            return true;
        foreach (var child in MathTree.ChildrenOf(node))
            if (Contains(child, id))
                return true;
        return false;
    }

    private static string BuildStructureSummary(EquationWorkspace workspace)
    {
        var lines = new List<string>();
        for (var groupIndex = 0; groupIndex < workspace.Groups.Length; groupIndex++)
        {
            var group = workspace.Groups[groupIndex];
            lines.Add($"그룹 {groupIndex + 1} · {(group.Alignment == AlignmentMode.Equals ? "등호 정렬" : "일반 정렬")}");
            for (var rowIndex = 0; rowIndex < group.Rows.Length; rowIndex++)
                lines.Add($"  {rowIndex + 1}행: {PlainExpression(group.Rows[rowIndex].Root)}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string PlainExpression(MathNode node) => node switch
    {
        TextNode text => text.Text,
        MathSpaceNode => "[얇은 수학 공백]",
        SequenceNode sequence => string.Concat(sequence.Children.Select(PlainExpression)),
        FractionNode fraction => $"({PlainExpression(fraction.Numerator)})/({PlainExpression(fraction.Denominator)})",
        RadicalNode radical => $"√({PlainExpression(radical.Radicand)})",
        ScriptNode script => $"{PlainExpression(script.Base)}{(script.Subscript is null ? string.Empty : $"₍{PlainExpression(script.Subscript)}₎")}{(script.Superscript is null ? string.Empty : $"^{PlainExpression(script.Superscript)}")}",
        MatrixNode matrix => $"[{string.Join("; ", Enumerable.Range(0, matrix.Rows).Select(row =>
            string.Join(", ", Enumerable.Range(0, matrix.Columns).Select(column => PlainExpression(matrix.Cells[row * matrix.Columns + column])))))}]",
        IntegralNode integral => $"∫{(integral.LowerLimit is null ? string.Empty : $"₍{PlainExpression(integral.LowerLimit)}₎")}{(integral.UpperLimit is null ? string.Empty : $"^{PlainExpression(integral.UpperLimit)}")}{PlainExpression(integral.Integrand)} d{PlainExpression(integral.DifferentialVariable)}",
        SummationNode summation => $"Σ{(summation.LowerLimit is null ? string.Empty : $"₍{PlainExpression(summation.LowerLimit)}₎")}{(summation.UpperLimit is null ? string.Empty : $"^{PlainExpression(summation.UpperLimit)}")}{PlainExpression(summation.Body)}",
        LimitNode limit => $"lim₍{PlainExpression(limit.Approach)}₎ {PlainExpression(limit.Body)}",
        BracketsNode brackets => BracketOpen(brackets.Kind) + PlainExpression(brackets.Content) + BracketClose(brackets.Kind),
        CasesNode cases => $"경우 나누기[{string.Join("; ", cases.Rows.Select(row => $"{PlainExpression(row.Expression)} (조건: {PlainExpression(row.Condition)})"))}]",
        _ => string.Empty
    };

    private static string BracketOpen(MathBracketKind kind) => kind switch
    {
        MathBracketKind.Parentheses => "(",
        MathBracketKind.Square => "[",
        MathBracketKind.Curly => "{",
        MathBracketKind.Angle => "⟨",
        _ => "("
    };

    private static string BracketClose(MathBracketKind kind) => kind switch
    {
        MathBracketKind.Parentheses => ")",
        MathBracketKind.Square => "]",
        MathBracketKind.Curly => "}",
        MathBracketKind.Angle => "⟩",
        _ => ")"
    };

    private static string MakeSuggestedFileName(string name, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (safe.Length == 0)
            safe = "AetherMath";
        return safe.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? safe : safe + extension;
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException;

    private readonly record struct CloseApproval(
        Guid Token,
        EditingSession Session,
        long Revision,
        long NavigationGeneration);

    private readonly record struct QueuedSaveOperation(
        SaveOperationTicket Ticket,
        Task<bool> Operation);

    private sealed class SaveOperationTicket(long sequence, EditingSession session, Guid stateId)
    {
        public long Sequence { get; } = sequence;
        public EditingSession? Session { get; set; } = session;
        public Guid StateId { get; } = stateId;
        public bool IsCompleted { get; set; }
        public bool IsRetired { get; set; }
    }

    private readonly record struct RecoveryCleanupResult(bool Removed, PersistenceDiagnostic? Diagnostic);

    private sealed class PendingWorkspaceActionState(
        WorkspaceActionKind kind,
        EditingSession originalSession,
        long navigationGeneration,
        string? openPath = null,
        EquationWorkspace? openWorkspace = null,
        WorkspaceRecovery? recovery = null,
        RecoverySummary? recoverySummary = null)
    {
        public WorkspaceActionKind Kind { get; } = kind;
        public EditingSession OriginalSession { get; } = originalSession;
        public long NavigationGeneration { get; } = navigationGeneration;
        public string? OpenPath { get; } = openPath;
        public EquationWorkspace? OpenWorkspace { get; set; } = openWorkspace;
        public WorkspaceRecovery? Recovery { get; } = recovery;
        public RecoverySummary? RecoverySummary { get; } = recoverySummary;
        public string? RelinkOldPath { get; set; }
    }

    private void SetWorkspaceName(string value)
    {
        if (_workspaceName == value)
            return;
        _workspaceName = value;
        OnPropertyChanged(nameof(WorkspaceName));
    }

    private void SetStatus(string value)
    {
        if (_statusText == value)
            return;
        _statusText = value;
        OnPropertyChanged(nameof(StatusText));
    }

    private void UpdateLatexCodeState(EquationWorkspace workspace)
    {
        var code = LatexSerializer.ToLatex(workspace);
        var textDiagnostics = LatexSerializer.GetTextDiagnostics(workspace);
        var layoutDiagnostics = LatexSerializer.GetLayoutDiagnostics(workspace);
        var incompleteSlotNodeIds = LatexSerializer.GetIncompleteSlotNodeIds(workspace);
        var canExport = textDiagnostics.IsEmpty && incompleteSlotNodeIds.IsEmpty;
        var diagnostic = BuildLatexCodeDiagnostic(textDiagnostics, incompleteSlotNodeIds, layoutDiagnostics);

        var codeChanged = !string.Equals(_latexCode, code, StringComparison.Ordinal);
        var syntaxChanged = !string.Equals(_latexSyntax.Source, code, StringComparison.Ordinal);
        var syntax = syntaxChanged
            ? new LatexSyntaxSnapshot(code, LatexSyntaxTokenizer.Tokenize(code))
            : _latexSyntax;
        var textDiagnosticsChanged = !_latexTextDiagnostics.SequenceEqual(textDiagnostics);
        var layoutDiagnosticsChanged = !_latexLayoutDiagnostics.SequenceEqual(layoutDiagnostics);
        var incompleteSlotsChanged = !_latexIncompleteSlotNodeIds.SequenceEqual(incompleteSlotNodeIds);
        var canExportChanged = _canExportLatex != canExport;
        var diagnosticChanged = !string.Equals(_latexCodeDiagnostic, diagnostic, StringComparison.Ordinal);

        // Publish only after every source/diagnostic field belongs to the same workspace snapshot.
        _latexCode = code;
        _latexSyntax = syntax;
        _latexTextDiagnostics = textDiagnostics;
        _latexLayoutDiagnostics = layoutDiagnostics;
        _latexIncompleteSlotNodeIds = incompleteSlotNodeIds;
        _canExportLatex = canExport;
        _latexCodeDiagnostic = diagnostic;
        _latexCodeWorkspace = workspace;
        if (string.Equals(_latexCopyMessage, LatexCodeUpdatingMessage, StringComparison.Ordinal))
            ClearLatexCopyMessage();

        if (codeChanged)
            OnPropertyChanged(nameof(LatexCode));
        if (syntaxChanged)
            OnPropertyChanged(nameof(LatexSyntax));
        if (textDiagnosticsChanged)
            OnPropertyChanged(nameof(LatexTextDiagnostics));
        if (layoutDiagnosticsChanged)
            OnPropertyChanged(nameof(LatexLayoutDiagnostics));
        if (incompleteSlotsChanged)
            OnPropertyChanged(nameof(LatexIncompleteSlotNodeIds));
        if (canExportChanged)
            OnPropertyChanged(nameof(CanExportLatex));
        if (diagnosticChanged)
            OnPropertyChanged(nameof(LatexCodeDiagnostic));

        NotifyLatexCopyState();
    }

    private static string BuildLatexCodeDiagnostic(
        ImmutableArray<LatexTextDiagnostic> textDiagnostics,
        ImmutableArray<Guid> incompleteSlotNodeIds,
        ImmutableArray<LatexLayoutDiagnostic> layoutDiagnostics)
    {
        var messages = new List<string>(3);
        if (!textDiagnostics.IsEmpty)
        {
            var distinctCodePoints = textDiagnostics.Select(diagnostic => diagnostic.CodePointLabel)
                .Distinct(StringComparer.Ordinal).ToArray();
            var codePointPreview = string.Join(", ", distinctCodePoints.Take(3));
            if (distinctCodePoints.Length > 3)
                codePointPreview += ", …";
            messages.Add($"제어 문자 {textDiagnostics.Length}개({codePointPreview})가 있어 외부 LaTeX 재사용은 지원되지 않습니다. 생성 코드에는 원문이 유지됩니다.");
        }
        if (!incompleteSlotNodeIds.IsEmpty)
            messages.Add($"필수 수식 슬롯 {incompleteSlotNodeIds.Length}개가 비어 있어 수식이 미완성입니다.");
        if (!layoutDiagnostics.IsEmpty)
            messages.Add($"{layoutDiagnostics.Length}개 그룹의 논리 행 간격은 LaTeX 코드에 반영되지 않습니다.");
        return string.Join(Environment.NewLine, messages);
    }

    private void SetSelectedNodeDescription(string value)
    {
        if (_selectedNodeDescription == value)
            return;
        _selectedNodeDescription = value;
        OnPropertyChanged(nameof(SelectedNodeDescription));
    }

    private void SetStructureSummary(string value)
    {
        if (_structureSummary == value)
            return;
        _structureSummary = value;
        OnPropertyChanged(nameof(StructureSummary));
    }

    private void SetCurrentPath(string? value)
    {
        if (_currentPath == value)
            return;
        _currentPath = value;
        SetRecoveryOriginalPath(value);
        OnPropertyChanged(nameof(CurrentPath));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
