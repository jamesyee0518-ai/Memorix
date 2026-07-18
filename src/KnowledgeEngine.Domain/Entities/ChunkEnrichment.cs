namespace KnowledgeEngine.Domain.Entities;

public class ChunkEnrichment
{
    public Guid Id { get; set; }
    public Guid ChunkId { get; set; }
    public Guid UserId { get; set; }
    public Guid? LocalizationId { get; set; }
    public string LanguageCode { get; set; } = "zh-CN";
    public string? Summary { get; set; }
    public string? Keywords { get; set; }
    public string? Entities { get; set; }
    public string? Facts { get; set; }
    public string? HypotheticalQuestions { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public string SourceContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
