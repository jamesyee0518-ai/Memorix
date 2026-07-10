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
/// Local-mode <see cref="IVectorStore"/> implementation.
/// Stores embeddings as JSON text in the <c>chunk_embeddings</c> table and performs
/// cosine similarity search in-memory. Suitable for local-first workspaces where no
/// dedicated vector index extension is available.
/// </summary>
public class LocalVectorStore : IVectorStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<LocalVectorStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LocalVectorStore(AppDbContext db, ILogger<LocalVectorStore> logger)
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
        var metadataJson = metadata != null && metadata.Count > 0
            ? JsonSerializer.Serialize(metadata)
            : null;
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

        if (metadataJson != null)
        {
            _logger.LogDebug("LocalVectorStore upserted chunk {ChunkId} (dim={Dim})", chunkId, dimension);
        }
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
        // Load all indexed embeddings for the workspace (in-memory cosine).
        var sql = @"
            SELECT ce.chunk_id, ce.document_id, ce.embedding_json,
                   dc.content, dc.chunk_title, d.title
            FROM chunk_embeddings ce
            LEFT JOIN document_chunks dc ON dc.id::text = ce.chunk_id
            LEFT JOIN documents d ON d.id::text = ce.document_id
            WHERE ce.workspace_id = @workspaceId
              AND ce.status = 'indexed'
              AND ce.embedding_json IS NOT NULL";

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
            shouldClose = true;
        }

        var results = new List<(string ChunkId, string DocumentId, string Content, string? Title, string? HeadingPath, float[] Vector)>();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@workspaceId", NpgsqlDbType.Text) { Value = workspaceId });

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var embeddingJson = reader.IsDBNull(reader.GetOrdinal("embedding_json"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("embedding_json"));
                if (string.IsNullOrEmpty(embeddingJson)) continue;

                float[]? vec;
                try
                {
                    vec = JsonSerializer.Deserialize<float[]>(embeddingJson, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (vec == null || vec.Length == 0) continue;

                var chunkId = reader.GetString(reader.GetOrdinal("chunk_id"));
                var documentId = reader.IsDBNull(reader.GetOrdinal("document_id"))
                    ? string.Empty
                    : reader.GetString(reader.GetOrdinal("document_id"));
                var content = reader.IsDBNull(reader.GetOrdinal("content"))
                    ? string.Empty
                    : reader.GetString(reader.GetOrdinal("content"));
                var title = reader.IsDBNull(reader.GetOrdinal("title"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("title"));
                var heading = reader.IsDBNull(reader.GetOrdinal("chunk_title"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("chunk_title"));

                results.Add((chunkId, documentId, content, title, heading, vec));
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        // Compute cosine similarity in memory and take top-K.
        var ranked = results
            .Select(r => new VectorSearchResult
            {
                ChunkId = r.ChunkId,
                DocumentId = r.DocumentId,
                Content = r.Content,
                DocumentTitle = r.Title,
                HeadingPath = r.HeadingPath,
                Score = CosineSimilarity(queryVector, r.Vector),
                Metadata = new Dictionary<string, string>()
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return ranked;
    }

    public async Task RebuildAsync(string workspaceId, CancellationToken ct = default)
    {
        // Mark all embeddings for the workspace as stale so they get re-processed.
        var sql = "UPDATE chunk_embeddings SET status = 'stale', updated_at = @now WHERE workspace_id = @workspaceId";
        var parameters = new[]
        {
            new NpgsqlParameter("@now", NpgsqlDbType.Text) { Value = DateTime.UtcNow.ToString("o") },
            new NpgsqlParameter("@workspaceId", NpgsqlDbType.Text) { Value = workspaceId }
        };
        await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        _logger.LogInformation("LocalVectorStore marked all embeddings stale for workspace {WorkspaceId}", workspaceId);
    }

    public async Task<VectorIndexStats> GetStatsAsync(string workspaceId, CancellationToken ct = default)
    {
        var sql = @"
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE status = 'indexed') AS indexed,
                COUNT(*) FILTER (WHERE status = 'failed') AS failed,
                COUNT(*) FILTER (WHERE status = 'stale') AS stale
            FROM chunk_embeddings
            WHERE workspace_id = @workspaceId";

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

    /// <summary>
    /// Cosine similarity between two vectors. Returns 0 for zero-norm vectors.
    /// </summary>
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length == 0 || b.Length == 0) return 0;
        var len = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
