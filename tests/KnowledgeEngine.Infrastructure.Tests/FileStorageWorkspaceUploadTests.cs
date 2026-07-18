using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class FileStorageWorkspaceUploadTests
{
    [Fact]
    public async Task Upload_StoresFileUnderActiveWorkspace()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
        await db.Database.EnsureCreatedAsync();
        var userId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            UserId = userId,
            Name = "Research",
            Mode = "cloud",
            StorageProvider = "postgres",
            FileProvider = "minio",
            JobProvider = "redis",
            ModelProvider = "cloud",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var factory = new RecordingStorageFactory();
        var service = new FileStorageService(
            factory,
            db,
            new TestCurrentUserContext(userId),
            new FixedConfigService(workspaceId.ToString()),
            new FixedAuthorizationService(),
            NullLogger<FileStorageService>.Instance);

        await using var stream = new MemoryStream([1, 2, 3]);
        var result = await service.UploadPdfAsync(
            "paper.pdf", "application/pdf", stream.Length, stream);

        Assert.True(result.Success);
        Assert.Equal(workspaceId.ToString(), factory.RequestedWorkspaceId);
        var file = await db.Files.SingleAsync();
        Assert.Equal(workspaceId, file.WorkspaceId);
        Assert.StartsWith($"workspaces/{workspaceId}/files/", file.ObjectKey);
        Assert.EndsWith("/original.pdf", file.ObjectKey);
        Assert.Equal(file.ObjectKey, factory.Storage.ObjectKey);
    }

    [Fact]
    public async Task InternalUpload_UsesExplicitWorkspaceInsteadOfCurrentProvider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
        var workspaceId = Guid.CreateVersion7();
        var factory = new RecordingStorageFactory();
        var service = new FileStorageService(
            factory,
            db,
            new TestCurrentUserContext(Guid.CreateVersion7()),
            new FixedConfigService(Guid.CreateVersion7().ToString()),
            new FixedAuthorizationService(),
            NullLogger<FileStorageService>.Instance);

        await using var stream = new MemoryStream([1, 2, 3]);
        await service.UploadFileInternalAsync(
            workspaceId.ToString(),
            "knowledge-engine",
            "sources/original.pdf",
            stream,
            "application/pdf",
            stream.Length);

        Assert.Equal(workspaceId.ToString(), factory.RequestedWorkspaceId);
        Assert.Equal("sources/original.pdf", factory.Storage.ObjectKey);
    }

    private sealed class RecordingStorageFactory : IFileStorageFactory
    {
        public RecordingStorage Storage { get; } = new();
        public string? RequestedWorkspaceId { get; private set; }
        public Task<IFileStorageProvider> GetProviderAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Workspace-specific provider must be used.");
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
        public string? ObjectKey { get; private set; }
        public Task UploadFileAsync(string bucket, string objectKey, Stream stream, string contentType, long? fileSize = null, CancellationToken cancellationToken = default)
        {
            ObjectKey = objectKey;
            return Task.CompletedTask;
        }
        public Task<string> GetPresignedDownloadUrlAsync(string bucket, string objectKey, int expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Task<string?> GetCurrentWorkspaceIdAsync(CancellationToken ct = default) => Task.FromResult<string?>(workspaceId);
        public Task<LocalConfig> LoadConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveConfigAsync(LocalConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetCurrentWorkspaceIdAsync(string workspaceId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedAuthorizationService : IWorkspaceAuthorizationService
    {
        public Task<WorkspaceAccessResult> AuthorizeAsync(Guid workspaceId, CancellationToken ct = default) =>
            Task.FromResult(WorkspaceAccessResult.Allowed);
    }
}
