using System.Net.Http;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Ai;

/// <summary>
/// Unified model provider that wraps ILlmService + IEmbeddingService.
/// Implements IModelProvider so business code has a single interface.
/// </summary>
public class UnifiedModelProvider : IModelProvider
{
    private readonly ILlmService _llmService;
    private readonly IEmbeddingService _embeddingService;
    private readonly LlmSettings _llmSettings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UnifiedModelProvider> _logger;

    public string ProviderName { get; }

    public UnifiedModelProvider(
        ILlmService llmService,
        IEmbeddingService embeddingService,
        IOptions<LlmSettings> llmSettings,
        IOptions<EmbeddingSettings> embeddingSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<UnifiedModelProvider> logger,
        string providerName = "lmstudio")
    {
        _llmService = llmService;
        _embeddingService = embeddingService;
        _llmSettings = llmSettings.Value;
        _embeddingSettings = embeddingSettings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        ProviderName = providerName;
    }

    public Task<LlmResult> ChatAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
    {
        return _llmService.CompleteAsync(systemPrompt, userPrompt, model, ct);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        return _embeddingService.EmbedAsync(text, ct);
    }

    public Task<List<float[]>> EmbedBatchAsync(List<string> texts, CancellationToken ct = default)
    {
        return _embeddingService.EmbedBatchAsync(texts, ct);
    }

    public async Task<ModelHealthStatus> HealthCheckAsync(CancellationToken ct = default)
    {
        var status = new ModelHealthStatus
        {
            Provider = ProviderName,
            ChatModel = _llmSettings.Model,
            EmbeddingModel = _embeddingSettings.Model
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync(_llmSettings.Endpoint.TrimEnd('/') + "/v1/models", ct);
            status.Status = response.IsSuccessStatusCode ? "ok" : $"error: {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            status.Status = "unreachable";
            status.ErrorMessage = ex.Message;
        }

        return status;
    }
}
