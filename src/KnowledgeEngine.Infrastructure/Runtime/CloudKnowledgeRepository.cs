using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Cloud knowledge repository.
///
/// This implementation uses IAppDbContext (PostgreSQL) directly when the process
/// is the cloud backend itself. When used as a desktop client connecting to a
/// remote cloud, it should delegate via HttpClient (not yet implemented).
///
/// Used when workspace.mode == "cloud".
/// Implements ICloudKnowledgeRepository.
/// </summary>
public class CloudKnowledgeRepository : ICloudKnowledgeRepository
{
    private readonly IAppDbContext _db;
    private readonly ILogger<CloudKnowledgeRepository> _logger;
    private string? _apiBaseUrl;
    private string? _authToken;

    public CloudKnowledgeRepository(IAppDbContext db, ILogger<CloudKnowledgeRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Configures the cloud API base URL and auth token.
    /// </summary>
    public void Configure(string apiBaseUrl, string authToken)
    {
        _apiBaseUrl = apiBaseUrl;
        _authToken = authToken;
        _logger.LogInformation("CloudKnowledgeRepository configured with API: {Url}", apiBaseUrl);
    }

    // ===== Topics =====

    public async Task<TopicDto> CreateTopicAsync(CreateTopicInput input, CancellationToken ct = default)
    {
        var topic = new Domain.Entities.Topic
        {
            Id = Guid.NewGuid(),
            UserId = Guid.TryParse(input.WorkspaceId, out var uid) ? uid : Guid.Empty,
            Name = input.Name,
            Description = input.Description,
            Domain = input.Domain,
            Visibility = "private",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Topics.Add(topic);
        await _db.SaveChangesAsync(ct);
        return MapTopic(topic);
    }

    public async Task<TopicDto?> GetTopicAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var topic = await _db.Topics.FirstOrDefaultAsync(t => t.Id == gid, ct);
        return topic != null ? MapTopic(topic) : null;
    }

    public async Task<List<TopicDto>> ListTopicsAsync(string workspaceId, CancellationToken ct = default)
    {
        var query = _db.Topics.AsQueryable();
        if (Guid.TryParse(workspaceId, out var uid))
            query = query.Where(t => t.UserId == uid);
        var topics = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return topics.Select(MapTopic).ToList();
    }

    // ===== Inbox Items =====

    public async Task<InboxItemDto> CreateInboxItemAsync(CreateInboxItemInput input, CancellationToken ct = default)
    {
        var item = new Domain.Entities.InboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.TryParse(input.WorkspaceId, out var wid) ? wid : Guid.Empty,
            TopicId = Guid.TryParse(input.TopicId, out var tid) ? tid : null,
            InputType = input.InputType,
            ItemType = input.InputType,
            Title = input.Title,
            ContentText = input.ContentText,
            SourceUrl = input.SourceUrl,
            FilePath = input.FilePath,
            Status = "pending",
            CreatedFrom = input.CreatedFrom,
            OriginDeviceId = input.OriginDeviceId,
            OriginClientVersion = input.OriginClientVersion,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.InboxItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return MapInboxItem(item);
    }

    public async Task<List<InboxItemDto>> ListInboxItemsAsync(
        string workspaceId, string? status = null, string? inputType = null,
        string? topicId = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        var query = _db.InboxItems.AsQueryable();
        if (Guid.TryParse(workspaceId, out var wid))
            query = query.Where(i => i.WorkspaceId == wid);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(i => i.Status == status);
        if (!string.IsNullOrEmpty(inputType))
            query = query.Where(i => i.InputType == inputType || i.ItemType == inputType);
        if (!string.IsNullOrEmpty(topicId) && Guid.TryParse(topicId, out var tid))
            query = query.Where(i => i.TopicId == tid);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return items.Select(MapInboxItem).ToList();
    }

    public async Task<List<InboxItemDto>> ListMobileCaptureItemsAsync(
        string workspaceId,
        string clientId,
        int limit = 50,
        CancellationToken ct = default)
    {
        var query = _db.InboxItems.AsQueryable();
        if (Guid.TryParse(workspaceId, out var wid))
            query = query.Where(i => i.WorkspaceId == wid);

        var items = await query
            .Where(i => i.CreatedFrom == "mobile" && i.OriginDeviceId == clientId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);
        return items.Select(MapInboxItem).ToList();
    }

    public async Task<InboxItemDto?> GetInboxItemAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var item = await _db.InboxItems.FirstOrDefaultAsync(i => i.Id == gid, ct);
        return item != null ? MapInboxItem(item) : null;
    }

    public async Task UpdateInboxItemAsync(string id, UpdateInboxItemInput input, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var item = await _db.InboxItems.FirstOrDefaultAsync(i => i.Id == gid, ct);
        if (item != null)
        {
            if (input.Title != null) item.Title = input.Title;
            if (input.ContentText != null) item.ContentText = input.ContentText;
            if (input.TopicId != null && Guid.TryParse(input.TopicId, out var tid)) item.TopicId = tid;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateInboxItemStatusAsync(string id, string status, string? errorMessage = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var item = await _db.InboxItems.FirstOrDefaultAsync(i => i.Id == gid, ct);
        if (item != null)
        {
            item.Status = status;
            item.ErrorMessage = errorMessage;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task SetInboxItemImportedAsync(string inboxItemId, string sourceId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(inboxItemId, out var gid)) return;
        var item = await _db.InboxItems.FirstOrDefaultAsync(i => i.Id == gid, ct);
        if (item != null)
        {
            item.SourceId = Guid.TryParse(sourceId, out var sid) ? sid : null;
            item.Status = "imported";
            item.ImportedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task IncrementRetryCountAsync(string inboxItemId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(inboxItemId, out var gid)) return;
        var item = await _db.InboxItems.FirstOrDefaultAsync(i => i.Id == gid, ct);
        if (item != null)
        {
            item.RetryCount += 1;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task ArchiveInboxItemAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var item = await _db.InboxItems.FirstOrDefaultAsync(i => i.Id == gid, ct);
        if (item != null)
        {
            item.Status = "archived";
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteInboxItemAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var item = await _db.InboxItems.FirstOrDefaultAsync(i => i.Id == gid, ct);
        if (item != null)
        {
            _db.InboxItems.Remove(item);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> CountInboxItemsAsync(string workspaceId, string? status = null, CancellationToken ct = default)
    {
        var query = _db.InboxItems.AsQueryable();
        if (Guid.TryParse(workspaceId, out var wid))
            query = query.Where(i => i.WorkspaceId == wid);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(i => i.Status == status);
        return await query.CountAsync(ct);
    }

    public async Task<bool> IsDuplicateUrlAsync(string workspaceId, string url, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return false;
        return await _db.Sources.AnyAsync(s => s.UserId == wid && s.Url == url, ct)
            || await _db.InboxItems.AnyAsync(i => i.WorkspaceId == wid && i.SourceUrl == url, ct);
    }

    public async Task<bool> IsDuplicateContentAsync(string workspaceId, string contentHash, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return false;
        return await _db.Sources.AnyAsync(s => s.UserId == wid && s.ContentHash == contentHash, ct);
    }

    // ===== Inbox Attachments (§7.2) =====

    public async Task<InboxAttachmentDto> CreateInboxAttachmentAsync(CreateInboxAttachmentInput input, CancellationToken ct = default)
    {
        var attachment = new Domain.Entities.InboxAttachment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.TryParse(input.WorkspaceId, out var wid) ? wid : Guid.Empty,
            InboxItemId = Guid.TryParse(input.InboxItemId, out var iid) ? iid : Guid.Empty,
            FileId = Guid.TryParse(input.FileId, out var fid) ? fid : Guid.Empty,
            Role = input.Role,
            Filename = input.Filename,
            MimeType = input.MimeType,
            SizeBytes = input.SizeBytes,
            CreatedAt = DateTime.UtcNow
        };
        _db.InboxAttachments.Add(attachment);
        await _db.SaveChangesAsync(ct);
        return MapInboxAttachment(attachment);
    }

    public async Task<List<InboxAttachmentDto>> ListInboxAttachmentsAsync(string inboxItemId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(inboxItemId, out var iid)) return new List<InboxAttachmentDto>();
        var attachments = await _db.InboxAttachments
            .Where(a => a.InboxItemId == iid)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        return attachments.Select(MapInboxAttachment).ToList();
    }

    // ===== File Objects (§7.3) =====

    public async Task<FileObjectDto> CreateFileObjectAsync(CreateFileObjectInput input, CancellationToken ct = default)
    {
        var file = new Domain.Entities.FileObject
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.TryParse(input.WorkspaceId, out var wid) ? wid : Guid.Empty,
            StorageProvider = input.StorageProvider,
            Bucket = input.Bucket ?? string.Empty,
            ObjectKey = input.ObjectKey ?? string.Empty,
            LocalPath = input.LocalPath,
            OriginalFilename = input.OriginalFilename,
            MimeType = input.MimeType,
            Extension = input.Extension,
            SizeBytes = input.SizeBytes,
            Sha256 = input.Sha256,
            CreatedAt = DateTime.UtcNow
        };
        _db.Files.Add(file);
        await _db.SaveChangesAsync(ct);
        return MapFileObject(file);
    }

    public async Task<FileObjectDto?> GetFileObjectAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == gid, ct);
        return file != null ? MapFileObject(file) : null;
    }

    // ===== Import Jobs (§7.5) =====

    public async Task<ImportJobDto> CreateImportJobAsync(CreateImportJobInput input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var job = new Domain.Entities.ImportJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.TryParse(input.WorkspaceId, out var wid) ? wid : Guid.Empty,
            InboxItemId = Guid.TryParse(input.InboxItemId, out var iid) ? iid : Guid.Empty,
            JobType = input.JobType,
            Status = "running",
            Attempt = 1,
            MaxAttempts = 3,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return MapImportJob(job);
    }

    public async Task<ImportJobDto?> GetImportJobAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == gid, ct);
        return job != null ? MapImportJob(job) : null;
    }

    public async Task<List<ImportJobDto>> ListImportJobsAsync(string workspaceId, string? status = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return new List<ImportJobDto>();
        var query = _db.ImportJobs.Where(j => j.WorkspaceId == wid);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(j => j.Status == status);
        var jobs = await query.OrderByDescending(j => j.CreatedAt).ToListAsync(ct);
        return jobs.Select(MapImportJob).ToList();
    }

    public async Task UpdateImportJobAsync(string id, string status, string? sourceId = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == gid, ct);
        if (job == null) return;

        job.Status = status;
        if (sourceId != null && Guid.TryParse(sourceId, out var sid))
            job.SourceId = sid;
        if (errorCode != null)
            job.ErrorCode = errorCode;
        if (errorMessage != null)
            job.ErrorMessage = errorMessage;
        if (status == "succeeded" || status == "failed")
            job.FinishedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    // ===== Inbox Events (§7.6) =====

    public async Task CreateInboxEventAsync(string workspaceId, string inboxItemId, string eventType, string? payload = null, string? createdBy = null, CancellationToken ct = default)
    {
        var evt = new Domain.Entities.InboxEvent
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.TryParse(workspaceId, out var wid) ? wid : Guid.Empty,
            InboxItemId = Guid.TryParse(inboxItemId, out var iid) ? iid : Guid.Empty,
            EventType = eventType,
            EventPayload = payload,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
        _db.InboxEvents.Add(evt);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<InboxEventDto>> ListInboxEventsAsync(string inboxItemId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(inboxItemId, out var iid)) return new List<InboxEventDto>();
        var events = await _db.InboxEvents
            .Where(e => e.InboxItemId == iid)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
        return events.Select(MapInboxEvent).ToList();
    }

    // ===== Sync Cursors (§7.7) =====

    public async Task<SyncCursorDto?> GetSyncCursorAsync(string workspaceId, string cursorType, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return null;
        var cursor = await _db.SyncCursors
            .FirstOrDefaultAsync(c => c.WorkspaceId == wid && c.CursorType == cursorType, ct);
        return cursor != null ? MapSyncCursor(cursor) : null;
    }

    public async Task UpdateSyncCursorAsync(string workspaceId, string cursorType, string cursorValue, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return;
        var now = DateTime.UtcNow;

        var existing = await _db.SyncCursors
            .FirstOrDefaultAsync(c => c.WorkspaceId == wid && c.CursorType == cursorType, ct);

        if (existing != null)
        {
            existing.CursorValue = cursorValue;
            existing.LastSyncedAt = now;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.SyncCursors.Add(new Domain.Entities.SyncCursor
            {
                Id = Guid.NewGuid(),
                WorkspaceId = wid,
                RemoteWorkspaceId = wid,
                CursorType = cursorType,
                CursorValue = cursorValue,
                LastSyncedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    // ===== Cloud Inbox Sync Logs =====

    public async Task<CloudInboxSyncLogDto> CreateCloudInboxSyncLogAsync(CreateCloudInboxSyncLogInput input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var startedAt = input.StartedAt == default ? now : input.StartedAt;
        var finishedAt = input.FinishedAt == default ? now : input.FinishedAt;
        var log = new Domain.Entities.CloudInboxSyncLog
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.TryParse(input.WorkspaceId, out var wid) ? wid : Guid.Empty,
            Direction = input.Direction,
            Status = input.Status,
            CloudApiBaseUrl = input.CloudApiBaseUrl,
            CloudWorkspaceId = input.CloudWorkspaceId,
            Retention = input.Retention,
            PulledCount = input.PulledCount,
            FailedCount = input.FailedCount,
            NextCursor = input.NextCursor,
            ErrorMessage = input.ErrorMessage,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationMs = Math.Max(0, (long)(finishedAt - startedAt).TotalMilliseconds),
            CreatedAt = now
        };

        _db.CloudInboxSyncLogs.Add(log);
        await _db.SaveChangesAsync(ct);
        return MapCloudInboxSyncLog(log);
    }

    public async Task<List<CloudInboxSyncLogDto>> ListCloudInboxSyncLogsAsync(string workspaceId, int limit = 10, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return new List<CloudInboxSyncLogDto>();
        var logs = await _db.CloudInboxSyncLogs
            .Where(l => l.WorkspaceId == wid)
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);
        return logs.Select(MapCloudInboxSyncLog).ToList();
    }

    // ===== Mobile Devices =====

    public async Task<MobileDeviceDto> UpsertMobileDeviceAsync(UpsertMobileDeviceInput input, CancellationToken ct = default)
    {
        if (!Guid.TryParse(input.WorkspaceId, out var wid))
        {
            throw new ArgumentException("Invalid workspace id", nameof(input.WorkspaceId));
        }

        var clientId = input.ClientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required", nameof(input.ClientId));
        }

        var now = DateTime.UtcNow;
        var device = await _db.MobileDevices
            .FirstOrDefaultAsync(d => d.WorkspaceId == wid && d.ClientId == clientId, ct);

        if (device == null)
        {
            device = new MobileDevice
            {
                Id = Guid.NewGuid(),
                WorkspaceId = wid,
                ClientId = clientId,
                BoundAt = now,
                CreatedAt = now
            };
            _db.MobileDevices.Add(device);
        }

        device.DeviceName = string.IsNullOrWhiteSpace(input.DeviceName) ? device.DeviceName : input.DeviceName.Trim();
        device.Platform = string.IsNullOrWhiteSpace(input.Platform) ? device.Platform : input.Platform.Trim();
        device.PushToken = string.IsNullOrWhiteSpace(input.PushToken) ? device.PushToken : input.PushToken.Trim();
        device.Status = "active";
        device.LastSeenAt = now;
        device.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return MapMobileDevice(device);
    }

    public async Task<MobileDeviceDto> UpdateMobileDeviceRefreshTokenAsync(
        string workspaceId,
        string clientId,
        string refreshTokenHash,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid))
        {
            throw new ArgumentException("Invalid workspace id", nameof(workspaceId));
        }

        var device = await _db.MobileDevices
            .FirstOrDefaultAsync(d => d.WorkspaceId == wid && d.ClientId == clientId.Trim(), ct)
            ?? throw new InvalidOperationException("Mobile device not found");

        device.RefreshTokenHash = refreshTokenHash;
        device.RefreshTokenExpiresAt = expiresAt;
        device.Status = "active";
        device.LastSeenAt = DateTime.UtcNow;
        device.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapMobileDevice(device);
    }

    public async Task<List<MobileDeviceDto>> ListMobileDevicesAsync(string workspaceId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return new List<MobileDeviceDto>();
        var devices = await _db.MobileDevices
            .Where(d => d.WorkspaceId == wid)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);
        return devices.Select(MapMobileDevice).ToList();
    }

    public async Task<MobileDeviceDto?> GetMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid) || string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var device = await _db.MobileDevices
            .FirstOrDefaultAsync(d => d.WorkspaceId == wid && d.ClientId == clientId.Trim(), ct);
        return device == null ? null : MapMobileDevice(device);
    }

    public async Task<MobileDeviceDto?> GetMobileDeviceByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenHash)) return null;
        var device = await _db.MobileDevices
            .FirstOrDefaultAsync(d => d.RefreshTokenHash == refreshTokenHash, ct);
        return device == null ? null : MapMobileDevice(device);
    }

    public async Task DeactivateMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid) || string.IsNullOrWhiteSpace(clientId)) return;
        var device = await _db.MobileDevices
            .FirstOrDefaultAsync(d => d.WorkspaceId == wid && d.ClientId == clientId.Trim(), ct);
        if (device == null) return;

        device.Status = "revoked";
        device.RefreshTokenHash = null;
        device.RefreshTokenExpiresAt = null;
        device.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ===== Push Notifications =====

    public async Task<PushNotificationDto> CreatePushNotificationAsync(CreatePushNotificationInput input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var notification = new PushNotification
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.TryParse(input.WorkspaceId, out var wid) ? wid : Guid.Empty,
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

        _db.PushNotifications.Add(notification);
        await _db.SaveChangesAsync(ct);
        return MapPushNotification(notification);
    }

    public async Task<List<PushNotificationDto>> ListPendingPushNotificationsAsync(int limit = 20, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var notifications = await _db.PushNotifications
            .Where(n => n.Status == "pending" && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
            .OrderBy(n => n.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);
        return notifications.Select(MapPushNotification).ToList();
    }

    public async Task<List<PushNotificationDto>> ListPushNotificationsAsync(string workspaceId, string? status = null, int limit = 50, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return new List<PushNotificationDto>();

        var query = _db.PushNotifications.Where(n => n.WorkspaceId == wid);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(n => n.Status == status.Trim());
        }

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);
        return notifications.Select(MapPushNotification).ToList();
    }

    public async Task MarkPushNotificationSentAsync(string id, string? providerResponse = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var notification = await _db.PushNotifications.FirstOrDefaultAsync(n => n.Id == gid, ct);
        if (notification == null) return;

        notification.Status = "sent";
        notification.ProviderResponse = providerResponse;
        notification.SentAt = DateTime.UtcNow;
        notification.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkPushNotificationFailedAsync(string id, string errorMessage, DateTime? nextAttemptAt, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var notification = await _db.PushNotifications.FirstOrDefaultAsync(n => n.Id == gid, ct);
        if (notification == null) return;

        notification.Attempt += 1;
        notification.Status = nextAttemptAt == null || notification.Attempt >= notification.MaxAttempts
            ? "failed"
            : "pending";
        notification.ErrorMessage = errorMessage;
        notification.NextAttemptAt = notification.Status == "pending" ? nextAttemptAt : null;
        notification.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ===== Sources =====

    public async Task<SourceDto> CreateSourceAsync(CreateSourceInput input, CancellationToken ct = default)
    {
        var source = new Domain.Entities.Source
        {
            Id = Guid.NewGuid(),
            UserId = Guid.TryParse(input.WorkspaceId, out var uid) ? uid : Guid.Empty,
            TopicId = Guid.TryParse(input.TopicId, out var tid) ? tid : null,
            SourceType = input.SourceType,
            Title = input.Title,
            Url = input.Url,
            Domain = "",
            ContentHash = input.ContentHash,
            Status = "pending",
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Sources.Add(source);
        await _db.SaveChangesAsync(ct);
        return MapSource(source);
    }

    public async Task<SourceDto?> GetSourceAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == gid, ct);
        return source != null ? MapSource(source) : null;
    }

    public async Task<List<SourceDto>> ListSourcesAsync(string workspaceId, string? topicId = null, CancellationToken ct = default)
    {
        var query = _db.Sources.AsQueryable();
        if (Guid.TryParse(workspaceId, out var uid))
            query = query.Where(s => s.UserId == uid);
        if (Guid.TryParse(topicId, out var tid))
            query = query.Where(s => s.TopicId == tid);
        var sources = await query.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        return sources.Select(MapSource).ToList();
    }

    // ===== Documents =====

    public async Task<DocumentDto> CreateDocumentAsync(CreateDocumentInput input, CancellationToken ct = default)
    {
        var doc = new Domain.Entities.Document
        {
            Id = Guid.NewGuid(),
            UserId = Guid.TryParse(input.WorkspaceId, out var uid) ? uid : Guid.Empty,
            TopicId = Guid.TryParse(input.TopicId, out var tid) ? tid : null,
            SourceId = Guid.TryParse(input.SourceId, out var sid) ? sid : Guid.Empty,
            Title = input.Title,
            TitleOriginal = input.Title,
            ContentMarkdown = input.ContentMarkdown,
            ContentText = input.ContentText,
            AiStatus = "pending",
            ChunkStatus = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);
        return MapDocument(doc);
    }

    public async Task<DocumentDto?> GetDocumentAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == gid, ct);
        return doc != null ? MapDocument(doc) : null;
    }

    public async Task<List<DocumentDto>> ListDocumentsAsync(string workspaceId, string? topicId = null, CancellationToken ct = default)
    {
        var query = _db.Documents.AsQueryable();
        if (Guid.TryParse(workspaceId, out var uid))
            query = query.Where(d => d.UserId == uid);
        if (Guid.TryParse(topicId, out var tid))
            query = query.Where(d => d.TopicId == tid);
        var docs = await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        return docs.Select(MapDocument).ToList();
    }

    // ===== Document Chunks =====

    public async Task SaveChunksAsync(string documentId, List<ChunkDto> chunks, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return;

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == docId, ct);

        // Delete old chunks
        var oldChunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .ToListAsync(ct);
        if (oldChunks.Count > 0)
        {
            _db.DocumentChunks.RemoveRange(oldChunks);
        }

        // Insert new chunks
        foreach (var chunk in chunks)
        {
            _db.DocumentChunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                SourceId = document?.SourceId ?? Guid.Empty,
                UserId = document?.UserId ?? Guid.Empty,
                TopicId = document?.TopicId,
                ChunkIndex = chunk.ChunkIndex,
                ChunkTitle = chunk.ChunkTitle,
                Content = chunk.Content,
                ContentOriginal = string.IsNullOrEmpty(chunk.ContentOriginal) ? chunk.Content : chunk.ContentOriginal,
                ContentNormalized = chunk.ContentNormalized,
                DetectedLanguage = chunk.DetectedLanguage,
                LanguageConfidence = chunk.LanguageConfidence.HasValue ? (decimal)chunk.LanguageConfidence.Value : null,
                LanguageDistribution = chunk.LanguageDistribution,
                ContentType = chunk.ContentType,
                ProcessingRoute = chunk.ProcessingRoute,
                LocalizationRequired = chunk.LocalizationRequired,
                ChunkGroupId = Guid.TryParse(chunk.ChunkGroupId, out var groupId) ? groupId : Guid.NewGuid(),
                ParentChunkId = Guid.TryParse(chunk.ParentChunkId, out var parentId) ? parentId : null,
                TokenCount = chunk.TokenCount,
                CharCount = chunk.CharCount,
                EmbeddingStatus = "pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    // ===== Search =====

    public async Task<List<SearchResultDto>> SearchDocumentsAsync(string workspaceId, string query, int limit = 20, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return new List<SearchResultDto>();
        if (string.IsNullOrWhiteSpace(query)) return new List<SearchResultDto>();

        var docs = await _db.Documents
            .Where(d => d.UserId == wid
                && (d.Title.Contains(query) || (d.ContentText != null && d.ContentText.Contains(query))))
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return docs.Select(d => new SearchResultDto
        {
            DocumentId = d.Id.ToString(),
            Title = d.Title,
            ContentSnippet = d.ContentText != null && d.ContentText.Length > 200
                ? d.ContentText.Substring(0, 200)
                : d.ContentText,
            Score = 1.0,
            SourceUrl = null
        }).ToList();
    }

    // ===== Settings =====

    public async Task<string?> GetSettingAsync(string workspaceId, string key, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return null;
        var setting = await _db.WorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == wid && s.Key == key, ct);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string workspaceId, string key, string value, CancellationToken ct = default)
    {
        if (!Guid.TryParse(workspaceId, out var wid)) return;
        var existing = await _db.WorkspaceSettings
            .FirstOrDefaultAsync(s => s.WorkspaceId == wid && s.Key == key, ct);
        if (existing != null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.WorkspaceSettings.Add(new Domain.Entities.WorkspaceSetting
            {
                Id = Guid.NewGuid(),
                WorkspaceId = wid,
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    // ===== Tags (Phase 4) =====

    public async Task<TagDto> CreateTagAsync(CreateTagInput input, CancellationToken ct = default)
    {
        var normalizedName = (input.NormalizedName ?? input.Name).ToLower().Trim();
        var now = DateTime.UtcNow;

        // Check for duplicates by (WorkspaceId, NormalizedName)
        var existing = await _db.Tags
            .FirstOrDefaultAsync(t => t.WorkspaceId == input.WorkspaceId && t.NormalizedName == normalizedName, ct);
        if (existing != null) return MapTag(existing);

        var tag = new Domain.Entities.Tag
        {
            Id = Guid.NewGuid(),
            UserId = Guid.TryParse(input.WorkspaceId, out var uid) ? uid : Guid.Empty,
            WorkspaceId = input.WorkspaceId,
            Name = input.Name,
            NormalizedName = normalizedName,
            DisplayName = input.DisplayName ?? input.Name,
            TagType = input.TagType,
            Type = input.TagType,
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
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(ct);
        return MapTag(tag);
    }

    public async Task<TagDto?> GetTagAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == gid, ct);
        return tag != null ? MapTag(tag) : null;
    }

    public async Task<List<TagDto>> ListTagsAsync(string workspaceId, string? tagType = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        var query = _db.Tags.Where(t => t.WorkspaceId == workspaceId && !t.IsArchived);
        if (!string.IsNullOrEmpty(tagType))
            query = query.Where(t => t.TagType == tagType);
        var tags = await query
            .OrderByDescending(t => t.UsageCount)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return tags.Select(MapTag).ToList();
    }

    public async Task UpdateTagAsync(string id, UpdateTagInput input, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == gid, ct);
        if (tag == null) return;

        if (input.Name != null) tag.Name = input.Name;
        if (input.DisplayName != null) tag.DisplayName = input.DisplayName;
        if (input.TagType != null) { tag.TagType = input.TagType; tag.Type = input.TagType; }
        if (input.Description != null) tag.Description = input.Description;
        if (input.Color != null) tag.Color = input.Color;
        if (input.Aliases != null) tag.Aliases = input.Aliases;
        if (input.IsArchived.HasValue) tag.IsArchived = input.IsArchived.Value;
        tag.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteTagAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == gid, ct);
        if (tag != null)
        {
            _db.Tags.Remove(tag);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ===== Document Tags (Phase 4) =====

    public async Task<List<DocumentTagDto>> GetDocumentTagsAsync(string documentId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return new List<DocumentTagDto>();
        var docTags = await _db.DocumentTags
            .Where(dt => dt.DocumentId == docId)
            .ToListAsync(ct);
        var tagIds = docTags.Select(dt => dt.TagId).Distinct().ToList();
        var tags = await _db.Tags
            .Where(t => tagIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        return docTags.Select(dt => MapDocumentTag(dt, tags.GetValueOrDefault(dt.TagId))).ToList();
    }

    public async Task<DocumentTagDto> AddDocumentTagAsync(string documentId, string tagName, string? tagType, string source, decimal? confidence, string? reason, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId))
            throw new ArgumentException("Invalid documentId", nameof(documentId));

        var normalizedName = tagName.ToLower().Trim();

        // Resolve workspace from the document to scope tag lookups (avoid cross-workspace leak)
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == docId, ct);
        var workspaceId = document?.UserId.ToString() ?? "";

        // Find or create tag by (WorkspaceId, NormalizedName)
        var tag = await _db.Tags
            .FirstOrDefaultAsync(t => t.WorkspaceId == workspaceId && t.NormalizedName == normalizedName, ct);
        if (tag == null)
        {
            tag = new Domain.Entities.Tag
            {
                Id = Guid.NewGuid(),
                UserId = document?.UserId ?? Guid.Empty,
                WorkspaceId = workspaceId,
                Name = tagName,
                NormalizedName = normalizedName,
                DisplayName = tagName,
                TagType = tagType ?? "custom",
                Type = tagType ?? "custom",
                Source = source,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Tags.Add(tag);
            await _db.SaveChangesAsync(ct);
        }

        // Check if document-tag link already exists
        var existing = await _db.DocumentTags
            .FirstOrDefaultAsync(dt => dt.DocumentId == docId && dt.TagId == tag.Id, ct);
        if (existing != null)
        {
            existing.Source = source;
            existing.Confidence = confidence;
            existing.Reason = reason;
            await _db.SaveChangesAsync(ct);
            return MapDocumentTag(existing, tag);
        }

        var docTag = new Domain.Entities.DocumentTag
        {
            DocumentId = docId,
            TagId = tag.Id,
            Source = source,
            Confidence = confidence,
            Reason = reason,
            IsConfirmed = false,
            CreatedAt = DateTime.UtcNow
        };
        _db.DocumentTags.Add(docTag);

        // Increment usage count
        tag.UsageCount += 1;
        tag.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapDocumentTag(docTag, tag);
    }

    public async Task ConfirmDocumentTagAsync(string documentId, string tagId, string? confirmedBy, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return;
        if (!Guid.TryParse(tagId, out var tid)) return;
        var docTag = await _db.DocumentTags
            .FirstOrDefaultAsync(dt => dt.DocumentId == docId && dt.TagId == tid, ct);
        if (docTag != null)
        {
            docTag.IsConfirmed = true;
            docTag.ConfirmedBy = confirmedBy;
            docTag.ConfirmedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteDocumentTagAsync(string documentId, string tagId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return;
        if (!Guid.TryParse(tagId, out var tid)) return;
        var docTag = await _db.DocumentTags
            .FirstOrDefaultAsync(dt => dt.DocumentId == docId && dt.TagId == tid, ct);
        if (docTag != null)
        {
            _db.DocumentTags.Remove(docTag);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ===== Entities (Phase 4) =====

    public async Task<EntityDto> CreateEntityAsync(CreateEntityInput input, CancellationToken ct = default)
    {
        var normalizedName = (input.NormalizedName ?? input.Name).ToLower().Trim();
        var now = DateTime.UtcNow;

        // Check for duplicates by (WorkspaceId, NormalizedName, EntityType)
        var existing = await _db.Entities
            .FirstOrDefaultAsync(e => e.WorkspaceId == input.WorkspaceId && e.NormalizedName == normalizedName && e.EntityType == input.EntityType, ct);
        if (existing != null) return MapEntity(existing);

        var entity = new Domain.Entities.Entity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.TryParse(input.WorkspaceId, out var uid) ? uid : Guid.Empty,
            WorkspaceId = input.WorkspaceId,
            Name = input.Name,
            NormalizedName = normalizedName,
            DisplayName = input.DisplayName ?? input.Name,
            EntityType = input.EntityType,
            Aliases = input.Aliases,
            Description = input.Description,
            Source = input.Source,
            UsageCount = 0,
            IsVerified = false,
            IsArchived = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Entities.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapEntity(entity);
    }

    public async Task<EntityDto?> GetEntityAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        var entity = await _db.Entities.FirstOrDefaultAsync(e => e.Id == gid, ct);
        return entity != null ? MapEntity(entity) : null;
    }

    public async Task<List<EntityDto>> ListEntitiesAsync(string workspaceId, string? entityType = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        var query = _db.Entities.Where(e => e.WorkspaceId == workspaceId && !e.IsArchived);
        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(e => e.EntityType == entityType);
        var entities = await query
            .OrderByDescending(e => e.UsageCount)
            .ThenByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(MapEntity).ToList();
    }

    public async Task DeleteEntityAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return;
        var entity = await _db.Entities.FirstOrDefaultAsync(e => e.Id == gid, ct);
        if (entity != null)
        {
            _db.Entities.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ===== Document Entities (Phase 4) =====

    public async Task<List<DocumentEntityDto>> GetDocumentEntitiesAsync(string documentId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return new List<DocumentEntityDto>();
        var docEntities = await _db.DocumentEntities
            .Where(de => de.DocumentId == docId)
            .ToListAsync(ct);
        var entityIds = docEntities.Select(de => de.EntityId).Distinct().ToList();
        var entities = await _db.Entities
            .Where(e => entityIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        return docEntities.Select(de => MapDocumentEntity(de, entities.GetValueOrDefault(de.EntityId))).ToList();
    }

    public async Task DeleteDocumentEntityAsync(string documentId, string entityId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return;
        if (!Guid.TryParse(entityId, out var eid)) return;
        var docEntity = await _db.DocumentEntities
            .FirstOrDefaultAsync(de => de.DocumentId == docId && de.EntityId == eid, ct);
        if (docEntity != null)
        {
            _db.DocumentEntities.Remove(docEntity);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ===== Chunks (Phase 4) =====

    public async Task<List<ChunkDto>> GetDocumentChunksAsync(string documentId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return new List<ChunkDto>();
        var chunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
        return chunks.Select(MapChunk).ToList();
    }

    public async Task<ChunkDto?> GetChunkAsync(string chunkId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(chunkId, out var gid)) return null;
        var chunk = await _db.DocumentChunks.FirstOrDefaultAsync(c => c.Id == gid, ct);
        return chunk != null ? MapChunk(chunk) : null;
    }

    public async Task DeleteChunksByDocumentAsync(string documentId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId)) return;
        var chunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .ToListAsync(ct);
        if (chunks.Count > 0)
        {
            _db.DocumentChunks.RemoveRange(chunks);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ===== Embeddings (Phase 4) =====

    public async Task<ChunkEmbeddingDto?> GetChunkEmbeddingAsync(string chunkId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(chunkId, out var gid)) return null;
        var embedding = await _db.ChunkEmbeddings
            .FirstOrDefaultAsync(e => e.ChunkId == gid, ct);
        return embedding != null ? MapChunkEmbedding(embedding) : null;
    }

    public async Task SaveChunkEmbeddingAsync(SaveChunkEmbeddingInput input, CancellationToken ct = default)
    {
        if (!Guid.TryParse(input.ChunkId, out var chunkId)) return;
        var now = DateTime.UtcNow;

        var existing = await _db.ChunkEmbeddings
            .FirstOrDefaultAsync(e => e.ChunkId == chunkId && e.Provider == input.Provider && e.Model == input.Model, ct);

        if (existing != null)
        {
            existing.ModelVersion = input.ModelVersion;
            existing.Dimension = input.Dimension;
            existing.EmbeddingJson = input.EmbeddingJson;
            existing.ChunkContentHash = input.ChunkContentHash;
            existing.Status = input.Status;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.ChunkEmbeddings.Add(new Domain.Entities.ChunkEmbedding
            {
                Id = Guid.NewGuid(),
                ChunkId = chunkId,
                WorkspaceId = input.WorkspaceId,
                Provider = input.Provider,
                Model = input.Model,
                ModelVersion = input.ModelVersion,
                Dimension = input.Dimension,
                EmbeddingJson = input.EmbeddingJson,
                ChunkContentHash = input.ChunkContentHash,
                Status = input.Status,
                RetryCount = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkEmbeddingsStaleAsync(string workspaceId, string? model = null, CancellationToken ct = default)
    {
        var query = _db.ChunkEmbeddings.Where(e => e.WorkspaceId == workspaceId);
        if (!string.IsNullOrEmpty(model))
            query = query.Where(e => e.Model == model);
        var embeddings = await query.ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var e in embeddings)
        {
            e.Status = "stale";
            e.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    // ===== Vector Index State (Phase 4) =====

    public async Task<VectorIndexStateDto?> GetVectorIndexStateAsync(string workspaceId, CancellationToken ct = default)
    {
        var state = await _db.VectorIndexStates
            .FirstOrDefaultAsync(v => v.WorkspaceId == workspaceId, ct);
        return state != null ? MapVectorIndexState(state) : null;
    }

    public async Task UpdateVectorIndexStateAsync(string workspaceId, UpdateVectorIndexStateInput input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var existing = await _db.VectorIndexStates
            .FirstOrDefaultAsync(v => v.WorkspaceId == workspaceId, ct);

        if (existing != null)
        {
            if (input.Provider != null) existing.Provider = input.Provider;
            if (input.Model != null) existing.Model = input.Model;
            if (input.Dimension.HasValue) existing.Dimension = input.Dimension.Value;
            if (input.IndexBackend != null) existing.IndexBackend = input.IndexBackend;
            if (input.TotalChunks.HasValue) existing.TotalChunks = input.TotalChunks.Value;
            if (input.IndexedChunks.HasValue) existing.IndexedChunks = input.IndexedChunks.Value;
            if (input.FailedChunks.HasValue) existing.FailedChunks = input.FailedChunks.Value;
            if (input.StaleChunks.HasValue) existing.StaleChunks = input.StaleChunks.Value;
            if (input.Status != null) existing.Status = input.Status;
            if (input.LastRebuiltAt.HasValue) existing.LastRebuiltAt = input.LastRebuiltAt.Value;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.VectorIndexStates.Add(new Domain.Entities.VectorIndexState
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Provider = input.Provider ?? "",
                Model = input.Model ?? "",
                Dimension = input.Dimension,
                IndexBackend = input.IndexBackend ?? "pgvector",
                TotalChunks = input.TotalChunks ?? 0,
                IndexedChunks = input.IndexedChunks ?? 0,
                FailedChunks = input.FailedChunks ?? 0,
                StaleChunks = input.StaleChunks ?? 0,
                Status = input.Status ?? "idle",
                LastRebuiltAt = input.LastRebuiltAt,
                SchemaVersion = "v1",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    // ===== Mappers =====

    private static TopicDto MapTopic(Domain.Entities.Topic t) => new()
    {
        Id = t.Id.ToString(),
        WorkspaceId = t.UserId.ToString(),
        Name = t.Name,
        Description = t.Description,
        Domain = t.Domain,
        Status = t.Status,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    private static InboxItemDto MapInboxItem(Domain.Entities.InboxItem i) => new()
    {
        Id = i.Id.ToString(),
        WorkspaceId = i.WorkspaceId.ToString(),
        UserId = i.UserId?.ToString(),
        TopicId = i.TopicId?.ToString(),
        InputType = i.InputType,
        ItemType = i.ItemType,
        Title = i.Title,
        ContentText = i.ContentText,
        SourceUrl = i.SourceUrl,
        FilePath = i.FilePath,
        Status = i.Status,
        SuggestedTopicId = i.SuggestedTopicId?.ToString(),
        SuggestedTitle = i.SuggestedTitle,
        SuggestedTags = i.SuggestedTags,
        CreatedFrom = i.CreatedFrom ?? "desktop",
        OriginDeviceId = i.OriginDeviceId,
        OriginClientVersion = i.OriginClientVersion,
        SourceId = i.SourceId?.ToString(),
        ErrorCode = i.ErrorCode,
        ErrorMessage = i.ErrorMessage,
        RetryCount = i.RetryCount,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
        ImportedAt = i.ImportedAt,
        ArchivedAt = i.ArchivedAt
    };

    private static InboxAttachmentDto MapInboxAttachment(Domain.Entities.InboxAttachment a) => new()
    {
        Id = a.Id.ToString(),
        WorkspaceId = a.WorkspaceId.ToString(),
        InboxItemId = a.InboxItemId.ToString(),
        FileId = a.FileId.ToString(),
        Role = a.Role,
        Filename = a.Filename,
        MimeType = a.MimeType,
        SizeBytes = a.SizeBytes,
        CreatedAt = a.CreatedAt
    };

    private static FileObjectDto MapFileObject(Domain.Entities.FileObject f) => new()
    {
        Id = f.Id.ToString(),
        WorkspaceId = f.WorkspaceId.ToString(),
        StorageProvider = f.StorageProvider,
        Bucket = f.Bucket,
        ObjectKey = f.ObjectKey,
        LocalPath = f.LocalPath,
        OriginalFilename = f.OriginalFilename ?? string.Empty,
        MimeType = f.MimeType ?? string.Empty,
        Extension = f.Extension,
        SizeBytes = f.SizeBytes,
        Sha256 = f.Sha256,
        CreatedAt = f.CreatedAt
    };

    private static ImportJobDto MapImportJob(Domain.Entities.ImportJob j) => new()
    {
        Id = j.Id.ToString(),
        WorkspaceId = j.WorkspaceId.ToString(),
        InboxItemId = j.InboxItemId.ToString(),
        SourceId = j.SourceId?.ToString(),
        JobType = j.JobType,
        Status = j.Status,
        Attempt = j.Attempt,
        MaxAttempts = j.MaxAttempts,
        StartedAt = j.StartedAt,
        FinishedAt = j.FinishedAt,
        ErrorCode = j.ErrorCode,
        ErrorMessage = j.ErrorMessage,
        CreatedAt = j.CreatedAt,
        UpdatedAt = j.UpdatedAt
    };

    private static InboxEventDto MapInboxEvent(Domain.Entities.InboxEvent e) => new()
    {
        Id = e.Id.ToString(),
        WorkspaceId = e.WorkspaceId.ToString(),
        InboxItemId = e.InboxItemId.ToString(),
        EventType = e.EventType,
        EventPayload = e.EventPayload,
        CreatedBy = e.CreatedBy,
        CreatedAt = e.CreatedAt
    };

    private static SyncCursorDto MapSyncCursor(Domain.Entities.SyncCursor c) => new()
    {
        Id = c.Id.ToString(),
        WorkspaceId = c.WorkspaceId.ToString(),
        RemoteWorkspaceId = c.RemoteWorkspaceId.ToString(),
        CursorType = c.CursorType,
        CursorValue = c.CursorValue,
        LastSyncedAt = c.LastSyncedAt,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static CloudInboxSyncLogDto MapCloudInboxSyncLog(Domain.Entities.CloudInboxSyncLog l) => new()
    {
        Id = l.Id.ToString(),
        WorkspaceId = l.WorkspaceId.ToString(),
        Direction = l.Direction,
        Status = l.Status,
        CloudApiBaseUrl = l.CloudApiBaseUrl,
        CloudWorkspaceId = l.CloudWorkspaceId,
        Retention = l.Retention,
        PulledCount = l.PulledCount,
        FailedCount = l.FailedCount,
        NextCursor = l.NextCursor,
        ErrorMessage = l.ErrorMessage,
        StartedAt = l.StartedAt,
        FinishedAt = l.FinishedAt,
        DurationMs = l.DurationMs,
        CreatedAt = l.CreatedAt
    };

    private static MobileDeviceDto MapMobileDevice(MobileDevice d) => new()
    {
        Id = d.Id.ToString(),
        WorkspaceId = d.WorkspaceId.ToString(),
        ClientId = d.ClientId,
        DeviceName = d.DeviceName,
        Platform = d.Platform,
        PushToken = d.PushToken,
        RefreshTokenExpiresAt = d.RefreshTokenExpiresAt,
        Status = d.Status,
        LastSeenAt = d.LastSeenAt,
        BoundAt = d.BoundAt,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };

    private static PushNotificationDto MapPushNotification(PushNotification n) => new()
    {
        Id = n.Id.ToString(),
        WorkspaceId = n.WorkspaceId.ToString(),
        ClientId = n.ClientId,
        PushToken = n.PushToken,
        Title = n.Title,
        Body = n.Body,
        DataJson = n.DataJson,
        Status = n.Status,
        Attempt = n.Attempt,
        MaxAttempts = n.MaxAttempts,
        ProviderResponse = n.ProviderResponse,
        ErrorMessage = n.ErrorMessage,
        NextAttemptAt = n.NextAttemptAt,
        SentAt = n.SentAt,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };

    private static SourceDto MapSource(Domain.Entities.Source s) => new()
    {
        Id = s.Id.ToString(),
        WorkspaceId = s.UserId.ToString(),
        TopicId = s.TopicId?.ToString(),
        SourceType = s.SourceType,
        Title = s.Title,
        Url = s.Url,
        Domain = s.Domain,
        ContentHash = s.ContentHash,
        Status = s.Status,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt
    };

    private static DocumentDto MapDocument(Domain.Entities.Document d) => new()
    {
        Id = d.Id.ToString(),
        WorkspaceId = d.UserId.ToString(),
        TopicId = d.TopicId?.ToString(),
        SourceId = d.SourceId.ToString(),
        Title = d.Title,
        ContentMarkdown = d.ContentMarkdown,
        ContentText = d.ContentText,
        Summary = d.Summary,
        TitleOriginal = d.TitleOriginal,
        TitleZh = d.TitleZh,
        PrimaryLanguage = d.PrimaryLanguage ?? d.Language,
        LanguageDistribution = d.LanguageDistribution,
        IsMultilingual = d.IsMultilingual,
        LocalizationStrategy = d.LocalizationStrategy,
        LocalizationLevel = d.LocalizationLevel,
        LanguageDetectStatus = d.LanguageDetectStatus,
        LocalizationStatus = d.LocalizationStatus,
        ContentHash = d.ContentHash,
        AiStatus = d.AiStatus,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };

    private static TagDto MapTag(Domain.Entities.Tag t) => new()
    {
        Id = t.Id.ToString(),
        WorkspaceId = t.WorkspaceId,
        Name = t.Name,
        NormalizedName = t.NormalizedName ?? t.Name.ToLower(),
        DisplayName = t.DisplayName ?? t.Name,
        TagType = t.TagType ?? t.Type ?? "custom",
        Description = t.Description,
        Color = t.Color,
        Aliases = t.Aliases,
        Source = t.Source,
        UsageCount = t.UsageCount,
        IsSystem = t.IsSystem,
        IsArchived = t.IsArchived,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    private static DocumentTagDto MapDocumentTag(Domain.Entities.DocumentTag dt, Domain.Entities.Tag? tag) => new()
    {
        Id = $"{dt.DocumentId}:{dt.TagId}",
        DocumentId = dt.DocumentId.ToString(),
        TagId = dt.TagId.ToString(),
        TagName = tag?.Name ?? "",
        TagType = tag?.TagType ?? tag?.Type ?? "custom",
        Source = dt.Source,
        Confidence = dt.Confidence,
        Reason = dt.Reason,
        IsConfirmed = dt.IsConfirmed,
        ConfirmedBy = dt.ConfirmedBy,
        ConfirmedAt = dt.ConfirmedAt,
        CreatedAt = dt.CreatedAt
    };

    private static EntityDto MapEntity(Domain.Entities.Entity e) => new()
    {
        Id = e.Id.ToString(),
        WorkspaceId = e.WorkspaceId,
        Name = e.Name,
        NormalizedName = e.NormalizedName ?? e.Name.ToLower(),
        DisplayName = e.DisplayName ?? e.Name,
        EntityType = e.EntityType,
        Aliases = e.Aliases,
        Description = e.Description,
        ExternalRef = e.ExternalRef,
        Source = e.Source,
        UsageCount = e.UsageCount,
        IsVerified = e.IsVerified,
        IsArchived = e.IsArchived,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    private static DocumentEntityDto MapDocumentEntity(Domain.Entities.DocumentEntity de, Domain.Entities.Entity? entity) => new()
    {
        Id = $"{de.DocumentId}:{de.EntityId}",
        DocumentId = de.DocumentId.ToString(),
        EntityId = de.EntityId.ToString(),
        EntityName = entity?.Name ?? "",
        EntityType = entity?.EntityType ?? "other",
        MentionCount = de.MentionCount,
        FirstMention = de.FirstMention,
        MentionExamples = de.MentionExamples,
        Importance = de.Importance,
        Role = de.Role,
        Sentiment = de.Sentiment,
        Source = entity?.Source ?? "ai",
        Confidence = de.Confidence,
        CreatedAt = de.CreatedAt,
        UpdatedAt = de.CreatedAt
    };

    private static ChunkDto MapChunk(Domain.Entities.DocumentChunk c) => new()
    {
        Id = c.Id.ToString(),
        DocumentId = c.DocumentId.ToString(),
        ChunkIndex = c.ChunkIndex,
        ChunkTitle = c.ChunkTitle,
        Content = c.Content,
        ContentOriginal = c.ContentOriginal,
        ContentNormalized = c.ContentNormalized,
        DetectedLanguage = c.DetectedLanguage,
        LanguageConfidence = c.LanguageConfidence.HasValue ? (double)c.LanguageConfidence.Value : null,
        LanguageDistribution = c.LanguageDistribution,
        ContentType = c.ContentType,
        ProcessingRoute = c.ProcessingRoute,
        LocalizationRequired = c.LocalizationRequired,
        ChunkGroupId = c.ChunkGroupId?.ToString(),
        ParentChunkId = c.ParentChunkId?.ToString(),
        TokenCount = c.TokenCount ?? 0,
        CharCount = c.CharCount ?? 0,
        ChunkUid = c.ChunkUid,
        HeadingPath = c.HeadingPath,
        SectionLevel = c.SectionLevel,
        ContentHash = c.ContentHash,
        PrevChunkId = c.PrevChunkId?.ToString(),
        NextChunkId = c.NextChunkId?.ToString(),
        PageStart = c.PageStart,
        PageEnd = c.PageEnd,
        IndexStatus = c.IndexStatus ?? "pending"
    };

    private static ChunkEmbeddingDto MapChunkEmbedding(Domain.Entities.ChunkEmbedding e) => new()
    {
        Id = e.Id.ToString(),
        WorkspaceId = e.WorkspaceId,
        ChunkId = e.ChunkId.ToString(),
        Provider = e.Provider,
        Model = e.Model,
        ModelVersion = e.ModelVersion,
        Dimension = e.Dimension ?? 0,
        Status = e.Status,
        ErrorMessage = e.ErrorMessage,
        RetryCount = e.RetryCount,
        ChunkContentHash = e.ChunkContentHash ?? string.Empty,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    private static VectorIndexStateDto MapVectorIndexState(Domain.Entities.VectorIndexState v) => new()
    {
        Id = v.Id.ToString(),
        WorkspaceId = v.WorkspaceId,
        Provider = v.Provider,
        Model = v.Model,
        Dimension = v.Dimension ?? 0,
        IndexBackend = v.IndexBackend,
        TotalChunks = v.TotalChunks,
        IndexedChunks = v.IndexedChunks,
        FailedChunks = v.FailedChunks,
        StaleChunks = v.StaleChunks,
        Status = v.Status,
        LastRebuiltAt = v.LastRebuiltAt,
        SchemaVersion = v.SchemaVersion ?? "v1",
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt
    };
}
