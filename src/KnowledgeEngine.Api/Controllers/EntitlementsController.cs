using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/entitlements")]
public sealed class EntitlementsController : BaseController
{
    private readonly IAiBillingService _billing;
    private readonly ICurrentUserContext _currentUser;

    public EntitlementsController(
        IAiBillingService billing,
        ICurrentUserContext currentUser)
    {
        _billing = billing;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var response = await _billing.GetEntitlementsAsync(userId, workspaceId, ct);
        return Ok(ApiResponse<BillingEntitlementsResponse>.Ok(response, GetTraceId()));
    }

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");
}
