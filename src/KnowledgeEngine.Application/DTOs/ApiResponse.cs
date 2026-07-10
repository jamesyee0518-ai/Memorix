namespace KnowledgeEngine.Application.DTOs;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public ApiError? Error { get; set; }
    public string? TraceId { get; set; }

    public static ApiResponse<T> Ok(T data, string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            TraceId = traceId
        };
    }

    public static ApiResponse<T> Fail(string code, string message, string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = new ApiError { Code = code, Message = message },
            TraceId = traceId
        };
    }

    public static ApiResponse<object> FailObject(string code, string message, string? traceId = null)
    {
        return new ApiResponse<object>
        {
            Success = false,
            Error = new ApiError { Code = code, Message = message },
            TraceId = traceId
        };
    }
}

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
