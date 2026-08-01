using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Post-ASR correction pipeline node.
/// <para>
/// NodeId = "correction", DependsOn = ["asr"].
/// </para>
/// <para>
/// Consumes the <c>RAW_MODEL</c> segments produced by the ASR node, applies
/// dictionary-based corrections (brand names, terminology, homophones, user
/// entries) via <see cref="IPostAsrCorrectionService"/>, and creates new
/// <c>POST_PROCESSED</c> <see cref="TranscriptionSegment"/> records without
/// modifying the originals. Correction failures are non-fatal: the node still
/// reports success because the upstream RAW_MODEL segments remain valid.
/// </para>
/// </summary>
public class CorrectionNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "correction";

    /// <inheritdoc/>
    public string DisplayName => "Post-ASR Correction";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "asr" };

    private readonly IAppDbContext _db;
    private readonly IPostAsrCorrectionService _correctionService;
    private readonly ILogger<CorrectionNode> _logger;

    public CorrectionNode(
        IAppDbContext db,
        IPostAsrCorrectionService correctionService,
        ILogger<CorrectionNode> logger)
    {
        _db = db;
        _correctionService = correctionService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // Requires the ASR node to have produced segments.
        return Task.FromResult(context.Segments is { Count: > 0 });
    }

    /// <inheritdoc/>
    public async Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var segments = context.Segments;
        if (segments is null || segments.Count == 0)
        {
            return NodeExecutionResult.Fail("No segments available for correction (ASR node produced no output).");
        }

        var job = await _db.TranscriptionJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == context.JobId, ct);

        var workspaceId = job?.WorkspaceId ?? context.WorkspaceId;
        var language = job?.Language;
        var jobId = job?.Id ?? context.JobId;

        try
        {
            var fullText = string.Join("\n", segments.Select(s => s.Text));

            // Run correction on the full transcript first for statistics / early exit.
            var fullRequest = new CorrectionRequest
            {
                Text = fullText,
                WorkspaceId = workspaceId,
                Language = language,
                SegmentUuids = segments.Select(s => s.SegmentUuid).ToList(),
                Context = null,
            };
            var fullResult = await _correctionService.CorrectAsync(fullRequest, ct);

            // No dictionary matches -> nothing to correct; succeed without creating duplicates.
            if (fullResult.AppliedDictionaryEntries == 0)
            {
                _logger.LogInformation(
                    "Job {JobId}: correction node found no dictionary matches, skipping POST_PROCESSED segments",
                    jobId);
                return NodeExecutionResult.Ok(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["correctedCount"] = 0,
                    ["appliedEntries"] = 0,
                });
            }

            // Correct each segment individually for per-segment corrected text.
            var now = DateTime.UtcNow;
            var correctedSegments = new List<TranscriptionSegment>();

            foreach (var seg in segments)
            {
                var segRequest = new CorrectionRequest
                {
                    Text = seg.Text,
                    WorkspaceId = workspaceId,
                    Language = language,
                    SegmentUuids = new List<string> { seg.SegmentUuid },
                    Context = fullText,
                };
                var segResult = await _correctionService.CorrectAsync(segRequest, ct);

                correctedSegments.Add(new TranscriptionSegment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionJobId = jobId,
                    WorkspaceId = workspaceId,
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

            _db.TranscriptionSegments.AddRange(correctedSegments);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Job {JobId}: correction node applied {ChangeCount} changes from {EntryCount} entries",
                jobId, fullResult.Changes.Count, fullResult.AppliedDictionaryEntries);

            return NodeExecutionResult.Ok(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["correctedCount"] = correctedSegments.Count,
                ["appliedEntries"] = fullResult.AppliedDictionaryEntries,
                ["changeCount"] = fullResult.Changes.Count,
            });
        }
        catch (Exception ex)
        {
            // Correction is non-fatal; RAW_MODEL segments remain usable.
            _logger.LogWarning(ex, "Job {JobId}: correction node failed, continuing with RAW_MODEL segments", jobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }
}
