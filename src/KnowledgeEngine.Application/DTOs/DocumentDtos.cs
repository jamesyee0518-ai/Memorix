namespace KnowledgeEngine.Application.DTOs;

public class DocumentListItem
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string AiStatus { get; set; } = "pending";
    public int? ValueScore { get; set; }
    public int? WordCount { get; set; }
    public int? ReadingTimeMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Phase 3: Source metadata & status
    public string? SourceType { get; set; }
    public string? SourceDomain { get; set; }
    public int? QualityScore { get; set; }
    public string ParseStatus { get; set; } = "pending";
    public string CleanStatus { get; set; } = "pending";
    public string ChunkStatus { get; set; } = "pending";
    public string IndexStatus { get; set; } = "pending";
    public string TagStatus { get; set; } = "pending";
    public string EntityStatus { get; set; } = "pending";
    public string EmbeddingStatus { get; set; } = "pending";
}

public class DocumentDetail
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? ContentMarkdown { get; set; }
    public string? ContentText { get; set; }
    public string? Language { get; set; }
    public int? WordCount { get; set; }
    public int? ReadingTimeMinutes { get; set; }

    // AI processing results
    public string? Summary { get; set; }
    public string? OneSentenceConclusion { get; set; }
    public string? KeyPoints { get; set; }
    public string? BusinessSignals { get; set; }
    public string? TechnicalSignals { get; set; }
    public string? Risks { get; set; }
    public string? Opportunities { get; set; }
    public string? ReusableMaterials { get; set; }

    public int? ValueScore { get; set; }
    public int? QualityScore { get; set; }

    public string AiStatus { get; set; } = "pending";
    public string? AiModel { get; set; }
    public string? PromptVersion { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // Phase 3: Source metadata
    public string? SourceType { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceDomain { get; set; }
    public string? Author { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? RecommendedTags { get; set; }

    // Phase 3: Scoring
    public string? ValueScoreReason { get; set; }
    public bool ShouldDeepProcess { get; set; }

    // Phase 3: Multi-stage status
    public string ParseStatus { get; set; } = "pending";
    public string CleanStatus { get; set; } = "pending";
    public string ChunkStatus { get; set; } = "pending";
    public string IndexStatus { get; set; } = "pending";
    public string TagStatus { get; set; } = "pending";
    public string EntityStatus { get; set; } = "pending";
    public string EmbeddingStatus { get; set; } = "pending";

    // Phase 3: Parser metadata
    public string? ParserName { get; set; }
    public string? ParserVersion { get; set; }
    public string? CleanerVersion { get; set; }

    // Phase 3: AI raw output
    public string? AiRawOutput { get; set; }
    public string? AiErrorMessage { get; set; }

    public List<TagResponse> Tags { get; set; } = new();
    public List<EntityInDocument> Entities { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DocumentResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AiStatus { get; set; } = "pending";
    public int? ValueScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TagResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "topic";
    public string? Description { get; set; }
    public string Source { get; set; } = "ai";
    public decimal? Confidence { get; set; }
}

public class EntityInDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MentionCount { get; set; }
    public decimal? Confidence { get; set; }
    public string? Evidence { get; set; }
}

public class ProcessingLogItem
{
    public Guid Id { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? DocumentId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProcessingStatusResponse
{
    public string ParseStatus { get; set; } = "pending";
    public string CleanStatus { get; set; } = "pending";
    public string AiStatus { get; set; } = "pending";
    public string ChunkStatus { get; set; } = "pending";
    public string IndexStatus { get; set; } = "pending";
    public string? AiErrorMessage { get; set; }
}

/// <summary>
/// Request body for re-summarizing a document. All fields optional; when omitted
/// the system reuses its current model/prompt configuration.
/// </summary>
public class ResummarizeRequestDto
{
    /// <summary>Optional model provider override (e.g. openai, deepseek).</summary>
    public string? ModelProvider { get; set; }

    /// <summary>Optional model name override (e.g. gpt-4o-mini).</summary>
    public string? ModelName { get; set; }

    /// <summary>Optional prompt version override (e.g. summary_v1).</summary>
    public string? PromptVersion { get; set; }
}

/// <summary>
/// Request body for retrying a failed source. Allows retrying from a specific
/// pipeline step (e.g. "ai_summarize") instead of re-running the whole pipeline.
/// </summary>
public class RetrySourceRequestDto
{
    /// <summary>
    /// Step to resume from. "ai_summarize" re-queues only the AI stage;
    /// null/other values re-run the pipeline from the beginning.
    /// </summary>
    public string? FromStep { get; set; }
}
