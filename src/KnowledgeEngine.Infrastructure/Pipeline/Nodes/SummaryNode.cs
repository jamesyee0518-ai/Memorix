using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Summary generation pipeline node.
/// <para>
/// NodeId = "summary", DependsOn = ["asr"].
/// </para>
/// <para>
/// Builds the full transcript from the ASR segments and asks the
/// <see cref="ILlmService"/> to produce a concise summary. The summary text,
/// token usage, and model identifier are published to the shared context for
/// downstream consumers (e.g. document creation, UI display).
/// </para>
/// <para>
/// When an <see cref="IPromptRegistryService"/> is available, the system prompt
/// is resolved from the registry using the key <c>summary.transcription</c>.
/// Otherwise, a built-in default prompt is used.
/// </para>
/// </summary>
public class SummaryNode : IPipelineNode
{
    private const string PromptKey = "summary.transcription";

    /// <inheritdoc/>
    public string NodeId => "summary";

    /// <inheritdoc/>
    public string DisplayName => "Summary Generation";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "asr" };

    private const string DefaultSystemPrompt =
        "You are a professional meeting and audio transcription assistant. " +
        "Read the following transcript and produce a concise, well-structured summary. " +
        "Include: (1) a one-sentence overview, (2) key points as a bullet list, " +
        "(3) any action items or decisions. Respond in the same language as the transcript.";

    private readonly ILlmService _llm;
    private readonly IPromptRegistryService? _promptRegistry;
    private readonly ILogger<SummaryNode> _logger;

    public SummaryNode(
        ILlmService llm,
        ILogger<SummaryNode> logger,
        IPromptRegistryService? promptRegistry = null)
    {
        _llm = llm;
        _promptRegistry = promptRegistry;
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
            return NodeExecutionResult.Fail("No segments available for summary (ASR node produced no output).");
        }

        var fullText = string.Join("\n", segments.OrderBy(s => s.SegmentIndex).Select(s => s.Text));
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return NodeExecutionResult.Fail("Transcript text is empty; cannot generate summary.");
        }

        // Truncate to a safe working length to avoid token-limit errors.
        const int maxChars = 12000;
        var userInput = fullText.Length > maxChars
            ? fullText.Substring(0, maxChars) + "\n…[truncated]"
            : fullText;

        // Resolve the system prompt from the Prompt Registry, falling back to default.
        string systemPrompt = DefaultSystemPrompt;
        string? promptVersion = null;

        if (_promptRegistry != null)
        {
            try
            {
                var registryPrompt = await _promptRegistry.GetActivePromptAsync(PromptKey, null, ct);
                if (registryPrompt != null && !string.IsNullOrWhiteSpace(registryPrompt.SystemPrompt))
                {
                    systemPrompt = registryPrompt.SystemPrompt;
                    promptVersion = registryPrompt.Version;

                    // Apply template if the registry prompt provides one.
                    if (!string.IsNullOrWhiteSpace(registryPrompt.UserPromptTemplate))
                    {
                        userInput = registryPrompt.UserPromptTemplate
                            .Replace("{{content}}", userInput, StringComparison.OrdinalIgnoreCase);
                    }

                    _logger.LogInformation(
                        "Job {JobId}: using Prompt Registry {PromptKey} v{Version} for summary",
                        context.JobId, PromptKey, promptVersion);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Job {JobId}: failed to resolve prompt from registry, using default",
                    context.JobId);
            }
        }

        try
        {
            _logger.LogInformation("Job {JobId}: summary node generating summary from {SegmentCount} segments",
                context.JobId, segments.Count);

            var llmResult = await _llm.CompleteAsync(systemPrompt, userInput, ct: ct);

            var output = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["summary"] = llmResult.Content,
                ["model"] = llmResult.Model,
                ["inputTokens"] = llmResult.InputTokens,
                ["outputTokens"] = llmResult.OutputTokens,
            };

            if (promptVersion != null)
            {
                output["promptVersion"] = promptVersion;
            }

            return NodeExecutionResult.Ok(output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: summary node failed", context.JobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }
}
