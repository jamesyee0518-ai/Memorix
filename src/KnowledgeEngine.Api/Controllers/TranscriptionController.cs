using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Transcription job management API.
/// Create, monitor, and manage audio transcription tasks.
/// </summary>
[ApiController]
[Route("api/transcription")]
[Authorize]
public class TranscriptionController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAudioCapabilityOrchestrator _orchestrator;
    private readonly IProviderRegistry _providerRegistry;
    private readonly IAudioPolicyRouter _policyRouter;
    private readonly IVersionMergeService _versionMergeService;
    private readonly ILogger<TranscriptionController> _logger;

    public TranscriptionController(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        IAudioCapabilityOrchestrator orchestrator,
        IProviderRegistry providerRegistry,
        IAudioPolicyRouter policyRouter,
        IVersionMergeService versionMergeService,
        ILogger<TranscriptionController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _orchestrator = orchestrator;
        _providerRegistry = providerRegistry;
        _policyRouter = policyRouter;
        _versionMergeService = versionMergeService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new transcription job for an existing audio asset.
    /// </summary>
    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob([FromBody] CreateTranscriptionJobRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var workspaceId = await GetWorkspaceIdFromAssetAsync(request.AudioAssetId, ct);
        if (workspaceId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("ASSET_NOT_FOUND", "Audio asset not found", GetTraceId()));
        }

        try
        {
            var jobId = await _orchestrator.StartTranscriptionAsync(
                request.AudioAssetId, request, userId, workspaceId, ct);

            return Ok(ApiResponse<object>.Ok(new { jobId, status = "pending" }, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start transcription job for asset {AudioAssetId}", request.AudioAssetId);
            return StatusCode(500, ApiResponse<object>.FailObject("JOB_START_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Gets the status and segments of a transcription job.
    /// </summary>
    [HttpGet("jobs/{jobId}")]
    public async Task<IActionResult> GetJobStatus(Guid jobId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        try
        {
            var status = await _orchestrator.GetJobStatusAsync(jobId, ct);
            return Ok(ApiResponse<TranscriptionStatusResponse>.Ok(status, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get job status for {JobId}", jobId);
            return NotFound(ApiResponse<object>.FailObject("JOB_NOT_FOUND", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Cancels an in-progress transcription job.
    /// </summary>
    [HttpPost("jobs/{jobId}/cancel")]
    public async Task<IActionResult> CancelJob(Guid jobId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        try
        {
            await _orchestrator.CancelJobAsync(jobId, ct);
            return Ok(ApiResponse<object>.Ok(new { jobId, status = "cancelled" }, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel job {JobId}", jobId);
            return BadRequest(ApiResponse<object>.FailObject("CANCEL_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Lists transcription jobs for the current user.
    /// </summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> ListJobs(
        [FromQuery] string? status,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var query = _db.TranscriptionJobs.Where(j => j.UserId == userId);

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(j => j.Status == status);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip(offset)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(j => new TranscriptionJobDto
            {
                Id = j.Id,
                AudioAssetId = j.AudioAssetId,
                WorkspaceId = j.WorkspaceId,
                UserId = j.UserId,
                ExecutionMode = j.ExecutionMode,
                CredentialMode = j.CredentialMode,
                ProviderId = j.ProviderId,
                ModelId = j.ModelId,
                FallbackPolicy = j.FallbackPolicy,
                Language = j.Language,
                EnableVad = j.EnableVad,
                EnableSpeakerDiarization = j.EnableSpeakerDiarization,
                EnablePunctuation = j.EnablePunctuation,
                Hotwords = j.Hotwords,
                EstimatedCost = j.EstimatedCost,
                ActualCost = j.ActualCost,
                Status = j.Status,
                ErrorMessage = j.ErrorMessage,
                DocumentId = j.DocumentId,
                SegmentCount = j.SegmentCount,
                CreatedAt = j.CreatedAt,
                StartedAt = j.StartedAt,
                CompletedAt = j.CompletedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<List<TranscriptionJobDto>>.Ok(jobs, GetTraceId()));
    }

    /// <summary>
    /// Gets transcription segments for a job.
    /// </summary>
    [HttpGet("jobs/{jobId}/segments")]
    public async Task<IActionResult> GetSegments(Guid jobId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var segments = await _db.TranscriptionSegments
            .Where(s => s.TranscriptionJobId == jobId)
            .OrderBy(s => s.SegmentIndex)
            .Select(s => new TranscriptionSegmentDto
            {
                Id = s.Id,
                TranscriptionJobId = s.TranscriptionJobId,
                DocumentId = s.DocumentId,
                SegmentUuid = s.SegmentUuid,
                SourceStartMs = s.SourceStartMs,
                SourceEndMs = s.SourceEndMs,
                ProviderId = s.ProviderId,
                ModelId = s.ModelId,
                Confidence = s.Confidence,
                SpeakerKey = s.SpeakerKey,
                Text = s.Text,
                Version = s.Version,
                SegmentIndex = s.SegmentIndex,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<List<TranscriptionSegmentDto>>.Ok(segments, GetTraceId()));
    }

    /// <summary>
    /// Lists all available ASR providers and their descriptors.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> ListProviders(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var asrDescriptors = await _providerRegistry.GetAsrDescriptorsAsync(ct);
        return Ok(ApiResponse<List<AsrProviderDescriptor>>.Ok(asrDescriptors, GetTraceId()));
    }

    // ================================================================
    // Segment editing endpoints
    // ================================================================

    /// <summary>
    /// Updates the text of a single transcription segment (creates a USER_EDITED version).
    /// </summary>
    [HttpPut("segments/{segmentId}")]
    public async Task<IActionResult> EditSegment(Guid segmentId, [FromBody] EditSegmentRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var segment = await _db.TranscriptionSegments.FirstOrDefaultAsync(s => s.Id == segmentId, ct);
        if (segment == null)
        {
            return NotFound(ApiResponse<object>.FailObject("SEGMENT_NOT_FOUND", "Segment not found", GetTraceId()));
        }

        // Save the current text as the baseline if this is the first edit
        var existingUserVersion = await _db.TranscriptionVersions
            .Where(v => v.SegmentUuid == segment.SegmentUuid && v.Version == Domain.Enums.SegmentVersions.UserEdited)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Create a USER_EDITED version record
        var userVersion = new TranscriptionVersion
        {
            Id = Guid.NewGuid(),
            TranscriptionJobId = segment.TranscriptionJobId,
            SegmentUuid = segment.SegmentUuid,
            Version = Domain.Enums.SegmentVersions.UserEdited,
            ParentVersionId = existingUserVersion?.Id,
            Text = request.Text,
            ProviderId = segment.ProviderId,
            ModelId = segment.ModelId,
            CreatedBy = _currentUser.UserId.ToString(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.TranscriptionVersions.Add(userVersion);

        // Update the segment's current text and version label
        segment.Text = request.Text;
        segment.Version = Domain.Enums.SegmentVersions.UserEdited;
        segment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            segmentId = segment.Id,
            segmentUuid = segment.SegmentUuid,
            versionId = userVersion.Id,
            version = userVersion.Version,
            text = segment.Text
        }, GetTraceId()));
    }

    /// <summary>
    /// Gets the full version history for a segment (version tree).
    /// </summary>
    [HttpGet("segments/{segmentId}/versions")]
    public async Task<IActionResult> GetSegmentVersions(Guid segmentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var segment = await _db.TranscriptionSegments.FirstOrDefaultAsync(s => s.Id == segmentId, ct);
        if (segment == null)
        {
            return NotFound(ApiResponse<object>.FailObject("SEGMENT_NOT_FOUND", "Segment not found", GetTraceId()));
        }

        var versions = await _versionMergeService.GetVersionHistoryAsync(segment.SegmentUuid, ct);

        var result = versions.Select(v => new
        {
            id = v.Id,
            segmentUuid = v.SegmentUuid,
            version = v.Version,
            parentVersionId = v.ParentVersionId,
            text = v.Text,
            providerId = v.ProviderId,
            modelId = v.ModelId,
            createdBy = v.CreatedBy,
            createdAt = v.CreatedAt
        }).ToList();

        return Ok(ApiResponse<object>.Ok(result, GetTraceId()));
    }

    /// <summary>
    /// Performs a three-way merge for a segment, reconciling user edits
    /// with server re-transcription against the original baseline.
    /// </summary>
    [HttpPost("segments/{segmentId}/merge")]
    public async Task<IActionResult> MergeSegment(Guid segmentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var segment = await _db.TranscriptionSegments.FirstOrDefaultAsync(s => s.Id == segmentId, ct);
        if (segment == null)
        {
            return NotFound(ApiResponse<object>.FailObject("SEGMENT_NOT_FOUND", "Segment not found", GetTraceId()));
        }

        try
        {
            var merged = await _versionMergeService.MergeAsync(
                segment.TranscriptionJobId, segment.SegmentUuid, ct);

            // Update the segment to show the merged text
            segment.Text = merged.Text;
            segment.Version = Domain.Enums.SegmentVersions.Merged;
            segment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(ApiResponse<object>.Ok(new
            {
                segmentId = segment.Id,
                segmentUuid = segment.SegmentUuid,
                mergedVersionId = merged.Id,
                mergedText = merged.Text,
                parentVersionId = merged.ParentVersionId
            }, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailObject("MERGE_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Bulk merge all segments for a transcription job.
    /// </summary>
    [HttpPost("jobs/{jobId}/merge-all")]
    public async Task<IActionResult> MergeAllSegments(Guid jobId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var segments = await _db.TranscriptionSegments
            .Where(s => s.TranscriptionJobId == jobId)
            .ToListAsync(ct);

        if (segments.Count == 0)
        {
            return NotFound(ApiResponse<object>.FailObject("NO_SEGMENTS", "No segments found for job", GetTraceId()));
        }

        var results = new List<object>();
        var merged = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var segment in segments)
        {
            try
            {
                var mergeResult = await _versionMergeService.MergeAsync(jobId, segment.SegmentUuid, ct);
                segment.Text = mergeResult.Text;
                segment.Version = Domain.Enums.SegmentVersions.Merged;
                segment.UpdatedAt = DateTime.UtcNow;
                merged++;
                results.Add(new { segmentUuid = segment.SegmentUuid, status = "merged", versionId = mergeResult.Id });
            }
            catch (InvalidOperationException)
            {
                skipped++;
                results.Add(new { segmentUuid = segment.SegmentUuid, status = "skipped" });
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new { segmentUuid = segment.SegmentUuid, status = "failed", error = ex.Message });
            }
        }

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            jobId,
            totalSegments = segments.Count,
            merged,
            skipped,
            failed,
            results
        }, GetTraceId()));
    }

    /// <summary>
    /// Publishes the current version of a segment (marks it as PUBLISHED).
    /// </summary>
    [HttpPost("segments/{segmentId}/publish")]
    public async Task<IActionResult> PublishSegment(Guid segmentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var segment = await _db.TranscriptionSegments.FirstOrDefaultAsync(s => s.Id == segmentId, ct);
        if (segment == null)
        {
            return NotFound(ApiResponse<object>.FailObject("SEGMENT_NOT_FOUND", "Segment not found", GetTraceId()));
        }

        // Create a PUBLISHED version record
        var publishedVersion = new TranscriptionVersion
        {
            Id = Guid.NewGuid(),
            TranscriptionJobId = segment.TranscriptionJobId,
            SegmentUuid = segment.SegmentUuid,
            Version = Domain.Enums.SegmentVersions.Published,
            ParentVersionId = null, // Will be linked to the current version
            Text = segment.Text,
            ProviderId = segment.ProviderId,
            ModelId = segment.ModelId,
            CreatedBy = _currentUser.UserId.ToString(),
            CreatedAt = DateTime.UtcNow,
        };

        // Link to the most recent version as parent
        var latestVersion = await _db.TranscriptionVersions
            .Where(v => v.SegmentUuid == segment.SegmentUuid)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
        publishedVersion.ParentVersionId = latestVersion?.Id;

        _db.TranscriptionVersions.Add(publishedVersion);

        segment.Version = Domain.Enums.SegmentVersions.Published;
        segment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            segmentId = segment.Id,
            segmentUuid = segment.SegmentUuid,
            publishedVersionId = publishedVersion.Id
        }, GetTraceId()));
    }

    /// <summary>
    /// Explains the routing decision for a given context (for debugging and UI display).
    /// </summary>
    [HttpPost("routing/explain")]
    public async Task<IActionResult> ExplainRouting([FromBody] AsrRoutingContext context, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var decision = await _policyRouter.ExplainAsrRoutingAsync(context, ct);
        return Ok(ApiResponse<RoutingDecision>.Ok(decision, GetTraceId()));
    }

    private async Task<Guid?> GetWorkspaceIdFromAssetAsync(Guid audioAssetId, CancellationToken ct)
    {
        var asset = await _db.AudioAssets
            .Where(a => a.Id == audioAssetId)
            .Select(a => new { a.WorkspaceId, a.UserId })
            .FirstOrDefaultAsync(ct);

        if (asset == null) return null;
        if (asset.UserId != _currentUser.UserId) return null;
        return asset.WorkspaceId;
    }
}

/// <summary>
/// Request to edit a transcription segment's text.
/// </summary>
public class EditSegmentRequest
{
    public string Text { get; set; } = string.Empty;
}
