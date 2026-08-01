using System.Collections.Concurrent;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Api.Hubs;

/// <summary>
/// WebSocket hub for real-time streaming transcription.
/// Clients send audio chunks and receive partial/final transcription results.
/// </summary>
[Authorize]
public class TranscriptionHub : Hub
{
    private readonly IProviderRegistry _providerRegistry;
    private readonly IAudioPolicyRouter _policyRouter;
    private readonly ILogger<TranscriptionHub> _logger;

    // Per-connection session state: stores the audio buffer and resolved provider.
    private static readonly ConcurrentDictionary<string, StreamingSession> _sessions = new();

    public TranscriptionHub(
        IProviderRegistry providerRegistry,
        IAudioPolicyRouter policyRouter,
        ILogger<TranscriptionHub> logger)
    {
        _providerRegistry = providerRegistry;
        _policyRouter = policyRouter;
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst("sub")?.Value
                     ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("TranscriptionHub: connection rejected - no user identity");
            Context.Abort();
            return;
        }

        _logger.LogInformation("TranscriptionHub: client connected {ConnectionId} for user {UserId}",
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Starts a streaming transcription session.
    /// Client calls this to initialize the session with language and provider preferences.
    /// </summary>
    public async Task StartSession(StartSessionRequest request)
    {
        var sessionId = Context.ConnectionId;
        _logger.LogInformation("TranscriptionHub: starting session {SessionId}, language={Language}, provider={Provider}",
            sessionId, request.Language, request.PreferredProviderId);

        var routingContext = new AsrRoutingContext
        {
            Language = request.Language,
            EnableVad = false,
            EnablePunctuation = request.EnablePunctuation,
            EnableHotwords = request.Hotwords?.Count > 0,
            PreferredProviderId = request.PreferredProviderId,
            DataClassification = Domain.Enums.DataClassification.INTERNAL,
            FallbackPolicy = Domain.Enums.FallbackPolicies.Stop
        };

        try
        {
            var provider = await _policyRouter.ResolveAsrProviderAsync(routingContext, Context.ConnectionAborted);
            var descriptor = await provider.GetDescriptorAsync(Context.ConnectionAborted);

            // Create and store session state
            var session = new StreamingSession
            {
                Provider = provider,
                Descriptor = descriptor,
                Language = request.Language,
                EnablePunctuation = request.EnablePunctuation,
                Hotwords = request.Hotwords,
                SampleRate = request.SampleRate > 0 ? request.SampleRate : 16000,
                AudioBuffer = new MemoryStream(),
            };
            _sessions[sessionId] = session;

            if (descriptor.SupportsStreaming)
            {
                await Clients.Caller.SendAsync("SessionStarted", new
                {
                    sessionId,
                    providerId = descriptor.ProviderId,
                    modelId = descriptor.ModelId,
                    supportsStreaming = true
                });
            }
            else
            {
                // Provider doesn't support streaming — use batch mode with buffering
                await Clients.Caller.SendAsync("SessionStarted", new
                {
                    sessionId,
                    providerId = descriptor.ProviderId,
                    modelId = descriptor.ModelId,
                    supportsStreaming = false,
                    message = "Provider uses batch mode; audio will be transcribed when final chunk is received."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TranscriptionHub: failed to resolve provider for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("SessionError", new { message = ex.Message });
        }
    }

    /// <summary>
    /// Receives an audio chunk from the client for streaming transcription.
    /// Buffers audio data; when <see cref="AudioChunkMessage.IsFinal"/> is true,
    /// triggers transcription and returns results.
    /// </summary>
    public async Task SendAudioChunk(AudioChunkMessage chunk)
    {
        var sessionId = Context.ConnectionId;

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            await Clients.Caller.SendAsync("Error", new
            {
                message = "No active session. Call StartSession first."
            });
            return;
        }

        try
        {
            // Append audio data to the buffer
            if (chunk.Data.Length > 0)
            {
                session.AudioBuffer.Write(chunk.Data, 0, chunk.Data.Length);
            }

            _logger.LogDebug(
                "TranscriptionHub: received chunk {ChunkIndex} ({Bytes} bytes, total buffered: {Total}), session {SessionId}",
                chunk.ChunkIndex, chunk.Data.Length, session.AudioBuffer.Length, sessionId);

            // Acknowledge receipt
            await Clients.Caller.SendAsync("ChunkReceived", new
            {
                chunkIndex = chunk.ChunkIndex,
                sessionId,
                totalBuffered = session.AudioBuffer.Length
            });

            // If this is the final chunk, trigger transcription
            if (chunk.IsFinal)
            {
                await TranscribeBufferedAudioAsync(session, sessionId, Context.ConnectionAborted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TranscriptionHub: error processing audio chunk {ChunkIndex} for session {SessionId}",
                chunk.ChunkIndex, sessionId);
            await Clients.Caller.SendAsync("Error", new { message = ex.Message });
        }
    }

    /// <summary>
    /// Transcribes the buffered audio using the resolved provider.
    /// For streaming-capable providers, uses TranscribeStream; otherwise uses batch TranscribeAsync.
    /// </summary>
    private async Task TranscribeBufferedAudioAsync(
        StreamingSession session,
        string sessionId,
        CancellationToken ct)
    {
        if (session.AudioBuffer.Length == 0)
        {
            await Clients.Caller.SendAsync("Error", new
            {
                message = "No audio data received."
            });
            return;
        }

        var audioData = session.AudioBuffer.ToArray();
        session.AudioBuffer.SetLength(0); // Reset buffer for next utterance

        _logger.LogInformation(
            "TranscriptionHub: transcribing {Bytes} bytes for session {SessionId} (provider: {ProviderId})",
            audioData.Length, sessionId, session.Descriptor.ProviderId);

        // Write audio to a temporary file for batch transcription
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"memorix_asr_{sessionId}_{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(tempFilePath, audioData, ct);

            var request = new AsrTranscriptionRequest
            {
                AudioFilePath = tempFilePath,
                Language = session.Language,
                EnablePunctuation = session.EnablePunctuation,
                EnableVad = false,
                EnableSpeakerDiarization = false,
                Hotwords = session.Hotwords,
                DurationMs = 0,
                SegmentUuidPrefix = sessionId,
            };

            // Try streaming first if supported
            if (session.Descriptor.SupportsStreaming)
            {
                var streamRequest = new AsrStreamingRequest
                {
                    SessionId = sessionId,
                    Language = session.Language,
                    EnablePunctuation = session.EnablePunctuation,
                    Hotwords = session.Hotwords,
                    PreferredProviderId = session.Descriptor.ProviderId,
                };

                var streamResults = session.Provider.TranscribeStream(streamRequest, ct);
                if (streamResults != null)
                {
                    await foreach (var partial in streamResults.WithCancellation(ct))
                    {
                        await Clients.Caller.SendAsync("PartialResult", new
                        {
                            sessionId,
                            partial.PartialText,
                            finalText = partial.FinalText,
                            startMs = partial.StartMs,
                            endMs = partial.EndMs,
                            isFinal = partial.IsFinal,
                            segmentIndex = partial.SegmentIndex
                        });
                    }

                    await Clients.Caller.SendAsync("TranscriptionComplete", new
                    {
                        sessionId,
                        status = "completed"
                    });
                    return;
                }
            }

            // Fall back to batch transcription
            _logger.LogInformation(
                "TranscriptionHub: using batch transcription for session {SessionId}", sessionId);

            var result = await session.Provider.TranscribeAsync(request, ct);

            // Send each segment as a result
            for (var i = 0; i < result.Segments.Count; i++)
            {
                var segment = result.Segments[i];
                await Clients.Caller.SendAsync("PartialResult", new
                {
                    sessionId,
                    partialText = string.Empty,
                    finalText = segment.Text,
                    startMs = segment.StartMs,
                    endMs = segment.EndMs,
                    isFinal = true,
                    segmentIndex = i,
                    speakerKey = segment.SpeakerKey,
                    confidence = segment.Confidence,
                    segmentUuid = segment.SegmentUuid
                });
            }

            // Send full text
            await Clients.Caller.SendAsync("TranscriptionComplete", new
            {
                sessionId,
                status = "completed",
                fullText = result.FullText,
                language = result.Language,
                durationMs = result.DurationMs,
                segmentCount = result.Segments.Count,
                providerId = result.ProviderId,
                modelId = result.ModelId
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TranscriptionHub: transcription cancelled for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("TranscriptionComplete", new
            {
                sessionId,
                status = "cancelled"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TranscriptionHub: transcription failed for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("Error", new
            {
                message = $"Transcription failed: {ex.Message}"
            });
        }
        finally
        {
            // Clean up temp file
            try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Ends the streaming transcription session.
    /// </summary>
    public async Task EndSession()
    {
        var sessionId = Context.ConnectionId;
        _logger.LogInformation("TranscriptionHub: ending session {SessionId}", sessionId);

        // If there's buffered audio that hasn't been transcribed, process it
        if (_sessions.TryGetValue(sessionId, out var session) && session.AudioBuffer.Length > 0)
        {
            _logger.LogInformation(
                "TranscriptionHub: processing remaining {Bytes} bytes for session {SessionId}",
                session.AudioBuffer.Length, sessionId);
            await TranscribeBufferedAudioAsync(session, sessionId, Context.ConnectionAborted);
        }

        await Clients.Caller.SendAsync("SessionEnded", new { sessionId });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sessionId = Context.ConnectionId;
        _logger.LogInformation("TranscriptionHub: client disconnected {SessionId}", sessionId);

        // Clean up session state
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.AudioBuffer.Dispose();
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Internal session state ──

    private sealed class StreamingSession
    {
        public IAsrProvider Provider { get; set; } = null!;
        public AsrProviderDescriptor Descriptor { get; set; } = null!;
        public string? Language { get; set; }
        public bool EnablePunctuation { get; set; } = true;
        public List<string>? Hotwords { get; set; }
        public int SampleRate { get; set; } = 16000;
        public MemoryStream AudioBuffer { get; set; } = new();
    }
}

/// <summary>
/// Request to start a streaming transcription session.
/// </summary>
public class StartSessionRequest
{
    public string? Language { get; set; }
    public bool EnablePunctuation { get; set; } = true;
    public List<string>? Hotwords { get; set; }
    public string? PreferredProviderId { get; set; }
    public int SampleRate { get; set; } = 16000;
}

/// <summary>
/// Audio chunk message from the client.
/// </summary>
public class AudioChunkMessage
{
    public int ChunkIndex { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string Format { get; set; } = "pcm_s16le";
    public int SampleRate { get; set; } = 16000;
    public bool IsFinal { get; set; }
}
