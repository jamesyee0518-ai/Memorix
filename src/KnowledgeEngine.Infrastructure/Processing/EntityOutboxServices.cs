using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// Keeps persistent entity-derived data consistent. Search, QA and graph queries
/// read canonical entities directly; the persistent entity embedding is the only
/// materialized entity index that must be invalidated after identity changes.
/// </summary>
public sealed class EntityIndexSyncService(AppDbContext db) : IEntityIndexSyncService
{
    public async Task SyncAsync(
        Guid entityId,
        string workspaceId,
        long entityVersion,
        string eventType,
        CancellationToken ct = default)
    {
        var entity = await db.Entities.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == entityId && x.WorkspaceId == workspaceId, ct);
        if (entity == null && eventType != "ENTITY_MERGED")
            throw new KeyNotFoundException($"Entity not found for index sync: {entityId}");

        var stale = await db.EntityEmbeddings
            .Where(x => x.EntityId == entityId)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.EntityEmbeddings.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }
}

public sealed class EntityOutboxProcessor(
    AppDbContext db,
    IEntityIndexSyncService indexSync,
    ILogger<EntityOutboxProcessor> logger) : IEntityOutboxProcessor
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct = default)
    {
        var item = await db.EntityOutboxEvents
            .Where(x => x.Status == "pending")
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (item == null) return false;

        item.Status = "processing";
        await db.SaveChangesAsync(ct);
        try
        {
            await indexSync.SyncAsync(
                item.EntityId,
                item.WorkspaceId,
                item.EntityVersion,
                item.EventType,
                ct);
            item.Status = "completed";
            item.ErrorMessage = null;
            item.ProcessedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            item.RetryCount++;
            item.ErrorMessage = ex.Message.Length <= 2000
                ? ex.Message
                : ex.Message[..2000];
            item.Status = item.RetryCount >= 10 ? "failed" : "pending";
            logger.LogWarning(ex,
                "Entity outbox event {EventId} failed on retry {RetryCount}",
                item.Id, item.RetryCount);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class EntityOutboxWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EntityOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<IEntityOutboxProcessor>();
                var processed = await processor.ProcessNextAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Entity outbox worker iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
