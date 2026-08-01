using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Translation pipeline node.
/// <para>
/// NodeId = "translation", DependsOn = ["asr"].
/// </para>
/// <para>
/// Translates the transcript into a target language specified via
/// <see cref="PipelineContext.Metadata"/>["targetLanguage"]. If no target
/// language is specified the node is skipped (returns false from
/// <see cref="CanExecuteAsync"/>). The translated text and per-segment
/// translations are published to the shared context.
/// </para>
/// </summary>
public class TranslationNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "translation";

    /// <inheritdoc/>
    public string DisplayName => "Translation";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "asr" };

    private const string SystemPrompt =
        "You are a professional translator. Translate the following transcript into {0}. " +
        "Preserve the original meaning and tone. Respond with STRICT JSON only " +
        "(no markdown fences). " +
        "Schema: {\"translatedText\":string,\"segments\":[{\"index\":number,\"text\":string}]}.";

    private readonly ILlmService _llm;
    private readonly ILogger<TranslationNode> _logger;

    public TranslationNode(ILlmService llm, ILogger<TranslationNode> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // Only execute if a target language is specified in metadata.
        var hasTarget = context.Metadata.TryGetValue("targetLanguage", out var target)
                        && target is string s
                        && !string.IsNullOrWhiteSpace(s);
        return Task.FromResult(hasTarget && context.Segments is { Count: > 0 });
    }

    /// <inheritdoc/>
    public async Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var segments = context.Segments;
        if (segments is null || segments.Count == 0)
        {
            return NodeExecutionResult.Fail("No segments available for translation.");
        }

        if (!context.Metadata.TryGetValue("targetLanguage", out var targetLangObj)
            || targetLangObj is not string targetLanguage
            || string.IsNullOrWhiteSpace(targetLanguage))
        {
            return NodeExecutionResult.Fail("Target language not specified in pipeline metadata.");
        }

        var fullText = string.Join("\n", segments.OrderBy(s => s.SegmentIndex).Select(s => s.Text));
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return NodeExecutionResult.Fail("Transcript text is empty; cannot translate.");
        }

        const int maxChars = 12000;
        var userInput = fullText.Length > maxChars
            ? fullText.Substring(0, maxChars) + "\n…[truncated]"
            : fullText;

        try
        {
            var systemPrompt = string.Format(SystemPrompt, targetLanguage);

            _logger.LogInformation(
                "Job {JobId}: translation node translating {SegmentCount} segments to {TargetLanguage}",
                context.JobId, segments.Count, targetLanguage);

            var llmResult = await _llm.CompleteAsync(systemPrompt, userInput, ct: ct);

            var (translatedText, translatedSegments) = ParseTranslationResponse(llmResult.Content);

            return NodeExecutionResult.Ok(
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["translatedText"] = translatedText,
                    ["translatedSegments"] = translatedSegments,
                    ["targetLanguage"] = targetLanguage,
                    ["model"] = llmResult.Model,
                    ["inputTokens"] = llmResult.InputTokens,
                    ["outputTokens"] = llmResult.OutputTokens,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: translation node failed", context.JobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Parses the LLM JSON response into the translated text and per-segment translations.
    /// </summary>
    private static (string translatedText, List<Dictionary<string, object>> segments) ParseTranslationResponse(
        string content)
    {
        var defaultResult = (content ?? string.Empty, new List<Dictionary<string, object>>());

        if (string.IsNullOrWhiteSpace(content))
        {
            return defaultResult;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                trimmed = trimmed[..lastFence];
            }
            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            // If not valid JSON, return the raw content as the translated text.
            return (trimmed, new List<Dictionary<string, object>>());
        }

        var json = trimmed.Substring(start, end - start + 1);

        try
        {
            using var doc = JsonDocument.Parse(json);
            string translatedText = string.Empty;
            var segmentList = new List<Dictionary<string, object>>();

            if (doc.RootElement.TryGetProperty("translatedText", out var textEl)
                && textEl.ValueKind == JsonValueKind.String)
            {
                translatedText = textEl.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("segments", out var segArr)
                && segArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in segArr.EnumerateArray())
                {
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in item.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => (object?)(prop.Value.GetString() ?? string.Empty) ?? string.Empty,
                            JsonValueKind.Number => prop.Value.GetDecimal(),
                            _ => prop.Value.GetRawText(),
                        };
                    }
                    segmentList.Add(dict);
                }
            }

            return (translatedText, segmentList);
        }
        catch
        {
            return (trimmed, new List<Dictionary<string, object>>());
        }
    }
}
