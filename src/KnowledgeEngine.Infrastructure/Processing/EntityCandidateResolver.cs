using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// Bounded, workspace-safe candidate retrieval and deterministic scoring.
/// Expensive vector and LLM channels are intentionally downstream of this
/// blocking layer so the resolver never performs an all-pairs entity scan.
/// </summary>
public sealed partial class EntityCandidateResolver(
    IAppDbContext db,
    IEntityNameNormalizer normalizer,
    ITerminologyService terminology,
    IEntityVectorSimilarityService vectorSimilarity,
    IOptions<EntityResolutionSettings> options,
    ILogger<EntityCandidateResolver> logger) : IEntityCandidateResolver
{
    private readonly EntityResolutionSettings _settings = options.Value;
    public const string ResolverVersion = "entity_candidate_v1";
    private const int MaxBlockingCandidates = 200;

    [GeneratedRegex(@"(?i)(?:^|[\s._-]|(?<=[a-z]))(?:v)?(\d+(?:\.\d+)*(?:[a-z])?)(?:$|[\s._-])")]
    private static partial Regex VersionRegex();

    public decimal GetAutoLinkThreshold(string entityType) =>
        entityType.ToUpperInvariant() switch
        {
            "PERSON" => 0.96m,
            "COMPANY" => 0.95m,
            "MODEL" => 0.95m,
            "TECHNOLOGY" => 0.92m,
            "CONCEPT" => 0.93m,
            _ => 0.92m
        };

    public bool ShouldAutoLink(EntityCandidateMatch match, string entityType) =>
        _settings.EnableAutoLink
        && !_settings.ShadowMode
        && !match.HardBlocked
        && match.TotalScore >= GetAutoLinkThreshold(entityType);

    public async Task<IReadOnlyList<EntityCandidateMatch>> RetrieveAsync(
        EntityCandidateRequest request,
        CancellationToken ct = default)
    {
        var key = request.Normalized.NormalizedKey;
        if (key.Length == 0 || request.WorkspaceId.Length == 0) return [];

        var terminologyVariants = await ExpandTerminologyAsync(request, ct);
        terminologyVariants.Add(key);
        var blockPrefixes = terminologyVariants
            .Select(BlockPrefix)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToList();
        var abbreviation = request.Normalized.Abbreviation?.ToLowerInvariant();

        var entityMap = new Dictionary<Guid, Entity>();
        foreach (var prefix in blockPrefixes)
        {
            var blocked = await db.Entities.AsNoTracking()
                .Where(x => x.WorkspaceId == request.WorkspaceId
                    && x.EntityType.ToUpper() == request.Normalized.EntityType
                    && x.Status != "merged"
                    && !x.IsArchived
                    && ((x.NormalizedKey != null && x.NormalizedKey.StartsWith(prefix))
                        || (x.NormalizedName != null && x.NormalizedName.StartsWith(prefix))))
                .OrderByDescending(x => x.IsVerified)
                .ThenByDescending(x => x.UsageCount)
                .Take(50)
                .ToListAsync(ct);
            foreach (var entity in blocked)
                entityMap.TryAdd(entity.Id, entity);
            if (entityMap.Count >= MaxBlockingCandidates) break;
        }

        if (abbreviation != null && entityMap.Count < MaxBlockingCandidates)
        {
            var abbreviationMatches = await db.Entities.AsNoTracking()
                .Where(x => x.WorkspaceId == request.WorkspaceId
                    && x.EntityType.ToUpper() == request.Normalized.EntityType
                    && x.Status != "merged"
                    && !x.IsArchived
                    && x.Abbreviation != null
                    && x.Abbreviation.ToLower() == abbreviation)
                .Take(50)
                .ToListAsync(ct);
            foreach (var entity in abbreviationMatches)
                entityMap.TryAdd(entity.Id, entity);
        }

        var relationMatches = await FindRelationMatchesAsync(request, ct);
        if (relationMatches.Count > 0 && entityMap.Count < MaxBlockingCandidates)
        {
            var relatedEntities = await db.Entities.AsNoTracking()
                .Where(x => relationMatches.Contains(x.Id)
                    && x.WorkspaceId == request.WorkspaceId
                    && x.EntityType.ToUpper() == request.Normalized.EntityType
                    && x.Status != "merged"
                    && !x.IsArchived)
                .Take(50)
                .ToListAsync(ct);
            foreach (var entity in relatedEntities)
                entityMap.TryAdd(entity.Id, entity);
        }

        if (_settings.EnableVectorCandidates && entityMap.Count < MaxBlockingCandidates)
        {
            var semanticPool = await db.Entities.AsNoTracking()
                .Where(x => x.WorkspaceId == request.WorkspaceId
                    && x.EntityType.ToUpper() == request.Normalized.EntityType
                    && x.Status != "merged"
                    && !x.IsArchived)
                .OrderByDescending(x => x.IsVerified)
                .ThenByDescending(x => x.UsageCount)
                .Take(Math.Clamp(_settings.SemanticPoolSize, 1, 200))
                .ToListAsync(ct);
            foreach (var entity in semanticPool)
                entityMap.TryAdd(entity.Id, entity);
        }

        var entities = entityMap.Values
            .OrderByDescending(x => x.IsVerified)
            .ThenByDescending(x => x.UsageCount)
            .Take(MaxBlockingCandidates)
            .ToList();

        if (entities.Count == 0) return [];

        var entityIds = entities.Select(x => x.Id).ToList();
        var aliases = await db.EntityAliases.AsNoTracking()
            .Where(x => entityIds.Contains(x.EntityId)
                && (x.ValidTo == null || x.ValidTo > DateTime.UtcNow))
            .ToListAsync(ct);
        var aliasesByEntity = aliases
            .GroupBy(x => x.EntityId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var sourceMatches = await FindSourceDomainMatchesAsync(
            entityIds, request.SourceDomain, ct);
        var queryContext = string.Join(' ', new[]
        {
            request.Mention, request.Context, request.Description
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var vectorScores = _settings.EnableVectorCandidates
            ? await vectorSimilarity.ScoreAsync(
                request.WorkspaceId, request.Mention, queryContext, entities, ct)
            : new Dictionary<Guid, EntityVectorScores>();

        var matches = new List<EntityCandidateMatch>(entities.Count);
        foreach (var entity in entities)
        {
            var candidateKey = entity.NormalizedKey ?? entity.NormalizedName
                ?? normalizer.Normalize(entity.Name, entity.EntityType).NormalizedKey;
            var nameScore = Similarity(key, candidateKey);
            if (vectorScores.TryGetValue(entity.Id, out var vectorScore))
                nameScore = Math.Max(nameScore, vectorScore.NameScore);
            var entityAliases = aliasesByEntity.GetValueOrDefault(entity.Id) ?? [];
            var aliasScore = entityAliases.Count == 0
                ? 0m
                : entityAliases.Max(x =>
                    Similarity(key, x.NormalizedAlias) * (x.IsVerified ? 1m : 0.85m));

            var terminologyScore = terminologyVariants.Any(x =>
                string.Equals(x, candidateKey, StringComparison.Ordinal)
                || entityAliases.Any(a => string.Equals(
                    a.NormalizedAlias, x, StringComparison.Ordinal)))
                ? 1m
                : 0m;
            aliasScore = Math.Max(aliasScore, terminologyScore);

            var descriptionScore = TokenSimilarity(
                request.Description, entity.Description);
            if (vectorScore != null)
                descriptionScore = Math.Max(
                    descriptionScore, vectorScore.DescriptionScore);
            var contextScore = TokenSimilarity(queryContext,
                string.Join(' ', entity.Name, entity.Description));
            var sourceScore = sourceMatches.Contains(entity.Id) ? 1m : 0m;
            var relationScore = relationMatches.Contains(entity.Id) ? 1m : 0m;
            var hardBlocked = HasVersionConflict(key, candidateKey);
            var reasons = BuildReasonCodes(
                nameScore, aliasScore, descriptionScore, contextScore,
                relationScore, sourceScore, hardBlocked);

            var total = hardBlocked
                ? 0m
                : Clamp(
                    0.30m * nameScore
                    + 0.20m * aliasScore
                    + 0.20m * descriptionScore
                    + 0.15m * contextScore
                    + 0.10m * relationScore
                    + 0.05m * sourceScore);
            matches.Add(new EntityCandidateMatch
            {
                EntityId = entity.Id,
                NameScore = nameScore,
                AliasScore = aliasScore,
                DescriptionScore = descriptionScore,
                ContextScore = contextScore,
                RelationScore = relationScore,
                SourceScore = sourceScore,
                TotalScore = total,
                HardBlocked = hardBlocked,
                ReasonCodes = reasons
            });
        }

        return matches
            .OrderBy(x => x.HardBlocked)
            .ThenByDescending(x => x.TotalScore)
            .ThenBy(x => x.EntityId)
            .Take(Math.Min(
                Math.Clamp(request.TopK, 1, 20),
                Math.Clamp(_settings.CandidateTopK, 1, 20)))
            .ToList();
    }

    private async Task<HashSet<Guid>> FindSourceDomainMatchesAsync(
        IReadOnlyCollection<Guid> entityIds,
        string? sourceDomain,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceDomain)) return [];
        return (await (
            from de in db.DocumentEntities.AsNoTracking()
            join document in db.Documents.AsNoTracking()
                on de.DocumentId equals document.Id
            where entityIds.Contains(de.EntityId)
                && document.SourceDomain == sourceDomain
            select de.EntityId
        ).Distinct().ToListAsync(ct)).ToHashSet();
    }

    private async Task<HashSet<Guid>> FindRelationMatchesAsync(
        EntityCandidateRequest request,
        CancellationToken ct)
    {
        if (request.CooccurringNormalizedKeys.Count == 0) return [];
        var keys = request.CooccurringNormalizedKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToList();
        if (keys.Count == 0) return [];

        var seedIds = await db.Entities.AsNoTracking()
            .Where(x => x.WorkspaceId == request.WorkspaceId
                && x.Status != "merged"
                && x.NormalizedKey != null
                && keys.Contains(x.NormalizedKey))
            .Select(x => x.Id)
            .Take(50)
            .ToListAsync(ct);
        if (seedIds.Count == 0) return [];

        var explicitRelations = await db.EntityRelations.AsNoTracking()
            .Where(x => x.UserId == request.UserId
                && (seedIds.Contains(x.SourceEntityId)
                    || seedIds.Contains(x.TargetEntityId)))
            .Select(x => seedIds.Contains(x.SourceEntityId)
                ? x.TargetEntityId
                : x.SourceEntityId)
            .Take(100)
            .ToListAsync(ct);

        var documentIds = await db.DocumentEntities.AsNoTracking()
            .Where(x => seedIds.Contains(x.EntityId))
            .Select(x => x.DocumentId)
            .Distinct()
            .Take(100)
            .ToListAsync(ct);
        var coDocumentIds = documentIds.Count == 0
            ? []
            : await db.DocumentEntities.AsNoTracking()
                .Where(x => documentIds.Contains(x.DocumentId)
                    && !seedIds.Contains(x.EntityId))
                .Select(x => x.EntityId)
                .Distinct()
                .Take(100)
                .ToListAsync(ct);

        return explicitRelations.Concat(coDocumentIds).ToHashSet();
    }

    private async Task<HashSet<string>> ExpandTerminologyAsync(
        EntityCandidateRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.WorkspaceId, out var workspaceId)) return [];
        try
        {
            var expanded = await terminology.ExpandQueryAsync(
                request.UserId, workspaceId, request.Mention, ct: ct);
            return expanded
                .Select(x => normalizer.Normalize(
                    x, request.Normalized.EntityType).NormalizedKey)
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Terminology candidate expansion failed for workspace {WorkspaceId}",
                request.WorkspaceId);
            return [];
        }
    }

    private static IReadOnlyList<string> BuildReasonCodes(
        decimal name,
        decimal alias,
        decimal description,
        decimal context,
        decimal relation,
        decimal source,
        bool hardBlocked)
    {
        var reasons = new List<string>();
        if (hardBlocked) reasons.Add("MODEL_VERSION_CONFLICT");
        if (name >= 0.85m) reasons.Add("NAME_SIMILAR");
        if (alias >= 0.85m) reasons.Add("ALIAS_OR_TERMINOLOGY_MATCH");
        if (description >= 0.60m) reasons.Add("DESCRIPTION_SIMILAR");
        if (context >= 0.60m) reasons.Add("CONTEXT_SIMILAR");
        if (relation > 0m) reasons.Add("RELATION_NEIGHBOR_MATCH");
        if (source > 0m) reasons.Add("SOURCE_DOMAIN_MATCH");
        if (reasons.Count == 0) reasons.Add("WEAK_BLOCKING_CANDIDATE");
        return reasons;
    }

    internal static decimal Similarity(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return 1m;
        if (left.Length == 0 || right.Length == 0) return 0m;
        var a = Bigrams(left);
        var b = Bigrams(right);
        if (a.Count == 0 || b.Count == 0)
            return left[0] == right[0] ? 0.5m : 0m;
        var overlap = a.Intersect(b).Count();
        return Clamp(2m * overlap / (a.Count + b.Count));
    }

    private static decimal TokenSimilarity(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return 0m;
        var a = Tokens(left);
        var b = Tokens(right);
        if (a.Count == 0 || b.Count == 0) return 0m;
        return Clamp((decimal)a.Intersect(b).Count() / a.Union(b).Count());
    }

    public static bool HasVersionConflict(string left, string right)
    {
        var leftVersion = VersionRegex().Matches($" {left} ")
            .Select(x => x.Groups[1].Value).FirstOrDefault();
        var rightVersion = VersionRegex().Matches($" {right} ")
            .Select(x => x.Groups[1].Value).FirstOrDefault();
        if (leftVersion == null || rightVersion == null
            || string.Equals(leftVersion, rightVersion, StringComparison.OrdinalIgnoreCase))
            return false;

        var leftStem = VersionRegex().Replace($" {left} ", " ").Trim();
        var rightStem = VersionRegex().Replace($" {right} ", " ").Trim();
        return Similarity(leftStem, rightStem) >= 0.75m;
    }

    private static HashSet<string> Bigrams(string value)
    {
        var compact = new string(value.Where(char.IsLetterOrDigit).ToArray())
            .ToLowerInvariant();
        if (compact.Length < 2) return compact.Length == 0 ? [] : [compact];
        return Enumerable.Range(0, compact.Length - 1)
            .Select(i => compact.Substring(i, 2))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Tokens(string value) =>
        Regex.Split(value.ToLowerInvariant(), @"[^\p{L}\p{N}+#.]+")
            .Where(x => x.Length > 1)
            .ToHashSet(StringComparer.Ordinal);

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 1m);
    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    private static string BlockPrefix(string value)
    {
        if (value.Length == 0) return string.Empty;
        var length = value.Any(IsCjk) ? Math.Min(2, value.Length) : Math.Min(4, value.Length);
        return value[..length];
    }
}
