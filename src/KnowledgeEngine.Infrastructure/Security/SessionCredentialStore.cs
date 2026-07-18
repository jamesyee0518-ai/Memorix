using System.Collections.Concurrent;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Security;

/// <summary>
/// Safe non-persistent fallback used until a platform credential provider is
/// available. Secrets are never written to SQLite, JSON config, or logs.
/// </summary>
public sealed class SessionCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new();

    public Task SetAsync(string keyRef, string secret, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _secrets[keyRef] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string keyRef, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_secrets.TryGetValue(keyRef, out var value) ? value : null);
    }

    public Task DeleteAsync(string keyRef, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _secrets.TryRemove(keyRef, out _);
        return Task.CompletedTask;
    }
}
