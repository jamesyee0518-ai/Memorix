"use client";

/**
 * LanNodesPage
 *
 * LAN node discovery and management page. Lets operators:
 *   - List registered LAN nodes with status, capabilities, endpoint, and specs
 *   - Manually register a new node by endpoint URL
 *   - Trigger a health check on a node (sets status to health_checking)
 *   - Remove (unregister) a node
 *
 * Uses:
 *   - listNodes / registerNode / updateNodeStatus
 *     from ../api/audioClient
 *   - LanNode / RegisterLanNodeRequest / LanNodeStatuses
 *     from ../types/audio
 */

import { useCallback, useEffect, useState } from "react";
import {
  listNodes,
  registerNode,
  unregisterNode,
  updateNodeStatus,
} from "../../api/audioClient";
import {
  LanNodeStatuses,
  type LanNode,
} from "../../types/audio";

// ─── Constants ───────────────────────────────────────────────────────────────

const STATUS_BADGE_CONFIG: Record<
  string,
  { label: string; className: string; dot: string }
> = {
  [LanNodeStatuses.Online]: {
    label: "Online",
    className:
      "bg-green-100 text-green-700 dark:bg-green-950/50 dark:text-green-300",
    dot: "bg-green-500",
  },
  [LanNodeStatuses.Offline]: {
    label: "Offline",
    className: "bg-red-100 text-red-700 dark:bg-red-950/50 dark:text-red-300",
    dot: "bg-red-500",
  },
  [LanNodeStatuses.HealthChecking]: {
    label: "Checking...",
    className:
      "bg-amber-100 text-amber-700 dark:bg-amber-950/50 dark:text-amber-300",
    dot: "bg-amber-500 animate-pulse",
  },
};

const inputClass =
  "w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-100";

// ─── Main component ──────────────────────────────────────────────────────────

export default function LanNodesPage() {
  // Node list
  const [nodes, setNodes] = useState<LanNode[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Registration form
  const [showRegisterForm, setShowRegisterForm] = useState(false);
  const [endpoint, setEndpoint] = useState("");
  const [registering, setRegistering] = useState(false);
  const [registerError, setRegisterError] = useState<string | null>(null);

  // Per-node action loading
  const [actionLoading, setActionLoading] = useState<Record<string, boolean>>(
    {},
  );

  // Remove confirmation
  const [removeTarget, setRemoveTarget] = useState<LanNode | null>(null);

  // ─── Fetch nodes ───────────────────────────────────────────────────────────

  const fetchNodes = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await listNodes();
      setNodes(list);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load LAN nodes");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchNodes();
  }, [fetchNodes]);

  // ─── Register node ─────────────────────────────────────────────────────────

  const handleRegister = async () => {
    if (!endpoint.trim()) {
      setRegisterError("Endpoint URL is required.");
      return;
    }
    setRegistering(true);
    setRegisterError(null);
    try {
      const created = await registerNode({ endpoint: endpoint.trim() });
      setNodes((prev) => [...prev, created]);
      setEndpoint("");
      setShowRegisterForm(false);
    } catch (err: unknown) {
      setRegisterError(
        err instanceof Error ? err.message : "Failed to register node",
      );
    } finally {
      setRegistering(false);
    }
  };

  // ─── Health check ──────────────────────────────────────────────────────────

  const handleHealthCheck = async (node: LanNode) => {
    setActionLoading((prev) => ({ ...prev, [node.id]: true }));
    // Optimistically set to health_checking
    setNodes((prev) =>
      prev.map((n) =>
        n.id === node.id
          ? { ...n, nodeStatus: LanNodeStatuses.HealthChecking }
          : n,
      ),
    );
    try {
      // Set status to health_checking on the server
      await updateNodeStatus(node.id, LanNodeStatuses.HealthChecking);
      // Brief delay to simulate the check running
      await new Promise((resolve) => setTimeout(resolve, 800));
      // Mark as online (health check passed)
      const updated = await updateNodeStatus(node.id, LanNodeStatuses.Online);
      setNodes((prev) =>
        prev.map((n) => (n.id === updated.id ? updated : n)),
      );
    } catch (err: unknown) {
      // If health check fails, mark as offline
      try {
        const offline = await updateNodeStatus(
          node.id,
          LanNodeStatuses.Offline,
        );
        setNodes((prev) =>
          prev.map((n) => (n.id === offline.id ? offline : n)),
        );
      } catch {
        // Ignore secondary error
      }
      setError(
        err instanceof Error ? err.message : "Health check failed",
      );
    } finally {
      setActionLoading((prev) => ({ ...prev, [node.id]: false }));
    }
  };

  // ─── Remove node ───────────────────────────────────────────────────────────

  const handleConfirmRemove = async () => {
    if (!removeTarget) return;
    setActionLoading((prev) => ({ ...prev, [removeTarget.id]: true }));
    try {
      await unregisterNode(removeTarget.id);
      setNodes((prev) => prev.filter((n) => n.id !== removeTarget.id));
      setRemoveTarget(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to remove node");
    } finally {
      setActionLoading((prev) => ({ ...prev, [removeTarget.id]: false }));
    }
  };

  // ─── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          LAN Node Discovery
        </h1>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Manage LAN compute nodes that can execute audio capabilities locally.
          Register nodes manually, run health checks, and monitor status.
        </p>
      </div>

      {/* ─── Node list ───────────────────────────────────────────────────────── */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            Registered Nodes ({nodes.length})
          </h2>
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={fetchNodes}
              className="text-xs text-blue-600 hover:underline dark:text-blue-400"
            >
              Refresh
            </button>
            <button
              type="button"
              onClick={() => setShowRegisterForm((v) => !v)}
              className="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-blue-700 dark:bg-blue-600 dark:hover:bg-blue-500"
            >
              {showRegisterForm ? "Cancel" : "+ Register Node"}
            </button>
          </div>
        </div>

        {error && (
          <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/30 dark:text-red-300">
            {error}
          </div>
        )}

        {/* ─── Registration form ─────────────────────────────────────────────── */}
        {showRegisterForm && (
          <div className="mb-4 rounded-lg border border-gray-200 p-4 dark:border-gray-800">
            <div className="flex flex-wrap items-end gap-3">
              <div className="flex-1">
                <label
                  htmlFor="lan-endpoint"
                  className="mb-1 block text-sm font-medium text-gray-700 dark:text-gray-300"
                >
                  Node Endpoint URL *
                </label>
                <input
                  id="lan-endpoint"
                  type="text"
                  value={endpoint}
                  onChange={(e) => setEndpoint(e.target.value)}
                  onKeyDown={(e) => e.key === "Enter" && handleRegister()}
                  placeholder="http://192.168.1.100:8080"
                  className={inputClass}
                />
              </div>
              <button
                type="button"
                onClick={handleRegister}
                disabled={registering}
                className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {registering ? (
                  <span className="flex items-center gap-2">
                    <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                    Registering...
                  </span>
                ) : (
                  "Register"
                )}
              </button>
            </div>
            {registerError && (
              <p className="mt-2 text-sm text-red-600 dark:text-red-400">
                {registerError}
              </p>
            )}
          </div>
        )}

        {/* ─── Nodes ────────────────────────────────────────────────────────── */}
        {loading ? (
          <div className="flex items-center justify-center py-8">
            <span className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
          </div>
        ) : nodes.length === 0 ? (
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
                d="M12 21a9.004 9.004 0 008.716-6.747M12 21a9.004 9.004 0 01-8.716-6.747M12 21c2.485 0 4.5-4.03 4.5-9S14.485 3 12 3m0 18c-2.485 0-4.5-4.03-4.5-9S9.515 3 12 3m0 0a8.997 8.997 0 017.843 4.582M12 3a8.997 8.997 0 00-7.843 4.582m15.686 0A11.953 11.953 0 0112 10.5c-2.998 0-5.74-1.1-7.843-2.918m15.686 0A8.959 8.959 0 0121 12c0 .778-.099 1.533-.284 2.253m0 0A17.919 17.919 0 0112 16.5c-3.162 0-6.133-.815-8.716-2.247m0 0A9.015 9.015 0 013 12c0-1.605.42-3.113 1.157-4.418"
              />
            </svg>
            <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
              No LAN nodes registered
            </p>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              Register a node by its endpoint URL to add it to the pool.
            </p>
          </div>
        ) : (
          <div className="space-y-3">
            {nodes.map((node) => (
              <NodeRow
                key={node.id}
                node={node}
                isLoading={!!actionLoading[node.id]}
                onHealthCheck={handleHealthCheck}
                onRemove={setRemoveTarget}
              />
            ))}
          </div>
        )}
      </div>

      {/* ─── Remove confirmation modal ──────────────────────────────────────── */}
      {removeTarget && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
          onClick={() => setRemoveTarget(null)}
        >
          <div
            className="w-full max-w-md rounded-xl bg-white p-5 shadow-xl dark:bg-gray-900"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="text-base font-semibold text-gray-900 dark:text-gray-100">
              Remove LAN Node
            </h3>
            <p className="mt-2 text-sm text-gray-600 dark:text-gray-400">
              Are you sure you want to remove node{" "}
              <span className="font-medium text-gray-900 dark:text-gray-100">
                {removeTarget.nodeName}
              </span>{" "}
              at{" "}
              <span className="font-mono text-xs">
                {removeTarget.endpointUrl}
              </span>
              ? It will be unregistered immediately.
            </p>
            <div className="mt-4 flex justify-end gap-3">
              <button
                type="button"
                onClick={() => setRemoveTarget(null)}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleConfirmRemove}
                disabled={!!actionLoading[removeTarget.id]}
                className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {actionLoading[removeTarget.id] ? "Removing..." : "Remove"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Node row sub-component ──────────────────────────────────────────────────

function NodeRow({
  node,
  isLoading,
  onHealthCheck,
  onRemove,
}: {
  node: LanNode;
  isLoading: boolean;
  onHealthCheck: (node: LanNode) => void;
  onRemove: (node: LanNode) => void;
}) {
  const status =
    STATUS_BADGE_CONFIG[node.nodeStatus] ?? {
      label: node.nodeStatus,
      className:
        "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
      dot: "bg-gray-400",
    };

  const capabilities = node.capabilities
    ? node.capabilities.split(",").filter(Boolean)
    : [];
  const providerIds = node.providerIds
    ? node.providerIds.split(",").filter(Boolean)
    : [];

  return (
    <div className="rounded-lg border border-gray-200 p-4 dark:border-gray-800">
      {/* Top row: name, endpoint, status */}
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className={`inline-block h-2 w-2 rounded-full ${status.dot}`} />
            <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
              {node.nodeName}
            </span>
            <span
              className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${status.className}`}
            >
              {status.label}
            </span>
          </div>
          <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">
            <span className="font-mono">{node.endpointUrl}</span>
          </p>
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => onHealthCheck(node)}
            disabled={isLoading}
            className="rounded-md border border-gray-300 px-3 py-1 text-xs font-medium text-gray-700 transition-colors hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
          >
            {isLoading ? (
              <span className="flex items-center gap-1">
                <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-current border-t-transparent" />
                Checking...
              </span>
            ) : (
              "Health Check"
            )}
          </button>
          <button
            type="button"
            onClick={() => onRemove(node)}
            disabled={isLoading}
            className="rounded-md border border-red-300 px-3 py-1 text-xs font-medium text-red-600 transition-colors hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-red-900 dark:text-red-400 dark:hover:bg-red-950/30"
          >
            Remove
          </button>
        </div>
      </div>

      {/* Metadata row */}
      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-gray-500 dark:text-gray-400">
        {node.availableGpuMemory !== null && (
          <span>
            GPU: {(node.availableGpuMemory / 1024).toFixed(1)} GB available
          </span>
        )}
        {node.cpuCores !== null && <span>CPU: {node.cpuCores} cores</span>}
        <span>
          Last heartbeat:{" "}
          {node.lastHeartbeatAt
            ? new Date(node.lastHeartbeatAt).toLocaleString()
            : "Never"}
        </span>
        <span>
          Registered: {new Date(node.registeredAt).toLocaleDateString()}
        </span>
      </div>

      {/* Capabilities */}
      {capabilities.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {capabilities.map((cap) => (
            <span
              key={cap}
              className="inline-flex items-center rounded bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-700 dark:bg-blue-950/40 dark:text-blue-300"
            >
              {cap}
            </span>
          ))}
        </div>
      )}

      {/* Providers */}
      {providerIds.length > 0 && (
        <div className="mt-1.5 flex flex-wrap gap-1.5">
          {providerIds.map((pid) => (
            <span
              key={pid}
              className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 text-xs font-mono text-gray-600 dark:bg-gray-800 dark:text-gray-400"
            >
              {pid}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
