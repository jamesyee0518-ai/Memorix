using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.PlatformOperator)]
[Route("api/internal/ai")]
public sealed class InternalAiUsageController : BaseController
{
    private readonly IAiBillingService _billing;

    public InternalAiUsageController(IAiBillingService billing)
    {
        _billing = billing;
    }

    [HttpPost("usage-events")]
    public async Task<IActionResult> RecordUsage(
        [FromBody] RecordUsageEventRequest request,
        CancellationToken ct)
    {
        var response = await _billing.RecordUsageAsync(request, ct);
        return Ok(ApiResponse<UsageEventResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost("attempts")]
    public async Task<IActionResult> StartAttempt(
        [FromBody] StartAiAttemptRequest request,
        CancellationToken ct)
    {
        var response = await _billing.StartAttemptAsync(request, ct);
        return Ok(ApiResponse<AiAttemptResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost("attempts/{attemptId:guid}/complete")]
    public async Task<IActionResult> CompleteAttempt(
        Guid attemptId,
        [FromBody] CompleteAiAttemptRequest request,
        CancellationToken ct)
    {
        var response = await _billing.CompleteAttemptAsync(attemptId, request, ct);
        return Ok(ApiResponse<AiAttemptResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost("jobs/{jobId:guid}/complete")]
    public async Task<IActionResult> CompleteJob(
        Guid jobId,
        [FromBody] CompleteAiJobRequest request,
        CancellationToken ct)
    {
        var response = await _billing.CompleteJobAsync(jobId, request, ct);
        return Ok(ApiResponse<AiBillingJobResponse>.Ok(response, GetTraceId()));
    }
}
