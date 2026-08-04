using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Meeting lifecycle management API.
/// Create meetings, manage speakers, recording, transcription, minutes, and action items.
/// </summary>
[ApiController]
[Route("api/v1/meetings")]
[Authorize]
public class MeetingController : BaseController
{
    private readonly IMeetingService _meetingService;
    private readonly IRecordingService _recordingService;
    private readonly IMeetingPublishingService _publishingService;
    private readonly IMeetingProcessingService _processingService;
    private readonly ILogger<MeetingController> _logger;

    public MeetingController(
        IMeetingService meetingService,
        IRecordingService recordingService,
        IMeetingPublishingService publishingService,
        IMeetingProcessingService processingService,
        ILogger<MeetingController> logger)
    {
        _meetingService = meetingService;
        _recordingService = recordingService;
        _publishingService = publishingService;
        _processingService = processingService;
        _logger = logger;
    }

    // ── Meeting CRUD ──

    /// <summary>Create a new meeting.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMeetingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required." });

        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();

        var meeting = await _meetingService.CreateAsync(request, userId, ct);
        return CreatedAtAction(nameof(Get), new { meetingId = meeting.Id }, meeting);
    }

    /// <summary>Get meeting details.</summary>
    [HttpGet("{meetingId:guid}")]
    public async Task<IActionResult> Get(Guid meetingId, CancellationToken ct)
    {
        var meeting = await _meetingService.GetAsync(meetingId, ct);
        return meeting == null ? NotFound() : Ok(meeting);
    }

    /// <summary>Update meeting.</summary>
    [HttpPatch("{meetingId:guid}")]
    public async Task<IActionResult> Update(Guid meetingId, [FromBody] UpdateMeetingRequest request, CancellationToken ct)
    {
        var meeting = await _meetingService.UpdateAsync(meetingId, request, ct);
        return meeting == null ? NotFound() : Ok(meeting);
    }

    /// <summary>Finish a meeting (set status to COMPLETED).</summary>
    [HttpPost("{meetingId:guid}/finish")]
    public async Task<IActionResult> Finish(Guid meetingId, CancellationToken ct)
    {
        var ok = await _meetingService.FinishAsync(meetingId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Delete a meeting (soft delete).</summary>
    [HttpDelete("{meetingId:guid}")]
    public async Task<IActionResult> Delete(Guid meetingId, CancellationToken ct)
    {
        var ok = await _meetingService.DeleteAsync(meetingId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>List meetings.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var meetings = await _meetingService.ListAsync(workspaceId, limit, offset, ct);
        return Ok(meetings);
    }

    // ── Speaker management ──

    /// <summary>Get all speakers for a meeting.</summary>
    [HttpGet("{meetingId:guid}/speakers")]
    public async Task<IActionResult> GetSpeakers(Guid meetingId, CancellationToken ct)
    {
        var speakers = await _meetingService.GetSpeakersAsync(meetingId, ct);
        return Ok(speakers);
    }

    /// <summary>Update a speaker (name, identity status).</summary>
    [HttpPatch("{meetingId:guid}/speakers/{speakerId:guid}")]
    public async Task<IActionResult> UpdateSpeaker(
        Guid meetingId, Guid speakerId,
        [FromBody] UpdateSpeakerRequest request, CancellationToken ct)
    {
        var speaker = await _meetingService.UpdateSpeakerAsync(speakerId, request, ct);
        return speaker == null ? NotFound() : Ok(speaker);
    }

    /// <summary>Merge multiple speakers into one.</summary>
    [HttpPost("{meetingId:guid}/speakers/merge")]
    public async Task<IActionResult> MergeSpeakers(
        Guid meetingId, [FromBody] MergeSpeakersRequest request, CancellationToken ct)
    {
        var ok = await _meetingService.MergeSpeakersAsync(meetingId, request, ct);
        return ok ? NoContent() : NotFound();
    }

    // ── Minutes management ──

    /// <summary>Get all minutes versions for a meeting.</summary>
    [HttpGet("{meetingId:guid}/minutes")]
    public async Task<IActionResult> GetMinutes(Guid meetingId, CancellationToken ct)
    {
        var minutes = await _meetingService.GetMinutesAsync(meetingId, ct);
        return Ok(minutes);
    }

    /// <summary>Set the official minutes version for a meeting.</summary>
    [HttpPost("{meetingId:guid}/minutes/{minutesId:guid}/set-official")]
    public async Task<IActionResult> SetOfficialMinutes(Guid meetingId, Guid minutesId, CancellationToken ct)
    {
        var minutes = await _meetingService.SetOfficialMinutesAsync(meetingId, minutesId, ct);
        return minutes == null ? NotFound() : Ok(minutes);
    }

    // ── Action items ──

    /// <summary>Get all action items for a meeting.</summary>
    [HttpGet("{meetingId:guid}/action-items")]
    public async Task<IActionResult> GetActionItems(Guid meetingId, CancellationToken ct)
    {
        var items = await _meetingService.GetActionItemsAsync(meetingId, ct);
        return Ok(items);
    }

    /// <summary>Confirm, modify, or ignore an action item.</summary>
    [HttpPost("action-items/{actionItemId:guid}/confirm")]
    public async Task<IActionResult> ConfirmActionItem(
        Guid actionItemId, [FromBody] ConfirmActionItemRequest request, CancellationToken ct)
    {
        var item = await _meetingService.ConfirmActionItemAsync(actionItemId, request, ct);
        return item == null ? NotFound() : Ok(item);
    }

    // ── Helpers ──

    // ══════════════════════════════════════════════════════════════════════
    //  Recording control (§12.2)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Start recording for a meeting.</summary>
    [HttpPost("{meetingId:guid}/recording/start")]
    public async Task<IActionResult> StartRecording(
        Guid meetingId, [FromBody] StartRecordingRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();

        request ??= new StartRecordingRequest();
        var session = await _recordingService.StartAsync(meetingId, request, userId, ct);
        return Ok(session);
    }

    /// <summary>Pause recording.</summary>
    [HttpPost("{meetingId:guid}/recording/pause")]
    public async Task<IActionResult> PauseRecording(Guid meetingId, CancellationToken ct)
    {
        var session = await _recordingService.PauseAsync(meetingId, ct);
        return Ok(session);
    }

    /// <summary>Resume recording after pause.</summary>
    [HttpPost("{meetingId:guid}/recording/resume")]
    public async Task<IActionResult> ResumeRecording(Guid meetingId, CancellationToken ct)
    {
        var session = await _recordingService.ResumeAsync(meetingId, ct);
        return Ok(session);
    }

    /// <summary>Stop recording and finalize the meeting.</summary>
    [HttpPost("{meetingId:guid}/recording/stop")]
    public async Task<IActionResult> StopRecording(Guid meetingId, CancellationToken ct)
    {
        var session = await _recordingService.StopAsync(meetingId, ct);
        return Ok(session);
    }

    /// <summary>Get current recording status.</summary>
    [HttpGet("{meetingId:guid}/recording/status")]
    public async Task<IActionResult> GetRecordingStatus(Guid meetingId, CancellationToken ct)
    {
        var session = await _recordingService.GetStatusAsync(meetingId, ct);
        return session == null ? NotFound() : Ok(session);
    }

    /// <summary>List all recording chunks for a meeting.</summary>
    [HttpGet("{meetingId:guid}/recording/chunks")]
    public async Task<IActionResult> GetRecordingChunks(Guid meetingId, CancellationToken ct)
    {
        var chunks = await _recordingService.GetChunksAsync(meetingId, ct);
        return Ok(chunks);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Asset upload (§12.2)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Upload an audio or video file for a meeting.</summary>
    [HttpPost("{meetingId:guid}/assets")]
    [RequestSizeLimit(2_000_000_000)] // 2 GB
    public async Task<IActionResult> UploadAsset(Guid meetingId, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();

        using var stream = file.OpenReadStream();
        var asset = await _meetingService.UploadAssetAsync(
            meetingId, stream, file.FileName, file.ContentType, file.Length, userId, ct);
        return CreatedAtAction(nameof(GetAssets), new { meetingId }, asset);
    }

    /// <summary>List all assets for a meeting.</summary>
    [HttpGet("{meetingId:guid}/assets")]
    public async Task<IActionResult> GetAssets(Guid meetingId, CancellationToken ct)
    {
        var assets = await _meetingService.GetAssetsAsync(meetingId, ct);
        return Ok(assets);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Transcription (§12.3)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Trigger transcription for a meeting.</summary>
    [HttpPost("{meetingId:guid}/transcriptions")]
    public async Task<IActionResult> CreateTranscription(
        Guid meetingId, [FromBody] CreateTranscriptionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();

        try
        {
            var transcript = await _meetingService.TriggerTranscriptionAsync(meetingId, request, userId, ct);
            return CreatedAtAction(nameof(GetTranscript), new { transcriptId = transcript.Id }, transcript);
        }
        catch (KnowledgeEngine.Application.Exceptions.NotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>List all transcripts for a meeting.</summary>
    [HttpGet("{meetingId:guid}/transcripts")]
    public async Task<IActionResult> GetTranscripts(Guid meetingId, CancellationToken ct)
    {
        var transcripts = await _meetingService.GetTranscriptsAsync(meetingId, ct);
        return Ok(transcripts);
    }

    /// <summary>Get a specific transcript with segments.</summary>
    [HttpGet("/api/v1/transcripts/{transcriptId:guid}")]
    public async Task<IActionResult> GetTranscript(Guid transcriptId, CancellationToken ct)
    {
        var transcript = await _meetingService.GetTranscriptAsync(transcriptId, ct);
        return transcript == null ? NotFound() : Ok(transcript);
    }

    /// <summary>Update a transcript segment (text or speaker).</summary>
    [HttpPatch("/api/v1/transcript-segments/{segmentId:guid}")]
    public async Task<IActionResult> UpdateSegment(
        Guid segmentId, [FromBody] UpdateSegmentRequest request, CancellationToken ct)
    {
        var segment = await _meetingService.UpdateSegmentAsync(segmentId, request, ct);
        return segment == null ? NotFound() : Ok(segment);
    }

    /// <summary>Set the official transcript version for a meeting.</summary>
    [HttpPost("{meetingId:guid}/transcripts/{transcriptId:guid}/set-official")]
    public async Task<IActionResult> SetOfficialTranscript(
        Guid meetingId, Guid transcriptId, CancellationToken ct)
    {
        var ok = await _meetingService.SetOfficialTranscriptAsync(meetingId, transcriptId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Trigger reprocessing of a meeting.</summary>
    [HttpPost("{meetingId:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid meetingId, CancellationToken ct)
    {
        var ok = await _meetingService.ReprocessAsync(meetingId, ct);
        return ok ? NoContent() : NotFound();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Speaker split (§12.4)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Split a speaker into two (reassign segments).</summary>
    [HttpPost("{meetingId:guid}/speakers/split")]
    public async Task<IActionResult> SplitSpeaker(
        Guid meetingId, [FromBody] SplitSpeakerRequest request, CancellationToken ct)
    {
        var ok = await _meetingService.SplitSpeakerAsync(meetingId, request, ct);
        return ok ? NoContent() : NotFound();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Minutes generation (§12.5)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Generate meeting minutes from transcript using LLM.</summary>
    [HttpPost("{meetingId:guid}/minutes/generate")]
    public async Task<IActionResult> GenerateMinutes(
        Guid meetingId, [FromBody] GenerateMinutesRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();

        request ??= new GenerateMinutesRequest();
        var minutes = await _meetingService.GenerateMinutesAsync(meetingId, request, userId, ct);
        return minutes == null
            ? NotFound(new { error = "No transcript segments found for this meeting." })
            : Ok(minutes);
    }

    /// <summary>Update minutes content.</summary>
    [HttpPatch("/api/v1/minutes/{minutesId:guid}")]
    public async Task<IActionResult> UpdateMinutes(
        Guid minutesId, [FromBody] UpdateMinutesRequest request, CancellationToken ct)
    {
        var minutes = await _meetingService.UpdateMinutesAsync(minutesId, request, ct);
        return minutes == null ? NotFound() : Ok(minutes);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Action item additional operations (§12.6)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Create a task from an action item.</summary>
    [HttpPost("/api/v1/action-items/{actionItemId:guid}/create-task")]
    public async Task<IActionResult> CreateTaskFromActionItem(Guid actionItemId, CancellationToken ct)
    {
        var item = await _meetingService.CreateTaskFromActionItemAsync(actionItemId, ct);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Batch confirm multiple action items.</summary>
    [HttpPost("/api/v1/action-items/batch-confirm")]
    public async Task<IActionResult> BatchConfirmActionItems(
        [FromBody] BatchConfirmActionItemsRequest request, CancellationToken ct)
    {
        var items = await _meetingService.BatchConfirmActionItemsAsync(request, ct);
        return Ok(items);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Recording recovery
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Scan and recover meetings left in incomplete recording state.</summary>
    [HttpPost("recording/recover")]
    public async Task<IActionResult> RecoverIncompleteRecordings(CancellationToken ct)
    {
        var results = await _recordingService.RecoverIncompleteMeetingsAsync(ct);
        return Ok(results);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Knowledge base publishing (P2)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Publish meeting minutes to the knowledge base.</summary>
    [HttpPost("{meetingId:guid}/minutes/{minutesId:guid}/publish")]
    public async Task<IActionResult> PublishMinutes(Guid meetingId, Guid minutesId, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        var result = await _publishingService.PublishMinutesAsync(meetingId, minutesId, userId, ct);
        return Ok(result);
    }

    /// <summary>Publish transcript to the knowledge base.</summary>
    [HttpPost("{meetingId:guid}/transcripts/{transcriptId:guid}/publish")]
    public async Task<IActionResult> PublishTranscript(Guid meetingId, Guid transcriptId, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        var result = await _publishingService.PublishTranscriptAsync(meetingId, transcriptId, userId, ct);
        return Ok(result);
    }

    /// <summary>Publish confirmed action items to the knowledge base.</summary>
    [HttpPost("{meetingId:guid}/action-items/publish")]
    public async Task<IActionResult> PublishActionItems(Guid meetingId, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        var result = await _publishingService.PublishActionItemsAsync(meetingId, userId, ct);
        return Ok(result);
    }

    /// <summary>Publish all meeting results (minutes, transcript, action items) to the knowledge base.</summary>
    [HttpPost("{meetingId:guid}/publish")]
    public async Task<IActionResult> PublishAll(Guid meetingId, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        var result = await _publishingService.PublishAllAsync(meetingId, userId, ct);
        return Ok(result);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Processing pipeline (§14 - async task orchestration)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Create the processing pipeline tasks for a meeting's audio asset.</summary>
    [HttpPost("{meetingId:guid}/processing/pipeline")]
    public async Task<IActionResult> CreateProcessingPipeline(
        Guid meetingId, [FromBody] CreateProcessingPipelineRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        if (request.AudioAssetId == Guid.Empty)
            return BadRequest(new { error = "AudioAssetId is required." });
        var tasks = await _processingService.CreatePipelineAsync(meetingId, request.AudioAssetId, userId, ct);
        return Ok(tasks);
    }

    /// <summary>Execute the processing pipeline for a meeting.</summary>
    [HttpPost("{meetingId:guid}/processing/execute")]
    public async Task<IActionResult> ExecuteProcessingPipeline(Guid meetingId, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        var result = await _processingService.ExecutePipelineAsync(meetingId, userId, ct);
        return Ok(result);
    }

    /// <summary>Get the processing pipeline status for a meeting.</summary>
    [HttpGet("{meetingId:guid}/processing/status")]
    public async Task<IActionResult> GetProcessingStatus(Guid meetingId, CancellationToken ct)
    {
        var result = await _processingService.GetPipelineProgressAsync(meetingId, ct);
        return Ok(result);
    }

    /// <summary>Get individual task statuses for a meeting.</summary>
    [HttpGet("{meetingId:guid}/processing/tasks")]
    public async Task<IActionResult> GetProcessingTasks(Guid meetingId, CancellationToken ct)
    {
        var tasks = await _processingService.GetTaskStatusAsync(meetingId, ct);
        return Ok(tasks);
    }

    /// <summary>Retry a failed processing task.</summary>
    [HttpPost("processing/tasks/{taskId:guid}/retry")]
    public async Task<IActionResult> RetryTask(Guid taskId, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        var success = await _processingService.RetryTaskAsync(taskId, userId, ct);
        return success ? Ok(new { status = "retried" }) : BadRequest(new { error = "Max retries exceeded or task not found." });
    }

    /// <summary>Cancel all processing tasks for a meeting.</summary>
    [HttpPost("{meetingId:guid}/processing/cancel")]
    public async Task<IActionResult> CancelProcessing(Guid meetingId, CancellationToken ct)
    {
        var userId = GetCurrentUserOr401();
        if (userId == Guid.Empty) return Unauthorized();
        var success = await _processingService.CancelPipelineAsync(meetingId, userId, ct);
        return success ? Ok(new { status = "canceled" }) : NotFound();
    }

    // ── Helpers ──

    private Guid GetCurrentUserOr401()
    {
        return User.FindFirst("sub")?.Value is { } subStr && Guid.TryParse(subStr, out var uid)
            ? uid
            : Guid.Empty;
    }
}
