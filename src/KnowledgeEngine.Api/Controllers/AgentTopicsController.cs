using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Route("api/agent/topics")]
public class AgentTopicsController : AgentApiControllerBase
{
    private readonly IAppDbContext _db;

    public AgentTopicsController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetTopics(CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var apiKey = ApiKey!;

        // Get all active topics for the user
        var topics = await _db.Topics
            .Where(t => t.UserId == userId && t.Status == "active")
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        // Filter by allowed topic IDs if set
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
            topics = topics.Where(t => allowedTopicIds.Contains(t.Id)).ToList();
        }

        // Get document counts and report counts for each topic
        var topicIds = topics.Select(t => t.Id).ToList();

        var docCounts = await _db.Documents
            .Where(d => d.UserId == userId && d.TopicId.HasValue && topicIds.Contains(d.TopicId.Value))
            .GroupBy(d => d.TopicId!.Value)
            .Select(g => new { TopicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        var reportCounts = await _db.Reports
            .Where(r => r.UserId == userId && r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value))
            .GroupBy(r => r.TopicId!.Value)
            .Select(g => new { TopicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        var items = topics.Select(t => new AgentTopicItem
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Domain = t.Domain,
            Status = t.Status,
            DocumentCount = docCounts.TryGetValue(t.Id, out var dc) ? dc : 0,
            ReportCount = reportCounts.TryGetValue(t.Id, out var rc) ? rc : 0,
            CreatedAt = t.CreatedAt
        }).ToList();

        var traceId = GetTraceId();

        return Ok(new
        {
            items,
            trace_id = traceId
        });
    }
}
