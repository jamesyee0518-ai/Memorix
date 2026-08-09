using System.Text.Json;
using System.Net.Http.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/media/jobs")]
[Authorize]
public class MediaJobsController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IAiBillingService _billing;

    public MediaJobsController(IAppDbContext db, ICurrentUserContext currentUser, IHttpClientFactory httpClientFactory, IConfiguration configuration, IAiBillingService billing)
    {
        _db = db;
        _currentUser = currentUser;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _billing = billing;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMediaJobRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not Guid userId)
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        if (!Guid.TryParse(request.WorkspaceId, out var workspaceId) || string.IsNullOrWhiteSpace(request.Capability))
            return BadRequest(ApiResponse<object>.FailObject("INVALID_MEDIA_JOB", "workspaceId and capability are required", GetTraceId()));

        var workspace = await _db.Workspaces.FirstOrDefaultAsync(x => x.Id == workspaceId, ct);
        if (workspace is null || (workspace.UserId.HasValue && workspace.UserId != userId))
            return NotFound(ApiResponse<object>.FailObject("WORKSPACE_NOT_FOUND", "Workspace not found", GetTraceId()));

        var route = NormalizeRoute(request.RoutePreference);
        if (route is null)
            return BadRequest(ApiResponse<object>.FailObject("INVALID_MEDIA_ROUTE", "routePreference must be local_first, byok, or platform_cloud", GetTraceId()));

        var model = await ResolveModelAsync(request.Capability.Trim(), route, ct);
        if (model is null)
            return BadRequest(ApiResponse<object>.FailObject("NO_MEDIA_MODEL", "No enabled model can serve this capability and route", GetTraceId()));

        var inputAssetIds = (request.InputAssetIds ?? [])
            .Where(value => Guid.TryParse(value, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();
        if (inputAssetIds.Count != (request.InputAssetIds?.Count ?? 0))
            return BadRequest(ApiResponse<object>.FailObject("INVALID_MEDIA_ASSET", "Each inputAssetId must be a UUID", GetTraceId()));
        var ownedAssetCount = inputAssetIds.Count == 0 ? 0 : await _db.Files
            .CountAsync(file => file.WorkspaceId == workspaceId && inputAssetIds.Contains(file.Id), ct);
        if (ownedAssetCount != inputAssetIds.Count)
            return BadRequest(ApiResponse<object>.FailObject("MEDIA_ASSET_NOT_FOUND", "One or more input assets do not belong to this workspace", GetTraceId()));

        var now = DateTime.UtcNow;
        var job = new MediaJob
        {
            Id = Guid.CreateVersion7(), UserId = userId, WorkspaceId = workspaceId,
            Capability = request.Capability.Trim(), Route = route,
            ProviderId = model.ProviderId, ModelId = model.ModelId,
            ParametersJson = request.Parameters.ValueKind is JsonValueKind.Undefined ? "{}" : request.Parameters.GetRawText(),
            InputAssetIdsJson = JsonSerializer.Serialize(inputAssetIds),
            Status = MediaJobStatuses.Queued, EventsJson = "[]", CreatedAt = now
        };
        var billingJob = await _billing.CreateJobAsync(userId, new CreateAiBillingJobRequest
        {
            ClientJobId = $"media-{job.Id:N}", WorkspaceId = workspaceId,
            JobType = $"media.{job.Capability}", TargetType = "media_job", TargetId = job.Id,
            ExecutionMode = ToBillingExecutionMode(route), ProviderId = model.ProviderId, ModelId = model.ModelId,
            DataPolicy = route == "local_first" ? "on_device" : "provider_processing",
            ModelPolicy = route
        }, ct);
        job.BillingJobId = billingJob.JobId;
        _db.MediaJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        await DispatchAsync(job, ct);
        if (job.Status == MediaJobStatuses.Failed)
            await CompleteBillingAsync(job, "failed", job.ErrorCode, job.ErrorMessage, ct);
        return Accepted(ApiResponse<MediaJobResponse>.Ok(ToResponse(job), GetTraceId()));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? workspaceId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not Guid userId)
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        var query = _db.MediaJobs.Where(x => x.UserId == userId);
        if (Guid.TryParse(workspaceId, out var workspace)) query = query.Where(x => x.WorkspaceId == workspace);
        var jobs = await query.OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        return Ok(ApiResponse<List<MediaJobResponse>>.Ok(jobs.Select(ToResponse).ToList(), GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var job = await FindOwned(id, ct);
        return job is null ? NotFound(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_FOUND", "Media job not found", GetTraceId()))
            : Ok(ApiResponse<MediaJobResponse>.Ok(ToResponse(job), GetTraceId()));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var job = await FindOwned(id, ct);
        if (job is null) return NotFound(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_FOUND", "Media job not found", GetTraceId()));
        job.CancellationRequested = true;
        if (job.Status is MediaJobStatuses.Created or MediaJobStatuses.Queued)
        {
            job.Status = MediaJobStatuses.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        if (job.Status == MediaJobStatuses.Cancelled)
            await CompleteBillingAsync(job, "cancelled", null, null, ct);
        else
            await ForwardCancellationAsync(job, ct);
        return Ok(ApiResponse<MediaJobResponse>.Ok(ToResponse(job), GetTraceId()));
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var previous = await FindOwned(id, ct);
        if (previous is null) return NotFound(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_FOUND", "Media job not found", GetTraceId()));
        if (previous.Status is not (MediaJobStatuses.Failed or MediaJobStatuses.Cancelled))
            return Conflict(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_RETRYABLE", "Only failed or cancelled media jobs can be retried", GetTraceId()));

        var now = DateTime.UtcNow;
        var job = new MediaJob
        {
            Id = Guid.CreateVersion7(), UserId = previous.UserId, WorkspaceId = previous.WorkspaceId,
            Capability = previous.Capability, Route = previous.Route, ProviderId = previous.ProviderId, ModelId = previous.ModelId,
            ParametersJson = previous.ParametersJson, InputAssetIdsJson = previous.InputAssetIdsJson,
            Status = MediaJobStatuses.Queued, EventsJson = "[]", CreatedAt = now
        };
        var billingJob = await _billing.CreateJobAsync(previous.UserId, new CreateAiBillingJobRequest
        {
            ClientJobId = $"media-{job.Id:N}", WorkspaceId = job.WorkspaceId,
            JobType = $"media.{job.Capability}", TargetType = "media_job", TargetId = job.Id,
            ExecutionMode = ToBillingExecutionMode(job.Route), ProviderId = job.ProviderId ?? string.Empty, ModelId = job.ModelId ?? string.Empty,
            DataPolicy = job.Route == "local_first" ? "on_device" : "provider_processing", ModelPolicy = job.Route
        }, ct);
        job.BillingJobId = billingJob.JobId;
        _db.MediaJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        await DispatchAsync(job, ct);
        if (job.Status == MediaJobStatuses.Failed)
            await CompleteBillingAsync(job, "failed", job.ErrorCode, job.ErrorMessage, ct);
        return Accepted(ApiResponse<MediaJobResponse>.Ok(ToResponse(job), GetTraceId()));
    }

    private async Task<MediaJob?> FindOwned(Guid id, CancellationToken ct) =>
        _currentUser.UserId is Guid userId
            ? await _db.MediaJobs.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
            : null;

    private static MediaJobResponse ToResponse(MediaJob job) => new()
    {
        Id = job.Id, WorkspaceId = job.WorkspaceId, Capability = job.Capability,
        Status = job.Status, Route = job.Route, CancellationRequested = job.CancellationRequested,
        BillingJobId = job.BillingJobId,
        OutputJson = job.OutputAssetIdsJson,
        ErrorCode = job.ErrorCode, ErrorMessage = job.ErrorMessage, CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt, CompletedAt = job.CompletedAt
    };

    private static string? NormalizeRoute(string? route) => route?.Trim().ToLowerInvariant() switch
    {
        null or "" or "local_first" => "local_first",
        "byok" => "byok",
        "platform_cloud" => "platform_cloud",
        _ => null
    };

    private static string ToBillingExecutionMode(string route) => route switch
    {
        "local_first" => "LOCAL",
        "byok" => "USER_BYOK",
        _ => "MEMORIX_CLOUD"
    };

    private async Task<ModelRegistry?> ResolveModelAsync(string capability, string route, CancellationToken ct)
    {
        var models = await _db.ModelRegistries
            .Where(x => x.IsEnabled && x.Capability == capability)
            .ToListAsync(ct);
        var executionMode = route == "local_first" ? "LOCAL_DEVICE" : "THIRD_PARTY_CLOUD";
        var credentialMode = route == "byok" ? "USER_BYOK" : route == "platform_cloud" ? "PLATFORM_MANAGED" : null;
        return models.FirstOrDefault(x =>
            x.ExecutionModes.Split(',', StringSplitOptions.TrimEntries).Contains(executionMode) &&
            (credentialMode is null || x.CredentialModes.Split(',', StringSplitOptions.TrimEntries).Contains(credentialMode)));
    }

    private async Task DispatchAsync(MediaJob job, CancellationToken ct)
    {
        var baseUrl = _configuration["MediaExecution:BaseUrl"]?.TrimEnd('/');
        var token = _configuration["MediaExecution:DispatchToken"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
        {
            job.Status = MediaJobStatuses.Failed;
            job.ErrorCode = "MEDIA_EXECUTION_NOT_CONFIGURED";
            job.ErrorMessage = "Media execution service is not configured";
            await _db.SaveChangesAsync(ct);
            return;
        }
        var assetIds = JsonSerializer.Deserialize<List<Guid>>(job.InputAssetIdsJson) ?? [];
        var assets = assetIds.Count == 0 ? [] : await _db.Files
            .Where(file => file.WorkspaceId == job.WorkspaceId && assetIds.Contains(file.Id))
            .ToListAsync(ct);
        if (assets.Count != assetIds.Count)
        {
            job.Status = MediaJobStatuses.Failed;
            job.ErrorCode = "MEDIA_ASSET_NOT_FOUND";
            job.ErrorMessage = "An input asset is no longer available in this workspace";
            await _db.SaveChangesAsync(ct);
            return;
        }
        var controlPlaneUrl = (_configuration["MediaExecution:ControlPlaneBaseUrl"] ?? _configuration["PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
        if (assets.Count > 0 && string.IsNullOrWhiteSpace(controlPlaneUrl))
        {
            job.Status = MediaJobStatuses.Failed;
            job.ErrorCode = "MEDIA_ASSET_CHANNEL_NOT_CONFIGURED";
            job.ErrorMessage = "The controlled media asset channel is not configured";
            await _db.SaveChangesAsync(ct);
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/internal/media/jobs")
        {
            Content = JsonContent.Create(new
            {
                id = job.Id.ToString(), organization_id = job.WorkspaceId.ToString(),
                workspace_id = job.WorkspaceId.ToString(), capability = job.Capability,
                route_preference = job.Route,
                input_assets = assets.Select(asset => new {
                    id = asset.Id.ToString(),
                    url = $"{controlPlaneUrl}/api/internal/media/jobs/{job.Id}/assets/{asset.Id}",
                    mime_type = asset.MimeType,
                    sha256 = asset.Sha256,
                    size_bytes = asset.SizeBytes
                }).ToArray(),
                parameters = JsonDocument.Parse(job.ParametersJson).RootElement.Clone()
            })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        try
        {
            // Persist the hand-off before the remote service can issue a callback.
            // This prevents a fast terminal callback from being overwritten by a
            // stale "leased" update after SendAsync returns.
            job.Status = MediaJobStatuses.Leased;
            await _db.SaveChangesAsync(ct);
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Media service returned {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            job.Status = MediaJobStatuses.Failed;
            job.ErrorCode = "MEDIA_DISPATCH_FAILED";
            job.ErrorMessage = ex.Message;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task ForwardCancellationAsync(MediaJob job, CancellationToken ct)
    {
        var baseUrl = _configuration["MediaExecution:BaseUrl"]?.TrimEnd('/');
        var token = _configuration["MediaExecution:DispatchToken"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token)) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/internal/media/jobs/{job.Id}/cancel");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The cancellation flag stays durable in Memorix. A reconciliation
            // worker can retry transient dispatch failures without falsely
            // claiming the running model was stopped.
            job.ErrorCode = "MEDIA_CANCEL_FORWARD_FAILED";
            job.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task CompleteBillingAsync(MediaJob job, string status, string? errorCode, string? errorMessage, CancellationToken ct)
    {
        if (!job.BillingJobId.HasValue) return;
        await _billing.CompleteJobAsync(job.BillingJobId.Value, new CompleteAiJobRequest
        {
            Status = status, ErrorCode = errorCode, ErrorMessage = errorMessage
        }, ct);
    }
}

public sealed class CreateMediaJobRequest
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public string? RoutePreference { get; set; }
    public JsonElement Parameters { get; set; }
    public List<string>? InputAssetIds { get; set; }
}

public sealed class MediaJobResponse
{
    public Guid Id { get; set; }
    public Guid? BillingJobId { get; set; }
    public string OutputJson { get; set; } = "[]";
    public Guid WorkspaceId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public bool CancellationRequested { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
