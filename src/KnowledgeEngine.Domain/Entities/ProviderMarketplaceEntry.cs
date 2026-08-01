namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Represents a provider entry in the marketplace catalog.
/// Users can browse, install, rate, and uninstall third-party or official
/// audio capability providers through the marketplace.
/// </summary>
public class ProviderMarketplaceEntry
{
    public Guid Id { get; set; }

    /// <summary>Display name of the marketplace entry.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of the provider's capabilities.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Provider identifier (e.g. "whisper_cpp", "funasr", "azure_speech").</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Capability this entry provides (e.g. "audio.transcription").</summary>
    public string Capability { get; set; } = string.Empty;

    /// <summary>Execution mode string (see <see cref="Enums.ExecutionMode"/>).</summary>
    public string ExecutionMode { get; set; } = string.Empty;

    /// <summary>Credential mode string (see <see cref="Enums.CredentialMode"/>).</summary>
    public string CredentialMode { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated supported language codes
    /// (e.g. "zh,en,ja"). Empty means all languages.
    /// </summary>
    public string SupportedLanguages { get; set; } = string.Empty;

    /// <summary>Pricing unit (e.g. "SECOND", "MINUTE", "REQUEST").</summary>
    public string PricingUnit { get; set; } = string.Empty;

    /// <summary>Whether this is an official Memorix-bundled provider.</summary>
    public bool IsOfficial { get; set; }

    /// <summary>Provider version string (e.g. "1.2.0").</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>User rating from 0 to 5 (averaged).</summary>
    public decimal Rating { get; set; }

    /// <summary>Total number of successful installs.</summary>
    public long InstallCount { get; set; }

    /// <summary>Whether this provider is currently installed on the local instance.</summary>
    public bool IsInstalled { get; set; }

    /// <summary>Author or publisher name.</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Optional author homepage / repository URL.</summary>
    public string? AuthorUrl { get; set; }

    /// <summary>JSON array of tags for search and filtering (e.g. ["local","gpu","free"]).</summary>
    public string TagsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Status values for marketplace entries (install state metadata).
/// </summary>
public static class MarketplaceEntryStatuses
{
    public const string Installed = "installed";
    public const string NotInstalled = "not_installed";
    public const string Pending = "pending";
}
