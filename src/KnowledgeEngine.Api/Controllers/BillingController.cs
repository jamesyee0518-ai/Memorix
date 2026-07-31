using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/billing")]
public sealed class BillingController : BaseController
{
    private readonly IAiBillingService _billing;
    private readonly IBillingPortalService _portal;
    private readonly ICurrentUserContext _currentUser;

    public BillingController(
        IAiBillingService billing,
        IBillingPortalService portal,
        ICurrentUserContext currentUser)
    {
        _billing = billing;
        _portal = portal;
        _currentUser = currentUser;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _portal.GetOverviewAsync(RequireUserId(), workspaceId, ct);
        return Ok(ApiResponse<BillingOverviewResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] Guid workspaceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var response = await _portal.GetUsageAsync(RequireUserId(), workspaceId, from, to, ct);
        return Ok(ApiResponse<BillingUsageResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet("bills")]
    public async Task<IActionResult> GetBills(
        [FromQuery] Guid workspaceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var response = await _portal.GetBillsAsync(RequireUserId(), workspaceId, from, to, ct);
        return Ok(ApiResponse<BillingBillsResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet("pricing")]
    public async Task<IActionResult> GetPricing(
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _portal.GetPricingAsync(RequireUserId(), workspaceId, ct);
        return Ok(ApiResponse<BillingPricingResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _billing.GetSummaryAsync(RequireUserId(), workspaceId, ct);
        return Ok(ApiResponse<BillingSummaryResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJob(
        Guid jobId,
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _billing.GetJobAsync(RequireUserId(), workspaceId, jobId, ct);
        return response == null
            ? NotFound(ApiResponse<object>.FailObject("job_not_found", "The AI job does not exist.", GetTraceId()))
            : Ok(ApiResponse<AiBillingJobResponse>.Ok(response, GetTraceId()));
    }

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");
}
