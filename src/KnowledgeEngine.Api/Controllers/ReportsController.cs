using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/reports")]
public class ReportsController : BaseController
{
    private readonly IReportService _reportService;
    private readonly ICurrentUserContext _currentUser;

    public ReportsController(IReportService reportService, ICurrentUserContext currentUser)
    {
        _reportService = reportService;
        _currentUser = currentUser;
    }

    [HttpPost("daily")]
    public async Task<IActionResult> CreateDailyReport([FromBody] CreateDailyReportRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.CreateDailyReportAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<CreateReportResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<CreateReportResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("weekly")]
    public async Task<IActionResult> CreateWeeklyReport([FromBody] CreateWeeklyReportRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.CreateWeeklyReportAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<CreateReportResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<CreateReportResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("topic")]
    public async Task<IActionResult> CreateTopicReport([FromBody] CreateTopicReportRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.CreateTopicReportAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<CreateReportResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<CreateReportResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? topicId,
        [FromQuery] string? reportType,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.GetAllAsync(userId.Value, topicId, reportType, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<PagedResult<ReportListItem>>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<PagedResult<ReportListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.GetByIdAsync(userId.Value, id, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ReportDetail>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ReportDetail>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("{id:guid}/regenerate")]
    public async Task<IActionResult> Regenerate([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.RegenerateAsync(userId.Value, id, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<CreateReportResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<CreateReportResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateReportRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.UpdateAsync(userId.Value, id, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ReportDetail>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ReportDetail>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.ArchiveAsync(userId.Value, id, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<object>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJobStatus([FromRoute] Guid jobId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _reportService.GetJobStatusAsync(userId.Value, jobId, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ReportJobStatusResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ReportJobStatusResponse>.Ok(result.Data!, GetTraceId()));
    }
}
