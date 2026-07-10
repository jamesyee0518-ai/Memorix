namespace KnowledgeEngine.Domain.Entities;

public class FeedbackItem
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? BetaUserId { get; set; }

    public string FeedbackType { get; set; } = string.Empty;
    public string? Module { get; set; }
    public string? Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }

    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }

    public string Status { get; set; } = "open";
    public string Priority { get; set; } = "medium";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
