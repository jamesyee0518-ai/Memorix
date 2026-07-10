using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Agent;

public class AgentPermissionGuard : IAgentPermissionGuard
{
    private readonly IAppDbContext _db;
    private readonly ILogger<AgentPermissionGuard> _logger;

    // Default read-only tools allowed when no agent profile is specified
    private static readonly HashSet<string> DefaultAllowedTools = new()
    {
        "list_topics",
        "search_memory",
        "ask_memory",
        "get_document",
        "get_report"
    };

    // Phase 7: Sensitivity levels that require AllowSensitiveDocuments permission
    private static readonly HashSet<string> SensitiveLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "private",
        "sensitive",
        "restricted"
    };

    // Phase 7: Tool name to scope mapping (used when Scopes is null/empty)
    private static readonly Dictionary<string, string> ToolToScopeMap = new()
    {
        ["list_topics"] = "workspace:read",
        ["search_memory"] = "search:read",
        ["ask_memory"] = "rag:read",
        ["get_document"] = "document:read",
        ["get_report"] = "report:read",
        ["create_inbox_item"] = "inbox:write",
        ["import_url"] = "source:import"
    };

    // Cache the loaded profile within the scope (scoped service = one instance per request)
    private AgentProfile? _cachedProfile;
    private Guid? _cachedProfileId;
    private bool _profileLoaded;

    public AgentPermissionGuard(IAppDbContext db, ILogger<AgentPermissionGuard> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> CanUseToolAsync(Guid userId, Guid? agentProfileId, string toolName, CancellationToken ct = default)
    {
        if (!agentProfileId.HasValue)
        {
            // No profile specified: allow all default read-only tools
            return DefaultAllowedTools.Contains(toolName);
        }

        var profile = await GetOrCreateProfileAsync(userId, agentProfileId.Value, ct);

        if (profile == null)
        {
            return false;
        }

        if (profile.Status != "active")
        {
            return false;
        }

        // If no tool restrictions, allow all default tools
        if (string.IsNullOrWhiteSpace(profile.AllowedToolNames))
        {
            return DefaultAllowedTools.Contains(toolName);
        }

        List<string>? allowedTools = null;
        try
        {
            allowedTools = JsonSerializer.Deserialize<List<string>>(profile.AllowedToolNames);
        }
        catch
        {
            _logger.LogWarning("Failed to deserialize AllowedToolNames for agent profile {Id}", agentProfileId);
            return false;
        }

        if (allowedTools == null || allowedTools.Count == 0)
        {
            return DefaultAllowedTools.Contains(toolName);
        }

        return allowedTools.Contains(toolName);
    }

    public async Task<List<Guid>> GetAccessibleTopicIdsAsync(Guid userId, Guid? agentProfileId, CancellationToken ct = default)
    {
        if (!agentProfileId.HasValue)
        {
            // No profile: return empty list meaning all topics accessible
            return new List<Guid>();
        }

        var profile = await GetOrCreateProfileAsync(userId, agentProfileId.Value, ct);
        if (profile == null)
        {
            return new List<Guid>();
        }

        if (string.IsNullOrWhiteSpace(profile.AllowedTopicIds))
        {
            // No topic restrictions: return empty list meaning all accessible
            return new List<Guid>();
        }

        try
        {
            var topicIds = JsonSerializer.Deserialize<List<Guid>>(profile.AllowedTopicIds);
            return topicIds ?? new List<Guid>();
        }
        catch
        {
            _logger.LogWarning("Failed to deserialize AllowedTopicIds for agent profile {Id}", agentProfileId);
            return new List<Guid>();
        }
    }

    public async Task<bool> CanAccessDocumentAsync(Guid userId, Guid? agentProfileId, Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);

        if (document == null)
        {
            return false;
        }

        // Phase 7: Check sensitivity level
        // "public" and "normal" are always allowed;
        // "private"/"sensitive"/"restricted" require AllowSensitiveDocuments
        if (IsSensitiveLevel(document.SensitivityLevel))
        {
            if (!agentProfileId.HasValue)
            {
                // No profile: deny sensitive documents (AllowSensitiveDocuments defaults to false)
                return false;
            }

            var profile = await GetOrCreateProfileAsync(userId, agentProfileId.Value, ct);
            if (profile == null || !profile.AllowSensitiveDocuments)
            {
                return false;
            }
        }

        // If no topic restriction, allow access
        var allowedTopicIds = await GetAccessibleTopicIdsAsync(userId, agentProfileId, ct);
        if (allowedTopicIds.Count == 0)
        {
            return true;
        }

        // If document has no topic but there are restrictions, deny
        if (!document.TopicId.HasValue)
        {
            return false;
        }

        return allowedTopicIds.Contains(document.TopicId.Value);
    }

    public int GetMaxResults(Guid? agentProfileId)
    {
        if (!agentProfileId.HasValue)
        {
            return 20;
        }

        // Use the cached profile if available (loaded during CanUseToolAsync)
        if (_cachedProfile != null && _cachedProfileId == agentProfileId.Value)
        {
            return _cachedProfile.MaxResultsPerCall > 0 ? _cachedProfile.MaxResultsPerCall : 20;
        }

        // Default when profile not yet loaded
        return 20;
    }

    /// <summary>
    /// Loads and caches the agent profile for the current scope.
    /// </summary>
    private async Task<AgentProfile?> GetOrCreateProfileAsync(Guid userId, Guid agentProfileId, CancellationToken ct)
    {
        // Return cached profile if already loaded for this ID
        if (_profileLoaded && _cachedProfileId == agentProfileId)
        {
            return _cachedProfile;
        }

        _cachedProfile = await _db.AgentProfiles
            .FirstOrDefaultAsync(a => a.Id == agentProfileId && a.UserId == userId, ct);
        _cachedProfileId = agentProfileId;
        _profileLoaded = true;

        return _cachedProfile;
    }

    /// <summary>
    /// Loads and caches the agent profile by ID only (no userId filter).
    /// Used by HasScopeAsync and FilterSensitiveDocumentsAsync which only receive profileId.
    /// </summary>
    private async Task<AgentProfile?> GetProfileByIdAsync(Guid profileId, CancellationToken ct)
    {
        // Return cached profile if already loaded for this ID
        if (_profileLoaded && _cachedProfileId == profileId)
        {
            return _cachedProfile;
        }

        _cachedProfile = await _db.AgentProfiles
            .FirstOrDefaultAsync(a => a.Id == profileId, ct);
        _cachedProfileId = profileId;
        _profileLoaded = true;

        return _cachedProfile;
    }

    // ===== Phase 7: Scope & Sensitivity Methods =====

    /// <summary>
    /// Checks whether a sensitivity level requires special permission.
    /// "public" and "normal" are always accessible; "private"/"sensitive"/"restricted" require AllowSensitiveDocuments.
    /// </summary>
    private static bool IsSensitiveLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return false;
        return SensitiveLevels.Contains(level);
    }

    /// <inheritdoc/>
    public async Task<bool> HasScopeAsync(Guid profileId, string scope, CancellationToken ct = default)
    {
        var profile = await GetProfileByIdAsync(profileId, ct);
        if (profile == null)
        {
            return false;
        }

        // If Scopes is explicitly set, check directly
        if (!string.IsNullOrWhiteSpace(profile.Scopes))
        {
            List<string>? scopes = null;
            try
            {
                scopes = JsonSerializer.Deserialize<List<string>>(profile.Scopes);
            }
            catch
            {
                _logger.LogWarning("Failed to deserialize Scopes for agent profile {Id}", profileId);
                return false;
            }

            return scopes != null && scopes.Contains(scope);
        }

        // Scopes is null/empty: infer from AllowedToolNames
        if (string.IsNullOrWhiteSpace(profile.AllowedToolNames))
        {
            return false;
        }

        List<string>? allowedTools = null;
        try
        {
            allowedTools = JsonSerializer.Deserialize<List<string>>(profile.AllowedToolNames);
        }
        catch
        {
            _logger.LogWarning("Failed to deserialize AllowedToolNames for agent profile {Id}", profileId);
            return false;
        }

        if (allowedTools == null || allowedTools.Count == 0)
        {
            return false;
        }

        // Infer scopes from tool names
        var inferredScopes = new HashSet<string>();
        foreach (var toolName in allowedTools)
        {
            if (ToolToScopeMap.TryGetValue(toolName, out var inferredScope))
            {
                inferredScopes.Add(inferredScope);
            }
        }

        return inferredScopes.Contains(scope);
    }

    /// <inheritdoc/>
    public async Task<List<Document>> FilterSensitiveDocumentsAsync(List<Document> documents, Guid profileId, CancellationToken ct = default)
    {
        if (documents == null || documents.Count == 0)
        {
            return new List<Document>();
        }

        var profile = await GetProfileByIdAsync(profileId, ct);
        if (profile == null)
        {
            // No profile found: filter out all sensitive documents
            return documents.Where(d => !IsSensitiveLevel(d.SensitivityLevel)).ToList();
        }

        // If profile allows sensitive documents, return all
        if (profile.AllowSensitiveDocuments)
        {
            return documents;
        }

        // Filter out sensitive documents
        return documents.Where(d => !IsSensitiveLevel(d.SensitivityLevel)).ToList();
    }
}
