namespace KnowledgeEngine.Domain.Entities;

public class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Guid TagId { get; set; }
    public string Source { get; set; } = "ai";
    public decimal? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }

    // Phase 4 fields
    public string? Reason { get; set; }
    public bool IsConfirmed { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}
