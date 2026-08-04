using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Meeting lifecycle management service.
/// Handles meeting CRUD, speaker management, minutes version orchestration,
/// and action item confirmation flow.
/// </summary>
public class MeetingService : IMeetingService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<MeetingService> _logger;
    private readonly ILlmService? _llmService;
    private readonly IPrivacyTransformationService? _privacyService;
    private readonly IFileStorageProvider? _fileStorage;
    private readonly IMediaPreparationService? _mediaPrepService;

    /// <summary>Maximum characters per chunk before splitting the transcript for LLM processing.</summary>
    private const int LlmChunkCharLimit = 50_000;

    public MeetingService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<MeetingService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public MeetingService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<MeetingService> logger,
        ILlmService llmService,
        IPrivacyTransformationService privacyService,
        IFileStorageProvider fileStorage,
        IMediaPreparationService mediaPrepService)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _llmService = llmService;
        _privacyService = privacyService;
        _fileStorage = fileStorage;
        _mediaPrepService = mediaPrepService;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Meeting CRUD
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<MeetingDto> CreateAsync(CreateMeetingRequest request, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            WorkspaceId = request.WorkspaceId,
            TopicId = request.TopicId,
            Title = request.Title,
            Description = request.Description,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "zh-CN" : request.Language,
            Status = MeetingStatuses.Created,
            CreatedBy = userId,
            ProcessingPreset = string.IsNullOrWhiteSpace(request.ProcessingPreset)
                ? ProcessingPresets.LocalFirst
                : request.ProcessingPreset,
            DataClassification = string.IsNullOrWhiteSpace(request.DataClassification)
                ? MeetingDataClassification.Internal
                : request.DataClassification,
            AllowAudioUpload = request.AllowAudioUpload,
            AllowTextUpload = request.AllowTextUpload,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Meeting created: {MeetingId} by {UserId}, title=\"{Title}\"",
            meeting.Id, userId, meeting.Title);

        return ToMeetingDto(meeting);
    }

    /// <inheritdoc/>
    public async Task<MeetingDto?> GetAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return null;
        }

        var speakers = await _db.MeetingSpeakers
            .Where(s => s.MeetingId == meetingId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        var actionItems = await _db.ActionItems
            .Where(a => a.MeetingId == meetingId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var dto = ToMeetingDto(meeting);
        dto.Speakers = speakers.Select(ToSpeakerDto).ToList();
        dto.ActionItems = actionItems.Select(ToActionItemDto).ToList();
        return dto;
    }

    /// <inheritdoc/>
    public async Task<MeetingDto?> UpdateAsync(Guid meetingId, UpdateMeetingRequest request, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return null;
        }

        if (request.Title != null)
        {
            meeting.Title = request.Title;
        }
        if (request.Description != null)
        {
            meeting.Description = request.Description;
        }
        if (request.Language != null)
        {
            meeting.Language = request.Language;
        }
        if (request.TopicId.HasValue)
        {
            meeting.TopicId = request.TopicId;
        }
        if (request.ProcessingPreset != null)
        {
            meeting.ProcessingPreset = request.ProcessingPreset;
        }
        if (request.DataClassification != null)
        {
            meeting.DataClassification = request.DataClassification;
        }
        if (request.AllowAudioUpload.HasValue)
        {
            meeting.AllowAudioUpload = request.AllowAudioUpload.Value;
        }
        if (request.AllowTextUpload.HasValue)
        {
            meeting.AllowTextUpload = request.AllowTextUpload.Value;
        }

        meeting.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Meeting updated: {MeetingId}", meetingId);

        return ToMeetingDto(meeting);
    }

    /// <inheritdoc/>
    public async Task<bool> FinishAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return false;
        }

        var endedAt = DateTime.UtcNow;
        meeting.Status = MeetingStatuses.Completed;
        meeting.EndedAt = endedAt;

        if (meeting.StartedAt.HasValue)
        {
            meeting.DurationMs = (long)(endedAt - meeting.StartedAt.Value).TotalMilliseconds;
        }

        meeting.UpdatedAt = endedAt;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Meeting finished: {MeetingId}, duration={DurationMs}ms",
            meetingId, meeting.DurationMs);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return false;
        }

        // Soft delete: mark as DELETED rather than removing the row.
        meeting.Status = MeetingStatuses.Deleted;
        meeting.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Meeting soft-deleted: {MeetingId}", meetingId);

        return true;
    }

    /// <inheritdoc/>
    public async Task<List<MeetingDto>> ListAsync(Guid? workspaceId, int limit, int offset, CancellationToken ct)
    {
        var userId = RequireUserId();

        if (limit < 1) limit = 20;
        if (limit > 100) limit = 100;
        if (offset < 0) offset = 0;

        var query = _db.Meetings
            .Where(m => m.CreatedBy == userId && m.Status != MeetingStatuses.Deleted);

        if (workspaceId.HasValue)
        {
            query = query.Where(m => m.WorkspaceId == workspaceId.Value);
        }

        var meetings = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return meetings.Select(ToMeetingDto).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Speaker management
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<MeetingSpeakerDto>> GetSpeakersAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return new List<MeetingSpeakerDto>();
        }

        var speakers = await _db.MeetingSpeakers
            .Where(s => s.MeetingId == meetingId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        return speakers.Select(ToSpeakerDto).ToList();
    }

    /// <inheritdoc/>
    public async Task<MeetingSpeakerDto?> UpdateSpeakerAsync(Guid speakerId, UpdateSpeakerRequest request, CancellationToken ct)
    {
        var userId = RequireUserId();

        var speaker = await _db.MeetingSpeakers
            .FirstOrDefaultAsync(s => s.Id == speakerId, ct);
        if (speaker == null)
        {
            return null;
        }

        // Verify the current user owns the parent meeting.
        var meetingOwned = await _db.Meetings
            .AnyAsync(m => m.Id == speaker.MeetingId && m.CreatedBy == userId, ct);
        if (!meetingOwned)
        {
            return null;
        }

        if (request.DisplayName != null)
        {
            speaker.DisplayName = request.DisplayName;
        }
        if (request.IdentityStatus != null)
        {
            speaker.IdentityStatus = request.IdentityStatus;
        }

        // When the user supplies a display name, the identity is at least user-confirmed.
        if (request.DisplayName != null &&
            string.Equals(speaker.IdentityStatus, SpeakerIdentityStatuses.Unconfirmed,
                StringComparison.OrdinalIgnoreCase))
        {
            speaker.IdentityStatus = SpeakerIdentityStatuses.UserConfirmed;
        }

        speaker.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Speaker updated: {SpeakerId}, applyToAll={ApplyToAll}",
            speakerId, request.ApplyToAll);

        // Note: When ApplyToAll is true, downstream transcription-segment updates
        // are handled by the transcription service (segment-level speaker_key rewrite).

        return ToSpeakerDto(speaker);
    }

    /// <inheritdoc/>
    public async Task<bool> MergeSpeakersAsync(Guid meetingId, MergeSpeakersRequest request, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return false;
        }

        if (request.SpeakerIds.Count < 2)
        {
            return false;
        }

        var speakers = await _db.MeetingSpeakers
            .Where(s => s.MeetingId == meetingId && request.SpeakerIds.Contains(s.Id))
            .ToListAsync(ct);

        if (speakers.Count < 2)
        {
            return false;
        }

        // Generate a stable global speaker id for the merged cluster.
        var mergedGlobalId = $"spk_{Guid.NewGuid():N}".Substring(0, 36);

        var now = DateTime.UtcNow;
        foreach (var speaker in speakers)
        {
            speaker.GlobalSpeakerId = mergedGlobalId;
            if (!string.IsNullOrEmpty(request.TargetDisplayName))
            {
                speaker.DisplayName = request.TargetDisplayName;
                speaker.IdentityStatus = SpeakerIdentityStatuses.UserConfirmed;
            }
            speaker.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Merged {Count} speakers in meeting {MeetingId} to global id {GlobalId}",
            speakers.Count, meetingId, mergedGlobalId);

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Minutes management
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<MeetingMinutesDto>> GetMinutesAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return new List<MeetingMinutesDto>();
        }

        var minutes = await _db.MeetingMinutesVersions
            .Where(m => m.MeetingId == meetingId)
            .OrderByDescending(m => m.VersionNo)
            .ToListAsync(ct);

        return minutes.Select(ToMinutesDto).ToList();
    }

    /// <inheritdoc/>
    public async Task<MeetingMinutesDto?> SetOfficialMinutesAsync(Guid meetingId, Guid minutesId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return null;
        }

        var minutes = await _db.MeetingMinutesVersions
            .FirstOrDefaultAsync(m => m.Id == minutesId && m.MeetingId == meetingId, ct);
        if (minutes == null)
        {
            return null;
        }

        meeting.OfficialMinutesVersionId = minutesId;
        meeting.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Official minutes set: meeting={MeetingId}, minutes={MinutesId}",
            meetingId, minutesId);

        return ToMinutesDto(minutes);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Action items
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<ActionItemDto>> GetActionItemsAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
        {
            return new List<ActionItemDto>();
        }

        var items = await _db.ActionItems
            .Where(a => a.MeetingId == meetingId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return items.Select(ToActionItemDto).ToList();
    }

    /// <inheritdoc/>
    public async Task<ActionItemDto?> ConfirmActionItemAsync(
        Guid actionItemId,
        ConfirmActionItemRequest request,
        CancellationToken ct)
    {
        var userId = RequireUserId();

        var item = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.Id == actionItemId, ct);
        if (item == null)
        {
            return null;
        }

        // Verify the current user owns the parent meeting.
        var meetingOwned = await _db.Meetings
            .AnyAsync(m => m.Id == item.MeetingId && m.CreatedBy == userId, ct);
        if (!meetingOwned)
        {
            return null;
        }

        var hasModifications = false;

        if (request.TaskText != null)
        {
            item.TaskText = request.TaskText;
            hasModifications = true;
        }
        if (request.OwnerText != null)
        {
            item.OwnerText = request.OwnerText;
            hasModifications = true;
        }
        if (request.OwnerUserId.HasValue)
        {
            item.OwnerUserId = request.OwnerUserId;
            hasModifications = true;
        }
        if (request.DueDate.HasValue)
        {
            item.DueDate = request.DueDate;
            hasModifications = true;
        }
        if (!string.IsNullOrEmpty(request.Priority))
        {
            item.Priority = request.Priority;
            hasModifications = true;
        }

        // Determine the resulting confirmation status.
        if (request.CreateTask)
        {
            // In a full implementation this would insert a Memorix task entity.
            // For now we generate a task id placeholder and mark as converted.
            item.TaskId = Guid.NewGuid();
            item.ConfirmationStatus = ActionItemConfirmationStatuses.ConvertedToTask;
        }
        else if (hasModifications)
        {
            item.ConfirmationStatus = ActionItemConfirmationStatuses.Modified;
        }
        else
        {
            // No modifications and no task creation — plain confirmation.
            // If the item was previously ignored, re-confirming reactivates it.
            item.ConfirmationStatus = ActionItemConfirmationStatuses.Confirmed;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Action item confirmed: {ActionItemId}, status={Status}, createTask={CreateTask}",
            actionItemId, item.ConfirmationStatus, request.CreateTask);

        return ToActionItemDto(item);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Speaker split
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<bool> SplitSpeakerAsync(Guid meetingId, SplitSpeakerRequest request, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null) return false;

        var speaker = await _db.MeetingSpeakers
            .FirstOrDefaultAsync(s => s.Id == request.SpeakerId && s.MeetingId == meetingId, ct);
        if (speaker == null) return false;

        // Create a new speaker entry for the split-off portion.
        var newSpeaker = new MeetingSpeaker
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            SpeakerKey = $"spk_split_{Guid.NewGuid():N}".Substring(0, 36),
            GlobalSpeakerId = $"spk_{Guid.NewGuid():N}".Substring(0, 36),
            DisplayName = speaker.DisplayName,
            IdentityStatus = SpeakerIdentityStatuses.Unconfirmed,
            Confidence = speaker.Confidence,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.MeetingSpeakers.Add(newSpeaker);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Speaker split: source={SourceId}, new={NewId}, segments={SegmentCount}",
            request.SpeakerId, newSpeaker.Id, request.SegmentUuids.Count);

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Asset management
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<MeetingAssetDto> UploadAssetAsync(
        Guid meetingId, Stream stream, string fileName, string mimeType, long fileSize, Guid userId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
            throw new NotFoundException($"Meeting {meetingId} not found or not owned by current user.");

        // Save stream to a temporary local file for FFmpeg normalization
        var tempDir = Path.Combine(Path.GetTempPath(), "memorix-meeting-assets");
        Directory.CreateDirectory(tempDir);
        var tempFilePath = Path.Combine(tempDir, $"{meetingId}_{fileName}");

        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true))
        {
            await stream.CopyToAsync(fileStream, ct);
        }

        // Run FFmpeg normalization and media preparation (if service is available)
        string? normalizedFilePath = null;
        string sourceSha256 = string.Empty;
        long durationMs = 0;
        int sampleRate = 0;
        int channels = 0;

        if (_mediaPrepService != null)
        {
            try
            {
                var prepResult = await _mediaPrepService.PrepareAsync(tempFilePath, mimeType, ct);
                normalizedFilePath = prepResult.NormalizedFilePath;
                sourceSha256 = prepResult.SourceSha256;
                durationMs = prepResult.DurationMs;
                sampleRate = prepResult.SampleRate;
                channels = prepResult.Channels;

                _logger.LogInformation(
                    "FFmpeg normalization completed for meeting {MeetingId}: duration={DurationMs}ms, sr={SampleRate}, ch={Channels}",
                    meetingId, durationMs, sampleRate, channels);
            }
            catch (Exception ex)
            {
                // FFmpeg failure should not block asset upload — degradation strategy §14.4
                _logger.LogWarning(ex,
                    "FFmpeg normalization failed for meeting {MeetingId}, falling back to raw file. Error: {Error}",
                    meetingId, ex.Message);
            }
        }

        // Upload original file to storage
        var objectKey = $"meetings/{meetingId}/assets/{fileName}";
        var bucket = "memorix-meetings";

        if (_fileStorage != null)
        {
            await _fileStorage.EnsureBucketExistsAsync(bucket, ct);
            // Re-open the temp file for upload since the original stream was consumed
            await using var uploadStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 81920, useAsync: true);
            await _fileStorage.UploadFileAsync(bucket, objectKey, uploadStream, mimeType, fileSize, ct);
        }

        // Create AudioAsset record
        var asset = new AudioAsset
        {
            Id = Guid.NewGuid(),
            SourceId = meetingId,
            MeetingId = meetingId,
            WorkspaceId = meeting.WorkspaceId,
            UserId = userId,
            OriginalFilePath = objectKey,
            NormalizedFilePath = normalizedFilePath,
            SourceSha256 = sourceSha256,
            FileSizeBytes = fileSize,
            MimeType = mimeType,
            DurationMs = durationMs,
            SampleRate = sampleRate,
            Channels = channels,
            DataClassification = meeting.DataClassification,
            AllowsOffDevice = meeting.AllowAudioUpload,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.AudioAssets.Add(asset);
        await _db.SaveChangesAsync(ct);

        // Clean up temp file (normalized file is managed by audio cache service)
        TryDeleteFile(tempFilePath);

        _logger.LogInformation(
            "Meeting asset uploaded: meeting={MeetingId}, asset={AssetId}, file={FileName}, size={Size}, normalized={Normalized}",
            meetingId, asset.Id, fileName, fileSize, normalizedFilePath != null);

        return new MeetingAssetDto
        {
            Id = asset.Id,
            MeetingId = meetingId,
            AssetType = mimeType.StartsWith("video/") ? "VIDEO" : "AUDIO",
            StorageMode = "LOCAL",
            Uri = objectKey,
            MimeType = mimeType,
            FileSize = fileSize,
            DurationMs = durationMs,
            Checksum = string.IsNullOrEmpty(sourceSha256) ? null : sourceSha256,
            CreatedAt = asset.CreatedAt
        };
    }

    /// <summary>Best-effort file deletion that swallows exceptions (temp cleanup only).</summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <inheritdoc/>
    public async Task<List<MeetingAssetDto>> GetAssetsAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null) return new List<MeetingAssetDto>();

        var assets = await _db.AudioAssets
            .Where(a => a.MeetingId == meetingId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return assets.Select(a => new MeetingAssetDto
        {
            Id = a.Id,
            MeetingId = meetingId,
            AssetType = a.MimeType.StartsWith("video/") ? "VIDEO" : "AUDIO",
            StorageMode = "LOCAL",
            Uri = a.OriginalFilePath,
            MimeType = a.MimeType,
            FileSize = a.FileSizeBytes,
            DurationMs = a.DurationMs,
            Checksum = a.SourceSha256,
            CreatedAt = a.CreatedAt
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Transcription management
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<TranscriptVersionDto> TriggerTranscriptionAsync(
        Guid meetingId, CreateTranscriptionRequest request, Guid userId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null)
            throw new NotFoundException($"Meeting {meetingId} not found or not owned by current user.");

        // Find the source asset
        AudioAsset? asset = null;
        if (request.SourceAssetId.HasValue)
        {
            asset = await _db.AudioAssets
                .FirstOrDefaultAsync(a => a.Id == request.SourceAssetId.Value && a.MeetingId == meetingId, ct);
        }
        else
        {
            // Use the most recent audio asset
            asset = await _db.AudioAssets
                .Where(a => a.MeetingId == meetingId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        if (asset == null)
            throw new NotFoundException("No audio asset found for this meeting. Upload an asset first.");

        // Create a transcription job
        var job = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            AudioAssetId = asset.Id,
            WorkspaceId = meeting.WorkspaceId,
            UserId = userId,
            ExecutionMode = "LOCAL_DEVICE",
            CredentialMode = "NO_CREDENTIAL",
            ProviderId = string.Empty,
            ModelId = string.Empty,
            FallbackPolicy = "STOP",
            Language = request.Language ?? meeting.Language,
            EnableVad = request.EnableVad,
            EnableSpeakerDiarization = request.EnableSpeakerDiarization,
            EnablePunctuation = request.EnablePunctuation,
            Hotwords = request.Hotwords != null
                ? JsonSerializer.Serialize(request.Hotwords)
                : null,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.TranscriptionJobs.Add(job);

        // Set meeting to PROCESSING status
        meeting.Status = MeetingStatuses.Processing;
        meeting.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Transcription triggered: meeting={MeetingId}, job={JobId}, asset={AssetId}",
            meetingId, job.Id, asset.Id);

        return new TranscriptVersionDto
        {
            Id = job.Id,
            MeetingId = meetingId,
            VersionNo = 1,
            VersionType = "BATCH",
            Status = "pending",
            Provider = string.Empty,
            Model = string.Empty,
            Language = job.Language,
            SourceAssetId = asset.Id,
            ParentVersionId = null,
            CreatedBy = userId.ToString(),
            CreatedAt = job.CreatedAt,
            Segments = null
        };
    }

    /// <inheritdoc/>
    public async Task<List<TranscriptVersionDto>> GetTranscriptsAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null) return new List<TranscriptVersionDto>();

        var jobs = await _db.TranscriptionJobs
            .Where(j => j.AudioAssetId != default &&
                        _db.AudioAssets.Any(a => a.Id == j.AudioAssetId && a.MeetingId == meetingId))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

        return jobs.Select((j, idx) => new TranscriptVersionDto
        {
            Id = j.Id,
            MeetingId = meetingId,
            VersionNo = idx + 1,
            VersionType = "BATCH",
            Status = j.Status,
            Provider = j.ProviderId,
            Model = j.ModelId,
            Language = j.Language,
            SourceAssetId = j.AudioAssetId,
            ParentVersionId = null,
            CreatedBy = j.UserId.ToString(),
            CreatedAt = j.CreatedAt,
            Segments = null
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<TranscriptVersionDto?> GetTranscriptAsync(Guid transcriptId, CancellationToken ct)
    {
        var job = await _db.TranscriptionJobs
            .FirstOrDefaultAsync(j => j.Id == transcriptId, ct);
        if (job == null) return null;

        var segments = await _db.TranscriptionSegments
            .Where(s => s.TranscriptionJobId == transcriptId)
            .OrderBy(s => s.SegmentIndex)
            .ToListAsync(ct);

        // Resolve speaker display names
        var meetingId = await _db.AudioAssets
            .Where(a => a.Id == job.AudioAssetId)
            .Select(a => a.MeetingId)
            .FirstOrDefaultAsync(ct) ?? Guid.Empty;

        var speakers = meetingId != Guid.Empty
            ? await _db.MeetingSpeakers
                .Where(s => s.MeetingId == meetingId)
                .ToDictionaryAsync(s => s.SpeakerKey ?? string.Empty, s => s.DisplayName, ct)
            : new Dictionary<string, string?>();

        var segmentDtos = segments.Select(s => new TranscriptSegmentDto
        {
            Id = s.Id,
            SegmentUuid = s.SegmentUuid,
            StartMs = s.SourceStartMs,
            EndMs = s.SourceEndMs,
            SpeakerKey = s.SpeakerKey,
            SpeakerDisplayName = s.SpeakerKey != null && speakers.TryGetValue(s.SpeakerKey, out var name) ? name : null,
            Text = s.Text,
            Confidence = s.Confidence,
            Version = s.Version,
            SegmentIndex = s.SegmentIndex,
            ManualEdited = s.Version == SegmentVersions.UserEdited
        }).ToList();

        return new TranscriptVersionDto
        {
            Id = job.Id,
            MeetingId = meetingId,
            VersionNo = 1,
            VersionType = "BATCH",
            Status = job.Status,
            Provider = job.ProviderId,
            Model = job.ModelId,
            Language = job.Language,
            SourceAssetId = job.AudioAssetId,
            ParentVersionId = null,
            CreatedBy = job.UserId.ToString(),
            CreatedAt = job.CreatedAt,
            Segments = segmentDtos
        };
    }

    /// <inheritdoc/>
    public async Task<TranscriptSegmentDto?> UpdateSegmentAsync(
        Guid segmentId, UpdateSegmentRequest request, CancellationToken ct)
    {
        var userId = RequireUserId();

        var segment = await _db.TranscriptionSegments
            .FirstOrDefaultAsync(s => s.Id == segmentId, ct);
        if (segment == null) return null;

        if (request.Text != null)
        {
            // Save a USER_EDITED version of the segment text
            var version = new TranscriptionVersion
            {
                Id = Guid.NewGuid(),
                TranscriptionJobId = segment.TranscriptionJobId,
                SegmentUuid = segment.SegmentUuid,
                Version = SegmentVersions.UserEdited,
                ParentVersionId = null,
                Text = request.Text,
                ProviderId = segment.ProviderId,
                ModelId = segment.ModelId,
                CreatedBy = userId.ToString(),
                CreatedAt = DateTime.UtcNow
            };
            _db.TranscriptionVersions.Add(version);

            segment.Text = request.Text;
            segment.Version = SegmentVersions.UserEdited;
            segment.UpdatedAt = DateTime.UtcNow;
        }

        if (request.SpeakerKey != null)
        {
            segment.SpeakerKey = request.SpeakerKey;
            segment.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Segment updated: {SegmentId}", segmentId);

        return new TranscriptSegmentDto
        {
            Id = segment.Id,
            SegmentUuid = segment.SegmentUuid,
            StartMs = segment.SourceStartMs,
            EndMs = segment.SourceEndMs,
            SpeakerKey = segment.SpeakerKey,
            Text = segment.Text,
            Confidence = segment.Confidence,
            Version = segment.Version,
            SegmentIndex = segment.SegmentIndex,
            ManualEdited = true
        };
    }

    /// <inheritdoc/>
    public async Task<bool> SetOfficialTranscriptAsync(Guid meetingId, Guid transcriptId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null) return false;

        meeting.OfficialTranscriptVersionId = transcriptId;
        meeting.UpdatedAt = DateTime.UtcNow;

        // Mark existing minutes as STALE since the transcript changed
        var existingMinutes = await _db.MeetingMinutesVersions
            .Where(m => m.MeetingId == meetingId && m.Status == MinutesVersionStatuses.Ready)
            .ToListAsync(ct);
        foreach (var m in existingMinutes)
        {
            m.Status = MinutesVersionStatuses.Stale;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Official transcript set: meeting={MeetingId}, transcript={TranscriptId}",
            meetingId, transcriptId);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> ReprocessAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null) return false;

        meeting.Status = MeetingStatuses.Processing;
        meeting.UpdatedAt = DateTime.UtcNow;

        // Mark existing minutes as STALE
        var minutes = await _db.MeetingMinutesVersions
            .Where(m => m.MeetingId == meetingId && m.Status != MinutesVersionStatuses.Superseded)
            .ToListAsync(ct);
        foreach (var m in minutes)
        {
            m.Status = MinutesVersionStatuses.Stale;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Meeting reprocessing triggered: {MeetingId}", meetingId);

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Minutes generation
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<MeetingMinutesDto?> GenerateMinutesAsync(
        Guid meetingId, GenerateMinutesRequest request, Guid userId, CancellationToken ct)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId, ct);
        if (meeting == null) return null;

        // Gather transcript text
        var transcriptId = request.TranscriptVersionId ?? meeting.OfficialTranscriptVersionId;
        List<TranscriptionSegment> segments;

        if (transcriptId.HasValue)
        {
            segments = await _db.TranscriptionSegments
                .Where(s => s.TranscriptionJobId == transcriptId.Value)
                .OrderBy(s => s.SegmentIndex)
                .ToListAsync(ct);
        }
        else
        {
            // Find segments from any transcription job for this meeting
            var jobIds = await _db.AudioAssets
                .Where(a => a.MeetingId == meetingId)
                .SelectMany(a => _db.TranscriptionJobs.Where(j => j.AudioAssetId == a.Id))
                .Select(j => j.Id)
                .ToListAsync(ct);

            segments = await _db.TranscriptionSegments
                .Where(s => jobIds.Contains(s.TranscriptionJobId))
                .OrderBy(s => s.SegmentIndex)
                .ToListAsync(ct);
        }

        if (segments.Count == 0)
        {
            _logger.LogWarning("No transcript segments found for meeting {MeetingId}", meetingId);
            return null;
        }

        // Build transcript text with timestamps and speaker info
        var speakers = await _db.MeetingSpeakers
            .Where(s => s.MeetingId == meetingId)
            .ToDictionaryAsync(s => s.SpeakerKey ?? string.Empty, s => s.DisplayName ?? s.SpeakerKey, ct);

        var transcriptText = new StringBuilder();
        foreach (var seg in segments)
        {
            var ts = TimeSpan.FromMilliseconds(seg.SourceStartMs);
            var speaker = seg.SpeakerKey != null && speakers.TryGetValue(seg.SpeakerKey, out var name)
                ? name
                : seg.SpeakerKey ?? "未知说话人";
            transcriptText.AppendLine($"[{ts:hh\\:mm\\:ss}] {speaker}: {seg.Text}");
        }

        var fullText = transcriptText.ToString();

        // Apply privacy masking if needed
        var maskingMode = request.MaskingMode ?? PrivacyMaskingModes.Off;
        if (_privacyService != null && maskingMode != PrivacyMaskingModes.Off)
        {
            var masked = await _privacyService.MaskAsync(meetingId, fullText, maskingMode, ct);
            if (masked.Blocked)
            {
                _logger.LogWarning("Minutes generation blocked by LOCAL_ONLY masking mode for meeting {MeetingId}", meetingId);
                return null;
            }
            fullText = masked.MaskedText;
        }

        // Call LLM to generate minutes
        var systemPrompt = BuildMinutesSystemPrompt(meeting.Language);
        var userPrompt = BuildMinutesUserPrompt(meeting.Title, fullText);

        string llmContent = "{}";
        string llmModel = "unknown";
        string llmProvider = "local";

        // Check if transcript needs chunking
        var chunks = SplitTranscriptIntoChunks(fullText, LlmChunkCharLimit);

        if (_llmService != null)
        {
            if (chunks.Count > 1)
            {
                _logger.LogInformation(
                    "Transcript too long ({Length} chars), splitting into {ChunkCount} chunks for LLM processing",
                    fullText.Length, chunks.Count);

                var chunkResponses = new List<string>();
                var firstResult = true;
                foreach (var chunk in chunks)
                {
                    var chunkPrompt = BuildMinutesUserPrompt(meeting.Title, chunk);
                    var chunkResult = await _llmService.CompleteAsync(systemPrompt, chunkPrompt, null, ct);
                    chunkResponses.Add(chunkResult.Content);
                    if (firstResult)
                    {
                        llmModel = chunkResult.Model;
                        firstResult = false;
                    }
                }

                llmContent = MergeChunkedMinutesResponses(chunkResponses, meeting.Title);
            }
            else
            {
                var llmResult = await _llmService.CompleteAsync(systemPrompt, userPrompt, null, ct);
                llmContent = llmResult.Content;
                llmModel = llmResult.Model;
            }
        }
        else
        {
            // Fallback: create a minimal structured output
            llmContent = JsonSerializer.Serialize(new
            {
                summary = $"会议「{meeting.Title}」已完成，共 {segments.Count} 个转写片段。",
                topics = Array.Empty<string>(),
                decisions = Array.Empty<string>(),
                action_items = Array.Empty<object>(),
                risks = Array.Empty<string>(),
                open_items = Array.Empty<string>()
            });
        }

        // Restore original values from masked text
        if (_privacyService != null && maskingMode != PrivacyMaskingModes.Off)
        {
            llmContent = await _privacyService.RestoreAsync(meetingId, llmContent, ct);
        }

        // Extract summary from LLM response
        var summary = ExtractSummary(llmContent, meeting.Title, segments.Count);

        // Determine next version number
        var maxVersionNo = await _db.MeetingMinutesVersions
            .Where(m => m.MeetingId == meetingId)
            .Select(m => (int?)m.VersionNo)
            .MaxAsync(ct) ?? 0;

        var minutes = new MeetingMinutesVersion
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            VersionNo = maxVersionNo + 1,
            TranscriptVersionId = transcriptId,
            TemplateId = request.TemplateId,
            Summary = summary,
            ContentJson = llmContent,
            Provider = llmProvider,
            Model = llmModel,
            Status = MinutesVersionStatuses.Draft,
            CreatedBy = userId.ToString(),
            CreatedAt = DateTime.UtcNow
        };

        _db.MeetingMinutesVersions.Add(minutes);

        // Mark previous minutes as SUPERSEDED
        var previousMinutes = await _db.MeetingMinutesVersions
            .Where(m => m.MeetingId == meetingId && m.Status == MinutesVersionStatuses.Ready)
            .ToListAsync(ct);
        foreach (var prev in previousMinutes)
        {
            prev.Status = MinutesVersionStatuses.Superseded;
        }

        await _db.SaveChangesAsync(ct);

        // Extract and create action items from LLM response
        await ExtractAndCreateActionItemsAsync(meetingId, minutes.Id, llmContent, ct);

        _logger.LogInformation(
            "Minutes generated: meeting={MeetingId}, version={VersionNo}, model={Model}",
            meetingId, minutes.VersionNo, llmModel);

        return ToMinutesDto(minutes);
    }

    /// <inheritdoc/>
    public async Task<MeetingMinutesDto?> UpdateMinutesAsync(
        Guid minutesId, UpdateMinutesRequest request, CancellationToken ct)
    {
        var minutes = await _db.MeetingMinutesVersions
            .FirstOrDefaultAsync(m => m.Id == minutesId, ct);
        if (minutes == null) return null;

        if (request.Summary != null)
        {
            minutes.Summary = request.Summary;
        }
        if (request.ContentJson != null)
        {
            minutes.ContentJson = request.ContentJson;
        }

        minutes.Status = MinutesVersionStatuses.Ready;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Minutes updated: {MinutesId}", minutesId);

        return ToMinutesDto(minutes);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Action item batch operations
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<ActionItemDto>> BatchConfirmActionItemsAsync(
        BatchConfirmActionItemsRequest request, CancellationToken ct)
    {
        var results = new List<ActionItemDto>();

        foreach (var itemId in request.ActionItemIds)
        {
            var confirmRequest = new ConfirmActionItemRequest
            {
                CreateTask = request.CreateTasks
            };
            var dto = await ConfirmActionItemAsync(itemId, confirmRequest, ct);
            if (dto != null) results.Add(dto);
        }

        _logger.LogInformation(
            "Batch confirmed {Count} action items, createTasks={CreateTasks}",
            results.Count, request.CreateTasks);

        return results;
    }

    /// <inheritdoc/>
    public async Task<ActionItemDto?> CreateTaskFromActionItemAsync(Guid actionItemId, CancellationToken ct)
    {
        var userId = RequireUserId();

        var item = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.Id == actionItemId, ct);
        if (item == null) return null;

        // Verify ownership
        var meetingOwned = await _db.Meetings
            .AnyAsync(m => m.Id == item.MeetingId && m.CreatedBy == userId, ct);
        if (!meetingOwned) return null;

        item.TaskId = Guid.NewGuid();
        item.ConfirmationStatus = ActionItemConfirmationStatuses.ConvertedToTask;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Task created from action item: {ActionItemId}", actionItemId);

        return ToActionItemDto(item);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Minutes generation helpers
    // ══════════════════════════════════════════════════════════════════════

    private static string BuildMinutesSystemPrompt(string language)
    {
        var langName = language.StartsWith("zh") ? "中文" : "English";
        return $$"""
            你是一个专业的会议纪要生成助手。请根据提供的会议转写文本，生成结构化的会议纪要。
            输出语言: {{langName}}

            输出格式为 JSON，包含以下字段:
            {
              "summary": "一句话摘要",
              "topics": ["讨论议题1", "讨论议题2"],
              "decisions": ["已达成决议1"],
              "open_items": ["未决事项1"],
              "action_items": [
                {
                  "task": "任务描述",
                  "owner": "负责人（如不确定则null）",
                  "due_date": "截止日期（如不确定则null）",
                  "priority": "HIGH/MEDIUM/LOW",
                  "source_segments": ["seg_001"],
                  "confidence": 0.85
                }
              ],
              "risks": ["风险和分歧1"],
              "key_quotes": [
                {
                  "text": "关键原话",
                  "start_ms": 0,
                  "speaker": "说话人"
                }
              ]
            }

            重要规则:
            - 不得从常识推断会议中未明确出现的负责人和日期
            - 不确定字段返回 null
            - 决议、行动项和风险必须包含来源片段
            - 低置信度结果标记为待确认
            """;
    }

    private static string BuildMinutesUserPrompt(string meetingTitle, string transcriptText)
    {
        return $"""
            会议标题: {meetingTitle}
            
            转写文本:
            {transcriptText}
            
            请生成结构化会议纪要 (JSON):
            """;
    }

    /// <summary>
    /// Splits a long transcript into chunks that fit within the LLM context window.
    /// Splitting is done at paragraph/sentence boundaries to preserve context.
    /// </summary>
    private static List<string> SplitTranscriptIntoChunks(string transcriptText, int maxCharsPerChunk)
    {
        if (transcriptText.Length <= maxCharsPerChunk)
            return new List<string> { transcriptText };

        var chunks = new List<string>();
        var lines = transcriptText.Split('\n');
        var currentChunk = new StringBuilder();
        var currentLength = 0;

        foreach (var line in lines)
        {
            var lineLength = line.Length + 1; // +1 for the newline
            if (currentLength + lineLength > maxCharsPerChunk && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.AppendLine(line);
            currentLength += lineLength;
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    /// <summary>
    /// Merges multiple LLM chunk responses into a single structured JSON output.
    /// Each chunk response is expected to be a JSON object with the same structure
    /// as the minutes output (summary, topics, decisions, action_items, etc.).
    /// </summary>
    private static string MergeChunkedMinutesResponses(List<string> chunkResponses, string meetingTitle)
    {
        if (chunkResponses.Count == 1)
            return chunkResponses[0];

        var mergedSummary = new List<string>();
        var mergedTopics = new List<string>();
        var mergedDecisions = new List<string>();
        var mergedActionItems = new List<JsonElement>();
        var mergedRisks = new List<string>();
        var mergedOpenItems = new List<string>();

        foreach (var response in chunkResponses)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("summary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.String)
                    mergedSummary.Add(summaryEl.GetString() ?? string.Empty);

                if (root.TryGetProperty("topics", out var topicsEl) && topicsEl.ValueKind == JsonValueKind.Array)
                    foreach (var t in topicsEl.EnumerateArray())
                        if (t.ValueKind == JsonValueKind.String)
                            mergedTopics.Add(t.GetString() ?? string.Empty);

                if (root.TryGetProperty("decisions", out var decEl) && decEl.ValueKind == JsonValueKind.Array)
                    foreach (var d in decEl.EnumerateArray())
                        if (d.ValueKind == JsonValueKind.String)
                            mergedDecisions.Add(d.GetString() ?? string.Empty);

                if (root.TryGetProperty("action_items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    foreach (var item in itemsEl.EnumerateArray())
                        mergedActionItems.Add(item.Clone());

                if (root.TryGetProperty("risks", out var risksEl) && risksEl.ValueKind == JsonValueKind.Array)
                    foreach (var r in risksEl.EnumerateArray())
                        if (r.ValueKind == JsonValueKind.String)
                            mergedRisks.Add(r.GetString() ?? string.Empty);

                if (root.TryGetProperty("open_items", out var openEl) && openEl.ValueKind == JsonValueKind.Array)
                    foreach (var o in openEl.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String)
                            mergedOpenItems.Add(o.GetString() ?? string.Empty);
            }
            catch (JsonException)
            {
                // Skip malformed chunks
            }
        }

        return JsonSerializer.Serialize(new
        {
            summary = mergedSummary.Count > 0 
                ? string.Join(" ", mergedSummary) 
                : $"会议「{meetingTitle}」已完成分块处理，共 {chunkResponses.Count} 个片段。",
            topics = mergedTopics.Distinct().ToList(),
            decisions = mergedDecisions.Distinct().ToList(),
            action_items = mergedActionItems,
            risks = mergedRisks.Distinct().ToList(),
            open_items = mergedOpenItems.Distinct().ToList(),
            chunk_count = chunkResponses.Count
        });
    }

    private static string ExtractSummary(string llmContent, string meetingTitle, int segmentCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(llmContent);
            if (doc.RootElement.TryGetProperty("summary", out var summaryEl))
            {
                return summaryEl.GetString() ?? $"会议「{meetingTitle}」已完成，共 {segmentCount} 个转写片段。";
            }
        }
        catch (JsonException)
        {
            // If LLM returns non-JSON, use first 200 chars as summary
            if (llmContent.Length > 200)
                return llmContent[..200] + "...";
            return llmContent;
        }
        return $"会议「{meetingTitle}」已完成，共 {segmentCount} 个转写片段。";
    }

    private async Task ExtractAndCreateActionItemsAsync(
        Guid meetingId, Guid minutesId, string llmContent, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(llmContent);
            if (!doc.RootElement.TryGetProperty("action_items", out var itemsEl))
                return;

            foreach (var item in itemsEl.EnumerateArray())
            {
                var taskText = item.TryGetProperty("task", out var taskEl) ? taskEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(taskText)) continue;

                var owner = item.TryGetProperty("owner", out var ownerEl) ? ownerEl.GetString() : null;
                var dueDateStr = item.TryGetProperty("due_date", out var dueEl) ? dueEl.GetString() : null;
                var priority = item.TryGetProperty("priority", out var priEl) ? priEl.GetString() : "MEDIUM";
                var confidence = item.TryGetProperty("confidence", out var confEl) ? confEl.GetDecimal() : 0m;
                var sourceSegments = item.TryGetProperty("source_segments", out var segEl)
                    ? segEl.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList()
                    : new List<string>();

                var actionItem = new ActionItem
                {
                    Id = Guid.NewGuid(),
                    MeetingId = meetingId,
                    MinutesVersionId = minutesId,
                    TaskText = taskText,
                    OwnerText = owner,
                    DueDate = DateTime.TryParse(dueDateStr, out var dd) ? dd : null,
                    Priority = priority ?? "MEDIUM",
                    Confidence = confidence,
                    ConfirmationStatus = ActionItemConfirmationStatuses.PendingConfirmation,
                    SourceSegmentIds = sourceSegments.Count > 0
                        ? JsonSerializer.Serialize(sourceSegments)
                        : null,
                    CreatedAt = DateTime.UtcNow
                };

                _db.ActionItems.Add(actionItem);
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to extract action items from LLM response");
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the current authenticated user id, or throws if unauthenticated.
    /// </summary>
    private Guid RequireUserId()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            throw new UnauthorizedException("User is not authenticated");
        }
        return _currentUser.UserId.Value;
    }

    /// <summary>
    /// Loads a meeting by id, scoped to the current user. Returns null when the
    /// meeting does not exist or the user has no ownership (no exception thrown).
    /// </summary>
    private async Task<Meeting?> GetOwnedMeetingAsync(Guid meetingId, CancellationToken ct)
    {
        var userId = RequireUserId();
        return await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId && m.CreatedBy == userId, ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DTO mapping (manual, AutoMapper-style)
    // ══════════════════════════════════════════════════════════════════════

    private static MeetingDto ToMeetingDto(Meeting m)
    {
        return new MeetingDto
        {
            Id = m.Id,
            WorkspaceId = m.WorkspaceId,
            TopicId = m.TopicId,
            Title = m.Title,
            Description = m.Description,
            Language = m.Language,
            Status = m.Status,
            StartedAt = m.StartedAt,
            EndedAt = m.EndedAt,
            DurationMs = m.DurationMs,
            CreatedBy = m.CreatedBy,
            ProcessingPreset = m.ProcessingPreset,
            DataClassification = m.DataClassification,
            AllowAudioUpload = m.AllowAudioUpload,
            AllowTextUpload = m.AllowTextUpload,
            OfficialTranscriptVersionId = m.OfficialTranscriptVersionId,
            OfficialMinutesVersionId = m.OfficialMinutesVersionId,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            Speakers = null,
            ActionItems = null
        };
    }

    private static MeetingSpeakerDto ToSpeakerDto(MeetingSpeaker s)
    {
        return new MeetingSpeakerDto
        {
            Id = s.Id,
            MeetingId = s.MeetingId,
            SpeakerKey = s.SpeakerKey,
            GlobalSpeakerId = s.GlobalSpeakerId,
            DisplayName = s.DisplayName,
            ParticipantId = s.ParticipantId,
            IdentityStatus = s.IdentityStatus,
            Confidence = s.Confidence
        };
    }

    private static MeetingMinutesDto ToMinutesDto(MeetingMinutesVersion m)
    {
        return new MeetingMinutesDto
        {
            Id = m.Id,
            MeetingId = m.MeetingId,
            VersionNo = m.VersionNo,
            TranscriptVersionId = m.TranscriptVersionId,
            TemplateId = m.TemplateId,
            Summary = m.Summary,
            ContentJson = m.ContentJson,
            Provider = m.Provider,
            Model = m.Model,
            Status = m.Status,
            CreatedBy = m.CreatedBy,
            CreatedAt = m.CreatedAt
        };
    }

    private static ActionItemDto ToActionItemDto(ActionItem a)
    {
        List<string>? sourceSegmentIds = null;
        if (!string.IsNullOrWhiteSpace(a.SourceSegmentIds))
        {
            try
            {
                sourceSegmentIds = JsonSerializer.Deserialize<List<string>>(a.SourceSegmentIds);
            }
            catch (JsonException)
            {
                // If the stored JSON is malformed, return null rather than crashing.
                sourceSegmentIds = null;
            }
        }

        return new ActionItemDto
        {
            Id = a.Id,
            MeetingId = a.MeetingId,
            MinutesVersionId = a.MinutesVersionId,
            TaskText = a.TaskText,
            OwnerText = a.OwnerText,
            OwnerUserId = a.OwnerUserId,
            DueDate = a.DueDate,
            Priority = a.Priority,
            Confidence = a.Confidence,
            ConfirmationStatus = a.ConfirmationStatus,
            TaskId = a.TaskId,
            SourceSegmentIds = sourceSegmentIds,
            CreatedAt = a.CreatedAt
        };
    }
}
