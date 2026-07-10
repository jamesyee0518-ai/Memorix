using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Security;

public class ApiKeyService : IApiKeyService
{
    private const string KeyPrefix = "ke_live_";
    private const string HashSalt = "KE_API_SALT_v1";

    private readonly IAppDbContext _db;
    private readonly ILogger<ApiKeyService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ApiKeyService(IAppDbContext db, ILogger<ApiKeyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ===== Create =====

    public async Task<ApiResponse<CreateApiKeyResponse>> CreateAsync(
        Guid userId,
        CreateApiKeyRequest request,
        CancellationToken ct = default)
    {
        try
        {
            // Generate random key: ke_live_ + 32 chars hex
            var randomBytes = new byte[16]; // 16 bytes = 32 hex chars
            RandomNumberGenerator.Fill(randomBytes);
            var hexPart = Convert.ToHexString(randomBytes).ToLowerInvariant();
            var fullKey = KeyPrefix + hexPart;

            // Key prefix = first 12 chars (ke_live_xxxx)
            var keyPrefix = fullKey.Length >= 12 ? fullKey.Substring(0, 12) : fullKey;

            // Hash: SHA256(fullKey + salt)
            var keyHash = HashKey(fullKey);

            var now = DateTime.UtcNow;
            var apiKey = new ApiKey
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = request.Name,
                KeyPrefix = keyPrefix,
                KeyHash = keyHash,
                PermissionScope = request.PermissionScope ?? "full_read",
                AllowedTopicIds = request.AllowedTopicIds != null
                    ? JsonSerializer.Serialize(request.AllowedTopicIds, JsonOptions)
                    : null,
                AllowedActions = request.AllowedActions != null
                    ? JsonSerializer.Serialize(request.AllowedActions, JsonOptions)
                    : null,
                RateLimitPerMinute = request.RateLimitPerMinute ?? 60,
                DailyQuota = request.DailyQuota ?? 1000,
                ExpiresAt = request.ExpiresAt,
                Status = "active",
                CreatedAt = now
            };

            _db.ApiKeys.Add(apiKey);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("API Key created: {KeyPrefix} for user {UserId}", keyPrefix, userId);

            return ApiResponse<CreateApiKeyResponse>.Ok(new CreateApiKeyResponse
            {
                Id = apiKey.Id,
                ApiKey = fullKey,
                KeyPrefix = keyPrefix,
                Name = apiKey.Name,
                PermissionScope = apiKey.PermissionScope,
                Status = apiKey.Status,
                CreatedAt = apiKey.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create API Key for user {UserId}", userId);
            return ApiResponse<CreateApiKeyResponse>.Fail("create_api_key_error", ex.Message);
        }
    }

    // ===== GetAll =====

    public async Task<ApiResponse<List<ApiKeyListItem>>> GetAllAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        try
        {
            var keys = await _db.ApiKeys
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreatedAt)
                .ToListAsync(ct);

            var items = keys.Select(k => new ApiKeyListItem
            {
                Id = k.Id,
                Name = k.Name,
                KeyPrefix = k.KeyPrefix,
                PermissionScope = k.PermissionScope,
                AllowedTopicIds = DeserializeGuidList(k.AllowedTopicIds),
                AllowedActions = DeserializeStringList(k.AllowedActions),
                RateLimitPerMinute = k.RateLimitPerMinute,
                DailyQuota = k.DailyQuota,
                ExpiresAt = k.ExpiresAt,
                Status = k.Status,
                CreatedAt = k.CreatedAt,
                LastUsedAt = k.LastUsedAt
            }).ToList();

            return ApiResponse<List<ApiKeyListItem>>.Ok(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get API Keys for user {UserId}", userId);
            return ApiResponse<List<ApiKeyListItem>>.Fail("get_api_keys_error", ex.Message);
        }
    }

    // ===== Disable =====

    public async Task<ApiResponse<object>> DisableAsync(
        Guid userId,
        Guid keyId,
        CancellationToken ct = default)
    {
        try
        {
            var apiKey = await _db.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId, ct);

            if (apiKey == null)
            {
                return ApiResponse<object>.Fail("api_key_not_found", "API Key not found");
            }

            apiKey.Status = "disabled";
            await _db.SaveChangesAsync(ct);

            return ApiResponse<object>.Ok(new { id = keyId, status = "disabled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable API Key {KeyId}", keyId);
            return ApiResponse<object>.Fail("disable_api_key_error", ex.Message);
        }
    }

    // ===== Delete (soft delete = disable) =====

    public async Task<ApiResponse<object>> DeleteAsync(
        Guid userId,
        Guid keyId,
        CancellationToken ct = default)
    {
        try
        {
            var apiKey = await _db.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId, ct);

            if (apiKey == null)
            {
                return ApiResponse<object>.Fail("api_key_not_found", "API Key not found");
            }

            apiKey.Status = "disabled";
            await _db.SaveChangesAsync(ct);

            return ApiResponse<object>.Ok(new { id = keyId, deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete API Key {KeyId}", keyId);
            return ApiResponse<object>.Fail("delete_api_key_error", ex.Message);
        }
    }

    // ===== Validate =====

    public async Task<ApiKey?> ValidateAsync(string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(KeyPrefix))
        {
            return null;
        }

        // Extract prefix (first 12 chars)
        var prefix = rawKey.Length >= 12 ? rawKey.Substring(0, 12) : rawKey;

        var apiKey = await _db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix && k.Status == "active", ct);

        if (apiKey == null)
        {
            return null;
        }

        // Verify hash
        var computedHash = HashKey(rawKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(apiKey.KeyHash)))
        {
            return null;
        }

        // Check expiration
        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < DateTime.UtcNow)
        {
            return null;
        }

        // Update last used
        apiKey.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return apiKey;
    }

    // ===== CheckRateLimit =====

    public async Task<bool> CheckRateLimitAsync(
        Guid apiKeyId,
        int rateLimitPerMinute,
        CancellationToken ct = default)
    {
        var oneMinuteAgo = DateTime.UtcNow.AddSeconds(-60);

        var recentCount = await _db.ApiCallLogs
            .CountAsync(l => l.ApiKeyId == apiKeyId && l.CreatedAt >= oneMinuteAgo, ct);

        return recentCount < rateLimitPerMinute;
    }

    // ===== CheckDailyQuota =====

    public async Task<bool> CheckDailyQuotaAsync(
        Guid apiKeyId,
        int dailyQuota,
        CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        var todayCount = await _db.ApiCallLogs
            .CountAsync(l => l.ApiKeyId == apiKeyId && l.CreatedAt >= today, ct);

        return todayCount < dailyQuota;
    }

    // ===== LogApiCall =====

    public async Task LogApiCallAsync(ApiCallLog log, CancellationToken ct = default)
    {
        try
        {
            _db.ApiCallLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log API call for key {ApiKeyId}", log.ApiKeyId);
        }
    }

    // ===== Private Helpers =====

    private static string HashKey(string key)
    {
        var input = key + HashSalt;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<Guid>? DeserializeGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
