using System.Globalization;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace KnowledgeEngine.Infrastructure.Search;

/// <summary>
/// Cloud-mode <see cref="IVectorStore"/> implementation backed by PostgreSQL + pgvector.
///
/// Uses pgvector's native <c>&lt;=&gt;</c> cosine distance operator for ANN search
/// directly in SQL against the <c>document_chunks.embedding</c> column (the column
/// that <see cref="KnowledgeEngine.Infrastructure.Processing.EmbeddingWorker"/> writes to).
/// This avoids loading all embeddings into memory, unlike
/// <see cref="LocalVectorStore"/>.
/// </summary>
public class PgVectorStore : IVectorStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<PgVectorStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PgVectorStore(AppDbContext db, ILogger<PgVectorStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task UpsertAsync(
        string workspaceId,
        string chunkId,
        float[] embedding,
        Dictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        var embeddingJson = JsonSerializer.Serialize(embedding);
        var dimension = embedding.Length;
        var now = DateTime.UtcNow.ToString("o");

        var provider = metadata != null && metadata.TryGetValue("provider", out var p) ? p : "local";
        var model = metadata != null && metadata.TryGetValue("model", out var m) ? m : "unknown";
        var contentHash = metadata != null && metadata.TryGetValue("chunk_content_hash", out var h) ? h : "";
        var documentId = metadata != null && metadata.TryGetValue("document_id", out var d) ? d : "";

        // INSERT ... ON CONFLICT upsert against chunk_embeddings(chunk_id, provider, model, chunk_content_hash)
        var sql = @"
            INSERT INTO chunk_embeddings
                (id, workspace_id, document_id, chunk_id, provider, model, dimension,
                 embedding_json, chunk_content_hash, status, retry_count, created_at, updated_at)
            VALUES
                (@id, @workspaceId, @documentId, @chunkId, @provider, @model, @dimension,
                 @embeddingJson, @chunkContentHash, 'indexed', 0, @now, @now)
            ON CONFLICT (chunk_id, provider, model, chunk_content_hash)
            DO UPDATE SET
                embedding_json = EXCLUDED.embedding_json,
                dimension = EXCLUDED.dimension,
                status = 'indexed',
                updated_at = EXCLUDED.updated_at";

        var parameters = new[]
        {
            new NpgsqlParameter("@id", NpgsqlDbType.Text) { Value = $"{workspaceId}:{chunkId}:{provider}:{model}" },
            new NpgsqlParameter("@workspaceId", NpgsqlDbType.Text) { Value = workspaceId },
            new NpgsqlParameter("@documentId", NpgsqlDbType.Text) { Value = documentId },
            new NpgsqlParameter("@chunkId", NpgsqlDbType.Text) { Value = chunkId },
            new NpgsqlParameter("@provider", NpgsqlDbType.Text) { Value = provider },
            new NpgsqlParameter("@model", NpgsqlDbType.Text) { Value = model },
            new NpgsqlParameter("@dimension", NpgsqlDbType.Integer) { Value = dimension },
            new NpgsqlParameter("@embeddingJson", NpgsqlDbType.Text) { Value = embeddingJson },
            new NpgsqlParameter("@chunkContentHash", NpgsqlDbType.Text) { Value = contentHash },
            new NpgsqlParameter("@now", NpgsqlDbType.Text) { Value = now }
        };

        await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);

        _logger.LogDebug("PgVectorStore upserted chunk {ChunkId} (dim={Dim})", chunkId, dimension);
    }

    public async Task DeleteAsync(string workspaceId, string chunkId, CancellationToken ct = default)
    {
        var sql = "DELETE FROM chunk_embeddings WHERE workspace_id = @workspaceId AND chunk_id = @chunkId";
        var parameters = new[]
        {
            new NpgsqlParameter("@workspaceId", NpgsqlDbType.Text) { Value = workspaceId },
            new NpgsqlParameter("@chunkId", NpgsqlDbType.Text) { Value = chunkId }
        };
        await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
    }

    public async Task<List<VectorSearchResult>> SearchAsync(
        string workspaceId,
        float[] queryVector,
        int topK = 10,
        CancellationToken ct = default)
    {
        // Build the vector string format: "[0.1,0.2,...]"
        var vectorStr = "[" + string.Join(",", queryVector.Select(v => v.ToString("G8", CultureInfo.InvariantCulture))) + "]";

        // Use pgvector's native <=> cosine distance operator against document_chunks.embedding.
        // The embedding column is written by EmbeddingWorker and is a pgvector vector type.
        // We join with documents for the title.
        var sql = @"
            SELECT c.id::text,
                   c.document_id::text,
                   c.content,
                   c.heading_path,
                   d.title,
                   1 - (c.embedding <=> @queryVector::vector) AS score
            FROM document_chunks c
            LEFT JOIN documents d ON d.id = c.document_id
            WHERE c.user_id = @workspaceId::uuid
              AND c.embedding IS NOT NULL
              AND c.embedding_status = 'done'
            ORDER BY c.embedding <=> @queryVector::vector
            LIMIT @topK";

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
            shouldClose = true;
        }

        var results = new List<VectorSearchResult>();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@queryVector", NpgsqlDbType.Text) { Value = vectorStr });
            cmd.Parameters.Add(new NpgsqlParameter("@workspaceId", NpgsqlDbType.Text) { Value = workspaceId });
            cmd.Parameters.Add(new NpgsqlParameter("@topK", NpgsqlDbType.Integer) { Value = topK });

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new VectorSearchResult
                {
                    ChunkId = reader.GetString(0),
                    DocumentId = reader.GetString(1),
                    Content = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    HeadingPath = reader.IsDBNull(3) ? null : reader.GetString(3),
                    DocumentTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Score = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                    Metadata = new Dictionary<string, string>()
                });
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }

    public async Task RebuildAsync(string workspaceId, CancellationToken ct = default)
    {
        // Mark all embeddings as 'stale' in document_chunks table so they get re-processed.
        var sql = "UPDATE document_chunks SET embedding_status = 'stale', updated_at = @now WHERE user_id = @workspaceId::uuid";
        var parameters = new[]
        {
            new NpgsqlParameter("@now", NpgsqlDbType.Text) { Value = DateTime.UtcNow.ToString("o") },
            new NpgsqlParameter("@workspaceId", NpgsqlDbType.Text) { Value = workspaceId }
        };
        await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        _logger.LogInformation("PgVectorStore marked all embeddings stale for workspace {WorkspaceId}", workspaceId);
    }

    public async Task<VectorIndexStats> GetStatsAsync(string workspaceId, CancellationToken ct = default)
    {
        // Count from document_chunks grouped by embedding_status
        var sql = @"
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE embedding_status = 'done') AS indexed,
                COUNT(*) FILTER (WHERE embedding_status = 'failed') AS failed,
                COUNT(*) FILTER (WHERE embedding_status = 'stale') AS stale
            FROM document_chunks
            WHERE user_id = @workspaceId::uuid";

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
            shouldClose = true;
        }

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@workspaceId", NpgsqlDbType.Text) { Value = workspaceId });

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return new VectorIndexStats
                {
                    TotalChunks = reader.GetInt32(reader.GetOrdinal("total")),
                    IndexedChunks = reader.GetInt32(reader.GetOrdinal("indexed")),
                    FailedChunks = reader.GetInt32(reader.GetOrdinal("failed")),
                    StaleChunks = reader.GetInt32(reader.GetOrdinal("stale")),
                    Status = "idle"
                };
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return new VectorIndexStats();
    }
}
