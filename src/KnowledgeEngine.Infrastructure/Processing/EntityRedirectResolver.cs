using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class EntityRedirectResolver(IAppDbContext db) : IEntityRedirectResolver
{
    public async Task<EntityRedirectResult> ResolveAsync(
        Guid entityId,
        string workspaceId,
        CancellationToken ct = default)
    {
        var current = entityId;
        var visited = new HashSet<Guid>();
        for (var depth = 0; depth <= 10; depth++)
        {
            if (!visited.Add(current))
                throw new InvalidOperationException(
                    $"Entity redirect loop detected at {current}.");
            var entity = await db.Entities.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == current && x.WorkspaceId == workspaceId, ct)
                ?? throw new KeyNotFoundException($"Entity not found: {current}");
            if (entity.Status != "merged" || !entity.MergedIntoId.HasValue)
            {
                return new EntityRedirectResult
                {
                    EntityId = current,
                    RedirectedFrom = current == entityId ? null : entityId,
                    Depth = depth
                };
            }
            current = entity.MergedIntoId.Value;
        }
        throw new InvalidOperationException(
            $"Entity redirect depth exceeds 10 for {entityId}.");
    }
}
