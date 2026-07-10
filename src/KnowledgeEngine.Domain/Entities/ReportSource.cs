namespace KnowledgeEngine.Domain.Entities;

public class ReportSource
{
    public Guid ReportId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? ChunkId { get; set; }

    public int? CitationIndex { get; set; }
    public decimal? RelevanceScore { get; set; }
    public string? SourceRole { get; set; }

    public DateTime CreatedAt { get; set; }
}
