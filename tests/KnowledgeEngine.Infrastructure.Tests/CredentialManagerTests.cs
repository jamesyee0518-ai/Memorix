using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.Audio;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class CredentialManagerTests
{
    private static async Task<AppDbContext> CreateDbAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static CredentialManager CreateService(AppDbContext db, ICredentialStore? store = null)
    {
        return new CredentialManager(
            store ?? new InMemoryCredentialStore(),
            db,
            NullLogger<CredentialManager>.Instance);
    }

    private static StoreCredentialRequest MakeRequest(
        string secret = "sk-test-1234567890abcdef",
        string providerId = "zhipu",
        string credentialType = "api_key",
        string ownerType = CredentialOwnerTypes.User,
        Guid? ownerId = null,
        DateTime? expiresAt = null) => new()
    {
        ProviderId = providerId,
        CredentialType = credentialType,
        Secret = secret,
        OwnerType = ownerType,
        OwnerId = ownerId ?? Guid.NewGuid(),
        ExpiresAt = expiresAt,
    };

    [Fact]
    public async Task StoreAndRetrieve_RoundtripsSecret()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var store = new InMemoryCredentialStore();
        var svc = CreateService(db, store);

        var request = MakeRequest(secret: "sk-test-api-key-1234567890");
        var credential = await svc.StoreAsync(request, CancellationToken.None);

        var secret = await svc.GetSecretAsync(credential.Id, CancellationToken.None);

        Assert.NotNull(secret);
        Assert.Equal("sk-test-api-key-1234567890", secret);
    }

    [Fact]
    public async Task GetSecret_DisabledCredential_ReturnsNull()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var svc = CreateService(db);

        var credential = await svc.StoreAsync(MakeRequest(), CancellationToken.None);
        await svc.DisableAsync(credential.Id, CancellationToken.None);

        var secret = await svc.GetSecretAsync(credential.Id, CancellationToken.None);

        Assert.Null(secret);
    }

    [Fact]
    public async Task GetSecret_ExpiredCredential_ReturnsNull()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var svc = CreateService(db);

        var request = MakeRequest(expiresAt: DateTime.UtcNow.AddMinutes(-1));
        var credential = await svc.StoreAsync(request, CancellationToken.None);

        var secret = await svc.GetSecretAsync(credential.Id, CancellationToken.None);

        Assert.Null(secret);
    }

    [Fact]
    public async Task Verify_ValidApiKey_ReturnsTrue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var svc = CreateService(db);

        var credential = await svc.StoreAsync(
            MakeRequest(secret: "sk-valid-api-key-1234567890", credentialType: "api_key"),
            CancellationToken.None);

        var result = await svc.VerifyAsync(credential.Id, CancellationToken.None);

        Assert.True(result);

        // LastVerifiedAt should be updated.
        db.ChangeTracker.Clear();
        var stored = await db.ProviderCredentials.FirstAsync(c => c.Id == credential.Id);
        Assert.NotNull(stored.LastVerifiedAt);
    }

    [Fact]
    public async Task Verify_InvalidFormat_ReturnsFalse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var svc = CreateService(db);

        // "short" is only 5 characters, below the 8-char minimum for API key format.
        var credential = await svc.StoreAsync(
            MakeRequest(secret: "short", credentialType: "api_key"),
            CancellationToken.None);

        var result = await svc.VerifyAsync(credential.Id, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Disable_SetsStatusToDisabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var svc = CreateService(db);

        var credential = await svc.StoreAsync(MakeRequest(), CancellationToken.None);
        await svc.DisableAsync(credential.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var stored = await db.ProviderCredentials.FirstAsync(c => c.Id == credential.Id);

        Assert.Equal(CredentialStatuses.Disabled, stored.Status);
    }

    [Fact]
    public async Task Rotate_PreservesSecretWithNewNonce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var svc = CreateService(db);

        var credential = await svc.StoreAsync(
            MakeRequest(secret: "sk-rotate-test-1234567890"),
            CancellationToken.None);

        // Capture original nonce (KeyVersion) and ciphertext.
        db.ChangeTracker.Clear();
        var before = await db.ProviderCredentials.FirstAsync(c => c.Id == credential.Id);
        var originalNonce = before.KeyVersion;
        var originalEncrypted = before.EncryptedSecret;

        await svc.RotateAsync(credential.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var after = await db.ProviderCredentials.FirstAsync(c => c.Id == credential.Id);

        // Nonce should have changed (re-encrypted with a fresh nonce).
        Assert.NotEqual(originalNonce, after.KeyVersion);
        // Ciphertext should have changed.
        Assert.NotEqual(originalEncrypted, after.EncryptedSecret);

        // The decrypted secret should still match the original plaintext.
        var secret = await svc.GetSecretAsync(credential.Id, CancellationToken.None);
        Assert.Equal("sk-rotate-test-1234567890", secret);
    }

    [Fact]
    public async Task FindActive_ReturnsActiveCredential()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var svc = CreateService(db);

        var ownerId = Guid.NewGuid();
        var credential = await svc.StoreAsync(
            MakeRequest(providerId: "zhipu", ownerId: ownerId),
            CancellationToken.None);

        var found = await svc.FindActiveAsync(
            "zhipu", CredentialOwnerTypes.User, ownerId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(credential.Id, found!.Id);
        Assert.Equal(CredentialStatuses.Active, found.Status);

        // Should not find for a different provider.
        var notFound = await svc.FindActiveAsync(
            "azure", CredentialOwnerTypes.User, ownerId, CancellationToken.None);
        Assert.Null(notFound);
    }

    // ── In-Memory Credential Store ──

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _store = new();

        public Task SetAsync(string keyRef, string secret, CancellationToken ct = default)
        {
            _store[keyRef] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string keyRef, CancellationToken ct = default)
        {
            return Task.FromResult(
                _store.TryGetValue(keyRef, out var value) ? value : null);
        }

        public Task DeleteAsync(string keyRef, CancellationToken ct = default)
        {
            _store.Remove(keyRef);
            return Task.CompletedTask;
        }
    }
}
