namespace KnowledgeEngine.Domain.Entities;

public class QaMessage
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public string Role { get; set; } = string.Empty; // user, assistant, system
    public string Content { get; set; } = string.Empty;

    public string? Citations { get; set; }       // JSONB
    public string? RetrievalSnapshot { get; set; } // JSONB

    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? LatencyMs { get; set; }

    public DateTime CreatedAt { get; set; }
}
