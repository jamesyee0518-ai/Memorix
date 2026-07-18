using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Local job queue using SQLite.
/// Phase 1: simple table-based polling.
/// </summary>
public class LocalJobQueue : IJobQueue
{
    private readonly string _connectionString;
    private readonly ILogger<LocalJobQueue> _logger;

    public LocalJobQueue(string dbPath, ILogger<LocalJobQueue> logger)
    {
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task<JobDto> EnqueueAsync(CreateJobInput input, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO jobs (id, workspace_id, job_type, target_type, target_id, status, retry_count, created_at)
            VALUES ($id, $ws, $jt, $tt, $ti, 'pending', 0, $now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$ws", input.WorkspaceId);
        cmd.Parameters.AddWithValue("$jt", input.JobType);
        cmd.Parameters.AddWithValue("$tt", input.TargetType);
        cmd.Parameters.AddWithValue("$ti", input.TargetId);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Enqueued job {JobId} type={JobType}", id, input.JobType);

        return new JobDto
        {
            Id = id,
            WorkspaceId = input.WorkspaceId,
            JobType = input.JobType,
            TargetType = input.TargetType,
            TargetId = input.TargetId,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<JobDto?> GetNextAsync(string[]? jobTypes = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, job_type, target_type, target_id, status, retry_count, created_at
            FROM jobs
            WHERE status = 'pending'";
        if (jobTypes != null && jobTypes.Length > 0)
        {
            var placeholders = string.Join(",", jobTypes.Select((_, i) => $"$jt{i}"));
            cmd.CommandText += $" AND job_type IN ({placeholders})";
            for (var i = 0; i < jobTypes.Length; i++)
                cmd.Parameters.AddWithValue($"$jt{i}", jobTypes[i]);
        }
        cmd.CommandText += " ORDER BY created_at ASC LIMIT 1";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new JobDto
        {
            Id = reader.GetString(0),
            WorkspaceId = reader.GetString(1),
            JobType = reader.GetString(2),
            TargetType = reader.GetString(3),
            TargetId = reader.GetString(4),
            Status = reader.GetString(5),
            RetryCount = reader.GetInt32(6),
            CreatedAt = DateTime.Parse(reader.GetString(7))
        };
    }

    public async Task MarkRunningAsync(string jobId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE jobs SET status = 'running', started_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", jobId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkDoneAsync(string jobId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE jobs SET status = 'done', finished_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", jobId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(string jobId, string error, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE jobs SET status = 'failed', finished_at = $now, error_message = $err, retry_count = retry_count + 1 WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", jobId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$err", error);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE status = 'pending'";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }
}
