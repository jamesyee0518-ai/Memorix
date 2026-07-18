namespace KnowledgeEngine.Application.Interfaces;

public interface IAISummaryService
{
    Task<AiSummaryResult> SummarizeAsync(string title, string contentText, string sourceType, CancellationToken ct = default);
}

public class AiSummaryResult
{
    public string? Summary { get; set; }
    public string? OneSentenceConclusion { get; set; }
    /// <summary>
    /// Serialized JSON array of key point objects (text/importance/evidence),
    /// preserved as a raw JSON string for the frontend to parse directly.
    /// </summary>
    public string? KeyPoints { get; set; }
    public List<string> BusinessSignals { get; set; } = new();
    public List<string> TechnicalSignals { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public List<string> Opportunities { get; set; } = new();
    public List<string> ReusableMaterials { get; set; } = new();
    public List<string> RecommendedTags { get; set; } = new();
    public int? ValueScore { get; set; }
    public int? QualityScore { get; set; }
    public string? ValueScoreReason { get; set; }
    public bool ShouldDeepProcess { get; set; } = true;
    public string? AiRawOutput { get; set; }
    public string? AiModel { get; set; }
    public string? PromptVersion { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public List<TagResult> Tags { get; set; } = new();
    public List<EntityResult> Entities { get; set; } = new();
}

public class TagResult
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Description { get; set; }
    public decimal? Confidence { get; set; }
    public string? Reason { get; set; }
}

public class EntityResult
{
    public string? Name { get; set; }
    public string? EntityType { get; set; }
    public string? Description { get; set; }
    public decimal? Confidence { get; set; }
    public decimal? Importance { get; set; }
    public int MentionCount { get; set; } = 1;
    public List<string>? Aliases { get; set; }
    public List<string>? Examples { get; set; }
    public string? Role { get; set; }
    public string? Sentiment { get; set; }
    public string? FirstMention { get; set; }
}
