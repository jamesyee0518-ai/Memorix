namespace KnowledgeEngine.Application.Exceptions;

public class NotFoundException : AppException
{
    public NotFoundException(string message) : base("NOT_FOUND", message)
    {
    }

    public NotFoundException(string resource, object id) : base("NOT_FOUND", $"{resource} not found: {id}")
    {
    }
}
