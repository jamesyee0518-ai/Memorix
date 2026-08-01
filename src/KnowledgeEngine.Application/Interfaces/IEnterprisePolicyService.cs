using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Service for managing enterprise-level policies governing provider usage,
/// cost limits, quotas, audit, and data classification.
/// </summary>
public interface IEnterprisePolicyService
{
    /// <summary>
    /// Lists all policies, optionally filtered by workspace.
    /// Global policies (null WorkspaceId) are always included.
    /// </summary>
    Task<List<EnterprisePolicy>> ListPoliciesAsync(Guid? workspaceId, CancellationToken ct);

    /// <summary>
    /// Creates a new enterprise policy.
    /// </summary>
    Task<EnterprisePolicy> CreatePolicyAsync(EnterprisePolicy policy, CancellationToken ct);

    /// <summary>
    /// Deletes a policy by ID. Returns true if the policy was found and deleted.
    /// </summary>
    Task<bool> DeletePolicyAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Validates whether a provider is allowed for usage under the applicable
    /// enterprise policies for the given workspace. Returns true if allowed.
    /// </summary>
    Task<bool> ValidateProviderUsageAsync(Guid? workspaceId, string providerId, CancellationToken ct);
}
