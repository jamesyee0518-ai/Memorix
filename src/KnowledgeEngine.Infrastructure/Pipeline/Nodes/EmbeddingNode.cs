using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Embedding generation pipeline node.
/// <para>
/// NodeId = "embedding", DependsOn = ["asr"].
/// </para>
/// <para>
/// Generates a vector embedding for each ASR segment via
/// <see cref="IEmbeddingService"/>, enabling semantic search and clustering of
/// transcription content. Embeddings are batched for efficiency and published to
/// the shared context as a list of <c>(segmentUuid, embedding)</c> pairs.
/// </para>
/// </summary>
public class EmbeddingNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "embedding";

    /// <inheritdoc/>
    public string DisplayName => "Segment Embeddings";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "asr" };

    /// <summary>Maximum number of texts to embed in a single batch request.</summary>
    private const int BatchSize = 64;

    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<EmbeddingNode> _logger;

    public EmbeddingNode(IEmbeddingService embeddingService, ILogger<EmbeddingNode> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        return Task.FromResult(context.Segments is { Count: > 0 });
    }

    /// <inheritdoc/>
    public async Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var segments = context.Segments;
        if (segments is null || segments.Count == 0)
        {
            return NodeExecutionResult.Fail("No segments available for embedding (ASR node produced no output).");
        }

        var ordered = segments.OrderBy(s => s.SegmentIndex).ToList();
        var texts = ordered.Select(s => s.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        if (texts.Count == 0)
        {
            return NodeExecutionResult.Fail("All segment texts are empty; cannot generate embeddings.");
        }

        try
        {
            _logger.LogInformation("Job {JobId}: embedding node generating embeddings for {SegmentCount} segments",
                context.JobId, ordered.Count);

            var allEmbeddings = new List<float[]>();

            for (int i = 0; i < texts.Count; i += BatchSize)
            {
                var batch = texts.Skip(i).Take(BatchSize).ToList();
                var batchVectors = await _embeddingService.EmbedBatchAsync(batch, ct);
                allEmbeddings.AddRange(batchVectors);
            }

            // Pair each embedding back to its segment UUID (texts already filtered to non-empty).
            var segmentUuids = ordered
                .Where(s => !string.IsNullOrWhiteSpace(s.Text))
                .Select(s => s.SegmentUuid)
                .ToList();

            var pairs = new List<SegmentEmbedding>();
            for (int i = 0; i < allEmbeddings.Count && i < segmentUuids.Count; i++)
            {
                pairs.Add(new SegmentEmbedding
                {
                    SegmentUuid = segmentUuids[i],
                    Vector = allEmbeddings[i],
                    Dimension = allEmbeddings[i].Length,
                });
            }

            return NodeExecutionResult.Ok(
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["embeddings"] = pairs,
                    ["embeddingCount"] = pairs.Count,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: embedding node failed", context.JobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }
}

/// <summary>
/// A segment UUID paired with its embedding vector.
/// </summary>
public class SegmentEmbedding
{
    public string SegmentUuid { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public int Dimension { get; set; }
}
