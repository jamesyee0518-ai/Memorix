using KnowledgeEngine.Api.Controllers;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class WorkspaceInboxAuthorizationTests
{
    [Fact]
    public async Task DifferentUser_CannotReadWorkspaceChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
        await db.Database.EnsureCreatedAsync();
        var workspaceId = Guid.CreateVersion7();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            UserId = Guid.CreateVersion7(),
            Name = "Private cloud workspace",
            Mode = "cloud",
            StorageProvider = "postgres",
            FileProvider = "minio",
            JobProvider = "redis",
            ModelProvider = "cloud",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = new WorkspaceInboxController(
            null!,
            new TestWorkspaceAuthorizationService(
                WorkspaceAccessResult.Forbidden))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.Changes(
            workspaceId, null, 100, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    private sealed class TestWorkspaceAuthorizationService(
        WorkspaceAccessResult result) : IWorkspaceAuthorizationService
    {
        public Task<WorkspaceAccessResult> AuthorizeAsync(
            Guid workspaceId,
            CancellationToken ct = default) => Task.FromResult(result);
    }
}
