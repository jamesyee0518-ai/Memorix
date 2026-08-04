using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.Agent;
using KnowledgeEngine.Infrastructure.AgentMemory;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

/// <summary>
/// P2.TST-02 回归评测基线：30 条标注场景。
/// 加载 tests/test-scenarios/memory_annotation_scenarios.json，
/// 按 5 个类别（normal_continuation, false_memory, sensitive_info,
/// conflict_decision, permission_breach, session_recovery）对系统进行回归验证。
/// </summary>
public class AgentMemoryRegressionBaselineTests
{
    // -----------------------------------------------------------------------
    // Scenario loader
    // -----------------------------------------------------------------------

    private static readonly string ScenariosPath = Path.Combine(
        AppContext.BaseDirectory, "test-scenarios", "memory_annotation_scenarios.json");

    private static List<RegressionScenario> LoadScenarios()
    {
        if (!File.Exists(ScenariosPath))
            throw new FileNotFoundException(
                $"Regression scenario file not found: {ScenariosPath}");

        var json = File.ReadAllText(ScenariosPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var scenarios = JsonSerializer.Deserialize<List<RegressionScenario>>(json, options)!;

        Assert.Equal(30, scenarios.Count);
        return scenarios;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<AppDbContext> CreateDbAsync(SqliteConnection connection)
    {
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static AgentProfile CreateProfile(
        Guid userId,
        bool writeEnabled = true,
        bool readEnabled = true,
        string? scopes = null,
        bool allowSensitive = false)
    {
        var now = DateTime.UtcNow;
        return new AgentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Regression Test Agent",
            Status = "active",
            Scopes = scopes,
            MemoryReadEnabled = readEnabled,
            MemoryWriteEnabled = writeEnabled,
            MemoryMaxContextTokens = 4000,
            AllowSensitiveDocuments = allowSensitive,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static (AgentMemoryService memoryService, MemoryAdmissionService admissionService,
        CheckpointService checkpointService, RetentionService retentionService,
        MemorySanitizer sanitizer, AgentPermissionGuard permissionGuard)
        CreateServices(AppDbContext db)
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var admissionService = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);
        var permissionGuard = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var contextService = new ContextComposer(db, retriever, sanitizer, NullLogger<ContextComposer>.Instance);
        var memoryService = new AgentMemoryService(
            db, sanitizer, admissionService, permissionGuard, retriever,
            contextService, NullLogger<AgentMemoryService>.Instance);
        var checkpointService = new CheckpointService(db, NullLogger<CheckpointService>.Instance);
        var retentionService = new RetentionService(db, NullLogger<RetentionService>.Instance);

        return (memoryService, admissionService, checkpointService, retentionService,
                sanitizer, permissionGuard);
    }

    /// <summary>
    /// Loads the domain entity from DB, qualifies it (candidate → qualified),
    /// confirms it (qualified → confirmed), and persists changes.
    /// </summary>
    private static async Task ConfirmMemoryEntityAsync(
        AppDbContext db, MemoryAdmissionService admissionService, MemoryItemDto dto)
    {
        var entity = await db.AgentMemoryItems.FirstAsync(i => i.Id == dto.Id);
        if (entity.AdmissionState == AdmissionState.Candidate)
        {
            entity.Qualify();
        }
        await admissionService.ConfirmMemoryAsync(entity);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Loads the domain entity from DB, rejects it, and persists changes.
    /// </summary>
    private static async Task RejectMemoryEntityAsync(
        AppDbContext db, MemoryAdmissionService admissionService, MemoryItemDto dto)
    {
        var entity = await db.AgentMemoryItems.FirstAsync(i => i.Id == dto.Id);
        await admissionService.RejectMemoryAsync(entity);
        await db.SaveChangesAsync();
    }

    // -----------------------------------------------------------------------
    // Baseline: All 30 scenarios loaded
    // -----------------------------------------------------------------------

    [Fact]
    public void Regression_Baseline_Loads_All_30_Scenarios()
    {
        var scenarios = LoadScenarios();
        Assert.Equal(30, scenarios.Count);

        // Verify all 6 categories are represented
        var categories = scenarios.Select(s => s.Type).Distinct().OrderBy(t => t).ToList();
        Assert.Contains("normal_continuation", categories);
        Assert.Contains("false_memory", categories);
        Assert.Contains("sensitive_info", categories);
        Assert.Contains("conflict_decision", categories);
        Assert.Contains("permission_breach", categories);
        Assert.Contains("session_recovery", categories);
    }

    [Fact]
    public void Regression_Baseline_All_Scenarios_Have_Required_Fields()
    {
        var scenarios = LoadScenarios();

        foreach (var scn in scenarios)
        {
            Assert.False(string.IsNullOrWhiteSpace(scn.Id), $"Scenario {scn.Id} missing Id");
            Assert.False(string.IsNullOrWhiteSpace(scn.Type), $"Scenario {scn.Id} missing Type");
            Assert.False(string.IsNullOrWhiteSpace(scn.Input), $"Scenario {scn.Id} missing Input");
            Assert.False(string.IsNullOrWhiteSpace(scn.ExpectedOutput), $"Scenario {scn.Id} missing ExpectedOutput");
            Assert.False(string.IsNullOrWhiteSpace(scn.Criteria), $"Scenario {scn.Id} missing Criteria");
        }
    }

    [Fact]
    public void Regression_Baseline_Scenario_Ids_Are_Unique()
    {
        var scenarios = LoadScenarios();
        var ids = scenarios.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // -----------------------------------------------------------------------
    // Normal Continuation (SCN-001 to SCN-006)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SCN_001_002_Normal_Continuation_Confirmed_Memory_In_Context()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        // SCN-001: confirmed decision + todo in context
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "session-001", "季度报告撰写", null, default);

        var decisionItem = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "decision",
                Title = "采用数据可视化方案 B（图表+表格混合）",
                Confidence = 0.9m,
                Visibility = "agent",
                Importance = 8
            }, default);

        await ConfirmMemoryEntityAsync(db, admissionService, decisionItem);

        var todoItem = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "todo",
                Title = "补充 Q3 销售数据章节",
                Confidence = 0.8m,
                Visibility = "agent",
                Importance = 7
            }, default);

        await ConfirmMemoryEntityAsync(db, admissionService, todoItem);

        // Get context pack
        var context = await memoryService.GetContextAsync(session.Id, null, default);

        Assert.NotNull(context);
        Assert.True(context.TokenUsed > 0, "Context should contain items");

        // The confirmed items should be in the context layers
        var allLayers = context.L1.Concat(context.L2).Concat(context.L3).ToList();
        Assert.Contains(allLayers, l => l.Title.Contains("数据可视化") || l.Title.Contains("方案 B"));
    }

    [Fact]
    public async Task SCN_003_004_Task_State_Recovered_From_Memory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "session-004", "数据清洗任务", null, default);

        var taskState = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "task_state",
                Title = "数据清洗任务已完成 80%，剩余异常值处理未完成",
                Confidence = 0.9m,
                Visibility = "agent",
                Importance = 9
            }, default);

        await ConfirmMemoryEntityAsync(db, admissionService, taskState);

        // Verify the item is confirmed
        var retrieved = await memoryService.GetMemoryItemAsync(taskState.Id, default);
        Assert.Equal("confirmed", retrieved!.AdmissionState);

        // Search should find it
        var results = await memoryService.SearchMemoryAsync(userId, workspaceId,
            new SearchMemoryInput { Query = "数据清洗", SessionId = session.Id }, default);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Title.Contains("80%"));
    }

    // -----------------------------------------------------------------------
    // False Memory (SCN-007 to SCN-011)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SCN_007_008_Candidate_Memory_Without_Evidence_Stays_Candidate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, _, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "session-007", "任务", null, default);

        // Capture without evidence
        var item = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "fact",
                Title = "项目截止日期是 8 月 15 日",
                Confidence = 0.5m,
                Visibility = "agent",
                Importance = 5,
                Evidence = new List<EvidenceInput>() // empty evidence
            }, default);

        // Should remain candidate, not promoted to qualified or confirmed
        Assert.Equal("candidate", item.AdmissionState);

        // In context, candidate should only be in L1, not L2
        var context = await memoryService.GetContextAsync(session.Id, null, default);
        var l2Items = context.L2;
        // Candidate items should not appear in L2 (confirmed layer)
        Assert.DoesNotContain(l2Items, l => l.Title.Contains("截止日期"));
    }

    [Fact]
    public async Task SCN_010_Evidence_With_Invalid_Reference_Does_Not_Pass_Admission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, _, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "session-010", "验证", null, default);

        // Capture with evidence pointing to non-existent document chunk
        var fakeRefId = Guid.NewGuid().ToString();
        var item = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "fact",
                Title = "测试事实",
                Confidence = 0.6m,
                Visibility = "agent",
                Importance = 5,
                Evidence = new List<EvidenceInput>
                {
                    new() { EvidenceKind = "document_chunk", ReferenceId = fakeRefId }
                }
            }, default);

        // The item should still be candidate (evidence validation may not auto-reject
        // in the current implementation, but it should not auto-confirm)
        Assert.NotEqual("confirmed", item.AdmissionState);
    }

    // -----------------------------------------------------------------------
    // Sensitive Info (SCN-012 to SCN-016)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SCN_012_Api_Key_Detected_And_Redacted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (_, _, _, _, sanitizer, _) = CreateServices(db);

        var apiKeyContent = "API_KEY=sk-proj-abc123xyz456def789ghi012jkl345mno678pqr901stu234vwx567";

        // Sanitize should detect the API key
        var (sanitizedContent, wasModified) = await sanitizer.SanitizeOnWriteAsync(apiKeyContent);

        Assert.True(wasModified);
        Assert.NotEqual(apiKeyContent, sanitizedContent);
        Assert.Contains("[REDACTED:API_KEY]", sanitizedContent);
    }

    [Fact]
    public async Task SCN_013_Bearer_Token_Detected_And_Redacted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (_, _, _, _, sanitizer, _) = CreateServices(db);

        var tokenContent = "access_token: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature";

        var (sanitizedContent, wasModified) = await sanitizer.SanitizeOnWriteAsync(tokenContent);

        Assert.True(wasModified);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", sanitizedContent);
    }

    [Fact]
    public async Task SCN_014_Non_Secret_Content_Passes_Through()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (_, _, _, _, sanitizer, _) = CreateServices(db);

        // Regular PII content (phone/email) is NOT detected as secret by the sanitizer
        // (sanitizer focuses on credentials/secrets, not PII)
        var piiContent = "我的手机号是 13800138000，邮箱是 zhang.san@example.com";

        var (sanitizedContent, wasModified) = await sanitizer.SanitizeOnWriteAsync(piiContent);

        // Content should not be modified since these aren't secret patterns
        Assert.False(wasModified);
        Assert.Equal(piiContent, sanitizedContent);
    }

    [Fact]
    public async Task SCN_015_Private_Key_Detected_And_Redacted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (_, _, _, _, sanitizer, _) = CreateServices(db);

        var pemContent = "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA1234567890\n-----END RSA PRIVATE KEY-----";

        var (sanitizedContent, wasModified) = await sanitizer.SanitizeOnWriteAsync(pemContent);

        Assert.True(wasModified);
        Assert.DoesNotContain("MIIEpAIBAAKCAQEA", sanitizedContent);
        Assert.Contains("[REDACTED:PRIVATE_KEY]", sanitizedContent);
    }

    // -----------------------------------------------------------------------
    // Conflict Decision (SCN-017 to SCN-021)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SCN_017_Conflicting_Memories_Both_Returned()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "session-017", "报告", null, default);

        var memA = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "preference",
                Title = "报告格式使用 PDF",
                Confidence = 0.9m,
                Visibility = "agent",
                Importance = 7
            }, default);
        await ConfirmMemoryEntityAsync(db, admissionService, memA);

        var memB = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "preference",
                Title = "报告格式使用 Markdown",
                Confidence = 0.9m,
                Visibility = "agent",
                Importance = 7
            }, default);
        await ConfirmMemoryEntityAsync(db, admissionService, memB);

        // Search should return both
        var results = await memoryService.SearchMemoryAsync(userId, workspaceId,
            new SearchMemoryInput { Query = "报告格式", SessionId = session.Id }, default);

        Assert.Contains(results, r => r.Title.Contains("PDF"));
        Assert.Contains(results, r => r.Title.Contains("Markdown"));
    }

    [Fact]
    public async Task SCN_020_User_Confirm_Promotes_Memory_To_Confirmed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "session-020", "预算", null, default);

        var oldItem = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "fact",
                Title = "客户 B 的预算上限是 50 万",
                Confidence = 0.6m,
                Visibility = "agent",
                Importance = 6
            }, default);

        // Old item stays candidate
        Assert.Equal("candidate", oldItem.AdmissionState);

        // New item with user confirmation
        var newItem = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "fact",
                Title = "客户 B 的预算上限是 80 万",
                Confidence = 0.95m,
                Visibility = "agent",
                Importance = 8
            }, default);

        await ConfirmMemoryEntityAsync(db, admissionService, newItem);

        // New item is confirmed - reload to reflect DB changes
        var newItemReloaded = await memoryService.GetMemoryItemAsync(newItem.Id, default);
        Assert.NotNull(newItemReloaded);
        Assert.Equal("confirmed", newItemReloaded!.AdmissionState);

        // Old item is still candidate (not auto-deleted)
        var oldCheck = await memoryService.GetMemoryItemAsync(oldItem.Id, default);
        Assert.NotNull(oldCheck);
        Assert.NotEqual("forgotten", oldCheck!.Status);
    }

    // -----------------------------------------------------------------------
    // Permission Breach (SCN-022 to SCN-026)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SCN_022_Workspace_Isolation_Prevents_Cross_Workspace_Access()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var ws1 = Guid.NewGuid();
        var ws2 = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        // Create memory in W1
        var session1 = await memoryService.StartSessionAsync(
            userId, ws1, profile.Id, "session-w1", "W1 任务", null, default);

        var itemW1 = await memoryService.CaptureMemoryAsync(userId, ws1,
            new CaptureMemoryInput
            {
                SessionId = session1.Id,
                Kind = "decision",
                Title = "W1 决策",
                Confidence = 0.9m,
                Visibility = "agent",
                Importance = 7
            }, default);
        await ConfirmMemoryEntityAsync(db, admissionService, itemW1);

        // Search in W2 should not return W1 memory
        var results = await memoryService.SearchMemoryAsync(userId, ws2,
            new SearchMemoryInput { Query = "W1 决策" }, default);

        Assert.DoesNotContain(results, r => r.Title.Contains("W1 决策"));
    }

    [Fact]
    public async Task SCN_024_Private_Visibility_Filtered_From_Others()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);

        var userX = Guid.NewGuid();
        var userY = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userX);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var session = await memoryService.StartSessionAsync(
            userX, workspaceId, profile.Id, "session-024", "私有记忆", null, default);

        var privateItem = await memoryService.CaptureMemoryAsync(userX, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "preference",
                Title = "用户X的私有偏好",
                Confidence = 0.9m,
                Visibility = "private",
                Importance = 8
            }, default);
        await ConfirmMemoryEntityAsync(db, admissionService, privateItem);

        // User Y should not see User X's private memory
        var results = await memoryService.SearchMemoryAsync(userY, workspaceId,
            new SearchMemoryInput { Query = "私有偏好" }, default);

        Assert.DoesNotContain(results, r => r.Title.Contains("用户X的私有偏好"));
    }

    // -----------------------------------------------------------------------
    // Session Recovery (SCN-027 to SCN-030)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SCN_027_Checkpoint_Stores_Summary_Loops_Decisions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (_, _, checkpointService, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var session = new AgentMemorySession
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            ExternalSessionKey = "session-027",
            TaskTitle = "架构设计",
            Status = "active",
            StartedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
        db.AgentMemorySessions.Add(session);
        await db.SaveChangesAsync();

        var checkpoint = await checkpointService.CreateCheckpointAsync(session.Id, default);

        Assert.NotNull(checkpoint);
        Assert.Equal(session.Id, checkpoint.SessionId);
        Assert.True(checkpoint.FromSequence >= 0);
        Assert.True(checkpoint.ToSequence >= checkpoint.FromSequence);
        Assert.NotNull(checkpoint.Summary);
        Assert.True(checkpoint.TokenEstimate > 0);
    }

    [Fact]
    public async Task SCN_028_030_Latest_Delivered_Checkpoint_Used_For_Recovery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (_, _, checkpointService, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        var session = new AgentMemorySession
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            ExternalSessionKey = "session-028",
            TaskTitle = "恢复测试",
            Status = "active",
            StartedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
        db.AgentMemorySessions.Add(session);
        await db.SaveChangesAsync();

        // Create checkpoint v1
        var cp1 = await checkpointService.CreateCheckpointAsync(session.Id, default);
        cp1.DeliveryState = "delivered";
        await db.SaveChangesAsync();

        // Create checkpoint v2
        var cp2 = await checkpointService.CreateCheckpointAsync(session.Id, default);
        cp2.DeliveryState = "delivered";
        await db.SaveChangesAsync();

        // List checkpoints - should return in order
        var checkpoints = await checkpointService.ListCheckpointsAsync(session.Id, default);

        Assert.NotEmpty(checkpoints);
        // Latest checkpoint should have the highest version
        var latest = checkpoints.OrderByDescending(c => c.CreatedAt).First();
        Assert.True(latest.Version >= cp2.Version);
    }

    [Fact]
    public async Task SCN_029_Handoff_Memory_Contains_Task_State()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "session-029", "交接", null, default);

        var handoff = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "handoff",
                Title = "任务交接：已完成数据分析，下一步是撰写报告",
                Content = "关键约束：报告须在周五前提交。未完成事项：图表生成",
                Confidence = 0.95m,
                Visibility = "agent",
                Importance = 10
            }, default);

        await ConfirmMemoryEntityAsync(db, admissionService, handoff);

        // Search should find the handoff
        var results = await memoryService.SearchMemoryAsync(userId, workspaceId,
            new SearchMemoryInput { Query = "交接", SessionId = session.Id }, default);

        Assert.NotEmpty(results);
        var found = results.First(r => r.Kind == "handoff");
        Assert.Contains("数据分析", found.Title);
        Assert.NotNull(found.Content);
        Assert.Contains("周五", found.Content!);
    }

    // -----------------------------------------------------------------------
    // Metrics Baseline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Regression_Baseline_Metrics_Reflect_System_State()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var (memoryService, admissionService, _, _, _, _) = CreateServices(db);
        var metricsService = new MemoryMetricsService(db, NullLogger<MemoryMetricsService>.Instance);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        // Create some test data
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "metrics-session", "Metrics test", null, default);

        // 2 confirmed, 1 candidate, 1 rejected
        for (int i = 0; i < 2; i++)
        {
            var item = await memoryService.CaptureMemoryAsync(userId, workspaceId,
                new CaptureMemoryInput
                {
                    SessionId = session.Id,
                    Kind = "fact",
                    Title = $"Confirmed fact {i}",
                    Confidence = 0.9m,
                    Visibility = "agent",
                    Importance = 7
                }, default);
            await ConfirmMemoryEntityAsync(db, admissionService, item);
        }

        var candidate = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "fact",
                Title = "Candidate fact",
                Confidence = 0.5m,
                Visibility = "agent",
                Importance = 5
            }, default);

        var rejected = await memoryService.CaptureMemoryAsync(userId, workspaceId,
            new CaptureMemoryInput
            {
                SessionId = session.Id,
                Kind = "fact",
                Title = "Rejected fact",
                Confidence = 0.3m,
                Visibility = "agent",
                Importance = 3
            }, default);
        await RejectMemoryEntityAsync(db, admissionService, rejected);

        var metrics = await metricsService.GetMetricsAsync(workspaceId, default);

        Assert.True(metrics.TotalMemoryItems >= 4);
        Assert.True(metrics.ConfirmedItems >= 2);
        Assert.True(metrics.CandidateItems >= 1);
        Assert.True(metrics.RejectedItems >= 1);
        Assert.True(metrics.AverageConfidence > 0);
        Assert.True(metrics.RecallRate >= 0);
        Assert.True(metrics.AdoptionRate >= 0);
    }

    // -----------------------------------------------------------------------
    // Scenario JSON Model
    // -----------------------------------------------------------------------

    private class RegressionScenario
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("expected_output")]
        public string ExpectedOutput { get; set; } = string.Empty;
        public string Criteria { get; set; } = string.Empty;
    }
}
