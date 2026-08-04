namespace KnowledgeEngine.Domain.Entities;

public class AgentMemoryAccessLog
{
    public Guid Id { get; set; }
    public Guid? MemoryItemId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? AgentProfileId { get; set; }
    public string Action { get; set; } = string.Empty; // read | write | deliver | export | delete
    public string? TraceId { get; set; }
    public DateTime CreatedAt { get; set; }
}
