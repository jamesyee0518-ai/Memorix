using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Runtime health check API.
/// Reports the status of database, file storage, model services, etc.
/// </summary>
[ApiController]
[Route("api/runtime")]
public class RuntimeController : BaseController
{
    private readonly IRuntimeHealthService _healthService;

    public RuntimeController(IRuntimeHealthService healthService)
    {
        _healthService = healthService;
    }

    /// <summary>
    /// Check the health of all runtime components.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckHealth(CancellationToken ct)
    {
        var status = await _healthService.CheckHealthAsync(ct);

        var dto = new RuntimeHealthDto
        {
            Database = status.Database,
            FileStorage = status.FileStorage,
            JobQueue = status.JobQueue,
            LlmService = status.LlmService,
            EmbeddingService = status.EmbeddingService,
            Ollama = status.Ollama,
            LmStudio = status.LmStudio,
            CloudApi = status.CloudApi,
            Overall = status.Overall,
            WorkspaceMode = status.WorkspaceMode,
            CheckedAt = status.CheckedAt
        };

        return Ok(ApiResponse<RuntimeHealthDto>.Ok(dto, GetTraceId()));
    }

    /// <summary>
    /// Detect local Ollama and LM Studio services from the local API process.
    /// </summary>
    [HttpGet("local-models")]
    [AllowAnonymous]
    public async Task<IActionResult> DetectLocalModels(CancellationToken ct)
    {
        var status = await _healthService.DetectLocalModelsAsync(ct);
        var dto = new LocalModelDetectionDto
        {
            Ollama = MapProvider(status.Ollama),
            LmStudio = MapProvider(status.LmStudio),
            CheckedAt = status.CheckedAt
        };

        return Ok(ApiResponse<LocalModelDetectionDto>.Ok(dto, GetTraceId()));
    }

    private static LocalModelProviderDetectionDto MapProvider(LocalModelProviderStatus status)
    {
        return new LocalModelProviderDetectionDto
        {
            Available = status.Available,
            Status = status.Status,
            Endpoint = status.Endpoint
        };
    }
}
