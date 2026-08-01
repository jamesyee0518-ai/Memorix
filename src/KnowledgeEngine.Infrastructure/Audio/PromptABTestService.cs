using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Implementation of <see cref="IPromptABTestService"/>.
/// Manages A/B test lifecycle and probabilistic variant assignment
/// based on the configured <see cref="PromptABTest.TrafficSplitPercent"/>.
/// </summary>
public class PromptABTestService : IPromptABTestService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<PromptABTestService> _logger;
    private static readonly Random _random = new();

    public PromptABTestService(
        IAppDbContext db,
        ILogger<PromptABTestService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PromptABTest> CreateTestAsync(PromptABTest test, CancellationToken ct)
    {
        if (test.Id == Guid.Empty)
        {
            test.Id = Guid.NewGuid();
        }

        // Validate that both variant prompts exist
        var variantA = await _db.PromptRegistries
            .FirstOrDefaultAsync(p => p.Id == test.VariantAId, ct)
            ?? throw new InvalidOperationException(
                $"Variant A prompt with ID '{test.VariantAId}' not found.");

        var variantB = await _db.PromptRegistries
            .FirstOrDefaultAsync(p => p.Id == test.VariantBId, ct)
            ?? throw new InvalidOperationException(
                $"Variant B prompt with ID '{test.VariantBId}' not found.");

        if (variantA.PromptKey != variantB.PromptKey)
        {
            throw new InvalidOperationException(
                "Variant A and Variant B must belong to the same prompt key.");
        }

        if (test.TrafficSplitPercent < 0 || test.TrafficSplitPercent > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(test.TrafficSplitPercent),
                "TrafficSplitPercent must be between 0 and 100.");
        }

        test.PromptKey = variantA.PromptKey;
        test.Status = PromptABTestStatuses.Created;
        test.CreatedAt = DateTime.UtcNow;
        test.UpdatedAt = DateTime.UtcNow;

        _db.PromptABTests.Add(test);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created A/B test '{Name}' (ID: {Id}) for prompt key '{Key}'",
            test.Name, test.Id, test.PromptKey);

        return test;
    }

    /// <inheritdoc />
    public async Task<PromptABTest> StartTestAsync(Guid testId, CancellationToken ct)
    {
        var test = await _db.PromptABTests
            .FirstOrDefaultAsync(t => t.Id == testId, ct)
            ?? throw new InvalidOperationException($"A/B test with ID '{testId}' not found.");

        if (test.Status == PromptABTestStatuses.Running)
        {
            throw new InvalidOperationException("A/B test is already running.");
        }

        if (test.Status == PromptABTestStatuses.Completed)
        {
            throw new InvalidOperationException("Cannot start a completed A/B test.");
        }

        test.Status = PromptABTestStatuses.Running;
        test.StartDate = DateTime.UtcNow;
        test.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Started A/B test {TestId}", testId);

        return test;
    }

    /// <inheritdoc />
    public async Task<(Guid VariantId, PromptRegistry Prompt)> AssignVariantAsync(
        Guid testId, CancellationToken ct)
    {
        var test = await _db.PromptABTests
            .FirstOrDefaultAsync(t => t.Id == testId, ct)
            ?? throw new InvalidOperationException($"A/B test with ID '{testId}' not found.");

        if (test.Status != PromptABTestStatuses.Running)
        {
            throw new InvalidOperationException(
                $"A/B test '{testId}' is not running (current status: {test.Status}).");
        }

        // Probabilistic assignment based on TrafficSplitPercent
        // TrafficSplitPercent = percent of traffic that goes to Variant B
        Guid assignedVariantId;
        int roll;
        lock (_random)
        {
            roll = _random.Next(1, 101); // 1..100 inclusive
        }

        if (roll <= test.TrafficSplitPercent)
        {
            assignedVariantId = test.VariantBId;
        }
        else
        {
            assignedVariantId = test.VariantAId;
        }

        var prompt = await _db.PromptRegistries
            .FirstOrDefaultAsync(p => p.Id == assignedVariantId, ct)
            ?? throw new InvalidOperationException(
                $"Assigned variant prompt with ID '{assignedVariantId}' not found.");

        _logger.LogDebug(
            "Assigned variant {Variant} (roll={Roll}, split={Split}%) for test {TestId}",
            assignedVariantId == test.VariantAId ? "A" : "B",
            roll, test.TrafficSplitPercent, testId);

        return (assignedVariantId, prompt);
    }

    /// <inheritdoc />
    public async Task CompleteTestAsync(Guid testId, Guid winnerVariantId, CancellationToken ct)
    {
        var test = await _db.PromptABTests
            .FirstOrDefaultAsync(t => t.Id == testId, ct)
            ?? throw new InvalidOperationException($"A/B test with ID '{testId}' not found.");

        if (test.Status == PromptABTestStatuses.Completed)
        {
            throw new InvalidOperationException("A/B test is already completed.");
        }

        if (winnerVariantId != test.VariantAId && winnerVariantId != test.VariantBId)
        {
            throw new ArgumentException(
                "Winner variant ID must match either VariantAId or VariantBId.",
                nameof(winnerVariantId));
        }

        test.Status = PromptABTestStatuses.Completed;
        test.WinnerVariantId = winnerVariantId;
        test.EndDate = DateTime.UtcNow;
        test.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Completed A/B test {TestId} with winner variant {WinnerId}",
            testId, winnerVariantId);
    }

    /// <inheritdoc />
    public async Task<List<PromptABTest>> ListActiveTestsAsync(CancellationToken ct)
    {
        return await _db.PromptABTests
            .Where(t => t.Status == PromptABTestStatuses.Running)
            .OrderByDescending(t => t.StartDate)
            .ToListAsync(ct);
    }
}
