"use client";

/**
 * PromptRegistryPage
 *
 * Prompt Registry management page. Lets operators:
 *   - Search for a prompt by key and view its version history
 *   - Create a new prompt version (draft)
 *   - Publish or archive prompt versions
 *   - View and create A/B test configurations
 *
 * Uses:
 *   - listPrompts / createPrompt / publishPrompt / archivePrompt
 *   - listTests / createTest
 *     from ../api/audioClient
 *   - PromptRegistry / CreatePromptRequest / PromptABTest /
 *     CreateABTestRequest / PromptRegistryStatuses / PromptABTestStatuses
 *     from ../types/audio
 */

import { useCallback, useEffect, useState } from "react";
import {
  archivePrompt,
  createPrompt,
  createTest,
  listPrompts,
  listTests,
  publishPrompt,
} from "../../api/audioClient";
import {
  PromptABTestStatuses,
  PromptRegistryStatuses,
  type CreateABTestRequest,
  type CreatePromptRequest,
  type PromptABTest,
  type PromptRegistry,
} from "../../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const COMMON_PROMPT_KEYS = [
  "summary.default",
  "summary.brief",
  "summary.detailed",
  "entity.extract",
  "entity.normalize",
  "topic.suggest",
  "qa.answer",
];

const STATUS_BADGE_CONFIG: Record<
  string,
  { label: string; className: string }
> = {
  [PromptRegistryStatuses.Draft]: {
    label: "Draft",
    className:
      "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
  },
  [PromptRegistryStatuses.Published]: {
    label: "Published",
    className:
      "bg-green-100 text-green-700 dark:bg-green-950/50 dark:text-green-300",
  },
  [PromptRegistryStatuses.Archived]: {
    label: "Archived",
    className:
      "bg-amber-100 text-amber-700 dark:bg-amber-950/50 dark:text-amber-300",
  },
};

const TEST_STATUS_BADGE_CONFIG: Record<
  string,
  { label: string; className: string }
> = {
  [PromptABTestStatuses.Created]: {
    label: "Created",
    className:
      "bg-blue-100 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300",
  },
  [PromptABTestStatuses.Running]: {
    label: "Running",
    className:
      "bg-green-100 text-green-700 dark:bg-green-950/50 dark:text-green-300",
  },
  [PromptABTestStatuses.Completed]: {
    label: "Completed",
    className:
      "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
  },
};

const inputClass =
  "w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100";

// ─── Default form states ─────────────────────────────────────────────────────

function getDefaultPromptForm(key: string): CreatePromptRequest {
  return {
    promptKey: key,
    version: null,
    title: null,
    description: null,
    systemPrompt: "",
    userPromptTemplate: null,
    language: null,
    providerCompatibility: null,
  };
}

function getDefaultTestForm(): CreateABTestRequest {
  return {
    name: null,
    variantAId: "",
    variantBId: "",
    trafficSplitPercent: 50,
  };
}

// ─── Main component ──────────────────────────────────────────────────────────

export default function PromptRegistryPage() {
  // Prompt key search
  const [promptKey, setPromptKey] = useState("summary.default");
  const [activeKey, setActiveKey] = useState("summary.default");

  // Versions list
  const [versions, setVersions] = useState<PromptRegistry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Create form
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [promptForm, setPromptForm] = useState<CreatePromptRequest>(
    getDefaultPromptForm("summary.default"),
  );
  const [formSubmitting, setFormSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Action loading per version
  const [actionLoading, setActionLoading] = useState<Record<string, boolean>>(
    {},
  );

  // Expanded version (for viewing system prompt)
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // A/B tests
  const [tests, setTests] = useState<PromptABTest[]>([]);
  const [testsLoading, setTestsLoading] = useState(true);
  const [testsError, setTestsError] = useState<string | null>(null);
  const [showTestForm, setShowTestForm] = useState(false);
  const [testForm, setTestForm] = useState<CreateABTestRequest>(
    getDefaultTestForm(),
  );
  const [testSubmitting, setTestSubmitting] = useState(false);
  const [testFormError, setTestFormError] = useState<string | null>(null);

  // ─── Fetch versions ────────────────────────────────────────────────────────

  const fetchVersions = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await listPrompts(activeKey);
      setVersions(list);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load prompts");
    } finally {
      setLoading(false);
    }
  }, [activeKey]);

  useEffect(() => {
    fetchVersions();
  }, [fetchVersions]);

  // ─── Fetch A/B tests ───────────────────────────────────────────────────────

  const fetchTests = useCallback(async () => {
    setTestsLoading(true);
    setTestsError(null);
    try {
      const list = await listTests();
      setTests(list);
    } catch (err: unknown) {
      setTestsError(
        err instanceof Error ? err.message : "Failed to load A/B tests",
      );
    } finally {
      setTestsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchTests();
  }, [fetchTests]);

  // ─── Search ────────────────────────────────────────────────────────────────

  const handleSearch = () => {
    if (promptKey.trim()) {
      setActiveKey(promptKey.trim());
      setPromptForm((f) => ({ ...f, promptKey: promptKey.trim() }));
    }
  };

  // ─── Create prompt version ─────────────────────────────────────────────────

  const handleCreatePrompt = async () => {
    if (!promptForm.promptKey) {
      setFormError("Prompt key is required.");
      return;
    }
    if (!promptForm.systemPrompt) {
      setFormError("System prompt is required.");
      return;
    }
    setFormSubmitting(true);
    setFormError(null);
    try {
      const created = await createPrompt(promptForm);
      setVersions((prev) => [...prev, created]);
      setPromptForm(getDefaultPromptForm(activeKey));
      setShowCreateForm(false);
    } catch (err: unknown) {
      setFormError(
        err instanceof Error ? err.message : "Failed to create prompt",
      );
    } finally {
      setFormSubmitting(false);
    }
  };

  // ─── Publish / Archive ─────────────────────────────────────────────────────

  const handlePublish = async (prompt: PromptRegistry) => {
    setActionLoading((prev) => ({ ...prev, [prompt.id]: true }));
    try {
      const updated = await publishPrompt(prompt.id);
      setVersions((prev) =>
        prev.map((p) => {
          if (p.id === updated.id) return updated;
          // Publishing one version archives the previous active one
          if (p.promptKey === updated.promptKey && p.isActive && p.id !== updated.id) {
            return { ...p, isActive: false, status: "archived" };
          }
          return p;
        }),
      );
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to publish prompt");
    } finally {
      setActionLoading((prev) => ({ ...prev, [prompt.id]: false }));
    }
  };

  const handleArchive = async (prompt: PromptRegistry) => {
    setActionLoading((prev) => ({ ...prev, [prompt.id]: true }));
    try {
      const updated = await archivePrompt(prompt.id);
      setVersions((prev) => prev.map((p) => (p.id === updated.id ? updated : p)));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to archive prompt");
    } finally {
      setActionLoading((prev) => ({ ...prev, [prompt.id]: false }));
    }
  };

  // ─── Create A/B test ───────────────────────────────────────────────────────

  const handleCreateTest = async () => {
    if (!testForm.variantAId || !testForm.variantBId) {
      setTestFormError("Both variant A and variant B are required.");
      return;
    }
    if (testForm.variantAId === testForm.variantBId) {
      setTestFormError("Variant A and B must be different.");
      return;
    }
    setTestSubmitting(true);
    setTestFormError(null);
    try {
      const created = await createTest(testForm);
      setTests((prev) => [...prev, created]);
      setTestForm(getDefaultTestForm());
      setShowTestForm(false);
    } catch (err: unknown) {
      setTestFormError(
        err instanceof Error ? err.message : "Failed to create A/B test",
      );
    } finally {
      setTestSubmitting(false);
    }
  };

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Prompt Registry
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Manage versioned prompts for AI capabilities. View version history,
          publish or archive versions, and configure A/B tests.
        </p>
      </div>

      {/* ─── Prompt key search ──────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-gray-100">
          Search Prompts
        </h2>
        <div className="flex flex-wrap items-center gap-3">
          <input
            type="text"
            value={promptKey}
            onChange={(e) => setPromptKey(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleSearch()}
            list="prompt-keys-list"
            placeholder="Enter prompt key (e.g. summary.default)"
            className="flex-1 rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
          />
          <datalist id="prompt-keys-list">
            {COMMON_PROMPT_KEYS.map((k) => (
              <option key={k} value={k} />
            ))}
          </datalist>
          <button
            type="button"
            onClick={handleSearch}
            className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 dark:bg-blue-600 dark:hover:bg-blue-500"
          >
            Search
          </button>
          <button
            type="button"
            onClick={() => {
              setPromptForm(getDefaultPromptForm(activeKey));
              setShowCreateForm((v) => !v);
            }}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
          >
            {showCreateForm ? "Cancel" : "+ New Version"}
          </button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/30 dark:text-red-300">
          {error}
        </div>
      )}

      {/* ─── Create form ─────────────────────────────────────────────────────── */}
      {showCreateForm && (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Create New Prompt Version
          </h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Prompt Key *
              </label>
              <input
                type="text"
                value={promptForm.promptKey}
                onChange={(e) =>
                  setPromptForm((f) => ({ ...f, promptKey: e.target.value }))
                }
                placeholder="summary.default"
                className={inputClass}
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Version (optional)
              </label>
              <input
                type="text"
                value={promptForm.version ?? ""}
                onChange={(e) =>
                  setPromptForm((f) => ({
                    ...f,
                    version: e.target.value || null,
                  }))
                }
                placeholder="e.g. 1.2.0 (auto-incremented if empty)"
                className={inputClass}
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Title
              </label>
              <input
                type="text"
                value={promptForm.title ?? ""}
                onChange={(e) =>
                  setPromptForm((f) => ({
                    ...f,
                    title: e.target.value || null,
                  }))
                }
                placeholder="e.g. Default Summary Prompt"
                className={inputClass}
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Language (optional)
              </label>
              <input
                type="text"
                value={promptForm.language ?? ""}
                onChange={(e) =>
                  setPromptForm((f) => ({
                    ...f,
                    language: e.target.value || null,
                  }))
                }
                placeholder="e.g. zh-CN, en (empty = all)"
                className={inputClass}
              />
            </div>
            <div className="md:col-span-2">
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Description
              </label>
              <input
                type="text"
                value={promptForm.description ?? ""}
                onChange={(e) =>
                  setPromptForm((f) => ({
                    ...f,
                    description: e.target.value || null,
                  }))
                }
                placeholder="What this prompt does..."
                className={inputClass}
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Provider Compatibility
              </label>
              <input
                type="text"
                value={promptForm.providerCompatibility ?? ""}
                onChange={(e) =>
                  setPromptForm((f) => ({
                    ...f,
                    providerCompatibility: e.target.value || null,
                  }))
                }
                placeholder="e.g. openai,azure (empty = all)"
                className={inputClass}
              />
            </div>
            <div className="md:col-span-2">
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                System Prompt *
              </label>
              <textarea
                rows={4}
                value={promptForm.systemPrompt}
                onChange={(e) =>
                  setPromptForm((f) => ({ ...f, systemPrompt: e.target.value }))
                }
                placeholder="You are a helpful assistant..."
                className={`${inputClass} font-mono`}
              />
            </div>
            <div className="md:col-span-2">
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                User Prompt Template
              </label>
              <textarea
                rows={3}
                value={promptForm.userPromptTemplate ?? ""}
                onChange={(e) =>
                  setPromptForm((f) => ({
                    ...f,
                    userPromptTemplate: e.target.value || null,
                  }))
                }
                placeholder="Summarize the following: {{content}}"
                className={`${inputClass} font-mono`}
              />
            </div>
          </div>

          {formError && (
            <p className="mt-3 text-sm text-red-600 dark:text-red-400">
              {formError}
            </p>
          )}

          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={handleCreatePrompt}
              disabled={formSubmitting}
              className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-600 dark:hover:bg-blue-500"
            >
              {formSubmitting ? (
                <span className="flex items-center gap-2">
                  <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                  Creating...
                </span>
              ) : (
                "Create Version"
              )}
            </button>
            <button
              type="button"
              onClick={() => {
                setShowCreateForm(false);
                setFormError(null);
              }}
              className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* ─── Version history ─────────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            Version History:{" "}
            <code className="font-mono text-blue-600 dark:text-blue-400">
              {activeKey}
            </code>{" "}
            ({versions.length})
          </h2>
          <button
            type="button"
            onClick={fetchVersions}
            className="text-xs text-blue-600 hover:underline dark:text-blue-400"
          >
            Refresh
          </button>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-8">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : versions.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No versions found
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              No prompt versions exist for &quot;{activeKey}&quot;.
            </p>
          </div>
        ) : (
          <div className="space-y-3">
            {versions
              .slice()
              .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
              .map((prompt) => (
                <PromptVersionCard
                  key={prompt.id}
                  prompt={prompt}
                  isLoading={!!actionLoading[prompt.id]}
                  isExpanded={expandedId === prompt.id}
                  onToggleExpand={() =>
                    setExpandedId((prev) => (prev === prompt.id ? null : prompt.id))
                  }
                  onPublish={handlePublish}
                  onArchive={handleArchive}
                />
              ))}
          </div>
        )}
      </div>

      {/* ─── A/B tests ───────────────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            A/B Tests ({tests.length})
          </h2>
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={fetchTests}
              className="text-xs text-blue-600 hover:underline dark:text-blue-400"
            >
              Refresh
            </button>
            <button
              type="button"
              onClick={() => setShowTestForm((v) => !v)}
              className="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-blue-700 dark:bg-blue-600 dark:hover:bg-blue-500"
            >
              {showTestForm ? "Cancel" : "+ New Test"}
            </button>
          </div>
        </div>

        {showTestForm && (
          <div className="mb-4 rounded-lg border border-gray-200 p-4 dark:border-gray-800">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Test Name
                </label>
                <input
                  type="text"
                  value={testForm.name ?? ""}
                  onChange={(e) =>
                    setTestForm((f) => ({
                      ...f,
                      name: e.target.value || null,
                    }))
                  }
                  placeholder="e.g. Summary v1.0 vs v1.1"
                  className={inputClass}
                />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Traffic Split to Variant B (%)
                </label>
                <input
                  type="number"
                  min={0}
                  max={100}
                  value={testForm.trafficSplitPercent}
                  onChange={(e) =>
                    setTestForm((f) => ({
                      ...f,
                      trafficSplitPercent: Number(e.target.value),
                    }))
                  }
                  className={inputClass}
                />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Variant A ID (control) *
                </label>
                <select
                  value={testForm.variantAId}
                  onChange={(e) =>
                    setTestForm((f) => ({ ...f, variantAId: e.target.value }))
                  }
                  className={inputClass}
                >
                  <option value="">Select a version...</option>
                  {versions.map((v) => (
                    <option key={v.id} value={v.id}>
                      v{v.version} - {v.title || v.promptKey}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Variant B ID (challenger) *
                </label>
                <select
                  value={testForm.variantBId}
                  onChange={(e) =>
                    setTestForm((f) => ({ ...f, variantBId: e.target.value }))
                  }
                  className={inputClass}
                >
                  <option value="">Select a version...</option>
                  {versions.map((v) => (
                    <option key={v.id} value={v.id}>
                      v{v.version} - {v.title || v.promptKey}
                    </option>
                  ))}
                </select>
              </div>
            </div>
            {testFormError && (
              <p className="mt-3 text-sm text-red-600 dark:text-red-400">
                {testFormError}
              </p>
            )}
            <div className="mt-4 flex items-center gap-3">
              <button
                type="button"
                onClick={handleCreateTest}
                disabled={testSubmitting}
                className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {testSubmitting ? "Creating..." : "Create Test"}
              </button>
            </div>
          </div>
        )}

        {testsLoading ? (
          <div className="flex items-center justify-center py-6">
            <span className="inline-block h-5 w-5 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : testsError ? (
          <p className="text-sm text-red-600 dark:text-red-400">{testsError}</p>
        ) : tests.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No A/B tests configured
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              Create an A/B test to compare two prompt versions.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-gray-200 bg-gray-50 text-xs uppercase tracking-wide text-gray-500 dark:border-gray-800 dark:bg-gray-950 dark:text-gray-400">
                <tr>
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Key</th>
                  <th className="px-4 py-3 font-medium">Split</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Winner</th>
                  <th className="px-4 py-3 font-medium">Dates</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-gray-800">
                {tests.map((test) => {
                  const statusBadge =
                    TEST_STATUS_BADGE_CONFIG[test.status] ?? {
                      label: test.status,
                      className:
                        "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
                    };
                  return (
                    <tr
                      key={test.id}
                      className="hover:bg-gray-50 dark:hover:bg-gray-950/50"
                    >
                      <td className="px-4 py-3 font-medium text-gray-900 dark:text-gray-100">
                        {test.name || "Untitled"}
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                        <code className="font-mono">{test.promptKey}</code>
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                        {test.trafficSplitPercent}% / {100 - test.trafficSplitPercent}%
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${statusBadge.className}`}
                        >
                          {statusBadge.label}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                        {test.winnerVariantId
                          ? test.winnerVariantId === test.variantAId
                            ? "Variant A"
                            : "Variant B"
                          : "-"}
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                        {new Date(test.startDate).toLocaleDateString()}
                        {test.endDate
                          ? ` - ${new Date(test.endDate).toLocaleDateString()}`
                          : " - ongoing"}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Prompt version card sub-component ───────────────────────────────────────

function PromptVersionCard({
  prompt,
  isLoading,
  isExpanded,
  onToggleExpand,
  onPublish,
  onArchive,
}: {
  prompt: PromptRegistry;
  isLoading: boolean;
  isExpanded: boolean;
  onToggleExpand: () => void;
  onPublish: (prompt: PromptRegistry) => void;
  onArchive: (prompt: PromptRegistry) => void;
}) {
  const statusBadge =
    STATUS_BADGE_CONFIG[prompt.status] ?? {
      label: prompt.status,
      className:
        "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
    };

  return (
    <div
      className={`rounded-lg border p-4 transition-colors ${
        prompt.isActive
          ? "border-green-300 bg-green-50/50 dark:border-green-800 dark:bg-green-950/20"
          : "border-gray-200 dark:border-gray-800"
      }`}
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            {prompt.isActive && (
              <span className="inline-flex items-center gap-1 rounded bg-green-100 px-2 py-0.5 text-xs font-bold text-green-700 dark:bg-green-950/50 dark:text-green-300">
                Active
              </span>
            )}
            <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
              v{prompt.version}
            </span>
            <span
              className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${statusBadge.className}`}
            >
              {statusBadge.label}
            </span>
          </div>
          {prompt.title && (
            <p className="mt-1 text-sm text-gray-700 dark:text-gray-300">
              {prompt.title}
            </p>
          )}
          {prompt.description && (
            <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">
              {prompt.description}
            </p>
          )}
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-2">
          {prompt.status === PromptRegistryStatuses.Draft && (
            <button
              type="button"
              onClick={() => onPublish(prompt)}
              disabled={isLoading}
              className="rounded-md bg-green-600 px-3 py-1 text-xs font-medium text-white transition-colors hover:bg-green-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-green-600 dark:hover:bg-green-500"
            >
              {isLoading ? "..." : "Publish"}
            </button>
          )}
          {prompt.status === PromptRegistryStatuses.Published && (
            <button
              type="button"
              onClick={() => onArchive(prompt)}
              disabled={isLoading}
              className="rounded-md border border-amber-300 px-3 py-1 text-xs font-medium text-amber-700 transition-colors hover:bg-amber-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-amber-900 dark:text-amber-400 dark:hover:bg-amber-950/30"
            >
              {isLoading ? "..." : "Archive"}
            </button>
          )}
          <button
            type="button"
            onClick={onToggleExpand}
            className="text-xs text-blue-600 hover:underline dark:text-blue-400"
          >
            {isExpanded ? "Hide" : "View"}
          </button>
        </div>
      </div>

      {/* Metadata row */}
      <div className="mt-2 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-gray-500 dark:text-gray-400">
        {prompt.language && <span>Lang: {prompt.language}</span>}
        {prompt.providerCompatibility && (
          <span>Providers: {prompt.providerCompatibility}</span>
        )}
        {prompt.evaluationScore !== null && (
          <span>Score: {prompt.evaluationScore}/100</span>
        )}
        <span>By: {prompt.createdBy}</span>
        <span>Created: {new Date(prompt.createdAt).toLocaleDateString()}</span>
        {prompt.publishedAt && (
          <span>Published: {new Date(prompt.publishedAt).toLocaleDateString()}</span>
        )}
      </div>

      {/* Expanded content */}
      {isExpanded && (
        <div className="mt-3 space-y-3 border-t border-gray-100 pt-3 dark:border-gray-800">
          <div>
            <span className="text-xs font-medium text-gray-600 dark:text-gray-400">
              System Prompt
            </span>
            <pre className="mt-1 max-h-48 overflow-auto whitespace-pre-wrap rounded-lg bg-gray-50 p-3 text-xs text-gray-700 dark:bg-gray-950 dark:text-gray-300">
              {prompt.systemPrompt}
            </pre>
          </div>
          {prompt.userPromptTemplate && (
            <div>
              <span className="text-xs font-medium text-gray-600 dark:text-gray-400">
                User Prompt Template
              </span>
              <pre className="mt-1 max-h-32 overflow-auto whitespace-pre-wrap rounded-lg bg-gray-50 p-3 text-xs text-gray-700 dark:bg-gray-950 dark:text-gray-300">
                {prompt.userPromptTemplate}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
