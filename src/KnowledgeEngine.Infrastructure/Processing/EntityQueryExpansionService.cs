using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class EntityQueryExpansionService(
    AppDbContext db,
    IEntityRedirectResolver redirects,
    IChineseTokenizer tokenizer,
    IOptions<EntityResolutionSettings> settings) : IEntityQueryExpansionService
{
    public async Task<EntityQueryExpansion> ExpandAsync(
        Guid userId,
        string query,
        IReadOnlyCollection<Guid>? explicitEntityIds = null,
        CancellationToken ct = default)
    {
        var requested = explicitEntityIds?.Distinct().ToList() ?? [];
        var enableDetection = settings.Value.Enabled
            && settings.Value.EnableEntitySearchExpansion;
        var raw = query.Trim().ToLowerInvariant();
        var tokens = tokenizer.Tokenize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Append(raw)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = await db.Entities.AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsArchived)
            .OrderByDescending(x => x.MentionCount)
            .Take(10000)
            .Select(x => new
            {
                x.Id,
                x.WorkspaceId,
                x.CanonicalName,
                x.Name,
                x.PreferredNameZh,
                x.PreferredNameEn,
                x.Abbreviation,
                x.NormalizedKey,
                x.NormalizedName,
                x.Status
            })
            .ToListAsync(ct);
        var candidateIds = candidates.Select(x => x.Id).ToList();
        var aliases = await db.EntityAliases.AsNoTracking()
            .Where(x => candidateIds.Contains(x.EntityId))
            .Select(x => new
            {
                x.EntityId,
                x.Alias,
                x.NormalizedAlias,
                x.IsVerified
            })
            .ToListAsync(ct);
        var aliasesByEntity = aliases.GroupBy(x => x.EntityId)
            .ToDictionary(x => x.Key, x => x.ToList());

        foreach (var entity in enableDetection ? candidates : candidates.Take(0))
        {
            var names = new[]
            {
                entity.NormalizedKey,
                entity.NormalizedName,
                entity.CanonicalName,
                entity.Name,
                entity.PreferredNameZh,
                entity.PreferredNameEn,
                entity.Abbreviation
            }.Where(x => !string.IsNullOrWhiteSpace(x))
             .Select(x => x!.Trim().ToLowerInvariant());
            var aliasNames = aliasesByEntity.GetValueOrDefault(entity.Id, [])
                .Select(x => x.NormalizedAlias.Trim().ToLowerInvariant());
            if (names.Concat(aliasNames).Any(name =>
                name.Length >= 2
                && (tokens.Contains(name)
                    || raw.Contains(name, StringComparison.OrdinalIgnoreCase))))
            {
                requested.Add(entity.Id);
            }
        }

        var resolvedIds = new HashSet<Guid>();
        foreach (var id in requested.Distinct().Take(20))
        {
            var entity = candidates.FirstOrDefault(x => x.Id == id)
                ?? await db.Entities.AsNoTracking()
                    .Where(x => x.Id == id && x.UserId == userId)
                    .Select(x => new
                    {
                        x.Id,
                        x.WorkspaceId,
                        x.CanonicalName,
                        x.Name,
                        x.PreferredNameZh,
                        x.PreferredNameEn,
                        x.Abbreviation,
                        x.NormalizedKey,
                        x.NormalizedName,
                        x.Status
                    })
                    .FirstOrDefaultAsync(ct);
            if (entity == null) continue;
            var resolved = await redirects.ResolveAsync(id, entity.WorkspaceId, ct);
            resolvedIds.Add(resolved.EntityId);
        }
        var finalEntities = await db.Entities.AsNoTracking()
            .Where(x => resolvedIds.Contains(x.Id) && x.UserId == userId)
            .ToListAsync(ct);
        var finalAliases = await db.EntityAliases.AsNoTracking()
            .Where(x => resolvedIds.Contains(x.EntityId) && x.IsVerified)
            .Select(x => x.Alias)
            .Distinct()
            .Take(50)
            .ToListAsync(ct);
        return new EntityQueryExpansion
        {
            EntityIds = finalEntities.Select(x => x.Id).ToList(),
            CanonicalTerms = finalEntities.SelectMany(x => new[]
                {
                    x.CanonicalName ?? x.Name,
                    x.PreferredNameZh,
                    x.PreferredNameEn,
                    x.Abbreviation
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList(),
            VerifiedAliases = finalAliases
        };
    }
}
