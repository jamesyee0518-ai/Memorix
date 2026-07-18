using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

public interface IAgentPermissionGuard
{
    Task<bool> CanUseToolAsync(Guid userId, Guid? agentProfileId, string toolName, CancellationToken ct = default);
    Task<List<Guid>> GetAccessibleTopicIdsAsync(Guid userId, Guid? agentProfileId, CancellationToken ct = default);
    Task<bool> CanAccessDocumentAsync(Guid userId, Guid? agentProfileId, Guid documentId, CancellationToken ct = default);
    int GetMaxResults(Guid? agentProfileId);

    /// <summary>
    /// Checks whether the agent profile has the specified scope.
    /// If Scopes is null/empty, scopes are inferred from AllowedToolNames.
    /// </summary>
    Task<bool> HasScopeAsync(Guid profileId, string scope, CancellationToken ct = default);

    /// <summary>
    /// Filters a list of documents based on the agent profile's AllowSensitiveDocuments setting.
    /// Documents with sensitivity level private/sensitive/restricted are removed
    /// unless the profile allows sensitive documents.
    /// </summary>
    Task<List<Document>> FilterSensitiveDocumentsAsync(List<Document> documents, Guid profileId, CancellationToken ct = default);
}
