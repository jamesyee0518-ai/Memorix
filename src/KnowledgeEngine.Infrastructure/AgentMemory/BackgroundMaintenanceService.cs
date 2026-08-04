using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Runs periodic maintenance on the agent memory store:
/// 1. Recalculates FreshnessAt for active items
/// 2. Auto-archives low-value items
/// 3. Batch re-embeds items without embeddings
/// 4. Detects stale evidence (orphaned references)
///
/// P3.INF-02: Background maintenance service.
/// </summary>
public class BackgroundMaintenanceService
{
    private readonly IAppDbContext _db;
    private readonly RetentionService _retentionService;
    private readonly MemoryEmbeddingService _embeddingService;
    private readonly ILogger<BackgroundMaintenanceService> _logger;

    // Items whose FreshnessAt is older than this (in days) are refreshed
    private const int StaleFreshnessDays = 30;

    public BackgroundMaintenanceService(
        IAppDbContext db,
        RetentionService retentionService,
        MemoryEmbeddingService embeddingService,
        ILogger<BackgroundMaintenanceService> logger)
    {
        _db = db;
        _retentionService = retentionService;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full maintenance cycle across all workspaces.
    /// </summary>
    public async Task<MaintenanceReport> RunMaintenanceCycleAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("RunMaintenanceCycle: starting maintenance cycle");

        var report = new MaintenanceReport();

        // Step 1: Recalculate FreshnessAt for all active items
        report.FreshnessUpdated = await RefreshFreshnessAsync(ct);

        // Step 2: Auto-archive candidates across all workspaces
        report.AutoArchived = await AutoArchiveAsync(ct);

        // Step 3: Batch re-embed items without embeddings
        report.Embedded = await BatchReEmbedAsync(ct);

        // Step 4: Detect stale evidence
        report.StaleEvidenceDetected = await DetectStaleEvidenceAsync(ct);

        _logger.LogInformation(
            "RunMaintenanceCycle: completed. FreshnessUpdated={Freshness}, AutoArchived={Archived}, Embedded={Embedded}, StaleEvidence={Stale}",
            report.FreshnessUpdated, report.AutoArchived, report.Embedded, report.StaleEvidenceDetected);

        return report;
    }

    /// <summary>
    /// Step 1: Recalculates FreshnessAt for all active memory items.
    /// Sets FreshnessAt to the current time if it is null or very old (older than StaleFreshnessDays).
    /// </summary>
    private async Task<int> RefreshFreshnessAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleThreshold = now.AddDays(-StaleFreshnessDays);

        var items = await _db.AgentMemoryItems
            .Where(i => i.Status == MemoryStatus.Active)
            .ToListAsync(ct);

        var updatedCount = 0;
        foreach (var item in items)
        {
            if (item.FreshnessAt == null || item.FreshnessAt < staleThreshold)
            {
                item.FreshnessAt = now;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogDebug(
            "RefreshFreshness: updated {Updated}/{Total} active items",
            updatedCount, items.Count);

        return updatedCount;
    }

    /// <summary>
    /// Step 2: Auto-archives low-value items across all workspaces.
    /// Calls RetentionService.GetArchiveCandidatesAsync for each distinct workspace
    /// and archives the identified candidates.
    /// </summary>
    private async Task<int> AutoArchiveAsync(CancellationToken ct)
    {
        // Get all distinct workspace IDs from active memory items
        var workspaceIds = await _db.AgentMemoryItems
            .Where(i => i.Status == MemoryStatus.Active)
            .Select(i => i.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);

        var archivedCount = 0;

        foreach (var workspaceId in workspaceIds)
        {
            var candidateIds = await _retentionService.GetArchiveCandidatesAsync(workspaceId, ct);

            foreach (var itemId in candidateIds)
            {
                // Only archive confirmed items (Archive() requires Confirmed state)
                var item = await _db.AgentMemoryItems
                    .FirstOrDefaultAsync(i => i.Id == itemId, ct);

                if (item == null)
                    continue;

                if (item.AdmissionState == AdmissionState.Confirmed
                    && item.Status == MemoryStatus.Active)
                {
                    await _retentionService.ArchiveItemAsync(itemId, ct);
                    archivedCount++;
                }
            }
        }

        _logger.LogDebug(
            "AutoArchive: archived {Count} items across {WorkspaceCount} workspaces",
            archivedCount, workspaceIds.Count);

        return archivedCount;
    }

    /// <summary>
    /// Step 3: Batch re-embeds memory items that do not yet have embeddings.
    /// Processes all workspaces.
    /// </summary>
    private async Task<int> BatchReEmbedAsync(CancellationToken ct)
    {
        // Get all distinct workspace IDs from active memory items
        var workspaceIds = await _db.AgentMemoryItems
            .Where(i => i.Status == MemoryStatus.Active)
            .Select(i => i.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);

        var totalEmbedded = 0;

        foreach (var workspaceId in workspaceIds)
        {
            var embedded = await _embeddingService.BatchEmbedAsync(workspaceId, batchSize: 50, ct);
            totalEmbedded += embedded;
        }

        _logger.LogDebug(
            "BatchReEmbed: embedded {Count} items across {WorkspaceCount} workspaces",
            totalEmbedded, workspaceIds.Count);

        return totalEmbedded;
    }

    /// <summary>
    /// Step 4: Detects stale evidence whose ReferenceId no longer exists in the
    /// source tables. For now, this flags evidence records whose ReferenceId
    /// cannot be resolved to any known source (empty or orphaned).
    /// </summary>
    private async Task<int> DetectStaleEvidenceAsync(CancellationToken ct)
    {
        var allEvidence = await _db.AgentMemoryEvidences
            .ToListAsync(ct);

        if (allEvidence.Count == 0)
            return 0;

        // Collect all reference IDs to check
        var documentChunkIds = await _db.DocumentChunks
            .Select(c => c.Id.ToString())
            .ToHashSetAsync(ct);

        var documentIds = await _db.Documents
            .Select(d => d.Id.ToString())
            .ToHashSetAsync(ct);

        var staleCount = 0;
        foreach (var evidence in allEvidence)
        {
            if (string.IsNullOrWhiteSpace(evidence.ReferenceId))
            {
                staleCount++;
                continue;
            }

            // For DocumentChunk evidence, check if the chunk still exists
            if (evidence.EvidenceKind == EvidenceKind.DocumentChunk)
            {
                if (!documentChunkIds.Contains(evidence.ReferenceId))
                {
                    staleCount++;
                }
            }
            // For Report evidence, check if the document still exists
            else if (evidence.EvidenceKind == EvidenceKind.Report)
            {
                if (!documentIds.Contains(evidence.ReferenceId))
                {
                    staleCount++;
                }
            }
            // For other evidence types, consider non-GUID reference IDs that look stale
            // (UserInput, SessionEvent, etc. are transient and not tracked in source tables)
        }

        _logger.LogDebug(
            "DetectStaleEvidence: found {Stale}/{Total} stale evidence records",
            staleCount, allEvidence.Count);

        return staleCount;
    }
}

/// <summary>
/// Report from a maintenance cycle run.
/// </summary>
public class MaintenanceReport
{
    /// <summary>
    /// Number of items whose FreshnessAt was updated.
    /// </summary>
    public int FreshnessUpdated { get; set; }

    /// <summary>
    /// Number of items auto-archived.
    /// </summary>
    public int AutoArchived { get; set; }

    /// <summary>
    /// Number of items embedded (or re-embedded).
    /// </summary>
    public int Embedded { get; set; }

    /// <summary>
    /// Number of stale evidence records detected.
    /// </summary>
    public int StaleEvidenceDetected { get; set; }
}
