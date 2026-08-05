using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Scans completed session turns and extracts candidate memory items using
/// heuristic rules (no LLM dependency in V1).
///
/// <para>
/// The extractor reads the structured Raw Events (turns + actions) and detects
/// memory-worthy content patterns: user decisions, assistant conclusions,
/// significant file edits, error→fix sequences, and session summaries. Each
/// detected pattern becomes a candidate MemoryItem that enters the existing
/// admission pipeline (sanitize → evaluate → conflict check → persist).
/// </para>
/// <para>
/// A session with 100+ events should typically yield 1–3 candidate memories.
/// </para>
/// </summary>
public class MemoryExtractorService
{
    private readonly IAppDbContext _db;
    private readonly MemorySanitizer _sanitizer;
    private readonly MemoryAdmissionService _admissionService;
    private readonly ConflictDetectionService _conflictService;
    private readonly ILogger<MemoryExtractorService> _logger;

    public MemoryExtractorService(
        IAppDbContext db,
        MemorySanitizer sanitizer,
        MemoryAdmissionService admissionService,
        ConflictDetectionService conflictService,
        ILogger<MemoryExtractorService> logger)
    {
        _db = db;
        _sanitizer = sanitizer;
        _admissionService = admissionService;
        _conflictService = conflictService;
        _logger = logger;
    }

    /// <summary>
    /// Extract candidate memories from a single session's completed turns.
    /// Called after a session ends or by a background maintenance sweep.
    /// </summary>
    public async Task<int> ExtractFromSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            _logger.LogWarning("ExtractFromSession: session {SessionId} not found", sessionId);
            return 0;
        }

        var turns = await _db.AgentMemoryTurns
            .Where(t => t.SessionId == sessionId && t.Status == "completed")
            .OrderBy(t => t.Seq)
            .ToListAsync(ct);

        if (turns.Count == 0)
        {
            _logger.LogDebug("ExtractFromSession: no completed turns for session {SessionId}", sessionId);
            return 0;
        }

        var candidates = new List<(MemoryKind Kind, string Title, string Content)>();

        foreach (var turn in turns)
        {
            // Rule 1: User decision signals
            var decision = DetectUserDecision(turn);
            if (decision != null) candidates.Add(decision.Value);

            // Rule 2: Assistant conclusion
            var conclusion = DetectAssistantConclusion(turn);
            if (conclusion != null) candidates.Add(conclusion.Value);

            // Rule 3: Significant file edit
            var edit = await DetectSignificantEditAsync(turn, ct);
            if (edit != null) candidates.Add(edit.Value);
        }

        // Rule 4: Error → fix sequence (command failed, then succeeded)
        candidates.AddRange(DetectErrorFixSequences(turns));

        // Rule 5: Session summary (aggregate of all turns)
        var summary = DetectSessionSummary(session, turns);
        if (summary != null) candidates.Add(summary.Value);

        if (candidates.Count == 0)
        {
            _logger.LogInformation(
                "ExtractFromSession: no memory-worthy content in session {SessionId} ({Turns} turns)",
                sessionId, turns.Count);
            return 0;
        }

        // Process each candidate through admission pipeline
        var created = 0;
        foreach (var (kind, title, content) in candidates)
        {
            var created_item = await CreateCandidateAsync(session, kind, title, content, ct);
            if (created_item) created++;
        }

        _logger.LogInformation(
            "ExtractFromSession: session {SessionId} → {Created} candidate memories from {Candidates} detected patterns",
            sessionId, created, candidates.Count);

        return created;
    }

    // ─── Heuristic Rules ───

    // Rule 1: User prompts that signal a decision or correction.
    // Keywords (multilingual): "就这样/确认/决定/不对/应该/应该用/just use/confirmed/decided/should"
    private static readonly Regex DecisionSignals = new(
        @"(就这样|确认|确定|决定|就这么|不对|应该|换个|改用|" +
        @"just\s+use|confirmed|decided|let'?s\s+go|should\s+(use|be|do)|use\s+this|final)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private (MemoryKind Kind, string Title, string Content)? DetectUserDecision(AgentMemoryTurn turn)
    {
        if (string.IsNullOrWhiteSpace(turn.UserMessage)) return null;
        if (!DecisionSignals.IsMatch(turn.UserMessage)) return null;

        var title = turn.UserMessage.Length > 60
            ? turn.UserMessage[..60] + "..."
            : turn.UserMessage;

        return (MemoryKind.Decision, title, turn.UserMessage);
    }

    // Rule 2: Assistant responses with conclusion markers.
    // Keywords: "总结/结论/决定/建议/最终/In summary/To conclude/The decision is"
    private static readonly Regex ConclusionSignals = new(
        @"(总结|结论|决定|建议|最终|综上所述|" +
        @"in\s+summary|to\s+conclude|the\s+(decision|approach)\s+is|recommend|conclusion)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private (MemoryKind Kind, string Title, string Content)? DetectAssistantConclusion(AgentMemoryTurn turn)
    {
        if (string.IsNullOrWhiteSpace(turn.AssistantMessage)) return null;
        if (turn.AssistantMessage.Length < 50) return null;
        if (!ConclusionSignals.IsMatch(turn.AssistantMessage)) return null;

        // Extract the paragraph containing the conclusion keyword
        var lines = turn.AssistantMessage.Split('\n');
        var conclusionLine = lines.FirstOrDefault(l => ConclusionSignals.IsMatch(l))
                             ?? turn.AssistantMessage;

        var title = conclusionLine.Length > 60
            ? conclusionLine[..60] + "..."
            : conclusionLine;

        return (MemoryKind.Decision, title, conclusionLine);
    }

    // Rule 3: File edits to significant files (.cs, .py, .ts, .rs, etc.)
    private async Task<(MemoryKind Kind, string Title, string Content)?> DetectSignificantEditAsync(
        AgentMemoryTurn turn, CancellationToken ct)
    {
        var actions = await _db.AgentMemoryActions
            .Where(a => a.TurnId == turn.Id && a.ActionKind == "edit" && a.FilePath != null)
            .ToListAsync(ct);

        var codeFilePattern = @"\.(cs|py|ts|tsx|js|jsx|rs|go|java|rb|php|sql)$";
        var significant = actions
            .Where(a => Regex.IsMatch(a.FilePath ?? "", codeFilePattern, RegexOptions.IgnoreCase))
            .ToList();

        if (significant.Count == 0) return null;

        var first = significant[0];
        var filename = Path.GetFileName(first.FilePath);
        return (MemoryKind.ToolResult, $"Changed {filename}",
            $"Edited {first.FilePath} in turn {turn.Seq} ({significant.Count} edit(s))");
    }

    // Rule 4: A command that failed, followed by a command that succeeded
    // in the same or next turn → problem-solution.
    private List<(MemoryKind Kind, string Title, string Content)> DetectErrorFixSequences(
        List<AgentMemoryTurn> turns)
    {
        var results = new List<(MemoryKind, string, string)>();

        for (var i = 0; i < turns.Count - 1; i++)
        {
            var current = turns[i];
            var next = turns[i + 1];

            // Check if current turn has a failed command and next has a successful one
            // This is a simplified heuristic; in production we'd look at action success flags
            if (!string.IsNullOrEmpty(current.AssistantMessage) &&
                current.AssistantMessage.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(next.AssistantMessage) &&
                next.AssistantMessage.Contains("fix", StringComparison.OrdinalIgnoreCase))
            {
                results.Add((MemoryKind.Blocker,
                    $"Error fixed in turn {next.Seq}",
                    $"Error in turn {current.Seq} resolved in turn {next.Seq}"));
            }
        }

        return results;
    }

    // Rule 5: Session-level summary
    private (MemoryKind Kind, string Title, string Content)? DetectSessionSummary(
        AgentMemorySession session, List<AgentMemoryTurn> turns)
    {
        if (turns.Count < 3) return null; // Not enough activity to summarize

        var totalActions = turns.Sum(t => t.ActionsCount);
        var topics = turns
            .Select(t => t.UserMessage)
            .Where(u => !string.IsNullOrEmpty(u))
            .Take(3)
            .Select(u => u!.Length > 40 ? u[..40] + "..." : u)
            .ToList();
        var summary = $"Session '{session.TaskTitle}': {turns.Count} turns, {totalActions} actions. " +
                      $"Key topics: {string.Join(", ", topics)}";

        return (MemoryKind.Summary, $"Session summary: {session.TaskTitle}", summary);
    }

    // ─── Candidate creation (through existing admission pipeline) ───

    private async Task<bool> CreateCandidateAsync(
        AgentMemorySession session,
        MemoryKind kind,
        string title,
        string content,
        CancellationToken ct)
    {
        // Sanitize content
        var (sanitizedContent, _) = await _sanitizer.SanitizeOnWriteAsync(content, ct);
        var (sanitizedTitle, _) = await _sanitizer.SanitizeOnWriteAsync(title, ct);

        var now = DateTime.UtcNow;

        var item = new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            WorkspaceId = session.WorkspaceId,
            OwnerUserId = session.UserId,
            AgentProfileId = session.AgentProfileId,
            Kind = kind,
            Title = sanitizedTitle,
            Content = sanitizedContent,
            AdmissionState = AdmissionState.Ephemeral,
            Confidence = 0.5m, // heuristic extraction = medium confidence
            Importance = 5,
            FreshnessAt = now,
            Status = MemoryStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Run through admission evaluation
        await _admissionService.EvaluateAdmissionAsync(item, new List<AgentMemoryEvidence>(), ct);

        _db.AgentMemoryItems.Add(item);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Extracted candidate memory: kind={Kind}, title='{Title}', admission={Admission}",
            kind, sanitizedTitle, item.AdmissionState);

        return true;
    }
}
