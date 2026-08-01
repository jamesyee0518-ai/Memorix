"use client";

/**
 * CorrectionDictionaryPage
 *
 * Post-ASR correction dictionary management page. Lets operators:
 *   - List correction entries in a table
 *   - Add new entries (original text, corrected text, category)
 *   - Edit entries (original, corrected, category, language, active)
 *   - Delete (deactivate) entries
 *   - Filter by category and language
 *   - Bulk import entries via a JSON array textarea
 *
 * Uses:
 *   - listCorrectionEntries / addCorrectionEntry /
 *     updateCorrectionEntry / deleteCorrectionEntry
 *     from ../api/audioClient
 *   - CorrectionDictionaryEntry / AddCorrectionEntryRequest /
 *     UpdateCorrectionEntryRequest from ../types/audio
 */

import { useCallback, useEffect, useState } from "react";
import {
  addCorrectionEntry,
  deleteCorrectionEntry,
  listCorrectionEntries,
  updateCorrectionEntry,
} from "../api/audioClient";
import type {
  AddCorrectionEntryRequest,
  CorrectionDictionaryEntry,
  UpdateCorrectionEntryRequest,
} from "../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const CATEGORIES: { value: string; label: string }[] = [
  { value: "", label: "All Categories" },
  { value: "brand", label: "Brand" },
  { value: "person", label: "Person" },
  { value: "term", label: "Term" },
  { value: "abbreviation", label: "Abbreviation" },
  { value: "homophone", label: "Homophone" },
  { value: "custom", label: "Custom" },
];

const CATEGORY_BADGE_COLORS: Record<string, string> = {
  brand: "bg-purple-50 text-purple-700 dark:bg-purple-950/40 dark:text-purple-300",
  person: "bg-blue-50 text-blue-700 dark:bg-blue-950/40 dark:text-blue-300",
  term: "bg-green-50 text-green-700 dark:bg-green-950/40 dark:text-green-300",
  abbreviation:
    "bg-amber-50 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300",
  homophone:
    "bg-pink-50 text-pink-700 dark:bg-pink-950/40 dark:text-pink-300",
  custom: "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
};

const inputClass =
  "w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100";

// ─── Main component ──────────────────────────────────────────────────────────

export default function CorrectionDictionaryPage() {
  // Entries list
  const [entries, setEntries] = useState<CorrectionDictionaryEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [categoryFilter, setCategoryFilter] = useState("");
  const [languageFilter, setLanguageFilter] = useState("");

  // Add form
  const [showAddForm, setShowAddForm] = useState(false);
  const [addForm, setAddForm] = useState<AddCorrectionEntryRequest>({
    workspaceId: null,
    original: "",
    corrected: "",
    category: "term",
  });
  const [addSubmitting, setAddSubmitting] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  // Edit modal
  const [editTarget, setEditTarget] = useState<CorrectionDictionaryEntry | null>(null);
  const [editForm, setEditForm] = useState<UpdateCorrectionEntryRequest>({
    original: null,
    corrected: null,
    category: null,
    language: null,
    isActive: null,
  });
  const [editSubmitting, setEditSubmitting] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  // Delete confirmation
  const [deleteTarget, setDeleteTarget] = useState<CorrectionDictionaryEntry | null>(null);
  const [deleteLoading, setDeleteLoading] = useState(false);

  // Bulk import
  const [showBulkImport, setShowBulkImport] = useState(false);
  const [bulkJson, setBulkJson] = useState("");
  const [bulkSubmitting, setBulkSubmitting] = useState(false);
  const [bulkError, setBulkError] = useState<string | null>(null);
  const [bulkResult, setBulkResult] = useState<string | null>(null);

  // ─── Fetch entries ─────────────────────────────────────────────────────────

  const fetchEntries = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await listCorrectionEntries({
        category: categoryFilter || undefined,
      });
      // Client-side language filter
      const filtered = languageFilter
        ? list.filter(
            (e) =>
              e.language?.toLowerCase().includes(languageFilter.toLowerCase()),
          )
        : list;
      setEntries(filtered);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load entries");
    } finally {
      setLoading(false);
    }
  }, [categoryFilter, languageFilter]);

  useEffect(() => {
    fetchEntries();
  }, [fetchEntries]);

  // ─── Add entry ─────────────────────────────────────────────────────────────

  const handleAdd = async () => {
    if (!addForm.original || !addForm.corrected) {
      setAddError("Original text and corrected text are required.");
      return;
    }
    setAddSubmitting(true);
    setAddError(null);
    try {
      const created = await addCorrectionEntry(addForm);
      setEntries((prev) => [created, ...prev]);
      setAddForm({
        workspaceId: null,
        original: "",
        corrected: "",
        category: "term",
      });
      setShowAddForm(false);
    } catch (err: unknown) {
      setAddError(err instanceof Error ? err.message : "Failed to add entry");
    } finally {
      setAddSubmitting(false);
    }
  };

  // ─── Edit entry ────────────────────────────────────────────────────────────

  const openEditModal = (entry: CorrectionDictionaryEntry) => {
    setEditTarget(entry);
    setEditForm({
      original: entry.originalText,
      corrected: entry.correctedText,
      category: entry.category,
      language: entry.language,
      isActive: entry.isActive,
    });
    setEditError(null);
  };

  const handleEdit = async () => {
    if (!editTarget) return;
    setEditSubmitting(true);
    setEditError(null);
    try {
      const updated = await updateCorrectionEntry(editTarget.id, editForm);
      setEntries((prev) =>
        prev.map((e) => (e.id === updated.id ? updated : e)),
      );
      setEditTarget(null);
    } catch (err: unknown) {
      setEditError(err instanceof Error ? err.message : "Failed to update entry");
    } finally {
      setEditSubmitting(false);
    }
  };

  // ─── Delete entry ──────────────────────────────────────────────────────────

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleteLoading(true);
    try {
      await deleteCorrectionEntry(deleteTarget.id);
      setEntries((prev) => prev.filter((e) => e.id !== deleteTarget.id));
      setDeleteTarget(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to delete entry");
    } finally {
      setDeleteLoading(false);
    }
  };

  // ─── Bulk import ───────────────────────────────────────────────────────────

  const handleBulkImport = async () => {
    setBulkError(null);
    setBulkResult(null);
    let parsed: unknown;
    try {
      parsed = JSON.parse(bulkJson);
    } catch {
      setBulkError("Invalid JSON. Please paste a valid JSON array.");
      return;
    }
    if (!Array.isArray(parsed)) {
      setBulkError("Input must be a JSON array of correction entries.");
      return;
    }
    setBulkSubmitting(true);
    let success = 0;
    let failed = 0;
    const errors: string[] = [];
    for (let i = 0; i < parsed.length; i++) {
      const item = parsed[i] as Record<string, unknown>;
      if (!item.original || !item.corrected) {
        failed++;
        errors.push(`Row ${i + 1}: missing "original" or "corrected"`);
        continue;
      }
      try {
        await addCorrectionEntry({
          workspaceId: null,
          original: String(item.original),
          corrected: String(item.corrected),
          category: item.category ? String(item.category) : null,
        });
        success++;
      } catch (err: unknown) {
        failed++;
        errors.push(
          `Row ${i + 1}: ${err instanceof Error ? err.message : "unknown error"}`,
        );
      }
    }
    setBulkSubmitting(false);
    setBulkResult(
      `Imported ${success} entries${failed > 0 ? `, ${failed} failed` : ""}.`,
    );
    if (errors.length > 0) {
      setBulkError(errors.join("\n"));
    }
    if (success > 0) {
      await fetchEntries();
    }
  };

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Correction Dictionary
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Manage post-ASR text correction entries. Define original-to-corrected
          mappings for brands, names, terms, and more.
        </p>
      </div>

      {/* ─── Filters & actions ───────────────────────────────────────────────── */}
      <div className="flex flex-wrap items-center gap-3">
        <select
          value={categoryFilter}
          onChange={(e) => setCategoryFilter(e.target.value)}
          className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
        >
          {CATEGORIES.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </select>
        <input
          type="text"
          value={languageFilter}
          onChange={(e) => setLanguageFilter(e.target.value)}
          placeholder="Filter by language..."
          className="w-44 rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
        />
        <button
          type="button"
          onClick={fetchEntries}
          className="text-xs text-blue-600 hover:underline dark:text-blue-400"
        >
          Refresh
        </button>
        <div className="ml-auto flex items-center gap-2">
          <button
            type="button"
            onClick={() => setShowBulkImport((v) => !v)}
            className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
          >
            {showBulkImport ? "Cancel" : "Bulk Import"}
          </button>
          <button
            type="button"
            onClick={() => setShowAddForm((v) => !v)}
            className="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-blue-700 dark:bg-blue-600 dark:hover:bg-blue-500"
          >
            {showAddForm ? "Cancel" : "+ Add Entry"}
          </button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/30 dark:text-red-300">
          {error}
        </div>
      )}

      {/* ─── Add form ────────────────────────────────────────────────────────── */}
      {showAddForm && (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Add Correction Entry
          </h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Original Text *
              </label>
              <input
                type="text"
                value={addForm.original}
                onChange={(e) =>
                  setAddForm((f) => ({ ...f, original: e.target.value }))
                }
                placeholder="e.g. Open AI"
                className={inputClass}
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Corrected Text *
              </label>
              <input
                type="text"
                value={addForm.corrected}
                onChange={(e) =>
                  setAddForm((f) => ({ ...f, corrected: e.target.value }))
                }
                placeholder="e.g. OpenAI"
                className={inputClass}
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                Category
              </label>
              <select
                value={addForm.category ?? "term"}
                onChange={(e) =>
                  setAddForm((f) => ({ ...f, category: e.target.value }))
                }
                className={inputClass}
              >
                {CATEGORIES.filter((c) => c.value).map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            </div>
          </div>
          {addError && (
            <p className="mt-3 text-sm text-red-600 dark:text-red-400">
              {addError}
            </p>
          )}
          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={handleAdd}
              disabled={addSubmitting}
              className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {addSubmitting ? "Adding..." : "Add Entry"}
            </button>
            <button
              type="button"
              onClick={() => {
                setShowAddForm(false);
                setAddError(null);
              }}
              className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* ─── Bulk import ─────────────────────────────────────────────────────── */}
      {showBulkImport && (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <h2 className="mb-2 text-lg font-semibold text-gray-900 dark:text-gray-100">
            Bulk Import
          </h2>
          <p className="mb-3 text-sm text-gray-500 dark:text-gray-400">
            Paste a JSON array of correction entries. Each item should have
            &quot;original&quot;, &quot;corrected&quot;, and optionally
            &quot;category&quot;.
          </p>
          <textarea
            rows={8}
            value={bulkJson}
            onChange={(e) => setBulkJson(e.target.value)}
            placeholder={'[\n  { "original": "Open AI", "corrected": "OpenAI", "category": "brand" },\n  { "original": "GPT4", "corrected": "GPT-4", "category": "abbreviation" }\n]'}
            className={`${inputClass} font-mono`}
          />
          {bulkResult && (
            <p className="mt-2 text-sm text-green-600 dark:text-green-400">
              {bulkResult}
            </p>
          )}
          {bulkError && (
            <pre className="mt-2 whitespace-pre-wrap text-sm text-red-600 dark:text-red-400">
              {bulkError}
            </pre>
          )}
          <div className="mt-3 flex items-center gap-3">
            <button
              type="button"
              onClick={handleBulkImport}
              disabled={bulkSubmitting}
              className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {bulkSubmitting ? "Importing..." : "Import Entries"}
            </button>
          </div>
        </div>
      )}

      {/* ─── Entries table ───────────────────────────────────────────────────── */}
      <div className="overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm dark:border-gray-800 dark:bg-gray-900">
        {loading ? (
          <div className="flex items-center justify-center py-12">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : entries.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12 text-center">
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No correction entries found
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              {categoryFilter || languageFilter
                ? "Try adjusting your filters."
                : "Add an entry to get started."}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-gray-200 bg-gray-50 text-xs uppercase tracking-wide text-gray-500 dark:border-gray-800 dark:bg-gray-950 dark:text-gray-400">
                <tr>
                  <th className="px-4 py-3 font-medium">Original</th>
                  <th className="px-4 py-3 font-medium">Corrected</th>
                  <th className="px-4 py-3 font-medium">Category</th>
                  <th className="px-4 py-3 font-medium">Language</th>
                  <th className="px-4 py-3 font-medium">Active</th>
                  <th className="px-4 py-3 text-right font-medium">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-gray-800">
                {entries.map((entry) => (
                  <tr
                    key={entry.id}
                    className="hover:bg-gray-50 dark:hover:bg-gray-950/50"
                  >
                    <td className="px-4 py-3">
                      <span className="font-mono text-sm text-gray-900 dark:text-gray-100">
                        {entry.originalText}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span className="font-mono text-sm text-gray-900 dark:text-gray-100">
                        {entry.correctedText}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${
                          CATEGORY_BADGE_COLORS[entry.category] ??
                          CATEGORY_BADGE_COLORS.custom
                        }`}
                      >
                        {entry.category}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                      {entry.language || "-"}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-block h-2 w-2 rounded-full ${
                          entry.isActive
                            ? "bg-green-500"
                            : "bg-gray-400"
                        }`}
                      />
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          type="button"
                          onClick={() => openEditModal(entry)}
                          className="rounded-md border border-gray-300 px-3 py-1 text-xs font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          onClick={() => setDeleteTarget(entry)}
                          className="rounded-md border border-red-300 px-3 py-1 text-xs font-medium text-red-600 transition-colors hover:bg-red-50 dark:border-red-900 dark:text-red-400 dark:hover:bg-red-950/30"
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ─── Edit modal ──────────────────────────────────────────────────────── */}
      {editTarget && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
          onClick={() => setEditTarget(null)}
        >
          <div
            className="w-full max-w-lg rounded-xl bg-white p-5 shadow-xl dark:bg-gray-900"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="mb-4 text-base font-semibold text-gray-900 dark:text-gray-100">
              Edit Correction Entry
            </h3>
            <div className="space-y-4">
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Original Text
                </label>
                <input
                  type="text"
                  value={editForm.original ?? ""}
                  onChange={(e) =>
                    setEditForm((f) => ({ ...f, original: e.target.value }))
                  }
                  className={inputClass}
                />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Corrected Text
                </label>
                <input
                  type="text"
                  value={editForm.corrected ?? ""}
                  onChange={(e) =>
                    setEditForm((f) => ({ ...f, corrected: e.target.value }))
                  }
                  className={inputClass}
                />
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                    Category
                  </label>
                  <select
                    value={editForm.category ?? "custom"}
                    onChange={(e) =>
                      setEditForm((f) => ({ ...f, category: e.target.value }))
                    }
                    className={inputClass}
                  >
                    {CATEGORIES.filter((c) => c.value).map((c) => (
                      <option key={c.value} value={c.value}>
                        {c.label}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300">
                    Language
                  </label>
                  <input
                    type="text"
                    value={editForm.language ?? ""}
                    onChange={(e) =>
                      setEditForm((f) => ({
                        ...f,
                        language: e.target.value || null,
                      }))
                    }
                    placeholder="e.g. zh-CN"
                    className={inputClass}
                  />
                </div>
              </div>
              <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
                <input
                  type="checkbox"
                  checked={editForm.isActive ?? true}
                  onChange={(e) =>
                    setEditForm((f) => ({ ...f, isActive: e.target.checked }))
                  }
                  className="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500 dark:border-gray-600 dark:bg-gray-950"
                />
                Active
              </label>
            </div>
            {editError && (
              <p className="mt-3 text-sm text-red-600 dark:text-red-400">
                {editError}
              </p>
            )}
            <div className="mt-4 flex justify-end gap-3">
              <button
                type="button"
                onClick={() => setEditTarget(null)}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleEdit}
                disabled={editSubmitting}
                className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {editSubmitting ? "Saving..." : "Save Changes"}
              </button>
            </div>
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
              Delete Correction Entry
            </h3>
            <p className="mt-2 text-sm text-gray-600 dark:text-gray-400">
              Are you sure you want to delete the correction for{" "}
              <span className="font-mono font-medium text-gray-900 dark:text-gray-100">
                {deleteTarget.originalText}
              </span>{" "}
              to{" "}
              <span className="font-mono font-medium text-gray-900 dark:text-gray-100">
                {deleteTarget.correctedText}
              </span>
              ?
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
                disabled={deleteLoading}
                className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {deleteLoading ? "Deleting..." : "Delete"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
