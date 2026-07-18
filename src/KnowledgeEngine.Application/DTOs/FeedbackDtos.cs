using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== Feedback Requests =====

public class CreateFeedbackRequest
{
    [JsonPropertyName("feedback_type")]
    public string FeedbackType { get; set; } = string.Empty;

    [JsonPropertyName("module")]
    public string? Module { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("related_entity_type")]
    public string? RelatedEntityType { get; set; }

    [JsonPropertyName("related_entity_id")]
    public Guid? RelatedEntityId { get; set; }
}

// ===== Feedback Responses =====

public class FeedbackResponse
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string FeedbackType { get; set; } = string.Empty;
    public string? Module { get; set; }
    public string? Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string Status { get; set; } = "open";
    public string Priority { get; set; } = "medium";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FeedbackListItem
{
    public Guid Id { get; set; }
    public string FeedbackType { get; set; } = string.Empty;
    public string? Module { get; set; }
    public string? Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public string Priority { get; set; } = "medium";
    public DateTime CreatedAt { get; set; }
}

// ===== Feedback Admin Requests =====

public class UpdateFeedbackRequest
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }
}

// ===== Feedback Stats Responses =====

public class FeedbackStatsResponse
{
    public int TotalCount { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
    public Dictionary<string, int> BySeverity { get; set; } = new();
    public Dictionary<string, int> ByStatus { get; set; } = new();
}
