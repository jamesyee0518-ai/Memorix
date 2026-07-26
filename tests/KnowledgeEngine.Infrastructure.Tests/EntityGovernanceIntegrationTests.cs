using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Processing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class EntityGovernanceIntegrationTests
{
    [Fact]
    public async Task Maintenance_BackfillsAliasesMentionsAndReportsQuality()
    {
        await using var fixture = await Fixture.CreateAsync();
        var aliasJob = await fixture.Governance.StartMaintenanceAsync(
            fixture.UserId, new StartEntityMaintenanceRequest
            {
                WorkspaceId = fixture.WorkspaceId,
                Operation = "ALIAS_MIGRATION",
                BatchSize = 20,
                IdempotencyKey = "alias-migration-test"
            });
        Assert.True(await fixture.Governance.ProcessNextBatchAsync());
        Assert.Equal("completed",
            (await fixture.Governance.GetTaskAsync(fixture.UserId, aliasJob.Id))!.Status);
        Assert.True(await fixture.Db.EntityAliases.AnyAsync(
            x => x.Alias == "LLM" && x.SourceType == "migration"));

        var mentionJob = await fixture.Governance.StartMaintenanceAsync(
            fixture.UserId, new StartEntityMaintenanceRequest
            {
                WorkspaceId = fixture.WorkspaceId,
                Operation = "HISTORICAL_MENTION_BACKFILL",
                BatchSize = 20,
                IdempotencyKey = "mention-backfill-test"
            });
        Assert.True(await fixture.Governance.ProcessNextBatchAsync());
        Assert.Equal("completed",
            (await fixture.Governance.GetTaskAsync(fixture.UserId, mentionJob.Id))!.Status);
        var mention = await fixture.Db.EntityMentions.SingleAsync();
        Assert.Equal("legacy_document_entity", mention.ResolutionMethod);
        Assert.Equal("LINKED", mention.ResolutionStatus);

        var metrics = await fixture.Governance.GetQualityMetricsAsync(
            fixture.UserId, fixture.WorkspaceId);
        Assert.Equal(1, metrics.ActiveEntityCount);
        Assert.Equal(1, metrics.LinkedMentionCount);
        Assert.Equal(1m, metrics.MentionLinkRate);
    }

    [Fact]
    public async Task GraphAndOutbox_UseCanonicalEntityAndCompleteIndexSync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = fixture.NewEntity(Guid.NewGuid(), "Transformer", "TECHNOLOGY");
        fixture.Db.Entities.Add(other);
        fixture.Db.EntityRelations.Add(new EntityRelation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SourceEntityId = fixture.EntityId,
            TargetEntityId = other.Id,
            RelationType = "USES",
            EvidenceDocumentId = fixture.DocumentId,
            CreatedAt = DateTime.UtcNow
        });
        fixture.Db.EntityEmbeddings.Add(new EntityEmbedding
        {
            Id = Guid.NewGuid(),
            EntityId = fixture.EntityId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            Provider = "test",
            Model = "test",
            EmbeddingType = "name",
            ContentHash = "old",
            Status = "done",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        fixture.Db.EntityOutboxEvents.Add(new EntityOutboxEvent
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            EntityId = fixture.EntityId,
            EventType = "ENTITY_REINDEX_REQUIRED",
            EntityVersion = 1,
            IdempotencyKey = "outbox-test",
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var graph = await fixture.Graph.GetGraphAsync(
            fixture.UserId, fixture.WorkspaceId, null, "zh-CN");
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Single(graph.Edges);
        Assert.Equal(fixture.EntityId, graph.Edges[0].SourceEntityId);
        var documents = await fixture.Graph.GetDocumentsAsync(
            fixture.UserId, fixture.EntityId, "zh-CN");
        Assert.Single(documents);
        Assert.Equal("大型语言模型", documents[0].DisplayEntityName);

        Assert.True(await fixture.Outbox.ProcessNextAsync());
        Assert.False(await fixture.Db.EntityEmbeddings.AnyAsync());
        Assert.Equal("completed",
            (await fixture.Db.EntityOutboxEvents.SingleAsync()).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Db { get; }
        public EntityGovernanceService Governance { get; }
        public EntityKnowledgeGraphService Graph { get; }
        public EntityOutboxProcessor Outbox { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid WorkspaceId { get; } = Guid.NewGuid();
        public Guid EntityId { get; } = Guid.NewGuid();
        public Guid DocumentId { get; } = Guid.NewGuid();

        private Fixture(SqliteConnection connection, AppDbContext db)
        {
            this.connection = connection;
            Db = db;
            var redirects = new EntityRedirectResolver(db);
            var merge = new EntityMergeService(db, redirects);
            Governance = new EntityGovernanceService(
                db,
                new EntityNameNormalizer(new EntityTypeRegistry()),
                new EmptyCandidates(),
                merge,
                NullLogger<EntityGovernanceService>.Instance);
            Graph = new EntityKnowledgeGraphService(
                db, redirects, Options.Create(new EntityResolutionSettings()));
            var sync = new EntityIndexSyncService(db);
            Outbox = new EntityOutboxProcessor(
                db, sync, NullLogger<EntityOutboxProcessor>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, db);
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            var now = DateTime.UtcNow;
            Db.Workspaces.Add(new Workspace
            {
                Id = WorkspaceId,
                UserId = UserId,
                Name = "Test",
                CreatedAt = now,
                UpdatedAt = now
            });
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
                Title = "语言模型研究",
                CreatedAt = now,
                UpdatedAt = now
            });
            Db.Entities.Add(NewEntity(EntityId, "大型语言模型", "TECHNOLOGY"));
            Db.Entities.Local.Single().Aliases = "[\"LLM\",\"Large Language Model\"]";
            Db.DocumentEntities.Add(new DocumentEntity
            {
                DocumentId = DocumentId,
                EntityId = EntityId,
                MentionCount = 2,
                FirstMention = "large language model",
                Evidence = "large language model evidence",
                CreatedAt = now
            });
            await Db.SaveChangesAsync();
        }

        public Entity NewEntity(Guid id, string name, string type) =>
            new()
            {
                Id = id,
                UserId = UserId,
                WorkspaceId = WorkspaceId.ToString(),
                Name = name,
                CanonicalName = name,
                PreferredNameZh = name,
                NormalizedName = name.ToLowerInvariant(),
                NormalizedKey = name.ToLowerInvariant(),
                EntityType = type,
                Status = "active",
                RowVersion = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class EmptyCandidates : IEntityCandidateResolver
    {
        public Task<IReadOnlyList<EntityCandidateMatch>> RetrieveAsync(
            EntityCandidateRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EntityCandidateMatch>>([]);
        public decimal GetAutoLinkThreshold(string entityType) => 1m;
        public bool ShouldAutoLink(EntityCandidateMatch match, string entityType) => false;
    }
}
