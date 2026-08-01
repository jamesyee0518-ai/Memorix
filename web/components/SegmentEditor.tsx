"use client";

/**
 * SegmentEditor
 *
 * Renders a list of transcription segments with inline editing, version
 * lifecycle controls, and a per-segment version-history modal.
 *
 * Per-segment actions:
 *   - Editable text area + Save  -> editSegment()      (creates USER_EDITED)
 *   - Merge button               -> mergeSegment()      (creates MERGED)
 *   - Publish button             -> publishSegment()    (creates PUBLISHED)
 *   - History button             -> getSegmentVersions() (opens modal)
 *
 * A version badge (RAW_MODEL / USER_EDITED / MERGED / PUBLISHED / ...) is
 * shown next to each segment.
 *
 * Uses:
 *   - editSegment / mergeSegment / publishSegment / getSegmentVersions
 *     from ../api/audioClient
 *   - TranscriptionSegment / TranscriptionVersion / SegmentVersions
 *     from ../types/audio
 */

import { useCallback, useEffect, useState } from "react";
import {
  editSegment,
  getSegmentVersions,
  mergeSegment,
  publishSegment,
} from "../api/audioClient";
import {
  SegmentVersions,
  type TranscriptionSegment,
  type TranscriptionVersion,
} from "../types/audio";

export interface SegmentEditorProps {
  /** The segments to render and edit. */
  segments: TranscriptionSegment[];
  /**
   * Called whenever a segment's text or version changes after a successful
   * API operation. The parent should update its segment state.
   */
  onSegmentsChange?: (segments: TranscriptionSegment[]) => void;
  /** Disables all editing controls (e.g. while job is still running). */
  readOnly?: boolean;
}

// ─── Version badge styling ───────────────────────────────────────────────────

const versionBadgeConfig: Record<string, { label: string; className: string }> = {
  [SegmentVersions.RawModel]: {
    label: "RAW_MODEL",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  [SegmentVersions.PostProcessed]: {
    label: "POST_PROCESSED",
    className: "bg-blue-100 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300",
  },
  [SegmentVersions.ServerRetranscribed]: {
    label: "SERVER_RETRANSCRIBED",
    className: "bg-indigo-100 text-indigo-700 dark:bg-indigo-950/50 dark:text-indigo-300",
  },
  [SegmentVersions.UserEdited]: {
    label: "USER_EDITED",
    className: "bg-amber-100 text-amber-700 dark:bg-amber-950/50 dark:text-amber-300",
  },
  [SegmentVersions.Merged]: {
    label: "MERGED",
    className: "bg-purple-100 text-purple-700 dark:bg-purple-950/50 dark:text-purple-300",
  },
  [SegmentVersions.Published]: {
    label: "PUBLISHED",
    className: "bg-green-100 text-green-700 dark:bg-green-950/50 dark:text-green-300",
  },
};

function getVersionBadge(version: string) {
  return (
    versionBadgeConfig[version] ?? {
      label: version,
      className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
    }
  );
}

// ─── Timestamp formatting ────────────────────────────────────────────────────

function formatTimestamp(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const millis = ms % 1000;
  const padded = (n: number, len = 2) => String(n).padStart(len, "0");
  if (hours > 0) {
    return `${padded(hours)}:${padded(minutes)}:${padded(seconds)}.${padded(millis, 3)}`;
  }
  return `${padded(minutes)}:${padded(seconds)}.${padded(millis, 3)}`;
}

// ─── Main component ──────────────────────────────────────────────────────────

type ActionState = "idle" | "loading" | "success" | "error";

interface SegmentRowState {
  /** Local draft text being edited (may differ from saved segment.text). */
  draftText: string;
  /** Whether the draft differs from the saved text. */
  isDirty: boolean;
  saveState: ActionState;
  mergeState: ActionState;
  publishState: ActionState;
  /** Last error message from any action. */
  error: string | null;
  /** Whether the version-history modal is open for this segment. */
  showHistory: boolean;
}

export function SegmentEditor({
  segments,
  onSegmentsChange,
  readOnly = false,
}: SegmentEditorProps) {
  // Per-segment row state keyed by segment id.
  const [rowStates, setRowStates] = useState<Record<string, SegmentRowState>>({});

  // Initialise / sync row state when the segments prop changes.
  useEffect(() => {
    setRowStates((prev) => {
      const next: Record<string, SegmentRowState> = {};
      for (const seg of segments) {
        const existing = prev[seg.id];
        if (existing && existing.draftText !== undefined) {
          // Preserve the existing draft unless the server text changed and
          // the draft was not dirty.
          if (!existing.isDirty) {
            next[seg.id] = { ...existing, draftText: seg.text };
          } else {
            next[seg.id] = existing;
          }
        } else {
          next[seg.id] = {
            draftText: seg.text,
            isDirty: false,
            saveState: "idle",
            mergeState: "idle",
            publishState: "idle",
            error: null,
            showHistory: false,
          };
        }
      }
      return next;
    });
  }, [segments]);

  const updateRow = useCallback(
    (segmentId: string, patch: Partial<SegmentRowState>) => {
      setRowStates((prev) => ({
        ...prev,
        [segmentId]: { ...prev[segmentId], ...patch },
      }));
    },
    [],
  );

  const handleTextChange = useCallback(
    (segmentId: string, text: string, savedText: string) => {
      updateRow(segmentId, {
        draftText: text,
        isDirty: text !== savedText,
        saveState: "idle",
        error: null,
      });
    },
    [updateRow],
  );

  const handleSave = useCallback(
    async (segment: TranscriptionSegment) => {
      const row = rowStates[segment.id];
      if (!row || !row.isDirty) return;
      updateRow(segment.id, { saveState: "loading", error: null });
      try {
        const result = await editSegment(segment.id, { text: row.draftText });
        const updatedSegment: TranscriptionSegment = {
          ...segment,
          text: result.text,
          version: result.version,
        };
        const newSegments = segments.map((s) =>
          s.id === segment.id ? updatedSegment : s,
        );
        onSegmentsChange?.(newSegments);
        updateRow(segment.id, {
          draftText: result.text,
          isDirty: false,
          saveState: "success",
        });
        // Clear success indicator after a moment.
        setTimeout(() => updateRow(segment.id, { saveState: "idle" }), 2000);
      } catch (err: unknown) {
        updateRow(segment.id, {
          saveState: "error",
          error: err instanceof Error ? err.message : "Save failed",
        });
      }
    },
    [rowStates, segments, onSegmentsChange, updateRow],
  );

  const handleMerge = useCallback(
    async (segment: TranscriptionSegment) => {
      updateRow(segment.id, { mergeState: "loading", error: null });
      try {
        const result = await mergeSegment(segment.id);
        const updatedSegment: TranscriptionSegment = {
          ...segment,
          text: result.mergedText,
          version: SegmentVersions.Merged,
        };
        const newSegments = segments.map((s) =>
          s.id === segment.id ? updatedSegment : s,
        );
        onSegmentsChange?.(newSegments);
        updateRow(segment.id, {
          draftText: result.mergedText,
          isDirty: false,
          mergeState: "success",
        });
        setTimeout(() => updateRow(segment.id, { mergeState: "idle" }), 2000);
      } catch (err: unknown) {
        updateRow(segment.id, {
          mergeState: "error",
          error: err instanceof Error ? err.message : "Merge failed",
        });
      }
    },
    [segments, onSegmentsChange, updateRow],
  );

  const handlePublish = useCallback(
    async (segment: TranscriptionSegment) => {
      updateRow(segment.id, { publishState: "loading", error: null });
      try {
        await publishSegment(segment.id);
        const updatedSegment: TranscriptionSegment = {
          ...segment,
          version: SegmentVersions.Published,
        };
        const newSegments = segments.map((s) =>
          s.id === segment.id ? updatedSegment : s,
        );
        onSegmentsChange?.(newSegments);
        updateRow(segment.id, { publishState: "success" });
        setTimeout(() => updateRow(segment.id, { publishState: "idle" }), 2000);
      } catch (err: unknown) {
        updateRow(segment.id, {
          publishState: "error",
          error: err instanceof Error ? err.message : "Publish failed",
        });
      }
    },
    [segments, onSegmentsChange, updateRow],
  );

  const toggleHistory = useCallback(
    (segmentId: string, open: boolean) => {
      updateRow(segmentId, { showHistory: open });
    },
    [updateRow],
  );

  if (segments.length === 0) {
    return (
      <div className="rounded-lg border border-dashed border-gray-300 p-8 text-center dark:border-gray-700">
        <p className="text-sm text-gray-500 dark:text-gray-400">
          No segments available. Segments will appear once transcription completes.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {segments.map((segment, index) => {
        const row = rowStates[segment.id];
        if (!row) return null;
        const badge = getVersionBadge(segment.version);
        const isPublished = segment.version === SegmentVersions.Published;

        return (
          <div key={segment.id}>
            <SegmentRow
              segment={segment}
              index={index}
              row={row}
              badge={badge}
              readOnly={readOnly}
              isPublished={isPublished}
              onTextChange={handleTextChange}
              onSave={handleSave}
              onMerge={handleMerge}
              onPublish={handlePublish}
              onToggleHistory={toggleHistory}
            />
            {row.showHistory && (
              <VersionHistoryModal
                segment={segment}
                onClose={() => toggleHistory(segment.id, false)}
              />
            )}
          </div>
        );
      })}
    </div>
  );
}

// ─── Segment row sub-component ───────────────────────────────────────────────

interface SegmentRowProps {
  segment: TranscriptionSegment;
  index: number;
  row: SegmentRowState;
  badge: { label: string; className: string };
  readOnly: boolean;
  isPublished: boolean;
  onTextChange: (segmentId: string, text: string, savedText: string) => void;
  onSave: (segment: TranscriptionSegment) => void;
  onMerge: (segment: TranscriptionSegment) => void;
  onPublish: (segment: TranscriptionSegment) => void;
  onToggleHistory: (segmentId: string, open: boolean) => void;
}

function SegmentRow({
  segment,
  index,
  row,
  badge,
  readOnly,
  isPublished,
  onTextChange,
  onSave,
  onMerge,
  onPublish,
  onToggleHistory,
}: SegmentRowProps) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm dark:border-gray-800 dark:bg-gray-900">
      {/* Header: index, timestamps, speaker, confidence, version badge */}
      <div className="flex flex-wrap items-center gap-2 mb-3">
        <span className="inline-flex h-6 w-6 items-center justify-center rounded bg-gray-100 text-xs font-medium text-gray-600 dark:bg-gray-800 dark:text-gray-400">
          {index + 1}
        </span>
        <span className="font-mono text-xs text-gray-500 dark:text-gray-400">
          {formatTimestamp(segment.sourceStartMs)} → {formatTimestamp(segment.sourceEndMs)}
        </span>
        {segment.speakerKey && (
          <span className="inline-flex items-center rounded bg-indigo-100 px-1.5 py-0.5 text-xs text-indigo-700 dark:bg-indigo-950/50 dark:text-indigo-300">
            {segment.speakerKey}
          </span>
        )}
        <span className="text-xs text-gray-400 dark:text-gray-500">
          conf {(segment.confidence * 100).toFixed(0)}%
        </span>
        <span
          className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${badge.className}`}
        >
          {badge.label}
        </span>
      </div>

      {/* Editable text */}
      <textarea
        value={row.draftText}
        readOnly={readOnly}
        onChange={(e) => onTextChange(segment.id, e.target.value, segment.text)}
        rows={2}
        className="w-full resize-y rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 transition-colors focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:cursor-not-allowed disabled:opacity-70 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
        placeholder="Segment text..."
      />

      {/* Error message */}
      {row.error && (
        <p className="mt-2 text-xs text-red-600 dark:text-red-400">{row.error}</p>
      )}

      {/* Action buttons */}
      {!readOnly && (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <ActionButton
            label="Save"
            loadingLabel="Saving..."
            state={row.saveState}
            disabled={!row.isDirty}
            onClick={() => onSave(segment)}
            variant="primary"
          />
          <ActionButton
            label="Merge"
            loadingLabel="Merging..."
            state={row.mergeState}
            disabled={isPublished}
            onClick={() => onMerge(segment)}
            variant="secondary"
          />
          <ActionButton
            label="Publish"
            loadingLabel="Publishing..."
            state={row.publishState}
            disabled={isPublished}
            onClick={() => onPublish(segment)}
            variant="success"
          />
          <button
            type="button"
            onClick={() => onToggleHistory(segment.id, !row.showHistory)}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-600 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-400 dark:hover:bg-gray-800"
          >
            History
          </button>
        </div>
      )}
    </div>
  );
}

// ─── Action button sub-component ─────────────────────────────────────────────

function ActionButton({
  label,
  loadingLabel,
  state,
  disabled,
  onClick,
  variant,
}: {
  label: string;
  loadingLabel: string;
  state: ActionState;
  disabled?: boolean;
  onClick: () => void;
  variant: "primary" | "secondary" | "success";
}) {
  const variantClasses: Record<string, string> = {
    primary:
      "bg-blue-600 text-white hover:bg-blue-700 disabled:bg-blue-300 dark:disabled:bg-blue-900",
    secondary:
      "bg-gray-100 text-gray-700 hover:bg-gray-200 disabled:opacity-50 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700",
    success:
      "bg-green-600 text-white hover:bg-green-700 disabled:bg-green-300 dark:disabled:bg-green-900",
  };

  const labelToShow = state === "loading" ? loadingLabel : label;

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled || state === "loading"}
      className={`rounded-md px-3 py-1.5 text-xs font-medium transition-colors disabled:cursor-not-allowed ${variantClasses[variant]}`}
    >
      {state === "loading" && (
        <span className="mr-1 inline-block h-3 w-3 animate-spin rounded-full border-2 border-current border-t-transparent align-middle" />
      )}
      {state === "success" && <span className="mr-1">&#10003;</span>}
      {state === "error" && <span className="mr-1">&#10007;</span>}
      {labelToShow}
    </button>
  );
}

// ─── Version history modal ───────────────────────────────────────────────────

function VersionHistoryModal({
  segment,
  onClose,
}: {
  segment: TranscriptionSegment;
  onClose: () => void;
}) {
  const [versions, setVersions] = useState<TranscriptionVersion[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getSegmentVersions(segment.id)
      .then((list) => {
        if (!cancelled) setVersions(list);
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load versions");
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [segment.id]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      onClick={onClose}
    >
      <div
        className="max-h-[80vh] w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-xl dark:bg-gray-900"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Modal header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3 dark:border-gray-800">
          <div>
            <h3 className="text-base font-semibold text-gray-900 dark:text-gray-100">
              Version History
            </h3>
            <p className="text-xs text-gray-500 dark:text-gray-400">
              Segment {segment.segmentIndex + 1} · {segment.segmentUuid}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-md p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 dark:hover:bg-gray-800 dark:hover:text-gray-300"
            aria-label="Close"
          >
            <svg className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
              <path
                fillRule="evenodd"
                d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
                clipRule="evenodd"
              />
            </svg>
          </button>
        </div>

        {/* Modal body */}
        <div className="max-h-[60vh] overflow-y-auto p-5">
          {loading && (
            <div className="flex items-center justify-center py-8">
              <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
            </div>
          )}

          {error && (
            <p className="text-sm text-red-600 dark:text-red-400">{error}</p>
          )}

          {!loading && !error && versions.length === 0 && (
            <p className="py-8 text-center text-sm text-gray-500 dark:text-gray-400">
              No version history found.
            </p>
          )}

          {!loading && !error && versions.length > 0 && (
            <div className="space-y-3">
              {versions.map((version) => {
                const badge = getVersionBadge(version.version);
                return (
                  <div
                    key={version.id}
                    className="rounded-lg border border-gray-200 p-3 dark:border-gray-800"
                  >
                    <div className="mb-2 flex flex-wrap items-center gap-2">
                      <span
                        className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${badge.className}`}
                      >
                        {badge.label}
                      </span>
                      <span className="text-xs text-gray-400 dark:text-gray-500">
                        {version.providerId}/{version.modelId}
                      </span>
                      {version.createdBy && (
                        <span className="text-xs text-gray-400 dark:text-gray-500">
                          by {version.createdBy}
                        </span>
                      )}
                      <span className="ml-auto text-xs text-gray-400 dark:text-gray-500">
                        {new Date(version.createdAt).toLocaleString()}
                      </span>
                    </div>
                    <p className="text-sm text-gray-800 dark:text-gray-200">
                      {version.text}
                    </p>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default SegmentEditor;
