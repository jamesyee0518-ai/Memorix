using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/ai-jobs")]
public class AiJobsController : BaseController
{
    private readonly AiJobService _aiJobService;

    public AiJobsController(AiJobService aiJobService)
    {
        _aiJobService = aiJobService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _aiJobService.GetAllAsync(status, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<AiJobListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _aiJobService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<AiJobResponse>.Ok(result.Data!, GetTraceId()));
    }
}
