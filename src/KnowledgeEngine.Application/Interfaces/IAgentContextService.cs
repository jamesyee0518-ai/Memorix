using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IAgentContextService
{
    Task<ContextPackDto> BuildContextPackAsync(Guid sessionId, int maxTokens, CancellationToken ct = default);
}
