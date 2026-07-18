using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IOAuthBindingService
{
    Task<OAuthStartResultDto> StartAsync(StartOAuthDto input, CancellationToken ct = default);
    Task CompleteAsync(string code, string state, CancellationToken ct = default);
    Task<OAuthStatusDto> GetStatusAsync(string sessionId, CancellationToken ct = default);
}
