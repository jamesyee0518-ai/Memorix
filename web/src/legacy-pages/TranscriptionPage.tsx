"use client";

/**
 * TranscriptionPage
 *
 * The main audio transcription page. Provides:
 *   1. An audio upload form (drag-and-drop + file picker) with provider,
 *      language, and capability options (VAD, diarization, punctuation,
 *      hotwords).
 *   2. A transcription job list with status indicators.
 *   3. A job detail view showing segments with timestamps and the
 *      SegmentEditor for inline editing.
 *
 * Uses:
 *   - uploadAudio / listJobs / getJobStatus / cancelJob from ../api/audioClient
 *   - ProviderSelector from ../components/ProviderSelector
 *   - SegmentEditor from ../components/SegmentEditor
 *   - Types from ../types/audio
 */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { cancelJob, getJobStatus, listJobs, uploadAudio } from "../../api/audioClient";
import { ProviderSelector } from "../../components/ProviderSelector";
import { SegmentEditor } from "../../components/SegmentEditor";
import {
  DataClassification,
  FallbackPolicies,
  TranscriptionJobStatuses,
  type AsrRoutingContext,
  type AudioUploadParams,
  type TranscriptionJob,
  type TranscriptionSegment,
  type TranscriptionStatusResponse,
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

const DATA_CLASSIFICATIONS: { value: DataClassification; label: string }[] = [
  { value: "PUBLIC", label: "Public" },
  { value: "INTERNAL", label: "Internal" },
  { value: "PRIVATE", label: "Private" },
  { value: "STRICT_LOCAL", label: "Strict Local" },
];

const STATUS_BADGE_CONFIG: Record<string, { label: string; className: string }> = {
  [TranscriptionJobStatuses.Pending]: {
    label: "Pending",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  [TranscriptionJobStatuses.Running]: {
    label: "Running",
    className: "bg-blue-100 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300",
  },
  [TranscriptionJobStatuses.Completed]: {
    label: "Completed",
    className: "bg-green-100 text-green-700 dark:bg-green-950/50 dark:text-green-300",
  },
  [TranscriptionJobStatuses.Failed]: {
    label: "Failed",
    className: "bg-red-100 text-red-700 dark:bg-red-950/50 dark:text-red-300",
  },
  [TranscriptionJobStatuses.Cancelled]: {
    label: "Cancelled",
    className: "bg-amber-100 text-amber-700 dark:bg-amber-950/50 dark:text-amber-300",
  },
};

// ─── Main component ──────────────────────────────────────────────────────────

export default function TranscriptionPage() {
  // Upload form state
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState("");
  const [language, setLanguage] = useState("");
  const [providerId, setProviderId] = useState<string | null>(null);
  const [enableVad, setEnableVad] = useState(true);
  const [enableDiarization, setEnableDiarization] = useState(false);
  const [enablePunctuation, setEnablePunctuation] = useState(true);
  const [hotwords, setHotwords] = useState("");
  const [dataClassification, setDataClassification] =
    useState<DataClassification>("PRIVATE");
  const [autoStart, setAutoStart] = useState(true);
  const [isDragOver, setIsDragOver] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [uploadSuccess, setUploadSuccess] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Job list state
  const [jobs, setJobs] = useState<TranscriptionJob[]>([]);
  const [jobsLoading, setJobsLoading] = useState(true);
  const [jobsError, setJobsError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>("");

  // Selected job detail state
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [jobDetail, setJobDetail] = useState<TranscriptionStatusResponse | null>(
    null,
  );
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [segments, setSegments] = useState<TranscriptionSegment[]>([]);

  // ─── Fetch jobs ────────────────────────────────────────────────────────────

  const fetchJobs = useCallback(async () => {
    setJobsLoading(true);
    setJobsError(null);
    try {
      const list = await listJobs({
        status: statusFilter || undefined,
        limit: 50,
      });
      setJobs(list);
    } catch (err: unknown) {
      setJobsError(err instanceof Error ? err.message : "Failed to load jobs");
    } finally {
      setJobsLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => {
    fetchJobs();
  }, [fetchJobs]);

  // Auto-refresh job list every 5s when there are running/pending jobs
  useEffect(() => {
    const hasActive = jobs.some(
      (j) =>
        j.status === TranscriptionJobStatuses.Running ||
        j.status === TranscriptionJobStatuses.Pending,
    );
    if (!hasActive) return;
    const interval = setInterval(fetchJobs, 5000);
    return () => clearInterval(interval);
  }, [jobs, fetchJobs]);

  // ─── Fetch job detail ──────────────────────────────────────────────────────

  const fetchJobDetail = useCallback(async (jobId: string) => {
    setDetailLoading(true);
    setDetailError(null);
    try {
      const detail = await getJobStatus(jobId);
      setJobDetail(detail);
      setSegments(detail.segments ?? []);
    } catch (err: unknown) {
      setDetailError(err instanceof Error ? err.message : "Failed to load job");
    } finally {
      setDetailLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!selectedJobId) return;
    fetchJobDetail(selectedJobId);
  }, [selectedJobId, fetchJobDetail]);

  // Auto-refresh detail while running
  useEffect(() => {
    if (!jobDetail || jobDetail.status !== TranscriptionJobStatuses.Running) return;
    const interval = setInterval(() => {
      if (selectedJobId) fetchJobDetail(selectedJobId);
    }, 3000);
    return () => clearInterval(interval);
  }, [jobDetail, selectedJobId, fetchJobDetail]);

  // ─── Upload handlers ───────────────────────────────────────────────────────

  const handleFileSelect = (selectedFile: File | null | undefined) => {
    if (!selectedFile) return;
    setFile(selectedFile);
    setUploadError(null);
    setUploadSuccess(null);
    if (!title) {
      setTitle(selectedFile.name.replace(/\.[^.]+$/, ""));
    }
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    const droppedFile = e.dataTransfer.files?.[0];
    if (droppedFile) handleFileSelect(droppedFile);
  };

  const handleUpload = async () => {
    if (!file) {
      setUploadError("Please select an audio file first.");
      return;
    }
    setUploading(true);
    setUploadError(null);
    setUploadSuccess(null);
    try {
      const params: AudioUploadParams = {
        file,
        title: title || null,
        topicId: null,
        language: language || null,
        enableVad,
        enableSpeakerDiarization: enableDiarization,
        enablePunctuation,
        hotwords: hotwords.trim()
          ? hotwords.split(/[,\n]/).map((h) => h.trim()).filter(Boolean)
          : null,
        dataClassification,
        preferredProviderId: providerId,
        preferredModelId: null,
        fallbackPolicy: FallbackPolicies.PlatformFallback,
        autoStart,
      };
      const result = await uploadAudio(params);
      setUploadSuccess(
        `Uploaded successfully. Job ID: ${result.transcriptionJobId} (status: ${result.status})`,
      );
      setFile(null);
      setTitle("");
      setHotwords("");
      if (fileInputRef.current) fileInputRef.current.value = "";
      // Refresh job list and select the new job
      await fetchJobs();
      setSelectedJobId(result.transcriptionJobId);
    } catch (err: unknown) {
      setUploadError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setUploading(false);
    }
  };

  const handleCancelJob = async (jobId: string) => {
    try {
      await cancelJob(jobId);
      await fetchJobs();
      if (selectedJobId === jobId) fetchJobDetail(jobId);
    } catch (err: unknown) {
      setDetailError(err instanceof Error ? err.message : "Cancel failed");
    }
  };

  // ─── Routing context for ProviderSelector ─────────────────────────────────

  const routingContext = useMemo<AsrRoutingContext | undefined>(() => {
    if (!file) return undefined;
    return {
      dataClassification,
      preferredExecutionMode: null,
      preferredCredentialMode: null,
      preferredProviderId: providerId,
      preferredModelId: null,
      language: language || null,
      enableVad,
      enableSpeakerDiarization: enableDiarization,
      enablePunctuation,
      enableHotwords: hotwords.trim().length > 0,
      enableWordTimestamp: false,
      fileSizeBytes: file.size,
      durationMs: 0,
      mimeType: file.type,
      fallbackPolicy: FallbackPolicies.PlatformFallback,
      userId: null,
      workspaceId: null,
      tenantId: null,
    };
  }, [
    file,
    dataClassification,
    providerId,
    language,
    enableVad,
    enableDiarization,
    enablePunctuation,
    hotwords,
  ]);

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Audio Transcription
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Upload audio files and manage transcription jobs with provider routing,
          VAD, diarization, and hotwords.
        </p>
      </div>

      {/* ─── Upload form ────────────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
          Upload Audio
        </h2>

        {/* Drag-and-drop zone */}
        <div
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
          onClick={() => fileInputRef.current?.click()}
          className={`flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed p-8 text-center transition-colors ${
            isDragOver
              ? "border-blue-500 bg-blue-50 dark:bg-blue-950/30"
              : "border-gray-300 hover:border-gray-400 dark:border-gray-700 dark:hover:border-gray-600"
          }`}
        >
          <input
            ref={fileInputRef}
            type="file"
            accept="audio/*"
            className="hidden"
            onChange={(e) => handleFileSelect(e.target.files?.[0])}
          />
          {file ? (
            <div className="space-y-1">
              <p className="text-sm font-medium text-gray-900 dark:text-gray-100">
                {file.name}
              </p>
              <p className="text-xs text-gray-500 dark:text-gray-400">
                {(file.size / 1024 / 1024).toFixed(2)} MB · {file.type || "unknown type"}
              </p>
              <p className="text-xs text-blue-600 dark:text-blue-400">
                Click to change file
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              <svg
                className="mx-auto h-10 w-10 text-gray-400"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.5}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 16.5V9.75m0 0l3 3m-3-3l-3 3M6.75 19.5a4.5 4.5 0 01-1.41-8.775 5.25 5.25 0 0110.233-2.33 3 3 0 013.758 3.848A3.752 3.752 0 0118 19.5H6.75z"
                />
              </svg>
              <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
                Drag and drop an audio file here
              </p>
              <p className="text-xs text-gray-500 dark:text-gray-400">
                or click to browse · WAV, MP3, M4A, FLAC, OGG
              </p>
            </div>
          )}
        </div>

        {/* Form fields */}
        <div className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2">
          {/* Title */}
          <div>
            <label
              htmlFor="audio-title"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Title
            </label>
            <input
              id="audio-title"
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Optional title for this audio"
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            />
          </div>

          {/* Language */}
          <div>
            <label
              htmlFor="language-select"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Language
            </label>
            <select
              id="language-select"
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

          {/* Data classification */}
          <div>
            <label
              htmlFor="classification-select"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Data Classification
            </label>
            <select
              id="classification-select"
              value={dataClassification}
              onChange={(e) =>
                setDataClassification(e.target.value as DataClassification)
              }
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            >
              {DATA_CLASSIFICATIONS.map((dc) => (
                <option key={dc.value} value={dc.value}>
                  {dc.label}
                </option>
              ))}
            </select>
          </div>

          {/* Hotwords */}
          <div>
            <label
              htmlFor="hotwords-input"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Hotwords
            </label>
            <input
              id="hotwords-input"
              type="text"
              value={hotwords}
              onChange={(e) => setHotwords(e.target.value)}
              placeholder="Comma-separated proper nouns, terms..."
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            />
          </div>
        </div>

        {/* Provider selector (full width) */}
        <div className="mt-4">
          <ProviderSelector
            value={providerId}
            onChange={setProviderId}
            routingContext={routingContext}
          />
        </div>

        {/* Toggles */}
        <div className="mt-4 flex flex-wrap gap-4">
          <Toggle label="VAD" checked={enableVad} onChange={setEnableVad} />
          <Toggle
            label="Speaker Diarization"
            checked={enableDiarization}
            onChange={setEnableDiarization}
          />
          <Toggle
            label="Punctuation"
            checked={enablePunctuation}
            onChange={setEnablePunctuation}
          />
          <Toggle
            label="Auto-start transcription"
            checked={autoStart}
            onChange={setAutoStart}
          />
        </div>

        {/* Error / success messages */}
        {uploadError && (
          <p className="mt-3 text-sm text-red-600 dark:text-red-400">{uploadError}</p>
        )}
        {uploadSuccess && (
          <p className="mt-3 text-sm text-green-600 dark:text-green-400">
            {uploadSuccess}
          </p>
        )}

        {/* Upload button */}
        <div className="mt-4">
          <button
            type="button"
            onClick={handleUpload}
            disabled={!file || uploading}
            className="rounded-lg bg-blue-600 px-5 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-600 dark:hover:bg-blue-500"
          >
            {uploading ? (
              <span className="flex items-center gap-2">
                <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                Uploading...
              </span>
            ) : (
              "Upload & Transcribe"
            )}
          </button>
        </div>
      </div>

      {/* ─── Job list + detail (side by side on large screens) ─────────────── */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Job list */}
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              Transcription Jobs
            </h2>
            <button
              type="button"
              onClick={fetchJobs}
              className="text-xs text-blue-600 hover:underline dark:text-blue-400"
            >
              Refresh
            </button>
          </div>

          {/* Status filter */}
          <div className="mb-3">
            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-xs text-gray-900 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            >
              <option value="">All statuses</option>
              <option value={TranscriptionJobStatuses.Pending}>Pending</option>
              <option value={TranscriptionJobStatuses.Running}>Running</option>
              <option value={TranscriptionJobStatuses.Completed}>Completed</option>
              <option value={TranscriptionJobStatuses.Failed}>Failed</option>
              <option value={TranscriptionJobStatuses.Cancelled}>Cancelled</option>
            </select>
          </div>

          {jobsLoading ? (
            <div className="flex items-center justify-center py-8">
              <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
            </div>
          ) : jobsError ? (
            <p className="text-sm text-red-600 dark:text-red-400">{jobsError}</p>
          ) : jobs.length === 0 ? (
            <p className="py-8 text-center text-sm text-gray-500 dark:text-gray-400">
              No transcription jobs yet. Upload an audio file to get started.
            </p>
          ) : (
            <div className="max-h-[500px] space-y-2 overflow-y-auto">
              {jobs.map((job) => {
                const status = STATUS_BADGE_CONFIG[job.status] ?? {
                  label: job.status,
                  className:
                    "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
                };
                const isSelected = job.id === selectedJobId;
                return (
                  <button
                    key={job.id}
                    type="button"
                    onClick={() => setSelectedJobId(job.id)}
                    className={`w-full rounded-lg border p-3 text-left transition-colors ${
                      isSelected
                        ? "border-blue-500 bg-blue-50 dark:border-blue-700 dark:bg-blue-950/30"
                        : "border-gray-200 hover:bg-gray-50 dark:border-gray-800 dark:hover:bg-gray-800/50"
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="truncate text-sm font-medium text-gray-900 dark:text-gray-100">
                        Job {job.id.slice(0, 8)}...
                      </span>
                      <span
                        className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${status.className}`}
                      >
                        {status.label}
                      </span>
                    </div>
                    <div className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-gray-500 dark:text-gray-400">
                      <span>Provider: {job.providerId}</span>
                      {job.language && <span>Lang: {job.language}</span>}
                      {job.segmentCount !== null && (
                        <span>Segments: {job.segmentCount}</span>
                      )}
                      <span>{new Date(job.createdAt).toLocaleDateString()}</span>
                    </div>
                    {job.status === TranscriptionJobStatuses.Running && (
                      <div className="mt-2 h-1 w-full overflow-hidden rounded-full bg-gray-200 dark:bg-gray-700">
                        <div className="h-full w-1/3 animate-pulse rounded-full bg-blue-500" />
                      </div>
                    )}
                  </button>
                );
              })}
            </div>
          )}
        </div>

        {/* Job detail */}
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Job Detail
          </h2>

          {!selectedJobId ? (
            <p className="py-8 text-center text-sm text-gray-500 dark:text-gray-400">
              Select a job from the list to view details.
            </p>
          ) : detailLoading ? (
            <div className="flex items-center justify-center py-8">
              <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
            </div>
          ) : detailError ? (
            <p className="text-sm text-red-600 dark:text-red-400">{detailError}</p>
          ) : jobDetail ? (
            <div className="space-y-4">
              {/* Job metadata */}
              <div className="rounded-lg bg-gray-50 p-3 dark:bg-gray-900/50">
                <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs">
                  <MetaItem label="Job ID" value={jobDetail.jobId} />
                  <MetaItem
                    label="Status"
                    value={
                      STATUS_BADGE_CONFIG[jobDetail.status]?.label ?? jobDetail.status
                    }
                  />
                  <MetaItem
                    label="Provider"
                    value={jobDetail.providerId ?? "-"}
                  />
                  <MetaItem label="Model" value={jobDetail.modelId ?? "-"} />
                  <MetaItem
                    label="Segments"
                    value={String(jobDetail.segmentCount ?? 0)}
                  />
                  <MetaItem
                    label="Est. Cost"
                    value={
                      jobDetail.estimatedCost != null
                        ? `$${jobDetail.estimatedCost.toFixed(4)}`
                        : "-"
                    }
                  />
                  <MetaItem
                    label="Created"
                    value={new Date(jobDetail.createdAt).toLocaleString()}
                  />
                  <MetaItem
                    label="Completed"
                    value={
                      jobDetail.completedAt
                        ? new Date(jobDetail.completedAt).toLocaleString()
                        : "-"
                    }
                  />
                </div>

                {jobDetail.errorMessage && (
                  <p className="mt-2 text-xs text-red-600 dark:text-red-400">
                    Error: {jobDetail.errorMessage}
                  </p>
                )}

                {(jobDetail.status === TranscriptionJobStatuses.Running ||
                  jobDetail.status === TranscriptionJobStatuses.Pending) && (
                  <button
                    type="button"
                    onClick={() => handleCancelJob(jobDetail.jobId)}
                    className="mt-2 rounded-md bg-red-50 px-3 py-1 text-xs font-medium text-red-600 transition-colors hover:bg-red-100 dark:bg-red-950/30 dark:text-red-400 dark:hover:bg-red-950/50"
                  >
                    Cancel Job
                  </button>
                )}
              </div>

              {/* Segments */}
              <div>
                <h3 className="mb-2 text-sm font-medium text-gray-700 dark:text-gray-300">
                  Segments ({segments.length})
                </h3>
                <SegmentEditor
                  segments={segments}
                  onSegmentsChange={setSegments}
                  readOnly={
                    jobDetail.status !== TranscriptionJobStatuses.Completed
                  }
                />
              </div>
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}

// ─── Helper sub-components ───────────────────────────────────────────────────

function Toggle({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-center gap-2">
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onChange(!checked)}
        className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors ${
          checked
            ? "bg-blue-600"
            : "bg-gray-300 dark:bg-gray-700"
        }`}
      >
        <span
          className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
            checked ? "translate-x-4" : "translate-x-0.5"
          }`}
        />
      </button>
      <span className="text-sm text-gray-700 dark:text-gray-300">{label}</span>
    </label>
  );
}

function MetaItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="font-medium text-gray-500 dark:text-gray-400">
        {label}:
      </span>{" "}
      <span className="text-gray-900 dark:text-gray-100">{value}</span>
    </div>
  );
}
