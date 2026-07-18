namespace KnowledgeEngine.Application.Exceptions;

public class ValidationException : AppException
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(string message) : base("VALIDATION_ERROR", message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IDictionary<string, string[]> errors) : base("VALIDATION_ERROR", "One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string message) : base("VALIDATION_ERROR", message)
    {
        Errors = new Dictionary<string, string[]>
        {
            { field, new[] { message } }
        };
    }
}
