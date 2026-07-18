namespace KnowledgeEngine.Application.DTOs;

public class TagListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "topic";
    public string? Description { get; set; }
    public int DocumentCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
