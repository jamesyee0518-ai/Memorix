using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IUsageService
{
    Task<ApiResponse<UsageResponse>> GetUsageAsync(Guid userId, CancellationToken ct = default);
    Task RecordUsageAsync(Guid userId, UsageType usageType, int count = 1, CancellationToken ct = default);
    Task RecordTokensAsync(Guid userId, int inputTokens, int outputTokens, CancellationToken ct = default);
    Task RecordAgentUsageAsync(Guid userId, string toolName, bool success, CancellationToken ct = default);
}
