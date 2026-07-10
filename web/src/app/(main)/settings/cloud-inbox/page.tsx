"use client";

import { useCallback, useEffect, useState } from "react";
import { toast } from "sonner";
import {
  AlertTriangle,
  CheckCircle2,
  Cloud,
  CloudOff,
  Download,
  Loader2,
  Save,
  Shield,
} from "lucide-react";
import { ApiRequestError, cloudInboxApi } from "@/lib/api";
import type {
  CloudInboxPullStrategy,
  CloudInboxRetention,
  CloudInboxSyncLog,
} from "@/lib/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

const MAX_SYNC_LOGS = 10;

function formatSyncTime(timeStr: string | null | undefined): string {
  if (!timeStr) return "从未同步";
  const d = new Date(timeStr);
  return d.toLocaleString("zh-CN");
}

function retentionLabel(retention: CloudInboxRetention): string {
  switch (retention) {
    case "deleteOriginal":
      return "删除原始文件";
    case "deleteAll":
      return "全部删除";
    default:
      return "保留副本";
  }
}

function logStatusLabel(status: CloudInboxSyncLog["status"]): string {
  switch (status) {
    case "success":
      return "成功";
    case "partial":
      return "部分失败";
    default:
      return "失败";
  }
}

function ToggleSwitch({
  checked,
  disabled,
  onChange,
}: {
  checked: boolean;
  disabled?: boolean;
  onChange: () => void;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => !disabled && onChange()}
      className={cn(
        "relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors disabled:cursor-not-allowed disabled:opacity-50",
        checked ? "bg-primary" : "bg-input"
      )}
    >
      <span
        className={cn(
          "pointer-events-none inline-block size-5 transform rounded-full bg-background shadow-lg ring-0 transition-transform",
          checked ? "translate-x-5" : "translate-x-0"
        )}
      />
    </button>
  );
}

function RadioGroup<T extends string>({
  value,
  onChange,
  options,
  disabled,
}: {
  value: T;
  onChange: (v: T) => void;
  options: { value: T; label: string; description?: string }[];
  disabled?: boolean;
}) {
  return (
    <div className="space-y-2">
      {options.map((opt) => (
        <button
          key={opt.value}
          type="button"
          disabled={disabled}
          onClick={() => !disabled && onChange(opt.value)}
          className={cn(
            "flex w-full items-start gap-3 rounded-lg border p-3 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-50",
            value === opt.value
              ? "border-primary bg-primary/5"
              : "border-border hover:bg-muted/50"
          )}
        >
          <span
            className={cn(
              "mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-full border-2",
              value === opt.value
                ? "border-primary"
                : "border-muted-foreground/40"
            )}
          >
            {value === opt.value && (
              <span className="size-2 rounded-full bg-primary" />
            )}
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium">{opt.label}</p>
            {opt.description && (
              <p className="mt-0.5 text-xs text-muted-foreground">
                {opt.description}
              </p>
            )}
          </div>
        </button>
      ))}
    </div>
  );
}

export default function CloudInboxPage() {
  const [cloudEnabled, setCloudEnabled] = useState(false);
  const [privacyDialogOpen, setPrivacyDialogOpen] = useState(false);
  const [pullStrategy, setPullStrategy] =
    useState<CloudInboxPullStrategy>("manual");
  const [cloudRetention, setCloudRetention] =
    useState<CloudInboxRetention>("keep");
  const [cloudApiBaseUrl, setCloudApiBaseUrl] = useState("");
  const [cloudWorkspaceId, setCloudWorkspaceId] = useState("");
  const [authToken, setAuthToken] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isPulling, setIsPulling] = useState(false);
  const [lastSyncTime, setLastSyncTime] = useState<string | null>(null);
  const [lastPullSummary, setLastPullSummary] = useState<string | null>(null);
  const [syncLogs, setSyncLogs] = useState<CloudInboxSyncLog[]>([]);

  const isConnected =
    cloudEnabled && Boolean(cloudApiBaseUrl.trim()) && Boolean(cloudWorkspaceId.trim());

  const loadSyncLogs = useCallback(async () => {
    const logs = await cloudInboxApi.listLogs(MAX_SYNC_LOGS);
    setSyncLogs(logs);
    setLastSyncTime(logs[0]?.finishedAt ?? null);
    if (logs[0]) {
      setLastPullSummary(
        logs[0].status === "failed"
          ? logs[0].errorMessage ?? "上次拉取失败"
          : `已拉取 ${logs[0].pulledCount} 条，失败 ${logs[0].failedCount} 条`
      );
    } else {
      setLastPullSummary(null);
    }
  }, []);

  useEffect(() => {
    const load = async () => {
      setIsLoading(true);
      try {
        const settings = await cloudInboxApi.getSettings();
        setCloudEnabled(settings.enabled);
        setPullStrategy(settings.pullStrategy);
        setCloudRetention(settings.retention);
        setCloudApiBaseUrl(settings.cloudApiBaseUrl ?? "");
        setCloudWorkspaceId(settings.cloudWorkspaceId ?? "");
        await loadSyncLogs();
      } catch (err) {
        const message =
          err instanceof ApiRequestError ? err.message : "加载云端收件箱设置失败";
        toast.error(message);
      } finally {
        setIsLoading(false);
      }
    };
    load();
  }, [loadSyncLogs]);

  const saveSettings = async (enabled = cloudEnabled) => {
    setIsSaving(true);
    try {
      const settings = await cloudInboxApi.updateSettings({
        enabled,
        pullStrategy,
        retention: cloudRetention,
        cloudApiBaseUrl: cloudApiBaseUrl.trim() || undefined,
        cloudWorkspaceId: cloudWorkspaceId.trim() || undefined,
      });
      setCloudEnabled(settings.enabled);
      setCloudApiBaseUrl(settings.cloudApiBaseUrl ?? cloudApiBaseUrl);
      setCloudWorkspaceId(settings.cloudWorkspaceId ?? cloudWorkspaceId);
      toast.success("云端收件箱设置已保存");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "保存设置失败";
      toast.error(message);
      throw err;
    } finally {
      setIsSaving(false);
    }
  };

  const handleToggleCloud = () => {
    if (!cloudEnabled) {
      if (!cloudApiBaseUrl.trim() || !cloudWorkspaceId.trim()) {
        toast.error("请先填写云端 API 地址和云端工作区 ID");
        return;
      }
      setPrivacyDialogOpen(true);
    } else {
      saveSettings(false).catch(() => {});
    }
  };

  const handleConfirmEnable = async () => {
    try {
      await saveSettings(true);
      setPrivacyDialogOpen(false);
    } catch {
      // saveSettings already shows the error.
    }
  };

  const handlePullNow = async () => {
    if (!authToken.trim()) {
      toast.error("请输入云端访问 Token");
      return;
    }

    setIsPulling(true);
    setLastPullSummary(null);
    try {
      const result = await cloudInboxApi.pull({
        cloudApiBaseUrl: cloudApiBaseUrl.trim() || undefined,
        cloudWorkspaceId: cloudWorkspaceId.trim() || undefined,
        authToken: authToken.trim(),
        retention: cloudRetention,
      });
      setLastSyncTime(result.pulledAt);
      setLastPullSummary(
        `已拉取 ${result.pulledCount} 条，失败 ${result.failedCount} 条`
      );
      await loadSyncLogs();
      toast.success("云端收件箱拉取完成");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "拉取云端收件箱失败";
      const failedAt = new Date().toISOString();
      setLastSyncTime(failedAt);
      setLastPullSummary(message);
      await loadSyncLogs().catch(() => {});
      toast.error(message);
    } finally {
      setIsPulling(false);
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h2 className="flex items-center gap-2 text-lg font-semibold">
          {cloudEnabled ? (
            <Cloud className="size-5 text-green-600" />
          ) : (
            <CloudOff className="size-5" />
          )}
          云端收件箱
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          管理云端收件箱的启用、拉取策略与隐私设置
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">连接配置</CardTitle>
          <CardDescription>
            用于把手机端采集到云端 Inbox 的资料拉取到当前本地工作区。
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isLoading ? (
            <div className="flex items-center gap-2 py-4 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" />
              正在加载设置...
            </div>
          ) : (
            <>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="cloud-api-base-url">云端 API 地址</Label>
                  <Input
                    id="cloud-api-base-url"
                    placeholder="例如：https://api.example.com"
                    value={cloudApiBaseUrl}
                    onChange={(e) => setCloudApiBaseUrl(e.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="cloud-workspace-id">云端工作区 ID</Label>
                  <Input
                    id="cloud-workspace-id"
                    placeholder="云端 workspace id"
                    value={cloudWorkspaceId}
                    onChange={(e) => setCloudWorkspaceId(e.target.value)}
                  />
                </div>
              </div>

              <div className="flex items-center justify-between rounded-lg border px-4 py-3">
                <div className="flex items-center gap-3">
                  {cloudEnabled ? (
                    <Cloud className="size-5 text-green-600" />
                  ) : (
                    <CloudOff className="size-5 text-muted-foreground" />
                  )}
                  <div>
                    <p className="text-sm font-medium">
                      {cloudEnabled ? "已启用" : "未启用"}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {cloudEnabled
                        ? "手机端采集资料可通过云端 Inbox 拉取到本地"
                        : "启用前请确认云端资料会短暂经过云服务器"}
                    </p>
                  </div>
                </div>
                <ToggleSwitch
                  checked={cloudEnabled}
                  disabled={isSaving}
                  onChange={handleToggleCloud}
                />
              </div>

              <div className="flex justify-end">
                <Button onClick={() => saveSettings()} disabled={isSaving}>
                  {isSaving ? (
                    <Loader2 className="mr-2 size-4 animate-spin" />
                  ) : (
                    <Save className="mr-2 size-4" />
                  )}
                  保存设置
                </Button>
              </div>
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">连接状态</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap items-center gap-4">
            <div className="flex items-center gap-2">
              <span className="text-xs font-medium text-muted-foreground">
                连接状态：
              </span>
              <Badge
                className={
                  isConnected
                    ? "bg-green-100 text-green-700"
                    : "bg-slate-100 text-slate-600"
                }
              >
                {isConnected ? "已配置" : "未配置"}
              </Badge>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs font-medium text-muted-foreground">
                上次同步：
              </span>
              <span className="text-sm">{formatSyncTime(lastSyncTime)}</span>
            </div>
            {lastPullSummary && (
              <div className="text-sm text-muted-foreground">
                {lastPullSummary}
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex items-start justify-between gap-3">
            <div>
              <CardTitle className="text-base">同步日志</CardTitle>
              <CardDescription>
                最近 {MAX_SYNC_LOGS} 次云端 Inbox 拉取结果已持久保存到当前工作区。
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {syncLogs.length === 0 ? (
            <div className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
              还没有同步记录。完成一次立即拉取后，这里会显示结果。
            </div>
          ) : (
            <div className="space-y-2">
              {syncLogs.map((log) => (
                <div
                  key={log.id}
                  className="grid gap-3 rounded-lg border p-3 sm:grid-cols-[1fr_auto]"
                >
                  <div className="min-w-0 space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge
                        className={
                          log.status === "success"
                            ? "bg-green-100 text-green-700"
                            : log.status === "partial"
                              ? "bg-amber-100 text-amber-700"
                              : "bg-red-100 text-red-700"
                        }
                      >
                        {logStatusLabel(log.status)}
                      </Badge>
                      <span className="text-sm font-medium">
                        {formatSyncTime(log.finishedAt)}
                      </span>
                      <span className="text-xs text-muted-foreground">
                        {retentionLabel(log.retention)}
                      </span>
                    </div>
                    <p className="text-xs text-muted-foreground">
                      云端工作区：{log.cloudWorkspaceId || "未填写"}
                      {log.cloudApiBaseUrl && ` · ${log.cloudApiBaseUrl}`}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      耗时：{Math.max(0, Math.round(log.durationMs / 1000))} 秒
                      {log.nextCursor && ` · 游标：${log.nextCursor}`}
                    </p>
                    {log.errorMessage && (
                      <p
                        className={cn(
                          "text-xs",
                          log.status === "failed"
                            ? "text-red-600"
                            : "text-muted-foreground"
                        )}
                      >
                        {log.errorMessage}
                      </p>
                    )}
                  </div>
                  <div className="flex items-center gap-2 text-sm">
                    <span className="rounded-md bg-muted px-2 py-1">
                      拉取 {log.pulledCount}
                    </span>
                    <span className="rounded-md bg-muted px-2 py-1">
                      失败 {log.failedCount}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">拉取策略</CardTitle>
          <CardDescription>
            设置桌面端从云端收件箱拉取资料的策略
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <RadioGroup<CloudInboxPullStrategy>
            value={pullStrategy}
            onChange={setPullStrategy}
            disabled={!cloudEnabled}
            options={[
              {
                value: "manual",
                label: "手动拉取",
                description: "需要手动点击拉取按钮",
              },
              {
                value: "onStartup",
                label: "启动时自动拉取",
                description: "桌面端启动时自动拉取云端收件箱",
              },
              {
                value: "scheduled",
                label: "定时拉取（每30分钟）",
                description: "每隔 30 分钟自动拉取一次",
              },
            ]}
          />
          <div className="grid gap-3 sm:grid-cols-[1fr_auto]">
            <Input
              type="password"
              placeholder="云端访问 Token，仅用于本次拉取，不保存"
              value={authToken}
              onChange={(e) => setAuthToken(e.target.value)}
              disabled={!cloudEnabled}
            />
            <Button
              variant="outline"
              onClick={handlePullNow}
              disabled={!cloudEnabled || isPulling || !isConnected}
            >
              {isPulling ? (
                <Loader2 className="mr-1.5 size-3.5 animate-spin" />
              ) : (
                <Download className="mr-1.5 size-3.5" />
              )}
              立即拉取
            </Button>
          </div>
          {!cloudEnabled && (
            <span className="text-xs text-muted-foreground">
              请先启用云端收件箱
            </span>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Shield className="size-4 text-muted-foreground" />
            隐私设置
          </CardTitle>
          <CardDescription>
            设置云端收件箱资料在拉取后的云端保留策略
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <RadioGroup<CloudInboxRetention>
            value={cloudRetention}
            onChange={setCloudRetention}
            disabled={!cloudEnabled}
            options={[
              {
                value: "keep",
                label: "拉取后保留云端副本",
                description: "云端保留原始文件，可多次拉取",
              },
              {
                value: "deleteOriginal",
                label: "拉取后删除云端原始文件",
                description: "拉取完成后删除云端原始文件，仅保留元数据",
              },
              {
                value: "deleteAll",
                label: "拉取后全部删除",
                description: "高隐私模式，拉取完成后删除云端所有相关数据",
              },
            ]}
          />
          {cloudRetention === "deleteAll" && (
            <div className="flex items-start gap-2 rounded-lg bg-amber-50 p-3">
              <AlertTriangle className="mt-0.5 size-4 shrink-0 text-amber-600" />
              <p className="text-xs text-amber-700">
                当前后端已把保留策略传给拉取接口；云端删除动作需云端 Inbox 服务支持。
              </p>
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={privacyDialogOpen} onOpenChange={setPrivacyDialogOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Shield className="size-5 text-amber-500" />
              隐私提示
            </DialogTitle>
            <DialogDescription>
              启用云端收件箱后，手机端采集的资料将上传到云端服务器。桌面端会按你的设置将资料拉取回本地工作区。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button onClick={handleConfirmEnable} disabled={isSaving}>
              {isSaving ? (
                <Loader2 className="mr-2 size-4 animate-spin" />
              ) : (
                <CheckCircle2 className="mr-2 size-4" />
              )}
              我已了解，启用
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
