using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Ai;

public class OpenAiLlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly LlmSettings _settings;
    private readonly ILogger<OpenAiLlmService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiLlmService(
        HttpClient httpClient,
        IOptions<LlmSettings> settings,
        ILogger<OpenAiLlmService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var endpoint = _settings.Endpoint.TrimEnd('/');
        _httpClient.BaseAddress = new Uri(endpoint);

        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }
    }

    public async Task<LlmResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? model = null,
        CancellationToken ct = default)
    {
        var usedModel = string.IsNullOrEmpty(model) ? _settings.Model : model;
        var maxTokens = _settings.MaxTokens > 0 ? _settings.MaxTokens : 4096;

        var requestBody = new
        {
            model = usedModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = maxTokens,
            temperature = 0.3
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", requestBody, JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "LLM API network error: {Message}", ex.Message);
            throw new InvalidOperationException($"LLM API network error: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("LLM API returned status {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"LLM API returned status {(int)response.StatusCode}: {errorBody}");
        }

        ChatCompletionResponse? completion;
        try
        {
            completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM API response JSON");
            throw new InvalidOperationException($"Failed to parse LLM API response: {ex.Message}", ex);
        }

        if (completion == null || completion.Choices == null || completion.Choices.Count == 0)
        {
            throw new InvalidOperationException("LLM API returned empty choices");
        }

        return new LlmResult
        {
            Content = completion.Choices[0].Message?.Content ?? string.Empty,
            InputTokens = completion.Usage?.PromptTokens ?? 0,
            OutputTokens = completion.Usage?.CompletionTokens ?? 0,
            Model = completion.Model ?? usedModel
        };
    }

    private class ChatCompletionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public Message? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
