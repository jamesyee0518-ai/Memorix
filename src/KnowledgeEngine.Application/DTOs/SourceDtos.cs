namespace KnowledgeEngine.Application.DTOs;

public class ImportUrlRequest
{
    public Guid TopicId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
}

public class ImportTextRequest
{
    public Guid TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class SourceResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Domain { get; set; }
    public string? Author { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid? OriginalFileId { get; set; }
    public string? ContentHash { get; set; }
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SourceListItem
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Domain { get; set; }
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime ImportedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SourceDetail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Domain { get; set; }
    public string? Author { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid? OriginalFileId { get; set; }
    public string? RawText { get; set; }
    public string? ContentHash { get; set; }
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
