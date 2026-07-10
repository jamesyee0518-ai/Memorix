namespace KnowledgeEngine.Domain.Entities;

public class QaSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public string? Title { get; set; }
    public string Status { get; set; } = "active";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
