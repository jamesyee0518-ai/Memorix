using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Post-ASR text correction service implementation.
/// Loads correction dictionary entries for the workspace and applies them to
/// ASR output text using case-insensitive matching with word-boundary awareness.
/// Supports brand names, person names, terminology, abbreviations, homophones,
/// and user-defined dictionary entries.
/// </summary>
public class PostAsrCorrectionService : IPostAsrCorrectionService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<PostAsrCorrectionService> _logger;

    /// <summary>
    /// Built-in default correction entries that apply globally regardless of
    /// workspace. These cover common ASR misrecognitions for well-known brands,
    /// terms, and abbreviations.
    /// </summary>
    private static readonly List<BuiltInEntry> BuiltInEntries = new()
    {
        // Brand names
        new("iphone", "iPhone", "brand"),
        new("ipad", "iPad", "brand"),
        new("macbook", "MacBook", "brand"),
        new("airpods", "AirPods", "brand"),
        new("youtube", "YouTube", "brand"),
        new("github", "GitHub", "brand"),
        new("linkedin", "LinkedIn", "brand"),
        new("wechat", "WeChat", "brand"),
        new("tiktok", "TikTok", "brand"),
        new("openai", "OpenAI", "brand"),
        new("chatgpt", "ChatGPT", "brand"),
        new("stackoverflow", "StackOverflow", "brand"),

        // Abbreviations
        new("i'm", "I'm", "abbreviation"),
        new("i've", "I've", "abbreviation"),
        new("i'll", "I'll", "abbreviation"),
        new("i'd", "I'd", "abbreviation"),
        new("dont", "don't", "abbreviation"),
        new("cant", "can't", "abbreviation"),
        new("wont", "won't", "abbreviation"),
        new("isnt", "isn't", "abbreviation"),
        new("didnt", "didn't", "abbreviation"),
        new("doesnt", "doesn't", "abbreviation"),

        // Homophones (common ASR errors)
        new("their are", "there are", "homophone"),
        new("their is", "there is", "homophone"),
        new("its a", "it's a", "homophone"),
        new("whats", "what's", "homophone"),
        new("thats", "that's", "homophone"),
        new("lets", "let's", "homophone"),
        new("im", "I'm", "homophone"),
    };

    public PostAsrCorrectionService(
        IAppDbContext db,
        ILogger<PostAsrCorrectionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── IPostAsrCorrectionService ──

    /// <inheritdoc/>
    public async Task<CorrectionResult> CorrectAsync(CorrectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Text))
        {
            return new CorrectionResult { CorrectedText = string.Empty };
        }

        // Load user/tenant dictionary entries for this workspace (and global entries).
        var userEntries = await LoadDictionaryEntriesAsync(request.WorkspaceId, request.Language, ct);

        // Merge built-in entries with user entries. User entries take precedence
        // (applied first) so workspace-specific overrides win.
        var allEntries = new List<(string Original, string Corrected, string Category)>();

        // User entries first (highest priority), sorted by original length descending
        // so longer matches are applied before shorter ones.
        foreach (var entry in userEntries.OrderByDescending(e => e.OriginalText.Length))
        {
            allEntries.Add((entry.OriginalText, entry.CorrectedText, entry.Category));
        }

        // Built-in entries next (lower priority).
        foreach (var entry in BuiltInEntries.OrderByDescending(e => e.Original.Length))
        {
            allEntries.Add((entry.Original, entry.Corrected, entry.Category));
        }

        var changes = new List<CorrectionChange>();
        var appliedEntryCount = 0;
        var correctedText = request.Text;

        foreach (var (original, corrected, category) in allEntries)
        {
            if (string.IsNullOrEmpty(original))
            {
                continue;
            }

            var (resultText, applied) = ApplyCorrection(correctedText, original, corrected, category, changes);

            if (applied)
            {
                correctedText = resultText;
                appliedEntryCount++;
            }
        }

        if (appliedEntryCount > 0)
        {
            _logger.LogInformation(
                "Post-ASR correction applied {ChangeCount} changes from {EntryCount} dictionary entries" +
                " (workspace: {WorkspaceId}, segments: {SegmentCount})",
                changes.Count, appliedEntryCount, request.WorkspaceId,
                request.SegmentUuids?.Count ?? 0);
        }

        return new CorrectionResult
        {
            CorrectedText = correctedText,
            Changes = changes,
            AppliedDictionaryEntries = appliedEntryCount,
        };
    }

    /// <inheritdoc/>
    public async Task<CorrectionDictionary> AddEntryAsync(
        Guid? workspaceId, string original, string corrected, string? category, string? createdBy, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var entry = new CorrectionDictionary
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            OriginalText = original,
            CorrectedText = corrected,
            Category = string.IsNullOrWhiteSpace(category) ? "custom" : category,
            Language = null,
            CreatedBy = createdBy,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.CorrectionDictionaries.Add(entry);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created correction dictionary entry {EntryId} for workspace {WorkspaceId}: '{Original}' -> '{Corrected}'",
            entry.Id, workspaceId, original, corrected);

        return entry;
    }

    /// <inheritdoc/>
    public async Task<List<CorrectionDictionary>> ListEntriesAsync(
        Guid? workspaceId, string? category, CancellationToken ct)
    {
        var query = _db.CorrectionDictionaries
            .Where(e => e.IsActive);

        // Include both workspace-specific and global (null workspace) entries.
        query = query.Where(e => e.WorkspaceId == workspaceId || e.WorkspaceId == null);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(e => e.Category == category);
        }

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteEntryAsync(Guid entryId, CancellationToken ct)
    {
        var entry = await _db.CorrectionDictionaries
            .FirstOrDefaultAsync(e => e.Id == entryId, ct);

        if (entry == null)
        {
            return false;
        }

        // Soft-delete by deactivating.
        entry.IsActive = false;
        entry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deactivated correction dictionary entry {EntryId}", entryId);

        return true;
    }

    // ── Private Helpers ──

    /// <summary>
    /// Loads active dictionary entries for the given workspace and optional language filter.
    /// Returns both workspace-specific and global entries.
    /// </summary>
    private async Task<List<CorrectionDictionary>> LoadDictionaryEntriesAsync(
        Guid? workspaceId, string? language, CancellationToken ct)
    {
        var query = _db.CorrectionDictionaries
            .Where(e => e.IsActive);

        // Include both workspace-specific and global (null workspace) entries.
        query = query.Where(e => e.WorkspaceId == workspaceId || e.WorkspaceId == null);

        // Filter by language if specified. Include entries with null language (language-agnostic).
        if (!string.IsNullOrWhiteSpace(language))
        {
            query = query.Where(e => e.Language == null || e.Language == language);
        }

        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Applies a single correction (original -> corrected) to the text.
    /// Uses case-insensitive matching with word-boundary awareness for Latin scripts.
    /// For CJK text, performs direct substring replacement.
    /// </summary>
    /// <returns>The corrected text and whether any replacement was made.</returns>
    private static (string Text, bool Applied) ApplyCorrection(
        string text, string original, string corrected, string category,
        List<CorrectionChange> changes)
    {
        // Determine whether to use word boundaries.
        // Word boundaries apply to ASCII letter/digit sequences (Latin scripts).
        // CJK characters don't have word boundaries in the regex sense.
        bool useWordBoundary = ShouldUseWordBoundary(original);

        string pattern = useWordBoundary
            ? $@"\b{Regex.Escape(original)}\b"
            : Regex.Escape(original);

        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        bool applied = false;

        string result = regex.Replace(text, match =>
        {
            applied = true;
            // Preserve the capitalization pattern of the matched text.
            return MatchCapitalization(match.Value, corrected);
        });

        if (applied)
        {
            changes.Add(new CorrectionChange
            {
                Original = original,
                Corrected = corrected,
                Category = category,
            });
        }

        return (result, applied);
    }

    /// <summary>
    /// Determines whether word boundaries should be used for the given text.
    /// Word boundaries are used when the text consists primarily of ASCII
    /// letters or digits (Latin scripts). CJK and other non-ASCII characters
    /// do not benefit from \b word boundaries.
    /// </summary>
    private static bool ShouldUseWordBoundary(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Check the first and last characters. If both are ASCII word characters,
        // use word boundaries to avoid partial matches.
        char first = text[0];
        char last = text[^1];

        return IsAsciiWordChar(first) && IsAsciiWordChar(last);
    }

    /// <summary>
    /// Returns true if the character is an ASCII letter, digit, or underscore.
    /// </summary>
    private static bool IsAsciiWordChar(char c)
    {
        return (c >= 'a' && c <= 'z') ||
               (c >= 'A' && c <= 'Z') ||
               (c >= '0' && c <= '9') ||
               c == '_';
    }

    /// <summary>
    /// Adjusts the capitalization of the corrected text to match the pattern
    /// of the original matched text.
    /// - All uppercase -> uppercase
    /// - Title case (first letter upper, rest lower) -> title case
    /// - All lowercase -> lowercase
    /// - Mixed -> return as-is
    /// </summary>
    private static string MatchCapitalization(string matched, string corrected)
    {
        if (string.IsNullOrEmpty(corrected))
        {
            return corrected;
        }

        // Check if matched text is all uppercase.
        bool allUpper = matched.All(c => !char.IsLower(c)) && matched.Any(char.IsUpper);
        if (allUpper)
        {
            return corrected.ToUpperInvariant();
        }

        // Check if matched text is all lowercase.
        bool allLower = matched.All(c => !char.IsUpper(c)) && matched.Any(char.IsLower);
        if (allLower)
        {
            return corrected.ToLowerInvariant();
        }

        // Check title case: first char upper, rest lower.
        if (matched.Length > 0 && char.IsUpper(matched[0]))
        {
            bool restLower = matched.Skip(1).All(c => !char.IsUpper(c));
            if (restLower)
            {
                if (corrected.Length == 1)
                {
                    return corrected.ToUpperInvariant();
                }
                return char.ToUpperInvariant(corrected[0]) + corrected[1..].ToLowerInvariant();
            }
        }

        // Mixed case or single character - return as-is.
        return corrected;
    }

    /// <summary>
    /// Represents a built-in correction entry.
    /// </summary>
    private sealed record BuiltInEntry(string Original, string Corrected, string Category);
}
