using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Implementation of <see cref="IEnterprisePolicyService"/>.
/// Manages enterprise-level policies and validates provider usage
/// against provider_restriction policies.
/// </summary>
public class EnterprisePolicyService : IEnterprisePolicyService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<EnterprisePolicyService> _logger;

    public EnterprisePolicyService(
        IAppDbContext db,
        ILogger<EnterprisePolicyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<EnterprisePolicy>> ListPoliciesAsync(
        Guid? workspaceId, CancellationToken ct)
    {
        // Return workspace-specific policies plus global policies (null WorkspaceId)
        var query = _db.EnterprisePolicies
            .Where(p => p.WorkspaceId == null || p.WorkspaceId == workspaceId);

        return await query
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<EnterprisePolicy> CreatePolicyAsync(
        EnterprisePolicy policy, CancellationToken ct)
    {
        if (policy.Id == Guid.Empty)
        {
            policy.Id = Guid.NewGuid();
        }

        policy.CreatedAt = DateTime.UtcNow;
        policy.UpdatedAt = DateTime.UtcNow;

        _db.EnterprisePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created enterprise policy '{Name}' (ID: {Id}, Type: {Type})",
            policy.PolicyName, policy.Id, policy.PolicyType);

        return policy;
    }

    /// <inheritdoc />
    public async Task<bool> DeletePolicyAsync(Guid id, CancellationToken ct)
    {
        var policy = await _db.EnterprisePolicies
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (policy == null)
        {
            return false;
        }

        _db.EnterprisePolicies.Remove(policy);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted enterprise policy {Id}", id);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateProviderUsageAsync(
        Guid? workspaceId, string providerId, CancellationToken ct)
    {
        // Retrieve all applicable provider_restriction policies,
        // ordered by priority (highest first).
        var policies = await _db.EnterprisePolicies
            .Where(p => p.IsEnabled
                        && p.PolicyType == EnterprisePolicyTypes.ProviderRestriction
                        && (p.WorkspaceId == null || p.WorkspaceId == workspaceId))
            .OrderByDescending(p => p.Priority)
            .ToListAsync(ct);

        if (policies.Count == 0)
        {
            // No restrictions defined; provider is allowed
            return true;
        }

        foreach (var policy in policies)
        {
            var rules = ParseRules(policy.RulesJson);

            // Check "allowedProviders" list — if defined, provider must be in the list
            if (rules.TryGetValue("allowedProviders", out var allowedValue)
                && allowedValue is JsonElement allowedElement
                && allowedElement.ValueKind == JsonValueKind.Array)
            {
                var allowedProviders = allowedElement.EnumerateArray()
                    .Select(a => a.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (allowedProviders.Count > 0)
                {
                    var isAllowed = allowedProviders.Any(
                        p => string.Equals(p, providerId, StringComparison.OrdinalIgnoreCase));

                    if (!isAllowed)
                    {
                        _logger.LogWarning(
                            "Provider '{ProviderId}' is not in the allowed list of policy '{PolicyName}'",
                            providerId, policy.PolicyName);
                        return false;
                    }

                    // Provider explicitly allowed by this policy
                    return true;
                }
            }

            // Check "blockedProviders" list — if defined, provider must NOT be in the list
            if (rules.TryGetValue("blockedProviders", out var blockedValue)
                && blockedValue is JsonElement blockedElement
                && blockedElement.ValueKind == JsonValueKind.Array)
            {
                var blockedProviders = blockedElement.EnumerateArray()
                    .Select(b => b.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (blockedProviders.Count > 0)
                {
                    var isBlocked = blockedProviders.Any(
                        p => string.Equals(p, providerId, StringComparison.OrdinalIgnoreCase));

                    if (isBlocked)
                    {
                        _logger.LogWarning(
                            "Provider '{ProviderId}' is blocked by policy '{PolicyName}'",
                            providerId, policy.PolicyName);
                        return false;
                    }
                }
            }
        }

        // No explicit allow/block rules matched; default to allowed
        return true;
    }

    /// <summary>
    /// Parses the policy rules JSON string into a dictionary of properties.
    /// Returns an empty dictionary on parse failure.
    /// </summary>
    private static Dictionary<string, object?> ParseRules(string rulesJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            using var doc = JsonDocument.Parse(rulesJson);
            var result = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value;
            }
            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }
}
