namespace KnowledgeEngine.Domain.Entities;

public class SearchLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public string Query { get; set; } = string.Empty;
    public string SearchType { get; set; } = string.Empty;
    public string? Filters { get; set; } // JSONB
    public int? ResultCount { get; set; }
    public int? LatencyMs { get; set; }

    public DateTime CreatedAt { get; set; }
}
