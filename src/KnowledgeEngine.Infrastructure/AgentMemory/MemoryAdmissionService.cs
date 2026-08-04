using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Evaluates admission state transitions for memory items.
///
/// Lifecycle:
///   ephemeral -> candidate  (auto-promote on capture)
///   candidate -> qualified   (evidence.Count >= 1 and evidence is accessible)
///   qualified -> confirmed   (requires user confirmation via feedback)
/// </summary>
public class MemoryAdmissionService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<MemoryAdmissionService> _logger;

    public MemoryAdmissionService(IAppDbContext db, ILogger<MemoryAdmissionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates and applies the appropriate admission state transition for a memory item
    /// based on the provided evidence.
    ///
    /// Rules:
    /// - If current state is Ephemeral, promote to Candidate.
    /// - If current state is Candidate and evidence.Count >= 1 with accessible evidence, qualify to Qualified.
    /// - Qualified -> Confirmed requires user confirmation (handled externally via feedback).
    /// </summary>
    /// <param name="item">The memory item to evaluate.</param>
    /// <param name="evidence">The list of evidence associated with the item.</param>
    /// <returns>The resulting admission state after evaluation.</returns>
    public Task<AdmissionState> EvaluateAdmissionAsync(
        AgentMemoryItem item,
        List<AgentMemoryEvidence> evidence,
        CancellationToken ct = default)
    {
        var currentState = item.AdmissionState;

        // Step 1: Ephemeral -> Candidate (auto-promote)
        if (currentState == AdmissionState.Ephemeral)
        {
            item.PromoteToCandidate();
            currentState = AdmissionState.Candidate;
            _logger.LogDebug(
                "Memory item {ItemId} promoted from Ephemeral to Candidate",
                item.Id);
        }

        // Step 2: Candidate -> Qualified (requires at least 1 accessible evidence)
        if (currentState == AdmissionState.Candidate)
        {
            var hasAccessibleEvidence = evidence != null &&
                evidence.Any(e => IsEvidenceAccessible(e));

            if (hasAccessibleEvidence)
            {
                item.Qualify();
                currentState = AdmissionState.Qualified;
                _logger.LogDebug(
                    "Memory item {ItemId} qualified from Candidate to Qualified (evidence count: {Count})",
                    item.Id,
                    evidence?.Count ?? 0);
            }
            else
            {
                _logger.LogDebug(
                    "Memory item {ItemId} remains Candidate: no accessible evidence (evidence count: {Count})",
                    item.Id,
                    evidence?.Count ?? 0);
            }
        }

        // Step 3: Qualified -> Confirmed requires user confirmation
        // This is NOT done here; it requires explicit user feedback.
        // The caller (AgentMemoryService) should call ConfirmUserFeedbackAsync when
        // a confirmation feedback is received.

        return Task.FromResult(currentState);
    }

    /// <summary>
    /// Promotes a qualified memory item to confirmed state.
    /// This should be called when user confirmation feedback is received.
    /// </summary>
    public Task ConfirmMemoryAsync(AgentMemoryItem item, CancellationToken ct = default)
    {
        if (item.AdmissionState == AdmissionState.Qualified)
        {
            item.Confirm();
            _logger.LogInformation(
                "Memory item {ItemId} confirmed (Qualified -> Confirmed)",
                item.Id);
        }
        else
        {
            _logger.LogWarning(
                "Cannot confirm memory item {ItemId}: current state is {State}, expected Qualified",
                item.Id,
                item.AdmissionState);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Rejects a candidate or qualified memory item.
    /// </summary>
    public Task RejectMemoryAsync(AgentMemoryItem item, CancellationToken ct = default)
    {
        if (item.AdmissionState == AdmissionState.Candidate ||
            item.AdmissionState == AdmissionState.Qualified)
        {
            item.Reject();
            _logger.LogInformation(
                "Memory item {ItemId} rejected ({State} -> Rejected)",
                item.Id,
                item.AdmissionState);
        }
        else
        {
            _logger.LogWarning(
                "Cannot reject memory item {ItemId}: current state is {State}",
                item.Id,
                item.AdmissionState);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks whether a piece of evidence is accessible (i.e., has a valid reference).
    /// In this Phase 1 implementation, evidence is considered accessible if it has a non-empty ReferenceId.
    /// </summary>
    private static bool IsEvidenceAccessible(AgentMemoryEvidence evidence)
    {
        return !string.IsNullOrWhiteSpace(evidence.ReferenceId);
    }
}
