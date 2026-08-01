using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Root ASR (Automatic Speech Recognition) pipeline node.
/// <para>
/// NodeId = "asr", DependsOn = [] (root node).
/// </para>
/// <para>
/// Loads the <see cref="AudioAsset"/> and <see cref="TranscriptionJob"/> for the
/// current context, performs media preparation (FFmpeg normalization + VAD),
/// resolves the best ASR provider via <see cref="IAudioPolicyRouter"/>, executes
/// transcription, and persists <c>RAW_MODEL</c> <see cref="TranscriptionSegment"/>
/// records. The produced segments are stored on
/// <see cref="PipelineContext.Segments"/> for all downstream nodes.
/// </para>
/// </summary>
public class AsrNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "asr";

    /// <inheritdoc/>
    public string DisplayName => "ASR Transcription";

    /// <inheritdoc/>
    public List<string> DependsOn => new();

    private readonly IAppDbContext _db;
    private readonly IAudioPolicyRouter _router;
    private readonly IMediaPreparationService _mediaPrep;
    private readonly IAudioCacheService _audioCache;
    private readonly ILogger<AsrNode> _logger;

    public AsrNode(
        IAppDbContext db,
        IAudioPolicyRouter router,
        IMediaPreparationService mediaPrep,
        IAudioCacheService audioCache,
        ILogger<AsrNode> logger)
    {
        _db = db;
        _router = router;
        _mediaPrep = mediaPrep;
        _audioCache = audioCache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        if (context.AudioAssetId == Guid.Empty)
        {
            return false;
        }

        var asset = await _db.AudioAssets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == context.AudioAssetId, ct);
        return asset != null;
    }

    /// <inheritdoc/>
    public async Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var job = await _db.TranscriptionJobs
            .FirstOrDefaultAsync(j => j.Id == context.JobId, ct);
        if (job == null)
        {
            return NodeExecutionResult.Fail($"TranscriptionJob {context.JobId} not found.");
        }

        var audioAsset = await _db.AudioAssets
            .FirstOrDefaultAsync(a => a.Id == context.AudioAssetId, ct);
        if (audioAsset == null)
        {
            return NodeExecutionResult.Fail($"AudioAsset {context.AudioAssetId} not found.");
        }

        job.Status = TranscriptionJobStatuses.Running;
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            // ── Media preparation: normalize + VAD + segment ──
            var audioFilePath = !string.IsNullOrEmpty(audioAsset.NormalizedFilePath)
                ? audioAsset.NormalizedFilePath
                : audioAsset.OriginalFilePath;

            var prepResult = await _mediaPrep.PrepareAsync(audioFilePath, audioAsset.MimeType, ct);

            if (string.IsNullOrEmpty(audioAsset.NormalizedFilePath))
            {
                audioAsset.NormalizedFilePath = prepResult.NormalizedFilePath;
                audioAsset.SourceSha256 = prepResult.SourceSha256;
                audioAsset.DurationMs = prepResult.DurationMs;
                audioAsset.SampleRate = prepResult.SampleRate;
                audioAsset.Channels = prepResult.Channels;
                audioAsset.UpdatedAt = DateTime.UtcNow;
            }

            // ── Resolve ASR provider via the policy router ──
            var hotwords = DeserializeHotwords(job.Hotwords);

            var routingContext = new AsrRoutingContext
            {
                DataClassification = ParseDataClassification(audioAsset.DataClassification),
                PreferredProviderId = string.IsNullOrEmpty(job.ProviderId) ? null : job.ProviderId,
                PreferredModelId = string.IsNullOrEmpty(job.ModelId) ? null : job.ModelId,
                Language = job.Language,
                EnableVad = job.EnableVad,
                EnableSpeakerDiarization = job.EnableSpeakerDiarization,
                EnablePunctuation = job.EnablePunctuation,
                EnableHotwords = hotwords is { Count: > 0 },
                EnableWordTimestamp = false,
                FileSizeBytes = audioAsset.FileSizeBytes,
                DurationMs = prepResult.DurationMs,
                MimeType = audioAsset.MimeType,
                FallbackPolicy = job.FallbackPolicy,
                UserId = job.UserId,
                WorkspaceId = job.WorkspaceId,
            };

            var provider = await _router.ResolveAsrProviderAsync(routingContext, ct);
            var descriptor = await provider.GetDescriptorAsync(ct);

            job.ProviderId = descriptor.ProviderId;
            job.ModelId = descriptor.ModelId;
            job.ExecutionMode = routingContext.PreferredExecutionMode?.ToString()
                ?? descriptor.ExecutionModes.FirstOrDefault().ToString()
                ?? job.ExecutionMode;
            job.CredentialMode = routingContext.PreferredCredentialMode?.ToString()
                ?? descriptor.CredentialModes.FirstOrDefault().ToString()
                ?? job.CredentialMode;
            await _db.SaveChangesAsync(ct);

            // ── Audio cache dedup ──
            var transcriptionFilePath = prepResult.NormalizedFilePath;
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

            // ── Execute transcription ──
            var transcriptionRequest = new AsrTranscriptionRequest
            {
                AudioFilePath = transcriptionFilePath,
                AudioCacheKey = prepResult.CacheKey,
                MimeType = audioAsset.MimeType,
                FileSizeBytes = audioAsset.FileSizeBytes,
                DurationMs = prepResult.DurationMs,
                Language = job.Language,
                EnableVad = job.EnableVad,
                EnableSpeakerDiarization = job.EnableSpeakerDiarization,
                EnablePunctuation = job.EnablePunctuation,
                Hotwords = hotwords,
                DataClassification = ParseDataClassification(audioAsset.DataClassification),
                PreferredExecutionMode = routingContext.PreferredExecutionMode,
                PreferredCredentialMode = routingContext.PreferredCredentialMode,
                PreferredProviderId = descriptor.ProviderId,
                PreferredModelId = descriptor.ModelId,
                FallbackPolicy = job.FallbackPolicy,
                SegmentUuidPrefix = $"{job.Id:N}",
                UserId = job.UserId,
                WorkspaceId = job.WorkspaceId,
            };

            _logger.LogInformation(
                "Job {JobId}: ASR node executing with provider {ProviderId} (model: {ModelId})",
                job.Id, descriptor.ProviderId, descriptor.ModelId);

            var result = await provider.TranscribeAsync(transcriptionRequest, ct);

            // ── Persist RAW_MODEL segments ──
            var now = DateTime.UtcNow;
            var segments = new List<TranscriptionSegment>();

            for (int i = 0; i < result.Segments.Count; i++)
            {
                var seg = result.Segments[i];
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
                    ProviderId = result.ProviderId,
                    ModelId = result.ModelId,
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
            job.EstimatedCost = result.EstimatedCost;
            job.ActualCost = result.EstimatedCost;
            job.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            // Publish segments into the shared context for downstream nodes.
            context.Segments = segments;

            return NodeExecutionResult.Ok(
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["providerId"] = result.ProviderId,
                    ["modelId"] = result.ModelId,
                    ["segmentCount"] = segments.Count,
                    ["fullText"] = result.FullText,
                    ["language"] = result.Language ?? job.Language ?? string.Empty,
                },
                result.EstimatedCost ?? 0m);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Status = TranscriptionJobStatuses.Failed;
            job.ErrorMessage = Truncate(ex.Message, 2000);
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Job {JobId}: ASR node failed", job.Id);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }

    /// <summary>Deserializes a JSON hotwords array into a list, or null when empty.</summary>
    private static List<string>? DeserializeHotwords(string? hotwordsJson)
    {
        if (string.IsNullOrWhiteSpace(hotwordsJson))
        {
            return null;
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(hotwordsJson);
            return list is { Count: > 0 } ? list : null;
        }
        catch
        {
            return null;
        }
    }

    private static DataClassification ParseDataClassification(string value)
    {
        return (value ?? string.Empty).ToUpperInvariant() switch
        {
            "PUBLIC" => DataClassification.PUBLIC,
            "INTERNAL" => DataClassification.INTERNAL,
            "PRIVATE" => DataClassification.PRIVATE,
            "STRICT_LOCAL" => DataClassification.STRICT_LOCAL,
            _ => DataClassification.INTERNAL,
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
