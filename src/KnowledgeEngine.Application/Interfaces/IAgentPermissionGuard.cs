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

    // ===== Agent Memory Permission Methods (Phase 1) =====

    /// <summary>
    /// Checks whether the agent profile is allowed to read agent memory.
    /// Requires profile.MemoryReadEnabled == true.
    /// </summary>
    Task<bool> CanReadMemoryAsync(Guid userId, Guid? agentProfileId, Guid workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the agent profile is allowed to write (capture) agent memory.
    /// Requires profile.MemoryWriteEnabled == true.
    /// </summary>
    Task<bool> CanWriteMemoryAsync(Guid userId, Guid? agentProfileId, Guid workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the agent profile is allowed to confirm agent memory items.
    /// Requires HasScopeAsync(profileId, "agent_memory:confirm").
    /// </summary>
    Task<bool> CanConfirmMemoryAsync(Guid userId, Guid? agentProfileId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the agent profile is allowed to delete agent memory items.
    /// Requires HasScopeAsync(profileId, "agent_memory:delete").
    /// </summary>
    Task<bool> CanDeleteMemoryAsync(Guid userId, Guid? agentProfileId, Guid workspaceId, CancellationToken ct = default);
}
