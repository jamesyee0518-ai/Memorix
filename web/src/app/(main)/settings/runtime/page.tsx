"use client";

import { useEffect, useState, useCallback } from "react";
import { toast } from "sonner";
import {
  Activity,
  Loader2,
  RefreshCw,
  Database,
  HardDrive,
  ListChecks,
  Brain,
  Sparkles,
  Server,
  Cloud,
} from "lucide-react";
import { runtimeApi, ApiRequestError } from "@/lib/api";
import type { RuntimeHealth } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import { cn } from "@/lib/utils";

interface HealthItem {
  key: keyof RuntimeHealth;
  label: string;
  icon: typeof Database;
}

const healthItems: HealthItem[] = [
  { key: "database", label: "数据库", icon: Database },
  { key: "fileStorage", label: "文件存储", icon: HardDrive },
  { key: "jobQueue", label: "任务队列", icon: ListChecks },
  { key: "llmService", label: "LLM 服务", icon: Brain },
  { key: "embeddingService", label: "Embedding 服务", icon: Sparkles },
  { key: "ollama", label: "Ollama", icon: Server },
  { key: "lmStudio", label: "LM Studio", icon: Server },
];

function isHealthy(status: string): boolean {
  const s = status.toLowerCase();
  return (
    s === "ok" ||
    s === "healthy" ||
    s === "up" ||
    s === "connected" ||
    s === "running"
  );
}

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

export default function RuntimePage() {
  const [health, setHealth] = useState<RuntimeHealth | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const fetchHealth = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await runtimeApi.health();
      setHealth(data);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载运行时状态失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchHealth();
  }, [fetchHealth]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!health) {
    return (
      <div className="space-y-4">
        <div>
          <h2 className="text-lg font-semibold">运行时状态</h2>
          <p className="text-sm text-muted-foreground">
            查看系统各组件的运行状态
          </p>
        </div>
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Activity className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">无法获取运行时状态</p>
            <Button className="mt-4" onClick={fetchHealth}>
              <RefreshCw className="mr-2 size-4" />
              重新加载
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  const overallHealthy = isHealthy(health.overall);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">运行时状态</h2>
          <p className="text-sm text-muted-foreground">
            查看系统各组件的运行状态
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={fetchHealth}>
          <RefreshCw className="mr-2 size-4" />
          刷新
        </Button>
      </div>

      {/* 总览 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Activity className="size-4 text-primary" />
            总体状态
          </CardTitle>
          <CardDescription>系统整体运行状态概览</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">总体状态</span>
            <div className="flex items-center gap-2">
              <span
                className={cn(
                  "size-2.5 rounded-full",
                  overallHealthy ? "bg-green-500" : "bg-red-500"
                )}
              />
              <Badge
                className={cn(
                  overallHealthy
                    ? "bg-green-100 text-green-700"
                    : "bg-red-100 text-red-700"
                )}
              >
                {overallHealthy ? "正常运行" : "异常"}
              </Badge>
            </div>
          </div>
          {health.workspaceMode && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">
                当前工作区模式
              </span>
              <Badge variant="secondary">{health.workspaceMode}</Badge>
            </div>
          )}
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">检查时间</span>
            <span className="text-sm font-medium">
              {formatDate(health.checkedAt)}
            </span>
          </div>
        </CardContent>
      </Card>

      {/* 组件状态 */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">组件状态</CardTitle>
          <CardDescription>各核心组件的运行状态</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 sm:grid-cols-2">
            {healthItems.map((item) => {
              const Icon = item.icon;
              const status = (health[item.key] as string) ?? "unknown";
              const healthy = isHealthy(status);
              return (
                <div
                  key={item.key}
                  className="flex items-center justify-between rounded-lg border p-3"
                >
                  <div className="flex items-center gap-3">
                    <div
                      className={cn(
                        "flex size-9 items-center justify-center rounded-lg",
                        healthy
                          ? "bg-green-50 text-green-600"
                          : "bg-red-50 text-red-600"
                      )}
                    >
                      <Icon className="size-4" />
                    </div>
                    <div>
                      <p className="text-sm font-medium">{item.label}</p>
                      <p className="text-xs text-muted-foreground">{status}</p>
                    </div>
                  </div>
                  <span
                    className={cn(
                      "size-2.5 rounded-full",
                      healthy ? "bg-green-500" : "bg-red-500"
                    )}
                    title={healthy ? "正常" : "异常"}
                  />
                </div>
              );
            })}
          </div>
        </CardContent>
      </Card>

      {/* 云端 API 状态 */}
      {health.cloudApi && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Cloud className="size-4 text-primary" />
              云端 API
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">连接状态</span>
              <div className="flex items-center gap-2">
                <span
                  className={cn(
                    "size-2.5 rounded-full",
                    isHealthy(health.cloudApi)
                      ? "bg-green-500"
                      : "bg-red-500"
                  )}
                />
                <span className="text-sm font-medium">{health.cloudApi}</span>
              </div>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
