using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/usage")]
public class UsageController : BaseController
{
    private readonly IUsageService _usageService;
    private readonly ICurrentUserContext _currentUser;

    public UsageController(IUsageService usageService, ICurrentUserContext currentUser)
    {
        _usageService = usageService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _usageService.GetUsageAsync(userId.Value, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<UsageResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<UsageResponse>.Ok(result.Data!, GetTraceId()));
    }
}
