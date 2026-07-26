using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class EntityDisambiguationService(
    IAppDbContext db,
    ILlmService llm,
    IOptions<EntityResolutionSettings> options,
    ILogger<EntityDisambiguationService> logger) : IEntityDisambiguationService
{
    public const string PromptVersion = "entity_disambiguation_v1";
    private readonly EntityResolutionSettings _settings = options.Value;
    private static readonly HashSet<string> Decisions =
    [
        "SAME_ENTITY",
        "DIFFERENT_ENTITY",
        "INSUFFICIENT_EVIDENCE",
        "RELATED_BUT_NOT_SAME"
    ];

    public async Task<EntityDisambiguationResult> DecideAsync(
        EntityDisambiguationRequest request,
        CancellationToken ct = default)
    {
        var usableCandidates = request.Candidates
            .Where(x => !x.HardBlocked)
            .OrderByDescending(x => x.TotalScore)
            .Take(5)
            .ToList();
        if (!_settings.EnableLlmDisambiguation || usableCandidates.Count == 0)
            return Fallback("LLM_DISAMBIGUATION_DISABLED_OR_NO_CANDIDATE");

        var ids = usableCandidates.Select(x => x.EntityId).ToList();
        var entities = await db.Entities.AsNoTracking()
            .Where(x => ids.Contains(x.Id)
                && x.WorkspaceId == request.WorkspaceId
                && x.Status != "merged")
            .ToListAsync(ct);
        if (entities.Count == 0) return Fallback("NO_VALID_CANDIDATE");

        var aliases = await db.EntityAliases.AsNoTracking()
            .Where(x => ids.Contains(x.EntityId) && x.IsVerified)
            .ToListAsync(ct);
        var aliasesByEntity = aliases
            .GroupBy(x => x.EntityId)
            .ToDictionary(x => x.Key, x => x.Select(a => a.Alias).Take(20).ToList());
        var externalIds = await db.EntityExternalIds.AsNoTracking()
            .Where(x => ids.Contains(x.EntityId) && x.IsVerified)
            .ToListAsync(ct);
        var externalByEntity = externalIds
            .GroupBy(x => x.EntityId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(id => new { id.IdType, id.IdValue }).Take(20).ToList());
        var relations = await db.EntityRelations.AsNoTracking()
            .Where(x => ids.Contains(x.SourceEntityId)
                || ids.Contains(x.TargetEntityId))
            .Take(100)
            .ToListAsync(ct);
        var neighborIds = relations
            .SelectMany(x => new[] { x.SourceEntityId, x.TargetEntityId })
            .Except(ids)
            .Distinct()
            .ToList();
        var neighborNames = await db.Entities.AsNoTracking()
            .Where(x => neighborIds.Contains(x.Id)
                && x.WorkspaceId == request.WorkspaceId)
            .ToDictionaryAsync(
                x => x.Id,
                x => x.CanonicalName ?? x.Name,
                ct);
        var relationsByEntity = ids.ToDictionary(
            id => id,
            id => relations
                .Where(x => x.SourceEntityId == id || x.TargetEntityId == id)
                .Select(x =>
                {
                    var outbound = x.SourceEntityId == id;
                    var neighborId = outbound ? x.TargetEntityId : x.SourceEntityId;
                    return new CandidateRelationContext
                    {
                        RelationType = x.RelationType,
                        Direction = outbound ? "outbound" : "inbound",
                        NeighborEntityId = neighborId,
                        NeighborName = neighborNames.GetValueOrDefault(neighborId)
                    };
                })
                .Take(20)
                .ToList());

        var candidatePayload = usableCandidates.Join(
            entities,
            score => score.EntityId,
            entity => entity.Id,
            (score, entity) => new
            {
                entity_id = entity.Id,
                canonical_name = entity.CanonicalName ?? entity.Name,
                preferred_name_zh = entity.PreferredNameZh,
                preferred_name_en = entity.PreferredNameEn,
                abbreviation = entity.Abbreviation,
                entity_type = entity.EntityType,
                description = entity.Description,
                verified_aliases = aliasesByEntity.GetValueOrDefault(entity.Id) ?? [],
                verified_external_ids = externalByEntity.GetValueOrDefault(entity.Id) ?? [],
                relation_neighbors = relationsByEntity.GetValueOrDefault(entity.Id) ?? [],
                scores = new
                {
                    name = score.NameScore,
                    alias = score.AliasScore,
                    description = score.DescriptionScore,
                    context = score.ContextScore,
                    relation = score.RelationScore,
                    source = score.SourceScore,
                    total = score.TotalScore
                },
                reason_codes = score.ReasonCodes
            }).ToList();

        var systemPrompt = """
            You are an entity identity disambiguation engine.
            Decide whether the mention refers to exactly one candidate identity.
            Hard constraints were applied before this call and must never be overridden.
            Similar names, related products, parent companies, model families, and model
            versions are not automatically the same entity.
            If evidence is weak or ambiguous, choose INSUFFICIENT_EVIDENCE.
            Return JSON only:
            {
              "decision": "SAME_ENTITY|DIFFERENT_ENTITY|INSUFFICIENT_EVIDENCE|RELATED_BUT_NOT_SAME",
              "candidate_entity_id": "uuid or null",
              "confidence": 0.0,
              "reason_codes": ["UPPER_SNAKE_CASE"],
              "explanation": "one concise sentence"
            }
            """;
        var userPrompt = JsonSerializer.Serialize(new
        {
            mention = request.Mention,
            entity_type = request.EntityType,
            context = request.Context,
            document = new
            {
                title = request.DocumentTitle,
                source_domain = request.SourceDomain,
                published_at = request.PublishedAt,
                language = request.Language
            },
            candidates = candidatePayload
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(_settings.LlmTimeoutSeconds, 5, 120)));
        try
        {
            var response = await llm.CompleteAsync(
                systemPrompt, userPrompt, _settings.LlmModel, timeout.Token);
            var parsed = Parse(response.Content);
            if (parsed == null) return Fallback(
                "LLM_INVALID_STRUCTURED_OUTPUT", response.Model, response.Content);

            var decision = parsed.Decision?.Trim().ToUpperInvariant();
            if (decision == null || !Decisions.Contains(decision))
                return Fallback("LLM_INVALID_DECISION", response.Model, response.Content);
            var allowedIds = entities.Select(x => x.Id).ToHashSet();
            if (decision == "SAME_ENTITY"
                && (!parsed.CandidateEntityId.HasValue
                    || !allowedIds.Contains(parsed.CandidateEntityId.Value)))
                return Fallback(
                    "LLM_CANDIDATE_OUT_OF_SCOPE", response.Model, response.Content);

            return new EntityDisambiguationResult
            {
                Decision = decision,
                CandidateEntityId = parsed.CandidateEntityId,
                Confidence = Math.Clamp(parsed.Confidence, 0m, 1m),
                ReasonCodes = NormalizeReasonCodes(parsed.ReasonCodes),
                Explanation = Truncate(parsed.Explanation, 1000),
                Model = response.Model,
                InputTokens = response.InputTokens,
                OutputTokens = response.OutputTokens,
                PromptVersion = PromptVersion,
                RawOutput = Truncate(response.Content, 8000),
                IsFallback = false
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Entity disambiguation timed out for workspace {WorkspaceId}",
                request.WorkspaceId);
            return Fallback("LLM_TIMEOUT");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Entity disambiguation degraded for workspace {WorkspaceId}",
                request.WorkspaceId);
            return Fallback("LLM_UNAVAILABLE");
        }
    }

    private static DisambiguationOutput? Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonSerializer.Deserialize<DisambiguationOutput>(
                value[start..(end + 1)],
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> NormalizeReasonCodes(
        IReadOnlyCollection<string>? values) =>
        (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant())
            .Where(x => x.Length <= 100)
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToList();

    private static EntityDisambiguationResult Fallback(
        string reason,
        string? model = null,
        string? rawOutput = null) =>
        new()
        {
            Decision = "INSUFFICIENT_EVIDENCE",
            Confidence = 0m,
            ReasonCodes = [reason],
            Model = model,
            PromptVersion = PromptVersion,
            RawOutput = Truncate(rawOutput, 8000),
            IsFallback = true
        };

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];

    private sealed class DisambiguationOutput
    {
        [JsonPropertyName("decision")]
        public string? Decision { get; set; }

        [JsonPropertyName("candidate_entity_id")]
        public Guid? CandidateEntityId { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }

        [JsonPropertyName("reason_codes")]
        public List<string>? ReasonCodes { get; set; }

        [JsonPropertyName("explanation")]
        public string? Explanation { get; set; }
    }

    private sealed class CandidateRelationContext
    {
        [JsonPropertyName("relation_type")]
        public string RelationType { get; set; } = string.Empty;

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = string.Empty;

        [JsonPropertyName("neighbor_entity_id")]
        public Guid NeighborEntityId { get; set; }

        [JsonPropertyName("neighbor_name")]
        public string? NeighborName { get; set; }
    }
}
