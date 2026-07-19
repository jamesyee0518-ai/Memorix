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

    [Fact]
    public async Task EnsureIdentityAndBindingSetupAsync_UpgradesLegacyWorkspaceSchema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
            CREATE TABLE workspaces (
                id TEXT PRIMARY KEY,
                sync_enabled INTEGER NOT NULL DEFAULT 0,
                inbox_enabled INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO workspaces (id, sync_enabled, inbox_enabled) VALUES ('ws-inbox', 1, 1);
            INSERT INTO workspaces (id, sync_enabled, inbox_enabled) VALUES ('ws-metadata', 1, 0);
            """);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);

        await db.EnsureIdentityAndBindingSetupAsync();

        Assert.True(await HasColumnAsync(connection, "workspaces", "sync_mode"));
        Assert.True(await HasTableAsync(connection, "local_installations"));
        Assert.True(await HasTableAsync(connection, "local_profiles"));
        Assert.True(await HasTableAsync(connection, "device_identities"));
        Assert.True(await HasTableAsync(connection, "cloud_account_bindings"));
        Assert.True(await HasTableAsync(connection, "workspace_bindings"));
        Assert.True(await HasTableAsync(connection, "sync_inbox_staging"));
        Assert.Equal("inbox_only", await ScalarAsync(
            connection, "SELECT sync_mode FROM workspaces WHERE id = 'ws-inbox'"));
        Assert.Equal("metadata", await ScalarAsync(
            connection, "SELECT sync_mode FROM workspaces WHERE id = 'ws-metadata'"));
    }

    [Fact]
    public async Task EnsureIdentityAndBindingSetupAsync_AddsMissingLegacySyncColumns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
            CREATE TABLE workspaces (id TEXT PRIMARY KEY);
            INSERT INTO workspaces (id) VALUES ('ws-legacy');
            """);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);

        await db.EnsureIdentityAndBindingSetupAsync();

        Assert.True(await HasColumnAsync(connection, "workspaces", "sync_enabled"));
        Assert.True(await HasColumnAsync(connection, "workspaces", "inbox_enabled"));
        Assert.True(await HasColumnAsync(connection, "workspaces", "sync_mode"));
        Assert.Equal("none", await ScalarAsync(
            connection, "SELECT sync_mode FROM workspaces WHERE id = 'ws-legacy'"));
    }

    [Fact]
    public async Task EnsureIdentityAndBindingSetupAsync_MigratesPascalCaseDesktopColumns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
            CREATE TABLE workspaces (
                Id TEXT PRIMARY KEY,
                SyncEnabled INTEGER NOT NULL DEFAULT 0,
                InboxEnabled INTEGER NOT NULL DEFAULT 0,
                UserId TEXT
            );
            INSERT INTO workspaces (Id, SyncEnabled, InboxEnabled, UserId)
            VALUES ('ws-legacy', 1, 1, '00000000-0000-0000-0000-000000000123');
            """);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);

        await db.EnsureIdentityAndBindingSetupAsync();

        Assert.Equal("1", await ScalarAsync(
            connection, "SELECT sync_enabled FROM workspaces WHERE Id = 'ws-legacy'"));
        Assert.Equal("1", await ScalarAsync(
            connection, "SELECT inbox_enabled FROM workspaces WHERE Id = 'ws-legacy'"));
        Assert.Equal("inbox_only", await ScalarAsync(
            connection, "SELECT sync_mode FROM workspaces WHERE Id = 'ws-legacy'"));
        Assert.Equal("00000000-0000-0000-0000-000000000123", await ScalarAsync(
            connection, "SELECT user_id FROM workspaces WHERE Id = 'ws-legacy'"));
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
