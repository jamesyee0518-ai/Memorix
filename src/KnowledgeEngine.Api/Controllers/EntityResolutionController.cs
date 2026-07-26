using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public sealed class EntityResolutionController(
    IEntityGovernanceService governance,
    ICurrentUserContext currentUser) : BaseController
{
    [HttpPost("scan")]
    public async Task<IActionResult> StartScan(
        [FromBody] StartEntityScanRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        request.IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? idempotencyKey
            : request.IdempotencyKey;
        var result = await governance.StartDuplicateScanAsync(
            RequireUserId(), request, ct);
        return Ok(ApiResponse<EntityGovernanceTaskDto>.Ok(result, GetTraceId()));
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid jobId, CancellationToken ct)
    {
        var result = await governance.GetTaskAsync(RequireUserId(), jobId, ct);
        return result == null
            ? NotFound(ApiResponse<object>.Fail(
                "ENTITY_SCAN_NOT_FOUND", "实体扫描任务不存在。", GetTraceId()))
            : Ok(ApiResponse<EntityGovernanceTaskDto>.Ok(result, GetTraceId()));
    }

    [HttpGet("jobs/{jobId:guid}/candidates")]
    public async Task<IActionResult> GetCandidates(Guid jobId, CancellationToken ct)
    {
        var result = await governance.ListCandidatesAsync(
            RequireUserId(), jobId, ct);
        return Ok(ApiResponse<IReadOnlyList<EntityGovernanceTaskDto>>.Ok(
            result, GetTraceId()));
    }

    [HttpPost("jobs/{jobId:guid}/pause")]
    public async Task<IActionResult> Pause(Guid jobId, CancellationToken ct)
    {
        var result = await governance.PauseAsync(RequireUserId(), jobId, ct);
        return Ok(ApiResponse<EntityGovernanceTaskDto>.Ok(result, GetTraceId()));
    }

    [HttpPost("jobs/{jobId:guid}/resume")]
    public async Task<IActionResult> Resume(Guid jobId, CancellationToken ct)
    {
        var result = await governance.ResumeAsync(RequireUserId(), jobId, ct);
        return Ok(ApiResponse<EntityGovernanceTaskDto>.Ok(result, GetTraceId()));
    }

    [HttpPost("jobs/{jobId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid jobId, CancellationToken ct)
    {
        var result = await governance.RetryAsync(RequireUserId(), jobId, ct);
        return Ok(ApiResponse<EntityGovernanceTaskDto>.Ok(result, GetTraceId()));
    }

    private Guid RequireUserId()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == null)
            throw new UnauthorizedException("User is not authenticated.");
        return currentUser.UserId.Value;
    }
}
