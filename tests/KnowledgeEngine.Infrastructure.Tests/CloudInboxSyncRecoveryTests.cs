using System.Net;
using System.Text;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class CloudInboxSyncRecoveryTests
{
    [Fact]
    public async Task AckFailure_DoesNotAdvanceCursor_AndRetryDoesNotDuplicateLocalItem()
    {
        await RunRecoveryScenarioAsync(wrappedResponses: false);
    }

    [Fact]
    public async Task WrappedApiResponses_AreUnwrappedForChangesAndAck()
    {
        await RunRecoveryScenarioAsync(wrappedResponses: true);
    }

    [Fact]
    public async Task MissingScopedChangesRoute_FallsBackToLegacyRoute()
    {
        await RunRecoveryScenarioAsync(
            wrappedResponses: false,
            scopedRouteMissing: true);
    }

    [Fact]
    public async Task MissingScopedAckRoute_FallsBackToLegacyRoute()
    {
        await RunRecoveryScenarioAsync(
            wrappedResponses: false,
            scopedAckRouteMissing: true);
    }

    private static async Task RunRecoveryScenarioAsync(
        bool wrappedResponses,
        bool scopedRouteMissing = false,
        bool scopedAckRouteMissing = false)
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(), $"memorix-sync-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE inbox_items ADD COLUMN content TEXT");
            var workspaceId = Guid.CreateVersion7();
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                Name = "Local",
                Mode = "local",
                StorageProvider = "local_fs",
                FileProvider = "local_fs",
                JobProvider = "local",
                ModelProvider = "local",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var handler = new CloudProtocolHandler(
                wrappedResponses, scopedRouteMissing, scopedAckRouteMissing);
            var repository = new LocalKnowledgeRepository(
                dbPath, NullLogger<LocalKnowledgeRepository>.Instance);
            var service = new CloudInboxSyncService(
                repository,
                new TestHttpClientFactory(handler),
                db,
                NullLogger<CloudInboxSyncService>.Instance);

            var first = await service.PullAsync(
                workspaceId.ToString(),
                "https://api.example.com",
                Guid.CreateVersion7().ToString(),
                "access-token",
                "deleteAll");

            Assert.Equal(1, first.FailedCount);
            Assert.Null(first.NextCursor);
            var staging = await db.SyncInboxStaging.SingleAsync();
            Assert.True(
                await repository.CountInboxItemsAsync(workspaceId.ToString()) == 1,
                staging.ErrorMessage);
            Assert.Equal("local_imported", staging.Status);
            Assert.Null(await repository.GetSyncCursorAsync(
                workspaceId.ToString(), "inbox"));

            var second = await service.PullAsync(
                workspaceId.ToString(),
                "https://api.example.com",
                handler.CloudWorkspaceId,
                "access-token",
                "deleteAll");

            Assert.Equal(0, second.FailedCount);
            Assert.Equal("cursor-2", second.NextCursor);
            Assert.Equal(1, await repository.CountInboxItemsAsync(workspaceId.ToString()));
            staging = await db.SyncInboxStaging.SingleAsync();
            Assert.Equal("imported", staging.Status);
            Assert.Equal(2, handler.AckCalls);
            if (scopedAckRouteMissing)
            {
                Assert.Equal(2, handler.LegacyAckCalls);
                Assert.Equal(4, handler.TotalAckRequests);
            }
            Assert.Equal(scopedRouteMissing ? 4 : 2, handler.ChangesCalls);
            if (scopedRouteMissing)
            {
                Assert.Equal(2, handler.LegacyChangesCalls);
            }
            Assert.Equal("cursor-2", (await repository.GetSyncCursorAsync(
                workspaceId.ToString(), "inbox"))?.CursorValue);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CloudProtocolHandler(
        bool wrappedResponses,
        bool scopedRouteMissing,
        bool scopedAckRouteMissing) : HttpMessageHandler
    {
        public string CloudWorkspaceId { get; } = Guid.CreateVersion7().ToString();
        public int AckCalls { get; private set; }
        public int TotalAckRequests { get; private set; }
        public int LegacyAckCalls { get; private set; }
        public int ChangesCalls { get; private set; }
        public int LegacyChangesCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath.EndsWith("/inbox/changes") == true)
            {
                ChangesCalls++;
                var isLegacy = request.RequestUri.AbsolutePath == "/api/inbox/changes";
                if (isLegacy) LegacyChangesCalls++;
                if (scopedRouteMissing && !isLegacy)
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.NotFound));
                }
                var payload = """
                    {
                      "items": [{
                        "id": "cloud-item-1",
                        "inputType": "text",
                        "title": "Cloud item",
                        "contentText": "Imported once"
                      }],
                      "nextCursor": "cursor-2",
                      "hasMore": false
                    }
                    """;
                return Task.FromResult(Json(
                    HttpStatusCode.OK,
                    wrappedResponses
                        ? $$"""{"success":true,"data":{{payload}},"traceId":"test"}"""
                        : payload));
            }
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath.EndsWith("/ack") == true)
            {
                TotalAckRequests++;
                var isLegacy = request.RequestUri.AbsolutePath.StartsWith(
                    "/v1/inbox/items/", StringComparison.Ordinal);
                if (isLegacy) LegacyAckCalls++;
                if (scopedAckRouteMissing && !isLegacy)
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.NotFound));
                }
                AckCalls++;
                return Task.FromResult(AckCalls == 1
                    ? Json(HttpStatusCode.ServiceUnavailable, """{"error":"temporary"}""")
                    : Json(
                        HttpStatusCode.OK,
                        wrappedResponses
                            ? """
                              {
                                "success": true,
                                "data": {
                                  "acknowledged": true,
                                  "retentionApplied": "deleteAll"
                                }
                              }
                              """
                            : """
                              {
                                "acknowledged": true,
                                "retentionApplied": "deleteAll"
                              }
                              """));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
