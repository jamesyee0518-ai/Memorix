namespace KnowledgeEngine.Domain.Entities;

public class RetrievalLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? QaMessageId { get; set; }

    public string Query { get; set; } = string.Empty;
    public string RetrievalType { get; set; } = string.Empty;
    public string? RetrievedChunks { get; set; } // JSONB
    public string? FinalContext { get; set; }    // JSONB
    public int? LatencyMs { get; set; }

    public DateTime CreatedAt { get; set; }
}
