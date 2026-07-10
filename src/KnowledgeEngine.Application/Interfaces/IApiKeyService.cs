using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

public interface IApiKeyService
{
    Task<ApiResponse<CreateApiKeyResponse>> CreateAsync(Guid userId, CreateApiKeyRequest request, CancellationToken ct = default);
    Task<ApiResponse<List<ApiKeyListItem>>> GetAllAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResponse<object>> DisableAsync(Guid userId, Guid keyId, CancellationToken ct = default);
    Task<ApiResponse<object>> DeleteAsync(Guid userId, Guid keyId, CancellationToken ct = default);
    Task<ApiKey?> ValidateAsync(string rawKey, CancellationToken ct = default);
    Task<bool> CheckRateLimitAsync(Guid apiKeyId, int rateLimitPerMinute, CancellationToken ct = default);
    Task<bool> CheckDailyQuotaAsync(Guid apiKeyId, int dailyQuota, CancellationToken ct = default);
    Task LogApiCallAsync(ApiCallLog log, CancellationToken ct = default);
}
