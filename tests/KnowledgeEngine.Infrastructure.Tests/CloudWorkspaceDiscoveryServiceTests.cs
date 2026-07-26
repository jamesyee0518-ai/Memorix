using System.Net;
using System.Text;
using KnowledgeEngine.Api.Services;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class CloudWorkspaceDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsWorkspacesAndNegotiatedCapabilities()
    {
        var accountId = Guid.NewGuid();
        var bindingService = new FakeBindingService(accountId, "https://cloud.example.test", "secret-token");
        var handler = new RoutingHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/workspaces" => Json("""
                    {"success":true,"data":[{"id":"ws-1","name":"研究资料","mode":"cloud","role":"owner"}]}
                    """),
                "/api/desktop/cloud-api-capabilities" => Json("""
                    {"success":true,"data":{"apiVersion":"1.2","features":["workspace_discovery","cloud_inbox"]}}
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var service = CreateService(bindingService, handler);

        var result = await service.DiscoverAsync(accountId);

        Assert.True(result.Compatible);
        Assert.Equal("1.2", result.CloudApiVersion);
        Assert.Equal("ws-1", Assert.Single(result.Workspaces).Id);
        Assert.Contains("cloud_inbox", result.Capabilities);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsNonLoopbackHttpCloudApi()
    {
        var accountId = Guid.NewGuid();
        var service = CreateService(
            new FakeBindingService(accountId, "http://cloud.example.test", "secret-token"),
            new RoutingHandler(_ => throw new InvalidOperationException("HTTP must not be called.")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DiscoverAsync(accountId));

        Assert.Contains("HTTPS", error.Message);
    }

    private static CloudWorkspaceDiscoveryService CreateService(
        IBindingService bindingService,
        HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:MinimumCloudApiVersion"] = "1.0"
            })
            .Build();
        return new CloudWorkspaceDiscoveryService(
            bindingService,
            new FixedHttpClientFactory(handler),
            configuration);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }

    private sealed class FakeBindingService(
        Guid accountId,
        string cloudApiBaseUrl,
        string accessToken) : IBindingService
    {
        public Task<List<CloudAccountBindingDto>> ListCloudAccountsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<CloudAccountBindingDto>
            {
                new()
                {
                    Id = accountId,
                    CloudApiBaseUrl = cloudApiBaseUrl,
                    BindingStatus = "active"
                }
            });

        public Task<string?> GetAccessTokenAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<string?>(id == accountId ? accessToken : null);
        public Task<string?> RefreshAccessTokenAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<string?>(id == accountId ? accessToken : null);

        public Task<CloudAccountBindingDto> BindCloudAccountAsync(
            CreateCloudAccountBindingDto input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnbindCloudAccountAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkspaceBindingDto> CreateWorkspaceBindingAsync(
            CreateWorkspaceBindingDto input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<WorkspaceBindingDto>> ListWorkspaceBindingsAsync(
            Guid? workspaceId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkspaceBindingDto> UpdateWorkspaceBindingAsync(
            Guid id, UpdateWorkspaceBindingDto input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnbindWorkspaceAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetRefreshTokenAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
