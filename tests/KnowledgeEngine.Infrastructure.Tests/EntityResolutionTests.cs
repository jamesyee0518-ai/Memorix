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

public class EntityResolutionTests
{
    [Fact]
    public void Normalizer_UsesStableKeysParentheticalAliasesAndFixedTypes()
    {
        var registry = new EntityTypeRegistry();
        var normalizer = new EntityNameNormalizer(registry);

        var llm = normalizer.Normalize("大型语言模型（LLM）", "技术");
        var company = normalizer.Normalize("OpenAI, Inc.", "company");
        var gpt4 = normalizer.Normalize("GPT-4", "model");
        var gpt4o = normalizer.Normalize("GPT-4o", "model");

        Assert.Equal("大型语言模型", llm.CanonicalName);
        Assert.Equal("TECHNOLOGY", llm.EntityType);
        Assert.Equal("LLM", llm.Abbreviation);
        Assert.Contains("LLM", llm.AliasCandidates);
        Assert.Equal("openai", company.NormalizedKey);
        Assert.NotEqual(gpt4.NormalizedKey, gpt4o.NormalizedKey);
        Assert.Equal("CONCEPT", registry.Normalize("unknown_dynamic_type"));
    }

    [Fact]
    public async Task ResolveDocumentAsync_GroupsVariantsAndIsIdempotent()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var extracted = new[]
        {
            Entity("OpenAI, Inc.", "company", 0.92m, 2),
            Entity("OpenAI", "公司", 0.88m, 3)
        };

        var first = await fixture.Orchestrator.ResolveDocumentAsync(fixture.DocumentId, extracted);
        var second = await fixture.Orchestrator.ResolveDocumentAsync(fixture.DocumentId, extracted);

        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(1, second.LinkedCount);
        Assert.Equal(1, await fixture.Db.Entities.CountAsync());
        Assert.Equal(1, await fixture.Db.EntityMentions.CountAsync());
        Assert.Equal(1, await fixture.Db.DocumentEntities.CountAsync());

        var entity = await fixture.Db.Entities.SingleAsync();
        var mention = await fixture.Db.EntityMentions.SingleAsync();
        var association = await fixture.Db.DocumentEntities.SingleAsync();
        Assert.Equal("openai", entity.NormalizedKey);
        Assert.Equal("COMPANY", entity.EntityType);
        Assert.Equal("pending_review", entity.Status);
        Assert.Equal(5, mention.OccurrenceCount);
        Assert.Equal(5, association.MentionCount);
        Assert.Equal(5, entity.MentionCount);
        Assert.Equal(1, entity.SourceCount);
    }

    [Fact]
    public async Task ResolveDocumentAsync_LinksOnlyVerifiedUniqueAlias()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var entity = fixture.NewEntity("大语言模型", "TECHNOLOGY");
        fixture.Db.Entities.Add(entity);
        fixture.Db.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(),
            EntityId = entity.Id,
            UserId = fixture.UserId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            Alias = "LLM",
            NormalizedAlias = "llm",
            AliasType = "ABBREVIATION",
            SourceType = "manual",
            IsVerified = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Orchestrator.ResolveDocumentAsync(
            fixture.DocumentId,
            [Entity("LLM", "technology", 0.95m)]);

        Assert.Equal(1, result.LinkedCount);
        Assert.Equal(1, await fixture.Db.Entities.CountAsync());
        var mention = await fixture.Db.EntityMentions.SingleAsync();
        Assert.Equal(entity.Id, mention.EntityId);
        Assert.Equal("ALIAS_EXACT", mention.ResolutionMethod);
        Assert.Equal("AUTO_LINKED", mention.ResolutionStatus);
    }

    [Fact]
    public async Task ResolveDocumentAsync_DoesNotLinkAliasAcrossWorkspaces()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var otherWorkspace = Guid.NewGuid();
        fixture.Db.Workspaces.Add(new Workspace
        {
            Id = otherWorkspace,
            UserId = fixture.UserId,
            Name = "Other",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        var otherEntity = fixture.NewEntity("大语言模型", "TECHNOLOGY", otherWorkspace);
        fixture.Db.Entities.Add(otherEntity);
        fixture.Db.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(),
            EntityId = otherEntity.Id,
            UserId = fixture.UserId,
            WorkspaceId = otherWorkspace.ToString(),
            Alias = "LLM",
            NormalizedAlias = "llm",
            AliasType = "ABBREVIATION",
            SourceType = "manual",
            IsVerified = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Orchestrator.ResolveDocumentAsync(
            fixture.DocumentId,
            [Entity("LLM", "technology", 0.95m)]);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(2, await fixture.Db.Entities.CountAsync());
        var linked = await fixture.Db.EntityMentions.SingleAsync();
        Assert.NotEqual(otherEntity.Id, linked.EntityId);
        Assert.Equal(fixture.WorkspaceId.ToString(), linked.WorkspaceId);
    }

    [Fact]
    public async Task ResolveDocumentAsync_LinksVerifiedExternalIdDespiteNameVariant()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var entity = fixture.NewEntity("OpenAI", "COMPANY");
        fixture.Db.Entities.Add(entity);
        fixture.Db.EntityExternalIds.Add(new EntityExternalId
        {
            Id = Guid.NewGuid(),
            EntityId = entity.Id,
            UserId = fixture.UserId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            IdType = "WIKIDATA",
            IdValue = "q24283660",
            Source = "manual",
            IsVerified = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();
        var extracted = Entity("Open AI Research", "company", 0.95m);
        extracted.ExternalIds =
        [
            new EntityExternalIdResult
            {
                IdType = "wikidata",
                IdValue = "Q24283660",
                Source = "document"
            }
        ];

        var result = await fixture.Orchestrator.ResolveDocumentAsync(
            fixture.DocumentId, [extracted]);

        Assert.Equal(1, result.LinkedCount);
        Assert.Equal(1, await fixture.Db.Entities.CountAsync());
        var mention = await fixture.Db.EntityMentions.SingleAsync();
        Assert.Equal(entity.Id, mention.EntityId);
        Assert.Equal("EXTERNAL_ID_EXACT", mention.ResolutionMethod);
    }

    [Fact]
    public async Task CandidateResolver_HardBlocksModelVersionBoundary()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var gpt4o = fixture.NewEntity("GPT-4o", "MODEL");
        fixture.Db.Entities.Add(gpt4o);
        await fixture.Db.SaveChangesAsync();
        var registry = new EntityTypeRegistry();
        var normalizer = new EntityNameNormalizer(registry);

        var candidates = await fixture.CandidateResolver.RetrieveAsync(
            new EntityCandidateRequest
            {
                UserId = fixture.UserId,
                WorkspaceId = fixture.WorkspaceId.ToString(),
                Normalized = normalizer.Normalize("GPT-4", "MODEL"),
                Mention = "GPT-4"
            });

        var candidate = Assert.Single(candidates);
        Assert.Equal(gpt4o.Id, candidate.EntityId);
        Assert.True(candidate.HardBlocked);
        Assert.Equal(0m, candidate.TotalScore);
        Assert.Contains("MODEL_VERSION_CONFLICT", candidate.ReasonCodes);
    }

    [Fact]
    public async Task CandidateResolver_UsesApprovedTerminologyForCrossLanguageBlocking()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var llm = fixture.NewEntity("Large Language Model", "TECHNOLOGY");
        fixture.Db.Entities.Add(llm);
        await fixture.Terminology.UpsertAsync(
            fixture.UserId,
            fixture.WorkspaceId,
            new Terminology
            {
                SourceLanguage = "en",
                SourceTerm = "Large Language Model",
                TargetLanguage = "zh-CN",
                TargetTerm = "大型语言模型",
                ReviewStatus = "approved",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            false);
        await fixture.Db.SaveChangesAsync();
        var registry = new EntityTypeRegistry();
        var normalizer = new EntityNameNormalizer(registry);

        var candidates = await fixture.CandidateResolver.RetrieveAsync(
            new EntityCandidateRequest
            {
                UserId = fixture.UserId,
                WorkspaceId = fixture.WorkspaceId.ToString(),
                Normalized = normalizer.Normalize("大型语言模型", "TECHNOLOGY"),
                Mention = "大型语言模型"
            });

        var candidate = Assert.Single(candidates);
        Assert.Equal(llm.Id, candidate.EntityId);
        Assert.Equal(1m, candidate.AliasScore);
        Assert.Contains("ALIAS_OR_TERMINOLOGY_MATCH", candidate.ReasonCodes);
    }

    [Fact]
    public async Task ResolveDocumentAsync_AuditsHardBlockedVersionCandidateAndCreatesSeparateEntity()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var gpt4o = fixture.NewEntity("GPT-4o", "MODEL");
        fixture.Db.Entities.Add(gpt4o);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Orchestrator.ResolveDocumentAsync(
            fixture.DocumentId,
            [Entity("GPT-4", "MODEL", 0.96m)]);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(2, await fixture.Db.Entities.CountAsync());
        var candidate = await fixture.Db.EntityResolutionCandidates.SingleAsync();
        Assert.Equal(gpt4o.Id, candidate.CandidateEntityId);
        Assert.Equal("HARD_BLOCKED", candidate.Decision);
        Assert.Contains("MODEL_VERSION_CONFLICT", candidate.ReasonCodes);
    }

    [Fact]
    public async Task CandidateResolver_RecallsRelationNeighborOutsideNamePrefixBlock()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var microsoft = fixture.NewEntity("Microsoft", "COMPANY");
        var azure = fixture.NewEntity("Cloud Platform", "PRODUCT");
        fixture.Db.Entities.AddRange(microsoft, azure);
        fixture.Db.EntityRelations.Add(new EntityRelation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SourceEntityId = microsoft.Id,
            TargetEntityId = azure.Id,
            RelationType = "DEVELOPS",
            Confidence = 0.95m,
            CreatedAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();
        var normalizer = new EntityNameNormalizer(new EntityTypeRegistry());

        var candidates = await fixture.CandidateResolver.RetrieveAsync(
            new EntityCandidateRequest
            {
                UserId = fixture.UserId,
                WorkspaceId = fixture.WorkspaceId.ToString(),
                Normalized = normalizer.Normalize("Azure Service", "PRODUCT"),
                Mention = "Azure Service",
                CooccurringNormalizedKeys = [microsoft.NormalizedKey!]
            });

        var candidate = Assert.Single(candidates);
        Assert.Equal(azure.Id, candidate.EntityId);
        Assert.Equal(1m, candidate.RelationScore);
        Assert.Contains("RELATION_NEIGHBOR_MATCH", candidate.ReasonCodes);
    }

    [Fact]
    public async Task VectorSimilarity_PersistsNameAndDescriptionEmbeddingsByContentHash()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var entity = fixture.NewEntity("OpenAI", "COMPANY");
        entity.Description = "Artificial intelligence research laboratory";
        fixture.Db.Entities.Add(entity);
        await fixture.Db.SaveChangesAsync();
        var embedding = new DeterministicEmbeddingService();
        var service = new EntityVectorSimilarityService(
            fixture.Db,
            embedding,
            Options.Create(new EmbeddingSettings
            {
                Endpoint = "http://localhost:1234",
                Model = "test-embedding"
            }),
            NullLogger<EntityVectorSimilarityService>.Instance);

        var first = await service.ScoreAsync(
            fixture.WorkspaceId.ToString(),
            "OpenAI",
            "Artificial intelligence research laboratory",
            [entity]);
        var second = await service.ScoreAsync(
            fixture.WorkspaceId.ToString(),
            "OpenAI",
            "Artificial intelligence research laboratory",
            [entity]);

        Assert.Equal(1m, first[entity.Id].NameScore);
        Assert.Equal(1m, first[entity.Id].DescriptionScore);
        Assert.Equal(first[entity.Id].NameScore, second[entity.Id].NameScore);
        Assert.Equal(2, await fixture.Db.EntityEmbeddings.CountAsync());
        Assert.All(await fixture.Db.EntityEmbeddings.ToListAsync(), row =>
        {
            Assert.Equal("done", row.Status);
            Assert.Equal("test-embedding", row.Model);
            Assert.Equal(2, row.Dimension);
            Assert.NotEmpty(row.ContentHash);
        });
        Assert.Equal(6, embedding.TotalInputCount);
    }

    [Fact]
    public async Task GovernanceScan_IsIdempotentPausableResumableAndReportsProgress()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        fixture.Db.Entities.AddRange(
            fixture.NewEntity("OpenAI Research", "COMPANY"),
            fixture.NewEntity("OpenAI Research Lab", "COMPANY"));
        await fixture.Db.SaveChangesAsync();
        var request = new StartEntityScanRequest
        {
            WorkspaceId = fixture.WorkspaceId,
            EntityType = "company",
            BatchSize = 1,
            IdempotencyKey = "duplicate-scan-test"
        };

        var first = await fixture.Governance.StartDuplicateScanAsync(
            fixture.UserId, request);
        var replay = await fixture.Governance.StartDuplicateScanAsync(
            fixture.UserId, request);
        Assert.Equal(first.Id, replay.Id);

        var paused = await fixture.Governance.PauseAsync(
            fixture.UserId, first.Id);
        Assert.Equal("paused", paused.Status);
        Assert.False(await fixture.Governance.ProcessNextBatchAsync());

        await fixture.Governance.ResumeAsync(fixture.UserId, first.Id);
        Assert.True(await fixture.Governance.ProcessNextBatchAsync());
        var partial = await fixture.Governance.GetTaskAsync(
            fixture.UserId, first.Id);
        Assert.NotNull(partial);
        Assert.Equal(1, partial.ProcessedItems);
        Assert.Equal("running", partial.Status);

        Assert.True(await fixture.Governance.ProcessNextBatchAsync());
        var completed = await fixture.Governance.GetTaskAsync(
            fixture.UserId, first.Id);
        Assert.NotNull(completed);
        Assert.Equal("completed", completed.Status);
        Assert.Equal(2, completed.ProcessedItems);
        Assert.Equal(2, completed.SucceededItems);
        var candidates = await fixture.Governance.ListCandidatesAsync(
            fixture.UserId, first.Id);
        var candidate = Assert.Single(candidates);
        Assert.Equal("DUPLICATE_CANDIDATE", candidate.TaskType);
        Assert.NotNull(candidate.SubjectEntityId);
        Assert.NotNull(candidate.CandidateEntityId);
    }

    [Fact]
    public async Task Disambiguation_UsesOnlyInScopeCandidateAndReturnsStructuredDecision()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var entity = fixture.NewEntity("OpenAI", "COMPANY");
        entity.Description = "Artificial intelligence research company";
        fixture.Db.Entities.Add(entity);
        await fixture.Db.SaveChangesAsync();
        var llm = new StubLlmService($$"""
            ```json
            {
              "decision": "SAME_ENTITY",
              "candidate_entity_id": "{{entity.Id}}",
              "confidence": 0.94,
              "reason_codes": ["context_match"],
              "explanation": "The context identifies the same company."
            }
            ```
            """);
        var service = new EntityDisambiguationService(
            fixture.Db,
            llm,
            Options.Create(new EntityResolutionSettings()),
            NullLogger<EntityDisambiguationService>.Instance);

        var result = await service.DecideAsync(new EntityDisambiguationRequest
        {
            UserId = fixture.UserId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            Mention = "Open AI",
            EntityType = "COMPANY",
            Context = "Open AI announced a new model.",
            Candidates =
            [
                new EntityCandidateMatch
                {
                    EntityId = entity.Id,
                    TotalScore = 0.84m,
                    NameScore = 0.9m
                }
            ]
        });

        Assert.Equal("SAME_ENTITY", result.Decision);
        Assert.Equal(entity.Id, result.CandidateEntityId);
        Assert.Equal(0.94m, result.Confidence);
        Assert.Contains("CONTEXT_MATCH", result.ReasonCodes);
        Assert.False(result.IsFallback);
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task Disambiguation_MalformedOutputAndHardBlockFailClosed()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var entity = fixture.NewEntity("GPT-4o", "MODEL");
        fixture.Db.Entities.Add(entity);
        await fixture.Db.SaveChangesAsync();
        var llm = new StubLlmService("not-json");
        var service = new EntityDisambiguationService(
            fixture.Db,
            llm,
            Options.Create(new EntityResolutionSettings()),
            NullLogger<EntityDisambiguationService>.Instance);
        var normalCandidate = new EntityCandidateMatch
        {
            EntityId = entity.Id,
            TotalScore = 0.84m
        };

        var malformed = await service.DecideAsync(new EntityDisambiguationRequest
        {
            UserId = fixture.UserId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            Mention = "GPT",
            EntityType = "MODEL",
            Candidates = [normalCandidate]
        });
        var blocked = await service.DecideAsync(new EntityDisambiguationRequest
        {
            UserId = fixture.UserId,
            WorkspaceId = fixture.WorkspaceId.ToString(),
            Mention = "GPT-4",
            EntityType = "MODEL",
            Candidates =
            [
                new EntityCandidateMatch
                {
                    EntityId = entity.Id,
                    TotalScore = 0.99m,
                    HardBlocked = true,
                    ReasonCodes = ["MODEL_VERSION_CONFLICT"]
                }
            ]
        });

        Assert.Equal("INSUFFICIENT_EVIDENCE", malformed.Decision);
        Assert.Contains("LLM_INVALID_STRUCTURED_OUTPUT", malformed.ReasonCodes);
        Assert.True(malformed.IsFallback);
        Assert.Equal("INSUFFICIENT_EVIDENCE", blocked.Decision);
        Assert.Contains(
            "LLM_DISAMBIGUATION_DISABLED_OR_NO_CANDIDATE",
            blocked.ReasonCodes);
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task ResolveDocumentAsync_LinksHighConfidenceLlmDecisionAndAuditsIt()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var entity = fixture.NewEntity("International Business Machines", "COMPANY");
        fixture.Db.Entities.Add(entity);
        await fixture.Db.SaveChangesAsync();
        var candidate = new EntityCandidateMatch
        {
            EntityId = entity.Id,
            NameScore = 0.8m,
            ContextScore = 0.8m,
            TotalScore = 0.84m,
            ReasonCodes = ["NAME_SIMILAR", "CONTEXT_SIMILAR"]
        };
        var registry = new EntityTypeRegistry();
        var normalizer = new EntityNameNormalizer(registry);
        var orchestrator = new EntityResolutionOrchestrator(
            fixture.Db,
            normalizer,
            registry,
            new StubCandidateResolver(candidate),
            new StubDisambiguationService(new EntityDisambiguationResult
            {
                Decision = "SAME_ENTITY",
                CandidateEntityId = entity.Id,
                Confidence = 0.95m,
                ReasonCodes = ["LLM_CONTEXT_CONFIRMED"],
                Explanation = "The context identifies IBM.",
                Model = "test-llm",
                PromptVersion = "entity_disambiguation_v1"
            }),
            Options.Create(new EntityResolutionSettings
            {
                EnableAutoLink = true,
                EnableLlmDisambiguation = true,
                LlmMinimumCandidateScore = 0.78m,
                LlmLinkConfidence = 0.90m
            }),
            NullLogger<EntityResolutionOrchestrator>.Instance);

        var result = await orchestrator.ResolveDocumentAsync(
            fixture.DocumentId,
            [
                new EntityResult
                {
                    Name = "IBM",
                    Mention = "IBM",
                    EntityType = "COMPANY",
                    Confidence = 0.95m,
                    Importance = 0.9m
                }
            ]);

        Assert.Equal(1, result.LinkedCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, await fixture.Db.Entities.CountAsync());
        var mention = await fixture.Db.EntityMentions.SingleAsync();
        Assert.Equal("LLM_CONTEXT", mention.ResolutionMethod);
        Assert.Equal(0.95m, mention.ResolutionScore);
        var audit = await fixture.Db.EntityResolutionCandidates.SingleAsync();
        Assert.Equal("LLM_LINKED", audit.Decision);
        Assert.Equal("SAME_ENTITY", audit.LlmDecision);
        Assert.Equal("test-llm", audit.LlmModel);
    }

    [Fact]
    public async Task ResolveDocumentAsync_LlmInsufficientEvidenceCreatesReviewTask()
    {
        await using var fixture = await ResolutionFixture.CreateAsync();
        var candidateEntity = fixture.NewEntity("Acme Corporation", "COMPANY");
        fixture.Db.Entities.Add(candidateEntity);
        await fixture.Db.SaveChangesAsync();
        var candidate = new EntityCandidateMatch
        {
            EntityId = candidateEntity.Id,
            TotalScore = 0.82m,
            NameScore = 0.8m,
            ReasonCodes = ["NAME_SIMILAR"]
        };
        var registry = new EntityTypeRegistry();
        var normalizer = new EntityNameNormalizer(registry);
        var orchestrator = new EntityResolutionOrchestrator(
            fixture.Db,
            normalizer,
            registry,
            new StubCandidateResolver(candidate),
            new StubDisambiguationService(new EntityDisambiguationResult
            {
                Decision = "INSUFFICIENT_EVIDENCE",
                Confidence = 0.55m,
                ReasonCodes = ["AMBIGUOUS_COMPANY_NAME"],
                Explanation = "The context is not specific enough.",
                Model = "test-llm"
            }),
            Options.Create(new EntityResolutionSettings
            {
                EnableLlmDisambiguation = true,
                LlmMinimumCandidateScore = 0.78m,
                LlmLinkConfidence = 0.90m
            }),
            NullLogger<EntityResolutionOrchestrator>.Instance);

        var result = await orchestrator.ResolveDocumentAsync(
            fixture.DocumentId,
            [Entity("Acme Labs", "COMPANY", 0.95m)]);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(2, await fixture.Db.Entities.CountAsync());
        var task = await fixture.Db.EntityGovernanceTasks.SingleAsync(
            x => x.TaskType == "UNRESOLVED_MENTION");
        Assert.Equal("pending", task.Status);
        Assert.Equal(candidateEntity.Id, task.CandidateEntityId);
        Assert.Contains("AMBIGUOUS_COMPANY_NAME", task.ReasonCodes);
        var audit = await fixture.Db.EntityResolutionCandidates.SingleAsync();
        Assert.Equal("LLM_INSUFFICIENT_EVIDENCE", audit.Decision);
        Assert.Equal("INSUFFICIENT_EVIDENCE", audit.LlmDecision);
    }

    private static EntityResult Entity(
        string name,
        string type,
        decimal confidence,
        int mentionCount = 1) =>
        new()
        {
            Name = name,
            Mention = name,
            EntityType = type,
            Confidence = confidence,
            Importance = 0.8m,
            MentionCount = mentionCount
        };

    private sealed class ResolutionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ResolutionFixture(
            SqliteConnection connection,
            AppDbContext db,
            TerminologyService terminology,
            EntityCandidateResolver candidateResolver,
            EntityGovernanceService governance,
            EntityResolutionOrchestrator orchestrator,
            Guid userId,
            Guid workspaceId,
            Guid documentId)
        {
            _connection = connection;
            Db = db;
            Terminology = terminology;
            CandidateResolver = candidateResolver;
            Governance = governance;
            Orchestrator = orchestrator;
            UserId = userId;
            WorkspaceId = workspaceId;
            DocumentId = documentId;
        }

        public AppDbContext Db { get; }
        public TerminologyService Terminology { get; }
        public EntityCandidateResolver CandidateResolver { get; }
        public EntityGovernanceService Governance { get; }
        public EntityResolutionOrchestrator Orchestrator { get; }
        public Guid UserId { get; }
        public Guid WorkspaceId { get; }
        public Guid DocumentId { get; }

        public static async Task<ResolutionFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var userId = Guid.NewGuid();
            var workspaceId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            db.Users.Add(new User
            {
                Id = userId,
                Email = $"{userId:N}@example.local",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                UserId = userId,
                Name = "Workspace",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Sources.Add(new Source
            {
                Id = sourceId,
                UserId = userId,
                SourceType = "text",
                ImportedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Documents.Add(new Document
            {
                Id = documentId,
                SourceId = sourceId,
                UserId = userId,
                WorkspaceId = workspaceId,
                Title = "Entity resolution test",
                Language = "en",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();

            var registry = new EntityTypeRegistry();
            var normalizer = new EntityNameNormalizer(registry);
            var terminology = new TerminologyService(db);
            var candidateResolver = new EntityCandidateResolver(
                db,
                normalizer,
                terminology,
                new EmptyVectorSimilarityService(),
                Options.Create(new EntityResolutionSettings
                {
                    EnableVectorCandidates = false
                }),
                NullLogger<EntityCandidateResolver>.Instance);
            var orchestrator = new EntityResolutionOrchestrator(
                db,
                normalizer,
                registry,
                candidateResolver,
                new FallbackDisambiguationService(),
                Options.Create(new EntityResolutionSettings
                {
                    EnableVectorCandidates = false,
                    EnableLlmDisambiguation = false
                }),
                NullLogger<EntityResolutionOrchestrator>.Instance);
            var governance = new EntityGovernanceService(
                db,
                normalizer,
                candidateResolver,
                new EntityMergeService(db, new EntityRedirectResolver(db)),
                NullLogger<EntityGovernanceService>.Instance);
            return new ResolutionFixture(
                connection,
                db,
                terminology,
                candidateResolver,
                governance,
                orchestrator,
                userId,
                workspaceId,
                documentId);
        }

        public Entity NewEntity(string name, string type, Guid? workspaceId = null)
        {
            var normalizer = new EntityNameNormalizer(new EntityTypeRegistry());
            var normalized = normalizer.Normalize(name, type);
            return new Entity
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                WorkspaceId = (workspaceId ?? WorkspaceId).ToString(),
                Name = normalized.CanonicalName,
                CanonicalName = normalized.CanonicalName,
                NormalizedName = normalized.NormalizedKey,
                NormalizedKey = normalized.NormalizedKey,
                EntityType = normalized.EntityType,
                Status = "active",
                Source = "manual",
                IsVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class EmptyVectorSimilarityService : IEntityVectorSimilarityService
    {
        public Task<IReadOnlyDictionary<Guid, EntityVectorScores>> ScoreAsync(
            string workspaceId,
            string queryName,
            string? queryContext,
            IReadOnlyCollection<Entity> candidates,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, EntityVectorScores>>(
                new Dictionary<Guid, EntityVectorScores>());
    }

    private sealed class FallbackDisambiguationService : IEntityDisambiguationService
    {
        public Task<EntityDisambiguationResult> DecideAsync(
            EntityDisambiguationRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new EntityDisambiguationResult
            {
                Decision = "INSUFFICIENT_EVIDENCE",
                ReasonCodes = ["TEST_FALLBACK"],
                IsFallback = true
            });
    }

    private sealed class DeterministicEmbeddingService : IEmbeddingService
    {
        public int TotalInputCount { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(Vector(text));

        public Task<List<float[]>> EmbedBatchAsync(
            List<string> texts,
            CancellationToken ct = default)
        {
            TotalInputCount += texts.Count;
            return Task.FromResult(texts.Select(Vector).ToList());
        }

        private static float[] Vector(string text) =>
            text.Contains("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? [1f, 0f]
                : [0f, 1f];
    }

    private sealed class StubLlmService(string content) : ILlmService
    {
        public int CallCount { get; private set; }

        public Task<LlmResult> CompleteAsync(
            string systemPrompt,
            string userPrompt,
            string? model = null,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new LlmResult
            {
                Content = content,
                Model = model ?? "test-llm"
            });
        }
    }

    private sealed class StubCandidateResolver(EntityCandidateMatch candidate)
        : IEntityCandidateResolver
    {
        public Task<IReadOnlyList<EntityCandidateMatch>> RetrieveAsync(
            EntityCandidateRequest request,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EntityCandidateMatch>>([candidate]);

        public decimal GetAutoLinkThreshold(string entityType) => 0.92m;
        public bool ShouldAutoLink(EntityCandidateMatch match, string entityType) => false;
    }

    private sealed class StubDisambiguationService(EntityDisambiguationResult result)
        : IEntityDisambiguationService
    {
        public Task<EntityDisambiguationResult> DecideAsync(
            EntityDisambiguationRequest request,
            CancellationToken ct = default) => Task.FromResult(result);
    }
}
