using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class EntityService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<EntityService> _logger;

    public EntityService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<EntityService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<EntityListItem>>> GetAllAsync(
        string? entityType = null,
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Entities.Where(e => e.UserId == userId);

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(e => e.EntityType == entityType);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLowerInvariant();
            query = query.Where(e =>
                e.Name.ToLower().Contains(searchLower) ||
                (e.NormalizedName != null && e.NormalizedName.Contains(searchLower)));
        }

        var total = await query.CountAsync(ct);
        var entities = await query
            .OrderByDescending(e => e.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var entityIds = entities.Select(e => e.Id).ToList();

        // Get document counts for each entity
        var docCounts = await _db.DocumentEntities
            .Where(de => entityIds.Contains(de.EntityId))
            .GroupBy(de => de.EntityId)
            .Select(g => new { EntityId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EntityId, x => x.Count, ct);

        var items = entities.Select(e =>
            Mapper.ToEntityListItem(e, docCounts.GetValueOrDefault(e.Id))).ToList();

        var result = new PagedResult<EntityListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<EntityListItem>>.Ok(result);
    }

    public async Task<ApiResponse<EntityDetail>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var entity = await _db.Entities.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId, ct);
        if (entity == null)
        {
            throw new NotFoundException("Entity", id);
        }

        // Load related documents
        var relatedDocs = await (
            from de in _db.DocumentEntities
            join d in _db.Documents on de.DocumentId equals d.Id
            where de.EntityId == id && d.UserId == userId
            select new { Document = d, DocumentEntity = de }
        ).ToListAsync(ct);

        var relatedDocuments = relatedDocs.Select(x => new RelatedDocument
        {
            DocumentId = x.Document.Id,
            Title = x.Document.Title,
            MentionCount = x.DocumentEntity.MentionCount,
            Confidence = x.DocumentEntity.Confidence,
            Evidence = x.DocumentEntity.Evidence
        }).ToList();

        return ApiResponse<EntityDetail>.Ok(Mapper.ToEntityDetail(entity, relatedDocuments));
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
