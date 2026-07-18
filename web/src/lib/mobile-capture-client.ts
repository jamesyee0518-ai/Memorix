const MOBILE_CAPTURE_CLIENT_ID_KEY = "memorix_mobile_capture_client_id";

export function getMobileCaptureClientId() {
  if (typeof window === "undefined") return undefined;

  const existing = localStorage.getItem(MOBILE_CAPTURE_CLIENT_ID_KEY);
  if (existing) return existing;

  const id =
    typeof crypto !== "undefined" && "randomUUID" in crypto
      ? crypto.randomUUID()
      : `mobile-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  localStorage.setItem(MOBILE_CAPTURE_CLIENT_ID_KEY, id);
  return id;
}
