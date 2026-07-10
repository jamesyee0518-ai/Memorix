using System.Text.Json;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[AllowAnonymous]
public abstract class AgentApiControllerBase : ControllerBase
{
    /// <summary>
    /// Gets the authenticated Agent user ID from HttpContext.Items (set by AgentAuthMiddleware).
    /// </summary>
    protected Guid? AgentUserId
    {
        get
        {
            if (HttpContext.Items.TryGetValue("AgentUserId", out var uid) && uid is Guid userId)
            {
                return userId;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the authenticated Agent API Key ID from HttpContext.Items.
    /// </summary>
    protected Guid? AgentApiKeyId
    {
        get
        {
            if (HttpContext.Items.TryGetValue("AgentApiKeyId", out var kid) && kid is Guid apiKeyId)
            {
                return apiKeyId;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the full ApiKey object from HttpContext.Items.
    /// </summary>
    protected ApiKey? ApiKey
    {
        get
        {
            if (HttpContext.Items.TryGetValue("ApiKey", out var key) && key is ApiKey apiKey)
            {
                return apiKey;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the trace_id from HttpContext.Items.
    /// </summary>
    protected string? GetTraceId()
    {
        return HttpContext.Items.TryGetValue("trace_id", out var traceId) ? traceId?.ToString() : null;
    }

    /// <summary>
    /// Checks if a specific action is allowed by the API key's AllowedActions.
    /// If AllowedActions is null/empty, all actions are allowed.
    /// </summary>
    protected bool CheckActionAllowed(string action)
    {
        var apiKey = ApiKey;
        if (apiKey == null)
            return false;

        if (string.IsNullOrWhiteSpace(apiKey.AllowedActions))
            return true; // No restriction = all allowed

        var allowedActions = DeserializeStringList(apiKey.AllowedActions);
        if (allowedActions == null || allowedActions.Count == 0)
            return true;

        return allowedActions.Contains(action, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a specific topic is allowed by the API key's AllowedTopicIds.
    /// If AllowedTopicIds is null/empty, all topics are allowed.
    /// </summary>
    protected bool CheckTopicAllowed(Guid? topicId)
    {
        var apiKey = ApiKey;
        if (apiKey == null)
            return false;

        // If no topic specified, allow (will be filtered later)
        if (topicId == null)
            return true;

        if (string.IsNullOrWhiteSpace(apiKey.AllowedTopicIds))
            return true; // No restriction = all allowed

        var allowedTopics = DeserializeGuidList(apiKey.AllowedTopicIds);
        if (allowedTopics == null || allowedTopics.Count == 0)
            return true;

        return allowedTopics.Contains(topicId.Value);
    }

    /// <summary>
    /// Returns a 403 Forbidden response with the specified error code and message.
    /// </summary>
    protected IActionResult Forbidden(string code, string message)
    {
        var traceId = GetTraceId();
        return StatusCode(403, new
        {
            success = false,
            error = new { code, message },
            trace_id = traceId
        });
    }

    private static List<string>? DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static List<Guid>? DeserializeGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json);
        }
        catch
        {
            return null;
        }
    }
}
