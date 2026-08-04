using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Agent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

/// <summary>
/// P-1.ENG-03 测试基线：针对 <see cref="AgentToolService"/>（实现 IAgentToolService）的单元测试。
/// 使用 xunit + Moq：IAppDbContext、IAgentPermissionGuard、IUsageService 等依赖均通过 Moq 注入。
/// </summary>
public class AgentToolServiceTests
{
    /// <summary>
    /// 构造一个最小可用的 IAppDbContext mock。
    /// InvokeToolAsync 会在每次调用时向 AgentInvocationLogs 写入一条日志并调用 SaveChangesAsync，
    /// 因此需要为这两者提供默认实现。
    /// </summary>
    private static Mock<IAppDbContext> CreateDbMock()
    {
        var db = new Mock<IAppDbContext>();
        var logs = new Mock<DbSet<AgentInvocationLog>>();
        db.SetupGet(x => x.AgentInvocationLogs).Returns(logs.Object);
        db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(1);
        return db;
    }

    private static AgentToolService CreateService(
        Mock<IAppDbContext> db,
        Mock<IAgentPermissionGuard>? guard = null,
        Mock<IUsageService>? usage = null)
    {
        guard ??= new Mock<IAgentPermissionGuard>();
        usage ??= new Mock<IUsageService>();
        usage.Setup(x => x.RecordAgentUsageAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new AgentToolService(
            db.Object,
            Mock.Of<ISearchService>(),
            Mock.Of<IQaService>(),
            guard.Object,
            usage.Object,
            NullLogger<AgentToolService>.Instance);
    }

    [Fact]
    public async Task ListToolsAsync_ReturnsDefaultTools()
    {
        var db = CreateDbMock();
        var service = CreateService(db);

        // 未指定 agentProfileId 时，应返回全部默认工具
        var tools = await service.ListToolsAsync(null);

        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("list_topics", names);
        Assert.Contains("search_memory", names);
        Assert.Contains("ask_memory", names);
        Assert.Contains("get_document", names);
        Assert.Contains("get_report", names);
        Assert.Contains("create_inbox_item", names);
        Assert.Contains("import_url", names);

        // 默认工具集共 7 个
        Assert.Equal(7, tools.Count);
    }

    [Fact]
    public async Task InvokeToolAsync_InvalidTool_Throws()
    {
        var db = CreateDbMock();

        // 放行权限检查，使流程进入工具分发逻辑（从而命中未知工具分支）
        var guard = new Mock<IAgentPermissionGuard>();
        guard.Setup(x => x.CanUseToolAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(db, guard);

        // 注意：当前实现对于未知工具名不会抛出异常，而是返回 Success=false 且
        // Error 包含 "Unknown tool" 的失败结果。本测试固定该行为；
        // 若未来改为抛出异常，应同步更新为 Assert.ThrowsAsync。
        var result = await service.InvokeToolAsync(
            Guid.NewGuid(),
            "this_tool_does_not_exist",
            new Dictionary<string, object>());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Unknown tool", result.Error);
    }
}
