using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IEntityMergeService
{
    Task<EntityMergePreview> PreviewAsync(
        Guid userId, EntityMergePreviewRequest request, CancellationToken ct = default);
    Task<EntityMergeResult> MergeAsync(
        Guid userId, ExecuteEntityMergeRequest request, CancellationToken ct = default);
    Task<EntityMergeResult> RevertAsync(
        Guid userId, Guid mergeId, string requestId, CancellationToken ct = default);
    Task<IReadOnlyList<EntityMergeHistoryItem>> GetHistoryAsync(
        Guid userId, Guid? workspaceId, int limit = 100, CancellationToken ct = default);
    Task<Guid> AddBlockAsync(
        Guid userId, AddEntityMergeBlockRequest request, CancellationToken ct = default);
    Task<bool> RemoveBlockAsync(
        Guid userId, Guid blockId, CancellationToken ct = default);
}

public interface IEntityRedirectResolver
{
    Task<EntityRedirectResult> ResolveAsync(
        Guid entityId, string workspaceId, CancellationToken ct = default);
}

public interface IEntityIndexSyncService
{
    Task SyncAsync(
        Guid entityId,
        string workspaceId,
        long entityVersion,
        string eventType,
        CancellationToken ct = default);
}

public interface IEntityOutboxProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken ct = default);
}

public sealed class EntityRedirectResult
{
    public Guid EntityId { get; init; }
    public Guid? RedirectedFrom { get; init; }
    public int Depth { get; init; }
}
