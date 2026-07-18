using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace KnowledgeEngine.Api.Middlewares;

public class TraceIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string TraceIdHeader = "X-Trace-Id";

    public TraceIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = context.Request.Headers[TraceIdHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(traceId))
        {
            traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        }

        context.Items["trace_id"] = traceId;
        context.Response.Headers[TraceIdHeader] = traceId;

        await _next(context);
    }
}
