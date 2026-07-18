using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Route("api/agent/reports")]
public class AgentReportsController : AgentApiControllerBase
{
    private readonly IAppDbContext _db;

    public AgentReportsController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetReports(
        [FromQuery] Guid? topicId,
        [FromQuery] string? reportType,
        CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("reports:read"))
        {
            return Forbidden("ACTION_NOT_ALLOWED", "This API key does not have permission for 'reports:read'.");
        }

        // Check topic permission
        if (topicId.HasValue && !CheckTopicAllowed(topicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to the requested topic.");
        }

        var query = _db.Reports
            .Where(r => r.UserId == userId && (r.Status == "done" || r.Status == "completed"));

        if (topicId.HasValue)
        {
            query = query.Where(r => r.TopicId == topicId.Value);
        }

        if (!string.IsNullOrWhiteSpace(reportType))
        {
            query = query.Where(r => r.ReportType == reportType);
        }

        // Filter by allowed topic IDs from API key
        var apiKey = ApiKey!;
        List<Guid>? allowedTopicIds = null;
        if (!string.IsNullOrWhiteSpace(apiKey.AllowedTopicIds))
        {
            try
            {
                allowedTopicIds = JsonSerializer.Deserialize<List<Guid>>(apiKey.AllowedTopicIds);
            }
            catch { }
        }

        if (allowedTopicIds != null && allowedTopicIds.Count > 0)
        {
            query = query.Where(r => r.TopicId.HasValue && allowedTopicIds.Contains(r.TopicId.Value));
        }

        var reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var items = reports.Select(r => new AgentReportListItem
        {
            Id = r.Id,
            TopicId = r.TopicId,
            ReportType = r.ReportType,
            Title = r.Title,
            Status = r.Status,
            QualityScore = r.QualityScore,
            GeneratedByModel = r.GeneratedByModel,
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            CreatedAt = r.CreatedAt
        }).ToList();

        var traceId = GetTraceId();

        return Ok(new
        {
            items,
            trace_id = traceId
        });
    }

    [HttpGet("{reportId:guid}")]
    public async Task<IActionResult> GetReport([FromRoute] Guid reportId, CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("reports:read"))
        {
            return Forbidden("ACTION_NOT_ALLOWED", "This API key does not have permission for 'reports:read'.");
        }

        var report = await _db.Reports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.UserId == userId, ct);

        if (report == null)
        {
            return NotFound(new
            {
                success = false,
                error = new { code = "REPORT_NOT_FOUND", message = "Report not found." },
                trace_id = GetTraceId()
            });
        }

        // Check topic permission
        if (report.TopicId.HasValue && !CheckTopicAllowed(report.TopicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to this report's topic.");
        }

        var result = new AgentReportDetail
        {
            Id = report.Id,
            TopicId = report.TopicId,
            ReportType = report.ReportType,
            Title = report.Title,
            ContentMarkdown = report.ContentMarkdown ?? string.Empty,
            Query = report.Query,
            StartDate = report.StartDate,
            EndDate = report.EndDate,
            GeneratedByModel = report.GeneratedByModel,
            Status = report.Status,
            QualityScore = report.QualityScore,
            CreatedAt = report.CreatedAt,
            UpdatedAt = report.UpdatedAt,
            TraceId = GetTraceId()
        };

        return Ok(new
        {
            id = result.Id,
            topic_id = result.TopicId,
            report_type = result.ReportType,
            title = result.Title,
            content_markdown = result.ContentMarkdown,
            query = result.Query,
            start_date = result.StartDate,
            end_date = result.EndDate,
            generated_by_model = result.GeneratedByModel,
            status = result.Status,
            quality_score = result.QualityScore,
            created_at = result.CreatedAt,
            updated_at = result.UpdatedAt,
            trace_id = result.TraceId
        });
    }
}
