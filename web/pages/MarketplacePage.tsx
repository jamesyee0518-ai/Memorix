"use client";

/**
 * MarketplacePage
 *
 * Provider marketplace browsing page. Lets operators:
 *   - Browse a grid of provider cards with name, description, rating,
 *     install count, capability, and execution mode
 *   - Filter by capability and execution mode
 *   - Search providers by name
 *   - Install or uninstall providers
 *
 * Uses:
 *   - listEntries / installEntry / uninstallEntry from ../api/audioClient
 *   - ProviderMarketplaceEntry / AudioCapabilities / ExecutionModes
 *     from ../types/audio
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  installEntry,
  listEntries,
  uninstallEntry,
} from "../api/audioClient";
import {
  AudioCapabilities,
  ExecutionModes,
  type ProviderMarketplaceEntry,
} from "../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const CAPABILITY_FILTERS: { value: string; label: string }[] = [
  { value: "", label: "All Capabilities" },
  { value: AudioCapabilities.Transcription, label: "Transcription" },
  { value: AudioCapabilities.Synthesis, label: "Synthesis" },
  { value: AudioCapabilities.Vad, label: "VAD" },
  { value: AudioCapabilities.Punctuation, label: "Punctuation" },
  { value: AudioCapabilities.Diarization, label: "Diarization" },
];

const EXECUTION_MODE_FILTERS: { value: string; label: string }[] = [
  { value: "", label: "All Modes" },
  { value: ExecutionModes.LocalDevice, label: "Local Device" },
  { value: ExecutionModes.LocalLanNode, label: "LAN Node" },
  { value: ExecutionModes.MemorixCloud, label: "Memorix Cloud" },
  { value: ExecutionModes.ThirdPartyCloud, label: "Third-Party Cloud" },
];

const CAPABILITY_LABELS: Record<string, string> = {
  [AudioCapabilities.Transcription]: "Transcription",
  [AudioCapabilities.Synthesis]: "Synthesis",
  [AudioCapabilities.Vad]: "VAD",
  [AudioCapabilities.Punctuation]: "Punctuation",
  [AudioCapabilities.Diarization]: "Diarization",
  [AudioCapabilities.Correction]: "Correction",
};

// ─── Main component ──────────────────────────────────────────────────────────

export default function MarketplacePage() {
  // Entry list
  const [entries, setEntries] = useState<ProviderMarketplaceEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [searchQuery, setSearchQuery] = useState("");
  const [capabilityFilter, setCapabilityFilter] = useState("");
  const [executionModeFilter, setExecutionModeFilter] = useState("");

  // Per-entry action loading
  const [actionLoading, setActionLoading] = useState<Record<string, boolean>>(
    {},
  );

  // ─── Fetch entries ─────────────────────────────────────────────────────────

  const fetchEntries = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await listEntries({
        capability: capabilityFilter || undefined,
      });
      setEntries(list);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load marketplace");
    } finally {
      setLoading(false);
    }
  }, [capabilityFilter]);

  useEffect(() => {
    fetchEntries();
  }, [fetchEntries]);

  // ─── Install / Uninstall ───────────────────────────────────────────────────

  const handleInstall = async (entry: ProviderMarketplaceEntry) => {
    setActionLoading((prev) => ({ ...prev, [entry.id]: true }));
    try {
      const updated = await installEntry(entry.id);
      setEntries((prev) => prev.map((e) => (e.id === updated.id ? updated : e)));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to install provider");
    } finally {
      setActionLoading((prev) => ({ ...prev, [entry.id]: false }));
    }
  };

  const handleUninstall = async (entry: ProviderMarketplaceEntry) => {
    setActionLoading((prev) => ({ ...prev, [entry.id]: true }));
    try {
      await uninstallEntry(entry.id);
      setEntries((prev) =>
        prev.map((e) =>
          e.id === entry.id ? { ...e, isInstalled: false } : e,
        ),
      );
    } catch (err: unknown) {
      setError(
        err instanceof Error ? err.message : "Failed to uninstall provider",
      );
    } finally {
      setActionLoading((prev) => ({ ...prev, [entry.id]: false }));
    }
  };

  // ─── Client-side filtering (search + execution mode) ───────────────────────

  const filteredEntries = useMemo(() => {
    return entries.filter((entry) => {
      // Search by name
      if (searchQuery) {
        const query = searchQuery.toLowerCase();
        const matchesName = entry.name.toLowerCase().includes(query);
        const matchesProvider = entry.providerId
          .toLowerCase()
          .includes(query);
        if (!matchesName && !matchesProvider) return false;
      }
      // Filter by execution mode
      if (executionModeFilter && entry.executionMode !== executionModeFilter) {
        return false;
      }
      return true;
    });
  }, [entries, searchQuery, executionModeFilter]);

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Provider Marketplace
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Browse and install audio capability providers. Compare ratings,
          install counts, and execution modes.
        </p>
      </div>

      {/* ─── Filters ────────────────────────────────────────────────────────── */}
      <div className="flex flex-wrap items-center gap-3">
        <input
          type="text"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          placeholder="Search by name or provider..."
          className="w-64 rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
        />
        <select
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
        <select
          value={executionModeFilter}
          onChange={(e) => setExecutionModeFilter(e.target.value)}
          className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
        >
          {EXECUTION_MODE_FILTERS.map((m) => (
            <option key={m.value} value={m.value}>
              {m.label}
            </option>
          ))}
        </select>
        <button
          type="button"
          onClick={fetchEntries}
          className="text-xs text-blue-600 hover:underline dark:text-blue-400"
        >
          Refresh
        </button>
        <span className="ml-auto text-xs text-gray-500 dark:text-gray-400">
          {filteredEntries.length} of {entries.length} providers
        </span>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/30 dark:text-red-300">
          {error}
        </div>
      )}

      {/* ─── Provider grid ──────────────────────────────────────────────────── */}
      {loading ? (
        <div className="flex items-center justify-center py-12">
          <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
        </div>
      ) : filteredEntries.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-12 text-center">
          <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
            No providers found
          </p>
          <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
            {searchQuery || capabilityFilter || executionModeFilter
              ? "Try adjusting your filters."
              : "No providers are available in the marketplace."}
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {filteredEntries.map((entry) => (
            <ProviderCard
              key={entry.id}
              entry={entry}
              isLoading={!!actionLoading[entry.id]}
              onInstall={handleInstall}
              onUninstall={handleUninstall}
            />
          ))}
        </div>
      )}
    </div>
  );
}

// ─── Provider card sub-component ─────────────────────────────────────────────

function ProviderCard({
  entry,
  isLoading,
  onInstall,
  onUninstall,
}: {
  entry: ProviderMarketplaceEntry;
  isLoading: boolean;
  onInstall: (entry: ProviderMarketplaceEntry) => void;
  onUninstall: (entry: ProviderMarketplaceEntry) => void;
}) {
  const tags: string[] = (() => {
    try {
      return JSON.parse(entry.tagsJson) as string[];
    } catch {
      return [];
    }
  })();

  return (
    <div className="flex flex-col rounded-xl border border-gray-200 bg-white p-5 shadow-sm transition-shadow hover:shadow-md dark:border-gray-800 dark:bg-gray-900">
      {/* Header */}
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="truncate text-base font-semibold text-gray-900 dark:text-gray-100">
              {entry.name}
            </h3>
            {entry.isOfficial && (
              <span className="inline-flex shrink-0 items-center rounded bg-blue-100 px-1.5 py-0.5 text-xs font-bold text-blue-700 dark:bg-blue-950/50 dark:text-blue-300">
                Official
              </span>
            )}
          </div>
          <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">
            by {entry.authorName}
            {entry.version && ` - v${entry.version}`}
          </p>
        </div>
        {entry.isInstalled && (
          <span className="inline-flex shrink-0 items-center rounded bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700 dark:bg-green-950/50 dark:text-green-300">
            Installed
          </span>
        )}
      </div>

      {/* Description */}
      <p className="mt-3 flex-1 text-sm text-gray-600 dark:text-gray-400">
        {entry.description}
      </p>

      {/* Metadata */}
      <div className="mt-3 flex flex-wrap gap-2">
        <span className="inline-flex items-center rounded bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-700 dark:bg-blue-950/40 dark:text-blue-300">
          {CAPABILITY_LABELS[entry.capability] ?? entry.capability}
        </span>
        <span className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600 dark:bg-gray-800 dark:text-gray-400">
          {entry.executionMode.replace(/_/g, " ").toLowerCase()}
        </span>
        <span className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600 dark:bg-gray-800 dark:text-gray-400">
          {entry.pricingUnit || "free"}
        </span>
        {tags.slice(0, 2).map((tag) => (
          <span
            key={tag}
            className="inline-flex items-center rounded bg-purple-50 px-2 py-0.5 text-xs font-medium text-purple-700 dark:bg-purple-950/40 dark:text-purple-300"
          >
            {tag}
          </span>
        ))}
      </div>

      {/* Stats row */}
      <div className="mt-3 flex items-center gap-4 text-xs text-gray-500 dark:text-gray-400">
        <span className="flex items-center gap-1">
          <StarRating rating={entry.rating} />
          <span className="font-medium text-gray-700 dark:text-gray-300">
            {entry.rating.toFixed(1)}
          </span>
        </span>
        <span>{entry.installCount.toLocaleString()} installs</span>
      </div>

      {/* Action button */}
      <div className="mt-4">
        {entry.isInstalled ? (
          <button
            type="button"
            onClick={() => onUninstall(entry)}
            disabled={isLoading}
            className="w-full rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-600 transition-colors hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-red-900 dark:text-red-400 dark:hover:bg-red-950/30"
          >
            {isLoading ? "Uninstalling..." : "Uninstall"}
          </button>
        ) : (
          <button
            type="button"
            onClick={() => onInstall(entry)}
            disabled={isLoading}
            className="w-full rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-600 dark:hover:bg-blue-500"
          >
            {isLoading ? "Installing..." : "Install"}
          </button>
        )}
      </div>
    </div>
  );
}

// ─── Star rating sub-component ───────────────────────────────────────────────

function StarRating({ rating }: { rating: number }) {
  const fullStars = Math.floor(rating);
  const hasHalf = rating - fullStars >= 0.5;
  return (
    <span className="flex items-center">
      {Array.from({ length: 5 }).map((_, i) => {
        const isFull = i < fullStars;
        const isHalf = i === fullStars && hasHalf;
        return (
          <svg
            key={i}
            className={`h-3.5 w-3.5 ${
              isFull || isHalf
                ? "text-amber-400"
                : "text-gray-300 dark:text-gray-600"
            }`}
            fill="currentColor"
            viewBox="0 0 20 20"
          >
            {isHalf ? (
              <>
                <defs>
                  <linearGradient id={`half-star-${i}`}>
                    <stop offset="50%" stopColor="currentColor" />
                    <stop offset="50%" stopColor="transparent" />
                  </linearGradient>
                </defs>
                <path
                  fill={`url(#half-star-${i})`}
                  d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z"
                />
              </>
            ) : (
              <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
            )}
          </svg>
        );
      })}
    </span>
  );
}
