using System.Diagnostics;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Route("api/agent/search")]
public class AgentSearchController : AgentApiControllerBase
{
    private readonly ISearchService _searchService;
    private readonly IUsageService _usageService;

    public AgentSearchController(ISearchService searchService, IUsageService usageService)
    {
        _searchService = searchService;
        _usageService = usageService;
    }

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] AgentSearchRequest request, CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("search:query"))
        {
            return Forbidden("ACTION_NOT_ALLOWED", "This API key does not have permission for 'search:query'.");
        }

        // Check topic permission
        if (request.TopicId.HasValue && !CheckTopicAllowed(request.TopicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to the requested topic.");
        }

        // Limit max results
        if (request.Limit <= 0)
        {
            request.Limit = 10;
        }
        if (request.Limit > 20)
        {
            request.Limit = 20;
        }

        var sw = Stopwatch.StartNew();

        // Call the existing SearchService
        var searchRequest = new SearchRequest
        {
            TopicId = request.TopicId,
            Query = request.Query,
            SearchType = string.IsNullOrWhiteSpace(request.SearchType) ? "hybrid" : request.SearchType,
            Limit = request.Limit
        };

        var result = await _searchService.SearchAsync(userId.Value, searchRequest, ct);

        sw.Stop();

        if (!result.Success)
        {
            var traceId = GetTraceId();
            return Ok(new
            {
                success = false,
                error = new { code = result.Error!.Code, message = result.Error!.Message },
                trace_id = traceId
            });
        }

        // Map to Agent format
        var searchResult = result.Data!;
        var items = searchResult.Items.Select(s => new AgentSearchResultItem
        {
            DocumentId = s.DocumentId,
            ChunkId = s.ChunkId,
            Title = s.Title,
            Snippet = s.Snippet,
            SourceType = s.SourceType,
            SourceUrl = s.SourceUrl,
            SourceDomain = s.SourceDomain,
            PublishedAt = s.PublishedAt,
            ValueScore = s.ValueScore,
            Score = s.Score
        }).ToList();

        var agentResult = new AgentSearchResult
        {
            Items = items,
            Metadata = new AgentSearchMetadata
            {
                Total = searchResult.Total,
                LatencyMs = (int)sw.ElapsedMilliseconds
            },
            TraceId = GetTraceId()
        };

        // Record usage (fire and forget)
        _ = _usageService.RecordUsageAsync(userId.Value, UsageType.ApiCall, 1, ct);
        _ = _usageService.RecordUsageAsync(userId.Value, UsageType.Search, 1, ct);

        return Ok(new
        {
            items = agentResult.Items,
            metadata = agentResult.Metadata,
            trace_id = agentResult.TraceId
        });
    }
}
