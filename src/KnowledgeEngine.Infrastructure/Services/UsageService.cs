using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Services;

public class UsageService : IUsageService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<UsageService> _logger;

    public UsageService(IAppDbContext db, ILogger<UsageService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ===== GetUsage =====

    public async Task<ApiResponse<UsageResponse>> GetUsageAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var sevenDaysAgo = today.AddDays(-6); // last 7 days including today

            // Get today's usage
            var todayUsage = await _db.UserUsageDaily
                .FirstOrDefaultAsync(u => u.UserId == userId && u.UsageDate == today, ct);

            // Get last 7 days
            var last7Days = await _db.UserUsageDaily
                .Where(u => u.UserId == userId && u.UsageDate >= sevenDaysAgo && u.UsageDate <= today)
                .OrderBy(u => u.UsageDate)
                .ToListAsync(ct);

            // Build response
            var response = new UsageResponse
            {
                Today = todayUsage != null ? ToUsageDailyItem(todayUsage) : new UsageDailyItem { Date = today },
                Last7Days = last7Days.Select(ToUsageDailyItem).ToList(),
                Totals = new UsageTotals
                {
                    SearchCount = last7Days.Sum(u => u.SearchCount),
                    QaCount = last7Days.Sum(u => u.QaCount),
                    ReportCount = last7Days.Sum(u => u.ReportCount),
                    ApiCallCount = last7Days.Sum(u => u.ApiCallCount),
                    AgentCallCount = last7Days.Sum(u => u.AgentCallCount),
                    InputTokens = last7Days.Sum(u => u.InputTokens),
                    OutputTokens = last7Days.Sum(u => u.OutputTokens)
                }
            };

            return ApiResponse<UsageResponse>.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get usage for user {UserId}", userId);
            return ApiResponse<UsageResponse>.Fail("get_usage_error", ex.Message);
        }
    }

    // ===== RecordUsage =====

    public async Task RecordUsageAsync(Guid userId, UsageType usageType, int count = 1, CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;

            var usage = await _db.UserUsageDaily
                .FirstOrDefaultAsync(u => u.UserId == userId && u.UsageDate == today, ct);

            if (usage == null)
            {
                usage = new UserUsageDaily
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    UsageDate = today,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.UserUsageDaily.Add(usage);
            }

            switch (usageType)
            {
                case UsageType.Search:
                    usage.SearchCount += count;
                    break;
                case UsageType.Qa:
                    usage.QaCount += count;
                    break;
                case UsageType.Report:
                    usage.ReportCount += count;
                    break;
                case UsageType.Export:
                    usage.ExportCount += count;
                    break;
                case UsageType.ApiCall:
                    usage.ApiCallCount += count;
                    break;
                case UsageType.Import:
                    usage.ImportedCount += count;
                    break;
                case UsageType.Document:
                    usage.DocumentCount += count;
                    break;
            }

            usage.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record usage {UsageType} for user {UserId}", usageType, userId);
        }
    }

    // ===== RecordTokens =====

    public async Task RecordTokensAsync(Guid userId, int inputTokens, int outputTokens, CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;

            var usage = await _db.UserUsageDaily
                .FirstOrDefaultAsync(u => u.UserId == userId && u.UsageDate == today, ct);

            if (usage == null)
            {
                usage = new UserUsageDaily
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    UsageDate = today,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.UserUsageDaily.Add(usage);
            }

            usage.InputTokens += inputTokens;
            usage.OutputTokens += outputTokens;
            usage.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record tokens for user {UserId}", userId);
        }
    }

    // ===== RecordAgentUsage =====

    public async Task RecordAgentUsageAsync(Guid userId, string toolName, bool success, CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;

            var usage = await _db.UserUsageDaily
                .FirstOrDefaultAsync(u => u.UserId == userId && u.UsageDate == today, ct);

            if (usage == null)
            {
                usage = new UserUsageDaily
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    UsageDate = today,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.UserUsageDaily.Add(usage);
            }

            usage.AgentCallCount++;

            if (toolName == "search_memory")
                usage.AgentSearchCount++;
            else if (toolName == "ask_memory")
                usage.AgentQaCount++;
            else if (toolName == "create_inbox_item" || toolName == "import_url")
                usage.AgentWriteCount++;

            if (success)
                usage.AgentSuccessCount++;
            else
                usage.AgentFailedCount++;

            usage.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record agent usage {ToolName} for user {UserId}", toolName, userId);
        }
    }

    // ===== Private Helpers =====

    private static UsageDailyItem ToUsageDailyItem(UserUsageDaily u)
    {
        return new UsageDailyItem
        {
            Date = u.UsageDate,
            ImportedCount = u.ImportedCount,
            DocumentCount = u.DocumentCount,
            SearchCount = u.SearchCount,
            QaCount = u.QaCount,
            ReportCount = u.ReportCount,
            ExportCount = u.ExportCount,
            ApiCallCount = u.ApiCallCount,
            AgentCallCount = u.AgentCallCount,
            AgentSearchCount = u.AgentSearchCount,
            AgentQaCount = u.AgentQaCount,
            AgentWriteCount = u.AgentWriteCount,
            AgentSuccessCount = u.AgentSuccessCount,
            AgentFailedCount = u.AgentFailedCount,
            InputTokens = u.InputTokens,
            OutputTokens = u.OutputTokens,
            EmbeddingTokens = u.EmbeddingTokens,
            StorageBytes = u.StorageBytes
        };
    }
}
