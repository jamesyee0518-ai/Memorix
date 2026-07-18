using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KnowledgeEngine.Application.Security;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/feedback")]
public class FeedbackController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public FeedbackController(IAppDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFeedbackRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var now = DateTime.UtcNow;
        var feedback = new FeedbackItem
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            FeedbackType = request.FeedbackType,
            Module = request.Module,
            Severity = request.Severity,
            Title = request.Title,
            Content = request.Content,
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId,
            Status = "open",
            Priority = "medium",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.FeedbackItems.Add(feedback);
        await _db.SaveChangesAsync(ct);

        var response = new FeedbackResponse
        {
            Id = feedback.Id,
            UserId = feedback.UserId,
            FeedbackType = feedback.FeedbackType,
            Module = feedback.Module,
            Severity = feedback.Severity,
            Title = feedback.Title,
            Content = feedback.Content,
            RelatedEntityType = feedback.RelatedEntityType,
            RelatedEntityId = feedback.RelatedEntityId,
            Status = feedback.Status,
            Priority = feedback.Priority,
            CreatedAt = feedback.CreatedAt,
            UpdatedAt = feedback.UpdatedAt
        };

        return StatusCode(201, ApiResponse<FeedbackResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var feedbacks = await _db.FeedbackItems
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        var items = feedbacks.Select(f => new FeedbackListItem
        {
            Id = f.Id,
            FeedbackType = f.FeedbackType,
            Module = f.Module,
            Severity = f.Severity,
            Title = f.Title,
            Status = f.Status,
            Priority = f.Priority,
            CreatedAt = f.CreatedAt
        }).ToList();

        return Ok(ApiResponse<List<FeedbackListItem>>.Ok(items, GetTraceId()));
    }

    // ===== Admin Endpoints =====

    // ===== GET /api/feedback/all — 管理端查看全部反馈（支持 status/type/severity 筛选 + 分页）=====
    [HttpGet("all")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperator)]
    public async Task<IActionResult> GetAllAdmin(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.FeedbackItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(f => f.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(f => f.FeedbackType == type);
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(f => f.Severity == severity);
        }

        var total = await query.CountAsync(ct);

        var feedbacks = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = feedbacks.Select(f => new FeedbackListItem
        {
            Id = f.Id,
            FeedbackType = f.FeedbackType,
            Module = f.Module,
            Severity = f.Severity,
            Title = f.Title,
            Status = f.Status,
            Priority = f.Priority,
            CreatedAt = f.CreatedAt
        }).ToList();

        var paged = new PagedResult<FeedbackListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<FeedbackListItem>>.Ok(paged, GetTraceId()));
    }

    // ===== GET /api/feedback/stats — 反馈统计（按 type/severity/status 分组计数）=====
    [HttpGet("stats")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperator)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var allFeedbacks = await _db.FeedbackItems.ToListAsync(ct);

        var stats = new FeedbackStatsResponse
        {
            TotalCount = allFeedbacks.Count,
            ByType = allFeedbacks
                .GroupBy(f => f.FeedbackType)
                .ToDictionary(g => g.Key, g => g.Count()),
            BySeverity = allFeedbacks
                .Where(f => f.Severity != null)
                .GroupBy(f => f.Severity!)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByStatus = allFeedbacks
                .GroupBy(f => f.Status)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return Ok(ApiResponse<FeedbackStatsResponse>.Ok(stats, GetTraceId()));
    }

    // ===== PUT /api/feedback/{id} — 更新反馈状态/优先级 =====
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperator)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateFeedbackRequest request, CancellationToken ct)
    {
        var feedback = await _db.FeedbackItems.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feedback == null)
        {
            return Ok(ApiResponse<FeedbackResponse>.Fail("not_found", "Feedback not found", GetTraceId()));
        }

        if (request.Status != null)
            feedback.Status = request.Status;
        if (request.Priority != null)
            feedback.Priority = request.Priority;

        feedback.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var response = new FeedbackResponse
        {
            Id = feedback.Id,
            UserId = feedback.UserId,
            FeedbackType = feedback.FeedbackType,
            Module = feedback.Module,
            Severity = feedback.Severity,
            Title = feedback.Title,
            Content = feedback.Content,
            RelatedEntityType = feedback.RelatedEntityType,
            RelatedEntityId = feedback.RelatedEntityId,
            Status = feedback.Status,
            Priority = feedback.Priority,
            CreatedAt = feedback.CreatedAt,
            UpdatedAt = feedback.UpdatedAt
        };

        return Ok(ApiResponse<FeedbackResponse>.Ok(response, GetTraceId()));
    }
}
