using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Manages memory retention by identifying low-value items for archival
/// and handling archive/restore/forget lifecycle operations.
///
/// Scoring formula:
///   retentionScore = 0.35*value + 0.25*source + 0.20*access + 0.20*freshness
/// Items with a low retentionScore are candidates for archival.
/// </summary>
public class RetentionService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<RetentionService> _logger;

    // Scoring weights
    private const double WeightValue = 0.35;
    private const double WeightSource = 0.25;
    private const double WeightAccess = 0.20;
    private const double WeightFreshness = 0.20;

    // Items with a retention score below this threshold are archive candidates
    private const double ArchiveThreshold = 0.3;

    // Items older than this (in days) are more likely to be archived
    private const int StaleAgeDays = 30;

    public RetentionService(IAppDbContext db, ILogger<RetentionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Identifies memory items that are candidates for archival based on
    /// value, source (evidence), access patterns, and freshness.
    /// </summary>
    /// <returns>A list of memory item IDs that should be archived.</returns>
    public async Task<List<Guid>> GetArchiveCandidatesAsync(
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var items = await _db.AgentMemoryItems
            .Where(i => i.WorkspaceId == workspaceId && i.Status == MemoryStatus.Active)
            .ToListAsync(ct);

        if (items.Count == 0)
            return new List<Guid>();

        var itemIds = items.Select(i => i.Id).ToList();

        // Load evidence counts per item
        var evidenceCounts = await _db.AgentMemoryEvidences
            .Where(e => itemIds.Contains(e.MemoryItemId))
            .GroupBy(e => e.MemoryItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        // Load read access counts per item
        var accessCounts = await _db.AgentMemoryAccessLogs
            .Where(a => a.MemoryItemId != null
                        && itemIds.Contains(a.MemoryItemId.Value)
                        && a.Action == "read")
            .GroupBy(a => a.MemoryItemId!.Value)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        var now = DateTime.UtcNow;
        var candidates = new List<Guid>();

        foreach (var item in items)
        {
            var score = ComputeRetentionScore(item, evidenceCounts, accessCounts, now);

            if (score < ArchiveThreshold)
            {
                candidates.Add(item.Id);
                _logger.LogDebug(
                    "Archive candidate: item {ItemId}, score={Score:F3}, importance={Importance}, confidence={Confidence}, age={AgeDays}d, reads={Reads}, evidence={Evidence}",
                    item.Id, score, item.Importance, item.Confidence,
                    (int)(now - item.UpdatedAt).TotalDays,
                    accessCounts.TryGetValue(item.Id, out var rc) ? rc : 0,
                    evidenceCounts.TryGetValue(item.Id, out var ec) ? ec : 0);
            }
        }

        _logger.LogInformation(
            "GetArchiveCandidates: {CandidateCount}/{TotalCount} items are archive candidates in workspace {WorkspaceId}",
            candidates.Count, items.Count, workspaceId);

        return candidates;
    }

    /// <summary>
    /// Archives a confirmed memory item. Idempotent: if already archived, no-op.
    /// </summary>
    public async Task ArchiveItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.AgentMemoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);

        if (item == null)
        {
            _logger.LogWarning("ArchiveItem: item {ItemId} not found", itemId);
            return;
        }

        // Idempotent: if already archived, no-op
        if (item.Status == MemoryStatus.Archived)
        {
            _logger.LogDebug("ArchiveItem: item {ItemId} is already archived (no-op)", itemId);
            return;
        }

        item.Archive();
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Archived memory item {ItemId}", itemId);
    }

    /// <summary>
    /// Restores an archived memory item to active state. Idempotent.
    /// </summary>
    public async Task RestoreItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.AgentMemoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);

        if (item == null)
        {
            _logger.LogWarning("RestoreItem: item {ItemId} not found", itemId);
            return;
        }

        // Idempotent: if already active, no-op
        if (item.Status == MemoryStatus.Active)
        {
            _logger.LogDebug("RestoreItem: item {ItemId} is already active (no-op)", itemId);
            return;
        }

        item.Restore();
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Restored memory item {ItemId}", itemId);
    }

    /// <summary>
    /// Forgets (soft-deletes) a confirmed memory item.
    /// The item's Status is set to Forgotten.
    /// </summary>
    public async Task ForgetItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.AgentMemoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);

        if (item == null)
        {
            _logger.LogWarning("ForgetItem: item {ItemId} not found", itemId);
            return;
        }

        // If already forgotten, no-op
        if (item.Status == MemoryStatus.Forgotten)
        {
            _logger.LogDebug("ForgetItem: item {ItemId} is already forgotten (no-op)", itemId);
            return;
        }

        item.Forget();
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Forgot memory item {ItemId}", itemId);
    }

    // ===== Private helpers =====

    /// <summary>
    /// Computes a retention score (0.0 - 1.0) for a memory item.
    /// Higher scores indicate the item should be retained; lower scores indicate archival candidacy.
    ///
    /// Score = 0.35*value + 0.25*source + 0.20*access + 0.20*freshness
    /// </summary>
    private static double ComputeRetentionScore(
        AgentMemoryItem item,
        Dictionary<Guid, int> evidenceCounts,
        Dictionary<Guid, int> accessCounts,
        DateTime now)
    {
        // Value: normalized importance (0-10) + confidence (0-1)
        var importanceScore = item.Importance / 10.0;
        var confidenceScore = (double)item.Confidence;
        var value = (importanceScore * 0.6) + (confidenceScore * 0.4);

        // Source: items with evidence are more valuable
        var evidenceCount = evidenceCounts.TryGetValue(item.Id, out var ec) ? ec : 0;
        var source = Math.Min(1.0, evidenceCount / 3.0); // 3+ evidence items = max score

        // Access: items that have been read are more valuable
        var readCount = accessCounts.TryGetValue(item.Id, out var rc) ? rc : 0;
        var access = Math.Min(1.0, readCount / 5.0); // 5+ reads = max score

        // Freshness: newer items are more valuable
        var ageDays = (now - item.UpdatedAt).TotalDays;
        var freshness = ageDays <= 0 ? 1.0 : Math.Max(0, 1.0 - (ageDays / StaleAgeDays));

        return WeightValue * value
             + WeightSource * source
             + WeightAccess * access
             + WeightFreshness * freshness;
    }
}
