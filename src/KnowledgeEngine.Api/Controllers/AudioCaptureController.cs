using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Audio capture and upload API.
/// Accepts audio files from web, desktop, and mobile clients,
/// creates audio assets, and optionally starts transcription jobs.
/// </summary>
[ApiController]
[Route("api/audio")]
[Authorize]
public class AudioCaptureController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IFileStorageFactory _fileStorageFactory;
    private readonly IAudioCapabilityOrchestrator _orchestrator;
    private readonly IMediaPreparationService _mediaPrep;
    private readonly ILogger<AudioCaptureController> _logger;

    public AudioCaptureController(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        IFileStorageFactory fileStorageFactory,
        IAudioCapabilityOrchestrator orchestrator,
        IMediaPreparationService mediaPrep,
        ILogger<AudioCaptureController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorageFactory = fileStorageFactory;
        _orchestrator = orchestrator;
        _mediaPrep = mediaPrep;
        _logger = logger;
    }

    /// <summary>
    /// Uploads an audio file and optionally starts transcription.
    /// Supports common audio formats (wav, mp3, m4a, flac, ogg, opus) and video formats.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    public async Task<IActionResult> UploadAudio(
        [FromForm] IFormFile file,
        [FromForm] string? title,
        [FromForm] Guid? topicId,
        [FromForm] string? language,
        [FromForm] bool enableVad = true,
        [FromForm] bool enableSpeakerDiarization = false,
        [FromForm] bool enablePunctuation = true,
        [FromForm] string? hotwordsJson = null,
        [FromForm] string dataClassification = "INTERNAL",
        [FromForm] string? preferredProviderId = null,
        [FromForm] string? preferredModelId = null,
        [FromForm] string fallbackPolicy = "STOP",
        [FromForm] bool autoStart = true,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_FILE", "No audio file provided", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var workspaceId = await GetWorkspaceIdAsync(ct);
        if (workspaceId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "No active workspace found", GetTraceId()));
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"memorix-audio-upload-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
        await using (var stream = System.IO.File.Create(tempPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        try
        {
            var sha256 = await _mediaPrep.ComputeSha256Async(tempPath, ct);
            var existing = await FindAudioAssetBySha256Async(sha256, workspaceId.Value, ct);
            if (existing != null)
            {
                return Ok(ApiResponse<AudioUploadResponse>.Ok(new AudioUploadResponse
                {
                    AudioAssetId = existing.Id,
                    TranscriptionJobId = Guid.Empty,
                    Status = "deduplicated"
                }, GetTraceId()));
            }

            var classification = Enum.Parse<DataClassification>(dataClassification, ignoreCase: true);
            var allowsOffDevice = classification != DataClassification.STRICT_LOCAL;

            var audioAsset = new AudioAsset
            {
                Id = Guid.NewGuid(),
                SourceId = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                UserId = userId,
                OriginalFilePath = tempPath,
                SourceSha256 = sha256,
                FileSizeBytes = file.Length,
                MimeType = file.ContentType,
                DataClassification = dataClassification,
                AllowsOffDevice = allowsOffDevice,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.AudioAssets.Add(audioAsset);
            await _db.SaveChangesAsync(ct);

            Guid jobId = Guid.Empty;
            if (autoStart)
            {
                var hotwords = string.IsNullOrEmpty(hotwordsJson)
                    ? null
                    : hotwordsJson;

                var request = new CreateTranscriptionJobRequest
                {
                    AudioAssetId = audioAsset.Id,
                    Language = language,
                    EnableVad = enableVad,
                    EnableSpeakerDiarization = enableSpeakerDiarization,
                    EnablePunctuation = enablePunctuation,
                    Hotwords = hotwordsJson != null
                        ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(hotwordsJson)
                        : null,
                    DataClassification = classification,
                    PreferredProviderId = preferredProviderId,
                    PreferredModelId = preferredModelId,
                    FallbackPolicy = fallbackPolicy
                };

                jobId = await _orchestrator.StartTranscriptionAsync(audioAsset.Id, request, userId, workspaceId, ct);
            }

            return Ok(ApiResponse<AudioUploadResponse>.Ok(new AudioUploadResponse
            {
                AudioAssetId = audioAsset.Id,
                TranscriptionJobId = jobId,
                Status = autoStart ? "pending" : "uploaded"
            }, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload audio file");
            return StatusCode(500, ApiResponse<object>.FailObject("UPLOAD_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Gets audio asset metadata by ID.
    /// </summary>
    [HttpGet("assets/{assetId}")]
    public async Task<IActionResult> GetAsset(Guid assetId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var asset = await _db.AudioAssets.FindAsync(new object[] { assetId }, ct);
        if (asset == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Audio asset not found", GetTraceId()));
        }

        if (asset.UserId != _currentUser.UserId)
        {
            return Forbid();
        }

        var dto = new AudioAssetDto
        {
            Id = asset.Id,
            SourceId = asset.SourceId,
            WorkspaceId = asset.WorkspaceId,
            OriginalFilePath = asset.OriginalFilePath,
            NormalizedFilePath = asset.NormalizedFilePath,
            SourceSha256 = asset.SourceSha256,
            FileSizeBytes = asset.FileSizeBytes,
            MimeType = asset.MimeType,
            DurationMs = asset.DurationMs,
            SampleRate = asset.SampleRate,
            Channels = asset.Channels,
            DataClassification = asset.DataClassification,
            AllowsOffDevice = asset.AllowsOffDevice,
            CreatedAt = asset.CreatedAt
        };

        return Ok(ApiResponse<AudioAssetDto>.Ok(dto, GetTraceId()));
    }

    /// <summary>
    /// Lists audio assets for the current user.
    /// </summary>
    [HttpGet("assets")]
    public async Task<IActionResult> ListAssets([FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var assets = await _db.AudioAssets
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(a => new AudioAssetDto
            {
                Id = a.Id,
                SourceId = a.SourceId,
                WorkspaceId = a.WorkspaceId,
                OriginalFilePath = a.OriginalFilePath,
                NormalizedFilePath = a.NormalizedFilePath,
                SourceSha256 = a.SourceSha256,
                FileSizeBytes = a.FileSizeBytes,
                MimeType = a.MimeType,
                DurationMs = a.DurationMs,
                SampleRate = a.SampleRate,
                Channels = a.Channels,
                DataClassification = a.DataClassification,
                AllowsOffDevice = a.AllowsOffDevice,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<List<AudioAssetDto>>.Ok(assets, GetTraceId()));
    }

    private async Task<Guid?> GetWorkspaceIdAsync(CancellationToken ct)
    {
        var workspaceIdClaim = User.FindFirst("workspace_id")?.Value;
        if (Guid.TryParse(workspaceIdClaim, out var wsId))
        {
            return wsId;
        }

        var firstAsset = await _db.AudioAssets
            .Where(a => a.UserId == _currentUser.UserId)
            .Select(a => a.WorkspaceId)
            .FirstOrDefaultAsync(ct);
        return firstAsset;
    }

    private async Task<AudioAsset?> FindAudioAssetBySha256Async(string sha256, Guid workspaceId, CancellationToken ct)
    {
        return await _db.AudioAssets
            .FirstOrDefaultAsync(a => a.SourceSha256 == sha256 && a.WorkspaceId == workspaceId, ct);
    }
}
