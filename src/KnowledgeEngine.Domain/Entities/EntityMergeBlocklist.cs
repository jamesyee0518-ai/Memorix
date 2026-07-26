namespace KnowledgeEngine.Domain.Entities;

public class EntityMergeBlocklist
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid EntityIdA { get; set; }
    public Guid EntityIdB { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = "manual";
    public Guid? OperatorId { get; set; }
    public bool IsPermanent { get; set; } = true;
    public DateTime? ValidUntil { get; set; }
    public DateTime CreatedAt { get; set; }
}
