using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace KnowledgeEngine.Infrastructure.Processing;

public class EmbeddingWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmbeddingWorker> _logger;
    private readonly IOptions<EmbeddingSettings> _embeddingSettings;

    public EmbeddingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EmbeddingWorker> logger,
        IOptions<EmbeddingSettings> embeddingSettings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _embeddingSettings = embeddingSettings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmbeddingWorker started. Polling every {Interval}s.",
            PollingInterval.TotalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in EmbeddingWorker polling cycle");
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

        _logger.LogInformation("EmbeddingWorker stopped.");
    }

    private async Task PollAndProcessAsync(CancellationToken ct)
    {
        List<DocumentChunk> pendingChunks;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Find chunks with embedding_status = "pending" OR "stale" (batch of BatchSize).
            // "stale" chunks are produced by IVectorStore.RebuildAsync and must be re-embedded.
            pendingChunks = await db.DocumentChunks
                .Where(c => c.EmbeddingStatus == "pending"
                    || c.EmbeddingStatus == "stale"
                    || (c.EmbeddingStatus == "failed"
                        && db.Documents.Any(d => d.Id == c.DocumentId && d.EmbeddingStatus == "pending")))
                .OrderBy(c => c.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        if (pendingChunks.Count == 0)
        {
            return;
        }

        _logger.LogInformation("EmbeddingWorker found {Count} chunk(s) to embed", pendingChunks.Count);

        using var scope2 = _scopeFactory.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<IAppDbContext>();
        var embeddingService = scope2.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var appDbContext = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var chunkIds = pendingChunks.Select(c => c.Id).ToHashSet();
        var chunksToUpdate = await db2.DocumentChunks
            .Where(c => chunkIds.Contains(c.Id))
            .ToListAsync(ct);

        // Fetch document titles for §10.6 embedding text construction
        var documentIds = chunksToUpdate.Select(c => c.DocumentId).Distinct().ToList();
        var documents = await db2.Documents
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);

        var model = _embeddingSettings.Value.Model;
        var provider = DetermineProvider(_embeddingSettings.Value.Endpoint);

        // Auto stale detection (Task 3): if a ChunkEmbedding already exists for a
        // chunk with a matching ChunkContentHash and status "done", the content
        // hasn't changed — skip re-embedding and mark the chunk as done directly.
        var skipped = new List<DocumentChunk>();
        var toEmbed = new List<DocumentChunk>();
        foreach (var chunk in chunksToUpdate)
        {
            var existingEmbedding = await db2.ChunkEmbeddings
                .FirstOrDefaultAsync(ce => ce.ChunkId == chunk.Id
                    && ce.ChunkContentHash == chunk.ContentHash
                    && ce.Status == "done", ct);
            if (existingEmbedding != null)
            {
                // Content hasn't changed, skip re-embedding
                chunk.EmbeddingStatus = "done";
                chunk.EmbeddingModel = existingEmbedding.Model;
                chunk.UpdatedAt = DateTime.UtcNow;
                skipped.Add(chunk);
            }
            else
            {
                toEmbed.Add(chunk);
            }
        }

        if (skipped.Count > 0)
        {
            await db2.SaveChangesAsync(ct);
            _logger.LogInformation("EmbeddingWorker skipped {Count} chunk(s) with unchanged content", skipped.Count);
        }

        if (toEmbed.Count == 0)
        {
            return;
        }

        // Mark the remaining chunks as processing
        foreach (var chunk in toEmbed)
        {
            chunk.EmbeddingStatus = "processing";
            chunk.UpdatedAt = DateTime.UtcNow;
        }
        await db2.SaveChangesAsync(ct);

        // Build embedding texts per §10.6: include document title + heading path + content
        var texts = toEmbed.Select(c =>
        {
            var title = documents.TryGetValue(c.DocumentId, out var doc) ? doc.Title : "";
            var headingPath = c.HeadingPath ?? "";
            return $"文档标题：{title}\n章节路径：{headingPath}\n正文：\n{c.Content}";
        }).ToList();

        // Try batch embedding first (BatchSize chunks in a single API call)
        List<float[]>? batchResults = null;
        try
        {
            batchResults = await embeddingService.EmbedBatchAsync(texts, ct);
            if (batchResults == null || batchResults.Count != toEmbed.Count)
            {
                _logger.LogWarning("EmbeddingWorker batch embed returned unexpected result count ({Actual} vs {Expected}), falling back to individual embedding",
                    batchResults?.Count ?? 0, toEmbed.Count);
                batchResults = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EmbeddingWorker batch embed failed for {Count} chunk(s), falling back to individual embedding",
                toEmbed.Count);
            batchResults = null;
        }

        if (batchResults != null)
        {
            // Batch success: persist each result individually
            for (var i = 0; i < toEmbed.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var chunk = toEmbed[i];
                var embedding = batchResults[i];

                await EmbedAndPersistAsync(appDbContext, db2, chunk, embedding, model, provider, ct);
            }
        }
        else
        {
            // Fallback: embed each chunk individually (handles per-chunk failures gracefully)
            foreach (var chunk in toEmbed)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // §10.6: construct embedding text with document title + heading path + content
                    var title = documents.TryGetValue(chunk.DocumentId, out var doc) ? doc.Title : "";
                    var headingPath = chunk.HeadingPath ?? "";
                    var embeddingText = $"文档标题：{title}\n章节路径：{headingPath}\n正文：\n{chunk.Content}";
                    var embedding = await embeddingService.EmbedAsync(embeddingText, ct);
                    await EmbedAndPersistAsync(appDbContext, db2, chunk, embedding, model, provider, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EmbeddingWorker failed for chunk {ChunkId}", chunk.Id);
                    await MarkChunkFailedAsync(db2, chunk, ct);
                }
            }
        }
    }

    /// <summary>
    /// Writes the embedding vector to document_chunks, marks the chunk as done,
    /// and keeps the chunk_embeddings table in sync.
    /// </summary>
    private async Task EmbedAndPersistAsync(
        AppDbContext appDbContext,
        IAppDbContext db,
        DocumentChunk chunk,
        float[] embedding,
        string model,
        string provider,
        CancellationToken ct)
    {
        try
        {
            var isSqlite = appDbContext.Database.IsSqlite();

            // PostgreSQL stores the searchable pgvector directly on the chunk.
            // SQLite has no vector column; its canonical local representation is
            // chunk_embeddings.embedding_json, consumed by LocalVectorStore.
            if (!isSqlite)
            {
                await WriteEmbeddingAsync(appDbContext, chunk.Id, embedding, model, ct);
            }

            if (isSqlite)
            {
                await UpsertChunkEmbeddingAsync(db, chunk, embedding, provider, model, ct);
            }

            // Update chunk status to done
            chunk.EmbeddingStatus = "done";
            chunk.EmbeddingModel = model;
            chunk.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Keep the auxiliary JSON table in sync for cloud mode too.
            if (!isSqlite)
            {
                try
                {
                    await UpsertChunkEmbeddingAsync(db, chunk, embedding, provider, model, ct);
                }
                catch (Exception syncEx)
                {
                    _logger.LogWarning(syncEx, "EmbeddingWorker failed to sync chunk_embeddings for chunk {ChunkId}", chunk.Id);
                }
            }

            await UpdateDocumentEmbeddingStatusAsync(db, chunk.DocumentId, ct);

            _logger.LogDebug("EmbeddingWorker embedded chunk {ChunkId}", chunk.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmbeddingWorker failed for chunk {ChunkId}", chunk.Id);
            await MarkChunkFailedAsync(db, chunk, ct);
        }
    }

    /// <summary>
    /// Marks a chunk as failed and persists the state, swallowing save errors.
    /// </summary>
    private async Task MarkChunkFailedAsync(IAppDbContext db, DocumentChunk chunk, CancellationToken ct)
    {
        chunk.EmbeddingStatus = "failed";
        chunk.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            await UpdateDocumentEmbeddingStatusAsync(db, chunk.DocumentId, ct);
        }
        catch (Exception saveEx)
        {
            _logger.LogError(saveEx, "Failed to save error state for chunk {ChunkId}", chunk.Id);
        }
    }

    private static async Task UpdateDocumentEmbeddingStatusAsync(
        IAppDbContext db,
        Guid documentId,
        CancellationToken ct)
    {
        var statuses = await db.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId)
            .Select(chunk => chunk.EmbeddingStatus)
            .ToListAsync(ct);
        var document = await db.Documents.FirstOrDefaultAsync(doc => doc.Id == documentId, ct);
        if (document == null || statuses.Count == 0) return;

        document.EmbeddingStatus = statuses.All(status => status == "done")
            ? "done"
            : statuses.Any(status => status == "failed")
                && statuses.All(status => status is "done" or "failed")
                ? "failed"
                : "processing";
        document.IndexStatus = document.EmbeddingStatus;
        document.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Create or update the ChunkEmbedding record for a chunk so the
    /// chunk_embeddings table stays in sync with document_chunks.embedding.
    /// </summary>
    private async Task UpsertChunkEmbeddingAsync(
        IAppDbContext db,
        DocumentChunk chunk,
        float[] embedding,
        string provider,
        string model,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var embeddingJson = System.Text.Json.JsonSerializer.Serialize(embedding);
        var dimension = embedding.Length;

        // Look up existing record(s) for this chunk + provider + model
        var existing = await db.ChunkEmbeddings
            .Where(ce => ce.ChunkId == chunk.Id && ce.Provider == provider && ce.Model == model)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            // Update the first record; remove any duplicates to keep a single row in sync
            var primary = existing[0];
            primary.WorkspaceId = "default";
            primary.Provider = provider;
            primary.Model = model;
            primary.Dimension = dimension;
            primary.EmbeddingJson = embeddingJson;
            primary.ChunkContentHash = chunk.ContentHash;
            primary.Status = "done";
            primary.ErrorMessage = null;
            primary.RetryCount = 0;
            primary.UpdatedAt = now;

            for (var i = 1; i < existing.Count; i++)
            {
                db.ChunkEmbeddings.Remove(existing[i]);
            }
        }
        else
        {
            db.ChunkEmbeddings.Add(new ChunkEmbedding
            {
                Id = Guid.NewGuid(),
                ChunkId = chunk.Id,
                WorkspaceId = "default",
                Provider = provider,
                Model = model,
                Dimension = dimension,
                EmbeddingJson = embeddingJson,
                ChunkContentHash = chunk.ContentHash,
                Status = "done",
                RetryCount = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Infers the embedding provider name from the configured endpoint.
    /// </summary>
    private static string DetermineProvider(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return "openai";
        var lower = endpoint.ToLowerInvariant();
        if (lower.Contains("ollama")) return "ollama";
        if (lower.Contains(":1234")) return "lmstudio";
        if (lower.Contains("anthropic")) return "anthropic";
        return "openai";
    }

    private static async Task WriteEmbeddingAsync(
        AppDbContext dbContext,
        Guid chunkId,
        float[] embedding,
        string model,
        CancellationToken ct)
    {
        // Build the vector string format: "[0.1,0.2,...]"
        var vectorStr = "[" + string.Join(",", embedding.Select(v => v.ToString("G8", System.Globalization.CultureInfo.InvariantCulture))) + "]";

        var sql = "UPDATE document_chunks SET embedding = @embedding::vector, embedding_model = @model WHERE id = @chunkId";

        var parameters = new[]
        {
            new NpgsqlParameter("@embedding", NpgsqlDbType.Text) { Value = vectorStr },
            new NpgsqlParameter("@model", NpgsqlDbType.Text) { Value = model },
            new NpgsqlParameter("@chunkId", NpgsqlDbType.Uuid) { Value = chunkId }
        };

        await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, ct);
    }
}
