/**
 * Web client configuration.
 *
 * Mirrors the pattern from mobile/src/config.ts but uses browser-friendly
 * environment variables (Vite-style import.meta.env) with sensible defaults.
 */

// In Vite-based projects, environment variables are exposed via import.meta.env.
// In other bundlers (Webpack, etc.) you can use process.env.
// We support both with a fallback to a default development URL.
const envApiBaseUrl =
  (typeof import.meta !== "undefined" &&
    (import.meta as unknown as { env?: Record<string, string> }).env?.VITE_API_BASE_URL) ||
  (typeof process !== "undefined" && process.env?.VITE_API_BASE_URL) ||
  "http://localhost:9101/api";

const envHubBaseUrl =
  (typeof import.meta !== "undefined" &&
    (import.meta as unknown as { env?: Record<string, string> }).env?.VITE_HUB_BASE_URL) ||
  (typeof process !== "undefined" && process.env?.VITE_HUB_BASE_URL) ||
  "http://localhost:9101";

/** Base URL for all REST API endpoints (includes the /api suffix). */
export const API_BASE_URL = envApiBaseUrl;

/** Base URL for SignalR hub endpoints (no /api suffix). */
export const HUB_BASE_URL = envHubBaseUrl;

/** Full URL for the TranscriptionHub SignalR endpoint. */
export const TRANSCRIPTION_HUB_URL = `${HUB_BASE_URL}/hubs/transcription`;

/** Client version identifier sent in the X-Memorix-Client-Version header. */
export const CLIENT_VERSION = "memorix-web/0.1.0";
