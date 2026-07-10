using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== Search Request =====

public class SearchRequest
{
    public Guid? TopicId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string SearchType { get; set; } = "hybrid"; // keyword, vector, hybrid

    [JsonPropertyName("filters")]
    public SearchFilters? Filters { get; set; }

    public int Limit { get; set; } = 20;
}

public class SearchFilters
{
    public string? SourceType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? MinValueScore { get; set; }
    public List<Guid>? TagIds { get; set; }
    public List<Guid>? EntityIds { get; set; }
    public string? Domain { get; set; }
}

// ===== Search Response =====

public class SearchResult
{
    public string Query { get; set; } = string.Empty;
    public string SearchType { get; set; } = string.Empty;
    public int Total { get; set; }
    public List<SearchResultItem> Items { get; set; } = new();
    public SearchDebugInfo? DebugInfo { get; set; }
}

public class SearchResultItem
{
    public Guid DocumentId { get; set; }
    public Guid ChunkId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceDomain { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int? ValueScore { get; set; }
    public double Score { get; set; }
    public ScoreDetail? ScoreDetail { get; set; }
}

public class ScoreDetail
{
    public double KeywordScore { get; set; }
    public double VectorScore { get; set; }
    public double FreshnessScore { get; set; }
    public double ValueScore { get; set; }
    public double MetadataScore { get; set; }
}

public class SearchDebugInfo
{
    public string? SearchMode { get; set; }
    public long? LatencyMs { get; set; }
    public int? KeywordMatchCount { get; set; }
    public int? VectorMatchCount { get; set; }
    public string? RewrittenQuery { get; set; }
}
