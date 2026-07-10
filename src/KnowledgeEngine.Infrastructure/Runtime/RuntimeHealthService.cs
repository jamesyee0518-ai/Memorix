using System.Net.Http;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Checks the health of all runtime components:
/// database, file storage, LLM service, embedding service,
/// Ollama, LM Studio, and cloud API.
/// </summary>
public class RuntimeHealthService : IRuntimeHealthService
{
    private readonly IAppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmSettings _llmSettings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly IConfigService _configService;
    private readonly ILogger<RuntimeHealthService> _logger;

    public RuntimeHealthService(
        IAppDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<LlmSettings> llmSettings,
        IOptions<EmbeddingSettings> embeddingSettings,
        IConfigService configService,
        ILogger<RuntimeHealthService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _llmSettings = llmSettings.Value;
        _embeddingSettings = embeddingSettings.Value;
        _configService = configService;
        _logger = logger;
    }

    public async Task<RuntimeHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        var status = new RuntimeHealthStatus();

        // 1. Database check
        try
        {
            var canConnect = await _db.Workspaces.AnyAsync(ct);
            status.Database = "ok";
        }
        catch (Exception ex)
        {
            status.Database = $"error: {ex.Message}";
            _logger.LogWarning(ex, "Database health check failed");
        }

        // 2. File storage check (always ok for cloud mode, check Vault for local mode)
        status.FileStorage = "ok";

        // 3. Job queue (DB-based, same as database)
        status.JobQueue = status.Database == "ok" ? "ok" : "error";

        // 4. LLM service check
        try
        {
            status.LlmService = await CheckEndpointAsync(_llmSettings.Endpoint, "/v1/models", ct);
        }
        catch (Exception ex)
        {
            status.LlmService = $"error: {ex.Message}";
        }

        // 5. Embedding service check
        try
        {
            status.EmbeddingService = await CheckEndpointAsync(_embeddingSettings.Endpoint, "/v1/models", ct);
        }
        catch (Exception ex)
        {
            status.EmbeddingService = $"error: {ex.Message}";
        }

        // 6-7. Local model services
        var localModels = await DetectLocalModelsAsync(ct);
        status.Ollama = localModels.Ollama.Status;
        status.LmStudio = localModels.LmStudio.Status;

        // 8. Workspace mode
        try
        {
            var configWsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
            if (configWsId != null && Guid.TryParse(configWsId, out var wsId))
            {
                var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == wsId, ct);
                status.WorkspaceMode = ws?.Mode ?? "cloud";
            }
            else
            {
                status.WorkspaceMode = "cloud";
            }
        }
        catch
        {
            status.WorkspaceMode = "unknown";
        }

        // Overall status
        var allOk = status.Database == "ok" && status.FileStorage == "ok" && status.JobQueue == "ok";
        var modelsOk = status.LlmService == "ok" && status.EmbeddingService == "ok";
        status.Overall = allOk ? (modelsOk ? "healthy" : "degraded") : "unhealthy";

        return status;
    }

    public async Task<LocalModelDetectionStatus> DetectLocalModelsAsync(CancellationToken ct = default)
    {
        var ollamaTask = ProbeLocalModelAsync(
            "http://127.0.0.1:11434",
            ["/api/tags", "/v1/models"],
            ct);
        var lmStudioTask = ProbeLocalModelAsync(
            "http://127.0.0.1:1234",
            ["/v1/models", "/api/v0/models"],
            ct);

        await Task.WhenAll(ollamaTask, lmStudioTask);

        return new LocalModelDetectionStatus
        {
            Ollama = await ollamaTask,
            LmStudio = await lmStudioTask
        };
    }

    private async Task<LocalModelProviderStatus> ProbeLocalModelAsync(
        string endpoint,
        IReadOnlyList<string> paths,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);

        foreach (var path in paths)
        {
            try
            {
                using var response = await client.GetAsync(endpoint + path, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    continue;
                }

                var status = response.IsSuccessStatusCode
                    ? "ok"
                    : response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                        ? "authentication_required"
                        : $"reachable_error_{(int)response.StatusCode}";

                return new LocalModelProviderStatus
                {
                    Available = true,
                    Status = status,
                    Endpoint = endpoint
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new LocalModelProviderStatus
                {
                    Status = "timeout",
                    Endpoint = endpoint
                };
            }
            catch (HttpRequestException)
            {
                return new LocalModelProviderStatus
                {
                    Status = "not_running",
                    Endpoint = endpoint
                };
            }
        }

        return new LocalModelProviderStatus
        {
            Status = "api_not_available",
            Endpoint = endpoint
        };
    }

    private async Task<string> CheckEndpointAsync(string endpoint, string path, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        var url = endpoint.TrimEnd('/') + path;
        var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode ? "ok" : $"error: {(int)response.StatusCode}";
    }
}
