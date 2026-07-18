using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

public interface ISourceProcessor
{
    bool Supports(string sourceType);
    Task<ParseResult> ParseAsync(Source source, CancellationToken ct = default);
}

public class ParseResult
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? Domain { get; set; }
    public string? Language { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string? RawHtml { get; set; }
    public string? Markdown { get; set; }
    public string ParserName { get; set; } = string.Empty;
    public string ParserVersion { get; set; } = string.Empty;
}
