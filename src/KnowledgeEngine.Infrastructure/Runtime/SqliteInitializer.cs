using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Initializes a SQLite database with Phase 1 + Phase 2 schema.
/// Phase 1 tables: workspaces, settings, topics, inbox_items, sources, files, jobs, documents, document_chunks.
/// Phase 2 tables: inbox_attachments, file_objects, import_jobs, inbox_events, sync_cursors.
/// Also runs migrations (ALTER TABLE ADD COLUMN) for existing databases.
/// </summary>
public class SqliteInitializer
{
    private readonly ILogger<SqliteInitializer> _logger;

    public SqliteInitializer(ILogger<SqliteInitializer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates the SQLite database file and all tables.
    /// Idempotent: safe to call multiple times.
    /// Also creates a default topic per §9.4.
    /// </summary>
    public async Task InitializeAsync(string dbPath, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync(ct);

            // Enable WAL mode for better concurrency
            await using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                await pragma.ExecuteNonQueryAsync(ct);
            }

            // Create tables (IF NOT EXISTS for idempotency)
            var schemas = GetSchemas();
            foreach (var sql in schemas)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Phase 2 migrations: add new columns to existing tables
            await RunMigrationsAsync(conn, ct);

            // §9.4: Create default topic if none exists
            await CreateDefaultTopicAsync(conn, ct);

            _logger.LogInformation("SQLite database initialized at {Path} with {Count} schema items", dbPath, schemas.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SQLite database at {Path}: {Message}", dbPath, ex.Message);
            throw new InvalidOperationException($"SQLite initialization failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Runs ALTER TABLE ADD COLUMN migrations for existing databases
    /// that were created before Phase 2 schema expansion.
    /// </summary>
    private async Task RunMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        // inbox_items: add Phase 2 columns
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "user_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "suggested_topic_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "suggested_title", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "suggested_tags", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "origin_device_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "origin_client_version", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "source_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "error_code", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "retry_count", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "imported_at", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "inbox_items", "archived_at", "TEXT", ct);

        // sources: add inbox_item_id
        await AddColumnIfNotExistsAsync(conn, "sources", "inbox_item_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "sources", "author", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "sources", "published_at", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "sources", "original_file_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "sources", "raw_text", "TEXT", ct);

        // documents: Phase 3 fields
        await AddColumnIfNotExistsAsync(conn, "documents", "source_type", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "source_url", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "source_domain", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "author", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "published_at", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "recommended_tags", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "value_score_reason", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "should_deep_process", "INTEGER NOT NULL DEFAULT 1", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "parse_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "clean_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "index_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "parser_name", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "parser_version", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "cleaner_version", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "ai_raw_output", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "ai_error_message", "TEXT", ct);

        // Phase 3 migrations: chunk/ai tracking columns
        await AddColumnIfNotExistsAsync(conn, "documents", "chunk_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "ai_model", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "prompt_version", "TEXT", ct);

        // Phase 4 migrations: documents table new status columns
        await AddColumnIfNotExistsAsync(conn, "documents", "tag_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "entity_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "embedding_status", "TEXT NOT NULL DEFAULT 'pending'", ct);

        // Multilingual processing foundation
        await AddColumnIfNotExistsAsync(conn, "documents", "language", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "title_original", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "title_zh", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "summary_zh", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "keywords_zh", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localization_model", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localization_prompt_version", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localized_at", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localization_quality_score", "INTEGER", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localization_quality_issues", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "glossary_version", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "primary_language", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "language_distribution", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "is_multilingual", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localization_strategy", "TEXT NOT NULL DEFAULT 'none'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localization_level", "TEXT NOT NULL DEFAULT 'L1'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "language_detect_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "localization_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "enrichment_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "fulltext_index_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "documents", "content_hash", "TEXT", ct);

        // Phase 4 migrations: document_chunks table new columns
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "chunk_uid", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "heading_path", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "section_level", "INTEGER", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "content_hash", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "prev_chunk_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "next_chunk_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "index_status", "TEXT NOT NULL DEFAULT 'pending'", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "content_original", "TEXT NOT NULL DEFAULT ''", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "content_normalized", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "detected_language", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "language_confidence", "REAL", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "language_distribution", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "content_type", "TEXT NOT NULL DEFAULT 'paragraph'", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "processing_route", "TEXT NOT NULL DEFAULT 'review'", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "localization_required", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "chunk_group_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "parent_chunk_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "paragraph_start", "INTEGER", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "paragraph_end", "INTEGER", ct);
        await AddColumnIfNotExistsAsync(conn, "document_chunks", "bounding_box", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "chunk_embeddings", "language_code", "TEXT NOT NULL DEFAULT 'und'", ct);
        await AddColumnIfNotExistsAsync(conn, "chunk_embeddings", "embedding_type", "TEXT NOT NULL DEFAULT 'original'", ct);
        await AddColumnIfNotExistsAsync(conn, "chunk_embeddings", "localization_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "chunk_embeddings", "source_content_hash", "TEXT", ct);

        await using (var backfill = conn.CreateCommand())
        {
            backfill.CommandText = @"UPDATE documents SET title_original = title WHERE title_original IS NULL;
UPDATE documents SET primary_language = language WHERE primary_language IS NULL AND language IS NOT NULL;
UPDATE document_chunks SET content_original = content WHERE content_original = '';";
            await backfill.ExecuteNonQueryAsync(ct);
        }

        // Phase 5 migrations: user_usage_daily Agent dimension columns
        await AddColumnIfNotExistsAsync(conn, "user_usage_daily", "agent_call_count", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "user_usage_daily", "agent_search_count", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "user_usage_daily", "agent_qa_count", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "user_usage_daily", "agent_write_count", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "user_usage_daily", "agent_success_count", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfNotExistsAsync(conn, "user_usage_daily", "agent_failed_count", "INTEGER NOT NULL DEFAULT 0", ct);

        // Phase 7 migrations: security & permission model
        await AddColumnIfNotExistsAsync(conn, "documents", "sensitivity_level", "TEXT NOT NULL DEFAULT 'normal'", ct);
        await AddColumnIfNotExistsAsync(conn, "agent_profiles", "scopes", "TEXT", ct);

        // Phase 7 migrations: feedback_items missing columns
        await AddColumnIfNotExistsAsync(conn, "feedback_items", "beta_user_id", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "feedback_items", "related_entity_type", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "feedback_items", "priority", "TEXT DEFAULT 'medium'", ct);

        // Phase 7 migrations: beta_users missing columns
        await AddColumnIfNotExistsAsync(conn, "beta_users", "beta_group", "TEXT", ct);
        await AddColumnIfNotExistsAsync(conn, "beta_users", "platform", "TEXT", ct);

        // Identity, device, binding, and explicit sync-mode foundation.
        await AddColumnIfNotExistsAsync(conn, "workspaces", "sync_mode", "TEXT NOT NULL DEFAULT 'none'", ct);
        await AddColumnIfNotExistsAsync(conn, "device_identities", "private_key_ref", "TEXT NOT NULL DEFAULT ''", ct);
        await using (var syncModeBackfill = conn.CreateCommand())
        {
            syncModeBackfill.CommandText = """
                UPDATE workspaces
                SET sync_mode = CASE
                    WHEN inbox_enabled = 1 THEN 'inbox_only'
                    WHEN sync_enabled = 1 THEN 'metadata'
                    ELSE 'none'
                END
                WHERE sync_mode IS NULL OR sync_mode = '' OR sync_mode = 'none';
                """;
            await syncModeBackfill.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Phase 2 migrations completed");
        _logger.LogInformation("Phase 3 migrations completed");
        _logger.LogInformation("Phase 4 migrations completed");
        _logger.LogInformation("Phase 5 migrations completed");
        _logger.LogInformation("Phase 7 migrations completed");
    }

    /// <summary>
    /// Adds a column to a table if it doesn't already exist.
    /// </summary>
    private async Task AddColumnIfNotExistsAsync(SqliteConnection conn, string table, string column, string definition, CancellationToken ct)
    {
        // Check if column exists using PRAGMA table_info
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await checkCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var existingCol = reader.GetString(1); // name column
            if (existingCol.Equals(column, StringComparison.OrdinalIgnoreCase))
                return; // Column already exists
        }

        // Add the column
        var alterCmd = conn.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alterCmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Added column {Column} to table {Table}", column, table);
    }

    /// <summary>
    /// Creates a default topic "我的知识库" if no topics exist (§9.4).
    /// </summary>
    private async Task CreateDefaultTopicAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM topics";
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct));
            if (count > 0) return;
        }

        var topicId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO topics (id, workspace_id, name, description, domain, status, created_at, updated_at)
            VALUES ($id, 'default', '我的知识库', '默认知识库，所有未分类的资料都会保存在这里', '', 'active', $now, $now)";
        cmd.Parameters.AddWithValue("$id", topicId);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Created default topic '我的知识库' (id={Id})", topicId);
    }

    private static string[] GetSchemas() => new[]
    {
        // ===== Phase 1 Tables =====
        @"CREATE TABLE IF NOT EXISTS workspaces (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            mode TEXT NOT NULL,
            storage_provider TEXT NOT NULL,
            file_provider TEXT NOT NULL,
            job_provider TEXT NOT NULL,
            model_provider TEXT,
            local_db_path TEXT,
            local_vault_path TEXT,
            cloud_api_base_url TEXT,
            cloud_workspace_id TEXT,
            sync_enabled INTEGER NOT NULL DEFAULT 0,
            inbox_enabled INTEGER NOT NULL DEFAULT 0,
            sync_mode TEXT NOT NULL DEFAULT 'none',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE TABLE IF NOT EXISTS topics (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT,
            domain TEXT,
            status TEXT NOT NULL DEFAULT 'active',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_topics_workspace_id ON topics(workspace_id)",

        // ===== inbox_items (§7.1) — full Phase 2 schema =====
        @"CREATE TABLE IF NOT EXISTS inbox_items (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            user_id TEXT,
            topic_id TEXT,
            input_type TEXT NOT NULL DEFAULT 'text',
            item_type TEXT NOT NULL DEFAULT 'text',
            title TEXT,
            content TEXT,
            content_text TEXT,
            source_url TEXT,
            file_path TEXT,
            status TEXT NOT NULL DEFAULT 'pending',
            suggested_topic_id TEXT,
            suggested_title TEXT,
            suggested_tags TEXT,
            created_from TEXT NOT NULL DEFAULT 'desktop',
            origin_device_id TEXT,
            origin_client_version TEXT,
            source_id TEXT,
            error_code TEXT,
            error_message TEXT,
            retry_count INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            imported_at TEXT,
            archived_at TEXT
        )",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_workspace_status ON inbox_items(workspace_id, status, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_workspace_type ON inbox_items(workspace_id, input_type, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_workspace_id ON inbox_items(workspace_id)",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_status ON inbox_items(status)",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_topic ON inbox_items(workspace_id, topic_id, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_source ON inbox_items(source_id)",

        // ===== inbox_attachments (§7.2) =====
        @"CREATE TABLE IF NOT EXISTS inbox_attachments (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            inbox_item_id TEXT NOT NULL,
            file_id TEXT NOT NULL,
            role TEXT NOT NULL DEFAULT 'primary',
            filename TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (inbox_item_id) REFERENCES inbox_items(id)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_attachments_item ON inbox_attachments(inbox_item_id)",

        // ===== file_objects (§7.3) =====
        @"CREATE TABLE IF NOT EXISTS file_objects (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            storage_provider TEXT NOT NULL DEFAULT 'local_fs',
            bucket TEXT,
            object_key TEXT,
            local_path TEXT,
            original_filename TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            extension TEXT,
            size_bytes INTEGER NOT NULL,
            sha256 TEXT,
            created_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_file_workspace ON file_objects(workspace_id, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_file_hash ON file_objects(workspace_id, sha256)",

        // ===== sources (§7.4) — with inbox_item_id =====
        @"CREATE TABLE IF NOT EXISTS sources (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            topic_id TEXT,
            inbox_item_id TEXT,
            source_type TEXT NOT NULL,
            title TEXT,
            url TEXT,
            domain TEXT,
            author TEXT,
            published_at TEXT,
            local_file_path TEXT,
            original_file_id TEXT,
            raw_text TEXT,
            content_hash TEXT,
            status TEXT NOT NULL DEFAULT 'pending',
            error_message TEXT,
            retry_count INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_sources_workspace_id ON sources(workspace_id)",
        @"CREATE INDEX IF NOT EXISTS idx_sources_topic_id ON sources(topic_id)",
        @"CREATE INDEX IF NOT EXISTS idx_sources_status ON sources(status)",
        @"CREATE INDEX IF NOT EXISTS idx_sources_workspace_status ON sources(workspace_id, status, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_sources_inbox_item ON sources(inbox_item_id)",
        @"CREATE INDEX IF NOT EXISTS idx_sources_url ON sources(workspace_id, url)",

        // ===== import_jobs (§7.5) =====
        @"CREATE TABLE IF NOT EXISTS import_jobs (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            inbox_item_id TEXT NOT NULL,
            source_id TEXT,
            job_type TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'queued',
            attempt INTEGER NOT NULL DEFAULT 0,
            max_attempts INTEGER NOT NULL DEFAULT 3,
            started_at TEXT,
            finished_at TEXT,
            error_code TEXT,
            error_message TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_import_jobs_status ON import_jobs(workspace_id, status, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_import_jobs_inbox ON import_jobs(inbox_item_id)",

        // ===== inbox_events (§7.6) =====
        @"CREATE TABLE IF NOT EXISTS inbox_events (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            inbox_item_id TEXT NOT NULL,
            event_type TEXT NOT NULL,
            event_payload TEXT,
            created_by TEXT,
            created_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_inbox_events_item ON inbox_events(inbox_item_id, created_at)",

        // ===== sync_cursors (§7.7) =====
        @"CREATE TABLE IF NOT EXISTS sync_cursors (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            remote_workspace_id TEXT NOT NULL,
            cursor_type TEXT NOT NULL,
            cursor_value TEXT,
            last_synced_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_cursor ON sync_cursors(workspace_id, remote_workspace_id, cursor_type)",

        // ===== identity and workspace binding foundation =====
        @"CREATE TABLE IF NOT EXISTS local_installations (
            id TEXT PRIMARY KEY,
            installation_key TEXT NOT NULL UNIQUE,
            platform TEXT NOT NULL,
            device_name TEXT NOT NULL,
            app_version TEXT NOT NULL DEFAULT '',
            status TEXT NOT NULL DEFAULT 'active',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE TABLE IF NOT EXISTS local_profiles (
            id TEXT PRIMARY KEY,
            installation_id TEXT NOT NULL,
            display_name TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'active',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_local_profiles_installation_status ON local_profiles(installation_id, status)",
        @"CREATE TABLE IF NOT EXISTS device_identities (
            id TEXT PRIMARY KEY,
            installation_id TEXT NOT NULL,
            device_key TEXT NOT NULL UNIQUE,
            public_key TEXT NOT NULL,
            private_key_ref TEXT NOT NULL,
            key_algorithm TEXT NOT NULL DEFAULT 'ed25519',
            status TEXT NOT NULL DEFAULT 'active',
            last_seen_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE TABLE IF NOT EXISTS cloud_account_bindings (
            id TEXT PRIMARY KEY,
            local_profile_id TEXT NOT NULL,
            cloud_user_id TEXT NOT NULL,
            cloud_api_base_url TEXT NOT NULL,
            account_display_name TEXT,
            account_email_masked TEXT,
            token_key_ref TEXT NOT NULL UNIQUE,
            binding_status TEXT NOT NULL DEFAULT 'active',
            last_authenticated_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(local_profile_id, cloud_api_base_url, cloud_user_id)
        )",
        @"CREATE TABLE IF NOT EXISTS workspace_bindings (
            id TEXT PRIMARY KEY,
            local_workspace_id TEXT NOT NULL,
            cloud_account_binding_id TEXT NOT NULL,
            cloud_workspace_id TEXT NOT NULL,
            sync_mode TEXT NOT NULL DEFAULT 'none',
            binding_status TEXT NOT NULL DEFAULT 'active',
            primary_device_id TEXT,
            upload_original_files INTEGER NOT NULL DEFAULT 0,
            conflict_policy TEXT NOT NULL DEFAULT 'manual',
            last_inbox_cursor TEXT,
            last_sync_cursor TEXT,
            last_sync_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(local_workspace_id, cloud_workspace_id)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_workspace_bindings_account_status ON workspace_bindings(cloud_account_binding_id, binding_status)",
        @"CREATE TABLE IF NOT EXISTS sync_inbox_staging (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            binding_id TEXT,
            cloud_inbox_item_id TEXT NOT NULL,
            cloud_revision INTEGER,
            content_hash TEXT,
            remote_metadata_json TEXT,
            status TEXT NOT NULL DEFAULT 'discovered',
            local_inbox_item_id TEXT,
            duplicate_document_id TEXT,
            import_batch_id TEXT,
            error_message TEXT,
            discovered_at TEXT NOT NULL,
            imported_at TEXT,
            updated_at TEXT NOT NULL,
            UNIQUE(workspace_id, cloud_inbox_item_id)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_sync_inbox_staging_status ON sync_inbox_staging(workspace_id, status, discovered_at)",

        // ===== cloud_inbox_sync_logs =====
        @"CREATE TABLE IF NOT EXISTS cloud_inbox_sync_logs (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            direction TEXT NOT NULL DEFAULT 'pull',
            status TEXT NOT NULL,
            cloud_api_base_url TEXT,
            cloud_workspace_id TEXT,
            retention TEXT NOT NULL DEFAULT 'keep',
            pulled_count INTEGER NOT NULL DEFAULT 0,
            failed_count INTEGER NOT NULL DEFAULT 0,
            next_cursor TEXT,
            error_message TEXT,
            started_at TEXT NOT NULL,
            finished_at TEXT NOT NULL,
            duration_ms INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_cloud_inbox_sync_logs_workspace_created ON cloud_inbox_sync_logs(workspace_id, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_cloud_inbox_sync_logs_workspace_status ON cloud_inbox_sync_logs(workspace_id, status)",

        // ===== Legacy files table (kept for backward compat) =====
        @"CREATE TABLE IF NOT EXISTS files (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            source_id TEXT,
            local_path TEXT,
            file_name TEXT,
            mime_type TEXT,
            file_size INTEGER,
            file_hash TEXT,
            created_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_files_workspace_id ON files(workspace_id)",
        @"CREATE INDEX IF NOT EXISTS idx_files_source_id ON files(source_id)",

        // ===== Legacy jobs table (kept for backward compat) =====
        @"CREATE TABLE IF NOT EXISTS jobs (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            job_type TEXT NOT NULL,
            target_type TEXT NOT NULL,
            target_id TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'pending',
            error_message TEXT,
            retry_count INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            started_at TEXT,
            finished_at TEXT
        )",
        @"CREATE INDEX IF NOT EXISTS idx_jobs_workspace_id ON jobs(workspace_id)",
        @"CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status)",
        @"CREATE INDEX IF NOT EXISTS idx_jobs_type ON jobs(job_type)",

        // ===== Documents table =====
        @"CREATE TABLE IF NOT EXISTS documents (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            topic_id TEXT,
            source_id TEXT,
            title TEXT NOT NULL,
            content_markdown TEXT,
            content_text TEXT,
            language TEXT,
            title_original TEXT,
            title_zh TEXT,
            summary_zh TEXT,
            keywords_zh TEXT,
            localization_model TEXT,
            localization_prompt_version TEXT,
            localized_at TEXT,
            localization_quality_score INTEGER,
            localization_quality_issues TEXT,
            glossary_version TEXT,
            primary_language TEXT,
            language_distribution TEXT,
            is_multilingual INTEGER NOT NULL DEFAULT 0,
            localization_strategy TEXT NOT NULL DEFAULT 'none',
            localization_level TEXT NOT NULL DEFAULT 'L1',
            language_detect_status TEXT NOT NULL DEFAULT 'pending',
            localization_status TEXT NOT NULL DEFAULT 'pending',
            enrichment_status TEXT NOT NULL DEFAULT 'pending',
            fulltext_index_status TEXT NOT NULL DEFAULT 'pending',
            content_hash TEXT,
            summary TEXT,
            ai_status TEXT NOT NULL DEFAULT 'pending',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_documents_workspace_id ON documents(workspace_id)",
        @"CREATE INDEX IF NOT EXISTS idx_documents_topic_id ON documents(topic_id)",

        // ===== document_processing_logs (Phase 3) =====
        @"CREATE TABLE IF NOT EXISTS document_processing_logs (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            source_id TEXT,
            document_id TEXT,
            step_name TEXT NOT NULL,
            status TEXT NOT NULL,
            message TEXT,
            error_code TEXT,
            error_stack TEXT,
            input_snapshot TEXT,
            output_snapshot TEXT,
            started_at TEXT,
            finished_at TEXT,
            duration_ms INTEGER,
            created_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_processing_logs_source ON document_processing_logs(source_id)",
        @"CREATE INDEX IF NOT EXISTS idx_processing_logs_document ON document_processing_logs(document_id)",

        // ===== Document chunks table =====
        @"CREATE TABLE IF NOT EXISTS document_chunks (
            id TEXT PRIMARY KEY,
            document_id TEXT NOT NULL,
            chunk_index INTEGER NOT NULL,
            chunk_title TEXT,
            content TEXT NOT NULL,
            content_original TEXT NOT NULL DEFAULT '',
            content_normalized TEXT,
            detected_language TEXT,
            language_confidence REAL,
            language_distribution TEXT,
            content_type TEXT NOT NULL DEFAULT 'paragraph',
            processing_route TEXT NOT NULL DEFAULT 'review',
            localization_required INTEGER NOT NULL DEFAULT 0,
            chunk_group_id TEXT,
            parent_chunk_id TEXT,
            paragraph_start INTEGER,
            paragraph_end INTEGER,
            bounding_box TEXT,
            token_count INTEGER DEFAULT 0,
            char_count INTEGER DEFAULT 0
        )",
        @"CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON document_chunks(document_id)",

        // ===== Multilingual terminology =====
        @"CREATE TABLE IF NOT EXISTS terminology (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            workspace_id TEXT,
            source_language TEXT NOT NULL,
            source_term TEXT NOT NULL,
            target_language TEXT NOT NULL,
            target_term TEXT NOT NULL,
            aliases TEXT,
            domain TEXT,
            priority INTEGER NOT NULL DEFAULT 0,
            review_status TEXT NOT NULL DEFAULT 'approved',
            version TEXT NOT NULL DEFAULT 'v1',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS idx_terminology_user_pair ON terminology(user_id, source_term, target_term)",
        @"CREATE TABLE IF NOT EXISTS chunk_localizations (
            id TEXT PRIMARY KEY, chunk_id TEXT NOT NULL, user_id TEXT NOT NULL, workspace_id TEXT,
            language_code TEXT NOT NULL, heading_localized TEXT, content_localized TEXT NOT NULL,
            translation_type TEXT NOT NULL, model TEXT, prompt_version TEXT NOT NULL, glossary_version TEXT,
            quality_score INTEGER, quality_issues TEXT, review_status TEXT NOT NULL, status TEXT NOT NULL,
            source_content_hash TEXT NOT NULL, idempotency_key TEXT NOT NULL,
            reviewed_at TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS idx_chunk_localizations_chunk_language ON chunk_localizations(chunk_id, language_code)",
        @"CREATE UNIQUE INDEX IF NOT EXISTS idx_chunk_localizations_idempotency ON chunk_localizations(idempotency_key)",
        @"CREATE TABLE IF NOT EXISTS chunk_enrichments (
            id TEXT PRIMARY KEY, chunk_id TEXT NOT NULL, user_id TEXT NOT NULL, localization_id TEXT,
            language_code TEXT NOT NULL, summary TEXT, keywords TEXT, entities TEXT, facts TEXT,
            hypothetical_questions TEXT, model TEXT, prompt_version TEXT, source_content_hash TEXT NOT NULL,
            status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS idx_chunk_enrichments_chunk_language ON chunk_enrichments(chunk_id, language_code)",
        @"CREATE TABLE IF NOT EXISTS multilingual_batch_jobs (
            id TEXT PRIMARY KEY, user_id TEXT NOT NULL, document_id TEXT NOT NULL, job_type TEXT NOT NULL,
            status TEXT NOT NULL, force INTEGER NOT NULL DEFAULT 0, max_chunks INTEGER NOT NULL DEFAULT 500,
            total_items INTEGER NOT NULL DEFAULT 0, processed_items INTEGER NOT NULL DEFAULT 0,
            succeeded_items INTEGER NOT NULL DEFAULT 0, failed_items INTEGER NOT NULL DEFAULT 0,
            current_chunk_id TEXT, error_message TEXT, retry_count INTEGER NOT NULL DEFAULT 0,
            started_at TEXT, finished_at TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_multilingual_jobs_user_status ON multilingual_batch_jobs(user_id, status, created_at)",

        // ===== Phase 4 Tables =====

        // ===== tags (Phase 4) =====
        @"CREATE TABLE IF NOT EXISTS tags (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            user_id TEXT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            display_name TEXT,
            tag_type TEXT NOT NULL DEFAULT 'topic',
            description TEXT,
            color TEXT,
            aliases TEXT,
            source TEXT NOT NULL DEFAULT 'manual',
            usage_count INTEGER NOT NULL DEFAULT 0,
            is_system INTEGER NOT NULL DEFAULT 0,
            is_archived INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(workspace_id, normalized_name)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_tags_workspace ON tags(workspace_id, tag_type)",

        // ===== document_tags (Phase 4) =====
        @"CREATE TABLE IF NOT EXISTS document_tags (
            document_id TEXT NOT NULL,
            tag_id TEXT NOT NULL,
            source TEXT NOT NULL DEFAULT 'ai',
            confidence REAL,
            reason TEXT,
            is_confirmed INTEGER NOT NULL DEFAULT 0,
            confirmed_by TEXT,
            confirmed_at TEXT,
            created_at TEXT NOT NULL,
            PRIMARY KEY(document_id, tag_id)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_document_tags_tag ON document_tags(tag_id)",

        // ===== entities (Phase 4) =====
        @"CREATE TABLE IF NOT EXISTS entities (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            user_id TEXT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            display_name TEXT,
            entity_type TEXT NOT NULL,
            aliases TEXT,
            description TEXT,
            external_ref TEXT,
            source TEXT NOT NULL DEFAULT 'ai',
            usage_count INTEGER NOT NULL DEFAULT 0,
            is_verified INTEGER NOT NULL DEFAULT 0,
            is_archived INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(workspace_id, normalized_name, entity_type)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_entities_workspace ON entities(workspace_id, entity_type)",

        // ===== document_entities (Phase 4) =====
        @"CREATE TABLE IF NOT EXISTS document_entities (
            document_id TEXT NOT NULL,
            entity_id TEXT NOT NULL,
            mention_count INTEGER NOT NULL DEFAULT 1,
            first_mention TEXT,
            mention_examples TEXT,
            importance REAL,
            role TEXT,
            sentiment TEXT,
            source TEXT NOT NULL DEFAULT 'ai',
            confidence REAL,
            created_at TEXT NOT NULL,
            PRIMARY KEY(document_id, entity_id)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_document_entities_entity ON document_entities(entity_id)",

        // ===== chunk_embeddings (Phase 4) =====
        @"CREATE TABLE IF NOT EXISTS chunk_embeddings (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            chunk_id TEXT NOT NULL,
            provider TEXT NOT NULL DEFAULT 'openai',
            model TEXT NOT NULL,
            model_version TEXT,
            dimension INTEGER NOT NULL DEFAULT 0,
            embedding_json TEXT,
            vector_ref TEXT,
            chunk_content_hash TEXT NOT NULL DEFAULT '',
            language_code TEXT NOT NULL DEFAULT 'und',
            embedding_type TEXT NOT NULL DEFAULT 'original',
            localization_id TEXT,
            source_content_hash TEXT,
            status TEXT NOT NULL DEFAULT 'pending',
            error_message TEXT,
            retry_count INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(chunk_id, language_code, embedding_type, provider, model)
        )",
        @"CREATE INDEX IF NOT EXISTS idx_chunk_embeddings_chunk ON chunk_embeddings(chunk_id)",
        @"CREATE INDEX IF NOT EXISTS idx_chunk_embeddings_status ON chunk_embeddings(workspace_id, status)",

        // ===== vector_index_states (Phase 4) =====
        @"CREATE TABLE IF NOT EXISTS vector_index_states (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL UNIQUE,
            provider TEXT NOT NULL DEFAULT '',
            model TEXT NOT NULL DEFAULT '',
            dimension INTEGER NOT NULL DEFAULT 0,
            index_backend TEXT NOT NULL DEFAULT 'sqlite',
            total_chunks INTEGER NOT NULL DEFAULT 0,
            indexed_chunks INTEGER NOT NULL DEFAULT 0,
            failed_chunks INTEGER NOT NULL DEFAULT 0,
            stale_chunks INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'idle',
            last_rebuilt_at TEXT,
            schema_version TEXT NOT NULL DEFAULT 'v1',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",

        // ===== Phase 5 Tables =====

        // ===== search_logs (Phase 5) =====
        @"CREATE TABLE IF NOT EXISTS search_logs (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            topic_id TEXT,
            query TEXT NOT NULL,
            search_type TEXT NOT NULL DEFAULT 'hybrid',
            filters TEXT,
            result_count INTEGER NOT NULL DEFAULT 0,
            latency_ms INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        )",

        // ===== qa_sessions (Phase 5) =====
        @"CREATE TABLE IF NOT EXISTS qa_sessions (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            topic_id TEXT,
            title TEXT NOT NULL DEFAULT 'New Session',
            status TEXT NOT NULL DEFAULT 'active',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",

        // ===== qa_messages (Phase 5) =====
        @"CREATE TABLE IF NOT EXISTS qa_messages (
            id TEXT PRIMARY KEY,
            session_id TEXT NOT NULL,
            user_id TEXT NOT NULL,
            topic_id TEXT,
            role TEXT NOT NULL,
            content TEXT NOT NULL,
            citations TEXT,
            retrieval_snapshot TEXT,
            model TEXT,
            input_tokens INTEGER,
            output_tokens INTEGER,
            latency_ms INTEGER,
            created_at TEXT NOT NULL
        )",

        // ===== retrieval_logs (Phase 5) =====
        @"CREATE TABLE IF NOT EXISTS retrieval_logs (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            topic_id TEXT,
            qa_message_id TEXT,
            query TEXT NOT NULL,
            retrieval_type TEXT NOT NULL DEFAULT 'hybrid',
            retrieved_chunks TEXT,
            final_context TEXT,
            latency_ms INTEGER,
            created_at TEXT NOT NULL
        )",

        // ===== feedback_items (Phase 5) =====
        @"CREATE TABLE IF NOT EXISTS feedback_items (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            feedback_type TEXT NOT NULL DEFAULT 'general',
            module TEXT,
            title TEXT,
            content TEXT,
            severity TEXT NOT NULL DEFAULT 'normal',
            status TEXT NOT NULL DEFAULT 'open',
            related_entity_id TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",

        // ===== reports (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS reports (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            topic_id TEXT,

            report_type TEXT NOT NULL,
            title TEXT NOT NULL,
            slug TEXT,

            content_markdown TEXT NOT NULL,
            summary TEXT,
            one_sentence_conclusion TEXT,

            query TEXT,
            start_date TEXT,
            end_date TEXT,

            source_document_ids TEXT,
            source_chunk_ids TEXT,
            source_report_ids TEXT,
            citations_json TEXT,

            template_id TEXT,
            generation_mode TEXT NOT NULL DEFAULT 'manual',
            generated_by_model TEXT,
            prompt_version TEXT,
            model_config_snapshot TEXT,

            status TEXT NOT NULL DEFAULT 'pending',
            quality_score INTEGER,
            citation_coverage REAL,
            evidence_count INTEGER,

            export_status TEXT NOT NULL DEFAULT 'not_exported',
            last_exported_at TEXT,

            error_message TEXT,
            created_by TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",

        // ===== report_jobs (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS report_jobs (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            topic_id TEXT,

            report_type TEXT NOT NULL,
            report_id TEXT,

            status TEXT NOT NULL DEFAULT 'pending',

            input_params TEXT NOT NULL,
            plan_json TEXT,
            retrieval_snapshot_json TEXT,
            prompt_snapshot TEXT,
            model_output TEXT,
            model TEXT,
            prompt_version TEXT,

            progress INTEGER NOT NULL DEFAULT 0,
            current_step TEXT,

            error_code TEXT,
            error_message TEXT,
            retry_count INTEGER NOT NULL DEFAULT 0,

            started_at TEXT,
            finished_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",

        // ===== report_sources (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS report_sources (
            report_id TEXT NOT NULL,
            document_id TEXT NOT NULL,
            chunk_id TEXT,

            citation_index INTEGER,
            relevance_score REAL,
            source_role TEXT,

            created_at TEXT NOT NULL,
            PRIMARY KEY (report_id, document_id, chunk_id)
        )",

        // ===== report_templates (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS report_templates (
            id TEXT PRIMARY KEY,
            user_id TEXT,

            name TEXT NOT NULL,
            report_type TEXT NOT NULL,
            description TEXT,

            template_markdown TEXT NOT NULL,
            system_prompt TEXT,
            user_prompt_template TEXT,

            output_rules TEXT,
            is_system INTEGER NOT NULL DEFAULT 0,
            is_active INTEGER NOT NULL DEFAULT 1,

            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",

        // ===== export_jobs (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS export_jobs (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            topic_id TEXT,

            export_type TEXT NOT NULL,
            target_type TEXT NOT NULL,
            target_id TEXT,

            status TEXT NOT NULL DEFAULT 'pending',
            params TEXT,
            file_id TEXT,
            output_path TEXT,

            progress INTEGER NOT NULL DEFAULT 0,
            error_message TEXT,
            retry_count INTEGER NOT NULL DEFAULT 0,

            started_at TEXT,
            finished_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",

        // ===== export_files (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS export_files (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,

            export_job_id TEXT,
            file_name TEXT NOT NULL,
            file_type TEXT NOT NULL,
            mime_type TEXT,
            file_size INTEGER,

            storage_provider TEXT NOT NULL,
            storage_path TEXT NOT NULL,
            download_url TEXT,

            checksum TEXT,
            created_at TEXT NOT NULL,
            expires_at TEXT
        )",

        // ===== agent_profiles (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS agent_profiles (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT,
            allowed_tool_names TEXT,
            allowed_topic_ids TEXT,
            allow_sensitive_documents INTEGER NOT NULL DEFAULT 0,
            max_results_per_call INTEGER NOT NULL DEFAULT 20,
            rate_limit_per_minute INTEGER NOT NULL DEFAULT 60,
            daily_quota INTEGER NOT NULL DEFAULT 1000,
            api_key_id TEXT,
            transport TEXT NOT NULL DEFAULT 'stdio',
            mcp_server_path TEXT,
            status TEXT NOT NULL DEFAULT 'active',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_agent_profiles_user_status ON agent_profiles(user_id, status)",

        // ===== agent_invocation_logs (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS agent_invocation_logs (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            agent_profile_id TEXT,
            api_key_id TEXT,
            transport TEXT NOT NULL DEFAULT 'cloud_api',
            tool_name TEXT NOT NULL,
            input_json TEXT,
            output_summary TEXT,
            result_count INTEGER,
            prompt_tokens INTEGER,
            completion_tokens INTEGER,
            status TEXT NOT NULL DEFAULT 'success',
            error_code TEXT,
            error_message TEXT,
            latency_ms INTEGER NOT NULL DEFAULT 0,
            trace_id TEXT,
            ip_address TEXT,
            user_agent TEXT,
            created_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_agent_invocation_logs_user_created ON agent_invocation_logs(user_id, created_at)",
        @"CREATE INDEX IF NOT EXISTS idx_agent_invocation_logs_transport_tool ON agent_invocation_logs(transport, tool_name)",

        // ===== report_citations (Phase 6) =====
        @"CREATE TABLE IF NOT EXISTS report_citations (
            id TEXT PRIMARY KEY,
            report_id TEXT NOT NULL,
            document_id TEXT NOT NULL,
            chunk_id TEXT,
            citation_index INTEGER NOT NULL,
            citation_key TEXT,
            quote_text TEXT,
            section_key TEXT,
            title TEXT,
            source_url TEXT,
            source_domain TEXT,
            source_type TEXT,
            relevance_score REAL,
            source_role TEXT,
            created_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_report_citations_report ON report_citations(report_id)",
        @"CREATE INDEX IF NOT EXISTS idx_report_citations_document ON report_citations(document_id)",

        // ===== user_usage_daily (Phase 5) =====
        @"CREATE TABLE IF NOT EXISTS user_usage_daily (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            usage_date TEXT NOT NULL,
            imported_count INTEGER NOT NULL DEFAULT 0,
            document_count INTEGER NOT NULL DEFAULT 0,
            search_count INTEGER NOT NULL DEFAULT 0,
            qa_count INTEGER NOT NULL DEFAULT 0,
            report_count INTEGER NOT NULL DEFAULT 0,
            export_count INTEGER NOT NULL DEFAULT 0,
            api_call_count INTEGER NOT NULL DEFAULT 0,
            agent_call_count INTEGER NOT NULL DEFAULT 0,
            agent_search_count INTEGER NOT NULL DEFAULT 0,
            agent_qa_count INTEGER NOT NULL DEFAULT 0,
            agent_write_count INTEGER NOT NULL DEFAULT 0,
            agent_success_count INTEGER NOT NULL DEFAULT 0,
            agent_failed_count INTEGER NOT NULL DEFAULT 0,
            input_tokens INTEGER NOT NULL DEFAULT 0,
            output_tokens INTEGER NOT NULL DEFAULT 0,
            embedding_tokens INTEGER NOT NULL DEFAULT 0,
            storage_bytes INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE UNIQUE INDEX IF NOT EXISTS idx_user_usage_daily_user_date ON user_usage_daily(user_id, usage_date)",

        // ===== Phase 7 Tables =====

        // ===== beta_users (Phase 7) =====
        @"CREATE TABLE IF NOT EXISTS beta_users (
            id TEXT PRIMARY KEY,
            user_id TEXT,
            email TEXT NOT NULL,
            name TEXT,
            user_type TEXT DEFAULT 'unknown',
            invite_code TEXT,
            beta_group TEXT,
            platform TEXT,
            status TEXT NOT NULL DEFAULT 'invited',
            onboarded_at TEXT,
            last_feedback_at TEXT,
            notes TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_beta_users_email_status ON beta_users(email, status)",
        @"CREATE INDEX IF NOT EXISTS idx_beta_users_status ON beta_users(status)",
        @"CREATE INDEX IF NOT EXISTS idx_beta_users_beta_group ON beta_users(beta_group)",

        // ===== release_notes (Phase 7) =====
        @"CREATE TABLE IF NOT EXISTS release_notes (
            id TEXT PRIMARY KEY,
            version TEXT NOT NULL,
            title TEXT NOT NULL,
            channel TEXT NOT NULL DEFAULT 'alpha',
            content_markdown TEXT NOT NULL,
            highlights TEXT,
            known_issues TEXT,
            is_published INTEGER NOT NULL DEFAULT 0,
            published_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )",
        @"CREATE INDEX IF NOT EXISTS idx_release_notes_channel_published ON release_notes(channel, is_published, published_at)",
        @"CREATE INDEX IF NOT EXISTS idx_release_notes_version ON release_notes(version)"
    };

    /// <summary>
    /// Checks if the SQLite database file exists.
    /// </summary>
    public bool DatabaseExists(string dbPath)
    {
        return File.Exists(dbPath);
    }
}
