using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Ai;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingSettings _settings;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiEmbeddingService(
        HttpClient httpClient,
        IOptions<EmbeddingSettings> settings,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var endpoint = _settings.Endpoint.TrimEnd('/');
        _httpClient.BaseAddress = new Uri(endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(60);

        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var results = await EmbedBatchAsync(new List<string> { text }, ct);
        return results[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(List<string> texts, CancellationToken ct = default)
    {
        if (texts == null || texts.Count == 0)
        {
            return new List<float[]>();
        }

        var requestBody = new
        {
            model = _settings.Model,
            input = texts
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/v1/embeddings", requestBody, JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Embedding API network error: {Message}", ex.Message);
            throw new InvalidOperationException($"Embedding API network error: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Embedding API returned status {StatusCode}: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Embedding API returned status {(int)response.StatusCode}: {errorBody}");
        }

        EmbeddingResponse? embeddingResponse;
        try
        {
            embeddingResponse = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse embedding API response JSON");
            throw new InvalidOperationException($"Failed to parse embedding API response: {ex.Message}", ex);
        }

        if (embeddingResponse?.Data == null || embeddingResponse.Data.Count == 0)
        {
            throw new InvalidOperationException("Embedding API returned empty data");
        }

        // Sort by index to maintain order
        var sorted = embeddingResponse.Data.OrderBy(d => d.Index).ToList();
        return sorted.Select(d => d.Embedding ?? Array.Empty<float>()).ToList();
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }

        [JsonPropertyName("usage")]
        public EmbeddingUsage? Usage { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private class EmbeddingUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
