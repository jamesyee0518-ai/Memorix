using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Manages BYOK (Bring Your Own Key) credentials for audio providers.
/// Credentials are AES-GCM encrypted at rest, decrypted only transiently during task execution.
/// </summary>
public interface ICredentialManager
{
    /// <summary>
    /// Stores a new provider credential with AES-GCM encryption.
    /// </summary>
    Task<ProviderCredential> StoreAsync(StoreCredentialRequest request, CancellationToken ct);

    /// <summary>
    /// Retrieves the decrypted secret for transient use during task execution.
    /// The secret must not be logged or persisted in memory beyond the task scope.
    /// </summary>
    Task<string?> GetSecretAsync(Guid credentialId, CancellationToken ct);

    /// <summary>
    /// Verifies credential validity by making a lightweight test call to the provider.
    /// Updates LastVerifiedAt on success.
    /// </summary>
    Task<bool> VerifyAsync(Guid credentialId, CancellationToken ct);

    /// <summary>
    /// Disables a credential without deleting it.
    /// </summary>
    Task DisableAsync(Guid credentialId, CancellationToken ct);

    /// <summary>
    /// Rotates the encryption key for a credential.
    /// </summary>
    Task RotateAsync(Guid credentialId, CancellationToken ct);

    /// <summary>
    /// Lists credentials for a given owner (user or tenant).
    /// Never returns the encrypted secret.
    /// </summary>
    Task<List<CredentialDto>> ListByOwnerAsync(string ownerType, Guid ownerId, CancellationToken ct);

    /// <summary>
    /// Finds an active credential for a specific provider and owner.
    /// </summary>
    Task<ProviderCredential?> FindActiveAsync(string providerId, string ownerType, Guid ownerId, CancellationToken ct);
}
