using System.Reflection;
using KnowledgeEngine.Api.Controllers;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class WorkspaceAuthorizationControllerTests
{
    [Fact]
    public async Task FileDownload_ForbiddenBeforeStorageServiceCall()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
        await db.Database.EnsureCreatedAsync();
        var workspaceId = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        db.Files.Add(new FileObject
        {
            Id = fileId,
            WorkspaceId = workspaceId,
            Bucket = "private",
            ObjectKey = "secret.pdf",
            StorageProvider = "minio",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var storageService = new FileStorageService(
            null!,
            db,
            new TestCurrentUserContext(Guid.CreateVersion7()),
            new FixedConfigService(workspaceId.ToString()),
            new FixedAuthorizationService(WorkspaceAccessResult.Forbidden),
            NullLogger<FileStorageService>.Instance);
        var controller = new FilesController(
            storageService,
            db,
            new FixedAuthorizationService(WorkspaceAccessResult.Forbidden))
        {
            ControllerContext = Context()
        };

        var result = await controller.GetDownloadUrl(fileId, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("pairing-code")]
    [InlineData("list")]
    [InlineData("deactivate")]
    public async Task DeviceManagement_ForbiddenBeforeRepositoryOrJwtCall(string action)
    {
        var workspaceId = Guid.CreateVersion7();
        var repository = DispatchProxy.Create<IKnowledgeRepository, ThrowingRepositoryProxy>();
        var jwt = new ThrowingJwtTokenService();
        var controller = new MobileDevicesController(
            new FixedConfigService(workspaceId.ToString()),
            repository,
            jwt,
            new FixedAuthorizationService(WorkspaceAccessResult.Forbidden),
            Options.Create(new JwtSettings()))
        {
            ControllerContext = Context()
        };

        IActionResult result = action switch
        {
            "bind" => await controller.Bind(
                new MobileDeviceBindDto { ClientId = "device-1" }),
            "pairing-code" => await controller.CreatePairingCode(),
            "list" => await controller.List(),
            "deactivate" => await controller.Deactivate(
                new MobileDeviceDeactivateDto { ClientId = "device-1" }),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(0, ((ThrowingRepositoryProxy)(object)repository).Calls);
        Assert.Equal(0, jwt.Calls);
    }

    private static ControllerContext Context() => new()
    {
        HttpContext = new DefaultHttpContext()
    };

    private sealed class FixedAuthorizationService(
        WorkspaceAccessResult result) : IWorkspaceAuthorizationService
    {
        public Task<WorkspaceAccessResult> AuthorizeAsync(
            Guid workspaceId,
            CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class FixedConfigService(string workspaceId) : IConfigService
    {
        public Task<string?> GetCurrentWorkspaceIdAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>(workspaceId);
        public Task<LocalConfig> LoadConfigAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task SaveConfigAsync(LocalConfig config, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task SetCurrentWorkspaceIdAsync(string id, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingJwtTokenService : IJwtTokenService
    {
        public int Calls { get; private set; }
        public string GenerateToken(Guid userId, string email, string role = "user")
        {
            Calls++;
            throw new InvalidOperationException("JWT service must not be called.");
        }
        public string GenerateMobileDeviceToken(
            Guid workspaceId,
            Guid deviceId,
            string clientId,
            DateTime expiresAt)
        {
            Calls++;
            throw new InvalidOperationException("JWT service must not be called.");
        }
    }

    private class ThrowingRepositoryProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException(
                $"Repository method {targetMethod?.Name} must not be called.");
        }
    }

    private sealed class TestCurrentUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "user@example.com";
        public bool IsAuthenticated => true;
    }
}
