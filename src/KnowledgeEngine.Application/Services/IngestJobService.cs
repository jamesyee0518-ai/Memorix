using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class IngestJobService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<IngestJobService> _logger;

    public IngestJobService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<IngestJobService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<JobListItem>>> GetJobsAsync(Guid? sourceId = null, string? status = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.IngestJobs.Where(j => j.UserId == userId);

        if (sourceId.HasValue)
        {
            query = query.Where(j => j.SourceId == sourceId.Value);
        }

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

        var items = jobs.Select(Mapper.ToJobListItem).ToList();

        var result = new PagedResult<JobListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<JobListItem>>.Ok(result);
    }

    public async Task<ApiResponse<JobResponse>> GetJobByIdAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var job = await _db.IngestJobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);
        if (job == null)
        {
            throw new NotFoundException("Job", id);
        }

        return ApiResponse<JobResponse>.Ok(Mapper.ToJobResponse(job));
    }

    public async Task<ApiResponse<PagedResult<JobListItem>>> GetJobsBySourceAsync(Guid sourceId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.IngestJobs.Where(j => j.UserId == userId && j.SourceId == sourceId);

        var total = await query.CountAsync(ct);
        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = jobs.Select(Mapper.ToJobListItem).ToList();

        var result = new PagedResult<JobListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<JobListItem>>.Ok(result);
    }

    public async Task<ApiResponse<JobResponse>> CreateJobAsync(Guid userId, Guid sourceId, string jobType, CancellationToken ct = default)
    {
        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId, ct);
        if (source == null)
        {
            throw new NotFoundException("Source", sourceId);
        }

        var now = DateTime.UtcNow;
        var job = new Domain.Entities.IngestJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceId = sourceId,
            JobType = jobType,
            Status = "pending",
            CreatedAt = now
        };
        _db.IngestJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return ApiResponse<JobResponse>.Ok(Mapper.ToJobResponse(job));
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
