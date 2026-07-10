"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import {
  AlertCircle,
  CheckCircle2,
  Cloud,
  Database,
  HardDrive,
  Loader2,
  Server,
  ShieldCheck,
  Sparkles,
} from "lucide-react";
import { ApiRequestError, runtimeApi, workspaceApi } from "@/lib/api";
import type { RuntimeHealth, Workspace } from "@/lib/types";
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

function getModeLabel(mode?: string) {
  if (mode === "local") return "本地模式";
  if (mode === "cloud") return "云端模式";
  if (mode === "hybrid") return "混合模式";
  return "未配置";
}

function getStorageLabel(workspace: Workspace | null) {
  if (!workspace) return "未配置";
  if (workspace.mode === "local") return "本地 SQLite";
  if (workspace.mode === "cloud") return "云端 PostgreSQL";
  return workspace.storageProvider || "混合存储";
}

function getFileLabel(workspace: Workspace | null) {
  if (!workspace) return "未配置";
  if (workspace.mode === "local") return "本地 Vault";
  if (workspace.mode === "cloud") return "云端对象存储";
  return workspace.fileProvider || "混合文件存储";
}

function getModelLocation(workspace: Workspace | null) {
  const provider = workspace?.modelProvider;
  if (!provider) return "未配置";
  if (provider === "ollama" || provider === "lmstudio") return "本地模型";
  return "云端或自定义模型";
}

function statusClass(value?: string) {
  if (!value) return "bg-slate-100 text-slate-600";
  const normalized = value.toLowerCase();
  if (["ok", "healthy", "connected", "available"].includes(normalized)) {
    return "bg-emerald-100 text-emerald-700";
  }
  if (["warning", "degraded"].includes(normalized)) {
    return "bg-amber-100 text-amber-700";
  }
  return "bg-red-100 text-red-700";
}

export function WorkspaceStatusPanel({
  compact = false,
}: {
  compact?: boolean;
}) {
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [health, setHealth] = useState<RuntimeHealth | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;

    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [currentWorkspace, runtimeHealth] = await Promise.all([
          workspaceApi.getCurrent().catch(() => null),
          runtimeApi.health().catch(() => null),
        ]);
        if (!mounted) return;
        setWorkspace(currentWorkspace);
        setHealth(runtimeHealth);
      } catch (err) {
        if (!mounted) return;
        const message =
          err instanceof ApiRequestError ? err.message : "加载工作区状态失败";
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

  const sendsToCloudModel =
    workspace?.modelProvider &&
    !["ollama", "lmstudio"].includes(workspace.modelProvider);
  const mode = workspace?.mode;

  const items = [
    {
      label: "数据保存",
      value: getStorageLabel(workspace),
      icon: Database,
      tone: mode === "local" ? "text-emerald-700 bg-emerald-50" : "text-blue-700 bg-blue-50",
    },
    {
      label: "文件保存",
      value: getFileLabel(workspace),
      detail: workspace?.localVaultPath,
      icon: HardDrive,
      tone: mode === "local" ? "text-emerald-700 bg-emerald-50" : "text-blue-700 bg-blue-50",
    },
    {
      label: "模型运行",
      value: getModelLocation(workspace),
      detail: workspace?.modelProvider,
      icon: Sparkles,
      tone: sendsToCloudModel
        ? "text-amber-700 bg-amber-50"
        : "text-purple-700 bg-purple-50",
    },
    {
      label: "云端 Inbox",
      value: workspace?.inboxEnabled ? "已启用" : "未启用",
      icon: Cloud,
      tone: workspace?.inboxEnabled
        ? "text-blue-700 bg-blue-50"
        : "text-slate-600 bg-slate-100",
    },
  ];

  return (
    <Card>
      <CardHeader className={compact ? "pb-2" : undefined}>
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="size-4 text-emerald-600" />
              数据与运行状态
            </CardTitle>
            <CardDescription>
              当前工作区的数据位置、文件位置、模型运行位置和云端能力状态。
            </CardDescription>
          </div>
          <Badge
            className={cn(
              "h-6",
              mode === "local"
                ? "bg-emerald-100 text-emerald-700"
                : mode === "cloud"
                  ? "bg-blue-100 text-blue-700"
                  : "bg-amber-100 text-amber-700"
            )}
          >
            {getModeLabel(mode)}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {loading ? (
          <div className="flex items-center gap-2 py-2 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" />
            正在读取工作区状态...
          </div>
        ) : error ? (
          <div className="flex items-center gap-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertCircle className="size-4" />
            {error}
          </div>
        ) : (
          <>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {items.map((item) => {
                const Icon = item.icon;
                return (
                  <div
                    key={item.label}
                    className="rounded-lg border bg-white px-3 py-3"
                  >
                    <div className="flex items-center gap-2">
                      <div
                        className={cn(
                          "flex size-8 items-center justify-center rounded-lg",
                          item.tone
                        )}
                      >
                        <Icon className="size-4" />
                      </div>
                      <div className="min-w-0">
                        <p className="text-xs text-muted-foreground">
                          {item.label}
                        </p>
                        <p className="truncate text-sm font-medium">
                          {item.value}
                        </p>
                      </div>
                    </div>
                    {item.detail && (
                      <p className="mt-2 truncate text-xs text-muted-foreground">
                        {item.detail}
                      </p>
                    )}
                  </div>
                );
              })}
            </div>

            <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-slate-50 px-3 py-3">
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <Server className="size-4 text-muted-foreground" />
                <span className="text-muted-foreground">运行时：</span>
                <Badge className={statusClass(health?.overall)}>
                  {health?.overall ?? "未知"}
                </Badge>
                <span className="text-muted-foreground">数据库</span>
                <Badge className={statusClass(health?.database)}>
                  {health?.database ?? "未知"}
                </Badge>
                <span className="text-muted-foreground">模型</span>
                <Badge className={statusClass(health?.llmService)}>
                  {health?.llmService ?? "未知"}
                </Badge>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button variant="outline" size="sm" render={<Link href="/settings/workspace" />}>
                  工作区设置
                </Button>
                <Button variant="outline" size="sm" render={<Link href="/settings/model-config" />}>
                  模型配置
                </Button>
              </div>
            </div>

            {sendsToCloudModel ? (
              <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
                <AlertCircle className="mt-0.5 size-4 shrink-0" />
                当前模型提供者可能会把处理内容发送到云端模型服务。
              </div>
            ) : (
              <div className="flex items-start gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
                <CheckCircle2 className="mt-0.5 size-4 shrink-0" />
                当前配置倾向本地处理；云端 Inbox 和同步仍以工作区设置为准。
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
