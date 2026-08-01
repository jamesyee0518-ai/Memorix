"use client";

/**
 * ProviderSelector
 *
 * A controlled dropdown of available ASR providers. When a provider is
 * selected the component renders its capability descriptor (execution modes,
 * credential modes, supported capabilities, data residency) below the select.
 *
 * A "Explain Routing" button calls the routing explain endpoint and renders
 * the decision steps inline so users can understand why a provider was (or
 * would be) chosen.
 *
 * Uses:
 *   - listProviders() / explainRouting() from ../api/audioClient
 *   - AsrProviderDescriptor / AsrRoutingContext / RoutingDecision from ../types/audio
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { explainRouting, listProviders } from "../api/audioClient";
import type {
  AsrProviderDescriptor,
  AsrRoutingContext,
  RoutingDecision,
} from "../types/audio";

export interface ProviderSelectorProps {
  /** Currently selected provider id, or null for "auto". */
  value: string | null;
  /** Called when the user picks a different provider. */
  onChange: (providerId: string | null) => void;
  /**
   * Optional routing context used by the "Explain Routing" action.
   * If omitted, the button is hidden.
   */
  routingContext?: AsrRoutingContext;
  /** Pre-loaded provider list. When omitted the component fetches on mount. */
  providers?: AsrProviderDescriptor[];
  /** Optional label for the select element. */
  label?: string;
  /** Optional id for the select element (defaults to "provider-select"). */
  id?: string;
  /** Disables the control. */
  disabled?: boolean;
}

const capabilityLabels: Record<string, string> = {
  supportsStreaming: "Streaming",
  supportsBatch: "Batch",
  supportsVad: "VAD",
  supportsPunctuation: "Punctuation",
  supportsDiarization: "Diarization",
  supportsHotwords: "Hotwords",
  supportsWordTimestamp: "Word Timestamp",
  supportsSegmentTimestamp: "Segment Timestamp",
};

export function ProviderSelector({
  value,
  onChange,
  routingContext,
  providers: initialProviders,
  label = "Provider",
  id = "provider-select",
  disabled = false,
}: ProviderSelectorProps) {
  const [providers, setProviders] = useState<AsrProviderDescriptor[]>(
    initialProviders ?? [],
  );
  const [loading, setLoading] = useState(!initialProviders);
  const [error, setError] = useState<string | null>(null);

  const [routingDecision, setRoutingDecision] =
    useState<RoutingDecision | null>(null);
  const [routingLoading, setRoutingLoading] = useState(false);
  const [routingError, setRoutingError] = useState<string | null>(null);
  const [showRouting, setShowRouting] = useState(false);

  // Fetch providers on mount when not provided via props.
  useEffect(() => {
    if (initialProviders) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    listProviders()
      .then((list) => {
        if (!cancelled) setProviders(list);
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load providers");
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [initialProviders]);

  const selectedDescriptor = useMemo<AsrProviderDescriptor | null>(() => {
    if (!value) return null;
    return providers.find((p) => p.providerId === value) ?? null;
  }, [providers, value]);

  const handleExplainRouting = useCallback(async () => {
    if (!routingContext) return;
    setRoutingLoading(true);
    setRoutingError(null);
    setShowRouting(true);
    try {
      const decision = await explainRouting(routingContext);
      setRoutingDecision(decision);
    } catch (err: unknown) {
      setRoutingError(
        err instanceof Error ? err.message : "Failed to explain routing",
      );
      setRoutingDecision(null);
    } finally {
      setRoutingLoading(false);
    }
  }, [routingContext]);

  return (
    <div className="space-y-3">
      {/* Select + routing button */}
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex-1 min-w-[200px]">
          <label
            htmlFor={id}
            className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
          >
            {label}
          </label>
          <select
            id={id}
            value={value ?? ""}
            disabled={disabled || loading}
            onChange={(e) => onChange(e.target.value === "" ? null : e.target.value)}
            className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm transition-colors focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:cursor-not-allowed disabled:opacity-60 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100"
          >
            <option value="">Auto (let router decide)</option>
            {providers.map((p) => (
              <option key={`${p.providerId}:${p.modelId}`} value={p.providerId}>
                {p.providerId} ({p.modelId})
              </option>
            ))}
          </select>
        </div>

        {routingContext && (
          <button
            type="button"
            onClick={handleExplainRouting}
            disabled={disabled || routingLoading}
            className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 shadow-sm transition-colors hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-60 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-300 dark:hover:bg-gray-800"
          >
            {routingLoading ? "Explaining..." : "Explain Routing"}
          </button>
        )}
      </div>

      {/* Loading / error states for the provider list */}
      {loading && (
        <p className="text-xs text-gray-500 dark:text-gray-400">
          Loading providers...
        </p>
      )}
      {error && (
        <p className="text-xs text-red-600 dark:text-red-400">{error}</p>
      )}

      {/* Selected provider descriptor */}
      {selectedDescriptor && (
        <ProviderDescriptorCard descriptor={selectedDescriptor} />
      )}

      {/* Routing explanation panel */}
      {showRouting && (
        <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 dark:border-blue-900 dark:bg-blue-950/40">
          <div className="flex items-center justify-between mb-2">
            <h4 className="text-sm font-semibold text-blue-900 dark:text-blue-200">
              Routing Decision
            </h4>
            <button
              type="button"
              onClick={() => setShowRouting(false)}
              className="text-xs text-blue-600 hover:underline dark:text-blue-400"
            >
              Close
            </button>
          </div>

          {routingError && (
            <p className="text-sm text-red-600 dark:text-red-400">
              {routingError}
            </p>
          )}

          {routingLoading && !routingDecision && !routingError && (
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Resolving routing...
            </p>
          )}

          {routingDecision && (
            <div className="space-y-2">
              <div className="flex flex-wrap gap-2">
                <DescriptorPill label="Provider" value={routingDecision.selectedProviderId} />
                <DescriptorPill label="Model" value={routingDecision.selectedModelId} />
                <DescriptorPill label="Execution" value={routingDecision.executionMode} />
                <DescriptorPill label="Credential" value={routingDecision.credentialMode} />
              </div>

              {routingDecision.steps.length > 0 && (
                <div>
                  <p className="text-xs font-medium text-blue-800 dark:text-blue-300 mb-1">
                    Steps
                  </p>
                  <ol className="list-decimal list-inside space-y-1 text-xs text-gray-700 dark:text-gray-300">
                    {routingDecision.steps.map((step, i) => (
                      <li key={i}>{step}</li>
                    ))}
                  </ol>
                </div>
              )}

              {routingDecision.eliminatedProviders.length > 0 && (
                <div>
                  <p className="text-xs font-medium text-blue-800 dark:text-blue-300 mb-1">
                    Eliminated providers
                  </p>
                  <div className="flex flex-wrap gap-1">
                    {routingDecision.eliminatedProviders.map((id) => (
                      <span
                        key={id}
                        className="inline-flex items-center rounded bg-red-100 px-1.5 py-0.5 text-xs text-red-700 dark:bg-red-950/50 dark:text-red-300"
                      >
                        {id}
                      </span>
                    ))}
                  </div>
                </div>
              )}

              {routingDecision.fallbackReason && (
                <p className="text-xs text-amber-700 dark:text-amber-400">
                  Fallback: {routingDecision.fallbackReason}
                </p>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ─── Helper sub-components ───────────────────────────────────────────────────

/**
 * Renders a compact card describing a provider's capabilities and constraints.
 */
function ProviderDescriptorCard({
  descriptor,
}: {
  descriptor: AsrProviderDescriptor;
}) {
  const capabilities = Object.entries(capabilityLabels)
    .filter(([key]) => descriptor[key as keyof AsrProviderDescriptor] === true)
    .map(([, label]) => label);

  return (
    <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 dark:border-gray-800 dark:bg-gray-900/50">
      <div className="flex items-center justify-between mb-2">
        <span className="text-sm font-medium text-gray-900 dark:text-gray-100">
          {descriptor.providerId}
          <span className="ml-2 text-xs text-gray-500 dark:text-gray-400">
            / {descriptor.modelId}
          </span>
        </span>
        {descriptor.sendsAudioOffDevice && (
          <span className="inline-flex items-center rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:bg-amber-950/50 dark:text-amber-300">
            Sends audio off-device
          </span>
        )}
      </div>

      <div className="flex flex-wrap gap-1.5 mb-2">
        {descriptor.executionModes.map((mode) => (
          <span
            key={mode}
            className="inline-flex items-center rounded bg-blue-100 px-1.5 py-0.5 text-xs text-blue-700 dark:bg-blue-950/50 dark:text-blue-300"
          >
            {mode}
          </span>
        ))}
        {descriptor.credentialModes.map((mode) => (
          <span
            key={mode}
            className="inline-flex items-center rounded bg-purple-100 px-1.5 py-0.5 text-xs text-purple-700 dark:bg-purple-950/50 dark:text-purple-300"
          >
            {mode}
          </span>
        ))}
      </div>

      {capabilities.length > 0 && (
        <div className="flex flex-wrap gap-1 mb-2">
          {capabilities.map((cap) => (
            <span
              key={cap}
              className="inline-flex items-center rounded bg-green-100 px-1.5 py-0.5 text-xs text-green-700 dark:bg-green-950/50 dark:text-green-300"
            >
              {cap}
            </span>
          ))}
        </div>
      )}

      <div className="grid grid-cols-2 gap-x-4 gap-y-0.5 text-xs text-gray-600 dark:text-gray-400">
        {descriptor.supportedLanguages.length > 0 && (
          <div>
            <span className="font-medium">Languages:</span>{" "}
            {descriptor.supportedLanguages.join(", ")}
          </div>
        )}
        {descriptor.dataRegion && (
          <div>
            <span className="font-medium">Region:</span> {descriptor.dataRegion}
          </div>
        )}
        {descriptor.pricingUnit && (
          <div>
            <span className="font-medium">Pricing:</span> {descriptor.pricingUnit}
          </div>
        )}
        <div>
          <span className="font-medium">Retention:</span>{" "}
          {descriptor.storesProviderData}
        </div>
        {descriptor.acceptedMimeTypes.length > 0 && (
          <div className="col-span-2">
            <span className="font-medium">Formats:</span>{" "}
            {descriptor.acceptedMimeTypes.join(", ")}
          </div>
        )}
      </div>
    </div>
  );
}

function DescriptorPill({ label, value }: { label: string; value: string }) {
  return (
    <span className="inline-flex items-center gap-1 rounded bg-white px-2 py-0.5 text-xs shadow-sm dark:bg-gray-800">
      <span className="font-medium text-gray-500 dark:text-gray-400">{label}:</span>
      <span className="text-gray-900 dark:text-gray-100">{value}</span>
    </span>
  );
}

export default ProviderSelector;
