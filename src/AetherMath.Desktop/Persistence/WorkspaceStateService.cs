using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Persistence;

/// <summary>
/// Stores recent-workspace metadata and recovery snapshots under an explicitly supplied root.
/// It never writes to a workspace's original path.
/// </summary>
public sealed class WorkspaceStateService : IWorkspaceStateService
{
    public const string RecentIndexFileName = "recent-workspaces.json";
    public const string RecoveryDirectoryName = "recovery";
    public const string RootLockFileName = ".state.lock";

    private const int CurrentVersion = 1;
    private const int MaximumUnpinnedRecentEntries = 20;
    private static readonly JsonSerializerOptions SerializerOptions = WorkspaceJsonCodec.CreateOptions();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RootGates = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceStateService(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
    }

    public string RootDirectory { get; }

    public async Task<RecentWorkspaceIndexResult> ReadRecentWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        var index = await ReadRecentIndexNoLockAsync(cancellationToken).ConfigureAwait(false);
        return await CreateRecentSnapshotAsync(index.Entries, index.CanWrite, index.Diagnostic, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RecentWorkspaceMutationResult> RecordSuccessfulUseAsync(
        string path,
        string displayName,
        DateTimeOffset usedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var fullPath = NormalizeUserPath(path);
        var usedAt = NormalizeTimestamp(usedAtUtc, nameof(usedAtUtc));

        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        var index = await ReadRecentIndexNoLockAsync(cancellationToken).ConfigureAwait(false);
        if (!index.CanWrite)
            return await CreateMutationResultAsync(RecentWorkspaceMutationStatus.WriteBlocked, index, cancellationToken)
                .ConfigureAwait(false);

        var entries = index.Entries.ToList();
        var existingIndex = entries.FindIndex(entry => PathsEqual(entry.FullPath, fullPath));
        var pinned = existingIndex >= 0 && entries[existingIndex].IsPinned;
        var replacement = new StoredRecentEntry(fullPath, displayName.Trim(), usedAt, pinned);
        if (existingIndex >= 0)
            entries[existingIndex] = replacement;
        else
            entries.Add(replacement);

        entries = NormalizeRecentEntries(entries);
        await WriteRecentIndexNoLockAsync(entries, cancellationToken).ConfigureAwait(false);
        var updatedIndex = new RecentIndexReadResult(entries, true, null);
        return await CreateMutationResultAsync(RecentWorkspaceMutationStatus.Applied, updatedIndex, CancellationToken.None)
            .ConfigureAwait(false);
    }

    public async Task<RecentWorkspaceMutationResult> SetPinnedAsync(
        string path,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = NormalizeUserPath(path);
        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        var index = await ReadRecentIndexNoLockAsync(cancellationToken).ConfigureAwait(false);
        if (!index.CanWrite)
            return await CreateMutationResultAsync(RecentWorkspaceMutationStatus.WriteBlocked, index, cancellationToken)
                .ConfigureAwait(false);

        var entries = index.Entries.ToList();
        var entryIndex = entries.FindIndex(entry => PathsEqual(entry.FullPath, fullPath));
        if (entryIndex < 0)
            return await CreateMutationResultAsync(RecentWorkspaceMutationStatus.NotFound, index, cancellationToken)
                .ConfigureAwait(false);
        if (entries[entryIndex].IsPinned == isPinned)
            return await CreateMutationResultAsync(RecentWorkspaceMutationStatus.Unchanged, index, cancellationToken)
                .ConfigureAwait(false);

        entries[entryIndex] = entries[entryIndex] with { IsPinned = isPinned };
        entries = NormalizeRecentEntries(entries);
        await WriteRecentIndexNoLockAsync(entries, cancellationToken).ConfigureAwait(false);
        return await CreateMutationResultAsync(
            RecentWorkspaceMutationStatus.Applied,
            new RecentIndexReadResult(entries, true, null),
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<RecentWorkspaceMutationResult> RemoveRecentAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = NormalizeUserPath(path);
        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        var index = await ReadRecentIndexNoLockAsync(cancellationToken).ConfigureAwait(false);
        if (!index.CanWrite)
            return await CreateMutationResultAsync(RecentWorkspaceMutationStatus.WriteBlocked, index, cancellationToken)
                .ConfigureAwait(false);

        var entries = index.Entries.Where(entry => !PathsEqual(entry.FullPath, fullPath)).ToList();
        if (entries.Count == index.Entries.Count)
            return await CreateMutationResultAsync(RecentWorkspaceMutationStatus.NotFound, index, cancellationToken)
                .ConfigureAwait(false);

        await WriteRecentIndexNoLockAsync(entries, cancellationToken).ConfigureAwait(false);
        return await CreateMutationResultAsync(
            RecentWorkspaceMutationStatus.Applied,
            new RecentIndexReadResult(entries, true, null),
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<RecoveryWriteResult> SaveRecoveryAsync(
        WorkspaceRecovery recovery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = ValidateAndNormalizeRecovery(recovery);
        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        var directory = GetRecoveryDirectoryPath();
        try
        {
            EnsureSafeDirectory(directory, createIfMissing: true);
        }
        catch (IOException)
        {
            return new RecoveryWriteResult(RecoveryWriteStatus.WriteBlocked,
                new PersistenceDiagnostic(WorkspaceStateDiagnosticCodes.UnsafePath,
                    "복구 디렉터리가 일반 폴더가 아니어서 저장을 차단했습니다.", directory));
        }
        var targetPath = GetRecoveryPath(normalized.RecoveryId);

        if (TryGetFileAttributes(targetPath, out var attributes))
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) != 0)
                return new RecoveryWriteResult(RecoveryWriteStatus.WriteBlocked,
                    new PersistenceDiagnostic(WorkspaceStateDiagnosticCodes.UnsafePath,
                        "복구 파일 경로가 일반 파일이 아니어서 덮어쓰지 않았습니다.", targetPath));

            var existing = await ReadRecoveryNoLockAsync(normalized.RecoveryId, cancellationToken).ConfigureAwait(false);
            if (existing.Status is not (RecoveryReadStatus.Found or RecoveryReadStatus.NotFound))
                return new RecoveryWriteResult(RecoveryWriteStatus.WriteBlocked, existing.Diagnostic);
        }

        await AtomicWriteJsonAsync(targetPath, CreateRecoveryEnvelope(normalized), cancellationToken).ConfigureAwait(false);
        return new RecoveryWriteResult(RecoveryWriteStatus.Written, null);
    }

    public async Task<RecoveryListResult> ListRecoveriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        var directory = GetRecoveryDirectoryPath();
        if (!TryGetDirectoryAttributes(directory, out var attributes))
            return new RecoveryListResult(ImmutableArray<RecoverySummary>.Empty, ImmutableArray<PersistenceDiagnostic>.Empty);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            return new RecoveryListResult(ImmutableArray<RecoverySummary>.Empty,
                ImmutableArray.Create(new PersistenceDiagnostic(WorkspaceStateDiagnosticCodes.UnsafePath,
                    "복구 디렉터리가 재분석 지점이어서 읽지 않았습니다.", directory)));

        var summaries = ImmutableArray.CreateBuilder<RecoverySummary>();
        var diagnostics = ImmutableArray.CreateBuilder<PersistenceDiagnostic>();
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new PersistenceDiagnostic(WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                "복구 목록을 읽을 수 없습니다.", directory));
            return new RecoveryListResult(ImmutableArray<RecoverySummary>.Empty, diagnostics.ToImmutable());
        }

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (!TryGetCanonicalRecoveryId(fileName, out var recoveryId))
                continue;

            RecoveryReadResult read;
            try
            {
                read = await ReadRecoveryNoLockAsync(recoveryId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new PersistenceDiagnostic(WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                    "복구 파일을 읽을 수 없습니다.", path));
                continue;
            }
            if (read.Status == RecoveryReadStatus.Found && read.Recovery is not null)
            {
                var recovery = read.Recovery;
                summaries.Add(new RecoverySummary(
                    recovery.RecoveryId,
                    recovery.Snapshot.Workspace.Id,
                    recovery.Snapshot.Workspace.Name,
                    recovery.CapturedAtUtc,
                    recovery.OriginalPath,
                    recovery.Snapshot.Revision));
            }
            else if (read.Diagnostic is not null)
            {
                diagnostics.Add(read.Diagnostic);
            }
        }

        var ordered = summaries
            .OrderByDescending(summary => summary.CapturedAtUtc)
            .ThenBy(summary => summary.RecoveryId.ToString("D"), StringComparer.Ordinal)
            .ToImmutableArray();
        return new RecoveryListResult(ordered, diagnostics.ToImmutable());
    }

    public async Task<RecoveryReadResult> ReadRecoveryAsync(Guid recoveryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfEmpty(recoveryId, nameof(recoveryId));
        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        return await ReadRecoveryNoLockAsync(recoveryId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteRecoveryAsync(Guid recoveryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfEmpty(recoveryId, nameof(recoveryId));
        await using var lease = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
        var directory = GetRecoveryDirectoryPath();
        if (!TryGetDirectoryAttributes(directory, out var directoryAttributes))
            return false;
        if ((directoryAttributes & FileAttributes.Directory) == 0 ||
            (directoryAttributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("복구 디렉터리가 안전한 일반 폴더가 아니어서 삭제를 차단했습니다.");

        var path = GetRecoveryPath(recoveryId);
        if (!TryGetFileAttributes(path, out var attributes))
            return false;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("지정된 복구 경로가 일반 파일이 아니어서 삭제하지 않았습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return true;
    }

    private async Task<RecentIndexReadResult> ReadRecentIndexNoLockAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(RootDirectory, RecentIndexFileName);
        if (!TryGetFileAttributes(path, out var attributes))
            return new RecentIndexReadResult(new List<StoredRecentEntry>(), true, null);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            return InvalidRecentIndex(WorkspaceStateDiagnosticCodes.UnsafePath,
                "최근 작업 색인 경로가 일반 파일이 아니어서 읽기/쓰기를 차단했습니다.", path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (HasDuplicateJsonProperties(root) || root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
                return InvalidRecentIndex(WorkspaceStateDiagnosticCodes.RecentIndexInvalid,
                    "최근 작업 색인의 버전 필드가 없거나 올바르지 않습니다.", path);
            if (version != CurrentVersion)
                return InvalidRecentIndex(WorkspaceStateDiagnosticCodes.RecentIndexUnsupportedVersion,
                    $"지원하지 않는 최근 작업 색인 버전입니다: {version}", path);

            var envelope = JsonSerializer.Deserialize<RecentIndexEnvelope>(bytes, SerializerOptions);
            if (envelope is null || envelope.Version != CurrentVersion || envelope.Entries.IsDefault)
                return InvalidRecentIndex(WorkspaceStateDiagnosticCodes.RecentIndexInvalid,
                    "최근 작업 색인의 필수 필드가 올바르지 않습니다.", path);

            var entries = new List<StoredRecentEntry>(envelope.Entries.Length);
            foreach (var entry in envelope.Entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.DisplayName) ||
                    entry.LastUsedAtUtc == default || !TryNormalizeStoredPath(entry.FullPath, out var fullPath))
                    return InvalidRecentIndex(WorkspaceStateDiagnosticCodes.RecentIndexInvalid,
                        "최근 작업 색인 항목의 이름, 경로 또는 시각이 올바르지 않습니다.", path);

                entries.Add(entry with
                {
                    FullPath = fullPath,
                    DisplayName = entry.DisplayName.Trim(),
                    LastUsedAtUtc = entry.LastUsedAtUtc.ToUniversalTime()
                });
            }

            return new RecentIndexReadResult(NormalizeRecentEntries(entries), true, null);
        }
        catch (JsonException)
        {
            return InvalidRecentIndex(WorkspaceStateDiagnosticCodes.RecentIndexInvalid,
                "최근 작업 색인이 손상되었거나 지원하지 않는 필드를 포함합니다. 원본을 보존했습니다.", path);
        }
        catch (ArgumentException)
        {
            return InvalidRecentIndex(WorkspaceStateDiagnosticCodes.RecentIndexInvalid,
                "최근 작업 색인의 시각 또는 경로 메타데이터가 올바르지 않습니다. 원본을 보존했습니다.", path);
        }
    }

    private async Task WriteRecentIndexNoLockAsync(List<StoredRecentEntry> entries, CancellationToken cancellationToken)
    {
        var path = Path.Combine(RootDirectory, RecentIndexFileName);
        EnsureSafeFileReplacement(path);
        var nullableEntries = entries.Select(static entry => (StoredRecentEntry?)entry).ToImmutableArray();
        var envelope = new RecentIndexEnvelope(CurrentVersion, nullableEntries);
        await AtomicWriteJsonAsync(path, envelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RecentWorkspaceMutationResult> CreateMutationResultAsync(
        RecentWorkspaceMutationStatus status,
        RecentIndexReadResult index,
        CancellationToken cancellationToken)
    {
        var snapshot = await CreateRecentSnapshotAsync(index.Entries, index.CanWrite, index.Diagnostic, cancellationToken)
            .ConfigureAwait(false);
        return new RecentWorkspaceMutationResult(status, snapshot, index.Diagnostic);
    }

    private Task<RecentWorkspaceIndexResult> CreateRecentSnapshotAsync(
        IReadOnlyCollection<StoredRecentEntry> entries,
        bool canWrite,
        PersistenceDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<RecentWorkspaceEntry>(entries.Count);
        foreach (var entry in SortRecentEntries(entries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = ReadFileMetadata(entry.FullPath);
            builder.Add(new RecentWorkspaceEntry(
                entry.FullPath,
                entry.DisplayName,
                entry.LastUsedAtUtc,
                entry.IsPinned,
                fileInfo.Status == RecentWorkspaceFileStatus.Missing,
                fileInfo.FileModifiedAtUtc,
                fileInfo.Status,
                fileInfo.Diagnostic));
        }

        return Task.FromResult(new RecentWorkspaceIndexResult(builder.ToImmutable(), canWrite, diagnostic));
    }

    private async Task<RecoveryReadResult> ReadRecoveryNoLockAsync(Guid recoveryId, CancellationToken cancellationToken)
    {
        var directory = GetRecoveryDirectoryPath();
        if (!TryGetDirectoryAttributes(directory, out var directoryAttributes))
            return new RecoveryReadResult(RecoveryReadStatus.NotFound, null, null);
        if ((directoryAttributes & FileAttributes.Directory) == 0 ||
            (directoryAttributes & FileAttributes.ReparsePoint) != 0)
            return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.UnsafePath,
                "복구 디렉터리가 재분석 지점이어서 읽지 않았습니다.", directory);

        var path = GetRecoveryPath(recoveryId);
        if (!TryGetFileAttributes(path, out var attributes))
            return new RecoveryReadResult(RecoveryReadStatus.NotFound, null, null);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.UnsafePath,
                "복구 파일 경로가 일반 파일이 아니어서 읽지 않았습니다.", path);

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (HasDuplicateJsonProperties(root) || root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
                return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                    "복구 데이터의 버전 필드가 없거나 올바르지 않습니다.", path);
            if (version != CurrentVersion)
                return RecoveryFailure(RecoveryReadStatus.Unsupported,
                    WorkspaceStateDiagnosticCodes.RecoveryUnsupportedVersion,
                    $"지원하지 않는 복구 데이터 버전입니다: {version}", path);

            var envelope = JsonSerializer.Deserialize<RecoveryEnvelope>(bytes, SerializerOptions);
            if (envelope is null || envelope.Version != CurrentVersion)
                return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                    "복구 데이터의 필수 필드가 올바르지 않습니다.", path);
            if (envelope.RecoveryId != recoveryId)
                return RecoveryFailure(RecoveryReadStatus.Corrupt,
                    WorkspaceStateDiagnosticCodes.RecoveryIdentityMismatch,
                    "복구 ID와 파일명이 일치하지 않습니다.", path);
            if (envelope.OriginalPath is not null && !Path.IsPathFullyQualified(envelope.OriginalPath))
                return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                    "저장된 원본 경로 메타데이터가 절대 경로가 아닙니다.", path);

            var recovery = ValidateAndNormalizeRecovery(new WorkspaceRecovery(
                envelope.RecoveryId,
                envelope.Snapshot,
                envelope.CapturedAtUtc,
                envelope.OriginalPath));
            return new RecoveryReadResult(RecoveryReadStatus.Found, recovery, null);
        }
        catch (JsonException)
        {
            return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                "복구 데이터가 손상되었거나 지원하지 않는 필드를 포함합니다.", path);
        }
        catch (InvalidDataException)
        {
            return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                "복구 작업 데이터의 구조 또는 필수 슬롯이 올바르지 않습니다.", path);
        }
        catch (ArgumentException)
        {
            return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                "복구 메타데이터가 올바르지 않습니다.", path);
        }
        catch (NotSupportedException)
        {
            return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                "복구 데이터에 지원하지 않는 값이 있습니다.", path);
        }
        catch (PathTooLongException)
        {
            return RecoveryFailure(RecoveryReadStatus.Corrupt, WorkspaceStateDiagnosticCodes.RecoveryInvalid,
                "복구 메타데이터의 경로가 너무 깁니다.", path);
        }
    }

    private async Task<RootLockLease> AcquireRootLockAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = RootGates.GetOrAdd(RootDirectory, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(RootDirectory);
            EnsureSafeDirectory(RootDirectory, createIfMissing: false);
            var lockPath = Path.Combine(RootDirectory, RootLockFileName);
            EnsureSafeFileReplacement(lockPath);
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 1, FileOptions.Asynchronous | FileOptions.WriteThrough);
            return new RootLockLease(gate, stream);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    private string GetRecoveryDirectoryPath() => Path.Combine(RootDirectory, RecoveryDirectoryName);

    private string GetRecoveryPath(Guid recoveryId) =>
        Path.Combine(GetRecoveryDirectoryPath(), $"{recoveryId:D}.json");

    private static RecoveryEnvelope CreateRecoveryEnvelope(WorkspaceRecovery recovery) =>
        new(CurrentVersion, recovery.RecoveryId, recovery.CapturedAtUtc, recovery.OriginalPath, recovery.Snapshot);

    private static WorkspaceRecovery ValidateAndNormalizeRecovery(WorkspaceRecovery recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ThrowIfEmpty(recovery.RecoveryId, nameof(recovery.RecoveryId));
        ArgumentNullException.ThrowIfNull(recovery.Snapshot);
        if (recovery.Snapshot.Revision < 0 || recovery.Snapshot.StateId == Guid.Empty || recovery.Snapshot.Workspace is null)
            throw new InvalidDataException("복구 snapshot 메타데이터가 올바르지 않습니다.");
        WorkspaceValidator.Validate(recovery.Snapshot.Workspace);
        var capturedAt = NormalizeTimestamp(recovery.CapturedAtUtc, nameof(recovery.CapturedAtUtc));
        var originalPath = recovery.OriginalPath is null ? null : NormalizeUserPath(recovery.OriginalPath);
        return recovery with { CapturedAtUtc = capturedAt, OriginalPath = originalPath };
    }

    private static void ThrowIfEmpty(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("빈 GUID는 사용할 수 없습니다.", parameterName);
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp == default)
            throw new ArgumentException("기본값 시각은 사용할 수 없습니다.", parameterName);
        return timestamp.ToUniversalTime();
    }

    private static string NormalizeUserPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static bool TryNormalizeStoredPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static List<StoredRecentEntry> NormalizeRecentEntries(IEnumerable<StoredRecentEntry> source)
    {
        var merged = new Dictionary<string, StoredRecentEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source)
        {
            if (!merged.TryGetValue(entry.FullPath, out var current))
            {
                merged.Add(entry.FullPath, entry);
                continue;
            }

            var useNew = entry.LastUsedAtUtc > current.LastUsedAtUtc ||
                         (entry.LastUsedAtUtc == current.LastUsedAtUtc &&
                          string.Compare(entry.DisplayName, current.DisplayName, StringComparison.Ordinal) < 0);
            var chosen = useNew ? entry : current;
            merged[entry.FullPath] = chosen with { IsPinned = current.IsPinned || entry.IsPinned };
        }

        var pinned = merged.Values.Where(entry => entry.IsPinned);
        var unpinned = merged.Values.Where(entry => !entry.IsPinned)
            .OrderByDescending(entry => entry.LastUsedAtUtc)
            .ThenBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FullPath, StringComparer.Ordinal)
            .Take(MaximumUnpinnedRecentEntries);
        return SortRecentEntries(pinned.Concat(unpinned)).ToList();
    }

    private static IOrderedEnumerable<StoredRecentEntry> SortRecentEntries(IEnumerable<StoredRecentEntry> entries) =>
        entries.OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.LastUsedAtUtc)
            .ThenBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FullPath, StringComparer.Ordinal);

    private static (RecentWorkspaceFileStatus Status, DateTimeOffset? FileModifiedAtUtc,
        PersistenceDiagnostic? Diagnostic) ReadFileMetadata(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
                return (RecentWorkspaceFileStatus.MetadataUnavailable, null,
                    new PersistenceDiagnostic(WorkspaceStateDiagnosticCodes.UnsafePath,
                        "최근 작업 경로가 파일이 아닙니다.", path));

            var modified = File.GetLastWriteTimeUtc(path);
            if (modified == DateTime.FromFileTimeUtc(0))
                return (RecentWorkspaceFileStatus.Missing, null, null);
            return (RecentWorkspaceFileStatus.Available, new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc)), null);
        }
        catch (FileNotFoundException)
        {
            return (RecentWorkspaceFileStatus.Missing, null, null);
        }
        catch (DirectoryNotFoundException)
        {
            return (RecentWorkspaceFileStatus.Missing, null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return (RecentWorkspaceFileStatus.MetadataUnavailable, null,
                new PersistenceDiagnostic(WorkspaceStateDiagnosticCodes.RecentFileMetadataUnavailable,
                    "최근 작업 파일의 수정 시각을 읽을 수 없습니다.", path));
        }
    }

    private static RecentIndexReadResult InvalidRecentIndex(string code, string message, string path) =>
        new(new List<StoredRecentEntry>(), false, new PersistenceDiagnostic(code, message, path));

    private static RecoveryReadResult RecoveryFailure(
        RecoveryReadStatus status,
        string code,
        string message,
        string path) => new(status, null, new PersistenceDiagnostic(code, message, path));

    private static bool HasDuplicateJsonProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateJsonProperties(property.Value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (HasDuplicateJsonProperties(item))
                    return true;
        }

        return false;
    }

    private static bool TryGetFileAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static bool TryGetDirectoryAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static void EnsureSafeDirectory(string path, bool createIfMissing)
    {
        if (!TryGetDirectoryAttributes(path, out var attributes))
        {
            if (!createIfMissing)
                throw new DirectoryNotFoundException($"디렉터리를 찾을 수 없습니다: {path}");
            Directory.CreateDirectory(path);
            if (!TryGetDirectoryAttributes(path, out attributes))
                throw new DirectoryNotFoundException($"디렉터리를 찾을 수 없습니다: {path}");
        }
        if ((attributes & FileAttributes.Directory) == 0)
            throw new IOException($"디렉터리 경로가 일반 파일을 가리킵니다: {path}");
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"재분석 지점 디렉터리는 사용할 수 없습니다: {path}");
    }

    private static void EnsureSafeFileReplacement(string path)
    {
        if (!TryGetFileAttributes(path, out var attributes))
            return;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException($"일반 파일이 아닌 경로는 교체할 수 없습니다: {path}");
    }

    private async Task AtomicWriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new IOException("저장 경로에 디렉터리가 없습니다.");
        EnsureSafeDirectory(directory, createIfMissing: true);
        EnsureSafeFileReplacement(path);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeFileReplacement(path);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool TryGetCanonicalRecoveryId(string fileName, out Guid recoveryId)
    {
        recoveryId = Guid.Empty;
        if (!fileName.EndsWith(".json", StringComparison.Ordinal))
            return false;
        var stem = fileName[..^5];
        return Guid.TryParseExact(stem, "D", out recoveryId) &&
               string.Equals(fileName, $"{recoveryId:D}.json", StringComparison.Ordinal);
    }

    private sealed record StoredRecentEntry(string FullPath, string DisplayName, DateTimeOffset LastUsedAtUtc, bool IsPinned);
    private sealed record RecentIndexEnvelope(int Version, ImmutableArray<StoredRecentEntry?> Entries);
    private sealed record RecoveryEnvelope(
        int Version,
        Guid RecoveryId,
        DateTimeOffset CapturedAtUtc,
        string? OriginalPath,
        WorkspaceSnapshot Snapshot);
    private sealed record RecentIndexReadResult(
        List<StoredRecentEntry> Entries,
        bool CanWrite,
        PersistenceDiagnostic? Diagnostic);

    private sealed class RootLockLease(SemaphoreSlim gate, FileStream stream) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
