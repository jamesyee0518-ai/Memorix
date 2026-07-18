using System.Security.Claims;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class WorkspaceAuthorizationServiceTests
{
    [Fact]
    public async Task OwnerUser_IsAllowed()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();

        var result = await fixture.AuthorizeAsUserAsync(fixture.OwnerId);

        Assert.Equal(WorkspaceAccessResult.Allowed, result);
    }

    [Fact]
    public async Task DifferentUser_IsForbidden()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();

        var result = await fixture.AuthorizeAsUserAsync(Guid.CreateVersion7());

        Assert.Equal(WorkspaceAccessResult.Forbidden, result);
    }

    [Fact]
    public async Task DeviceToken_ForSameWorkspace_IsAllowed()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();

        var result = await fixture.AuthorizeAsDeviceAsync(fixture.WorkspaceId);

        Assert.Equal(WorkspaceAccessResult.Allowed, result);
    }

    [Fact]
    public async Task DeviceToken_ForDifferentWorkspace_IsForbidden()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();

        var result = await fixture.AuthorizeAsDeviceAsync(Guid.CreateVersion7());

        Assert.Equal(WorkspaceAccessResult.Forbidden, result);
    }

    [Fact]
    public async Task UnauthenticatedIdentity_IsForbidden()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();

        var result = await fixture.AuthorizeAsync(
            new TestCurrentUserContext(null),
            new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(WorkspaceAccessResult.Forbidden, result);
    }

    [Fact]
    public async Task MissingWorkspace_ReturnsNotFound()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();

        var result = await fixture.AuthorizeAsync(
            new TestCurrentUserContext(fixture.OwnerId),
            UserPrincipal(fixture.OwnerId),
            Guid.CreateVersion7());

        Assert.Equal(WorkspaceAccessResult.NotFound, result);
    }

    [Fact]
    public async Task WorkspaceWithoutOwner_IsForbidden()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync(
            withoutOwner: true);

        var result = await fixture.AuthorizeAsUserAsync(Guid.CreateVersion7());

        Assert.Equal(WorkspaceAccessResult.Forbidden, result);
    }

    private static ClaimsPrincipal UserPrincipal(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim("user_id", userId.ToString())],
            "test"));

    private sealed class AuthorizationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly AppDbContext _db;
        public Guid WorkspaceId { get; }
        public Guid OwnerId { get; }

        private AuthorizationFixture(
            SqliteConnection connection,
            AppDbContext db,
            Guid workspaceId,
            Guid ownerId)
        {
            _connection = connection;
            _db = db;
            WorkspaceId = workspaceId;
            OwnerId = ownerId;
        }

        public static async Task<AuthorizationFixture> CreateAsync(
            bool withoutOwner = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            Guid? resolvedOwner = withoutOwner ? null : Guid.CreateVersion7();
            var workspaceId = Guid.CreateVersion7();
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                UserId = resolvedOwner,
                Name = "Workspace",
                Mode = "cloud",
                StorageProvider = "postgres",
                FileProvider = "minio",
                JobProvider = "redis",
                ModelProvider = "cloud",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return new AuthorizationFixture(
                connection,
                db,
                workspaceId,
                resolvedOwner ?? Guid.Empty);
        }

        public Task<WorkspaceAccessResult> AuthorizeAsUserAsync(Guid userId) =>
            AuthorizeAsync(
                new TestCurrentUserContext(userId),
                UserPrincipal(userId));

        public Task<WorkspaceAccessResult> AuthorizeAsDeviceAsync(
            Guid tokenWorkspaceId)
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim("token_type", "mobile_device"),
                    new Claim("workspace_id", tokenWorkspaceId.ToString())
                ],
                "test");
            return AuthorizeAsync(
                new TestCurrentUserContext(null),
                new ClaimsPrincipal(identity));
        }

        public async Task<WorkspaceAccessResult> AuthorizeAsync(
            ICurrentUserContext currentUser,
            ClaimsPrincipal principal,
            Guid? workspaceId = null)
        {
            var httpContext = new DefaultHttpContext { User = principal };
            var service = new WorkspaceAuthorizationService(
                _db,
                currentUser,
                new HttpContextAccessor { HttpContext = httpContext });
            return await service.AuthorizeAsync(
                workspaceId ?? WorkspaceId);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => null;
        public bool IsAuthenticated => userId.HasValue;
    }
}
