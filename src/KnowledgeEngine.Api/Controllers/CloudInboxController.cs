using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Infrastructure.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/cloud-inbox")]
[Authorize]
public class CloudInboxController : BaseController
{
    private const string PullStrategySetting = "cloud_inbox_pull_strategy";
    private const string RetentionSetting = "cloud_inbox_retention";
    private readonly IWorkspaceService _workspaceService;
    private readonly IConfigService _configService;
    private readonly IKnowledgeRepository _repo;
    private readonly CloudInboxSyncService _syncService;
    private readonly IBindingService _bindingService;
    private readonly CloudInboxScheduleMonitor _scheduleMonitor;

    public CloudInboxController(
        IWorkspaceService workspaceService,
        IConfigService configService,
        IKnowledgeRepository repo,
        CloudInboxSyncService syncService,
        IBindingService bindingService,
        CloudInboxScheduleMonitor scheduleMonitor)
    {
        _workspaceService = workspaceService;
        _configService = configService;
        _repo = repo;
        _syncService = syncService;
        _bindingService = bindingService;
        _scheduleMonitor = scheduleMonitor;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var workspace = await GetCurrentWorkspaceAsync(ct);
        if (workspace == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        return Ok(ApiResponse<CloudInboxSettingsDto>.Ok(
            await MapSettingsAsync(workspace, ct), GetTraceId()));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateCloudInboxSettingsDto input, CancellationToken ct)
    {
        var workspace = await GetCurrentWorkspaceAsync(ct);
        if (workspace == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        var updated = await _workspaceService.UpdateWorkspaceAsync(workspace.Id, new UpdateWorkspaceDto
        {
            InboxEnabled = input.Enabled,
            SyncEnabled = input.Enabled,
            CloudApiBaseUrl = input.CloudApiBaseUrl?.Trim(),
            CloudWorkspaceId = input.CloudWorkspaceId?.Trim()
        }, ct);
        var pullStrategy = NormalizePullStrategy(input.PullStrategy);
        var retention = NormalizeRetention(input.Retention);
        await _repo.SetSettingAsync(
            workspace.Id.ToString(), PullStrategySetting, pullStrategy, ct);
        await _repo.SetSettingAsync(
            workspace.Id.ToString(), RetentionSetting, retention, ct);

        return Ok(ApiResponse<CloudInboxSettingsDto>.Ok(
            await MapSettingsAsync(updated, ct), GetTraceId()));
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var workspace = await GetCurrentWorkspaceAsync(ct);
        if (workspace == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        var latestLog = (await _repo.ListCloudInboxSyncLogsAsync(workspace.Id.ToString(), 1, ct)).FirstOrDefault();
        var schedule = _scheduleMonitor.Get(workspace.Id);

        return Ok(ApiResponse<CloudInboxStatusDto>.Ok(new CloudInboxStatusDto
        {
            Enabled = workspace.InboxEnabled,
            Connected = workspace.InboxEnabled &&
                !string.IsNullOrWhiteSpace(workspace.CloudApiBaseUrl) &&
                !string.IsNullOrWhiteSpace(workspace.CloudWorkspaceId),
            CloudApiBaseUrl = workspace.CloudApiBaseUrl,
            CloudWorkspaceId = workspace.CloudWorkspaceId,
            LastPulledAt = latestLog?.FinishedAt,
            WorkerActive = schedule.WorkerActive,
            IsRunning = schedule.IsRunning,
            CurrentPullStartedAt = schedule.StartedAt,
            NextPullAt = schedule.NextPullAt,
            ConsecutiveFailures = schedule.ConsecutiveFailures,
            RetryAt = schedule.RetryAt,
            LastScheduleError = schedule.LastError
        }, GetTraceId()));
    }

    [HttpPost("schedule/retry")]
    public async Task<IActionResult> Retry(CancellationToken ct)
    {
        var workspace = await GetCurrentWorkspaceAsync(ct);
        if (workspace == null)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }
        _scheduleMonitor.RequestRetry(workspace.Id);
        return Ok(ApiResponse<object>.Ok(new { queued = true }, GetTraceId()));
    }

    [HttpPost("schedule/cancel")]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        var workspace = await GetCurrentWorkspaceAsync(ct);
        if (workspace == null)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }
        return Ok(ApiResponse<object>.Ok(
            new { cancelled = _scheduleMonitor.Cancel(workspace.Id) },
            GetTraceId()));
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int limit = 10, CancellationToken ct = default)
    {
        var workspace = await GetCurrentWorkspaceAsync(ct);
        if (workspace == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        var logs = await _repo.ListCloudInboxSyncLogsAsync(
            workspace.Id.ToString(),
            Math.Clamp(limit, 1, 100),
            ct);
        return Ok(ApiResponse<List<CloudInboxSyncLogDto>>.Ok(logs, GetTraceId()));
    }

    [HttpPost("pull")]
    public async Task<IActionResult> Pull([FromBody] CloudInboxPullDto input, CancellationToken ct)
    {
        var workspace = await GetCurrentWorkspaceAsync(ct);
        if (workspace == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        var cloudApiBaseUrl = input.CloudApiBaseUrl ?? workspace.CloudApiBaseUrl;
        var cloudWorkspaceId = input.CloudWorkspaceId ?? workspace.CloudWorkspaceId;

        if (!workspace.InboxEnabled)
        {
            return BadRequest(ApiResponse<object>.FailObject("CLOUD_INBOX_DISABLED", "云端收件箱未启用", GetTraceId()));
        }
        if (string.IsNullOrWhiteSpace(cloudApiBaseUrl) || string.IsNullOrWhiteSpace(cloudWorkspaceId))
        {
            return BadRequest(ApiResponse<object>.FailObject("CLOUD_INBOX_NOT_CONFIGURED", "请先配置云端 API 地址和云端工作区 ID", GetTraceId()));
        }
        var authToken = input.AuthToken;
        if (string.IsNullOrWhiteSpace(authToken) && input.CloudAccountBindingId.HasValue)
        {
            authToken = await _bindingService.GetAccessTokenAsync(
                input.CloudAccountBindingId.Value, ct);
        }
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "NO_AUTH_TOKEN",
                "请先连接云端账号，或提供云端访问 Token",
                GetTraceId()));
        }

        var startedAt = DateTime.UtcNow;
        try
        {
            var result = await _syncService.PullAsync(
                workspace.Id.ToString(),
                cloudApiBaseUrl,
                cloudWorkspaceId,
                authToken,
                input.Retention,
                ct);

            var pulledAt = DateTime.UtcNow;
            await _repo.CreateCloudInboxSyncLogAsync(new CreateCloudInboxSyncLogInput
            {
                WorkspaceId = workspace.Id.ToString(),
                Direction = "pull",
                Status = result.FailedCount > 0 ? "partial" : "success",
                CloudApiBaseUrl = cloudApiBaseUrl,
                CloudWorkspaceId = cloudWorkspaceId,
                Retention = input.Retention,
                PulledCount = result.PulledCount,
                FailedCount = result.FailedCount,
                NextCursor = result.NextCursor,
                StartedAt = startedAt,
                FinishedAt = pulledAt
            }, ct);

            return Ok(ApiResponse<CloudInboxPullResultDto>.Ok(new CloudInboxPullResultDto
            {
                PulledCount = result.PulledCount,
                FailedCount = result.FailedCount,
                NextCursor = result.NextCursor,
                PulledAt = pulledAt
            }, GetTraceId()));
        }
        catch (Exception ex)
        {
            var failedAt = DateTime.UtcNow;
            await _repo.CreateCloudInboxSyncLogAsync(new CreateCloudInboxSyncLogInput
            {
                WorkspaceId = workspace.Id.ToString(),
                Direction = "pull",
                Status = "failed",
                CloudApiBaseUrl = cloudApiBaseUrl,
                CloudWorkspaceId = cloudWorkspaceId,
                Retention = input.Retention,
                FailedCount = 1,
                ErrorMessage = ex.Message,
                StartedAt = startedAt,
                FinishedAt = failedAt
            }, ct);

            return BadRequest(ApiResponse<object>.FailObject("CLOUD_INBOX_PULL_FAILED", ex.Message, GetTraceId()));
        }
    }

    private async Task<WorkspaceDto?> GetCurrentWorkspaceAsync(CancellationToken ct)
    {
        var currentWorkspaceId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (string.IsNullOrWhiteSpace(currentWorkspaceId)) return null;
        if (!Guid.TryParse(currentWorkspaceId, out var workspaceId)) return null;
        return await _workspaceService.GetWorkspaceAsync(workspaceId, ct);
    }

    private async Task<CloudInboxSettingsDto> MapSettingsAsync(
        WorkspaceDto workspace,
        CancellationToken ct) => new()
        {
            Enabled = workspace.InboxEnabled,
            PullStrategy = NormalizePullStrategy(await _repo.GetSettingAsync(
                workspace.Id.ToString(), PullStrategySetting, ct)),
            Retention = NormalizeRetention(await _repo.GetSettingAsync(
                workspace.Id.ToString(), RetentionSetting, ct)),
            CloudApiBaseUrl = workspace.CloudApiBaseUrl,
            CloudWorkspaceId = workspace.CloudWorkspaceId,
            SyncEnabled = workspace.SyncEnabled
        };

    private static string NormalizePullStrategy(string? value) =>
        value is "manual" or "onStartup" or "scheduled" ? value : "manual";

    private static string NormalizeRetention(string? value) =>
        value is "keep" or "deleteOriginal" or "deleteAll" ? value : "keep";
}

public class CloudInboxSettingsDto
{
    public bool Enabled { get; set; }
    public string PullStrategy { get; set; } = "manual";
    public string Retention { get; set; } = "keep";
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public bool SyncEnabled { get; set; }
}

public class UpdateCloudInboxSettingsDto
{
    public bool Enabled { get; set; }
    public string PullStrategy { get; set; } = "manual";
    public string Retention { get; set; } = "keep";
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
}

public class CloudInboxStatusDto
{
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public DateTime? LastPulledAt { get; set; }
    public int PendingRemoteCount { get; set; }
    public bool WorkerActive { get; set; }
    public bool IsRunning { get; set; }
    public DateTime? CurrentPullStartedAt { get; set; }
    public DateTime? NextPullAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? RetryAt { get; set; }
    public string? LastScheduleError { get; set; }
}

public class CloudInboxPullDto
{
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public string? AuthToken { get; set; }
    public Guid? CloudAccountBindingId { get; set; }
    public string Retention { get; set; } = "keep";
}

public class CloudInboxPullResultDto
{
    public int PulledCount { get; set; }
    public int FailedCount { get; set; }
    public string? NextCursor { get; set; }
    public DateTime PulledAt { get; set; }
}
