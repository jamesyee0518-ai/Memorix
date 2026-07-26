using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IKnowledgeGraphService
{
    Task<EntityGraphDto> GetGraphAsync(
        Guid userId,
        Guid? workspaceId,
        string? entityType,
        string? language,
        int limit = 300,
        CancellationToken ct = default);
    Task<EntityGraphDto> GetNeighborsAsync(
        Guid userId,
        Guid entityId,
        string? language,
        int limit = 100,
        CancellationToken ct = default);
    Task<IReadOnlyList<EntityGraphDocumentDto>> GetDocumentsAsync(
        Guid userId,
        Guid entityId,
        string? language,
        int limit = 100,
        CancellationToken ct = default);
}
