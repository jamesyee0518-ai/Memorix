using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Computes quality and operational metrics for the agent memory store.
///
/// P3.OPS-01: Quality metrics for monitoring memory system health.
/// </summary>
public class MemoryMetricsService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<MemoryMetricsService> _logger;

    public MemoryMetricsService(
        IAppDbContext db,
        ILogger<MemoryMetricsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Calculates comprehensive quality metrics for the memory store,
    /// optionally filtered by workspace.
    /// </summary>
    public async Task<MemoryQualityMetrics> GetMetricsAsync(
        Guid? workspaceId = null,
        CancellationToken ct = default)
    {
        var query = _db.AgentMemoryItems.AsQueryable();

        if (workspaceId.HasValue)
        {
            query = query.Where(i => i.WorkspaceId == workspaceId.Value);
        }

        var items = await query.ToListAsync(ct);

        var total = items.Count;
        var confirmed = items.Count(i => i.AdmissionState == AdmissionState.Confirmed);
        var candidate = items.Count(i => i.AdmissionState == AdmissionState.Candidate
                                         || i.AdmissionState == AdmissionState.Qualified);
        var rejected = items.Count(i => i.AdmissionState == AdmissionState.Rejected);

        // Recall rate: confirmed / total
        var recallRate = total > 0 ? (double)confirmed / total : 0.0;

        // Adoption rate: confirmed / (confirmed + rejected)
        var adoptionDenominator = confirmed + rejected;
        var adoptionRate = adoptionDenominator > 0 ? (double)confirmed / adoptionDenominator : 0.0;

        // Rejection rate: rejected / total
        var rejectionRate = total > 0 ? (double)rejected / total : 0.0;

        // Conflict count: detect conflicting pairs (same Kind + similar title + different content)
        var conflictCount = CountConflicts(items);

        // Sanitization hit rate: items modified by sanitizer / total
        // We approximate this by counting access logs with action = "sanitize"
        var sanitizeActions = await _db.AgentMemoryAccessLogs
            .Where(a => a.Action == "sanitize")
            .Select(a => a.MemoryItemId)
            .Distinct()
            .CountAsync(ct);

        var sanitizationHitRate = total > 0 ? (double)sanitizeActions / total : 0.0;

        // Average confidence
        var averageConfidence = total > 0
            ? (double)items.Average(i => i.Confidence)
            : 0.0;

        // P95 latency: time between write and first read, from access logs
        var p95LatencyMs = await ComputeP95LatencyAsync(ct);

        // Estimated cost (rough): total items * $0.001 + embeddings * $0.0001
        var embeddingCount = await _db.ChunkEmbeddings
            .Where(e => e.EmbeddingType == "agent_memory")
            .CountAsync(ct);

        var estimatedCostUsd = (total * 0.001) + (embeddingCount * 0.0001);

        var metrics = new MemoryQualityMetrics
        {
            TotalMemoryItems = total,
            ConfirmedItems = confirmed,
            CandidateItems = candidate,
            RejectedItems = rejected,
            RecallRate = recallRate,
            AdoptionRate = adoptionRate,
            RejectionRate = rejectionRate,
            ConflictCount = conflictCount,
            SanitizationHitRate = sanitizationHitRate,
            AverageConfidence = averageConfidence,
            P95LatencyMs = p95LatencyMs,
            EstimatedCostUsd = estimatedCostUsd,
            EmbeddingCount = embeddingCount
        };

        _logger.LogInformation(
            "GetMetrics: total={Total}, confirmed={Confirmed}, candidate={Candidate}, rejected={Rejected}, " +
            "conflicts={Conflicts}, avgConfidence={AvgConf:F2}, p95Latency={P95}ms, cost=${Cost:F4}",
            metrics.TotalMemoryItems, metrics.ConfirmedItems, metrics.CandidateItems,
            metrics.RejectedItems, metrics.ConflictCount, metrics.AverageConfidence,
            metrics.P95LatencyMs, metrics.EstimatedCostUsd);

        return metrics;
    }

    /// <summary>
    /// Counts conflicting pairs among a set of memory items.
    /// A conflict exists when two confirmed, active items share the same Kind,
    /// have similar titles (Contains match), but different content.
    /// Returns the number of conflicting pairs (not the number of items involved).
    /// </summary>
    private static int CountConflicts(List<Domain.Entities.AgentMemoryItem> items)
    {
        var eligibleKinds = new HashSet<MemoryKind>
        {
            MemoryKind.Fact,
            MemoryKind.Decision,
            MemoryKind.Constraint
        };

        var eligible = items
            .Where(i => i.AdmissionState == AdmissionState.Confirmed
                        && i.Status == MemoryStatus.Active
                        && eligibleKinds.Contains(i.Kind))
            .ToList();

        if (eligible.Count < 2)
            return 0;

        var conflictCount = 0;

        for (var i = 0; i < eligible.Count; i++)
        {
            for (var j = i + 1; j < eligible.Count; j++)
            {
                var a = eligible[i];
                var b = eligible[j];

                if (a.Kind != b.Kind)
                    continue;

                var titleA = (a.Title ?? string.Empty).ToLowerInvariant();
                var titleB = (b.Title ?? string.Empty).ToLowerInvariant();

                // Fuzzy match: bidirectional Contains or significant token overlap
                if (titleA.Contains(titleB) || titleB.Contains(titleA)
                    || (titleA.Length > 0 && titleB.Length > 0 && HasTokenOverlap(titleA, titleB)))
                {
                    // Different content => conflict
                    var contentA = (a.Content ?? string.Empty).Trim().ToLowerInvariant();
                    var contentB = (b.Content ?? string.Empty).Trim().ToLowerInvariant();

                    if (!string.IsNullOrEmpty(contentA)
                        && !string.IsNullOrEmpty(contentB)
                        && contentA != contentB)
                    {
                        conflictCount++;
                    }
                }
            }
        }

        return conflictCount;
    }

    /// <summary>
    /// Checks whether two strings share at least one common token (length > 1).
    /// </summary>
    private static bool HasTokenOverlap(string a, string b)
    {
        var tokensA = a.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '-', '_' },
                              StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.ToLowerInvariant())
                       .Where(t => t.Length > 1)
                       .ToHashSet();

        var tokensB = b.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '-', '_' },
                              StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.ToLowerInvariant())
                       .Where(t => t.Length > 1);

        return tokensA.Overlaps(tokensB);
    }

    /// <summary>
    /// Computes the P95 latency between memory item creation (write) and the first
    /// read access, based on access logs.
    /// Returns milliseconds; 0 if no read-access data is available.
    /// </summary>
    private async Task<double> ComputeP95LatencyAsync(CancellationToken ct)
    {
        // Get all write logs (action = "write") with their memory item IDs and timestamps
        var writes = await _db.AgentMemoryAccessLogs
            .Where(a => a.Action == "write" && a.MemoryItemId != null)
            .Select(a => new { a.MemoryItemId, a.CreatedAt })
            .ToListAsync(ct);

        if (writes.Count == 0)
            return 0.0;

        // Get all read logs (action = "read") with their memory item IDs and timestamps
        var reads = await _db.AgentMemoryAccessLogs
            .Where(a => a.Action == "read" && a.MemoryItemId != null)
            .Select(a => new { a.MemoryItemId, a.CreatedAt })
            .ToListAsync(ct);

        if (reads.Count == 0)
            return 0.0;

        // For each item, find the earliest read after the write
        var latencies = new List<double>();

        var writesByItem = writes
            .GroupBy(w => w.MemoryItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(w => w.CreatedAt));

        var readsByItem = reads
            .GroupBy(r => r.MemoryItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(r => r.CreatedAt));

        foreach (var (itemId, writeTime) in writesByItem)
        {
            if (readsByItem.TryGetValue(itemId, out var firstRead))
            {
                if (firstRead >= writeTime)
                {
                    var latencyMs = (firstRead - writeTime).TotalMilliseconds;
                    if (latencyMs >= 0)
                        latencies.Add(latencyMs);
                }
            }
        }

        if (latencies.Count == 0)
            return 0.0;

        // Sort and get P95
        latencies.Sort();

        var p95Index = (int)Math.Ceiling(latencies.Count * 0.95) - 1;
        p95Index = Math.Max(0, Math.Min(p95Index, latencies.Count - 1));

        return latencies[p95Index];
    }
}

/// <summary>
/// Comprehensive quality and operational metrics for the agent memory system.
/// </summary>
public class MemoryQualityMetrics
{
    /// <summary>Total number of memory items.</summary>
    public int TotalMemoryItems { get; set; }

    /// <summary>Number of confirmed memory items.</summary>
    public int ConfirmedItems { get; set; }

    /// <summary>Number of candidate (candidate + qualified) memory items.</summary>
    public int CandidateItems { get; set; }

    /// <summary>Number of rejected memory items.</summary>
    public int RejectedItems { get; set; }

    /// <summary>Recall rate: confirmed / total.</summary>
    public double RecallRate { get; set; }

    /// <summary>Adoption rate: confirmed / (confirmed + rejected).</summary>
    public double AdoptionRate { get; set; }

    /// <summary>Rejection rate: rejected / total.</summary>
    public double RejectionRate { get; set; }

    /// <summary>Number of conflicting item pairs detected.</summary>
    public int ConflictCount { get; set; }

    /// <summary>Sanitization hit rate: items modified by sanitizer / total.</summary>
    public double SanitizationHitRate { get; set; }

    /// <summary>Average confidence across all memory items.</summary>
    public double AverageConfidence { get; set; }

    /// <summary>P95 latency (ms) between write and first read.</summary>
    public double P95LatencyMs { get; set; }

    /// <summary>Estimated monthly cost in USD (rough estimate).</summary>
    public double EstimatedCostUsd { get; set; }

    /// <summary>Number of memory item embeddings stored.</summary>
    public int EmbeddingCount { get; set; }
}
