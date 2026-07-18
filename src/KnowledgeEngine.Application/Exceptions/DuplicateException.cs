namespace KnowledgeEngine.Application.Exceptions;

public class DuplicateException : AppException
{
    public DuplicateException(string message) : base("DUPLICATE", message)
    {
    }
}
