using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class TopicsController : BaseController
{
    private readonly TopicService _topicService;

    public TopicsController(TopicService topicService)
    {
        _topicService = topicService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _topicService.GetAllAsync(page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<TopicListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _topicService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<TopicDetail>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTopicRequest request, CancellationToken ct)
    {
        var result = await _topicService.CreateAsync(request, ct);
        return StatusCode(201, ApiResponse<TopicResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateTopicRequest request, CancellationToken ct)
    {
        var result = await _topicService.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<TopicResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _topicService.DeleteAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }
}
