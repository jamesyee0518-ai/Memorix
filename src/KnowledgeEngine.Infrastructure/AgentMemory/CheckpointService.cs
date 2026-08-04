using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Manages session checkpoints for context preservation and handoff.
///
/// A checkpoint captures a snapshot of the session's active memory items,
/// including goals, completed items, key decisions, open issues, evidence,
/// and next steps. Checkpoints enable session recovery and agent handoff.
/// </summary>
public class CheckpointService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<CheckpointService> _logger;

    // Rough token estimation: ~4 characters per token
    private const int CharsPerToken = 4;

    public CheckpointService(IAppDbContext db, ILogger<CheckpointService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Creates a checkpoint for the given session by summarizing all active memory items.
    /// The checkpoint is saved with DeliveryState = "pending".
    /// </summary>
    /// <param name="sessionId">The session ID to checkpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created checkpoint.</returns>
    public async Task<AgentMemoryCheckpoint> CreateCheckpointAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var session = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            _logger.LogWarning("CreateCheckpoint: session {SessionId} not found", sessionId);
            throw new InvalidOperationException($"Session {sessionId} not found.");
        }

        // Load all active memory items for the session
        var items = await _db.AgentMemoryItems
            .Where(i => i.SessionId == sessionId && i.Status == MemoryStatus.Active)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);

        // Load evidence for these items
        var itemIds = items.Select(i => i.Id).ToList();
        var evidences = await _db.AgentMemoryEvidences
            .Where(e => itemIds.Contains(e.MemoryItemId))
            .ToListAsync(ct);

        // Extract summary components
        var summary = BuildSummary(items, evidences);
        var openLoops = ExtractOpenLoops(items);
        var decisions = ExtractDecisions(items);

        // Calculate sequence range based on item count
        var fromSequence = items.Count > 0 ? 1 : 0;
        var toSequence = items.Count;

        // Estimate tokens from summary + JSON payloads
        var tokenEstimate = EstimateTokens(summary, openLoops, decisions);

        var checkpoint = new AgentMemoryCheckpoint
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            FromSequence = fromSequence,
            ToSequence = toSequence,
            Summary = summary,
            OpenLoopsJson = openLoops.Count > 0 ? JsonSerializer.Serialize(openLoops) : null,
            DecisionsJson = decisions.Count > 0 ? JsonSerializer.Serialize(decisions) : null,
            TokenEstimate = tokenEstimate,
            DeliveryState = "pending",
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };

        _db.AgentMemoryCheckpoints.Add(checkpoint);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Checkpoint created for session {SessionId}: items={ItemCount}, tokens={TokenEstimate}, checkpointId={CheckpointId}",
            sessionId, items.Count, tokenEstimate, checkpoint.Id);

        return checkpoint;
    }

    /// <summary>
    /// Gets the latest delivered checkpoint for a session.
    /// </summary>
    public async Task<AgentMemoryCheckpoint?> GetLatestDeliveredCheckpointAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var checkpoint = await _db.AgentMemoryCheckpoints
            .Where(c => c.SessionId == sessionId && c.DeliveryState == "delivered")
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (checkpoint == null)
        {
            _logger.LogDebug("No delivered checkpoint found for session {SessionId}", sessionId);
        }

        return checkpoint;
    }

    /// <summary>
    /// Marks a checkpoint as delivered.
    /// </summary>
    public async Task MarkDeliveredAsync(Guid checkpointId, CancellationToken ct = default)
    {
        var checkpoint = await _db.AgentMemoryCheckpoints
            .FirstOrDefaultAsync(c => c.Id == checkpointId, ct);

        if (checkpoint == null)
        {
            _logger.LogWarning("MarkDelivered: checkpoint {CheckpointId} not found", checkpointId);
            return;
        }

        checkpoint.DeliveryState = "delivered";
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Checkpoint {CheckpointId} marked as delivered for session {SessionId}",
            checkpointId, checkpoint.SessionId);
    }

    /// <summary>
    /// Restores context from the latest delivered checkpoint for a session.
    /// Returns the checkpoint for context restoration (does not modify memory items).
    /// </summary>
    public async Task<AgentMemoryCheckpoint?> RestoreFromCheckpointAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var checkpoint = await GetLatestDeliveredCheckpointAsync(sessionId, ct);

        if (checkpoint == null)
        {
            _logger.LogWarning(
                "RestoreFromCheckpoint: no delivered checkpoint found for session {SessionId}",
                sessionId);
            return null;
        }

        _logger.LogInformation(
            "Restoring from checkpoint {CheckpointId} for session {SessionId}",
            checkpoint.Id, sessionId);

        return checkpoint;
    }

    /// <summary>
    /// Lists all checkpoints for a session, ordered by creation date (newest first).
    /// </summary>
    public async Task<List<AgentMemoryCheckpoint>> ListCheckpointsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        return await _db.AgentMemoryCheckpoints
            .Where(c => c.SessionId == sessionId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    // ===== Private helpers =====

    /// <summary>
    /// Builds a human-readable summary from the session's memory items.
    /// </summary>
    private static string BuildSummary(List<AgentMemoryItem> items, List<AgentMemoryEvidence> evidences)
    {
        var goals = items.Where(i => i.Kind == MemoryKind.TaskState || i.Kind == MemoryKind.Todo).ToList();
        var completed = items.Where(i => i.Kind == MemoryKind.Todo && i.AdmissionState == AdmissionState.Confirmed).ToList();
        var keyDecisions = items.Where(i => i.Kind == MemoryKind.Decision).ToList();
        var openIssues = items.Where(i => i.Kind == MemoryKind.Blocker).ToList();
        var lessons = items.Where(i => i.Kind == MemoryKind.Lesson).ToList();
        var evidenceItems = items.Where(i => evidences.Any(e => e.MemoryItemId == i.Id)).ToList();

        var sb = new System.Text.StringBuilder();

        if (goals.Count > 0)
        {
            sb.AppendLine("## Goals & Task State");
            foreach (var g in goals)
                sb.AppendLine($"- {g.Title}: {g.Content}");
            sb.AppendLine();
        }

        if (keyDecisions.Count > 0)
        {
            sb.AppendLine("## Key Decisions");
            foreach (var d in keyDecisions)
                sb.AppendLine($"- {d.Title}: {d.Content}");
            sb.AppendLine();
        }

        if (openIssues.Count > 0)
        {
            sb.AppendLine("## Open Issues / Blockers");
            foreach (var b in openIssues)
                sb.AppendLine($"- {b.Title}: {b.Content}");
            sb.AppendLine();
        }

        if (completed.Count > 0)
        {
            sb.AppendLine("## Completed Items");
            foreach (var c in completed)
                sb.AppendLine($"- {c.Title}");
            sb.AppendLine();
        }

        if (evidenceItems.Count > 0)
        {
            sb.AppendLine("## Evidence Summary");
            foreach (var ei in evidenceItems)
            {
                var ev = evidences.Where(e => e.MemoryItemId == ei.Id).ToList();
                sb.AppendLine($"- {ei.Title}: {ev.Count} evidence(s)");
            }
            sb.AppendLine();
        }

        if (lessons.Count > 0)
        {
            sb.AppendLine("## Lessons Learned");
            foreach (var l in lessons)
                sb.AppendLine($"- {l.Title}: {l.Content}");
            sb.AppendLine();
        }

        // Next steps: infer from open todos and blockers
        var nextSteps = items
            .Where(i => (i.Kind == MemoryKind.Todo || i.Kind == MemoryKind.Blocker)
                        && i.AdmissionState != AdmissionState.Rejected)
            .ToList();
        if (nextSteps.Count > 0)
        {
            sb.AppendLine("## Next Steps");
            foreach (var ns in nextSteps)
                sb.AppendLine($"- {ns.Title}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts open loops (uncompleted todos and blockers) as JSON-serializable objects.
    /// </summary>
    private static List<object> ExtractOpenLoops(List<AgentMemoryItem> items)
    {
        return items
            .Where(i => (i.Kind == MemoryKind.Todo || i.Kind == MemoryKind.Blocker)
                        && i.AdmissionState != AdmissionState.Rejected)
            .Select(i => new
            {
                id = i.Id,
                title = i.Title,
                kind = i.Kind.ToString().ToLowerInvariant(),
                state = i.AdmissionState.ToString().ToLowerInvariant()
            })
            .Cast<object>()
            .ToList();
    }

    /// <summary>
    /// Extracts decisions as JSON-serializable objects.
    /// </summary>
    private static List<object> ExtractDecisions(List<AgentMemoryItem> items)
    {
        return items
            .Where(i => i.Kind == MemoryKind.Decision || i.Kind == MemoryKind.Rationale)
            .Select(i => new
            {
                id = i.Id,
                title = i.Title,
                content = i.Content,
                state = i.AdmissionState.ToString().ToLowerInvariant(),
                confidence = i.Confidence
            })
            .Cast<object>()
            .ToList();
    }

    /// <summary>
    /// Estimates token count from the summary text and JSON payloads.
    /// </summary>
    private static int EstimateTokens(string summary, List<object> openLoops, List<object> decisions)
    {
        var summaryTokens = summary.Length / CharsPerToken;
        var openLoopsTokens = openLoops.Count > 0
            ? JsonSerializer.Serialize(openLoops).Length / CharsPerToken
            : 0;
        var decisionsTokens = decisions.Count > 0
            ? JsonSerializer.Serialize(decisions).Length / CharsPerToken
            : 0;

        return Math.Max(1, summaryTokens + openLoopsTokens + decisionsTokens);
    }
}
