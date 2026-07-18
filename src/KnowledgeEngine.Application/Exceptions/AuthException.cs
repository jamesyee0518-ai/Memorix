namespace KnowledgeEngine.Application.Exceptions;

public class AuthException : AppException
{
    public AuthException(string message) : base("AUTH_ERROR", message)
    {
    }
}

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string message = "Unauthorized") : base("UNAUTHORIZED", message)
    {
    }
}
