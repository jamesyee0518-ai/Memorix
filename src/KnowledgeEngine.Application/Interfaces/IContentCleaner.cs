namespace KnowledgeEngine.Application.Interfaces;

public interface IContentCleaner
{
    Task<CleanResult> CleanAsync(string rawText, string? rawHtml, string? markdown, CancellationToken ct = default);
}

public class CleanResult
{
    public string CleanedMarkdown { get; set; } = string.Empty;
    public string CleanedText { get; set; } = string.Empty;
    public string CleanerVersion { get; set; } = "1.0";
}
