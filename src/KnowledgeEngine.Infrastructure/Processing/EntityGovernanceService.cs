using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class EntityGovernanceService(
    IAppDbContext db,
    IEntityNameNormalizer normalizer,
    IEntityCandidateResolver candidateResolver,
    IEntityMergeService mergeService,
    ILogger<EntityGovernanceService> logger) : IEntityGovernanceService
{
    private static readonly HashSet<string> MaintenanceOperations =
    [
        "ALIAS_MIGRATION",
        "HISTORICAL_MENTION_BACKFILL",
        "REDIRECT_COMPRESSION",
        "ENTITY_REINDEX"
    ];

    public async Task<EntityGovernanceTaskDto> StartDuplicateScanAsync(
        Guid userId,
        StartEntityScanRequest request,
        CancellationToken ct = default)
    {
        var workspaceId = request.WorkspaceId.ToString();
        var workspaceExists = await db.Workspaces.AsNoTracking()
            .AnyAsync(x => x.Id == request.WorkspaceId && x.UserId == userId, ct);
        if (!workspaceExists)
            throw new KeyNotFoundException($"Workspace not found: {request.WorkspaceId}");

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();
        var existing = await db.EntityGovernanceTasks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId
                && x.IdempotencyKey == idempotencyKey, ct);
        if (existing != null) return Map(existing);

        var normalizedType = string.IsNullOrWhiteSpace(request.EntityType)
            ? null
            : request.EntityType.Trim().ToUpperInvariant();
        var query = db.Entities.AsNoTracking().Where(x =>
            x.WorkspaceId == workspaceId && x.Status != "merged" && !x.IsArchived);
        if (normalizedType != null)
            query = query.Where(x => x.EntityType.ToUpper() == normalizedType);

        var now = DateTime.UtcNow;
        var task = new EntityGovernanceTask
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            TaskType = "DUPLICATE_SCAN",
            Status = "pending",
            Priority = 10,
            IdempotencyKey = idempotencyKey,
            Cursor = "0",
            TotalItems = await query.CountAsync(ct),
            Payload = JsonSerializer.Serialize(new DuplicateScanPayload
            {
                EntityType = normalizedType,
                BatchSize = Math.Clamp(request.BatchSize, 1, 200)
            }),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.EntityGovernanceTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return Map(task);
    }

    public async Task<EntityGovernanceTaskDto> StartMaintenanceAsync(
        Guid userId,
        StartEntityMaintenanceRequest request,
        CancellationToken ct = default)
    {
        var operation = request.Operation.Trim().ToUpperInvariant();
        if (!MaintenanceOperations.Contains(operation))
            throw new ArgumentException(
                $"Unsupported maintenance operation: {request.Operation}");
        var workspaceId = request.WorkspaceId.ToString();
        var workspaceExists = await db.Workspaces.AsNoTracking()
            .AnyAsync(x => x.Id == request.WorkspaceId && x.UserId == userId, ct);
        if (!workspaceExists)
            throw new KeyNotFoundException($"Workspace not found: {request.WorkspaceId}");
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"{operation}:{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();
        var existing = await db.EntityGovernanceTasks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId
                && x.IdempotencyKey == idempotencyKey, ct);
        if (existing != null) return Map(existing);

        var total = operation == "HISTORICAL_MENTION_BACKFILL"
            ? await db.DocumentEntities.CountAsync(x =>
                db.Entities.Any(e => e.Id == x.EntityId
                    && e.WorkspaceId == workspaceId), ct)
            : await db.Entities.CountAsync(x => x.WorkspaceId == workspaceId, ct);
        var now = DateTime.UtcNow;
        var task = new EntityGovernanceTask
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            TaskType = "ENTITY_MAINTENANCE",
            Status = "pending",
            Priority = 20,
            IdempotencyKey = idempotencyKey,
            Cursor = "0",
            TotalItems = total,
            Payload = JsonSerializer.Serialize(new MaintenancePayload
            {
                Operation = operation,
                BatchSize = Math.Clamp(request.BatchSize, 1, 200)
            }),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.EntityGovernanceTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return Map(task);
    }

    public async Task<EntityGovernanceTaskDto?> GetTaskAsync(
        Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await FindOwnedTaskAsync(userId, taskId, false, ct);
        return task == null ? null : Map(task);
    }

    public async Task<IReadOnlyList<EntityGovernanceTaskDto>> ListCandidatesAsync(
        Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var parent = await FindOwnedTaskAsync(userId, taskId, false, ct)
            ?? throw new KeyNotFoundException($"Entity governance task not found: {taskId}");
        var tasks = await db.EntityGovernanceTasks.AsNoTracking()
            .Where(x => x.ParentTaskId == parent.Id
                && x.TaskType == "DUPLICATE_CANDIDATE")
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return tasks.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<EntityGovernanceTaskDto>> ListTasksAsync(
        Guid userId,
        Guid? workspaceId,
        string? status,
        string? taskType,
        int limit = 100,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var query = db.EntityGovernanceTasks.AsNoTracking()
            .Where(x => x.UserId == userId);
        if (workspaceId.HasValue)
        {
            var value = workspaceId.Value.ToString();
            query = query.Where(x => x.WorkspaceId == value);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var value = status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == value);
        }
        if (!string.IsNullOrWhiteSpace(taskType))
        {
            var value = taskType.Trim().ToUpperInvariant();
            query = query.Where(x => x.TaskType == value);
        }
        var tasks = await query.OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return tasks.Select(Map).ToList();
    }

    public async Task<EntityGovernanceTaskDto> DecideAsync(
        Guid userId,
        Guid taskId,
        EntityGovernanceDecisionRequest request,
        CancellationToken ct = default)
    {
        var task = await FindOwnedTaskAsync(userId, taskId, true, ct)
            ?? throw new KeyNotFoundException($"Entity governance task not found: {taskId}");
        if (task.Status is "completed" or "rejected" or "deferred")
            return Map(task);
        if (!task.SubjectEntityId.HasValue || !task.CandidateEntityId.HasValue)
            throw new InvalidOperationException("The task does not contain an entity pair.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("IdempotencyKey is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Decision reason is required.");

        var decision = request.Decision.Trim().ToUpperInvariant();
        switch (decision)
        {
            case "MERGE":
            {
                var workspaceId = Guid.Parse(task.WorkspaceId);
                var preview = await mergeService.PreviewAsync(userId, new EntityMergePreviewRequest
                {
                    WorkspaceId = workspaceId,
                    EntityIdA = task.SubjectEntityId.Value,
                    EntityIdB = task.CandidateEntityId.Value
                }, ct);
                if (!preview.CanExecute)
                    throw new InvalidOperationException(
                        $"Merge is blocked: {string.Join(", ", preview.HardBlocks)}");
                await mergeService.MergeAsync(userId, new ExecuteEntityMergeRequest
                {
                    WorkspaceId = workspaceId,
                    SourceEntityId = preview.SourceEntityId,
                    TargetEntityId = preview.TargetEntityId,
                    ExpectedSourceVersion = preview.SourceVersion,
                    ExpectedTargetVersion = preview.TargetVersion,
                    Reason = request.Reason,
                    Method = "governance_review",
                    Score = task.Score,
                    IdempotencyKey = request.IdempotencyKey
                }, ct);
                task.Status = "completed";
                break;
            }
            case "REJECT":
                task.Status = "rejected";
                break;
            case "BLOCK":
                await mergeService.AddBlockAsync(userId, new AddEntityMergeBlockRequest
                {
                    WorkspaceId = Guid.Parse(task.WorkspaceId),
                    EntityIdA = task.SubjectEntityId.Value,
                    EntityIdB = task.CandidateEntityId.Value,
                    Reason = request.Reason,
                    IsPermanent = true
                }, ct);
                task.Status = "rejected";
                break;
            case "DEFER":
                task.Status = "deferred";
                break;
            default:
                throw new ArgumentException(
                    "Decision must be MERGE, REJECT, BLOCK, or DEFER.");
        }
        task.Result = JsonSerializer.Serialize(new
        {
            decision,
            reason = request.Reason,
            idempotencyKey = request.IdempotencyKey
        });
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(task);
    }

    public async Task<EntityQualityMetrics> GetQualityMetricsAsync(
        Guid userId,
        Guid? workspaceId,
        CancellationToken ct = default)
    {
        var workspace = workspaceId?.ToString();
        var entities = db.Entities.AsNoTracking()
            .Where(x => x.UserId == userId);
        var mentions = db.EntityMentions.AsNoTracking()
            .Where(x => x.UserId == userId);
        var tasks = db.EntityGovernanceTasks.AsNoTracking()
            .Where(x => x.UserId == userId);
        var merges = db.EntityMergeLogs.AsNoTracking()
            .Where(x => x.UserId == userId);
        var outbox = db.EntityOutboxEvents.AsNoTracking()
            .Where(x => x.UserId == userId);
        if (workspace != null)
        {
            entities = entities.Where(x => x.WorkspaceId == workspace);
            mentions = mentions.Where(x => x.WorkspaceId == workspace);
            tasks = tasks.Where(x => x.WorkspaceId == workspace);
            merges = merges.Where(x => x.WorkspaceId == workspace);
            outbox = outbox.Where(x => x.WorkspaceId == workspace);
        }
        var active = await entities.CountAsync(
            x => x.Status != "merged" && !x.IsArchived, ct);
        var merged = await entities.CountAsync(x => x.Status == "merged", ct);
        var aliasQuery = db.EntityAliases.AsNoTracking()
            .Where(x => x.UserId == userId);
        if (workspace != null)
            aliasQuery = aliasQuery.Where(x => x.WorkspaceId == workspace);
        var mentionCount = await mentions.CountAsync(ct);
        var linked = await mentions.CountAsync(x => x.EntityId.HasValue, ct);
        var unresolved = mentionCount - linked;
        var completedMerges = await merges.CountAsync(
            x => x.Status == "completed" || x.Status == "reverted", ct);
        var revertedMerges = await merges.CountAsync(x => x.Status == "reverted", ct);
        var oldestPending = await outbox.Where(x => x.Status == "pending")
            .OrderBy(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var typeDistribution = await entities
            .Where(x => x.Status != "merged")
            .GroupBy(x => x.EntityType)
            .Select(x => new { Key = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var versionDistribution = await entities
            .Where(x => x.Status != "merged")
            .GroupBy(x => x.NormalizationVersion)
            .Select(x => new { Key = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var duplicateCandidates = await tasks.CountAsync(
            x => x.TaskType == "DUPLICATE_CANDIDATE"
                && x.Status == "pending", ct);
        return new EntityQualityMetrics
        {
            WorkspaceId = workspaceId,
            ActiveEntityCount = active,
            MergedEntityCount = merged,
            AliasCount = await aliasQuery.CountAsync(ct),
            MentionCount = mentionCount,
            LinkedMentionCount = linked,
            UnresolvedMentionCount = unresolved,
            MentionLinkRate = Ratio(linked, mentionCount),
            UnresolvedRate = Ratio(unresolved, mentionCount),
            PendingReviewCount = await tasks.CountAsync(
                x => x.Status == "pending", ct),
            DuplicateCandidateCount = duplicateCandidates,
            CompletedMergeCount = completedMerges,
            RevertedMergeCount = revertedMerges,
            MergeRevertRate = Ratio(revertedMerges, completedMerges),
            EstimatedDuplicateRate = Ratio(duplicateCandidates, active),
            PendingOutboxCount = await outbox.CountAsync(
                x => x.Status == "pending" || x.Status == "processing", ct),
            FailedOutboxCount = await outbox.CountAsync(
                x => x.Status == "failed", ct),
            OldestPendingOutboxSeconds = oldestPending.HasValue
                ? Math.Max(0, (DateTime.UtcNow - oldestPending.Value).TotalSeconds)
                : null,
            EntityTypeDistribution = typeDistribution,
            NormalizationVersionDistribution = versionDistribution
        };
    }

    public Task<EntityGovernanceTaskDto> PauseAsync(
        Guid userId, Guid taskId, CancellationToken ct = default) =>
        SetStatusAsync(userId, taskId, ["pending", "running"], "paused", ct);

    public Task<EntityGovernanceTaskDto> ResumeAsync(
        Guid userId, Guid taskId, CancellationToken ct = default) =>
        SetStatusAsync(userId, taskId, ["paused"], "pending", ct);

    public async Task<EntityGovernanceTaskDto> RetryAsync(
        Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await FindOwnedTaskAsync(userId, taskId, true, ct)
            ?? throw new KeyNotFoundException($"Entity governance task not found: {taskId}");
        if (task.Status != "failed")
            throw new InvalidOperationException("Only failed tasks can be retried.");
        task.Status = "pending";
        task.ErrorMessage = null;
        task.CompletedAt = null;
        task.RetryCount++;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(task);
    }

    public async Task<bool> ProcessNextBatchAsync(CancellationToken ct = default)
    {
        var task = await db.EntityGovernanceTasks
            .Where(x => (x.TaskType == "DUPLICATE_SCAN"
                    || x.TaskType == "ENTITY_MAINTENANCE")
                && (x.Status == "pending" || x.Status == "running"))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (task == null) return false;

        try
        {
            task.Status = "running";
            task.StartedAt ??= DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (task.TaskType == "ENTITY_MAINTENANCE")
                return await ProcessMaintenanceBatchAsync(task, ct);

            var payload = ParsePayload(task.Payload);
            var offset = int.TryParse(task.Cursor, out var parsed) ? Math.Max(0, parsed) : 0;
            var query = db.Entities.AsNoTracking().Where(x =>
                x.WorkspaceId == task.WorkspaceId
                && x.Status != "merged"
                && !x.IsArchived);
            if (!string.IsNullOrWhiteSpace(payload.EntityType))
                query = query.Where(x => x.EntityType.ToUpper() == payload.EntityType);
            var entities = await query
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Skip(offset)
                .Take(payload.BatchSize)
                .ToListAsync(ct);

            foreach (var entity in entities)
            {
                if (await IsPausedAsync(task.Id, ct)) return true;
                try
                {
                    await ScanEntityAsync(task, entity, ct);
                    task.SucceededItems++;
                }
                catch (Exception ex)
                {
                    task.FailedItems++;
                    logger.LogWarning(ex,
                        "Duplicate scan failed for entity {EntityId} in task {TaskId}",
                        entity.Id, task.Id);
                }
                task.ProcessedItems++;
                offset++;
                task.Cursor = offset.ToString();
                task.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            if (entities.Count < payload.BatchSize || task.ProcessedItems >= task.TotalItems)
            {
                task.Status = "completed";
                task.CompletedAt = DateTime.UtcNow;
                task.Result = JsonSerializer.Serialize(new
                {
                    candidateCount = await db.EntityGovernanceTasks.CountAsync(
                        x => x.ParentTaskId == task.Id
                            && x.TaskType == "DUPLICATE_CANDIDATE", ct)
                });
                task.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return true;
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = Truncate(ex.Message, 2000);
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Duplicate scan task {TaskId} failed", task.Id);
            return true;
        }
    }

    private async Task<bool> ProcessMaintenanceBatchAsync(
        EntityGovernanceTask task,
        CancellationToken ct)
    {
        var payload = ParseMaintenancePayload(task.Payload);
        var offset = int.TryParse(task.Cursor, out var parsed) ? Math.Max(0, parsed) : 0;
        var batchSize = Math.Clamp(payload.BatchSize, 1, 200);
        var processed = 0;
        if (payload.Operation == "HISTORICAL_MENTION_BACKFILL")
        {
            var links = await (
                from link in db.DocumentEntities.AsNoTracking()
                join entity in db.Entities.AsNoTracking()
                    on link.EntityId equals entity.Id
                where entity.WorkspaceId == task.WorkspaceId
                orderby link.DocumentId, link.EntityId
                select new { link, entity }
            ).Skip(offset).Take(batchSize).ToListAsync(ct);
            foreach (var row in links)
            {
                if (await IsPausedAsync(task.Id, ct)) return true;
                var exists = await db.EntityMentions.AnyAsync(x =>
                    x.DocumentId == row.link.DocumentId
                    && x.EntityId == row.entity.Id, ct);
                if (!exists)
                {
                    var now = DateTime.UtcNow;
                    db.EntityMentions.Add(new EntityMention
                    {
                        Id = Guid.NewGuid(),
                        UserId = task.UserId,
                        WorkspaceId = task.WorkspaceId,
                        DocumentId = row.link.DocumentId,
                        EntityId = row.entity.Id,
                        MentionText = row.link.FirstMention
                            ?? row.entity.CanonicalName
                            ?? row.entity.Name,
                        NormalizedMention = row.entity.NormalizedKey
                            ?? row.entity.NormalizedName
                            ?? row.entity.Name.ToLowerInvariant(),
                        SuggestedType = row.entity.EntityType,
                        ContextText = row.link.Evidence,
                        OccurrenceCount = Math.Max(1, row.link.MentionCount),
                        ExtractionBatchId = task.Id,
                        ModelVersion = "legacy_compatibility",
                        SchemaVersion = "entity_mention_v1_compat",
                        ResolutionStatus = "LINKED",
                        ResolutionMethod = "legacy_document_entity",
                        ResolutionScore = row.link.Confidence ?? 0.5m,
                        ReasonCodes = "[\"LEGACY_AGGREGATE_WEAK_EVIDENCE\"]",
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    task.SucceededItems++;
                }
                processed++;
            }
        }
        else
        {
            var entities = await db.Entities
                .Where(x => x.WorkspaceId == task.WorkspaceId)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync(ct);
            foreach (var entity in entities)
            {
                if (await IsPausedAsync(task.Id, ct)) return true;
                switch (payload.Operation)
                {
                    case "ALIAS_MIGRATION":
                        await MigrateLegacyAliasesAsync(task, entity, ct);
                        break;
                    case "REDIRECT_COMPRESSION":
                        if (entity.Status == "merged" && entity.MergedIntoId.HasValue)
                        {
                            var resolved = await new EntityRedirectResolver(db)
                                .ResolveAsync(entity.Id, task.WorkspaceId, ct);
                            if (entity.MergedIntoId != resolved.EntityId)
                            {
                                entity.MergedIntoId = resolved.EntityId;
                                entity.RowVersion++;
                                entity.UpdatedAt = DateTime.UtcNow;
                            }
                        }
                        break;
                    case "ENTITY_REINDEX":
                        var exists = await db.EntityOutboxEvents.AnyAsync(x =>
                            x.IdempotencyKey ==
                                $"maintenance:{task.Id:N}:{entity.Id:N}:{entity.RowVersion}", ct);
                        if (!exists)
                            db.EntityOutboxEvents.Add(new EntityOutboxEvent
                            {
                                Id = Guid.NewGuid(),
                                UserId = task.UserId,
                                WorkspaceId = task.WorkspaceId,
                                EntityId = entity.Id,
                                EventType = "ENTITY_REINDEX_REQUIRED",
                                EntityVersion = entity.RowVersion,
                                Payload = JsonSerializer.Serialize(new { taskId = task.Id }),
                                IdempotencyKey =
                                    $"maintenance:{task.Id:N}:{entity.Id:N}:{entity.RowVersion}",
                                Status = "pending",
                                CreatedAt = DateTime.UtcNow
                            });
                        break;
                }
                task.SucceededItems++;
                processed++;
            }
        }
        task.ProcessedItems += processed;
        task.Cursor = (offset + processed).ToString();
        task.UpdatedAt = DateTime.UtcNow;
        if (processed < batchSize || task.ProcessedItems >= task.TotalItems)
        {
            task.Status = "completed";
            task.CompletedAt = DateTime.UtcNow;
            task.Result = JsonSerializer.Serialize(new
            {
                payload.Operation,
                task.ProcessedItems,
                task.SucceededItems,
                task.FailedItems
            });
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task MigrateLegacyAliasesAsync(
        EntityGovernanceTask task,
        Entity entity,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entity.Aliases)) return;
        var values = ParseLegacyAliases(entity.Aliases);
        foreach (var value in values)
        {
            var normalized = normalizer.Normalize(value, entity.EntityType);
            if (string.IsNullOrWhiteSpace(normalized.NormalizedKey)) continue;
            var exists = await db.EntityAliases.AnyAsync(x =>
                x.EntityId == entity.Id
                && x.NormalizedAlias == normalized.NormalizedKey, ct);
            if (exists) continue;
            var now = DateTime.UtcNow;
            db.EntityAliases.Add(new EntityAlias
            {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                UserId = task.UserId,
                WorkspaceId = task.WorkspaceId,
                Alias = value,
                NormalizedAlias = normalized.NormalizedKey,
                AliasType = "LEGACY_IMPORT",
                SourceType = "migration",
                SourceId = task.Id.ToString(),
                Confidence = 0.5m,
                IsVerified = false,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private static IReadOnlyList<string> ParseLegacyAliases(string value)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(value);
            if (parsed is { Count: > 0 })
                return parsed.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()).Distinct().ToList();
        }
        catch
        {
            // Fall through to the legacy comma/newline format.
        }
        return value.Split([',', ';', '\n', '，', '；'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task ScanEntityAsync(
        EntityGovernanceTask parent,
        Entity subject,
        CancellationToken ct)
    {
        var normalized = normalizer.Normalize(
            subject.CanonicalName ?? subject.Name, subject.EntityType);
        var candidates = await candidateResolver.RetrieveAsync(
            new EntityCandidateRequest
            {
                UserId = parent.UserId,
                WorkspaceId = parent.WorkspaceId,
                Normalized = normalized,
                Mention = subject.CanonicalName ?? subject.Name,
                Context = subject.Description,
                Description = subject.Description
            }, ct);

        foreach (var candidate in candidates.Where(x =>
            x.EntityId != subject.Id
            && !x.HardBlocked
            && (x.TotalScore >= 0.60m
                || x.NameScore >= 0.78m
                || x.AliasScore >= 0.80m)))
        {
            var (first, second) = OrderPair(subject.Id, candidate.EntityId);
            var exists = await db.EntityGovernanceTasks.AnyAsync(x =>
                x.ParentTaskId == parent.Id
                && x.SubjectEntityId == first
                && x.CandidateEntityId == second, ct);
            if (exists) continue;
            var now = DateTime.UtcNow;
            db.EntityGovernanceTasks.Add(new EntityGovernanceTask
            {
                Id = Guid.NewGuid(),
                UserId = parent.UserId,
                WorkspaceId = parent.WorkspaceId,
                TaskType = "DUPLICATE_CANDIDATE",
                ParentTaskId = parent.Id,
                SubjectEntityId = first,
                CandidateEntityId = second,
                Status = "pending",
                Priority = (int)Math.Round(candidate.TotalScore * 100m),
                Score = candidate.TotalScore,
                ReasonCodes = JsonSerializer.Serialize(candidate.ReasonCodes),
                Payload = JsonSerializer.Serialize(candidate),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private async Task<EntityGovernanceTaskDto> SetStatusAsync(
        Guid userId,
        Guid taskId,
        IReadOnlyCollection<string> allowed,
        string status,
        CancellationToken ct)
    {
        var task = await FindOwnedTaskAsync(userId, taskId, true, ct)
            ?? throw new KeyNotFoundException($"Entity governance task not found: {taskId}");
        if (!allowed.Contains(task.Status))
            throw new InvalidOperationException(
                $"Task in status '{task.Status}' cannot transition to '{status}'.");
        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(task);
    }

    private async Task<EntityGovernanceTask?> FindOwnedTaskAsync(
        Guid userId, Guid taskId, bool tracked, CancellationToken ct)
    {
        var query = tracked
            ? db.EntityGovernanceTasks.AsQueryable()
            : db.EntityGovernanceTasks.AsNoTracking();
        return await query.FirstOrDefaultAsync(
            x => x.Id == taskId && x.UserId == userId, ct);
    }

    private async Task<bool> IsPausedAsync(Guid taskId, CancellationToken ct)
    {
        var status = await db.EntityGovernanceTasks.AsNoTracking()
            .Where(x => x.Id == taskId)
            .Select(x => x.Status)
            .FirstAsync(ct);
        return status == "paused";
    }

    private static DuplicateScanPayload ParsePayload(string? payload)
    {
        try
        {
            return JsonSerializer.Deserialize<DuplicateScanPayload>(payload ?? "{}")
                ?? new DuplicateScanPayload();
        }
        catch
        {
            return new DuplicateScanPayload();
        }
    }

    private static MaintenancePayload ParseMaintenancePayload(string? payload)
    {
        try
        {
            return JsonSerializer.Deserialize<MaintenancePayload>(payload ?? "{}")
                ?? new MaintenancePayload();
        }
        catch
        {
            return new MaintenancePayload();
        }
    }

    private static (Guid First, Guid Second) OrderPair(Guid left, Guid right) =>
        string.CompareOrdinal(left.ToString("N"), right.ToString("N")) <= 0
            ? (left, right)
            : (right, left);

    private static EntityGovernanceTaskDto Map(EntityGovernanceTask task) =>
        new()
        {
            Id = task.Id,
            WorkspaceId = Guid.TryParse(task.WorkspaceId, out var workspaceId)
                ? workspaceId
                : Guid.Empty,
            TaskType = task.TaskType,
            ParentTaskId = task.ParentTaskId,
            SubjectEntityId = task.SubjectEntityId,
            CandidateEntityId = task.CandidateEntityId,
            MentionId = task.MentionId,
            Status = task.Status,
            Priority = task.Priority,
            Cursor = task.Cursor,
            TotalItems = task.TotalItems,
            ProcessedItems = task.ProcessedItems,
            SucceededItems = task.SucceededItems,
            FailedItems = task.FailedItems,
            Score = task.Score,
            ReasonCodes = ParseReasons(task.ReasonCodes),
            ErrorMessage = task.ErrorMessage,
            RetryCount = task.RetryCount,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt
        };

    private static IReadOnlyList<string> ParseReasons(string? value)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value ?? "[]") ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static decimal Ratio(int numerator, int denominator) =>
        denominator == 0
            ? 0m
            : Math.Round((decimal)numerator / denominator, 4);

    private sealed class DuplicateScanPayload
    {
        public string? EntityType { get; set; }
        public int BatchSize { get; set; } = 50;
    }

    private sealed class MaintenancePayload
    {
        public string Operation { get; set; } = string.Empty;
        public int BatchSize { get; set; } = 50;
    }
}

public sealed class EntityGovernanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EntityGovernanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<IEntityGovernanceService>();
                var processed = await service.ProcessNextBatchAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Entity governance worker iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
