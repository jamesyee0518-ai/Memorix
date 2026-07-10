namespace KnowledgeEngine.Domain.Entities;

public class DocumentEntity
{
    public Guid DocumentId { get; set; }
    public Guid EntityId { get; set; }
    public int MentionCount { get; set; } = 1;
    public decimal? Confidence { get; set; }
    public string? Evidence { get; set; }
    public DateTime CreatedAt { get; set; }

    // Phase 4 fields
    public string? FirstMention { get; set; }
    public string? MentionExamples { get; set; }  // JSON
    public decimal? Importance { get; set; }
    public string? Role { get; set; }
    public string? Sentiment { get; set; }
}
