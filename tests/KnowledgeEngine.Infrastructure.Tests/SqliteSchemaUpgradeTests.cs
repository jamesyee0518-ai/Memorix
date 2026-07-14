using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class SqliteSchemaUpgradeTests
{
    [Fact]
    public async Task EnsureMultilingualSetupAsync_UpgradesLegacyDesktopSchema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
            CREATE TABLE documents (Id TEXT PRIMARY KEY, Title TEXT NOT NULL, Language TEXT);
            CREATE TABLE document_chunks (Id TEXT PRIMARY KEY, Content TEXT NOT NULL);
            CREATE TABLE chunk_embeddings (Id TEXT PRIMARY KEY);
            INSERT INTO documents (Id, Title, Language) VALUES ('doc-1', 'Legacy document', 'en');
            INSERT INTO document_chunks (Id, Content) VALUES ('chunk-1', 'Legacy content');
            """);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);

        await db.EnsureMultilingualSetupAsync();

        Assert.True(await HasColumnAsync(connection, "documents", "title_original"));
        Assert.True(await HasColumnAsync(connection, "document_chunks", "bounding_box"));
        Assert.True(await HasColumnAsync(connection, "chunk_embeddings", "embedding_type"));
        Assert.True(await HasTableAsync(connection, "multilingual_batch_jobs"));
        Assert.Equal("Legacy document", await ScalarAsync(connection, "SELECT title_original FROM documents WHERE Id = 'doc-1'"));
        Assert.Equal("Legacy content", await ScalarAsync(connection, "SELECT content_original FROM document_chunks WHERE Id = 'chunk-1'"));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
