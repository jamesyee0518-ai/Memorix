using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IBillingPortalService
{
    Task<BillingOverviewResponse> GetOverviewAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default);
    Task<BillingUsageResponse> GetUsageAsync(
        Guid userId,
        Guid workspaceId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);
    Task<BillingBillsResponse> GetBillsAsync(
        Guid userId,
        Guid workspaceId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);
    Task<BillingPricingResponse> GetPricingAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default);
}
