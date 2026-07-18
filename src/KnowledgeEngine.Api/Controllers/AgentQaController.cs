using System.Diagnostics;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Route("api/agent/qa")]
public class AgentQaController : AgentApiControllerBase
{
    private readonly IQaService _qaService;
    private readonly IUsageService _usageService;

    public AgentQaController(IQaService qaService, IUsageService usageService)
    {
        _qaService = qaService;
        _usageService = usageService;
    }

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AgentQaRequest request, CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("qa:ask"))
        {
            return Forbidden("ACTION_NOT_ALLOWED", "This API key does not have permission for 'qa:ask'.");
        }

        // Check topic permission
        if (request.TopicId.HasValue && !CheckTopicAllowed(request.TopicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to the requested topic.");
        }

        var sw = Stopwatch.StartNew();

        // Step 1: Create a session (Agent API is stateless - each call creates a new session)
        var createSessionRequest = new CreateQaSessionRequest
        {
            TopicId = request.TopicId,
            Title = $"Agent API: {Truncate(request.Query, 50)}"
        };

        var sessionResult = await _qaService.CreateSessionAsync(userId.Value, createSessionRequest, ct);
        if (!sessionResult.Success)
        {
            sw.Stop();
            var traceId = GetTraceId();
            return Ok(new
            {
                success = false,
                error = new { code = sessionResult.Error!.Code, message = sessionResult.Error!.Message },
                trace_id = traceId
            });
        }

        var sessionId = sessionResult.Data!.Id;

        // Step 2: Ask the question
        var askRequest = new QaAskRequest
        {
            SessionId = sessionId,
            Query = request.Query,
            TopicId = request.TopicId
        };

        var qaResult = await _qaService.AskAsync(userId.Value, askRequest, ct);

        sw.Stop();

        if (!qaResult.Success)
        {
            var traceId = GetTraceId();
            return Ok(new
            {
                success = false,
                error = new { code = qaResult.Error!.Code, message = qaResult.Error!.Message },
                trace_id = traceId
            });
        }

        var answer = qaResult.Data!;

        // Map to Agent format
        var citations = answer.Citations.Select(c => new AgentQaCitation
        {
            Index = c.Index,
            DocumentId = c.DocumentId,
            ChunkId = c.ChunkId,
            Title = c.Title,
            SourceUrl = c.SourceUrl,
            SourceDomain = c.SourceDomain,
            SourceType = c.SourceType,
            Snippet = c.Snippet,
            Score = c.Score
        }).ToList();

        // Build warnings if applicable
        var warnings = new List<AgentWarning>();
        if (answer.Retrieval != null && answer.Retrieval.RetrievedCount == 0)
        {
            warnings.Add(new AgentWarning
            {
                Code = "NO_RESULTS",
                Message = "No relevant documents found in the knowledge base."
            });
        }
        else if (answer.Retrieval != null && answer.Retrieval.TopScore < 0.25)
        {
            warnings.Add(new AgentWarning
            {
                Code = "LOW_RELEVANCE",
                Message = "Retrieved documents have low relevance scores. Use with caution."
            });
        }

        var result = new AgentQaResult
        {
            Answer = answer.Answer,
            Citations = citations,
            Metadata = new AgentQaMetadata
            {
                TopicId = request.TopicId,
                RetrievedCount = answer.Retrieval?.RetrievedCount ?? 0,
                UsedCount = answer.Retrieval?.UsedCount ?? 0,
                Model = answer.Model,
                LatencyMs = (int)sw.ElapsedMilliseconds
            },
            Warnings = warnings,
            TraceId = GetTraceId()
        };

        // Record usage (fire and forget)
        _ = _usageService.RecordUsageAsync(userId.Value, UsageType.ApiCall, 1, ct);
        _ = _usageService.RecordUsageAsync(userId.Value, UsageType.Qa, 1, ct);
        if (answer.InputTokens.HasValue || answer.OutputTokens.HasValue)
        {
            _ = _usageService.RecordTokensAsync(
                userId.Value,
                answer.InputTokens ?? 0,
                answer.OutputTokens ?? 0,
                ct);
        }

        return Ok(new
        {
            answer = result.Answer,
            citations = result.Citations,
            metadata = result.Metadata,
            warnings = result.Warnings,
            trace_id = result.TraceId
        });
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Length <= maxLen ? s : s.Substring(0, maxLen);
    }
}
