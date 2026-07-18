namespace KnowledgeEngine.Domain.Entities;

public class VectorIndexState
{
    public Guid Id { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int? Dimension { get; set; }
    public string IndexBackend { get; set; } = "sqlite_vec";
    public int TotalChunks { get; set; }
    public int IndexedChunks { get; set; }
    public int FailedChunks { get; set; }
    public int StaleChunks { get; set; }
    public string Status { get; set; } = "idle";  // idle/indexing/rebuilding/error
    public DateTime? LastRebuiltAt { get; set; }
    public string? SchemaVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
