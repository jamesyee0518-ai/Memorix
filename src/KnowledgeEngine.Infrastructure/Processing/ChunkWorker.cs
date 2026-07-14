using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public class ChunkWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChunkWorker> _logger;

    public ChunkWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ChunkWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChunkWorker started. Polling every {Interval}s.",
            PollingInterval.TotalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ChunkWorker polling cycle");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ChunkWorker stopped.");
    }

    private async Task PollAndProcessAsync(CancellationToken ct)
    {
        List<Guid> pendingDocumentIds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Find documents with chunk_status = "pending" and ai_status = "done"
            pendingDocumentIds = await db.Documents
                .Where(d => d.ChunkStatus == "pending" && d.AiStatus == "done")
                .Select(d => d.Id)
                .ToListAsync(ct);
        }

        if (pendingDocumentIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("ChunkWorker found {Count} document(s) to chunk", pendingDocumentIds.Count);

        foreach (var documentId in pendingDocumentIds)
        {
            if (ct.IsCancellationRequested) break;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var chunkingService = scope.ServiceProvider.GetRequiredService<IChunkingService>();
            var fullTextIndex = scope.ServiceProvider.GetRequiredService<IChineseFullTextIndexService>();

            try
            {
                // Mark as processing
                var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
                if (doc == null) continue;

                if (doc.ChunkStatus != "pending")
                {
                    // Already being processed by another cycle
                    continue;
                }

                doc.ChunkStatus = "processing";
                doc.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                // Delete old chunks (idempotent: re-chunking removes old chunks first)
                var oldChunks = await db.DocumentChunks
                    .Where(c => c.DocumentId == documentId)
                    .ToListAsync(ct);
                if (oldChunks.Count > 0)
                {
                    // Clean up orphaned ChunkEmbedding records for chunks being replaced.
                    // Old chunks are deleted, so their embedding records must be removed too;
                    // otherwise they become stale references to non-existent chunks.
                    var oldChunkIds = oldChunks.Select(c => c.Id).ToList();
                    var oldEmbeddings = await db.ChunkEmbeddings
                        .Where(ce => oldChunkIds.Contains(ce.ChunkId))
                        .ToListAsync(ct);
                    if (oldEmbeddings.Count > 0)
                    {
                        db.ChunkEmbeddings.RemoveRange(oldEmbeddings);
                    }

                    db.DocumentChunks.RemoveRange(oldChunks);
                    await db.SaveChangesAsync(ct);
                }

                // Chunk the document
                var newChunks = chunkingService.ChunkDocument(doc);
                if (newChunks.Count > 0)
                {
                    foreach (var chunk in newChunks)
                    {
                        db.DocumentChunks.Add(chunk);
                    }
                    await db.SaveChangesAsync(ct);
                }

                // Mark as done
                doc.ChunkStatus = "done";
                doc.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                try
                {
                    await fullTextIndex.IndexDocumentAsync(documentId, ct);
                }
                catch (Exception indexEx)
                {
                    _logger.LogWarning(indexEx, "Chunking succeeded but FTS5 indexing failed for document {DocumentId}", documentId);
                }

                _logger.LogInformation("ChunkWorker completed chunking for document {DocumentId}, created {Count} chunks",
                    documentId, newChunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChunkWorker failed for document {DocumentId}", documentId);

                try
                {
                    var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
                    if (doc != null)
                    {
                        doc.ChunkStatus = "failed";
                        doc.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to save error state for document {DocumentId}", documentId);
                }
            }
        }
    }
}
