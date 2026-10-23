using System.Collections.Immutable;
using AetherMath.Editing;

namespace AetherMath.Persistence;

/// <summary>
/// Provides recent-workspace metadata and recovery snapshots under an explicit state root.
/// </summary>
public interface IWorkspaceStateService
{
    string RootDirectory { get; }

    Task<RecentWorkspaceIndexResult> ReadRecentWorkspacesAsync(CancellationToken cancellationToken = default);

    Task<RecentWorkspaceMutationResult> RecordSuccessfulUseAsync(
        string path,
        string displayName,
        DateTimeOffset usedAtUtc,
        CancellationToken cancellationToken = default);

    Task<RecentWorkspaceMutationResult> SetPinnedAsync(
        string path,
        bool isPinned,
        CancellationToken cancellationToken = default);

    Task<RecentWorkspaceMutationResult> RemoveRecentAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<RecoveryWriteResult> SaveRecoveryAsync(
        WorkspaceRecovery recovery,
        CancellationToken cancellationToken = default);

    Task<RecoveryListResult> ListRecoveriesAsync(CancellationToken cancellationToken = default);

    Task<RecoveryReadResult> ReadRecoveryAsync(Guid recoveryId, CancellationToken cancellationToken = default);

    Task<bool> DeleteRecoveryAsync(Guid recoveryId, CancellationToken cancellationToken = default);
}

public sealed record PersistenceDiagnostic(string Code, string Message, string? Path = null);

public static class WorkspaceStateDiagnosticCodes
{
    public const string RecentIndexInvalid = "RecentIndexInvalid";
    public const string RecentIndexUnsupportedVersion = "RecentIndexUnsupportedVersion";
    public const string RecoveryInvalid = "RecoveryInvalid";
    public const string RecoveryUnsupportedVersion = "RecoveryUnsupportedVersion";
    public const string RecoveryIdentityMismatch = "RecoveryIdentityMismatch";
    public const string UnsafePath = "UnsafePath";
    public const string RecentFileMetadataUnavailable = "RecentFileMetadataUnavailable";
}

public enum RecentWorkspaceFileStatus
{
    Available,
    Missing,
    MetadataUnavailable
}

public sealed record RecentWorkspaceEntry(
    string FullPath,
    string DisplayName,
    DateTimeOffset LastUsedAtUtc,
    bool IsPinned,
    bool IsMissing,
    DateTimeOffset? FileModifiedAtUtc,
    RecentWorkspaceFileStatus FileStatus,
    PersistenceDiagnostic? FileDiagnostic);

public sealed record RecentWorkspaceIndexResult(
    ImmutableArray<RecentWorkspaceEntry> Entries,
    bool CanWrite,
    PersistenceDiagnostic? Diagnostic);

public enum RecentWorkspaceMutationStatus
{
    Applied,
    Unchanged,
    NotFound,
    WriteBlocked
}

public sealed record RecentWorkspaceMutationResult(
    RecentWorkspaceMutationStatus Status,
    RecentWorkspaceIndexResult Snapshot,
    PersistenceDiagnostic? Diagnostic);

public sealed record WorkspaceRecovery(
    Guid RecoveryId,
    WorkspaceSnapshot Snapshot,
    DateTimeOffset CapturedAtUtc,
    string? OriginalPath);

public enum RecoveryWriteStatus
{
    Written,
    WriteBlocked
}

public sealed record RecoveryWriteResult(RecoveryWriteStatus Status, PersistenceDiagnostic? Diagnostic);

public sealed record RecoverySummary(
    Guid RecoveryId,
    Guid WorkspaceId,
    string WorkspaceName,
    DateTimeOffset CapturedAtUtc,
    string? OriginalPath,
    long Revision);

public sealed record RecoveryListResult(
    ImmutableArray<RecoverySummary> Recoveries,
    ImmutableArray<PersistenceDiagnostic> Diagnostics);

public enum RecoveryReadStatus
{
    Found,
    NotFound,
    Corrupt,
    Unsupported
}

public sealed record RecoveryReadResult(
    RecoveryReadStatus Status,
    WorkspaceRecovery? Recovery,
    PersistenceDiagnostic? Diagnostic);
