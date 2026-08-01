"use client";

/**
 * VoiceQaPage
 *
 * Voice Q&A page that combines speech recognition and text-to-speech.
 * Records audio from the microphone, transcribes it in real-time via the
 * TranscriptionHub (SignalR/WebSocket), sends the transcribed text as a
 * question to the QA API, displays the answer, and speaks it using the
 * TtsPlayer component.
 *
 * Features:
 *   - "Click to record" button with live transcription display
 *   - Automatic QA question submission when recording stops
 *   - Answer display with auto-speak TTS (toggleable)
 *   - Conversation history (user questions + AI answers)
 *   - Per-answer "Replay" button that uses TtsPlayer
 *   - Collapsible settings panel (ASR language, auto-speak toggle)
 *   - Comprehensive error handling
 *
 * Recording pipeline (same pattern as StreamingTranscriptionPage):
 *   MediaRecorder + AudioContext + ScriptProcessorNode -> PCM 16kHz chunks
 *   -> TranscriptionHubClient.sendAudioChunk() -> real-time transcription
 *
 * Uses:
 *   - TranscriptionHubClient from ../api/websocket
 *   - apiRequest from ../api/audioClient (for QA API calls)
 *   - getAccessToken from ../storage/auth (auth check before recording)
 *   - TtsPlayer from ../components/TtsPlayer (with forwardRef for imperative control)
 *   - Types from ../types/audio
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { TranscriptionHubClient } from "../api/websocket";
import { apiRequest } from "../api/audioClient";
import { getAccessToken } from "../storage/auth";
import TtsPlayer, { type TtsPlayerHandle } from "../components/TtsPlayer";
import type {
  AsrSegment,
  HubErrorEvent,
  PartialResultEvent,
  SessionStartedEvent,
  StartSessionRequest,
  TranscriptionCompleteEvent,
} from "../types/audio";

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
/** Timeout to wait for TranscriptionComplete before using accumulated text. */
const TRANSCRIPTION_TIMEOUT_MS = 5000;

// ─── Types ───────────────────────────────────────────────────────────────────

interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  text: string;
  timestamp: number;
  /** Whether this message encountered an error. */
  isError?: boolean;
}

type RecordingState = "idle" | "starting" | "recording" | "stopping";

// ─── QA API helpers ──────────────────────────────────────────────────────────

/**
 * Creates a QA session by calling POST /qa/sessions.
 * Uses the apiRequest wrapper which handles auth tokens and envelope unwrapping.
 */
async function createQaSession(): Promise<string> {
  const session = await apiRequest<{ id: string }>("/qa/sessions", {
    method: "POST",
    body: JSON.stringify({ title: "Voice Q&A" }),
  });
  return session.id;
}

/**
 * Asks a question to the QA API by calling POST /qa/ask.
 * Returns the answer text extracted from the response.
 */
async function askQaQuestion(sessionId: string, query: string): Promise<string> {
  const result = await apiRequest<{
    answer?: string;
    response?: string;
    text?: string;
    content?: string;
    message?: string;
  }>("/qa/ask", {
    method: "POST",
    body: JSON.stringify({ sessionId, query }),
  });
  return (
    result.answer ??
    result.response ??
    result.text ??
    result.content ??
    result.message ??
    ""
  );
}

// ─── Helper functions ────────────────────────────────────────────────────────

/** Formats a timestamp (ms) as a locale time string. */
function formatTime(ts: number): string {
  return new Date(ts).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  });
}

/** Formats a duration in seconds as mm:ss. */
function formatDuration(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

// ─── Main component ──────────────────────────────────────────────────────────

export default function VoiceQaPage() {
  // ── Conversation state ─────────────────────────────────────────────────────
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [liveTranscript, setLiveTranscript] = useState("");

  // ── Settings state ─────────────────────────────────────────────────────────
  const [language, setLanguage] = useState("zh");
  const [autoSpeak, setAutoSpeak] = useState(true);
  const [showSettings, setShowSettings] = useState(false);

  // ── Recording state ────────────────────────────────────────────────────────
  const [recordingState, setRecordingState] = useState<RecordingState>("idle");
  const [recordingDuration, setRecordingDuration] = useState(0);
  const [sessionInfo, setSessionInfo] = useState<SessionStartedEvent | null>(
    null,
  );

  // ── QA state ───────────────────────────────────────────────────────────────
  const [isAsking, setIsAsking] = useState(false);

  // ── Error state ────────────────────────────────────────────────────────────
  const [error, setError] = useState<string | null>(null);

  // ── Segments (for potential future use / debugging) ────────────────────────
  const [segments, setSegments] = useState<AsrSegment[]>([]);

  // ── Refs for mutable resources ─────────────────────────────────────────────
  const ttsPlayerRef = useRef<TtsPlayerHandle>(null);
  const hubClientRef = useRef<TranscriptionHubClient | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioStreamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const sourceNodeRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const chunkIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const durationIntervalRef = useRef<ReturnType<typeof setInterval> | null>(
    null,
  );
  const chunkQueueRef = useRef<Uint8Array[]>([]);
  const chunkIndexRef = useRef(0);
  const isRecordingRef = useRef(false);
  const isStoppingRef = useRef(false);

  // Accumulated transcription text (built from final partial results).
  const fullTextRef = useRef("");

  // QA session ID (created lazily on first question).
  const sessionIdRef = useRef<string | null>(null);

  // Ref to the latest askQuestion function (avoids stale closures in hub handlers).
  const askQuestionRef = useRef<
    ((text: string) => Promise<void>) | null
  >(null);

  // Auto-scroll anchor.
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // ── Auto-scroll to bottom on new messages or live transcript ───────────────
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, liveTranscript]);

  // ── Update askQuestionRef whenever askQuestion changes ─────────────────────
  // (Defined below; this effect is registered after askQuestion is declared.)

  // ── Cleanup on unmount ─────────────────────────────────────────────────────
  useEffect(() => {
    return () => {
      cleanupAll();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Recording duration timer ───────────────────────────────────────────────
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

  // ── Cleanup helpers ────────────────────────────────────────────────────────

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

  // ── Ensure QA session exists (lazy creation) ───────────────────────────────

  const ensureSession = useCallback(async (): Promise<string> => {
    if (sessionIdRef.current) return sessionIdRef.current;
    const id = await createQaSession();
    sessionIdRef.current = id;
    return id;
  }, []);

  // ── Ask a question and display the answer ──────────────────────────────────

  const askQuestion = useCallback(
    async (question: string) => {
      if (!question.trim()) return;

      // Add user message to conversation
      const userMessage: ChatMessage = {
        id: crypto.randomUUID(),
        role: "user",
        text: question,
        timestamp: Date.now(),
      };
      setMessages((prev) => [...prev, userMessage]);

      setIsAsking(true);
      setError(null);

      try {
        const sessionId = await ensureSession();
        const answerText = await askQaQuestion(sessionId, question);

        if (!answerText) {
          throw new Error("Received an empty answer from the QA service.");
        }

        // Add assistant message
        const assistantMessage: ChatMessage = {
          id: crypto.randomUUID(),
          role: "assistant",
          text: answerText,
          timestamp: Date.now(),
        };
        setMessages((prev) => [...prev, assistantMessage]);

        // Auto-play TTS if enabled
        if (autoSpeak) {
          await ttsPlayerRef.current?.synthesizeAndPlay(answerText);
        }
      } catch (err: unknown) {
        const errMsg =
          err instanceof Error ? err.message : "Failed to get an answer.";

        // Add error message to conversation
        const errorMessage: ChatMessage = {
          id: crypto.randomUUID(),
          role: "assistant",
          text: `Error: ${errMsg}`,
          timestamp: Date.now(),
          isError: true,
        };
        setMessages((prev) => [...prev, errorMessage]);
        setError(errMsg);
      } finally {
        setIsAsking(false);
      }
    },
    [ensureSession, autoSpeak],
  );

  // Keep askQuestionRef in sync with the latest askQuestion closure.
  useEffect(() => {
    askQuestionRef.current = askQuestion;
  }, [askQuestion]);

  // ── Start recording ────────────────────────────────────────────────────────

  const handleStartRecording = useCallback(async () => {
    setError(null);
    setLiveTranscript("");
    setSegments([]);
    setRecordingDuration(0);
    setSessionInfo(null);
    fullTextRef.current = "";
    setRecordingState("starting");

    // Check auth before starting
    const token = getAccessToken();
    if (!token) {
      setError("Please log in to use Voice Q&A.");
      setRecordingState("idle");
      return;
    }

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
        chunkQueueRef.current.push(
          new Uint8Array(pcm16.buffer, pcm16.byteOffset, pcm16.byteLength),
        );
      };

      source.connect(processor);
      processor.connect(audioContext.destination);

      // 3. Create a MediaRecorder as a fallback / for potential replay
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
          // Accumulate final text
          const sep =
            fullTextRef.current && !fullTextRef.current.endsWith(" ")
              ? " "
              : "";
          fullTextRef.current = fullTextRef.current + sep + event.finalText;

          // Track segment (for potential debugging / display)
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
            if (
              prev.some((s) => s.segmentIndex === newSegment.segmentIndex)
            ) {
              return prev;
            }
            return [...prev, newSegment];
          });

          setLiveTranscript(fullTextRef.current);
        } else {
          // Show interim partial text appended to accumulated finals
          const partial = event.partialText;
          setLiveTranscript(
            fullTextRef.current
              ? `${fullTextRef.current} ${partial}`
              : partial,
          );
        }
      };

      client.onTranscriptionComplete = (event: TranscriptionCompleteEvent) => {
        // Use the server-provided full text if available
        if (event.fullText) {
          fullTextRef.current = event.fullText;
        }

        setLiveTranscript("");

        // If we were stopping (user clicked Stop), trigger the QA flow
        if (isStoppingRef.current) {
          isStoppingRef.current = false;
          const text = fullTextRef.current;
          fullTextRef.current = "";

          if (text && text.trim()) {
            // Fire and forget; askQuestion handles its own errors
            askQuestionRef.current?.(text.trim());
          }
        }
      };

      client.onError = (event: HubErrorEvent) => {
        setError(event.message);
      };

      await client.connect();
      hubClientRef.current = client;

      // 5. Start the streaming session
      const sessionRequest: StartSessionRequest = {
        language: language || null,
        enablePunctuation: true,
        hotwords: null,
        preferredProviderId: null,
        sampleRate: TARGET_SAMPLE_RATE,
      };
      await client.startSession(sessionRequest);

      // 6. Start sending audio chunks at a regular interval
      isRecordingRef.current = true;
      isStoppingRef.current = false;
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
        queue.length = 0;

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
          // Silently skip failed chunk sends
        }
      }, CHUNK_INTERVAL_MS);

      setRecordingState("recording");
    } catch (err: unknown) {
      setError(
        err instanceof Error
          ? err.message
          : "Failed to start recording. Check microphone permissions.",
      );
      setRecordingState("idle");
      stopAudioPipeline();
    }
  }, [language, stopAudioPipeline]);

  // ── Stop recording ─────────────────────────────────────────────────────────

  const handleStopRecording = useCallback(async () => {
    setRecordingState("stopping");
    isStoppingRef.current = true;

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

    // Wait for TranscriptionComplete, then disconnect.
    // If it doesn't fire within the timeout, use accumulated text.
    setTimeout(async () => {
      if (isStoppingRef.current) {
        // TranscriptionComplete didn't fire in time; use accumulated text.
        isStoppingRef.current = false;
        const text = fullTextRef.current;
        fullTextRef.current = "";
        setLiveTranscript("");

        if (text && text.trim()) {
          askQuestionRef.current?.(text.trim());
        }
      }

      if (hubClientRef.current) {
        try {
          await hubClientRef.current.disconnect();
        } catch {
          // best effort
        }
        hubClientRef.current = null;
      }
      setSessionInfo(null);
      setRecordingState("idle");
    }, TRANSCRIPTION_TIMEOUT_MS);
  }, [stopAudioPipeline]);

  // ── Replay an answer via TtsPlayer ─────────────────────────────────────────

  const handleReplay = useCallback((text: string) => {
    ttsPlayerRef.current?.synthesizeAndPlay(text);
  }, []);

  // ── Clear conversation ─────────────────────────────────────────────────────

  const handleClearConversation = useCallback(() => {
    setMessages([]);
    setSegments([]);
    setLiveTranscript("");
    setError(null);
    sessionIdRef.current = null;
  }, []);

  // ── Render ─────────────────────────────────────────────────────────────────

  const isRecording = recordingState === "recording";
  const isBusy =
    recordingState === "starting" || recordingState === "stopping";
  const isRecordButtonDisabled = isBusy || isAsking;

  return (
    <div className="mx-auto flex h-screen max-w-3xl flex-col">
      {/* ─── Header ──────────────────────────────────────────────────────────── */}
      <header className="shrink-0 border-b border-gray-200 bg-white px-4 py-3 dark:border-gray-800 dark:bg-gray-900">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-bold text-gray-900 dark:text-gray-100">
              Voice Q&amp;A
            </h1>
            <p className="text-xs text-gray-500 dark:text-gray-400">
              Speak your question, get a spoken answer
            </p>
          </div>
          <button
            type="button"
            onClick={() => setShowSettings((v) => !v)}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
          >
            {showSettings ? "Hide Settings" : "Settings"}
          </button>
        </div>
      </header>

      {/* ─── Settings panel (collapsible) ────────────────────────────────────── */}
      {showSettings && (
        <div className="shrink-0 border-b border-gray-200 bg-gray-50 px-4 py-3 dark:border-gray-800 dark:bg-gray-900/50">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {/* ASR Language */}
            <div>
              <label
                htmlFor="qa-language"
                className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Recognition Language
              </label>
              <select
                id="qa-language"
                value={language}
                onChange={(e) => setLanguage(e.target.value)}
                disabled={isRecording || isBusy}
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:cursor-not-allowed disabled:opacity-60 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              >
                {LANGUAGES.map((lang) => (
                  <option key={lang.value} value={lang.value}>
                    {lang.label}
                  </option>
                ))}
              </select>
            </div>

            {/* Auto-speak toggle */}
            <div className="flex items-end">
              <label className="flex cursor-pointer items-center gap-2">
                <button
                  type="button"
                  role="switch"
                  aria-checked={autoSpeak}
                  onClick={() => setAutoSpeak((v) => !v)}
                  className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors ${
                    autoSpeak
                      ? "bg-blue-600"
                      : "bg-gray-300 dark:bg-gray-700"
                  }`}
                >
                  <span
                    className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
                      autoSpeak ? "translate-x-4" : "translate-x-0.5"
                    }`}
                  />
                </button>
                <span className="text-sm text-gray-700 dark:text-gray-300">
                  Auto-speak answers
                </span>
              </label>
            </div>
          </div>

          {/* Clear conversation */}
          {messages.length > 0 && (
            <button
              type="button"
              onClick={handleClearConversation}
              className="mt-3 text-xs text-red-600 hover:underline dark:text-red-400"
            >
              Clear conversation
            </button>
          )}
        </div>
      )}

      {/* ─── Messages (scrollable) ───────────────────────────────────────────── */}
      <main className="flex-1 overflow-y-auto px-4 py-4 space-y-3">
        {messages.length === 0 && !liveTranscript && (
          <div className="flex h-full flex-col items-center justify-center text-center">
            <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-gray-100 dark:bg-gray-800">
              <svg
                className="h-8 w-8 text-gray-400"
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
            </div>
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Click the microphone button below to ask a question.
            </p>
          </div>
        )}

        {/* Conversation messages */}
        {messages.map((msg) => (
          <MessageBubble
            key={msg.id}
            message={msg}
            onReplay={handleReplay}
            isReplaying={isAsking && msg.role === "assistant"}
          />
        ))}

        {/* Live transcript during recording */}
        {(isRecording || isBusy) && liveTranscript && (
          <div className="flex justify-end">
            <div className="max-w-[80%] rounded-2xl rounded-br-sm bg-blue-100 px-4 py-2 dark:bg-blue-950/50">
              <p className="text-sm text-blue-900 dark:text-blue-200">
                {liveTranscript}
                <span className="ml-0.5 inline-block h-4 w-0.5 animate-pulse bg-blue-500 align-middle" />
              </p>
            </div>
          </div>
        )}

        {/* Thinking indicator */}
        {isAsking && (
          <div className="flex justify-start">
            <div className="rounded-2xl rounded-bl-sm bg-gray-100 px-4 py-3 dark:bg-gray-800">
              <div className="flex items-center gap-1.5">
                <span className="inline-block h-2 w-2 animate-bounce rounded-full bg-gray-400 [animation-delay:-0.3s]" />
                <span className="inline-block h-2 w-2 animate-bounce rounded-full bg-gray-400 [animation-delay:-0.15s]" />
                <span className="inline-block h-2 w-2 animate-bounce rounded-full bg-gray-400" />
              </div>
            </div>
          </div>
        )}

        {/* Auto-scroll anchor */}
        <div ref={messagesEndRef} />
      </main>

      {/* ─── Error banner ────────────────────────────────────────────────────── */}
      {error && (
        <div className="shrink-0 border-t border-red-200 bg-red-50 px-4 py-2 dark:border-red-900 dark:bg-red-950/30">
          <p className="text-xs text-red-700 dark:text-red-400">{error}</p>
        </div>
      )}

      {/* ─── TTS Player (compact) ────────────────────────────────────────────── */}
      <div className="shrink-0 border-t border-gray-200 bg-white px-4 py-2 dark:border-gray-800 dark:bg-gray-900">
        <TtsPlayer ref={ttsPlayerRef} compact />
      </div>

      {/* ─── Recording button (sticky bottom) ────────────────────────────────── */}
      <footer className="shrink-0 border-t border-gray-200 bg-white px-4 py-3 dark:border-gray-800 dark:bg-gray-900">
        <div className="flex items-center justify-center gap-4">
          {/* Recording duration / status */}
          {isRecording && (
            <span className="font-mono text-sm text-red-600 dark:text-red-400">
              {formatDuration(recordingDuration)}
            </span>
          )}

          {/* Record / Stop button */}
          <button
            type="button"
            onClick={
              isRecording || isBusy
                ? handleStopRecording
                : handleStartRecording
            }
            disabled={isRecordButtonDisabled}
            className={`flex h-16 w-16 flex-col items-center justify-center rounded-full shadow-lg transition-all disabled:cursor-not-allowed disabled:opacity-50 ${
              isRecording
                ? "bg-red-600 hover:bg-red-700"
                : "bg-blue-600 hover:bg-blue-700"
            }`}
            aria-label={isRecording ? "Stop recording" : "Start recording"}
          >
            {isBusy ? (
              <span className="inline-block h-6 w-6 animate-spin rounded-full border-4 border-white border-t-transparent" />
            ) : isRecording ? (
              <span className="h-5 w-5 rounded-sm bg-white" />
            ) : (
              <svg
                className="h-7 w-7 text-white"
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
            )}
          </button>

          {/* Session info */}
          {sessionInfo && (
            <div className="text-xs text-gray-500 dark:text-gray-400">
              <div>{sessionInfo.providerId}</div>
              <div>{sessionInfo.modelId}</div>
            </div>
          )}
        </div>

        {/* Status text */}
        <p className="mt-1 text-center text-xs text-gray-400 dark:text-gray-500">
          {isBusy
            ? "Please wait..."
            : isRecording
              ? "Recording... click to stop"
              : isAsking
                ? "Getting answer..."
                : "Click to speak your question"}
        </p>
      </footer>
    </div>
  );
}

// ─── Sub-components ──────────────────────────────────────────────────────────

/**
 * Renders a single chat message bubble. User messages are right-aligned
 * (blue), assistant messages are left-aligned (gray). Assistant messages
 * include a "Replay" button to re-synthesize TTS.
 */
function MessageBubble({
  message,
  onReplay,
  isReplaying,
}: {
  message: ChatMessage;
  onReplay: (text: string) => void;
  isReplaying: boolean;
}) {
  const isUser = message.role === "user";

  return (
    <div className={`flex ${isUser ? "justify-end" : "justify-start"}`}>
      <div
        className={`max-w-[85%] rounded-2xl px-4 py-2.5 ${
          isUser
            ? "rounded-br-sm bg-blue-600 text-white"
            : message.isError
              ? "rounded-bl-sm bg-red-100 text-red-900 dark:bg-red-950/50 dark:text-red-200"
              : "rounded-bl-sm bg-gray-100 text-gray-900 dark:bg-gray-800 dark:text-gray-100"
        }`}
      >
        {/* Role label + timestamp */}
        <div
          className={`mb-0.5 flex items-center gap-2 text-xs ${
            isUser
              ? "text-blue-100"
              : message.isError
                ? "text-red-500 dark:text-red-400"
                : "text-gray-500 dark:text-gray-400"
          }`}
        >
          <span className="font-medium">
            {isUser ? "You" : message.isError ? "Error" : "AI"}
          </span>
          <span>{formatTime(message.timestamp)}</span>
        </div>

        {/* Message text */}
        <p className="text-sm whitespace-pre-wrap">{message.text}</p>

        {/* Replay button for assistant messages (non-error) */}
        {!isUser && !message.isError && (
          <button
            type="button"
            onClick={() => onReplay(message.text)}
            disabled={isReplaying}
            className="mt-2 inline-flex items-center gap-1.5 rounded-md bg-white/20 px-2.5 py-1 text-xs font-medium transition-colors hover:bg-white/30 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-white/10 dark:hover:bg-white/20"
          >
            {isReplaying ? (
              <>
                <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-current border-t-transparent" />
                Speaking...
              </>
            ) : (
              <>
                <svg
                  className="h-3.5 w-3.5"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M15.536 8.464a5 5 0 010 7.072m2.828-9.9a9 9 0 010 12.728M5.586 15H4a1 1 0 01-1-1v-4a1 1 0 011-1h1.586l4.707-4.707C10.923 3.663 12 4.109 12 5v14c0 .891-1.077 1.337-1.707.707L5.586 15z"
                  />
                </svg>
                Replay
              </>
            )}
          </button>
        )}
      </div>
    </div>
  );
}
