using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing.Processors;

public class TextProcessor : ISourceProcessor
{
    private const string ParserVersion = "1.0";

    private readonly ILogger<TextProcessor> _logger;

    public TextProcessor(ILogger<TextProcessor> logger)
    {
        _logger = logger;
    }

    public bool Supports(string sourceType)
    {
        return string.Equals(sourceType, "text", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceType, "markdown", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ParseResult> ParseAsync(Source source, CancellationToken ct = default)
    {
        var text = source.RawText ?? string.Empty;

        var result = new ParseResult
        {
            Title = source.Title,
            Author = source.Author,
            PublishedAt = source.PublishedAt,
            RawText = text,
            Markdown = text,
            ParserName = "text",
            ParserVersion = ParserVersion
        };

        _logger.LogInformation("Text parsed: title={Title}, length={Length}",
            source.Title, text.Length);

        return Task.FromResult(result);
    }
}
