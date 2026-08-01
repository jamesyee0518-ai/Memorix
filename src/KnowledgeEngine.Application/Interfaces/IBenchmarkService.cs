using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// A single entry in a benchmark ranking leaderboard.
/// </summary>
public class RankingEntry
{
    public Guid ModelRegistryId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The metric value used for ranking (e.g. throughput, CER, unit cost).</summary>
    public decimal Score { get; set; }

    /// <summary>Name of the metric used for ranking (e.g. "throughput", "cer", "unit_cost").</summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary>1-based rank position.</summary>
    public int Rank { get; set; }
}

/// <summary>
/// Benchmark evaluation service for running, storing, and ranking model benchmarks.
/// </summary>
public interface IBenchmarkService
{
    /// <summary>
    /// Runs a benchmark on the specified model: performs a health check on the model's
    /// provider, records throughput / RTF / TTFB metrics, and stores the result.
    /// </summary>
    /// <param name="modelRegistryId">The registered model to benchmark.</param>
    /// <param name="datasetName">Name of the evaluation dataset (stored in the result).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<BenchmarkResult> RunBenchmarkAsync(Guid modelRegistryId, string datasetName, CancellationToken ct);

    /// <summary>
    /// Retrieves benchmark results with optional filters.
    /// </summary>
    /// <param name="modelRegistryId">Filter by model. Null returns all models.</param>
    /// <param name="benchmarkName">Filter by benchmark name. Null returns all.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<BenchmarkResult>> GetResultsAsync(Guid? modelRegistryId, string? benchmarkName, CancellationToken ct);

    /// <summary>
    /// Produces a ranked leaderboard of models for the given category.
    /// Categories: fastest, most_accurate, lowest_cost, best_chinese, best_mobile, best_meeting.
    /// </summary>
    /// <param name="category">Ranking category from <see cref="BenchmarkRankings"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<RankingEntry>> GetRankingsAsync(string category, CancellationToken ct);
}
