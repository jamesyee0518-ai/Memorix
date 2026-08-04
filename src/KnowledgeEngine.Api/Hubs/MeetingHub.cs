using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Api.Hubs;

/// <summary>
/// SignalR hub for real-time meeting audio streaming and live transcript updates (§12.7).
/// Consolidates the WebSocket endpoints specified by the development document into a single
/// hub connection per client:
///   - /ws/v1/meetings/{meetingId}/audio      → <see cref="SendAudioChunk"/> / "AudioChunk" event
///   - /ws/v1/meetings/{meetingId}/transcript  → "TranscriptUpdate" event (<see cref="TranscriptUpdateEvent"/>)
///   - /ws/v1/jobs/{jobId}/progress            → handled by <see cref="JobProgressHub"/>
/// Clients join a meeting group to receive broadcasts; the recording client streams audio
/// chunks, and server-side transcription services push interim/final transcript results to
/// all meeting participants via <c>IHubContext&lt;MeetingHub&gt;</c>.
/// </summary>
[Authorize]
public class MeetingHub : Hub
{
    private readonly ILogger<MeetingHub> _logger;

    public MeetingHub(ILogger<MeetingHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects to the hub. Rejects connections without a user identity.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("MeetingHub: connection rejected - no user identity ({ConnectionId})",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        _logger.LogInformation("MeetingHub: client connected {ConnectionId} for user {UserId}",
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    /// <inheritdoc/>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("MeetingHub: client disconnected {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Adds the calling client to the meeting group so it receives audio and
    /// transcript broadcasts for the given meeting.
    /// </summary>
    public async Task JoinMeeting(Guid meetingId)
    {
        var groupName = MeetingGroup(meetingId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation("MeetingHub: {ConnectionId} joined meeting {MeetingId}",
            Context.ConnectionId, meetingId);

        await Clients.Group(groupName).SendAsync("MeetingJoined", new
        {
            meetingId,
            connectionId = Context.ConnectionId
        });
    }

    /// <summary>
    /// Removes the calling client from the meeting group.
    /// </summary>
    public async Task LeaveMeeting(Guid meetingId)
    {
        var groupName = MeetingGroup(meetingId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation("MeetingHub: {ConnectionId} left meeting {MeetingId}",
            Context.ConnectionId, meetingId);

        await Clients.Group(groupName).SendAsync("MeetingLeft", new
        {
            meetingId,
            connectionId = Context.ConnectionId
        });
    }

    /// <summary>
    /// Receives an audio chunk from the recording client and broadcasts it to all
    /// meeting participants. Server-side transcription services consume the stream
    /// and push back <see cref="TranscriptUpdateEvent"/> messages (via
    /// <c>IHubContext&lt;MeetingHub&gt;</c>) with event names "transcript.interim"
    /// or "transcript.final".
    /// </summary>
    public async Task SendAudioChunk(MeetingAudioChunk chunk)
    {
        if (chunk == null || chunk.MeetingId == Guid.Empty)
        {
            _logger.LogWarning("MeetingHub: SendAudioChunk rejected - invalid chunk ({ConnectionId})",
                Context.ConnectionId);
            return;
        }

        var groupName = MeetingGroup(chunk.MeetingId);

        _logger.LogDebug(
            "MeetingHub: audio chunk #{SequenceNo} ({Bytes} bytes, format={Format}, sampleRate={SampleRate}, isFinal={IsFinal}) for meeting {MeetingId}",
            chunk.SequenceNo, chunk.Data?.Length ?? 0, chunk.Format, chunk.SampleRate, chunk.IsFinal, chunk.MeetingId);

        // Broadcast the audio chunk to all meeting participants.
        await Clients.Group(groupName).SendAsync("AudioChunk", chunk);
    }

    /// <summary>
    /// Builds the SignalR group name for a meeting: <c>meeting_{meetingId}</c>.
    /// </summary>
    public static string MeetingGroup(Guid meetingId) => $"meeting_{meetingId}";
}

/// <summary>
/// A chunk of meeting audio streamed from the recording client.
/// Maps to the /ws/v1/meetings/{meetingId}/audio endpoint payload.
/// </summary>
public class MeetingAudioChunk
{
    /// <summary>Identifier of the meeting this audio belongs to.</summary>
    public Guid MeetingId { get; set; }

    /// <summary>Monotonic sequence number of the chunk within the meeting stream.</summary>
    public int SequenceNo { get; set; }

    /// <summary>Raw audio bytes for this chunk.</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>Audio format, e.g. "pcm_s16le".</summary>
    public string Format { get; set; } = "pcm_s16le";

    /// <summary>Sample rate in Hz.</summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>Indicates whether this is the final chunk of the stream.</summary>
    public bool IsFinal { get; set; }
}

/// <summary>
/// A live transcript update pushed to meeting participants.
/// Maps to the /ws/v1/meetings/{meetingId}/transcript endpoint payload.
/// </summary>
public class TranscriptUpdateEvent
{
    /// <summary>Identifier of the meeting this transcript update belongs to.</summary>
    public Guid MeetingId { get; set; }

    /// <summary>Event type, e.g. "transcript.interim" or "transcript.final".</summary>
    public string Event { get; set; } = "transcript.interim";

    /// <summary>Monotonic sequence number of the transcript segment.</summary>
    public int Sequence { get; set; }

    /// <summary>Start offset of the segment in milliseconds.</summary>
    public long StartMs { get; set; }

    /// <summary>End offset of the segment in milliseconds.</summary>
    public long EndMs { get; set; }

    /// <summary>Transcribed text for the segment.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Whether this is a finalized (non-interim) transcript segment.</summary>
    public bool IsFinal { get; set; }

    /// <summary>Optional speaker identifier from diarization.</summary>
    public string? SpeakerKey { get; set; }

    /// <summary>Optional human-readable speaker display name.</summary>
    public string? SpeakerDisplayName { get; set; }
}
