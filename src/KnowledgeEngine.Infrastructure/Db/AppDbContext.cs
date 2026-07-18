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
    public DbSet<Terminology> Terminology => Set<Terminology>();
    public DbSet<ChunkLocalization> ChunkLocalizations => Set<ChunkLocalization>();
    public DbSet<ChunkEnrichment> ChunkEnrichments => Set<ChunkEnrichment>();
    public DbSet<MultilingualBatchJob> MultilingualBatchJobs => Set<MultilingualBatchJob>();
    public DbSet<LocalInstallation> LocalInstallations => Set<LocalInstallation>();
    public DbSet<LocalProfile> LocalProfiles => Set<LocalProfile>();
    public DbSet<DeviceIdentity> DeviceIdentities => Set<DeviceIdentity>();
    public DbSet<CloudAccountBinding> CloudAccountBindings => Set<CloudAccountBinding>();
    public DbSet<WorkspaceBinding> WorkspaceBindings => Set<WorkspaceBinding>();
    public DbSet<SyncInboxStaging> SyncInboxStaging => Set<SyncInboxStaging>();

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
        ConfigureTerminology(modelBuilder);
        ConfigureChunkLocalization(modelBuilder);
        ConfigureChunkEnrichment(modelBuilder);
        ConfigureMultilingualBatchJob(modelBuilder);
        ConfigureIdentityAndBindingFoundation(modelBuilder);
        ConfigureSyncInboxStaging(modelBuilder);
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
    /// Adds the multilingual compatibility columns to databases created by older builds.
    /// EnsureCreated only creates missing tables and does not evolve existing ones.
    /// </summary>
    public async Task EnsureMultilingualSetupAsync(CancellationToken ct = default)
    {
        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var statements = new[]
            {
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS title_original varchar(1000)",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS title_zh varchar(1000)",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS summary_zh text",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS keywords_zh text",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localization_model varchar(100)",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localization_prompt_version varchar(50)",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localized_at timestamp with time zone",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localization_quality_score integer",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localization_quality_issues text",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS glossary_version varchar(64)",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS primary_language varchar(20)",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS language_distribution text",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS is_multilingual boolean NOT NULL DEFAULT false",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localization_strategy varchar(30) NOT NULL DEFAULT 'none'",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localization_level varchar(10) NOT NULL DEFAULT 'L1'",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS language_detect_status varchar(30) NOT NULL DEFAULT 'pending'",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS localization_status varchar(30) NOT NULL DEFAULT 'pending'",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS enrichment_status varchar(30) NOT NULL DEFAULT 'pending'",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS fulltext_index_status varchar(30) NOT NULL DEFAULT 'pending'",
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS content_hash varchar(64)",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS content_original text NOT NULL DEFAULT ''",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS content_normalized text",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS detected_language varchar(20)",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS language_confidence numeric(6,5)",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS language_distribution text",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS content_type varchar(30) NOT NULL DEFAULT 'paragraph'",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS processing_route varchar(30) NOT NULL DEFAULT 'review'",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS localization_required boolean NOT NULL DEFAULT false",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS chunk_group_id uuid",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS parent_chunk_id uuid",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS paragraph_start integer",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS paragraph_end integer",
                "ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS bounding_box text"
            };
            foreach (var statement in statements)
                await Database.ExecuteSqlRawAsync(statement, ct);
        }
        else if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var documents = new Dictionary<string, string>
            {
                ["title_original"] = "TEXT", ["title_zh"] = "TEXT", ["summary_zh"] = "TEXT", ["keywords_zh"] = "TEXT",
                ["localization_model"] = "TEXT", ["localization_prompt_version"] = "TEXT", ["localized_at"] = "TEXT",
                ["localization_quality_score"] = "INTEGER", ["localization_quality_issues"] = "TEXT", ["glossary_version"] = "TEXT",
                ["primary_language"] = "TEXT",
                ["language_distribution"] = "TEXT", ["is_multilingual"] = "INTEGER NOT NULL DEFAULT 0",
                ["localization_strategy"] = "TEXT NOT NULL DEFAULT 'none'", ["localization_level"] = "TEXT NOT NULL DEFAULT 'L1'",
                ["language_detect_status"] = "TEXT NOT NULL DEFAULT 'pending'", ["localization_status"] = "TEXT NOT NULL DEFAULT 'pending'",
                ["enrichment_status"] = "TEXT NOT NULL DEFAULT 'pending'", ["fulltext_index_status"] = "TEXT NOT NULL DEFAULT 'pending'",
                ["content_hash"] = "TEXT"
            };
            var chunks = new Dictionary<string, string>
            {
                ["content_original"] = "TEXT NOT NULL DEFAULT ''", ["content_normalized"] = "TEXT",
                ["detected_language"] = "TEXT", ["language_confidence"] = "REAL", ["language_distribution"] = "TEXT",
                ["content_type"] = "TEXT NOT NULL DEFAULT 'paragraph'", ["processing_route"] = "TEXT NOT NULL DEFAULT 'review'",
                ["localization_required"] = "INTEGER NOT NULL DEFAULT 0", ["chunk_group_id"] = "TEXT", ["parent_chunk_id"] = "TEXT",
                ["paragraph_start"] = "INTEGER", ["paragraph_end"] = "INTEGER", ["bounding_box"] = "TEXT"
            };
            foreach (var column in documents)
                await AddSqliteColumnIfMissingAsync("documents", column.Key, column.Value, ct);
            foreach (var column in chunks)
                await AddSqliteColumnIfMissingAsync("document_chunks", column.Key, column.Value, ct);

            var embeddings = new Dictionary<string, string>
            {
                ["language_code"] = "TEXT NOT NULL DEFAULT 'und'",
                ["embedding_type"] = "TEXT NOT NULL DEFAULT 'original'",
                ["localization_id"] = "TEXT",
                ["source_content_hash"] = "TEXT"
            };
            foreach (var column in embeddings)
                await AddSqliteColumnIfMissingAsync("chunk_embeddings", column.Key, column.Value, ct);
        }

        await Database.ExecuteSqlRawAsync("UPDATE documents SET title_original = title WHERE title_original IS NULL", ct);
        await Database.ExecuteSqlRawAsync("UPDATE documents SET primary_language = language WHERE primary_language IS NULL AND language IS NOT NULL", ct);
        await Database.ExecuteSqlRawAsync("UPDATE document_chunks SET content_original = content WHERE content_original = ''", ct);
        await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_documents_primary_language ON documents(primary_language)", ct);
        await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_documents_language_detect_status ON documents(language_detect_status)", ct);
        await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_chunks_detected_language ON document_chunks(detected_language)", ct);
        await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_chunks_processing_route ON document_chunks(processing_route)", ct);
        await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_chunks_chunk_group_id ON document_chunks(chunk_group_id)", ct);
        var terminologySql = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? """
            CREATE TABLE IF NOT EXISTS terminology (
                id uuid PRIMARY KEY, user_id uuid NOT NULL, workspace_id uuid,
                source_language TEXT NOT NULL,
                source_term TEXT NOT NULL,
                target_language TEXT NOT NULL,
                target_term TEXT NOT NULL,
                aliases TEXT,
                domain TEXT,
                priority INTEGER NOT NULL DEFAULT 0,
                review_status TEXT NOT NULL DEFAULT 'approved',
                version TEXT NOT NULL DEFAULT 'v1',
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            )
            """
            : """
            CREATE TABLE IF NOT EXISTS terminology (
                id TEXT PRIMARY KEY, user_id TEXT NOT NULL, workspace_id TEXT,
                source_language TEXT NOT NULL, source_term TEXT NOT NULL,
                target_language TEXT NOT NULL, target_term TEXT NOT NULL,
                aliases TEXT, domain TEXT, priority INTEGER NOT NULL DEFAULT 0,
                review_status TEXT NOT NULL DEFAULT 'approved', version TEXT NOT NULL DEFAULT 'v1',
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            )
            """;
        await Database.ExecuteSqlRawAsync(terminologySql, ct);
        await Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS ix_terminology_user_pair ON terminology(user_id, source_term, target_term)", ct);

        var localizationSql = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? """
              CREATE TABLE IF NOT EXISTS chunk_localizations (
                id uuid PRIMARY KEY, chunk_id uuid NOT NULL, user_id uuid NOT NULL, workspace_id uuid,
                language_code varchar(20) NOT NULL, heading_localized text, content_localized text NOT NULL,
                translation_type varchar(30) NOT NULL, model varchar(100), prompt_version varchar(50) NOT NULL,
                glossary_version varchar(64), quality_score integer, quality_issues text,
                review_status varchar(30) NOT NULL, status varchar(30) NOT NULL,
                source_content_hash varchar(128) NOT NULL, idempotency_key varchar(128) NOT NULL,
                reviewed_at timestamp with time zone, created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
              )
              """
            : """
              CREATE TABLE IF NOT EXISTS chunk_localizations (
                id TEXT PRIMARY KEY, chunk_id TEXT NOT NULL, user_id TEXT NOT NULL, workspace_id TEXT,
                language_code TEXT NOT NULL, heading_localized TEXT, content_localized TEXT NOT NULL,
                translation_type TEXT NOT NULL, model TEXT, prompt_version TEXT NOT NULL, glossary_version TEXT,
                quality_score INTEGER, quality_issues TEXT, review_status TEXT NOT NULL, status TEXT NOT NULL,
                source_content_hash TEXT NOT NULL, idempotency_key TEXT NOT NULL,
                reviewed_at TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
              )
              """;
        await Database.ExecuteSqlRawAsync(localizationSql, ct);
        await Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS ix_chunk_localizations_chunk_language ON chunk_localizations(chunk_id, language_code)", ct);
        await Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS ix_chunk_localizations_idempotency ON chunk_localizations(idempotency_key)", ct);

        var enrichmentSql = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? """
              CREATE TABLE IF NOT EXISTS chunk_enrichments (
                id uuid PRIMARY KEY, chunk_id uuid NOT NULL, user_id uuid NOT NULL, localization_id uuid,
                language_code varchar(20) NOT NULL, summary text, keywords text, entities text, facts text,
                hypothetical_questions text, model varchar(100), prompt_version varchar(50),
                source_content_hash varchar(128) NOT NULL, status varchar(30) NOT NULL,
                created_at timestamp with time zone NOT NULL, updated_at timestamp with time zone NOT NULL
              )
              """
            : """
              CREATE TABLE IF NOT EXISTS chunk_enrichments (
                id TEXT PRIMARY KEY, chunk_id TEXT NOT NULL, user_id TEXT NOT NULL, localization_id TEXT,
                language_code TEXT NOT NULL, summary TEXT, keywords TEXT, entities TEXT, facts TEXT,
                hypothetical_questions TEXT, model TEXT, prompt_version TEXT, source_content_hash TEXT NOT NULL,
                status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
              )
              """;
        await Database.ExecuteSqlRawAsync(enrichmentSql, ct);
        await Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS ix_chunk_enrichments_chunk_language ON chunk_enrichments(chunk_id, language_code)", ct);

        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await Database.ExecuteSqlRawAsync("ALTER TABLE chunk_embeddings ADD COLUMN IF NOT EXISTS language_code varchar(20) NOT NULL DEFAULT 'und'", ct);
            await Database.ExecuteSqlRawAsync("ALTER TABLE chunk_embeddings ADD COLUMN IF NOT EXISTS embedding_type varchar(40) NOT NULL DEFAULT 'original'", ct);
            await Database.ExecuteSqlRawAsync("ALTER TABLE chunk_embeddings ADD COLUMN IF NOT EXISTS localization_id uuid", ct);
            await Database.ExecuteSqlRawAsync("ALTER TABLE chunk_embeddings ADD COLUMN IF NOT EXISTS source_content_hash varchar(128)", ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS multilingual_batch_jobs (
                    id uuid PRIMARY KEY, user_id uuid NOT NULL, document_id uuid NOT NULL,
                    job_type varchar(30) NOT NULL, status varchar(30) NOT NULL, force boolean NOT NULL DEFAULT false,
                    max_chunks integer NOT NULL DEFAULT 500, total_items integer NOT NULL DEFAULT 0,
                    processed_items integer NOT NULL DEFAULT 0, succeeded_items integer NOT NULL DEFAULT 0,
                    failed_items integer NOT NULL DEFAULT 0, current_chunk_id uuid, error_message text,
                    retry_count integer NOT NULL DEFAULT 0, started_at timestamp with time zone,
                    finished_at timestamp with time zone, created_at timestamp with time zone NOT NULL,
                    updated_at timestamp with time zone NOT NULL)
                """, ct);
        }
        else if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS multilingual_batch_jobs (
                    id TEXT PRIMARY KEY, user_id TEXT NOT NULL, document_id TEXT NOT NULL,
                    job_type TEXT NOT NULL, status TEXT NOT NULL, force INTEGER NOT NULL DEFAULT 0,
                    max_chunks INTEGER NOT NULL DEFAULT 500, total_items INTEGER NOT NULL DEFAULT 0,
                    processed_items INTEGER NOT NULL DEFAULT 0, succeeded_items INTEGER NOT NULL DEFAULT 0,
                    failed_items INTEGER NOT NULL DEFAULT 0, current_chunk_id TEXT, error_message TEXT,
                    retry_count INTEGER NOT NULL DEFAULT 0, started_at TEXT, finished_at TEXT,
                    created_at TEXT NOT NULL, updated_at TEXT NOT NULL)
                """, ct);
            await Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS ix_multilingual_jobs_user_status ON multilingual_batch_jobs(user_id, status, created_at)", ct);
            await Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS ix_multilingual_jobs_document_type ON multilingual_batch_jobs(document_id, job_type)", ct);
        }
    }

    public async Task EnsureIdentityAndBindingSetupAsync(CancellationToken ct = default)
    {
        var isPostgres = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        if (isPostgres)
        {
            await Database.ExecuteSqlRawAsync(
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS role varchar(50) NOT NULL DEFAULT 'user'", ct);
            await Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS ix_users_role ON users(role)", ct);
            await Database.ExecuteSqlRawAsync(
                "ALTER TABLE workspaces ADD COLUMN IF NOT EXISTS sync_mode varchar(30) NOT NULL DEFAULT 'none'", ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS local_installations (
                    id uuid PRIMARY KEY, installation_key varchar(100) NOT NULL UNIQUE,
                    platform varchar(50) NOT NULL, device_name varchar(200) NOT NULL,
                    app_version varchar(50) NOT NULL DEFAULT '', status varchar(30) NOT NULL DEFAULT 'active',
                    created_at timestamp with time zone NOT NULL, updated_at timestamp with time zone NOT NULL)
                """, ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS local_profiles (
                    id uuid PRIMARY KEY, installation_id uuid NOT NULL, display_name varchar(200) NOT NULL,
                    status varchar(30) NOT NULL DEFAULT 'active',
                    created_at timestamp with time zone NOT NULL, updated_at timestamp with time zone NOT NULL)
                """, ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS device_identities (
                    id uuid PRIMARY KEY, installation_id uuid NOT NULL, device_key varchar(100) NOT NULL UNIQUE,
                    public_key text NOT NULL, private_key_ref varchar(300) NOT NULL,
                    key_algorithm varchar(30) NOT NULL,
                    status varchar(30) NOT NULL DEFAULT 'active', last_seen_at timestamp with time zone,
                    created_at timestamp with time zone NOT NULL, updated_at timestamp with time zone NOT NULL)
                """, ct);
            await Database.ExecuteSqlRawAsync(
                "ALTER TABLE device_identities ADD COLUMN IF NOT EXISTS private_key_ref varchar(300) NOT NULL DEFAULT ''", ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS cloud_account_bindings (
                    id uuid PRIMARY KEY, local_profile_id uuid NOT NULL, cloud_user_id varchar(200) NOT NULL,
                    cloud_api_base_url varchar(2048) NOT NULL, account_display_name varchar(200),
                    account_email_masked varchar(320), token_key_ref varchar(300) NOT NULL UNIQUE,
                    binding_status varchar(30) NOT NULL DEFAULT 'active',
                    last_authenticated_at timestamp with time zone,
                    created_at timestamp with time zone NOT NULL, updated_at timestamp with time zone NOT NULL,
                    UNIQUE(local_profile_id, cloud_api_base_url, cloud_user_id))
                """, ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS workspace_bindings (
                    id uuid PRIMARY KEY, local_workspace_id uuid NOT NULL, cloud_account_binding_id uuid NOT NULL,
                    cloud_workspace_id varchar(200) NOT NULL, sync_mode varchar(30) NOT NULL DEFAULT 'none',
                    binding_status varchar(30) NOT NULL DEFAULT 'active', primary_device_id uuid,
                    upload_original_files boolean NOT NULL DEFAULT false,
                    conflict_policy varchar(30) NOT NULL DEFAULT 'manual', last_inbox_cursor text,
                    last_sync_cursor text, last_sync_at timestamp with time zone,
                    created_at timestamp with time zone NOT NULL, updated_at timestamp with time zone NOT NULL,
                    UNIQUE(local_workspace_id, cloud_workspace_id))
                """, ct);
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS sync_inbox_staging (
                    id uuid PRIMARY KEY, workspace_id uuid NOT NULL, binding_id uuid,
                    cloud_inbox_item_id varchar(300) NOT NULL, cloud_revision bigint,
                    content_hash varchar(128), remote_metadata_json text,
                    status varchar(30) NOT NULL DEFAULT 'discovered', local_inbox_item_id uuid,
                    duplicate_document_id uuid, import_batch_id uuid, error_message varchar(2000),
                    discovered_at timestamp with time zone NOT NULL, imported_at timestamp with time zone,
                    updated_at timestamp with time zone NOT NULL,
                    UNIQUE(workspace_id, cloud_inbox_item_id))
                """, ct);
        }
        else if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (await SqliteTableExistsAsync("users", ct))
            {
                await AddSqliteColumnIfMissingAsync(
                    "users", "role", "TEXT NOT NULL DEFAULT 'user'", ct);
            }
            await AddSqliteColumnIfMissingAsync(
                "workspaces", "sync_mode", "TEXT NOT NULL DEFAULT 'none'", ct);
            var statements = new[]
            {
                """
                CREATE TABLE IF NOT EXISTS local_installations (
                    id TEXT PRIMARY KEY, installation_key TEXT NOT NULL UNIQUE, platform TEXT NOT NULL,
                    device_name TEXT NOT NULL, app_version TEXT NOT NULL DEFAULT '',
                    status TEXT NOT NULL DEFAULT 'active', created_at TEXT NOT NULL, updated_at TEXT NOT NULL)
                """,
                """
                CREATE TABLE IF NOT EXISTS local_profiles (
                    id TEXT PRIMARY KEY, installation_id TEXT NOT NULL, display_name TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'active', created_at TEXT NOT NULL, updated_at TEXT NOT NULL)
                """,
                """
                CREATE TABLE IF NOT EXISTS device_identities (
                    id TEXT PRIMARY KEY, installation_id TEXT NOT NULL, device_key TEXT NOT NULL UNIQUE,
                    public_key TEXT NOT NULL, private_key_ref TEXT NOT NULL, key_algorithm TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'active',
                    last_seen_at TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL)
                """,
                """
                CREATE TABLE IF NOT EXISTS cloud_account_bindings (
                    id TEXT PRIMARY KEY, local_profile_id TEXT NOT NULL, cloud_user_id TEXT NOT NULL,
                    cloud_api_base_url TEXT NOT NULL, account_display_name TEXT, account_email_masked TEXT,
                    token_key_ref TEXT NOT NULL UNIQUE, binding_status TEXT NOT NULL DEFAULT 'active',
                    last_authenticated_at TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                    UNIQUE(local_profile_id, cloud_api_base_url, cloud_user_id))
                """,
                """
                CREATE TABLE IF NOT EXISTS workspace_bindings (
                    id TEXT PRIMARY KEY, local_workspace_id TEXT NOT NULL, cloud_account_binding_id TEXT NOT NULL,
                    cloud_workspace_id TEXT NOT NULL, sync_mode TEXT NOT NULL DEFAULT 'none',
                    binding_status TEXT NOT NULL DEFAULT 'active', primary_device_id TEXT,
                    upload_original_files INTEGER NOT NULL DEFAULT 0,
                    conflict_policy TEXT NOT NULL DEFAULT 'manual', last_inbox_cursor TEXT,
                    last_sync_cursor TEXT, last_sync_at TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                    UNIQUE(local_workspace_id, cloud_workspace_id))
                """,
                """
                CREATE TABLE IF NOT EXISTS sync_inbox_staging (
                    id TEXT PRIMARY KEY, workspace_id TEXT NOT NULL, binding_id TEXT,
                    cloud_inbox_item_id TEXT NOT NULL, cloud_revision INTEGER, content_hash TEXT,
                    remote_metadata_json TEXT, status TEXT NOT NULL DEFAULT 'discovered',
                    local_inbox_item_id TEXT, duplicate_document_id TEXT, import_batch_id TEXT,
                    error_message TEXT, discovered_at TEXT NOT NULL, imported_at TEXT,
                    updated_at TEXT NOT NULL, UNIQUE(workspace_id, cloud_inbox_item_id))
                """
            };
            foreach (var statement in statements)
            {
                await Database.ExecuteSqlRawAsync(statement, ct);
            }
            await AddSqliteColumnIfMissingAsync(
                "device_identities", "private_key_ref", "TEXT NOT NULL DEFAULT ''", ct);
        }

        await Database.ExecuteSqlRawAsync(isPostgres
            ? """
              UPDATE workspaces
              SET sync_mode = CASE
                  WHEN inbox_enabled THEN 'inbox_only'
                  WHEN sync_enabled THEN 'metadata'
                  ELSE 'none'
              END
              WHERE sync_mode IS NULL OR sync_mode = ''
                 OR (sync_mode = 'none' AND (inbox_enabled OR sync_enabled))
              """
            : """
              UPDATE workspaces
              SET sync_mode = CASE
                  WHEN inbox_enabled = 1 THEN 'inbox_only'
                  WHEN sync_enabled = 1 THEN 'metadata'
                  ELSE 'none'
              END
              WHERE sync_mode IS NULL OR sync_mode = ''
                 OR (sync_mode = 'none' AND (inbox_enabled = 1 OR sync_enabled = 1))
              """, ct);

        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_local_profiles_installation_status ON local_profiles(installation_id, status)", ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_workspace_bindings_account_status ON workspace_bindings(cloud_account_binding_id, binding_status)", ct);
        await Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_sync_inbox_staging_status ON sync_inbox_staging(workspace_id, status, discovered_at)", ct);
    }

    private async Task AddSqliteColumnIfMissingAsync(string table, string column, string definition, CancellationToken ct)
    {
        var connection = Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);
        try
        {
            await using var check = connection.CreateCommand();
            check.CommandText = $"PRAGMA table_info({table})";
            await using var reader = await check.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
            }
            await reader.DisposeAsync();
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
            await alter.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }

    private async Task<bool> SqliteTableExistsAsync(string table, CancellationToken ct)
    {
        var connection = Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0;
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
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
        e.Property(u => u.Role).IsRequired().HasMaxLength(50);
        e.Property(u => u.Status).IsRequired().HasMaxLength(50);
        e.Property(u => u.Timezone).IsRequired().HasMaxLength(64);
        e.Property(u => u.CreatedAt).IsRequired();
        e.Property(u => u.UpdatedAt).IsRequired();

        e.HasIndex(u => u.Email).IsUnique();
        e.HasIndex(u => u.Role);
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
        e.Property(d => d.TitleOriginal).HasColumnName("title_original").HasMaxLength(1000);
        e.Property(d => d.TitleZh).HasColumnName("title_zh").HasMaxLength(1000);
        e.Property(d => d.SummaryZh).HasColumnName("summary_zh").HasColumnType("text");
        e.Property(d => d.KeywordsZh).HasColumnName("keywords_zh").HasColumnType("text");
        e.Property(d => d.LocalizationModel).HasColumnName("localization_model").HasMaxLength(100);
        e.Property(d => d.LocalizationPromptVersion).HasColumnName("localization_prompt_version").HasMaxLength(50);
        e.Property(d => d.LocalizedAt).HasColumnName("localized_at");
        e.Property(d => d.LocalizationQualityScore).HasColumnName("localization_quality_score");
        e.Property(d => d.LocalizationQualityIssues).HasColumnName("localization_quality_issues").HasColumnType("text");
        e.Property(d => d.GlossaryVersion).HasColumnName("glossary_version").HasMaxLength(64);
        e.Property(d => d.PrimaryLanguage).HasColumnName("primary_language").HasMaxLength(20);
        e.Property(d => d.LanguageDistribution).HasColumnName("language_distribution").HasColumnType("text");
        e.Property(d => d.IsMultilingual).HasColumnName("is_multilingual").IsRequired().HasDefaultValue(false);
        e.Property(d => d.LocalizationStrategy).HasColumnName("localization_strategy").IsRequired().HasMaxLength(30).HasDefaultValue("none");
        e.Property(d => d.LocalizationLevel).HasColumnName("localization_level").IsRequired().HasMaxLength(10).HasDefaultValue("L1");
        e.Property(d => d.LanguageDetectStatus).HasColumnName("language_detect_status").IsRequired().HasMaxLength(30).HasDefaultValue("pending");
        e.Property(d => d.LocalizationStatus).HasColumnName("localization_status").IsRequired().HasMaxLength(30).HasDefaultValue("pending");
        e.Property(d => d.EnrichmentStatus).HasColumnName("enrichment_status").IsRequired().HasMaxLength(30).HasDefaultValue("pending");
        e.Property(d => d.FulltextIndexStatus).HasColumnName("fulltext_index_status").IsRequired().HasMaxLength(30).HasDefaultValue("pending");
        e.Property(d => d.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
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
        e.HasIndex(d => d.PrimaryLanguage);
        e.HasIndex(d => d.LanguageDetectStatus);
        e.HasIndex(d => d.LocalizationStatus);
        e.HasIndex(d => d.ValueScore);
        e.HasIndex(d => d.CreatedAt);
    }

    private static void ConfigureTerminology(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<Terminology>();
        e.ToTable("terminology");
        e.HasKey(t => t.Id);
        e.Property(t => t.Id).HasColumnName("id").HasColumnType("uuid");
        e.Property(t => t.UserId).HasColumnName("user_id").HasColumnType("uuid");
        e.Property(t => t.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid");
        e.Property(t => t.SourceLanguage).HasColumnName("source_language").IsRequired().HasMaxLength(20);
        e.Property(t => t.SourceTerm).HasColumnName("source_term").IsRequired().HasMaxLength(300);
        e.Property(t => t.TargetLanguage).HasColumnName("target_language").IsRequired().HasMaxLength(20);
        e.Property(t => t.TargetTerm).HasColumnName("target_term").IsRequired().HasMaxLength(300);
        e.Property(t => t.Aliases).HasColumnName("aliases").HasColumnType("text");
        e.Property(t => t.Domain).HasColumnName("domain").HasMaxLength(100);
        e.Property(t => t.Priority).HasColumnName("priority");
        e.Property(t => t.ReviewStatus).HasColumnName("review_status").IsRequired().HasMaxLength(30);
        e.Property(t => t.Version).HasColumnName("version").IsRequired().HasMaxLength(30);
        e.Property(t => t.CreatedAt).HasColumnName("created_at");
        e.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        e.HasIndex(t => new { t.UserId, t.SourceTerm, t.TargetTerm }).IsUnique();
        e.HasIndex(t => new { t.UserId, t.Priority });
    }

    private static void ConfigureChunkLocalization(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ChunkLocalization>();
        e.ToTable("chunk_localizations"); e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid"); e.Property(x => x.ChunkId).HasColumnName("chunk_id").HasColumnType("uuid");
        e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid"); e.Property(x => x.WorkspaceId).HasColumnName("workspace_id").HasColumnType("uuid");
        e.Property(x => x.LanguageCode).HasColumnName("language_code").IsRequired().HasMaxLength(20);
        e.Property(x => x.HeadingLocalized).HasColumnName("heading_localized").HasColumnType("text"); e.Property(x => x.ContentLocalized).HasColumnName("content_localized").IsRequired().HasColumnType("text");
        e.Property(x => x.TranslationType).HasColumnName("translation_type").IsRequired().HasMaxLength(30); e.Property(x => x.Model).HasColumnName("model").HasMaxLength(100);
        e.Property(x => x.PromptVersion).HasColumnName("prompt_version").IsRequired().HasMaxLength(50); e.Property(x => x.GlossaryVersion).HasColumnName("glossary_version").HasMaxLength(64);
        e.Property(x => x.QualityScore).HasColumnName("quality_score"); e.Property(x => x.QualityIssues).HasColumnName("quality_issues").HasColumnType("text"); e.Property(x => x.ReviewStatus).HasColumnName("review_status").IsRequired().HasMaxLength(30);
        e.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30); e.Property(x => x.SourceContentHash).HasColumnName("source_content_hash").IsRequired().HasMaxLength(128);
        e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);
        e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasIndex(x => new { x.ChunkId, x.LanguageCode }).IsUnique(); e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => new { x.UserId, x.Status });
    }

    private static void ConfigureChunkEnrichment(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ChunkEnrichment>();
        e.ToTable("chunk_enrichments"); e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid"); e.Property(x => x.ChunkId).HasColumnName("chunk_id").HasColumnType("uuid");
        e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid"); e.Property(x => x.LocalizationId).HasColumnName("localization_id").HasColumnType("uuid");
        e.Property(x => x.LanguageCode).HasColumnName("language_code").IsRequired().HasMaxLength(20);
        e.Property(x => x.Summary).HasColumnName("summary").HasColumnType("text"); e.Property(x => x.Keywords).HasColumnName("keywords").HasColumnType("text");
        e.Property(x => x.Entities).HasColumnName("entities").HasColumnType("text"); e.Property(x => x.Facts).HasColumnName("facts").HasColumnType("text");
        e.Property(x => x.HypotheticalQuestions).HasColumnName("hypothetical_questions").HasColumnType("text"); e.Property(x => x.Model).HasColumnName("model").HasMaxLength(100);
        e.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(50); e.Property(x => x.SourceContentHash).HasColumnName("source_content_hash").IsRequired().HasMaxLength(128);
        e.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30);
        e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasIndex(x => new { x.ChunkId, x.LanguageCode }).IsUnique(); e.HasIndex(x => new { x.UserId, x.Status });
    }

    private static void ConfigureMultilingualBatchJob(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<MultilingualBatchJob>();
        e.ToTable("multilingual_batch_jobs"); e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.UserId).HasColumnName("user_id"); e.Property(x => x.DocumentId).HasColumnName("document_id");
        e.Property(x => x.JobType).HasColumnName("job_type").HasMaxLength(30).IsRequired();
        e.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        e.Property(x => x.Force).HasColumnName("force"); e.Property(x => x.MaxChunks).HasColumnName("max_chunks");
        e.Property(x => x.TotalItems).HasColumnName("total_items"); e.Property(x => x.ProcessedItems).HasColumnName("processed_items");
        e.Property(x => x.SucceededItems).HasColumnName("succeeded_items"); e.Property(x => x.FailedItems).HasColumnName("failed_items");
        e.Property(x => x.CurrentChunkId).HasColumnName("current_chunk_id"); e.Property(x => x.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        e.Property(x => x.RetryCount).HasColumnName("retry_count"); e.Property(x => x.StartedAt).HasColumnName("started_at");
        e.Property(x => x.FinishedAt).HasColumnName("finished_at"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasIndex(x => new { x.UserId, x.Status, x.CreatedAt });
        e.HasIndex(x => new { x.DocumentId, x.JobType });
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
        e.Property(c => c.ContentOriginal).HasColumnName("content_original").IsRequired().HasColumnType("text");
        e.Property(c => c.ContentNormalized).HasColumnName("content_normalized").HasColumnType("text");
        e.Property(c => c.DetectedLanguage).HasColumnName("detected_language").HasMaxLength(20);
        e.Property(c => c.LanguageConfidence).HasColumnName("language_confidence").HasPrecision(6, 5);
        e.Property(c => c.LanguageDistribution).HasColumnName("language_distribution").HasColumnType("text");
        e.Property(c => c.ContentType).HasColumnName("content_type").IsRequired().HasMaxLength(30).HasDefaultValue("paragraph");
        e.Property(c => c.ProcessingRoute).HasColumnName("processing_route").IsRequired().HasMaxLength(30).HasDefaultValue("review");
        e.Property(c => c.LocalizationRequired).HasColumnName("localization_required").IsRequired().HasDefaultValue(false);
        e.Property(c => c.ChunkGroupId).HasColumnName("chunk_group_id").HasColumnType("uuid");
        e.Property(c => c.ParentChunkId).HasColumnName("parent_chunk_id").HasColumnType("uuid");
        e.Property(c => c.ParagraphStart).HasColumnName("paragraph_start");
        e.Property(c => c.ParagraphEnd).HasColumnName("paragraph_end");
        e.Property(c => c.BoundingBox).HasColumnName("bounding_box").HasColumnType("text");
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
        e.HasIndex(c => c.DetectedLanguage);
        e.HasIndex(c => c.ProcessingRoute);
        e.HasIndex(c => c.ChunkGroupId);
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
        e.Property(ce => ce.LanguageCode).HasColumnName("language_code").IsRequired().HasMaxLength(20).HasDefaultValue("und");
        e.Property(ce => ce.EmbeddingType).HasColumnName("embedding_type").IsRequired().HasMaxLength(40).HasDefaultValue("original");
        e.Property(ce => ce.LocalizationId).HasColumnName("localization_id").HasColumnType("uuid");
        e.Property(ce => ce.SourceContentHash).HasColumnName("source_content_hash").HasMaxLength(128);
        e.Property(ce => ce.Status).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        e.Property(ce => ce.ErrorMessage).HasColumnType("text");
        e.Property(ce => ce.RetryCount).IsRequired().HasDefaultValue(0);
        e.Property(ce => ce.CreatedAt).IsRequired();
        e.Property(ce => ce.UpdatedAt).IsRequired();

        e.HasIndex(ce => new { ce.ChunkId, ce.LanguageCode, ce.EmbeddingType, ce.Provider, ce.Model }).IsUnique();
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
        e.Property(w => w.SyncMode).IsRequired().HasMaxLength(30);
        e.Property(w => w.ModelConfig).HasColumnType("text");
        e.Property(w => w.CreatedAt).IsRequired();
        e.Property(w => w.UpdatedAt).IsRequired();

        e.HasIndex(w => w.UserId);
        e.HasIndex(w => w.Mode);
        e.HasIndex(w => w.SyncMode);
    }

    private static void ConfigureIdentityAndBindingFoundation(ModelBuilder modelBuilder)
    {
        var installation = modelBuilder.Entity<LocalInstallation>();
        installation.ToTable("local_installations");
        installation.HasKey(x => x.Id);
        installation.Property(x => x.Id).HasColumnType("uuid");
        installation.Property(x => x.InstallationKey).IsRequired().HasMaxLength(100);
        installation.Property(x => x.Platform).IsRequired().HasMaxLength(50);
        installation.Property(x => x.DeviceName).IsRequired().HasMaxLength(200);
        installation.Property(x => x.AppVersion).HasMaxLength(50);
        installation.Property(x => x.Status).IsRequired().HasMaxLength(30);
        installation.HasIndex(x => x.InstallationKey).IsUnique();

        var profile = modelBuilder.Entity<LocalProfile>();
        profile.ToTable("local_profiles");
        profile.HasKey(x => x.Id);
        profile.Property(x => x.Id).HasColumnType("uuid");
        profile.Property(x => x.InstallationId).HasColumnType("uuid");
        profile.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        profile.Property(x => x.Status).IsRequired().HasMaxLength(30);
        profile.HasIndex(x => new { x.InstallationId, x.Status });

        var device = modelBuilder.Entity<DeviceIdentity>();
        device.ToTable("device_identities");
        device.HasKey(x => x.Id);
        device.Property(x => x.Id).HasColumnType("uuid");
        device.Property(x => x.InstallationId).HasColumnType("uuid");
        device.Property(x => x.DeviceKey).IsRequired().HasMaxLength(100);
        device.Property(x => x.PublicKey).IsRequired().HasColumnType("text");
        device.Property(x => x.PrivateKeyRef).IsRequired().HasMaxLength(300);
        device.Property(x => x.KeyAlgorithm).IsRequired().HasMaxLength(30);
        device.Property(x => x.Status).IsRequired().HasMaxLength(30);
        device.HasIndex(x => x.DeviceKey).IsUnique();

        var account = modelBuilder.Entity<CloudAccountBinding>();
        account.ToTable("cloud_account_bindings");
        account.HasKey(x => x.Id);
        account.Property(x => x.Id).HasColumnType("uuid");
        account.Property(x => x.LocalProfileId).HasColumnType("uuid");
        account.Property(x => x.CloudUserId).IsRequired().HasMaxLength(200);
        account.Property(x => x.CloudApiBaseUrl).IsRequired().HasMaxLength(2048);
        account.Property(x => x.AccountDisplayName).HasMaxLength(200);
        account.Property(x => x.AccountEmailMasked).HasMaxLength(320);
        account.Property(x => x.TokenKeyRef).IsRequired().HasMaxLength(300);
        account.Property(x => x.BindingStatus).IsRequired().HasMaxLength(30);
        account.HasIndex(x => new { x.LocalProfileId, x.CloudApiBaseUrl, x.CloudUserId }).IsUnique();
        account.HasIndex(x => x.TokenKeyRef).IsUnique();

        var binding = modelBuilder.Entity<WorkspaceBinding>();
        binding.ToTable("workspace_bindings");
        binding.HasKey(x => x.Id);
        binding.Property(x => x.Id).HasColumnType("uuid");
        binding.Property(x => x.LocalWorkspaceId).HasColumnType("uuid");
        binding.Property(x => x.CloudAccountBindingId).HasColumnType("uuid");
        binding.Property(x => x.CloudWorkspaceId).IsRequired().HasMaxLength(200);
        binding.Property(x => x.SyncMode).IsRequired().HasMaxLength(30);
        binding.Property(x => x.BindingStatus).IsRequired().HasMaxLength(30);
        binding.Property(x => x.PrimaryDeviceId).HasColumnType("uuid");
        binding.Property(x => x.ConflictPolicy).IsRequired().HasMaxLength(30);
        binding.Property(x => x.LastInboxCursor).HasMaxLength(1000);
        binding.Property(x => x.LastSyncCursor).HasMaxLength(1000);
        binding.HasIndex(x => new { x.LocalWorkspaceId, x.CloudWorkspaceId }).IsUnique();
        binding.HasIndex(x => new { x.CloudAccountBindingId, x.BindingStatus });
    }

    private static void ConfigureSyncInboxStaging(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<SyncInboxStaging>();
        e.ToTable("sync_inbox_staging");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.WorkspaceId).HasColumnType("uuid");
        e.Property(x => x.BindingId).HasColumnType("uuid");
        e.Property(x => x.CloudInboxItemId).IsRequired().HasMaxLength(300);
        e.Property(x => x.ContentHash).HasMaxLength(128);
        e.Property(x => x.RemoteMetadataJson).HasColumnType("text");
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.LocalInboxItemId).HasColumnType("uuid");
        e.Property(x => x.DuplicateDocumentId).HasColumnType("uuid");
        e.Property(x => x.ImportBatchId).HasColumnType("uuid");
        e.Property(x => x.ErrorMessage).HasMaxLength(2000);
        e.HasIndex(x => new { x.WorkspaceId, x.CloudInboxItemId }).IsUnique();
        e.HasIndex(x => new { x.WorkspaceId, x.Status, x.DiscoveredAt });
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
