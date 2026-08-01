using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Speaker diarization pipeline node.
/// <para>
/// NodeId = "diarization", DependsOn = ["asr"].
/// </para>
/// <para>
/// Resolves a provider that supports speaker diarization via
/// <see cref="IProviderRegistry"/> and delegates the audio asset to it.
/// The resulting speaker labels are merged into the existing
/// <see cref="TranscriptionSegment"/> records on the context and persisted
/// to the database. If no diarization-capable provider is available, the
/// node is skipped (returns false from <see cref="CanExecuteAsync"/>).
/// </para>
/// </summary>
public class DiarizationNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "diarization";

    /// <inheritdoc/>
    public string DisplayName => "Speaker Diarization";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "asr" };

    private readonly IProviderRegistry _providerRegistry;
    private readonly IAppDbContext _db;
    private readonly ILogger<DiarizationNode> _logger;

    public DiarizationNode(
        IProviderRegistry providerRegistry,
        IAppDbContext db,
        ILogger<DiarizationNode> logger)
    {
        _providerRegistry = providerRegistry;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        if (context.Segments is not { Count: > 0 })
            return false;

        // Check if a diarization-capable provider exists.
        var providers = await _providerRegistry.GetAsrProvidersAsync(ct);
        var hasDiarizationProvider = providers.Any(p =>
        {
            var descriptor = p.GetDescriptorAsync(ct).GetAwaiter().GetResult();
            return descriptor.SupportsDiarization;
        });

        return hasDiarizationProvider;
    }

    /// <inheritdoc/>
    public async Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var segments = context.Segments;
        if (segments is null || segments.Count == 0)
        {
            return NodeExecutionResult.Fail("No segments available for diarization.");
        }

        // Find a diarization-capable provider.
        var providers = await _providerRegistry.GetAsrProvidersAsync(ct);
        IAsrProvider? diarizationProvider = null;
        AsrProviderDescriptor? descriptor = null;

        foreach (var provider in providers)
        {
            var desc = await provider.GetDescriptorAsync(ct);
            if (desc.SupportsDiarization)
            {
                diarizationProvider = provider;
                descriptor = desc;
                break;
            }
        }

        if (diarizationProvider == null || descriptor == null)
        {
            _logger.LogInformation("Job {JobId}: no diarization-capable provider found, skipping", context.JobId);
            return NodeExecutionResult.Ok(); // Not a failure — diarization is optional.
        }

        // Load the audio asset and invoke the diarization-capable provider to
        // obtain real speaker labels, then merge them into the existing segments.
        try
        {
            _logger.LogInformation(
                "Job {JobId}: diarization node using provider {ProviderId} (model: {ModelId})",
                context.JobId, descriptor.ProviderId, descriptor.ModelId);

            // 1. Load the AudioAsset to get the normalized file path.
            var audioAsset = await _db.AudioAssets
                .FirstOrDefaultAsync(a => a.Id == context.AudioAssetId, ct);

            if (audioAsset == null)
            {
                return NodeExecutionResult.Fail(
                    $"AudioAsset {context.AudioAssetId} not found for diarization.");
            }

            var audioFilePath = !string.IsNullOrEmpty(audioAsset.NormalizedFilePath)
                ? audioAsset.NormalizedFilePath
                : audioAsset.OriginalFilePath;

            // 2. Build the diarization request.
            var diarizationRequest = new AsrTranscriptionRequest
            {
                AudioFilePath = audioFilePath,
                MimeType = audioAsset.MimeType,
                FileSizeBytes = audioAsset.FileSizeBytes,
                DurationMs = audioAsset.DurationMs,
                EnableSpeakerDiarization = true,
                EnableVad = true,
                EnablePunctuation = true,
                DataClassification = (audioAsset.DataClassification ?? string.Empty).ToUpperInvariant() switch
                {
                    "PUBLIC" => DataClassification.PUBLIC,
                    "INTERNAL" => DataClassification.INTERNAL,
                    "PRIVATE" => DataClassification.PRIVATE,
                    "STRICT_LOCAL" => DataClassification.STRICT_LOCAL,
                    _ => DataClassification.INTERNAL,
                },
                PreferredProviderId = descriptor.ProviderId,
                PreferredModelId = descriptor.ModelId,
                UserId = context.UserId,
                WorkspaceId = context.WorkspaceId,
            };

            // 3. Call the diarization provider.
            var diarizationResult = await diarizationProvider.TranscribeAsync(diarizationRequest, ct);

            // 4. Map speaker labels from the result segments to the existing
            //    context segments based on time overlap.
            static long GetOverlapMs(long startA, long endA, long startB, long endB)
            {
                var overlap = Math.Min(endA, endB) - Math.Max(startA, startB);
                return overlap > 0 ? overlap : 0;
            }

            var labeledSegments = diarizationResult.Segments
                .Where(s => !string.IsNullOrWhiteSpace(s.SpeakerKey))
                .ToList();

            int speakerCount;

            if (labeledSegments.Count > 0)
            {
                foreach (var segment in segments)
                {
                    var bestMatch = labeledSegments
                        .OrderByDescending(rs => GetOverlapMs(
                            segment.SourceStartMs, segment.SourceEndMs,
                            rs.StartMs, rs.EndMs))
                        .FirstOrDefault();

                    segment.SpeakerKey = bestMatch?.SpeakerKey;
                }

                speakerCount = segments
                    .Select(s => s.SpeakerKey)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }
            else
            {
                // Fallback: the provider returned no speaker labels — assign
                // sequential numbering so downstream nodes still have a key.
                speakerCount = 0;
                var speakerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var segment in segments.OrderBy(s => s.SegmentIndex))
                {
                    if (!speakerMap.TryGetValue(segment.SpeakerKey ?? string.Empty, out var speakerKey))
                    {
                        speakerCount++;
                        speakerKey = $"speaker_{speakerCount}";
                        speakerMap[segment.SpeakerKey ?? string.Empty] = speakerKey;
                    }
                    segment.SpeakerKey = speakerKey;
                }
            }

            // Persist speaker labels to database.
            var segmentIds = segments.Select(s => s.Id).ToHashSet();
            var dbSegments = await _db.TranscriptionSegments
                .Where(s => segmentIds.Contains(s.Id))
                .ToListAsync(ct);

            foreach (var dbSegment in dbSegments)
            {
                var matchingSegment = segments.First(s => s.Id == dbSegment.Id);
                dbSegment.SpeakerKey = matchingSegment.SpeakerKey;
            }

            await _db.SaveChangesAsync(ct);

            return NodeExecutionResult.Ok(
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["speakerCount"] = speakerCount,
                    ["providerId"] = descriptor.ProviderId,
                    ["modelId"] = descriptor.ModelId,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: diarization node failed", context.JobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }
}
