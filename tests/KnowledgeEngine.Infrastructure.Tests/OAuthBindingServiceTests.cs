using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Infrastructure.Security;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class OAuthBindingServiceTests
{
    [Fact]
    public async Task StartAsync_UsesPkceAndDoesNotExposeVerifier()
    {
        var service = new OAuthBindingService(new FakeHttpClientFactory(), new FakeBindingService());

        var result = await service.StartAsync(new StartOAuthDto
        {
            AuthorizationEndpoint = "https://account.example.com/oauth/authorize",
            TokenEndpoint = "https://account.example.com/oauth/token",
            ClientId = "memorix-desktop",
            RedirectUri = "http://127.0.0.1:43121/api/oauth/callback",
            CloudApiBaseUrl = "https://api.example.com"
        });

        Assert.StartsWith("oauth_", result.SessionId);
        Assert.Contains("code_challenge=", result.AuthorizationUrl);
        Assert.Contains("code_challenge_method=S256", result.AuthorizationUrl);
        Assert.Contains("state=", result.AuthorizationUrl);
        Assert.Contains("nonce=", result.AuthorizationUrl);
        Assert.DoesNotContain("code_verifier", result.AuthorizationUrl);
    }

    [Fact]
    public async Task StartAsync_RejectsNonLoopbackRedirect()
    {
        var service = new OAuthBindingService(new FakeHttpClientFactory(), new FakeBindingService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(new StartOAuthDto
        {
            AuthorizationEndpoint = "https://account.example.com/oauth/authorize",
            TokenEndpoint = "https://account.example.com/oauth/token",
            ClientId = "memorix-desktop",
            RedirectUri = "https://evil.example.com/callback",
            CloudApiBaseUrl = "https://api.example.com"
        }));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeBindingService : IBindingService
    {
        public Task<CloudAccountBindingDto> BindCloudAccountAsync(
            CreateCloudAccountBindingDto input,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<CloudAccountBindingDto>> ListCloudAccountsAsync(
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnbindCloudAccountAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<WorkspaceBindingDto> CreateWorkspaceBindingAsync(
            CreateWorkspaceBindingDto input,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<WorkspaceBindingDto>> ListWorkspaceBindingsAsync(
            Guid? workspaceId = null,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkspaceBindingDto> UpdateWorkspaceBindingAsync(
            Guid id,
            UpdateWorkspaceBindingDto input,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnbindWorkspaceAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<string?> GetRefreshTokenAsync(
            Guid cloudAccountBindingId,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetAccessTokenAsync(
            Guid cloudAccountBindingId,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}
