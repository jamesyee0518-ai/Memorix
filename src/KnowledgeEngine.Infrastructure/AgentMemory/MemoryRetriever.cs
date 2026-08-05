using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Full-text memory retrieval service using EF Core LIKE/Contains queries.
/// Scores results using a weighted formula:
///   score = 0.35*semantic + 0.25*confidence + 0.20*freshness + 0.10*importance + 0.10*accessCount
///
/// In hybrid mode (P2.INF-03), if embeddings are available for items, the semantic
/// score is computed using cosine similarity instead of FTS-based relevance.
/// </summary>
public class MemoryRetriever
{
    private readonly IAppDbContext _db;
    private readonly ILogger<MemoryRetriever> _logger;

    // Scoring weights (semantic replaces relevance in hybrid mode)
    private const double WeightSemantic = 0.35;
    private const double WeightRelevance = 0.35; // Alias for FTS fallback
    private const double WeightConfidence = 0.25;
    private const double WeightFreshness = 0.20;
    private const double WeightImportance = 0.10;
    private const double WeightAccessCount = 0.10;

    // Priority boosts for admission states
    private const double ConfirmedBoost = 0.15;
    private const double CandidatePenalty = 0.10;

    // Freshness decay: items older than 30 days get a freshness score approaching 0
    private const int FreshnessDecayDays = 30;

    // Embedding type used for agent memory items in ChunkEmbeddings
    private const string MemoryEmbeddingType = "agent_memory";

    public MemoryRetriever(IAppDbContext db, ILogger<MemoryRetriever> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Searches memory items for the given user and workspace.
    /// Applies text matching, workspace/topic/kind/admission-state filters, and computes a weighted score.
    /// </summary>
    public async Task<List<MemoryItemDto>> SearchAsync(
        Guid userId,
        Guid workspaceId,
        SearchMemoryInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Query))
        {
            // No query: return recent items ordered by creation date
            var recentItems = await _db.AgentMemoryItems
                .Where(i => i.WorkspaceId == workspaceId
                            && i.OwnerUserId == userId
                            && i.Status == MemoryStatus.Active)
                .OrderByDescending(i => i.CreatedAt)
                .Skip(input.Offset)
                .Take(input.Limit)
                .ToListAsync(ct);

            return recentItems.Select(MapToDto).ToList();
        }

        // Build the base query with workspace and user filtering
        var queryTerms = input.Query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

        var query = _db.AgentMemoryItems
            .Where(i => i.WorkspaceId == workspaceId
                        && i.OwnerUserId == userId
                        && i.Status == MemoryStatus.Active);

        // Apply session filter
        if (input.SessionId.HasValue)
        {
            query = query.Where(i => i.SessionId == input.SessionId.Value);
        }

        // Apply project filter — join through session to ProjectId
        if (input.ProjectId.HasValue)
        {
            query = query.Where(i => i.Session != null && i.Session.ProjectId == input.ProjectId.Value);
        }

        // Apply kind filter (single kind)
        if (!string.IsNullOrWhiteSpace(input.Kind) &&
            Enum.TryParse<MemoryKind>(input.Kind, true, out var kindFilter))
        {
            query = query.Where(i => i.Kind == kindFilter);
        }

        // Apply types filter (multiple kinds)
        if (input.Types != null && input.Types.Count > 0)
        {
            var parsedKinds = input.Types
                .Where(t => Enum.TryParse<MemoryKind>(t, true, out _))
                .Select(t => Enum.Parse<MemoryKind>(t, true))
                .ToList();
            if (parsedKinds.Count > 0)
            {
                query = query.Where(i => parsedKinds.Contains(i.Kind));
            }
        }

        // Apply admission state filter
        if (!string.IsNullOrWhiteSpace(input.AdmissionState) &&
            Enum.TryParse<AdmissionState>(input.AdmissionState, true, out var stateFilter))
        {
            query = query.Where(i => i.AdmissionState == stateFilter);
        }

        // Text search using Contains (LIKE) for each query term
        // A match in either Title or Content counts
        foreach (var term in queryTerms)
        {
            var localTerm = term;
            query = query.Where(i =>
                i.Title.ToLower().Contains(localTerm) ||
                (i.Content != null && i.Content.ToLower().Contains(localTerm)) ||
                (i.Summary != null && i.Summary.ToLower().Contains(localTerm)));
        }

        // Fetch matching items (before pagination, since we need to compute scores)
        var matchedItems = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(input.Limit * 3) // Fetch 3x to allow for re-ranking
            .ToListAsync(ct);

        if (matchedItems.Count == 0)
        {
            return new List<MemoryItemDto>();
        }

        // Load evidence and access counts for the matched items
        var itemIds = matchedItems.Select(i => i.Id).ToList();
        var evidences = await _db.AgentMemoryEvidences
            .Where(e => itemIds.Contains(e.MemoryItemId))
            .ToListAsync(ct);
        var accessCounts = await _db.AgentMemoryAccessLogs
            .Where(a => a.MemoryItemId != null && itemIds.Contains(a.MemoryItemId.Value) && a.Action == "read")
            .GroupBy(a => a.MemoryItemId!.Value)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        // Compute scores and rank
        var now = DateTime.UtcNow;
        var maxAccessCount = accessCounts.Count > 0 ? accessCounts.Values.Max() : 1;

        var scoredItems = matchedItems
            .Select(item =>
            {
                var relevance = ComputeRelevance(item, queryTerms);
                var confidence = (double)(item.Confidence);
                var freshness = ComputeFreshness(item, now);
                var importance = item.Importance / 10.0;
                var accessCount = accessCounts.TryGetValue(item.Id, out var count) ? count : 0;
                var normalizedAccess = (double)accessCount / maxAccessCount;

                var score = WeightRelevance * relevance
                          + WeightConfidence * confidence
                          + WeightFreshness * freshness
                          + WeightImportance * importance
                          + WeightAccessCount * normalizedAccess;

                // Attach evidence
                var dto = MapToDto(item);
                dto.Evidence = evidences
                    .Where(e => e.MemoryItemId == item.Id)
                    .Select(e => new EvidenceDto
                    {
                        Id = e.Id,
                        MemoryItemId = e.MemoryItemId,
                        EvidenceKind = e.EvidenceKind.ToString(),
                        ReferenceId = e.ReferenceId,
                        Locator = e.Locator,
                        Relation = e.Relation,
                        CapturedAt = e.CapturedAt
                    }).ToList();

                return (Dto: dto, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .Skip(input.Offset)
            .Take(input.Limit)
            .Select(x => x.Dto)
            .ToList();

        _logger.LogDebug(
            "Memory search for user {UserId} in workspace {WorkspaceId}: query='{Query}', matched={Matched}, returned={Returned}",
            userId, workspaceId, input.Query, matchedItems.Count, scoredItems.Count);

        return scoredItems;
    }

    /// <summary>
    /// Hybrid search that combines FTS-based text matching with optional vector similarity.
    /// If a query embedding is provided, items with embeddings are scored using cosine similarity
    /// for the semantic component. Items without embeddings fall back to FTS-based relevance.
    ///
    /// Scoring formula (unchanged from Phase 1):
    ///   score = 0.35*semantic + 0.25*confidence + 0.20*freshness + 0.10*importance + 0.10*accessCount
    ///
    /// Priority adjustments:
    /// - Confirmed items get a +0.15 boost (appear in L2)
    /// - Candidate items get a -0.10 penalty (appear in L1 hints only, lower priority)
    /// </summary>
    public async Task<List<MemoryItemDto>> HybridSearchAsync(
        Guid userId,
        Guid workspaceId,
        SearchMemoryInput input,
        float[]? queryEmbedding = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Query))
        {
            // No query: delegate to the base SearchAsync for recent items
            return await SearchAsync(userId, workspaceId, input, ct);
        }

        var queryTerms = input.Query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

        var query = _db.AgentMemoryItems
            .Where(i => i.WorkspaceId == workspaceId
                        && i.OwnerUserId == userId
                        && i.Status == MemoryStatus.Active);

        // Apply session filter
        if (input.SessionId.HasValue)
        {
            query = query.Where(i => i.SessionId == input.SessionId.Value);
        }

        // Apply project filter — join through session to ProjectId
        if (input.ProjectId.HasValue)
        {
            query = query.Where(i => i.Session != null && i.Session.ProjectId == input.ProjectId.Value);
        }

        // Apply kind filter (single kind)
        if (!string.IsNullOrWhiteSpace(input.Kind) &&
            Enum.TryParse<MemoryKind>(input.Kind, true, out var kindFilter))
        {
            query = query.Where(i => i.Kind == kindFilter);
        }

        // Apply types filter (multiple kinds)
        if (input.Types != null && input.Types.Count > 0)
        {
            var parsedKinds = input.Types
                .Where(t => Enum.TryParse<MemoryKind>(t, true, out _))
                .Select(t => Enum.Parse<MemoryKind>(t, true))
                .ToList();
            if (parsedKinds.Count > 0)
            {
                query = query.Where(i => parsedKinds.Contains(i.Kind));
            }
        }

        // Apply admission state filter
        if (!string.IsNullOrWhiteSpace(input.AdmissionState) &&
            Enum.TryParse<AdmissionState>(input.AdmissionState, true, out var stateFilter))
        {
            query = query.Where(i => i.AdmissionState == stateFilter);
        }

        // Text search using Contains (LIKE) for each query term
        foreach (var term in queryTerms)
        {
            var localTerm = term;
            query = query.Where(i =>
                i.Title.ToLower().Contains(localTerm) ||
                (i.Content != null && i.Content.ToLower().Contains(localTerm)) ||
                (i.Summary != null && i.Summary.ToLower().Contains(localTerm)));
        }

        // Fetch matching items
        var matchedItems = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(input.Limit * 3)
            .ToListAsync(ct);

        if (matchedItems.Count == 0)
        {
            return new List<MemoryItemDto>();
        }

        // Load evidence and access counts
        var itemIds = matchedItems.Select(i => i.Id).ToList();
        var evidences = await _db.AgentMemoryEvidences
            .Where(e => itemIds.Contains(e.MemoryItemId))
            .ToListAsync(ct);
        var accessCounts = await _db.AgentMemoryAccessLogs
            .Where(a => a.MemoryItemId != null && itemIds.Contains(a.MemoryItemId.Value) && a.Action == "read")
            .GroupBy(a => a.MemoryItemId!.Value)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        // Load embeddings for items if query embedding is available
        Dictionary<Guid, float[]>? itemEmbeddings = null;
        if (queryEmbedding != null && queryEmbedding.Length > 0)
        {
            var embeddingRecords = await _db.ChunkEmbeddings
                .Where(e => e.EmbeddingType == MemoryEmbeddingType
                            && itemIds.Contains(e.ChunkId)
                            && e.EmbeddingJson != null)
                .ToListAsync(ct);

            itemEmbeddings = new Dictionary<Guid, float[]>();
            foreach (var rec in embeddingRecords)
            {
                try
                {
                    var vec = JsonSerializer.Deserialize<float[]>(rec.EmbeddingJson!);
                    if (vec != null && vec.Length > 0)
                    {
                        itemEmbeddings[rec.ChunkId] = vec;
                    }
                }
                catch
                {
                    // Skip malformed embeddings
                }
            }

            _logger.LogDebug(
                "HybridSearch: loaded {EmbeddingCount}/{ItemCount} embeddings for semantic scoring",
                itemEmbeddings.Count, itemIds.Count);
        }

        // Compute scores and rank
        var now = DateTime.UtcNow;
        var maxAccessCount = accessCounts.Count > 0 ? accessCounts.Values.Max() : 1;

        var scoredItems = matchedItems
            .Select(item =>
            {
                // Semantic score: use cosine similarity if embeddings available, else FTS relevance
                double semantic;
                if (queryEmbedding != null && itemEmbeddings != null
                    && itemEmbeddings.TryGetValue(item.Id, out var itemVec))
                {
                    semantic = CosineSimilarity(queryEmbedding, itemVec);
                }
                else
                {
                    // Fall back to FTS-based relevance
                    semantic = ComputeRelevance(item, queryTerms);
                }

                var confidence = (double)(item.Confidence);
                var freshness = ComputeFreshness(item, now);
                var importance = item.Importance / 10.0;
                var accessCount = accessCounts.TryGetValue(item.Id, out var count) ? count : 0;
                var normalizedAccess = (double)accessCount / maxAccessCount;

                var score = WeightSemantic * semantic
                          + WeightConfidence * confidence
                          + WeightFreshness * freshness
                          + WeightImportance * importance
                          + WeightAccessCount * normalizedAccess;

                // Apply admission-state priority adjustments
                // Confirmed items get a boost (appear in L2)
                // Candidate items get a penalty (appear in L1 hints only, lower priority)
                if (item.AdmissionState == AdmissionState.Confirmed)
                {
                    score += ConfirmedBoost;
                }
                else if (item.AdmissionState == AdmissionState.Candidate)
                {
                    score -= CandidatePenalty;
                }

                var dto = MapToDto(item);
                dto.Evidence = evidences
                    .Where(e => e.MemoryItemId == item.Id)
                    .Select(e => new EvidenceDto
                    {
                        Id = e.Id,
                        MemoryItemId = e.MemoryItemId,
                        EvidenceKind = e.EvidenceKind.ToString(),
                        ReferenceId = e.ReferenceId,
                        Locator = e.Locator,
                        Relation = e.Relation,
                        CapturedAt = e.CapturedAt
                    }).ToList();

                return (Dto: dto, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .Skip(input.Offset)
            .Take(input.Limit)
            .Select(x => x.Dto)
            .ToList();

        _logger.LogDebug(
            "HybridSearch for user {UserId} in workspace {WorkspaceId}: query='{Query}', hasEmbedding={HasEmbedding}, matched={Matched}, returned={Returned}",
            userId, workspaceId, input.Query, queryEmbedding != null, matchedItems.Count, scoredItems.Count);

        return scoredItems;
    }

    /// <summary>
    /// Computes cosine similarity between two float vectors.
    /// Returns a value in the range [0, 1] for typical normalized embeddings.
    /// </summary>
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0.0;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA <= 0 || magB <= 0)
            return 0.0;

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    /// <summary>
    /// Computes a relevance score (0.0 - 1.0) based on how many query terms match the item.
    /// Title matches are weighted higher than content matches.
    /// </summary>
    private static double ComputeRelevance(Domain.Entities.AgentMemoryItem item, List<string> queryTerms)
    {
        if (queryTerms.Count == 0)
            return 0.5; // Default relevance when no query

        var titleLower = item.Title.ToLowerInvariant();
        var contentLower = (item.Content ?? string.Empty).ToLowerInvariant();
        var summaryLower = (item.Summary ?? string.Empty).ToLowerInvariant();

        var titleMatches = 0;
        var contentMatches = 0;

        foreach (var term in queryTerms)
        {
            if (titleLower.Contains(term))
                titleMatches++;
            if (contentLower.Contains(term) || summaryLower.Contains(term))
                contentMatches++;
        }

        var titleScore = (double)titleMatches / queryTerms.Count;
        var contentScore = (double)contentMatches / (queryTerms.Count * 2);

        // Title matches are worth 3x content matches
        var combined = (titleScore * 0.75) + (contentScore * 0.25);
        return Math.Min(1.0, combined);
    }

    /// <summary>
    /// Computes a freshness score (0.0 - 1.0) using exponential decay.
    /// Items updated within the last day get ~1.0; items older than FreshnessDecayDays get approaching 0.
    /// </summary>
    private static double ComputeFreshness(Domain.Entities.AgentMemoryItem item, DateTime now)
    {
        var referenceDate = item.FreshnessAt ?? item.UpdatedAt;
        var ageDays = (now - referenceDate).TotalDays;

        if (ageDays <= 0)
            return 1.0;

        // Exponential decay: e^(-ageDays / decayDays)
        var decayConstant = FreshnessDecayDays / Math.Log(100); // ~6.5 days for 99% decay at 30 days
        var freshness = Math.Exp(-ageDays / decayConstant);

        return Math.Max(0, Math.Min(1.0, freshness));
    }

    /// <summary>
    /// Maps a domain entity to a DTO.
    /// </summary>
    private static MemoryItemDto MapToDto(Domain.Entities.AgentMemoryItem item)
    {
        return new MemoryItemDto
        {
            Id = item.Id,
            SessionId = item.SessionId,
            WorkspaceId = item.WorkspaceId,
            OwnerUserId = item.OwnerUserId,
            AgentProfileId = item.AgentProfileId,
            Kind = item.Kind.ToString().ToLowerInvariant(),
            Title = item.Title,
            Content = item.Content,
            Summary = item.Summary,
            AdmissionState = item.AdmissionState.ToString().ToLowerInvariant(),
            Confidence = item.Confidence,
            Visibility = item.Visibility.ToString().ToLowerInvariant(),
            Importance = item.Importance,
            FreshnessAt = item.FreshnessAt,
            Status = item.Status.ToString().ToLowerInvariant(),
            CreatedAt = item.CreatedAt,
            Evidence = new List<EvidenceDto>()
        };
    }
}
