using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Contract for a single node in the transcription DAG pipeline.
/// Each node declares its identifier, human-readable name, and the list of
/// predecessor node IDs it depends on. The DAG engine uses <see cref="DependsOn"/>
/// to perform a topological sort and to schedule independent nodes in parallel.
/// </summary>
public interface IPipelineNode
{
    /// <summary>
    /// Stable, unique identifier for this node within a pipeline graph
    /// (e.g. "asr", "correction", "summary").
    /// </summary>
    string NodeId { get; }

    /// <summary>
    /// Human-readable display name used for logging and diagnostics.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// List of predecessor node IDs that must complete successfully before
    /// this node can execute. An empty list marks the node as a root node.
    /// </summary>
    List<string> DependsOn { get; }

    /// <summary>
    /// Executes the node's work against the shared pipeline context.
    /// The engine guarantees that all <see cref="DependsOn"/> nodes have
    /// completed successfully before invoking this method.
    /// </summary>
    /// <param name="context">The shared pipeline context carrying job state and prior results.</param>
    /// <param name="ct">Cancellation token; cancelled when the per-node timeout elapses or the caller cancels.</param>
    /// <returns>The execution result including output data, success flag, and cost.</returns>
    Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct);

    /// <summary>
    /// Pre-execution guard. Returns false if the node cannot currently run
    /// (e.g. missing prerequisites in context). When false, the node is
    /// recorded as skipped rather than failed, and dependent branches are
    /// not blocked.
    /// </summary>
    Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct);
}

/// <summary>
/// Shared, mutable state carried through the DAG pipeline. All nodes read
/// from and write to a single instance, enabling downstream nodes to consume
/// upstream outputs via the <see cref="Results"/> dictionary.
/// </summary>
public class PipelineContext
{
    /// <summary>The transcription job identifier this pipeline run belongs to.</summary>
    public Guid JobId { get; set; }

    /// <summary>The source audio asset being transcribed.</summary>
    public Guid AudioAssetId { get; set; }

    /// <summary>The workspace scope, if any.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>The user that initiated the pipeline run.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The transcription segments produced by the ASR node and consumed by
    /// downstream nodes (correction, summary, entity, embedding).
    /// </summary>
    public List<TranscriptionSegment> Segments { get; set; } = new();

    /// <summary>
    /// Cross-node result store keyed by node ID. The DAG engine stores each
    /// node's <see cref="NodeExecutionResult"/> here under its <c>NodeId</c>,
    /// which also powers idempotency: a node whose result already exists is
    /// skipped on re-runs of the same context.
    /// </summary>
    public Dictionary<string, object> Results { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Free-form metadata bag for passing auxiliary information (language,
    /// provider hints, hotwords, etc.) between nodes.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Outcome of executing a single pipeline node.
/// </summary>
public class NodeExecutionResult
{
    /// <summary>Whether the node completed its work successfully.</summary>
    public bool Success { get; set; }

    /// <summary>
    /// Output payload produced by the node (e.g. corrected segments, summary
    /// text, extracted entities, embedding vectors). Consumed by downstream
    /// nodes via <see cref="PipelineContext.Results"/>.
    /// </summary>
    public Dictionary<string, object> OutputData { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Error message describing why the node failed, if <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Estimated cost incurred by this node's provider calls.</summary>
    public decimal Cost { get; set; }

    public static NodeExecutionResult Ok() => new() { Success = true };

    public static NodeExecutionResult Ok(Dictionary<string, object> output, decimal cost = 0m) =>
        new() { Success = true, OutputData = output, Cost = cost };

    public static NodeExecutionResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// Aggregate result of a full DAG pipeline execution.
/// </summary>
public class DagExecutionResult
{
    /// <summary>
    /// True only if every scheduled node completed successfully (nodes skipped
    /// via <c>CanExecuteAsync</c> or idempotency do not count as failures).
    /// </summary>
    public bool OverallSuccess { get; set; }

    /// <summary>Per-node results keyed by node ID.</summary>
    public Dictionary<string, NodeExecutionResult> NodeResults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Node IDs that were executed in this run.</summary>
    public List<string> ExecutedNodeIds { get; set; } = new();

    /// <summary>Node IDs that were skipped (idempotency or CanExecute guard).</summary>
    public List<string> SkippedNodeIds { get; set; } = new();

    /// <summary>Node IDs that failed or were blocked by a failed dependency.</summary>
    public List<string> FailedNodeIds { get; set; } = new();

    /// <summary>Total wall-clock duration of the pipeline run.</summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>Sum of all node costs.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>True if the dependency graph contained a cycle.</summary>
    public bool HasCycle { get; set; }
}
