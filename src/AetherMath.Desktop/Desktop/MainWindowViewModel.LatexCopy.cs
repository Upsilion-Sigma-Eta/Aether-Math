using AetherMath.Editing;

namespace AetherMath.Desktop;

public enum LatexCopyStatus
{
    Copied,
    CopiedPreviousSnapshot,
    Blocked,
    Busy,
    Unavailable,
    Failed,
    Canceled
}

public sealed record LatexCopyResult(LatexCopyStatus Status, string Message);

public sealed partial class MainWindowViewModel
{
    private const string LatexCodeUpdatingMessage = "LaTeX 코드가 현재 작업에 맞게 갱신 중입니다.";
    private bool _isCopyingLatex;
    private string _latexCopyMessage = string.Empty;
    private EditingSession? _latexCopyMessageSession;
    private long? _latexCopyMessageRevision;

    public bool CanCopyLatex => !_isCopyingLatex && IsLatexCodeCurrent && CanExportLatex;
    public bool IsCopyingLatex => _isCopyingLatex;
    public string LatexCopyDisabledReason => _isCopyingLatex
        ? "LaTeX 코드 복사가 진행 중입니다."
        : !IsLatexCodeCurrent
            ? LatexCodeUpdatingMessage
            : !CanExportLatex
                ? string.IsNullOrWhiteSpace(LatexCodeDiagnostic)
                    ? "현재 LaTeX 코드는 외부에 재사용할 수 없습니다."
                    : LatexCodeDiagnostic
                : string.Empty;
    public string LatexCopyMessage => _latexCopyMessage;

    private bool IsLatexCodeCurrent => ReferenceEquals(_latexCodeWorkspace, _session.Workspace);

    public async Task<LatexCopyResult> CopyLatexAsync(Func<string, Task>? writeTextAsync)
    {
        if (_isCopyingLatex)
            return new LatexCopyResult(LatexCopyStatus.Busy, "LaTeX 코드 복사가 이미 진행 중입니다.");

        if (!IsLatexCodeCurrent)
        {
            SetLatexCopyMessage(LatexCodeUpdatingMessage);
            return new LatexCopyResult(LatexCopyStatus.Blocked, LatexCodeUpdatingMessage);
        }

        if (!CanExportLatex)
        {
            var blockedMessage = string.IsNullOrWhiteSpace(LatexCodeDiagnostic)
                ? "현재 LaTeX 코드는 외부에 재사용할 수 없습니다."
                : LatexCodeDiagnostic;
            SetLatexCopyMessage(blockedMessage);
            return new LatexCopyResult(LatexCopyStatus.Blocked, blockedMessage);
        }

        if (writeTextAsync is null)
        {
            const string unavailableMessage = "클립보드 쓰기 기능을 사용할 수 없습니다.";
            SetLatexCopyMessage(unavailableMessage);
            return new LatexCopyResult(LatexCopyStatus.Unavailable, unavailableMessage);
        }

        var session = _session;
        var snapshot = session.CaptureSnapshot();
        if (!ReferenceEquals(_latexCodeWorkspace, snapshot.Workspace))
        {
            SetLatexCopyMessage(LatexCodeUpdatingMessage);
            return new LatexCopyResult(LatexCopyStatus.Blocked, LatexCodeUpdatingMessage);
        }
        var source = _latexCode;
        var copyMessageChanged = _latexCopyMessage.Length != 0;
        _isCopyingLatex = true;
        _latexCopyMessage = string.Empty;
        _latexCopyMessageSession = null;
        _latexCopyMessageRevision = null;
        OnPropertyChanged(nameof(IsCopyingLatex));
        if (copyMessageChanged)
            OnPropertyChanged(nameof(LatexCopyMessage));
        NotifyLatexCopyState();

        try
        {
            await writeTextAsync(source).ConfigureAwait(true);

            var copiedPreviousSnapshot = !IsCurrentLatexCopySnapshot(session, snapshot, source);
            var status = copiedPreviousSnapshot
                ? LatexCopyStatus.CopiedPreviousSnapshot
                : LatexCopyStatus.Copied;
            var message = copiedPreviousSnapshot
                ? "요청 당시 이전 수식의 LaTeX 코드를 복사했습니다."
                : "LaTeX 코드를 복사했습니다.";
            SetLatexCopyMessage(message);
            return new LatexCopyResult(status, message);
        }
        catch (OperationCanceledException)
        {
            const string message = "LaTeX 코드 복사가 취소되었습니다.";
            SetLatexCopyMessage(message);
            return new LatexCopyResult(LatexCopyStatus.Canceled, message);
        }
        catch (Exception exception)
        {
            var message = $"LaTeX 코드 복사에 실패했습니다: {exception.Message}";
            SetLatexCopyMessage(message);
            return new LatexCopyResult(LatexCopyStatus.Failed, message);
        }
        finally
        {
            _isCopyingLatex = false;
            OnPropertyChanged(nameof(IsCopyingLatex));
            NotifyLatexCopyState();
        }
    }

    private bool IsCurrentLatexCopySnapshot(
        EditingSession session,
        WorkspaceSnapshot snapshot,
        string source)
    {
        var currentSnapshot = session.CaptureSnapshot();
        return ReferenceEquals(_session, session) &&
            currentSnapshot.Revision == snapshot.Revision &&
            currentSnapshot.StateId == snapshot.StateId &&
            string.Equals(_latexCode, source, StringComparison.Ordinal);
    }

    private void NotifyLatexCopyState()
    {
        OnPropertyChanged(nameof(CanCopyLatex));
        OnPropertyChanged(nameof(LatexCopyDisabledReason));
    }

    private void SetLatexCopyMessage(string value)
    {
        var changed = !string.Equals(_latexCopyMessage, value, StringComparison.Ordinal);
        _latexCopyMessage = value;
        _latexCopyMessageSession = value.Length == 0 ? null : _session;
        _latexCopyMessageRevision = value.Length == 0 ? null : _session.Revision;
        if (changed)
            OnPropertyChanged(nameof(LatexCopyMessage));
    }

    private void ClearLatexCopyMessage() => SetLatexCopyMessage(string.Empty);

    private void ClearLatexCopyMessageIfDocumentChanged()
    {
        if (_latexCopyMessage.Length == 0 ||
            ReferenceEquals(_latexCopyMessageSession, _session) &&
            _latexCopyMessageRevision == _session.Revision)
            return;
        ClearLatexCopyMessage();
    }
}
