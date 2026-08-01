using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Infrastructure.Audio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Audio provider usage and cost analytics API.
/// Provides per-user usage records, summary aggregations, and by-provider
/// breakdowns for billing, audit, and cost analysis.
/// </summary>
[ApiController]
[Route("api/audio/usage")]
[Authorize]
public class AudioUsageController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ProviderUsageMeteringService _meteringService;
    private readonly ILogger<AudioUsageController> _logger;

    public AudioUsageController(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ProviderUsageMeteringService meteringService,
        ILogger<AudioUsageController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _meteringService = meteringService;
        _logger = logger;
    }

    /// <summary>
    /// Gets usage records for the current user within an optional date range.
    /// Results are ordered by creation time ascending.
    /// </summary>
    /// <param name="from">Start of the date range (inclusive). Defaults to 30 days ago.</param>
    /// <param name="to">End of the date range (inclusive). Defaults to now.</param>
    /// <param name="limit">Maximum number of records to return (1-500).</param>
    /// <param name="offset">Number of records to skip for pagination.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> GetUsage(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var records = await _db.ProviderUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= fromDate && r.CreatedAt <= toDate)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(r => new ProviderUsageRecordDto
            {
                Id = r.Id,
                TenantId = r.TenantId,
                UserId = r.UserId,
                WorkspaceId = r.WorkspaceId,
                Capability = r.Capability,
                ProviderId = r.ProviderId,
                ModelId = r.ModelId,
                CredentialMode = r.CredentialMode,
                ExecutionMode = r.ExecutionMode,
                DurationMs = r.DurationMs,
                RequestCount = r.RequestCount,
                InputUnits = r.InputUnits,
                OutputUnits = r.OutputUnits,
                EstimatedCost = r.EstimatedCost,
                ActualCost = r.ActualCost,
                Status = r.Status,
                ErrorMessage = r.ErrorMessage,
                TranscriptionJobId = r.TranscriptionJobId,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<List<ProviderUsageRecordDto>>.Ok(records, GetTraceId()));
    }

    /// <summary>
    /// Gets a usage summary for the current user: total cost, total duration,
    /// total request count, and a per-provider breakdown.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetUsageSummary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var records = await _db.ProviderUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= fromDate && r.CreatedAt <= toDate)
            .Select(r => new
            {
                r.ProviderId,
                r.DurationMs,
                r.RequestCount,
                Cost = r.ActualCost ?? r.EstimatedCost ?? 0m,
                r.Status
            })
            .ToListAsync(ct);

        var summary = new AudioUsageSummary
        {
            TotalCost = records.Sum(r => r.Cost),
            TotalDurationMs = records.Sum(r => r.DurationMs),
            TotalRequests = records.Sum(r => r.RequestCount),
            RecordCount = records.Count,
            From = fromDate,
            To = toDate,
            ByProvider = records
                .GroupBy(r => r.ProviderId)
                .Select(g => new ProviderUsageBreakdown
                {
                    ProviderId = g.Key,
                    Cost = g.Sum(r => r.Cost),
                    DurationMs = g.Sum(r => r.DurationMs),
                    RequestCount = g.Sum(r => r.RequestCount),
                    RecordCount = g.Count()
                })
                .OrderByDescending(b => b.Cost)
                .ToList()
        };

        return Ok(ApiResponse<AudioUsageSummary>.Ok(summary, GetTraceId()));
    }

    /// <summary>
    /// Gets a per-provider usage breakdown for the current user.
    /// Each entry includes total cost, duration, and request count for one provider.
    /// </summary>
    [HttpGet("by-provider")]
    public async Task<IActionResult> GetUsageByProvider(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var breakdown = await _db.ProviderUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= fromDate && r.CreatedAt <= toDate)
            .GroupBy(r => r.ProviderId)
            .Select(g => new ProviderUsageBreakdown
            {
                ProviderId = g.Key,
                Cost = g.Sum(r => r.ActualCost ?? r.EstimatedCost ?? 0m),
                DurationMs = g.Sum(r => r.DurationMs),
                RequestCount = g.Sum(r => r.RequestCount),
                RecordCount = g.Count()
            })
            .OrderByDescending(b => b.Cost)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<ProviderUsageBreakdown>>.Ok(breakdown, GetTraceId()));
    }

    /// <summary>
    /// Gets a per-capability usage breakdown for the current user.
    /// Groups usage records by capability (e.g. audio.transcription, audio.synthesis).
    /// </summary>
    [HttpGet("by-capability")]
    public async Task<IActionResult> GetUsageByCapability(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var breakdown = await _db.ProviderUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= fromDate && r.CreatedAt <= toDate)
            .GroupBy(r => r.Capability)
            .Select(g => new CapabilityUsageBreakdown
            {
                Capability = g.Key,
                Cost = g.Sum(r => r.ActualCost ?? r.EstimatedCost ?? 0m),
                DurationMs = g.Sum(r => r.DurationMs),
                RequestCount = g.Sum(r => r.RequestCount),
                RecordCount = g.Count()
            })
            .OrderByDescending(b => b.Cost)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<CapabilityUsageBreakdown>>.Ok(breakdown, GetTraceId()));
    }

    /// <summary>
    /// Gets daily usage chart data for the current user.
    /// Each entry represents one day's aggregated cost, duration, and request count.
    /// </summary>
    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyUsage(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        // Fetch raw records and group in memory to ensure cross-provider compatibility
        var records = await _db.ProviderUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= fromDate && r.CreatedAt <= toDate)
            .Select(r => new
            {
                r.CreatedAt,
                r.DurationMs,
                r.RequestCount,
                Cost = r.ActualCost ?? r.EstimatedCost ?? 0m
            })
            .ToListAsync(ct);

        var daily = records
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new DailyUsageBreakdown
            {
                Date = g.Key,
                Cost = g.Sum(r => r.Cost),
                DurationMs = g.Sum(r => r.DurationMs),
                RequestCount = g.Sum(r => r.RequestCount),
                RecordCount = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        return Ok(ApiResponse<List<DailyUsageBreakdown>>.Ok(daily, GetTraceId()));
    }
}

/// <summary>
/// Aggregated usage summary for a user within a date range.
/// </summary>
public class AudioUsageSummary
{
    /// <summary>Total cost across all providers (uses ActualCost when available, else EstimatedCost).</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Total audio duration processed, in milliseconds.</summary>
    public long TotalDurationMs { get; set; }

    /// <summary>Total number of provider requests.</summary>
    public int TotalRequests { get; set; }

    /// <summary>Total number of usage records.</summary>
    public int RecordCount { get; set; }

    /// <summary>Start of the summary period.</summary>
    public DateTime From { get; set; }

    /// <summary>End of the summary period.</summary>
    public DateTime To { get; set; }

    /// <summary>Per-provider breakdown.</summary>
    public List<ProviderUsageBreakdown> ByProvider { get; set; } = new();
}

/// <summary>
/// Usage breakdown for a single provider.
/// </summary>
public class ProviderUsageBreakdown
{
    /// <summary>Provider identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Total cost for this provider.</summary>
    public decimal Cost { get; set; }

    /// <summary>Total audio duration for this provider, in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Total request count for this provider.</summary>
    public int RequestCount { get; set; }

    /// <summary>Number of usage records for this provider.</summary>
    public int RecordCount { get; set; }
}

/// <summary>
/// Usage breakdown for a single capability (e.g. transcription, synthesis).
/// </summary>
public class CapabilityUsageBreakdown
{
    /// <summary>Capability identifier (audio.transcription, audio.synthesis, etc.).</summary>
    public string Capability { get; set; } = string.Empty;

    /// <summary>Total cost for this capability.</summary>
    public decimal Cost { get; set; }

    /// <summary>Total audio duration for this capability, in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Total request count for this capability.</summary>
    public int RequestCount { get; set; }

    /// <summary>Number of usage records for this capability.</summary>
    public int RecordCount { get; set; }
}

/// <summary>
/// Daily usage aggregation for charting.
/// </summary>
public class DailyUsageBreakdown
{
    /// <summary>The calendar date (UTC) for this aggregation bucket.</summary>
    public DateTime Date { get; set; }

    /// <summary>Total cost on this date.</summary>
    public decimal Cost { get; set; }

    /// <summary>Total audio duration on this date, in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Total request count on this date.</summary>
    public int RequestCount { get; set; }

    /// <summary>Number of usage records on this date.</summary>
    public int RecordCount { get; set; }
}
