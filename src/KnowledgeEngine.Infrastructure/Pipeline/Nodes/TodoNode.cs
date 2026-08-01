using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Action-item / todo extraction pipeline node.
/// <para>
/// NodeId = "todo", DependsOn = ["asr"].
/// </para>
/// <para>
/// Reads the full transcript from the ASR segments and asks the
/// <see cref="ILlmService"/> to extract action items as structured JSON.
/// The parsed todo list is published to the shared context for downstream
/// consumption (e.g. document enrichment, notification creation).
/// </para>
/// </summary>
public class TodoNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "todo";

    /// <inheritdoc/>
    public string DisplayName => "Todo Extraction";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "asr" };

    private const string SystemPrompt =
        "You are an action-item extraction engine. Read the transcript and extract all " +
        "action items, tasks, decisions, and follow-ups. Respond with STRICT JSON only " +
        "(no markdown fences). " +
        "Schema: {\"todos\":[{\"text\":string,\"assignee\":string|null,\"dueDate\":string|null,\"priority\":\"high|medium|low\",\"category\":\"task|decision|followup|risk\"}]}. " +
        "Use the same language as the transcript for todo text.";

    private readonly ILlmService _llm;
    private readonly ILogger<TodoNode> _logger;

    public TodoNode(ILlmService llm, ILogger<TodoNode> logger)
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
            return NodeExecutionResult.Fail("No segments available for todo extraction (ASR node produced no output).");
        }

        var fullText = string.Join("\n", segments.OrderBy(s => s.SegmentIndex).Select(s => s.Text));
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return NodeExecutionResult.Fail("Transcript text is empty; cannot extract todos.");
        }

        const int maxChars = 12000;
        var userInput = fullText.Length > maxChars
            ? fullText.Substring(0, maxChars) + "\n…[truncated]"
            : fullText;

        try
        {
            _logger.LogInformation("Job {JobId}: todo node extracting action items from {SegmentCount} segments",
                context.JobId, segments.Count);

            var llmResult = await _llm.CompleteAsync(SystemPrompt, userInput, ct: ct);

            var todos = ParseTodos(llmResult.Content);

            return NodeExecutionResult.Ok(
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["todos"] = todos,
                    ["todoCount"] = todos.Count,
                    ["model"] = llmResult.Model,
                    ["inputTokens"] = llmResult.InputTokens,
                    ["outputTokens"] = llmResult.OutputTokens,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: todo node failed", context.JobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Parses the LLM JSON response into a list of todo dictionaries.
    /// Tolerates trailing text and markdown fences.
    /// </summary>
    private static List<Dictionary<string, object>> ParseTodos(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<Dictionary<string, object>>();
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
            return new List<Dictionary<string, object>>();
        }

        var json = trimmed.Substring(start, end - start + 1);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new List<Dictionary<string, object>>();

            if (doc.RootElement.TryGetProperty("todos", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in item.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => (object?)(prop.Value.GetString() ?? string.Empty) ?? string.Empty,
                            JsonValueKind.Number => prop.Value.GetDecimal(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => string.Empty,
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
            return new List<Dictionary<string, object>>();
        }
    }
}
