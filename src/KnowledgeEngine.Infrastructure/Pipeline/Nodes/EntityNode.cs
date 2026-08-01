using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Entity extraction pipeline node.
/// <para>
/// NodeId = "entity", DependsOn = ["asr"].
/// </para>
/// <para>
/// Extracts named entities (people, organizations, locations, products, dates,
/// terminology) from the ASR transcript using the <see cref="ILlmService"/>.
/// The LLM is prompted to return strict JSON so the result can be parsed into a
/// structured list. Extracted entities are published to the shared context for
/// downstream indexing and linking.
/// </para>
/// </summary>
public class EntityNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "entity";

    /// <inheritdoc/>
    public string DisplayName => "Entity Extraction";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "asr" };

    private const string SystemPrompt =
        "You are a named-entity recognition engine. Read the transcript and extract " +
        "named entities. Respond with STRICT JSON only (no markdown fences). " +
        "Schema: {\"entities\":[{\"name\":string,\"type\":\"person|organization|location|product|date|term|other\",\"mentions\":number}]}. " +
        "Use the same language as the transcript for entity names.";

    private readonly ILlmService _llm;
    private readonly ILogger<EntityNode> _logger;

    public EntityNode(ILlmService llm, ILogger<EntityNode> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        return Task.FromResult(context.Segments is { Count: > 0 });
    }

    /// <inheritdoc/>
    public async Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var segments = context.Segments;
        if (segments is null || segments.Count == 0)
        {
            return NodeExecutionResult.Fail("No segments available for entity extraction (ASR node produced no output).");
        }

        var fullText = string.Join("\n", segments.OrderBy(s => s.SegmentIndex).Select(s => s.Text));
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return NodeExecutionResult.Fail("Transcript text is empty; cannot extract entities.");
        }

        const int maxChars = 12000;
        var userInput = fullText.Length > maxChars
            ? fullText.Substring(0, maxChars) + "\n…[truncated]"
            : fullText;

        try
        {
            _logger.LogInformation("Job {JobId}: entity node extracting entities from {SegmentCount} segments",
                context.JobId, segments.Count);

            var llmResult = await _llm.CompleteAsync(SystemPrompt, userInput, ct: ct);

            var entities = ParseEntities(llmResult.Content);

            return NodeExecutionResult.Ok(
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["entities"] = entities,
                    ["entityCount"] = entities.Count,
                    ["model"] = llmResult.Model,
                    ["inputTokens"] = llmResult.InputTokens,
                    ["outputTokens"] = llmResult.OutputTokens,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: entity node failed", context.JobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Parses the LLM JSON response into a list of entity dictionaries.
    /// Tolerates trailing text and markdown fences.
    /// </summary>
    private static List<Dictionary<string, object>> ParseEntities(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<Dictionary<string, object>>();
        }

        // Strip markdown code fences if present.
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

        // Locate the JSON object bounds.
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return new List<Dictionary<string, object>>();
        }

        var json = trimmed.Substring(start, end - start + 1);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new List<Dictionary<string, object>>();

            if (doc.RootElement.TryGetProperty("entities", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in item.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                            JsonValueKind.Number => prop.Value.GetDecimal(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.GetRawText(),
                        };
                    }
                    result.Add(dict);
                }
            }

            return result;
        }
        catch
        {
            // Malformed JSON from the model is treated as "no entities" rather than a hard failure.
            return new List<Dictionary<string, object>>();
        }
    }
}
