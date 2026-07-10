namespace KnowledgeEngine.Domain.Entities;

public class Tag
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "topic";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    // Phase 4 fields
    public string WorkspaceId { get; set; } = string.Empty;
    public string? NormalizedName { get; set; }
    public string? DisplayName { get; set; }
    public string? TagType { get; set; }  // 新字段，区别于已有 Type
    public string? Color { get; set; }
    public string? Aliases { get; set; }  // JSON
    public string Source { get; set; } = "manual";
    public int UsageCount { get; set; }
    public bool IsSystem { get; set; }
    public bool IsArchived { get; set; }
    public DateTime UpdatedAt { get; set; }
}
