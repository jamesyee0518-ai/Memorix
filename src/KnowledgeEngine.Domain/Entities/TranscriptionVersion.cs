using KnowledgeEngine.Domain.Enums;

namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Immutable record of a single transcription-segment text version within the
/// version tree. A segment (identified by <see cref="SegmentUuid"/>) accrues
/// multiple versions over its lifetime: RAW_MODEL (ASR output) ->
/// POST_PROCESSED (dictionary correction) -> SERVER_RETRANSCRIBED (re-transcription)
/// / USER_EDITED (manual edit) -> MERGED (three-way merge) -> PUBLISHED.
/// Each version records its <see cref="ParentVersionId"/> to form a tree, enabling
/// full provenance and three-way merge against any ancestor.
/// </summary>
public class TranscriptionVersion
{
    public Guid Id { get; set; }

    public Guid TranscriptionJobId { get; set; }

    /// <summary>Stable segment UUID this version belongs to. Never changes across versions.</summary>
    public string SegmentUuid { get; set; } = string.Empty;

    /// <summary>
    /// Version label: RAW_MODEL / POST_PROCESSED / SERVER_RETRANSCRIBED /
    /// USER_EDITED / MERGED / PUBLISHED (see <see cref="SegmentVersions"/>).
    /// </summary>
    public string Version { get; set; } = SegmentVersions.RawModel;

    /// <summary>
    /// Parent version in the version tree. Root versions (RAW_MODEL) have a null parent.
    /// Used to reconstruct the full provenance chain and to drive three-way merges.
    /// </summary>
    public Guid? ParentVersionId { get; set; }

    /// <summary>The transcribed text for this version.</summary>
    public string Text { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Identifier of the user or system process that created this version.</summary>
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Static rules governing the transcription version tree: which version
/// transitions are permitted, the priority ordering used to pick the "latest"
/// version for display, and helpers for walking the tree.
/// </summary>
public static class TranscriptionVersionTree
{
    /// <summary>
    /// Allowed child versions for each parent version. A transition is valid
    /// only if the target appears in the source's allowed-children list.
    /// Mirrors the priority ordering in <see cref="GetPriority"/>.
    /// </summary>
    private static readonly Dictionary<string, string[]> Progression = new(StringComparer.OrdinalIgnoreCase)
    {
        [SegmentVersions.RawModel] = new[]
        {
            SegmentVersions.PostProcessed,
            SegmentVersions.ServerRetranscribed,
            SegmentVersions.UserEdited,
            SegmentVersions.Merged,
            SegmentVersions.Published,
        },
        [SegmentVersions.PostProcessed] = new[]
        {
            SegmentVersions.ServerRetranscribed,
            SegmentVersions.UserEdited,
            SegmentVersions.Merged,
            SegmentVersions.Published,
        },
        [SegmentVersions.ServerRetranscribed] = new[]
        {
            SegmentVersions.UserEdited,
            SegmentVersions.Merged,
            SegmentVersions.Published,
        },
        [SegmentVersions.UserEdited] = new[]
        {
            SegmentVersions.ServerRetranscribed,
            SegmentVersions.Merged,
            SegmentVersions.Published,
        },
        [SegmentVersions.Merged] = new[]
        {
            SegmentVersions.UserEdited,
            SegmentVersions.Published,
        },
        [SegmentVersions.Published] = new[]
        {
            SegmentVersions.UserEdited,
        },
    };

    /// <summary>
    /// Returns true if a version may transition directly from
    /// <paramref name="fromVersion"/> to <paramref name="toVersion"/>.
    /// </summary>
    public static bool CanTransition(string fromVersion, string toVersion)
    {
        if (string.IsNullOrEmpty(fromVersion) || string.IsNullOrEmpty(toVersion))
            return false;

        if (!Progression.TryGetValue(fromVersion, out var children))
            return false;

        return Array.Exists(children, c => string.Equals(c, toVersion, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the list of versions that may be created directly from
    /// <paramref name="version"/>. Returns an empty list for unknown versions.
    /// </summary>
    public static IReadOnlyList<string> GetAllowedChildren(string version)
    {
        return Progression.TryGetValue(version ?? string.Empty, out var children)
            ? children
            : Array.Empty<string>();
    }

    /// <summary>
    /// Returns a priority value for a version label. Higher values are preferred
    /// when multiple versions of the same segment UUID coexist and the caller
    /// must pick the "latest" one for display.
    /// </summary>
    public static int GetPriority(string version)
    {
        return (version ?? string.Empty) switch
        {
            SegmentVersions.Published => 6,
            SegmentVersions.Merged => 5,
            SegmentVersions.UserEdited => 4,
            SegmentVersions.PostProcessed => 3,
            SegmentVersions.ServerRetranscribed => 2,
            SegmentVersions.RawModel => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// The canonical root version label from which every version tree starts.
    /// </summary>
    public const string RootVersion = SegmentVersions.RawModel;

    /// <summary>
    /// Returns true if <paramref name="version"/> is a recognized segment version label.
    /// </summary>
    public static bool IsValidVersion(string version)
    {
        return (version ?? string.Empty) switch
        {
            SegmentVersions.RawModel
            or SegmentVersions.PostProcessed
            or SegmentVersions.ServerRetranscribed
            or SegmentVersions.UserEdited
            or SegmentVersions.Merged
            or SegmentVersions.Published => true,
            _ => false,
        };
    }
}
