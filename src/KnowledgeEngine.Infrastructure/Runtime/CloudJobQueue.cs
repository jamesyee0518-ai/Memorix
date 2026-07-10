using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Cloud job queue stub.
/// Phase 1: not yet implemented, throws NotSupportedException.
/// Will use Redis in future phases.
/// </summary>
public class CloudJobQueue : ICloudJobQueue
{
    private readonly ILogger<CloudJobQueue> _logger;
    private string? _redisConnectionString;

    public CloudJobQueue(ILogger<CloudJobQueue> logger)
    {
        _logger = logger;
    }

    public void Configure(string redisConnectionString)
    {
        _redisConnectionString = redisConnectionString;
        _logger.LogInformation("CloudJobQueue configured with Redis connection");
    }

    public Task<JobDto> EnqueueAsync(CreateJobInput input, CancellationToken ct = default)
        => throw new NotSupportedException("CloudJobQueue is not yet implemented. Use LocalJobQueue for now.");

    public Task<JobDto?> GetNextAsync(string[]? jobTypes = null, CancellationToken ct = default)
        => throw new NotSupportedException("CloudJobQueue is not yet implemented.");

    public Task MarkRunningAsync(string jobId, CancellationToken ct = default)
        => throw new NotSupportedException("CloudJobQueue is not yet implemented.");

    public Task MarkDoneAsync(string jobId, CancellationToken ct = default)
        => throw new NotSupportedException("CloudJobQueue is not yet implemented.");

    public Task MarkFailedAsync(string jobId, string error, CancellationToken ct = default)
        => throw new NotSupportedException("CloudJobQueue is not yet implemented.");

    public Task<int> GetPendingCountAsync(CancellationToken ct = default)
        => throw new NotSupportedException("CloudJobQueue is not yet implemented.");
}
