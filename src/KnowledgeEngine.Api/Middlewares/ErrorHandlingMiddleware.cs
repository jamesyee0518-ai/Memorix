using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;

namespace KnowledgeEngine.Api.Middlewares;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.Items.TryGetValue("trace_id", out var tid) ? tid?.ToString() : Guid.NewGuid().ToString("N");

        string code;
        string message;
        int statusCode;

        switch (exception)
        {
            case ValidationException validationEx:
                code = validationEx.Code;
                message = validationEx.Message;
                statusCode = StatusCodes.Status400BadRequest;
                _logger.LogWarning(exception, "Validation error: {Message} | TraceId: {TraceId}", message, traceId);
                break;

            case NotFoundException:
                code = exception is AppException appEx1 ? appEx1.Code : "NOT_FOUND";
                message = exception.Message;
                statusCode = StatusCodes.Status404NotFound;
                _logger.LogWarning(exception, "Not found: {Message} | TraceId: {TraceId}", message, traceId);
                break;

            case DuplicateException:
                code = "DUPLICATE";
                message = exception.Message;
                statusCode = StatusCodes.Status409Conflict;
                _logger.LogWarning(exception, "Duplicate: {Message} | TraceId: {TraceId}", message, traceId);
                break;

            case AuthException:
                code = "AUTH_ERROR";
                message = exception.Message;
                statusCode = StatusCodes.Status401Unauthorized;
                _logger.LogWarning(exception, "Auth error: {Message} | TraceId: {TraceId}", message, traceId);
                break;

            case UnauthorizedException:
                code = "UNAUTHORIZED";
                message = exception.Message;
                statusCode = StatusCodes.Status401Unauthorized;
                _logger.LogWarning(exception, "Unauthorized: {Message} | TraceId: {TraceId}", message, traceId);
                break;

            case AppException appEx:
                code = appEx.Code;
                message = appEx.Message;
                statusCode = StatusCodes.Status400BadRequest;
                _logger.LogWarning(exception, "App error [{Code}]: {Message} | TraceId: {TraceId}", code, message, traceId);
                break;

            default:
                code = "INTERNAL_ERROR";
                message = "An internal server error occurred";
                statusCode = StatusCodes.Status500InternalServerError;
                _logger.LogError(exception, "Unhandled exception: {Message} | TraceId: {TraceId}", exception.Message, traceId);
                break;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new
        {
            success = false,
            error = new ApiError { Code = code, Message = message },
            trace_id = traceId
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
