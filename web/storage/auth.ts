/**
 * Auth token storage for the web client.
 *
 * Mirrors the pattern from mobile/src/storage/auth.ts but uses localStorage
 * instead of AsyncStorage. Stores the JWT access token and refresh token
 * used by the apiRequest wrapper for authenticated API calls.
 */

const ACCESS_TOKEN_KEY = "memorix.web.access_token";
const REFRESH_TOKEN_KEY = "memorix.web.refresh_token";

/** Reads the stored access token, or null if not authenticated. */
export function getAccessToken(): string | null {
  try {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  } catch {
    return null;
  }
}

/** Persists the access token. */
export function setAccessToken(token: string): void {
  try {
    localStorage.setItem(ACCESS_TOKEN_KEY, token);
  } catch {
    // localStorage may be unavailable (private browsing, etc.)
  }
}

/** Reads the stored refresh token, or null if not present. */
export function getRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  } catch {
    return null;
  }
}

/** Persists the refresh token. */
export function setRefreshToken(token: string): void {
  try {
    localStorage.setItem(REFRESH_TOKEN_KEY, token);
  } catch {
    // localStorage may be unavailable
  }
}

/** Removes both access and refresh tokens. */
export function clearTokens(): void {
  try {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  } catch {
    // best effort
  }
}
