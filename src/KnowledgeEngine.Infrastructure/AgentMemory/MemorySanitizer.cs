using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Sanitizes memory content by detecting and redacting sensitive information
/// such as API keys, private keys, tokens, and JWTs.
/// </summary>
public class MemorySanitizer
{
    private readonly ILogger<MemorySanitizer> _logger;

    // Ordered list of (pattern, redactionLabel) tuples.
    // The order matters: more specific patterns should be evaluated first.
    private static readonly (Regex Pattern, string Label)[] SensitivePatterns =
    {
        // Private keys (PEM blocks): -----BEGIN [RSA | EC | OPENSSH | ...] PRIVATE KEY-----
        (
            new Regex(
                @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----",
                RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "PRIVATE_KEY"
        ),

        // OpenAI-style API keys: sk-proj-xxx, sk-xxx
        (
            new Regex(
                @"sk-[a-zA-Z0-9\-_]{20,}",
                RegexOptions.Compiled),
            "API_KEY"
        ),

        // Bearer tokens: Bearer xxx
        (
            new Regex(
                @"Bearer\s+[a-zA-Z0-9\-_\.=]+",
                RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "BEARER_TOKEN"
        ),

        // GitHub personal access tokens: ghp_xxx, gho_xxx, ghs_xxx, ghu_xxx, ghr_xxx
        (
            new Regex(
                @"gh[pousr]_[a-zA-Z0-9]{36,}",
                RegexOptions.Compiled),
            "GITHUB_TOKEN"
        ),

        // Generic PAT-style tokens: tok_xxx, pat_xxx, token_xxx
        (
            new Regex(
                @"(?i)(tok|pat|token)[_\-][a-zA-Z0-9\-_]{16,}",
                RegexOptions.Compiled),
            "ACCESS_TOKEN"
        ),

        // JWT tokens: eyJxxxxx.eyJxxxxx.xxxxx (three base64url segments)
        (
            new Regex(
                @"eyJ[a-zA-Z0-9_\-]+\.eyJ[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+",
                RegexOptions.Compiled),
            "JWT"
        ),

        // Slack tokens: xoxb-xxx, xoxp-xxx, xoxa-xxx
        (
            new Regex(
                @"xox[abp]-[a-zA-Z0-9\-]{10,}",
                RegexOptions.Compiled),
            "SLACK_TOKEN"
        ),

        // AWS access key IDs: AKIAxxxxxxxx
        (
            new Regex(
                @"AKIA[0-9A-Z]{16}",
                RegexOptions.Compiled),
            "AWS_ACCESS_KEY"
        ),

        // AWS secret access keys (40-char base64 after "aws_secret" context)
        (
            new Regex(
                @"(?i)aws_secret_access_key[""']?\s*[:=]\s*[""']?[a-zA-Z0-9/+=]{40}",
                RegexOptions.Compiled),
            "AWS_SECRET_KEY"
        ),

        // Generic high-entropy hex strings that look like secrets (64+ hex chars)
        (
            new Regex(
                @"\b[a-f0-9]{64,}\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "HEX_SECRET"
        ),
    };

    public MemorySanitizer(ILogger<MemorySanitizer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sanitizes content before writing to the memory store.
    /// Returns the sanitized content and a flag indicating whether any modification was made.
    /// </summary>
    public Task<(string SanitizedContent, bool WasModified)> SanitizeOnWriteAsync(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult((content, false));
        }

        var (sanitized, wasModified, redactionSummary) = ApplyRedaction(content);

        if (wasModified)
        {
            _logger.LogWarning(
                "Sensitive content detected and redacted during write: {Summary}",
                redactionSummary);
        }

        return Task.FromResult((sanitized, wasModified));
    }

    /// <summary>
    /// Sanitizes content before returning to the reader.
    /// Applies the same redaction logic in case content was stored before sanitization was in place.
    /// </summary>
    public Task<string> SanitizeOnReadAsync(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(content ?? string.Empty);
        }

        var (sanitized, wasModified, redactionSummary) = ApplyRedaction(content);

        if (wasModified)
        {
            _logger.LogWarning(
                "Sensitive content detected and redacted during read: {Summary}",
                redactionSummary);
        }

        return Task.FromResult(sanitized);
    }

    /// <summary>
    /// Applies all redaction patterns to the content.
    /// Returns the sanitized content, a flag indicating whether modifications were made,
    /// and a summary of what was redacted.
    /// </summary>
    private (string SanitizedContent, bool WasModified, string Summary) ApplyRedaction(string content)
    {
        var result = content;
        var wasModified = false;
        var redactionCounts = new Dictionary<string, int>();

        foreach (var (pattern, label) in SensitivePatterns)
        {
            var matches = pattern.Matches(result);
            if (matches.Count > 0)
            {
                result = pattern.Replace(result, $"[REDACTED:{label}]");
                wasModified = true;
                redactionCounts[label] = matches.Count;
            }
        }

        var summary = redactionCounts.Count > 0
            ? string.Join(", ", redactionCounts.Select(kv => $"{kv.Key}={kv.Value}"))
            : "none";

        return (result, wasModified, summary);
    }
}
