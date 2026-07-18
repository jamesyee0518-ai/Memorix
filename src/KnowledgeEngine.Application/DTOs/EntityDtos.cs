namespace KnowledgeEngine.Application.DTOs;

public class EntityListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DocumentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EntityDetail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NormalizedName { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Metadata { get; set; }
    public List<RelatedDocument> RelatedDocuments { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RelatedDocument
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int MentionCount { get; set; }
    public decimal? Confidence { get; set; }
    public string? Evidence { get; set; }
}
