using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class JobsController : BaseController
{
    private readonly IngestJobService _jobService;

    public JobsController(IngestJobService jobService)
    {
        _jobService = jobService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? sourceId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _jobService.GetJobsAsync(sourceId, status, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<JobListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _jobService.GetJobByIdAsync(id, ct);
        return Ok(ApiResponse<JobResponse>.Ok(result.Data!, GetTraceId()));
    }
}
