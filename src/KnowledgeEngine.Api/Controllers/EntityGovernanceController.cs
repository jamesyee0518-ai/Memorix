using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public sealed class EntityGovernanceController(
    IEntityGovernanceService governance,
    ICurrentUserContext currentUser) : BaseController
{
    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? status,
        [FromQuery] string? taskType,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var result = await governance.ListTasksAsync(
            RequireUserId(), workspaceId, status, taskType, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<EntityGovernanceTaskDto>>.Ok(
            result, GetTraceId()));
    }

    [HttpPost("maintenance")]
    public async Task<IActionResult> StartMaintenance(
        [FromBody] StartEntityMaintenanceRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            request.IdempotencyKey = idempotencyKey;
        var result = await governance.StartMaintenanceAsync(
            RequireUserId(), request, ct);
        return Ok(ApiResponse<EntityGovernanceTaskDto>.Ok(result, GetTraceId()));
    }

    [HttpPost("tasks/{id:guid}/decision")]
    public async Task<IActionResult> Decide(
        Guid id,
        [FromBody] EntityGovernanceDecisionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            request.IdempotencyKey = idempotencyKey ?? string.Empty;
        var result = await governance.DecideAsync(
            RequireUserId(), id, request, ct);
        return Ok(ApiResponse<EntityGovernanceTaskDto>.Ok(result, GetTraceId()));
    }

    [HttpGet("quality-metrics")]
    public async Task<IActionResult> GetQualityMetrics(
        [FromQuery] Guid? workspaceId,
        CancellationToken ct = default)
    {
        var result = await governance.GetQualityMetricsAsync(
            RequireUserId(), workspaceId, ct);
        return Ok(ApiResponse<EntityQualityMetrics>.Ok(result, GetTraceId()));
    }

    private Guid RequireUserId()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == null)
            throw new UnauthorizedException("User is not authenticated.");
        return currentUser.UserId.Value;
    }
}
