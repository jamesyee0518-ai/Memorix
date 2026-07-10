using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/beta-users")]
public class BetaUserController : BaseController
{
    private readonly IAppDbContext _db;

    public BetaUserController(IAppDbContext db)
    {
        _db = db;
    }

    // ===== GET /api/beta-users — 列表（支持 status/group 筛选 + 分页）=====
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? group,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.BetaUsers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(b => b.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            query = query.Where(b => b.BetaGroup == group);
        }

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = users.Select(MapToListItem).ToList();

        var paged = new PagedResult<BetaUserListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<BetaUserListItem>>.Ok(paged, GetTraceId()));
    }

    // ===== GET /api/beta-users/{id} — 详情 =====
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var user = await _db.BetaUsers.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (user == null)
        {
            return Ok(ApiResponse<BetaUserResponse>.Fail("not_found", "Beta user not found", GetTraceId()));
        }

        return Ok(ApiResponse<BetaUserResponse>.Ok(MapToResponse(user), GetTraceId()));
    }

    // ===== POST /api/beta-users — 邀请内测用户（email + betaGroup + platform）=====
    [HttpPost]
    public async Task<IActionResult> Invite([FromBody] InviteBetaUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Ok(ApiResponse<BetaUserResponse>.Fail("validation_error", "Email is required", GetTraceId()));
        }

        // Check for duplicate email
        var existing = await _db.BetaUsers.FirstOrDefaultAsync(b => b.Email == request.Email, ct);
        if (existing != null)
        {
            return Ok(ApiResponse<BetaUserResponse>.Fail("duplicate", "A beta user with this email already exists", GetTraceId()));
        }

        var now = DateTime.UtcNow;
        var user = new BetaUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            Name = request.DisplayName,
            UserType = "unknown",
            InviteCode = GenerateInviteCode(),
            BetaGroup = request.BetaGroup,
            Platform = request.Platform,
            Status = "invited",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.BetaUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, ApiResponse<BetaUserResponse>.Ok(MapToResponse(user), GetTraceId()));
    }

    // ===== PUT /api/beta-users/{id} — 更新（status/notes/betaGroup）=====
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateBetaUserRequest request, CancellationToken ct)
    {
        var user = await _db.BetaUsers.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (user == null)
        {
            return Ok(ApiResponse<BetaUserResponse>.Fail("not_found", "Beta user not found", GetTraceId()));
        }

        if (request.Status != null)
            user.Status = request.Status;
        if (request.Notes != null)
            user.Notes = request.Notes;
        if (request.BetaGroup != null)
            user.BetaGroup = request.BetaGroup;

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<BetaUserResponse>.Ok(MapToResponse(user), GetTraceId()));
    }

    // ===== POST /api/beta-users/{id}/activate — 激活 =====
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        var user = await _db.BetaUsers.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (user == null)
        {
            return Ok(ApiResponse<BetaUserResponse>.Fail("not_found", "Beta user not found", GetTraceId()));
        }

        user.Status = "active";
        user.OnboardedAt ??= DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<BetaUserResponse>.Ok(MapToResponse(user), GetTraceId()));
    }

    // ===== POST /api/beta-users/{id}/pause — 暂停 =====
    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause([FromRoute] Guid id, CancellationToken ct)
    {
        var user = await _db.BetaUsers.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (user == null)
        {
            return Ok(ApiResponse<BetaUserResponse>.Fail("not_found", "Beta user not found", GetTraceId()));
        }

        user.Status = "paused";
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<BetaUserResponse>.Ok(MapToResponse(user), GetTraceId()));
    }

    // ===== DELETE /api/beta-users/{id} — 删除 =====
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var user = await _db.BetaUsers.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (user == null)
        {
            return Ok(ApiResponse<object>.Fail("not_found", "Beta user not found", GetTraceId()));
        }

        _db.BetaUsers.Remove(user);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { id, deleted = true }, GetTraceId()));
    }

    // ===== Helpers =====

    private static BetaUserResponse MapToResponse(BetaUser b)
    {
        return new BetaUserResponse
        {
            Id = b.Id,
            UserId = b.UserId,
            Email = b.Email,
            Name = b.Name,
            UserType = b.UserType,
            InviteCode = b.InviteCode,
            BetaGroup = b.BetaGroup,
            Platform = b.Platform,
            Status = b.Status,
            OnboardedAt = b.OnboardedAt,
            LastFeedbackAt = b.LastFeedbackAt,
            Notes = b.Notes,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        };
    }

    private static BetaUserListItem MapToListItem(BetaUser b)
    {
        return new BetaUserListItem
        {
            Id = b.Id,
            Email = b.Email,
            Name = b.Name,
            BetaGroup = b.BetaGroup,
            Status = b.Status,
            OnboardedAt = b.OnboardedAt,
            LastFeedbackAt = b.LastFeedbackAt,
            CreatedAt = b.CreatedAt
        };
    }

    private static string GenerateInviteCode()
    {
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}
