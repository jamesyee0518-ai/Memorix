using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class FileStorageWorkspaceDownloadTests
{
    [Fact]
    public async Task Download_UsesFileWorkspaceInsteadOfCurrentUserId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
        await db.Database.EnsureCreatedAsync();
        var workspaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        db.Files.Add(new FileObject
        {
            Id = fileId,
            WorkspaceId = workspaceId,
            Bucket = "workspace-files",
            ObjectKey = "docs/report.pdf",
            OriginalFilename = "report.pdf",
            StorageProvider = "minio",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var factory = new RecordingStorageFactory();
        var service = new FileStorageService(
            factory,
            db,
            new TestCurrentUserContext(userId),
            new FixedConfigService(workspaceId.ToString()),
            new FixedAuthorizationService(WorkspaceAccessResult.Allowed),
            NullLogger<FileStorageService>.Instance);

        var result = await service.GetDownloadUrlAsync(fileId);

        Assert.True(result.Success);
        Assert.Equal(workspaceId.ToString(), factory.RequestedWorkspaceId);
        Assert.Equal(1, factory.Storage.PresignCalls);
    }

    private sealed class RecordingStorageFactory : IFileStorageFactory
    {
        public RecordingStorage Storage { get; } = new();
        public string? RequestedWorkspaceId { get; private set; }

        public Task<IFileStorageProvider> GetProviderAsync(CancellationToken ct = default) =>
            Task.FromResult<IFileStorageProvider>(Storage);

        public Task<IFileStorageProvider> GetProviderForWorkspaceAsync(
            string workspaceId,
            CancellationToken ct = default)
        {
            RequestedWorkspaceId = workspaceId;
            return Task.FromResult<IFileStorageProvider>(Storage);
        }
    }

    private sealed class RecordingStorage : IFileStorageProvider
    {
        public int PresignCalls { get; private set; }

        public Task<string> GetPresignedDownloadUrlAsync(
            string bucket,
            string objectKey,
            int expiry,
            CancellationToken cancellationToken = default)
        {
            PresignCalls++;
            return Task.FromResult("https://download.example.com/report.pdf");
        }

        public Task UploadFileAsync(string bucket, string objectKey, Stream stream, string contentType, long? fileSize = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetFilePathAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestCurrentUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "owner@example.com";
        public bool IsAuthenticated => true;
    }

    private sealed class FixedConfigService(string workspaceId) : IConfigService
    {
        public Task<string?> GetCurrentWorkspaceIdAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>(workspaceId);
        public Task<LocalConfig> LoadConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveConfigAsync(LocalConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetCurrentWorkspaceIdAsync(string workspaceId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedAuthorizationService(
        WorkspaceAccessResult result) : IWorkspaceAuthorizationService
    {
        public Task<WorkspaceAccessResult> AuthorizeAsync(
            Guid workspaceId,
            CancellationToken ct = default) => Task.FromResult(result);
    }
}
