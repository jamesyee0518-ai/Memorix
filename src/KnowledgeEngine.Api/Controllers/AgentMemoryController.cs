using System.Security.Claims;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.AgentMemory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Exposes Agent Memory endpoints for creating sessions, capturing memory
/// candidates, searching memories, and retrieving assembled context packs.
/// All endpoints require JWT authentication. The userId is resolved from the
/// <c>user_id</c> / <c>ClaimTypes.NameIdentifier</c> claim and the workspaceId
/// from the <c>workspace_id</c> claim.
/// </summary>
[ApiController]
[Authorize]
[Route("api/agent-memory")]
public class AgentMemoryController : BaseController
{
    private readonly IAgentMemoryService _memoryService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAppDbContext _db;
    private readonly MemoryAdmissionService _admissionService;
    private readonly CheckpointService _checkpointService;
    private readonly RetentionService _retentionService;
    private readonly MemoryMetricsService _metricsService;

    public AgentMemoryController(
        IAgentMemoryService memoryService,
        ICurrentUserContext currentUser,
        IAppDbContext db,
        MemoryAdmissionService admissionService,
        CheckpointService checkpointService,
        RetentionService retentionService,
        MemoryMetricsService metricsService)
    {
        _memoryService = memoryService;
        _currentUser = currentUser;
        _db = db;
        _admissionService = admissionService;
        _checkpointService = checkpointService;
        _retentionService = retentionService;
        _metricsService = metricsService;
    }

    // ===== POST /api/agent-memory/sessions =====

    /// <summary>
    /// Create (or resume) an agent memory session.
    /// </summary>
    [HttpPost("sessions")]
    [ProducesResponseType(typeof(ApiResponse<SessionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> StartSession(
        [FromBody] StartSessionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var ids = TryGetUserIdAndWorkspaceId();
        if (ids == null) return Unauthorized();

        var (userId, workspaceId) = ids.Value;

        var session = await _memoryService.StartSessionAsync(
            userId,
            workspaceId,
            request.AgentProfileId,
            request.ExternalSessionKey,
            request.TaskTitle,
            request.TopicId,
            ct);

        return StatusCode(201, ApiResponse<SessionDto>.Ok(session, GetTraceId()));
    }

    // ===== GET /api/agent-memory/sessions =====

    /// <summary>
    /// List sessions for the current user within the current workspace.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ApiResponse<List<SessionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListSessions(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var ids = TryGetUserIdAndWorkspaceId();
        if (ids == null) return Unauthorized();

        var (userId, workspaceId) = ids.Value;

        var sessions = await _memoryService.ListSessionsAsync(userId, workspaceId, limit, offset, ct);
        return Ok(ApiResponse<List<SessionDto>>.Ok(sessions, GetTraceId()));
    }

    // ===== GET /api/agent-memory/sessions/{id} =====

    /// <summary>
    /// Get session details by ID.
    /// </summary>
    [HttpGet("sessions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSession([FromRoute] Guid id, CancellationToken ct)
    {
        var session = await _memoryService.GetSessionAsync(id, ct);
        if (session == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Session not found", GetTraceId()));
        }

        return Ok(ApiResponse<SessionDto>.Ok(session, GetTraceId()));
    }

    // ===== POST /api/agent-memory/sessions/{id}/close =====

    /// <summary>
    /// Close an active session.
    /// </summary>
    [HttpPost("sessions/{id:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CloseSession(
        [FromRoute] Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        await _memoryService.CloseSessionAsync(id, ct);
        return NoContent();
    }

    // ===== POST /api/agent-memory/items =====

    /// <summary>
    /// Submit a memory candidate for admission processing.
    /// Supports idempotent submissions via the Idempotency-Key header.
    /// </summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(ApiResponse<MemoryItemDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CaptureMemory(
        [FromBody] CaptureMemoryInput input,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var ids = TryGetUserIdAndWorkspaceId();
        if (ids == null) return Unauthorized();

        var (userId, workspaceId) = ids.Value;

        var item = await _memoryService.CaptureMemoryAsync(userId, workspaceId, input, ct);
        return StatusCode(201, ApiResponse<MemoryItemDto>.Ok(item, GetTraceId()));
    }

    // ===== GET /api/agent-memory/items/{id} =====

    /// <summary>
    /// Get a memory item by ID.
    /// </summary>
    [HttpGet("items/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<MemoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMemoryItem([FromRoute] Guid id, CancellationToken ct)
    {
        var item = await _memoryService.GetMemoryItemAsync(id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Memory item not found", GetTraceId()));
        }

        return Ok(ApiResponse<MemoryItemDto>.Ok(item, GetTraceId()));
    }

    // ===== POST /api/agent-memory/search =====

    /// <summary>
    /// Search the agent memory store for items matching the query.
    /// </summary>
    [HttpPost("search")]
    [ProducesResponseType(typeof(ApiResponse<List<MemoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SearchMemory(
        [FromBody] SearchMemoryInput input,
        CancellationToken ct)
    {
        var ids = TryGetUserIdAndWorkspaceId();
        if (ids == null) return Unauthorized();

        var (userId, workspaceId) = ids.Value;

        var items = await _memoryService.SearchMemoryAsync(userId, workspaceId, input, ct);
        return Ok(ApiResponse<List<MemoryItemDto>>.Ok(items, GetTraceId()));
    }

    // ===== POST /api/agent-memory/sessions/{id}/context =====

    /// <summary>
    /// Retrieve the assembled context pack for a session.
    /// </summary>
    [HttpPost("sessions/{id:guid}/context")]
    [ProducesResponseType(typeof(ApiResponse<ContextPackDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetContext(
        [FromRoute] Guid id,
        [FromQuery] int? maxTokens,
        CancellationToken ct)
    {
        var context = await _memoryService.GetContextAsync(id, maxTokens, ct);
        return Ok(ApiResponse<ContextPackDto>.Ok(context, GetTraceId()));
    }

    // ===== POST /api/agent-memory/items/{id}/confirm (P2.API-03) =====

    /// <summary>
    /// Confirm or reject a memory item. Creates a Feedback record and transitions the admission state.
    /// </summary>
    [HttpPost("items/{id:guid}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<MemoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmMemoryItem(
        [FromRoute] Guid id,
        [FromBody] ConfirmMemoryItemRequest request,
        CancellationToken ct)
    {
        var ids = TryGetUserIdAndWorkspaceId();
        if (ids == null) return Unauthorized();
        var (userId, _) = ids.Value;

        var item = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Memory item not found", GetTraceId()));
        }

        var action = string.IsNullOrWhiteSpace(request.Action) ? "confirm" : request.Action.ToLowerInvariant();

        // Create feedback record
        var feedback = new AgentMemoryFeedback
        {
            Id = Guid.NewGuid(),
            MemoryItemId = id,
            UserId = userId,
            Action = action,
            Note = request.Note,
            CreatedAt = DateTime.UtcNow
        };
        _db.AgentMemoryFeedbacks.Add(feedback);

        // Apply state transition
        if (action == "confirm")
        {
            await _admissionService.ConfirmMemoryAsync(item, ct);
        }
        else if (action == "reject")
        {
            await _admissionService.RejectMemoryAsync(item, ct);
        }

        await _db.SaveChangesAsync(ct);

        var dto = await _memoryService.GetMemoryItemAsync(id, ct);
        return Ok(ApiResponse<MemoryItemDto>.Ok(dto!, GetTraceId()));
    }

    // ===== POST /api/agent-memory/items/{id}/archive (P2.API-03) =====

    /// <summary>
    /// Archive a confirmed memory item.
    /// </summary>
    [HttpPost("items/{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ArchiveMemoryItem([FromRoute] Guid id, CancellationToken ct)
    {
        var item = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Memory item not found", GetTraceId()));
        }

        await _retentionService.ArchiveItemAsync(id, ct);
        return NoContent();
    }

    // ===== POST /api/agent-memory/items/{id}/restore (P2.API-03) =====

    /// <summary>
    /// Restore an archived memory item to active state.
    /// </summary>
    [HttpPost("items/{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RestoreMemoryItem([FromRoute] Guid id, CancellationToken ct)
    {
        var item = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Memory item not found", GetTraceId()));
        }

        await _retentionService.RestoreItemAsync(id, ct);
        return NoContent();
    }

    // ===== DELETE /api/agent-memory/items/{id} (P2.API-03) =====

    /// <summary>
    /// Forget (soft-delete) a confirmed memory item.
    /// </summary>
    [HttpDelete("items/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForgetMemoryItem([FromRoute] Guid id, CancellationToken ct)
    {
        var item = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Memory item not found", GetTraceId()));
        }

        await _retentionService.ForgetItemAsync(id, ct);
        return NoContent();
    }

    // ===== GET /api/agent-memory/items/{id}/evidence (P2.API-03) =====

    /// <summary>
    /// Get evidence for a memory item.
    /// </summary>
    [HttpGet("items/{id:guid}/evidence")]
    [ProducesResponseType(typeof(ApiResponse<List<EvidenceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetEvidence([FromRoute] Guid id, CancellationToken ct)
    {
        var item = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Memory item not found", GetTraceId()));
        }

        var evidence = await _db.AgentMemoryEvidences
            .Where(e => e.MemoryItemId == id)
            .OrderBy(e => e.CapturedAt)
            .Select(e => new EvidenceDto
            {
                Id = e.Id,
                MemoryItemId = e.MemoryItemId,
                EvidenceKind = e.EvidenceKind.ToString(),
                ReferenceId = e.ReferenceId,
                Locator = e.Locator,
                Relation = e.Relation,
                CapturedAt = e.CapturedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<List<EvidenceDto>>.Ok(evidence, GetTraceId()));
    }

    // ===== GET /api/agent-memory/items/{id}/feedback (P2.API-03) =====

    /// <summary>
    /// Get feedback history for a memory item.
    /// </summary>
    [HttpGet("items/{id:guid}/feedback")]
    [ProducesResponseType(typeof(ApiResponse<List<FeedbackDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFeedback([FromRoute] Guid id, CancellationToken ct)
    {
        var item = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Memory item not found", GetTraceId()));
        }

        var feedbacks = await _db.AgentMemoryFeedbacks
            .Where(f => f.MemoryItemId == id)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FeedbackDto
            {
                Id = f.Id,
                MemoryItemId = f.MemoryItemId,
                UserId = f.UserId,
                Action = f.Action,
                Note = f.Note,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<List<FeedbackDto>>.Ok(feedbacks, GetTraceId()));
    }

    // ===== GET /api/agent-memory/access-log (P2.API-03) =====

    /// <summary>
    /// Get access logs, optionally filtered by session or memory item.
    /// </summary>
    [HttpGet("access-log")]
    [ProducesResponseType(typeof(ApiResponse<List<AccessLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAccessLogs(
        [FromQuery] Guid? sessionId,
        [FromQuery] Guid? memoryItemId,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var ids = TryGetUserIdAndWorkspaceId();
        if (ids == null) return Unauthorized();

        var query = _db.AgentMemoryAccessLogs.AsQueryable();

        if (sessionId.HasValue)
        {
            query = query.Where(a => a.SessionId == sessionId.Value);
        }

        if (memoryItemId.HasValue)
        {
            query = query.Where(a => a.MemoryItemId == memoryItemId.Value);
        }

        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(a => new AccessLogDto
            {
                Id = a.Id,
                MemoryItemId = a.MemoryItemId,
                SessionId = a.SessionId,
                AgentProfileId = a.AgentProfileId,
                Action = a.Action,
                TraceId = a.TraceId,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<List<AccessLogDto>>.Ok(logs, GetTraceId()));
    }

    // ===== POST /api/agent-memory/sessions/{id}/checkpoint (P2.API-03) =====

    /// <summary>
    /// Create a checkpoint for a session.
    /// </summary>
    [HttpPost("sessions/{id:guid}/checkpoint")]
    [ProducesResponseType(typeof(ApiResponse<CheckpointDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateCheckpoint([FromRoute] Guid id, CancellationToken ct)
    {
        var session = await _db.AgentMemorySessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session == null)
        {
            return NotFound(ApiResponse<object>.Fail("not_found", "Session not found", GetTraceId()));
        }

        var checkpoint = await _checkpointService.CreateCheckpointAsync(id, ct);

        var dto = MapCheckpointToDto(checkpoint);
        return StatusCode(201, ApiResponse<CheckpointDto>.Ok(dto, GetTraceId()));
    }

    // ===== GET /api/agent-memory/sessions/{id}/checkpoints (P2.API-03) =====

    /// <summary>
    /// List checkpoints for a session.
    /// </summary>
    [HttpGet("sessions/{id:guid}/checkpoints")]
    [ProducesResponseType(typeof(ApiResponse<List<CheckpointDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListCheckpoints([FromRoute] Guid id, CancellationToken ct)
    {
        var checkpoints = await _checkpointService.ListCheckpointsAsync(id, ct);
        var dtos = checkpoints.Select(MapCheckpointToDto).ToList();
        return Ok(ApiResponse<List<CheckpointDto>>.Ok(dtos, GetTraceId()));
    }

    // ===== GET /api/agent-memory/health (P2.OPS-01) =====

    /// <summary>
    /// Health check endpoint returning basic agent memory statistics.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [AllowAnonymous]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var totalSessions = await _db.AgentMemorySessions.CountAsync(ct);
        var totalItems = await _db.AgentMemoryItems.CountAsync(ct);
        var totalCheckpoints = await _db.AgentMemoryCheckpoints.CountAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            status = "healthy",
            total_sessions = totalSessions,
            total_items = totalItems,
            total_checkpoints = totalCheckpoints
        }, GetTraceId()));
    }

    // ===== GET /api/agent-memory/metrics (P3.OPS-01) =====

    /// <summary>
    /// Returns quality and operational metrics for the agent memory system.
    /// Optionally filtered by workspace.
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(ApiResponse<MemoryQualityMetrics>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] Guid? workspaceId,
        CancellationToken ct = default)
    {
        var ids = TryGetUserIdAndWorkspaceId();
        if (ids == null) return Unauthorized();

        // Use the workspace from query if provided, otherwise use the current workspace
        var (_, currentWorkspaceId) = ids.Value;
        var effectiveWorkspaceId = workspaceId ?? currentWorkspaceId;

        var metrics = await _metricsService.GetMetricsAsync(effectiveWorkspaceId, ct);
        return Ok(ApiResponse<MemoryQualityMetrics>.Ok(metrics, GetTraceId()));
    }

    // ===== Helpers =====

    /// <summary>
    /// Resolves the authenticated user ID (from <c>ClaimTypes.NameIdentifier</c>
    /// via <see cref="ICurrentUserContext"/>) and the workspace ID (from the
    /// <c>workspace_id</c> JWT claim). Returns null if either is missing.
    /// </summary>
    private (Guid userId, Guid workspaceId)? TryGetUserIdAndWorkspaceId()
    {
        var userId = _currentUser.UserId;
        if (userId == null) return null;

        var workspaceIdClaim = User.FindFirst("workspace_id");
        if (workspaceIdClaim == null || !Guid.TryParse(workspaceIdClaim.Value, out var workspaceId))
            return null;

        return (userId.Value, workspaceId);
    }

    /// <summary>
    /// Maps a checkpoint entity to a DTO.
    /// </summary>
    private static CheckpointDto MapCheckpointToDto(AgentMemoryCheckpoint checkpoint)
    {
        return new CheckpointDto
        {
            Id = checkpoint.Id,
            SessionId = checkpoint.SessionId,
            FromSequence = checkpoint.FromSequence,
            ToSequence = checkpoint.ToSequence,
            Summary = checkpoint.Summary,
            OpenLoopsJson = checkpoint.OpenLoopsJson,
            DecisionsJson = checkpoint.DecisionsJson,
            TokenEstimate = checkpoint.TokenEstimate,
            DeliveryState = checkpoint.DeliveryState,
            CreatedAt = checkpoint.CreatedAt,
            Version = checkpoint.Version
        };
    }
}

// ===== Request DTOs =====

/// <summary>
/// Request body for POST /api/agent-memory/sessions.
/// </summary>
public class StartSessionRequest
{
    public string ExternalSessionKey { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public Guid? AgentProfileId { get; set; }
    public Guid? TopicId { get; set; }
}

/// <summary>
/// Request body for POST /api/agent-memory/items/{id}/confirm.
/// </summary>
public class ConfirmMemoryItemRequest
{
    /// <summary>
    /// The action to perform: "confirm" or "reject". Defaults to "confirm".
    /// </summary>
    public string Action { get; set; } = "confirm";

    /// <summary>
    /// Optional note explaining the confirmation or rejection.
    /// </summary>
    public string? Note { get; set; }
}

/// <summary>
/// DTO for a session checkpoint.
/// </summary>
public class CheckpointDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int FromSequence { get; set; }
    public int ToSequence { get; set; }
    public string? Summary { get; set; }
    public string? OpenLoopsJson { get; set; }
    public string? DecisionsJson { get; set; }
    public int TokenEstimate { get; set; }
    public string DeliveryState { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; }
}
