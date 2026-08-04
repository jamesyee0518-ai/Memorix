namespace KnowledgeEngine.Domain.Entities;

public class AgentMemorySession
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? AgentProfileId { get; set; }
    public string ExternalSessionKey { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "active"; // active | closed
    public DateTime StartedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? TopicId { get; set; }

    // Navigation
    public List<AgentMemoryItem> Items { get; set; } = new();
    public List<AgentMemoryCheckpoint> Checkpoints { get; set; } = new();
}
