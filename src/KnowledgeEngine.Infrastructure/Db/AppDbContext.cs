using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Db;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<FileObject> Files => Set<FileObject>();
    public DbSet<IngestJob> IngestJobs => Set<IngestJob>();

    // Phase 2 entities
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<DocumentEntity> DocumentEntities => Set<DocumentEntity>();
    public DbSet<EntityRelation> EntityRelations => Set<EntityRelation>();
    public DbSet<AiJob> AiJobs => Set<AiJob>();

    // Phase 3 entities
    public DbSet<SearchLog> SearchLogs => Set<SearchLog>();
    public DbSet<QaSession> QaSessions => Set<QaSession>();
    public DbSet<QaMessage> QaMessages => Set<QaMessage>();
    public DbSet<RetrievalLog> RetrievalLogs => Set<RetrievalLog>();
    public DbSet<DocumentProcessingLog> DocumentProcessingLogs => Set<DocumentProcessingLog>();

    // Phase 4 entities
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();
    public DbSet<ReportJob> ReportJobs => Set<ReportJob>();
    public DbSet<ReportSource> ReportSources => Set<ReportSource>();
    public DbSet<ReportCitation> ReportCitations => Set<ReportCitation>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<ExportFile> ExportFiles => Set<ExportFile>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<AgentInvocationLog> AgentInvocationLogs => Set<AgentInvocationLog>();

    // Phase 4 data-layer entities (tags/entities/embeddings/vector index)
    public DbSet<ChunkEmbedding> ChunkEmbeddings => Set<ChunkEmbedding>();
    public DbSet<VectorIndexState> VectorIndexStates => Set<VectorIndexState>();

    // Phase 5 entities
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiCallLog> ApiCallLogs => Set<ApiCallLog>();
    public DbSet<UserUsageDaily> UserUsageDaily => Set<UserUsageDaily>();
    public DbSet<BetaUser> BetaUsers => Set<BetaUser>();
    public DbSet<FeedbackItem> FeedbackItems => Set<FeedbackItem>();

    // Phase 7 entities
    public DbSet<ReleaseNote> ReleaseNotes => Set<ReleaseNote>();

    // Dual-mode foundation entities
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<InboxItem> InboxItems => Set<InboxItem>();
    public DbSet<InboxAttachment> InboxAttachments => Set<InboxAttachment>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<InboxEvent> InboxEvents => Set<InboxEvent>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();
    public DbSet<CloudInboxSyncLog> CloudInboxSyncLogs => Set<CloudInboxSyncLog>();
    public DbSet<MobileDevice> MobileDevices => Set<MobileDevice>();
    public DbSet<PushNotification> PushNotifications => Set<PushNotification>();
    public DbSet<WorkspaceSetting> WorkspaceSettings => Set<WorkspaceSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        ConfigureUser(modelBuilder);
        ConfigureTopic(modelBuilder);
        ConfigureSource(modelBuilder);
        ConfigureFileObject(modelBuilder);
        ConfigureIngestJob(modelBuilder);

        // Phase 2 configurations
        ConfigureDocument(modelBuilder);
        ConfigureDocumentChunk(modelBuilder);
        ConfigureTag(modelBuilder);
        ConfigureDocumentTag(modelBuilder);
        ConfigureEntity(modelBuilder);
        ConfigureDocumentEntity(modelBuilder);
        ConfigureEntityRelation(modelBuilder);
        ConfigureAiJob(modelBuilder);

        // Phase 3 configurations
        ConfigureSearchLog(modelBuilder);
        ConfigureQaSession(modelBuilder);
        ConfigureQaMessage(modelBuilder);
        ConfigureRetrievalLog(modelBuilder);
        ConfigureDocumentProcessingLog(modelBuilder);

        // Phase 4 configurations
        ConfigureReport(modelBuilder);
        ConfigureReportTemplate(modelBuilder);
        ConfigureReportJob(modelBuilder);
        ConfigureReportSource(modelBuilder);
        ConfigureReportCitation(modelBuilder);
        ConfigureExportJob(modelBuilder);
        ConfigureExportFile(modelBuilder);
        ConfigureAgentProfile(modelBuilder);
        ConfigureAgentInvocationLog(modelBuilder);
        ConfigureChunkEmbedding(modelBuilder);
        ConfigureVectorIndexState(modelBuilder);

        // Phase 5 configurations
        ConfigureApiKey(modelBuilder);
        ConfigureApiCallLog(modelBuilder);
        ConfigureUserUsageDaily(modelBuilder);
        ConfigureBetaUser(modelBuilder);
        ConfigureFeedbackItem(modelBuilder);

        // Phase 7 configurations
        ConfigureReleaseNote(modelBuilder);

        // Dual-mode foundation configurations
        ConfigureWorkspace(modelBuilder);
        ConfigureInboxItem(modelBuilder);
        ConfigureInboxAttachment(modelBuilder);
        ConfigureImportJob(modelBuilder);
        ConfigureInboxEvent(modelBuilder);
        ConfigureSyncCursor(modelBuilder);
        ConfigureCloudInboxSyncLog(modelBuilder);
        ConfigureMobileDevice(modelBuilder);
        ConfigurePushNotification(modelBuilder);
        ConfigureWorkspaceSetting(modelBuilder);
    }

    /// <summary>
    /// Ensures the pgvector extension and embedding column exist.
    /// Call this after EnsureCreated().
    /// </summary>
    public async Task EnsureVectorSetupAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector", ct);
        await Database.ExecuteSqlRawAsync(
            "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS embedding vector(2560)", ct);
    }

    /// <summary>
    /// Ensures dual-mode tables added after the initial EnsureCreated() schema exist.
    /// </summary>
    public async Task EnsureDualModeSetupAsync(CancellationToken ct = default)
    {
        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS cloud_inbox_sync_logs (
                id uuid PRIMARY KEY,
                workspace_id uuid NOT NULL,
                direction varchar(50) NOT NULL,
                status varchar(50) NOT NULL,
                cloud_api_base_url varchar(2048),
                cloud_workspace_id varchar(200),
                retention varchar(50) NOT NULL,
                pulled_count integer NOT NULL DEFAULT 0,
                failed_count integer NOT NULL DEFAULT 0,
                next_cursor text,
                error_message varchar(2000),
                started_at timestamp with time zone NOT NULL,
                finished_at timestamp with time zone NOT NULL,
                duration_ms bigint NOT NULL DEFAULT 0,
                created_at timestamp with time zone NOT NULL
            )
            """, ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_cloud_inbox_sync_logs_workspace_created ON cloud_inbox_sync_logs(workspace_id, created_at)", ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_cloud_inbox_sync_logs_workspace_status ON cloud_inbox_sync_logs(workspace_id, status)", ct);

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS mobile_devices (
                id uuid PRIMARY KEY,
                workspace_id uuid NOT NULL,
                client_id varchar(200) NOT NULL,
                device_name varchar(200),
                platform varchar(100),
                push_token text,
                refresh_token_hash varchar(128),
                refresh_token_expires_at timestamp with time zone,
                status varchar(50) NOT NULL DEFAULT 'active',
                last_seen_at timestamp with time zone,
                bound_at timestamp with time zone NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            )
            """, ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_mobile_devices_workspace_client ON mobile_devices(workspace_id, client_id)", ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_mobile_devices_workspace_updated ON mobile_devices(workspace_id, updated_at)", ct);
        await Database.ExecuteSqlRawAsync(
            "ALTER TABLE mobile_devices ADD COLUMN IF NOT EXISTS refresh_token_hash varchar(128)", ct);
        await Database.ExecuteSqlRawAsync(
            "ALTER TABLE mobile_devices ADD COLUMN IF NOT EXISTS refresh_token_expires_at timestamp with time zone", ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_mobile_devices_refresh_token_hash ON mobile_devices(refresh_token_hash)", ct);

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS push_notifications (
                id uuid PRIMARY KEY,
                workspace_id uuid NOT NULL,
                client_id varchar(200) NOT NULL,
                push_token text NOT NULL,
                title varchar(300) NOT NULL,
                body varchar(2000) NOT NULL,
                data_json text,
                status varchar(50) NOT NULL DEFAULT 'pending',
                attempt integer NOT NULL DEFAULT 0,
                max_attempts integer NOT NULL DEFAULT 3,
                provider_response text,
                error_message varchar(2000),
                next_attempt_at timestamp with time zone,
                sent_at timestamp with time zone,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            )
            """, ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_push_notifications_status_next ON push_notifications(status, next_attempt_at)", ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_push_notifications_workspace_created ON push_notifications(workspace_id, created_at)", ct);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<User>();
        e.ToTable("users");
        e.HasKey(u => u.Id);
        e.Property(u => u.Id).HasColumnType("uuid");
        e.Property(u => u.Email).IsRequired().HasMaxLength(255);
        e.Property(u => u.Nickname).HasMaxLength(100);
        e.Property(u => u.PasswordHash).IsRequired().HasMaxLength(255);
        e.Property(u => u.AvatarUrl).HasMaxLength(1024);
        e.Property(u => u.PlanCode).IsRequired().HasMaxLength(50);
        e.Property(u => u.Status).IsRequired().HasMaxLength(50);
        e.Property(u => u.Timezone).IsRequired().HasMaxLength(64);
        e.Property(u => u.CreatedAt).IsRequired();
        e.Property(u => u.UpdatedAt).IsRequired();

        e.HasIndex(u => u.Email).IsUnique();
    }

    private static void ConfigureTopic(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Topic>();
        e.ToTable("topics");
        e.HasKey(t => t.Id);
        e.Property(t => t.Id).HasColumnType("uuid");
        e.Property(t => t.UserId).HasColumnType("uuid");
        e.Property(t => t.Name).IsRequired().HasMaxLength(200);
        e.Property(t => t.Description).HasMaxLength(2000);
        e.Property(t => t.Domain).HasMaxLength(255);
        e.Property(t => t.Visibility).IsRequired().HasMaxLength(50);
        e.Property(t => t.Status).IsRequired().HasMaxLength(50);
        e.Property(t => t.CreatedAt).IsRequired();
        e.Property(t => t.UpdatedAt).IsRequired();

        e.HasIndex(t => t.UserId);
    }

    private static void ConfigureSource(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Source>();
        e.ToTable("sources");
        e.HasKey(s => s.Id);
        e.Property(s => s.Id).HasColumnType("uuid");
        e.Property(s => s.UserId).HasColumnType("uuid");
        e.Property(s => s.TopicId).HasColumnType("uuid");
        e.Property(s => s.SourceType).IsRequired().HasMaxLength(50);
        e.Property(s => s.Title).HasMaxLength(500);
        e.Property(s => s.Url).HasMaxLength(2048);
        e.Property(s => s.Domain).HasMaxLength(255);
        e.Property(s => s.Author).HasMaxLength(255);
        e.Property(s => s.OriginalFileId).HasColumnType("uuid");
        e.Property(s => s.RawText).HasColumnType("text");
        e.Property(s => s.ContentHash).HasMaxLength(128);
        e.Property(s => s.Status).IsRequired().HasMaxLength(50);
        e.Property(s => s.ErrorMessage).HasMaxLength(2000);
        e.Property(s => s.ImportedAt).IsRequired();
        e.Property(s => s.CreatedAt).IsRequired();
        e.Property(s => s.UpdatedAt).IsRequired();

        e.HasIndex(s => s.UserId);
        e.HasIndex(s => s.TopicId);
        e.HasIndex(s => s.Status);
        e.HasIndex(s => s.ContentHash);
        e.HasIndex(s => s.ImportedAt);
    }

    private static void ConfigureFileObject(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<FileObject>();
        e.ToTable("files");
        e.HasKey(f => f.Id);
        e.Property(f => f.Id).HasColumnType("uuid");
        e.Property(f => f.WorkspaceId).HasColumnType("uuid");
        e.Property(f => f.Bucket).HasMaxLength(255);
        e.Property(f => f.ObjectKey).HasMaxLength(1024);
        e.Property(f => f.LocalPath).HasMaxLength(2048);
        e.Property(f => f.OriginalFilename).HasMaxLength(500);
        e.Property(f => f.MimeType).HasMaxLength(255);
        e.Property(f => f.Extension).HasMaxLength(50);
        e.Property(f => f.Sha256).HasMaxLength(128);
        e.Property(f => f.StorageProvider).IsRequired().HasMaxLength(50);
        e.Property(f => f.CreatedAt).IsRequired();

        e.HasIndex(f => f.WorkspaceId);
        e.HasIndex(f => f.Sha256);
    }

    private static void ConfigureIngestJob(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<IngestJob>();
        e.ToTable("ingest_jobs");
        e.HasKey(j => j.Id);
        e.Property(j => j.Id).HasColumnType("uuid");
        e.Property(j => j.UserId).HasColumnType("uuid");
        e.Property(j => j.SourceId).HasColumnType("uuid");
        e.Property(j => j.JobType).IsRequired().HasMaxLength(50);
        e.Property(j => j.Status).IsRequired().HasMaxLength(50);
        e.Property(j => j.ErrorMessage).HasMaxLength(2000);
        e.Property(j => j.CreatedAt).IsRequired();

        e.HasIndex(j => j.UserId);
        e.HasIndex(j => j.SourceId);
        e.HasIndex(j => j.Status);
    }

    private static void ConfigureDocument(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Document>();
        e.ToTable("documents");
        e.HasKey(d => d.Id);
        e.Property(d => d.Id).HasColumnType("uuid");
        e.Property(d => d.SourceId).HasColumnType("uuid");
        e.Property(d => d.UserId).HasColumnType("uuid");
        e.Property(d => d.TopicId).HasColumnType("uuid");
        e.Property(d => d.Title).IsRequired().HasMaxLength(1000);
        e.Property(d => d.ContentMarkdown).HasColumnType("text");
        e.Property(d => d.ContentText).HasColumnType("text");
        e.Property(d => d.Language).HasMaxLength(20);
        e.Property(d => d.Summary).HasColumnType("text");
        e.Property(d => d.OneSentenceConclusion).HasMaxLength(2000);
        // JSONB fields stored as text
        e.Property(d => d.KeyPoints).HasColumnType("text");
        e.Property(d => d.BusinessSignals).HasColumnType("text");
        e.Property(d => d.TechnicalSignals).HasColumnType("text");
        e.Property(d => d.Risks).HasColumnType("text");
        e.Property(d => d.Opportunities).HasColumnType("text");
        e.Property(d => d.ReusableMaterials).HasColumnType("text");
        e.Property(d => d.AiStatus).IsRequired().HasMaxLength(50);
        e.Property(d => d.AiModel).HasMaxLength(100);
        e.Property(d => d.PromptVersion).HasMaxLength(50);
        // Phase 3: ChunkStatus
        e.Property(d => d.ChunkStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        // Phase 3: Source metadata
        e.Property(d => d.SourceType).HasMaxLength(50);
        e.Property(d => d.SourceUrl).HasMaxLength(2048);
        e.Property(d => d.SourceDomain).HasMaxLength(255);
        e.Property(d => d.Author).HasMaxLength(255);
        e.Property(d => d.RecommendedTags).HasColumnType("text");
        e.Property(d => d.ValueScoreReason).HasColumnType("text");
        e.Property(d => d.ShouldDeepProcess).IsRequired().HasDefaultValue(true);
        // Phase 3: Multi-stage status
        e.Property(d => d.ParseStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        e.Property(d => d.CleanStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        e.Property(d => d.IndexStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        // Phase 4: Tag/Entity/Embedding status
        e.Property(d => d.TagStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        e.Property(d => d.EntityStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        e.Property(d => d.EmbeddingStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        // Phase 3: Parser/cleaner metadata
        e.Property(d => d.ParserName).HasMaxLength(100);
        e.Property(d => d.ParserVersion).HasMaxLength(50);
        e.Property(d => d.CleanerVersion).HasMaxLength(50);
        // Phase 3: AI raw output
        e.Property(d => d.AiRawOutput).HasColumnType("text");
        e.Property(d => d.AiErrorMessage).HasColumnType("text");
        // Phase 7: Sensitivity level
        e.Property(d => d.SensitivityLevel).IsRequired().HasMaxLength(50).HasDefaultValue("normal");
        e.Property(d => d.CreatedAt).IsRequired();
        e.Property(d => d.UpdatedAt).IsRequired();

        e.HasIndex(d => d.UserId);
        e.HasIndex(d => d.TopicId);
        e.HasIndex(d => d.SourceId);
        e.HasIndex(d => d.AiStatus);
        e.HasIndex(d => d.ChunkStatus);
        e.HasIndex(d => d.ParseStatus);
        e.HasIndex(d => d.CleanStatus);
        e.HasIndex(d => d.IndexStatus);
        e.HasIndex(d => d.TagStatus);
        e.HasIndex(d => d.EntityStatus);
        e.HasIndex(d => d.EmbeddingStatus);
        e.HasIndex(d => d.ValueScore);
        e.HasIndex(d => d.CreatedAt);
    }

    private static void ConfigureDocumentChunk(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<DocumentChunk>();
        e.ToTable("document_chunks");
        e.HasKey(c => c.Id);
        e.Property(c => c.Id).HasColumnType("uuid");
        e.Property(c => c.DocumentId).HasColumnType("uuid");
        e.Property(c => c.SourceId).HasColumnType("uuid");
        e.Property(c => c.UserId).HasColumnType("uuid");
        e.Property(c => c.TopicId).HasColumnType("uuid");
        e.Property(c => c.ChunkIndex).IsRequired();
        e.Property(c => c.ChunkTitle).HasMaxLength(500);
        e.Property(c => c.Content).IsRequired().HasColumnType("text");
        e.Property(c => c.ContentMarkdown).HasColumnType("text");
        e.Property(c => c.TokenCount);
        e.Property(c => c.CharCount);
        e.Property(c => c.StartOffset);
        e.Property(c => c.EndOffset);
        // Embedding: float[] cannot be natively mapped to pgvector without Pgvector.EntityFrameworkCore.
        // Ignore from EF Core's normal mapping; the column is created via EnsureVectorSetupAsync()
        // and all vector operations use raw SQL.
        e.Ignore(c => c.Embedding);
        e.Property(c => c.EmbeddingModel).HasMaxLength(100);
        e.Property(c => c.EmbeddingStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        e.Property(c => c.QualityScore);
        e.Property(c => c.Metadata).HasColumnType("text");
        e.Property(c => c.CreatedAt).IsRequired();
        e.Property(c => c.UpdatedAt).IsRequired();
        // Phase 4 fields
        e.Property(c => c.ChunkUid).HasMaxLength(200);
        e.Property(c => c.HeadingPath).HasMaxLength(1000);
        e.Property(c => c.SectionLevel);
        e.Property(c => c.ContentHash).HasMaxLength(128);
        e.Property(c => c.PrevChunkId).HasColumnType("uuid");
        e.Property(c => c.NextChunkId).HasColumnType("uuid");
        e.Property(c => c.PageStart);
        e.Property(c => c.PageEnd);
        e.Property(c => c.IndexStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");

        e.HasIndex(c => c.DocumentId);
        e.HasIndex(c => c.UserId);
        e.HasIndex(c => new { c.UserId, c.TopicId });
        e.HasIndex(c => c.EmbeddingStatus);
        e.HasIndex(c => c.ContentHash);
    }

    private static void ConfigureTag(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Tag>();
        e.ToTable("tags");
        e.HasKey(t => t.Id);
        e.Property(t => t.Id).HasColumnType("uuid");
        e.Property(t => t.UserId).HasColumnType("uuid");
        e.Property(t => t.Name).IsRequired().HasMaxLength(200);
        e.Property(t => t.Type).IsRequired().HasMaxLength(50);
        e.Property(t => t.Description).HasMaxLength(1000);
        e.Property(t => t.CreatedAt).IsRequired();
        // Phase 4 fields
        e.Property(t => t.WorkspaceId).IsRequired().HasMaxLength(200);
        e.Property(t => t.NormalizedName).HasMaxLength(200);
        e.Property(t => t.DisplayName).HasMaxLength(200);
        e.Property(t => t.TagType).HasMaxLength(50);
        e.Property(t => t.Color).HasMaxLength(50);
        e.Property(t => t.Aliases).HasColumnType("text");
        e.Property(t => t.Source).IsRequired().HasMaxLength(50).HasDefaultValue("manual");
        e.Property(t => t.UsageCount).IsRequired().HasDefaultValue(0);
        e.Property(t => t.IsSystem).IsRequired().HasDefaultValue(false);
        e.Property(t => t.IsArchived).IsRequired().HasDefaultValue(false);
        e.Property(t => t.UpdatedAt).IsRequired();

        e.HasIndex(t => new { t.WorkspaceId, t.NormalizedName }).IsUnique();
        e.HasIndex(t => new { t.WorkspaceId, t.TagType });
        e.HasIndex(t => new { t.UserId, t.Name, t.Type }).IsUnique();
        e.HasIndex(t => new { t.UserId, t.Type });
    }

    private static void ConfigureDocumentTag(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<DocumentTag>();
        e.ToTable("document_tags");
        e.HasKey(dt => new { dt.DocumentId, dt.TagId });
        e.Property(dt => dt.DocumentId).HasColumnType("uuid");
        e.Property(dt => dt.TagId).HasColumnType("uuid");
        e.Property(dt => dt.Source).IsRequired().HasMaxLength(50);
        e.Property(dt => dt.Confidence).HasColumnType("numeric(5,4)");
        e.Property(dt => dt.CreatedAt).IsRequired();
        // Phase 4 fields
        e.Property(dt => dt.Reason).HasMaxLength(2000);
        e.Property(dt => dt.IsConfirmed).IsRequired().HasDefaultValue(false);
        e.Property(dt => dt.ConfirmedBy).HasMaxLength(200);
        e.Property(dt => dt.ConfirmedAt);

        e.HasIndex(dt => dt.TagId);
    }

    private static void ConfigureEntity(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Entity>();
        e.ToTable("entities");
        e.HasKey(en => en.Id);
        e.Property(en => en.Id).HasColumnType("uuid");
        e.Property(en => en.UserId).HasColumnType("uuid");
        e.Property(en => en.Name).IsRequired().HasMaxLength(500);
        e.Property(en => en.NormalizedName).HasMaxLength(500);
        e.Property(en => en.EntityType).IsRequired().HasMaxLength(50);
        e.Property(en => en.Description).HasMaxLength(2000);
        e.Property(en => en.Metadata).HasColumnType("text");
        e.Property(en => en.CreatedAt).IsRequired();
        e.Property(en => en.UpdatedAt).IsRequired();
        // Phase 4 fields
        e.Property(en => en.WorkspaceId).IsRequired().HasMaxLength(200);
        e.Property(en => en.DisplayName).HasMaxLength(500);
        e.Property(en => en.Aliases).HasColumnType("text");
        e.Property(en => en.ExternalRef).HasMaxLength(1000);
        e.Property(en => en.Source).IsRequired().HasMaxLength(50).HasDefaultValue("ai");
        e.Property(en => en.UsageCount).IsRequired().HasDefaultValue(0);
        e.Property(en => en.IsVerified).IsRequired().HasDefaultValue(false);
        e.Property(en => en.IsArchived).IsRequired().HasDefaultValue(false);

        e.HasIndex(en => new { en.WorkspaceId, en.NormalizedName, en.EntityType }).IsUnique();
        e.HasIndex(en => new { en.WorkspaceId, en.EntityType });
        e.HasIndex(en => new { en.UserId, en.NormalizedName, en.EntityType }).IsUnique();
        e.HasIndex(en => en.UserId);
        e.HasIndex(en => en.EntityType);
        e.HasIndex(en => en.Name);
    }

    private static void ConfigureDocumentEntity(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<DocumentEntity>();
        e.ToTable("document_entities");
        e.HasKey(de => new { de.DocumentId, de.EntityId });
        e.Property(de => de.DocumentId).HasColumnType("uuid");
        e.Property(de => de.EntityId).HasColumnType("uuid");
        e.Property(de => de.Confidence).HasColumnType("numeric(5,4)");
        e.Property(de => de.Evidence).HasMaxLength(2000);
        e.Property(de => de.CreatedAt).IsRequired();
        // Phase 4 fields
        e.Property(de => de.FirstMention).HasMaxLength(2000);
        e.Property(de => de.MentionExamples).HasColumnType("text");
        e.Property(de => de.Importance).HasColumnType("numeric(5,4)");
        e.Property(de => de.Role).HasMaxLength(100);
        e.Property(de => de.Sentiment).HasMaxLength(50);

        e.HasIndex(de => de.EntityId);
    }

    private static void ConfigureEntityRelation(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<EntityRelation>();
        e.ToTable("entity_relations");
        e.HasKey(r => r.Id);
        e.Property(r => r.Id).HasColumnType("uuid");
        e.Property(r => r.UserId).HasColumnType("uuid");
        e.Property(r => r.SourceEntityId).HasColumnType("uuid");
        e.Property(r => r.TargetEntityId).HasColumnType("uuid");
        e.Property(r => r.RelationType).IsRequired().HasMaxLength(100);
        e.Property(r => r.EvidenceDocumentId).HasColumnType("uuid");
        e.Property(r => r.EvidenceText).HasMaxLength(2000);
        e.Property(r => r.CreatedAt).IsRequired();

        e.HasIndex(r => r.UserId);
        e.HasIndex(r => r.SourceEntityId);
        e.HasIndex(r => r.TargetEntityId);
        e.HasIndex(r => r.RelationType);
    }

    private static void ConfigureAiJob(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AiJob>();
        e.ToTable("ai_jobs");
        e.HasKey(j => j.Id);
        e.Property(j => j.Id).HasColumnType("uuid");
        e.Property(j => j.UserId).HasColumnType("uuid");
        e.Property(j => j.JobType).IsRequired().HasMaxLength(50);
        e.Property(j => j.TargetType).IsRequired().HasMaxLength(50);
        e.Property(j => j.TargetId).HasColumnType("uuid");
        e.Property(j => j.Status).IsRequired().HasMaxLength(50);
        e.Property(j => j.Model).HasMaxLength(100);
        e.Property(j => j.PromptVersion).HasMaxLength(50);
        e.Property(j => j.ErrorMessage).HasMaxLength(2000);
        e.Property(j => j.CreatedAt).IsRequired();

        e.HasIndex(j => j.UserId);
        e.HasIndex(j => j.Status);
        e.HasIndex(j => new { j.TargetType, j.TargetId });
    }

    // ===== Phase 3 Entity Configurations =====

    private static void ConfigureSearchLog(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<SearchLog>();
        e.ToTable("search_logs");
        e.HasKey(s => s.Id);
        e.Property(s => s.Id).HasColumnType("uuid");
        e.Property(s => s.UserId).HasColumnType("uuid");
        e.Property(s => s.TopicId).HasColumnType("uuid");
        e.Property(s => s.Query).IsRequired().HasColumnType("text");
        e.Property(s => s.SearchType).IsRequired().HasMaxLength(50);
        e.Property(s => s.Filters).HasColumnType("text");
        e.Property(s => s.CreatedAt).IsRequired();

        e.HasIndex(s => new { s.UserId, s.TopicId, s.CreatedAt });
        e.HasIndex(s => s.UserId);
    }

    private static void ConfigureQaSession(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<QaSession>();
        e.ToTable("qa_sessions");
        e.HasKey(s => s.Id);
        e.Property(s => s.Id).HasColumnType("uuid");
        e.Property(s => s.UserId).HasColumnType("uuid");
        e.Property(s => s.TopicId).HasColumnType("uuid");
        e.Property(s => s.Title).HasMaxLength(500);
        e.Property(s => s.Status).IsRequired().HasMaxLength(50).HasDefaultValue("active");
        e.Property(s => s.CreatedAt).IsRequired();
        e.Property(s => s.UpdatedAt).IsRequired();

        e.HasIndex(s => new { s.UserId, s.TopicId });
        e.HasIndex(s => s.UserId);
    }

    private static void ConfigureQaMessage(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<QaMessage>();
        e.ToTable("qa_messages");
        e.HasKey(m => m.Id);
        e.Property(m => m.Id).HasColumnType("uuid");
        e.Property(m => m.SessionId).HasColumnType("uuid");
        e.Property(m => m.UserId).HasColumnType("uuid");
        e.Property(m => m.TopicId).HasColumnType("uuid");
        e.Property(m => m.Role).IsRequired().HasMaxLength(50);
        e.Property(m => m.Content).IsRequired().HasColumnType("text");
        e.Property(m => m.Citations).HasColumnType("text");
        e.Property(m => m.RetrievalSnapshot).HasColumnType("text");
        e.Property(m => m.Model).HasMaxLength(100);
        e.Property(m => m.CreatedAt).IsRequired();

        e.HasIndex(m => new { m.SessionId, m.UserId });
        e.HasIndex(m => m.SessionId);
    }

    private static void ConfigureRetrievalLog(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<RetrievalLog>();
        e.ToTable("retrieval_logs");
        e.HasKey(r => r.Id);
        e.Property(r => r.Id).HasColumnType("uuid");
        e.Property(r => r.UserId).HasColumnType("uuid");
        e.Property(r => r.TopicId).HasColumnType("uuid");
        e.Property(r => r.QaMessageId).HasColumnType("uuid");
        e.Property(r => r.Query).IsRequired().HasColumnType("text");
        e.Property(r => r.RetrievalType).IsRequired().HasMaxLength(50);
        e.Property(r => r.RetrievedChunks).HasColumnType("text");
        e.Property(r => r.FinalContext).HasColumnType("text");
        e.Property(r => r.CreatedAt).IsRequired();

        e.HasIndex(r => new { r.UserId, r.QaMessageId });
        e.HasIndex(r => r.UserId);
    }

    private static void ConfigureDocumentProcessingLog(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<DocumentProcessingLog>();
        e.ToTable("document_processing_logs");
        e.HasKey(l => l.Id);
        e.Property(l => l.Id).HasColumnType("uuid");
        e.Property(l => l.WorkspaceId).IsRequired().HasMaxLength(200);
        e.Property(l => l.SourceId).HasColumnType("uuid");
        e.Property(l => l.DocumentId).HasColumnType("uuid");
        e.Property(l => l.StepName).IsRequired().HasMaxLength(100);
        e.Property(l => l.Status).IsRequired().HasMaxLength(50);
        e.Property(l => l.Message).HasColumnType("text");
        e.Property(l => l.ErrorCode).HasMaxLength(100);
        e.Property(l => l.ErrorStack).HasColumnType("text");
        e.Property(l => l.InputSnapshot).HasColumnType("text");
        e.Property(l => l.OutputSnapshot).HasColumnType("text");
        e.Property(l => l.CreatedAt).IsRequired();

        e.HasIndex(l => l.SourceId);
        e.HasIndex(l => l.DocumentId);
        e.HasIndex(l => new { l.WorkspaceId, l.CreatedAt });
    }

    // ===== Phase 4 Entity Configurations =====

    private static void ConfigureReport(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Report>();
        e.ToTable("reports");
        e.HasKey(r => r.Id);
        e.Property(r => r.Id).HasColumnType("uuid");
        e.Property(r => r.UserId).HasColumnType("uuid");
        e.Property(r => r.TopicId).HasColumnType("uuid");
        e.Property(r => r.ReportType).IsRequired().HasMaxLength(50);
        e.Property(r => r.Title).IsRequired().HasMaxLength(500);
        e.Property(r => r.Slug).HasMaxLength(300);
        e.Property(r => r.ContentMarkdown).HasColumnType("text");
        e.Property(r => r.Summary).HasColumnType("text");
        e.Property(r => r.OneSentenceConclusion).HasColumnType("text");
        e.Property(r => r.Query).HasColumnType("text");
        e.Property(r => r.SourceDocumentIds).HasColumnType("text");
        e.Property(r => r.SourceChunkIds).HasColumnType("text");
        e.Property(r => r.SourceReportIds).HasColumnType("text");
        e.Property(r => r.Citations).HasColumnType("text");
        e.Property(r => r.TemplateId).HasColumnType("uuid");
        e.Property(r => r.GenerationMode).HasMaxLength(50);
        e.Property(r => r.GeneratedByModel).HasMaxLength(100);
        e.Property(r => r.PromptVersion).HasMaxLength(50);
        e.Property(r => r.ModelConfigSnapshot).HasColumnType("text");
        e.Property(r => r.Status).IsRequired().HasMaxLength(50);
        e.Property(r => r.QualityScore);
        e.Property(r => r.CitationCoverage).HasColumnType("double precision");
        e.Property(r => r.EvidenceCount);
        e.Property(r => r.ExportStatus).HasMaxLength(50);
        e.Property(r => r.LastExportedAt);
        e.Property(r => r.ErrorMessage).HasMaxLength(2000);
        e.Property(r => r.CreatedBy).HasMaxLength(100);
        e.Property(r => r.CreatedAt).IsRequired();
        e.Property(r => r.UpdatedAt).IsRequired();

        e.HasIndex(r => new { r.UserId, r.TopicId });
        e.HasIndex(r => r.ReportType);
        e.HasIndex(r => r.Status);
        e.HasIndex(r => r.CreatedAt);
    }

    private static void ConfigureReportTemplate(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ReportTemplate>();
        e.ToTable("report_templates");
        e.HasKey(t => t.Id);
        e.Property(t => t.Id).HasColumnType("uuid");
        e.Property(t => t.UserId).HasColumnType("uuid");
        e.Property(t => t.Name).IsRequired().HasMaxLength(200);
        e.Property(t => t.ReportType).IsRequired().HasMaxLength(50);
        e.Property(t => t.Description).HasMaxLength(1000);
        e.Property(t => t.TemplateMarkdown).HasColumnType("text");
        e.Property(t => t.SystemPrompt).HasColumnType("text");
        e.Property(t => t.UserPromptTemplate).HasColumnType("text");
        e.Property(t => t.OutputRules).HasColumnType("text");
        e.Property(t => t.CreatedAt).IsRequired();
        e.Property(t => t.UpdatedAt).IsRequired();

        e.HasIndex(t => t.UserId);
        e.HasIndex(t => t.ReportType);
    }

    private static void ConfigureReportJob(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ReportJob>();
        e.ToTable("report_jobs");
        e.HasKey(j => j.Id);
        e.Property(j => j.Id).HasColumnType("uuid");
        e.Property(j => j.UserId).HasColumnType("uuid");
        e.Property(j => j.TopicId).HasColumnType("uuid");
        e.Property(j => j.ReportType).IsRequired().HasMaxLength(50);
        e.Property(j => j.ReportId).HasColumnType("uuid");
        e.Property(j => j.Status).IsRequired().HasMaxLength(50);
        e.Property(j => j.InputParams).HasColumnType("text");
        e.Property(j => j.PlanJson).HasColumnType("text");
        e.Property(j => j.RetrievalSnapshotJson).HasColumnType("text");
        e.Property(j => j.PromptSnapshot).HasColumnType("text");
        e.Property(j => j.ModelOutput).HasColumnType("text");
        e.Property(j => j.Model).HasMaxLength(100);
        e.Property(j => j.PromptVersion).HasMaxLength(50);
        e.Property(j => j.Progress);
        e.Property(j => j.CurrentStep).HasMaxLength(100);
        e.Property(j => j.ErrorCode).HasMaxLength(100);
        e.Property(j => j.ErrorMessage).HasMaxLength(2000);
        e.Property(j => j.CreatedAt).IsRequired();
        e.Property(j => j.UpdatedAt).IsRequired();

        e.HasIndex(j => j.UserId);
        e.HasIndex(j => j.TopicId);
        e.HasIndex(j => j.Status);
        e.HasIndex(j => j.CreatedAt);
    }

    private static void ConfigureReportSource(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ReportSource>();
        e.ToTable("report_sources");
        // Composite key with nullable ChunkId - use ReportId + DocumentId + ChunkId
        e.HasKey(rs => new { rs.ReportId, rs.DocumentId, rs.ChunkId });
        e.Property(rs => rs.ReportId).HasColumnType("uuid");
        e.Property(rs => rs.DocumentId).HasColumnType("uuid");
        e.Property(rs => rs.ChunkId).HasColumnType("uuid");
        e.Property(rs => rs.SourceRole).HasMaxLength(50);
        e.Property(rs => rs.RelevanceScore).HasColumnType("numeric(6,4)");
        e.Property(rs => rs.CreatedAt).IsRequired();

        e.HasIndex(rs => rs.DocumentId);
        e.HasIndex(rs => rs.ChunkId);
    }

    private static void ConfigureReportCitation(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ReportCitation>();
        e.ToTable("report_citations");
        e.HasKey(c => c.Id);
        e.Property(c => c.Id).HasColumnType("uuid");
        e.Property(c => c.ReportId).HasColumnType("uuid");
        e.Property(c => c.DocumentId).HasColumnType("uuid");
        e.Property(c => c.ChunkId).HasColumnType("uuid");
        e.Property(c => c.CitationIndex).IsRequired();
        e.Property(c => c.CitationKey).HasMaxLength(50);
        e.Property(c => c.QuoteText).HasColumnType("text");
        e.Property(c => c.SectionKey).HasMaxLength(100);
        e.Property(c => c.Title).HasMaxLength(1000);
        e.Property(c => c.SourceUrl).HasMaxLength(2048);
        e.Property(c => c.SourceDomain).HasMaxLength(255);
        e.Property(c => c.SourceType).HasMaxLength(50);
        e.Property(c => c.RelevanceScore).HasColumnType("double precision");
        e.Property(c => c.SourceRole).HasMaxLength(50);
        e.Property(c => c.CreatedAt).IsRequired();

        e.HasIndex(c => c.ReportId);
        e.HasIndex(c => c.DocumentId);
        e.HasIndex(c => new { c.ReportId, c.CitationIndex });
    }

    private static void ConfigureExportJob(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ExportJob>();
        e.ToTable("export_jobs");
        e.HasKey(j => j.Id);
        e.Property(j => j.Id).HasColumnType("uuid");
        e.Property(j => j.UserId).HasColumnType("uuid");
        e.Property(j => j.TopicId).HasColumnType("uuid");
        e.Property(j => j.ExportType).IsRequired().HasMaxLength(50);
        e.Property(j => j.TargetType).IsRequired().HasMaxLength(50);
        e.Property(j => j.TargetId).HasColumnType("uuid");
        e.Property(j => j.Status).IsRequired().HasMaxLength(50);
        e.Property(j => j.Params).HasColumnType("text");
        e.Property(j => j.FileId).HasColumnType("uuid");
        e.Property(j => j.OutputPath).HasMaxLength(1000);
        e.Property(j => j.Progress);
        e.Property(j => j.ErrorMessage).HasMaxLength(2000);
        e.Property(j => j.CreatedAt).IsRequired();
        e.Property(j => j.UpdatedAt).IsRequired();

        e.HasIndex(j => j.UserId);
        e.HasIndex(j => j.Status);
    }

    private static void ConfigureExportFile(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ExportFile>();
        e.ToTable("export_files");
        e.HasKey(f => f.Id);
        e.Property(f => f.Id).HasColumnType("uuid");
        e.Property(f => f.UserId).HasColumnType("uuid");
        e.Property(f => f.ExportJobId).HasColumnType("uuid");
        e.Property(f => f.FileName).IsRequired().HasMaxLength(500);
        e.Property(f => f.FileType).IsRequired().HasMaxLength(50);
        e.Property(f => f.MimeType).HasMaxLength(200);
        e.Property(f => f.StorageProvider).IsRequired().HasMaxLength(50);
        e.Property(f => f.StoragePath).IsRequired().HasMaxLength(2000);
        e.Property(f => f.DownloadUrl).HasMaxLength(2000);
        e.Property(f => f.Checksum).HasMaxLength(128);
        e.Property(f => f.CreatedAt).IsRequired();

        e.HasIndex(f => f.UserId);
        e.HasIndex(f => f.ExportJobId);
    }

    private static void ConfigureAgentProfile(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AgentProfile>();
        e.ToTable("agent_profiles");
        e.HasKey(a => a.Id);
        e.Property(a => a.Id).HasColumnType("uuid");
        e.Property(a => a.UserId).HasColumnType("uuid");
        e.Property(a => a.Name).IsRequired().HasMaxLength(200);
        e.Property(a => a.Description).HasMaxLength(2000);
        e.Property(a => a.AllowedToolNames).HasColumnType("text");
        e.Property(a => a.AllowedTopicIds).HasColumnType("text");
        // Phase 7: Scopes (JSON array stored as text)
        e.Property(a => a.Scopes).HasColumnType("text");
        e.Property(a => a.AllowSensitiveDocuments).IsRequired().HasDefaultValue(false);
        e.Property(a => a.MaxResultsPerCall).IsRequired().HasDefaultValue(20);
        e.Property(a => a.RateLimitPerMinute).IsRequired().HasDefaultValue(60);
        e.Property(a => a.DailyQuota).IsRequired().HasDefaultValue(1000);
        e.Property(a => a.ApiKeyId).HasColumnType("uuid");
        e.Property(a => a.Transport).IsRequired().HasMaxLength(50).HasDefaultValue("stdio");
        e.Property(a => a.McpServerPath).HasMaxLength(2048);
        e.Property(a => a.Status).IsRequired().HasMaxLength(50).HasDefaultValue("active");
        e.Property(a => a.CreatedAt).IsRequired();
        e.Property(a => a.UpdatedAt).IsRequired();

        e.HasIndex(a => new { a.UserId, a.Status });
    }

    private static void ConfigureAgentInvocationLog(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AgentInvocationLog>();
        e.ToTable("agent_invocation_logs");
        e.HasKey(l => l.Id);
        e.Property(l => l.Id).HasColumnType("uuid");
        e.Property(l => l.UserId).HasColumnType("uuid");
        e.Property(l => l.AgentProfileId).HasColumnType("uuid");
        e.Property(l => l.ApiKeyId).HasColumnType("uuid");
        e.Property(l => l.Transport).IsRequired().HasMaxLength(50).HasDefaultValue("cloud_api");
        e.Property(l => l.ToolName).IsRequired().HasMaxLength(100);
        e.Property(l => l.InputJson).HasColumnType("text");
        e.Property(l => l.OutputSummary).HasColumnType("text");
        e.Property(l => l.Status).IsRequired().HasMaxLength(50).HasDefaultValue("success");
        e.Property(l => l.ErrorCode).HasMaxLength(100);
        e.Property(l => l.ErrorMessage).HasColumnType("text");
        e.Property(l => l.TraceId).HasMaxLength(100);
        e.Property(l => l.IpAddress).HasMaxLength(100);
        e.Property(l => l.UserAgent).HasMaxLength(500);
        e.Property(l => l.CreatedAt).IsRequired();

        e.HasIndex(l => new { l.UserId, l.CreatedAt });
        e.HasIndex(l => new { l.Transport, l.ToolName });
    }

    // ===== Phase 5 Entity Configurations =====

    private static void ConfigureChunkEmbedding(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ChunkEmbedding>();
        e.ToTable("chunk_embeddings");
        e.HasKey(ce => ce.Id);
        e.Property(ce => ce.Id).HasColumnType("uuid");
        e.Property(ce => ce.ChunkId).HasColumnType("uuid");
        e.Property(ce => ce.WorkspaceId).IsRequired().HasMaxLength(200);
        e.Property(ce => ce.Provider).IsRequired().HasMaxLength(100);
        e.Property(ce => ce.Model).IsRequired().HasMaxLength(200);
        e.Property(ce => ce.ModelVersion).HasMaxLength(100);
        e.Property(ce => ce.Dimension);
        e.Property(ce => ce.EmbeddingJson).HasColumnType("text");
        e.Property(ce => ce.VectorRef).HasMaxLength(1000);
        e.Property(ce => ce.ChunkContentHash).HasMaxLength(128);
        e.Property(ce => ce.Status).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        e.Property(ce => ce.ErrorMessage).HasColumnType("text");
        e.Property(ce => ce.RetryCount).IsRequired().HasDefaultValue(0);
        e.Property(ce => ce.CreatedAt).IsRequired();
        e.Property(ce => ce.UpdatedAt).IsRequired();

        e.HasIndex(ce => new { ce.ChunkId, ce.Provider, ce.Model, ce.ChunkContentHash }).IsUnique();
        e.HasIndex(ce => ce.ChunkId);
        e.HasIndex(ce => new { ce.WorkspaceId, ce.Status });
    }

    private static void ConfigureVectorIndexState(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<VectorIndexState>();
        e.ToTable("vector_index_state");
        e.HasKey(v => v.Id);
        e.Property(v => v.Id).HasColumnType("uuid");
        e.Property(v => v.WorkspaceId).IsRequired().HasMaxLength(200);
        e.Property(v => v.Provider).IsRequired().HasMaxLength(100);
        e.Property(v => v.Model).IsRequired().HasMaxLength(200);
        e.Property(v => v.Dimension);
        e.Property(v => v.IndexBackend).IsRequired().HasMaxLength(50).HasDefaultValue("sqlite_vec");
        e.Property(v => v.TotalChunks).IsRequired().HasDefaultValue(0);
        e.Property(v => v.IndexedChunks).IsRequired().HasDefaultValue(0);
        e.Property(v => v.FailedChunks).IsRequired().HasDefaultValue(0);
        e.Property(v => v.StaleChunks).IsRequired().HasDefaultValue(0);
        e.Property(v => v.Status).IsRequired().HasMaxLength(50).HasDefaultValue("idle");
        e.Property(v => v.LastRebuiltAt);
        e.Property(v => v.SchemaVersion).HasMaxLength(50);
        e.Property(v => v.CreatedAt).IsRequired();
        e.Property(v => v.UpdatedAt).IsRequired();

        e.HasIndex(v => new { v.WorkspaceId, v.Provider, v.Model }).IsUnique();
    }

    private static void ConfigureApiKey(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ApiKey>();
        e.ToTable("api_keys");
        e.HasKey(k => k.Id);
        e.Property(k => k.Id).HasColumnType("uuid");
        e.Property(k => k.UserId).HasColumnType("uuid");
        e.Property(k => k.Name).IsRequired().HasMaxLength(200);
        e.Property(k => k.KeyPrefix).IsRequired().HasMaxLength(50);
        e.Property(k => k.KeyHash).IsRequired().HasMaxLength(256);
        e.Property(k => k.PermissionScope).IsRequired().HasMaxLength(50);
        e.Property(k => k.AllowedTopicIds).HasColumnType("text");
        e.Property(k => k.AllowedActions).HasColumnType("text");
        e.Property(k => k.RateLimitPerMinute).IsRequired();
        e.Property(k => k.DailyQuota).IsRequired();
        e.Property(k => k.ExpiresAt);
        e.Property(k => k.Status).IsRequired().HasMaxLength(50);
        e.Property(k => k.CreatedAt).IsRequired();
        e.Property(k => k.LastUsedAt);

        e.HasIndex(k => new { k.UserId, k.Status, k.KeyPrefix });
    }

    private static void ConfigureApiCallLog(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ApiCallLog>();
        e.ToTable("api_call_logs");
        e.HasKey(l => l.Id);
        e.Property(l => l.Id).HasColumnType("uuid");
        e.Property(l => l.UserId).HasColumnType("uuid");
        e.Property(l => l.ApiKeyId).HasColumnType("uuid");
        e.Property(l => l.Endpoint).IsRequired().HasMaxLength(500);
        e.Property(l => l.RequestMethod).HasMaxLength(20);
        e.Property(l => l.RequestSummary).HasColumnType("text");
        e.Property(l => l.StatusCode);
        e.Property(l => l.ErrorCode).HasMaxLength(100);
        e.Property(l => l.LatencyMs);
        e.Property(l => l.InputTokens);
        e.Property(l => l.OutputTokens);
        e.Property(l => l.RetrievedCount);
        e.Property(l => l.IpAddress).HasMaxLength(100);
        e.Property(l => l.UserAgent).HasMaxLength(500);
        e.Property(l => l.CreatedAt).IsRequired();

        e.HasIndex(l => new { l.UserId, l.ApiKeyId, l.CreatedAt, l.Endpoint });
    }

    private static void ConfigureUserUsageDaily(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<UserUsageDaily>();
        e.ToTable("user_usage_daily");
        e.HasKey(u => u.Id);
        e.Property(u => u.Id).HasColumnType("uuid");
        e.Property(u => u.UserId).HasColumnType("uuid");
        e.Property(u => u.UsageDate).IsRequired();
        e.Property(u => u.ImportedCount).IsRequired();
        e.Property(u => u.DocumentCount).IsRequired();
        e.Property(u => u.SearchCount).IsRequired();
        e.Property(u => u.QaCount).IsRequired();
        e.Property(u => u.ReportCount).IsRequired();
        e.Property(u => u.ExportCount).IsRequired();
        e.Property(u => u.ApiCallCount).IsRequired();
        e.Property(u => u.AgentCallCount).IsRequired();
        e.Property(u => u.AgentSearchCount).IsRequired();
        e.Property(u => u.AgentQaCount).IsRequired();
        e.Property(u => u.AgentWriteCount).IsRequired();
        e.Property(u => u.AgentSuccessCount).IsRequired();
        e.Property(u => u.AgentFailedCount).IsRequired();
        e.Property(u => u.InputTokens).IsRequired();
        e.Property(u => u.OutputTokens).IsRequired();
        e.Property(u => u.EmbeddingTokens).IsRequired();
        e.Property(u => u.StorageBytes).IsRequired();
        e.Property(u => u.CreatedAt).IsRequired();
        e.Property(u => u.UpdatedAt).IsRequired();

        e.HasIndex(u => new { u.UserId, u.UsageDate }).IsUnique();
    }

    private static void ConfigureBetaUser(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<BetaUser>();
        e.ToTable("beta_users");
        e.HasKey(b => b.Id);
        e.Property(b => b.Id).HasColumnType("uuid");
        e.Property(b => b.UserId).HasColumnType("uuid");
        e.Property(b => b.Email).IsRequired().HasMaxLength(255);
        e.Property(b => b.Name).HasMaxLength(200);
        e.Property(b => b.UserType).HasMaxLength(50);
        e.Property(b => b.InviteCode).HasMaxLength(100);
        e.Property(b => b.BetaGroup).HasMaxLength(100);
        e.Property(b => b.Platform).HasMaxLength(100);
        e.Property(b => b.Status).IsRequired().HasMaxLength(50);
        e.Property(b => b.OnboardedAt);
        e.Property(b => b.LastFeedbackAt);
        e.Property(b => b.Notes).HasColumnType("text");
        e.Property(b => b.CreatedAt).IsRequired();
        e.Property(b => b.UpdatedAt).IsRequired();

        e.HasIndex(b => new { b.Email, b.Status });
    }

    private static void ConfigureFeedbackItem(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<FeedbackItem>();
        e.ToTable("feedback_items");
        e.HasKey(f => f.Id);
        e.Property(f => f.Id).HasColumnType("uuid");
        e.Property(f => f.UserId).HasColumnType("uuid");
        e.Property(f => f.BetaUserId).HasColumnType("uuid");
        e.Property(f => f.FeedbackType).IsRequired().HasMaxLength(50);
        e.Property(f => f.Module).HasMaxLength(100);
        e.Property(f => f.Severity).HasMaxLength(50);
        e.Property(f => f.Title).IsRequired().HasMaxLength(500);
        e.Property(f => f.Content).HasColumnType("text");
        e.Property(f => f.RelatedEntityType).HasMaxLength(50);
        e.Property(f => f.RelatedEntityId).HasColumnType("uuid");
        e.Property(f => f.Status).IsRequired().HasMaxLength(50);
        e.Property(f => f.Priority).IsRequired().HasMaxLength(50);
        e.Property(f => f.CreatedAt).IsRequired();
        e.Property(f => f.UpdatedAt).IsRequired();

        e.HasIndex(f => new { f.UserId, f.Status, f.Module });
    }

    private static void ConfigureReleaseNote(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ReleaseNote>();
        e.ToTable("release_notes");
        e.HasKey(r => r.Id);
        e.Property(r => r.Id).HasColumnType("uuid");
        e.Property(r => r.Version).IsRequired().HasMaxLength(100);
        e.Property(r => r.Title).IsRequired().HasMaxLength(500);
        e.Property(r => r.Channel).IsRequired().HasMaxLength(50).HasDefaultValue("alpha");
        e.Property(r => r.ContentMarkdown).HasColumnType("text");
        // Map JSON string properties to columns; ignore the List<string> properties
        e.Ignore(r => r.Highlights);
        e.Ignore(r => r.KnownIssues);
        e.Property(r => r.HighlightsJson).HasColumnType("text").HasColumnName("highlights");
        e.Property(r => r.KnownIssuesJson).HasColumnType("text").HasColumnName("known_issues");
        e.Property(r => r.IsPublished).IsRequired().HasDefaultValue(false);
        e.Property(r => r.PublishedAt);
        e.Property(r => r.CreatedAt).IsRequired();
        e.Property(r => r.UpdatedAt).IsRequired();

        e.HasIndex(r => new { r.Channel, r.IsPublished, r.PublishedAt });
        e.HasIndex(r => r.Version);
    }

    private static void ConfigureWorkspace(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Workspace>();
        e.ToTable("workspaces");
        e.HasKey(w => w.Id);
        e.Property(w => w.Id).HasColumnType("uuid");
        e.Property(w => w.UserId).HasColumnType("uuid");
        e.Property(w => w.Name).IsRequired().HasMaxLength(200);
        e.Property(w => w.Mode).IsRequired().HasMaxLength(50);
        e.Property(w => w.StorageProvider).IsRequired().HasMaxLength(50);
        e.Property(w => w.FileProvider).IsRequired().HasMaxLength(50);
        e.Property(w => w.JobProvider).IsRequired().HasMaxLength(50);
        e.Property(w => w.ModelProvider).HasMaxLength(50);
        e.Property(w => w.LocalDbPath).HasMaxLength(1024);
        e.Property(w => w.LocalVaultPath).HasMaxLength(1024);
        e.Property(w => w.CloudApiBaseUrl).HasMaxLength(2048);
        e.Property(w => w.CloudWorkspaceId).HasMaxLength(200);
        e.Property(w => w.ModelConfig).HasColumnType("text");
        e.Property(w => w.CreatedAt).IsRequired();
        e.Property(w => w.UpdatedAt).IsRequired();

        e.HasIndex(w => w.UserId);
        e.HasIndex(w => w.Mode);
    }

    private static void ConfigureInboxItem(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<InboxItem>();
        e.ToTable("inbox_items");
        e.HasKey(i => i.Id);
        e.Property(i => i.Id).HasColumnType("uuid");
        e.Property(i => i.WorkspaceId).HasColumnType("uuid");
        e.Property(i => i.UserId).HasColumnType("uuid");
        e.Property(i => i.TopicId).HasColumnType("uuid");
        e.Property(i => i.FileId).HasColumnType("uuid");
        e.Property(i => i.SuggestedTopicId).HasColumnType("uuid");
        e.Property(i => i.ItemType).IsRequired().HasMaxLength(50);
        e.Property(i => i.Title).HasMaxLength(500);
        e.Property(i => i.ContentText).HasColumnType("text");
        e.Property(i => i.SourceUrl).HasMaxLength(2048);
        e.Property(i => i.FilePath).HasMaxLength(1024);
        e.Property(i => i.Status).IsRequired().HasMaxLength(50);
        e.Property(i => i.SuggestedTitle).HasMaxLength(500);
        e.Property(i => i.ErrorMessage).HasMaxLength(2000);
        e.Property(i => i.CreatedFrom).HasMaxLength(50);
        e.Property(i => i.CreatedAt).IsRequired();
        e.Property(i => i.UpdatedAt).IsRequired();

        e.HasIndex(i => i.WorkspaceId);
        e.HasIndex(i => i.Status);
        e.HasIndex(i => new { i.WorkspaceId, i.Status });
    }

    private static void ConfigureInboxAttachment(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<InboxAttachment>();
        e.ToTable("inbox_attachments");
        e.HasKey(a => a.Id);
        e.Property(a => a.Id).HasColumnType("uuid");
        e.Property(a => a.WorkspaceId).HasColumnType("uuid");
        e.Property(a => a.InboxItemId).HasColumnType("uuid");
        e.Property(a => a.FileId).HasColumnType("uuid");
        e.Property(a => a.Role).IsRequired().HasMaxLength(50);
        e.Property(a => a.Filename).IsRequired().HasMaxLength(500);
        e.Property(a => a.MimeType).IsRequired().HasMaxLength(255);
        e.Property(a => a.SizeBytes).IsRequired();
        e.Property(a => a.CreatedAt).IsRequired();

        e.HasIndex(a => a.InboxItemId);
        e.HasIndex(a => a.WorkspaceId);
    }

    private static void ConfigureImportJob(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ImportJob>();
        e.ToTable("import_jobs");
        e.HasKey(j => j.Id);
        e.Property(j => j.Id).HasColumnType("uuid");
        e.Property(j => j.WorkspaceId).HasColumnType("uuid");
        e.Property(j => j.InboxItemId).HasColumnType("uuid");
        e.Property(j => j.SourceId).HasColumnType("uuid");
        e.Property(j => j.JobType).IsRequired().HasMaxLength(50);
        e.Property(j => j.Status).IsRequired().HasMaxLength(50);
        e.Property(j => j.ErrorCode).HasMaxLength(100);
        e.Property(j => j.ErrorMessage).HasMaxLength(2000);
        e.Property(j => j.CreatedAt).IsRequired();
        e.Property(j => j.UpdatedAt).IsRequired();

        e.HasIndex(j => j.WorkspaceId);
        e.HasIndex(j => j.Status);
        e.HasIndex(j => j.InboxItemId);
    }

    private static void ConfigureInboxEvent(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<InboxEvent>();
        e.ToTable("inbox_events");
        e.HasKey(ev => ev.Id);
        e.Property(ev => ev.Id).HasColumnType("uuid");
        e.Property(ev => ev.WorkspaceId).HasColumnType("uuid");
        e.Property(ev => ev.InboxItemId).HasColumnType("uuid");
        e.Property(ev => ev.EventType).IsRequired().HasMaxLength(50);
        e.Property(ev => ev.EventPayload).HasColumnType("text");
        e.Property(ev => ev.CreatedBy).HasMaxLength(200);
        e.Property(ev => ev.CreatedAt).IsRequired();

        e.HasIndex(ev => ev.InboxItemId);
        e.HasIndex(ev => ev.WorkspaceId);
    }

    private static void ConfigureSyncCursor(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<SyncCursor>();
        e.ToTable("sync_cursors");
        e.HasKey(c => c.Id);
        e.Property(c => c.Id).HasColumnType("uuid");
        e.Property(c => c.WorkspaceId).HasColumnType("uuid");
        e.Property(c => c.RemoteWorkspaceId).HasColumnType("uuid");
        e.Property(c => c.CursorType).IsRequired().HasMaxLength(50);
        e.Property(c => c.CursorValue).HasColumnType("text");
        e.Property(c => c.CreatedAt).IsRequired();
        e.Property(c => c.UpdatedAt).IsRequired();

        e.HasIndex(c => new { c.WorkspaceId, c.RemoteWorkspaceId, c.CursorType }).IsUnique();
    }

    private static void ConfigureCloudInboxSyncLog(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<CloudInboxSyncLog>();
        e.ToTable("cloud_inbox_sync_logs");
        e.HasKey(l => l.Id);
        e.Property(l => l.Id).HasColumnType("uuid");
        e.Property(l => l.WorkspaceId).HasColumnType("uuid");
        e.Property(l => l.Direction).IsRequired().HasMaxLength(50);
        e.Property(l => l.Status).IsRequired().HasMaxLength(50);
        e.Property(l => l.CloudApiBaseUrl).HasMaxLength(2048);
        e.Property(l => l.CloudWorkspaceId).HasMaxLength(200);
        e.Property(l => l.Retention).IsRequired().HasMaxLength(50);
        e.Property(l => l.NextCursor).HasColumnType("text");
        e.Property(l => l.ErrorMessage).HasMaxLength(2000);
        e.Property(l => l.StartedAt).IsRequired();
        e.Property(l => l.FinishedAt).IsRequired();
        e.Property(l => l.CreatedAt).IsRequired();

        e.HasIndex(l => new { l.WorkspaceId, l.CreatedAt });
        e.HasIndex(l => new { l.WorkspaceId, l.Status });
    }

    private static void ConfigureMobileDevice(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<MobileDevice>();
        e.ToTable("mobile_devices");
        e.HasKey(d => d.Id);
        e.Property(d => d.Id).HasColumnType("uuid");
        e.Property(d => d.WorkspaceId).HasColumnType("uuid");
        e.Property(d => d.ClientId).IsRequired().HasMaxLength(200);
        e.Property(d => d.DeviceName).HasMaxLength(200);
        e.Property(d => d.Platform).HasMaxLength(100);
        e.Property(d => d.PushToken).HasColumnType("text");
        e.Property(d => d.RefreshTokenHash).HasMaxLength(128);
        e.Property(d => d.Status).IsRequired().HasMaxLength(50);
        e.Property(d => d.BoundAt).IsRequired();
        e.Property(d => d.CreatedAt).IsRequired();
        e.Property(d => d.UpdatedAt).IsRequired();

        e.HasIndex(d => new { d.WorkspaceId, d.ClientId }).IsUnique();
        e.HasIndex(d => new { d.WorkspaceId, d.UpdatedAt });
        e.HasIndex(d => d.RefreshTokenHash);
    }

    private static void ConfigurePushNotification(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PushNotification>();
        e.ToTable("push_notifications");
        e.HasKey(n => n.Id);
        e.Property(n => n.Id).HasColumnType("uuid");
        e.Property(n => n.WorkspaceId).HasColumnType("uuid");
        e.Property(n => n.ClientId).IsRequired().HasMaxLength(200);
        e.Property(n => n.PushToken).IsRequired().HasColumnType("text");
        e.Property(n => n.Title).IsRequired().HasMaxLength(300);
        e.Property(n => n.Body).IsRequired().HasMaxLength(2000);
        e.Property(n => n.DataJson).HasColumnType("text");
        e.Property(n => n.Status).IsRequired().HasMaxLength(50);
        e.Property(n => n.ProviderResponse).HasColumnType("text");
        e.Property(n => n.ErrorMessage).HasMaxLength(2000);
        e.Property(n => n.CreatedAt).IsRequired();
        e.Property(n => n.UpdatedAt).IsRequired();

        e.HasIndex(n => new { n.Status, n.NextAttemptAt });
        e.HasIndex(n => new { n.WorkspaceId, n.CreatedAt });
    }

    private static void ConfigureWorkspaceSetting(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<WorkspaceSetting>();
        e.ToTable("workspace_settings");
        e.HasKey(s => s.Id);
        e.Property(s => s.Id).HasColumnType("uuid");
        e.Property(s => s.WorkspaceId).HasColumnType("uuid");
        e.Property(s => s.Key).IsRequired().HasMaxLength(200);
        e.Property(s => s.Value).HasColumnType("text");
        e.Property(s => s.UpdatedAt).IsRequired();

        e.HasIndex(s => new { s.WorkspaceId, s.Key }).IsUnique();
    }
}
