using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IAiBillingService
{
    Task EnsureDefaultsAsync(CancellationToken ct = default);
    Task<BillingEntitlementsResponse> GetEntitlementsAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default);
    Task<BillingSummaryResponse> GetSummaryAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default);
    Task<AiJobEstimateResponse> EstimateAsync(
        Guid userId,
        EstimateAiJobRequest request,
        CancellationToken ct = default);
    Task<AiBillingJobResponse> CreateJobAsync(
        Guid userId,
        CreateAiBillingJobRequest request,
        CancellationToken ct = default);
    Task<AiBillingJobResponse?> GetJobAsync(
        Guid userId,
        Guid workspaceId,
        Guid jobId,
        CancellationToken ct = default);
    Task<UsageEventResponse> RecordUsageAsync(
        RecordUsageEventRequest request,
        CancellationToken ct = default);
    Task<AiAttemptResponse> StartAttemptAsync(
        StartAiAttemptRequest request,
        CancellationToken ct = default);
    Task<AiAttemptResponse> CompleteAttemptAsync(
        Guid attemptId,
        CompleteAiAttemptRequest request,
        CancellationToken ct = default);
    Task<AiBillingJobResponse> CompleteJobAsync(
        Guid jobId,
        CompleteAiJobRequest request,
        CancellationToken ct = default);
}
