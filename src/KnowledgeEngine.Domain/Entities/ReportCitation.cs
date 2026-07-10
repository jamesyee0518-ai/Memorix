namespace KnowledgeEngine.Domain.Entities;

public class ReportCitation
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? ChunkId { get; set; }
    public int CitationIndex { get; set; }
    public string? CitationKey { get; set; } // CIT-1, CIT-2
    public string? QuoteText { get; set; }
    public string? SectionKey { get; set; }
    public string? Title { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceDomain { get; set; }
    public string? SourceType { get; set; }
    public double? RelevanceScore { get; set; }
    public string? SourceRole { get; set; }
    public DateTime CreatedAt { get; set; }
}
