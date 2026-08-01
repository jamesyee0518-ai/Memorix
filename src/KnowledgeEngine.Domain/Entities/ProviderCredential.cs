namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Encrypted BYOK credential for a third-party provider.
/// Secrets are AES-GCM encrypted and tenant-isolated.
/// </summary>
public class ProviderCredential
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }

    /// <summary>user / tenant</summary>
    public string OwnerType { get; set; } = "user";
    public Guid OwnerId { get; set; }

    /// <summary>zhipu / azure / aliyun / tencent / openai</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>api_key / oauth_token / bearer</summary>
    public string CredentialType { get; set; } = "api_key";

    /// <summary>AES-GCM encrypted secret. Never returned to frontend.</summary>
    public string EncryptedSecret { get; set; } = string.Empty;

    /// <summary>Encryption key version for rotation.</summary>
    public string KeyVersion { get; set; } = "v1";

    /// <summary>active / disabled / expired</summary>
    public string Status { get; set; } = "active";

    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Optional display label for UI.</summary>
    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
