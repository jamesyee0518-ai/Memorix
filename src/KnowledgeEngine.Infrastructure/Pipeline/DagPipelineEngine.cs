using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline;

/// <summary>
/// Executes a directed acyclic graph (DAG) of <see cref="IPipelineNode"/> instances
/// against a shared <see cref="PipelineContext"/>.
/// <para>
/// Capabilities:
/// <list type="bullet">
///   <item><b>Topological scheduling</b> — nodes run only after all <c>DependsOn</c> predecessors complete; cycles are detected up front and rejected.</item>
///   <item><b>Parallelism</b> — independent nodes (no shared pending dependency) within the same "wave" run concurrently via <see cref="Task.WhenAll"/>.</item>
///   <item><b>Fault isolation</b> — a failed node does not block independent branches; only its direct dependents are skipped.</item>
///   <item><b>Idempotency</b> — nodes whose result already exists in <see cref="PipelineContext.Results"/> are skipped, enabling safe re-runs of a partially completed pipeline.</item>
///   <item><b>Per-node timeout</b> — each node runs under a linked cancellation token that fires after the configured timeout (default 5 minutes).</item>
/// </list>
/// </para>
/// </summary>
public class DagPipelineEngine
{
    /// <summary>Default per-node execution timeout.</summary>
    public static readonly TimeSpan DefaultNodeTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<DagPipelineEngine> _logger;
    private readonly TimeSpan _nodeTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="DagPipelineEngine"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="nodeTimeout">Per-node execution timeout. Defaults to 5 minutes when null.</param>
    public DagPipelineEngine(ILogger<DagPipelineEngine> logger, TimeSpan? nodeTimeout = null)
    {
        _logger = logger;
        _nodeTimeout = nodeTimeout ?? DefaultNodeTimeout;
    }

    /// <summary>
    /// Executes the supplied pipeline graph against the given context.
    /// </summary>
    /// <param name="nodes">The complete set of pipeline nodes forming the DAG.</param>
    /// <param name="context">The shared pipeline context.</param>
    /// <param name="ct">Caller cancellation token; cancels the entire pipeline.</param>
    /// <returns>An aggregate result describing which nodes executed, skipped, or failed.</returns>
    public async Task<DagExecutionResult> ExecuteAsync(
        List<IPipelineNode> nodes,
        PipelineContext context,
        CancellationToken ct)
    {
        var overallStart = DateTime.UtcNow;
        var result = new DagExecutionResult();

        if (nodes == null || nodes.Count == 0)
        {
            result.OverallSuccess = true;
            result.TotalDuration = DateTime.UtcNow - overallStart;
            return result;
        }

        // ── Build node lookup and validate references ──
        var nodeMap = new Dictionary<string, IPipelineNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (!nodeMap.TryAdd(node.NodeId, node))
            {
                throw new InvalidOperationException(
                    $"Duplicate pipeline node id '{node.NodeId}'. Node ids must be unique within a graph.");
            }
        }

        foreach (var node in nodes)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!nodeMap.ContainsKey(dep))
                {
                    throw new InvalidOperationException(
                        $"Pipeline node '{node.NodeId}' depends on unknown node '{dep}'.");
                }
            }
        }

        // ── Cycle detection via Kahn's algorithm (topological sort) ──
        if (TryDetectCycle(nodeMap, out var cyclePath))
        {
            _logger.LogError("Pipeline graph contains a cycle: {Cycle}", string.Join(" -> ", cyclePath));
            result.HasCycle = true;
            result.OverallSuccess = false;
            result.TotalDuration = DateTime.UtcNow - overallStart;
            return result;
        }

        // ── Execution state ──
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in nodeMap.Keys)
        {
            remaining.Add(id);
        }

        // ── Idempotency: skip nodes already executed for this context ──
        foreach (var id in nodeMap.Keys)
        {
            if (context.Results.TryGetValue(id, out var existing) && existing is NodeExecutionResult ner && ner.Success)
            {
                completed.Add(id);
                remaining.Remove(id);
                result.NodeResults[id] = ner;
                result.SkippedNodeIds.Add(id);
                _logger.LogInformation("Pipeline node '{NodeId}' skipped (idempotent — already executed).", id);
            }
        }

        // ── Wave-based parallel execution ──
        while (remaining.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            // Nodes whose every dependency completed successfully are ready to run.
            var ready = remaining
                .Where(id => nodeMap[id].DependsOn.All(dep => completed.Contains(dep)))
                .ToList();

            // Nodes with at least one failed dependency are blocked; cascade-skip them.
            var blocked = remaining
                .Where(id => nodeMap[id].DependsOn.Any(dep => failed.Contains(dep)))
                .ToList();

            foreach (var id in blocked)
            {
                var node = nodeMap[id];
                var failedDep = node.DependsOn.First(dep => failed.Contains(dep));
                var blockedResult = NodeExecutionResult.Fail(
                    $"Blocked by failed dependency '{failedDep}'.");
                result.NodeResults[id] = blockedResult;
                result.FailedNodeIds.Add(id);
                failed.Add(id);
                remaining.Remove(id);
                _logger.LogWarning(
                    "Pipeline node '{NodeId}' skipped — dependency '{Dep}' failed.", id, failedDep);
            }

            if (ready.Count == 0)
            {
                // No ready nodes and nothing blocked means a cycle slipped past detection
                // (should not happen) or all remaining nodes are unschedulable.
                if (remaining.Count > 0)
                {
                    _logger.LogError(
                        "Pipeline deadlock — no executable nodes among: {Nodes}",
                        string.Join(", ", remaining));
                    foreach (var id in remaining)
                    {
                        result.NodeResults[id] = NodeExecutionResult.Fail("Unschedulable (deadlock).");
                        result.FailedNodeIds.Add(id);
                    }
                }

                break;
            }

            // Execute all ready nodes concurrently.
            var waveTasks = ready.Select(id => ExecuteNodeAsync(nodeMap[id], context, ct));
            var waveResults = await Task.WhenAll(waveTasks);

            foreach (var (id, nodeResult) in waveResults)
            {
                result.NodeResults[id] = nodeResult;
                // Publish the result into the shared context so downstream nodes
                // (and idempotent re-runs) can consume it.
                context.Results[id] = nodeResult;
                remaining.Remove(id);

                if (nodeResult.Success)
                {
                    completed.Add(id);
                    result.ExecutedNodeIds.Add(id);
                    _logger.LogInformation(
                        "Pipeline node '{NodeId}' completed successfully (cost: {Cost}).", id, nodeResult.Cost);
                }
                else
                {
                    failed.Add(id);
                    result.FailedNodeIds.Add(id);
                    _logger.LogWarning(
                        "Pipeline node '{NodeId}' failed: {Error}", id, nodeResult.ErrorMessage);
                }
            }
        }

        result.OverallSuccess = result.FailedNodeIds.Count == 0 && !result.HasCycle;
        result.TotalDuration = DateTime.UtcNow - overallStart;
        result.TotalCost = result.NodeResults.Values.Sum(r => r.Cost);

        _logger.LogInformation(
            "Pipeline execution finished in {Duration}ms — executed: {Executed}, skipped: {Skipped}, failed: {Failed}",
            (int)result.TotalDuration.TotalMilliseconds,
            result.ExecutedNodeIds.Count,
            result.SkippedNodeIds.Count,
            result.FailedNodeIds.Count);

        return result;
    }

    /// <summary>
    /// Executes a single node with the <see cref="CanExecuteAsync"/> guard and the
    /// per-node timeout. All node exceptions are translated into a failed
    /// <see cref="NodeExecutionResult"/> so a single node failure never tears down
    /// the whole pipeline (except caller cancellation).
    /// </summary>
    private async Task<(string NodeId, NodeExecutionResult Result)> ExecuteNodeAsync(
        IPipelineNode node,
        PipelineContext context,
        CancellationToken ct)
    {
        try
        {
            // Pre-execution guard: a false result is a skip, not a failure.
            if (!await node.CanExecuteAsync(context, ct))
            {
                _logger.LogInformation(
                    "Pipeline node '{NodeId}' skipped — CanExecuteAsync returned false.", node.NodeId);
                return (node.NodeId, NodeExecutionResult.Fail("Skipped: CanExecuteAsync returned false."));
            }

            // Linked token: cancelled when the caller cancels OR the per-node timeout elapses.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_nodeTimeout);

            var nodeResult = await node.ExecuteAsync(context, timeoutCts.Token);
            return (node.NodeId, nodeResult);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancelled the entire pipeline — propagate, do not swallow.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Per-node timeout elapsed.
            _logger.LogWarning(ex, "Pipeline node '{NodeId}' timed out after {Timeout}.", node.NodeId, _nodeTimeout);
            return (node.NodeId, NodeExecutionResult.Fail($"Node timed out after {_nodeTimeout}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline node '{NodeId}' threw an unhandled exception.", node.NodeId);
            return (node.NodeId, NodeExecutionResult.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Detects whether the node graph contains a cycle using Kahn's algorithm.
    /// Returns true and populates <paramref name="cyclePath"/> with one example
    /// cycle when a cycle is found.
    /// </summary>
    private static bool TryDetectCycle(
        Dictionary<string, IPipelineNode> nodeMap,
        out List<string> cyclePath)
    {
        cyclePath = new List<string>();

        // In-degree per node (number of dependencies still pending).
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Adjacency list: dependency -> nodes that depend on it.
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in nodeMap.Keys)
        {
            inDegree[id] = 0;
            dependents[id] = new List<string>();
        }

        foreach (var (id, node) in nodeMap)
        {
            foreach (var dep in node.DependsOn)
            {
                inDegree[id] = inDegree.GetValueOrDefault(id) + 1;
                if (dependents.TryGetValue(dep, out var list))
                {
                    list.Add(id);
                }
            }
        }

        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
            {
                queue.Enqueue(id);
            }
        }

        var visited = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;
            foreach (var dependent in dependents[current])
            {
                inDegree[dependent] -= 1;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (visited == nodeMap.Count)
        {
            return false; // acyclic
        }

        // Cycle exists — collect the nodes still participating in the cycle.
        cyclePath = inDegree
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .ToList();
        return true;
    }
}
