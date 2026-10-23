using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows.Input;
using AetherMath.Core;
using AetherMath.Editing;
using AetherMath.Layout;
using AetherMath.Rendering;

namespace AetherMath.Desktop;

public enum MathOutputMode
{
    WorkspaceStyle,
    Display,
    Inline
}

public enum MathOutputPreviewState
{
    Empty,
    Ready,
    Stale,
    Incomplete,
    Failed
}

public enum SvgPreviewExportStatus
{
    Saved,
    SavedStale,
    Busy,
    Stale,
    Incomplete,
    Canceled,
    Failed
}

public sealed record MathOutputModeChoice(MathOutputMode Mode, string DisplayName, string Description);

public sealed record MathOutputPreviewSnapshot(
    Guid WorkspaceId,
    long Revision,
    Guid StateId,
    MathOutputMode Mode,
    LayoutResult Layout);

public sealed record SvgPreviewExportResult(SvgPreviewExportStatus Status, string? Path = null, string? Message = null);

public sealed class MathOutputPreviewViewModel : INotifyPropertyChanged
{
    private static readonly ImmutableArray<MathOutputModeChoice> StableModeChoices =
    [
        new(MathOutputMode.WorkspaceStyle, "작업 조판 유지",
            "작업의 기본 조판과 그룹·행·노드의 명시 설정을 그대로 사용합니다."),
        new(MathOutputMode.Display, "독립 수식 (Display)",
            "작업 기본값을 Display 조판으로 투영합니다. 합·극한 배치가 달라질 수 있으며 명시 설정은 유지됩니다."),
        new(MathOutputMode.Inline, "인라인 수식 (Inline)",
            "작업 기본값을 Text 조판으로 투영합니다. 분수·큰 연산자 배치가 달라질 수 있으며 명시 설정은 유지됩니다.")
    ];

    private readonly IMathTextMetrics _metrics;
    private readonly ISvgExportService _svgExportService;
    private readonly RelayCommand _refreshPreviewCommand;
    private EditingSession _session;
    private MathOutputMode _selectedMode;
    private MathOutputPreviewSnapshot? _previewSnapshot;
    private EditingSession? _snapshotOwnerSession;
    private MathOutputPreviewState _state;
    private string _diagnostic = string.Empty;
    private string _lastExportSummary = "아직 SVG를 저장하지 않았습니다.";
    private bool _isExportBusy;
    private long _requestGeneration;
    private long _snapshotGeneration;

    public MathOutputPreviewViewModel(EditingSession session, IMathTextMetrics metrics,
        ISvgExportService svgExportService)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _svgExportService = svgExportService ?? throw new ArgumentNullException(nameof(svgExportService));
        _selectedMode = MathOutputMode.WorkspaceStyle;
        _state = MathOutputPreviewState.Empty;
        _session.PropertyChanged += OnSessionPropertyChanged;
        _refreshPreviewCommand = new RelayCommand(() => RefreshPreview());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImmutableArray<MathOutputModeChoice> ModeChoices => StableModeChoices;

    public MathOutputMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!Enum.IsDefined(typeof(MathOutputMode), value) || _selectedMode == value)
                return;
            _selectedMode = value;
            _requestGeneration++;
            OnPropertyChanged(nameof(SelectedMode));
            OnPropertyChanged(nameof(SelectedModeChoice));
            MarkSnapshotStale("출력 모드가 바뀌었습니다. 미리보기를 새로 고쳐 주세요.");
            NotifyPreviewStateProperties();
        }
    }

    public MathOutputModeChoice SelectedModeChoice
    {
        get => StableModeChoices.First(choice => choice.Mode == SelectedMode);
        set
        {
            if (value is null)
                return;
            var choice = StableModeChoices.FirstOrDefault(candidate => candidate == value);
            if (choice is not null)
                SelectedMode = choice.Mode;
        }
    }

    public MathOutputPreviewSnapshot? PreviewSnapshot => _previewSnapshot;
    public MathOutputPreviewState State => _state;
    public string Diagnostic => _diagnostic;
    public bool IsExportBusy => _isExportBusy;
    public bool CanExportSvg => !_isExportBusy && _state == MathOutputPreviewState.Ready && IsCurrentPreview();
    public ICommand RefreshPreviewCommand => _refreshPreviewCommand;

    public string SnapshotSummary => _previewSnapshot is null
        ? "미리보기가 아직 없습니다."
        : $"전체 작업 · {GetModeDisplayName(_previewSnapshot.Mode)} · revision {_previewSnapshot.Revision}";

    public string CurrentRevisionSummary
    {
        get
        {
            var currentRevision = _session.Revision;
            return _previewSnapshot is null
                ? $"현재 revision {currentRevision}"
                : $"현재 revision {currentRevision} · 미리보기 revision {_previewSnapshot.Revision}";
        }
    }

    public string StateSummary => _state switch
    {
        MathOutputPreviewState.Empty => "미리보기 없음",
        MathOutputPreviewState.Ready => "최신 미리보기",
        MathOutputPreviewState.Stale => "새로 고침 필요",
        MathOutputPreviewState.Incomplete => "필수 슬롯 미완성",
        MathOutputPreviewState.Failed => "미리보기 생성 실패",
        _ => "미리보기 상태 확인 필요"
    };

    public string ScopeSummary => "전체 작업 · 모든 그룹과 행";

    public string ExportDisabledReason
    {
        get
        {
            if (_isExportBusy)
                return "SVG 저장이 진행 중입니다.";
            if (_state == MathOutputPreviewState.Failed)
                return string.IsNullOrWhiteSpace(_diagnostic) ? "미리보기를 만들 수 없어 저장할 수 없습니다." : _diagnostic;
            if (_state == MathOutputPreviewState.Incomplete)
                return "필수 수식 슬롯이 비어 있어 SVG로 저장할 수 없습니다.";
            if (_state == MathOutputPreviewState.Stale || (_previewSnapshot is not null && !IsCurrentPreview()))
                return "작업 또는 출력 모드가 바뀌었습니다. 미리보기를 새로 고쳐야 저장할 수 있습니다.";
            if (_state == MathOutputPreviewState.Empty)
                return "먼저 미리보기를 생성해 주세요.";
            return string.Empty;
        }
    }

    public string LastExportSummary => _lastExportSummary;

    public void SetSession(EditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ReferenceEquals(_session, session))
            return;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session = session;
        _session.PropertyChanged += OnSessionPropertyChanged;
        _requestGeneration++;
        MarkSnapshotStale("작업이 바뀌었습니다. 미리보기를 새로 고쳐 주세요.");
        OnPropertyChanged(nameof(CurrentRevisionSummary));
        NotifyPreviewStateProperties();
    }

    public bool RefreshPreview()
    {
        var generation = ++_requestGeneration;
        var session = _session;
        var mode = _selectedMode;
        var source = session.CaptureSnapshot();
        var workspace = ProjectOutputMode(source.Workspace, mode);

        try
        {
            var layout = MathLayoutEngine.Layout(workspace, _metrics,
                new MathLayoutOptions(FontSize: workspace.Style.FontSize));
            if (!IsCaptureCurrent(session, source, mode, generation))
            {
                if (generation == _requestGeneration)
                    MarkSnapshotStale("작업이 미리보기 생성 중 바뀌었습니다. 다시 새로 고쳐 주세요.");
                return false;
            }

            var snapshot = new MathOutputPreviewSnapshot(source.Workspace.Id, source.Revision,
                source.StateId, mode, layout);
            _previewSnapshot = snapshot;
            _snapshotOwnerSession = session;
            _snapshotGeneration = generation;

            var hasEmptySlots = layout.Diagnostics.Any(diagnostic => diagnostic.Code == LayoutDiagnosticCode.EmptySlot);
            SetState(hasEmptySlots ? MathOutputPreviewState.Incomplete : MathOutputPreviewState.Ready,
                hasEmptySlots ? "필수 수식 슬롯이 비어 있습니다. 미리보기는 표시되지만 SVG로 저장할 수 없습니다." : string.Empty);
            OnPropertyChanged(nameof(PreviewSnapshot));
            NotifyPreviewStateProperties();
            return true;
        }
        catch (Exception exception)
        {
            if (generation == _requestGeneration && ReferenceEquals(session, _session))
            {
                SetState(MathOutputPreviewState.Failed, exception.Message);
                NotifyPreviewStateProperties();
            }
            return false;
        }
    }

    public Task<SvgPreviewExportResult> ExportSvgToPathAsync(string path,
        CancellationToken cancellationToken = default) => ExportSvgToPathCoreAsync(path, _previewSnapshot, cancellationToken);

    internal Task<SvgPreviewExportResult> ExportSvgToPathAsync(string path,
        MathOutputPreviewSnapshot? expectedSnapshot, CancellationToken cancellationToken = default) =>
        ExportSvgToPathCoreAsync(path, expectedSnapshot, cancellationToken);

    private async Task<SvgPreviewExportResult> ExportSvgToPathCoreAsync(string path,
        MathOutputPreviewSnapshot? expectedSnapshot, CancellationToken cancellationToken)
    {
        if (_isExportBusy)
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Busy, Message: "SVG 저장이 이미 진행 중입니다.");

        var snapshot = expectedSnapshot;
        if (snapshot is null)
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Stale, Message: ExportDisabledReason);
        if (!ReferenceEquals(_previewSnapshot, snapshot))
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Stale,
                Message: "요청을 시작한 미리보기가 바뀌었습니다. 현재 미리보기를 다시 확인해 주세요.");

        if (!CanExportSnapshot(snapshot))
        {
            if (_state == MathOutputPreviewState.Failed)
                return new SvgPreviewExportResult(SvgPreviewExportStatus.Failed, Message: _diagnostic);
            MarkSnapshotStale("작업 또는 출력 모드가 바뀌었습니다. 미리보기를 새로 고쳐 주세요.");
            NotifyPreviewStateProperties();
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Stale, Message: ExportDisabledReason);
        }

        if (_state == MathOutputPreviewState.Incomplete ||
            snapshot.Layout.Diagnostics.Any(diagnostic => diagnostic.Code == LayoutDiagnosticCode.EmptySlot))
            return SetExportResult(SvgPreviewExportStatus.Incomplete, null,
                "필수 수식 슬롯이 비어 있어 SVG로 저장할 수 없습니다.", updateCurrent: IsCurrentPreview());

        if (_state != MathOutputPreviewState.Ready)
        {
            MarkSnapshotStale("미리보기가 최신 상태가 아닙니다. 다시 새로 고쳐 주세요.");
            NotifyPreviewStateProperties();
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Stale, Message: ExportDisabledReason);
        }

        if (string.IsNullOrWhiteSpace(path))
            return SetExportResult(SvgPreviewExportStatus.Failed, null, "SVG 저장 경로가 비어 있습니다.",
                updateCurrent: true);

        _isExportBusy = true;
        var ownerSession = _snapshotOwnerSession!;
        var generation = _snapshotGeneration;
        var mode = snapshot.Mode;
        OnPropertyChanged(nameof(IsExportBusy));
        NotifyPreviewStateProperties();
        var stillCurrent = false;
        try
        {
            await _svgExportService.ExportAsync(path, snapshot.Layout, cancellationToken).ConfigureAwait(true);
            stillCurrent = IsExportCurrent(snapshot, ownerSession, generation, mode);
            if (!stillCurrent)
                return new SvgPreviewExportResult(SvgPreviewExportStatus.SavedStale, path,
                    "저장은 완료됐지만 원본 작업 또는 미리보기가 바뀌었습니다.");

            return SetExportResult(SvgPreviewExportStatus.Saved, path,
                $"SVG 저장 완료 · {Path.GetFileName(path)}", updateCurrent: true);
        }
        catch (OperationCanceledException)
        {
            stillCurrent = IsExportCurrent(snapshot, ownerSession, generation, mode);
            if (stillCurrent)
                return SetExportResult(SvgPreviewExportStatus.Canceled, path, "SVG 저장이 취소되었습니다.", updateCurrent: true);
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Canceled, path, "SVG 저장이 취소되었습니다.");
        }
        catch (MathExportException exception)
        {
            stillCurrent = IsExportCurrent(snapshot, ownerSession, generation, mode);
            if (stillCurrent)
            {
                SetState(MathOutputPreviewState.Incomplete, exception.Message);
                NotifyPreviewStateProperties();
                return SetExportResult(SvgPreviewExportStatus.Incomplete, path, exception.Message, updateCurrent: true);
            }
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Incomplete, path, exception.Message);
        }
        catch (Exception exception)
        {
            stillCurrent = IsExportCurrent(snapshot, ownerSession, generation, mode);
            if (stillCurrent)
                return SetExportResult(SvgPreviewExportStatus.Failed, path, exception.Message, updateCurrent: true);
            return new SvgPreviewExportResult(SvgPreviewExportStatus.Failed, path, exception.Message);
        }
        finally
        {
            _isExportBusy = false;
            OnPropertyChanged(nameof(IsExportBusy));
            NotifyPreviewStateProperties();
        }
    }

    private static EquationWorkspace ProjectOutputMode(EquationWorkspace workspace, MathOutputMode mode)
    {
        if (mode == MathOutputMode.WorkspaceStyle)
            return workspace;
        if (mode is not (MathOutputMode.Display or MathOutputMode.Inline))
            throw new ArgumentOutOfRangeException(nameof(mode));
        var projectedStyle = workspace.Style with
        {
            TypesettingStyle = mode == MathOutputMode.Display ? MathTypesettingStyle.Display : MathTypesettingStyle.Text
        };
        return workspace with { Style = projectedStyle };
    }

    private bool IsCaptureCurrent(EditingSession session, WorkspaceSnapshot source, MathOutputMode mode,
        long generation)
    {
        if (!ReferenceEquals(session, _session) || generation != _requestGeneration || mode != _selectedMode ||
            session.Revision != source.Revision || session.Workspace.Id != source.Workspace.Id)
            return false;
        return session.CaptureSnapshot().StateId == source.StateId;
    }

    private bool IsCurrentPreview()
    {
        var snapshot = _previewSnapshot;
        return snapshot is not null && (_state is MathOutputPreviewState.Ready or MathOutputPreviewState.Incomplete) &&
               CanExportSnapshot(snapshot);
    }

    private bool CanExportSnapshot(MathOutputPreviewSnapshot snapshot) =>
        ReferenceEquals(_snapshotOwnerSession, _session) && ReferenceEquals(_previewSnapshot, snapshot) &&
        snapshot.Mode == _selectedMode && _snapshotGeneration == _requestGeneration &&
        IsCurrentRevision(snapshot, _session);

    private bool IsExportCurrent(MathOutputPreviewSnapshot snapshot, EditingSession ownerSession,
        long generation, MathOutputMode mode) =>
        ReferenceEquals(ownerSession, _session) && ReferenceEquals(_snapshotOwnerSession, ownerSession) &&
        ReferenceEquals(_previewSnapshot, snapshot) && _snapshotGeneration == generation &&
        _requestGeneration == generation && _selectedMode == mode && IsCurrentRevision(snapshot, ownerSession);

    private static bool IsCurrentRevision(MathOutputPreviewSnapshot snapshot, EditingSession session)
    {
        if (session.Revision != snapshot.Revision)
            return false;
        var current = session.CaptureSnapshot();
        return current.StateId == snapshot.StateId && current.Workspace.Id == snapshot.WorkspaceId;
    }

    private SvgPreviewExportResult SetExportResult(SvgPreviewExportStatus status, string? path,
        string? message, bool updateCurrent)
    {
        if (updateCurrent)
        {
            _lastExportSummary = status switch
            {
                SvgPreviewExportStatus.Saved => $"저장됨 · {Path.GetFileName(path)}",
                SvgPreviewExportStatus.Canceled => "SVG 저장이 취소되었습니다.",
                SvgPreviewExportStatus.Incomplete => "필수 수식 슬롯이 비어 있어 SVG로 저장할 수 없습니다.",
                SvgPreviewExportStatus.Failed => $"SVG 저장 실패 · {message}",
                _ => _lastExportSummary
            };
            OnPropertyChanged(nameof(LastExportSummary));
        }
        return new SvgPreviewExportResult(status, path, message);
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(EditingSession.Workspace) or nameof(EditingSession.Revision)))
            return;
        _requestGeneration++;
        MarkSnapshotStale("작업 내용이 바뀌었습니다. 미리보기를 새로 고쳐 주세요.");
        OnPropertyChanged(nameof(CurrentRevisionSummary));
        NotifyPreviewStateProperties();
    }

    private void MarkSnapshotStale(string diagnostic)
    {
        if (_previewSnapshot is null)
            return;
        SetState(MathOutputPreviewState.Stale, diagnostic);
    }

    private void SetState(MathOutputPreviewState state, string diagnostic)
    {
        if (_state != state)
        {
            _state = state;
            OnPropertyChanged(nameof(State));
        }
        if (_diagnostic != diagnostic)
        {
            _diagnostic = diagnostic;
            OnPropertyChanged(nameof(Diagnostic));
        }
    }

    private void NotifyPreviewStateProperties()
    {
        OnPropertyChanged(nameof(CanExportSvg));
        OnPropertyChanged(nameof(ExportDisabledReason));
        OnPropertyChanged(nameof(StateSummary));
        OnPropertyChanged(nameof(SnapshotSummary));
        OnPropertyChanged(nameof(CurrentRevisionSummary));
    }

    private static string GetModeDisplayName(MathOutputMode mode) =>
        StableModeChoices.First(choice => choice.Mode == mode).DisplayName;

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this,
        new PropertyChangedEventArgs(propertyName));
}
