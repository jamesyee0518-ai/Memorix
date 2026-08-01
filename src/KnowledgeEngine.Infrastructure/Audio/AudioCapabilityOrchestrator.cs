using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Orchestrates the end-to-end audio transcription pipeline:
/// load audio asset -> create job -> media preparation (normalize + VAD + segment)
/// -> policy routing -> provider execution -> persist segments -> update job status.
/// Handles errors with fallback policy application.
/// </summary>
public class AudioCapabilityOrchestrator : IAudioCapabilityOrchestrator
{
    private readonly IAppDbContext _db;
    private readonly IProviderRegistry _registry;
    private readonly IAudioPolicyRouter _router;
    private readonly IMediaPreparationService _mediaPrep;
    private readonly IAudioCacheService _audioCache;
    private readonly ICredentialManager _credentialManager;
    private readonly IPostAsrCorrectionService _correctionService;
    private readonly ILogger<AudioCapabilityOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCapabilityOrchestrator"/> class.
    /// </summary>
    /// <param name="db">The application database context.</param>
    /// <param name="registry">The provider registry for direct provider lookup.</param>
    /// <param name="router">The audio policy router for capability-to-provider resolution.</param>
    /// <param name="mediaPrep">The media preparation service for FFmpeg normalization and VAD.</param>
    /// <param name="audioCache">The audio cache service for normalized audio deduplication.</param>
    /// <param name="credentialManager">The credential manager for BYOK credential access.</param>
    /// <param name="correctionService">The post-ASR correction service for dictionary-based text correction.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public AudioCapabilityOrchestrator(
        IAppDbContext db,
        IProviderRegistry registry,
        IAudioPolicyRouter router,
        IMediaPreparationService mediaPrep,
        IAudioCacheService audioCache,
        ICredentialManager credentialManager,
        IPostAsrCorrectionService correctionService,
        ILogger<AudioCapabilityOrchestrator> logger)
    {
        _db = db;
        _registry = registry;
        _router = router;
        _mediaPrep = mediaPrep;
        _audioCache = audioCache;
        _credentialManager = credentialManager;
        _correctionService = correctionService;
        _logger = logger;
    }

    // ── IAudioCapabilityOrchestrator ──

    /// <inheritdoc/>
    public async Task<Guid> StartTranscriptionAsync(
        Guid audioAssetId,
        CreateTranscriptionJobRequest request,
        Guid userId,
        Guid? workspaceId,
        CancellationToken ct)
    {
        // ── Step (a): Load AudioAsset from DB ──

        var audioAsset = await _db.AudioAssets
            .FirstOrDefaultAsync(a => a.Id == audioAssetId, ct);

        if (audioAsset == null)
        {
            throw new InvalidOperationException(
                $"AudioAsset {audioAssetId} not found.");
        }

        // ── Step (b): Create TranscriptionJob entity with four-layer decoupling fields ──

        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Serialize hotwords to JSON array string.
        var hotwordsJson = request.Hotwords is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(request.Hotwords)
            : null;

        var job = new TranscriptionJob
        {
            Id = jobId,
            AudioAssetId = audioAssetId,
            WorkspaceId = workspaceId,
            UserId = userId,

            // Four-layer decoupling fields (initial defaults; updated after routing resolves provider).
            ExecutionMode = ExecutionMode.LOCAL_DEVICE.ToString(),
            CredentialMode = CredentialMode.NO_CREDENTIAL.ToString(),
            ProviderId = request.PreferredProviderId ?? string.Empty,
            ModelId = request.PreferredModelId ?? string.Empty,
            FallbackPolicy = request.FallbackPolicy,

            // Request options
            Language = request.Language,
            EnableVad = request.EnableVad,
            EnableSpeakerDiarization = request.EnableSpeakerDiarization,
            EnablePunctuation = request.EnablePunctuation,
            Hotwords = hotwordsJson,

            // Initial state
            Status = TranscriptionJobStatuses.Pending,
            CreatedAt = now,
        };

        // ── Step (c): Save job to DB ──

        _db.TranscriptionJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created transcription job {JobId} for audio asset {AudioAssetId} (user: {UserId})",
            jobId, audioAssetId, userId);

        // ── Steps (d)-(h): Execute the transcription pipeline ──

        try
        {
            await ExecuteTranscriptionPipelineAsync(job, audioAsset, request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await UpdateJobStatusAsync(job, TranscriptionJobStatuses.Cancelled, "Cancelled by caller", ct);
            throw;
        }
        catch (Exception ex)
        {
            // ── Step (i): Handle errors - update job to failed and apply fallback policy ──
            await HandleTranscriptionFailureAsync(job, audioAsset, request, ex, ct);
        }

        return jobId;
    }

    /// <inheritdoc/>
    public async Task<TranscriptionStatusResponse> GetJobStatusAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.TranscriptionJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null)
        {
            throw new InvalidOperationException($"TranscriptionJob {jobId} not found.");
        }

        var segments = await _db.TranscriptionSegments
            .Where(s => s.TranscriptionJobId == jobId)
            .OrderBy(s => s.SegmentIndex)
            .ToListAsync(ct);

        // When multiple versions of the same segment exist (e.g. RAW_MODEL and
        // POST_PROCESSED), prefer the highest-priority version so the response
        // reflects the latest corrected text.
        var latestSegments = segments
            .GroupBy(s => s.SegmentUuid)
            .Select(g => g.OrderByDescending(s => SegmentVersionPriority(s.Version)).First())
            .OrderBy(s => s.SegmentIndex)
            .ToList();

        return new TranscriptionStatusResponse
        {
            JobId = job.Id,
            Status = job.Status,
            ErrorMessage = job.ErrorMessage,
            SegmentCount = job.SegmentCount ?? latestSegments.Count,
            ProviderId = string.IsNullOrEmpty(job.ProviderId) ? null : job.ProviderId,
            ModelId = string.IsNullOrEmpty(job.ModelId) ? null : job.ModelId,
            EstimatedCost = job.EstimatedCost,
            ActualCost = job.ActualCost,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            Segments = latestSegments.Select(ToSegmentDto).ToList(),
        };
    }

    /// <inheritdoc/>
    public async Task CancelJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.TranscriptionJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null)
        {
            _logger.LogWarning("Cannot cancel: transcription job {JobId} not found", jobId);
            return;
        }

        // Only allow cancellation of pending or running jobs.
        if (job.Status is not (TranscriptionJobStatuses.Pending or TranscriptionJobStatuses.Running))
        {
            _logger.LogWarning(
                "Cannot cancel job {JobId}: current status is {Status}",
                jobId, job.Status);
            return;
        }

        job.Status = TranscriptionJobStatuses.Cancelled;
        job.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Cancelled transcription job {JobId}", jobId);
    }

    // ── Pipeline Execution ──

    /// <summary>
    /// Executes the full transcription pipeline: media preparation, routing, execution, persistence.
    /// </summary>
    private async Task ExecuteTranscriptionPipelineAsync(
        TranscriptionJob job,
        AudioAsset audioAsset,
        CreateTranscriptionJobRequest request,
        CancellationToken ct)
    {
        // ── Update job to running ──

        job.Status = TranscriptionJobStatuses.Running;
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // ── Step (d): Run media preparation (normalize, VAD, segment) ──

        var audioFilePath = !string.IsNullOrEmpty(audioAsset.NormalizedFilePath)
            ? audioAsset.NormalizedFilePath
            : audioAsset.OriginalFilePath;

        _logger.LogInformation(
            "Job {JobId}: starting media preparation for {FilePath}",
            job.Id, audioFilePath);

        var prepResult = await _mediaPrep.PrepareAsync(audioFilePath, audioAsset.MimeType, ct);

        // Update audio asset with normalized path and metadata if not already set.
        if (string.IsNullOrEmpty(audioAsset.NormalizedFilePath))
        {
            audioAsset.NormalizedFilePath = prepResult.NormalizedFilePath;
            audioAsset.SourceSha256 = prepResult.SourceSha256;
            audioAsset.DurationMs = prepResult.DurationMs;
            audioAsset.SampleRate = prepResult.SampleRate;
            audioAsset.Channels = prepResult.Channels;
            audioAsset.UpdatedAt = DateTime.UtcNow;
        }

        // ── Step (e): Resolve ASR provider via AudioPolicyRouter ──

        var routingContext = new AsrRoutingContext
        {
            DataClassification = request.DataClassification,
            PreferredExecutionMode = null,
            PreferredCredentialMode = null,
            PreferredProviderId = request.PreferredProviderId,
            PreferredModelId = request.PreferredModelId,
            Language = request.Language,
            EnableVad = request.EnableVad,
            EnableSpeakerDiarization = request.EnableSpeakerDiarization,
            EnablePunctuation = request.EnablePunctuation,
            EnableHotwords = request.Hotwords is { Count: > 0 },
            EnableWordTimestamp = false,
            FileSizeBytes = audioAsset.FileSizeBytes,
            DurationMs = prepResult.DurationMs,
            MimeType = audioAsset.MimeType,
            FallbackPolicy = request.FallbackPolicy,
            UserId = job.UserId,
            WorkspaceId = job.WorkspaceId,
        };

        _logger.LogInformation("Job {JobId}: resolving ASR provider via policy router", job.Id);

        var provider = await _router.ResolveAsrProviderAsync(routingContext, ct);
        var descriptor = await provider.GetDescriptorAsync(ct);

        // Update job with resolved provider/model and four-layer decoupling info.
        job.ProviderId = descriptor.ProviderId;
        job.ModelId = descriptor.ModelId;
        job.ExecutionMode = routingContext.PreferredExecutionMode?.ToString()
            ?? descriptor.ExecutionModes.FirstOrDefault().ToString()
            ?? job.ExecutionMode;
        job.CredentialMode = routingContext.PreferredCredentialMode?.ToString()
            ?? descriptor.CredentialModes.FirstOrDefault().ToString()
            ?? job.CredentialMode;

        await _db.SaveChangesAsync(ct);

        // ── Step (f): Execute transcription via provider ──

        // Determine the best audio file path for transcription.
        // Use the normalized file from media preparation.
        var transcriptionFilePath = prepResult.NormalizedFilePath;

        // Use audio cache for deduplication if a cache key is available.
        if (!string.IsNullOrEmpty(prepResult.CacheKey))
        {
            var cachedPath = await _audioCache.GetAsync(prepResult.CacheKey, ct);
            if (!string.IsNullOrEmpty(cachedPath))
            {
                transcriptionFilePath = cachedPath;
            }
            else
            {
                await _audioCache.PutAsync(prepResult.CacheKey, transcriptionFilePath, ct);
            }
        }

        var transcriptionRequest = new AsrTranscriptionRequest
        {
            AudioFilePath = transcriptionFilePath,
            AudioCacheKey = prepResult.CacheKey,
            MimeType = audioAsset.MimeType,
            FileSizeBytes = audioAsset.FileSizeBytes,
            DurationMs = prepResult.DurationMs,
            Language = request.Language,
            EnableVad = request.EnableVad,
            EnableSpeakerDiarization = request.EnableSpeakerDiarization,
            EnablePunctuation = request.EnablePunctuation,
            Hotwords = request.Hotwords,
            DataClassification = request.DataClassification,
            PreferredExecutionMode = routingContext.PreferredExecutionMode,
            PreferredCredentialMode = routingContext.PreferredCredentialMode,
            PreferredProviderId = descriptor.ProviderId,
            PreferredModelId = descriptor.ModelId,
            FallbackPolicy = request.FallbackPolicy,
            SegmentUuidPrefix = $"{job.Id:N}",
            UserId = job.UserId,
            WorkspaceId = job.WorkspaceId,
        };

        _logger.LogInformation(
            "Job {JobId}: executing transcription with provider {ProviderId} (model: {ModelId})",
            job.Id, descriptor.ProviderId, descriptor.ModelId);

        var result = await provider.TranscribeAsync(transcriptionRequest, ct);

        // ── Step (g): Save TranscriptionSegments with stable segment_uuid ──

        var segments = new List<TranscriptionSegment>();
        var segmentNow = DateTime.UtcNow;

        for (int i = 0; i < result.Segments.Count; i++)
        {
            var seg = result.Segments[i];
            var segment = new TranscriptionSegment
            {
                Id = Guid.NewGuid(),
                TranscriptionJobId = job.Id,
                WorkspaceId = job.WorkspaceId,
                // Stable segment UUID: job_id + segment_index, never changes across versions.
                SegmentUuid = string.IsNullOrEmpty(seg.SegmentUuid)
                    ? $"{job.Id:N}-{i:D4}"
                    : seg.SegmentUuid,
                SourceStartMs = seg.StartMs,
                SourceEndMs = seg.EndMs,
                ProviderId = result.ProviderId,
                ModelId = result.ModelId,
                Confidence = seg.Confidence,
                SpeakerKey = seg.SpeakerKey,
                Text = seg.Text,
                Version = SegmentVersions.RawModel,
                SegmentIndex = i,
                CreatedAt = segmentNow,
                UpdatedAt = segmentNow,
            };

            segments.Add(segment);
        }

        if (segments.Count > 0)
        {
            _db.TranscriptionSegments.AddRange(segments);
        }

        // ── Step (g2): Post-ASR text correction ──
        //
        // Apply dictionary-based corrections (brand names, person names, terminology,
        // abbreviations, homophones, user entries) to the ASR output. Creates new
        // POST_PROCESSED segment records without modifying the original RAW_MODEL segments.
        // Correction failures are non-fatal: the job still completes with RAW_MODEL segments.

        if (segments.Count > 0)
        {
            try
            {
                var correctedSegments = await ApplyPostAsrCorrectionAsync(job, segments, request.Language, ct);
                if (correctedSegments.Count > 0)
                {
                    _db.TranscriptionSegments.AddRange(correctedSegments);
                    _logger.LogInformation(
                        "Job {JobId}: post-ASR correction created {Count} POST_PROCESSED segments",
                        job.Id, correctedSegments.Count);
                }
            }
            catch (Exception corrEx)
            {
                _logger.LogWarning(corrEx,
                    "Job {JobId}: post-ASR correction failed, continuing with RAW_MODEL segments only",
                    job.Id);
            }
        }

        // ── Step (h): Update job status to completed ──

        job.Status = TranscriptionJobStatuses.Completed;
        job.SegmentCount = segments.Count;
        job.EstimatedCost = result.EstimatedCost;
        job.ActualCost = result.EstimatedCost; // Updated when actual billing is reconciled.
        job.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Job {JobId}: transcription completed successfully. {SegmentCount} segments, provider: {ProviderId}",
            job.Id, segments.Count, result.ProviderId);
    }

    // ── Error Handling & Fallback ──

    /// <summary>
    /// Handles transcription failure by updating the job status and applying fallback policy.
    /// </summary>
    private async Task HandleTranscriptionFailureAsync(
        TranscriptionJob job,
        AudioAsset audioAsset,
        CreateTranscriptionJobRequest request,
        Exception ex,
        CancellationToken ct)
    {
        _logger.LogError(ex,
            "Job {JobId}: transcription failed with provider {ProviderId}. Error: {Message}",
            job.Id, job.ProviderId, ex.Message);

        job.ErrorMessage = ex.Message;

        // If fallback policy is STOP, mark as failed immediately.
        if (request.FallbackPolicy == FallbackPolicies.Stop)
        {
            job.Status = TranscriptionJobStatuses.Failed;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        // For LOCAL_FALLBACK and PLATFORM_FALLBACK, attempt a fallback routing.
        try
        {
            var fallbackContext = new AsrRoutingContext
            {
                DataClassification = request.DataClassification,
                // Relax execution mode preference to let the fallback policy pick the mode.
                PreferredExecutionMode = null,
                PreferredCredentialMode = null,
                PreferredProviderId = null,
                PreferredModelId = null,
                Language = request.Language,
                EnableVad = request.EnableVad,
                EnableSpeakerDiarization = request.EnableSpeakerDiarization,
                EnablePunctuation = request.EnablePunctuation,
                EnableHotwords = request.Hotwords is { Count: > 0 },
                FileSizeBytes = audioAsset.FileSizeBytes,
                DurationMs = audioAsset.DurationMs,
                MimeType = audioAsset.MimeType,
                FallbackPolicy = request.FallbackPolicy,
                UserId = job.UserId,
                WorkspaceId = job.WorkspaceId,
            };

            var fallbackProvider = await _router.ResolveAsrProviderAsync(fallbackContext, ct);
            var fallbackDescriptor = await fallbackProvider.GetDescriptorAsync(ct);

            _logger.LogInformation(
                "Job {JobId}: applying fallback policy {Policy}, resolved provider {ProviderId}",
                job.Id, request.FallbackPolicy, fallbackDescriptor.ProviderId);

            // Update job with fallback provider info.
            job.ProviderId = fallbackDescriptor.ProviderId;
            job.ModelId = fallbackDescriptor.ModelId;
            job.ExecutionMode = fallbackDescriptor.ExecutionModes.FirstOrDefault().ToString() ?? job.ExecutionMode;
            job.CredentialMode = fallbackDescriptor.CredentialModes.FirstOrDefault().ToString() ?? job.CredentialMode;

            // Execute fallback transcription.
            var fallbackRequest = new AsrTranscriptionRequest
            {
                AudioFilePath = audioAsset.NormalizedFilePath ?? audioAsset.OriginalFilePath,
                MimeType = audioAsset.MimeType,
                FileSizeBytes = audioAsset.FileSizeBytes,
                DurationMs = audioAsset.DurationMs,
                Language = request.Language,
                EnableVad = request.EnableVad,
                EnableSpeakerDiarization = request.EnableSpeakerDiarization,
                EnablePunctuation = request.EnablePunctuation,
                Hotwords = request.Hotwords,
                DataClassification = request.DataClassification,
                FallbackPolicy = FallbackPolicies.Stop, // No further fallback.
                SegmentUuidPrefix = $"{job.Id:N}",
                UserId = job.UserId,
                WorkspaceId = job.WorkspaceId,
            };

            var fallbackResult = await fallbackProvider.TranscribeAsync(fallbackRequest, ct);

            // Save fallback segments.
            var segments = new List<TranscriptionSegment>();
            var now = DateTime.UtcNow;

            for (int i = 0; i < fallbackResult.Segments.Count; i++)
            {
                var seg = fallbackResult.Segments[i];
                segments.Add(new TranscriptionSegment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionJobId = job.Id,
                    WorkspaceId = job.WorkspaceId,
                    SegmentUuid = string.IsNullOrEmpty(seg.SegmentUuid)
                        ? $"{job.Id:N}-{i:D4}"
                        : seg.SegmentUuid,
                    SourceStartMs = seg.StartMs,
                    SourceEndMs = seg.EndMs,
                    ProviderId = fallbackResult.ProviderId,
                    ModelId = fallbackResult.ModelId,
                    Confidence = seg.Confidence,
                    SpeakerKey = seg.SpeakerKey,
                    Text = seg.Text,
                    Version = SegmentVersions.RawModel,
                    SegmentIndex = i,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            if (segments.Count > 0)
            {
                _db.TranscriptionSegments.AddRange(segments);
            }

            job.Status = TranscriptionJobStatuses.Completed;
            job.SegmentCount = segments.Count;
            job.EstimatedCost = fallbackResult.EstimatedCost;
            job.ActualCost = fallbackResult.EstimatedCost;
            job.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Job {JobId}: fallback transcription completed. {SegmentCount} segments, provider: {ProviderId}",
                job.Id, segments.Count, fallbackResult.ProviderId);
        }
        catch (Exception fallbackEx)
        {
            _logger.LogError(fallbackEx,
                "Job {JobId}: fallback transcription also failed. Marking as failed.",
                job.Id);

            job.Status = TranscriptionJobStatuses.Failed;
            job.ErrorMessage = $"Primary: {ex.Message} | Fallback: {fallbackEx.Message}";
            job.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Applies post-ASR text correction to all segments and creates new
    /// POST_PROCESSED segment records without modifying the original RAW_MODEL segments.
    /// </summary>
    /// <param name="job">The transcription job.</param>
    /// <param name="rawSegments">The original RAW_MODEL segments.</param>
    /// <param name="language">The language code for dictionary filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of new POST_PROCESSED segments, or empty if no corrections were applied.</returns>
    private async Task<List<TranscriptionSegment>> ApplyPostAsrCorrectionAsync(
        TranscriptionJob job,
        List<TranscriptionSegment> rawSegments,
        string? language,
        CancellationToken ct)
    {
        // Build full text from all segments for context.
        var fullText = string.Join("\n", rawSegments.Select(s => s.Text));

        // Run correction on the full text first (for overall statistics and early exit).
        var fullRequest = new CorrectionRequest
        {
            Text = fullText,
            WorkspaceId = job.WorkspaceId,
            Language = language,
            SegmentUuids = rawSegments.Select(s => s.SegmentUuid).ToList(),
            Context = null,
        };
        var fullResult = await _correctionService.CorrectAsync(fullRequest, ct);

        // If no corrections were applied, skip creating POST_PROCESSED segments.
        if (fullResult.AppliedDictionaryEntries == 0)
        {
            _logger.LogInformation(
                "Job {JobId}: post-ASR correction found no dictionary matches, skipping POST_PROCESSED segments",
                job.Id);
            return new List<TranscriptionSegment>();
        }

        // Correct each segment individually to produce per-segment corrected text.
        var now = DateTime.UtcNow;
        var correctedSegments = new List<TranscriptionSegment>();

        foreach (var seg in rawSegments)
        {
            var segRequest = new CorrectionRequest
            {
                Text = seg.Text,
                WorkspaceId = job.WorkspaceId,
                Language = language,
                SegmentUuids = new List<string> { seg.SegmentUuid },
                Context = fullText,
            };
            var segResult = await _correctionService.CorrectAsync(segRequest, ct);

            correctedSegments.Add(new TranscriptionSegment
            {
                Id = Guid.NewGuid(),
                TranscriptionJobId = job.Id,
                WorkspaceId = job.WorkspaceId,
                // Stable segment UUID is preserved across versions.
                SegmentUuid = seg.SegmentUuid,
                SourceStartMs = seg.SourceStartMs,
                SourceEndMs = seg.SourceEndMs,
                ProviderId = seg.ProviderId,
                ModelId = seg.ModelId,
                Confidence = seg.Confidence,
                SpeakerKey = seg.SpeakerKey,
                Text = segResult.CorrectedText,
                Version = SegmentVersions.PostProcessed,
                SegmentIndex = seg.SegmentIndex,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        _logger.LogInformation(
            "Job {JobId}: post-ASR correction applied {ChangeCount} changes from {EntryCount} dictionary entries",
            job.Id, fullResult.Changes.Count, fullResult.AppliedDictionaryEntries);

        return correctedSegments;
    }

    /// <summary>
    /// Returns a priority value for a segment version. Higher values are preferred
    /// when multiple versions of the same segment UUID exist.
    /// </summary>
    private static int SegmentVersionPriority(string version)
    {
        return version switch
        {
            SegmentVersions.Published => 6,
            SegmentVersions.UserEdited => 5,
            SegmentVersions.Merged => 4,
            SegmentVersions.PostProcessed => 3,
            SegmentVersions.ServerRetranscribed => 2,
            SegmentVersions.RawModel => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// Updates a job's status and optional error message, then saves to DB.
    /// </summary>
    private async Task UpdateJobStatusAsync(
        TranscriptionJob job, string status, string? errorMessage, CancellationToken ct)
    {
        job.Status = status;
        job.ErrorMessage = errorMessage;
        job.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Maps a <see cref="TranscriptionSegment"/> entity to a <see cref="TranscriptionSegmentDto"/>.
    /// </summary>
    private static TranscriptionSegmentDto ToSegmentDto(TranscriptionSegment s)
    {
        return new TranscriptionSegmentDto
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
            CreatedAt = s.CreatedAt,
        };
    }
}
