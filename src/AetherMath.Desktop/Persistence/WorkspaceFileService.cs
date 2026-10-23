using System.Text.Json;
using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Persistence;

public sealed class WorkspaceFileService : IWorkspaceFileService
{
    public const string Extension = ".aethermath";

    private static readonly JsonSerializerOptions SerializerOptions = WorkspaceJsonCodec.CreateOptions();

    public async Task SaveAsync(string path, WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        WorkspaceValidator.Validate(snapshot.Workspace);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("저장 경로에 디렉터리가 없습니다.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot.Workspace, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public async Task<EquationWorkspace> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var workspace = await JsonSerializer.DeserializeAsync<EquationWorkspace>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        WorkspaceValidator.Validate(workspace);
        return workspace!;
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
}
