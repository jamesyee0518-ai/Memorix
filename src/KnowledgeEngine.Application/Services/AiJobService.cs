using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class AiJobService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<AiJobService> _logger;

    public AiJobService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<AiJobService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<AiJobListItem>>> GetAllAsync(
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.AiJobs.Where(j => j.UserId == userId);

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(j => j.Status == status);
        }

        var total = await query.CountAsync(ct);
        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = jobs.Select(Mapper.ToAiJobListItem).ToList();

        var result = new PagedResult<AiJobListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<AiJobListItem>>.Ok(result);
    }

    public async Task<ApiResponse<AiJobResponse>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var job = await _db.AiJobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);
        if (job == null)
        {
            throw new NotFoundException("AiJob", id);
        }

        return ApiResponse<AiJobResponse>.Ok(Mapper.ToAiJobResponse(job));
    }

    public async Task<ApiResponse<PagedResult<AiJobListItem>>> GetByTargetAsync(
        string targetType,
        Guid targetId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.AiJobs
            .Where(j => j.UserId == userId && j.TargetType == targetType && j.TargetId == targetId);

        var total = await query.CountAsync(ct);
        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = jobs.Select(Mapper.ToAiJobListItem).ToList();

        var result = new PagedResult<AiJobListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<AiJobListItem>>.Ok(result);
    }

    private Guid RequireUserId()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            throw new UnauthorizedException("User is not authenticated");
        }
        return _currentUser.UserId.Value;
    }
}
