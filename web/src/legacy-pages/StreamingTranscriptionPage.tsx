"use client";

/**
 * StreamingTranscriptionPage
 *
 * Real-time streaming transcription page. Uses the browser's MediaRecorder API
 * (backed by the Web Audio API getUserMedia) to capture microphone audio and
 * streams it in chunks to the TranscriptionHub via WebSocket (SignalR).
 *
 * Features:
 *   - Record button with Start / Stop controls
 *   - WebSocket connection status indicator (disconnected / connecting /
 *     connected / error)
 *   - Live partial results display (updates in real time)
 *   - Final transcription with a segment list (accumulated final segments)
 *   - Provider / language / punctuation / hotwords configuration before start
 *
 * Uses:
 *   - TranscriptionHubClient from ../api/websocket
 *   - Types from ../types/audio
 *     (PartialResultEvent, TranscriptionCompleteEvent, SessionStartedEvent,
 *      AsrSegment, StartSessionRequest, etc.)
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { TranscriptionHubClient } from "../../api/websocket";
import type {
  AsrSegment,
  HubErrorEvent,
  PartialResultEvent,
  SessionStartedEvent,
  StartSessionRequest,
  TranscriptionCompleteEvent,
} from "../../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const LANGUAGES: { value: string; label: string }[] = [
  { value: "", label: "Auto-detect" },
  { value: "zh", label: "Chinese (zh)" },
  { value: "en", label: "English (en)" },
  { value: "ja", label: "Japanese (ja)" },
  { value: "ko", label: "Korean (ko)" },
  { value: "es", label: "Spanish (es)" },
  { value: "fr", label: "French (fr)" },
  { value: "de", label: "German (de)" },
];

/** Audio chunk send interval in milliseconds. */
const CHUNK_INTERVAL_MS = 200;
/** Target sample rate for the audio sent to the hub. */
const TARGET_SAMPLE_RATE = 16000;

type ConnectionState =
  | "disconnected"
  | "connecting"
  | "connected"
  | "error";

type RecordingState = "idle" | "starting" | "recording" | "stopping";

// ─── Main component ──────────────────────────────────────────────────────────

export default function StreamingTranscriptionPage() {
  // Configuration state
  const [language, setLanguage] = useState("zh");
  const [enablePunctuation, setEnablePunctuation] = useState(true);
  const [hotwords, setHotwords] = useState("");
  const [preferredProviderId, setPreferredProviderId] = useState<string>("");

  // Connection & recording state
  const [connectionState, setConnectionState] = useState<ConnectionState>(
    "disconnected",
  );
  const [recordingState, setRecordingState] = useState<RecordingState>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Session info
  const [sessionInfo, setSessionInfo] = useState<SessionStartedEvent | null>(
    null,
  );

  // Live partial text (most recent partial result)
  const [partialText, setPartialText] = useState("");

  // Accumulated final segments
  const [segments, setSegments] = useState<AsrSegment[]>([]);

  // Full text (concatenated final segments)
  const [fullText, setFullText] = useState("");

  // Completion info
  const [completionInfo, setCompletionInfo] =
    useState<TranscriptionCompleteEvent | null>(null);

  // Recording duration timer
  const [recordingDuration, setRecordingDuration] = useState(0);

  // Refs for mutable resources that should not trigger re-renders
  const hubClientRef = useRef<TranscriptionHubClient | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioStreamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const sourceNodeRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const chunkIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const durationIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const chunkQueueRef = useRef<Uint8Array[]>([]);
  const chunkIndexRef = useRef(0);
  const isRecordingRef = useRef(false);

  // ─── Cleanup on unmount ─────────────────────────────────────────────────────

  useEffect(() => {
    return () => {
      cleanupAll();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ─── Duration timer ─────────────────────────────────────────────────────────

  useEffect(() => {
    if (recordingState === "recording") {
      const startTime = Date.now();
      durationIntervalRef.current = setInterval(() => {
        setRecordingDuration(Math.floor((Date.now() - startTime) / 1000));
      }, 1000);
    } else {
      if (durationIntervalRef.current) {
        clearInterval(durationIntervalRef.current);
        durationIntervalRef.current = null;
      }
    }
    return () => {
      if (durationIntervalRef.current) {
        clearInterval(durationIntervalRef.current);
      }
    };
  }, [recordingState]);

  // ─── Cleanup helpers ────────────────────────────────────────────────────────

  const stopAudioPipeline = useCallback(() => {
    isRecordingRef.current = false;

    if (chunkIntervalRef.current) {
      clearInterval(chunkIntervalRef.current);
      chunkIntervalRef.current = null;
    }

    if (processorRef.current) {
      processorRef.current.disconnect();
      processorRef.current = null;
    }
    if (sourceNodeRef.current) {
      sourceNodeRef.current.disconnect();
      sourceNodeRef.current = null;
    }
    if (audioContextRef.current) {
      audioContextRef.current.close().catch(() => {});
      audioContextRef.current = null;
    }
    if (mediaRecorderRef.current) {
      if (mediaRecorderRef.current.state !== "inactive") {
        mediaRecorderRef.current.stop();
      }
      mediaRecorderRef.current = null;
    }
    if (audioStreamRef.current) {
      audioStreamRef.current.getTracks().forEach((t) => t.stop());
      audioStreamRef.current = null;
    }
  }, []);

  const cleanupAll = useCallback(async () => {
    stopAudioPipeline();
    if (hubClientRef.current) {
      try {
        await hubClientRef.current.endSession();
      } catch {
        // best effort
      }
      try {
        await hubClientRef.current.disconnect();
      } catch {
        // best effort
      }
      hubClientRef.current = null;
    }
  }, [stopAudioPipeline]);

  // ─── Start recording ────────────────────────────────────────────────────────

  const handleStartRecording = useCallback(async () => {
    setErrorMessage(null);
    setPartialText("");
    setSegments([]);
    setFullText("");
    setCompletionInfo(null);
    setSessionInfo(null);
    setRecordingDuration(0);
    setRecordingState("starting");
    setConnectionState("connecting");

    try {
      // 1. Request microphone access
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          sampleRate: TARGET_SAMPLE_RATE,
          echoCancellation: true,
          noiseSuppression: true,
        },
      });
      audioStreamRef.current = stream;

      // 2. Set up AudioContext for resampling to 16kHz PCM
      const AudioContextClass =
        window.AudioContext ||
        (window as unknown as { webkitAudioContext: typeof AudioContext })
          .webkitAudioContext;
      const audioContext = new AudioContextClass({
        sampleRate: TARGET_SAMPLE_RATE,
      });
      audioContextRef.current = audioContext;

      const source = audioContext.createMediaStreamSource(stream);
      sourceNodeRef.current = source;

      // Use ScriptProcessorNode to capture raw PCM samples.
      // (AudioWorklet is the modern API but requires a separate worklet file;
      // ScriptProcessorNode works everywhere and is sufficient for streaming.)
      const bufferSize = 4096;
      const processor = audioContext.createScriptProcessor(bufferSize, 1, 1);
      processorRef.current = processor;

      processor.onaudioprocess = (event: AudioProcessingEvent) => {
        if (!isRecordingRef.current) return;
        const inputBuffer = event.inputBuffer;
        const channelData = inputBuffer.getChannelData(0);
        // Convert Float32 samples [-1.0, 1.0] to Int16 PCM
        const pcm16 = new Int16Array(channelData.length);
        for (let i = 0; i < channelData.length; i++) {
          const s = Math.max(-1, Math.min(1, channelData[i]));
          pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7fff;
        }
        // Store as Uint8Array for the hub
        chunkQueueRef.current.push(
          new Uint8Array(pcm16.buffer, pcm16.byteOffset, pcm16.byteLength),
        );
      };

      source.connect(processor);
      processor.connect(audioContext.destination);

      // 3. Also create a MediaRecorder as a fallback / for potential replay
      const mediaRecorder = new MediaRecorder(stream, {
        mimeType: MediaRecorder.isTypeSupported("audio/webm")
          ? "audio/webm"
          : "",
      });
      mediaRecorderRef.current = mediaRecorder;
      mediaRecorder.start();

      // 4. Connect to the TranscriptionHub
      const client = new TranscriptionHubClient();

      client.onSessionStarted = (event: SessionStartedEvent) => {
        setSessionInfo(event);
      };

      client.onPartialResult = (event: PartialResultEvent) => {
        if (event.isFinal && event.finalText) {
          // Final segment received
          setSegments((prev) => {
            const newSegment: AsrSegment = {
              segmentUuid: event.segmentUuid ?? crypto.randomUUID(),
              startMs: event.startMs ?? 0,
              endMs: event.endMs ?? 0,
              text: event.finalText!,
              confidence: event.confidence ?? 1,
              speakerKey: event.speakerKey ?? null,
              words: null,
              segmentIndex: event.segmentIndex,
            };
            // Avoid duplicate segment indices
            if (prev.some((s) => s.segmentIndex === newSegment.segmentIndex)) {
              return prev;
            }
            return [...prev, newSegment];
          });
          setFullText((prev) => {
            const sep = prev && !prev.endsWith(" ") ? " " : "";
            return prev + sep + event.finalText;
          });
          // Clear the partial text since we now have a final
          setPartialText("");
        } else {
          // Partial (interim) result
          setPartialText(event.partialText);
        }
      };

      client.onTranscriptionComplete = (event: TranscriptionCompleteEvent) => {
        setCompletionInfo(event);
        if (event.fullText) {
          setFullText(event.fullText);
        }
        setRecordingState("stopping");
      };

      client.onError = (event: HubErrorEvent) => {
        setErrorMessage(event.message);
        setConnectionState("error");
      };

      await client.connect();
      hubClientRef.current = client;
      setConnectionState("connected");

      // 5. Start the streaming session
      const sessionRequest: StartSessionRequest = {
        language: language || null,
        enablePunctuation,
        hotwords: hotwords.trim()
          ? hotwords.split(/[,\n]/).map((h) => h.trim()).filter(Boolean)
          : null,
        preferredProviderId: preferredProviderId || null,
        sampleRate: TARGET_SAMPLE_RATE,
      };
      await client.startSession(sessionRequest);

      // 6. Start sending audio chunks at a regular interval
      isRecordingRef.current = true;
      chunkIndexRef.current = 0;
      chunkQueueRef.current = [];

      chunkIntervalRef.current = setInterval(async () => {
        if (!isRecordingRef.current || !hubClientRef.current) return;
        const queue = chunkQueueRef.current;
        if (queue.length === 0) return;

        // Combine all queued chunks into a single buffer
        const totalLength = queue.reduce((sum, c) => sum + c.length, 0);
        const combined = new Uint8Array(totalLength);
        let offset = 0;
        for (const chunk of queue) {
          combined.set(chunk, offset);
          offset += chunk.length;
        }
        queue.length = 0; // clear the queue

        const chunkIndex = chunkIndexRef.current++;
        try {
          await hubClientRef.current.sendAudioChunk({
            chunkIndex,
            data: combined,
            format: "pcm_s16le",
            sampleRate: TARGET_SAMPLE_RATE,
            isFinal: false,
          });
        } catch {
          // Silently skip failed chunk sends; the hub will still
          // process whatever it received.
        }
      }, CHUNK_INTERVAL_MS);

      setRecordingState("recording");
    } catch (err: unknown) {
      setErrorMessage(
        err instanceof Error
          ? err.message
          : "Failed to start recording. Check microphone permissions.",
      );
      setConnectionState("error");
      setRecordingState("idle");
      stopAudioPipeline();
    }
  }, [
    language,
    enablePunctuation,
    hotwords,
    preferredProviderId,
    stopAudioPipeline,
  ]);

  // ─── Stop recording ─────────────────────────────────────────────────────────

  const handleStopRecording = useCallback(async () => {
    setRecordingState("stopping");

    // Send any remaining buffered audio as a final chunk
    if (hubClientRef.current && isRecordingRef.current) {
      isRecordingRef.current = false;
      const queue = chunkQueueRef.current;
      if (queue.length > 0) {
        const totalLength = queue.reduce((sum, c) => sum + c.length, 0);
        const combined = new Uint8Array(totalLength);
        let offset = 0;
        for (const chunk of queue) {
          combined.set(chunk, offset);
          offset += chunk.length;
        }
        queue.length = 0;
        try {
          await hubClientRef.current.sendAudioChunk({
            chunkIndex: chunkIndexRef.current++,
            data: combined,
            format: "pcm_s16le",
            sampleRate: TARGET_SAMPLE_RATE,
            isFinal: true,
          });
        } catch {
          // best effort
        }
      }

      // End the session (tells the hub to finalize transcription)
      try {
        await hubClientRef.current.endSession();
      } catch {
        // best effort
      }
    }

    // Stop the audio pipeline
    stopAudioPipeline();

    // Wait briefly for the TranscriptionComplete event, then disconnect
    setTimeout(async () => {
      if (hubClientRef.current) {
        try {
          await hubClientRef.current.disconnect();
        } catch {
          // best effort
        }
        hubClientRef.current = null;
      }
      setConnectionState("disconnected");
      setRecordingState("idle");
    }, 3000);
  }, [stopAudioPipeline]);

  // ─── Copy full text ─────────────────────────────────────────────────────────

  const handleCopyText = useCallback(() => {
    if (fullText) {
      navigator.clipboard.writeText(fullText).catch(() => {});
    }
  }, [fullText]);

  // ─── Clear results ──────────────────────────────────────────────────────────

  const handleClearResults = useCallback(() => {
    setPartialText("");
    setSegments([]);
    setFullText("");
    setCompletionInfo(null);
    setSessionInfo(null);
    setRecordingDuration(0);
    setErrorMessage(null);
  }, []);

  // ─── Render ────────────────────────────────────────────────────────────────

  const isRecording = recordingState === "recording";
  const isBusy =
    recordingState === "starting" || recordingState === "stopping";

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Streaming Transcription
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Real-time speech-to-text via microphone capture and WebSocket streaming
          to the TranscriptionHub.
        </p>
      </div>

      {/* ─── Connection status bar ───────────────────────────────────────────── */}
      <ConnectionStatusBar
        connectionState={connectionState}
        recordingState={recordingState}
        sessionInfo={sessionInfo}
        recordingDuration={recordingDuration}
      />

      {/* ─── Configuration (disabled while recording) ─────────────────────────── */}
      {!isRecording && !isBusy && (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Session Settings
          </h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {/* Language */}
            <div>
              <label
                htmlFor="stream-language"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Language
              </label>
              <select
                id="stream-language"
                value={language}
                onChange={(e) => setLanguage(e.target.value)}
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              >
                {LANGUAGES.map((lang) => (
                  <option key={lang.value} value={lang.value}>
                    {lang.label}
                  </option>
                ))}
              </select>
            </div>

            {/* Preferred provider */}
            <div>
              <label
                htmlFor="stream-provider"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Preferred Provider (optional)
              </label>
              <input
                id="stream-provider"
                type="text"
                value={preferredProviderId}
                onChange={(e) => setPreferredProviderId(e.target.value)}
                placeholder="e.g. openai, aliyun (auto if empty)"
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              />
            </div>

            {/* Hotwords */}
            <div className="md:col-span-2">
              <label
                htmlFor="stream-hotwords"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Hotwords
              </label>
              <input
                id="stream-hotwords"
                type="text"
                value={hotwords}
                onChange={(e) => setHotwords(e.target.value)}
                placeholder="Comma-separated proper nouns, terms..."
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              />
            </div>

            {/* Punctuation toggle */}
            <div className="md:col-span-2">
              <label className="flex cursor-pointer items-center gap-2">
                <button
                  type="button"
                  role="switch"
                  aria-checked={enablePunctuation}
                  onClick={() => setEnablePunctuation((v) => !v)}
                  className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors ${
                    enablePunctuation
                      ? "bg-blue-600"
                      : "bg-gray-300 dark:bg-gray-700"
                  }`}
                >
                  <span
                    className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
                      enablePunctuation ? "translate-x-4" : "translate-x-0.5"
                    }`}
                  />
                </button>
                <span className="text-sm text-gray-700 dark:text-gray-300">
                  Enable Punctuation
                </span>
              </label>
            </div>
          </div>
        </div>
      )}

      {/* ─── Recording controls ──────────────────────────────────────────────── */}
      <div className="flex items-center justify-center">
        <button
          type="button"
          onClick={isRecording || isBusy ? handleStopRecording : handleStartRecording}
          disabled={isBusy}
          className={`flex h-20 w-20 flex-col items-center justify-center rounded-full shadow-lg transition-all disabled:cursor-not-allowed disabled:opacity-50 ${
            isRecording
              ? "bg-red-600 hover:bg-red-700"
              : "bg-blue-600 hover:bg-blue-700"
          }`}
        >
          {isBusy ? (
            <span className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-white border-t-transparent" />
          ) : isRecording ? (
            <>
              <span className="h-6 w-6 rounded-sm bg-white" />
              <span className="mt-1 text-xs font-medium text-white">Stop</span>
            </>
          ) : (
            <>
              <svg
                className="h-8 w-8 text-white"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 18.75a6 6 0 006-6v-1.5m-6 7.5a6 6 0 01-6-6v-1.5m6 7.5v3.75m-3.75 0h7.5M12 15.75a3 3 0 01-3-3V4.5a3 3 0 116 0v8.25a3 3 0 01-3 3z"
                />
              </svg>
              <span className="mt-1 text-xs font-medium text-white">Record</span>
            </>
          )}
        </button>
      </div>

      {/* Error message */}
      {errorMessage && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 dark:border-red-900 dark:bg-red-950/30">
          <p className="text-sm text-red-700 dark:text-red-400">{errorMessage}</p>
        </div>
      )}

      {/* ─── Live partial results ─────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-gray-100">
          Live Transcript
        </h2>

        {/* Partial text (interim) */}
        {partialText && (
          <p className="mb-2 text-sm italic text-gray-500 dark:text-gray-400">
            {partialText}
            <span className="ml-0.5 inline-block h-4 w-0.5 animate-pulse bg-gray-400 align-middle" />
          </p>
        )}

        {/* Full text so far */}
        {fullText ? (
          <div>
            <p className="text-sm text-gray-900 dark:text-gray-100">{fullText}</p>
            <div className="mt-3 flex gap-2">
              <button
                type="button"
                onClick={handleCopyText}
                className="rounded-md border border-gray-300 px-3 py-1 text-xs font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
              >
                Copy Text
              </button>
              {!isRecording && (
                <button
                  type="button"
                  onClick={handleClearResults}
                  className="rounded-md border border-gray-300 px-3 py-1 text-xs font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
                >
                  Clear
                </button>
              )}
            </div>
          </div>
        ) : (
          !partialText && (
            <p className="py-6 text-center text-sm text-gray-400 dark:text-gray-500">
              {isRecording
                ? "Listening... speak into your microphone."
                : "Press the Record button to start real-time transcription."}
            </p>
          )
        )}
      </div>

      {/* ─── Final segments list ──────────────────────────────────────────────── */}
      {segments.length > 0 && (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Segments ({segments.length})
          </h2>
          <div className="space-y-2">
            {segments.map((segment, index) => (
              <div
                key={`${segment.segmentUuid}-${index}`}
                className="rounded-lg border border-gray-200 p-3 dark:border-gray-800"
              >
                <div className="mb-1 flex items-center gap-2 text-xs text-gray-500 dark:text-gray-400">
                  <span className="font-medium text-gray-600 dark:text-gray-300">
                    #{index + 1}
                  </span>
                  <span className="font-mono">
                    {formatTimestamp(segment.startMs)} →{" "}
                    {formatTimestamp(segment.endMs)}
                  </span>
                  {segment.speakerKey && (
                    <span className="inline-flex items-center rounded bg-indigo-100 px-1.5 py-0.5 text-xs text-indigo-700 dark:bg-indigo-950/50 dark:text-indigo-300">
                      {segment.speakerKey}
                    </span>
                  )}
                  {segment.confidence < 1 && (
                    <span className="text-xs text-gray-400">
                      conf {(segment.confidence * 100).toFixed(0)}%
                    </span>
                  )}
                </div>
                <p className="text-sm text-gray-900 dark:text-gray-100">
                  {segment.text}
                </p>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ─── Completion summary ───────────────────────────────────────────────── */}
      {completionInfo && (
        <div className="rounded-xl border border-green-200 bg-green-50 p-5 dark:border-green-900 dark:bg-green-950/30">
          <h2 className="mb-3 text-lg font-semibold text-green-900 dark:text-green-200">
            Transcription Complete
          </h2>
          <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-3">
            {completionInfo.providerId && (
              <CompletionItem label="Provider" value={completionInfo.providerId} />
            )}
            {completionInfo.modelId && (
              <CompletionItem label="Model" value={completionInfo.modelId} />
            )}
            {completionInfo.language && (
              <CompletionItem label="Language" value={completionInfo.language} />
            )}
            {completionInfo.durationMs != null && (
              <CompletionItem
                label="Duration"
                value={`${(completionInfo.durationMs / 1000).toFixed(1)}s`}
              />
            )}
            {completionInfo.segmentCount != null && (
              <CompletionItem
                label="Segments"
                value={String(completionInfo.segmentCount)}
              />
            )}
            <CompletionItem label="Status" value={completionInfo.status} />
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Helper sub-components ───────────────────────────────────────────────────

function ConnectionStatusBar({
  connectionState,
  recordingState,
  sessionInfo,
  recordingDuration,
}: {
  connectionState: ConnectionState;
  recordingState: RecordingState;
  sessionInfo: SessionStartedEvent | null;
  recordingDuration: number;
}) {
  const config: Record<
    ConnectionState,
    { label: string; dot: string; bg: string; text: string }
  > = {
    disconnected: {
      label: "Disconnected",
      dot: "bg-gray-400",
      bg: "bg-gray-50 dark:bg-gray-900/50",
      text: "text-gray-600 dark:text-gray-400",
    },
    connecting: {
      label: "Connecting...",
      dot: "bg-amber-500 animate-pulse",
      bg: "bg-amber-50 dark:bg-amber-950/30",
      text: "text-amber-700 dark:text-amber-300",
    },
    connected: {
      label: "Connected",
      dot: "bg-green-500",
      bg: "bg-green-50 dark:bg-green-950/30",
      text: "text-green-700 dark:text-green-300",
    },
    error: {
      label: "Error",
      dot: "bg-red-500",
      bg: "bg-red-50 dark:bg-red-950/30",
      text: "text-red-700 dark:text-red-400",
    },
  };

  const c = config[connectionState];
  const minutes = Math.floor(recordingDuration / 60);
  const seconds = recordingDuration % 60;
  const timeStr = `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;

  return (
    <div
      className={`flex flex-wrap items-center gap-3 rounded-lg border border-gray-200 px-4 py-2.5 ${c.bg} dark:border-gray-800`}
    >
      <div className="flex items-center gap-2">
        <span className={`inline-block h-2.5 w-2.5 rounded-full ${c.dot}`} />
        <span className={`text-sm font-medium ${c.text}`}>{c.label}</span>
      </div>

      {recordingState === "recording" && (
        <span className="text-sm font-mono text-gray-600 dark:text-gray-400">
          {timeStr}
        </span>
      )}

      {sessionInfo && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-gray-500 dark:text-gray-400">
          <span>Provider: {sessionInfo.providerId}</span>
          <span>Model: {sessionInfo.modelId}</span>
          {sessionInfo.supportsStreaming ? (
            <span className="inline-flex items-center rounded bg-blue-100 px-1.5 py-0.5 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300">
              Streaming
            </span>
          ) : (
            <span className="inline-flex items-center rounded bg-amber-100 px-1.5 py-0.5 text-amber-700 dark:bg-amber-950/50 dark:text-amber-300">
              Buffered
            </span>
          )}
        </div>
      )}

      <span className="ml-auto text-xs text-gray-400 dark:text-gray-500">
        WebSocket / SignalR
      </span>
    </div>
  );
}

function CompletionItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="font-medium text-green-700 dark:text-green-300">
        {label}:
      </span>{" "}
      <span className="text-gray-900 dark:text-gray-100">{value}</span>
    </div>
  );
}

function formatTimestamp(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  const millis = ms % 1000;
  const p = (n: number, len = 2) => String(n).padStart(len, "0");
  if (m > 0) return `${p(m)}:${p(s)}.${p(millis, 3)}`;
  return `${p(s)}.${p(millis, 3)}s`;
}
