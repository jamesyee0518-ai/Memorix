using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Composes a structured context pack from agent memory items for a session.
///
/// Layer allocation:
///   L1 (15% of token budget): Task state, open todos, blockers, risk hints
///   L2 (55% of token budget): Confirmed decisions, preferences, constraints, facts
///   L3 (30% of token budget): Evidence links, retrieval entries
/// </summary>
public class ContextComposer : IAgentContextService
{
    private readonly IAppDbContext _db;
    private readonly MemoryRetriever _retriever;
    private readonly ILogger<ContextComposer> _logger;

    // Token budget allocation percentages
    private const double L1BudgetRatio = 0.15;
    private const double L2BudgetRatio = 0.55;
    private const double L3BudgetRatio = 0.30;

    // Rough token estimation: ~4 characters per token
    private const int CharsPerToken = 4;

    // Memory kinds that belong to each layer
    private static readonly HashSet<MemoryKind> L1Kinds = new()
    {
        MemoryKind.TaskState,
        MemoryKind.Todo,
        MemoryKind.Blocker,
        MemoryKind.Handoff
    };

    private static readonly HashSet<MemoryKind> L2Kinds = new()
    {
        MemoryKind.Decision,
        MemoryKind.Preference,
        MemoryKind.Constraint,
        MemoryKind.Fact,
        MemoryKind.Rationale,
        MemoryKind.Lesson,
        MemoryKind.Summary
    };

    public ContextComposer(
        IAppDbContext db,
        MemoryRetriever retriever,
        ILogger<ContextComposer> logger)
    {
        _db = db;
        _retriever = retriever;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ContextPackDto> BuildContextPackAsync(
        Guid sessionId,
        int maxTokens,
        CancellationToken ct = default)
    {
        var session = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            _logger.LogWarning("ContextComposer: session {SessionId} not found", sessionId);
            return new ContextPackDto
            {
                SessionId = sessionId,
                TokenBudget = maxTokens,
                TokenUsed = 0
            };
        }

        // Load all active memory items for this session
        var items = await _db.AgentMemoryItems
            .Where(i => i.SessionId == sessionId && i.Status == MemoryStatus.Active)
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);

        // Load evidence for these items
        var itemIds = items.Select(i => i.Id).ToList();
        var evidences = await _db.AgentMemoryEvidences
            .Where(e => itemIds.Contains(e.MemoryItemId))
            .ToListAsync(ct);

        // Calculate token budgets per layer
        var l1Budget = (int)(maxTokens * L1BudgetRatio);
        var l2Budget = (int)(maxTokens * L2BudgetRatio);
        var l3Budget = (int)(maxTokens * L3BudgetRatio);

        // Build L1: Task state, open todos, blockers, risk hints
        var l1Items = items
            .Where(i => L1Kinds.Contains(i.Kind))
            .ToList();
        var l1Layers = BuildLayer(l1Items, evidences, l1Budget);

        // Build L2: Confirmed decisions, preferences, constraints, facts
        var l2Items = items
            .Where(i => L2Kinds.Contains(i.Kind) && i.AdmissionState == AdmissionState.Confirmed)
            .ToList();
        var l2Layers = BuildLayer(l2Items, evidences, l2Budget);

        // Build L3: Evidence links, retrieval entries
        var l3Layers = BuildEvidenceLayer(items, evidences, l3Budget);

        var tokenUsed = l1Layers.Sum(EstimateTokens) + l2Layers.Sum(EstimateTokens) + l3Layers.Sum(EstimateTokens);

        _logger.LogDebug(
            "Context pack built for session {SessionId}: L1={L1}, L2={L2}, L3={L3}, tokens={Used}/{Budget}",
            sessionId, l1Layers.Count, l2Layers.Count, l3Layers.Count, tokenUsed, maxTokens);

        return new ContextPackDto
        {
            SessionId = sessionId,
            TokenBudget = maxTokens,
            TokenUsed = tokenUsed,
            L1 = l1Layers,
            L2 = l2Layers,
            L3 = l3Layers
        };
    }

    /// <summary>
    /// Builds context layers from memory items, respecting the token budget.
    /// </summary>
    private List<ContextLayerDto> BuildLayer(
        List<AgentMemoryItem> items,
        List<AgentMemoryEvidence> evidences,
        int tokenBudget)
    {
        var result = new List<ContextLayerDto>();
        var usedTokens = 0;

        foreach (var item in items)
        {
            var layer = new ContextLayerDto
            {
                Type = item.Kind.ToString().ToLowerInvariant(),
                Title = item.Title,
                Content = item.Content ?? item.Summary ?? item.Title,
                Confidence = item.Confidence,
                AdmissionState = item.AdmissionState.ToString().ToLowerInvariant(),
                EvidenceRef = GetEvidenceRef(item.Id, evidences)
            };

            var tokens = EstimateTokens(layer);
            if (usedTokens + tokens > tokenBudget)
            {
                break;
            }

            result.Add(layer);
            usedTokens += tokens;
        }

        return result;
    }

    /// <summary>
    /// Builds the L3 evidence layer with evidence links and retrieval entries.
    /// </summary>
    private List<ContextLayerDto> BuildEvidenceLayer(
        List<AgentMemoryItem> items,
        List<AgentMemoryEvidence> evidences,
        int tokenBudget)
    {
        var result = new List<ContextLayerDto>();
        var usedTokens = 0;

        // Group evidence by memory item to provide concise references
        var evidenceByItem = evidences
            .GroupBy(e => e.MemoryItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in items.Where(i => i.AdmissionState != AdmissionState.Rejected))
        {
            if (!evidenceByItem.TryGetValue(item.Id, out var itemEvidence) || itemEvidence.Count == 0)
                continue;

            var evidenceSummary = string.Join("; ", itemEvidence.Select(e =>
                $"{e.EvidenceKind}:{e.ReferenceId}" +
                (!string.IsNullOrEmpty(e.Locator) ? $"@{e.Locator}" : "")));

            var layer = new ContextLayerDto
            {
                Type = "evidence",
                Title = item.Title,
                Content = evidenceSummary,
                Confidence = item.Confidence,
                AdmissionState = item.AdmissionState.ToString().ToLowerInvariant(),
                EvidenceRef = evidenceSummary
            };

            var tokens = EstimateTokens(layer);
            if (usedTokens + tokens > tokenBudget)
            {
                break;
            }

            result.Add(layer);
            usedTokens += tokens;
        }

        // If we still have budget, add retrieval entry points (pointers to items without evidence)
        foreach (var item in items.Where(i => i.AdmissionState == AdmissionState.Confirmed))
        {
            if (evidenceByItem.ContainsKey(item.Id))
                continue; // Already have evidence for this item

            var layer = new ContextLayerDto
            {
                Type = "retrieval_entry",
                Title = item.Title,
                Content = $"item:{item.Id}",
                Confidence = item.Confidence,
                AdmissionState = item.AdmissionState.ToString().ToLowerInvariant(),
                EvidenceRef = null
            };

            var tokens = EstimateTokens(layer);
            if (usedTokens + tokens > tokenBudget)
            {
                break;
            }

            result.Add(layer);
            usedTokens += tokens;
        }

        return result;
    }

    /// <summary>
    /// Gets a comma-separated evidence reference string for a memory item.
    /// </summary>
    private static string? GetEvidenceRef(Guid itemId, List<AgentMemoryEvidence> evidences)
    {
        var itemEvidence = evidences.Where(e => e.MemoryItemId == itemId).ToList();
        if (itemEvidence.Count == 0)
            return null;

        return string.Join(", ", itemEvidence.Select(e =>
            $"{e.EvidenceKind}:{e.ReferenceId}"));
    }

    /// <summary>
    /// Estimates the token count of a context layer.
    /// </summary>
    private static int EstimateTokens(ContextLayerDto layer)
    {
        var totalChars = (layer.Type?.Length ?? 0)
                       + (layer.Title?.Length ?? 0)
                       + (layer.Content?.Length ?? 0)
                       + (layer.EvidenceRef?.Length ?? 0);
        return Math.Max(1, totalChars / CharsPerToken);
    }
}
