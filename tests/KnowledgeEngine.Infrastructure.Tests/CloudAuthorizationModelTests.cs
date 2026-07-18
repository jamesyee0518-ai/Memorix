using System.Security.Claims;
using KnowledgeEngine.Api.Controllers;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Security;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Runtime;
using KnowledgeEngine.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class CloudAuthorizationModelTests
{
    [Fact]
    public void OperatorControllers_RequirePlatformOperatorPolicy()
    {
        AssertPolicy<BetaUserController>(AuthorizationPolicies.PlatformOperator);
        AssertPolicy<PushNotificationsController>(AuthorizationPolicies.PlatformOperator);
    }

    [Fact]
    public void JwtToken_ContainsRoleClaim()
    {
        var service = new JwtTokenService(Options.Create(new KnowledgeEngine.Application.Settings.JwtSettings
        {
            Secret = "test-secret-that-is-long-enough-for-hmac-sha256-signing",
            Issuer = "tests",
            Audience = "tests"
        }));

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(service.GenerateToken(Guid.NewGuid(), "operator@example.com", PlatformRoles.Operator));

        Assert.Contains(token.Claims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == PlatformRoles.Operator);
    }

    [Fact]
    public async Task WorkspaceService_AssignsOwnerAndRejectsOtherUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var ownerId = Guid.NewGuid();
        var ownerService = CreateWorkspaceService(db, ownerId);
        var created = await ownerService.CreateWorkspaceAsync(new CreateWorkspaceDto
        {
            Name = "Owner workspace",
            Mode = "cloud"
        });

        Assert.Equal(ownerId, created.UserId);
        var otherService = CreateWorkspaceService(db, Guid.NewGuid());
        Assert.Null(await otherService.GetWorkspaceAsync(created.Id));
        Assert.Empty(await otherService.ListWorkspacesAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => otherService.SetCurrentWorkspaceAsync(Guid.NewGuid(), created.Id));
    }

    private static WorkspaceService CreateWorkspaceService(AppDbContext db, Guid userId) =>
        new(
            db,
            new MemoryConfigService(),
            new TestCurrentUser(userId),
            NullLogger<WorkspaceService>.Instance);

    private static void AssertPolicy<T>(string expected)
    {
        var attribute = typeof(T).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();
        Assert.Equal(expected, attribute.Policy);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "user@example.com";
        public bool IsAuthenticated => true;
    }

    private sealed class MemoryConfigService : IConfigService
    {
        private LocalConfig _config = new();
        public Task<LocalConfig> LoadConfigAsync(CancellationToken ct = default) => Task.FromResult(_config);
        public Task SaveConfigAsync(LocalConfig config, CancellationToken ct = default)
        {
            _config = config;
            return Task.CompletedTask;
        }
        public Task<string?> GetCurrentWorkspaceIdAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>(_config.CurrentWorkspaceId);
        public Task SetCurrentWorkspaceIdAsync(string workspaceId, CancellationToken ct = default)
        {
            _config.CurrentWorkspaceId = workspaceId;
            return Task.CompletedTask;
        }
    }
}
