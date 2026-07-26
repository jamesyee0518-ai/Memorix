using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class EntityMergeService(
    AppDbContext db,
    IEntityRedirectResolver redirects) : IEntityMergeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<EntityMergePreview> PreviewAsync(
        Guid userId,
        EntityMergePreviewRequest request,
        CancellationToken ct = default)
    {
        var workspaceId = request.WorkspaceId.ToString();
        var first = await redirects.ResolveAsync(request.EntityIdA, workspaceId, ct);
        var second = await redirects.ResolveAsync(request.EntityIdB, workspaceId, ct);
        if (first.EntityId == second.EntityId)
            throw new InvalidOperationException("Both entity IDs already resolve to the same entity.");

        var entities = await db.Entities.AsNoTracking()
            .Where(x => (x.Id == first.EntityId || x.Id == second.EntityId)
                && x.WorkspaceId == workspaceId
                && x.UserId == userId)
            .ToListAsync(ct);
        if (entities.Count != 2)
            throw new KeyNotFoundException("One or both entities were not found in this workspace.");
        var a = entities.Single(x => x.Id == first.EntityId);
        var b = entities.Single(x => x.Id == second.EntityId);
        var target = await ChooseTargetAsync(a, b, ct);
        var source = target.Id == a.Id ? b : a;

        var sourceAliases = await db.EntityAliases.AsNoTracking()
            .Where(x => x.EntityId == source.Id).ToListAsync(ct);
        var targetAliases = await db.EntityAliases.AsNoTracking()
            .Where(x => x.EntityId == target.Id).ToListAsync(ct);
        var sourceExternal = await db.EntityExternalIds.AsNoTracking()
            .Where(x => x.EntityId == source.Id).ToListAsync(ct);
        var targetExternal = await db.EntityExternalIds.AsNoTracking()
            .Where(x => x.EntityId == target.Id).ToListAsync(ct);
        var mentionCount = await db.EntityMentions.CountAsync(
            x => x.EntityId == source.Id, ct);
        var documentCount = await db.DocumentEntities.CountAsync(
            x => x.EntityId == source.Id, ct);
        var relations = await db.EntityRelations.AsNoTracking()
            .Where(x => x.SourceEntityId == source.Id || x.TargetEntityId == source.Id)
            .ToListAsync(ct);
        var blocks = await GetHardBlocksAsync(
            workspaceId, source, target, sourceExternal, targetExternal, ct);
        var aliasConflicts = sourceAliases.Count(x =>
            targetAliases.Any(y => y.NormalizedAlias == x.NormalizedAlias));
        var externalConflicts = sourceExternal.Count(x =>
            targetExternal.Any(y => y.IdType == x.IdType && y.IdValue == x.IdValue));
        var selfLoops = relations.Count(x =>
            (x.SourceEntityId == source.Id && x.TargetEntityId == target.Id)
            || (x.TargetEntityId == source.Id && x.SourceEntityId == target.Id));

        return new EntityMergePreview
        {
            SourceEntityId = source.Id,
            TargetEntityId = target.Id,
            RecommendationReason = RecommendationReason(target),
            SourceVersion = source.RowVersion,
            TargetVersion = target.RowVersion,
            MentionCount = mentionCount,
            AliasCount = sourceAliases.Count,
            ExternalIdCount = sourceExternal.Count,
            DocumentAssociationCount = documentCount,
            RelationCount = relations.Count,
            AliasConflictCount = aliasConflicts,
            ExternalIdConflictCount = externalConflicts,
            SelfLoopCount = selfLoops,
            HardBlocks = blocks,
            EstimatedMilliseconds = Math.Max(
                50, (mentionCount + documentCount + relations.Count) * 2),
            CanExecute = blocks.Count == 0
        };
    }

    public async Task<EntityMergeResult> MergeAsync(
        Guid userId,
        ExecuteEntityMergeRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("IdempotencyKey is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Merge reason is required.");
        var workspaceId = request.WorkspaceId.ToString();
        var replay = await db.EntityMergeLogs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId
                && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (replay != null)
            return MapResult(replay, true);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var sourceResolved = await redirects.ResolveAsync(
                request.SourceEntityId, workspaceId, ct);
            var targetResolved = await redirects.ResolveAsync(
                request.TargetEntityId, workspaceId, ct);
            if (sourceResolved.EntityId == targetResolved.EntityId)
                throw new InvalidOperationException("Entities already resolve to the same identity.");

            var orderedIds = new[] { sourceResolved.EntityId, targetResolved.EntityId }
                .OrderBy(x => x).ToList();
            var locked = await db.Entities
                .Where(x => orderedIds.Contains(x.Id)
                    && x.WorkspaceId == workspaceId
                    && x.UserId == userId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);
            if (locked.Count != 2)
                throw new KeyNotFoundException("One or both merge entities were not found.");
            var source = locked.Single(x => x.Id == sourceResolved.EntityId);
            var target = locked.Single(x => x.Id == targetResolved.EntityId);
            if (source.RowVersion != request.ExpectedSourceVersion
                || target.RowVersion != request.ExpectedTargetVersion)
                throw new DbUpdateConcurrencyException(
                    "Entity versions changed after preview. Refresh and preview again.");

            var sourceExternal = await db.EntityExternalIds
                .Where(x => x.EntityId == source.Id).ToListAsync(ct);
            var targetExternal = await db.EntityExternalIds
                .Where(x => x.EntityId == target.Id).ToListAsync(ct);
            var hardBlocks = await GetHardBlocksAsync(
                workspaceId, source, target, sourceExternal, targetExternal, ct);
            if (hardBlocks.Count > 0)
                throw new InvalidOperationException(
                    $"Merge is blocked: {string.Join(", ", hardBlocks)}");

            var snapshot = await CaptureSnapshotAsync(source, target, ct);
            var now = DateTime.UtcNow;
            var mergeId = Guid.NewGuid();
            var summary = await MigrateAsync(source, target, now, ct);

            source.Status = "merged";
            source.MergedIntoId = target.Id;
            source.RowVersion++;
            source.UpdatedAt = now;
            target.RowVersion++;
            target.UpdatedAt = now;
            // Flush migrations inside the transaction before recalculating aggregates.
            // EF queries otherwise read the pre-merge database state.
            await db.SaveChangesAsync(ct);
            target.UsageCount = await db.DocumentEntities.CountAsync(
                x => x.EntityId == target.Id, ct);
            target.SourceCount = target.UsageCount;
            target.MentionCount = await db.EntityMentions
                .Where(x => x.EntityId == target.Id)
                .SumAsync(x => (int?)x.OccurrenceCount, ct) ?? 0;

            var log = new EntityMergeLog
            {
                Id = mergeId,
                UserId = userId,
                WorkspaceId = workspaceId,
                BatchId = Guid.NewGuid(),
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Reason = request.Reason.Trim(),
                Method = request.Method.Trim(),
                Score = request.Score,
                OperatorId = userId,
                DeviceId = request.DeviceId,
                RequestId = request.RequestId,
                BeforeSnapshot = JsonSerializer.Serialize(snapshot, JsonOptions),
                MigrationSummary = JsonSerializer.Serialize(summary, JsonOptions),
                ExpectedSourceVersion = request.ExpectedSourceVersion,
                ExpectedTargetVersion = request.ExpectedTargetVersion,
                Status = "completed",
                IdempotencyKey = request.IdempotencyKey.Trim(),
                CreatedAt = now,
                CompletedAt = now
            };
            db.EntityMergeLogs.Add(log);
            AddOutbox(userId, workspaceId, source, "ENTITY_MERGED", mergeId, now);
            AddOutbox(userId, workspaceId, target, "ENTITY_REINDEX_REQUIRED", mergeId, now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return MapResult(log, false);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<EntityMergeResult> RevertAsync(
        Guid userId,
        Guid mergeId,
        string requestId,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var log = await db.EntityMergeLogs.FirstOrDefaultAsync(
            x => x.Id == mergeId && x.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Merge log not found: {mergeId}");
        if (log.Status == "reverted") return MapResult(log, true);
        if (log.Status != "completed" || !log.CompletedAt.HasValue)
            throw new InvalidOperationException("Only completed merges can be reverted.");
        var snapshot = JsonSerializer.Deserialize<MergeSnapshot>(
            log.BeforeSnapshot, JsonOptions)
            ?? throw new InvalidOperationException("Merge snapshot is incomplete.");
        var completedAt = log.CompletedAt.Value;

        if (await HasPostMergeChangesAsync(log, snapshot, completedAt, ct))
        {
            var now = DateTime.UtcNow;
            db.EntityGovernanceTasks.Add(new EntityGovernanceTask
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WorkspaceId = log.WorkspaceId,
                TaskType = "SPLIT_REQUIRED",
                SubjectEntityId = log.SourceEntityId,
                CandidateEntityId = log.TargetEntityId,
                Status = "pending",
                Priority = 100,
                IdempotencyKey = $"split-required:{mergeId:N}",
                Payload = JsonSerializer.Serialize(new { mergeId, requestId }),
                ErrorMessage = "Post-merge data cannot be assigned safely by automatic revert.",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new EntityMergeResult
            {
                MergeId = mergeId,
                SourceEntityId = log.SourceEntityId,
                TargetEntityId = log.TargetEntityId,
                Status = "split_required",
                CompletedAt = now
            };
        }

        var source = await db.Entities.FirstAsync(x => x.Id == log.SourceEntityId, ct);
        var target = await db.Entities.FirstAsync(x => x.Id == log.TargetEntityId, ct);
        CopyEntity(snapshot.Source, source);
        CopyEntity(snapshot.Target, target);
        await RestoreSnapshotCollectionsAsync(snapshot, ct);

        var nowReverted = DateTime.UtcNow;
        log.Status = "reverted";
        log.RevertedAt = nowReverted;
        AddOutbox(userId, log.WorkspaceId, source, "ENTITY_MERGE_REVERTED", mergeId, nowReverted);
        AddOutbox(userId, log.WorkspaceId, target, "ENTITY_REINDEX_REQUIRED", mergeId, nowReverted);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapResult(log, false);
    }

    public async Task<IReadOnlyList<EntityMergeHistoryItem>> GetHistoryAsync(
        Guid userId,
        Guid? workspaceId,
        int limit = 100,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var query = db.EntityMergeLogs.AsNoTracking().Where(x => x.UserId == userId);
        if (workspaceId.HasValue)
        {
            var value = workspaceId.Value.ToString();
            query = query.Where(x => x.WorkspaceId == value);
        }
        var logs = await query.OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return logs.Select(x => new EntityMergeHistoryItem
            {
                MergeId = x.Id,
                WorkspaceId = Guid.TryParse(x.WorkspaceId, out var parsed)
                    ? parsed
                    : Guid.Empty,
                SourceEntityId = x.SourceEntityId,
                TargetEntityId = x.TargetEntityId,
                Reason = x.Reason,
                Method = x.Method,
                Score = x.Score,
                OperatorId = x.OperatorId,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                CompletedAt = x.CompletedAt,
                RevertedAt = x.RevertedAt
            })
            .ToList();
    }

    public async Task<Guid> AddBlockAsync(
        Guid userId,
        AddEntityMergeBlockRequest request,
        CancellationToken ct = default)
    {
        var workspaceId = request.WorkspaceId.ToString();
        var (a, b) = OrderPair(request.EntityIdA, request.EntityIdB);
        if (a == b) throw new ArgumentException("Cannot block an entity against itself.");
        var validEntities = await db.Entities.CountAsync(x =>
            (x.Id == a || x.Id == b)
            && x.WorkspaceId == workspaceId
            && x.UserId == userId, ct);
        if (validEntities != 2)
            throw new KeyNotFoundException("One or both entities were not found.");
        var existing = await db.EntityMergeBlocklist.FirstOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.EntityIdA == a && x.EntityIdB == b, ct);
        if (existing != null) return existing.Id;
        var item = new EntityMergeBlocklist
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            EntityIdA = a,
            EntityIdB = b,
            Reason = request.Reason.Trim(),
            Source = "manual",
            OperatorId = userId,
            IsPermanent = request.IsPermanent,
            ValidUntil = request.ValidUntil,
            CreatedAt = DateTime.UtcNow
        };
        db.EntityMergeBlocklist.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task<bool> RemoveBlockAsync(
        Guid userId, Guid blockId, CancellationToken ct = default)
    {
        var item = await db.EntityMergeBlocklist.FirstOrDefaultAsync(
            x => x.Id == blockId && x.UserId == userId, ct);
        if (item == null) return false;
        db.EntityMergeBlocklist.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<Entity> ChooseTargetAsync(Entity a, Entity b, CancellationToken ct)
    {
        var ids = new[] { a.Id, b.Id };
        var externalCounts = await db.EntityExternalIds.AsNoTracking()
            .Where(x => ids.Contains(x.EntityId) && x.IsVerified)
            .GroupBy(x => x.EntityId)
            .Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var aliasCounts = await db.EntityAliases.AsNoTracking()
            .Where(x => ids.Contains(x.EntityId))
            .GroupBy(x => x.EntityId)
            .Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        decimal Score(Entity x) =>
            (x.IsVerified ? 10000 : 0)
            + externalCounts.GetValueOrDefault(x.Id) * 1000
            + x.SourceCount * 100
            + x.MentionCount
            + (!string.IsNullOrWhiteSpace(x.Description) ? 50 : 0)
            + aliasCounts.GetValueOrDefault(x.Id) * 10
            + (x.Source == "manual" ? 25 : 0);
        var scoreA = Score(a);
        var scoreB = Score(b);
        if (scoreA != scoreB) return scoreA > scoreB ? a : b;
        return a.CreatedAt <= b.CreatedAt ? a : b;
    }

    private async Task<List<string>> GetHardBlocksAsync(
        string workspaceId,
        Entity source,
        Entity target,
        IReadOnlyCollection<EntityExternalId> sourceExternal,
        IReadOnlyCollection<EntityExternalId> targetExternal,
        CancellationToken ct)
    {
        var blocks = new List<string>();
        if (!string.Equals(source.EntityType, target.EntityType, StringComparison.OrdinalIgnoreCase))
            blocks.Add("ENTITY_TYPE_CONFLICT");
        if (source.EntityType.Equals("MODEL", StringComparison.OrdinalIgnoreCase)
            && EntityCandidateResolver.HasVersionConflict(
                source.NormalizedKey ?? source.Name,
                target.NormalizedKey ?? target.Name))
            blocks.Add("MODEL_VERSION_CONFLICT");
        var conflictingIdType = sourceExternal
            .Where(x => x.IsVerified)
            .Select(x => x.IdType)
            .Intersect(targetExternal.Where(x => x.IsVerified).Select(x => x.IdType))
            .Any(type =>
                sourceExternal.Where(x => x.IsVerified && x.IdType == type)
                    .Select(x => x.IdValue)
                    .Intersect(targetExternal.Where(x => x.IsVerified && x.IdType == type)
                        .Select(x => x.IdValue))
                    .Any() == false);
        if (conflictingIdType) blocks.Add("VERIFIED_EXTERNAL_ID_CONFLICT");
        var (a, b) = OrderPair(source.Id, target.Id);
        var blocked = await db.EntityMergeBlocklist.AsNoTracking().AnyAsync(x =>
            x.WorkspaceId == workspaceId
            && x.EntityIdA == a && x.EntityIdB == b
            && (x.IsPermanent || x.ValidUntil == null || x.ValidUntil > DateTime.UtcNow), ct);
        if (blocked) blocks.Add("MERGE_BLOCKLIST");
        return blocks;
    }

    private async Task<MergeSnapshot> CaptureSnapshotAsync(
        Entity source, Entity target, CancellationToken ct)
    {
        var ids = new[] { source.Id, target.Id };
        return new MergeSnapshot
        {
            // Never retain references to tracked entities here. The merge mutates
            // those instances before the snapshot JSON is serialized.
            Source = CloneEntity(source),
            Target = CloneEntity(target),
            Aliases = await db.EntityAliases.AsNoTracking()
                .Where(x => ids.Contains(x.EntityId)).ToListAsync(ct),
            ExternalIds = await db.EntityExternalIds.AsNoTracking()
                .Where(x => ids.Contains(x.EntityId)).ToListAsync(ct),
            Mentions = await db.EntityMentions.AsNoTracking()
                .Where(x => x.EntityId.HasValue && ids.Contains(x.EntityId.Value))
                .ToListAsync(ct),
            DocumentEntities = await db.DocumentEntities.AsNoTracking()
                .Where(x => ids.Contains(x.EntityId)).ToListAsync(ct),
            Relations = await db.EntityRelations.AsNoTracking()
                .Where(x => ids.Contains(x.SourceEntityId) || ids.Contains(x.TargetEntityId))
                .ToListAsync(ct),
            ResolutionCandidates = await db.EntityResolutionCandidates.AsNoTracking()
                .Where(x => ids.Contains(x.CandidateEntityId))
                .ToListAsync(ct)
        };
    }

    private async Task<MigrationSummary> MigrateAsync(
        Entity source, Entity target, DateTime now, CancellationToken ct)
    {
        var summary = new MigrationSummary();
        var sourceAliases = await db.EntityAliases
            .Where(x => x.EntityId == source.Id).ToListAsync(ct);
        var targetAliases = await db.EntityAliases
            .Where(x => x.EntityId == target.Id).ToListAsync(ct);
        foreach (var alias in sourceAliases)
        {
            if (targetAliases.Any(x => x.NormalizedAlias == alias.NormalizedAlias))
                db.EntityAliases.Remove(alias);
            else
            {
                alias.EntityId = target.Id;
                targetAliases.Add(alias);
                summary.Aliases++;
            }
        }
        var sourceKey = source.NormalizedKey ?? source.NormalizedName;
        if (!string.IsNullOrWhiteSpace(sourceKey)
            && sourceKey != target.NormalizedKey
            && !targetAliases.Any(x => x.NormalizedAlias == sourceKey))
        {
            db.EntityAliases.Add(new EntityAlias
            {
                Id = Guid.NewGuid(),
                EntityId = target.Id,
                UserId = target.UserId,
                WorkspaceId = target.WorkspaceId,
                Alias = source.CanonicalName ?? source.Name,
                NormalizedAlias = sourceKey,
                AliasType = "FORMER_NAME",
                SourceType = "merge",
                SourceId = source.Id.ToString(),
                IsVerified = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            summary.Aliases++;
        }

        var sourceExternal = await db.EntityExternalIds
            .Where(x => x.EntityId == source.Id).ToListAsync(ct);
        var targetExternal = await db.EntityExternalIds
            .Where(x => x.EntityId == target.Id).ToListAsync(ct);
        foreach (var external in sourceExternal)
        {
            if (targetExternal.Any(x => x.IdType == external.IdType
                && x.IdValue == external.IdValue))
                db.EntityExternalIds.Remove(external);
            else
            {
                external.EntityId = target.Id;
                targetExternal.Add(external);
                summary.ExternalIds++;
            }
        }

        var mentions = await db.EntityMentions
            .Where(x => x.EntityId == source.Id).ToListAsync(ct);
        foreach (var mention in mentions) mention.EntityId = target.Id;
        summary.Mentions = mentions.Count;

        var sourceDocs = await db.DocumentEntities
            .Where(x => x.EntityId == source.Id).ToListAsync(ct);
        var targetDocs = await db.DocumentEntities
            .Where(x => x.EntityId == target.Id).ToListAsync(ct);
        foreach (var sourceDoc in sourceDocs)
        {
            var existing = targetDocs.FirstOrDefault(x =>
                x.DocumentId == sourceDoc.DocumentId);
            if (existing != null)
            {
                existing.MentionCount += sourceDoc.MentionCount;
                existing.Confidence = Max(existing.Confidence, sourceDoc.Confidence);
                existing.Importance = Max(existing.Importance, sourceDoc.Importance);
                existing.Evidence = MergeText(existing.Evidence, sourceDoc.Evidence);
                existing.MentionExamples = MergeText(
                    existing.MentionExamples, sourceDoc.MentionExamples);
            }
            else
            {
                db.DocumentEntities.Add(CloneDocumentEntity(sourceDoc, target.Id));
                summary.DocumentAssociations++;
            }
            db.DocumentEntities.Remove(sourceDoc);
        }

        var relations = await db.EntityRelations
            .Where(x => x.SourceEntityId == source.Id || x.TargetEntityId == source.Id)
            .ToListAsync(ct);
        foreach (var relation in relations)
        {
            if (relation.SourceEntityId == source.Id) relation.SourceEntityId = target.Id;
            if (relation.TargetEntityId == source.Id) relation.TargetEntityId = target.Id;
        }
        var allTargetRelations = await db.EntityRelations
            .Where(x => x.SourceEntityId == target.Id || x.TargetEntityId == target.Id)
            .ToListAsync(ct);
        var seen = new HashSet<string>();
        foreach (var relation in allTargetRelations.Concat(relations).DistinctBy(x => x.Id))
        {
            if (relation.SourceEntityId == relation.TargetEntityId)
            {
                db.EntityRelations.Remove(relation);
                summary.SelfLoopsRemoved++;
                continue;
            }
            var key = $"{relation.SourceEntityId:N}|{relation.TargetEntityId:N}|{relation.RelationType}";
            if (!seen.Add(key))
                db.EntityRelations.Remove(relation);
            else
                summary.Relations++;
        }

        var embeddings = await db.EntityEmbeddings
            .Where(x => x.EntityId == source.Id || x.EntityId == target.Id)
            .ToListAsync(ct);
        db.EntityEmbeddings.RemoveRange(embeddings);
        var candidates = await db.EntityResolutionCandidates
            .Where(x => x.CandidateEntityId == source.Id).ToListAsync(ct);
        var targetCandidateMentionIds = await db.EntityResolutionCandidates
            .Where(x => x.CandidateEntityId == target.Id)
            .Select(x => x.MentionId)
            .ToHashSetAsync(ct);
        foreach (var candidate in candidates)
        {
            if (targetCandidateMentionIds.Contains(candidate.MentionId))
                db.EntityResolutionCandidates.Remove(candidate);
            else
            {
                candidate.CandidateEntityId = target.Id;
                targetCandidateMentionIds.Add(candidate.MentionId);
            }
        }
        return summary;
    }

    private async Task<bool> HasPostMergeChangesAsync(
        EntityMergeLog log, MergeSnapshot snapshot, DateTime completedAt, CancellationToken ct)
    {
        var ids = new[] { log.SourceEntityId, log.TargetEntityId };
        var snapshotAliasIds = snapshot.Aliases.Select(x => x.Id).ToList();
        var snapshotExternalIds = snapshot.ExternalIds.Select(x => x.Id).ToList();
        var snapshotMentionIds = snapshot.Mentions.Select(x => x.Id).ToList();
        var snapshotRelationIds = snapshot.Relations.Select(x => x.Id).ToList();
        return await db.EntityAliases.AnyAsync(x => ids.Contains(x.EntityId)
                && !snapshotAliasIds.Contains(x.Id) && x.CreatedAt > completedAt, ct)
            || await db.EntityExternalIds.AnyAsync(x => ids.Contains(x.EntityId)
                && !snapshotExternalIds.Contains(x.Id) && x.CreatedAt > completedAt, ct)
            || await db.EntityMentions.AnyAsync(x => x.EntityId.HasValue
                && ids.Contains(x.EntityId.Value)
                && !snapshotMentionIds.Contains(x.Id) && x.CreatedAt > completedAt, ct)
            || await db.EntityRelations.AnyAsync(x =>
                (ids.Contains(x.SourceEntityId) || ids.Contains(x.TargetEntityId))
                && !snapshotRelationIds.Contains(x.Id) && x.CreatedAt > completedAt, ct)
            || await db.DocumentEntities.AnyAsync(x => ids.Contains(x.EntityId)
                && x.CreatedAt > completedAt, ct);
    }

    private async Task RestoreSnapshotCollectionsAsync(
        MergeSnapshot snapshot, CancellationToken ct)
    {
        var entityIds = new[] { snapshot.Source.Id, snapshot.Target.Id };
        var snapshotAliasIds = snapshot.Aliases.Select(x => x.Id).ToHashSet();
        var generatedAliases = await db.EntityAliases.Where(x =>
            entityIds.Contains(x.EntityId)
            && !snapshotAliasIds.Contains(x.Id)
            && x.SourceType == "merge"
            && x.SourceId == snapshot.Source.Id.ToString()).ToListAsync(ct);
        db.EntityAliases.RemoveRange(generatedAliases);
        foreach (var item in snapshot.Aliases)
        {
            var current = await db.EntityAliases.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
            if (current == null) db.EntityAliases.Add(item);
            else CopyAlias(item, current);
        }
        foreach (var item in snapshot.ExternalIds)
        {
            var current = await db.EntityExternalIds.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
            if (current == null) db.EntityExternalIds.Add(item);
            else CopyExternalId(item, current);
        }
        foreach (var item in snapshot.Mentions)
        {
            var current = await db.EntityMentions.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
            if (current == null) db.EntityMentions.Add(item);
            else current.EntityId = item.EntityId;
        }
        var documentIds = snapshot.DocumentEntities.Select(x => x.DocumentId).Distinct().ToList();
        var currentDocs = await db.DocumentEntities.Where(x =>
            entityIds.Contains(x.EntityId) && documentIds.Contains(x.DocumentId)).ToListAsync(ct);
        db.DocumentEntities.RemoveRange(currentDocs);
        await db.SaveChangesAsync(ct);
        foreach (var item in snapshot.DocumentEntities)
            db.DocumentEntities.Add(item);

        foreach (var item in snapshot.Relations)
        {
            var current = await db.EntityRelations.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
            if (current == null) db.EntityRelations.Add(item);
            else
            {
                current.SourceEntityId = item.SourceEntityId;
                current.TargetEntityId = item.TargetEntityId;
                current.RelationType = item.RelationType;
                current.EvidenceDocumentId = item.EvidenceDocumentId;
                current.EvidenceText = item.EvidenceText;
                current.Confidence = item.Confidence;
            }
        }
        foreach (var item in snapshot.ResolutionCandidates)
        {
            var current = await db.EntityResolutionCandidates
                .FirstOrDefaultAsync(x => x.Id == item.Id, ct);
            if (current == null)
                db.EntityResolutionCandidates.Add(item);
            else
                current.CandidateEntityId = item.CandidateEntityId;
        }
    }

    private void AddOutbox(
        Guid userId, string workspaceId, Entity entity,
        string eventType, Guid mergeId, DateTime now)
    {
        db.EntityOutboxEvents.Add(new EntityOutboxEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            EntityId = entity.Id,
            EventType = eventType,
            EntityVersion = entity.RowVersion,
            Payload = JsonSerializer.Serialize(new { mergeId }),
            IdempotencyKey = $"{entity.Id:N}:{entity.RowVersion}:{eventType}:{mergeId:N}",
            Status = "pending",
            CreatedAt = now
        });
    }

    private static string RecommendationReason(Entity target) =>
        target.IsVerified
            ? "人工验证实体优先"
            : target.SourceCount > 0
                ? "来源数与提及完整度更高"
                : "标准名称与创建时间综合更稳定";

    private static EntityMergeResult MapResult(EntityMergeLog log, bool replay) =>
        new()
        {
            MergeId = log.Id,
            SourceEntityId = log.SourceEntityId,
            TargetEntityId = log.TargetEntityId,
            Status = log.Status,
            IdempotentReplay = replay,
            CompletedAt = log.RevertedAt ?? log.CompletedAt ?? log.CreatedAt
        };

    private static (Guid A, Guid B) OrderPair(Guid a, Guid b) =>
        string.CompareOrdinal(a.ToString("N"), b.ToString("N")) <= 0
            ? (a, b) : (b, a);

    private static decimal? Max(decimal? a, decimal? b) =>
        a.HasValue && b.HasValue ? Math.Max(a.Value, b.Value) : a ?? b;

    private static string? MergeText(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        if (string.IsNullOrWhiteSpace(b) || a.Contains(b, StringComparison.Ordinal)) return a;
        return $"{a}\n{b}";
    }

    private static DocumentEntity CloneDocumentEntity(DocumentEntity source, Guid entityId) =>
        new()
        {
            DocumentId = source.DocumentId,
            EntityId = entityId,
            MentionCount = source.MentionCount,
            FirstMention = source.FirstMention,
            MentionExamples = source.MentionExamples,
            Importance = source.Importance,
            Role = source.Role,
            Sentiment = source.Sentiment,
            Confidence = source.Confidence,
            Evidence = source.Evidence,
            CreatedAt = source.CreatedAt
        };

    private static Entity CloneEntity(Entity source) =>
        new()
        {
            Id = source.Id,
            UserId = source.UserId,
            Name = source.Name,
            NormalizedName = source.NormalizedName,
            EntityType = source.EntityType,
            Description = source.Description,
            Metadata = source.Metadata,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            WorkspaceId = source.WorkspaceId,
            DisplayName = source.DisplayName,
            Aliases = source.Aliases,
            ExternalRef = source.ExternalRef,
            Source = source.Source,
            UsageCount = source.UsageCount,
            IsVerified = source.IsVerified,
            IsArchived = source.IsArchived,
            CanonicalName = source.CanonicalName,
            PreferredNameZh = source.PreferredNameZh,
            PreferredNameEn = source.PreferredNameEn,
            Abbreviation = source.Abbreviation,
            NormalizedKey = source.NormalizedKey,
            Status = source.Status,
            MergedIntoId = source.MergedIntoId,
            Confidence = source.Confidence,
            SourceCount = source.SourceCount,
            MentionCount = source.MentionCount,
            RowVersion = source.RowVersion,
            NormalizationVersion = source.NormalizationVersion
        };

    private static void CopyEntity(Entity source, Entity target)
    {
        target.UserId = source.UserId;
        target.Name = source.Name;
        target.NormalizedName = source.NormalizedName;
        target.EntityType = source.EntityType;
        target.Description = source.Description;
        target.Metadata = source.Metadata;
        target.WorkspaceId = source.WorkspaceId;
        target.DisplayName = source.DisplayName;
        target.Aliases = source.Aliases;
        target.ExternalRef = source.ExternalRef;
        target.Source = source.Source;
        target.UsageCount = source.UsageCount;
        target.IsVerified = source.IsVerified;
        target.IsArchived = source.IsArchived;
        target.CanonicalName = source.CanonicalName;
        target.PreferredNameZh = source.PreferredNameZh;
        target.PreferredNameEn = source.PreferredNameEn;
        target.Abbreviation = source.Abbreviation;
        target.NormalizedKey = source.NormalizedKey;
        target.Status = source.Status;
        target.MergedIntoId = source.MergedIntoId;
        target.Confidence = source.Confidence;
        target.SourceCount = source.SourceCount;
        target.MentionCount = source.MentionCount;
        target.RowVersion = source.RowVersion;
        target.NormalizationVersion = source.NormalizationVersion;
        target.CreatedAt = source.CreatedAt;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static void CopyAlias(EntityAlias source, EntityAlias target)
    {
        target.EntityId = source.EntityId;
        target.Alias = source.Alias;
        target.NormalizedAlias = source.NormalizedAlias;
        target.LanguageCode = source.LanguageCode;
        target.AliasType = source.AliasType;
        target.SourceType = source.SourceType;
        target.SourceId = source.SourceId;
        target.Confidence = source.Confidence;
        target.IsVerified = source.IsVerified;
        target.ValidFrom = source.ValidFrom;
        target.ValidTo = source.ValidTo;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static void CopyExternalId(EntityExternalId source, EntityExternalId target)
    {
        target.EntityId = source.EntityId;
        target.IdType = source.IdType;
        target.IdValue = source.IdValue;
        target.Source = source.Source;
        target.IsVerified = source.IsVerified;
        target.Confidence = source.Confidence;
        target.UpdatedAt = source.UpdatedAt;
    }

    private sealed class MergeSnapshot
    {
        public Entity Source { get; set; } = new();
        public Entity Target { get; set; } = new();
        public List<EntityAlias> Aliases { get; set; } = [];
        public List<EntityExternalId> ExternalIds { get; set; } = [];
        public List<EntityMention> Mentions { get; set; } = [];
        public List<DocumentEntity> DocumentEntities { get; set; } = [];
        public List<EntityRelation> Relations { get; set; } = [];
        public List<EntityResolutionCandidate> ResolutionCandidates { get; set; } = [];
    }

    private sealed class MigrationSummary
    {
        public int Aliases { get; set; }
        public int ExternalIds { get; set; }
        public int Mentions { get; set; }
        public int DocumentAssociations { get; set; }
        public int Relations { get; set; }
        public int SelfLoopsRemoved { get; set; }
    }
}
