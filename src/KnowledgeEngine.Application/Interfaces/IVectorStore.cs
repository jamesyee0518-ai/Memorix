namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Abstraction over a vector index backend (Phase 4).
/// Local mode: <see cref="KnowledgeEngine.Infrastructure.Search.LocalVectorStore"/> (in-memory cosine over chunk_embeddings).
/// Cloud mode: <see cref="KnowledgeEngine.Infrastructure.Search.PgVectorStore"/> (pgvector &lt;=&gt; operator).
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Upsert an embedding for a chunk in the given workspace.
    /// </summary>
    Task UpsertAsync(string workspaceId, string chunkId, float[] embedding, Dictionary<string, string> metadata, CancellationToken ct = default);

    /// <summary>
    /// Delete the embedding for a chunk in the given workspace.
    /// </summary>
    Task DeleteAsync(string workspaceId, string chunkId, CancellationToken ct = default);

    /// <summary>
    /// Search the workspace vector index for the top-K most similar chunks.
    /// </summary>
    Task<List<VectorSearchResult>> SearchAsync(string workspaceId, float[] queryVector, int topK = 10, CancellationToken ct = default);

    /// <summary>
    /// Rebuild the entire workspace vector index (marks all as stale and re-processes).
    /// </summary>
    Task RebuildAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Return index statistics for the workspace.
    /// </summary>
    Task<VectorIndexStats> GetStatsAsync(string workspaceId, CancellationToken ct = default);
}

/// <summary>
/// A single vector search hit.
/// </summary>
public class VectorSearchResult
{
    public string ChunkId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? DocumentTitle { get; set; }
    public string? HeadingPath { get; set; }
    public double Score { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Vector index statistics for a workspace.
/// </summary>
public class VectorIndexStats
{
    public int TotalChunks { get; set; }
    public int IndexedChunks { get; set; }
    public int FailedChunks { get; set; }
    public int StaleChunks { get; set; }
    public string Status { get; set; } = "idle";
}
