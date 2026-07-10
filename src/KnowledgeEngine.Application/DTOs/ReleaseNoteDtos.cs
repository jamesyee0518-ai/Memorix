using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== ReleaseNote Requests =====

public class CreateReleaseNoteRequest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "alpha";

    [JsonPropertyName("content_markdown")]
    public string ContentMarkdown { get; set; } = string.Empty;

    [JsonPropertyName("highlights")]
    public List<string>? Highlights { get; set; }

    [JsonPropertyName("known_issues")]
    public List<string>? KnownIssues { get; set; }
}

public class UpdateReleaseNoteRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content_markdown")]
    public string? ContentMarkdown { get; set; }

    [JsonPropertyName("highlights")]
    public List<string>? Highlights { get; set; }

    [JsonPropertyName("known_issues")]
    public List<string>? KnownIssues { get; set; }

    [JsonPropertyName("is_published")]
    public bool? IsPublished { get; set; }
}

// ===== ReleaseNote Responses =====

public class ReleaseNoteResponse
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = "alpha";
    public string ContentMarkdown { get; set; } = string.Empty;
    public List<string>? Highlights { get; set; }
    public List<string>? KnownIssues { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ReleaseNoteListItem
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = "alpha";
    public List<string>? Highlights { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
