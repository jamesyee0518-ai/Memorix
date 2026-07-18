namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Links an inbox item to one or more stored file objects (§7.2).
/// Each attachment records the role (primary, supplementary, etc.)
/// and the file metadata at the time of upload.
/// </summary>
public class InboxAttachment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InboxItemId { get; set; }
    public Guid FileId { get; set; }

    /// <summary>"primary" | "supplementary" | "thumbnail" | ...</summary>
    public string Role { get; set; } = "primary";

    public string Filename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
