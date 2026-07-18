namespace KnowledgeEngine.Domain.Entities;

public class Terminology
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string SourceLanguage { get; set; } = "en";
    public string SourceTerm { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = "zh-CN";
    public string TargetTerm { get; set; } = string.Empty;
    public string? Aliases { get; set; }
    public string? Domain { get; set; }
    public int Priority { get; set; }
    public string ReviewStatus { get; set; } = "approved";
    public string Version { get; set; } = "v1";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
