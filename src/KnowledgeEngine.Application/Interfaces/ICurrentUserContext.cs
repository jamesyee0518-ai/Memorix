namespace KnowledgeEngine.Application.Interfaces;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
}
