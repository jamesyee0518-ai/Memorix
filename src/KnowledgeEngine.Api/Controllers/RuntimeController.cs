using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KnowledgeEngine.Application.Security;

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
    private readonly IWorkspaceService _workspaceService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAppDbContext _db;

    public RuntimeController(
        IRuntimeHealthService healthService,
        IWorkspaceService workspaceService,
        ICurrentUserContext currentUser,
        IAppDbContext db)
    {
        _healthService = healthService;
        _workspaceService = workspaceService;
        _currentUser = currentUser;
        _db = db;
    }

    /// <summary>
    /// Reports whether the local runtime is at a safe point for replacing the desktop application.
    /// Pending and paused work can survive a restart; actively running work must finish first.
    /// </summary>
    [HttpGet("update-safety")]
    [Authorize]
    public async Task<IActionResult> CheckUpdateSafety(CancellationToken ct)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        var workspace = await _workspaceService.GetCurrentWorkspaceAsync(userId, ct);
        var activeStatuses = new[] { "running", "processing" };
        var breakdown = new Dictionary<string, int>
        {
            ["资料处理"] = await _db.IngestJobs.AsNoTracking()
                .CountAsync(x => x.UserId == userId && activeStatuses.Contains(x.Status), ct),
            ["AI 处理"] = await _db.AiJobs.AsNoTracking()
                .CountAsync(x => x.UserId == userId && activeStatuses.Contains(x.Status), ct),
            ["多语言批处理"] = await _db.MultilingualBatchJobs.AsNoTracking()
                .CountAsync(x => x.UserId == userId && activeStatuses.Contains(x.Status), ct),
            ["实体治理"] = await _db.EntityGovernanceTasks.AsNoTracking()
                .CountAsync(x => x.UserId == userId && activeStatuses.Contains(x.Status), ct),
            ["报告生成"] = await _db.ReportJobs.AsNoTracking()
                .CountAsync(x => x.UserId == userId && activeStatuses.Contains(x.Status), ct),
            ["导出"] = await _db.ExportJobs.AsNoTracking()
                .CountAsync(x => x.UserId == userId && activeStatuses.Contains(x.Status), ct),
        };

        if (workspace != null)
        {
            breakdown["收件箱导入"] = await _db.ImportJobs.AsNoTracking()
                .CountAsync(x => x.WorkspaceId == workspace.Id && activeStatuses.Contains(x.Status), ct);
        }

        var activeJobs = breakdown.Values.Sum();
        return Ok(ApiResponse<object>.Ok(new
        {
            SafeToInstall = activeJobs == 0,
            ActiveJobs = activeJobs,
            Breakdown = breakdown.Where(x => x.Value > 0).ToDictionary(),
            Message = activeJobs == 0
                ? "当前可以安全安装更新。"
                : $"仍有 {activeJobs} 个任务正在运行，请等待任务完成后再安装。"
        }, GetTraceId()));
    }

    /// <summary>
    /// Check the current user's workspace with sanitized, user-actionable statuses.
    /// </summary>
    [HttpGet("workspace-health")]
    [Authorize]
    public async Task<IActionResult> CheckWorkspaceHealth(CancellationToken ct)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        var workspace = await _workspaceService.GetCurrentWorkspaceAsync(userId, ct);
        var status = await _healthService.CheckHealthAsync(ct);
        var issues = new List<string>();

        var knowledgeStorage = workspace == null ? "not_configured" : ToUserStatus(status.Database);
        var fileStorage = workspace == null ? "not_configured" : ToUserStatus(status.FileStorage);
        var backgroundProcessing = workspace == null ? "not_configured" : ToUserStatus(status.JobQueue);
        var aiService = ToUserStatus(status.LlmService);
        var embeddingService = ToUserStatus(status.EmbeddingService);
        var cloudSync = workspace?.Mode is "cloud" or "hybrid"
            ? ToOptionalUserStatus(status.CloudApi)
            : "not_configured";

        if (workspace == null) issues.Add("尚未配置工作区，请先完成工作区设置。");
        if (knowledgeStorage == "unavailable") issues.Add("知识库存储暂时不可用，请稍后重试。");
        if (fileStorage == "unavailable") issues.Add("文件存储暂时不可用，请检查工作区存储设置。");
        if (backgroundProcessing == "unavailable") issues.Add("后台处理暂时不可用，导入和分析任务可能延迟。");
        if (aiService == "unavailable") issues.Add("AI 模型服务暂时不可用，请检查模型配置。");
        if (embeddingService == "unavailable") issues.Add("向量模型服务暂时不可用，语义检索可能受影响。");
        if (cloudSync == "unavailable") issues.Add("云端同步暂时不可用，本地知识库仍可继续使用。");

        var coreUnavailable = knowledgeStorage == "unavailable" ||
            fileStorage == "unavailable" || backgroundProcessing == "unavailable";
        var aiUnavailable = aiService == "unavailable" || embeddingService == "unavailable";
        var overall = workspace == null
            ? "not_configured"
            : coreUnavailable
                ? "unavailable"
                : aiUnavailable || cloudSync == "unavailable"
                    ? "degraded"
                    : "healthy";

        var dto = new WorkspaceRuntimeHealthDto
        {
            WorkspaceId = workspace?.Id,
            WorkspaceName = workspace?.Name,
            WorkspaceMode = workspace?.Mode,
            KnowledgeStorage = knowledgeStorage,
            FileStorage = fileStorage,
            BackgroundProcessing = backgroundProcessing,
            AiService = aiService,
            EmbeddingService = embeddingService,
            CloudSync = cloudSync,
            Overall = overall,
            Issues = issues,
            CheckedAt = status.CheckedAt
        };

        return Ok(ApiResponse<WorkspaceRuntimeHealthDto>.Ok(dto, GetTraceId()));
    }

    /// <summary>
    /// Check the health of all platform runtime components.
    /// </summary>
    [HttpGet("health")]
    [HttpGet("platform-health")]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdmin)]
    public async Task<IActionResult> CheckPlatformHealth(CancellationToken ct)
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
    [Authorize(Policy = AuthorizationPolicies.PlatformAdmin)]
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

    private static string ToUserStatus(string status) =>
        status.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("healthy", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("running", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("connected", StringComparison.OrdinalIgnoreCase)
            ? "available"
            : status.Equals("not_configured", StringComparison.OrdinalIgnoreCase)
                ? "not_configured"
                : "unavailable";

    private static string ToOptionalUserStatus(string status) =>
        status.Equals("not_configured", StringComparison.OrdinalIgnoreCase)
            ? "not_configured"
            : ToUserStatus(status);
}
