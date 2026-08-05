using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Ingests normalized agent event batches from external collection shims.
/// Converts canonical <see cref="NormalizedEvent"/>s into structured
/// <see cref="AgentMemoryTurn"/> + <see cref="AgentMemoryAction"/> records,
/// with idempotent, resumable ingestion via <see cref="IngestOffset"/>.
/// </summary>
public class IngestService
{
    private readonly IAppDbContext _db;
    private readonly ProjectResolver _projectResolver;
    private readonly ILogger<IngestService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public IngestService(
        IAppDbContext db,
        ProjectResolver projectResolver,
        ILogger<IngestService> logger)
    {
        _db = db;
        _projectResolver = projectResolver;
        _logger = logger;
    }

    /// <summary>
    /// Ingest a batch of normalized events. Idempotent: if a SourceCursor is
    /// provided and matches an existing IngestOffset with the same checksum,
    /// the batch is skipped.
    /// </summary>
    public async Task<IngestResult> IngestAsync(
        Guid userId,
        Guid workspaceId,
        Guid? agentProfileId,
        IngestEventBatch batch,
        CancellationToken ct = default)
    {
        var result = new IngestResult
        {
            SessionId = batch.SessionId
        };

        // ── 1. Idempotency check ──
        if (!string.IsNullOrWhiteSpace(batch.SourceCursor))
        {
            var existingOffset = await _db.IngestOffsets
                .FirstOrDefaultAsync(o => o.Source == batch.SourceCursor, ct);

            if (existingOffset != null && existingOffset.Checksum == batch.Checksum)
            {
                result.EventsSkipped = batch.Events.Count;
                result.Message = "Batch already ingested (checksum match).";
                _logger.LogDebug(
                    "Ingest skipped (duplicate): cursor={Cursor}, checksum={Checksum}",
                    batch.SourceCursor, batch.Checksum);
                return result;
            }
        }

        // ── 2. Resolve project identity ──
        Guid? projectId = null;
        if (!string.IsNullOrWhiteSpace(batch.RepoName))
        {
            try
            {
                var project = await _projectResolver.ResolveOrCreateAsync(
                    batch.GitRemote, batch.RepoName, null, ct);
                projectId = project.Id;
                result.ProjectId = projectId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Project resolution failed for repo={RepoName}, continuing without project",
                    batch.RepoName);
            }
        }

        // ── 3. Resolve or create session (idempotent by external key) ──
        var externalKey = $"{batch.Agent}:{batch.SessionId}";
        var session = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.ExternalSessionKey == externalKey, ct);

        var now = DateTime.UtcNow;
        if (session == null)
        {
            session = new AgentMemorySession
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                UserId = userId,
                AgentProfileId = agentProfileId,
                ExternalSessionKey = externalKey,
                TaskTitle = batch.TaskTitle ?? $"{batch.Agent} session",
                Status = "active",
                StartedAt = now,
                LastActiveAt = now,
                ProjectId = projectId
            };
            _db.AgentMemorySessions.Add(session);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Ingest created session {SessionId} for agent={Agent}, project={ProjectId}",
                session.Id, batch.Agent, projectId);
        }
        else
        {
            session.LastActiveAt = now;
            // Update project if it was missing
            session.ProjectId ??= projectId;
        }

        // ── 4. Aggregate events into turns + actions ──
        var currentTurnSeq = await _db.AgentMemoryTurns
            .Where(t => t.SessionId == session.Id)
            .Select(t => (int?)t.Seq)
            .MaxAsync(ct) ?? 0;

        var (turns, actions) = AggregateEvents(batch.Events, session.Id, ref currentTurnSeq);

        if (turns.Count > 0)
        {
            _db.AgentMemoryTurns.AddRange(turns);
        }
        if (actions.Count > 0)
        {
            _db.AgentMemoryActions.AddRange(actions);
        }

        // ── 5. Record ingest offset ──
        if (!string.IsNullOrWhiteSpace(batch.SourceCursor))
        {
            var offset = await _db.IngestOffsets
                .FirstOrDefaultAsync(o => o.Source == batch.SourceCursor, ct);

            var lastEvent = batch.Events.LastOrDefault();
            var offsetValue = lastEvent?.Timestamp ?? now.ToString("o");

            if (offset == null)
            {
                _db.IngestOffsets.Add(new IngestOffset
                {
                    Id = Guid.NewGuid(),
                    Source = batch.SourceCursor,
                    Offset = offsetValue,
                    Checksum = batch.Checksum,
                    IngestedAt = now
                });
            }
            else
            {
                offset.Offset = offsetValue;
                offset.Checksum = batch.Checksum;
                offset.IngestedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);

        result.TurnsCreated = turns.Count;
        result.ActionsCreated = actions.Count;
        result.SessionId = session.Id.ToString();

        _logger.LogInformation(
            "Ingest complete: agent={Agent}, session={SessionId}, turns={Turns}, actions={Actions}",
            batch.Agent, session.Id, turns.Count, actions.Count);

        return result;
    }

    /// <summary>
    /// Group normalized events into turns. A turn boundary is detected at each
    /// user_prompt event. Actions (post_tool/post_edit/post_command) attach to
    /// the current turn. post_response sets the assistant message.
    /// </summary>
    private (List<AgentMemoryTurn> turns, List<AgentMemoryAction> actions) AggregateEvents(
        List<NormalizedEvent> events,
        Guid sessionId,
        ref int currentSeq)
    {
        var turns = new List<AgentMemoryTurn>();
        var actions = new List<AgentMemoryAction>();
        var now = DateTime.UtcNow;

        AgentMemoryTurn? currentTurn = null;
        var currentTurnActions = new List<AgentMemoryAction>();

        foreach (var evt in events)
        {
            switch (evt.EventType)
            {
                case "session_start":
                    // No turn created; session lifecycle only
                    break;

                case "user_prompt":
                    // Close previous turn
                    if (currentTurn != null)
                    {
                        currentTurn.ActionsCount = currentTurnActions.Count;
                        currentTurn.Status = "completed";
                        turns.Add(currentTurn);
                        actions.AddRange(currentTurnActions);
                    }

                    // Start new turn
                    currentSeq++;
                    currentTurnActions = new List<AgentMemoryAction>();
                    currentTurn = new AgentMemoryTurn
                    {
                        Id = Guid.NewGuid(),
                        SessionId = sessionId,
                        Seq = currentSeq,
                        UserMessage = evt.UserPrompt,
                        CreatedAt = ParseTimestamp(evt.Timestamp, now)
                    };
                    break;

                case "post_response":
                    if (currentTurn != null && !string.IsNullOrEmpty(evt.AiResponse))
                    {
                        currentTurn.AssistantMessage = evt.AiResponse;
                        currentTurn.TokensTotal = evt.TokensTotal;
                    }
                    break;

                case "post_tool":
                case "post_edit":
                case "post_command":
                    if (currentTurn != null)
                    {
                        currentTurnActions.Add(new AgentMemoryAction
                        {
                            Id = Guid.NewGuid(),
                            TurnId = currentTurn.Id,
                            ActionKind = evt.EventType == "post_edit" ? "edit"
                                       : evt.EventType == "post_command" ? "command"
                                       : "tool",
                            ToolName = evt.ToolName,
                            ToolInputJson = evt.ToolInput != null
                                ? JsonSerializer.Serialize(evt.ToolInput, JsonOptions) : null,
                            ToolResult = evt.ToolResult,
                            FilePath = evt.FilePath,
                            Command = evt.Command,
                            Success = true, // shim should report this; default true
                            CreatedAt = ParseTimestamp(evt.Timestamp, now)
                        });
                    }
                    break;

                case "session_end":
                    // Close final turn
                    if (currentTurn != null)
                    {
                        currentTurn.ActionsCount = currentTurnActions.Count;
                        currentTurn.Status = "completed";
                        turns.Add(currentTurn);
                        actions.AddRange(currentTurnActions);
                        currentTurn = null;
                        currentTurnActions = new List<AgentMemoryAction>();
                    }
                    break;
            }
        }

        // Close dangling turn (no session_end received)
        if (currentTurn != null && !turns.Contains(currentTurn))
        {
            currentTurn.ActionsCount = currentTurnActions.Count;
            // Leave status "active" — the turn may still be in progress
            turns.Add(currentTurn);
            actions.AddRange(currentTurnActions);
        }

        return (turns, actions);
    }

    private static DateTime ParseTimestamp(string? timestamp, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(timestamp)) return fallback;
        return DateTime.TryParse(timestamp, out var dt) ? dt : fallback;
    }
}
