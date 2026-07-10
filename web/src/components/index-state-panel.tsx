"use client";

import { useState, type ComponentType } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Loader2,
  AlertCircle,
  RefreshCw,
  Database,
  CheckCircle,
  XCircle,
  Clock,
  Layers,
} from "lucide-react";
import { indexApi, actionApi, ApiRequestError } from "@/lib/api";
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

// ===== 索引状态徽章配置 =====

const indexStatusConfig: Record<
  string,
  { label: string; className: string }
> = {
  idle: {
    label: "空闲",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  indexing: {
    label: "索引中",
    className:
      "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  rebuilding: {
    label: "重建中",
    className:
      "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300",
  },
  error: {
    label: "错误",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
};

function IndexStatusBadge({ status }: { status: string }) {
  const config = indexStatusConfig[status] ?? {
    label: status,
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  };
  return (
    <Badge
      variant="outline"
      className={cn("border-transparent font-medium", config.className)}
    >
      {config.label}
    </Badge>
  );
}

// ===== 格式化日期 =====

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

// ===== 统计项组件 =====

function StatItem({
  icon: Icon,
  label,
  value,
  colorClass,
}: {
  icon: ComponentType<{ className?: string }>;
  label: string;
  value: number | string;
  colorClass?: string;
}) {
  return (
    <div className="flex items-center gap-3 rounded-lg border p-3">
      <Icon className={cn("size-4 shrink-0", colorClass || "text-muted-foreground")} />
      <div className="min-w-0">
        <p className="text-xs font-medium text-muted-foreground">{label}</p>
        <p className="mt-0.5 text-sm font-semibold">{value}</p>
      </div>
    </div>
  );
}

// ===== 主组件 =====

export function IndexStatePanel() {
  const queryClient = useQueryClient();
  const [rebuilding, setRebuilding] = useState(false);

  const { data: state, isLoading, error } = useQuery({
    queryKey: ["index-state"],
    queryFn: () => indexApi.getState(),
  });

  const handleRebuild = async () => {
    setRebuilding(true);
    try {
      await actionApi.rebuildIndex();
      toast.success("已触发重建索引");
      queryClient.invalidateQueries({ queryKey: ["index-state"] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "操作失败";
      toast.error(message);
    } finally {
      setRebuilding(false);
    }
  };

  const handleRetry = () => {
    queryClient.invalidateQueries({ queryKey: ["index-state"] });
  };

  // 加载中
  if (isLoading) {
    return (
      <Card>
        <CardContent className="flex items-center justify-center py-8">
          <Loader2 className="size-5 animate-spin text-muted-foreground" />
        </CardContent>
      </Card>
    );
  }

  // 错误
  if (error) {
    const message =
      error instanceof ApiRequestError ? error.message : "加载索引状态失败";
    return (
      <Card>
        <CardContent className="flex flex-col items-center justify-center gap-3 py-8 text-center">
          <AlertCircle className="size-6 text-red-500" />
          <p className="text-sm text-red-600 dark:text-red-400">{message}</p>
          <Button variant="outline" size="sm" onClick={handleRetry}>
            <RefreshCw className="mr-1.5 size-3.5" />
            重试
          </Button>
        </CardContent>
      </Card>
    );
  }

  if (!state) return null;

  const progress =
    state.totalChunks > 0
      ? Math.round((state.indexedChunks / state.totalChunks) * 100)
      : 0;

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <Database className="size-4 text-indigo-600" />
              工作区索引状态
            </CardTitle>
            <CardDescription className="mt-1">
              后端: {state.indexBackend} · 模型: {state.model}
              {state.dimension ? ` · 维度: ${state.dimension}` : ""}
            </CardDescription>
          </div>
          <div className="flex items-center gap-2">
            <IndexStatusBadge status={state.status} />
            <Button
              variant="destructive"
              size="sm"
              onClick={handleRebuild}
              disabled={rebuilding}
            >
              {rebuilding ? (
                <Loader2 className="mr-1.5 size-3.5 animate-spin" />
              ) : (
                <RefreshCw className="mr-1.5 size-3.5" />
              )}
              重建索引
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* 进度条 */}
        <div>
          <div className="mb-1.5 flex items-center justify-between text-xs">
            <span className="font-medium text-muted-foreground">索引进度</span>
            <span className="text-muted-foreground">
              {state.indexedChunks} / {state.totalChunks} ({progress}%)
            </span>
          </div>
          <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
            <div
              className={cn(
                "h-full rounded-full transition-all",
                state.status === "error"
                  ? "bg-red-500"
                  : state.status === "rebuilding"
                    ? "bg-yellow-500"
                    : "bg-blue-500"
              )}
              style={{ width: `${progress}%` }}
            />
          </div>
        </div>

        {/* 统计信息 */}
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatItem
            icon={Layers}
            label="总分块数"
            value={state.totalChunks}
          />
          <StatItem
            icon={CheckCircle}
            label="已索引"
            value={state.indexedChunks}
            colorClass="text-green-600"
          />
          <StatItem
            icon={XCircle}
            label="失败"
            value={state.failedChunks}
            colorClass="text-red-600"
          />
          <StatItem
            icon={AlertCircle}
            label="已过期"
            value={state.staleChunks}
            colorClass="text-yellow-600"
          />
        </div>

        {/* 最后重建时间 */}
        <div className="flex items-center gap-3 rounded-lg border p-3">
          <Clock className="size-4 shrink-0 text-muted-foreground" />
          <div>
            <p className="text-xs font-medium text-muted-foreground">
              最后重建时间
            </p>
            <p className="mt-0.5 text-sm">{formatDate(state.lastRebuiltAt)}</p>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
