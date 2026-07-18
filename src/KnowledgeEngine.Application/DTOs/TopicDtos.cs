namespace KnowledgeEngine.Application.DTOs;

public class CreateTopicRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Domain { get; set; }
    public string Visibility { get; set; } = "private";
}

public class UpdateTopicRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Domain { get; set; }
    public string? Visibility { get; set; }
}

public class TopicResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Domain { get; set; }
    public string Visibility { get; set; } = "private";
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TopicListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Domain { get; set; }
    public string Visibility { get; set; } = "private";
    public string Status { get; set; } = "active";
    public int DocumentCount { get; set; }
    public int PendingCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TopicStats
{
    public int DocumentCount { get; set; }
    public int PendingCount { get; set; }
    public int FailedCount { get; set; }
    public int DoneCount { get; set; }
    public int TotalCount { get; set; }
}

public class TopicDetail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Domain { get; set; }
    public string Visibility { get; set; } = "private";
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public TopicStats Stats { get; set; } = new();
}
