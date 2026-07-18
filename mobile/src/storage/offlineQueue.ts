import AsyncStorage from "@react-native-async-storage/async-storage";
import NetInfo from "@react-native-community/netinfo";
import { captureText, captureUpload, captureUrl } from "../api/client";

const QUEUE_KEY = "memorix.mobile.offline_queue";

type OfflineQueuePayload =
  | {
      kind: "text";
      clientId: string;
      contentText: string;
      topicId?: string;
    }
  | {
      kind: "url";
      clientId: string;
      sourceUrl: string;
      title?: string;
      topicId?: string;
    }
  | {
      kind: "upload";
      clientId: string;
      uri: string;
      name: string;
      mimeType: string;
      topicId?: string;
    };

export type OfflineQueueItem = OfflineQueuePayload & {
  id: string;
  createdAt: string;
  updatedAt: string;
  attempts: number;
  nextAttemptAt?: string;
  lastError?: string;
};

let activeFlush: Promise<{
  sent: number;
  failed: number;
  remaining: OfflineQueueItem[];
}> | null = null;

export async function readQueue() {
  const raw = await AsyncStorage.getItem(QUEUE_KEY);
  if (!raw) return [];

  try {
    const parsed = JSON.parse(raw) as OfflineQueueItem[];
    if (!Array.isArray(parsed)) return [];
    return parsed.map((item) => ({
      ...item,
      updatedAt: item.updatedAt ?? item.createdAt,
      attempts: Number.isFinite(item.attempts) ? item.attempts : 0,
    }));
  } catch {
    return [];
  }
}

export async function enqueueCapture(item: OfflineQueuePayload) {
  const now = new Date().toISOString();
  const next: OfflineQueueItem = {
    ...item,
    id: `queue-${Date.now()}-${Math.random().toString(16).slice(2)}`,
    createdAt: now,
    updatedAt: now,
    attempts: 0,
  } as OfflineQueueItem;
  const queue = await readQueue();
  await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify([next, ...queue]));
  return next;
}

export function flushQueue(force = false) {
  if (activeFlush) return activeFlush;
  activeFlush = flushQueueInternal(force).finally(() => {
    activeFlush = null;
  });
  return activeFlush;
}

async function flushQueueInternal(force: boolean) {
  const state = await NetInfo.fetch();
  if (!state.isConnected) {
    return { sent: 0, failed: 0, remaining: await readQueue() };
  }

  const queue = await readQueue();
  const remaining: OfflineQueueItem[] = [];
  let sent = 0;
  let failed = 0;
  const now = Date.now();

  for (const item of [...queue].reverse()) {
    if (!force && item.nextAttemptAt && Date.parse(item.nextAttemptAt) > now) {
      remaining.unshift(item);
      continue;
    }

    try {
      if (item.kind === "text") await captureText(item);
      if (item.kind === "url") await captureUrl(item);
      if (item.kind === "upload") await captureUpload(item);
      sent += 1;
    } catch (error) {
      failed += 1;
      const attempts = (item.attempts ?? 0) + 1;
      const delayMs = Math.min(30 * 60_000, 5_000 * 2 ** Math.min(attempts - 1, 8));
      remaining.unshift({
        ...item,
        attempts,
        updatedAt: new Date().toISOString(),
        nextAttemptAt: new Date(Date.now() + delayMs).toISOString(),
        lastError: error instanceof Error ? error.message : "同步失败",
      });
    }
  }

  await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(remaining));
  return { sent, failed, remaining };
}
