namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Enterprise-level policy governing provider usage, cost limits, quotas,
/// audit requirements, and data classification across a workspace.
/// Rules are stored as JSON in <see cref="RulesJson"/> and interpreted
/// by the policy evaluation engine.
/// </summary>
public class EnterprisePolicy
{
    public Guid Id { get; set; }

    /// <summary>Optional workspace scope. Null = global policy applicable to all workspaces.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>Human-readable name for this policy.</summary>
    public string PolicyName { get; set; } = string.Empty;

    /// <summary>Policy type: provider_restriction / cost_limit / quota / audit / data_classification.</summary>
    public string PolicyType { get; set; } = EnterprisePolicyTypes.ProviderRestriction;

    /// <summary>JSON string containing the policy rules specific to <see cref="PolicyType"/>.</summary>
    public string RulesJson { get; set; } = "{}";

    /// <summary>Priority for conflict resolution. Higher number = higher priority.</summary>
    public int Priority { get; set; }

    /// <summary>Whether this policy is currently enforced.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>User or system that created this policy.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Static constants for <see cref="EnterprisePolicy.PolicyType"/> values.
/// </summary>
public static class EnterprisePolicyTypes
{
    public const string ProviderRestriction = "provider_restriction";
    public const string CostLimit = "cost_limit";
    public const string Quota = "quota";
    public const string Audit = "audit";
    public const string DataClassification = "data_classification";
}
