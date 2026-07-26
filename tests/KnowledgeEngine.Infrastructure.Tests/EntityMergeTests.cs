using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Processing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class EntityMergeTests
{
    [Fact]
    public async Task Merge_MigratesIdentityGraphRedirectsAndCanRevert()
    {
        await using var fixture = await MergeFixture.CreateAsync();
        var preview = await fixture.Service.PreviewAsync(fixture.UserId, new()
        {
            WorkspaceId = fixture.WorkspaceId,
            EntityIdA = fixture.SourceEntityId,
            EntityIdB = fixture.TargetEntityId
        });

        Assert.True(preview.CanExecute);
        Assert.Equal(fixture.SourceEntityId, preview.SourceEntityId);
        Assert.Equal(fixture.TargetEntityId, preview.TargetEntityId);
        Assert.Equal(1, preview.SelfLoopCount);

        var request = new ExecuteEntityMergeRequest
        {
            WorkspaceId = fixture.WorkspaceId,
            SourceEntityId = preview.SourceEntityId,
            TargetEntityId = preview.TargetEntityId,
            ExpectedSourceVersion = preview.SourceVersion,
            ExpectedTargetVersion = preview.TargetVersion,
            Reason = "人工确认同一公司",
            IdempotencyKey = "merge-test-1"
        };
        var result = await fixture.Service.MergeAsync(fixture.UserId, request);
        var replay = await fixture.Service.MergeAsync(fixture.UserId, request);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal("completed", result.Status);
        Assert.True(replay.IdempotentReplay);
        var source = await fixture.Db.Entities.SingleAsync(
            x => x.Id == fixture.SourceEntityId);
        Assert.Equal("merged", source.Status);
        Assert.Equal(fixture.TargetEntityId, source.MergedIntoId);
        Assert.All(await fixture.Db.EntityMentions.ToListAsync(),
            x => Assert.Equal(fixture.TargetEntityId, x.EntityId));
        var association = await fixture.Db.DocumentEntities.SingleAsync();
        Assert.Equal(fixture.TargetEntityId, association.EntityId);
        Assert.Equal(3, association.MentionCount);
        Assert.DoesNotContain(await fixture.Db.EntityRelations.ToListAsync(),
            x => x.SourceEntityId == x.TargetEntityId);
        Assert.All(await fixture.Db.EntityAliases.ToListAsync(),
            x => Assert.Equal(fixture.TargetEntityId, x.EntityId));
        Assert.Equal(2, await fixture.Db.EntityOutboxEvents.CountAsync());

        var redirected = await fixture.Redirects.ResolveAsync(
            fixture.SourceEntityId, fixture.WorkspaceId.ToString());
        Assert.Equal(fixture.TargetEntityId, redirected.EntityId);
        Assert.Equal(fixture.SourceEntityId, redirected.RedirectedFrom);

        var reverted = await fixture.Service.RevertAsync(
            fixture.UserId, result.MergeId, "revert-test-1");
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("reverted", reverted.Status);
        Assert.Equal("active", (await fixture.Db.Entities.SingleAsync(
            x => x.Id == fixture.SourceEntityId)).Status);
        Assert.Equal(2, await fixture.Db.DocumentEntities.CountAsync());
        Assert.Equal(fixture.SourceEntityId,
            (await fixture.Db.EntityMentions.SingleAsync()).EntityId);
        Assert.Equal(2, await fixture.Db.EntityAliases.CountAsync());
        Assert.Equal(2, await fixture.Db.EntityRelations.CountAsync());
        Assert.Equal(4, await fixture.Db.EntityOutboxEvents.CountAsync());
    }

    [Fact]
    public async Task Blocklist_PreventsMerge_AndPostMergeChangesRequireSplit()
    {
        await using var blocked = await MergeFixture.CreateAsync();
        await blocked.Service.AddBlockAsync(blocked.UserId, new()
        {
            WorkspaceId = blocked.WorkspaceId,
            EntityIdA = blocked.SourceEntityId,
            EntityIdB = blocked.TargetEntityId,
            Reason = "人工判定不同实体"
        });
        var blockedPreview = await blocked.Service.PreviewAsync(blocked.UserId, new()
        {
            WorkspaceId = blocked.WorkspaceId,
            EntityIdA = blocked.SourceEntityId,
            EntityIdB = blocked.TargetEntityId
        });
        Assert.False(blockedPreview.CanExecute);
        Assert.Contains("MERGE_BLOCKLIST", blockedPreview.HardBlocks);

        await using var fixture = await MergeFixture.CreateAsync();
        var preview = await fixture.Service.PreviewAsync(fixture.UserId, new()
        {
            WorkspaceId = fixture.WorkspaceId,
            EntityIdA = fixture.SourceEntityId,
            EntityIdB = fixture.TargetEntityId
        });
        var merged = await fixture.Service.MergeAsync(fixture.UserId, new()
        {
            WorkspaceId = fixture.WorkspaceId,
            SourceEntityId = preview.SourceEntityId,
            TargetEntityId = preview.TargetEntityId,
            ExpectedSourceVersion = preview.SourceVersion,
            ExpectedTargetVersion = preview.TargetVersion,
            Reason = "人工确认",
            IdempotencyKey = "merge-split-test"
        });
        fixture.Db.EntityMentions.Add(new EntityMention
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            DocumentId = fixture.DocumentId,
            EntityId = fixture.TargetEntityId,
            MentionText = "post merge",
            NormalizedMention = "post merge",
            SuggestedType = "COMPANY",
            ExtractionBatchId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddSeconds(1),
            UpdatedAt = DateTime.UtcNow.AddSeconds(1)
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.RevertAsync(
            fixture.UserId, merged.MergeId, "revert-needs-split");
        Assert.Equal("split_required", result.Status);
        Assert.True(await fixture.Db.EntityGovernanceTasks.AnyAsync(
            x => x.TaskType == "SPLIT_REQUIRED"));
    }

    private sealed class MergeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public EntityMergeService Service { get; }
        public EntityRedirectResolver Redirects { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid WorkspaceId { get; } = Guid.NewGuid();
        public Guid SourceEntityId { get; } = Guid.NewGuid();
        public Guid TargetEntityId { get; } = Guid.NewGuid();
        public Guid ThirdEntityId { get; } = Guid.NewGuid();
        public Guid DocumentId { get; } = Guid.NewGuid();

        private MergeFixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
            Redirects = new EntityRedirectResolver(db);
            Service = new EntityMergeService(
                db,
                Redirects);
        }

        public static async Task<MergeFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new MergeFixture(connection, db);
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            var now = DateTime.UtcNow.AddMinutes(-5);
            var sourceId = Guid.NewGuid();
            Db.Sources.Add(new Source
            {
                Id = sourceId,
                UserId = UserId,
                SourceType = "text",
                ImportedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            Db.Documents.Add(new Document
            {
                Id = DocumentId,
                SourceId = sourceId,
                UserId = UserId,
                WorkspaceId = WorkspaceId,
                Title = "OpenAI",
                CreatedAt = now,
                UpdatedAt = now
            });
            Db.Entities.AddRange(
                Entity(SourceEntityId, "Open AI", false, 1, now),
                Entity(TargetEntityId, "OpenAI", true, 2, now.AddMinutes(-1)),
                Entity(ThirdEntityId, "ChatGPT", false, 1, now));
            Db.EntityAliases.AddRange(
                Alias(SourceEntityId, "Open AI", "open ai", now),
                Alias(TargetEntityId, "OpenAI", "openai", now));
            Db.EntityMentions.Add(new EntityMention
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                WorkspaceId = WorkspaceId.ToString(),
                DocumentId = DocumentId,
                EntityId = SourceEntityId,
                MentionText = "Open AI",
                NormalizedMention = "open ai",
                SuggestedType = "COMPANY",
                OccurrenceCount = 2,
                ExtractionBatchId = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now
            });
            Db.DocumentEntities.AddRange(
                new DocumentEntity
                {
                    DocumentId = DocumentId,
                    EntityId = SourceEntityId,
                    MentionCount = 2,
                    CreatedAt = now
                },
                new DocumentEntity
                {
                    DocumentId = DocumentId,
                    EntityId = TargetEntityId,
                    MentionCount = 1,
                    CreatedAt = now
                });
            Db.EntityRelations.AddRange(
                Relation(SourceEntityId, TargetEntityId, now),
                Relation(SourceEntityId, ThirdEntityId, now));
            await Db.SaveChangesAsync();
        }

        private Entity Entity(
            Guid id, string name, bool verified, int sourceCount, DateTime now) =>
            new()
            {
                Id = id,
                UserId = UserId,
                WorkspaceId = WorkspaceId.ToString(),
                Name = name,
                CanonicalName = name,
                NormalizedName = name.ToLowerInvariant(),
                NormalizedKey = name.ToLowerInvariant(),
                EntityType = "COMPANY",
                Status = "active",
                IsVerified = verified,
                SourceCount = sourceCount,
                MentionCount = sourceCount,
                RowVersion = 1,
                CreatedAt = now,
                UpdatedAt = now
            };

        private EntityAlias Alias(
            Guid entityId, string alias, string normalized, DateTime now) =>
            new()
            {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                UserId = UserId,
                WorkspaceId = WorkspaceId.ToString(),
                Alias = alias,
                NormalizedAlias = normalized,
                AliasType = "NAME",
                SourceType = "manual",
                CreatedAt = now,
                UpdatedAt = now
            };

        private EntityRelation Relation(Guid source, Guid target, DateTime now) =>
            new()
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SourceEntityId = source,
                TargetEntityId = target,
                RelationType = "RELATED_TO",
                CreatedAt = now
            };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
