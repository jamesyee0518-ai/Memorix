using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.Audio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Api.Hubs;

/// <summary>
/// WebSocket hub for streaming text-to-speech synthesis.
/// Clients send text and receive audio chunks in real time.
/// Follows the same authentication and connection-lifecycle pattern as
/// <see cref="TranscriptionHub"/>.
/// </summary>
[Authorize]
public class TtsHub : Hub
{
    private readonly IAudioPolicyRouter _policyRouter;
    private readonly TtsSentenceSplitter _sentenceSplitter;
    private readonly ILogger<TtsHub> _logger;

    private const int FileStreamChunkSize = 32 * 1024; // 32 KB per chunk for batch-mode file reads

    public TtsHub(
        IAudioPolicyRouter policyRouter,
        TtsSentenceSplitter sentenceSplitter,
        ILogger<TtsHub> logger)
    {
        _policyRouter = policyRouter;
        _sentenceSplitter = sentenceSplitter;
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
            _logger.LogWarning("TtsHub: connection rejected - no user identity");
            Context.Abort();
            return;
        }

        _logger.LogInformation("TtsHub: client connected {ConnectionId} for user {UserId}",
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Starts a TTS streaming session.
    /// Client calls this to initialize the session with language, voice, and provider preferences.
    /// </summary>
    public async Task StartSession(TtsStartSessionRequest request)
    {
        var sessionId = Context.ConnectionId;
        _logger.LogInformation(
            "TtsHub: starting session {SessionId}, language={Language}, voice={Voice}, provider={Provider}",
            sessionId, request.Language, request.VoiceId, request.PreferredProviderId);

        var routingContext = new TtsRoutingContext
        {
            Language = request.Language,
            VoiceId = request.VoiceId,
            PreferredProviderId = request.PreferredProviderId,
            OutputFormat = request.OutputFormat ?? "wav",
            SupportsStreaming = true,
            DataClassification = DataClassification.INTERNAL,
            FallbackPolicy = FallbackPolicies.LocalFallback
        };

        try
        {
            var provider = await _policyRouter.ResolveTtsProviderAsync(
                routingContext, Context.ConnectionAborted);
            var descriptor = await provider.GetDescriptorAsync(Context.ConnectionAborted);

            await Clients.Caller.SendAsync("SessionStarted", new
            {
                sessionId,
                providerId = descriptor.ProviderId,
                modelId = descriptor.ModelId,
                supportsStreaming = descriptor.SupportsStreaming,
                supportsVoiceCloning = descriptor.SupportsVoiceCloning
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TtsHub: failed to resolve TTS provider for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("SessionError", new { message = ex.Message });
        }
    }

    /// <summary>
    /// Receives text from the client, synthesizes it, and streams audio chunks back.
    /// The text is split into sentence-level chunks for low-latency first-audio.
    /// </summary>
    public async Task SendText(TtsTextMessage message)
    {
        var sessionId = Context.ConnectionId;
        var ct = Context.ConnectionAborted;

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            await Clients.Caller.SendAsync("SynthesisError", new
            {
                sessionId,
                message = "Text is required."
            });
            return;
        }

        _logger.LogInformation(
            "TtsHub: received text ({TextLength} chars) for session {SessionId}",
            message.Text.Length, sessionId);

        // Resolve the TTS provider via the policy router.
        var routingContext = new TtsRoutingContext
        {
            Language = message.Language,
            VoiceId = message.VoiceId,
            PreferredProviderId = message.PreferredProviderId,
            OutputFormat = "wav",
            SupportsStreaming = true,
            DataClassification = DataClassification.INTERNAL,
            FallbackPolicy = FallbackPolicies.LocalFallback
        };

        ITtsProvider provider;
        try
        {
            provider = await _policyRouter.ResolveTtsProviderAsync(routingContext, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TtsHub: failed to resolve provider for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("SynthesisError", new { sessionId, message = ex.Message });
            return;
        }

        // Split text into sentence-level chunks for streaming.
        var sentences = _sentenceSplitter.Split(message.Text);
        var globalChunkIndex = 0;

        foreach (var sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                continue;

            // Try streaming synthesis first.
            var streamRequest = new TtsStreamRequest
            {
                SessionId = sessionId,
                Text = sentence,
                Language = message.Language,
                VoiceId = message.VoiceId,
                Speed = message.Speed,
                SampleRate = message.SampleRate,
                PreferredProviderId = message.PreferredProviderId
            };

            var stream = provider.SynthesizeStream(streamRequest, ct);
            if (stream != null)
            {
                // Streaming mode — forward each audio chunk to the client.
                await foreach (var chunk in stream.WithCancellation(ct))
                {
                    await Clients.Caller.SendAsync("AudioChunk", new
                    {
                        sessionId,
                        chunkIndex = globalChunkIndex++,
                        data = chunk.Data,
                        isFinal = chunk.IsFinal,
                        format = chunk.Format,
                        sampleRate = chunk.SampleRate
                    });
                }
            }
            else
            {
                // Batch mode — synthesize to file, then stream the file in chunks.
                var batchRequest = new TtsRequest
                {
                    Text = sentence,
                    Language = message.Language,
                    VoiceId = message.VoiceId,
                    Speed = message.Speed,
                    OutputFormat = "wav",
                    SampleRate = message.SampleRate,
                    PreferredProviderId = message.PreferredProviderId
                };

                try
                {
                    var result = await provider.SynthesizeAsync(batchRequest, ct);

                    if (File.Exists(result.OutputFilePath))
                    {
                        await using var fs = File.OpenRead(result.OutputFilePath);
                        var buffer = new byte[FileStreamChunkSize];
                        int bytesRead;

                        while ((bytesRead = await fs.ReadAsync(buffer, ct)) > 0)
                        {
                            var data = bytesRead == buffer.Length
                                ? buffer
                                : buffer.AsSpan(0, bytesRead).ToArray();

                            await Clients.Caller.SendAsync("AudioChunk", new
                            {
                                sessionId,
                                chunkIndex = globalChunkIndex++,
                                data,
                                isFinal = false,
                                format = result.OutputFormat,
                                sampleRate = message.SampleRate
                            });
                        }

                        // Clean up the temp file.
                        try { File.Delete(result.OutputFilePath); } catch { /* best-effort */ }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "TtsHub: batch synthesis failed for sentence in session {SessionId}", sessionId);
                    await Clients.Caller.SendAsync("SynthesisError", new
                    {
                        sessionId,
                        message = $"Synthesis failed: {ex.Message}"
                    });
                    return;
                }
            }
        }

        // Send final completion event.
        await Clients.Caller.SendAsync("SynthesisCompleted", new
        {
            sessionId,
            totalChunks = globalChunkIndex
        });
    }

    /// <summary>
    /// Ends the TTS streaming session.
    /// </summary>
    public async Task EndSession()
    {
        var sessionId = Context.ConnectionId;
        _logger.LogInformation("TtsHub: ending session {SessionId}", sessionId);
        await Clients.Caller.SendAsync("SessionEnded", new { sessionId });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sessionId = Context.ConnectionId;
        _logger.LogInformation("TtsHub: client disconnected {SessionId}", sessionId);
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Request to start a TTS streaming session.
/// </summary>
public class TtsStartSessionRequest
{
    public string? Language { get; set; }
    public string? VoiceId { get; set; }
    public string? PreferredProviderId { get; set; }
    public string? OutputFormat { get; set; }
}

/// <summary>
/// Text message from the client for TTS synthesis.
/// </summary>
public class TtsTextMessage
{
    public string Text { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? VoiceId { get; set; }
    public decimal Speed { get; set; } = 1.0m;
    public int SampleRate { get; set; } = 22050;
    public string? PreferredProviderId { get; set; }
}
