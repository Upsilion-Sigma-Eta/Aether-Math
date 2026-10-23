using AetherMath.Core;
using AetherMath.Editing;

namespace AetherMath.Persistence;

public interface IWorkspaceFileService
{
    Task SaveAsync(string path, WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<EquationWorkspace> OpenAsync(string path, CancellationToken cancellationToken = default);
}
