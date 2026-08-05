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

    /// <summary>
    /// The canonical project this session belongs to. Sessions from different
    /// agents on the same git repo collapse to the same ProjectId, enabling
    /// shared memory and cross-agent handoffs.
    /// </summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    // Navigation
    public List<AgentMemoryItem> Items { get; set; } = new();
    public List<AgentMemoryCheckpoint> Checkpoints { get; set; } = new();
}
