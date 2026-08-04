namespace KnowledgeEngine.Domain.Entities;

public class AgentMemoryFeedback
{
    public Guid Id { get; set; }
    public Guid MemoryItemId { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = string.Empty; // confirm | reject | edit | pin | archive | restore | forget
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public AgentMemoryItem? MemoryItem { get; set; }
}
