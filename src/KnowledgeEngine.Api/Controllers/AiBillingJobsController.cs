using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/ai/jobs")]
public sealed class AiBillingJobsController : BaseController
{
    private readonly IAiBillingService _billing;
    private readonly ICurrentUserContext _currentUser;

    public AiBillingJobsController(
        IAiBillingService billing,
        ICurrentUserContext currentUser)
    {
        _billing = billing;
        _currentUser = currentUser;
    }

    [HttpPost("estimate")]
    public async Task<IActionResult> Estimate(
        [FromBody] EstimateAiJobRequest request,
        CancellationToken ct)
    {
        var response = await _billing.EstimateAsync(RequireUserId(), request, ct);
        return Ok(ApiResponse<AiJobEstimateResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAiBillingJobRequest request,
        CancellationToken ct)
    {
        var response = await _billing.CreateJobAsync(RequireUserId(), request, ct);
        return Ok(ApiResponse<AiBillingJobResponse>.Ok(response, GetTraceId()));
    }

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");
}
