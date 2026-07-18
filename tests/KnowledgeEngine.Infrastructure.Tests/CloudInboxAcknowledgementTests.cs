using KnowledgeEngine.Api.Controllers;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class CloudInboxAcknowledgementTests
{
    [Fact]
    public async Task DeleteOriginal_IsIdempotentAndDeletesStorageOnce()
    {
        await using var fixture = await AckFixture.CreateAsync();
        var request = fixture.Request("deleteOriginal");

        var first = await fixture.Controller.Acknowledge(
            fixture.ItemId, fixture.WorkspaceId, request, CancellationToken.None);
        var second = await fixture.Controller.Acknowledge(
            fixture.ItemId, fixture.WorkspaceId, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(second);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Empty(await fixture.Db.InboxAttachments.ToListAsync());
        Assert.Empty(await fixture.Db.Files.ToListAsync());
        var item = await fixture.Db.InboxItems.SingleAsync();
        Assert.Equal("imported", item.Status);
        Assert.Null(item.FilePath);
        Assert.Single(await fixture.Db.WorkspaceSettings
            .Where(x => x.Key.StartsWith("cloud_inbox_ack_"))
            .ToListAsync());
    }

    [Fact]
    public async Task DeleteAll_RemovesInboxItemAfterFileDeletion()
    {
        await using var fixture = await AckFixture.CreateAsync();

        var result = await fixture.Controller.Acknowledge(
            fixture.ItemId,
            fixture.WorkspaceId,
            fixture.Request("deleteAll"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Empty(await fixture.Db.InboxItems.ToListAsync());
        Assert.Empty(await fixture.Db.InboxAttachments.ToListAsync());
        Assert.Empty(await fixture.Db.Files.ToListAsync());
    }

    [Fact]
    public async Task StorageFailure_DoesNotPersistAcknowledgementOrDeleteMetadata()
    {
        await using var fixture = await AckFixture.CreateAsync();
        fixture.Storage.ThrowOnDelete = true;

        await Assert.ThrowsAsync<IOException>(() => fixture.Controller.Acknowledge(
            fixture.ItemId,
            fixture.WorkspaceId,
            fixture.Request("deleteOriginal"),
            CancellationToken.None));

        Assert.Single(await fixture.Db.InboxItems.ToListAsync());
        Assert.Single(await fixture.Db.InboxAttachments.ToListAsync());
        Assert.Single(await fixture.Db.Files.ToListAsync());
        Assert.Empty(await fixture.Db.WorkspaceSettings
            .Where(x => x.Key.StartsWith("cloud_inbox_ack_"))
            .ToListAsync());
    }

    [Fact]
    public async Task DifferentUser_CannotAcknowledgeWorkspaceItem()
    {
        await using var fixture = await AckFixture.CreateAsync();
        var controller = new CloudInboxAcknowledgementsController(
            fixture.Db,
            new FakeStorageFactory(fixture.Storage),
            new TestWorkspaceAuthorizationService(
                WorkspaceAccessResult.Forbidden))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.Acknowledge(
            fixture.ItemId,
            fixture.WorkspaceId,
            fixture.Request("deleteAll"),
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(0, fixture.Storage.DeleteCalls);
        Assert.Single(await fixture.Db.InboxItems.ToListAsync());
    }

    private sealed class AckFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public FakeStorage Storage { get; }
        public CloudInboxAcknowledgementsController Controller { get; }
        public Guid WorkspaceId { get; }
        public Guid ItemId { get; }
        public Guid OwnerId { get; }

        private AckFixture(
            SqliteConnection connection,
            AppDbContext db,
            FakeStorage storage,
            Guid workspaceId,
            Guid itemId,
            Guid ownerId)
        {
            _connection = connection;
            Db = db;
            Storage = storage;
            WorkspaceId = workspaceId;
            ItemId = itemId;
            OwnerId = ownerId;
            Controller = new CloudInboxAcknowledgementsController(
                db,
                new FakeStorageFactory(storage),
                new TestWorkspaceAuthorizationService(
                    WorkspaceAccessResult.Allowed))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }

        public static async Task<AckFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var workspaceId = Guid.CreateVersion7();
            var ownerId = Guid.CreateVersion7();
            var itemId = Guid.CreateVersion7();
            var fileId = Guid.CreateVersion7();
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                Name = "Cloud",
                Mode = "cloud",
                StorageProvider = "minio",
                FileProvider = "minio",
                JobProvider = "cloud",
                ModelProvider = "cloud",
                UserId = ownerId,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.InboxItems.Add(new InboxItem
            {
                Id = itemId,
                WorkspaceId = workspaceId,
                InputType = "file",
                FilePath = "original.pdf",
                Status = "pending",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Files.Add(new FileObject
            {
                Id = fileId,
                WorkspaceId = workspaceId,
                Bucket = "inbox",
                ObjectKey = "original.pdf",
                StorageProvider = "minio",
                CreatedAt = now
            });
            db.InboxAttachments.Add(new InboxAttachment
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                InboxItemId = itemId,
                FileId = fileId,
                Filename = "original.pdf",
                MimeType = "application/pdf",
                CreatedAt = now
            });
            await db.SaveChangesAsync();
            return new AckFixture(
                connection, db, new FakeStorage(), workspaceId, itemId, ownerId);
        }

        public CloudInboxAcknowledgementRequest Request(string retention) => new()
        {
            CloudWorkspaceId = WorkspaceId.ToString(),
            LocalWorkspaceId = Guid.CreateVersion7().ToString(),
            LocalInboxItemId = Guid.CreateVersion7(),
            Retention = retention,
            Result = "imported",
            IdempotencyKey = $"ack-{ItemId}-{retention}"
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeStorageFactory(FakeStorage storage) : IFileStorageFactory
    {
        public Task<IFileStorageProvider> GetProviderAsync(CancellationToken ct = default) =>
            Task.FromResult<IFileStorageProvider>(storage);
        public Task<IFileStorageProvider> GetProviderForWorkspaceAsync(
            string workspaceId,
            CancellationToken ct = default) =>
            Task.FromResult<IFileStorageProvider>(storage);
    }

    private sealed class FakeStorage : IFileStorageProvider
    {
        public int DeleteCalls { get; private set; }
        public bool ThrowOnDelete { get; set; }

        public Task DeleteFileAsync(
            string bucket,
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            if (ThrowOnDelete) throw new IOException("storage unavailable");
            return Task.CompletedTask;
        }

        public Task UploadFileAsync(string bucket, string objectKey, Stream stream, string contentType, long? fileSize = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetPresignedDownloadUrlAsync(string bucket, string objectKey, int expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetFilePathAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestWorkspaceAuthorizationService(
        WorkspaceAccessResult result) : IWorkspaceAuthorizationService
    {
        public Task<WorkspaceAccessResult> AuthorizeAsync(
            Guid workspaceId,
            CancellationToken ct = default) => Task.FromResult(result);
    }
}
