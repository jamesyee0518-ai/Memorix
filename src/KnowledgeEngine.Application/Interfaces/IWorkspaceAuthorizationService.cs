namespace KnowledgeEngine.Application.Interfaces;

public interface IWorkspaceAuthorizationService
{
    Task<WorkspaceAccessResult> AuthorizeAsync(
        Guid workspaceId,
        CancellationToken ct = default);
}

public enum WorkspaceAccessResult
{
    Allowed,
    NotFound,
    Forbidden
}
