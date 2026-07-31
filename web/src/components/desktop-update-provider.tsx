"use client";

import type { Update } from "@tauri-apps/plugin-updater";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { Download, RefreshCw, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { isDesktopApp } from "@/lib/desktop";
import { runtimeApi } from "@/lib/api";

export type DesktopUpdateStatus =
  | "idle"
  | "checking"
  | "up-to-date"
  | "available"
  | "downloading"
  | "ready"
  | "installing"
  | "error";

export interface DesktopUpdateState {
  isDesktop: boolean;
  currentVersion: string;
  targetVersion?: string;
  notes?: string;
  publishedAt?: string;
  status: DesktopUpdateStatus;
  downloadedBytes: number;
  totalBytes?: number;
  progress?: number;
  lastCheckedAt?: string;
  error?: string;
  autoCheck: boolean;
  autoDownload: boolean;
}

interface DesktopUpdateContextValue extends DesktopUpdateState {
  checkForUpdates: (silent?: boolean) => Promise<void>;
  downloadUpdate: () => Promise<void>;
  installUpdate: () => Promise<void>;
  deferUpdate: () => void;
  skipVersion: () => void;
  setAutoCheck: (enabled: boolean) => void;
  setAutoDownload: (enabled: boolean) => void;
}

const AUTO_CHECK_KEY = "memorix.desktopUpdate.autoCheck";
const AUTO_DOWNLOAD_KEY = "memorix.desktopUpdate.autoDownload";
const SKIPPED_VERSION_KEY = "memorix.desktopUpdate.skippedVersion";
const CHECK_INTERVAL_MS = 6 * 60 * 60 * 1000;
const STARTUP_DELAY_MS = 20 * 1000;

const DesktopUpdateContext = createContext<DesktopUpdateContextValue | null>(null);

function readBoolean(key: string, fallback: boolean) {
  if (typeof window === "undefined") return fallback;
  const value = window.localStorage.getItem(key);
  return value == null ? fallback : value === "true";
}

function userMessage(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  if (/public key|signature|base64|key/i.test(message)) {
    return "更新签名配置尚未完成，请联系管理员配置正式更新公钥。";
  }
  if (/network|fetch|connect|timeout|dns|http/i.test(message)) {
    return "暂时无法连接更新服务器，请检查网络后重试。";
  }
  return message || "更新操作失败，请稍后重试。";
}

export function DesktopUpdateProvider({ children }: { children: React.ReactNode }) {
  const [desktop, setDesktop] = useState(false);
  const pendingUpdate = useRef<Update | null>(null);
  const downloaded = useRef(false);
  const checking = useRef(false);
  const [state, setState] = useState<DesktopUpdateState>({
    isDesktop: false,
    currentVersion: "",
    status: "idle",
    downloadedBytes: 0,
    autoCheck: readBoolean(AUTO_CHECK_KEY, true),
    autoDownload: readBoolean(AUTO_DOWNLOAD_KEY, false),
  });

  useEffect(() => {
    setDesktop(isDesktopApp());
  }, []);

  useEffect(() => {
    if (!desktop) return;
    setState((current) => ({ ...current, isDesktop: true }));
    void import("@tauri-apps/api/app")
      .then(({ getVersion }) => getVersion())
      .then((version) => setState((current) => ({ ...current, currentVersion: version })))
      .catch(() => undefined);
  }, [desktop]);

  const downloadUpdate = useCallback(async () => {
    const update = pendingUpdate.current;
    if (!update || downloaded.current) return;

    let received = 0;
    let total: number | undefined;
    setState((current) => ({
      ...current,
      status: "downloading",
      downloadedBytes: 0,
      totalBytes: undefined,
      progress: undefined,
      error: undefined,
    }));

    try {
      await update.download((event) => {
        if (event.event === "Started") {
          total = event.data.contentLength;
          setState((current) => ({ ...current, totalBytes: total }));
          return;
        }
        if (event.event === "Progress") {
          received += event.data.chunkLength;
          setState((current) => ({
            ...current,
            downloadedBytes: received,
            totalBytes: total,
            progress: total ? Math.min(100, Math.round((received / total) * 100)) : undefined,
          }));
          return;
        }
        downloaded.current = true;
        setState((current) => ({ ...current, status: "ready", progress: 100 }));
      });
    } catch (error) {
      setState((current) => ({
        ...current,
        status: "error",
        error: userMessage(error),
      }));
      throw error;
    }
  }, []);

  const checkForUpdates = useCallback(
    async (silent = false) => {
      if (!desktop || checking.current || state.status === "installing") return;
      checking.current = true;
      if (!silent) {
        setState((current) => ({ ...current, status: "checking", error: undefined }));
      }

      try {
        pendingUpdate.current?.close().catch(() => undefined);
        pendingUpdate.current = null;
        downloaded.current = false;

        const { check } = await import("@tauri-apps/plugin-updater");
        const update = await check({ timeout: 30_000 });
        const checkedAt = new Date().toISOString();

        if (!update) {
          setState((current) => ({
            ...current,
            status: "up-to-date",
            targetVersion: undefined,
            notes: undefined,
            lastCheckedAt: checkedAt,
            error: undefined,
          }));
          return;
        }

        pendingUpdate.current = update;
        const skipped =
          typeof window !== "undefined"
            ? window.localStorage.getItem(SKIPPED_VERSION_KEY)
            : null;
        setState((current) => ({
          ...current,
          status: skipped === update.version && silent ? "idle" : "available",
          targetVersion: update.version,
          notes: update.body,
          publishedAt: update.date,
          lastCheckedAt: checkedAt,
          downloadedBytes: 0,
          totalBytes: undefined,
          progress: undefined,
          error: undefined,
        }));

        if (state.autoDownload && skipped !== update.version) {
          await downloadUpdate();
        }
      } catch (error) {
        setState((current) => ({
          ...current,
          status: silent ? "idle" : "error",
          error: silent ? undefined : userMessage(error),
          lastCheckedAt: new Date().toISOString(),
        }));
      } finally {
        checking.current = false;
      }
    },
    [desktop, downloadUpdate, state.autoDownload, state.status]
  );

  const installUpdate = useCallback(async () => {
    const update = pendingUpdate.current;
    if (!update) return;

    let prepared = false;
    try {
      if (!downloaded.current) {
        await downloadUpdate();
      }
      const safety = await runtimeApi.updateSafety();
      if (!safety.safeToInstall) {
        throw new Error(safety.message);
      }
      setState((current) => ({ ...current, status: "installing", error: undefined }));
      const { invoke } = await import("@tauri-apps/api/core");
      await invoke<string>("prepare_update_install");
      prepared = true;
      await update.install();
      const { relaunch } = await import("@tauri-apps/plugin-process");
      await relaunch();
    } catch (error) {
      if (prepared) {
        const { invoke } = await import("@tauri-apps/api/core");
        await invoke("resume_after_update_failure").catch(() => undefined);
      }
      setState((current) => ({
        ...current,
        status: "error",
        error: userMessage(error),
      }));
    }
  }, [downloadUpdate]);

  const skipVersion = useCallback(() => {
    if (state.targetVersion && typeof window !== "undefined") {
      window.localStorage.setItem(SKIPPED_VERSION_KEY, state.targetVersion);
    }
    setState((current) => ({ ...current, status: "idle" }));
  }, [state.targetVersion]);

  const deferUpdate = useCallback(() => {
    setState((current) => ({ ...current, status: "idle" }));
  }, []);

  const setAutoCheck = useCallback((enabled: boolean) => {
    window.localStorage.setItem(AUTO_CHECK_KEY, String(enabled));
    setState((current) => ({ ...current, autoCheck: enabled }));
  }, []);

  const setAutoDownload = useCallback((enabled: boolean) => {
    window.localStorage.setItem(AUTO_DOWNLOAD_KEY, String(enabled));
    setState((current) => ({ ...current, autoDownload: enabled }));
  }, []);

  useEffect(() => {
    if (!desktop || !state.autoCheck) return;
    const startupTimer = window.setTimeout(() => void checkForUpdates(true), STARTUP_DELAY_MS);
    const interval = window.setInterval(() => void checkForUpdates(true), CHECK_INTERVAL_MS);
    return () => {
      window.clearTimeout(startupTimer);
      window.clearInterval(interval);
    };
  }, [checkForUpdates, desktop, state.autoCheck]);

  const value = useMemo(
    () => ({
      ...state,
      checkForUpdates,
      downloadUpdate,
      installUpdate,
      deferUpdate,
      skipVersion,
      setAutoCheck,
      setAutoDownload,
    }),
    [
      state,
      checkForUpdates,
      downloadUpdate,
      installUpdate,
      deferUpdate,
      skipVersion,
      setAutoCheck,
      setAutoDownload,
    ]
  );

  const dialogOpen = state.status === "available" || state.status === "downloading" ||
    state.status === "ready" || state.status === "installing";

  return (
    <DesktopUpdateContext.Provider value={value}>
      {children}
      <Dialog open={dialogOpen} onOpenChange={(open) => !open && state.status === "available" && deferUpdate()}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldCheck className="size-5 text-primary" />
              Memorix {state.targetVersion} 可用
            </DialogTitle>
            <DialogDescription>
              更新包将先下载并验证签名，安装前会备份本地数据库并安全停止本地服务。
            </DialogDescription>
          </DialogHeader>

          {state.notes && (
            <div className="max-h-40 overflow-y-auto whitespace-pre-wrap rounded-lg bg-muted p-3 text-sm">
              {state.notes}
            </div>
          )}

          {(state.status === "downloading" || state.status === "ready") && (
            <div className="space-y-2">
              <div className="flex justify-between text-xs text-muted-foreground">
                <span>{state.status === "ready" ? "下载完成" : "正在下载更新包"}</span>
                <span>{state.progress == null ? "计算中" : `${state.progress}%`}</span>
              </div>
              <div className="h-2 overflow-hidden rounded-full bg-muted">
                <div
                  className="h-full bg-primary transition-[width]"
                  style={{ width: `${state.progress ?? 8}%` }}
                />
              </div>
            </div>
          )}

          <DialogFooter>
            {state.status === "available" && (
              <>
                <Button variant="ghost" onClick={skipVersion}>跳过此版本</Button>
                <Button variant="outline" onClick={deferUpdate}>稍后提醒</Button>
                <Button onClick={() => void downloadUpdate()}>
                  <Download className="mr-2 size-4" />
                  下载更新
                </Button>
              </>
            )}
            {state.status === "downloading" && (
              <Button disabled>
                <RefreshCw className="mr-2 size-4 animate-spin" />
                正在下载
              </Button>
            )}
            {state.status === "ready" && (
              <Button onClick={() => void installUpdate()}>安装并重启</Button>
            )}
            {state.status === "installing" && (
              <Button disabled>
                <RefreshCw className="mr-2 size-4 animate-spin" />
                正在安装
              </Button>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </DesktopUpdateContext.Provider>
  );
}

export function useDesktopUpdate() {
  const context = useContext(DesktopUpdateContext);
  if (!context) {
    throw new Error("useDesktopUpdate must be used inside DesktopUpdateProvider");
  }
  return context;
}
