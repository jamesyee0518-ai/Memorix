using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IAgentMemoryService
{
    Task<SessionDto> StartSessionAsync(Guid userId, Guid workspaceId, Guid? agentProfileId, string externalSessionKey, string taskTitle, Guid? topicId, CancellationToken ct = default);
    Task<SessionDto?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<MemoryItemDto> CaptureMemoryAsync(Guid userId, Guid workspaceId, CaptureMemoryInput input, CancellationToken ct = default);
    Task<MemoryItemDto?> GetMemoryItemAsync(Guid itemId, CancellationToken ct = default);
    Task<List<MemoryItemDto>> SearchMemoryAsync(Guid userId, Guid workspaceId, SearchMemoryInput input, CancellationToken ct = default);
    Task<ContextPackDto> GetContextAsync(Guid sessionId, int? maxTokens = null, CancellationToken ct = default);
    Task<List<SessionDto>> ListSessionsAsync(Guid userId, Guid workspaceId, int limit = 50, int offset = 0, CancellationToken ct = default);
}
