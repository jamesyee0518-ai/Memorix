using KnowledgeEngine.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Local knowledge repository using SQLite.
/// Implements IKnowledgeRepository for the Phase 1 + Phase 2 schema.
/// Used when workspace.mode == "local".
/// </summary>
public class LocalKnowledgeRepository : IKnowledgeRepository
{
    private readonly string _connectionString;
    private readonly ILogger<LocalKnowledgeRepository> _logger;

    public LocalKnowledgeRepository(string dbPath, ILogger<LocalKnowledgeRepository> logger)
    {
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    // ===== Topics =====

    public async Task<TopicDto> CreateTopicAsync(CreateTopicInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO topics (id, workspace_id, name, description, domain, status, created_at, updated_at)
            VALUES ($id, $ws, $name, $desc, $dom, 'active', $now, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$name", input.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)input.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dom", (object?)input.Domain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);

        return new TopicDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            Name = input.Name,
            Description = input.Description,
            Domain = input.Domain,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public async Task<TopicDto?> GetTopicAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, workspace_id, name, description, domain, status, created_at, updated_at FROM topics WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadTopic(r);
    }

    public async Task<List<TopicDto>> ListTopicsAsync(string workspaceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, workspace_id, name, description, domain, status, created_at, updated_at FROM topics WHERE workspace_id = $ws ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        var results = new List<TopicDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadTopic(r));
        return results;
    }

    // ===== Inbox Items (Phase 2 full schema) =====

    public async Task<InboxItemDto> CreateInboxItemAsync(CreateInboxItemInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO inbox_items (
                id, workspace_id, user_id, topic_id, input_type, item_type, title, content, content_text,
                source_url, file_path, status, created_from, origin_device_id, origin_client_version,
                retry_count, created_at, updated_at
            ) VALUES (
                $id, $ws, $uid, $tid, $it, $it, $title, $content, $content,
                $url, $fp, 'pending', $cf, $odid, $ocv, 0, $now, $now
            )";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$uid", (object?)input.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tid", (object?)input.TopicId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$it", input.InputType);
        cmd.Parameters.AddWithValue("$title", (object?)input.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$content", (object?)input.ContentText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$url", (object?)input.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fp", (object?)input.FilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cf", (object?)input.CreatedFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$odid", (object?)input.OriginDeviceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ocv", (object?)input.OriginClientVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        return new InboxItemDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            UserId = input.UserId,
            TopicId = input.TopicId,
            InputType = input.InputType,
            ItemType = input.InputType,
            Title = input.Title,
            ContentText = input.ContentText,
            SourceUrl = input.SourceUrl,
            FilePath = input.FilePath,
            Status = "pending",
            CreatedFrom = input.CreatedFrom ?? "desktop",
            OriginDeviceId = input.OriginDeviceId,
            OriginClientVersion = input.OriginClientVersion,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<InboxItemDto?> GetInboxItemAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, user_id, topic_id, input_type, item_type, title, content, source_url,
                   file_path, status, suggested_topic_id, suggested_title, suggested_tags, created_from,
                   origin_device_id, origin_client_version, source_id, error_code, error_message, retry_count,
                   created_at, updated_at, imported_at, archived_at
            FROM inbox_items WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var item = ReadInboxItem(r);
        // Load attachments for this inbox item
        item.Attachments = await ListInboxAttachmentsAsync(id, ct);
        return item;
    }

    public async Task<List<InboxItemDto>> ListInboxItemsAsync(
        string workspaceId,
        string? status = null,
        string? inputType = null,
        string? topicId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        var sql = @"
            SELECT id, workspace_id, user_id, topic_id, input_type, item_type, title, content, source_url,
                   file_path, status, suggested_topic_id, suggested_title, suggested_tags, created_from,
                   origin_device_id, origin_client_version, source_id, error_code, error_message, retry_count,
                   created_at, updated_at, imported_at, archived_at
            FROM inbox_items WHERE workspace_id = $ws";
        cmd.Parameters.AddWithValue("$ws", workspaceId);

        if (!string.IsNullOrEmpty(status))
        {
            sql += " AND status = $st";
            cmd.Parameters.AddWithValue("$st", status);
        }
        if (!string.IsNullOrEmpty(inputType))
        {
            sql += " AND input_type = $it";
            cmd.Parameters.AddWithValue("$it", inputType);
        }
        if (!string.IsNullOrEmpty(topicId))
        {
            sql += " AND topic_id = $tid";
            cmd.Parameters.AddWithValue("$tid", topicId);
        }

        sql += " ORDER BY created_at DESC LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        cmd.CommandText = sql;
        var results = new List<InboxItemDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(ReadInboxItem(r));
        }
        return results;
    }

    public async Task<List<InboxItemDto>> ListMobileCaptureItemsAsync(
        string workspaceId,
        string clientId,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, user_id, topic_id, input_type, item_type, title, content, source_url,
                   file_path, status, suggested_topic_id, suggested_title, suggested_tags, created_from,
                   origin_device_id, origin_client_version, source_id, error_code, error_message, retry_count,
                   created_at, updated_at, imported_at, archived_at
            FROM inbox_items
            WHERE workspace_id = $ws
              AND created_from = 'mobile'
              AND origin_device_id = $clientId
            ORDER BY created_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$clientId", clientId);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));

        var results = new List<InboxItemDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(ReadInboxItem(r));
        }
        return results;
    }

    public async Task UpdateInboxItemAsync(string id, UpdateInboxItemInput input, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE inbox_items SET
                title = $title,
                content = $content,
                content_text = $content,
                topic_id = $tid,
                suggested_topic_id = $stid,
                suggested_title = $stitle,
                suggested_tags = $stags,
                updated_at = $now
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", (object?)input.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$content", (object?)input.ContentText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tid", (object?)input.TopicId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$stid", (object?)input.SuggestedTopicId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$stitle", (object?)input.SuggestedTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$stags", (object?)input.SuggestedTags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateInboxItemStatusAsync(string id, string status, string? errorMessage = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE inbox_items SET status = $st, error_message = $err, updated_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$err", (object?)errorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetInboxItemImportedAsync(string inboxItemId, string sourceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE inbox_items SET
                source_id = $sid,
                status = 'imported',
                imported_at = $now,
                updated_at = $now
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", inboxItemId);
        cmd.Parameters.AddWithValue("$sid", sourceId);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IncrementRetryCountAsync(string inboxItemId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE inbox_items SET
                retry_count = retry_count + 1,
                updated_at = $now
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", inboxItemId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ArchiveInboxItemAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE inbox_items SET status = 'archived', archived_at = $now, updated_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteInboxItemAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM inbox_items WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountInboxItemsAsync(string workspaceId, string? status = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM inbox_items WHERE workspace_id = $ws";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (!string.IsNullOrEmpty(status))
        {
            cmd.CommandText += " AND status = $st";
            cmd.Parameters.AddWithValue("$st", status);
        }
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> IsDuplicateUrlAsync(string workspaceId, string url, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM (
                SELECT 1 FROM sources WHERE workspace_id = $ws AND url = $url
                UNION ALL
                SELECT 1 FROM inbox_items WHERE workspace_id = $ws AND source_url = $url
            )";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$url", url);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return count > 0;
    }

    public async Task<bool> IsDuplicateContentAsync(string workspaceId, string contentHash, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sources WHERE workspace_id = $ws AND content_hash = $hash";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$hash", contentHash);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return count > 0;
    }

    // ===== Inbox Attachments (§7.2) =====

    public async Task<InboxAttachmentDto> CreateInboxAttachmentAsync(CreateInboxAttachmentInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO inbox_attachments (id, workspace_id, inbox_item_id, file_id, role, filename, mime_type, size_bytes, created_at)
            VALUES ($id, $ws, $iid, $fid, $role, $fn, $mt, $sz, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$iid", input.InboxItemId);
        cmd.Parameters.AddWithValue("$fid", input.FileId);
        cmd.Parameters.AddWithValue("$role", input.Role);
        cmd.Parameters.AddWithValue("$fn", input.Filename);
        cmd.Parameters.AddWithValue("$mt", input.MimeType);
        cmd.Parameters.AddWithValue("$sz", input.SizeBytes);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);

        return new InboxAttachmentDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            InboxItemId = input.InboxItemId,
            FileId = input.FileId,
            Role = input.Role,
            Filename = input.Filename,
            MimeType = input.MimeType,
            SizeBytes = input.SizeBytes,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<List<InboxAttachmentDto>> ListInboxAttachmentsAsync(string inboxItemId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, inbox_item_id, file_id, role, filename, mime_type, size_bytes, created_at
            FROM inbox_attachments WHERE inbox_item_id = $iid ORDER BY created_at ASC";
        cmd.Parameters.AddWithValue("$iid", inboxItemId);
        var results = new List<InboxAttachmentDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(new InboxAttachmentDto
            {
                Id = r.GetString(0),
                WorkspaceId = r.GetString(1),
                InboxItemId = r.GetString(2),
                FileId = r.GetString(3),
                Role = r.GetString(4),
                Filename = r.GetString(5),
                MimeType = r.GetString(6),
                SizeBytes = r.GetInt64(7),
                CreatedAt = DateTime.Parse(r.GetString(8))
            });
        }
        return results;
    }

    // ===== File Objects (§7.3) =====

    public async Task<FileObjectDto> CreateFileObjectAsync(CreateFileObjectInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO file_objects (id, workspace_id, storage_provider, bucket, object_key, local_path,
                original_filename, mime_type, extension, size_bytes, sha256, created_at)
            VALUES ($id, $ws, $sp, $bk, $ok, $lp, $fn, $mt, $ext, $sz, $sha, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$sp", input.StorageProvider);
        cmd.Parameters.AddWithValue("$bk", (object?)input.Bucket ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ok", (object?)input.ObjectKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lp", (object?)input.LocalPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fn", input.OriginalFilename);
        cmd.Parameters.AddWithValue("$mt", input.MimeType);
        cmd.Parameters.AddWithValue("$ext", (object?)input.Extension ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sz", input.SizeBytes);
        cmd.Parameters.AddWithValue("$sha", (object?)input.Sha256 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);

        return new FileObjectDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            StorageProvider = input.StorageProvider,
            Bucket = input.Bucket,
            ObjectKey = input.ObjectKey,
            LocalPath = input.LocalPath,
            OriginalFilename = input.OriginalFilename,
            MimeType = input.MimeType,
            Extension = input.Extension,
            SizeBytes = input.SizeBytes,
            Sha256 = input.Sha256,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<FileObjectDto?> GetFileObjectAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, storage_provider, bucket, object_key, local_path,
                   original_filename, mime_type, extension, size_bytes, sha256, created_at
            FROM file_objects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadFileObject(r);
    }

    // ===== Import Jobs (§7.5) =====

    public async Task<ImportJobDto> CreateImportJobAsync(CreateImportJobInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO import_jobs (id, workspace_id, inbox_item_id, job_type, status, attempt, max_attempts,
                started_at, created_at, updated_at)
            VALUES ($id, $ws, $iid, $jt, 'running', 1, 3, $started, $now, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$iid", input.InboxItemId);
        cmd.Parameters.AddWithValue("$jt", input.JobType);
        cmd.Parameters.AddWithValue("$started", nowStr);
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        return new ImportJobDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            InboxItemId = input.InboxItemId,
            JobType = input.JobType,
            Status = "running",
            Attempt = 1,
            MaxAttempts = 3,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<ImportJobDto?> GetImportJobAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, inbox_item_id, source_id, job_type, status, attempt, max_attempts,
                   started_at, finished_at, error_code, error_message, created_at, updated_at
            FROM import_jobs WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadImportJob(r);
    }

    public async Task<List<ImportJobDto>> ListImportJobsAsync(string workspaceId, string? status = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, inbox_item_id, source_id, job_type, status, attempt, max_attempts,
                   started_at, finished_at, error_code, error_message, created_at, updated_at
            FROM import_jobs WHERE workspace_id = $ws";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (!string.IsNullOrEmpty(status))
        {
            cmd.CommandText += " AND status = $st";
            cmd.Parameters.AddWithValue("$st", status);
        }
        cmd.CommandText += " ORDER BY created_at DESC";
        var results = new List<ImportJobDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadImportJob(r));
        return results;
    }

    public async Task UpdateImportJobAsync(string id, string status, string? sourceId = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        var cmd = conn.CreateCommand();

        // Build the UPDATE dynamically based on which fields are provided
        var setParts = new List<string> { "status = $st", "updated_at = $now" };
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$now", now);

        if (sourceId != null)
        {
            setParts.Add("source_id = $sid");
            cmd.Parameters.AddWithValue("$sid", sourceId);
        }
        if (errorCode != null)
        {
            setParts.Add("error_code = $ec");
            cmd.Parameters.AddWithValue("$ec", errorCode);
        }
        if (errorMessage != null)
        {
            setParts.Add("error_message = $em");
            cmd.Parameters.AddWithValue("$em", errorMessage);
        }
        // Set finished_at when status is terminal (succeeded or failed)
        if (status == "succeeded" || status == "failed")
        {
            setParts.Add("finished_at = $fin");
            cmd.Parameters.AddWithValue("$fin", now);
        }

        cmd.CommandText = $"UPDATE import_jobs SET {string.Join(", ", setParts)} WHERE id = $id";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Inbox Events (§7.6) =====

    public async Task CreateInboxEventAsync(string workspaceId, string inboxItemId, string eventType, string? payload = null, string? createdBy = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO inbox_events (id, workspace_id, inbox_item_id, event_type, event_payload, created_by, created_at)
            VALUES ($id, $ws, $iid, $et, $payload, $cb, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$iid", inboxItemId);
        cmd.Parameters.AddWithValue("$et", eventType);
        cmd.Parameters.AddWithValue("$payload", (object?)payload ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cb", (object?)createdBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<InboxEventDto>> ListInboxEventsAsync(string inboxItemId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, inbox_item_id, event_type, event_payload, created_by, created_at
            FROM inbox_events WHERE inbox_item_id = $iid ORDER BY created_at ASC";
        cmd.Parameters.AddWithValue("$iid", inboxItemId);
        var results = new List<InboxEventDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(new InboxEventDto
            {
                Id = r.GetString(0),
                WorkspaceId = r.GetString(1),
                InboxItemId = r.GetString(2),
                EventType = r.GetString(3),
                EventPayload = r.IsDBNull(4) ? null : r.GetString(4),
                CreatedBy = r.IsDBNull(5) ? null : r.GetString(5),
                CreatedAt = DateTime.Parse(r.GetString(6))
            });
        }
        return results;
    }

    // ===== Sync Cursors (§7.7) =====

    public async Task<SyncCursorDto?> GetSyncCursorAsync(string workspaceId, string cursorType, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, remote_workspace_id, cursor_type, cursor_value, last_synced_at, created_at, updated_at
            FROM sync_cursors WHERE workspace_id = $ws AND cursor_type = $ct";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$ct", cursorType);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new SyncCursorDto
        {
            Id = r.GetString(0),
            WorkspaceId = r.GetString(1),
            RemoteWorkspaceId = r.GetString(2),
            CursorType = r.GetString(3),
            CursorValue = r.IsDBNull(4) ? null : r.GetString(4),
            LastSyncedAt = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)),
            CreatedAt = DateTime.Parse(r.GetString(6)),
            UpdatedAt = DateTime.Parse(r.GetString(7))
        };
    }

    public async Task UpdateSyncCursorAsync(string workspaceId, string cursorType, string cursorValue, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        var cmd = conn.CreateCommand();
        // UPSERT: try insert, on conflict update
        cmd.CommandText = @"
            INSERT INTO sync_cursors (id, workspace_id, remote_workspace_id, cursor_type, cursor_value, last_synced_at, created_at, updated_at)
            VALUES ($id, $ws, $rws, $ct, $cv, $now, $now, $now)
            ON CONFLICT(workspace_id, remote_workspace_id, cursor_type) DO UPDATE SET
                cursor_value = $cv, last_synced_at = $now, updated_at = $now";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$rws", workspaceId); // remote_workspace_id defaults to workspace_id for local mode
        cmd.Parameters.AddWithValue("$ct", cursorType);
        cmd.Parameters.AddWithValue("$cv", cursorValue);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Cloud Inbox Sync Logs =====

    public async Task<CloudInboxSyncLogDto> CreateCloudInboxSyncLogAsync(CreateCloudInboxSyncLogInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow;
        var durationMs = Math.Max(0, (long)(input.FinishedAt - input.StartedAt).TotalMilliseconds);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureCloudInboxSyncLogTableAsync(conn, ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cloud_inbox_sync_logs (
                id, workspace_id, direction, status, cloud_api_base_url, cloud_workspace_id,
                retention, pulled_count, failed_count, next_cursor, error_message,
                started_at, finished_at, duration_ms, created_at
            ) VALUES (
                $id, $ws, $dir, $status, $baseUrl, $cloudWs,
                $retention, $pulled, $failed, $cursor, $error,
                $started, $finished, $duration, $created
            )";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$dir", input.Direction);
        cmd.Parameters.AddWithValue("$status", input.Status);
        cmd.Parameters.AddWithValue("$baseUrl", (object?)input.CloudApiBaseUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cloudWs", (object?)input.CloudWorkspaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$retention", input.Retention);
        cmd.Parameters.AddWithValue("$pulled", input.PulledCount);
        cmd.Parameters.AddWithValue("$failed", input.FailedCount);
        cmd.Parameters.AddWithValue("$cursor", (object?)input.NextCursor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)input.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$started", input.StartedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$finished", input.FinishedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$duration", durationMs);
        cmd.Parameters.AddWithValue("$created", createdAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);

        return new CloudInboxSyncLogDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            Direction = input.Direction,
            Status = input.Status,
            CloudApiBaseUrl = input.CloudApiBaseUrl,
            CloudWorkspaceId = input.CloudWorkspaceId,
            Retention = input.Retention,
            PulledCount = input.PulledCount,
            FailedCount = input.FailedCount,
            NextCursor = input.NextCursor,
            ErrorMessage = input.ErrorMessage,
            StartedAt = input.StartedAt,
            FinishedAt = input.FinishedAt,
            DurationMs = durationMs,
            CreatedAt = createdAt
        };
    }

    public async Task<List<CloudInboxSyncLogDto>> ListCloudInboxSyncLogsAsync(string workspaceId, int limit = 10, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureCloudInboxSyncLogTableAsync(conn, ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, direction, status, cloud_api_base_url, cloud_workspace_id,
                   retention, pulled_count, failed_count, next_cursor, error_message,
                   started_at, finished_at, duration_ms, created_at
            FROM cloud_inbox_sync_logs
            WHERE workspace_id = $ws
            ORDER BY created_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));

        var results = new List<CloudInboxSyncLogDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(ReadCloudInboxSyncLog(r));
        }
        return results;
    }

    // ===== Mobile Devices =====

    public async Task<MobileDeviceDto> UpsertMobileDeviceAsync(UpsertMobileDeviceInput input, CancellationToken ct = default)
    {
        var clientId = input.ClientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required", nameof(input.ClientId));
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureMobileDevicesTableAsync(conn, ct);

        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO mobile_devices (
                id, workspace_id, client_id, device_name, platform, push_token,
                status, last_seen_at, bound_at, created_at, updated_at
            ) VALUES (
                $id, $ws, $client, $name, $platform, $push,
                'active', $now, $now, $now, $now
            )
            ON CONFLICT(workspace_id, client_id) DO UPDATE SET
                device_name = COALESCE($name, device_name),
                platform = COALESCE($platform, platform),
                push_token = COALESCE($push, push_token),
                status = 'active',
                last_seen_at = $now,
                updated_at = $now";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$client", clientId);
        cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(input.DeviceName) ? (object)DBNull.Value : input.DeviceName.Trim());
        cmd.Parameters.AddWithValue("$platform", string.IsNullOrWhiteSpace(input.Platform) ? (object)DBNull.Value : input.Platform.Trim());
        cmd.Parameters.AddWithValue("$push", string.IsNullOrWhiteSpace(input.PushToken) ? (object)DBNull.Value : input.PushToken.Trim());
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        var select = conn.CreateCommand();
        select.CommandText = @"
            SELECT id, workspace_id, client_id, device_name, platform, push_token,
                   refresh_token_expires_at, status, last_seen_at, bound_at, created_at, updated_at
            FROM mobile_devices
            WHERE workspace_id = $ws AND client_id = $client
            LIMIT 1";
        select.Parameters.AddWithValue("$ws", input.WorkspaceId);
        select.Parameters.AddWithValue("$client", clientId);
        await using var r = await select.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
        {
            return ReadMobileDevice(r);
        }

        throw new InvalidOperationException("Failed to bind mobile device");
    }

    public async Task<List<MobileDeviceDto>> ListMobileDevicesAsync(string workspaceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureMobileDevicesTableAsync(conn, ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, client_id, device_name, platform, push_token,
                   refresh_token_expires_at, status, last_seen_at, bound_at, created_at, updated_at
            FROM mobile_devices
            WHERE workspace_id = $ws
            ORDER BY updated_at DESC";
        cmd.Parameters.AddWithValue("$ws", workspaceId);

        var results = new List<MobileDeviceDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(ReadMobileDevice(r));
        }
        return results;
    }

    public async Task<MobileDeviceDto> UpdateMobileDeviceRefreshTokenAsync(
        string workspaceId,
        string clientId,
        string refreshTokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureMobileDevicesTableAsync(conn, ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE mobile_devices
            SET refresh_token_hash = $hash,
                refresh_token_expires_at = $expires,
                status = 'active',
                last_seen_at = $now,
                updated_at = $now
            WHERE workspace_id = $ws AND client_id = $client";
        cmd.Parameters.AddWithValue("$hash", refreshTokenHash);
        cmd.Parameters.AddWithValue("$expires", expiresAt.ToString("o"));
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$client", clientId.Trim());
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0)
        {
            throw new InvalidOperationException("Mobile device not found");
        }

        return await GetMobileDeviceAsync(workspaceId, clientId, ct)
            ?? throw new InvalidOperationException("Mobile device not found");
    }

    public async Task<MobileDeviceDto?> GetMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureMobileDevicesTableAsync(conn, ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, client_id, device_name, platform, push_token,
                   refresh_token_expires_at, status, last_seen_at, bound_at, created_at, updated_at
            FROM mobile_devices
            WHERE workspace_id = $ws AND client_id = $client
            LIMIT 1";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$client", clientId.Trim());

        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadMobileDevice(r) : null;
    }

    public async Task<MobileDeviceDto?> GetMobileDeviceByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenHash)) return null;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureMobileDevicesTableAsync(conn, ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, client_id, device_name, platform, push_token,
                   refresh_token_expires_at, status, last_seen_at, bound_at, created_at, updated_at
            FROM mobile_devices
            WHERE refresh_token_hash = $hash
            LIMIT 1";
        cmd.Parameters.AddWithValue("$hash", refreshTokenHash);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadMobileDevice(r) : null;
    }

    public async Task DeactivateMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureMobileDevicesTableAsync(conn, ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE mobile_devices
            SET status = 'revoked',
                refresh_token_hash = NULL,
                refresh_token_expires_at = NULL,
                updated_at = $now
            WHERE workspace_id = $ws AND client_id = $client";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        cmd.Parameters.AddWithValue("$client", clientId.Trim());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Push Notifications =====

    public async Task<PushNotificationDto> CreatePushNotificationAsync(CreatePushNotificationInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsurePushNotificationsTableAsync(conn, ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO push_notifications (
                id, workspace_id, client_id, push_token, title, body, data_json,
                status, attempt, max_attempts, next_attempt_at, created_at, updated_at
            ) VALUES (
                $id, $ws, $client, $token, $title, $body, $data,
                'pending', 0, $max, $now, $now, $now
            )";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$client", input.ClientId);
        cmd.Parameters.AddWithValue("$token", input.PushToken);
        cmd.Parameters.AddWithValue("$title", input.Title);
        cmd.Parameters.AddWithValue("$body", input.Body);
        cmd.Parameters.AddWithValue("$data", (object?)input.DataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$max", Math.Clamp(input.MaxAttempts, 1, 10));
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        return new PushNotificationDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            ClientId = input.ClientId,
            PushToken = input.PushToken,
            Title = input.Title,
            Body = input.Body,
            DataJson = input.DataJson,
            Status = "pending",
            Attempt = 0,
            MaxAttempts = Math.Clamp(input.MaxAttempts, 1, 10),
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<List<PushNotificationDto>> ListPendingPushNotificationsAsync(int limit = 20, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsurePushNotificationsTableAsync(conn, ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, client_id, push_token, title, body, data_json,
                   status, attempt, max_attempts, provider_response, error_message,
                   next_attempt_at, sent_at, created_at, updated_at
            FROM push_notifications
            WHERE status = 'pending' AND (next_attempt_at IS NULL OR next_attempt_at <= $now)
            ORDER BY created_at
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));

        var results = new List<PushNotificationDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(ReadPushNotification(r));
        }
        return results;
    }

    public async Task<List<PushNotificationDto>> ListPushNotificationsAsync(string workspaceId, string? status = null, int limit = 50, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsurePushNotificationsTableAsync(conn, ct);

        var hasStatus = !string.IsNullOrWhiteSpace(status);
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT id, workspace_id, client_id, push_token, title, body, data_json,
                   status, attempt, max_attempts, provider_response, error_message,
                   next_attempt_at, sent_at, created_at, updated_at
            FROM push_notifications
            WHERE workspace_id = $ws {(hasStatus ? "AND status = $status" : "")}
            ORDER BY created_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (hasStatus)
        {
            cmd.Parameters.AddWithValue("$status", status!.Trim());
        }
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));

        var results = new List<PushNotificationDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            results.Add(ReadPushNotification(r));
        }
        return results;
    }

    public async Task MarkPushNotificationSentAsync(string id, string? providerResponse = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsurePushNotificationsTableAsync(conn, ct);

        var now = DateTime.UtcNow.ToString("o");
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE push_notifications
            SET status = 'sent',
                provider_response = $response,
                sent_at = $now,
                updated_at = $now
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$response", (object?)providerResponse ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkPushNotificationFailedAsync(string id, string errorMessage, DateTime? nextAttemptAt, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsurePushNotificationsTableAsync(conn, ct);

        var existing = conn.CreateCommand();
        existing.CommandText = "SELECT attempt, max_attempts FROM push_notifications WHERE id = $id";
        existing.Parameters.AddWithValue("$id", id);
        var attempt = 0;
        var maxAttempts = 3;
        await using (var r = await existing.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                attempt = r.GetInt32(0);
                maxAttempts = r.GetInt32(1);
            }
        }

        var nextAttempt = attempt + 1;
        var status = nextAttemptAt == null || nextAttempt >= maxAttempts ? "failed" : "pending";
        var now = DateTime.UtcNow.ToString("o");
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE push_notifications
            SET status = $status,
                attempt = $attempt,
                error_message = $error,
                next_attempt_at = $next,
                updated_at = $now
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$attempt", nextAttempt);
        cmd.Parameters.AddWithValue("$error", errorMessage);
        cmd.Parameters.AddWithValue("$next", status == "pending" && nextAttemptAt != null ? nextAttemptAt.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Sources =====

    public async Task<SourceDto> CreateSourceAsync(CreateSourceInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");

        // Extract domain from URL if available
        var domain = "";
        if (!string.IsNullOrEmpty(input.Url) && Uri.TryCreate(input.Url, UriKind.Absolute, out var uri))
        {
            domain = uri.Host;
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sources (id, workspace_id, topic_id, inbox_item_id, source_type, title, url, domain,
                author, local_file_path, raw_text, content_hash, status, created_at, updated_at)
            VALUES ($id, $ws, $tid, $iid, $st, $title, $url, $dom, $author, $fp, $raw, $hash, 'pending', $now, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$tid", (object?)input.TopicId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iid", (object?)input.InboxItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$st", input.SourceType);
        cmd.Parameters.AddWithValue("$title", (object?)input.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$url", (object?)input.Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dom", domain);
        cmd.Parameters.AddWithValue("$author", (object?)input.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fp", (object?)input.LocalFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$raw", (object?)input.RawText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", (object?)input.ContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        return new SourceDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            TopicId = input.TopicId,
            InboxItemId = input.InboxItemId,
            SourceType = input.SourceType,
            Title = input.Title,
            Url = input.Url,
            Domain = domain,
            Author = input.Author,
            LocalFilePath = input.LocalFilePath,
            ContentHash = input.ContentHash,
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<SourceDto?> GetSourceAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, workspace_id, topic_id, source_type, title, url, domain, local_file_path, content_hash, status, created_at, updated_at FROM sources WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadSource(r);
    }

    public async Task<List<SourceDto>> ListSourcesAsync(string workspaceId, string? topicId = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, workspace_id, topic_id, source_type, title, url, domain, local_file_path, content_hash, status, created_at, updated_at FROM sources WHERE workspace_id = $ws";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (!string.IsNullOrEmpty(topicId))
        {
            cmd.CommandText += " AND topic_id = $tid";
            cmd.Parameters.AddWithValue("$tid", topicId);
        }
        cmd.CommandText += " ORDER BY created_at DESC";
        var results = new List<SourceDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadSource(r));
        return results;
    }

    // ===== Documents =====

    public async Task<DocumentDto> CreateDocumentAsync(CreateDocumentInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO documents (id, workspace_id, topic_id, source_id, title, title_original, content_markdown, content_text, summary, ai_status, created_at, updated_at)
            VALUES ($id, $ws, $tid, $sid, $title, $title, $md, $txt, $sum, 'pending', $now, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$tid", (object?)input.TopicId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sid", (object?)input.SourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", input.Title);
        cmd.Parameters.AddWithValue("$md", (object?)input.ContentMarkdown ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$txt", (object?)input.ContentText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sum", DBNull.Value);
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        return new DocumentDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            TopicId = input.TopicId,
            SourceId = input.SourceId,
            Title = input.Title,
            ContentMarkdown = input.ContentMarkdown,
            ContentText = input.ContentText,
            AiStatus = "pending",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<DocumentDto?> GetDocumentAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, workspace_id, topic_id, source_id, title, content_markdown, content_text, summary, ai_status, created_at, updated_at, title_original, title_zh, primary_language, language_distribution, is_multilingual, localization_strategy, localization_level, language_detect_status, localization_status, content_hash FROM documents WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadDocument(r);
    }

    public async Task<List<DocumentDto>> ListDocumentsAsync(string workspaceId, string? topicId = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, workspace_id, topic_id, source_id, title, content_markdown, content_text, summary, ai_status, created_at, updated_at, title_original, title_zh, primary_language, language_distribution, is_multilingual, localization_strategy, localization_level, language_detect_status, localization_status, content_hash FROM documents WHERE workspace_id = $ws";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (!string.IsNullOrEmpty(topicId))
        {
            cmd.CommandText += " AND topic_id = $tid";
            cmd.Parameters.AddWithValue("$tid", topicId);
        }
        cmd.CommandText += " ORDER BY created_at DESC";
        var results = new List<DocumentDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadDocument(r));
        return results;
    }

    // ===== Document Chunks =====

    public async Task SaveChunksAsync(string documentId, List<ChunkDto> chunks, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) Remove existing chunks for this document (idempotent re-save)
        var delCmd = conn.CreateCommand();
        delCmd.Transaction = (SqliteTransaction)tx;
        delCmd.CommandText = "DELETE FROM document_chunks WHERE document_id = $did";
        delCmd.Parameters.AddWithValue("$did", documentId);
        await delCmd.ExecuteNonQueryAsync(ct);

        // 2) Insert new chunks
        foreach (var chunk in chunks)
        {
            var insCmd = conn.CreateCommand();
            insCmd.Transaction = (SqliteTransaction)tx;
            insCmd.CommandText = @"INSERT INTO document_chunks
                (id, document_id, chunk_index, chunk_title, content, content_original, content_normalized,
                 detected_language, language_confidence, language_distribution, content_type, processing_route,
                 localization_required, chunk_group_id, parent_chunk_id, token_count, char_count)
                VALUES ($id, $did, $idx, $title, $content, $original, $normalized, $language, $confidence,
                        $distribution, $contentType, $route, $localizationRequired, $groupId, $parentId, $tok, $char)";
            var chunkId = string.IsNullOrEmpty(chunk.Id) ? Guid.NewGuid().ToString() : chunk.Id;
            insCmd.Parameters.AddWithValue("$id", chunkId);
            insCmd.Parameters.AddWithValue("$did", documentId);
            insCmd.Parameters.AddWithValue("$idx", chunk.ChunkIndex);
            insCmd.Parameters.AddWithValue("$title", (object?)chunk.ChunkTitle ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("$content", chunk.Content ?? string.Empty);
            insCmd.Parameters.AddWithValue("$original", string.IsNullOrEmpty(chunk.ContentOriginal) ? chunk.Content ?? string.Empty : chunk.ContentOriginal);
            insCmd.Parameters.AddWithValue("$normalized", (object?)chunk.ContentNormalized ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("$language", (object?)chunk.DetectedLanguage ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("$confidence", (object?)chunk.LanguageConfidence ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("$distribution", (object?)chunk.LanguageDistribution ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("$contentType", chunk.ContentType);
            insCmd.Parameters.AddWithValue("$route", chunk.ProcessingRoute);
            insCmd.Parameters.AddWithValue("$localizationRequired", chunk.LocalizationRequired ? 1 : 0);
            insCmd.Parameters.AddWithValue("$groupId", (object?)chunk.ChunkGroupId ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("$parentId", (object?)chunk.ParentChunkId ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("$tok", chunk.TokenCount);
            insCmd.Parameters.AddWithValue("$char", chunk.CharCount);
            await insCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    // ===== Search =====

    public async Task<List<SearchResultDto>> SearchDocumentsAsync(string workspaceId, string query, int limit = 20, CancellationToken ct = default)
    {
        var results = new List<SearchResultDto>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        var pattern = $"%{query}%";
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // 1) Search documents table (title + content_text)
        var docCmd = conn.CreateCommand();
        docCmd.CommandText = @"
            SELECT id, title, content_text
            FROM documents
            WHERE workspace_id = $ws
              AND (title LIKE $q OR content_text LIKE $q)";
        docCmd.Parameters.AddWithValue("$ws", workspaceId);
        docCmd.Parameters.AddWithValue("$q", pattern);
        await using (var r = await docCmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var docId = r.GetString(0);
                var title = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                var content = r.IsDBNull(2) ? null : r.GetString(2);

                double score = 0;
                if (!string.IsNullOrEmpty(title) && title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 2.0;
                if (!string.IsNullOrEmpty(content) && content.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 1.0;

                results.Add(new SearchResultDto
                {
                    DocumentId = docId,
                    ChunkId = null,
                    Title = title,
                    ContentSnippet = BuildSnippet(content, query),
                    Score = score,
                    SourceUrl = null
                });
            }
        }

        // 2) Search document_chunks table (content)
        var chunkCmd = conn.CreateCommand();
        chunkCmd.CommandText = @"
            SELECT c.id, c.document_id, c.content, d.title, s.url
            FROM document_chunks c
            INNER JOIN documents d ON d.id = c.document_id
            LEFT JOIN sources s ON s.id = d.source_id
            WHERE d.workspace_id = $ws
              AND c.content LIKE $q";
        chunkCmd.Parameters.AddWithValue("$ws", workspaceId);
        chunkCmd.Parameters.AddWithValue("$q", pattern);
        await using (var r = await chunkCmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var chunkId = r.GetString(0);
                var docId = r.GetString(1);
                var content = r.GetString(2);
                var title = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                var sourceUrl = r.IsDBNull(4) ? null : r.GetString(4);

                results.Add(new SearchResultDto
                {
                    DocumentId = docId,
                    ChunkId = chunkId,
                    Title = title,
                    ContentSnippet = BuildSnippet(content, query),
                    Score = 0.5,
                    SourceUrl = sourceUrl
                });
            }
        }

        return results
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();
    }

    // ===== Settings =====

    public async Task<string?> GetSettingAsync(string workspaceId, string key, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", $"{workspaceId}:{key}");
        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString();
    }

    public async Task SetSettingAsync(string workspaceId, string key, string value, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO settings (key, value, updated_at) VALUES ($key, $val, $now)
            ON CONFLICT(key) DO UPDATE SET value = $val, updated_at = $now";
        cmd.Parameters.AddWithValue("$key", $"{workspaceId}:{key}");
        cmd.Parameters.AddWithValue("$val", value);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Tags (Phase 4) =====

    public async Task<TagDto> CreateTagAsync(CreateTagInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");
        var normalizedName = (input.NormalizedName ?? input.Name).ToLower().Trim();
        var displayName = input.DisplayName ?? input.Name;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Check for duplicates by (WorkspaceId, NormalizedName)
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT id FROM tags WHERE workspace_id = $ws AND normalized_name = $norm";
        checkCmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        checkCmd.Parameters.AddWithValue("$norm", normalizedName);
        await using (var r = await checkCmd.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                // Return existing tag
                var existingId = r.GetString(0);
                return await GetTagAsync(existingId, ct) ?? throw new InvalidOperationException("Tag disappeared");
            }
        }

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tags (id, workspace_id, name, normalized_name, display_name, tag_type, description, color, aliases, source, usage_count, is_system, is_archived, created_at, updated_at)
            VALUES ($id, $ws, $name, $norm, $dn, $tt, $desc, $color, $aliases, $src, 0, 0, 0, $now, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$name", input.Name);
        cmd.Parameters.AddWithValue("$norm", normalizedName);
        cmd.Parameters.AddWithValue("$dn", displayName);
        cmd.Parameters.AddWithValue("$tt", input.TagType);
        cmd.Parameters.AddWithValue("$desc", (object?)input.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$color", (object?)input.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$aliases", (object?)input.Aliases ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$src", input.Source);
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        return new TagDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            Name = input.Name,
            NormalizedName = normalizedName,
            DisplayName = displayName,
            TagType = input.TagType,
            Description = input.Description,
            Color = input.Color,
            Aliases = input.Aliases,
            Source = input.Source,
            UsageCount = 0,
            IsSystem = false,
            IsArchived = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<TagDto?> GetTagAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, name, normalized_name, display_name, tag_type, description, color, aliases, source, usage_count, is_system, is_archived, created_at, updated_at
            FROM tags WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadTag(r);
    }

    public async Task<List<TagDto>> ListTagsAsync(string workspaceId, string? tagType = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, name, normalized_name, display_name, tag_type, description, color, aliases, source, usage_count, is_system, is_archived, created_at, updated_at
            FROM tags WHERE workspace_id = $ws AND is_archived = 0";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (!string.IsNullOrEmpty(tagType))
        {
            cmd.CommandText += " AND tag_type = $tt";
            cmd.Parameters.AddWithValue("$tt", tagType);
        }
        cmd.CommandText += " ORDER BY usage_count DESC, created_at DESC LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var results = new List<TagDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadTag(r));
        return results;
    }

    public async Task UpdateTagAsync(string id, UpdateTagInput input, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        var cmd = conn.CreateCommand();

        var setParts = new List<string> { "updated_at = $now" };
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", now);

        if (input.Name != null) { setParts.Add("name = $name"); cmd.Parameters.AddWithValue("$name", input.Name); }
        if (input.DisplayName != null) { setParts.Add("display_name = $dn"); cmd.Parameters.AddWithValue("$dn", input.DisplayName); }
        if (input.TagType != null) { setParts.Add("tag_type = $tt"); cmd.Parameters.AddWithValue("$tt", input.TagType); }
        if (input.Description != null) { setParts.Add("description = $desc"); cmd.Parameters.AddWithValue("$desc", input.Description); }
        if (input.Color != null) { setParts.Add("color = $color"); cmd.Parameters.AddWithValue("$color", input.Color); }
        if (input.Aliases != null) { setParts.Add("aliases = $aliases"); cmd.Parameters.AddWithValue("$aliases", input.Aliases); }
        if (input.IsArchived.HasValue) { setParts.Add("is_archived = $arch"); cmd.Parameters.AddWithValue("$arch", input.IsArchived.Value ? 1 : 0); }

        cmd.CommandText = $"UPDATE tags SET {string.Join(", ", setParts)} WHERE id = $id";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteTagAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tags WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Document Tags (Phase 4) =====

    public async Task<List<DocumentTagDto>> GetDocumentTagsAsync(string documentId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT dt.document_id, dt.tag_id, dt.source, dt.confidence, dt.reason, dt.is_confirmed, dt.confirmed_by, dt.confirmed_at, dt.created_at,
                   t.name, t.tag_type
            FROM document_tags dt
            LEFT JOIN tags t ON t.id = dt.tag_id
            WHERE dt.document_id = $did
            ORDER BY dt.created_at ASC";
        cmd.Parameters.AddWithValue("$did", documentId);
        var results = new List<DocumentTagDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadDocumentTag(r));
        return results;
    }

    public async Task<DocumentTagDto> AddDocumentTagAsync(string documentId, string tagName, string? tagType, string source, decimal? confidence, string? reason, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");
        var normalizedName = tagName.ToLower().Trim();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Resolve workspace_id from the document to scope tag lookups (avoid cross-workspace leak)
        var wsCmd = conn.CreateCommand();
        wsCmd.CommandText = "SELECT workspace_id FROM documents WHERE id = $did";
        wsCmd.Parameters.AddWithValue("$did", documentId);
        var workspaceId = (string?)await wsCmd.ExecuteScalarAsync(ct) ?? "";

        // Find or create tag by (workspace_id, normalized name)
        string tagId;
        var findCmd = conn.CreateCommand();
        findCmd.CommandText = "SELECT id FROM tags WHERE workspace_id = $ws AND normalized_name = $norm LIMIT 1";
        findCmd.Parameters.AddWithValue("$ws", workspaceId);
        findCmd.Parameters.AddWithValue("$norm", normalizedName);
        var existingId = (string?)await findCmd.ExecuteScalarAsync(ct);
        if (existingId != null)
        {
            tagId = existingId;
        }
        else
        {
            tagId = Guid.NewGuid().ToString();
            var createTagCmd = conn.CreateCommand();
            createTagCmd.CommandText = @"
                INSERT INTO tags (id, workspace_id, name, normalized_name, display_name, tag_type, source, usage_count, is_system, is_archived, created_at, updated_at)
                VALUES ($id, $ws, $name, $norm, $name, $tt, $src, 0, 0, 0, $now, $now)";
            createTagCmd.Parameters.AddWithValue("$id", tagId);
            createTagCmd.Parameters.AddWithValue("$ws", workspaceId);
            createTagCmd.Parameters.AddWithValue("$name", tagName);
            createTagCmd.Parameters.AddWithValue("$norm", normalizedName);
            createTagCmd.Parameters.AddWithValue("$tt", tagType ?? "custom");
            createTagCmd.Parameters.AddWithValue("$src", source);
            createTagCmd.Parameters.AddWithValue("$now", nowStr);
            await createTagCmd.ExecuteNonQueryAsync(ct);
        }

        // Check if document-tag link already exists (UPSERT)
        var upsertCmd = conn.CreateCommand();
        upsertCmd.CommandText = @"
            INSERT INTO document_tags (document_id, tag_id, source, confidence, reason, is_confirmed, created_at)
            VALUES ($did, $tid, $src, $conf, $reason, 0, $now)
            ON CONFLICT(document_id, tag_id) DO UPDATE SET
                source = $src, confidence = $conf, reason = $reason";
        upsertCmd.Parameters.AddWithValue("$did", documentId);
        upsertCmd.Parameters.AddWithValue("$tid", tagId);
        upsertCmd.Parameters.AddWithValue("$src", source);
        upsertCmd.Parameters.AddWithValue("$conf", (object?)confidence ?? DBNull.Value);
        upsertCmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        upsertCmd.Parameters.AddWithValue("$now", nowStr);
        await upsertCmd.ExecuteNonQueryAsync(ct);

        // Increment usage count
        var incCmd = conn.CreateCommand();
        incCmd.CommandText = "UPDATE tags SET usage_count = usage_count + 1, updated_at = $now WHERE id = $tid";
        incCmd.Parameters.AddWithValue("$tid", tagId);
        incCmd.Parameters.AddWithValue("$now", nowStr);
        await incCmd.ExecuteNonQueryAsync(ct);

        return new DocumentTagDto
        {
            Id = $"{documentId}:{tagId}",
            DocumentId = documentId,
            TagId = tagId,
            TagName = tagName,
            TagType = tagType ?? "custom",
            Source = source,
            Confidence = confidence,
            Reason = reason,
            IsConfirmed = false,
            ConfirmedBy = null,
            ConfirmedAt = null,
            CreatedAt = now
        };
    }

    public async Task ConfirmDocumentTagAsync(string documentId, string tagId, string? confirmedBy, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var now = DateTime.UtcNow.ToString("o");
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE document_tags SET is_confirmed = 1, confirmed_by = $cb, confirmed_at = $now
            WHERE document_id = $did AND tag_id = $tid";
        cmd.Parameters.AddWithValue("$did", documentId);
        cmd.Parameters.AddWithValue("$tid", tagId);
        cmd.Parameters.AddWithValue("$cb", (object?)confirmedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteDocumentTagAsync(string documentId, string tagId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM document_tags WHERE document_id = $did AND tag_id = $tid";
        cmd.Parameters.AddWithValue("$did", documentId);
        cmd.Parameters.AddWithValue("$tid", tagId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Entities (Phase 4) =====

    public async Task<EntityDto> CreateEntityAsync(CreateEntityInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("o");
        var normalizedName = (input.NormalizedName ?? input.Name).ToLower().Trim();
        var displayName = input.DisplayName ?? input.Name;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Check for duplicates by (WorkspaceId, NormalizedName, EntityType)
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT id FROM entities WHERE workspace_id = $ws AND normalized_name = $norm AND entity_type = $et";
        checkCmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        checkCmd.Parameters.AddWithValue("$norm", normalizedName);
        checkCmd.Parameters.AddWithValue("$et", input.EntityType);
        await using (var r = await checkCmd.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                var existingId = r.GetString(0);
                return await GetEntityAsync(existingId, ct) ?? throw new InvalidOperationException("Entity disappeared");
            }
        }

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO entities (id, workspace_id, name, normalized_name, display_name, entity_type, aliases, description, external_ref, source, usage_count, is_verified, is_archived, created_at, updated_at)
            VALUES ($id, $ws, $name, $norm, $dn, $et, $aliases, $desc, $extref, $src, 0, 0, 0, $now, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$name", input.Name);
        cmd.Parameters.AddWithValue("$norm", normalizedName);
        cmd.Parameters.AddWithValue("$dn", displayName);
        cmd.Parameters.AddWithValue("$et", input.EntityType);
        cmd.Parameters.AddWithValue("$aliases", (object?)input.Aliases ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", (object?)input.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$extref", DBNull.Value);
        cmd.Parameters.AddWithValue("$src", input.Source);
        cmd.Parameters.AddWithValue("$now", nowStr);
        await cmd.ExecuteNonQueryAsync(ct);

        return new EntityDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            Name = input.Name,
            NormalizedName = normalizedName,
            DisplayName = displayName,
            EntityType = input.EntityType,
            Aliases = input.Aliases,
            Description = input.Description,
            ExternalRef = null,
            Source = input.Source,
            UsageCount = 0,
            IsVerified = false,
            IsArchived = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<EntityDto?> GetEntityAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, name, normalized_name, display_name, entity_type, aliases, description, external_ref, source, usage_count, is_verified, is_archived, created_at, updated_at
            FROM entities WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadEntity(r);
    }

    public async Task<List<EntityDto>> ListEntitiesAsync(string workspaceId, string? entityType = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, name, normalized_name, display_name, entity_type, aliases, description, external_ref, source, usage_count, is_verified, is_archived, created_at, updated_at
            FROM entities WHERE workspace_id = $ws AND is_archived = 0";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (!string.IsNullOrEmpty(entityType))
        {
            cmd.CommandText += " AND entity_type = $et";
            cmd.Parameters.AddWithValue("$et", entityType);
        }
        cmd.CommandText += " ORDER BY usage_count DESC, created_at DESC LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var results = new List<EntityDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadEntity(r));
        return results;
    }

    public async Task DeleteEntityAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM entities WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Document Entities (Phase 4) =====

    public async Task<List<DocumentEntityDto>> GetDocumentEntitiesAsync(string documentId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT de.document_id, de.entity_id, de.mention_count, de.first_mention, de.mention_examples, de.importance, de.role, de.sentiment, de.source, de.confidence, de.created_at,
                   e.name, e.entity_type
            FROM document_entities de
            LEFT JOIN entities e ON e.id = de.entity_id
            WHERE de.document_id = $did
            ORDER BY de.created_at ASC";
        cmd.Parameters.AddWithValue("$did", documentId);
        var results = new List<DocumentEntityDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadDocumentEntity(r));
        return results;
    }

    public async Task DeleteDocumentEntityAsync(string documentId, string entityId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM document_entities WHERE document_id = $did AND entity_id = $eid";
        cmd.Parameters.AddWithValue("$did", documentId);
        cmd.Parameters.AddWithValue("$eid", entityId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Chunks (Phase 4) =====

    public async Task<List<ChunkDto>> GetDocumentChunksAsync(string documentId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, document_id, chunk_index, chunk_title, content, token_count, char_count,
                   chunk_uid, heading_path, section_level, content_hash, prev_chunk_id, next_chunk_id, index_status,
                   content_original, content_normalized, detected_language, language_confidence, language_distribution,
                   content_type, processing_route, localization_required, chunk_group_id, parent_chunk_id
            FROM document_chunks WHERE document_id = $did ORDER BY chunk_index ASC";
        cmd.Parameters.AddWithValue("$did", documentId);
        var results = new List<ChunkDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) results.Add(ReadChunk(r));
        return results;
    }

    public async Task<ChunkDto?> GetChunkAsync(string chunkId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, document_id, chunk_index, chunk_title, content, token_count, char_count,
                   chunk_uid, heading_path, section_level, content_hash, prev_chunk_id, next_chunk_id, index_status,
                   content_original, content_normalized, detected_language, language_confidence, language_distribution,
                   content_type, processing_route, localization_required, chunk_group_id, parent_chunk_id
            FROM document_chunks WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", chunkId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadChunk(r);
    }

    public async Task DeleteChunksByDocumentAsync(string documentId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM document_chunks WHERE document_id = $did";
        cmd.Parameters.AddWithValue("$did", documentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Embeddings (Phase 4) =====

    public async Task<ChunkEmbeddingDto?> GetChunkEmbeddingAsync(string chunkId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, chunk_id, provider, model, model_version, dimension,
                   status, error_message, retry_count, chunk_content_hash, created_at, updated_at
            FROM chunk_embeddings
            WHERE chunk_id = $cid
            ORDER BY updated_at DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("$cid", chunkId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadChunkEmbedding(r);
    }

    public async Task SaveChunkEmbeddingAsync(SaveChunkEmbeddingInput input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("o");
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Check for an existing row by (chunk_id, provider, model) and update, otherwise insert.
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = @"
            SELECT id FROM chunk_embeddings
            WHERE chunk_id = $cid AND provider = $prov AND model = $model
            LIMIT 1";
        checkCmd.Parameters.AddWithValue("$cid", input.ChunkId);
        checkCmd.Parameters.AddWithValue("$prov", input.Provider);
        checkCmd.Parameters.AddWithValue("$model", input.Model);
        var existing = await checkCmd.ExecuteScalarAsync(ct);
        var id = existing?.ToString();

        if (!string.IsNullOrEmpty(id))
        {
            var upd = conn.CreateCommand();
            upd.CommandText = @"
                UPDATE chunk_embeddings
                SET workspace_id = $ws,
                    model_version = $mv,
                    dimension = $dim,
                    embedding_json = $ej,
                    chunk_content_hash = $hash,
                    status = $status,
                    error_message = NULL,
                    updated_at = $now
                WHERE id = $id";
            upd.Parameters.AddWithValue("$id", id);
            upd.Parameters.AddWithValue("$ws", input.WorkspaceId);
            upd.Parameters.AddWithValue("$mv", (object?)input.ModelVersion ?? DBNull.Value);
            upd.Parameters.AddWithValue("$dim", input.Dimension);
            upd.Parameters.AddWithValue("$ej", (object?)input.EmbeddingJson ?? DBNull.Value);
            upd.Parameters.AddWithValue("$hash", input.ChunkContentHash ?? string.Empty);
            upd.Parameters.AddWithValue("$status", input.Status);
            upd.Parameters.AddWithValue("$now", now);
            await upd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            id = Guid.NewGuid().ToString();
            var ins = conn.CreateCommand();
            ins.CommandText = @"
                INSERT INTO chunk_embeddings
                    (id, workspace_id, chunk_id, provider, model, model_version, dimension,
                     embedding_json, vector_ref, chunk_content_hash, status, error_message,
                     retry_count, created_at, updated_at)
                VALUES
                    ($id, $ws, $cid, $prov, $model, $mv, $dim,
                     $ej, NULL, $hash, $status, NULL, 0, $now, $now)";
            ins.Parameters.AddWithValue("$id", id);
            ins.Parameters.AddWithValue("$ws", input.WorkspaceId);
            ins.Parameters.AddWithValue("$cid", input.ChunkId);
            ins.Parameters.AddWithValue("$prov", input.Provider);
            ins.Parameters.AddWithValue("$model", input.Model);
            ins.Parameters.AddWithValue("$mv", (object?)input.ModelVersion ?? DBNull.Value);
            ins.Parameters.AddWithValue("$dim", input.Dimension);
            ins.Parameters.AddWithValue("$ej", (object?)input.EmbeddingJson ?? DBNull.Value);
            ins.Parameters.AddWithValue("$hash", input.ChunkContentHash ?? string.Empty);
            ins.Parameters.AddWithValue("$status", input.Status);
            ins.Parameters.AddWithValue("$now", now);
            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task MarkEmbeddingsStaleAsync(string workspaceId, string? model = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("o");
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE chunk_embeddings SET status = 'stale', updated_at = $now WHERE workspace_id = $ws";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        if (!string.IsNullOrEmpty(model))
        {
            cmd.CommandText += " AND model = $model";
            cmd.Parameters.AddWithValue("$model", model);
        }
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ===== Vector Index State (Phase 4) =====

    public async Task<VectorIndexStateDto?> GetVectorIndexStateAsync(string workspaceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, provider, model, dimension, index_backend,
                   total_chunks, indexed_chunks, failed_chunks, stale_chunks, status,
                   last_rebuilt_at, schema_version, created_at, updated_at
            FROM vector_index_states
            WHERE workspace_id = $ws
            LIMIT 1";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadVectorIndexState(r);
    }

    public async Task UpdateVectorIndexStateAsync(string workspaceId, UpdateVectorIndexStateInput input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("o");
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // One state row per workspace (workspace_id is UNIQUE). Update if it exists, otherwise insert.
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT id FROM vector_index_states WHERE workspace_id = $ws LIMIT 1";
        checkCmd.Parameters.AddWithValue("$ws", workspaceId);
        var existing = await checkCmd.ExecuteScalarAsync(ct);
        var id = existing?.ToString();

        if (!string.IsNullOrEmpty(id))
        {
            // Only update the non-null fields provided in the input.
            var setParts = new List<string> { "updated_at = $now" };
            var upd = conn.CreateCommand();
            upd.Parameters.AddWithValue("$id", id);
            upd.Parameters.AddWithValue("$now", now);
            if (input.Provider != null) { setParts.Add("provider = $prov"); upd.Parameters.AddWithValue("$prov", input.Provider); }
            if (input.Model != null) { setParts.Add("model = $model"); upd.Parameters.AddWithValue("$model", input.Model); }
            if (input.Dimension.HasValue) { setParts.Add("dimension = $dim"); upd.Parameters.AddWithValue("$dim", input.Dimension.Value); }
            if (input.IndexBackend != null) { setParts.Add("index_backend = $ib"); upd.Parameters.AddWithValue("$ib", input.IndexBackend); }
            if (input.TotalChunks.HasValue) { setParts.Add("total_chunks = $tc"); upd.Parameters.AddWithValue("$tc", input.TotalChunks.Value); }
            if (input.IndexedChunks.HasValue) { setParts.Add("indexed_chunks = $ic"); upd.Parameters.AddWithValue("$ic", input.IndexedChunks.Value); }
            if (input.FailedChunks.HasValue) { setParts.Add("failed_chunks = $fc"); upd.Parameters.AddWithValue("$fc", input.FailedChunks.Value); }
            if (input.StaleChunks.HasValue) { setParts.Add("stale_chunks = $sc"); upd.Parameters.AddWithValue("$sc", input.StaleChunks.Value); }
            if (input.Status != null) { setParts.Add("status = $status"); upd.Parameters.AddWithValue("$status", input.Status); }
            if (input.LastRebuiltAt.HasValue) { setParts.Add("last_rebuilt_at = $lra"); upd.Parameters.AddWithValue("$lra", input.LastRebuiltAt.Value.ToString("o")); }

            upd.CommandText = $"UPDATE vector_index_states SET {string.Join(", ", setParts)} WHERE id = $id";
            await upd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            id = Guid.NewGuid().ToString();
            var ins = conn.CreateCommand();
            ins.CommandText = @"
                INSERT INTO vector_index_states
                    (id, workspace_id, provider, model, dimension, index_backend,
                     total_chunks, indexed_chunks, failed_chunks, stale_chunks, status,
                     last_rebuilt_at, schema_version, created_at, updated_at)
                VALUES
                    ($id, $ws, $prov, $model, $dim, $ib,
                     $tc, $ic, $fc, $sc, $status, $lra, 'v1', $now, $now)";
            ins.Parameters.AddWithValue("$id", id);
            ins.Parameters.AddWithValue("$ws", workspaceId);
            ins.Parameters.AddWithValue("$prov", input.Provider ?? string.Empty);
            ins.Parameters.AddWithValue("$model", input.Model ?? string.Empty);
            ins.Parameters.AddWithValue("$dim", input.Dimension ?? 0);
            ins.Parameters.AddWithValue("$ib", input.IndexBackend ?? "sqlite");
            ins.Parameters.AddWithValue("$tc", input.TotalChunks ?? 0);
            ins.Parameters.AddWithValue("$ic", input.IndexedChunks ?? 0);
            ins.Parameters.AddWithValue("$fc", input.FailedChunks ?? 0);
            ins.Parameters.AddWithValue("$sc", input.StaleChunks ?? 0);
            ins.Parameters.AddWithValue("$status", input.Status ?? "idle");
            ins.Parameters.AddWithValue("$lra", input.LastRebuiltAt.HasValue ? (object)input.LastRebuiltAt.Value.ToString("o") : DBNull.Value);
            ins.Parameters.AddWithValue("$now", now);
            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    // ===== Helpers =====

    private static ChunkEmbeddingDto ReadChunkEmbedding(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        ChunkId = r.GetString(2),
        Provider = r.GetString(3),
        Model = r.GetString(4),
        ModelVersion = r.IsDBNull(5) ? null : r.GetString(5),
        Dimension = r.IsDBNull(6) ? 0 : r.GetInt32(6),
        Status = r.GetString(7),
        ErrorMessage = r.IsDBNull(8) ? null : r.GetString(8),
        RetryCount = r.IsDBNull(9) ? 0 : r.GetInt32(9),
        ChunkContentHash = r.IsDBNull(10) ? string.Empty : r.GetString(10),
        CreatedAt = DateTime.Parse(r.GetString(11)),
        UpdatedAt = DateTime.Parse(r.GetString(12))
    };

    private static VectorIndexStateDto ReadVectorIndexState(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        Provider = r.IsDBNull(2) ? string.Empty : r.GetString(2),
        Model = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        Dimension = r.IsDBNull(4) ? 0 : r.GetInt32(4),
        IndexBackend = r.IsDBNull(5) ? "sqlite" : r.GetString(5),
        TotalChunks = r.IsDBNull(6) ? 0 : r.GetInt32(6),
        IndexedChunks = r.IsDBNull(7) ? 0 : r.GetInt32(7),
        FailedChunks = r.IsDBNull(8) ? 0 : r.GetInt32(8),
        StaleChunks = r.IsDBNull(9) ? 0 : r.GetInt32(9),
        Status = r.IsDBNull(10) ? "idle" : r.GetString(10),
        LastRebuiltAt = r.IsDBNull(11) ? null : DateTime.Parse(r.GetString(11)),
        SchemaVersion = r.IsDBNull(12) ? "v1" : r.GetString(12),
        CreatedAt = DateTime.Parse(r.GetString(13)),
        UpdatedAt = DateTime.Parse(r.GetString(14))
    };

    private static TopicDto ReadTopic(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        Name = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        Domain = r.IsDBNull(4) ? null : r.GetString(4),
        Status = r.GetString(5),
        CreatedAt = DateTime.Parse(r.GetString(6)),
        UpdatedAt = DateTime.Parse(r.GetString(7))
    };

    private static InboxItemDto ReadInboxItem(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        UserId = r.IsDBNull(2) ? null : r.GetString(2),
        TopicId = r.IsDBNull(3) ? null : r.GetString(3),
        InputType = r.IsDBNull(4) ? "text" : r.GetString(4),
        ItemType = r.IsDBNull(5) ? "text" : r.GetString(5),
        Title = r.IsDBNull(6) ? null : r.GetString(6),
        ContentText = r.IsDBNull(7) ? null : r.GetString(7),
        SourceUrl = r.IsDBNull(8) ? null : r.GetString(8),
        FilePath = r.IsDBNull(9) ? null : r.GetString(9),
        Status = r.GetString(10),
        SuggestedTopicId = r.IsDBNull(11) ? null : r.GetString(11),
        SuggestedTitle = r.IsDBNull(12) ? null : r.GetString(12),
        SuggestedTags = r.IsDBNull(13) ? null : r.GetString(13),
        CreatedFrom = r.IsDBNull(14) ? "desktop" : r.GetString(14),
        OriginDeviceId = r.IsDBNull(15) ? null : r.GetString(15),
        OriginClientVersion = r.IsDBNull(16) ? null : r.GetString(16),
        SourceId = r.IsDBNull(17) ? null : r.GetString(17),
        ErrorCode = r.IsDBNull(18) ? null : r.GetString(18),
        ErrorMessage = r.IsDBNull(19) ? null : r.GetString(19),
        RetryCount = r.IsDBNull(20) ? 0 : r.GetInt32(20),
        CreatedAt = DateTime.Parse(r.GetString(21)),
        UpdatedAt = DateTime.Parse(r.GetString(22)),
        ImportedAt = r.IsDBNull(23) ? null : DateTime.Parse(r.GetString(23)),
        ArchivedAt = r.IsDBNull(24) ? null : DateTime.Parse(r.GetString(24))
    };

    private static SourceDto ReadSource(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        TopicId = r.IsDBNull(2) ? null : r.GetString(2),
        SourceType = r.GetString(3),
        Title = r.IsDBNull(4) ? null : r.GetString(4),
        Url = r.IsDBNull(5) ? null : r.GetString(5),
        Domain = r.IsDBNull(6) ? null : r.GetString(6),
        LocalFilePath = r.IsDBNull(7) ? null : r.GetString(7),
        ContentHash = r.IsDBNull(8) ? null : r.GetString(8),
        Status = r.GetString(9),
        CreatedAt = DateTime.Parse(r.GetString(10)),
        UpdatedAt = DateTime.Parse(r.GetString(11))
    };

    private static FileObjectDto ReadFileObject(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        StorageProvider = r.GetString(2),
        Bucket = r.IsDBNull(3) ? null : r.GetString(3),
        ObjectKey = r.IsDBNull(4) ? null : r.GetString(4),
        LocalPath = r.IsDBNull(5) ? null : r.GetString(5),
        OriginalFilename = r.GetString(6),
        MimeType = r.GetString(7),
        Extension = r.IsDBNull(8) ? null : r.GetString(8),
        SizeBytes = r.GetInt64(9),
        Sha256 = r.IsDBNull(10) ? null : r.GetString(10),
        CreatedAt = DateTime.Parse(r.GetString(11))
    };

    private static ImportJobDto ReadImportJob(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        InboxItemId = r.GetString(2),
        SourceId = r.IsDBNull(3) ? null : r.GetString(3),
        JobType = r.GetString(4),
        Status = r.GetString(5),
        Attempt = r.GetInt32(6),
        MaxAttempts = r.GetInt32(7),
        StartedAt = r.IsDBNull(8) ? null : DateTime.Parse(r.GetString(8)),
        FinishedAt = r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9)),
        ErrorCode = r.IsDBNull(10) ? null : r.GetString(10),
        ErrorMessage = r.IsDBNull(11) ? null : r.GetString(11),
        CreatedAt = DateTime.Parse(r.GetString(12)),
        UpdatedAt = DateTime.Parse(r.GetString(13))
    };

    private static DocumentDto ReadDocument(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        TopicId = r.IsDBNull(2) ? null : r.GetString(2),
        SourceId = r.IsDBNull(3) ? null : r.GetString(3),
        Title = r.GetString(4),
        ContentMarkdown = r.IsDBNull(5) ? null : r.GetString(5),
        ContentText = r.IsDBNull(6) ? null : r.GetString(6),
        Summary = r.IsDBNull(7) ? null : r.GetString(7),
        AiStatus = r.GetString(8),
        CreatedAt = DateTime.Parse(r.GetString(9)),
        UpdatedAt = DateTime.Parse(r.GetString(10)),
        TitleOriginal = r.IsDBNull(11) ? null : r.GetString(11),
        TitleZh = r.IsDBNull(12) ? null : r.GetString(12),
        PrimaryLanguage = r.IsDBNull(13) ? null : r.GetString(13),
        LanguageDistribution = r.IsDBNull(14) ? null : r.GetString(14),
        IsMultilingual = !r.IsDBNull(15) && r.GetInt32(15) != 0,
        LocalizationStrategy = r.IsDBNull(16) ? "none" : r.GetString(16),
        LocalizationLevel = r.IsDBNull(17) ? "L1" : r.GetString(17),
        LanguageDetectStatus = r.IsDBNull(18) ? "pending" : r.GetString(18),
        LocalizationStatus = r.IsDBNull(19) ? "pending" : r.GetString(19),
        ContentHash = r.IsDBNull(20) ? null : r.GetString(20)
    };

    /// <summary>
    /// Builds a short snippet around the first occurrence of <paramref name="query"/>
    /// within <paramref name="content"/>. Used to populate search result previews.
    /// </summary>
    private static string? BuildSnippet(string? content, string query)
    {
        if (string.IsNullOrEmpty(content)) return null;

        const int window = 200;
        const int pad = 60;

        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return content.Length > window ? content.Substring(0, window) + "..." : content;
        }

        var start = Math.Max(0, idx - pad);
        var length = Math.Min(window, content.Length - start);
        var snippet = content.Substring(start, length);
        var prefix = start > 0 ? "..." : "";
        var suffix = start + length < content.Length ? "..." : "";
        return prefix + snippet + suffix;
    }

    private static CloudInboxSyncLogDto ReadCloudInboxSyncLog(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        Direction = r.GetString(2),
        Status = r.GetString(3),
        CloudApiBaseUrl = r.IsDBNull(4) ? null : r.GetString(4),
        CloudWorkspaceId = r.IsDBNull(5) ? null : r.GetString(5),
        Retention = r.GetString(6),
        PulledCount = r.GetInt32(7),
        FailedCount = r.GetInt32(8),
        NextCursor = r.IsDBNull(9) ? null : r.GetString(9),
        ErrorMessage = r.IsDBNull(10) ? null : r.GetString(10),
        StartedAt = DateTime.Parse(r.GetString(11)),
        FinishedAt = DateTime.Parse(r.GetString(12)),
        DurationMs = r.GetInt64(13),
        CreatedAt = DateTime.Parse(r.GetString(14))
    };

    private static MobileDeviceDto ReadMobileDevice(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        ClientId = r.GetString(2),
        DeviceName = r.IsDBNull(3) ? null : r.GetString(3),
        Platform = r.IsDBNull(4) ? null : r.GetString(4),
        PushToken = r.IsDBNull(5) ? null : r.GetString(5),
        RefreshTokenExpiresAt = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6)),
        Status = r.GetString(7),
        LastSeenAt = r.IsDBNull(8) ? null : DateTime.Parse(r.GetString(8)),
        BoundAt = DateTime.Parse(r.GetString(9)),
        CreatedAt = DateTime.Parse(r.GetString(10)),
        UpdatedAt = DateTime.Parse(r.GetString(11))
    };

    private static PushNotificationDto ReadPushNotification(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        ClientId = r.GetString(2),
        PushToken = r.GetString(3),
        Title = r.GetString(4),
        Body = r.GetString(5),
        DataJson = r.IsDBNull(6) ? null : r.GetString(6),
        Status = r.GetString(7),
        Attempt = r.GetInt32(8),
        MaxAttempts = r.GetInt32(9),
        ProviderResponse = r.IsDBNull(10) ? null : r.GetString(10),
        ErrorMessage = r.IsDBNull(11) ? null : r.GetString(11),
        NextAttemptAt = r.IsDBNull(12) ? null : DateTime.Parse(r.GetString(12)),
        SentAt = r.IsDBNull(13) ? null : DateTime.Parse(r.GetString(13)),
        CreatedAt = DateTime.Parse(r.GetString(14)),
        UpdatedAt = DateTime.Parse(r.GetString(15))
    };

    private static async Task EnsureCloudInboxSyncLogTableAsync(SqliteConnection conn, CancellationToken ct)
    {
        var statements = new[]
        {
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
            "CREATE INDEX IF NOT EXISTS idx_cloud_inbox_sync_logs_workspace_created ON cloud_inbox_sync_logs(workspace_id, created_at)",
            "CREATE INDEX IF NOT EXISTS idx_cloud_inbox_sync_logs_workspace_status ON cloud_inbox_sync_logs(workspace_id, status)"
        };

        foreach (var statement in statements)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task EnsureMobileDevicesTableAsync(SqliteConnection conn, CancellationToken ct)
    {
        var statements = new[]
        {
            @"CREATE TABLE IF NOT EXISTS mobile_devices (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                client_id TEXT NOT NULL,
                device_name TEXT,
                platform TEXT,
                push_token TEXT,
                refresh_token_hash TEXT,
                refresh_token_expires_at TEXT,
                status TEXT NOT NULL DEFAULT 'active',
                last_seen_at TEXT,
                bound_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )",
            "CREATE UNIQUE INDEX IF NOT EXISTS idx_mobile_devices_workspace_client ON mobile_devices(workspace_id, client_id)",
            "CREATE INDEX IF NOT EXISTS idx_mobile_devices_workspace_updated ON mobile_devices(workspace_id, updated_at)",
            "CREATE INDEX IF NOT EXISTS idx_mobile_devices_refresh_token_hash ON mobile_devices(refresh_token_hash)"
        };

        foreach (var statement in statements)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await EnsureSqliteColumnAsync(conn, "mobile_devices", "refresh_token_hash", "TEXT", ct);
        await EnsureSqliteColumnAsync(conn, "mobile_devices", "refresh_token_expires_at", "TEXT", ct);
    }

    private static async Task EnsurePushNotificationsTableAsync(SqliteConnection conn, CancellationToken ct)
    {
        var statements = new[]
        {
            @"CREATE TABLE IF NOT EXISTS push_notifications (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                client_id TEXT NOT NULL,
                push_token TEXT NOT NULL,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                data_json TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                attempt INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 3,
                provider_response TEXT,
                error_message TEXT,
                next_attempt_at TEXT,
                sent_at TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )",
            "CREATE INDEX IF NOT EXISTS idx_push_notifications_status_next ON push_notifications(status, next_attempt_at)",
            "CREATE INDEX IF NOT EXISTS idx_push_notifications_workspace_created ON push_notifications(workspace_id, created_at)"
        };

        foreach (var statement in statements)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task EnsureSqliteColumnAsync(
        SqliteConnection conn,
        string tableName,
        string columnName,
        string columnType,
        CancellationToken ct)
    {
        var exists = false;
        var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName})";
        await using (var r = await check.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                if (string.Equals(r.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return;

        var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
        await alter.ExecuteNonQueryAsync(ct);
    }

    // ===== Phase 4 Read Helpers =====

    private static TagDto ReadTag(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        Name = r.GetString(2),
        NormalizedName = r.IsDBNull(3) ? "" : r.GetString(3),
        DisplayName = r.IsDBNull(4) ? "" : r.GetString(4),
        TagType = r.IsDBNull(5) ? "custom" : r.GetString(5),
        Description = r.IsDBNull(6) ? null : r.GetString(6),
        Color = r.IsDBNull(7) ? null : r.GetString(7),
        Aliases = r.IsDBNull(8) ? null : r.GetString(8),
        Source = r.IsDBNull(9) ? "user" : r.GetString(9),
        UsageCount = r.IsDBNull(10) ? 0 : r.GetInt32(10),
        IsSystem = r.IsDBNull(11) ? false : r.GetInt32(11) != 0,
        IsArchived = r.IsDBNull(12) ? false : r.GetInt32(12) != 0,
        CreatedAt = DateTime.Parse(r.GetString(13)),
        UpdatedAt = DateTime.Parse(r.GetString(14))
    };

    private static DocumentTagDto ReadDocumentTag(SqliteDataReader r) => new()
    {
        Id = $"{r.GetString(0)}:{r.GetString(1)}",
        DocumentId = r.GetString(0),
        TagId = r.GetString(1),
        Source = r.IsDBNull(2) ? "ai" : r.GetString(2),
        Confidence = r.IsDBNull(3) ? null : Convert.ToDecimal(r.GetDouble(3)),
        Reason = r.IsDBNull(4) ? null : r.GetString(4),
        IsConfirmed = r.IsDBNull(5) ? false : r.GetInt32(5) != 0,
        ConfirmedBy = r.IsDBNull(6) ? null : r.GetString(6),
        ConfirmedAt = r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7)),
        CreatedAt = DateTime.Parse(r.GetString(8)),
        TagName = r.IsDBNull(9) ? "" : r.GetString(9),
        TagType = r.IsDBNull(10) ? "custom" : r.GetString(10)
    };

    private static EntityDto ReadEntity(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        WorkspaceId = r.GetString(1),
        Name = r.GetString(2),
        NormalizedName = r.IsDBNull(3) ? "" : r.GetString(3),
        DisplayName = r.IsDBNull(4) ? "" : r.GetString(4),
        EntityType = r.IsDBNull(5) ? "other" : r.GetString(5),
        Aliases = r.IsDBNull(6) ? null : r.GetString(6),
        Description = r.IsDBNull(7) ? null : r.GetString(7),
        ExternalRef = r.IsDBNull(8) ? null : r.GetString(8),
        Source = r.IsDBNull(9) ? "ai" : r.GetString(9),
        UsageCount = r.IsDBNull(10) ? 0 : r.GetInt32(10),
        IsVerified = r.IsDBNull(11) ? false : r.GetInt32(11) != 0,
        IsArchived = r.IsDBNull(12) ? false : r.GetInt32(12) != 0,
        CreatedAt = DateTime.Parse(r.GetString(13)),
        UpdatedAt = DateTime.Parse(r.GetString(14))
    };

    private static DocumentEntityDto ReadDocumentEntity(SqliteDataReader r) => new()
    {
        Id = $"{r.GetString(0)}:{r.GetString(1)}",
        DocumentId = r.GetString(0),
        EntityId = r.GetString(1),
        MentionCount = r.IsDBNull(2) ? 0 : r.GetInt32(2),
        FirstMention = r.IsDBNull(3) ? null : r.GetString(3),
        MentionExamples = r.IsDBNull(4) ? null : r.GetString(4),
        Importance = r.IsDBNull(5) ? null : Convert.ToDecimal(r.GetDouble(5)),
        Role = r.IsDBNull(6) ? null : r.GetString(6),
        Sentiment = r.IsDBNull(7) ? null : r.GetString(7),
        Source = r.IsDBNull(8) ? "ai" : r.GetString(8),
        Confidence = r.IsDBNull(9) ? null : Convert.ToDecimal(r.GetDouble(9)),
        CreatedAt = DateTime.Parse(r.GetString(10)),
        EntityName = r.IsDBNull(11) ? "" : r.GetString(11),
        EntityType = r.IsDBNull(12) ? "other" : r.GetString(12),
        UpdatedAt = DateTime.Parse(r.GetString(10))
    };

    private static ChunkDto ReadChunk(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        DocumentId = r.GetString(1),
        ChunkIndex = r.GetInt32(2),
        ChunkTitle = r.IsDBNull(3) ? null : r.GetString(3),
        Content = r.IsDBNull(4) ? "" : r.GetString(4),
        TokenCount = r.IsDBNull(5) ? 0 : r.GetInt32(5),
        CharCount = r.IsDBNull(6) ? 0 : r.GetInt32(6),
        ChunkUid = r.IsDBNull(7) ? null : r.GetString(7),
        HeadingPath = r.IsDBNull(8) ? null : r.GetString(8),
        SectionLevel = r.IsDBNull(9) ? null : r.GetInt32(9),
        ContentHash = r.IsDBNull(10) ? null : r.GetString(10),
        PrevChunkId = r.IsDBNull(11) ? null : r.GetString(11),
        NextChunkId = r.IsDBNull(12) ? null : r.GetString(12),
        IndexStatus = r.IsDBNull(13) ? "pending" : r.GetString(13),
        ContentOriginal = r.IsDBNull(14) ? (r.IsDBNull(4) ? "" : r.GetString(4)) : r.GetString(14),
        ContentNormalized = r.IsDBNull(15) ? null : r.GetString(15),
        DetectedLanguage = r.IsDBNull(16) ? null : r.GetString(16),
        LanguageConfidence = r.IsDBNull(17) ? null : r.GetDouble(17),
        LanguageDistribution = r.IsDBNull(18) ? null : r.GetString(18),
        ContentType = r.IsDBNull(19) ? "paragraph" : r.GetString(19),
        ProcessingRoute = r.IsDBNull(20) ? "review" : r.GetString(20),
        LocalizationRequired = !r.IsDBNull(21) && r.GetInt32(21) != 0,
        ChunkGroupId = r.IsDBNull(22) ? null : r.GetString(22),
        ParentChunkId = r.IsDBNull(23) ? null : r.GetString(23)
    };
}
