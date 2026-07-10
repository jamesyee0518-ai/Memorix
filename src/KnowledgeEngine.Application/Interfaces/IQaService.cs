using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IQaService
{
    Task<ApiResponse<QaSessionResponse>> CreateSessionAsync(Guid userId, CreateQaSessionRequest request, CancellationToken ct = default);
    Task<ApiResponse<QaAnswerResponse>> AskAsync(Guid userId, QaAskRequest request, CancellationToken ct = default);
    Task<ApiResponse<List<QaMessageResponse>>> GetSessionMessagesAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<QaSessionListItem>>> GetSessionsAsync(Guid userId, Guid? topicId, CancellationToken ct = default);
}
