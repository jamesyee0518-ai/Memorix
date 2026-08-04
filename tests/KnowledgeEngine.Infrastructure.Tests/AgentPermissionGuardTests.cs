using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Agent;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

/// <summary>
/// P-1.ENG-03 测试基线：针对 <see cref="AgentPermissionGuard"/> 的单元测试。
/// AgentPermissionGuard 依赖 IAppDbContext，这里使用 Sqlite 内存数据库
/// （通过具体实现 AppDbContext）来提供真实的数据访问行为。
/// </summary>
public class AgentPermissionGuardTests
{
    private static async Task<AppDbContext> CreateDbAsync(SqliteConnection connection)
    {
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static AgentProfile NewProfile(
        Guid id,
        Guid userId,
        string? scopes = null,
        bool allowSensitive = false,
        string status = "active")
    {
        var now = DateTime.UtcNow;
        return new AgentProfile
        {
            Id = id,
            UserId = userId,
            Name = "Test Agent",
            Status = status,
            Scopes = scopes,
            AllowSensitiveDocuments = allowSensitive,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    [Fact]
    public async Task CanUseToolAsync_DefaultTools_ReturnsTrue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var guard = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);

        var userId = Guid.NewGuid();

        // 未指定 agentProfileId 时，默认只读工具应全部放行
        var defaultTools = new[]
        {
            "list_topics",
            "search_memory",
            "ask_memory",
            "get_document",
            "get_report"
        };

        foreach (var tool in defaultTools)
        {
            Assert.True(
                await guard.CanUseToolAsync(userId, null, tool),
                $"Default tool '{tool}' should be allowed when no profile is specified.");
        }
    }

    [Fact]
    public async Task CanUseToolAsync_NonDefaultTool_ReturnsFalse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var guard = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);

        var userId = Guid.NewGuid();

        // 写入类工具与未知工具均不在默认放行列表中，应被拒绝
        Assert.False(await guard.CanUseToolAsync(userId, null, "create_inbox_item"));
        Assert.False(await guard.CanUseToolAsync(userId, null, "import_url"));
        Assert.False(await guard.CanUseToolAsync(userId, null, "delete_everything"));
        Assert.False(await guard.CanUseToolAsync(userId, null, "nonsense_tool"));
    }

    [Fact]
    public async Task HasScopeAsync_WithExplicitScopes_ReturnsTrue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.AgentProfiles.Add(NewProfile(
            profileId,
            userId,
            scopes: "[\"workspace:read\",\"search:read\",\"rag:read\"]"));
        await db.SaveChangesAsync();

        var guard = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);

        // 显式设置的 scope 应返回 true
        Assert.True(await guard.HasScopeAsync(profileId, "workspace:read"));
        Assert.True(await guard.HasScopeAsync(profileId, "search:read"));
        Assert.True(await guard.HasScopeAsync(profileId, "rag:read"));

        // 未在显式列表中的 scope 应返回 false
        Assert.False(await guard.HasScopeAsync(profileId, "inbox:write"));
    }

    [Fact]
    public async Task FilterSensitiveDocumentsAsync_RemovesSensitive()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        // 配置为不允许访问敏感文档
        db.AgentProfiles.Add(NewProfile(profileId, userId, allowSensitive: false));
        await db.SaveChangesAsync();

        var guard = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);

        var normalDoc = new Document
        {
            Id = Guid.NewGuid(), UserId = userId, SourceId = Guid.NewGuid(),
            Title = "公开文档", SensitivityLevel = "normal"
        };
        var privateDoc = new Document
        {
            Id = Guid.NewGuid(), UserId = userId, SourceId = Guid.NewGuid(),
            Title = "私有文档", SensitivityLevel = "private"
        };
        var sensitiveDoc = new Document
        {
            Id = Guid.NewGuid(), UserId = userId, SourceId = Guid.NewGuid(),
            Title = "敏感文档", SensitivityLevel = "sensitive"
        };
        var restrictedDoc = new Document
        {
            Id = Guid.NewGuid(), UserId = userId, SourceId = Guid.NewGuid(),
            Title = "受限文档", SensitivityLevel = "restricted"
        };

        var docs = new List<Document> { normalDoc, privateDoc, sensitiveDoc, restrictedDoc };

        // 不允许敏感文档时，private/sensitive/restricted 都应被过滤掉
        var filtered = await guard.FilterSensitiveDocumentsAsync(docs, profileId);

        Assert.Single(filtered);
        Assert.Equal(normalDoc.Id, filtered[0].Id);

        // 补充验证：当 profile 允许敏感文档时，应返回全部
        var allowAllProfileId = Guid.NewGuid();
        db.AgentProfiles.Add(NewProfile(allowAllProfileId, userId, allowSensitive: true));
        await db.SaveChangesAsync();

        // 使用新的 guard 实例以避免 profile 缓存影响
        var guard2 = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);
        var filtered2 = await guard2.FilterSensitiveDocumentsAsync(docs, allowAllProfileId);
        Assert.Equal(4, filtered2.Count);
    }
}
