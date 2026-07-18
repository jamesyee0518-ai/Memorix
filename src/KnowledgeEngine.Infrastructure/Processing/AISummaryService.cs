using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Infrastructure.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

public class AISummaryService : IAISummaryService
{
    private readonly RuntimeRouter _runtimeRouter;
    private readonly ISummaryPromptManager _promptManager;
    private readonly LlmSettings _llmSettings;
    private readonly ILogger<AISummaryService> _logger;

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions KeyPointsSerializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    // Strict prompt suffix appended on the third retry attempt (§15.5)
    private const string StrictPromptSuffix =
        "\n\n重要：你必须只返回纯JSON，不要包含任何其他文字。不要包含markdown代码块标记。";

    public AISummaryService(
        RuntimeRouter runtimeRouter,
        ISummaryPromptManager promptManager,
        IOptions<LlmSettings> llmSettings,
        ILogger<AISummaryService> logger)
    {
        _runtimeRouter = runtimeRouter;
        _promptManager = promptManager;
        _llmSettings = llmSettings.Value;
        _logger = logger;
    }

    public async Task<AiSummaryResult> SummarizeAsync(string title, string contentText, string sourceType, CancellationToken ct = default)
    {
        var systemPrompt = _promptManager.GetSystemPrompt();
        var userPrompt = _promptManager.GetUserPrompt(title, contentText, sourceType);
        var promptVersion = _promptManager.GetPromptVersion();
        var model = _llmSettings.Model;

        _logger.LogInformation("Calling LLM for summarization: title={Title}, sourceType={SourceType}, model={Model}",
            title, sourceType, model);

        // §15.5 three-strategy retry: (1) first attempt, (2) repair JSON, (3) strict prompt.
        // rawOutput is captured from the LLM call so it can be repaired on parse failure.
        string rawOutput = string.Empty;
        LlmResult? llmResult = null;

        // First attempt
        try
        {
            llmResult = await CallLlmAsync(systemPrompt, userPrompt, model, ct);
            rawOutput = llmResult.Content;
            var analysis = ParseAnalysisResponse(rawOutput);
            ValidateAnalysisResponse(analysis);
            return BuildResult(analysis, llmResult ?? MakePlaceholderResult(rawOutput), promptVersion);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "LLM output was not valid JSON on first attempt; will try to repair.");
        }

        // Second attempt: repair the JSON and re-parse without a new LLM call
        try
        {
            var fixedJson = TryFixJson(rawOutput);
            var analysis = ParseAnalysisResponse(fixedJson);
            ValidateAnalysisResponse(analysis);
            _logger.LogWarning("Recovered malformed LLM JSON via JSON repair on second attempt.");
            // Token usage from the original (first) call is the best we have
            return BuildResult(analysis, llmResult ?? MakePlaceholderResult(rawOutput), promptVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JSON repair failed on second attempt; will retry with a stricter prompt.");
        }

        // Third attempt: re-request with a stricter prompt
        var strictPrompt = systemPrompt + StrictPromptSuffix;
        llmResult = await CallLlmAsync(strictPrompt, userPrompt, model, ct);
        rawOutput = llmResult.Content;
        var finalAnalysis = ParseAnalysisResponse(rawOutput);
        ValidateAnalysisResponse(finalAnalysis);
        _logger.LogWarning("Recovered LLM output via strict prompt on third attempt.");
        return BuildResult(finalAnalysis, llmResult, promptVersion);
    }

    /// <summary>
    /// Calls the LLM and logs token usage. Separated from parsing so the retry
    /// strategy can capture the raw output independently (§15.5).
    /// </summary>
    private async Task<LlmResult> CallLlmAsync(string systemPrompt, string userPrompt, string? model, CancellationToken ct)
    {
        var provider = await _runtimeRouter.GetModelProviderAsync(ct);
        // Passing null lets the routed provider use the workspace-specific model.
        var llmResult = await provider.ChatAsync(systemPrompt, userPrompt, null, ct);

        _logger.LogInformation("LLM response received: inputTokens={Input}, outputTokens={Output}, model={Model}",
            llmResult.InputTokens, llmResult.OutputTokens, llmResult.Model);

        return llmResult;
    }

    /// <summary>
    /// Builds the final <see cref="AiSummaryResult"/> from a parsed analysis and the LLM usage info.
    /// key_points objects are serialized to a JSON string for the frontend.
    /// </summary>
    private static AiSummaryResult BuildResult(AnalysisResponse analysis, LlmResult llmResult, string promptVersion)
    {
        // Serialize the structured key_points list into a JSON string (§15.4 key_points object schema)
        string? keyPointsJson = null;
        if (analysis.KeyPoints != null)
        {
            keyPointsJson = JsonSerializer.Serialize(analysis.KeyPoints, KeyPointsSerializeOptions);
        }

        return new AiSummaryResult
        {
            Summary = analysis.Summary,
            OneSentenceConclusion = analysis.OneSentenceConclusion,
            KeyPoints = keyPointsJson,
            BusinessSignals = analysis.BusinessSignals ?? new(),
            TechnicalSignals = analysis.TechnicalSignals ?? new(),
            Risks = analysis.Risks ?? new(),
            Opportunities = analysis.Opportunities ?? new(),
            ReusableMaterials = analysis.ReusableMaterials ?? new(),
            RecommendedTags = analysis.RecommendedTags ?? new(),
            ValueScore = analysis.ValueScore,
            QualityScore = analysis.QualityScore,
            ValueScoreReason = analysis.ValueScoreReason,
            ShouldDeepProcess = analysis.ShouldDeepProcess,
            AiRawOutput = llmResult.Content,
            AiModel = llmResult.Model,
            PromptVersion = promptVersion,
            InputTokens = llmResult.InputTokens,
            OutputTokens = llmResult.OutputTokens,
            Tags = (analysis.Tags ?? new()).Select(t => new TagResult
            {
                Name = t.Name,
                Type = t.Type,
                Description = t.Description,
                Confidence = t.Confidence,
                Reason = t.Reason
            }).ToList(),
            Entities = (analysis.Entities ?? new()).Select(e => new EntityResult
            {
                Name = e.Name,
                EntityType = e.EntityType,
                Description = e.Description,
                Confidence = e.Confidence,
                Importance = e.Importance,
                MentionCount = e.MentionCount,
                Aliases = e.Aliases,
                Examples = e.Examples,
                Role = e.Role,
                Sentiment = e.Sentiment
            }).ToList()
        };
    }

    /// <summary>
    /// Wraps a raw output string in an <see cref="LlmResult"/> with empty usage stats.
    /// Used when the result is recovered from JSON repair (no new LLM call was made).
    /// </summary>
    private static LlmResult MakePlaceholderResult(string rawOutput)
    {
        return new LlmResult { Content = rawOutput, Model = string.Empty };
    }

    private static AnalysisResponse ParseAnalysisResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("LLM returned empty content");
        }

        // Strip markdown code fences if present
        var json = StripMarkdownFences(content);

        // Let JsonException propagate so the §15.5 retry strategy can catch it
        var result = JsonSerializer.Deserialize<AnalysisResponse>(json, ParseOptions);
        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize LLM response");
        }
        return result;
    }

    private static string StripMarkdownFences(string content)
    {
        var json = content.Trim();
        if (json.StartsWith("```"))
        {
            // Remove the opening fence (```json or ```)
            var firstNewline = json.IndexOf('\n');
            if (firstNewline > 0)
            {
                json = json.Substring(firstNewline + 1);
            }
            // Remove the closing fence
            var closingFence = json.LastIndexOf("```");
            if (closingFence >= 0)
            {
                json = json.Substring(0, closingFence);
            }
            json = json.Trim();
        }
        return json;
    }

    /// <summary>
    /// Attempts to repair common LLM JSON mistakes: surrounding prose, markdown fences
    /// and trailing commas. Returns the (possibly) repaired JSON string.
    /// </summary>
    private static string TryFixJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = StripMarkdownFences(raw);

        // If the model wrapped the JSON in prose, trim to the outermost { ... }
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            text = text.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        // Remove trailing commas before } or ] (a frequent LLM mistake)
        text = Regex.Replace(text, @",\s*([}\]])", "$1");

        return text;
    }

    /// <summary>
    /// Validates the parsed AI output against the §15.4 contract. Throws
    /// <see cref="InvalidOperationException"/> when required fields are missing or out of range.
    /// </summary>
    private void ValidateAnalysisResponse(AnalysisResponse analysis)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(analysis.Summary))
            errors.Add("summary is empty");

        if (analysis.ValueScore.HasValue && (analysis.ValueScore < 0 || analysis.ValueScore > 100))
            errors.Add($"value_score {analysis.ValueScore} out of range 0-100");

        if (analysis.KeyPoints == null)
            errors.Add("key_points is null");

        if (analysis.RecommendedTags == null)
            errors.Add("recommended_tags is null");

        if (analysis.BusinessSignals == null)
            errors.Add("business_signals is null");
        if (analysis.TechnicalSignals == null)
            errors.Add("technical_signals is null");
        if (analysis.Risks == null)
            errors.Add("risks is null");
        if (analysis.Opportunities == null)
            errors.Add("opportunities is null");
        if (analysis.ReusableMaterials == null)
            errors.Add("reusable_materials is null");

        if (errors.Count > 0)
            throw new InvalidOperationException($"AI output validation failed: {string.Join(", ", errors)}");
    }

    // DTOs for LLM response parsing
    private class AnalysisResponse
    {
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("one_sentence_conclusion")]
        public string? OneSentenceConclusion { get; set; }

        [JsonPropertyName("key_points")]
        public List<KeyPointDto>? KeyPoints { get; set; }

        [JsonPropertyName("business_signals")]
        public List<string>? BusinessSignals { get; set; }

        [JsonPropertyName("technical_signals")]
        public List<string>? TechnicalSignals { get; set; }

        [JsonPropertyName("risks")]
        public List<string>? Risks { get; set; }

        [JsonPropertyName("opportunities")]
        public List<string>? Opportunities { get; set; }

        [JsonPropertyName("reusable_materials")]
        public List<string>? ReusableMaterials { get; set; }

        [JsonPropertyName("recommended_tags")]
        public List<string>? RecommendedTags { get; set; }

        [JsonPropertyName("value_score")]
        public int? ValueScore { get; set; }

        [JsonPropertyName("quality_score")]
        public int? QualityScore { get; set; }

        [JsonPropertyName("value_score_reason")]
        public string? ValueScoreReason { get; set; }

        [JsonPropertyName("should_deep_process")]
        public bool ShouldDeepProcess { get; set; } = true;

        [JsonPropertyName("tags")]
        public List<TagDto>? Tags { get; set; }

        [JsonPropertyName("entities")]
        public List<EntityDto>? Entities { get; set; }
    }

    private class KeyPointDto
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("importance")]
        public string? Importance { get; set; }

        [JsonPropertyName("evidence")]
        public string? Evidence { get; set; }
    }

    private class TagDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("confidence")]
        public decimal? Confidence { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    private class EntityDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("entity_type")]
        public string? EntityType { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("confidence")]
        public decimal? Confidence { get; set; }

        [JsonPropertyName("importance")]
        public decimal? Importance { get; set; }

        [JsonPropertyName("mention_count")]
        public int MentionCount { get; set; } = 1;

        [JsonPropertyName("aliases")]
        public List<string>? Aliases { get; set; }

        [JsonPropertyName("examples")]
        public List<string>? Examples { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("sentiment")]
        public string? Sentiment { get; set; }
    }
}
