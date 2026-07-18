using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/local-identity")]
[Authorize]
public class LocalIdentityController : BaseController
{
    private readonly ILocalIdentityService _identityService;

    public LocalIdentityController(ILocalIdentityService identityService)
    {
        _identityService = identityService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var identity = await _identityService.EnsureIdentityAsync(ct);
        return Ok(ApiResponse<LocalIdentityDto>.Ok(identity, GetTraceId()));
    }
}
