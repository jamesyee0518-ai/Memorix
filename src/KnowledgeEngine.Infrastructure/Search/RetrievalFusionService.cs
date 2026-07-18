using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Search;

public sealed class RetrievalFusionService : IRetrievalFusionService
{
    public List<SearchResultItem> Fuse(IReadOnlyDictionary<string, IReadOnlyList<SearchResultItem>> channels, int limit, int rankConstant = 60)
    {
        var merged = new Dictionary<string, SearchResultItem>();
        var raw = new Dictionary<string, double>();
        var matched = new Dictionary<string, HashSet<string>>();

        foreach (var (channel, items) in channels)
        {
            for (var rank = 0; rank < items.Count; rank++)
            {
                var candidate = items[rank];
                var key = candidate.ChunkGroupId.HasValue
                    ? $"group:{candidate.ChunkGroupId.Value}"
                    : $"{candidate.DocumentId}:{candidate.ChunkId}";
                raw[key] = raw.GetValueOrDefault(key) + 1d / (rankConstant + rank + 1d);
                if (!matched.TryGetValue(key, out var set)) matched[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(channel);
                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = candidate;
                }
                else
                {
                    existing.ScoreDetail ??= new ScoreDetail();
                    if (candidate.ScoreDetail != null)
                    {
                        existing.ScoreDetail.KeywordScore = Math.Max(existing.ScoreDetail.KeywordScore, candidate.ScoreDetail.KeywordScore);
                        existing.ScoreDetail.VectorScore = Math.Max(existing.ScoreDetail.VectorScore, candidate.ScoreDetail.VectorScore);
                        existing.ScoreDetail.FreshnessScore = Math.Max(existing.ScoreDetail.FreshnessScore, candidate.ScoreDetail.FreshnessScore);
                        existing.ScoreDetail.ValueScore = Math.Max(existing.ScoreDetail.ValueScore, candidate.ScoreDetail.ValueScore);
                        existing.ScoreDetail.MetadataScore = Math.Max(existing.ScoreDetail.MetadataScore, candidate.ScoreDetail.MetadataScore);
                    }
                    if (candidate.Score > existing.Score)
                    {
                        candidate.ScoreDetail = existing.ScoreDetail;
                        merged[key] = candidate;
                    }
                }
            }
        }

        var max = raw.Values.DefaultIfEmpty(1).Max();
        foreach (var (key, item) in merged)
        {
            item.FusionScore = raw[key];
            item.Score = max <= 0 ? 0 : raw[key] / max;
            item.MatchChannels = matched[key].OrderBy(x => x).ToList();
        }
        return merged.Values.OrderByDescending(x => x.FusionScore).ThenByDescending(x => x.Score).Take(limit).ToList();
    }
}
