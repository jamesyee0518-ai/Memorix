using System.Security.Claims;
using KnowledgeEngine.Api.Controllers;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.Agent;
using KnowledgeEngine.Infrastructure.AgentMemory;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

/// <summary>
/// Contract tests for AgentMemoryController endpoints and MCP tool contracts.
/// Uses Moq to mock IAgentMemoryService and ICurrentUserContext for controller tests,
/// and real services with SQLite in-memory for token budget and permission tests.
/// </summary>
public class AgentMemoryContractTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (Guid userId, Guid workspaceId) CreateUserAndWorkspace()
    {
        return (Guid.NewGuid(), Guid.NewGuid());
    }

    private static Mock<ICurrentUserContext> CreateMockUserContext(Guid? userId)
    {
        var mock = new Mock<ICurrentUserContext>();
        mock.SetupGet(x => x.UserId).Returns(userId);
        mock.SetupGet(x => x.Email).Returns("test@example.com");
        mock.SetupGet(x => x.IsAuthenticated).Returns(userId.HasValue);
        return mock;
    }

    private static ControllerContext CreateControllerContext(Guid workspaceId)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("workspace_id", workspaceId.ToString())
                }, "TestAuth"))
            }
        };
    }

    private static AgentMemoryController CreateController(
        Mock<IAgentMemoryService> mockService,
        Mock<ICurrentUserContext> mockUser,
        Guid workspaceId)
    {
        var controller = new AgentMemoryController(
            mockService.Object, mockUser.Object, null!, null!, null!, null!, null!)
        {
            ControllerContext = CreateControllerContext(workspaceId)
        };
        return controller;
    }

    // -----------------------------------------------------------------------
    // POST /api/agent-memory/sessions - StartSession
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartSession_Returns201Created()
    {
        var (userId, workspaceId) = CreateUserAndWorkspace();
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(userId);
        var controller = CreateController(mockService, mockUser, workspaceId);

        var session = new SessionDto
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Status = "active",
            TaskTitle = "Test task",
            StartedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
        mockService
            .Setup(s => s.StartSessionAsync(
                userId, workspaceId, It.IsAny<Guid?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var request = new StartSessionRequest
        {
            ExternalSessionKey = "ext-001",
            TaskTitle = "Test task"
        };

        var result = await controller.StartSession(request, null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<SessionDto>>(objectResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal(session.Id, apiResponse.Data!.Id);
    }

    [Fact]
    public async Task StartSession_WithoutUserId_Returns401()
    {
        var workspaceId = Guid.NewGuid();
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(null); // No user ID
        var controller = CreateController(mockService, mockUser, workspaceId);

        var request = new StartSessionRequest
        {
            ExternalSessionKey = "ext-001",
            TaskTitle = "Test task"
        };

        var result = await controller.StartSession(request, null, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        mockService.Verify(
            s => s.StartSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // GET /api/agent-memory/sessions - ListSessions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListSessions_Returns200OK()
    {
        var (userId, workspaceId) = CreateUserAndWorkspace();
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(userId);
        var controller = CreateController(mockService, mockUser, workspaceId);

        var sessions = new List<SessionDto>
        {
            new() { Id = Guid.NewGuid(), TaskTitle = "Task 1" },
            new() { Id = Guid.NewGuid(), TaskTitle = "Task 2" }
        };
        mockService
            .Setup(s => s.ListSessionsAsync(userId, workspaceId, 50, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        var result = await controller.ListSessions(50, 0, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<List<SessionDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(2, apiResponse.Data!.Count);
    }

    // -----------------------------------------------------------------------
    // GET /api/agent-memory/sessions/{id} - GetSession
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetSession_WithValidId_Returns200OK()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(Guid.NewGuid());
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        var sessionId = Guid.NewGuid();
        var session = new SessionDto { Id = sessionId, TaskTitle = "Test" };
        mockService
            .Setup(s => s.GetSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await controller.GetSession(sessionId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<SessionDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(sessionId, apiResponse.Data!.Id);
    }

    [Fact]
    public async Task GetSession_WithNonExistentId_Returns404NotFound()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(Guid.NewGuid());
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        mockService
            .Setup(s => s.GetSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionDto?)null);

        var result = await controller.GetSession(Guid.NewGuid(), CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(notFoundResult.Value);
        Assert.False(apiResponse.Success);
    }

    // -----------------------------------------------------------------------
    // POST /api/agent-memory/sessions/{id}/close - CloseSession
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CloseSession_Returns204NoContent()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(Guid.NewGuid());
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        var sessionId = Guid.NewGuid();
        mockService
            .Setup(s => s.CloseSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await controller.CloseSession(sessionId, null, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        mockService.Verify(s => s.CloseSessionAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // POST /api/agent-memory/items - CaptureMemory
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CaptureMemory_Returns201Created()
    {
        var (userId, workspaceId) = CreateUserAndWorkspace();
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(userId);
        var controller = CreateController(mockService, mockUser, workspaceId);

        var item = new MemoryItemDto
        {
            Id = Guid.NewGuid(),
            Title = "Test memory",
            Kind = "decision",
            AdmissionState = "qualified"
        };
        mockService
            .Setup(s => s.CaptureMemoryAsync(
                userId, workspaceId, It.IsAny<CaptureMemoryInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var input = new CaptureMemoryInput
        {
            Kind = "decision",
            Title = "Test memory",
            Content = "Some content"
        };

        var result = await controller.CaptureMemory(input, null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<MemoryItemDto>>(objectResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal(item.Id, apiResponse.Data!.Id);
    }

    [Fact]
    public async Task CaptureMemory_WithoutUserId_Returns401()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(null);
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        var input = new CaptureMemoryInput { Title = "Test", Content = "Content" };

        var result = await controller.CaptureMemory(input, null, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        mockService.Verify(
            s => s.CaptureMemoryAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<CaptureMemoryInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // GET /api/agent-memory/items/{id} - GetMemoryItem
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetMemoryItem_WithValidId_Returns200OK()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(Guid.NewGuid());
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        var itemId = Guid.NewGuid();
        var item = new MemoryItemDto { Id = itemId, Title = "Test item" };
        mockService
            .Setup(s => s.GetMemoryItemAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await controller.GetMemoryItem(itemId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<MemoryItemDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(itemId, apiResponse.Data!.Id);
    }

    [Fact]
    public async Task GetMemoryItem_WithNonExistentId_Returns404NotFound()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(Guid.NewGuid());
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        mockService
            .Setup(s => s.GetMemoryItemAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MemoryItemDto?)null);

        var result = await controller.GetMemoryItem(Guid.NewGuid(), CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
    }

    // -----------------------------------------------------------------------
    // POST /api/agent-memory/search - SearchMemory
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SearchMemory_Returns200OK_WithList()
    {
        var (userId, workspaceId) = CreateUserAndWorkspace();
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(userId);
        var controller = CreateController(mockService, mockUser, workspaceId);

        var items = new List<MemoryItemDto>
        {
            new() { Id = Guid.NewGuid(), Title = "Result 1" },
            new() { Id = Guid.NewGuid(), Title = "Result 2" }
        };
        mockService
            .Setup(s => s.SearchMemoryAsync(
                userId, workspaceId, It.IsAny<SearchMemoryInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var input = new SearchMemoryInput { Query = "test", Limit = 10 };

        var result = await controller.SearchMemory(input, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<List<MemoryItemDto>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(2, apiResponse.Data!.Count);
    }

    [Fact]
    public async Task SearchMemory_WithoutUserId_Returns401()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(null);
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        var input = new SearchMemoryInput { Query = "test" };

        var result = await controller.SearchMemory(input, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    // -----------------------------------------------------------------------
    // POST /api/agent-memory/sessions/{id}/context - GetContext
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetContext_Returns200OK_WithContextPackDto()
    {
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(Guid.NewGuid());
        var controller = CreateController(mockService, mockUser, Guid.NewGuid());

        var sessionId = Guid.NewGuid();
        var contextPack = new ContextPackDto
        {
            SessionId = sessionId,
            TokenBudget = 2000,
            TokenUsed = 500,
            L1 = new List<ContextLayerDto>(),
            L2 = new List<ContextLayerDto>(),
            L3 = new List<ContextLayerDto>()
        };
        mockService
            .Setup(s => s.GetContextAsync(sessionId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextPack);

        var result = await controller.GetContext(sessionId, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<ContextPackDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(sessionId, apiResponse.Data!.SessionId);
        Assert.Equal(2000, apiResponse.Data.TokenBudget);
    }

    // -----------------------------------------------------------------------
    // Token budget truncation in ContextComposer
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ContextComposer_TokenBudgetTruncation_TokenUsedDoesNotExceedBudget()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var composer = new ContextComposer(db, retriever, sanitizer, NullLogger<ContextComposer>.Instance);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        // Create a session
        db.AgentMemorySessions.Add(new AgentMemorySession
        {
            Id = sessionId,
            WorkspaceId = workspaceId,
            UserId = userId,
            Status = "active",
            StartedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        });

        // Create many L1 items (TaskState/Todo/Blocker/Handoff) with long content
        // to exceed a small token budget
        for (var i = 0; i < 20; i++)
        {
            var now = DateTime.UtcNow;
            db.AgentMemoryItems.Add(new AgentMemoryItem
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                WorkspaceId = workspaceId,
                OwnerUserId = userId,
                Kind = MemoryKind.Todo,
                Title = $"Todo item {i} with a reasonably long title to consume tokens",
                Content = $"This is a very long content for todo item number {i} that is designed to consume " +
                          $"a significant number of tokens when composed into the context pack layer. " +
                          $"Each item should contribute meaningfully to the total token count.",
                AdmissionState = AdmissionState.Confirmed,
                Confidence = 0.8m,
                Importance = 7,
                Status = MemoryStatus.Active,
                FreshnessAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        // Also create L2 items (confirmed Decision/Preference/etc.)
        for (var i = 0; i < 10; i++)
        {
            var now = DateTime.UtcNow;
            db.AgentMemoryItems.Add(new AgentMemoryItem
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                WorkspaceId = workspaceId,
                OwnerUserId = userId,
                Kind = MemoryKind.Decision,
                Title = $"Decision {i} with long title for token consumption testing",
                Content = $"This is the content for decision {i}. It contains enough text to be meaningful " +
                          $"and to consume tokens when included in the L2 layer of the context pack.",
                AdmissionState = AdmissionState.Confirmed,
                Confidence = 0.9m,
                Importance = 8,
                Status = MemoryStatus.Active,
                FreshnessAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();

        // Use a very small token budget
        var smallBudget = 100;
        var context = await composer.BuildContextPackAsync(sessionId, smallBudget, CancellationToken.None);

        Assert.Equal(smallBudget, context.TokenBudget);
        Assert.True(context.TokenUsed <= smallBudget,
            $"TokenUsed ({context.TokenUsed}) should not exceed TokenBudget ({smallBudget})");
    }

    [Fact]
    public async Task ContextComposer_WithLargeBudget_IncludesAllItems()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var composer = new ContextComposer(db, retriever, sanitizer, NullLogger<ContextComposer>.Instance);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(new AgentMemorySession
        {
            Id = sessionId,
            WorkspaceId = workspaceId,
            UserId = userId,
            Status = "active",
            StartedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        });

        var now = DateTime.UtcNow;
        db.AgentMemoryItems.Add(new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            WorkspaceId = workspaceId,
            OwnerUserId = userId,
            Kind = MemoryKind.Todo,
            Title = "Simple todo",
            Content = "Do something",
            AdmissionState = AdmissionState.Confirmed,
            Confidence = 0.8m,
            Importance = 5,
            Status = MemoryStatus.Active,
            FreshnessAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 10000, CancellationToken.None);

        Assert.True(context.TokenUsed <= 10000);
        Assert.NotEmpty(context.L1); // Todo is L1
    }

    [Fact]
    public async Task ContextComposer_WithNonExistentSession_ReturnsEmptyPack()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var composer = new ContextComposer(db, retriever, sanitizer, NullLogger<ContextComposer>.Instance);

        var context = await composer.BuildContextPackAsync(Guid.NewGuid(), 2000, CancellationToken.None);

        Assert.Equal(0, context.TokenUsed);
        Assert.Empty(context.L1);
        Assert.Empty(context.L2);
        Assert.Empty(context.L3);
    }

    // -----------------------------------------------------------------------
    // Permission denial: mock IAgentPermissionGuard.CanWriteMemoryAsync -> false
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CaptureMemory_WhenPermissionDenied_ThrowsUnauthorizedAccessException()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        // Create a session with an agent profile
        var session = new AgentMemorySession
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            AgentProfileId = profileId,
            ExternalSessionKey = "ext-001",
            TaskTitle = "Test",
            Status = "active",
            StartedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
        db.AgentMemorySessions.Add(session);

        // Create agent profile
        db.AgentProfiles.Add(new AgentProfile
        {
            Id = profileId,
            UserId = userId,
            Name = "Test Agent",
            Status = "active",
            MemoryWriteEnabled = true,
            MemoryReadEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Mock the permission guard to deny write
        var mockGuard = new Mock<IAgentPermissionGuard>();
        mockGuard
            .Setup(g => g.CanWriteMemoryAsync(userId, profileId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var admissionService = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var mockContextService = new Mock<IAgentContextService>();

        var service = new AgentMemoryService(
            db,
            sanitizer,
            admissionService,
            mockGuard.Object,
            retriever,
            mockContextService.Object,
            NullLogger<AgentMemoryService>.Instance);

        var input = new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "fact",
            Title = "Should be denied",
            Content = "Content"
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.CaptureMemoryAsync(userId, workspaceId, input));

        Assert.Contains("permission", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CaptureMemory_WhenPermissionAllowed_Succeeds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        var session = new AgentMemorySession
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            AgentProfileId = profileId,
            ExternalSessionKey = "ext-001",
            TaskTitle = "Test",
            Status = "active",
            StartedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
        db.AgentMemorySessions.Add(session);
        db.AgentProfiles.Add(new AgentProfile
        {
            Id = profileId,
            UserId = userId,
            Name = "Test Agent",
            Status = "active",
            MemoryWriteEnabled = true,
            MemoryReadEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Mock the permission guard to allow write
        var mockGuard = new Mock<IAgentPermissionGuard>();
        mockGuard
            .Setup(g => g.CanWriteMemoryAsync(userId, profileId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var admissionService = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var mockContextService = new Mock<IAgentContextService>();

        var service = new AgentMemoryService(
            db,
            sanitizer,
            admissionService,
            mockGuard.Object,
            retriever,
            mockContextService.Object,
            NullLogger<AgentMemoryService>.Instance);

        var input = new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "fact",
            Title = "Should succeed",
            Content = "Normal content",
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-1" } }
        };

        var item = await service.CaptureMemoryAsync(userId, workspaceId, input);

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal("qualified", item.AdmissionState);
    }

    // -----------------------------------------------------------------------
    // ListSessions without workspace claim returns 401
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListSessions_WithoutWorkspaceClaim_Returns401()
    {
        var userId = Guid.NewGuid();
        var mockService = new Mock<IAgentMemoryService>();
        var mockUser = CreateMockUserContext(userId);

        // Controller without workspace_id claim
        var controller = new AgentMemoryController(
            mockService.Object, mockUser.Object, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        var result = await controller.ListSessions(50, 0, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }
}
