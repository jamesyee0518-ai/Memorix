using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Service for managing A/B tests between prompt registry versions.
/// Handles test lifecycle (create -> start -> complete) and variant assignment
/// based on the configured traffic split percentage.
/// </summary>
public interface IPromptABTestService
{
    /// <summary>
    /// Creates a new A/B test in "created" status.
    /// </summary>
    Task<PromptABTest> CreateTestAsync(PromptABTest test, CancellationToken ct);

    /// <summary>
    /// Starts an A/B test, transitioning it to "running" status and setting StartDate.
    /// </summary>
    Task<PromptABTest> StartTestAsync(Guid testId, CancellationToken ct);

    /// <summary>
    /// Assigns a variant (A or B) to the current request based on the traffic split.
    /// Returns the assigned variant ID and its corresponding prompt.
    /// </summary>
    Task<(Guid VariantId, PromptRegistry Prompt)> AssignVariantAsync(Guid testId, CancellationToken ct);

    /// <summary>
    /// Completes an A/B test, recording the winning variant and setting EndDate.
    /// </summary>
    Task CompleteTestAsync(Guid testId, Guid winnerVariantId, CancellationToken ct);

    /// <summary>
    /// Lists all currently running A/B tests.
    /// </summary>
    Task<List<PromptABTest>> ListActiveTestsAsync(CancellationToken ct);
}
