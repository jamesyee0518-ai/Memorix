import { API_BASE_URL, CLIENT_VERSION } from "../config";
import {
  clearDeviceTokens,
  getDeviceAccessToken,
  getDeviceRefreshToken,
  setDeviceAccessToken,
  setDeviceRefreshToken,
} from "../storage/auth";

type ApiEnvelope<T> = {
  success: boolean;
  data?: T;
  error?: { code: string; message: string };
};

export type CaptureKind = "text" | "url" | "upload";

export async function apiRequest<T>(
  path: string,
  init: RequestInit = {},
  retrying = false
) {
  const token = await getDeviceAccessToken();
  const headers = {
    Accept: "application/json",
    "X-Memorix-Client-Version": CLIENT_VERSION,
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(init.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
    ...((init.headers as Record<string, string> | undefined) ?? {}),
  };

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers,
  });
  const body = (await response.json()) as ApiEnvelope<T>;
  if (response.status === 401 && !retrying && path !== "/mobile/devices/refresh") {
    const refreshed = await refreshDeviceToken().catch(() => undefined);
    if (refreshed) return apiRequest<T>(path, init, true);
  }

  if (!response.ok || body.success === false) {
    throw new Error(body.error?.message ?? `Request failed: ${response.status}`);
  }
  return body.data as T;
}

export async function refreshDeviceToken() {
  const refreshToken = await getDeviceRefreshToken();
  if (!refreshToken) return undefined;

  const response = await fetch(`${API_BASE_URL}/mobile/devices/refresh`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-Memorix-Client-Version": CLIENT_VERSION,
    },
    body: JSON.stringify({ refreshToken }),
  });
  const body = (await response.json()) as ApiEnvelope<DeviceBindingResponse>;
  if (!response.ok || body.success === false || !body.data) {
    await clearDeviceTokens();
    return undefined;
  }

  await setDeviceAccessToken(body.data.deviceAccessToken);
  await setDeviceRefreshToken(body.data.refreshToken);
  return body.data;
}

type DeviceBindingResponse = {
  device: {
    id: string;
    workspaceId: string;
    clientId: string;
    status: string;
  };
  deviceAccessToken: string;
  refreshToken: string;
  expiresAt: string;
  refreshTokenExpiresAt: string;
};

export function bindDevice(input: {
  clientId: string;
  deviceName?: string;
  platform?: string;
  pushToken?: string;
}) {
  return apiRequest<DeviceBindingResponse>("/mobile/devices/bind", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function pairDevice(input: {
  code: string;
  clientId: string;
  deviceName?: string;
  platform?: string;
  pushToken?: string;
}) {
  return apiRequest<DeviceBindingResponse>("/mobile/devices/pair", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function deactivateDevice(clientId: string) {
  return apiRequest("/mobile/devices/deactivate", {
    method: "POST",
    body: JSON.stringify({ clientId }),
  });
}

export function captureText(input: {
  clientId: string;
  contentText: string;
  topicId?: string;
}) {
  return apiRequest("/mobile/capture/text", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function captureUrl(input: {
  clientId: string;
  sourceUrl: string;
  title?: string;
  topicId?: string;
}) {
  return apiRequest("/mobile/capture/url", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function captureUpload(input: {
  clientId: string;
  uri: string;
  name: string;
  mimeType: string;
  topicId?: string;
}) {
  const formData = new FormData();
  formData.append("clientId", input.clientId);
  if (input.topicId) formData.append("topicId", input.topicId);
  formData.append("file", {
    uri: input.uri,
    name: input.name,
    type: input.mimeType,
  } as unknown as Blob);

  return apiRequest("/mobile/capture/upload", {
    method: "POST",
    body: formData,
  });
}
