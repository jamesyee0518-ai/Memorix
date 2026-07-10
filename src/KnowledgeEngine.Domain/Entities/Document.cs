namespace KnowledgeEngine.Domain.Entities;

public class Document
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

    // AI 处理结果
    public string? Summary { get; set; }
    public string? OneSentenceConclusion { get; set; }
    public string? KeyPoints { get; set; }        // JSONB
    public string? BusinessSignals { get; set; }   // JSONB
    public string? TechnicalSignals { get; set; }  // JSONB
    public string? Risks { get; set; }             // JSONB
    public string? Opportunities { get; set; }     // JSONB
    public string? ReusableMaterials { get; set; } // JSONB

    public int? ValueScore { get; set; }
    public int? QualityScore { get; set; }

    public string AiStatus { get; set; } = "pending";
    public string? AiModel { get; set; }
    public string? PromptVersion { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public string ChunkStatus { get; set; } = "pending";

    // Phase 3: Source metadata
    public string? SourceType { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceDomain { get; set; }
    public string? Author { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? RecommendedTags { get; set; }  // JSONB

    // Phase 3: Scoring
    public string? ValueScoreReason { get; set; }
    public bool ShouldDeepProcess { get; set; } = true;

    // Phase 3: Multi-stage status
    public string ParseStatus { get; set; } = "pending";
    public string CleanStatus { get; set; } = "pending";
    public string IndexStatus { get; set; } = "pending";

    // Phase 4: Tag/Entity/Embedding status
    public string TagStatus { get; set; } = "pending";
    public string EntityStatus { get; set; } = "pending";
    public string EmbeddingStatus { get; set; } = "pending";

    // Phase 3: Parser/cleaner metadata
    public string? ParserName { get; set; }
    public string? ParserVersion { get; set; }
    public string? CleanerVersion { get; set; }

    // Phase 3: AI raw output
    public string? AiRawOutput { get; set; }
    public string? AiErrorMessage { get; set; }

    // Phase 7: Security & permission model
    public string SensitivityLevel { get; set; } = "normal"; // public / normal / private / sensitive / restricted

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
