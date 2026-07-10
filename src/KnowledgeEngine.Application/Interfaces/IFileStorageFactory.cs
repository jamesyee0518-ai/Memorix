namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Factory for resolving the correct file storage provider
/// based on the current workspace's fileProvider setting.
/// </summary>
public interface IFileStorageFactory
{
    /// <summary>
    /// Returns the file storage provider for the current workspace.
    /// </summary>
    Task<IFileStorageProvider> GetProviderAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the file storage provider for a specific workspace.
    /// </summary>
    Task<IFileStorageProvider> GetProviderForWorkspaceAsync(string workspaceId, CancellationToken ct = default);
}
