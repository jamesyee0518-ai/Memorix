using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Suggests memory item consolidation (merges) by finding similar candidate items.
/// Only suggests merges for high-value memory types (Decision, Rationale, Constraint, Blocker).
/// Returns suggestions only — does not auto-merge.
/// </summary>
public class ConsolidationService
{
    private readonly IAppDbContext _db;
    private readonly MemoryRetriever _retriever;
    private readonly ILogger<ConsolidationService> _logger;

    // High-value memory kinds eligible for consolidation suggestions
    private static readonly HashSet<MemoryKind> HighValueKinds = new()
    {
        MemoryKind.Decision,
        MemoryKind.Rationale,
        MemoryKind.Constraint,
        MemoryKind.Blocker
    };

    // Minimum similarity threshold for suggesting a merge
    private const double MinSimilarityThreshold = 0.6;

    public ConsolidationService(
        IAppDbContext db,
        MemoryRetriever retriever,
        ILogger<ConsolidationService> logger)
    {
        _db = db;
        _retriever = retriever;
        _logger = logger;
    }

    /// <summary>
    /// Finds candidate memory item pairs that may benefit from consolidation (merging).
    /// Uses FTS-based similarity to identify potential duplicates or closely related items.
    /// Only considers high-value types: Decision, Rationale, Constraint, Blocker.
    /// </summary>
    /// <returns>A list of consolidation suggestions (not auto-merged).</returns>
    public async Task<List<ConsolidationSuggestion>> FindMergeCandidatesAsync(
        Guid workspaceId,
        CancellationToken ct = default)
    {
        // Load all active high-value memory items for the workspace
        var items = await _db.AgentMemoryItems
            .Where(i => i.WorkspaceId == workspaceId
                        && i.Status == MemoryStatus.Active
                        && (i.Kind == MemoryKind.Decision
                            || i.Kind == MemoryKind.Rationale
                            || i.Kind == MemoryKind.Constraint
                            || i.Kind == MemoryKind.Blocker))
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);

        if (items.Count < 2)
        {
            _logger.LogDebug(
                "FindMergeCandidates: fewer than 2 high-value items in workspace {WorkspaceId}",
                workspaceId);
            return new List<ConsolidationSuggestion>();
        }

        var suggestions = new List<ConsolidationSuggestion>();
        var processedPairs = new HashSet<string>();

        // Compare each pair of items for similarity
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var itemA = items[i];
                var itemB = items[j];

                // Skip if different kinds (only suggest merges within same kind)
                if (itemA.Kind != itemB.Kind)
                    continue;

                // Skip already-processed pairs
                var pairKey = itemA.Id.CompareTo(itemB.Id) < 0
                    ? $"{itemA.Id}:{itemB.Id}"
                    : $"{itemB.Id}:{itemA.Id}";
                if (processedPairs.Contains(pairKey))
                    continue;

                var similarity = ComputeSimilarity(itemA, itemB);

                if (similarity >= MinSimilarityThreshold)
                {
                    processedPairs.Add(pairKey);
                    suggestions.Add(new ConsolidationSuggestion
                    {
                        ItemIds = new List<Guid> { itemA.Id, itemB.Id },
                        Reason = BuildReason(itemA, itemB, similarity),
                        SimilarityScore = similarity
                    });
                }
            }
        }

        // Sort by similarity descending
        suggestions = suggestions
            .OrderByDescending(s => s.SimilarityScore)
            .ToList();

        _logger.LogInformation(
            "FindMergeCandidates: found {Count} merge candidates in workspace {WorkspaceId} from {ItemCount} items",
            suggestions.Count, workspaceId, items.Count);

        return suggestions;
    }

    /// <summary>
    /// Computes a text similarity score (0.0 - 1.0) between two memory items
    /// using Jaccard similarity on tokenized title + content.
    /// </summary>
    private static double ComputeSimilarity(
        Domain.Entities.AgentMemoryItem itemA,
        Domain.Entities.AgentMemoryItem itemB)
    {
        var tokensA = Tokenize(itemA.Title + " " + itemA.Content);
        var tokensB = Tokenize(itemB.Title + " " + itemB.Content);

        if (tokensA.Count == 0 || tokensB.Count == 0)
            return 0.0;

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>
    /// Tokenizes text into lowercase tokens for similarity comparison.
    /// </summary>
    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>();

        return text
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '-', '_' },
                   StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 1) // Skip single-character tokens
            .ToHashSet();
    }

    /// <summary>
    /// Builds a human-readable reason for the merge suggestion.
    /// </summary>
    private static string BuildReason(
        Domain.Entities.AgentMemoryItem itemA,
        Domain.Entities.AgentMemoryItem itemB,
        double similarity)
    {
        return $"Two {itemA.Kind.ToString().ToLowerInvariant()} items share {similarity:P0} text similarity: " +
               $"'{Truncate(itemA.Title, 60)}' and '{Truncate(itemB.Title, 60)}'. " +
               $"Consider merging to reduce redundancy.";
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
    }
}

/// <summary>
/// Represents a suggestion to consolidate (merge) two or more memory items.
/// </summary>
public class ConsolidationSuggestion
{
    /// <summary>
    /// The IDs of the memory items that could be merged.
    /// </summary>
    public List<Guid> ItemIds { get; set; } = new();

    /// <summary>
    /// Human-readable explanation of why these items should be merged.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Similarity score (0.0 - 1.0) indicating how similar the items are.
    /// </summary>
    public double SimilarityScore { get; set; }
}
