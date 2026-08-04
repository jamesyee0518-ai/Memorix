using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Scans historical memory items for sensitive data that may have been stored
/// before sanitization was enforced (or through bypass paths). Re-applies the
/// sanitizer to content and summary, redacting any detected secrets in-place,
/// and creates audit log entries for each modification.
///
/// P3.INF-04: Sensitive data scanner for historical memory.
/// </summary>
public class SensitiveDataScanner
{
    private readonly IAppDbContext _db;
    private readonly MemorySanitizer _sanitizer;
    private readonly ILogger<SensitiveDataScanner> _logger;

    public SensitiveDataScanner(
        IAppDbContext db,
        MemorySanitizer sanitizer,
        ILogger<SensitiveDataScanner> logger)
    {
        _db = db;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    /// <summary>
    /// Scans all (or workspace-filtered) memory items for sensitive data.
    /// For each item where the sanitizer detects and redacts sensitive content,
    /// the item is updated in the database and an audit log entry is created.
    /// </summary>
    /// <param name="workspaceId">Optional workspace filter. If null, scans all workspaces.</param>
    public async Task<ScanReport> ScanHistoricalMemoryAsync(
        Guid? workspaceId = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "ScanHistoricalMemory: starting scan for workspace {WorkspaceId}",
            workspaceId?.ToString() ?? "ALL");

        var query = _db.AgentMemoryItems.AsQueryable();

        if (workspaceId.HasValue)
        {
            query = query.Where(i => i.WorkspaceId == workspaceId.Value);
        }

        var items = await query.ToListAsync(ct);

        _logger.LogInformation(
            "ScanHistoricalMemory: scanning {Count} memory items", items.Count);

        var report = new ScanReport
        {
            TotalScanned = items.Count
        };

        var now = DateTime.UtcNow;
        var modifiedCount = 0;

        foreach (var item in items)
        {
            var wasModified = false;

            // Scan content
            if (!string.IsNullOrEmpty(item.Content))
            {
                var (sanitizedContent, contentModified) = await _sanitizer.SanitizeOnWriteAsync(item.Content, ct);
                if (contentModified)
                {
                    item.Content = sanitizedContent;
                    wasModified = true;
                    report.RedactionSummary.Add(
                        $"Item {item.Id}: content redacted");
                }
            }

            // Scan summary
            if (!string.IsNullOrEmpty(item.Summary))
            {
                var (sanitizedSummary, summaryModified) = await _sanitizer.SanitizeOnWriteAsync(item.Summary, ct);
                if (summaryModified)
                {
                    item.Summary = sanitizedSummary;
                    wasModified = true;
                    report.RedactionSummary.Add(
                        $"Item {item.Id}: summary redacted");
                }
            }

            if (wasModified)
            {
                item.UpdatedAt = now;
                modifiedCount++;

                // Create an audit log entry for the modification
                _db.AgentMemoryAccessLogs.Add(new AgentMemoryAccessLog
                {
                    Id = Guid.NewGuid(),
                    MemoryItemId = item.Id,
                    SessionId = item.SessionId,
                    AgentProfileId = item.AgentProfileId,
                    Action = "sanitize",
                    TraceId = $"sensitive-scan-{now:yyyyMMddHHmmss}",
                    CreatedAt = now
                });
            }
        }

        if (modifiedCount > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        report.ItemsModified = modifiedCount;

        _logger.LogInformation(
            "ScanHistoricalMemory: completed. Scanned={Scanned}, Modified={Modified}",
            report.TotalScanned, report.ItemsModified);

        return report;
    }
}

/// <summary>
/// Report from a sensitive data scan.
/// </summary>
public class ScanReport
{
    /// <summary>
    /// Total number of memory items scanned.
    /// </summary>
    public int TotalScanned { get; set; }

    /// <summary>
    /// Number of items that were modified (had sensitive data redacted).
    /// </summary>
    public int ItemsModified { get; set; }

    /// <summary>
    /// Summary of redactions performed, one entry per modified field.
    /// </summary>
    public List<string> RedactionSummary { get; set; } = new();
}
