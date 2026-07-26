using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Services;

public class EntityService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IEntityRedirectResolver _redirectResolver;
    private readonly IEntityNameNormalizer _normalizer;
    private readonly ILogger<EntityService> _logger;

    public EntityService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        IEntityRedirectResolver redirectResolver,
        IEntityNameNormalizer normalizer,
        ILogger<EntityService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _redirectResolver = redirectResolver;
        _normalizer = normalizer;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<EntityListItem>>> GetAllAsync(
        string? entityType = null,
        string? search = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Entities.Where(e => e.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            query = query.Where(e => e.Status == normalizedStatus);
        }

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

        var original = await _db.Entities.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId, ct);
        if (original == null)
            throw new NotFoundException("Entity", id);
        var resolved = await _redirectResolver.ResolveAsync(id, original.WorkspaceId, ct);
        var entity = await _db.Entities.FirstOrDefaultAsync(
            e => e.Id == resolved.EntityId && e.UserId == userId, ct);
        if (entity == null)
        {
            throw new NotFoundException("Entity", id);
        }

        // Load related documents
        var relatedDocs = await (
            from de in _db.DocumentEntities
            join d in _db.Documents on de.DocumentId equals d.Id
            where de.EntityId == entity.Id && d.UserId == userId
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

        var aliases = await _db.EntityAliases.AsNoTracking()
            .Where(x => x.EntityId == entity.Id && x.WorkspaceId == entity.WorkspaceId)
            .OrderByDescending(x => x.IsVerified)
            .ThenBy(x => x.Alias)
            .Select(x => new EntityAliasItem
            {
                Id = x.Id,
                Alias = x.Alias,
                NormalizedAlias = x.NormalizedAlias,
                LanguageCode = x.LanguageCode,
                AliasType = x.AliasType,
                SourceType = x.SourceType,
                Confidence = x.Confidence,
                IsVerified = x.IsVerified
            })
            .ToListAsync(ct);

        var detail = Mapper.ToEntityDetail(entity, relatedDocuments, aliases);
        detail.RedirectedFrom = resolved.RedirectedFrom;
        return ApiResponse<EntityDetail>.Ok(detail);
    }

    public async Task<EntityDetail> CreateAsync(
        CreateEntityRequest request, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        if (string.IsNullOrWhiteSpace(request.CanonicalName))
            throw new ArgumentException("CanonicalName is required.");
        var workspace = await _db.Workspaces.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == request.WorkspaceId && x.UserId == userId, ct)
            ?? throw new NotFoundException("Workspace", request.WorkspaceId);
        var normalized = _normalizer.Normalize(
            request.CanonicalName, request.EntityType);
        var workspaceId = workspace.Id.ToString();
        var exists = await _db.Entities.AnyAsync(x =>
            x.WorkspaceId == workspaceId
            && x.NormalizedKey == normalized.NormalizedKey
            && x.EntityType == normalized.EntityType
            && x.Status != "merged", ct);
        if (exists)
            throw new InvalidOperationException("An active entity with the same canonical identity already exists.");
        var now = DateTime.UtcNow;
        var entity = new Entity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            Name = normalized.CanonicalName,
            CanonicalName = normalized.CanonicalName,
            NormalizedName = normalized.NormalizedKey,
            NormalizedKey = normalized.NormalizedKey,
            EntityType = normalized.EntityType,
            PreferredNameZh = request.PreferredNameZh,
            PreferredNameEn = request.PreferredNameEn,
            Abbreviation = request.Abbreviation ?? normalized.Abbreviation,
            Description = request.Description,
            Source = "manual",
            Status = "active",
            IsVerified = true,
            Confidence = 1m,
            RowVersion = 1,
            NormalizationVersion = normalized.Version,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Entities.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Mapper.ToEntityDetail(entity, [], []);
    }

    public async Task<EntityDetail> UpdateAsync(
        Guid id, UpdateEntityRequest request, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var entity = await _db.Entities.FirstOrDefaultAsync(
            x => x.Id == id && x.UserId == userId, ct)
            ?? throw new NotFoundException("Entity", id);
        if (entity.RowVersion != request.ExpectedVersion)
            throw new DbUpdateConcurrencyException(
                "Entity version changed. Refresh and retry.");
        if (!string.IsNullOrWhiteSpace(request.CanonicalName)
            || !string.IsNullOrWhiteSpace(request.EntityType))
        {
            var normalized = _normalizer.Normalize(
                request.CanonicalName ?? entity.CanonicalName ?? entity.Name,
                request.EntityType ?? entity.EntityType);
            entity.Name = normalized.CanonicalName;
            entity.CanonicalName = normalized.CanonicalName;
            entity.NormalizedName = normalized.NormalizedKey;
            entity.NormalizedKey = normalized.NormalizedKey;
            entity.EntityType = normalized.EntityType;
            entity.NormalizationVersion = normalized.Version;
        }
        if (request.PreferredNameZh != null) entity.PreferredNameZh = request.PreferredNameZh;
        if (request.PreferredNameEn != null) entity.PreferredNameEn = request.PreferredNameEn;
        if (request.Abbreviation != null) entity.Abbreviation = request.Abbreviation;
        if (request.Description != null) entity.Description = request.Description;
        if (request.IsVerified.HasValue) entity.IsVerified = request.IsVerified.Value;
        if (request.IsArchived.HasValue) entity.IsArchived = request.IsArchived.Value;
        entity.RowVersion++;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Mapper.ToEntityDetail(entity, [], []);
    }

    public async Task<IReadOnlyList<EntityAliasItem>> GetAliasesAsync(
        Guid id, CancellationToken ct = default)
    {
        var entity = await GetOwnedResolvedEntityAsync(id, ct);
        return await _db.EntityAliases.AsNoTracking()
            .Where(x => x.EntityId == entity.Id)
            .OrderByDescending(x => x.IsVerified)
            .ThenBy(x => x.Alias)
            .Select(x => new EntityAliasItem
            {
                Id = x.Id,
                Alias = x.Alias,
                NormalizedAlias = x.NormalizedAlias,
                LanguageCode = x.LanguageCode,
                AliasType = x.AliasType,
                SourceType = x.SourceType,
                Confidence = x.Confidence,
                IsVerified = x.IsVerified
            }).ToListAsync(ct);
    }

    public async Task<EntityAliasItem> AddAliasAsync(
        Guid id, UpsertEntityAliasRequest request, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var entity = await GetOwnedResolvedEntityAsync(id, ct);
        var normalized = _normalizer.Normalize(request.Alias, entity.EntityType);
        if (string.IsNullOrWhiteSpace(normalized.NormalizedKey))
            throw new ArgumentException("Alias is required.");
        var exists = await _db.EntityAliases.AnyAsync(x =>
            x.EntityId == entity.Id
            && x.NormalizedAlias == normalized.NormalizedKey, ct);
        if (exists)
            throw new InvalidOperationException("The alias already exists.");
        var now = DateTime.UtcNow;
        var alias = new EntityAlias
        {
            Id = Guid.NewGuid(),
            EntityId = entity.Id,
            UserId = userId,
            WorkspaceId = entity.WorkspaceId,
            Alias = request.Alias.Trim(),
            NormalizedAlias = normalized.NormalizedKey,
            LanguageCode = request.LanguageCode,
            AliasType = request.AliasType,
            SourceType = "manual",
            Confidence = request.Confidence ?? (request.IsVerified ? 1m : 0.5m),
            IsVerified = request.IsVerified,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.EntityAliases.Add(alias);
        entity.RowVersion++;
        entity.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return new EntityAliasItem
        {
            Id = alias.Id,
            Alias = alias.Alias,
            NormalizedAlias = alias.NormalizedAlias,
            LanguageCode = alias.LanguageCode,
            AliasType = alias.AliasType,
            SourceType = alias.SourceType,
            Confidence = alias.Confidence,
            IsVerified = alias.IsVerified
        };
    }

    public async Task<EntityAliasItem> UpdateAliasAsync(
        Guid id, Guid aliasId, UpsertEntityAliasRequest request,
        CancellationToken ct = default)
    {
        var entity = await GetOwnedResolvedEntityAsync(id, ct);
        var alias = await _db.EntityAliases.FirstOrDefaultAsync(
            x => x.Id == aliasId && x.EntityId == entity.Id, ct)
            ?? throw new NotFoundException("EntityAlias", aliasId);
        var normalized = _normalizer.Normalize(request.Alias, entity.EntityType);
        alias.Alias = request.Alias.Trim();
        alias.NormalizedAlias = normalized.NormalizedKey;
        alias.LanguageCode = request.LanguageCode;
        alias.AliasType = request.AliasType;
        alias.IsVerified = request.IsVerified;
        alias.Confidence = request.Confidence;
        alias.UpdatedAt = DateTime.UtcNow;
        entity.RowVersion++;
        entity.UpdatedAt = alias.UpdatedAt;
        await _db.SaveChangesAsync(ct);
        return (await GetAliasesAsync(entity.Id, ct)).Single(x => x.Id == alias.Id);
    }

    public async Task<bool> DeleteAliasAsync(
        Guid id, Guid aliasId, CancellationToken ct = default)
    {
        var entity = await GetOwnedResolvedEntityAsync(id, ct);
        var alias = await _db.EntityAliases.FirstOrDefaultAsync(
            x => x.Id == aliasId && x.EntityId == entity.Id, ct);
        if (alias == null) return false;
        _db.EntityAliases.Remove(alias);
        entity.RowVersion++;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<EntityMentionItem>> GetMentionsAsync(
        Guid id, int limit = 100, CancellationToken ct = default)
    {
        var entity = await GetOwnedResolvedEntityAsync(id, ct);
        return await _db.EntityMentions.AsNoTracking()
            .Where(x => x.EntityId == entity.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new EntityMentionItem
            {
                Id = x.Id,
                DocumentId = x.DocumentId,
                ChunkId = x.ChunkId,
                MentionText = x.MentionText,
                ContextText = x.ContextText,
                OccurrenceCount = x.OccurrenceCount,
                ResolutionStatus = x.ResolutionStatus,
                ResolutionMethod = x.ResolutionMethod,
                ResolutionScore = x.ResolutionScore,
                CreatedAt = x.CreatedAt
            }).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EntityRelationItem>> GetRelationsAsync(
        Guid id, int limit = 100, CancellationToken ct = default)
    {
        var entity = await GetOwnedResolvedEntityAsync(id, ct);
        return await _db.EntityRelations.AsNoTracking()
            .Where(x => x.SourceEntityId == entity.Id || x.TargetEntityId == entity.Id)
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new EntityRelationItem
            {
                Id = x.Id,
                SourceEntityId = x.SourceEntityId,
                TargetEntityId = x.TargetEntityId,
                RelationType = x.RelationType,
                EvidenceDocumentId = x.EvidenceDocumentId,
                EvidenceText = x.EvidenceText,
                Confidence = x.Confidence,
                CreatedAt = x.CreatedAt
            }).ToListAsync(ct);
    }

    private async Task<Entity> GetOwnedResolvedEntityAsync(
        Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var original = await _db.Entities.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == id && x.UserId == userId, ct)
            ?? throw new NotFoundException("Entity", id);
        var resolved = await _redirectResolver.ResolveAsync(
            id, original.WorkspaceId, ct);
        return await _db.Entities.FirstAsync(
            x => x.Id == resolved.EntityId && x.UserId == userId, ct);
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
