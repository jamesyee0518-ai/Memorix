using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class EntityKnowledgeGraphService(
    AppDbContext db,
    IEntityRedirectResolver redirects,
    IOptions<EntityResolutionSettings> settings) : IKnowledgeGraphService
{
    public async Task<EntityGraphDto> GetGraphAsync(
        Guid userId,
        Guid? workspaceId,
        string? entityType,
        string? language,
        int limit = 300,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        limit = Math.Clamp(limit, 1, 1000);
        var query = db.Entities.AsNoTracking().Where(x =>
            x.UserId == userId && x.Status != "merged" && !x.IsArchived);
        if (workspaceId.HasValue)
        {
            var value = workspaceId.Value.ToString();
            query = query.Where(x => x.WorkspaceId == value);
        }
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var value = entityType.Trim().ToUpperInvariant();
            query = query.Where(x => x.EntityType.ToUpper() == value);
        }
        var entities = await query
            .OrderByDescending(x => x.MentionCount)
            .ThenByDescending(x => x.SourceCount)
            .Take(limit)
            .ToListAsync(ct);
        return await BuildGraphAsync(entities, language, ct);
    }

    public async Task<EntityGraphDto> GetNeighborsAsync(
        Guid userId,
        Guid entityId,
        string? language,
        int limit = 100,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var original = await db.Entities.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == entityId && x.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Entity not found: {entityId}");
        var resolved = await redirects.ResolveAsync(
            entityId, original.WorkspaceId, ct);
        var neighborIds = await db.EntityRelations.AsNoTracking()
            .Where(x => x.SourceEntityId == resolved.EntityId
                || x.TargetEntityId == resolved.EntityId)
            .Select(x => x.SourceEntityId == resolved.EntityId
                ? x.TargetEntityId
                : x.SourceEntityId)
            .Distinct()
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        neighborIds.Add(resolved.EntityId);
        var entities = await db.Entities.AsNoTracking()
            .Where(x => neighborIds.Contains(x.Id)
                && x.UserId == userId
                && x.Status != "merged")
            .ToListAsync(ct);
        return await BuildGraphAsync(entities, language, ct);
    }

    public async Task<IReadOnlyList<EntityGraphDocumentDto>> GetDocumentsAsync(
        Guid userId,
        Guid entityId,
        string? language,
        int limit = 100,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var original = await db.Entities.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == entityId && x.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Entity not found: {entityId}");
        var resolved = await redirects.ResolveAsync(
            entityId, original.WorkspaceId, ct);
        var entity = await db.Entities.AsNoTracking().FirstAsync(
            x => x.Id == resolved.EntityId, ct);
        var displayName = DisplayName(entity, language);
        var rows = await (
            from link in db.DocumentEntities.AsNoTracking()
            join document in db.Documents.AsNoTracking()
                on link.DocumentId equals document.Id
            where link.EntityId == resolved.EntityId && document.UserId == userId
            orderby link.MentionCount descending, document.UpdatedAt descending
            select new { link, document }
        ).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
        var documentIds = rows.Select(x => x.document.Id).ToList();
        var firstMentions = await db.EntityMentions.AsNoTracking()
            .Where(x => x.EntityId == resolved.EntityId
                && documentIds.Contains(x.DocumentId))
            .GroupBy(x => x.DocumentId)
            .Select(x => new
            {
                DocumentId = x.Key,
                Mention = x.OrderBy(y => y.CreatedAt)
                    .Select(y => y.MentionText)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Mention, ct);
        return rows.Select(x => new EntityGraphDocumentDto
        {
            DocumentId = x.document.Id,
            Title = x.document.Title,
            OriginalMention = firstMentions.GetValueOrDefault(x.document.Id)
                ?? x.link.FirstMention,
            DisplayEntityName = displayName,
            MentionCount = x.link.MentionCount,
            Evidence = x.link.Evidence
        }).ToList();
    }

    private async Task<EntityGraphDto> BuildGraphAsync(
        IReadOnlyList<Entity> entities,
        string? language,
        CancellationToken ct)
    {
        var ids = entities.Select(x => x.Id).ToList();
        if (ids.Count == 0) return new EntityGraphDto();
        var relations = await db.EntityRelations.AsNoTracking()
            .Where(x => ids.Contains(x.SourceEntityId)
                && ids.Contains(x.TargetEntityId)
                && x.SourceEntityId != x.TargetEntityId)
            .ToListAsync(ct);
        var documentLinks = await db.DocumentEntities.AsNoTracking()
            .Where(x => ids.Contains(x.EntityId))
            .Select(x => new { x.EntityId, x.DocumentId })
            .ToListAsync(ct);
        var documentsByEntity = documentLinks
            .GroupBy(x => x.EntityId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<Guid>)x.Select(y => y.DocumentId).Distinct().ToList());
        var edges = relations
            .GroupBy(x => new
            {
                x.SourceEntityId,
                x.TargetEntityId,
                Type = string.IsNullOrWhiteSpace(x.RelationType)
                    ? "RELATED_TO"
                    : x.RelationType
            })
            .Select(x => new EntityGraphEdgeDto
            {
                SourceEntityId = x.Key.SourceEntityId,
                TargetEntityId = x.Key.TargetEntityId,
                RelationType = x.Key.Type,
                Weight = x.Count(),
                EvidenceDocumentIds = x.Where(y => y.EvidenceDocumentId.HasValue)
                    .Select(y => y.EvidenceDocumentId!.Value)
                    .Distinct()
                    .ToList(),
                EvidenceDocumentCount = x.Where(y => y.EvidenceDocumentId.HasValue)
                    .Select(y => y.EvidenceDocumentId)
                    .Distinct()
                    .Count()
            })
            .OrderByDescending(x => x.Weight)
            .ToList();
        var degree = edges
            .SelectMany(x => new[] { x.SourceEntityId, x.TargetEntityId })
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.Count());
        var nodes = entities.Select(x => new EntityGraphNodeDto
        {
            Id = x.Id,
            Label = DisplayName(x, language),
            CanonicalName = x.CanonicalName ?? x.Name,
            EntityType = x.EntityType,
            MentionCount = x.MentionCount,
            SourceCount = x.SourceCount,
            Degree = degree.GetValueOrDefault(x.Id),
            DocumentIds = documentsByEntity.GetValueOrDefault(x.Id, [])
        }).OrderByDescending(x => x.Degree)
          .ThenByDescending(x => x.MentionCount)
          .ToList();
        return new EntityGraphDto
        {
            Nodes = nodes,
            Edges = edges,
            DocumentCount = documentLinks.Select(x => x.DocumentId).Distinct().Count()
        };
    }

    private static string DisplayName(Entity entity, string? language)
    {
        if (language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true
            && !string.IsNullOrWhiteSpace(entity.PreferredNameZh))
            return entity.PreferredNameZh;
        if (language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
            && !string.IsNullOrWhiteSpace(entity.PreferredNameEn))
            return entity.PreferredNameEn;
        return entity.CanonicalName ?? entity.DisplayName ?? entity.Name;
    }

    private void EnsureEnabled()
    {
        if (!settings.Value.Enabled || !settings.Value.EnableGraphBackend)
            throw new InvalidOperationException("The canonical entity graph is disabled.");
    }
}
