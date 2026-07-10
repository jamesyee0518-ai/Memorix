namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Detects the input type of inbox content (§17.5).
/// Used by the ImportService and controller to automatically classify
/// incoming content as "url", "text", "file", or "mixed".
/// </summary>
public class TypeDetector
{
    /// <summary>
    /// Detects the type from a single string input.
    /// Returns "url" if the input starts with http:// or https://, "text" otherwise.
    /// </summary>
    public string DetectType(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "text";

        var trimmed = input.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "url";
        }

        return "text";
    }

    /// <summary>
    /// Detects the type from multiple inputs (url, content, fileName).
    /// Returns "mixed" if more than one non-null/non-empty input is provided.
    /// Returns "url", "text", or "file" if only a single input is provided.
    /// </summary>
    public string DetectType(string? url, string? content, string? fileName)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(url);
        var hasContent = !string.IsNullOrWhiteSpace(content);
        var hasFile = !string.IsNullOrWhiteSpace(fileName);

        var count = (hasUrl ? 1 : 0) + (hasContent ? 1 : 0) + (hasFile ? 1 : 0);

        if (count > 1)
            return "mixed";

        if (hasUrl)
            return "url";

        if (hasFile)
            return "file";

        return "text";
    }
}
