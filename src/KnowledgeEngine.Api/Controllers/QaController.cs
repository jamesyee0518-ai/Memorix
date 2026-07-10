using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class QaController : BaseController
{
    private readonly IQaService _qaService;
    private readonly ICurrentUserContext _currentUser;

    public QaController(IQaService qaService, ICurrentUserContext currentUser)
    {
        _qaService = qaService;
        _currentUser = currentUser;
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateQaSessionRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _qaService.CreateSessionAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<QaSessionResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<QaSessionResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(
        [FromQuery] Guid? topicId,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _qaService.GetSessionsAsync(userId.Value, topicId, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<PagedResult<QaSessionListItem>>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<PagedResult<QaSessionListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] QaAskRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _qaService.AskAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<QaAnswerResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<QaAnswerResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("sessions/{sessionId:guid}/messages")]
    public async Task<IActionResult> GetMessages([FromRoute] Guid sessionId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _qaService.GetSessionMessagesAsync(userId.Value, sessionId, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<List<QaMessageResponse>>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<List<QaMessageResponse>>.Ok(result.Data!, GetTraceId()));
    }
}
