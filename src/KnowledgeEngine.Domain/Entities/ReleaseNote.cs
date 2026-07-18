namespace KnowledgeEngine.Domain.Entities;

public class ReleaseNote
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;  // e.g. "0.7.0-alpha"
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = "alpha";  // alpha / beta / rc / stable
    public string ContentMarkdown { get; set; } = string.Empty;

    // Public API properties (not mapped by EF)
    public List<string>? Highlights { get; set; }  // 关键更新要点
    public List<string>? KnownIssues { get; set; }  // 已知问题

    // EF-mapped JSON string properties
    public string? HighlightsJson { get; set; }
    public string? KnownIssuesJson { get; set; }

    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
