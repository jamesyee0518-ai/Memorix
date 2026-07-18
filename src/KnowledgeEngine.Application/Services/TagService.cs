using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class TagService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<TagService> _logger;

    public TagService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<TagService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<TagListItem>>> GetAllAsync(
        string? type = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Tags.Where(t => t.UserId == userId);

        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(t => t.Type == type);
        }

        var total = await query.CountAsync(ct);
        var tags = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var tagIds = tags.Select(t => t.Id).ToList();

        // Get document counts for each tag
        var docCounts = await _db.DocumentTags
            .Where(dt => tagIds.Contains(dt.TagId))
            .GroupBy(dt => dt.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count, ct);

        var items = tags.Select(t =>
            Mapper.ToTagListItem(t, docCounts.GetValueOrDefault(t.Id))).ToList();

        var result = new PagedResult<TagListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<TagListItem>>.Ok(result);
    }

    public async Task<ApiResponse<TagResponse>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (tag == null)
        {
            throw new NotFoundException("Tag", id);
        }

        return ApiResponse<TagResponse>.Ok(Mapper.ToTagResponse(tag));
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
