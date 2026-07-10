using System.Text.RegularExpressions;
using Html2Markdown;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace KnowledgeEngine.Infrastructure.Processing;

public class ContentProcessor : IContentProcessor
{
    private readonly ILogger<ContentProcessor> _logger;

    public ContentProcessor(ILogger<ContentProcessor> logger)
    {
        _logger = logger;
    }

    public Task<string> CleanHtmlAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Task.FromResult(string.Empty);
        }

        try
        {
            var converter = new Converter();
            var markdown = converter.Convert(html);
            return Task.FromResult(markdown);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert HTML to Markdown, returning original HTML");
            return Task.FromResult(html);
        }
    }

    public Task<string> ExtractPdfTextAsync(Stream pdfStream)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            using var document = PdfDocument.Open(pdfStream);

            foreach (Page? page in document.GetPages())
            {
                if (page == null) continue;
                var text = page.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }

            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from PDF");
            throw new InvalidOperationException($"Failed to extract text from PDF: {ex.Message}", ex);
        }
    }

    public string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Remove control characters (except common whitespace like \n, \r, \t)
        text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

        // Normalize line endings
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Replace multiple blank lines with a single blank line
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // Trim trailing whitespace on each line
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }
        text = string.Join('\n', lines);

        // Collapse multiple spaces into one (but preserve newlines)
        text = Regex.Replace(text, @"[ \t]{2,}", " ");

        return text.Trim();
    }

    public List<string> ChunkText(string text, int maxChunkSize = 2000)
    {
        var chunks = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        if (text.Length <= maxChunkSize)
        {
            chunks.Add(text.Trim());
            return chunks;
        }

        // Split by paragraphs (double newlines)
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new System.Text.StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            var trimmedPara = paragraph.Trim();
            if (string.IsNullOrEmpty(trimmedPara)) continue;

            // If a single paragraph exceeds maxChunkSize, split it further by sentences/words
            if (trimmedPara.Length > maxChunkSize)
            {
                // Flush current chunk first
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                // Split the large paragraph into smaller pieces
                var subPieces = SplitLargeText(trimmedPara, maxChunkSize);
                chunks.AddRange(subPieces);
                continue;
            }

            // If adding this paragraph would exceed the limit, flush current chunk
            if (currentChunk.Length > 0 && currentChunk.Length + trimmedPara.Length + 2 > maxChunkSize)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            if (currentChunk.Length > 0)
            {
                currentChunk.Append("\n\n");
            }
            currentChunk.Append(trimmedPara);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    private static List<string> SplitLargeText(string text, int maxChunkSize)
    {
        var result = new List<string>();
        var sentences = text.Split(new[] { ". ", "。", "!", "?", "! ", "? " },
            StringSplitOptions.RemoveEmptyEntries);

        var current = new System.Text.StringBuilder();
        foreach (var sentence in sentences)
        {
            var s = sentence.Trim();
            if (string.IsNullOrEmpty(s)) continue;

            if (s.Length > maxChunkSize)
            {
                // Flush current
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                // Hard split very long sentence
                for (int i = 0; i < s.Length; i += maxChunkSize)
                {
                    var length = Math.Min(maxChunkSize, s.Length - i);
                    result.Add(s.Substring(i, length).Trim());
                }
                continue;
            }

            if (current.Length > 0 && current.Length + s.Length + 1 > maxChunkSize)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(". ");
            }
            current.Append(s);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString().Trim());
        }

        return result;
    }
}
