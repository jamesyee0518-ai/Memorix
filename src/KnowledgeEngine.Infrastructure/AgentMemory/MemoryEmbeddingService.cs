using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Generates and stores vector embeddings for agent memory items.
/// Embeddings enable semantic search and hybrid retrieval (P2.INF-03).
///
/// Uses the existing ChunkEmbedding table to store memory item embeddings,
/// with ChunkId set to the memory item ID and EmbeddingType = "agent_memory".
/// </summary>
public class MemoryEmbeddingService
{
    private readonly IAppDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<MemoryEmbeddingService> _logger;

    private const string EmbeddingProvider = "agent-memory";
    private const string EmbeddingType = "agent_memory";

    public MemoryEmbeddingService(
        IAppDbContext db,
        IEmbeddingService embeddingService,
        ILogger<MemoryEmbeddingService> logger)
    {
        _db = db;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <summary>
    /// Generates an embedding for a single memory item and stores it in the ChunkEmbedding table.
    /// Returns true on success, false on failure (logs warning, does not throw).
    /// </summary>
    public async Task<bool> EmbedMemoryItemAsync(Guid memoryItemId, CancellationToken ct = default)
    {
        try
        {
            var item = await _db.AgentMemoryItems
                .FirstOrDefaultAsync(i => i.Id == memoryItemId, ct);

            if (item == null)
            {
                _logger.LogWarning("EmbedMemoryItem: item {ItemId} not found", memoryItemId);
                return false;
            }

            // Build text for embedding: Title + Content
            var text = string.Join("\n", new[] { item.Title, item.Content }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogDebug("EmbedMemoryItem: item {ItemId} has no text to embed", memoryItemId);
                return false;
            }

            // Generate embedding
            var embedding = await _embeddingService.EmbedAsync(text, ct);

            if (embedding == null || embedding.Length == 0)
            {
                _logger.LogWarning("EmbedMemoryItem: embedding service returned empty result for item {ItemId}", memoryItemId);
                return false;
            }

            // Check if an embedding already exists for this memory item
            var existing = await _db.ChunkEmbeddings
                .FirstOrDefaultAsync(e => e.ChunkId == memoryItemId && e.EmbeddingType == EmbeddingType, ct);

            var now = DateTime.UtcNow;

            if (existing != null)
            {
                // Update existing embedding
                existing.EmbeddingJson = JsonSerializer.Serialize(embedding);
                existing.Dimension = embedding.Length;
                existing.Status = "done";
                existing.UpdatedAt = now;
                existing.ChunkContentHash = text.GetHashCode().ToString("x");
            }
            else
            {
                // Create new embedding record
                _db.ChunkEmbeddings.Add(new ChunkEmbedding
                {
                    Id = Guid.NewGuid(),
                    ChunkId = memoryItemId,
                    WorkspaceId = item.WorkspaceId.ToString(),
                    Provider = EmbeddingProvider,
                    Model = "default",
                    Dimension = embedding.Length,
                    EmbeddingJson = JsonSerializer.Serialize(embedding),
                    ChunkContentHash = text.GetHashCode().ToString("x"),
                    LanguageCode = "und",
                    EmbeddingType = EmbeddingType,
                    Status = "done",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogDebug(
                "Embedded memory item {ItemId}: dimension={Dimension}",
                memoryItemId, embedding.Length);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to embed memory item {ItemId}: {Message}",
                memoryItemId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Batch-embeds memory items that do not yet have embeddings.
    /// Processes items in batches to avoid overwhelming the embedding service.
    /// </summary>
    /// <param name="workspaceId">The workspace to process.</param>
    /// <param name="batchSize">Number of items per batch (default 50).</param>
    /// <returns>Count of successfully embedded items.</returns>
    public async Task<int> BatchEmbedAsync(
        Guid workspaceId,
        int batchSize = 50,
        CancellationToken ct = default)
    {
        // Find memory items without embeddings
        var embeddedItemIds = await _db.ChunkEmbeddings
            .Where(e => e.EmbeddingType == EmbeddingType)
            .Select(e => e.ChunkId)
            .ToListAsync(ct);

        var itemsToEmbed = await _db.AgentMemoryItems
            .Where(i => i.WorkspaceId == workspaceId
                        && i.Status == MemoryStatus.Active
                        && !embeddedItemIds.Contains(i.Id))
            .OrderByDescending(i => i.UpdatedAt)
            .Take(batchSize)
            .Select(i => i.Id)
            .ToListAsync(ct);

        if (itemsToEmbed.Count == 0)
        {
            _logger.LogDebug("BatchEmbed: no items to embed for workspace {WorkspaceId}", workspaceId);
            return 0;
        }

        _logger.LogInformation(
            "BatchEmbed: processing {Count} items for workspace {WorkspaceId}",
            itemsToEmbed.Count, workspaceId);

        var successCount = 0;
        foreach (var itemId in itemsToEmbed)
        {
            if (await EmbedMemoryItemAsync(itemId, ct))
            {
                successCount++;
            }
        }

        _logger.LogInformation(
            "BatchEmbed: completed for workspace {WorkspaceId}: {Success}/{Total} items embedded",
            workspaceId, successCount, itemsToEmbed.Count);

        return successCount;
    }

    /// <summary>
    /// Loads the embedding vector for a memory item, if available.
    /// </summary>
    public async Task<float[]?> GetEmbeddingAsync(Guid memoryItemId, CancellationToken ct = default)
    {
        var embedding = await _db.ChunkEmbeddings
            .FirstOrDefaultAsync(e => e.ChunkId == memoryItemId && e.EmbeddingType == EmbeddingType, ct);

        if (embedding?.EmbeddingJson == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<float[]>(embedding.EmbeddingJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize embedding for item {ItemId}",
                memoryItemId);
            return null;
        }
    }
}
