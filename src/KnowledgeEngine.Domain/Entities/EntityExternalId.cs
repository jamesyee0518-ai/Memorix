namespace KnowledgeEngine.Domain.Entities;

public class EntityExternalId
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string IdType { get; set; } = string.Empty;
    public string IdValue { get; set; } = string.Empty;
    public string Source { get; set; } = "ai";
    public bool IsVerified { get; set; }
    public decimal? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
