"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import {
  AlertCircle,
  CheckCircle2,
  Cloud,
  CloudOff,
  Database,
  DownloadCloud,
  Inbox,
  Loader2,
  Search,
  Server,
  Smartphone,
  type LucideIcon,
} from "lucide-react";

import { ApiRequestError, cloudInboxApi, runtimeApi, workspaceApi } from "@/lib/api";
import type { CloudInboxStatus, CloudInboxSyncLog, RuntimeHealth, Workspace } from "@/lib/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { cn } from "@/lib/utils";

type FlowState = "ready" | "connected" | "syncing" | "failed" | "off" | "missing";

type FlowStep = {
  key: string;
  label: string;
  status: string;
  state: FlowState;
  detail: string;
  href?: string;
  icon: LucideIcon;
};

function stateClass(state: FlowState): string {
  switch (state) {
    case "ready":
    case "connected":
      return "bg-emerald-100 text-emerald-700";
    case "syncing":
      return "bg-blue-100 text-blue-700";
    case "failed":
      return "bg-red-100 text-red-700";
    case "off":
      return "bg-slate-100 text-slate-600";
    default:
      return "bg-amber-100 text-amber-700";
  }
}

function iconTone(state: FlowState): string {
  switch (state) {
    case "ready":
    case "connected":
      return "bg-emerald-50 text-emerald-700";
    case "syncing":
      return "bg-blue-50 text-blue-700";
    case "failed":
      return "bg-red-50 text-red-700";
    case "off":
      return "bg-slate-100 text-slate-600";
    default:
      return "bg-amber-50 text-amber-700";
  }
}

function formatTime(timeStr?: string): string {
  if (!timeStr) return "暂无记录";
  return new Date(timeStr).toLocaleString("zh-CN");
}

function isHealthy(value?: string): boolean {
  if (!value) return false;
  return ["ok", "healthy", "connected", "available"].includes(value.toLowerCase());
}

function getPullStep(log?: CloudInboxSyncLog, inboxEnabled?: boolean): Pick<FlowStep, "state" | "status" | "detail"> {
  if (!inboxEnabled) {
    return {
      state: "off",
      status: "已关闭",
      detail: "云端 Inbox 关闭后，桌面端不会从云端拉取资料。",
    };
  }

  if (!log) {
    return {
      state: "missing",
      status: "待拉取",
      detail: "尚无拉取记录。",
    };
  }

  if (log.status === "failed") {
    return {
      state: "failed",
      status: "失败",
      detail: log.errorMessage ?? "最近一次拉取失败。",
    };
  }

  if (log.status === "partial") {
    return {
      state: "failed",
      status: "部分失败",
      detail: `最近拉取 ${log.pulledCount} 条，失败 ${log.failedCount} 条。`,
    };
  }

  return {
    state: "connected",
    status: "已同步",
    detail: `最近拉取 ${log.pulledCount} 条，${formatTime(log.finishedAt)}。`,
  };
}

export function HybridDataFlowPanel({
  compact = false,
}: {
  compact?: boolean;
}) {
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [health, setHealth] = useState<RuntimeHealth | null>(null);
  const [cloudStatus, setCloudStatus] = useState<CloudInboxStatus | null>(null);
  const [logs, setLogs] = useState<CloudInboxSyncLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;

    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [currentWorkspace, runtimeHealth, status, syncLogs] = await Promise.all([
          workspaceApi.getCurrent().catch(() => null),
          runtimeApi.health().catch(() => null),
          cloudInboxApi.getStatus().catch(() => null),
          cloudInboxApi.listLogs(1).catch(() => [] as CloudInboxSyncLog[]),
        ]);

        if (!mounted) return;
        setWorkspace(currentWorkspace);
        setHealth(runtimeHealth);
        setCloudStatus(status);
        setLogs(syncLogs);
      } catch (err) {
        if (!mounted) return;
        const message =
          err instanceof ApiRequestError ? err.message : "加载混合模式数据流失败";
        setError(message);
      } finally {
        if (mounted) setLoading(false);
      }
    };

    load();
    return () => {
      mounted = false;
    };
  }, []);

  const latestLog = logs[0];
  const cloudConfigured =
    Boolean(cloudStatus?.connected) ||
    (Boolean(workspace?.cloudApiBaseUrl) && Boolean(workspace?.cloudWorkspaceId));
  const inboxEnabled = Boolean(workspace?.inboxEnabled || cloudStatus?.enabled);
  const pullStep = getPullStep(latestLog, inboxEnabled);
  const databaseReady = isHealthy(health?.database);
  const modelReady = isHealthy(health?.llmService) || workspace?.modelProvider === "ollama" || workspace?.modelProvider === "lmstudio";

  const steps = useMemo<FlowStep[]>(() => [
    {
      key: "capture",
      label: "手机采集",
      status: "可采集",
      state: workspace ? "ready" : "missing",
      detail: "文本、链接和文件可进入采集入口。",
      href: "/capture",
      icon: Smartphone,
    },
    {
      key: "cloud",
      label: "云端 Inbox",
      status: !inboxEnabled ? "已关闭" : cloudConfigured ? "已连接" : "未配置",
      state: !inboxEnabled ? "off" : cloudConfigured ? "connected" : "missing",
      detail: !inboxEnabled
        ? "云端收件箱当前关闭。"
        : cloudConfigured
          ? "云端地址和工作区已配置。"
          : "需要配置云端 API 地址和云端工作区 ID。",
      href: "/settings/cloud-inbox",
      icon: inboxEnabled ? Cloud : CloudOff,
    },
    {
      key: "pull",
      label: "桌面拉取",
      ...pullStep,
      href: "/settings/cloud-inbox",
      icon: DownloadCloud,
    },
    {
      key: "local-inbox",
      label: "本地 Inbox",
      status: workspace ? "可接收" : "未配置",
      state: workspace ? "ready" : "missing",
      detail: "拉取后的资料会先进入本地收件箱。",
      href: "/settings/inbox",
      icon: Inbox,
    },
    {
      key: "process",
      label: "本地处理",
      status: databaseReady ? "可处理" : "待检查",
      state: databaseReady ? "ready" : "missing",
      detail: databaseReady ? "本地数据库可用，导入与处理可继续。" : "请检查运行时数据库状态。",
      href: "/settings/runtime",
      icon: Database,
    },
    {
      key: "retrieval",
      label: "检索 / 问答 / MCP",
      status: modelReady ? "可使用" : "待配置",
      state: modelReady ? "ready" : "missing",
      detail: modelReady ? "模型与检索入口可承接本地知识库。" : "请配置模型后使用问答和智能处理。",
      href: "/qa",
      icon: Search,
    },
  ], [cloudConfigured, databaseReady, inboxEnabled, modelReady, pullStep, workspace]);

  const brokenStep = steps.find((step) => step.state === "failed" || step.state === "off" || step.state === "missing");
  const modeLabel = workspace?.mode === "hybrid" ? "混合模式" : workspace?.mode === "local" ? "本地模式" : workspace?.mode === "cloud" ? "云端模式" : "未配置";

  return (
    <Card>
      <CardHeader className={compact ? "pb-2" : undefined}>
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <Server className="size-4 text-blue-600" />
              混合模式数据流
            </CardTitle>
            <CardDescription>
              手机采集到本地知识库处理与检索的链路状态。
            </CardDescription>
          </div>
          <Badge
            className={cn(
              "h-6",
              workspace?.mode === "hybrid"
                ? "bg-amber-100 text-amber-700"
                : "bg-slate-100 text-slate-600"
            )}
          >
            {modeLabel}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {loading ? (
          <div className="flex items-center gap-2 py-2 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" />
            正在读取数据流状态...
          </div>
        ) : error ? (
          <div className="flex items-center gap-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertCircle className="size-4" />
            {error}
          </div>
        ) : (
          <>
            <div className="grid gap-2 lg:grid-cols-6">
              {steps.map((step, index) => {
                const Icon = step.icon;
                return (
                  <div key={step.key} className="relative">
                    <div className="h-full rounded-lg border bg-white p-3">
                      <div className="flex items-center justify-between gap-2">
                        <div className={cn("flex size-9 items-center justify-center rounded-lg", iconTone(step.state))}>
                          <Icon className="size-4" />
                        </div>
                        <Badge className={stateClass(step.state)}>{step.status}</Badge>
                      </div>
                      <p className="mt-3 text-sm font-medium">{step.label}</p>
                      <p className="mt-1 line-clamp-2 min-h-8 text-xs text-muted-foreground">
                        {step.detail}
                      </p>
                      {step.href && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="mt-2 h-7 px-0 text-xs"
                          render={<Link href={step.href} />}
                        >
                          查看
                        </Button>
                      )}
                    </div>
                    {index < steps.length - 1 && (
                      <div className="hidden lg:block absolute left-full top-1/2 z-10 h-px w-2 bg-border" />
                    )}
                  </div>
                );
              })}
            </div>

            <div
              className={cn(
                "flex items-start gap-2 rounded-lg px-3 py-2 text-sm",
                brokenStep
                  ? "border border-amber-200 bg-amber-50 text-amber-800"
                  : "border border-emerald-200 bg-emerald-50 text-emerald-800"
              )}
            >
              {brokenStep ? (
                <AlertCircle className="mt-0.5 size-4 shrink-0" />
              ) : (
                <CheckCircle2 className="mt-0.5 size-4 shrink-0" />
              )}
              <span>
                {brokenStep
                  ? `${brokenStep.label}：${brokenStep.status}。本地 Inbox、处理和检索能力仍可独立使用。`
                  : "云端采集到本地处理链路已连通。"}
              </span>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
