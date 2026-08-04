using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Core agent memory service that manages session lifecycle, memory capture,
/// search, and context retrieval.
///
/// Capture flow: sanitize -> admission evaluation -> persist -> audit log
/// </summary>
public class AgentMemoryService : IAgentMemoryService
{
    private readonly IAppDbContext _db;
    private readonly MemorySanitizer _sanitizer;
    private readonly MemoryAdmissionService _admissionService;
    private readonly IAgentPermissionGuard _permissionGuard;
    private readonly MemoryRetriever _retriever;
    private readonly IAgentContextService _contextService;
    private readonly ILogger<AgentMemoryService> _logger;

    public AgentMemoryService(
        IAppDbContext db,
        MemorySanitizer sanitizer,
        MemoryAdmissionService admissionService,
        IAgentPermissionGuard permissionGuard,
        MemoryRetriever retriever,
        IAgentContextService contextService,
        ILogger<AgentMemoryService> logger)
    {
        _db = db;
        _sanitizer = sanitizer;
        _admissionService = admissionService;
        _permissionGuard = permissionGuard;
        _retriever = retriever;
        _contextService = contextService;
        _logger = logger;
    }

    // ===== Session Management =====

    /// <inheritdoc/>
    public async Task<SessionDto> StartSessionAsync(
        Guid userId,
        Guid workspaceId,
        Guid? agentProfileId,
        string externalSessionKey,
        string taskTitle,
        Guid? topicId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var session = new AgentMemorySession
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            AgentProfileId = agentProfileId,
            ExternalSessionKey = externalSessionKey,
            TaskTitle = taskTitle,
            Status = "active",
            StartedAt = now,
            LastActiveAt = now,
            TopicId = topicId
        };

        _db.AgentMemorySessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Agent memory session started: {SessionId} for user {UserId} in workspace {WorkspaceId}",
            session.Id, userId, workspaceId);

        await RecordAccessLogAsync(null, session.Id, agentProfileId, "write", ct);

        return MapSessionToDto(session);
    }

    /// <inheritdoc/>
    public async Task<SessionDto?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            return null;
        }

        // Update last active time
        session.LastActiveAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await RecordAccessLogAsync(null, session.Id, session.AgentProfileId, "read", ct);

        return MapSessionToDto(session);
    }

    /// <inheritdoc/>
    public async Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            _logger.LogWarning("Cannot close session: session {SessionId} not found", sessionId);
            return;
        }

        session.Status = "closed";
        session.ClosedAt = DateTime.UtcNow;
        session.LastActiveAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Agent memory session closed: {SessionId}", sessionId);

        await RecordAccessLogAsync(null, session.Id, session.AgentProfileId, "write", ct);
    }

    /// <inheritdoc/>
    public async Task<List<SessionDto>> ListSessionsAsync(
        Guid userId,
        Guid workspaceId,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
    {
        var sessions = await _db.AgentMemorySessions
            .Where(s => s.UserId == userId && s.WorkspaceId == workspaceId)
            .OrderByDescending(s => s.LastActiveAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return sessions.Select(MapSessionToDto).ToList();
    }

    // ===== Memory Capture =====

    /// <inheritdoc/>
    public async Task<MemoryItemDto> CaptureMemoryAsync(
        Guid userId,
        Guid workspaceId,
        CaptureMemoryInput input,
        CancellationToken ct = default)
    {
        // Determine agent profile from session if not directly available
        Guid? agentProfileId = null;
        if (input.SessionId.HasValue)
        {
            var session = await _db.AgentMemorySessions
                .FirstOrDefaultAsync(s => s.Id == input.SessionId.Value, ct);
            if (session != null)
            {
                agentProfileId = session.AgentProfileId;
            }
        }

        // Step 1: Permission check
        var canWrite = await _permissionGuard.CanWriteMemoryAsync(userId, agentProfileId, workspaceId, ct);
        if (!canWrite)
        {
            _logger.LogWarning(
                "CaptureMemory denied: user {UserId} does not have write permission for workspace {WorkspaceId}",
                userId, workspaceId);
            throw new UnauthorizedAccessException("Agent does not have permission to write memory.");
        }

        // Step 2: Sanitize content
        var rawContent = input.Content ?? string.Empty;
        var (sanitizedContent, wasModified) = await _sanitizer.SanitizeOnWriteAsync(rawContent, ct);

        var sanitizedSummary = input.Summary;
        if (!string.IsNullOrWhiteSpace(sanitizedSummary))
        {
            var (sanitizedSummaryContent, _) = await _sanitizer.SanitizeOnWriteAsync(sanitizedSummary, ct);
            sanitizedSummary = sanitizedSummaryContent;
        }

        // Step 3: Create memory item
        var now = DateTime.UtcNow;
        var kind = ParseMemoryKind(input.Kind);
        var visibility = ParseVisibility(input.Visibility);

        var item = new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            SessionId = input.SessionId,
            WorkspaceId = workspaceId,
            OwnerUserId = userId,
            AgentProfileId = agentProfileId,
            Kind = kind,
            Title = input.Title,
            Content = sanitizedContent,
            Summary = sanitizedSummary,
            AdmissionState = AdmissionState.Ephemeral,
            Confidence = input.Confidence ?? 0,
            Visibility = visibility,
            Importance = input.Importance,
            FreshnessAt = now,
            Status = MemoryStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Step 4: Create evidence (with source record validation)
        var evidence = new List<AgentMemoryEvidence>();
        if (input.Evidence != null && input.Evidence.Count > 0)
        {
            foreach (var evInput in input.Evidence)
            {
                var evidenceKind = ParseEvidenceKind(evInput.EvidenceKind);

                // Validate source record exists for ToolInvocation and DocumentChunk evidence
                await ValidateEvidenceReferenceAsync(evidenceKind, evInput.ReferenceId, ct);

                evidence.Add(new AgentMemoryEvidence
                {
                    Id = Guid.NewGuid(),
                    MemoryItemId = item.Id,
                    EvidenceKind = evidenceKind,
                    ReferenceId = evInput.ReferenceId,
                    Locator = evInput.Locator,
                    Relation = evInput.Relation,
                    CapturedAt = now
                });
            }
        }

        // Step 5: Admission evaluation
        await _admissionService.EvaluateAdmissionAsync(item, evidence, ct);

        // Step 6: Persist
        _db.AgentMemoryItems.Add(item);
        if (evidence.Count > 0)
        {
            _db.AgentMemoryEvidences.AddRange(evidence);
        }

        await _db.SaveChangesAsync(ct);

        // Step 7: Audit log
        await RecordAccessLogAsync(item.Id, input.SessionId, agentProfileId, "write", ct);

        _logger.LogInformation(
            "Memory captured: item {ItemId}, kind={Kind}, admission={Admission}, sanitized={WasModified}",
            item.Id, item.Kind, item.AdmissionState, wasModified);

        // Step 8: Return DTO
        var dto = MapItemToDto(item);
        dto.Evidence = evidence.Select(e => new EvidenceDto
        {
            Id = e.Id,
            MemoryItemId = e.MemoryItemId,
            EvidenceKind = e.EvidenceKind.ToString(),
            ReferenceId = e.ReferenceId,
            Locator = e.Locator,
            Relation = e.Relation,
            CapturedAt = e.CapturedAt
        }).ToList();

        return dto;
    }

    /// <inheritdoc/>
    public async Task<MemoryItemDto?> GetMemoryItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.AgentMemoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);

        if (item == null)
        {
            return null;
        }

        // Sanitize on read
        var sanitizedContent = await _sanitizer.SanitizeOnReadAsync(item.Content ?? string.Empty, ct);

        var dto = MapItemToDto(item);
        dto.Content = sanitizedContent;

        // Load evidence
        var evidences = await _db.AgentMemoryEvidences
            .Where(e => e.MemoryItemId == itemId)
            .ToListAsync(ct);
        dto.Evidence = evidences.Select(e => new EvidenceDto
        {
            Id = e.Id,
            MemoryItemId = e.MemoryItemId,
            EvidenceKind = e.EvidenceKind.ToString(),
            ReferenceId = e.ReferenceId,
            Locator = e.Locator,
            Relation = e.Relation,
            CapturedAt = e.CapturedAt
        }).ToList();

        // Record read access
        await RecordAccessLogAsync(item.Id, item.SessionId, item.AgentProfileId, "read", ct);

        return dto;
    }

    // ===== Search =====

    /// <inheritdoc/>
    public async Task<List<MemoryItemDto>> SearchMemoryAsync(
        Guid userId,
        Guid workspaceId,
        SearchMemoryInput input,
        CancellationToken ct = default)
    {
        // Determine agent profile from session if available
        Guid? agentProfileId = null;
        if (input.SessionId.HasValue)
        {
            var session = await _db.AgentMemorySessions
                .FirstOrDefaultAsync(s => s.Id == input.SessionId.Value, ct);
            if (session != null)
            {
                agentProfileId = session.AgentProfileId;
            }
        }

        // Permission check
        var canRead = await _permissionGuard.CanReadMemoryAsync(userId, agentProfileId, workspaceId, ct);
        if (!canRead)
        {
            _logger.LogWarning(
                "SearchMemory denied: user {UserId} does not have read permission for workspace {WorkspaceId}",
                userId, workspaceId);
            return new List<MemoryItemDto>();
        }

        // Delegate to retriever
        var results = await _retriever.SearchAsync(userId, workspaceId, input, ct);

        // Sanitize content on read for each result
        foreach (var dto in results)
        {
            if (!string.IsNullOrEmpty(dto.Content))
            {
                dto.Content = await _sanitizer.SanitizeOnReadAsync(dto.Content, ct);
            }
        }

        // Record search access
        await RecordAccessLogAsync(null, input.SessionId, agentProfileId, "read", ct);

        return results;
    }

    // ===== Context =====

    /// <inheritdoc/>
    public async Task<ContextPackDto> GetContextAsync(
        Guid sessionId,
        int? maxTokens = null,
        CancellationToken ct = default)
    {
        var session = await _db.AgentMemorySessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            _logger.LogWarning("GetContext: session {SessionId} not found", sessionId);
            return new ContextPackDto
            {
                SessionId = sessionId,
                TokenBudget = maxTokens ?? 2000,
                TokenUsed = 0
            };
        }

        // Determine max tokens from agent profile if not specified
        var effectiveMaxTokens = maxTokens ?? 2000;
        if (!maxTokens.HasValue && session.AgentProfileId.HasValue)
        {
            var profile = await _db.AgentProfiles
                .FirstOrDefaultAsync(a => a.Id == session.AgentProfileId.Value, ct);
            if (profile != null && profile.MemoryMaxContextTokens > 0)
            {
                effectiveMaxTokens = profile.MemoryMaxContextTokens;
            }
        }

        // Delegate to context service (ContextComposer)
        var contextPack = await _contextService.BuildContextPackAsync(sessionId, effectiveMaxTokens, ct);

        // Record context delivery access
        await RecordAccessLogAsync(null, sessionId, session.AgentProfileId, "deliver", ct);

        return contextPack;
    }

    // ===== Evidence Retrieval (P2.INF-04) =====

    /// <summary>
    /// Retrieves all evidence for a memory item, validating source records where applicable.
    /// </summary>
    public async Task<List<EvidenceDto>> GetEvidenceAsync(Guid memoryItemId, CancellationToken ct = default)
    {
        var evidences = await _db.AgentMemoryEvidences
            .Where(e => e.MemoryItemId == memoryItemId)
            .OrderBy(e => e.CapturedAt)
            .ToListAsync(ct);

        if (evidences.Count == 0)
        {
            return new List<EvidenceDto>();
        }

        // Validate source records for each evidence
        var result = new List<EvidenceDto>();
        foreach (var ev in evidences)
        {
            var isValid = await ValidateEvidenceReferenceAsync(ev.EvidenceKind, ev.ReferenceId, ct);

            var dto = new EvidenceDto
            {
                Id = ev.Id,
                MemoryItemId = ev.MemoryItemId,
                EvidenceKind = ev.EvidenceKind.ToString(),
                ReferenceId = ev.ReferenceId,
                Locator = ev.Locator,
                Relation = ev.Relation,
                CapturedAt = ev.CapturedAt
            };

            if (!isValid)
            {
                // Append a note that the source record could not be validated
                dto.Relation = string.IsNullOrWhiteSpace(dto.Relation)
                    ? "[source_unverified]"
                    : dto.Relation + " [source_unverified]";
            }

            result.Add(dto);
        }

        return result;
    }

    // ===== Private helpers =====

    /// <summary>
    /// Validates that the referenced source record exists for ToolInvocation and DocumentChunk evidence.
    /// Logs a warning if the reference cannot be verified but does not throw.
    /// </summary>
    private async Task<bool> ValidateEvidenceReferenceAsync(EvidenceKind kind, string referenceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return false;
        }

        // Parse the reference ID as a Guid for DB lookups
        if (!Guid.TryParse(referenceId, out var refGuid))
        {
            // Non-GUID reference IDs (e.g., message IDs) cannot be validated against DB tables
            return true;
        }

        switch (kind)
        {
            case EvidenceKind.ToolInvocation:
                var invocationExists = await _db.AgentInvocationLogs
                    .AnyAsync(l => l.Id == refGuid, ct);
                if (!invocationExists)
                {
                    _logger.LogWarning(
                        "Evidence validation: ToolInvocation reference {ReferenceId} not found in AgentInvocationLogs",
                        referenceId);
                }
                return invocationExists;

            case EvidenceKind.DocumentChunk:
                var chunkExists = await _db.DocumentChunks
                    .AnyAsync(c => c.Id == refGuid, ct);
                if (!chunkExists)
                {
                    _logger.LogWarning(
                        "Evidence validation: DocumentChunk reference {ReferenceId} not found in DocumentChunks",
                        referenceId);
                }
                return chunkExists;

            default:
                // Other evidence kinds (UserInput, ManualConfirmation, etc.) do not require DB validation
                return true;
        }
    }

    /// <summary>
    /// Records an access log entry for audit purposes.
    /// </summary>
    private async Task RecordAccessLogAsync(
        Guid? memoryItemId,
        Guid? sessionId,
        Guid? agentProfileId,
        string action,
        CancellationToken ct = default)
    {
        var log = new AgentMemoryAccessLog
        {
            Id = Guid.NewGuid(),
            MemoryItemId = memoryItemId,
            SessionId = sessionId,
            AgentProfileId = agentProfileId,
            Action = action,
            TraceId = System.Diagnostics.Activity.Current?.Id,
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentMemoryAccessLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    private static SessionDto MapSessionToDto(AgentMemorySession session)
    {
        return new SessionDto
        {
            Id = session.Id,
            WorkspaceId = session.WorkspaceId,
            UserId = session.UserId,
            AgentProfileId = session.AgentProfileId,
            ExternalSessionKey = session.ExternalSessionKey,
            TaskTitle = session.TaskTitle,
            Status = session.Status,
            StartedAt = session.StartedAt,
            LastActiveAt = session.LastActiveAt,
            ClosedAt = session.ClosedAt,
            TopicId = session.TopicId
        };
    }

    private static MemoryItemDto MapItemToDto(AgentMemoryItem item)
    {
        return new MemoryItemDto
        {
            Id = item.Id,
            SessionId = item.SessionId,
            WorkspaceId = item.WorkspaceId,
            OwnerUserId = item.OwnerUserId,
            AgentProfileId = item.AgentProfileId,
            Kind = item.Kind.ToString().ToLowerInvariant(),
            Title = item.Title,
            Content = item.Content,
            Summary = item.Summary,
            AdmissionState = item.AdmissionState.ToString().ToLowerInvariant(),
            Confidence = item.Confidence,
            Visibility = item.Visibility.ToString().ToLowerInvariant(),
            Importance = item.Importance,
            FreshnessAt = item.FreshnessAt,
            Status = item.Status.ToString().ToLowerInvariant(),
            CreatedAt = item.CreatedAt,
            Evidence = new List<EvidenceDto>()
        };
    }

    private static MemoryKind ParseMemoryKind(string kind)
    {
        return Enum.TryParse<MemoryKind>(kind, true, out var result)
            ? result
            : MemoryKind.TaskState;
    }

    private static Visibility ParseVisibility(string visibility)
    {
        return Enum.TryParse<Visibility>(visibility, true, out var result)
            ? result
            : Visibility.Agent;
    }

    private static EvidenceKind ParseEvidenceKind(string kind)
    {
        return Enum.TryParse<EvidenceKind>(kind, true, out var result)
            ? result
            : EvidenceKind.ManualConfirmation;
    }
}
