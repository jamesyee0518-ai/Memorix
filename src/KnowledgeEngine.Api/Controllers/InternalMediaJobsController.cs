using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>Trusted callback endpoint used only by the configured media service.</summary>
[ApiController]
[Route("api/internal/media/jobs")]
public sealed class InternalMediaJobsController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IAiBillingService _billing;
    private readonly IHubContext<MediaJobHub> _mediaHub;
    private readonly ICredentialManager _credentials;
    private readonly IFileStorageFactory _storageFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    public InternalMediaJobsController(IAppDbContext db, IConfiguration configuration, IAiBillingService billing, IHubContext<MediaJobHub> mediaHub, ICredentialManager credentials, IFileStorageFactory storageFactory, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _configuration = configuration;
        _billing = billing;
        _mediaHub = mediaHub;
        _credentials = credentials;
        _storageFactory = storageFactory;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Streams one input asset to the trusted executor.  The asset must be
    /// explicitly attached to this job and belong to its workspace; this is not
    /// a general-purpose file download endpoint.
    /// </summary>
    [HttpGet("{id:guid}/assets/{assetId:guid}")]
    public async Task<IActionResult> DownloadInputAsset(Guid id, Guid assetId, CancellationToken ct)
    {
        if (!IsTrustedRequest()) return Unauthorized(ApiResponse<object>.FailObject("INVALID_SERVICE_CREDENTIAL", "Invalid media service credential", GetTraceId()));
        var job = await _db.MediaJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return NotFound(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_FOUND", "Media job not found", GetTraceId()));
        var inputIds = JsonSerializer.Deserialize<List<Guid>>(job.InputAssetIdsJson) ?? [];
        if (!inputIds.Contains(assetId)) return NotFound(ApiResponse<object>.FailObject("MEDIA_ASSET_NOT_FOUND", "Asset is not attached to this media job", GetTraceId()));
        var asset = await _db.Files.FirstOrDefaultAsync(x => x.Id == assetId && x.WorkspaceId == job.WorkspaceId, ct);
        if (asset is null) return NotFound(ApiResponse<object>.FailObject("MEDIA_ASSET_NOT_FOUND", "Asset is not available", GetTraceId()));
        var storage = await _storageFactory.GetProviderForWorkspaceAsync(job.WorkspaceId.ToString(), ct);
        var stream = await storage.DownloadFileAsync(asset.Bucket, asset.ObjectKey, ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!string.IsNullOrWhiteSpace(asset.Sha256)) Response.Headers["X-Media-Sha256"] = asset.Sha256;
        Response.Headers["X-Media-Size"] = asset.SizeBytes.ToString();
        return File(stream, asset.MimeType ?? "application/octet-stream", enableRangeProcessing: false);
    }

    [HttpPost("{id:guid}/events")]
    public async Task<IActionResult> RecordEvent(Guid id, [FromBody] MediaJobEventRequest request, CancellationToken ct)
    {
        if (!IsTrustedRequest()) return Unauthorized(ApiResponse<object>.FailObject("INVALID_SERVICE_CREDENTIAL", "Invalid media service credential", GetTraceId()));
        var job = await _db.MediaJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return NotFound(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_FOUND", "Media job not found", GetTraceId()));
        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 128)
            return BadRequest(ApiResponse<object>.FailObject("INVALID_MEDIA_EVENT", "A valid event id is required", GetTraceId()));
        var events = JsonSerializer.Deserialize<List<MediaJobEvent>>(job.EventsJson) ?? [];
        if (events.Any(x => string.Equals(x.Id, request.Id, StringComparison.Ordinal)))
            return Ok(ApiResponse<object>.Ok(new { id, job.Status, duplicate = true }, GetTraceId()));
        var eventData = request.Data.HasValue ? request.Data.Value.GetRawText() : null;
        if (request.Type == "completed")
        {
            if (string.IsNullOrWhiteSpace(eventData))
                return BadRequest(ApiResponse<object>.FailObject("INVALID_MEDIA_OUTPUT", "Completed media jobs require an artifact", GetTraceId()));
            eventData = await ArchiveOutputAsync(job, eventData, ct);
        }
        events.Add(new MediaJobEvent(request.Id, request.Type, request.Message, request.Progress, DateTime.UtcNow, eventData));
        job.EventsJson = JsonSerializer.Serialize(events);
        if (request.Type is "queued" or "retrying") job.Status = MediaJobStatuses.Queued;
        if (request.Type == "running") { job.Status = MediaJobStatuses.Running; job.StartedAt ??= DateTime.UtcNow; }
        if (request.Type == "completed")
        {
            job.Status = MediaJobStatuses.Completed;
            job.CompletedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(eventData)) job.OutputAssetIdsJson = eventData;
        }
        if (request.Type == "cancelled") { job.Status = MediaJobStatuses.Cancelled; job.CompletedAt = DateTime.UtcNow; }
        if (request.Type == "failed") { job.Status = MediaJobStatuses.Failed; job.ErrorCode = request.ErrorCode; job.ErrorMessage = request.Message; job.CompletedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
        await _mediaHub.Clients.Group(MediaJobHub.Group(id)).SendAsync("MediaJobEvent", new
        {
            id, request.Type, request.Message, request.Progress, request.ErrorCode, request.Data,
            status = job.Status, at = DateTime.UtcNow
        }, ct);
        if (request.Type is "completed" or "cancelled" or "failed")
            await CompleteBillingAsync(job, request.Type, request.ErrorCode, request.Message, ct);
        return Ok(ApiResponse<object>.Ok(new { id, job.Status }, GetTraceId()));
    }

    /// <summary>
    /// Provides an active MiniMax BYOK secret only to the authenticated internal
    /// executor for this specific BYOK job. The secret is never persisted in the
    /// job, included in event data, or returned to a browser.
    /// </summary>
    [HttpGet("{id:guid}/byok-credential")]
    public async Task<IActionResult> GetByokCredential(Guid id, CancellationToken ct)
    {
        if (!IsTrustedRequest()) return Unauthorized(ApiResponse<object>.FailObject("INVALID_SERVICE_CREDENTIAL", "Invalid media service credential", GetTraceId()));
        var job = await _db.MediaJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return NotFound(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_FOUND", "Media job not found", GetTraceId()));
        if (!string.Equals(job.Route, "byok", StringComparison.OrdinalIgnoreCase))
            return Conflict(ApiResponse<object>.FailObject("NOT_BYOK_JOB", "This media job is not using BYOK", GetTraceId()));
        if (!string.Equals(job.ProviderId, "minimax", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.FailObject("BYOK_PROVIDER_UNSUPPORTED", "The selected media provider does not support BYOK", GetTraceId()));

        var credential = await _credentials.FindActiveAsync("minimax", "user", job.UserId, ct);
        if (credential is null) return NotFound(ApiResponse<object>.FailObject("BYOK_CREDENTIAL_NOT_FOUND", "No active MiniMax BYOK credential is configured", GetTraceId()));
        var secret = await _credentials.GetSecretAsync(credential.Id, ct);
        if (string.IsNullOrWhiteSpace(secret)) return Conflict(ApiResponse<object>.FailObject("BYOK_CREDENTIAL_UNAVAILABLE", "The BYOK credential cannot be used", GetTraceId()));

        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse<object>.Ok(new { apiKey = secret }, GetTraceId()));
    }

    [HttpPost("{id:guid}/usage")]
    public async Task<IActionResult> RecordUsage(Guid id, [FromBody] MediaJobUsageRequest request, CancellationToken ct)
    {
        if (!IsTrustedRequest()) return Unauthorized(ApiResponse<object>.FailObject("INVALID_SERVICE_CREDENTIAL", "Invalid media service credential", GetTraceId()));
        if (request.Quantity <= 0 || request.Quantity > 86_400 || string.IsNullOrWhiteSpace(request.UsageType) ||
            string.IsNullOrWhiteSpace(request.Unit) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return BadRequest(ApiResponse<object>.FailObject("INVALID_MEDIA_USAGE", "Usage type, unit, positive quantity and idempotency key are required", GetTraceId()));
        var job = await _db.MediaJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return NotFound(ApiResponse<object>.FailObject("MEDIA_JOB_NOT_FOUND", "Media job not found", GetTraceId()));
        if (!job.BillingJobId.HasValue) return Conflict(ApiResponse<object>.FailObject("MEDIA_BILLING_NOT_READY", "Media job has no billing record", GetTraceId()));
        var usage = await _billing.RecordUsageAsync(new RecordUsageEventRequest
        {
            JobId = job.BillingJobId.Value,
            ProviderId = job.ProviderId ?? "local",
            ModelId = job.ModelId ?? "unknown",
            UsageType = request.UsageType.Trim(),
            Quantity = request.Quantity,
            Unit = request.Unit.Trim(),
            UsageSource = "EXECUTOR",
            OccurredAt = DateTime.UtcNow,
            IdempotencyKey = request.IdempotencyKey.Trim(),
            RawUsageJson = request.RawUsageJson
        }, ct);
        return Ok(ApiResponse<UsageEventResponse>.Ok(usage, GetTraceId()));
    }

    private bool IsTrustedRequest()
    {
        var configured = _configuration["MediaExecution:ServiceToken"];
        var provided = Request.Headers["X-Media-Service-Token"].ToString();
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured), Encoding.UTF8.GetBytes(provided));
    }

    private async Task<string> ArchiveOutputAsync(MediaJob job, string executorResultJson, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(executorResultJson);
        if (!document.RootElement.TryGetProperty("artifact", out var artifact))
            throw new InvalidOperationException("Executor did not provide an artifact");
        var filename = artifact.TryGetProperty("filename", out var filenameValue) ? filenameValue.GetString() : null;
        var mimeType = artifact.TryGetProperty("mime_type", out var mimeValue) ? mimeValue.GetString() : null;
        var sha256 = artifact.TryGetProperty("sha256", out var hashValue) ? hashValue.GetString() : null;
        var size = artifact.TryGetProperty("size_bytes", out var sizeValue) && sizeValue.TryGetInt64(out var parsedSize) ? parsedSize : 0;
        if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename || !filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mimeType, "video/mp4", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(sha256) || size <= 0 || size > 2L * 1024 * 1024 * 1024)
            throw new InvalidOperationException("Executor returned an invalid output artifact");
        var executionUrl = _configuration["MediaExecution:BaseUrl"]?.TrimEnd('/');
        var token = _configuration["MediaExecution:DispatchToken"];
        if (string.IsNullOrWhiteSpace(executionUrl) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Media execution archive channel is not configured");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{executionUrl}/internal/media/jobs/{job.Id}/artifacts/{Uri.EscapeDataString(filename)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength && contentLength != size)
            throw new InvalidOperationException("Executor output size does not match artifact metadata");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bucket = "knowledge-engine";
        var assetId = Guid.CreateVersion7();
        var objectKey = $"workspaces/{job.WorkspaceId}/media/{job.Id}/outputs/{assetId}.mp4";
        var storage = await _storageFactory.GetProviderForWorkspaceAsync(job.WorkspaceId.ToString(), ct);
        await storage.UploadFileAsync(bucket, objectKey, stream, "video/mp4", size, ct);
        _db.Files.Add(new FileObject
        {
            Id = assetId, WorkspaceId = job.WorkspaceId, Bucket = bucket, ObjectKey = objectKey,
            OriginalFilename = filename, MimeType = "video/mp4", Extension = "mp4", SizeBytes = size,
            Sha256 = sha256, StorageProvider = "managed", CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return JsonSerializer.Serialize(new[] { new { asset_id = assetId, filename, mime_type = "video/mp4", size_bytes = size, sha256 } });
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

public sealed record MediaJobEvent(string Id, string Type, string? Message, decimal? Progress, DateTime At, string? DataJson = null);
public sealed class MediaJobEventRequest
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Message { get; set; }
    public decimal? Progress { get; set; }
    public string? ErrorCode { get; set; }
    public JsonElement? Data { get; set; }
}

public sealed class MediaJobUsageRequest
{
    public string UsageType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RawUsageJson { get; set; }
}
