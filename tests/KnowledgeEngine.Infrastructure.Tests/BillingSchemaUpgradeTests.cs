using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class BillingSchemaUpgradeTests
{
    [Fact]
    public async Task EnsureBillingSetupAsync_UpgradesLegacyAiJobsAndCreatesBillingTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE ai_jobs (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "JobType" TEXT NOT NULL,
                    "TargetType" TEXT NOT NULL,
                    "TargetId" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);

        await db.EnsureBillingSetupAsync();
        await db.EnsureBillingSetupAsync();

        var aiJobColumns = await ReadColumnsAsync(connection, "ai_jobs");
        Assert.Contains("ClientJobId", aiJobColumns);
        Assert.Contains("ExecutionMode", aiJobColumns);
        Assert.Contains("BillingMode", aiJobColumns);
        Assert.Contains("EstimatedCredits", aiJobColumns);
        Assert.Contains("Currency", aiJobColumns);

        var billingTables = await ReadTablesAsync(connection);
        Assert.Contains("billing_accounts", billingTables);
        Assert.Contains("usage_events", billingTables);
        Assert.Contains("balance_reservations", billingTables);
        Assert.Contains("billing_charges", billingTables);
        Assert.Contains("provider_costs", billingTables);
        Assert.Contains("account_ledger", billingTables);
        Assert.Contains("recharge_products", billingTables);
        Assert.Contains("recharge_orders", billingTables);
        Assert.Contains("payment_attempts", billingTables);
        Assert.Contains("payment_notifications", billingTables);
        Assert.Contains("payment_refunds", billingTables);
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        SqliteConnection connection,
        string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(1));
        }
        return result;
    }

    private static async Task<HashSet<string>> ReadTablesAsync(SqliteConnection connection)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }
}
