"use client";

/**
 * BenchmarkPage
 *
 * Benchmark results and model leaderboard page. Lets operators:
 *   - View a ranked leaderboard sortable by category (fastest, most_accurate,
 *     lowest_cost, best_chinese, best_mobile, best_meeting)
 *   - Browse all benchmark results in a table with CER, WER, RTF, throughput,
 *     and cost columns
 *   - Filter results by benchmark name and dataset
 *   - Compare models visually via a simple bar chart (built with divs)
 *
 * Uses:
 *   - listResults / getRankings from ../api/audioClient
 *   - BenchmarkResult / RankingEntry / BenchmarkRankings from ../types/audio
 */

import { useCallback, useEffect, useState } from "react";
import { getRankings, listResults } from "../api/audioClient";
import {
  BenchmarkRankings,
  type BenchmarkResult,
  type RankingEntry,
} from "../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const RANKING_CATEGORIES: { value: string; label: string; desc: string }[] = [
  {
    value: BenchmarkRankings.Fastest,
    label: "Fastest",
    desc: "Ranked by throughput (segments/sec, higher is better)",
  },
  {
    value: BenchmarkRankings.MostAccurate,
    label: "Most Accurate",
    desc: "Ranked by Character Error Rate (lower is better)",
  },
  {
    value: BenchmarkRankings.LowestCost,
    label: "Lowest Cost",
    desc: "Ranked by unit cost (lower is better)",
  },
  {
    value: BenchmarkRankings.BestChinese,
    label: "Best Chinese",
    desc: "Ranked by Chinese language accuracy",
  },
  {
    value: BenchmarkRankings.BestMobile,
    label: "Best Mobile",
    desc: "Ranked by on-device performance",
  },
  {
    value: BenchmarkRankings.BestMeeting,
    label: "Best Meeting",
    desc: "Ranked by meeting transcription quality",
  },
];

// Metrics where lower is better (used for bar chart coloring and sorting)
const LOWER_IS_BETTER = new Set(["cer", "wer", "rtf", "ttfb", "unit_cost"]);

// ─── Main component ──────────────────────────────────────────────────────────

export default function BenchmarkPage() {
  // Rankings state
  const [rankings, setRankings] = useState<RankingEntry[]>([]);
  const [rankingsLoading, setRankingsLoading] = useState(true);
  const [rankingsError, setRankingsError] = useState<string | null>(null);
  const [category, setCategory] = useState<string>(BenchmarkRankings.Fastest);

  // Results state
  const [results, setResults] = useState<BenchmarkResult[]>([]);
  const [resultsLoading, setResultsLoading] = useState(true);
  const [resultsError, setResultsError] = useState<string | null>(null);

  // Filters
  const [benchmarkNameFilter, setBenchmarkNameFilter] = useState("");
  const [datasetFilter, setDatasetFilter] = useState("");

  // ─── Fetch rankings ────────────────────────────────────────────────────────

  const fetchRankings = useCallback(async () => {
    setRankingsLoading(true);
    setRankingsError(null);
    try {
      const list = await getRankings(category);
      setRankings(list);
    } catch (err: unknown) {
      setRankingsError(
        err instanceof Error ? err.message : "Failed to load rankings",
      );
    } finally {
      setRankingsLoading(false);
    }
  }, [category]);

  useEffect(() => {
    fetchRankings();
  }, [fetchRankings]);

  // ─── Fetch results ─────────────────────────────────────────────────────────

  const fetchResults = useCallback(async () => {
    setResultsLoading(true);
    setResultsError(null);
    try {
      const list = await listResults({
        benchmarkName: benchmarkNameFilter || undefined,
      });
      // Client-side dataset filter (API only supports benchmarkName)
      const filtered = datasetFilter
        ? list.filter(
            (r) =>
              r.datasetName?.toLowerCase().includes(datasetFilter.toLowerCase()),
          )
        : list;
      setResults(filtered);
    } catch (err: unknown) {
      setResultsError(
        err instanceof Error ? err.message : "Failed to load results",
      );
    } finally {
      setResultsLoading(false);
    }
  }, [benchmarkNameFilter, datasetFilter]);

  useEffect(() => {
    fetchResults();
  }, [fetchResults]);

  // ─── Derived data ──────────────────────────────────────────────────────────

  const activeCategoryInfo = RANKING_CATEGORIES.find(
    (c) => c.value === category,
  );

  // Unique benchmark names and dataset names for filter dropdowns
  const benchmarkNames = Array.from(
    new Set(results.map((r) => r.benchmarkName)),
  ).sort();
  const datasetNames = Array.from(
    new Set(
      results.map((r) => r.datasetName).filter((d): d is string => !!d),
    ),
  ).sort();

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Benchmark Results
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Compare model performance across accuracy, speed, and cost metrics.
          Switch ranking categories to see which models lead in each dimension.
        </p>
      </div>

      {/* ─── Leaderboard ─────────────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              Leaderboard
            </h2>
            {activeCategoryInfo && (
              <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">
                {activeCategoryInfo.desc}
              </p>
            )}
          </div>
          <div className="flex items-center gap-3">
            <label
              htmlFor="ranking-category"
              className="text-xs text-gray-500 dark:text-gray-400"
            >
              Sort by
            </label>
            <select
              id="ranking-category"
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            >
              {RANKING_CATEGORIES.map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={fetchRankings}
              className="text-xs text-blue-600 hover:underline dark:text-blue-400"
            >
              Refresh
            </button>
          </div>
        </div>

        {rankingsLoading ? (
          <div className="flex items-center justify-center py-8">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : rankingsError ? (
          <p className="text-sm text-red-600 dark:text-red-400">
            {rankingsError}
          </p>
        ) : rankings.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No rankings available
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              Run benchmarks to populate the leaderboard.
            </p>
          </div>
        ) : (
          <>
            {/* Comparison bar chart */}
            <RankingChart rankings={rankings} category={category} />

            {/* Ranking table */}
            <div className="mt-4 overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-gray-200 bg-gray-50 text-xs uppercase tracking-wide text-gray-500 dark:border-gray-800 dark:bg-gray-950 dark:text-gray-400">
                  <tr>
                    <th className="px-4 py-3 font-medium">Rank</th>
                    <th className="px-4 py-3 font-medium">Model</th>
                    <th className="px-4 py-3 font-medium">Provider</th>
                    <th className="px-4 py-3 font-medium">Metric</th>
                    <th className="px-4 py-3 text-right font-medium">Score</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-gray-800">
                  {rankings.map((entry) => (
                    <tr
                      key={entry.modelRegistryId}
                      className="hover:bg-gray-50 dark:hover:bg-gray-950/50"
                    >
                      <td className="px-4 py-3">
                        <RankBadge rank={entry.rank} />
                      </td>
                      <td className="px-4 py-3 font-medium text-gray-900 dark:text-gray-100">
                        {entry.displayName}
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                        <span className="font-mono">{entry.providerId}</span>
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                        <span className="font-mono">{entry.metric}</span>
                      </td>
                      <td className="px-4 py-3 text-right font-mono text-sm font-semibold text-gray-900 dark:text-gray-100">
                        {formatScore(entry.score, entry.metric)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>

      {/* ─── Results table ───────────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-gray-200 p-5 dark:border-gray-800">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            All Results ({results.length})
          </h2>
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="text"
              value={benchmarkNameFilter}
              onChange={(e) => setBenchmarkNameFilter(e.target.value)}
              placeholder="Filter by benchmark name..."
              list="benchmark-names-list"
              className="w-48 rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            />
            <datalist id="benchmark-names-list">
              {benchmarkNames.map((name) => (
                <option key={name} value={name} />
              ))}
            </datalist>
            <input
              type="text"
              value={datasetFilter}
              onChange={(e) => setDatasetFilter(e.target.value)}
              placeholder="Filter by dataset..."
              list="dataset-names-list"
              className="w-44 rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100"
            />
            <datalist id="dataset-names-list">
              {datasetNames.map((name) => (
                <option key={name} value={name} />
              ))}
            </datalist>
            <button
              type="button"
              onClick={fetchResults}
              className="text-xs text-blue-600 hover:underline dark:text-blue-400"
            >
              Refresh
            </button>
          </div>
        </div>

        {resultsLoading ? (
          <div className="flex items-center justify-center py-8">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : resultsError ? (
          <p className="p-5 text-sm text-red-600 dark:text-red-400">
            {resultsError}
          </p>
        ) : results.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12 text-center">
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No benchmark results found
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              {benchmarkNameFilter || datasetFilter
                ? "Try adjusting your filters."
                : "Run a benchmark to see results here."}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-gray-200 bg-gray-50 text-xs uppercase tracking-wide text-gray-500 dark:border-gray-800 dark:bg-gray-950 dark:text-gray-400">
                <tr>
                  <th className="px-4 py-3 font-medium">Benchmark</th>
                  <th className="px-4 py-3 font-medium">Dataset</th>
                  <th className="px-4 py-3 text-right font-medium">CER</th>
                  <th className="px-4 py-3 text-right font-medium">WER</th>
                  <th className="px-4 py-3 text-right font-medium">RTF</th>
                  <th className="px-4 py-3 text-right font-medium">Throughput</th>
                  <th className="px-4 py-3 text-right font-medium">Cost</th>
                  <th className="px-4 py-3 font-medium">Evaluated</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-gray-800">
                {results.map((r) => (
                  <tr
                    key={r.id}
                    className="hover:bg-gray-50 dark:hover:bg-gray-950/50"
                  >
                    <td className="px-4 py-3">
                      <div className="font-medium text-gray-900 dark:text-gray-100">
                        {r.benchmarkName}
                      </div>
                      <div className="text-xs text-gray-500 dark:text-gray-400">
                        Model:{" "}
                        <span className="font-mono">
                          {r.modelRegistryId.slice(0, 8)}...
                        </span>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                      {r.datasetName || "-"}
                    </td>
                    <td className="px-4 py-3 text-right font-mono text-sm text-gray-700 dark:text-gray-300">
                      {(r.cer * 100).toFixed(2)}%
                    </td>
                    <td className="px-4 py-3 text-right font-mono text-sm text-gray-700 dark:text-gray-300">
                      {(r.wer * 100).toFixed(2)}%
                    </td>
                    <td className="px-4 py-3 text-right font-mono text-sm text-gray-700 dark:text-gray-300">
                      {r.rtf.toFixed(3)}
                    </td>
                    <td className="px-4 py-3 text-right font-mono text-sm text-gray-700 dark:text-gray-300">
                      {r.throughput.toFixed(1)}/s
                    </td>
                    <td className="px-4 py-3 text-right font-mono text-sm text-gray-700 dark:text-gray-300">
                      {r.unitCost > 0
                        ? `$${r.unitCost.toFixed(4)}`
                        : "Free"}
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-500 dark:text-gray-400">
                      {new Date(r.evaluatedAt).toLocaleDateString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Ranking bar chart (simple divs) ─────────────────────────────────────────

function RankingChart({
  rankings,
  category,
}: {
  rankings: RankingEntry[];
  category: string;
}) {
  if (rankings.length === 0) return null;

  const lowerIsBetter = LOWER_IS_BETTER.has(rankings[0]?.metric);
  const scores = rankings.map((r) => r.score);
  const maxScore = Math.max(...scores);
  const minScore = Math.min(...scores);
  const range = maxScore - minScore || 1;

  return (
    <div className="rounded-lg border border-gray-100 bg-gray-50 p-4 dark:border-gray-800 dark:bg-gray-950/50">
      <div className="mb-2 flex items-center justify-between">
        <span className="text-xs font-medium text-gray-600 dark:text-gray-400">
          {category === "fastest" || category === "best_mobile"
            ? "Throughput comparison"
            : category === "most_accurate" || category === "best_chinese" || category === "best_meeting"
              ? "Error rate comparison (lower is better)"
              : "Cost comparison (lower is better)"}
        </span>
        <span className="text-xs text-gray-400 dark:text-gray-500">
          {lowerIsBetter ? "Lower is better" : "Higher is better"}
        </span>
      </div>
      <div className="space-y-2">
        {rankings.slice(0, 10).map((entry) => {
          // Normalize to 0-100% width for the bar
          const normalized = lowerIsBetter
            ? ((maxScore - entry.score) / range) * 70 + 30
            : ((entry.score - minScore) / range) * 70 + 30;
          const isBest = entry.rank === 1;
          return (
            <div key={entry.modelRegistryId} className="flex items-center gap-2">
              <div className="w-28 shrink-0 truncate text-xs text-gray-600 dark:text-gray-400">
                {entry.displayName}
              </div>
              <div className="relative h-6 flex-1 overflow-hidden rounded bg-gray-200 dark:bg-gray-800">
                <div
                  className={`flex h-full items-center justify-end rounded px-2 text-xs font-medium text-white transition-all ${
                    isBest
                      ? "bg-green-500 dark:bg-green-600"
                      : "bg-blue-500 dark:bg-blue-600"
                  }`}
                  style={{ width: `${Math.min(100, normalized)}%` }}
                >
                  {formatScore(entry.score, entry.metric)}
                </div>
              </div>
              <div className="w-8 shrink-0 text-right text-xs font-semibold text-gray-700 dark:text-gray-300">
                #{entry.rank}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ─── Rank badge ──────────────────────────────────────────────────────────────

function RankBadge({ rank }: { rank: number }) {
  const styles: Record<number, string> = {
    1: "bg-amber-100 text-amber-800 dark:bg-amber-950/50 dark:text-amber-300",
    2: "bg-gray-200 text-gray-700 dark:bg-gray-700 dark:text-gray-200",
    3: "bg-orange-100 text-orange-800 dark:bg-orange-950/50 dark:text-orange-300",
  };
  const style =
    styles[rank] ??
    "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400";
  const medals: Record<number, string> = { 1: "1st", 2: "2nd", 3: "3rd" };
  return (
    <span
      className={`inline-flex min-w-[2.5rem] items-center justify-center rounded-full px-2 py-0.5 text-xs font-bold ${style}`}
    >
      {medals[rank] ?? `${rank}th`}
    </span>
  );
}

// ─── Score formatter ─────────────────────────────────────────────────────────

function formatScore(score: number, metric: string): string {
  if (metric === "cer" || metric === "wer") {
    return `${(score * 100).toFixed(2)}%`;
  }
  if (metric === "rtf") {
    return score.toFixed(3);
  }
  if (metric === "throughput") {
    return `${score.toFixed(1)}/s`;
  }
  if (metric === "unit_cost" || metric === "cost") {
    return `$${score.toFixed(4)}`;
  }
  return score.toFixed(2);
}
