using System.Diagnostics;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace KnowledgeEngine.Api.Middlewares;

public class AgentAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AgentAuthMiddleware> _logger;

    public AgentAuthMiddleware(RequestDelegate next, ILogger<AgentAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only process Agent API paths
        if (!path.StartsWith("/api/agent/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();

        // Extract Bearer token
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorResponse(context, 401, "API_KEY_INVALID", "Missing or invalid Authorization header. Use 'Bearer <api_key>'.");
            return;
        }

        var rawKey = authHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            await WriteErrorResponse(context, 401, "API_KEY_INVALID", "API key is empty.");
            return;
        }

        // Validate the API key
        var apiKeyService = context.RequestServices.GetRequiredService<IApiKeyService>();
        var apiKey = await apiKeyService.ValidateAsync(rawKey, context.RequestAborted);

        if (apiKey == null)
        {
            // Could be invalid key, disabled, or expired - check by prefix
            await WriteErrorResponse(context, 401, "API_KEY_INVALID", "API key is invalid, disabled, or expired.");
            return;
        }

        // Check rate limit
        var rateLimitOk = await apiKeyService.CheckRateLimitAsync(apiKey.Id, apiKey.RateLimitPerMinute, context.RequestAborted);
        if (!rateLimitOk)
        {
            sw.Stop();
            await WriteErrorResponse(context, 429, "API_RATE_LIMIT_EXCEEDED",
                $"Rate limit exceeded. Limit: {apiKey.RateLimitPerMinute} requests per minute.");
            return;
        }

        // Check daily quota
        var dailyQuotaOk = await apiKeyService.CheckDailyQuotaAsync(apiKey.Id, apiKey.DailyQuota, context.RequestAborted);
        if (!dailyQuotaOk)
        {
            sw.Stop();
            await WriteErrorResponse(context, 429, "API_DAILY_QUOTA_EXCEEDED",
                $"Daily quota exceeded. Limit: {apiKey.DailyQuota} requests per day.");
            return;
        }

        // Store userId and apiKeyId in HttpContext.Items
        context.Items["AgentUserId"] = apiKey.UserId;
        context.Items["AgentApiKeyId"] = apiKey.Id;
        context.Items["ApiKey"] = apiKey;

        // Capture for logging after request completes
        var requestStartTime = sw.ElapsedMilliseconds;

        // Execute the request
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();

            // Log the API call
            try
            {
                var log = new ApiCallLog
                {
                    Id = Guid.NewGuid(),
                    UserId = apiKey.UserId,
                    ApiKeyId = apiKey.Id,
                    Endpoint = path,
                    RequestMethod = context.Request.Method,
                    RequestSummary = null,
                    StatusCode = context.Response.StatusCode,
                    ErrorCode = context.Response.StatusCode >= 400 ? GetErrorCode(context.Response.StatusCode) : null,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    CreatedAt = DateTime.UtcNow
                };

                // Fire and forget - don't block the response
                _ = Task.Run(async () =>
                {
                    using var scope = context.RequestServices.CreateScope();
                    var scopedApiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
                    await scopedApiKeyService.LogApiCallAsync(log);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log API call for path {Path}", path);
            }
        }
    }

    private static string GetErrorCode(int statusCode)
    {
        return statusCode switch
        {
            401 => "API_KEY_INVALID",
            403 => "FORBIDDEN",
            404 => "NOT_FOUND",
            429 => "RATE_LIMITED",
            _ => statusCode >= 500 ? "INTERNAL_ERROR" : "ERROR"
        };
    }

    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string code, string message)
    {
        var traceId = context.Items.TryGetValue("trace_id", out var tid) ? tid?.ToString() : Guid.NewGuid().ToString("N");

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new
        {
            success = false,
            error = new { code, message },
            trace_id = traceId
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
