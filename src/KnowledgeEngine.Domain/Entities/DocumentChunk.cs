namespace KnowledgeEngine.Domain.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid SourceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public int ChunkIndex { get; set; }
    public string? ChunkTitle { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ContentMarkdown { get; set; }

    public int? TokenCount { get; set; }
    public int? CharCount { get; set; }
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }

    // Embedding stored as float[] - serialized to pgvector
    public float[]? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }
    public string EmbeddingStatus { get; set; } = "pending";

    public int? QualityScore { get; set; }
    public string? Metadata { get; set; } // JSONB

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Phase 4 fields
    public string? ChunkUid { get; set; }
    public string? HeadingPath { get; set; }
    public int? SectionLevel { get; set; }
    public string? ContentHash { get; set; }
    public Guid? PrevChunkId { get; set; }
    public Guid? NextChunkId { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string IndexStatus { get; set; } = "pending";
}
