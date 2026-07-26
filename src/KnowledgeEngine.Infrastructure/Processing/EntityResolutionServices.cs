using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed partial class EntityTypeRegistry : IEntityTypeRegistry
{
    private static readonly string[] KnownTypes =
    [
        "PERSON", "ORGANIZATION", "COMPANY", "INSTITUTION", "PRODUCT",
        "MODEL_FAMILY", "MODEL", "TECHNOLOGY", "FRAMEWORK", "LIBRARY",
        "DATASET", "STANDARD", "LOCATION", "EVENT", "INDUSTRY", "CONCEPT", "DOCUMENT"
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORG"] = "ORGANIZATION",
        ["企业"] = "COMPANY",
        ["公司"] = "COMPANY",
        ["组织"] = "ORGANIZATION",
        ["机构"] = "INSTITUTION",
        ["人物"] = "PERSON",
        ["人名"] = "PERSON",
        ["产品"] = "PRODUCT",
        ["模型"] = "MODEL",
        ["模型系列"] = "MODEL_FAMILY",
        ["技术"] = "TECHNOLOGY",
        ["框架"] = "FRAMEWORK",
        ["库"] = "LIBRARY",
        ["数据集"] = "DATASET",
        ["标准"] = "STANDARD",
        ["地点"] = "LOCATION",
        ["事件"] = "EVENT",
        ["行业"] = "INDUSTRY",
        ["概念"] = "CONCEPT",
        ["文档"] = "DOCUMENT"
    };

    private static readonly HashSet<string> KnownSet = new(KnownTypes, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> All => KnownTypes;

    public bool IsKnown(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType)) return false;
        var value = entityType.Trim().Replace('-', '_').Replace(' ', '_');
        return KnownSet.Contains(value) || Aliases.ContainsKey(value);
    }

    public string Normalize(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType)) return "CONCEPT";
        var value = entityType.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
        if (Aliases.TryGetValue(value, out var mapped)) return mapped;
        return KnownSet.Contains(value) ? value : "CONCEPT";
    }
}

public sealed partial class EntityNameNormalizer(IEntityTypeRegistry typeRegistry) : IEntityNameNormalizer
{
    public const string CurrentVersion = "entity_norm_v1";

    [GeneratedRegex(@"[\(\（]([^\)）]{1,100})[\)）]", RegexOptions.Compiled)]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    private static readonly HashSet<string> CompanySuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "inc", "incorporated", "corp", "corporation", "co", "company",
        "ltd", "limited", "llc", "plc", "gmbh", "公司", "有限公司", "股份有限公司"
    };

    public EntityNameNormalizationResult Normalize(string rawName, string? entityType = null, string? languageHint = null)
    {
        var raw = rawName?.Trim() ?? string.Empty;
        var rules = new List<string>();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var type = typeRegistry.Normalize(entityType);

        var compatible = raw.Normalize(NormalizationForm.FormKC);
        if (!string.Equals(raw, compatible, StringComparison.Ordinal)) rules.Add("UNICODE_NFKC");

        compatible = compatible
            .Replace('‐', '-').Replace('‑', '-').Replace('‒', '-')
            .Replace('–', '-').Replace('—', '-').Replace('﹣', '-')
            .Replace('（', '(').Replace('）', ')');

        string? abbreviation = null;
        foreach (Match match in ParentheticalRegex().Matches(compatible))
        {
            var value = CollapseWhitespace(match.Groups[1].Value);
            if (value.Length == 0) continue;
            aliases.Add(value);
            rules.Add("PARENTHETICAL_ALIAS");
            if (LooksLikeAbbreviation(value)) abbreviation ??= value;
        }

        var canonical = CollapseWhitespace(ParentheticalRegex().Replace(compatible, " "));
        if (canonical.Length == 0) canonical = CollapseWhitespace(compatible);
        if (LooksLikeAbbreviation(canonical)) abbreviation ??= canonical;

        var keyBuilder = new StringBuilder(canonical.Length);
        foreach (var rune in canonical.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || rune.Value is '+' or '#' or '.')
            {
                keyBuilder.Append(rune.ToString().ToLowerInvariant());
            }
            else
            {
                keyBuilder.Append(' ');
            }
        }

        var key = CollapseWhitespace(keyBuilder.ToString()).Trim('.');
        if (type is "COMPANY" or "ORGANIZATION" or "INSTITUTION")
        {
            var parts = key.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (parts.Count > 1 && CompanySuffixes.Contains(parts[^1]))
            {
                parts.RemoveAt(parts.Count - 1);
                rules.Add("COMPANY_SUFFIX_NORMALIZED");
            }
            key = string.Join(' ', parts);
        }

        if (key.Length == 0)
            key = compatible.ToLowerInvariant();

        return new EntityNameNormalizationResult
        {
            RawName = raw,
            CanonicalName = canonical,
            NormalizedKey = key,
            EntityType = type,
            Abbreviation = abbreviation,
            AliasCandidates = aliases.Where(x => !string.Equals(x, canonical, StringComparison.OrdinalIgnoreCase)).ToList(),
            AppliedRules = rules.Distinct().ToList(),
            Version = CurrentVersion
        };
    }

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Trim(), " ");

    private static bool LooksLikeAbbreviation(string value)
    {
        if (value.Length is < 2 or > 20 || value.Any(char.IsWhiteSpace)) return false;
        var letters = value.Where(char.IsLetter).ToArray();
        return letters.Length >= 2 && letters.All(c => !char.IsLower(c));
    }
}

public sealed class EntityResolutionOrchestrator(
    IAppDbContext db,
    IEntityNameNormalizer normalizer,
    IEntityTypeRegistry typeRegistry,
    IEntityCandidateResolver candidateResolver,
    IEntityDisambiguationService disambiguationService,
    IOptions<EntityResolutionSettings> options,
    ILogger<EntityResolutionOrchestrator> logger) : IEntityResolutionOrchestrator
{
    private readonly EntityResolutionSettings _settings = options.Value;

    public async Task<EntityResolutionBatchResult> ResolveDocumentAsync(
        Guid documentId,
        IReadOnlyCollection<EntityResult> extractedEntities,
        EntityExtractionContext? extractionContext = null,
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == documentId, ct)
            ?? throw new KeyNotFoundException($"Document not found: {documentId}");

        var workspaceGuid = document.WorkspaceId ?? await db.Workspaces.AsNoTracking()
            .Where(x => x.UserId == document.UserId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (workspaceGuid == null)
        {
            throw new InvalidOperationException(
                $"Document {documentId} is not assigned to a workspace and no user workspace is available.");
        }

        var workspaceId = workspaceGuid.Value.ToString();
        document.WorkspaceId ??= workspaceGuid;

        var context = extractionContext ?? new EntityExtractionContext();
        var batchId = context.BatchId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;

        var previousAssociations = await db.DocumentEntities
            .Where(x => x.DocumentId == documentId)
            .ToListAsync(ct);
        var affectedEntityIds = previousAssociations.Select(x => x.EntityId).ToHashSet();
        var previousMentions = await db.EntityMentions
            .Where(x => x.DocumentId == documentId)
            .ToListAsync(ct);
        if (previousMentions.Count > 0)
        {
            var mentionIds = previousMentions.Select(x => x.Id).ToList();
            var candidates = await db.EntityResolutionCandidates
                .Where(x => mentionIds.Contains(x.MentionId))
                .ToListAsync(ct);
            db.EntityResolutionCandidates.RemoveRange(candidates);
            db.EntityMentions.RemoveRange(previousMentions);
        }
        db.DocumentEntities.RemoveRange(previousAssociations);

        var accepted = extractedEntities
            .Where(IsAccepted)
            .Select(item => BuildExtraction(item))
            .Where(item => item.Normalized.NormalizedKey.Length > 0)
            .GroupBy(item => $"{item.Normalized.EntityType}\u001f{item.Normalized.NormalizedKey}", StringComparer.Ordinal)
            .Select(MergeDocumentGroup)
            .ToList();

        var linkedCount = 0;
        var createdCount = 0;
        foreach (var item in accepted)
        {
            var externalMatch = await FindExternalIdEntityAsync(
                workspaceId, item.Normalized.EntityType, item.Source.ExternalIds, ct);
            var entity = externalMatch ?? await FindExactEntityAsync(workspaceId, item.Normalized, ct);
            IReadOnlyList<EntityCandidateMatch> candidateMatches = [];
            EntityDisambiguationResult? disambiguation = null;
            var resolutionMethod = "NEW_ENTITY";
            var resolutionStatus = "NEW_ENTITY";
            var score = 0.50m;
            var reasonCodes = new List<string> { "INSUFFICIENT_EVIDENCE" };

            if (entity != null)
            {
                linkedCount++;
                resolutionMethod = externalMatch != null
                    ? "EXTERNAL_ID_EXACT"
                    : entity.NormalizedKey == item.Normalized.NormalizedKey
                    ? "CANONICAL_EXACT"
                    : "ALIAS_EXACT";
                resolutionStatus = "AUTO_LINKED";
                score = 1m;
                reasonCodes =
                [
                    resolutionMethod == "CANONICAL_EXACT"
                        ? "CANONICAL_NAME_EXACT_MATCH"
                        : resolutionMethod == "EXTERNAL_ID_EXACT"
                            ? "VERIFIED_EXTERNAL_ID_EXACT_MATCH"
                            : "ALIAS_EXACT_MATCH",
                    "TYPE_COMPATIBLE"
                ];
            }
            else
            {
                candidateMatches = await candidateResolver.RetrieveAsync(
                    new EntityCandidateRequest
                    {
                        UserId = document.UserId,
                        WorkspaceId = workspaceId,
                        Normalized = item.Normalized,
                        Mention = FirstNonEmpty(
                            item.Source.Mention, item.Source.Name, item.Normalized.RawName),
                        Context = FirstNonEmptyOrNull(
                            item.Source.Evidence, item.Source.Examples?.FirstOrDefault()),
                        Description = item.Source.Description,
                        SourceDomain = document.SourceDomain,
                        CooccurringNormalizedKeys = accepted
                            .Where(x => !ReferenceEquals(x, item))
                            .Select(x => x.Normalized.NormalizedKey)
                            .Distinct(StringComparer.Ordinal)
                            .ToList()
                    }, ct);
                var scoredMatch = candidateMatches.FirstOrDefault(x => !x.HardBlocked);
                if (scoredMatch != null
                    && candidateResolver.ShouldAutoLink(
                        scoredMatch, item.Normalized.EntityType))
                {
                    entity = await db.Entities.FirstOrDefaultAsync(
                        x => x.Id == scoredMatch.EntityId
                            && x.WorkspaceId == workspaceId
                            && x.Status != "merged", ct);
                    if (entity != null)
                    {
                        linkedCount++;
                        resolutionMethod = "MULTI_CHANNEL_SCORE";
                        resolutionStatus = "AUTO_LINKED";
                        score = scoredMatch.TotalScore;
                        reasonCodes = scoredMatch.ReasonCodes.ToList();
                    }
                }

                if (entity == null)
                {
                    var llmCandidates = candidateMatches
                        .Where(x => !x.HardBlocked
                            && x.TotalScore >= _settings.LlmMinimumCandidateScore)
                        .Take(5)
                        .ToList();
                    if (llmCandidates.Count > 0)
                    {
                        disambiguation = await disambiguationService.DecideAsync(
                            new EntityDisambiguationRequest
                            {
                                UserId = document.UserId,
                                WorkspaceId = workspaceId,
                                Mention = FirstNonEmpty(
                                    item.Source.Mention,
                                    item.Source.Name,
                                    item.Normalized.RawName),
                                EntityType = item.Normalized.EntityType,
                                Context = FirstNonEmptyOrNull(
                                    item.Source.Evidence,
                                    item.Source.Examples?.FirstOrDefault(),
                                    item.Source.Description),
                                DocumentTitle = document.Title,
                                SourceDomain = document.SourceDomain,
                                PublishedAt = document.PublishedAt,
                                Language = document.PrimaryLanguage ?? document.Language,
                                Candidates = llmCandidates
                            }, ct);
                        if (disambiguation.Decision == "SAME_ENTITY"
                            && disambiguation.CandidateEntityId.HasValue
                            && disambiguation.Confidence >= _settings.LlmLinkConfidence)
                        {
                            entity = await db.Entities.FirstOrDefaultAsync(x =>
                                x.Id == disambiguation.CandidateEntityId.Value
                                && x.WorkspaceId == workspaceId
                                && x.Status != "merged", ct);
                            if (entity != null)
                            {
                                linkedCount++;
                                resolutionMethod = "LLM_CONTEXT";
                                resolutionStatus = "AUTO_LINKED";
                                score = disambiguation.Confidence;
                                reasonCodes = disambiguation.ReasonCodes.ToList();
                            }
                        }
                    }
                }
            }

            if (entity == null)
            {
                if (disambiguation != null)
                {
                    resolutionMethod = "LLM_CONTEXT_NEW_ENTITY";
                    score = disambiguation.Confidence;
                    reasonCodes = disambiguation.ReasonCodes.ToList();
                }
                entity = new Entity
                {
                    Id = Guid.NewGuid(),
                    UserId = document.UserId,
                    WorkspaceId = workspaceId,
                    Name = item.Normalized.CanonicalName,
                    CanonicalName = item.Normalized.CanonicalName,
                    DisplayName = item.Normalized.CanonicalName,
                    PreferredNameZh = IsChinese(item.Normalized.CanonicalName) ? item.Normalized.CanonicalName : null,
                    PreferredNameEn = IsChinese(item.Normalized.CanonicalName) ? null : item.Normalized.CanonicalName,
                    Abbreviation = item.Normalized.Abbreviation,
                    NormalizedName = item.Normalized.NormalizedKey,
                    NormalizedKey = item.Normalized.NormalizedKey,
                    NormalizationVersion = item.Normalized.Version,
                    EntityType = item.Normalized.EntityType,
                    Description = item.Source.Description,
                    Source = "ai",
                    Status = "pending_review",
                    Confidence = item.Source.Confidence,
                    IsVerified = false,
                    IsArchived = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Entities.Add(entity);
                createdCount++;
            }

            affectedEntityIds.Add(entity.Id);
            var mentionText = FirstNonEmpty(
                item.Source.Mention,
                item.Source.FirstMention,
                item.Source.Name,
                item.Normalized.RawName);
            var mentionNormalization = normalizer.Normalize(
                mentionText, item.Normalized.EntityType, document.PrimaryLanguage ?? document.Language);
            var mention = new EntityMention
            {
                Id = Guid.NewGuid(),
                UserId = document.UserId,
                WorkspaceId = workspaceId,
                DocumentId = documentId,
                EntityId = entity.Id,
                MentionText = mentionText,
                NormalizedMention = mentionNormalization.NormalizedKey,
                SuggestedType = item.Normalized.EntityType,
                ContextText = FirstNonEmptyOrNull(
                    item.Source.Evidence,
                    item.Source.Examples?.FirstOrDefault(),
                    item.Source.Description),
                StartOffset = item.Source.StartOffset,
                EndOffset = item.Source.EndOffset,
                OccurrenceCount = item.OccurrenceCount,
                ExtractionBatchId = batchId,
                ExtractionModel = context.Model,
                ModelVersion = context.ModelVersion,
                PromptVersion = context.PromptVersion,
                SchemaVersion = context.SchemaVersion,
                ExtractionConfidence = item.Source.Confidence,
                ResolutionStatus = resolutionStatus,
                ResolutionMethod = resolutionMethod,
                ResolutionScore = score,
                ReasonCodes = JsonSerializer.Serialize(reasonCodes),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.EntityMentions.Add(mention);

            if (disambiguation != null && resolutionStatus != "AUTO_LINKED")
            {
                db.EntityGovernanceTasks.Add(new EntityGovernanceTask
                {
                    Id = Guid.NewGuid(),
                    UserId = document.UserId,
                    WorkspaceId = workspaceId,
                    TaskType = "UNRESOLVED_MENTION",
                    MentionId = mention.Id,
                    SubjectEntityId = entity.Id,
                    CandidateEntityId = disambiguation.CandidateEntityId
                        ?? candidateMatches.FirstOrDefault(x => !x.HardBlocked)?.EntityId,
                    Status = "pending",
                    Priority = (int)Math.Round(
                        candidateMatches.FirstOrDefault(x => !x.HardBlocked)?.TotalScore * 100m
                        ?? 50m),
                    IdempotencyKey = $"unresolved-mention:{mention.Id:N}",
                    Score = disambiguation.Confidence,
                    ReasonCodes = JsonSerializer.Serialize(disambiguation.ReasonCodes),
                    Payload = JsonSerializer.Serialize(new
                    {
                        decision = disambiguation.Decision,
                        explanation = disambiguation.Explanation,
                        model = disambiguation.Model,
                        promptVersion = disambiguation.PromptVersion,
                        isFallback = disambiguation.IsFallback,
                        rawOutput = disambiguation.RawOutput
                    }),
                    ErrorMessage = disambiguation.IsFallback
                        ? string.Join(", ", disambiguation.ReasonCodes)
                        : null,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            if (resolutionStatus == "AUTO_LINKED" && candidateMatches.Count == 0)
            {
                db.EntityResolutionCandidates.Add(new EntityResolutionCandidate
                {
                    Id = Guid.NewGuid(),
                    MentionId = mention.Id,
                    CandidateEntityId = entity.Id,
                    WorkspaceId = workspaceId,
                    Rank = 1,
                    NameScore = resolutionMethod == "CANONICAL_EXACT" ? 1m : 0m,
                    AliasScore = resolutionMethod == "ALIAS_EXACT" ? 1m : 0m,
                    SourceScore = resolutionMethod == "EXTERNAL_ID_EXACT" ? 1m : 0m,
                    TotalScore = 1m,
                    Decision = "AUTO_LINKED",
                    ReasonCodes = mention.ReasonCodes,
                    CreatedAt = now
                });
            }

            var rank = 0;
            var fallbackLlmCandidateId = disambiguation?.CandidateEntityId
                ?? candidateMatches.FirstOrDefault(x => !x.HardBlocked)?.EntityId;
            foreach (var candidate in candidateMatches)
            {
                rank++;
                var decision = candidate.HardBlocked
                    ? "HARD_BLOCKED"
                    : resolutionStatus == "AUTO_LINKED" && candidate.EntityId == entity.Id
                        ? "AUTO_LINKED"
                        : candidate.TotalScore >= 0.78m
                            ? "LLM_REQUIRED"
                            : candidate.TotalScore >= 0.60m
                                ? "REVIEW_REQUIRED"
                                : "REJECTED";
                var isLlmCandidate = disambiguation != null
                    && fallbackLlmCandidateId == candidate.EntityId;
                if (isLlmCandidate)
                {
                    decision = disambiguation!.Decision switch
                    {
                        "SAME_ENTITY" when resolutionStatus == "AUTO_LINKED" => "LLM_LINKED",
                        "SAME_ENTITY" => "LLM_LOW_CONFIDENCE",
                        "DIFFERENT_ENTITY" => "LLM_REJECTED",
                        "RELATED_BUT_NOT_SAME" => "LLM_RELATED_NOT_SAME",
                        _ => "LLM_INSUFFICIENT_EVIDENCE"
                    };
                }
                db.EntityResolutionCandidates.Add(new EntityResolutionCandidate
                {
                    Id = Guid.NewGuid(),
                    MentionId = mention.Id,
                    CandidateEntityId = candidate.EntityId,
                    WorkspaceId = workspaceId,
                    Rank = rank,
                    NameScore = candidate.NameScore,
                    AliasScore = candidate.AliasScore,
                    DescriptionScore = candidate.DescriptionScore,
                    ContextScore = candidate.ContextScore,
                    RelationScore = candidate.RelationScore,
                    SourceScore = candidate.SourceScore,
                    TotalScore = candidate.TotalScore,
                    Decision = decision,
                    ReasonCodes = JsonSerializer.Serialize(candidate.ReasonCodes),
                    ResolverVersion = EntityCandidateResolver.ResolverVersion,
                    LlmDecision = isLlmCandidate ? disambiguation!.Decision : null,
                    LlmConfidence = isLlmCandidate ? disambiguation!.Confidence : null,
                    LlmExplanation = isLlmCandidate ? disambiguation!.Explanation : null,
                    LlmModel = isLlmCandidate ? disambiguation!.Model : null,
                    LlmPromptVersion = isLlmCandidate
                        ? disambiguation!.PromptVersion
                        : null,
                    LlmInputTokens = isLlmCandidate
                        ? disambiguation!.InputTokens
                        : null,
                    LlmOutputTokens = isLlmCandidate
                        ? disambiguation!.OutputTokens
                        : null,
                    CreatedAt = now
                });
            }

            await AddAliasSuggestionsAsync(entity, item, document, workspaceId, now, ct);
            await AddExternalIdSuggestionsAsync(entity, item.Source.ExternalIds, document, workspaceId, now, ct);

            db.DocumentEntities.Add(new DocumentEntity
            {
                DocumentId = documentId,
                EntityId = entity.Id,
                MentionCount = item.OccurrenceCount,
                Confidence = item.Source.Confidence ?? 0.8m,
                Evidence = FirstNonEmptyOrNull(item.Source.Evidence, item.Source.Description),
                FirstMention = mentionText,
                MentionExamples = item.Source.Examples is { Count: > 0 }
                    ? JsonSerializer.Serialize(item.Source.Examples)
                    : null,
                Importance = item.Source.Importance ?? 0.5m,
                Role = item.Source.Role,
                Sentiment = item.Source.Sentiment,
                CreatedAt = now
            });
        }

        document.EntityStatus = "done";
        document.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        foreach (var entityId in affectedEntityIds)
        {
            var entity = await db.Entities.FirstOrDefaultAsync(x => x.Id == entityId, ct);
            if (entity == null) continue;
            entity.UsageCount = await db.DocumentEntities.CountAsync(x => x.EntityId == entityId, ct);
            entity.SourceCount = entity.UsageCount;
            entity.MentionCount = await db.EntityMentions
                .Where(x => x.EntityId == entityId)
                .SumAsync(x => (int?)x.OccurrenceCount, ct) ?? 0;
            entity.UpdatedAt = now;
            entity.RowVersion++;
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Resolved entities for document {DocumentId}: accepted={Accepted}, linked={Linked}, created={Created}, workspace={WorkspaceId}",
            documentId, accepted.Count, linkedCount, createdCount, workspaceId);

        return new EntityResolutionBatchResult
        {
            BatchId = batchId,
            DocumentId = documentId,
            WorkspaceId = workspaceId,
            ExtractedCount = extractedEntities.Count,
            AcceptedCount = accepted.Count,
            LinkedCount = linkedCount,
            CreatedCount = createdCount,
            RejectedCount = extractedEntities.Count - accepted.Count
        };
    }

    private async Task<Entity?> FindExactEntityAsync(
        string workspaceId,
        EntityNameNormalizationResult normalized,
        CancellationToken ct)
    {
        var canonical = await db.Entities.FirstOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId
            && x.EntityType.ToUpper() == normalized.EntityType
            && x.Status != "merged"
            && (x.NormalizedKey == normalized.NormalizedKey || x.NormalizedName == normalized.NormalizedKey), ct);
        if (canonical != null) return canonical;

        var aliasEntityIds = await db.EntityAliases.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId
                && x.NormalizedAlias == normalized.NormalizedKey
                && x.IsVerified
                && (x.ValidTo == null || x.ValidTo > DateTime.UtcNow))
            .Select(x => x.EntityId)
            .Distinct()
            .Take(2)
            .ToListAsync(ct);
        if (aliasEntityIds.Count != 1) return null;

        return await db.Entities.FirstOrDefaultAsync(x =>
            x.Id == aliasEntityIds[0]
            && x.EntityType.ToUpper() == normalized.EntityType
            && x.Status != "merged", ct);
    }

    private async Task<Entity?> FindExternalIdEntityAsync(
        string workspaceId,
        string entityType,
        IReadOnlyCollection<EntityExternalIdResult>? externalIds,
        CancellationToken ct)
    {
        foreach (var externalId in externalIds ?? [])
        {
            var idType = NormalizeExternalIdType(externalId.IdType);
            var idValue = NormalizeExternalIdValue(externalId.IdValue);
            if (idValue.Length == 0) continue;

            var entityIds = await db.EntityExternalIds.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId
                    && x.IdType == idType
                    && x.IdValue == idValue
                    && x.IsVerified)
                .Select(x => x.EntityId)
                .Distinct()
                .Take(2)
                .ToListAsync(ct);
            if (entityIds.Count != 1) continue;

            var entity = await db.Entities.FirstOrDefaultAsync(x =>
                x.Id == entityIds[0]
                && x.EntityType.ToUpper() == entityType
                && x.Status != "merged", ct);
            if (entity != null) return entity;
        }

        return null;
    }

    private async Task AddAliasSuggestionsAsync(
        Entity entity,
        DocumentExtraction item,
        Document document,
        string workspaceId,
        DateTime now,
        CancellationToken ct)
    {
        var aliases = new List<(string Value, string? Language, string Type)>();
        aliases.AddRange(item.Normalized.AliasCandidates.Select(x => (x, (string?)null, "MODEL_GENERATED")));
        aliases.AddRange((item.Source.Aliases ?? []).Select(x => (x, (string?)null, "MODEL_GENERATED")));
        aliases.AddRange((item.Source.AliasDetails ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => (x.Value!, x.Language, NormalizeAliasType(x.AliasType))));
        if (!string.IsNullOrWhiteSpace(item.Source.CanonicalNameSuggestion)
            && !string.Equals(item.Source.CanonicalNameSuggestion, entity.CanonicalName, StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add((item.Source.CanonicalNameSuggestion!, document.PrimaryLanguage ?? document.Language, "MODEL_GENERATED"));
        }

        foreach (var alias in aliases.DistinctBy(x => x.Value, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedAlias = normalizer.Normalize(alias.Value, entity.EntityType, alias.Language);
            if (normalizedAlias.NormalizedKey.Length == 0
                || normalizedAlias.NormalizedKey == entity.NormalizedKey)
                continue;

            var exists = await db.EntityAliases.AnyAsync(x =>
                x.EntityId == entity.Id && x.NormalizedAlias == normalizedAlias.NormalizedKey, ct);
            if (exists) continue;

            db.EntityAliases.Add(new EntityAlias
            {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                UserId = document.UserId,
                WorkspaceId = workspaceId,
                Alias = alias.Value.Trim(),
                NormalizedAlias = normalizedAlias.NormalizedKey,
                LanguageCode = alias.Language,
                AliasType = alias.Type,
                SourceType = "ai",
                SourceId = document.Id.ToString(),
                Confidence = item.Source.Confidence,
                IsVerified = false,
                NormalizationVersion = normalizedAlias.Version,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private async Task AddExternalIdSuggestionsAsync(
        Entity entity,
        IReadOnlyCollection<EntityExternalIdResult>? externalIds,
        Document document,
        string workspaceId,
        DateTime now,
        CancellationToken ct)
    {
        foreach (var externalId in externalIds ?? [])
        {
            var idType = NormalizeExternalIdType(externalId.IdType);
            var idValue = NormalizeExternalIdValue(externalId.IdValue);
            if (idValue.Length == 0) continue;

            var exists = await db.EntityExternalIds.AnyAsync(x =>
                x.WorkspaceId == workspaceId
                && x.IdType == idType
                && x.IdValue == idValue, ct);
            if (exists) continue;

            db.EntityExternalIds.Add(new EntityExternalId
            {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                UserId = document.UserId,
                WorkspaceId = workspaceId,
                IdType = idType,
                IdValue = idValue,
                Source = string.IsNullOrWhiteSpace(externalId.Source) ? "ai" : externalId.Source.Trim(),
                IsVerified = false,
                Confidence = null,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private DocumentExtraction BuildExtraction(EntityResult source)
    {
        var canonical = FirstNonEmpty(source.CanonicalNameSuggestion, source.Name, source.Mention);
        var normalized = normalizer.Normalize(canonical, typeRegistry.Normalize(source.EntityType));
        return new DocumentExtraction(source, normalized, Math.Max(1, source.MentionCount));
    }

    private static DocumentExtraction MergeDocumentGroup(
        IGrouping<string, DocumentExtraction> group)
    {
        var first = group.OrderByDescending(x => x.Source.Confidence ?? 0).First();
        return first with { OccurrenceCount = group.Sum(x => x.OccurrenceCount) };
    }

    private static bool IsAccepted(EntityResult item) =>
        !string.IsNullOrWhiteSpace(FirstNonEmptyOrNull(item.Name, item.Mention, item.CanonicalNameSuggestion))
        && (!item.Importance.HasValue || item.Importance >= 0.4m)
        && (!item.Confidence.HasValue || item.Confidence >= 0.6m);

    private static string NormalizeAliasType(string? value)
    {
        var type = value?.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
        return type is "ABBREVIATION" or "TRANSLATION" or "FULL_NAME" or "SHORT_NAME"
            or "FORMER_NAME" or "SPELLING_VARIANT" or "TRANSLITERATION" or "USER_DEFINED"
            ? type
            : "MODEL_GENERATED";
    }

    private static string NormalizeExternalIdType(string? value)
    {
        var normalized = value?.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
        return normalized is "WIKIDATA" or "ORCID" or "DOI" or "ROR" or "GITHUB" or "DOMAIN"
            ? normalized
            : "OTHER";
    }

    private static string NormalizeExternalIdValue(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string FirstNonEmpty(params string?[] values) =>
        FirstNonEmptyOrNull(values) ?? string.Empty;

    private static string? FirstNonEmptyOrNull(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static bool IsChinese(string value) =>
        value.Any(c => c is >= '\u3400' and <= '\u9fff');

    private sealed record DocumentExtraction(
        EntityResult Source,
        EntityNameNormalizationResult Normalized,
        int OccurrenceCount);
}
