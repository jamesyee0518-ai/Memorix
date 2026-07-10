using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Manages workspace lifecycle: creation, switching, configuration.
/// A Workspace determines the runtime mode (local/cloud/hybrid).
/// </summary>
public interface IWorkspaceService
{
    Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceDto input, CancellationToken ct = default);
    Task<WorkspaceDto?> GetWorkspaceAsync(Guid id, CancellationToken ct = default);
    Task<WorkspaceDto?> GetCurrentWorkspaceAsync(Guid userId, CancellationToken ct = default);
    Task<List<WorkspaceDto>> ListWorkspacesAsync(Guid userId, CancellationToken ct = default);
    Task<WorkspaceDto> UpdateWorkspaceAsync(Guid id, UpdateWorkspaceDto input, CancellationToken ct = default);
    Task DeleteWorkspaceAsync(Guid id, CancellationToken ct = default);
    Task SetCurrentWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken ct = default);
    Task<WorkspaceDto> InitializeLocalWorkspaceAsync(InitLocalWorkspaceDto input, CancellationToken ct = default);
}
