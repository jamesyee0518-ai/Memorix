"use client";

/**
 * ByokConfigPage
 *
 * Bring-Your-Own-Key (BYOK) credential configuration page. Lets users:
 *   - List existing provider credentials (provider, status, label)
 *   - Add a new credential (provider, API key, label)
 *   - Delete (disable) a credential
 *   - Verify a credential makes a lightweight test call
 *   - See status indicators (active / disabled / expired)
 *   - API keys are masked in the display (the backend never returns the
 *     encrypted secret, so this page also masks any user-entered key).
 *
 * Uses:
 *   - listCredentials / createCredential / deleteCredential / verifyCredential
 *     from ../api/audioClient
 *   - ProviderCredential / StoreCredentialRequest / CredentialStatuses
 *     from ../types/audio
 */

import { useCallback, useEffect, useState } from "react";
import {
  createCredential,
  deleteCredential,
  listCredentials,
  verifyCredential,
} from "../api/audioClient";
import {
  CredentialStatuses,
  type ProviderCredential,
} from "../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const COMMON_PROVIDERS: { value: string; label: string }[] = [
  { value: "openai", label: "OpenAI (Whisper)" },
  { value: "azure", label: "Azure Speech" },
  { value: "aliyun", label: "Aliyun (Paraformer)" },
  { value: "tencent", label: "Tencent Cloud ASR" },
  { value: "volcengine", label: "Volcengine ASR" },
  { value: "baidu", label: "Baidu AI Cloud ASR" },
  { value: "xfyun", label: "iFlytek (Xfyun)" },
  { value: "google", label: "Google Cloud Speech" },
  { value: "aws", label: "AWS Transcribe" },
];

const CREDENTIAL_TYPES: { value: string; label: string }[] = [
  { value: "api_key", label: "API Key" },
  { value: "bearer_token", label: "Bearer Token" },
  { value: "oauth2", label: "OAuth 2.0" },
  { value: "access_key", label: "Access Key + Secret" },
];

const STATUS_BADGE_CONFIG: Record<string, { label: string; className: string; dot: string }> = {
  [CredentialStatuses.Active]: {
    label: "Active",
    className:
      "bg-green-100 text-green-700 dark:bg-green-950/50 dark:text-green-300",
    dot: "bg-green-500",
  },
  [CredentialStatuses.Disabled]: {
    label: "Disabled",
    className: "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
    dot: "bg-gray-400",
  },
  [CredentialStatuses.Expired]: {
    label: "Expired",
    className: "bg-red-100 text-red-700 dark:bg-red-950/50 dark:text-red-300",
    dot: "bg-red-500",
  },
};

// ─── Mask API key ────────────────────────────────────────────────────────────

/**
 * Masks a secret string for display, showing only the first 4 and last 4
 * characters. Short strings are fully masked.
 */
function maskSecret(secret: string): string {
  if (!secret) return "";
  if (secret.length <= 8) {
    return "*".repeat(secret.length);
  }
  return `${secret.slice(0, 4)}${"*".repeat(Math.max(8, secret.length - 8))}${secret.slice(-4)}`;
}

// ─── Main component ──────────────────────────────────────────────────────────

interface CredentialActionState {
  loading: boolean;
  error: string | null;
  success: string | null;
}

export default function ByokConfigPage() {
  // Credential list state
  const [credentials, setCredentials] = useState<ProviderCredential[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Add credential form state
  const [showAddForm, setShowAddForm] = useState(false);
  const [formProviderId, setFormProviderId] = useState("");
  const [formCredentialType, setFormCredentialType] = useState("api_key");
  const [formSecret, setFormSecret] = useState("");
  const [formLabel, setFormLabel] = useState("");
  const [formExpiresAt, setFormExpiresAt] = useState("");
  const [showSecret, setShowSecret] = useState(false);
  const [formSubmitting, setFormSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Per-credential action states (verify, delete)
  const [actionStates, setActionStates] = useState<
    Record<string, { verify?: CredentialActionState; delete?: CredentialActionState }>
  >({});

  // Delete confirmation state
  const [deleteTarget, setDeleteTarget] = useState<ProviderCredential | null>(null);

  // ─── Fetch credentials ────────────────────────────────────────────────────

  const fetchCredentials = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await listCredentials();
      setCredentials(list);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load credentials");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchCredentials();
  }, [fetchCredentials]);

  // ─── Update action state helper ────────────────────────────────────────────

  const updateAction = useCallback(
    (
      credentialId: string,
      action: "verify" | "delete",
      patch: Partial<CredentialActionState>,
    ) => {
      setActionStates((prev) => ({
        ...prev,
        [credentialId]: {
          ...prev[credentialId],
          [action]: {
            loading: false,
            error: null,
            success: null,
            ...prev[credentialId]?.[action],
            ...patch,
          },
        },
      }));
    },
    [],
  );

  // ─── Add credential ────────────────────────────────────────────────────────

  const handleAddCredential = async () => {
    if (!formProviderId) {
      setFormError("Please select a provider.");
      return;
    }
    if (!formSecret) {
      setFormError("Please enter an API key or secret.");
      return;
    }
    setFormSubmitting(true);
    setFormError(null);
    try {
      await createCredential({
        providerId: formProviderId,
        credentialType: formCredentialType,
        secret: formSecret,
        tenantId: null,
        label: formLabel || null,
        expiresAt: formExpiresAt || null,
      });
      // Reset form
      setFormProviderId("");
      setFormCredentialType("api_key");
      setFormSecret("");
      setFormLabel("");
      setFormExpiresAt("");
      setShowSecret(false);
      setShowAddForm(false);
      await fetchCredentials();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : "Failed to add credential");
    } finally {
      setFormSubmitting(false);
    }
  };

  // ─── Verify credential ─────────────────────────────────────────────────────

  const handleVerify = async (credential: ProviderCredential) => {
    updateAction(credential.id, "verify", {
      loading: true,
      error: null,
      success: null,
    });
    try {
      const result = await verifyCredential(credential.id);
      updateAction(credential.id, "verify", {
        loading: false,
        success: result.valid
          ? "Credential verified successfully."
          : "Credential is not valid.",
        error: result.valid ? null : "Verification failed: credential is not valid.",
      });
    } catch (err: unknown) {
      updateAction(credential.id, "verify", {
        loading: false,
        error: err instanceof Error ? err.message : "Verification failed",
      });
    }
  };

  // ─── Delete credential ─────────────────────────────────────────────────────

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    updateAction(deleteTarget.id, "delete", {
      loading: true,
      error: null,
      success: null,
    });
    try {
      await deleteCredential(deleteTarget.id);
      updateAction(deleteTarget.id, "delete", {
        loading: false,
        success: "Credential deleted.",
      });
      setDeleteTarget(null);
      await fetchCredentials();
    } catch (err: unknown) {
      updateAction(deleteTarget.id, "delete", {
        loading: false,
        error: err instanceof Error ? err.message : "Delete failed",
      });
    }
  };

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          BYOK Credential Configuration
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Manage your own API keys for ASR and TTS providers. Keys are encrypted
          at rest with AES-GCM and never exposed after storage.
        </p>
      </div>

      {/* ─── Credential list ───────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            Credentials ({credentials.length})
          </h2>
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={fetchCredentials}
              className="text-xs text-blue-600 hover:underline dark:text-blue-400"
            >
              Refresh
            </button>
            <button
              type="button"
              onClick={() => setShowAddForm((v) => !v)}
              className="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-blue-700 dark:bg-blue-600 dark:hover:bg-blue-500"
            >
              {showAddForm ? "Cancel" : "+ Add Credential"}
            </button>
          </div>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-8">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : error ? (
          <p className="text-sm text-red-600 dark:text-red-400">{error}</p>
        ) : credentials.length === 0 && !showAddForm ? (
          <div className="flex flex-col items-center justify-center py-12 text-center">
            <svg
              className="mb-3 h-12 w-12 text-gray-300 dark:text-gray-700"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.5}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M15.75 5.25a3 3 0 013 3m3 0a6 6 0 01-7.029 5.912c-.563-.097-1.159.026-1.563.43L10.5 17.25H8.25v2.25H6v2.25H2.25v-2.818c0-.597.237-1.17.659-1.591l6.995-6.995c.404-.404.527-1 .43-1.563A6.001 6.001 0 0118.75 5.25z"
              />
            </svg>
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No credentials configured
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              Add a provider API key to use BYOK mode for transcription and TTS.
            </p>
          </div>
        ) : (
          <div className="space-y-3">
            {credentials.map((credential) => (
              <CredentialRow
                key={credential.id}
                credential={credential}
                actionState={actionStates[credential.id]}
                onVerify={handleVerify}
                onDelete={setDeleteTarget}
              />
            ))}
          </div>
        )}
      </div>

      {/* ─── Add credential form ────────────────────────────────────────────── */}
      {showAddForm && (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Add New Credential
          </h2>

          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {/* Provider */}
            <div>
              <label
                htmlFor="byok-provider"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Provider *
              </label>
              <input
                id="byok-provider"
                type="text"
                list="byok-provider-list"
                value={formProviderId}
                onChange={(e) => setFormProviderId(e.target.value)}
                placeholder="e.g. openai, azure, aliyun"
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              />
              <datalist id="byok-provider-list">
                {COMMON_PROVIDERS.map((p) => (
                  <option key={p.value} value={p.value}>
                    {p.label}
                  </option>
                ))}
              </datalist>
            </div>

            {/* Credential type */}
            <div>
              <label
                htmlFor="byok-type"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Credential Type
              </label>
              <select
                id="byok-type"
                value={formCredentialType}
                onChange={(e) => setFormCredentialType(e.target.value)}
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              >
                {CREDENTIAL_TYPES.map((ct) => (
                  <option key={ct.value} value={ct.value}>
                    {ct.label}
                  </option>
                ))}
              </select>
            </div>

            {/* API key / secret */}
            <div className="md:col-span-2">
              <label
                htmlFor="byok-secret"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                API Key / Secret *
              </label>
              <div className="relative">
                <input
                  id="byok-secret"
                  type={showSecret ? "text" : "password"}
                  value={formSecret}
                  onChange={(e) => setFormSecret(e.target.value)}
                  placeholder="Paste your API key here..."
                  className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 pr-20 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
                />
                <button
                  type="button"
                  onClick={() => setShowSecret((v) => !v)}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-xs text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
                >
                  {showSecret ? "Hide" : "Show"}
                </button>
              </div>
              {formSecret && (
                <p className="mt-1 text-xs text-gray-400 dark:text-gray-500">
                  Will be stored as: <code className="font-mono">{maskSecret(formSecret)}</code>
                </p>
              )}
            </div>

            {/* Label */}
            <div>
              <label
                htmlFor="byok-label"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Label (optional)
              </label>
              <input
                id="byok-label"
                type="text"
                value={formLabel}
                onChange={(e) => setFormLabel(e.target.value)}
                placeholder="e.g. Production OpenAI key"
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              />
            </div>

            {/* Expires at */}
            <div>
              <label
                htmlFor="byok-expires"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Expires At (optional)
              </label>
              <input
                id="byok-expires"
                type="datetime-local"
                value={formExpiresAt}
                onChange={(e) => setFormExpiresAt(e.target.value)}
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
              />
            </div>
          </div>

          {formError && (
            <p className="mt-3 text-sm text-red-600 dark:text-red-400">{formError}</p>
          )}

          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={handleAddCredential}
              disabled={formSubmitting}
              className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-600 dark:hover:bg-blue-500"
            >
              {formSubmitting ? (
                <span className="flex items-center gap-2">
                  <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                  Saving...
                </span>
              ) : (
                "Save Credential"
              )}
            </button>
            <button
              type="button"
              onClick={() => {
                setShowAddForm(false);
                setFormError(null);
              }}
              className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

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
              Delete Credential
            </h3>
            <p className="mt-2 text-sm text-gray-600 dark:text-gray-400">
              Are you sure you want to delete the credential for{" "}
              <span className="font-medium text-gray-900 dark:text-gray-100">
                {deleteTarget.providerId}
              </span>
              {deleteTarget.label ? ` (${deleteTarget.label})` : ""}? This will
              disable it immediately. This action cannot be undone.
            </p>

            {actionStates[deleteTarget.id]?.delete?.error && (
              <p className="mt-2 text-sm text-red-600 dark:text-red-400">
                {actionStates[deleteTarget.id].delete?.error}
              </p>
            )}

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
                disabled={actionStates[deleteTarget.id]?.delete?.loading}
                className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {actionStates[deleteTarget.id]?.delete?.loading ? (
                  <span className="flex items-center gap-2">
                    <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                    Deleting...
                  </span>
                ) : (
                  "Delete"
                )}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Credential row sub-component ────────────────────────────────────────────

function CredentialRow({
  credential,
  actionState,
  onVerify,
  onDelete,
}: {
  credential: ProviderCredential;
  actionState?: {
    verify?: CredentialActionState;
    delete?: CredentialActionState;
  };
  onVerify: (credential: ProviderCredential) => void;
  onDelete: (credential: ProviderCredential) => void;
}) {
  const status =
    STATUS_BADGE_CONFIG[credential.status] ?? {
      label: credential.status,
      className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
      dot: "bg-gray-400",
    };

  const isActive = credential.status === CredentialStatuses.Active;
  const verifyState = actionState?.verify;
  const deleteState = actionState?.delete;

  return (
    <div className="rounded-lg border border-gray-200 p-4 dark:border-gray-800">
      {/* Top row: provider, label, status */}
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className={`inline-block h-2 w-2 rounded-full ${status.dot}`} />
            <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
              {credential.providerId}
            </span>
            <span
              className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${status.className}`}
            >
              {status.label}
            </span>
          </div>
          {credential.label && (
            <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">
              {credential.label}
            </p>
          )}
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => onVerify(credential)}
            disabled={!isActive || verifyState?.loading}
            className="rounded-md border border-gray-300 px-3 py-1 text-xs font-medium text-gray-700 transition-colors hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
          >
            {verifyState?.loading ? (
              <span className="flex items-center gap-1">
                <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-current border-t-transparent" />
                Verifying...
              </span>
            ) : (
              "Verify"
            )}
          </button>
          <button
            type="button"
            onClick={() => onDelete(credential)}
            disabled={deleteState?.loading}
            className="rounded-md border border-red-300 px-3 py-1 text-xs font-medium text-red-600 transition-colors hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-red-900 dark:text-red-400 dark:hover:bg-red-950/30"
          >
            Delete
          </button>
        </div>
      </div>

      {/* Metadata row */}
      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-gray-500 dark:text-gray-400">
        <span>
          Type: <span className="font-mono">{credential.credentialType}</span>
        </span>
        <span>
          Owner: {credential.ownerType}
        </span>
        <span>
          Last verified:{" "}
          {credential.lastVerifiedAt
            ? new Date(credential.lastVerifiedAt).toLocaleString()
            : "Never"}
        </span>
        {credential.expiresAt && (
          <span>
            Expires: {new Date(credential.expiresAt).toLocaleDateString()}
          </span>
        )}
        <span>
          Created: {new Date(credential.createdAt).toLocaleDateString()}
        </span>
      </div>

      {/* Masked key indicator */}
      <div className="mt-2 flex items-center gap-2">
        <span className="text-xs text-gray-400 dark:text-gray-500">Key:</span>
        <code className="rounded bg-gray-100 px-2 py-0.5 font-mono text-xs text-gray-600 dark:bg-gray-800 dark:text-gray-400">
          {isActive ? "••••••••••••••••" : "(disabled)"}
        </code>
      </div>

      {/* Verify feedback */}
      {verifyState?.success && (
        <p className="mt-2 text-xs text-green-600 dark:text-green-400">
          {verifyState.success}
        </p>
      )}
      {verifyState?.error && (
        <p className="mt-2 text-xs text-red-600 dark:text-red-400">
          {verifyState.error}
        </p>
      )}
    </div>
  );
}
