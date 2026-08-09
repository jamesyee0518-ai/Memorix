"use client";

/**
 * ModelRegistryPage
 *
 * Admin page for managing the audio model registry. Lets operators:
 *   - List all registered models with provider, capability, and health status
 *   - Filter models by capability (transcription / synthesis / vad / punctuation)
 *   - Add a new model registration
 *   - Toggle enable/disable per model
 *   - Delete (soft-delete) a model
 *   - View health check status and last check timestamp
 *
 * Uses:
 *   - listModels / createModel / updateModel / deleteModel
 *     from ../api/audioClient
 *   - ModelRegistry / RegisterModelRequest / ModelRegistryStatuses /
 *     AudioCapabilities from ../types/audio
 */

import { useCallback, useEffect, useState } from "react";
import {
  createModel,
  deleteModel,
  listModels,
  updateModel,
} from "../../api/audioClient";
import {
  AudioCapabilities,
  ModelRegistryStatuses,
  type ModelRegistry,
  type RegisterModelRequest,
} from "../../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const CAPABILITY_FILTERS: { value: string; label: string }[] = [
  { value: "", label: "All Capabilities" },
  { value: AudioCapabilities.Transcription, label: "Transcription" },
  { value: AudioCapabilities.Synthesis, label: "Synthesis" },
  { value: AudioCapabilities.Vad, label: "VAD" },
  { value: AudioCapabilities.Punctuation, label: "Punctuation" },
];

const CAPABILITY_LABELS: Record<string, string> = {
  [AudioCapabilities.Transcription]: "Transcription",
  [AudioCapabilities.Synthesis]: "Synthesis",
  [AudioCapabilities.Vad]: "VAD",
  [AudioCapabilities.Punctuation]: "Punctuation",
  [AudioCapabilities.Diarization]: "Diarization",
  [AudioCapabilities.Correction]: "Correction",
};

const HEALTH_BADGE_CONFIG: Record<
  string,
  { label: string; className: string; dot: string }
> = {
  [ModelRegistryStatuses.Healthy]: {
    label: "Healthy",
    className:
      "bg-green-100 text-green-700 dark:bg-green-950/50 dark:text-green-300",
    dot: "bg-green-500",
  },
  [ModelRegistryStatuses.Degraded]: {
    label: "Degraded",
    className:
      "bg-amber-100 text-amber-700 dark:bg-amber-950/50 dark:text-amber-300",
    dot: "bg-amber-500",
  },
  [ModelRegistryStatuses.Unhealthy]: {
    label: "Unhealthy",
    className: "bg-red-100 text-red-700 dark:bg-red-950/50 dark:text-red-300",
    dot: "bg-red-500",
  },
  [ModelRegistryStatuses.Unknown]: {
    label: "Unknown",
    className: "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
    dot: "bg-gray-400",
  },
};

const EXECUTION_MODE_OPTIONS = [
  "LOCAL_DEVICE",
  "LOCAL_LAN_NODE",
  "MEMORIX_CLOUD",
  "THIRD_PARTY_CLOUD",
];

const CREDENTIAL_MODE_OPTIONS = [
  "NO_CREDENTIAL",
  "USER_BYOK",
  "TENANT_BYOK",
  "PLATFORM_MANAGED",
];

const PRICING_UNIT_OPTIONS = ["", "REQUEST", "SECOND", "MINUTE", "TOKEN"];

// ─── Default form state ─────────────────────────────────────────────────────

function getDefaultForm(): RegisterModelRequest {
  return {
    providerId: "",
    modelId: "",
    displayName: null,
    capability: AudioCapabilities.Transcription,
    executionModes: "MEMORIX_CLOUD",
    credentialModes: "USER_BYOK",
    supportedLanguages: "",
    maxFileBytes: null,
    maxAudioDurationMs: null,
    acceptedMimeTypes: "audio/wav,audio/mpeg,audio/mp4",
    supportsStreaming: false,
    supportsBatch: true,
    supportsVad: false,
    supportsPunctuation: false,
    supportsDiarization: false,
    supportsHotwords: false,
    supportsWordTimestamp: false,
    supportsSegmentTimestamp: false,
    sendsAudioOffDevice: true,
    storesProviderData: false,
    pricingUnit: null,
    dataRegion: null,
    retentionPolicy: null,
    isEnabled: true,
    healthStatus: null,
  };
}

// ─── Main component ──────────────────────────────────────────────────────────

export default function ModelRegistryPage() {
  // Model list state
  const [models, setModels] = useState<ModelRegistry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filter state
  const [capabilityFilter, setCapabilityFilter] = useState("");
  const [enabledOnly, setEnabledOnly] = useState(false);

  // Add form state
  const [showAddForm, setShowAddForm] = useState(false);
  const [form, setForm] = useState<RegisterModelRequest>(getDefaultForm());
  const [formSubmitting, setFormSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Per-model action states (toggle, delete)
  const [actionLoading, setActionLoading] = useState<Record<string, boolean>>(
    {},
  );

  // Delete confirmation state
  const [deleteTarget, setDeleteTarget] = useState<ModelRegistry | null>(null);

  // ─── Fetch models ──────────────────────────────────────────────────────────

  const fetchModels = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await listModels({
        capability: capabilityFilter || undefined,
        enabledOnly: enabledOnly || undefined,
      });
      setModels(list);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load models");
    } finally {
      setLoading(false);
    }
  }, [capabilityFilter, enabledOnly]);

  useEffect(() => {
    fetchModels();
  }, [fetchModels]);

  // ─── Toggle enable/disable ─────────────────────────────────────────────────

  const handleToggle = async (model: ModelRegistry) => {
    setActionLoading((prev) => ({ ...prev, [model.id]: true }));
    try {
      const updated = await updateModel(model.id, {
        ...model,
        isEnabled: !model.isEnabled,
      });
      setModels((prev) =>
        prev.map((m) => (m.id === updated.id ? updated : m)),
      );
    } catch (err: unknown) {
      setError(
        err instanceof Error ? err.message : "Failed to toggle model",
      );
    } finally {
      setActionLoading((prev) => ({ ...prev, [model.id]: false }));
    }
  };

  // ─── Delete model ──────────────────────────────────────────────────────────

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    setActionLoading((prev) => ({ ...prev, [deleteTarget.id]: true }));
    try {
      await deleteModel(deleteTarget.id);
      setModels((prev) => prev.filter((m) => m.id !== deleteTarget.id));
      setDeleteTarget(null);
    } catch (err: unknown) {
      setError(
        err instanceof Error ? err.message : "Failed to delete model",
      );
    } finally {
      setActionLoading((prev) => ({ ...prev, [deleteTarget.id]: false }));
    }
  };

  // ─── Add model ─────────────────────────────────────────────────────────────

  const handleAddModel = async () => {
    if (!form.providerId || !form.modelId) {
      setFormError("Provider ID and Model ID are required.");
      return;
    }
    setFormSubmitting(true);
    setFormError(null);
    try {
      const created = await createModel(form);
      setModels((prev) => [...prev, created]);
      setForm(getDefaultForm());
      setShowAddForm(false);
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : "Failed to add model");
    } finally {
      setFormSubmitting(false);
    }
  };

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Model Registry
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Manage registered audio models across all capabilities. Toggle
          availability, monitor health, and register new models.
        </p>
      </div>

      {/* ─── Filters ────────────────────────────────────────────────────────── */}
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <label
            htmlFor="model-capability-filter"
            className="sr-only"
          >
            Filter by capability
          </label>
          <select
            id="model-capability-filter"
            value={capabilityFilter}
            onChange={(e) => setCapabilityFilter(e.target.value)}
            className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
          >
            {CAPABILITY_FILTERS.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
          <input
            type="checkbox"
            checked={enabledOnly}
            onChange={(e) => setEnabledOnly(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500 dark:border-gray-600 dark:bg-gray-950"
          />
          Enabled only
        </label>
        <button
          type="button"
          onClick={fetchModels}
          className="text-xs text-blue-600 hover:underline dark:text-blue-400"
        >
          Refresh
        </button>
        <div className="ml-auto">
          <button
            type="button"
            onClick={() => setShowAddForm((v) => !v)}
            className="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-blue-700 dark:bg-blue-600 dark:hover:bg-blue-500"
          >
            {showAddForm ? "Cancel" : "+ Add Model"}
          </button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/30 dark:text-red-300">
          {error}
        </div>
      )}

      {/* ─── Add model form ─────────────────────────────────────────────────── */}
      {showAddForm && (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Register New Model
          </h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <FormField label="Provider ID *" htmlFor="model-provider">
              <input
                id="model-provider"
                type="text"
                value={form.providerId}
                onChange={(e) =>
                  setForm((f) => ({ ...f, providerId: e.target.value }))
                }
                placeholder="e.g. openai, azure, aliyun"
                className={inputClass}
              />
            </FormField>
            <FormField label="Model ID *" htmlFor="model-id">
              <input
                id="model-id"
                type="text"
                value={form.modelId}
                onChange={(e) =>
                  setForm((f) => ({ ...f, modelId: e.target.value }))
                }
                placeholder="e.g. whisper-large-v3"
                className={inputClass}
              />
            </FormField>
            <FormField label="Display Name" htmlFor="model-display">
              <input
                id="model-display"
                type="text"
                value={form.displayName ?? ""}
                onChange={(e) =>
                  setForm((f) => ({
                    ...f,
                    displayName: e.target.value || null,
                  }))
                }
                placeholder="e.g. Whisper Large v3"
                className={inputClass}
              />
            </FormField>
            <FormField label="Capability" htmlFor="model-capability">
              <select
                id="model-capability"
                value={form.capability}
                onChange={(e) =>
                  setForm((f) => ({ ...f, capability: e.target.value }))
                }
                className={inputClass}
              >
                {Object.values(AudioCapabilities).map((cap) => (
                  <option key={cap} value={cap}>
                    {CAPABILITY_LABELS[cap] ?? cap}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Execution Modes" htmlFor="model-exec">
              <select
                id="model-exec"
                value={form.executionModes ?? ""}
                onChange={(e) =>
                  setForm((f) => ({ ...f, executionModes: e.target.value }))
                }
                className={inputClass}
              >
                {EXECUTION_MODE_OPTIONS.map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Credential Modes" htmlFor="model-cred">
              <select
                id="model-cred"
                value={form.credentialModes ?? ""}
                onChange={(e) =>
                  setForm((f) => ({ ...f, credentialModes: e.target.value }))
                }
                className={inputClass}
              >
                {CREDENTIAL_MODE_OPTIONS.map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Supported Languages" htmlFor="model-langs">
              <input
                id="model-langs"
                type="text"
                value={form.supportedLanguages ?? ""}
                onChange={(e) =>
                  setForm((f) => ({
                    ...f,
                    supportedLanguages: e.target.value || null,
                  }))
                }
                placeholder="e.g. zh-CN,en-US (empty = all)"
                className={inputClass}
              />
            </FormField>
            <FormField label="Pricing Unit" htmlFor="model-pricing">
              <select
                id="model-pricing"
                value={form.pricingUnit ?? ""}
                onChange={(e) =>
                  setForm((f) => ({
                    ...f,
                    pricingUnit: e.target.value || null,
                  }))
                }
                className={inputClass}
              >
                {PRICING_UNIT_OPTIONS.map((u) => (
                  <option key={u} value={u}>
                    {u || "None"}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Accepted MIME Types" htmlFor="model-mime">
              <input
                id="model-mime"
                type="text"
                value={form.acceptedMimeTypes ?? ""}
                onChange={(e) =>
                  setForm((f) => ({
                    ...f,
                    acceptedMimeTypes: e.target.value || null,
                  }))
                }
                placeholder="audio/wav,audio/mpeg"
                className={inputClass}
              />
            </FormField>
          </div>

          {/* Capability flags */}
          <div className="mt-4 flex flex-wrap gap-4">
            {[
              { key: "supportsStreaming", label: "Streaming" },
              { key: "supportsBatch", label: "Batch" },
              { key: "supportsVad", label: "VAD" },
              { key: "supportsPunctuation", label: "Punctuation" },
              { key: "supportsDiarization", label: "Diarization" },
              { key: "supportsHotwords", label: "Hotwords" },
              { key: "supportsWordTimestamp", label: "Word Timestamp" },
              {
                key: "supportsSegmentTimestamp",
                label: "Segment Timestamp",
              },
              { key: "sendsAudioOffDevice", label: "Sends Audio Off-Device" },
              { key: "storesProviderData", label: "Stores Provider Data" },
              { key: "isEnabled", label: "Enabled" },
            ].map((flag) => (
              <label
                key={flag.key}
                className="flex items-center gap-1.5 text-xs text-gray-700 dark:text-gray-300"
              >
                <input
                  type="checkbox"
                  checked={
                    form[flag.key as keyof RegisterModelRequest] as boolean
                  }
                  onChange={(e) =>
                    setForm((f) => ({
                      ...f,
                      [flag.key]: e.target.checked,
                    }))
                  }
                  className="h-3.5 w-3.5 rounded border-gray-300 text-blue-600 focus:ring-blue-500 dark:border-gray-600 dark:bg-gray-950"
                />
                {flag.label}
              </label>
            ))}
          </div>

          {formError && (
            <p className="mt-3 text-sm text-red-600 dark:text-red-400">
              {formError}
            </p>
          )}

          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={handleAddModel}
              disabled={formSubmitting}
              className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-600 dark:hover:bg-blue-500"
            >
              {formSubmitting ? (
                <span className="flex items-center gap-2">
                  <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                  Saving...
                </span>
              ) : (
                "Register Model"
              )}
            </button>
            <button
              type="button"
              onClick={() => {
                setShowAddForm(false);
                setFormError(null);
                setForm(getDefaultForm());
              }}
              className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* ─── Model list ──────────────────────────────────────────────────────── */}
      <div className="overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm dark:border-gray-800 dark:bg-gray-900">
        {loading ? (
          <div className="flex items-center justify-center py-12">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : models.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12 text-center">
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No models found
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              {capabilityFilter
                ? `No models for ${CAPABILITY_LABELS[capabilityFilter] ?? capabilityFilter}.`
                : "Register a new model to get started."}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-gray-200 bg-gray-50 text-xs uppercase tracking-wide text-gray-500 dark:border-gray-800 dark:bg-gray-950 dark:text-gray-400">
                <tr>
                  <th className="px-4 py-3 font-medium">Provider / Model</th>
                  <th className="px-4 py-3 font-medium">Capability</th>
                  <th className="px-4 py-3 font-medium">Health</th>
                  <th className="px-4 py-3 font-medium">Enabled</th>
                  <th className="px-4 py-3 font-medium">Last Check</th>
                  <th className="px-4 py-3 text-right font-medium">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-gray-800">
                {models.map((model) => (
                  <ModelRow
                    key={model.id}
                    model={model}
                    isLoading={!!actionLoading[model.id]}
                    onToggle={handleToggle}
                    onDelete={setDeleteTarget}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ─── Delete confirmation modal ──────────────────────────────────────── */}
      {deleteTarget && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
          onClick={() => setDeleteTarget(null)}
        >
          <div
            className="w-full max-w-md rounded-xl bg-white p-5 shadow-xl dark:bg-gray-900"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="text-base font-semibold text-gray-900 dark:text-gray-100">
              Delete Model
            </h3>
            <p className="mt-2 text-sm text-gray-600 dark:text-gray-400">
              Are you sure you want to delete{" "}
              <span className="font-medium text-gray-900 dark:text-gray-100">
                {deleteTarget.displayName || deleteTarget.modelId}
              </span>{" "}
              ({deleteTarget.providerId})? This will disable it immediately and
              cannot be undone.
            </p>
            <div className="mt-4 flex justify-end gap-3">
              <button
                type="button"
                onClick={() => setDeleteTarget(null)}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleConfirmDelete}
                disabled={!!actionLoading[deleteTarget.id]}
                className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {actionLoading[deleteTarget.id] ? "Deleting..." : "Delete"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Model row sub-component ─────────────────────────────────────────────────

function ModelRow({
  model,
  isLoading,
  onToggle,
  onDelete,
}: {
  model: ModelRegistry;
  isLoading: boolean;
  onToggle: (model: ModelRegistry) => void;
  onDelete: (model: ModelRegistry) => void;
}) {
  const health =
    HEALTH_BADGE_CONFIG[model.healthStatus] ?? {
      label: model.healthStatus || "unknown",
      className:
        "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
      dot: "bg-gray-400",
    };

  return (
    <tr className="hover:bg-gray-50 dark:hover:bg-gray-950/50">
      <td className="px-4 py-3">
        <div className="font-medium text-gray-900 dark:text-gray-100">
          {model.displayName || model.modelId}
        </div>
        <div className="text-xs text-gray-500 dark:text-gray-400">
          <span className="font-mono">{model.providerId}</span>
          {" / "}
          <span className="font-mono">{model.modelId}</span>
        </div>
      </td>
      <td className="px-4 py-3">
        <span className="inline-flex items-center rounded bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-700 dark:bg-blue-950/40 dark:text-blue-300">
          {CAPABILITY_LABELS[model.capability] ?? model.capability}
        </span>
      </td>
      <td className="px-4 py-3">
        <span
          className={`inline-flex items-center gap-1.5 rounded px-2 py-0.5 text-xs font-medium ${health.className}`}
        >
          <span className={`inline-block h-1.5 w-1.5 rounded-full ${health.dot}`} />
          {health.label}
        </span>
      </td>
      <td className="px-4 py-3">
        <button
          type="button"
          onClick={() => onToggle(model)}
          disabled={isLoading}
          className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors disabled:opacity-50 ${
            model.isEnabled
              ? "bg-blue-600"
              : "bg-gray-300 dark:bg-gray-700"
          }`}
        >
          <span
            className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition-transform ${
              model.isEnabled ? "translate-x-4.5" : "translate-x-0.5"
            }`}
          />
        </button>
      </td>
      <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
        {model.lastHealthCheckAt
          ? new Date(model.lastHealthCheckAt).toLocaleString()
          : "Never"}
      </td>
      <td className="px-4 py-3 text-right">
        <button
          type="button"
          onClick={() => onDelete(model)}
          disabled={isLoading}
          className="rounded-md border border-red-300 px-3 py-1 text-xs font-medium text-red-600 transition-colors hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-red-900 dark:text-red-400 dark:hover:bg-red-950/30"
        >
          Delete
        </button>
      </td>
    </tr>
  );
}

// ─── Shared form helpers ─────────────────────────────────────────────────────

const inputClass =
  "w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100";

function FormField({
  label,
  htmlFor,
  children,
}: {
  label: string;
  htmlFor: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label
        htmlFor={htmlFor}
        className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300"
      >
        {label}
      </label>
      {children}
    </div>
  );
}
