namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Benchmark evaluation result for a registered model.
/// Each record captures a single benchmark run against a specific dataset,
/// recording accuracy, speed, cost, and resource metrics.
/// </summary>
public class BenchmarkResult
{
    public Guid Id { get; set; }

    /// <summary>Reference to the evaluated <see cref="ModelRegistry"/> entry.</summary>
    public Guid ModelRegistryId { get; set; }

    /// <summary>Name of the benchmark suite that produced this result (e.g. "aishell-1", "commonvoice-zh").</summary>
    public string BenchmarkName { get; set; } = string.Empty;

    /// <summary>Character Error Rate (lower is better).</summary>
    public decimal Cer { get; set; }

    /// <summary>Word Error Rate (lower is better).</summary>
    public decimal Wer { get; set; }

    /// <summary>Real-Time Factor: processing_time / audio_duration (lower is better).</summary>
    public decimal Rtf { get; set; }

    /// <summary>Peak GPU memory usage in MB, or null if not applicable.</summary>
    public int? GpuMemoryMb { get; set; }

    /// <summary>Peak CPU memory usage in MB, or null if not measured.</summary>
    public int? CpuMemoryMb { get; set; }

    /// <summary>Time to First Byte in milliseconds (lower is better).</summary>
    public long Ttfb { get; set; }

    /// <summary>Throughput in segments per second (higher is better).</summary>
    public decimal Throughput { get; set; }

    /// <summary>Proper noun accuracy (0-1), or null if not measured.</summary>
    public decimal? ProperNounAccuracy { get; set; }

    /// <summary>Timestamp deviation in milliseconds (lower is better), or null if not measured.</summary>
    public decimal? TimestampDeviationMs { get; set; }

    /// <summary>Speaker diarization accuracy (0-1), or null if not measured.</summary>
    public decimal? SpeakerAccuracy { get; set; }

    /// <summary>User modification rate (0-1): fraction of segments edited by users (lower is better).</summary>
    public decimal? UserModificationRate { get; set; }

    /// <summary>Cost per unit (matches the model's pricing unit).</summary>
    public decimal UnitCost { get; set; }

    public DateTime EvaluatedAt { get; set; }

    /// <summary>Name of the evaluation dataset, or null if not specified.</summary>
    public string? DatasetName { get; set; }

    /// <summary>Free-form notes about the benchmark run.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Ranking category constants for benchmark leaderboards.
/// Used by <c>IBenchmarkService.GetRankingsAsync</c> to select the sort metric.
/// </summary>
public static class BenchmarkRankings
{
    /// <summary>Rank by throughput descending (fastest model first).</summary>
    public const string Fastest = "fastest";

    /// <summary>Rank by CER ascending (most accurate model first).</summary>
    public const string MostAccurate = "most_accurate";

    /// <summary>Rank by unit cost ascending (cheapest model first).</summary>
    public const string LowestCost = "lowest_cost";

    /// <summary>Rank by CER ascending on Chinese datasets.</summary>
    public const string BestChinese = "best_chinese";

    /// <summary>Rank by TTFB ascending (best for mobile/realtime first).</summary>
    public const string BestMobile = "best_mobile";

    /// <summary>Rank by speaker accuracy descending (best for meeting scenarios first).</summary>
    public const string BestMeeting = "best_meeting";
}
