using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Publishes confirmed meeting minutes, transcripts, and action items
/// into the Memorix knowledge base as searchable sources and documents.
/// </summary>
public interface IMeetingPublishingService
{
    /// <summary>Publish a specific minutes version to the knowledge base.</summary>
    Task<PublishResultDto> PublishMinutesAsync(Guid meetingId, Guid minutesId, Guid userId, CancellationToken ct);

    /// <summary>Publish a specific transcript version to the knowledge base.</summary>
    Task<PublishResultDto> PublishTranscriptAsync(Guid meetingId, Guid transcriptId, Guid userId, CancellationToken ct);

    /// <summary>Publish confirmed action items for a meeting to the knowledge base.</summary>
    Task<PublishResultDto> PublishActionItemsAsync(Guid meetingId, Guid userId, CancellationToken ct);

    /// <summary>Publish all meeting artifacts (minutes, transcript, action items).</summary>
    Task<PublishResultDto> PublishAllAsync(Guid meetingId, Guid userId, CancellationToken ct);
}

/// <summary>Result of a publishing operation.</summary>
public class PublishResultDto
{
    public Guid MeetingId { get; set; }
    public string Status { get; set; } = "published";
    public string? SourceId { get; set; }
    public string? DocumentId { get; set; }
    public int TasksCreated { get; set; }
    public string? Message { get; set; }
}
