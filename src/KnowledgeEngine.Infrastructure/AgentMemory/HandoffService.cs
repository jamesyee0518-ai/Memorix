using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Implements <see cref="IHandoffService"/> — point-to-point task handoffs between
/// coding agents. Each method checks permissions via <see cref="IAgentPermissionGuard"/>
/// and records an access log entry for auditability.
/// </summary>
public class HandoffService : IHandoffService
{
    private readonly IAppDbContext _db;
    private readonly IAgentPermissionGuard _permissionGuard;
    private readonly ILogger<HandoffService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public HandoffService(
        IAppDbContext db,
        IAgentPermissionGuard permissionGuard,
        ILogger<HandoffService> logger)
    {
        _db = db;
        _permissionGuard = permissionGuard;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandoffDto> CreateHandoffAsync(
        Guid userId,
        Guid? agentProfileId,
        CreateHandoffInput input,
        CancellationToken ct = default)
    {
        // Resolve originator session + project + agent type
        var fromSession = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == input.FromSessionId, ct);

        if (fromSession == null)
        {
            throw new InvalidOperationException(
                $"Cannot create handoff: session {input.FromSessionId} not found.");
        }

        // Permission: originator must have memory write
        var canWrite = await _permissionGuard.CanWriteMemoryAsync(
            userId, agentProfileId, fromSession.WorkspaceId, ct);
        if (!canWrite)
        {
            throw new UnauthorizedAccessException(
                "Agent does not have permission to create handoffs (memory write required).");
        }

        // Resolve originator's AgentType
        var fromAgent = await ResolveAgentTypeAsync(agentProfileId, ct);

        var now = DateTime.UtcNow;
        var handoff = new AgentMemoryHandoff
        {
            Id = Guid.NewGuid(),
            ProjectId = fromSession.ProjectId,
            FromSessionId = input.FromSessionId,
            FromAgent = fromAgent,
            ToAgent = input.ToAgent,
            Task = input.Task,
            Status = "open",
            ContextRefsJson = input.ContextRefs != null && input.ContextRefs.Count > 0
                ? JsonSerializer.Serialize(input.ContextRefs)
                : null,
            GitBranch = input.GitBranch,
            CommitSha = input.CommitSha,
            CreatedAt = now
        };

        _db.AgentMemoryHandoffs.Add(handoff);
        await _db.SaveChangesAsync(ct);

        await RecordAccessLogAsync(handoff.Id, agentProfileId, "write", ct);

        _logger.LogInformation(
            "Handoff created: {HandoffId}, from={FromAgent}, to={ToAgent}, project={ProjectId}, task='{Task}'",
            handoff.Id, fromAgent, input.ToAgent ?? "(broadcast)", handoff.ProjectId, input.Task);

        return MapToDto(handoff);
    }

    /// <inheritdoc/>
    public async Task<List<HandoffDto>> GetHandoffsAsync(
        Guid userId,
        Guid? agentProfileId,
        GetHandoffsInput input,
        CancellationToken ct = default)
    {
        // Resolve caller's AgentType and project for filtering
        var callerAgentType = await ResolveAgentTypeAsync(agentProfileId, ct);
        var status = string.IsNullOrWhiteSpace(input.Status) ? "open" : input.Status;

        // Resolve project: explicit filter, else from any session of this agent
        Guid? projectId = input.ProjectId;

        var query = _db.AgentMemoryHandoffs.AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(h => h.ProjectId == projectId.Value);
        }

        query = query.Where(h => h.Status == status);

        // Point-to-point matching: handoff targets this agent OR is a broadcast (ToAgent == null)
        var toAgentFilter = string.IsNullOrWhiteSpace(input.ToAgent) ? callerAgentType : input.ToAgent;
        query = query.Where(h => h.ToAgent == null || h.ToAgent == toAgentFilter);

        var handoffs = await query
            .OrderByDescending(h => h.CreatedAt)
            .Take(input.Limit > 0 ? input.Limit : 20)
            .ToListAsync(ct);

        await RecordAccessLogAsync(null, agentProfileId, "read", ct);

        return handoffs.Select(MapToDto).ToList();
    }

    /// <inheritdoc/>
    public async Task<HandoffDto> AcceptHandoffAsync(
        Guid userId,
        Guid? agentProfileId,
        Guid handoffId,
        Guid toSessionId,
        CancellationToken ct = default)
    {
        var handoff = await _db.AgentMemoryHandoffs
            .FirstOrDefaultAsync(h => h.Id == handoffId, ct);

        if (handoff == null)
        {
            throw new InvalidOperationException($"Handoff {handoffId} not found.");
        }

        // Permission: receiver must have memory read (they'll be reading context)
        var toSession = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == toSessionId, ct);
        if (toSession == null)
        {
            throw new InvalidOperationException($"Target session {toSessionId} not found.");
        }

        var canRead = await _permissionGuard.CanReadMemoryAsync(
            userId, agentProfileId, toSession.WorkspaceId, ct);
        if (!canRead)
        {
            throw new UnauthorizedAccessException(
                "Agent does not have permission to accept handoffs (memory read required).");
        }

        // Point-to-point: verify caller is the intended recipient
        var callerAgentType = await ResolveAgentTypeAsync(agentProfileId, ct);
        if (!string.IsNullOrEmpty(handoff.ToAgent) && handoff.ToAgent != callerAgentType)
        {
            throw new UnauthorizedAccessException(
                $"This handoff is addressed to '{handoff.ToAgent}', not '{callerAgentType}'.");
        }

        handoff.Accept(toSessionId);
        await _db.SaveChangesAsync(ct);

        await RecordAccessLogAsync(handoff.Id, agentProfileId, "write", ct);

        _logger.LogInformation(
            "Handoff {HandoffId} accepted by {AgentType} (session {ToSessionId})",
            handoffId, callerAgentType, toSessionId);

        return MapToDto(handoff);
    }

    /// <inheritdoc/>
    public async Task<HandoffDto> CompleteHandoffAsync(
        Guid userId,
        Guid? agentProfileId,
        Guid handoffId,
        string? resultSummary,
        CancellationToken ct = default)
    {
        var handoff = await _db.AgentMemoryHandoffs
            .FirstOrDefaultAsync(h => h.Id == handoffId, ct);

        if (handoff == null)
        {
            throw new InvalidOperationException($"Handoff {handoffId} not found.");
        }

        // Resolve the accepting session's workspace for permission check
        Guid workspaceId = Guid.Empty;
        if (handoff.ToSessionId.HasValue)
        {
            var toSession = await _db.AgentMemorySessions
                .FirstOrDefaultAsync(s => s.Id == handoff.ToSessionId.Value, ct);
            workspaceId = toSession?.WorkspaceId ?? Guid.Empty;
        }

        var canWrite = await _permissionGuard.CanWriteMemoryAsync(
            userId, agentProfileId, workspaceId, ct);
        if (!canWrite)
        {
            throw new UnauthorizedAccessException(
                "Agent does not have permission to complete handoffs (memory write required).");
        }

        handoff.Complete(resultSummary);
        await _db.SaveChangesAsync(ct);

        await RecordAccessLogAsync(handoff.Id, agentProfileId, "write", ct);

        _logger.LogInformation(
            "Handoff {HandoffId} completed by {AgentType}",
            handoffId, await ResolveAgentTypeAsync(agentProfileId, ct));

        return MapToDto(handoff);
    }

    // ===== Private helpers =====

    /// <summary>
    /// Resolve the AgentType from the profile. Falls back to "unknown" if no profile.
    /// </summary>
    private async Task<string> ResolveAgentTypeAsync(Guid? agentProfileId, CancellationToken ct)
    {
        if (!agentProfileId.HasValue) return "unknown";

        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(a => a.Id == agentProfileId.Value, ct);

        return profile?.AgentType ?? "unknown";
    }

    private async Task RecordAccessLogAsync(
        Guid? handoffId,
        Guid? agentProfileId,
        string action,
        CancellationToken ct)
    {
        _db.AgentMemoryAccessLogs.Add(new AgentMemoryAccessLog
        {
            Id = Guid.NewGuid(),
            // We record handoff operations against the access log. The MemoryItemId
            // column is repurposed here to carry the handoff id for traceability.
            MemoryItemId = handoffId,
            SessionId = null,
            AgentProfileId = agentProfileId,
            Action = $"handoff:{action}",
            TraceId = System.Diagnostics.Activity.Current?.Id,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private static HandoffDto MapToDto(AgentMemoryHandoff h)
    {
        List<string>? contextRefs = null;
        if (!string.IsNullOrWhiteSpace(h.ContextRefsJson))
        {
            try
            {
                contextRefs = JsonSerializer.Deserialize<List<string>>(h.ContextRefsJson);
            }
            catch { /* leave null on parse error */ }
        }

        return new HandoffDto
        {
            Id = h.Id,
            ProjectId = h.ProjectId,
            FromSessionId = h.FromSessionId,
            ToSessionId = h.ToSessionId,
            FromAgent = h.FromAgent,
            ToAgent = h.ToAgent,
            Task = h.Task,
            Status = h.Status,
            ContextRefs = contextRefs,
            GitBranch = h.GitBranch,
            CommitSha = h.CommitSha,
            ResultSummary = h.ResultSummary,
            CreatedAt = h.CreatedAt,
            AcceptedAt = h.AcceptedAt,
            CompletedAt = h.CompletedAt
        };
    }
}
