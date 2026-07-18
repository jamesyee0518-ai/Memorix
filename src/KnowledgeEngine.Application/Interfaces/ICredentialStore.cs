namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Stores secrets outside the application database. Platform-backed persistent
/// implementations (Keychain, DPAPI, Secret Service) can replace the default
/// session-only store without changing identity or sync services.
/// </summary>
public interface ICredentialStore
{
    Task SetAsync(string keyRef, string secret, CancellationToken ct = default);
    Task<string?> GetAsync(string keyRef, CancellationToken ct = default);
    Task DeleteAsync(string keyRef, CancellationToken ct = default);
}
