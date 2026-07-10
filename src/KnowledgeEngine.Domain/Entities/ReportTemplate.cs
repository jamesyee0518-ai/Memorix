namespace KnowledgeEngine.Domain.Entities;

public class ReportTemplate
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string TemplateMarkdown { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? UserPromptTemplate { get; set; }

    public string? OutputRules { get; set; } // JSONB
    public bool IsSystem { get; set; } = false;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
