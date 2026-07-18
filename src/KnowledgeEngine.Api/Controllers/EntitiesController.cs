using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class EntitiesController : BaseController
{
    private readonly EntityService _entityService;

    public EntitiesController(EntityService entityService)
    {
        _entityService = entityService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? entityType,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _entityService.GetAllAsync(entityType, search, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<EntityListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _entityService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<EntityDetail>.Ok(result.Data!, GetTraceId()));
    }
}
