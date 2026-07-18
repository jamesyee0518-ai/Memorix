namespace KnowledgeEngine.Application.Exceptions;

public class AppException : Exception
{
    public string Code { get; set; }

    public AppException(string code, string message) : base(message)
    {
        Code = code;
    }

    public AppException(string code, string message, Exception innerException) : base(message, innerException)
    {
        Code = code;
    }
}
