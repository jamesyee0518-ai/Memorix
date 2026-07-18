using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Processing;

public class MarkdownNormalizer : IMarkdownNormalizer
{
    private static readonly Regex MultiBlankLineRegex = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex EmptyLinkRegex = new(@"\[([ \t]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex ReferenceLinkRegex = new(@"\[([^\]]+)\]\(\s*\)", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex HeadingSpacingRegex = new(@"^(#{1,6})[ \t]*(.+?)[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public string Normalize(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var text = markdown;

        // Normalize line endings first so code-block extraction sees consistent \n
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Protect fenced code blocks from subsequent space/trim normalization,
        // which would otherwise corrupt intentional code indentation.
        var codeBlocks = new List<string>();
        text = Regex.Replace(text, @"(```[\s\S]*?```)", m =>
        {
            codeBlocks.Add(m.Value);
            return $"__CODE_BLOCK_{codeBlocks.Count - 1}__";
        });

        // Heading level demotion: if the document has more than one h1 (# ),
        // demote every heading (h1-h5) by one level so there is a single h1.
        var h1Count = Regex.Matches(text, @"^#\s", RegexOptions.Multiline).Count;
        if (h1Count > 1)
        {
            // All #-prefixed headings (1-5 hashes) shift down one level; h6 is left as-is.
            text = Regex.Replace(text, @"^(#{1,5})\s", "$1# ", RegexOptions.Multiline);
        }

        // Remove empty links like [ ](url) or [](url)
        text = EmptyLinkRegex.Replace(text, "");

        // Remove reference-style empty links like [text]()
        text = ReferenceLinkRegex.Replace(text, "$1");

        // Normalize heading spacing: ensure exactly one space after #
        text = HeadingSpacingRegex.Replace(text, "$1 $2");

        // Trim trailing whitespace on each line
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }
        text = string.Join('\n', lines);

        // Collapse multiple spaces (but preserve newlines)
        text = MultipleSpacesRegex.Replace(text, " ");

        // Collapse multiple blank lines into a single blank line
        text = MultiBlankLineRegex.Replace(text, "\n\n");

        // Restore protected code blocks
        for (int i = 0; i < codeBlocks.Count; i++)
        {
            text = text.Replace($"__CODE_BLOCK_{i}__", codeBlocks[i]);
        }

        return text.Trim();
    }
}
