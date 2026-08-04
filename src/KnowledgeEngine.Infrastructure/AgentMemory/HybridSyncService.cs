using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Handles hybrid (local + cloud) synchronization of UserPortable memory items.
/// UserPortable memories follow the user across workspaces and devices.
///
/// P3.INF-01: Cross-end sync for user-portable memories.
/// </summary>
public class HybridSyncService
{
    private readonly IAppDbContext _db;
    private readonly MemorySanitizer _sanitizer;
    private readonly ILogger<HybridSyncService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HybridSyncService(
        IAppDbContext db,
        MemorySanitizer sanitizer,
        ILogger<HybridSyncService> logger)
    {
        _db = db;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    /// <summary>
    /// Exports all UserPortable + Confirmed memory items for a user as a sanitized JSON blob.
    /// Double sanitization is applied (once on content, once on summary) to ensure no
    /// sensitive data leaks through the export channel.
    /// </summary>
    public async Task<ExportResult> ExportUserPortableMemoryAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var items = await _db.AgentMemoryItems
            .Where(i => i.OwnerUserId == userId
                        && i.WorkspaceId == workspaceId
                        && i.Visibility == Visibility.UserPortable
                        && i.AdmissionState == AdmissionState.Confirmed)
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);

        _logger.LogInformation(
            "ExportUserPortableMemory: found {Count} confirmed UserPortable items for user {UserId} in workspace {WorkspaceId}",
            items.Count, userId, workspaceId);

        var exportItems = new List<PortableMemoryExport>(items.Count);

        foreach (var item in items)
        {
            // Double sanitization: apply sanitizer to content and summary
            var (sanitizedContent, _) = await _sanitizer.SanitizeOnWriteAsync(item.Content, ct);
            var sanitizedSummary = item.Summary;
            if (!string.IsNullOrEmpty(sanitizedSummary))
            {
                var (sanitized, _) = await _sanitizer.SanitizeOnWriteAsync(sanitizedSummary, ct);
                sanitizedSummary = sanitized;
            }

            exportItems.Add(new PortableMemoryExport
            {
                Kind = item.Kind.ToString(),
                Title = item.Title,
                Content = sanitizedContent,
                Summary = sanitizedSummary,
                Confidence = item.Confidence,
                Importance = item.Importance,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                SourceWorkspaceId = item.WorkspaceId
            });
        }

        var json = JsonSerializer.Serialize(exportItems, JsonOptions);

        _logger.LogInformation(
            "ExportUserPortableMemory: serialized {Count} items into {Bytes} bytes of JSON",
            exportItems.Count, json.Length);

        return new ExportResult
        {
            ItemCount = exportItems.Count,
            JsonContent = json
        };
    }

    /// <summary>
    /// Syncs UserPortable + Active memory items to the cloud (stub implementation).
    /// In production, this would push to a cloud sync API. For now, it logs what
    /// would be synced and applies sanitization before the (simulated) upload.
    /// </summary>
    /// <returns>True on success.</returns>
    public async Task<bool> SyncToCloudAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var items = await _db.AgentMemoryItems
            .Where(i => i.OwnerUserId == userId
                        && i.WorkspaceId == workspaceId
                        && i.Visibility == Visibility.UserPortable
                        && i.Status == MemoryStatus.Active)
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);

        _logger.LogInformation(
            "SyncToCloud: preparing to sync {Count} UserPortable active items for user {UserId} in workspace {WorkspaceId}",
            items.Count, userId, workspaceId);

        foreach (var item in items)
        {
            // Apply sanitization before sync
            var (sanitizedContent, wasModified) = await _sanitizer.SanitizeOnWriteAsync(item.Content, ct);

            if (wasModified)
            {
                _logger.LogWarning(
                    "SyncToCloud: item {ItemId} contained sensitive data, sanitized before sync",
                    item.Id);
            }

            _logger.LogDebug(
                "SyncToCloud: [stub] would sync item {ItemId} ('{Title}') - sanitized={Sanitized}",
                item.Id, item.Title, wasModified);
        }

        _logger.LogInformation(
            "SyncToCloud: completed (stub) sync of {Count} items for user {UserId}",
            items.Count, userId);

        return true;
    }

    /// <summary>
    /// Imports memory items from an export JSON blob into the target workspace.
    /// Each item is re-created with a new ID and sanitized on import.
    /// </summary>
    /// <returns>Count of imported items.</returns>
    public async Task<int> ImportFromExportAsync(
        Guid userId,
        Guid workspaceId,
        string exportJson,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exportJson))
        {
            _logger.LogWarning("ImportFromExport: empty export JSON provided");
            return 0;
        }

        List<PortableMemoryExport> exportItems;
        try
        {
            exportItems = JsonSerializer.Deserialize<List<PortableMemoryExport>>(exportJson, JsonOptions)
                          ?? new List<PortableMemoryExport>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "ImportFromExport: failed to parse export JSON: {Message}", ex.Message);
            throw;
        }

        if (exportItems.Count == 0)
        {
            _logger.LogInformation("ImportFromExport: no items found in export JSON");
            return 0;
        }

        _logger.LogInformation(
            "ImportFromExport: importing {Count} items into workspace {WorkspaceId} for user {UserId}",
            exportItems.Count, workspaceId, userId);

        var now = DateTime.UtcNow;
        var importedCount = 0;

        foreach (var export in exportItems)
        {
            // Parse the Kind enum; skip invalid values
            if (!Enum.TryParse<MemoryKind>(export.Kind, ignoreCase: true, out var kind))
            {
                _logger.LogWarning("ImportFromExport: skipping item with invalid Kind '{Kind}'", export.Kind);
                continue;
            }

            // Apply sanitization on import
            var (sanitizedContent, _) = await _sanitizer.SanitizeOnWriteAsync(export.Content ?? string.Empty, ct);
            var sanitizedSummary = export.Summary;
            if (!string.IsNullOrEmpty(sanitizedSummary))
            {
                var (sanitized, _) = await _sanitizer.SanitizeOnWriteAsync(sanitizedSummary, ct);
                sanitizedSummary = sanitized;
            }

            var item = new AgentMemoryItem
            {
                Id = Guid.NewGuid(),
                SessionId = null,
                WorkspaceId = workspaceId,
                OwnerUserId = userId,
                AgentProfileId = null,
                Kind = kind,
                Title = export.Title ?? string.Empty,
                Content = sanitizedContent,
                Summary = sanitizedSummary,
                AdmissionState = AdmissionState.Confirmed, // Imported items are pre-confirmed
                Confidence = export.Confidence,
                Visibility = Visibility.UserPortable,
                Importance = export.Importance,
                FreshnessAt = now,
                Status = MemoryStatus.Active,
                CreatedAt = export.CreatedAt != default ? export.CreatedAt : now,
                UpdatedAt = now
            };

            _db.AgentMemoryItems.Add(item);
            importedCount++;
        }

        if (importedCount > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "ImportFromExport: successfully imported {Count}/{Total} items into workspace {WorkspaceId}",
            importedCount, exportItems.Count, workspaceId);

        return importedCount;
    }
}

/// <summary>
/// Result of a memory export operation.
/// </summary>
public class ExportResult
{
    /// <summary>
    /// Number of memory items included in the export.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// The serialized JSON content of the export.
    /// </summary>
    public string JsonContent { get; set; } = string.Empty;
}

/// <summary>
/// DTO representing a single portable memory item in the export/import JSON.
/// </summary>
public class PortableMemoryExport
{
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public decimal Confidence { get; set; }
    public int Importance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid SourceWorkspaceId { get; set; }
}
