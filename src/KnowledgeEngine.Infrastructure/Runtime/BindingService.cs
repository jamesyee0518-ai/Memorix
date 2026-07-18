using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Runtime;

public sealed class BindingService : IBindingService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RefreshLocks = new();
    private readonly IAppDbContext _db;
    private readonly ILocalIdentityService _identityService;
    private readonly ICredentialStore _credentialStore;
    private readonly IHttpClientFactory _httpClientFactory;

    public BindingService(
        IAppDbContext db,
        ILocalIdentityService identityService,
        ICredentialStore credentialStore,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _identityService = identityService;
        _credentialStore = credentialStore;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CloudAccountBindingDto> BindCloudAccountAsync(
        CreateCloudAccountBindingDto input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.CloudUserId) ||
            string.IsNullOrWhiteSpace(input.CloudApiBaseUrl) ||
            string.IsNullOrWhiteSpace(input.RefreshToken))
        {
            throw new ArgumentException("Cloud user, API base URL, and refresh token are required.");
        }

        var identity = await _identityService.EnsureIdentityAsync(ct);
        var apiBaseUrl = NormalizeApiBaseUrl(input.CloudApiBaseUrl);
        var binding = await _db.CloudAccountBindings.FirstOrDefaultAsync(x =>
            x.LocalProfileId == identity.LocalProfileId &&
            x.CloudUserId == input.CloudUserId.Trim() &&
            x.CloudApiBaseUrl == apiBaseUrl, ct);
        var now = DateTime.UtcNow;

        if (binding == null)
        {
            var bindingId = Guid.CreateVersion7();
            binding = new CloudAccountBinding
            {
                Id = bindingId,
                LocalProfileId = identity.LocalProfileId,
                CloudUserId = input.CloudUserId.Trim(),
                CloudApiBaseUrl = apiBaseUrl,
                TokenKeyRef = $"memorix/cloud-account/{bindingId:N}/refresh-token",
                CreatedAt = now
            };
            _db.CloudAccountBindings.Add(binding);
        }

        binding.AccountDisplayName = input.AccountDisplayName?.Trim();
        binding.AccountEmailMasked = input.AccountEmailMasked?.Trim();
        binding.BindingStatus = "active";
        binding.LastAuthenticatedAt = now;
        binding.UpdatedAt = now;
        await _credentialStore.SetAsync(binding.TokenKeyRef, input.RefreshToken, ct);
        if (!string.IsNullOrWhiteSpace(input.AccessToken))
        {
            ValidateOAuthMetadata(input);
            await StoreAccessTokenAsync(binding, new AccessTokenCredential
            {
                AccessToken = input.AccessToken,
                TokenEndpoint = input.TokenEndpoint!,
                ClientId = input.OAuthClientId!,
                ExpiresAt = DateTime.UtcNow.AddSeconds(
                    Math.Max(60, input.AccessTokenExpiresInSeconds ?? 3600))
            }, ct);
        }
        await _db.SaveChangesAsync(ct);
        return Map(binding);
    }

    public async Task<List<CloudAccountBindingDto>> ListCloudAccountsAsync(CancellationToken ct = default)
    {
        var identity = await _identityService.EnsureIdentityAsync(ct);
        return await _db.CloudAccountBindings
            .Where(x => x.LocalProfileId == identity.LocalProfileId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => Map(x))
            .ToListAsync(ct);
    }

    public async Task UnbindCloudAccountAsync(Guid id, CancellationToken ct = default)
    {
        var binding = await _db.CloudAccountBindings.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Cloud account binding {id} not found.");
        var activeWorkspaceBinding = await _db.WorkspaceBindings
            .AnyAsync(x => x.CloudAccountBindingId == id && x.BindingStatus == "active", ct);
        if (activeWorkspaceBinding)
        {
            throw new InvalidOperationException("Unbind all workspaces before removing the cloud account.");
        }
        binding.BindingStatus = "revoked";
        binding.UpdatedAt = DateTime.UtcNow;
        await _credentialStore.DeleteAsync(binding.TokenKeyRef, ct);
        await _credentialStore.DeleteAsync(AccessTokenKey(binding), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<WorkspaceBindingDto> CreateWorkspaceBindingAsync(
        CreateWorkspaceBindingDto input,
        CancellationToken ct = default)
    {
        ValidateSyncMode(input.SyncMode);
        ValidateConflictPolicy(input.ConflictPolicy);
        if (string.IsNullOrWhiteSpace(input.CloudWorkspaceId))
        {
            throw new ArgumentException("Cloud workspace ID is required.");
        }
        if (!await _db.Workspaces.AnyAsync(x => x.Id == input.LocalWorkspaceId, ct))
        {
            throw new InvalidOperationException($"Workspace {input.LocalWorkspaceId} not found.");
        }
        var account = await _db.CloudAccountBindings.FirstOrDefaultAsync(
            x => x.Id == input.CloudAccountBindingId && x.BindingStatus == "active", ct)
            ?? throw new InvalidOperationException("Active cloud account binding not found.");

        var existing = await _db.WorkspaceBindings.FirstOrDefaultAsync(x =>
            x.LocalWorkspaceId == input.LocalWorkspaceId &&
            x.CloudWorkspaceId == input.CloudWorkspaceId.Trim(), ct);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            existing = new WorkspaceBinding
            {
                Id = Guid.CreateVersion7(),
                LocalWorkspaceId = input.LocalWorkspaceId,
                CloudAccountBindingId = account.Id,
                CloudWorkspaceId = input.CloudWorkspaceId.Trim(),
                CreatedAt = now
            };
            _db.WorkspaceBindings.Add(existing);
        }
        existing.CloudAccountBindingId = account.Id;
        existing.SyncMode = input.SyncMode;
        existing.BindingStatus = "active";
        existing.PrimaryDeviceId = input.PrimaryDeviceId;
        existing.UploadOriginalFiles = input.UploadOriginalFiles;
        existing.ConflictPolicy = input.ConflictPolicy;
        existing.UpdatedAt = now;

        var workspace = await _db.Workspaces.FirstAsync(x => x.Id == input.LocalWorkspaceId, ct);
        ApplyCompatibilityFlags(workspace, existing.SyncMode, account, existing.CloudWorkspaceId);
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<List<WorkspaceBindingDto>> ListWorkspaceBindingsAsync(
        Guid? workspaceId = null,
        CancellationToken ct = default)
    {
        var query = _db.WorkspaceBindings.AsQueryable();
        if (workspaceId.HasValue)
        {
            query = query.Where(x => x.LocalWorkspaceId == workspaceId.Value);
        }
        return await query.OrderByDescending(x => x.UpdatedAt)
            .Select(x => Map(x))
            .ToListAsync(ct);
    }

    public async Task<WorkspaceBindingDto> UpdateWorkspaceBindingAsync(
        Guid id,
        UpdateWorkspaceBindingDto input,
        CancellationToken ct = default)
    {
        var binding = await _db.WorkspaceBindings.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Workspace binding {id} not found.");
        if (input.SyncMode != null)
        {
            ValidateSyncMode(input.SyncMode);
            binding.SyncMode = input.SyncMode;
        }
        if (input.ConflictPolicy != null)
        {
            ValidateConflictPolicy(input.ConflictPolicy);
            binding.ConflictPolicy = input.ConflictPolicy;
        }
        if (input.PrimaryDeviceId.HasValue) binding.PrimaryDeviceId = input.PrimaryDeviceId;
        if (input.UploadOriginalFiles.HasValue) binding.UploadOriginalFiles = input.UploadOriginalFiles.Value;
        binding.UpdatedAt = DateTime.UtcNow;

        var account = await _db.CloudAccountBindings.FirstAsync(
            x => x.Id == binding.CloudAccountBindingId, ct);
        var workspace = await _db.Workspaces.FirstAsync(x => x.Id == binding.LocalWorkspaceId, ct);
        ApplyCompatibilityFlags(workspace, binding.SyncMode, account, binding.CloudWorkspaceId);
        await _db.SaveChangesAsync(ct);
        return Map(binding);
    }

    public async Task UnbindWorkspaceAsync(Guid id, CancellationToken ct = default)
    {
        var binding = await _db.WorkspaceBindings.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Workspace binding {id} not found.");
        binding.BindingStatus = "revoked";
        binding.SyncMode = SyncModes.None;
        binding.UpdatedAt = DateTime.UtcNow;
        var workspace = await _db.Workspaces.FirstAsync(x => x.Id == binding.LocalWorkspaceId, ct);
        workspace.SyncMode = SyncModes.None;
        workspace.SyncEnabled = false;
        workspace.InboxEnabled = false;
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetRefreshTokenAsync(
        Guid cloudAccountBindingId,
        CancellationToken ct = default)
    {
        var keyRef = await _db.CloudAccountBindings
            .Where(x => x.Id == cloudAccountBindingId && x.BindingStatus == "active")
            .Select(x => x.TokenKeyRef)
            .FirstOrDefaultAsync(ct);
        return keyRef == null ? null : await _credentialStore.GetAsync(keyRef, ct);
    }

    public async Task<string?> GetAccessTokenAsync(
        Guid cloudAccountBindingId,
        CancellationToken ct = default)
    {
        var refreshLock = RefreshLocks.GetOrAdd(
            cloudAccountBindingId, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(ct);
        try
        {
            var binding = await _db.CloudAccountBindings
                .FirstOrDefaultAsync(x =>
                    x.Id == cloudAccountBindingId &&
                    x.BindingStatus == "active", ct);
            if (binding == null) return null;

            var stored = await _credentialStore.GetAsync(AccessTokenKey(binding), ct);
            if (string.IsNullOrWhiteSpace(stored)) return null;
            var credential = DeserializeAccessToken(stored);
            if (credential == null)
            {
                // Backward compatibility for access tokens stored before metadata support.
                return stored;
            }
            if (credential.ExpiresAt > DateTime.UtcNow.AddMinutes(1))
            {
                return credential.AccessToken;
            }

            var refreshToken = await _credentialStore.GetAsync(binding.TokenKeyRef, ct);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new InvalidOperationException("云端账号刷新令牌不存在，请重新登录。");
            }

            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsync(
                credential.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = credential.ClientId,
                    ["refresh_token"] = refreshToken
                }),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"云端账号令牌刷新失败（HTTP {(int)response.StatusCode}），请重新登录。");
            }

            var refreshed = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(
                cancellationToken: ct)
                ?? throw new InvalidOperationException("云端账号令牌刷新响应为空。");
            if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
            {
                throw new InvalidOperationException("云端账号令牌刷新响应缺少 access_token。");
            }

            credential.AccessToken = refreshed.AccessToken;
            credential.ExpiresAt = DateTime.UtcNow.AddSeconds(
                Math.Max(60, refreshed.ExpiresIn ?? 3600));
            await StoreAccessTokenAsync(binding, credential, ct);
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                await _credentialStore.SetAsync(
                    binding.TokenKeyRef, refreshed.RefreshToken, ct);
            }
            binding.LastAuthenticatedAt = DateTime.UtcNow;
            binding.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return credential.AccessToken;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static string AccessTokenKey(CloudAccountBinding binding) =>
        $"memorix/cloud-account/{binding.Id:N}/access-token";

    private Task StoreAccessTokenAsync(
        CloudAccountBinding binding,
        AccessTokenCredential credential,
        CancellationToken ct) =>
        _credentialStore.SetAsync(
            AccessTokenKey(binding),
            JsonSerializer.Serialize(credential),
            ct);

    private static AccessTokenCredential? DeserializeAccessToken(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<AccessTokenCredential>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateOAuthMetadata(CreateCloudAccountBindingDto input)
    {
        if (!Uri.TryCreate(input.TokenEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("OAuth token endpoint must be an absolute HTTPS URL.");
        }
        if (string.IsNullOrWhiteSpace(input.OAuthClientId))
        {
            throw new ArgumentException("OAuth client ID is required with an access token.");
        }
    }

    private sealed class AccessTokenCredential
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenEndpoint { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    private sealed class RefreshTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }

    private static void ApplyCompatibilityFlags(
        Workspace workspace,
        string syncMode,
        CloudAccountBinding account,
        string cloudWorkspaceId)
    {
        workspace.SyncMode = syncMode;
        workspace.SyncEnabled = syncMode != SyncModes.None;
        workspace.InboxEnabled = syncMode == SyncModes.InboxOnly;
        workspace.CloudApiBaseUrl = account.CloudApiBaseUrl;
        workspace.CloudWorkspaceId = cloudWorkspaceId;
        workspace.UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeApiBaseUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Cloud API base URL must be an absolute HTTP(S) URL.");
        }
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static void ValidateSyncMode(string value)
    {
        if (!SyncModes.IsValid(value)) throw new ArgumentException($"Unsupported sync mode: {value}");
    }

    private static void ValidateConflictPolicy(string value)
    {
        if (value is not ("manual" or "local_wins" or "cloud_wins"))
        {
            throw new ArgumentException($"Unsupported conflict policy: {value}");
        }
    }

    private static CloudAccountBindingDto Map(CloudAccountBinding x) => new()
    {
        Id = x.Id,
        LocalProfileId = x.LocalProfileId,
        CloudUserId = x.CloudUserId,
        CloudApiBaseUrl = x.CloudApiBaseUrl,
        AccountDisplayName = x.AccountDisplayName,
        AccountEmailMasked = x.AccountEmailMasked,
        BindingStatus = x.BindingStatus,
        LastAuthenticatedAt = x.LastAuthenticatedAt
    };

    private static WorkspaceBindingDto Map(WorkspaceBinding x) => new()
    {
        Id = x.Id,
        LocalWorkspaceId = x.LocalWorkspaceId,
        CloudAccountBindingId = x.CloudAccountBindingId,
        CloudWorkspaceId = x.CloudWorkspaceId,
        SyncMode = x.SyncMode,
        BindingStatus = x.BindingStatus,
        PrimaryDeviceId = x.PrimaryDeviceId,
        UploadOriginalFiles = x.UploadOriginalFiles,
        ConflictPolicy = x.ConflictPolicy,
        LastInboxCursor = x.LastInboxCursor,
        LastSyncCursor = x.LastSyncCursor,
        LastSyncAt = x.LastSyncAt
    };
}
