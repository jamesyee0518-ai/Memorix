using System.Text;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Publishes confirmed meeting minutes, transcripts, and action items
/// into the Memorix knowledge base as searchable sources and documents.
/// </summary>
public class MeetingPublishingService : IMeetingPublishingService
{
    private readonly IAppDbContext _db;
    private readonly IKnowledgeRepository _knowledgeRepo;
    private readonly ILogger<MeetingPublishingService> _logger;
    private readonly ICurrentUserContext _currentUser;

    public MeetingPublishingService(
        IAppDbContext db,
        IKnowledgeRepository knowledgeRepo,
        ILogger<MeetingPublishingService> logger,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _knowledgeRepo = knowledgeRepo;
        _logger = logger;
        _currentUser = currentUser;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Publish meeting minutes
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<PublishResultDto> PublishMinutesAsync(
        Guid meetingId, Guid minutesId, Guid userId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting == null)
        {
            _logger.LogWarning("PublishMinutes: meeting {MeetingId} not found", meetingId);
            return new PublishResultDto
            {
                MeetingId = meetingId,
                Status = "not_found",
                Message = "Meeting not found."
            };
        }

        var minutes = await _db.MeetingMinutesVersions
            .FirstOrDefaultAsync(v => v.Id == minutesId && v.MeetingId == meetingId, ct);
        if (minutes == null)
        {
            _logger.LogWarning("PublishMinutes: minutes {MinutesId} not found for meeting {MeetingId}", minutesId, meetingId);
            return new PublishResultDto
            {
                MeetingId = meetingId,
                Status = "not_found",
                Message = "Minutes version not found."
            };
        }

        var workspaceId = meeting.WorkspaceId?.ToString() ?? string.Empty;
        var topicId = meeting.TopicId?.ToString();
        var author = ResolveAuthor(userId);

        // Build markdown content
        var sb = new StringBuilder();
        sb.AppendLine($"# {meeting.Title} - 会议纪要");
        sb.AppendLine($"**日期**: {meeting.CreatedAt:yyyy-MM-dd}");
        sb.AppendLine($"**摘要**: {minutes.Summary}");
        sb.AppendLine();
        sb.AppendLine("## 结构化内容");
        sb.AppendLine(minutes.ContentJson);
        var markdown = sb.ToString();

        // Create source
        var source = await _knowledgeRepo.CreateSourceAsync(new CreateSourceInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            SourceType = "meeting_minutes",
            Title = $"{meeting.Title} - 会议纪要",
            Author = author,
            ContentHash = null
        }, ct);

        // Create document
        var document = await _knowledgeRepo.CreateDocumentAsync(new CreateDocumentInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            SourceId = source.Id,
            Title = $"{meeting.Title} - 会议纪要",
            ContentMarkdown = markdown,
            ContentText = markdown
        }, ct);

        _logger.LogInformation(
            "Published minutes {MinutesId} for meeting {MeetingId} → source {SourceId}, document {DocumentId}",
            minutesId, meetingId, source.Id, document.Id);

        return new PublishResultDto
        {
            MeetingId = meetingId,
            Status = "published",
            SourceId = source.Id,
            DocumentId = document.Id,
            TasksCreated = 0,
            Message = "Meeting minutes published to knowledge base."
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Publish transcript
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<PublishResultDto> PublishTranscriptAsync(
        Guid meetingId, Guid transcriptId, Guid userId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting == null)
        {
            _logger.LogWarning("PublishTranscript: meeting {MeetingId} not found", meetingId);
            return new PublishResultDto
            {
                MeetingId = meetingId,
                Status = "not_found",
                Message = "Meeting not found."
            };
        }

        var segments = await _db.TranscriptionSegments
            .Where(s => s.TranscriptionJobId == transcriptId)
            .OrderBy(s => s.SegmentIndex)
            .ToListAsync(ct);

        if (segments.Count == 0)
        {
            _logger.LogWarning("PublishTranscript: no segments found for transcript {TranscriptId}", transcriptId);
            return new PublishResultDto
            {
                MeetingId = meetingId,
                Status = "not_found",
                Message = "No transcript segments found."
            };
        }

        // Resolve speaker display names for the meeting
        var speakers = await _db.MeetingSpeakers
            .Where(s => s.MeetingId == meetingId)
            .ToDictionaryAsync(s => s.SpeakerKey ?? string.Empty, s => s.DisplayName, ct);

        var workspaceId = meeting.WorkspaceId?.ToString() ?? string.Empty;
        var topicId = meeting.TopicId?.ToString();
        var author = ResolveAuthor(userId);

        // Build markdown content
        var sb = new StringBuilder();
        sb.AppendLine("## 转写记录");
        foreach (var seg in segments)
        {
            var speaker = seg.SpeakerKey != null
                          && speakers.TryGetValue(seg.SpeakerKey, out var name)
                          && !string.IsNullOrEmpty(name)
                ? name
                : seg.SpeakerKey ?? "未知";
            var timestamp = TimeSpan.FromMilliseconds(seg.SourceStartMs).ToString(@"hh\:mm\:ss");
            sb.AppendLine($"[{timestamp}] {speaker}: {seg.Text}");
        }
        var markdown = sb.ToString();

        // Create source
        var source = await _knowledgeRepo.CreateSourceAsync(new CreateSourceInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            SourceType = "meeting_transcript",
            Title = $"{meeting.Title} - 转写记录",
            Author = author,
            ContentHash = null
        }, ct);

        // Create document
        var document = await _knowledgeRepo.CreateDocumentAsync(new CreateDocumentInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            SourceId = source.Id,
            Title = $"{meeting.Title} - 转写记录",
            ContentMarkdown = markdown,
            ContentText = markdown
        }, ct);

        _logger.LogInformation(
            "Published transcript {TranscriptId} for meeting {MeetingId} → source {SourceId}, document {DocumentId}",
            transcriptId, meetingId, source.Id, document.Id);

        return new PublishResultDto
        {
            MeetingId = meetingId,
            Status = "published",
            SourceId = source.Id,
            DocumentId = document.Id,
            TasksCreated = 0,
            Message = "Meeting transcript published to knowledge base."
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Publish action items
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<PublishResultDto> PublishActionItemsAsync(
        Guid meetingId, Guid userId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting == null)
        {
            _logger.LogWarning("PublishActionItems: meeting {MeetingId} not found", meetingId);
            return new PublishResultDto
            {
                MeetingId = meetingId,
                Status = "not_found",
                Message = "Meeting not found."
            };
        }

        // Load confirmed action items (CONFIRMED, MODIFIED, CONVERTED_TO_TASK)
        var actionItems = await _db.ActionItems
            .Where(a => a.MeetingId == meetingId
                        && a.ConfirmationStatus != ActionItemConfirmationStatuses.PendingConfirmation
                        && a.ConfirmationStatus != ActionItemConfirmationStatuses.Ignored)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        if (actionItems.Count == 0)
        {
            _logger.LogWarning("PublishActionItems: no confirmed action items for meeting {MeetingId}", meetingId);
            return new PublishResultDto
            {
                MeetingId = meetingId,
                Status = "not_found",
                Message = "No confirmed action items found."
            };
        }

        var workspaceId = meeting.WorkspaceId?.ToString() ?? string.Empty;
        var topicId = meeting.TopicId?.ToString();
        var author = ResolveAuthor(userId);

        // Build markdown content
        var sb = new StringBuilder();
        sb.AppendLine("## 行动项");
        foreach (var item in actionItems)
        {
            var owner = item.OwnerText ?? "未指定";
            var dueDate = item.DueDate?.ToString("yyyy-MM-dd") ?? "未指定";
            sb.AppendLine($"- [ ] {item.TaskText} (负责人: {owner}, 截止: {dueDate}, 优先级: {item.Priority})");
        }
        var markdown = sb.ToString();

        // Create source
        var source = await _knowledgeRepo.CreateSourceAsync(new CreateSourceInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            SourceType = "meeting_action_items",
            Title = $"{meeting.Title} - 行动项",
            Author = author,
            ContentHash = null
        }, ct);

        // Create document
        var document = await _knowledgeRepo.CreateDocumentAsync(new CreateDocumentInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            SourceId = source.Id,
            Title = $"{meeting.Title} - 行动项",
            ContentMarkdown = markdown,
            ContentText = markdown
        }, ct);

        _logger.LogInformation(
            "Published {Count} action items for meeting {MeetingId} → source {SourceId}, document {DocumentId}",
            actionItems.Count, meetingId, source.Id, document.Id);

        return new PublishResultDto
        {
            MeetingId = meetingId,
            Status = "published",
            SourceId = source.Id,
            DocumentId = document.Id,
            TasksCreated = actionItems.Count,
            Message = $"{actionItems.Count} action items published to knowledge base."
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Publish all
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<PublishResultDto> PublishAllAsync(
        Guid meetingId, Guid userId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting == null)
        {
            _logger.LogWarning("PublishAll: meeting {MeetingId} not found", meetingId);
            return new PublishResultDto
            {
                MeetingId = meetingId,
                Status = "not_found",
                Message = "Meeting not found."
            };
        }

        var results = new List<PublishResultDto>();
        var messages = new List<string>();
        var totalTasks = 0;

        // Publish minutes if an official version exists
        if (meeting.OfficialMinutesVersionId.HasValue)
        {
            var minutesResult = await PublishMinutesAsync(
                meetingId, meeting.OfficialMinutesVersionId.Value, userId, ct);
            results.Add(minutesResult);
            if (minutesResult.Status == "published")
                messages.Add("minutes published");
            totalTasks += minutesResult.TasksCreated;
        }

        // Publish transcript if an official version exists
        if (meeting.OfficialTranscriptVersionId.HasValue)
        {
            var transcriptResult = await PublishTranscriptAsync(
                meetingId, meeting.OfficialTranscriptVersionId.Value, userId, ct);
            results.Add(transcriptResult);
            if (transcriptResult.Status == "published")
                messages.Add("transcript published");
            totalTasks += transcriptResult.TasksCreated;
        }

        // Publish action items
        var actionResult = await PublishActionItemsAsync(meetingId, userId, ct);
        results.Add(actionResult);
        if (actionResult.Status == "published")
            messages.Add("action items published");
        totalTasks += actionResult.TasksCreated;

        var publishedCount = results.Count(r => r.Status == "published");
        var lastSourceId = results.LastOrDefault(r => r.SourceId != null)?.SourceId;
        var lastDocumentId = results.LastOrDefault(r => r.DocumentId != null)?.DocumentId;

        _logger.LogInformation(
            "PublishAll completed for meeting {MeetingId}: {Published}/{Total} artifacts published",
            meetingId, publishedCount, results.Count);

        return new PublishResultDto
        {
            MeetingId = meetingId,
            Status = publishedCount > 0 ? "published" : "failed",
            SourceId = lastSourceId,
            DocumentId = lastDocumentId,
            TasksCreated = totalTasks,
            Message = $"Published {publishedCount}/{results.Count} artifacts: {string.Join(", ", messages)}."
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════

    private string ResolveAuthor(Guid userId)
    {
        // Prefer the current user context email if available; fall back to user id
        if (_currentUser.IsAuthenticated && !string.IsNullOrEmpty(_currentUser.Email))
            return _currentUser.Email;
        return userId.ToString();
    }
}
