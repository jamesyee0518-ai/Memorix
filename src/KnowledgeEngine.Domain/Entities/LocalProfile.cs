namespace KnowledgeEngine.Domain.Entities;

public class LocalProfile
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public string DisplayName { get; set; } = "本地用户";
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
