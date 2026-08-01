using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Manages BYOK (Bring Your Own Key) credentials for audio providers with AES-GCM encryption at rest.
/// The master encryption key is stored in <see cref="ICredentialStore"/> under the key
/// "audio-credential-master-key". Secrets are decrypted only transiently during task execution
/// and are never logged or returned to the frontend.
/// </summary>
public class CredentialManager : ICredentialManager
{
    private const string MasterKeyRef = "audio-credential-master-key";
    private const int KeySizeBytes = 32;   // AES-256
    private const int NonceSizeBytes = 12;  // GCM standard
    private const int TagSizeBytes = 16;    // 128-bit authentication tag

    private readonly ICredentialStore _store;
    private readonly IAppDbContext _db;
    private readonly ILogger<CredentialManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialManager"/> class.
    /// </summary>
    /// <param name="store">The secret store for the master encryption key.</param>
    /// <param name="db">The application database context for credential persistence.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public CredentialManager(
        ICredentialStore store,
        IAppDbContext db,
        ILogger<CredentialManager> logger)
    {
        _store = store;
        _db = db;
        _logger = logger;
    }

    // ── ICredentialManager ──

    /// <inheritdoc/>
    public async Task<ProviderCredential> StoreAsync(StoreCredentialRequest request, CancellationToken ct)
    {
        var credentialId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var masterKey = await GetOrCreateMasterKeyAsync(ct);
        var (encryptedSecret, nonce) = EncryptSecret(request.Secret, masterKey);

        var credential = new ProviderCredential
        {
            Id = credentialId,
            TenantId = request.TenantId,
            OwnerType = request.OwnerType,
            OwnerId = request.OwnerId,
            ProviderId = request.ProviderId,
            CredentialType = request.CredentialType,
            EncryptedSecret = encryptedSecret,
            KeyVersion = nonce, // Store nonce alongside for decryption
            Status = CredentialStatuses.Active,
            ExpiresAt = request.ExpiresAt,
            Label = request.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.ProviderCredentials.Add(credential);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Stored credential {CredentialId} for provider {ProviderId} (owner: {OwnerType}/{OwnerId})",
            credentialId, request.ProviderId, request.OwnerType, request.OwnerId);

        return credential;
    }

    /// <inheritdoc/>
    public async Task<string?> GetSecretAsync(Guid credentialId, CancellationToken ct)
    {
        var credential = await _db.ProviderCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential == null)
        {
            _logger.LogWarning("Credential {CredentialId} not found", credentialId);
            return null;
        }

        if (credential.Status != CredentialStatuses.Active)
        {
            _logger.LogWarning(
                "Credential {CredentialId} is not active (status: {Status})",
                credentialId, credential.Status);
            return null;
        }

        if (credential.ExpiresAt.HasValue && credential.ExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("Credential {CredentialId} has expired", credentialId);
            return null;
        }

        var masterKey = await GetOrCreateMasterKeyAsync(ct);
        var secret = DecryptSecret(credential.EncryptedSecret, credential.KeyVersion, masterKey);

        return secret;
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyAsync(Guid credentialId, CancellationToken ct)
    {
        var credential = await _db.ProviderCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential == null)
        {
            _logger.LogWarning("Cannot verify: credential {CredentialId} not found", credentialId);
            return false;
        }

        if (credential.Status != CredentialStatuses.Active)
        {
            _logger.LogWarning(
                "Cannot verify: credential {CredentialId} is not active (status: {Status})",
                credentialId, credential.Status);
            return false;
        }

        // Lightweight validation: decrypt the secret and check format.
        // For API keys we verify the format without making a network call to the provider.
        var masterKey = await GetOrCreateMasterKeyAsync(ct);
        var secret = DecryptSecret(credential.EncryptedSecret, credential.KeyVersion, masterKey);

        bool isValid = credential.CredentialType switch
        {
            "api_key" => IsValidApiKeyFormat(secret),
            "oauth_token" => IsValidTokenFormat(secret),
            "bearer" => IsValidTokenFormat(secret),
            _ => !string.IsNullOrWhiteSpace(secret),
        };

        if (isValid)
        {
            credential.LastVerifiedAt = DateTime.UtcNow;
            credential.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Credential {CredentialId} for provider {ProviderId} verified successfully",
                credentialId, credential.ProviderId);
        }
        else
        {
            _logger.LogWarning(
                "Credential {CredentialId} for provider {ProviderId} failed format validation",
                credentialId, credential.ProviderId);
        }

        return isValid;
    }

    /// <inheritdoc/>
    public async Task DisableAsync(Guid credentialId, CancellationToken ct)
    {
        var credential = await _db.ProviderCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential == null)
        {
            _logger.LogWarning("Cannot disable: credential {CredentialId} not found", credentialId);
            return;
        }

        credential.Status = CredentialStatuses.Disabled;
        credential.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Disabled credential {CredentialId} for provider {ProviderId}",
            credentialId, credential.ProviderId);
    }

    /// <inheritdoc/>
    public async Task RotateAsync(Guid credentialId, CancellationToken ct)
    {
        var credential = await _db.ProviderCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

        if (credential == null)
        {
            _logger.LogWarning("Cannot rotate: credential {CredentialId} not found", credentialId);
            return;
        }

        // Decrypt with current nonce and master key.
        var masterKey = await GetOrCreateMasterKeyAsync(ct);
        var secret = DecryptSecret(credential.EncryptedSecret, credential.KeyVersion, masterKey);

        // Re-encrypt with a fresh nonce.
        var (newEncryptedSecret, newNonce) = EncryptSecret(secret, masterKey);

        credential.EncryptedSecret = newEncryptedSecret;
        credential.KeyVersion = newNonce;
        credential.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Rotated encryption for credential {CredentialId} (provider: {ProviderId})",
            credentialId, credential.ProviderId);
    }

    /// <inheritdoc/>
    public async Task<List<CredentialDto>> ListByOwnerAsync(string ownerType, Guid ownerId, CancellationToken ct)
    {
        var credentials = await _db.ProviderCredentials
            .Where(c => c.OwnerType == ownerType && c.OwnerId == ownerId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        // Project to DTO — never include the encrypted secret.
        return credentials.Select(c => new CredentialDto
        {
            Id = c.Id,
            ProviderId = c.ProviderId,
            CredentialType = c.CredentialType,
            OwnerType = c.OwnerType,
            OwnerId = c.OwnerId,
            Label = c.Label,
            Status = c.Status,
            LastVerifiedAt = c.LastVerifiedAt,
            ExpiresAt = c.ExpiresAt,
            CreatedAt = c.CreatedAt,
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<ProviderCredential?> FindActiveAsync(
        string providerId, string ownerType, Guid ownerId, CancellationToken ct)
    {
        return await _db.ProviderCredentials
            .FirstOrDefaultAsync(c =>
                c.ProviderId == providerId &&
                c.OwnerType == ownerType &&
                c.OwnerId == ownerId &&
                c.Status == CredentialStatuses.Active, ct);
    }

    // ── AES-GCM Encryption Helpers ──

    /// <summary>
    /// Retrieves the master encryption key from the credential store.
    /// If the key does not exist, a new 256-bit key is generated and stored.
    /// </summary>
    private async Task<byte[]> GetOrCreateMasterKeyAsync(CancellationToken ct)
    {
        var stored = await _store.GetAsync(MasterKeyRef, ct);

        if (!string.IsNullOrEmpty(stored))
        {
            var key = Convert.FromBase64String(stored);
            if (key.Length == KeySizeBytes)
            {
                return key;
            }

            _logger.LogWarning(
                "Stored master key has unexpected length {Length}, regenerating", key.Length);
        }

        // Generate a new 256-bit key.
        var newKey = RandomNumberGenerator.GetBytes(KeySizeBytes);
        await _store.SetAsync(MasterKeyRef, Convert.ToBase64String(newKey), ct);

        _logger.LogInformation("Generated and stored new audio credential master key");

        return newKey;
    }

    /// <summary>
    /// Encrypts a plaintext secret using AES-GCM.
    /// Returns a tuple of (base64-encoded nonce||tag||ciphertext, base64-encoded nonce).
    /// The nonce is stored separately in <see cref="ProviderCredential.KeyVersion"/> for rotation support.
    /// </summary>
    private static (string Encrypted, string Nonce) EncryptSecret(string plaintext, byte[] masterKey)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(masterKey, TagSizeBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Combine nonce + tag + ciphertext for storage.
        var combined = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

        return (Convert.ToBase64String(combined), Convert.ToBase64String(nonce));
    }

    /// <summary>
    /// Decrypts an AES-GCM encrypted secret.
    /// The encrypted payload is base64(nonce || tag || ciphertext), but the nonce used for
    /// decryption is taken from <paramref name="nonceBase64"/> (stored in KeyVersion) to support
    /// the case where the stored format may have been re-encrypted with a different nonce.
    /// </summary>
    private static string DecryptSecret(string encryptedBase64, string nonceBase64, byte[] masterKey)
    {
        var combined = Convert.FromBase64String(encryptedBase64);

        // Prefer the separately stored nonce (supports rotation), but fall back to the
        // nonce embedded at the start of the combined payload for backward compatibility.
        var nonce = !string.IsNullOrEmpty(nonceBase64)
            ? Convert.FromBase64String(nonceBase64)
            : combined[..NonceSizeBytes];

        var tag = combined[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = combined[(NonceSizeBytes + TagSizeBytes)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(masterKey, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    // ── Format Validation Helpers ──

    /// <summary>
    /// Validates that an API key string has a reasonable format.
    /// Does not make a network call to the provider.
    /// </summary>
    private static bool IsValidApiKeyFormat(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        // Most API keys are between 16 and 256 characters and contain
        // alphanumeric characters, dashes, underscores, or dots.
        if (secret.Length < 8 || secret.Length > 512)
            return false;

        return secret.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '=');
    }

    /// <summary>
    /// Validates that an OAuth or bearer token has a reasonable format.
    /// </summary>
    private static bool IsValidTokenFormat(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        // JWT-style tokens start with "eyJ", other tokens may vary.
        // Basic check: at least 16 characters.
        return secret.Length >= 16;
    }
}
