namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Maps sensitive entities to reversible placeholders before sending to external
/// LLM providers. Stored encrypted in the local trusted domain only.
/// External provider requests, logs and caches must never contain this table.
/// </summary>
public class PseudonymMapping
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }

    /// <summary>Scope of the mapping: REQUEST / MEETING / WORKSPACE</summary>
    public string Scope { get; set; } = PseudonymScopes.Meeting;

    /// <summary>Entity type: PERSON / ORG / PROJECT / PHONE / EMAIL / AMOUNT / CUSTOM</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Type-safe placeholder, e.g. [PERSON_001], [PROJECT_002], [AMOUNT_003].</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Encrypted original value (AES-GCM).</summary>
    public string EncryptedOriginal { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the normalized original for deduplication without decryption.</summary>
    public string NormalizedHash { get; set; } = string.Empty;

    /// <summary>Mapping version for forward compatibility.</summary>
    public int MappingVersion { get; set; } = 1;

    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Pseudonym mapping scope values.</summary>
public static class PseudonymScopes
{
    public const string Request = "REQUEST";
    public const string Meeting = "MEETING";
    public const string Workspace = "WORKSPACE";
}

/// <summary>Pseudonym entity type values.</summary>
public static class PseudonymEntityTypes
{
    public const string Person = "PERSON";
    public const string Org = "ORG";
    public const string Project = "PROJECT";
    public const string Phone = "PHONE";
    public const string Email = "EMAIL";
    public const string Amount = "AMOUNT";
    public const string Custom = "CUSTOM";
}

/// <summary>Privacy transformation (masking) modes.</summary>
public static class PrivacyMaskingModes
{
    public const string Off = "OFF";
    public const string MaskPii = "MASK_PII";
    public const string MaskCustom = "MASK_CUSTOM";
    public const string LocalOnly = "LOCAL_ONLY";
}
