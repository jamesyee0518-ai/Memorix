using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface ILocalIdentityService
{
    Task<LocalIdentityDto> EnsureIdentityAsync(CancellationToken ct = default);
}
