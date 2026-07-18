using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IBindingService
{
    Task<CloudAccountBindingDto> BindCloudAccountAsync(
        CreateCloudAccountBindingDto input,
        CancellationToken ct = default);
    Task<List<CloudAccountBindingDto>> ListCloudAccountsAsync(CancellationToken ct = default);
    Task UnbindCloudAccountAsync(Guid id, CancellationToken ct = default);
    Task<WorkspaceBindingDto> CreateWorkspaceBindingAsync(
        CreateWorkspaceBindingDto input,
        CancellationToken ct = default);
    Task<List<WorkspaceBindingDto>> ListWorkspaceBindingsAsync(
        Guid? workspaceId = null,
        CancellationToken ct = default);
    Task<WorkspaceBindingDto> UpdateWorkspaceBindingAsync(
        Guid id,
        UpdateWorkspaceBindingDto input,
        CancellationToken ct = default);
    Task UnbindWorkspaceAsync(Guid id, CancellationToken ct = default);
    Task<string?> GetRefreshTokenAsync(Guid cloudAccountBindingId, CancellationToken ct = default);
    Task<string?> GetAccessTokenAsync(Guid cloudAccountBindingId, CancellationToken ct = default);
}
