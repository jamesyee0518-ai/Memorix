using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Detects conflicting memory items — pairs of confirmed items with the same Kind,
/// similar Titles (fuzzy match), but contradictory Content.
///
/// Only applies to factual assertion types: Fact, Decision, Constraint.
///
/// P3.INF-03: Conflict detection service.
/// </summary>
public class ConflictDetectionService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ConflictDetectionService> _logger;

    // Memory kinds that are eligible for conflict detection
    private static readonly HashSet<MemoryKind> ConflictEligibleKinds = new()
    {
        MemoryKind.Fact,
        MemoryKind.Decision,
        MemoryKind.Constraint
    };

    // Minimum title similarity (via Contains) to consider two items as related
    private const double MinSimilarityForConflict = 0.3;

    public ConflictDetectionService(
        IAppDbContext db,
        ILogger<ConflictDetectionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Detects conflicting memory item pairs within a workspace.
    /// A conflict is identified when two confirmed items share the same Kind,
    /// have similar Titles (fuzzy match using Contains), but different Content.
    /// </summary>
    public async Task<List<MemoryConflict>> DetectConflictsAsync(
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var items = await _db.AgentMemoryItems
            .Where(i => i.WorkspaceId == workspaceId
                        && i.AdmissionState == AdmissionState.Confirmed
                        && i.Status == MemoryStatus.Active
                        && (i.Kind == MemoryKind.Fact
                            || i.Kind == MemoryKind.Decision
                            || i.Kind == MemoryKind.Constraint))
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);

        if (items.Count < 2)
        {
            _logger.LogDebug(
                "DetectConflicts: fewer than 2 eligible items in workspace {WorkspaceId}",
                workspaceId);
            return new List<MemoryConflict>();
        }

        var conflicts = new List<MemoryConflict>();

        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var itemA = items[i];
                var itemB = items[j];

                // Must be same Kind
                if (itemA.Kind != itemB.Kind)
                    continue;

                var similarity = ComputeTitleSimilarity(itemA.Title, itemB.Title);

                if (similarity < MinSimilarityForConflict)
                    continue;

                // Titles are similar; check if content is contradictory (different)
                if (IsContradictory(itemA.Content, itemB.Content))
                {
                    conflicts.Add(new MemoryConflict
                    {
                        ItemAId = itemA.Id,
                        ItemBId = itemB.Id,
                        Kind = itemA.Kind.ToString(),
                        Reason = BuildReason(itemA, itemB, similarity),
                        SimilarityScore = similarity
                    });
                }
            }
        }

        _logger.LogInformation(
            "DetectConflicts: found {Count} conflicts in workspace {WorkspaceId} from {ItemCount} items",
            conflicts.Count, workspaceId, items.Count);

        return conflicts;
    }

    /// <summary>
    /// Detects conflicting memory item pairs within a specific session.
    /// </summary>
    public async Task<List<MemoryConflict>> GetSessionConflictsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var items = await _db.AgentMemoryItems
            .Where(i => i.SessionId == sessionId
                        && i.AdmissionState == AdmissionState.Confirmed
                        && i.Status == MemoryStatus.Active
                        && (i.Kind == MemoryKind.Fact
                            || i.Kind == MemoryKind.Decision
                            || i.Kind == MemoryKind.Constraint))
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);

        if (items.Count < 2)
        {
            _logger.LogDebug(
                "GetSessionConflicts: fewer than 2 eligible items in session {SessionId}",
                sessionId);
            return new List<MemoryConflict>();
        }

        var conflicts = new List<MemoryConflict>();

        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var itemA = items[i];
                var itemB = items[j];

                if (itemA.Kind != itemB.Kind)
                    continue;

                var similarity = ComputeTitleSimilarity(itemA.Title, itemB.Title);

                if (similarity < MinSimilarityForConflict)
                    continue;

                if (IsContradictory(itemA.Content, itemB.Content))
                {
                    conflicts.Add(new MemoryConflict
                    {
                        ItemAId = itemA.Id,
                        ItemBId = itemB.Id,
                        Kind = itemA.Kind.ToString(),
                        Reason = BuildReason(itemA, itemB, similarity),
                        SimilarityScore = similarity
                    });
                }
            }
        }

        _logger.LogInformation(
            "GetSessionConflicts: found {Count} conflicts in session {SessionId} from {ItemCount} items",
            conflicts.Count, sessionId, items.Count);

        return conflicts;
    }

    /// <summary>
    /// Computes a title similarity score (0.0 - 1.0) using fuzzy matching.
    /// Uses bidirectional Contains: if either title contains the other (or significant
    /// token overlap), they are considered similar.
    /// </summary>
    private static double ComputeTitleSimilarity(string titleA, string titleB)
    {
        if (string.IsNullOrWhiteSpace(titleA) || string.IsNullOrWhiteSpace(titleB))
            return 0.0;

        var a = titleA.ToLowerInvariant().Trim();
        var b = titleB.ToLowerInvariant().Trim();

        // Exact match
        if (a == b)
            return 1.0;

        // Bidirectional Contains
        if (a.Contains(b) || b.Contains(a))
            return 0.8;

        // Token-based Jaccard similarity
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);

        if (tokensA.Count == 0 || tokensB.Count == 0)
            return 0.0;

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>
    /// Determines whether two content strings are contradictory.
    /// Two items with similar titles but different (non-identical) content are
    /// considered potentially contradictory.
    /// </summary>
    private static bool IsContradictory(string contentA, string contentB)
    {
        if (string.IsNullOrEmpty(contentA) && string.IsNullOrEmpty(contentB))
            return false;

        // Normalize for comparison
        var a = (contentA ?? string.Empty).Trim().ToLowerInvariant();
        var b = (contentB ?? string.Empty).Trim().ToLowerInvariant();

        // If content is identical, no conflict
        if (a == b)
            return false;

        // If one is empty and the other is not, that's not a contradiction
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        return true;
    }

    /// <summary>
    /// Tokenizes text into lowercase tokens.
    /// </summary>
    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>();

        return text
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '-', '_' },
                   StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 1)
            .ToHashSet();
    }

    /// <summary>
    /// Builds a human-readable reason for the conflict.
    /// </summary>
    private static string BuildReason(
        AgentMemoryItem itemA,
        AgentMemoryItem itemB,
        double similarity)
    {
        return $"Two {itemA.Kind.ToString().ToLowerInvariant()} items with similar titles " +
               $"('{Truncate(itemA.Title, 60)}' vs '{Truncate(itemB.Title, 60)}') " +
               $"have contradictory content (similarity={similarity:F2}). " +
               $"Item A: '{Truncate(itemA.Content, 80)}', Item B: '{Truncate(itemB.Content, 80)}'.";
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
    }
}

/// <summary>
/// Represents a detected conflict between two memory items.
/// </summary>
public class MemoryConflict
{
    /// <summary>
    /// The ID of the first conflicting item.
    /// </summary>
    public Guid ItemAId { get; set; }

    /// <summary>
    /// The ID of the second conflicting item.
    /// </summary>
    public Guid ItemBId { get; set; }

    /// <summary>
    /// The MemoryKind of the conflicting items.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable explanation of the conflict.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Similarity score (0.0 - 1.0) between the two items' titles.
    /// </summary>
    public double SimilarityScore { get; set; }
}
