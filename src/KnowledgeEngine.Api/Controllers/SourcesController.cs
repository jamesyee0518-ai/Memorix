using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class SourcesController : BaseController
{
    private readonly SourceService _sourceService;

    public SourcesController(SourceService sourceService)
    {
        _sourceService = sourceService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? topicId,
        [FromQuery] string? status,
        [FromQuery] string? sourceType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _sourceService.GetAllAsync(topicId, status, sourceType, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<SourceListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _sourceService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<SourceDetail>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("url")]
    public async Task<IActionResult> ImportUrl([FromBody] ImportUrlRequest request, CancellationToken ct)
    {
        var result = await _sourceService.ImportUrlAsync(request, ct);
        return StatusCode(201, ApiResponse<SourceResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("text")]
    public async Task<IActionResult> ImportText([FromBody] ImportTextRequest request, CancellationToken ct)
    {
        var result = await _sourceService.ImportTextAsync(request, ct);
        return StatusCode(201, ApiResponse<SourceResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("file")]
    [RequestSizeLimit(52_428_800)]
    [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
    public async Task<IActionResult> ImportFile(
        [FromForm] Guid topicId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.FailObject("VALIDATION_ERROR", "File is required", GetTraceId()));
        }

        await using var stream = file.OpenReadStream();
        var result = await _sourceService.ImportPdfAsync(topicId, file.FileName, file.ContentType, file.Length, stream, ct);
        return StatusCode(201, ApiResponse<SourceResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _sourceService.DeleteAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry([FromRoute] Guid id, [FromBody] RetrySourceRequestDto? request, CancellationToken ct)
    {
        var result = await _sourceService.RetryAsync(id, request?.FromStep, ct);
        return Ok(ApiResponse<SourceResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("{id:guid}/process")]
    public async Task<IActionResult> TriggerProcessing([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _sourceService.TriggerProcessingAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }
}
