using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Enterprise Policy Center API.
/// Manages enterprise-level policies for provider restrictions, cost limits,
/// quotas, audit requirements, and data classification.
/// </summary>
[ApiController]
[Route("api/enterprise/policies")]
[Authorize]
public class EnterprisePolicyController : BaseController
{
    private readonly IEnterprisePolicyService _policyService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<EnterprisePolicyController> _logger;

    public EnterprisePolicyController(
        IEnterprisePolicyService policyService,
        ICurrentUserContext currentUser,
        ILogger<EnterprisePolicyController> logger)
    {
        _policyService = policyService;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Lists all policies, optionally filtered by workspace.
    /// Global policies (no workspace) are always included.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListPolicies(
        [FromQuery] Guid? workspaceId, CancellationToken ct)
    {
        var policies = await _policyService.ListPoliciesAsync(workspaceId, ct);
        return Ok(ApiResponse<List<EnterprisePolicy>>.Ok(policies, GetTraceId()));
    }

    /// <summary>
    /// Creates a new enterprise policy.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] CreatePolicyRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.PolicyName))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_POLICY_NAME", "PolicyName is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.PolicyType))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_POLICY_TYPE", "PolicyType is required", GetTraceId()));
        }

        var policy = new EnterprisePolicy
        {
            WorkspaceId = request.WorkspaceId,
            PolicyName = request.PolicyName,
            PolicyType = request.PolicyType,
            RulesJson = request.RulesJson ?? "{}",
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
            CreatedBy = _currentUser.Email ?? _currentUser.UserId.Value.ToString()
        };

        var created = await _policyService.CreatePolicyAsync(policy, ct);
        return Ok(ApiResponse<EnterprisePolicy>.Ok(created, GetTraceId()));
    }

    /// <summary>
    /// Deletes a policy by ID.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePolicy(Guid id, CancellationToken ct)
    {
        var deleted = await _policyService.DeletePolicyAsync(id, ct);

        if (!deleted)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "POLICY_NOT_FOUND", $"Policy with ID '{id}' not found.", GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(new { id, deleted = true }, GetTraceId()));
    }

    /// <summary>
    /// Validates whether a provider is allowed for usage under the applicable policies.
    /// </summary>
    [HttpGet("validate/{providerId}")]
    public async Task<IActionResult> ValidateProviderUsage(
        string providerId, [FromQuery] Guid? workspaceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_PROVIDER_ID", "ProviderId is required", GetTraceId()));
        }

        var isAllowed = await _policyService.ValidateProviderUsageAsync(
            workspaceId, providerId, ct);

        return Ok(ApiResponse<object>.Ok(
            new { providerId, isAllowed, workspaceId }, GetTraceId()));
    }
}

// ===== Request DTOs =====

public class CreatePolicyRequest
{
    public Guid? WorkspaceId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public string PolicyType { get; set; } = string.Empty;
    public string? RulesJson { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
}
