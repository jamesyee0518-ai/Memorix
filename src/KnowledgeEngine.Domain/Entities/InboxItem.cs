namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Inbox items are the buffer layer for all incoming information sources (§6.1).
/// Sources include: mobile capture, desktop manual input, browser extension, etc.
/// </summary>
public class InboxItem
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TopicId { get; set; }

    /// <summary>
    /// "text" | "url" | "image" | "audio" | "file" | "mixed" (§6.1 inputType)
    /// </summary>
    public string InputType { get; set; } = "text";

    /// <summary>Legacy alias for InputType</summary>
    public string ItemType { get; set; } = "text";

    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? SourceUrl { get; set; }
    public string? FilePath { get; set; }
    public Guid? FileId { get; set; }

    /// <summary>
    /// "pending" | "classified" | "imported" | "processing" | "done" | "failed" | "archived"
    /// </summary>
    public string Status { get; set; } = "pending";

    // AI suggestion fields (§6.1)
    public Guid? SuggestedTopicId { get; set; }
    public string? SuggestedTitle { get; set; }
    public string? SuggestedTags { get; set; }

    // Origin tracking (§6.1)
    /// <summary>"mobile" | "desktop" | "extension" | "api"</summary>
    public string CreatedFrom { get; set; } = "desktop";
    public string? OriginDeviceId { get; set; }
    public string? OriginClientVersion { get; set; }

    // Import result tracking (§6.1)
    public Guid? SourceId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
