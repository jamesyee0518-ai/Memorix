"use client";

/**
 * TtsPlayer
 *
 * Text-to-Speech playback component. Accepts text input, synthesizes audio
 * via the TTS API, and provides playback controls (play/pause, speed, progress).
 *
 * Features:
 *   - Text area for input
 *   - Language selector (zh, en, ja, ko, etc.)
 *   - Provider dropdown (populated from listTtsProviders)
 *   - Voice dropdown (populated from listVoices based on selected provider)
 *   - Synthesis speed slider (0.5 - 2.0, default 1.0)
 *   - Playback speed control (0.5x - 2.0x)
 *   - Synthesize button with loading spinner
 *   - HTML5 <audio> element with play/pause and seekable progress bar
 *   - Cost estimate display
 *   - Error handling
 *   - Dark mode support via Tailwind dark: classes
 *   - Compact mode for embedding in other pages
 *   - Imperative API via forwardRef: synthesizeAndPlay(text)
 *
 * Uses:
 *   - synthesize / listTtsProviders / listVoices from ../api/audioClient
 *   - TtsRequest / TtsResult / TtsProviderDescriptor / VoiceProfile from ../types/audio
 *   - HUB_BASE_URL from ../config, getAccessToken from ../storage/auth (for audio blob fetch)
 */

import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from "react";
import { listTtsProviders, listVoices, synthesize } from "../api/audioClient";
import { HUB_BASE_URL } from "../config";
import { getAccessToken } from "../storage/auth";
import type {
  DataClassification,
  TtsProviderDescriptor,
  TtsRequest,
  TtsResult,
  VoiceProfile,
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

const DEFAULT_OUTPUT_FORMAT = "mp3";
const DEFAULT_SAMPLE_RATE = 24000;
const DEFAULT_SPEED = 1.0;
const DEFAULT_PITCH = 1.0;

const PLAYBACK_SPEEDS = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

// ─── Types ───────────────────────────────────────────────────────────────────

export interface TtsPlayerProps {
  /** Initial text to synthesize (optional). */
  initialText?: string;
  /** Called when synthesis completes successfully. */
  onSynthesized?: (result: TtsResult) => void;
  /** Compact mode for embedding in other pages (collapses settings, hides text area). */
  compact?: boolean;
}

/**
 * Imperative handle exposed via forwardRef. Allows parent components to
 * programmatically trigger synthesis and auto-playback.
 */
export interface TtsPlayerHandle {
  /** Sets the text, synthesizes it, and auto-plays the resulting audio. */
  synthesizeAndPlay: (text: string) => Promise<void>;
}

// ─── Helper functions ────────────────────────────────────────────────────────

/**
 * Builds a fetchable URL from the TtsResult.outputFilePath.
 * If the path is already a full URL (http/https), it is returned as-is.
 * Otherwise, it is resolved relative to the server base URL (HUB_BASE_URL).
 */
function buildAudioUrl(outputFilePath: string): string {
  if (/^https?:\/\//.test(outputFilePath)) {
    return outputFilePath;
  }
  const path = outputFilePath.startsWith("/")
    ? outputFilePath
    : `/${outputFilePath}`;
  return `${HUB_BASE_URL}${path}`;
}

/**
 * Fetches the synthesized audio file with the auth token and returns a
 * blob object URL that can be used as an <audio> src. The caller is
 * responsible for revoking the object URL when done.
 */
async function fetchAudioAsBlobUrl(outputFilePath: string): Promise<string> {
  const url = buildAudioUrl(outputFilePath);
  const token = getAccessToken();
  const response = await fetch(url, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!response.ok) {
    throw new Error(`Failed to load audio file (${response.status})`);
  }
  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

/** Formats a number of seconds as mm:ss. */
function formatDuration(seconds: number): string {
  if (!isFinite(seconds) || seconds < 0) return "00:00";
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

// ─── Main component ──────────────────────────────────────────────────────────

const TtsPlayer = forwardRef<TtsPlayerHandle, TtsPlayerProps>(function TtsPlayer(
  { initialText = "", onSynthesized, compact = false },
  ref,
) {
  // ── Input state ────────────────────────────────────────────────────────────
  const [text, setText] = useState(initialText);

  // ── Settings state ─────────────────────────────────────────────────────────
  const [language, setLanguage] = useState("");
  const [selectedProviderId, setSelectedProviderId] = useState("");
  const [selectedVoiceId, setSelectedVoiceId] = useState("");
  const [speed, setSpeed] = useState(DEFAULT_SPEED);
  const [playbackSpeed, setPlaybackSpeed] = useState(DEFAULT_SPEED);

  // ── Data state ─────────────────────────────────────────────────────────────
  const [providers, setProviders] = useState<TtsProviderDescriptor[]>([]);
  const [voices, setVoices] = useState<VoiceProfile[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(false);
  const [loadingVoices, setLoadingVoices] = useState(false);

  // ── Synthesis state ────────────────────────────────────────────────────────
  const [isSynthesizing, setIsSynthesizing] = useState(false);
  const [ttsResult, setTtsResult] = useState<TtsResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  // ── Playback state ─────────────────────────────────────────────────────────
  const [audioUrl, setAudioUrl] = useState<string | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);

  // ── UI state ───────────────────────────────────────────────────────────────
  // In compact mode, settings are collapsed by default.
  const [showSettings, setShowSettings] = useState(!compact);

  // ── Refs ───────────────────────────────────────────────────────────────────
  const audioRef = useRef<HTMLAudioElement>(null);
  const audioUrlRef = useRef<string | null>(null);

  // ── Fetch TTS providers on mount ───────────────────────────────────────────
  useEffect(() => {
    let cancelled = false;
    setLoadingProviders(true);
    setError(null);
    listTtsProviders()
      .then((list) => {
        if (!cancelled) setProviders(list);
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(
            err instanceof Error ? err.message : "Failed to load TTS providers",
          );
        }
      })
      .finally(() => {
        if (!cancelled) setLoadingProviders(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // ── Fetch voices when provider changes ─────────────────────────────────────
  useEffect(() => {
    if (!selectedProviderId) {
      setVoices([]);
      setSelectedVoiceId("");
      return;
    }
    let cancelled = false;
    setLoadingVoices(true);
    listVoices(selectedProviderId)
      .then((list) => {
        if (!cancelled) {
          setVoices(list);
          // Reset voice selection if the current voice is not in the new list.
          if (
            selectedVoiceId &&
            !list.some((v) => v.voiceId === selectedVoiceId)
          ) {
            setSelectedVoiceId("");
          }
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(
            err instanceof Error ? err.message : "Failed to load voices",
          );
        }
      })
      .finally(() => {
        if (!cancelled) setLoadingVoices(false);
      });
    return () => {
      cancelled = true;
    };
  }, [selectedProviderId]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Update playback speed on the audio element ─────────────────────────────
  useEffect(() => {
    if (audioRef.current) {
      audioRef.current.playbackRate = playbackSpeed;
    }
  }, [playbackSpeed]);

  // ── Cleanup blob URL on unmount ────────────────────────────────────────────
  useEffect(() => {
    return () => {
      if (audioUrlRef.current) {
        URL.revokeObjectURL(audioUrlRef.current);
      }
    };
  }, []);

  // ── Selected provider descriptor (memoised via find) ───────────────────────
  const selectedProvider =
    providers.find((p) => p.providerId === selectedProviderId) ?? null;

  // ── Synthesize ─────────────────────────────────────────────────────────────

  const doSynthesize = useCallback(
    async (textToSynthesize: string) => {
      if (!textToSynthesize.trim()) {
        setError("Please enter some text to synthesize.");
        return;
      }

      setIsSynthesizing(true);
      setError(null);

      // Stop current playback before synthesizing new audio.
      if (audioRef.current) {
        audioRef.current.pause();
      }

      try {
        const request: TtsRequest = {
          text: textToSynthesize,
          language: language || null,
          voiceId: selectedVoiceId || null,
          speed,
          pitch: DEFAULT_PITCH,
          outputFormat:
            selectedProvider?.outputFormats[0] ?? DEFAULT_OUTPUT_FORMAT,
          sampleRate:
            selectedProvider?.supportedSampleRates[0] ?? DEFAULT_SAMPLE_RATE,
          dataClassification: "INTERNAL" as DataClassification,
          preferredExecutionMode: null,
          preferredCredentialMode: null,
          preferredProviderId: selectedProviderId || null,
          preferredModelId: selectedProvider?.modelId ?? null,
          fallbackPolicy: "PLATFORM_FALLBACK",
          userId: null,
          workspaceId: null,
          tenantId: null,
        };

        const result = await synthesize(request);
        setTtsResult(result);
        onSynthesized?.(result);

        // Revoke the previous blob URL before creating a new one.
        if (audioUrlRef.current) {
          URL.revokeObjectURL(audioUrlRef.current);
        }

        // Fetch the audio file as a blob and create an object URL.
        const blobUrl = await fetchAudioAsBlobUrl(result.outputFilePath);
        audioUrlRef.current = blobUrl;
        setAudioUrl(blobUrl);

        // Load the new audio and attempt auto-play.
        if (audioRef.current) {
          audioRef.current.src = blobUrl;
          audioRef.current.playbackRate = playbackSpeed;
          try {
            await audioRef.current.play();
            setIsPlaying(true);
          } catch {
            // Autoplay may be blocked by the browser; user can click play.
            setIsPlaying(false);
          }
        }
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : "Synthesis failed");
      } finally {
        setIsSynthesizing(false);
      }
    },
    [
      language,
      selectedVoiceId,
      speed,
      selectedProvider,
      selectedProviderId,
      playbackSpeed,
      onSynthesized,
    ],
  );

  // ── Imperative handle ──────────────────────────────────────────────────────

  useImperativeHandle(
    ref,
    () => ({
      synthesizeAndPlay: async (textToSpeak: string) => {
        setText(textToSpeak);
        await doSynthesize(textToSpeak);
      },
    }),
    [doSynthesize],
  );

  // ── Play / pause toggle ────────────────────────────────────────────────────

  const togglePlayPause = useCallback(() => {
    const audio = audioRef.current;
    if (!audio || !audioUrl) return;
    if (audio.paused) {
      audio.play().catch(() => {});
    } else {
      audio.pause();
    }
  }, [audioUrl]);

  // ── Seek (click on progress bar) ───────────────────────────────────────────

  const handleSeek = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      const audio = audioRef.current;
      if (!audio || !duration) return;
      const rect = e.currentTarget.getBoundingClientRect();
      const ratio = (e.clientX - rect.left) / rect.width;
      audio.currentTime = Math.max(0, Math.min(1, ratio)) * duration;
    },
    [duration],
  );

  // ── Audio element event handlers ───────────────────────────────────────────

  const handleTimeUpdate = () => {
    if (audioRef.current) setCurrentTime(audioRef.current.currentTime);
  };
  const handleLoadedMetadata = () => {
    if (audioRef.current) setDuration(audioRef.current.duration);
  };
  const handlePlay = () => setIsPlaying(true);
  const handlePause = () => setIsPlaying(false);
  const handleEnded = () => {
    setIsPlaying(false);
    setCurrentTime(0);
  };

  // ── Render ─────────────────────────────────────────────────────────────────

  const progress = duration > 0 ? (currentTime / duration) * 100 : 0;

  return (
    <div className={compact ? "space-y-3" : "space-y-4"}>
      {/* Hidden HTML5 audio element */}
      <audio
        ref={audioRef}
        onTimeUpdate={handleTimeUpdate}
        onLoadedMetadata={handleLoadedMetadata}
        onPlay={handlePlay}
        onPause={handlePause}
        onEnded={handleEnded}
      />

      {/* ─── Text input (full mode) ─────────────────────────────────────────── */}
      {!compact && (
        <div>
          <label
            htmlFor="tts-text"
            className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
          >
            Text to Synthesize
          </label>
          <textarea
            id="tts-text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            rows={4}
            placeholder="Enter text to convert to speech..."
            className="w-full resize-y rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
          />
        </div>
      )}

      {/* ─── Current text display (compact mode) ────────────────────────────── */}
      {compact && text && (
        <div className="rounded-md bg-gray-50 p-2 dark:bg-gray-900/50">
          <p className="text-xs text-gray-600 dark:text-gray-400 line-clamp-3">
            {text}
          </p>
        </div>
      )}

      {/* ─── Settings toggle (compact mode) ─────────────────────────────────── */}
      {compact && (
        <button
          type="button"
          onClick={() => setShowSettings((v) => !v)}
          className="text-xs text-blue-600 hover:underline dark:text-blue-400"
        >
          {showSettings ? "Hide TTS settings" : "Show TTS settings"}
        </button>
      )}

      {/* ─── Settings panel ─────────────────────────────────────────────────── */}
      {showSettings && (
        <div
          className={
            compact
              ? "space-y-3"
              : "grid grid-cols-1 gap-4 md:grid-cols-2"
          }
        >
          {/* Language */}
          <div>
            <label
              htmlFor="tts-language"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Language
            </label>
            <select
              id="tts-language"
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

          {/* Provider */}
          <div>
            <label
              htmlFor="tts-provider"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              TTS Provider
            </label>
            <select
              id="tts-provider"
              value={selectedProviderId}
              onChange={(e) => setSelectedProviderId(e.target.value)}
              disabled={loadingProviders}
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:cursor-not-allowed disabled:opacity-60 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            >
              <option value="">Auto (let router decide)</option>
              {providers.map((p) => (
                <option
                  key={`${p.providerId}:${p.modelId}`}
                  value={p.providerId}
                >
                  {p.providerId} ({p.modelId})
                </option>
              ))}
            </select>
            {loadingProviders && (
              <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                Loading providers...
              </p>
            )}
          </div>

          {/* Voice */}
          <div>
            <label
              htmlFor="tts-voice"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Voice
            </label>
            <select
              id="tts-voice"
              value={selectedVoiceId}
              onChange={(e) => setSelectedVoiceId(e.target.value)}
              disabled={loadingVoices || !selectedProviderId}
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:cursor-not-allowed disabled:opacity-60 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            >
              <option value="">Default voice</option>
              {voices.map((v) => (
                <option key={v.voiceId} value={v.voiceId}>
                  {v.name}
                  {v.language ? ` (${v.language})` : ""}
                  {v.gender ? ` - ${v.gender}` : ""}
                </option>
              ))}
            </select>
            {loadingVoices && (
              <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                Loading voices...
              </p>
            )}
          </div>

          {/* Synthesis speed slider */}
          <div>
            <label
              htmlFor="tts-speed"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Synthesis Speed: {speed.toFixed(1)}x
            </label>
            <input
              id="tts-speed"
              type="range"
              min={0.5}
              max={2.0}
              step={0.1}
              value={speed}
              onChange={(e) => setSpeed(parseFloat(e.target.value))}
              className="w-full accent-blue-600"
            />
            <div className="mt-0.5 flex justify-between text-xs text-gray-400 dark:text-gray-500">
              <span>0.5x</span>
              <span>1.0x</span>
              <span>2.0x</span>
            </div>
          </div>
        </div>
      )}

      {/* ─── Synthesize button (full mode) ──────────────────────────────────── */}
      {!compact && (
        <button
          type="button"
          onClick={() => doSynthesize(text)}
          disabled={isSynthesizing || !text.trim()}
          className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-700 dark:hover:bg-blue-600"
        >
          {isSynthesizing && (
            <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
          )}
          {isSynthesizing ? "Synthesizing..." : "Synthesize"}
        </button>
      )}

      {/* ─── Synthesizing indicator (compact mode) ──────────────────────────── */}
      {compact && isSynthesizing && (
        <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-gray-400">
          <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          Synthesizing speech...
        </div>
      )}

      {/* ─── Error message ──────────────────────────────────────────────────── */}
      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 dark:border-red-900 dark:bg-red-950/30">
          <p className="text-sm text-red-700 dark:text-red-400">{error}</p>
        </div>
      )}

      {/* ─── Playback controls ──────────────────────────────────────────────── */}
      {audioUrl && (
        <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-800 dark:bg-gray-900/50">
          {/* Play/pause + progress bar */}
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={togglePlayPause}
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-blue-600 text-white shadow-sm transition-colors hover:bg-blue-700"
              aria-label={isPlaying ? "Pause" : "Play"}
            >
              {isPlaying ? (
                <svg
                  className="h-5 w-5"
                  fill="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path d="M6 4h4v16H6V4zm8 0h4v16h-4V4z" />
                </svg>
              ) : (
                <svg
                  className="h-5 w-5"
                  fill="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path d="M8 5v14l11-7z" />
                </svg>
              )}
            </button>

            {/* Seekable progress bar */}
            <div className="flex-1">
              <div
                className="group relative h-2 cursor-pointer rounded-full bg-gray-200 dark:bg-gray-700"
                onClick={handleSeek}
              >
                <div
                  className="absolute h-2 rounded-full bg-blue-600 transition-all"
                  style={{ width: `${progress}%` }}
                />
              </div>
              <div className="mt-1 flex justify-between text-xs text-gray-500 dark:text-gray-400">
                <span className="font-mono">
                  {formatDuration(currentTime)}
                </span>
                <span className="font-mono">{formatDuration(duration)}</span>
              </div>
            </div>
          </div>

          {/* Playback speed + metadata */}
          <div className="mt-2 flex flex-wrap items-center gap-4">
            <div className="flex items-center gap-2">
              <label
                htmlFor="tts-playback-speed"
                className="text-xs text-gray-500 dark:text-gray-400"
              >
                Playback:
              </label>
              <select
                id="tts-playback-speed"
                value={playbackSpeed}
                onChange={(e) => setPlaybackSpeed(parseFloat(e.target.value))}
                className="rounded border border-gray-300 bg-white px-2 py-0.5 text-xs text-gray-900 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              >
                {PLAYBACK_SPEEDS.map((s) => (
                  <option key={s} value={s}>
                    {s}x
                  </option>
                ))}
              </select>
            </div>

            {ttsResult?.estimatedCost != null && (
              <span className="text-xs text-gray-500 dark:text-gray-400">
                Est. cost: ${ttsResult.estimatedCost.toFixed(4)}
              </span>
            )}

            {ttsResult && (
              <span className="text-xs text-gray-400 dark:text-gray-500">
                {ttsResult.providerId}/{ttsResult.modelId}
              </span>
            )}
          </div>
        </div>
      )}
    </div>
  );
});

export default TtsPlayer;
