using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Processing;

public class ContentCleaner : IContentCleaner
{
    private const string CleanerVersion = "1.0";

    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style|noscript)[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private static readonly Regex MultiBlankLineRegex = new(
        @"\n{3,}",
        RegexOptions.Compiled);

    private static readonly Regex HtmlCommentRegex = new(
        @"<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly IContentProcessor _contentProcessor;
    private readonly IMarkdownNormalizer _markdownNormalizer;

    public ContentCleaner(
        IContentProcessor contentProcessor,
        IMarkdownNormalizer markdownNormalizer)
    {
        _contentProcessor = contentProcessor;
        _markdownNormalizer = markdownNormalizer;
    }

    public Task<CleanResult> CleanAsync(string rawText, string? rawHtml, string? markdown, CancellationToken ct = default)
    {
        // Determine the best source for cleaned markdown
        string cleanedMarkdown;

        if (!string.IsNullOrWhiteSpace(markdown))
        {
            cleanedMarkdown = markdown;
        }
        else if (!string.IsNullOrWhiteSpace(rawHtml))
        {
            // If we have raw HTML but no markdown, strip HTML tags to produce text
            cleanedMarkdown = StripHtml(rawHtml);
        }
        else
        {
            cleanedMarkdown = rawText ?? string.Empty;
        }

        // Normalize the markdown
        cleanedMarkdown = _markdownNormalizer.Normalize(cleanedMarkdown);

        // Produce cleaned text by stripping markdown formatting
        var cleanedText = _contentProcessor.NormalizeText(cleanedMarkdown);

        // Also normalize the raw text if it differs
        if (string.IsNullOrEmpty(cleanedText) && !string.IsNullOrWhiteSpace(rawText))
        {
            cleanedText = _contentProcessor.NormalizeText(rawText);
        }

        // Duplicate paragraph recognition: drop paragraphs whose normalized form
        // has already been seen, to remove repeated boilerplate / mirrored content.
        if (!string.IsNullOrEmpty(cleanedText))
        {
            var paragraphs = cleanedText.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>();
            var deduped = new List<string>();
            foreach (var para in paragraphs)
            {
                var normalized = Regex.Replace(para.Trim().ToLowerInvariant(), @"\s+", " ");
                if (string.IsNullOrEmpty(normalized)) continue;
                if (seen.Add(normalized))
                {
                    deduped.Add(para.Trim());
                }
            }
            cleanedText = string.Join("\n\n", deduped);
        }

        var result = new CleanResult
        {
            CleanedMarkdown = cleanedMarkdown,
            CleanedText = cleanedText,
            CleanerVersion = CleanerVersion
        };

        return Task.FromResult(result);
    }

    private static string StripHtml(string html)
    {
        // Remove HTML comments
        var text = HtmlCommentRegex.Replace(html, "");

        // Remove script/style/noscript blocks entirely
        text = ScriptStyleRegex.Replace(text, "");

        // Remove navigation menus (common <nav> blocks) - must happen before generic tag stripping
        // so the inner text is removed too.
        text = Regex.Replace(text, @"<nav[^>]*>.*?</nav>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // Remove footer
        text = Regex.Replace(text, @"<footer[^>]*>.*?</footer>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // Remove aside (sidebars)
        text = Regex.Replace(text, @"<aside[^>]*>.*?</aside>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // Remove ad blocks (class contains ad/ads/advertisement/banner/promo)
        text = Regex.Replace(text, @"<div[^>]*class=""[^""]*\b(ad|ads|advertisement|banner|promo)\b[^""]*""[^>]*>.*?</div>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // Remove social share buttons
        text = Regex.Replace(text, @"<div[^>]*class=""[^""]*\b(share|social|social-share)\b[^""]*""[^>]*>.*?</div>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Remove remaining HTML tags
        text = HtmlTagRegex.Replace(text, "");

        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Collapse multiple blank lines
        text = MultiBlankLineRegex.Replace(text, "\n\n");

        return text.Trim();
    }
}
