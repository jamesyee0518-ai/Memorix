using System.Text.RegularExpressions;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Search;

/// <summary>
/// Dependency-free fallback reranker. The interface allows a local or cloud cross-encoder
/// to replace it without changing QA orchestration.
/// </summary>
public sealed class HeuristicRerankerService : IRerankerService
{
    public Task<List<SearchResultItem>> RerankAsync(string query, List<SearchResultItem> candidates,
        int maxChunks, int maxPerDocument, CancellationToken ct = default)
    {
        var queryTerms = Terms(query);
        var ranked = candidates.Select(candidate => new
            {
                Candidate = candidate,
                Score = candidate.Score + LexicalOverlap(queryTerms, candidate) * 0.15
                    + Math.Min(candidate.MatchChannels.Count, 3) * 0.02
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Candidate.FusionScore)
            .ToList();

        var result = new List<SearchResultItem>();
        var perDocument = new Dictionary<Guid, int>();
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ranked)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = entry.Candidate;
            var groupKey = candidate.ChunkGroupId?.ToString() ?? $"{candidate.DocumentId}:{candidate.ChunkId}";
            if (!groups.Add(groupKey)) continue;
            if (perDocument.GetValueOrDefault(candidate.DocumentId) >= maxPerDocument) continue;
            perDocument[candidate.DocumentId] = perDocument.GetValueOrDefault(candidate.DocumentId) + 1;
            candidate.Score = Math.Clamp(entry.Score, 0, 1);
            result.Add(candidate);
            if (result.Count >= maxChunks) break;
        }
        return Task.FromResult(result);
    }

    private static double LexicalOverlap(HashSet<string> queryTerms, SearchResultItem candidate)
    {
        if (queryTerms.Count == 0) return 0;
        var candidateTerms = Terms(string.Join(' ', candidate.TitleZh, candidate.TitleOriginal, candidate.LocalizedSnippet, candidate.OriginalSnippet));
        return queryTerms.Count(term => candidateTerms.Contains(term)) / (double)queryTerms.Count;
    }

    private static HashSet<string> Terms(string? text) => Regex.Matches((text ?? string.Empty).ToLowerInvariant(), @"[\p{L}\p{N}+#.-]{2,}")
        .Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
