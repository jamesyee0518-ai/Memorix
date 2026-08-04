using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Privacy transformation service for external LLM calls.
/// <para>
/// Scans transcript text for sensitive entities (phone numbers, emails, ID cards,
/// person names, monetary amounts, project codes), replaces them with type-safe
/// placeholders before sending to external providers, and restores the originals
/// from encrypted mappings after receiving the LLM response.
/// </para>
/// <para>
/// Encryption version V1 uses Base64 encoding. Future versions will upgrade to
/// AES-GCM without changing the public interface.
/// </para>
/// </summary>
public class PrivacyTransformationService : IPrivacyTransformationService
{
    private readonly IAppDbContext _db;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<PrivacyTransformationService> _logger;

    // -----------------------------------------------------------------------
    // Regex patterns (compiled once for reuse)
    // -----------------------------------------------------------------------

    /// <summary>Chinese mobile phone number: 1[3-9] followed by 9 digits.</summary>
    private static readonly Regex PhoneRegex = new(
        @"1[3-9]\d{9}",
        RegexOptions.Compiled);

    /// <summary>Standard email address pattern.</summary>
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9.-]+",
        RegexOptions.Compiled);

    /// <summary>Chinese national ID card number (18 digits, last may be X).</summary>
    private static readonly Regex IdCardRegex = new(
        @"\d{17}[\dXx]",
        RegexOptions.Compiled);

    /// <summary>
    /// Chinese person name: 2-4 consecutive Chinese characters immediately
    /// followed by a speech verb. Uses lookahead so only the name is captured.
    /// </summary>
    private static readonly Regex PersonNameRegex = new(
        @"[\u4e00-\u9fa5]{2,4}(?=说|表示|认为|提出|建议|强调|指出|回答|问|回复|补充|提到)",
        RegexOptions.Compiled);

    /// <summary>Monetary amounts: Chinese yuan (e.g. 123元, 1234.56元) or USD (e.g. $100, $99.99).</summary>
    private static readonly Regex AmountRegex = new(
        @"\d+\.?\d*元|\$\d+\.?\d*",
        RegexOptions.Compiled);

    /// <summary>Project codes: text enclosed in square brackets, e.g. [项目代号].</summary>
    private static readonly Regex ProjectCodeRegex = new(
        @"\[[^\]]+\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Detects placeholder-shaped tokens (e.g. [PERSON_001]) for RESTORE_FAILED
    /// scanning and project-code filtering.
    /// </summary>
    private static readonly Regex PlaceholderTokenRegex = new(
        @"\[[A-Z]+_\d+\]",
        RegexOptions.Compiled);

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    private const string EncryptionVersion = "V1";
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public PrivacyTransformationService(
        IAppDbContext db,
        ICredentialStore credentialStore,
        ILogger<PrivacyTransformationService> logger)
    {
        _db = db;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    // =======================================================================
    // MaskAsync
    // =======================================================================

    /// <inheritdoc/>
    public async Task<PrivacyTransformResult> MaskAsync(
        Guid meetingId,
        string text,
        string maskingMode,
        CancellationToken ct)
    {
        var result = new PrivacyTransformResult { MaskedText = text };

        // --- OFF: return original text unchanged ---
        if (string.IsNullOrEmpty(maskingMode) ||
            maskingMode.Equals(PrivacyMaskingModes.Off, StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        // --- Detect entities ---
        var piiEntities = DetectPiiEntities(text);
        var customEntities = DetectCustomEntities(text);
        bool hasSensitiveEntities = piiEntities.Count > 0 || customEntities.Count > 0;

        // --- LOCAL_ONLY: block if any sensitive entity is found ---
        if (maskingMode.Equals(PrivacyMaskingModes.LocalOnly, StringComparison.OrdinalIgnoreCase))
        {
            if (hasSensitiveEntities)
            {
                result.Blocked = true;
                _logger.LogWarning(
                    "LOCAL_ONLY mode blocked text for meeting {MeetingId}: " +
                    "{PiiCount} PII entities, {CustomCount} custom entities detected.",
                    meetingId, piiEntities.Count, customEntities.Count);
            }
            return result;
        }

        // --- Determine which entities to mask based on mode ---
        // MASK_PII  → mask PII only
        // MASK_CUSTOM → mask both PII and custom (superset)
        List<DetectedEntity> entitiesToMask;
        if (maskingMode.Equals(PrivacyMaskingModes.MaskPii, StringComparison.OrdinalIgnoreCase))
        {
            entitiesToMask = new List<DetectedEntity>(piiEntities);
        }
        else if (maskingMode.Equals(PrivacyMaskingModes.MaskCustom, StringComparison.OrdinalIgnoreCase))
        {
            entitiesToMask = new List<DetectedEntity>(piiEntities);
            entitiesToMask.AddRange(customEntities);
        }
        else
        {
            // Unknown mode – treat as OFF
            _logger.LogWarning("Unknown masking mode '{Mode}' – no masking applied.", maskingMode);
            return result;
        }

        if (entitiesToMask.Count == 0)
        {
            return result;
        }

        // --- Resolve overlapping matches (keep the longer entity) ---
        entitiesToMask = ResolveOverlaps(entitiesToMask);

        // --- Load existing mappings for this meeting (to continue numbering) ---
        var existingMappings = await _db.PseudonymMappings
            .Where(m => m.MeetingId == meetingId)
            .ToListAsync(ct);

        // Per-type counter starting from the max existing number
        var typeCounters = existingMappings
            .GroupBy(m => m.EntityType)
            .ToDictionary(
                g => g.Key,
                g => g.Max(m => ExtractPlaceholderNumber(m.Placeholder)));

        // Existing dedup map: (EntityType, NormalizedHash) → Placeholder
        var placeholderMap = existingMappings
            .ToDictionary(
                m => BuildDedupKey(m.EntityType, m.NormalizedHash),
                m => m.Placeholder);

        var newMappings = new List<PseudonymMapping>();
        var maskRecords = new List<MaskRecord>();

        // --- First pass: assign placeholders in text order (natural numbering) ---
        foreach (var entity in entitiesToMask.OrderBy(e => e.StartIndex))
        {
            var normalizedHash = ComputeNormalizedHash(entity.Value);
            var dedupKey = BuildDedupKey(entity.EntityType, normalizedHash);

            if (!placeholderMap.TryGetValue(dedupKey, out var placeholder))
            {
                // Assign a new placeholder: [TYPE_NNN]
                if (!typeCounters.TryGetValue(entity.EntityType, out var counter))
                    counter = 0;
                counter++;
                typeCounters[entity.EntityType] = counter;

                placeholder = $"[{entity.EntityType}_{counter:D3}]";
                placeholderMap[dedupKey] = placeholder;

                // Create the PseudonymMapping record
                var mapping = new PseudonymMapping
                {
                    Id = Guid.NewGuid(),
                    MeetingId = meetingId,
                    Scope = PseudonymScopes.Meeting,
                    EntityType = entity.EntityType,
                    Placeholder = placeholder,
                    EncryptedOriginal = EncryptOriginal(entity.Value),
                    NormalizedHash = normalizedHash,
                    MappingVersion = 1,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.Add(DefaultExpiry)
                };
                newMappings.Add(mapping);

                // Store the original value in the credential store for future
                // AES-GCM key retrieval (V1 stores the raw secret).
                var keyRef = BuildCredentialKeyRef(meetingId, placeholder);
                await _credentialStore.SetAsync(keyRef, entity.Value, ct);
            }
        }

        // --- Second pass: replace entities in text from end to start ---
        // Processing in reverse order preserves earlier indices.
        var maskedText = text;
        foreach (var entity in entitiesToMask.OrderByDescending(e => e.StartIndex))
        {
            var normalizedHash = ComputeNormalizedHash(entity.Value);
            var dedupKey = BuildDedupKey(entity.EntityType, normalizedHash);
            var placeholder = placeholderMap[dedupKey];

            maskRecords.Add(new MaskRecord
            {
                EntityType = entity.EntityType,
                Placeholder = placeholder,
                StartIndex = entity.StartIndex,
                Length = entity.Length
            });

            maskedText = maskedText
                .Remove(entity.StartIndex, entity.Length)
                .Insert(entity.StartIndex, placeholder);
        }

        // --- Persist new mappings ---
        if (newMappings.Count > 0)
        {
            _db.PseudonymMappings.AddRange(newMappings);
            await _db.SaveChangesAsync(ct);
        }

        // Sort mask records by position for a clean audit trail
        maskRecords = maskRecords.OrderBy(r => r.StartIndex).ToList();

        result.MaskedText = maskedText;
        result.MaskedCount = newMappings.Count;
        result.Masks = maskRecords;

        _logger.LogInformation(
            "Masked {UniqueCount} unique entities ({TotalMatches} total matches) " +
            "for meeting {MeetingId} in {Mode} mode.",
            newMappings.Count, maskRecords.Count, meetingId, maskingMode);

        return result;
    }

    // =======================================================================
    // RestoreAsync
    // =======================================================================

    /// <inheritdoc/>
    public async Task<string> RestoreAsync(
        Guid meetingId,
        string text,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var mappings = await _db.PseudonymMappings
            .Where(m => m.MeetingId == meetingId)
            .ToListAsync(ct);

        var knownPlaceholders = mappings.Select(m => m.Placeholder).ToHashSet();

        if (mappings.Count == 0)
        {
            // No mappings – check for stray placeholder tokens
            LogRestoreFailedTokens(text, knownPlaceholders, meetingId);
            return text;
        }

        // Sort by placeholder length descending so that longer placeholders
        // are replaced first, preventing shorter ones from corrupting them.
        var sortedMappings = mappings
            .OrderByDescending(m => m.Placeholder.Length)
            .ThenBy(m => m.Placeholder)
            .ToList();

        var restoredText = text;

        foreach (var mapping in sortedMappings)
        {
            var original = DecryptOriginal(mapping.EncryptedOriginal);

            // Only replace at exact-match positions.
            // string.Replace performs exact token matching; the closing ']'
            // naturally prevents partial matches inside longer tokens.
            if (restoredText.Contains(mapping.Placeholder))
            {
                restoredText = restoredText.Replace(mapping.Placeholder, original);
            }
        }

        // Scan for placeholder-shaped tokens that remain in the text.
        // These are either:
        //   (a) not in the mapping table → RESTORE_FAILED (LLM hallucinated or
        //       the token was tampered with) – retain the placeholder as-is.
        //   (b) in the mapping table but the restored original itself contained
        //       a placeholder-shaped token – false positive, ignore.
        LogRestoreFailedTokens(restoredText, knownPlaceholders, meetingId);

        return restoredText;
    }

    // =======================================================================
    // GetMappingsAsync
    // =======================================================================

    /// <inheritdoc/>
    public async Task<List<PseudonymMapping>> GetMappingsAsync(
        Guid meetingId,
        CancellationToken ct)
    {
        return await _db.PseudonymMappings
            .Where(m => m.MeetingId == meetingId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    // =======================================================================
    // PurgeAsync
    // =======================================================================

    /// <inheritdoc/>
    public async Task PurgeAsync(Guid meetingId, CancellationToken ct)
    {
        var mappings = await _db.PseudonymMappings
            .Where(m => m.MeetingId == meetingId)
            .ToListAsync(ct);

        if (mappings.Count == 0)
            return;

        // Delete secrets from the credential store
        foreach (var mapping in mappings)
        {
            var keyRef = BuildCredentialKeyRef(meetingId, mapping.Placeholder);
            await _credentialStore.DeleteAsync(keyRef, ct);
        }

        // Delete mapping records from the database
        _db.PseudonymMappings.RemoveRange(mappings);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Purged {Count} pseudonym mappings for meeting {MeetingId}.",
            mappings.Count, meetingId);
    }

    // =======================================================================
    // Entity detection helpers
    // =======================================================================

    /// <summary>
    /// Detects PII entities in the text: phone numbers, emails, ID cards,
    /// and person names.
    /// </summary>
    private static List<DetectedEntity> DetectPiiEntities(string text)
    {
        var entities = new List<DetectedEntity>();

        // Phone numbers
        foreach (Match m in PhoneRegex.Matches(text))
        {
            entities.Add(new DetectedEntity(
                PseudonymEntityTypes.Phone, m.Value, m.Index, m.Length));
        }

        // Email addresses
        foreach (Match m in EmailRegex.Matches(text))
        {
            entities.Add(new DetectedEntity(
                PseudonymEntityTypes.Email, m.Value, m.Index, m.Length));
        }

        // ID card numbers (18 digits, last may be X)
        foreach (Match m in IdCardRegex.Matches(text))
        {
            entities.Add(new DetectedEntity(
                PseudonymEntityTypes.Custom, m.Value, m.Index, m.Length));
        }

        // Person names (2-4 Chinese characters before speech verbs)
        foreach (Match m in PersonNameRegex.Matches(text))
        {
            entities.Add(new DetectedEntity(
                PseudonymEntityTypes.Person, m.Value, m.Index, m.Length));
        }

        return entities;
    }

    /// <summary>
    /// Detects custom sensitive entities in the text: monetary amounts and
    /// project codes (text in square brackets).
    /// </summary>
    private static List<DetectedEntity> DetectCustomEntities(string text)
    {
        var entities = new List<DetectedEntity>();

        // Monetary amounts
        foreach (Match m in AmountRegex.Matches(text))
        {
            entities.Add(new DetectedEntity(
                PseudonymEntityTypes.Amount, m.Value, m.Index, m.Length));
        }

        // Project codes – text in square brackets
        foreach (Match m in ProjectCodeRegex.Matches(text))
        {
            // Skip if the bracket content already looks like a placeholder
            // (e.g. [PERSON_001]) to avoid masking our own tokens.
            if (PlaceholderTokenRegex.IsMatch(m.Value))
                continue;

            entities.Add(new DetectedEntity(
                PseudonymEntityTypes.Project, m.Value, m.Index, m.Length));
        }

        return entities;
    }

    // =======================================================================
    // Overlap resolution
    // =======================================================================

    /// <summary>
    /// Resolves overlapping entity matches by keeping the longer entity when
    /// two ranges overlap. Entities are sorted by start index, then by length
    /// descending, and greedily selected if they don't overlap any already
    /// kept entity.
    /// </summary>
    private static List<DetectedEntity> ResolveOverlaps(List<DetectedEntity> entities)
    {
        if (entities.Count <= 1)
            return entities;

        var sorted = entities
            .OrderBy(e => e.StartIndex)
            .ThenByDescending(e => e.Length)
            .ToList();

        var result = new List<DetectedEntity>();

        foreach (var entity in sorted)
        {
            bool overlaps = false;
            foreach (var kept in result)
            {
                if (entity.StartIndex < kept.StartIndex + kept.Length &&
                    entity.StartIndex + entity.Length > kept.StartIndex)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
                result.Add(entity);
        }

        return result;
    }

    // =======================================================================
    // Cryptography helpers
    // =======================================================================

    /// <summary>
    /// Computes the SHA-256 hash of the normalized original value.
    /// Normalization: trim + lowercase (case-insensitive deduplication).
    /// Returns a lowercase hex string.
    /// </summary>
    private static string ComputeNormalizedHash(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Encrypts (V1: Base64-encodes) the original value.
    /// The "V1:" prefix identifies the encryption version for forward
    /// compatibility with AES-GCM.
    /// </summary>
    private static string EncryptOriginal(string original)
    {
        var bytes = Encoding.UTF8.GetBytes(original);
        return $"{EncryptionVersion}:{Convert.ToBase64String(bytes)}";
    }

    /// <summary>
    /// Decrypts the EncryptedOriginal field back to the plain-text value.
    /// V1: Base64-decode. Future versions will use AES-GCM.
    /// </summary>
    private static string DecryptOriginal(string encrypted)
    {
        if (encrypted.StartsWith($"{EncryptionVersion}:", StringComparison.Ordinal))
        {
            var base64 = encrypted[EncryptionVersion.Length..];
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }

        // Legacy: treat as raw Base64 without version prefix
        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            throw new NotSupportedException(
                $"Unsupported encryption format for value: {encrypted[..Math.Min(20, encrypted.Length)]}...");
        }
    }

    // =======================================================================
    // Placeholder / key helpers
    // =======================================================================

    /// <summary>
    /// Extracts the numeric portion from a placeholder like [PERSON_001] → 1.
    /// </summary>
    private static int ExtractPlaceholderNumber(string placeholder)
    {
        var inner = placeholder.TrimStart('[').TrimEnd(']');
        var underscoreIndex = inner.LastIndexOf('_');
        if (underscoreIndex >= 0 &&
            int.TryParse(inner[(underscoreIndex + 1)..], out var num))
        {
            return num;
        }
        return 0;
    }

    /// <summary>
    /// Builds the deduplication key combining entity type and normalized hash.
    /// </summary>
    private static string BuildDedupKey(string entityType, string normalizedHash)
        => $"{entityType}:{normalizedHash}";

    /// <summary>
    /// Builds the credential store key reference for a pseudonym mapping.
    /// Format: pseudonym:{meetingId}:{placeholder}
    /// </summary>
    private static string BuildCredentialKeyRef(Guid meetingId, string placeholder)
        => $"pseudonym:{meetingId}:{placeholder}";

    // =======================================================================
    // Restore-failed logging
    // =======================================================================

    /// <summary>
    /// Scans the text for placeholder-shaped tokens that are not in the known
    /// set and logs them as RESTORE_FAILED. The placeholder is retained in the
    /// text (not restored) so the caller can detect the anomaly.
    /// </summary>
    private void LogRestoreFailedTokens(
        string text,
        HashSet<string> knownPlaceholders,
        Guid meetingId)
    {
        var reported = new HashSet<string>();
        foreach (Match m in PlaceholderTokenRegex.Matches(text))
        {
            if (knownPlaceholders.Contains(m.Value))
                continue;
            if (!reported.Add(m.Value))
                continue;

            _logger.LogWarning(
                "RESTORE_FAILED: Placeholder '{Placeholder}' found in LLM response " +
                "but does not exist in the mapping table for meeting {MeetingId}. " +
                "Placeholder retained (not restored).",
                m.Value, meetingId);
        }
    }

    // =======================================================================
    // Internal entity representation
    // =======================================================================

    /// <summary>
    /// Represents a detected sensitive entity within the source text.
    /// </summary>
    private sealed record DetectedEntity(
        string EntityType,
        string Value,
        int StartIndex,
        int Length);
}
