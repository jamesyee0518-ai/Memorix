using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Orchestrates the asynchronous meeting processing pipeline (§14).
/// Handles task splitting, state machine transitions, idempotency,
/// retry logic, and degradation strategies for the full meeting lifecycle:
/// audio normalize → VAD → batch transcribe → diarize → summarize → action items → publish.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is modelled as a DAG of <see cref="MeetingProcessingTask"/> entities (§14.1).
/// Each task transitions through a well-defined state machine (§14.2):
/// PENDING → QUEUED → RUNNING → SUCCEEDED / FAILED_RETRYABLE / FAILED_FINAL / CANCELED.
/// </para>
/// <para>
/// Idempotency (§14.3) is enforced via a stable key derived from
/// meetingId + audioAssetId + taskType + config hash, preventing duplicate task creation
/// and duplicate official-version writes on retry.
/// </para>
/// <para>
/// Degradation strategies (§14.4) are applied per task type on failure, allowing the
/// pipeline to continue with reduced quality where possible (e.g., transcript without
/// speakers, raw transcript without punctuation) or to pause for user confirmation.
/// </para>
/// </remarks>
public class MeetingProcessingService : IMeetingProcessingService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<MeetingProcessingService> _logger;
    private readonly IMediaPreparationService? _mediaPrep;
    private readonly IMeetingService? _meetingService;
    private readonly IMeetingPublishingService? _publishingService;

    // ══════════════════════════════════════════════════════════════════════
    //  Constructors
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal constructor — database, current user, and logger only.
    /// Optional pipeline services (media prep, meeting, publishing) will be null;
    /// their corresponding task types will fail at execution time with a clear error.
    /// </summary>
    public MeetingProcessingService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<MeetingProcessingService> logger)
        : this(db, currentUser, logger, null, null, null)
    {
    }

    /// <summary>
    /// Full constructor — injects all optional pipeline services.
    /// </summary>
    public MeetingProcessingService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<MeetingProcessingService> logger,
        IMediaPreparationService? mediaPrep,
        IMeetingService? meetingService,
        IMeetingPublishingService? publishingService)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _mediaPrep = mediaPrep;
        _meetingService = meetingService;
        _publishingService = publishingService;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Pipeline definition (§14.1 task splitting)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ordered pipeline definition: (TaskType, DependsOn).
    /// DependsOn is a comma-separated list of task types that must complete first.
    /// </summary>
    private static readonly (string TaskType, string? DependsOn)[] PipelineSteps =
    {
        (MeetingProcessingTaskTypes.AudioNormalize, null),
        (MeetingProcessingTaskTypes.VoiceActivityDetection, MeetingProcessingTaskTypes.AudioNormalize),
        (MeetingProcessingTaskTypes.BatchTranscribe, MeetingProcessingTaskTypes.VoiceActivityDetection),
        (MeetingProcessingTaskTypes.PunctuationRestore, MeetingProcessingTaskTypes.BatchTranscribe),
        (MeetingProcessingTaskTypes.SpeakerDiarize, MeetingProcessingTaskTypes.BatchTranscribe),
        (MeetingProcessingTaskTypes.TranscriptAlign, MeetingProcessingTaskTypes.SpeakerDiarize),
        (MeetingProcessingTaskTypes.TextNormalize, MeetingProcessingTaskTypes.PunctuationRestore),
        (MeetingProcessingTaskTypes.MeetingSummarize,
            $"{MeetingProcessingTaskTypes.TextNormalize},{MeetingProcessingTaskTypes.TranscriptAlign}"),
        (MeetingProcessingTaskTypes.ActionItemExtract, MeetingProcessingTaskTypes.MeetingSummarize),
        (MeetingProcessingTaskTypes.KnowledgePublish, MeetingProcessingTaskTypes.ActionItemExtract),
    };

    // ══════════════════════════════════════════════════════════════════════
    //  CreatePipelineAsync
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<MeetingProcessingTaskDto>> CreatePipelineAsync(
        Guid meetingId, Guid audioAssetId, Guid userId, CancellationToken ct)
    {
        _logger.LogInformation(
            "CreatePipeline: meeting={MeetingId}, audioAsset={AudioAssetId}, user={UserId}",
            meetingId, audioAssetId, userId);

        // Verify the meeting exists
        var meeting = await _db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting == null)
        {
            _logger.LogWarning("CreatePipeline: meeting {MeetingId} not found", meetingId);
            throw new InvalidOperationException($"Meeting {meetingId} not found.");
        }

        // Load any existing tasks for this meeting + audio asset (idempotency at pipeline level)
        var existingTasks = await _db.MeetingProcessingTasks
            .Where(t => t.MeetingId == meetingId && t.AudioAssetId == audioAssetId)
            .ToListAsync(ct);

        var existingByType = existingTasks.ToDictionary(t => t.TaskType, t => t);
        var createdCount = 0;

        foreach (var (taskType, dependsOn) in PipelineSteps)
        {
            // Idempotency (§14.3): if a task of this type already exists for the
            // meeting + audio asset, don't create a duplicate.
            if (existingByType.ContainsKey(taskType))
            {
                _logger.LogDebug(
                    "CreatePipeline: task {TaskType} already exists for meeting {MeetingId}, skipping",
                    taskType, meetingId);
                continue;
            }

            var idempotencyKey = ComputeIdempotencyKey(meetingId, audioAssetId, taskType, configHash: null);

            var task = new MeetingProcessingTask
            {
                Id = Guid.NewGuid(),
                MeetingId = meetingId,
                AudioAssetId = audioAssetId,
                TaskType = taskType,
                Status = MeetingProcessingTaskStatuses.Pending,
                IdempotencyKey = idempotencyKey,
                ExecutionMode = "LOCAL_DEVICE",
                CredentialMode = "NO_CREDENTIAL",
                DependsOn = dependsOn,
                RetryCount = 0,
                MaxRetries = 3,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
            };

            _db.MeetingProcessingTasks.Add(task);
            createdCount++;
        }

        if (createdCount > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "CreatePipeline: created {Count} tasks for meeting {MeetingId}",
                createdCount, meetingId);
        }

        // Return all tasks (existing + newly created) as DTOs
        var allTasks = await _db.MeetingProcessingTasks
            .Where(t => t.MeetingId == meetingId && t.AudioAssetId == audioAssetId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        return allTasks.Select(MapToDto).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExecutePipelineAsync
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<MeetingProcessingResultDto> ExecutePipelineAsync(
        Guid meetingId, Guid userId, CancellationToken ct)
    {
        _logger.LogInformation("ExecutePipeline: meeting={MeetingId}, user={UserId}", meetingId, userId);

        // 1. Load all PENDING / FAILED_RETRYABLE tasks for execution
        var tasks = await _db.MeetingProcessingTasks
            .Where(t => t.MeetingId == meetingId
                        && (t.Status == MeetingProcessingTaskStatuses.Pending
                            || t.Status == MeetingProcessingTaskStatuses.FailedRetryable))
            .ToListAsync(ct);

        if (tasks.Count == 0)
        {
            _logger.LogInformation(
                "ExecutePipeline: no pending/retryable tasks for meeting {MeetingId}", meetingId);
            return await BuildResultAsync(meetingId, ct);
        }

        // 2. Topological sort by DependsOn (dependency-ordered execution)
        var sortedTasks = TopologicalSort(tasks);

        // 3. Execute each task in dependency order
        foreach (var task in sortedTasks)
        {
            // Idempotency: skip if already SUCCEEDED (safety check)
            if (task.Status == MeetingProcessingTaskStatuses.Succeeded)
            {
                _logger.LogDebug(
                    "ExecutePipeline: task {TaskType} already succeeded, skipping", task.TaskType);
                continue;
            }

            // Check that all dependencies are SUCCEEDED
            if (!await AreDependenciesSatisfiedAsync(task, meetingId, ct))
            {
                _logger.LogWarning(
                    "ExecutePipeline: dependencies not satisfied for task {TaskType}, skipping",
                    task.TaskType);
                continue;
            }

            // Transition to RUNNING (§14.2 state machine: PENDING → RUNNING)
            task.Status = MeetingProcessingTaskStatuses.Running;
            task.StartedAt = DateTime.UtcNow;
            task.ErrorMessage = null;
            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            try
            {
                // Execute the task based on its type
                await ExecuteTaskAsync(task, userId, ct);

                // On success: transition to SUCCEEDED
                task.Status = MeetingProcessingTaskStatuses.Succeeded;
                task.FinishedAt = DateTime.UtcNow;
                task.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "ExecutePipeline: task {TaskType} succeeded for meeting {MeetingId}",
                    task.TaskType, meetingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ExecutePipeline: task {TaskType} failed for meeting {MeetingId}",
                    task.TaskType, meetingId);

                task.ErrorMessage = ex.Message;
                task.FinishedAt = DateTime.UtcNow;
                task.UpdatedAt = DateTime.UtcNow;

                // Apply degradation strategy (§14.4)
                var specialStatusSet = await ApplyDegradationStrategyAsync(task, ex, ct);

                // If degradation set a special status (e.g., WAITING_USER_CONFIRMATION),
                // skip the normal retry/final-failure logic
                if (!specialStatusSet)
                {
                    if (task.RetryCount < task.MaxRetries)
                    {
                        task.Status = MeetingProcessingTaskStatuses.FailedRetryable;
                    }
                    else
                    {
                        task.Status = MeetingProcessingTaskStatuses.FailedFinal;
                    }
                }

                await _db.SaveChangesAsync(ct);
            }
        }

        return await BuildResultAsync(meetingId, ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExecuteTaskAsync — dispatches to the correct handler per task type
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Executes a single task based on its TaskType.
    /// </summary>
    private async Task ExecuteTaskAsync(MeetingProcessingTask task, Guid userId, CancellationToken ct)
    {
        switch (task.TaskType)
        {
            case MeetingProcessingTaskTypes.AudioNormalize:
                await ExecuteAudioNormalizeAsync(task, ct);
                break;

            case MeetingProcessingTaskTypes.BatchTranscribe:
                await ExecuteBatchTranscribeAsync(task, userId, ct);
                break;

            case MeetingProcessingTaskTypes.MeetingSummarize:
                await ExecuteMeetingSummarizeAsync(task, userId, ct);
                break;

            case MeetingProcessingTaskTypes.KnowledgePublish:
                await ExecuteKnowledgePublishAsync(task, userId, ct);
                break;

            // Pass-through tasks: their work is performed as part of parent tasks
            // in the DAG pipeline (e.g., VAD is done by AUDIO_NORMALIZE via
            // IMediaPreparationService; punctuation/diarization/alignment by
            // BATCH_TRANSCRIBE; action item extraction by MEETING_SUMMARIZE).
            // These tasks are marked SUCCEEDED without separate execution.
            case MeetingProcessingTaskTypes.VoiceActivityDetection:
            case MeetingProcessingTaskTypes.PunctuationRestore:
            case MeetingProcessingTaskTypes.SpeakerDiarize:
            case MeetingProcessingTaskTypes.TranscriptAlign:
            case MeetingProcessingTaskTypes.TextNormalize:
            case MeetingProcessingTaskTypes.ActionItemExtract:
                _logger.LogDebug(
                    "ExecuteTask: pass-through task {TaskType} — work done by parent task",
                    task.TaskType);
                break;

            default:
                _logger.LogWarning(
                    "ExecuteTask: unknown task type {TaskType}, skipping", task.TaskType);
                break;
        }
    }

    /// <summary>
    /// AUDIO_NORMALIZE: Calls <see cref="IMediaPreparationService.PrepareAsync"/> to normalize
    /// the audio (16 kHz, mono, PCM s16le), compute SHA-256, run VAD, and update the
    /// <see cref="AudioAsset"/> with the normalized path, sha256, and duration.
    /// </summary>
    private async Task ExecuteAudioNormalizeAsync(MeetingProcessingTask task, CancellationToken ct)
    {
        if (_mediaPrep == null)
        {
            throw new InvalidOperationException(
                "IMediaPreparationService is not available. Cannot execute AUDIO_NORMALIZE.");
        }

        if (!task.AudioAssetId.HasValue)
        {
            throw new InvalidOperationException("AUDIO_NORMALIZE requires an AudioAssetId.");
        }

        var audioAsset = await _db.AudioAssets
            .FirstOrDefaultAsync(a => a.Id == task.AudioAssetId.Value, ct);

        if (audioAsset == null)
        {
            throw new InvalidOperationException(
                $"AudioAsset {task.AudioAssetId} not found for AUDIO_NORMALIZE.");
        }

        var result = await _mediaPrep.PrepareAsync(
            audioAsset.OriginalFilePath,
            audioAsset.MimeType,
            ct);

        // Update the AudioAsset with normalized path, sha256, duration
        audioAsset.NormalizedFilePath = result.NormalizedFilePath;
        audioAsset.SourceSha256 = result.SourceSha256;
        audioAsset.DurationMs = result.DurationMs;
        audioAsset.SampleRate = result.SampleRate;
        audioAsset.Channels = result.Channels;
        audioAsset.UpdatedAt = DateTime.UtcNow;

        // Store result data on the task
        task.ResultData = JsonSerializer.Serialize(new
        {
            normalizedFilePath = result.NormalizedFilePath,
            sourceSha256 = result.SourceSha256,
            cacheKey = result.CacheKey,
            durationMs = result.DurationMs,
            sampleRate = result.SampleRate,
            channels = result.Channels,
            vadSegmentCount = result.VadSegments.Count,
            segmentFilePaths = result.SegmentFilePaths,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AUDIO_NORMALIZE: asset {AudioAssetId} normalized, duration={DurationMs}ms, vadSegments={VadCount}",
            audioAsset.Id, result.DurationMs, result.VadSegments.Count);
    }

    /// <summary>
    /// BATCH_TRANSCRIBE: Creates a <see cref="TranscriptionJob"/> and calls
    /// <see cref="IMeetingService.TriggerTranscriptionAsync"/> to initiate the transcription
    /// (which internally handles VAD, punctuation, diarization, and alignment).
    /// </summary>
    private async Task ExecuteBatchTranscribeAsync(MeetingProcessingTask task, Guid userId, CancellationToken ct)
    {
        if (_meetingService == null)
        {
            throw new InvalidOperationException(
                "IMeetingService is not available. Cannot execute BATCH_TRANSCRIBE.");
        }

        // Create a TranscriptionJob record
        var transcriptionJob = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            AudioAssetId = task.AudioAssetId ?? Guid.Empty,
            UserId = userId,
            ExecutionMode = task.ExecutionMode,
            CredentialMode = task.CredentialMode,
            ProviderId = task.ProviderId ?? string.Empty,
            ModelId = task.ModelId ?? string.Empty,
            FallbackPolicy = "LOCAL_FALLBACK",
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };

        _db.TranscriptionJobs.Add(transcriptionJob);
        await _db.SaveChangesAsync(ct);

        // Link the task to the transcription job
        task.TranscriptionJobId = transcriptionJob.Id;

        // Trigger transcription via IMeetingService
        var request = new CreateTranscriptionRequest
        {
            SourceAssetId = task.AudioAssetId,
            EnableVad = true,
            EnableSpeakerDiarization = true,
            EnablePunctuation = true,
        };

        var transcript = await _meetingService.TriggerTranscriptionAsync(
            task.MeetingId, request, userId, ct);

        // Update transcription job status
        transcriptionJob.Status = "completed";
        transcriptionJob.CompletedAt = DateTime.UtcNow;

        // Store result data
        task.ResultData = JsonSerializer.Serialize(new
        {
            transcriptionJobId = transcriptionJob.Id,
            transcriptVersionId = transcript.Id,
            transcriptStatus = transcript.Status,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "BATCH_TRANSCRIBE: transcription {TranscriptId} created for meeting {MeetingId}",
            transcript.Id, task.MeetingId);
    }

    /// <summary>
    /// MEETING_SUMMARIZE: Calls <see cref="IMeetingService.GenerateMinutesAsync"/> to generate
    /// meeting minutes (and action items) from the official transcript.
    /// </summary>
    private async Task ExecuteMeetingSummarizeAsync(MeetingProcessingTask task, Guid userId, CancellationToken ct)
    {
        if (_meetingService == null)
        {
            throw new InvalidOperationException(
                "IMeetingService is not available. Cannot execute MEETING_SUMMARIZE.");
        }

        var request = new GenerateMinutesRequest();

        var minutes = await _meetingService.GenerateMinutesAsync(
            task.MeetingId, request, userId, ct);

        if (minutes == null)
        {
            throw new InvalidOperationException(
                "GenerateMinutesAsync returned null — minutes generation failed.");
        }

        // Store result data
        task.ResultData = JsonSerializer.Serialize(new
        {
            minutesVersionId = minutes.Id,
            minutesVersionNo = minutes.VersionNo,
            minutesStatus = minutes.Status,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "MEETING_SUMMARIZE: minutes {MinutesId} generated for meeting {MeetingId}",
            minutes.Id, task.MeetingId);
    }

    /// <summary>
    /// KNOWLEDGE_PUBLISH: Calls <see cref="IMeetingPublishingService.PublishAllAsync"/> to publish
    /// all meeting artifacts (minutes, transcript, action items) to the knowledge base.
    /// </summary>
    private async Task ExecuteKnowledgePublishAsync(MeetingProcessingTask task, Guid userId, CancellationToken ct)
    {
        if (_publishingService == null)
        {
            throw new InvalidOperationException(
                "IMeetingPublishingService is not available. Cannot execute KNOWLEDGE_PUBLISH.");
        }

        var result = await _publishingService.PublishAllAsync(task.MeetingId, userId, ct);

        // Store result data
        task.ResultData = JsonSerializer.Serialize(new
        {
            status = result.Status,
            sourceId = result.SourceId,
            documentId = result.DocumentId,
            tasksCreated = result.TasksCreated,
            message = result.Message,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "KNOWLEDGE_PUBLISH: meeting {MeetingId} published, status={Status}, tasks={TasksCreated}",
            task.MeetingId, result.Status, result.TasksCreated);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Degradation strategies (§14.4)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies the degradation strategy for a failed task based on its type (§14.4).
    /// Returns <c>true</c> if a special status was set (e.g., WAITING_USER_CONFIRMATION),
    /// indicating the normal retry/failure logic should be skipped.
    /// </summary>
    private Task<bool> ApplyDegradationStrategyAsync(
        MeetingProcessingTask task, Exception ex, CancellationToken ct)
    {
        switch (task.TaskType)
        {
            case MeetingProcessingTaskTypes.StreamingTranscribe:
                // §14.4: If real-time STT fails → continue recording,
                //         offline transcription after meeting ends
                _logger.LogWarning(
                    "Degradation: streaming STT failed for meeting {MeetingId}, " +
                    "will use offline transcription after meeting ends. Error: {Error}",
                    task.MeetingId, ex.Message);
                break;

            case MeetingProcessingTaskTypes.SpeakerDiarize:
                // §14.4: If speaker diarization fails → generate transcript without speakers,
                //         allow later reprocessing
                _logger.LogWarning(
                    "Degradation: speaker diarization failed for meeting {MeetingId}, " +
                    "transcript will be generated without speakers. Reprocessing allowed later.",
                    task.MeetingId);
                break;

            case MeetingProcessingTaskTypes.PunctuationRestore:
                // §14.4: If punctuation model fails → keep raw transcript,
                //         use rule-based or LLM post-processing
                _logger.LogWarning(
                    "Degradation: punctuation model failed for meeting {MeetingId}, " +
                    "keeping raw transcript with rule-based post-processing.",
                    task.MeetingId);
                break;

            case MeetingProcessingTaskTypes.MeetingSummarize:
                // §14.4: If local LLM fails → wait for user to confirm BYOK/cloud,
                //         or just save transcript
                _logger.LogWarning(
                    "Degradation: local LLM failed for meeting {MeetingId}, " +
                    "setting task to WAITING_USER_CONFIRMATION for BYOK/cloud confirmation.",
                    task.MeetingId);
                task.Status = MeetingProcessingTaskStatuses.WaitingUserConfirmation;
                return Task.FromResult(true);

            case MeetingProcessingTaskTypes.BatchTranscribe:
                // §14.4: If cloud provider fails → retry then prompt to switch provider
                _logger.LogWarning(
                    "Degradation: cloud provider failed for meeting {MeetingId}, " +
                    "will retry then prompt to switch provider. Error: {Error}",
                    task.MeetingId, ex.Message);
                break;

            case MeetingProcessingTaskTypes.KnowledgePublish:
                // §14.4: If knowledge base write fails → keep meeting artifacts,
                //         retry in background
                _logger.LogWarning(
                    "Degradation: knowledge base write failed for meeting {MeetingId}, " +
                    "keeping meeting artifacts for background retry. Error: {Error}",
                    task.MeetingId, ex.Message);
                break;

            default:
                _logger.LogWarning(
                    "Degradation: no specific strategy for task type {TaskType}, " +
                    "applying default retry logic.",
                    task.TaskType);
                break;
        }

        return Task.FromResult(false);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetTaskStatusAsync
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<MeetingProcessingTaskDto>> GetTaskStatusAsync(
        Guid meetingId, CancellationToken ct)
    {
        var tasks = await _db.MeetingProcessingTasks
            .Where(t => t.MeetingId == meetingId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        return tasks.Select(MapToDto).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RetryTaskAsync
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<bool> RetryTaskAsync(Guid taskId, Guid userId, CancellationToken ct)
    {
        var task = await _db.MeetingProcessingTasks
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task == null)
        {
            _logger.LogWarning("RetryTask: task {TaskId} not found", taskId);
            return false;
        }

        // Check if max retries have been exceeded
        if (task.RetryCount >= task.MaxRetries)
        {
            _logger.LogWarning(
                "RetryTask: task {TaskId} has exceeded max retries ({RetryCount}/{MaxRetries})",
                taskId, task.RetryCount, task.MaxRetries);
            return false;
        }

        // Reset to PENDING and increment retry count (§14.2 state machine)
        task.RetryCount++;
        task.Status = MeetingProcessingTaskStatuses.Pending;
        task.ErrorMessage = null;
        task.StartedAt = null;
        task.FinishedAt = null;
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "RetryTask: task {TaskId} ({TaskType}) reset to PENDING, retryCount={RetryCount}/{MaxRetries}",
            taskId, task.TaskType, task.RetryCount, task.MaxRetries);

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CancelPipelineAsync
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<bool> CancelPipelineAsync(Guid meetingId, Guid userId, CancellationToken ct)
    {
        // Cancel all PENDING / QUEUED / RUNNING / WAITING_USER_CONFIRMATION tasks
        var tasks = await _db.MeetingProcessingTasks
            .Where(t => t.MeetingId == meetingId
                        && (t.Status == MeetingProcessingTaskStatuses.Pending
                            || t.Status == MeetingProcessingTaskStatuses.Queued
                            || t.Status == MeetingProcessingTaskStatuses.Running
                            || t.Status == MeetingProcessingTaskStatuses.WaitingUserConfirmation))
            .ToListAsync(ct);

        if (tasks.Count == 0)
        {
            _logger.LogInformation(
                "CancelPipeline: no active tasks for meeting {MeetingId}", meetingId);
            return true;
        }

        foreach (var task in tasks)
        {
            task.Status = MeetingProcessingTaskStatuses.Canceled;
            task.FinishedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CancelPipeline: canceled {Count} tasks for meeting {MeetingId}",
            tasks.Count, meetingId);

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetPipelineProgressAsync
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<MeetingProcessingResultDto> GetPipelineProgressAsync(
        Guid meetingId, CancellationToken ct)
    {
        return await BuildResultAsync(meetingId, ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the idempotency key: SHA-256(meetingId | audioAssetId | taskType | configHash).
    /// Prevents duplicate task creation on retries (§14.3).
    /// </summary>
    private static string ComputeIdempotencyKey(
        Guid meetingId, Guid audioAssetId, string taskType, string? configHash)
    {
        var raw = $"{meetingId:N}|{audioAssetId:N}|{taskType}|{configHash ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Topological sort of tasks based on their <see cref="MeetingProcessingTask.DependsOn"/> field.
    /// Returns tasks in dependency order so that each task's dependencies appear before it.
    /// Dependencies not present in the input list (e.g., already SUCCEEDED) are skipped.
    /// </summary>
    private static List<MeetingProcessingTask> TopologicalSort(List<MeetingProcessingTask> tasks)
    {
        var taskByType = tasks.ToDictionary(t => t.TaskType, t => t);
        var visited = new HashSet<string>();
        var result = new List<MeetingProcessingTask>();

        void Visit(MeetingProcessingTask task)
        {
            if (visited.Contains(task.TaskType))
                return;

            visited.Add(task.TaskType);

            if (!string.IsNullOrEmpty(task.DependsOn))
            {
                var deps = task.DependsOn.Split(
                    ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var dep in deps)
                {
                    if (taskByType.TryGetValue(dep, out var depTask))
                    {
                        Visit(depTask);
                    }
                }
            }

            result.Add(task);
        }

        foreach (var task in tasks)
        {
            Visit(task);
        }

        return result;
    }

    /// <summary>
    /// Checks whether all dependencies of a task are satisfied (i.e., SUCCEEDED).
    /// A missing dependency is considered unsatisfied.
    /// </summary>
    private async Task<bool> AreDependenciesSatisfiedAsync(
        MeetingProcessingTask task, Guid meetingId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(task.DependsOn))
            return true;

        var deps = task.DependsOn.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dep in deps)
        {
            var depTask = await _db.MeetingProcessingTasks
                .FirstOrDefaultAsync(t => t.MeetingId == meetingId && t.TaskType == dep, ct);

            if (depTask == null)
            {
                _logger.LogWarning(
                    "Dependency {DepType} not found for task {TaskType} on meeting {MeetingId}",
                    dep, task.TaskType, meetingId);
                return false;
            }

            if (depTask.Status != MeetingProcessingTaskStatuses.Succeeded)
            {
                _logger.LogDebug(
                    "Dependency {DepType} is {Status} (not SUCCEEDED) for task {TaskType}",
                    dep, depTask.Status, task.TaskType);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the overall pipeline result DTO from the current task states.
    /// Computes per-status counts and derives an overall status.
    /// </summary>
    private async Task<MeetingProcessingResultDto> BuildResultAsync(
        Guid meetingId, CancellationToken ct)
    {
        var tasks = await _db.MeetingProcessingTasks
            .Where(t => t.MeetingId == meetingId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        var taskDtos = tasks.Select(MapToDto).ToList();

        var result = new MeetingProcessingResultDto
        {
            MeetingId = meetingId,
            TotalTasks = tasks.Count,
            SucceededTasks = tasks.Count(t => t.Status == MeetingProcessingTaskStatuses.Succeeded),
            FailedTasks = tasks.Count(t => t.Status == MeetingProcessingTaskStatuses.FailedRetryable
                                           || t.Status == MeetingProcessingTaskStatuses.FailedFinal),
            PendingTasks = tasks.Count(t => t.Status == MeetingProcessingTaskStatuses.Pending
                                            || t.Status == MeetingProcessingTaskStatuses.Queued),
            RunningTasks = tasks.Count(t => t.Status == MeetingProcessingTaskStatuses.Running
                                            || t.Status == MeetingProcessingTaskStatuses.WaitingUserConfirmation),
            CanceledTasks = tasks.Count(t => t.Status == MeetingProcessingTaskStatuses.Canceled),
            Tasks = taskDtos,
        };

        // Derive overall status
        if (result.TotalTasks == 0)
        {
            result.OverallStatus = "PENDING";
        }
        else if (result.SucceededTasks == result.TotalTasks)
        {
            result.OverallStatus = "COMPLETED";
        }
        else if (result.CanceledTasks == result.TotalTasks)
        {
            result.OverallStatus = "CANCELED";
        }
        else if (result.RunningTasks > 0)
        {
            result.OverallStatus = "RUNNING";
        }
        else if (result.FailedTasks > 0 && result.PendingTasks == 0)
        {
            result.OverallStatus = "FAILED";
        }
        else if (result.FailedTasks > 0)
        {
            result.OverallStatus = "PARTIAL_FAILURE";
        }
        else if (result.PendingTasks > 0)
        {
            result.OverallStatus = "PENDING";
        }
        else
        {
            result.OverallStatus = "UNKNOWN";
        }

        return result;
    }

    /// <summary>
    /// Maps a <see cref="MeetingProcessingTask"/> entity to its DTO.
    /// </summary>
    private static MeetingProcessingTaskDto MapToDto(MeetingProcessingTask task)
    {
        return new MeetingProcessingTaskDto
        {
            Id = task.Id,
            MeetingId = task.MeetingId,
            AudioAssetId = task.AudioAssetId,
            TranscriptionJobId = task.TranscriptionJobId,
            TaskType = task.TaskType,
            Status = task.Status,
            IdempotencyKey = task.IdempotencyKey,
            ExecutionMode = task.ExecutionMode,
            ProviderId = task.ProviderId,
            ModelId = task.ModelId,
            DependsOn = task.DependsOn,
            RetryCount = task.RetryCount,
            MaxRetries = task.MaxRetries,
            ErrorMessage = task.ErrorMessage,
            EstimatedCost = task.EstimatedCost,
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            FinishedAt = task.FinishedAt,
        };
    }
}
