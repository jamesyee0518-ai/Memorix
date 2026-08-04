namespace KnowledgeEngine.Domain.Entities;

public class AgentMemoryCheckpoint
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int FromSequence { get; set; }
    public int ToSequence { get; set; }
    public string? Summary { get; set; }
    public string? OpenLoopsJson { get; set; }
    public string? DecisionsJson { get; set; }
    public int TokenEstimate { get; set; }
    public string DeliveryState { get; set; } = "pending"; // pending | delivered | failed
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; } = 1;

    // Navigation
    public AgentMemorySession? Session { get; set; }
}
