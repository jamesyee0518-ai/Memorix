"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  Plus,
  History,
  Loader2,
  RefreshCw,
  AlertCircle,
  Clock,
  GitBranch,
  Hash,
  Database,
  CheckCircle2,
  CircleDot,
} from "lucide-react";
import { agentMemoryApi, ApiRequestError } from "@/lib/api";
import type {
  AgentMemorySession,
  AgentMemoryCheckpoint,
} from "@/lib/types";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";

// ===== 工具函数 =====

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

function deliveryStateBadgeClass(state: string): string {
  switch (state.toLowerCase()) {
    case "delivered":
    case "active":
      return "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300";
    case "pending":
      return "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300";
    case "failed":
    case "error":
      return "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300";
    default:
      return "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400";
  }
}

function deliveryStateLabel(state: string): string {
  const labels: Record<string, string> = {
    delivered: "已交付",
    active: "活跃",
    pending: "待处理",
    failed: "失败",
    error: "错误",
  };
  return labels[state.toLowerCase()] ?? state;
}

/** 安全解析 JSON 字符串 */
function safeParseJson(jsonStr?: string): unknown | null {
  if (!jsonStr) return null;
  try {
    return JSON.parse(jsonStr);
  } catch {
    return null;
  }
}

/** 格式化 JSON 为可读文本 */
function formatJsonSummary(jsonStr?: string): string | null {
  const parsed = safeParseJson(jsonStr);
  if (parsed === null) return null;
  if (Array.isArray(parsed)) {
    if (parsed.length === 0) return null;
    return parsed
      .map((item, i) => {
        if (typeof item === "string") return `${i + 1}. ${item}`;
        return `${i + 1}. ${JSON.stringify(item)}`;
      })
      .join("\n");
  }
  if (typeof parsed === "object" && parsed !== null) {
    return JSON.stringify(parsed, null, 2);
  }
  return String(parsed);
}

// ===== 时间线条目组件 =====

interface TimelineItemProps {
  checkpoint: AgentMemoryCheckpoint;
  isLast: boolean;
}

function TimelineItem({ checkpoint: cp, isLast }: TimelineItemProps) {
  const openLoops = formatJsonSummary(cp.openLoopsJson);
  const decisions = formatJsonSummary(cp.decisionsJson);

  return (
    <li className="relative ml-6 pb-6">
      {/* 时间线圆点 */}
      <span
        className={cn(
          "absolute -left-[34px] top-1 flex size-6 items-center justify-center rounded-full border-2 border-primary bg-background",
          isLast && "border-green-500",
        )}
      >
        {isLast ? (
          <CheckCircle2 className="size-3.5 text-green-500" />
        ) : (
          <CircleDot className="size-3 text-primary" />
        )}
      </span>

      {/* 连接线 */}
      {!isLast && (
        <span
          className="absolute -left-[23px] top-7 h-full w-0.5 bg-border"
          aria-hidden
        />
      )}

      <Card>
        <CardHeader className="space-y-2">
          <div className="flex items-start justify-between gap-3">
            <CardTitle className="flex items-center gap-2 text-sm">
              <Clock className="size-4 text-muted-foreground" />
              {formatDate(cp.createdAt)}
            </CardTitle>
            <div className="flex shrink-0 items-center gap-1.5">
              <Badge variant="outline" className="text-[10px]">
                v{cp.version}
              </Badge>
              <Badge
                className={cn(
                  "border-transparent text-[10px]",
                  deliveryStateBadgeClass(cp.deliveryState),
                )}
              >
                {deliveryStateLabel(cp.deliveryState)}
              </Badge>
            </div>
          </div>

          {/* 序列范围 */}
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <span className="inline-flex items-center gap-1">
              <GitBranch className="size-3" />
              序列范围{" "}
              <code className="rounded bg-muted px-1 py-0.5 text-[10px] font-medium text-foreground">
                {cp.fromSequence} → {cp.toSequence}
              </code>
            </span>
            <span className="inline-flex items-center gap-1">
              <Database className="size-3" />
              Token 估算{" "}
              <span className="font-medium text-foreground">
                {cp.tokenEstimate.toLocaleString()}
              </span>
            </span>
            <span className="inline-flex items-center gap-1">
              <Hash className="size-3" />
              <code className="text-[10px]">{cp.id.slice(0, 8)}</code>
            </span>
          </div>
        </CardHeader>

        <CardContent className="space-y-3">
          {/* 摘要 */}
          {cp.summary ? (
            <div className="rounded-lg border bg-muted/30 p-3">
              <p className="text-xs font-medium text-muted-foreground">摘要</p>
              <p className="mt-1 whitespace-pre-wrap text-sm leading-relaxed">
                {cp.summary}
              </p>
            </div>
          ) : (
            <p className="text-xs italic text-muted-foreground">
              无摘要信息
            </p>
          )}

          {/* 开放循环 */}
          {openLoops && (
            <div className="rounded-lg border p-3">
              <p className="text-xs font-medium text-amber-600">
                开放循环 (Open Loops)
              </p>
              <pre className="mt-1 whitespace-pre-wrap text-xs leading-relaxed text-muted-foreground">
                {openLoops}
              </pre>
            </div>
          )}

          {/* 决策 */}
          {decisions && (
            <div className="rounded-lg border p-3">
              <p className="text-xs font-medium text-blue-600">
                决策记录 (Decisions)
              </p>
              <pre className="mt-1 whitespace-pre-wrap text-xs leading-relaxed text-muted-foreground">
                {decisions}
              </pre>
            </div>
          )}
        </CardContent>
      </Card>
    </li>
  );
}

// ===== 加载骨架屏 =====

function TimelineSkeleton() {
  return (
    <div className="ml-6 space-y-6">
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="relative pb-6">
          <span className="absolute -left-[34px] top-1 size-6 animate-pulse rounded-full border-2 border-muted bg-muted" />
          <Card>
            <CardHeader className="space-y-2">
              <div className="h-4 w-40 animate-pulse rounded bg-muted" />
              <div className="flex gap-4">
                <div className="h-3 w-24 animate-pulse rounded bg-muted" />
                <div className="h-3 w-20 animate-pulse rounded bg-muted" />
              </div>
            </CardHeader>
            <CardContent className="space-y-2">
              <div className="h-16 w-full animate-pulse rounded bg-muted" />
            </CardContent>
          </Card>
        </div>
      ))}
    </div>
  );
}

// ===== 空状态 =====

function NoSessionSelected() {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center">
      <div className="flex size-16 items-center justify-center rounded-full bg-muted">
        <History className="size-8 text-muted-foreground" />
      </div>
      <h3 className="mt-4 text-lg font-semibold">请选择会话</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        从上方下拉菜单中选择一个会话，查看其检查点历史。
      </p>
    </div>
  );
}

function EmptyCheckpoints() {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center">
      <div className="flex size-16 items-center justify-center rounded-full bg-muted">
        <History className="size-8 text-muted-foreground" />
      </div>
      <h3 className="mt-4 text-lg font-semibold">暂无检查点</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        该会话尚未创建任何检查点，点击「创建检查点」按钮开始记录快照。
      </p>
    </div>
  );
}

// ===== 主页面 =====

export default function CheckpointsPage() {
  const queryClient = useQueryClient();
  const [selectedSessionId, setSelectedSessionId] = useState<string>("");

  // ===== 数据查询 =====

  // 获取会话列表
  const sessionsQuery = useQuery({
    queryKey: ["agent-memory-sessions"],
    queryFn: () => agentMemoryApi.listSessions(),
  });

  const sessions = sessionsQuery.data ?? [];

  // 自动选择第一个会话
  useEffect(() => {
    if (!selectedSessionId && sessions.length > 0) {
      setSelectedSessionId(sessions[0].id);
    }
  }, [sessions, selectedSessionId]);

  // 获取选中会话的检查点
  const checkpointsQuery = useQuery({
    queryKey: ["checkpoints-page", "checkpoints", selectedSessionId],
    queryFn: () => agentMemoryApi.listCheckpoints(selectedSessionId),
    enabled: !!selectedSessionId,
  });

  const checkpoints = checkpointsQuery.data ?? [];

  // 选中的会话对象
  const selectedSession = useMemo(
    () => sessions.find((s) => s.id === selectedSessionId),
    [sessions, selectedSessionId],
  );

  // ===== 创建检查点 Mutation =====

  const createMutation = useMutation({
    mutationFn: (sessionId: string) =>
      agentMemoryApi.createCheckpoint(sessionId),
    onSuccess: () => {
      toast.success("检查点已创建");
      void queryClient.invalidateQueries({
        queryKey: ["checkpoints-page", "checkpoints", selectedSessionId],
      });
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "创建检查点失败";
      toast.error(message);
    },
  });

  // ===== 事件处理 =====

  const handleRefresh = () => {
    void queryClient.invalidateQueries({
      queryKey: ["checkpoints-page", "checkpoints", selectedSessionId],
    });
    void queryClient.invalidateQueries({
      queryKey: ["agent-memory-sessions"],
    });
  };

  const handleCreateCheckpoint = () => {
    if (!selectedSessionId) {
      toast.error("请先选择一个会话");
      return;
    }
    createMutation.mutate(selectedSessionId);
  };

  // ===== 渲染 =====
  return (
    <div className="space-y-5">
      {/* 页头 */}
      <div>
        <Link
          className={buttonVariants({
            variant: "ghost",
            size: "sm",
            className: "-ml-3 mb-2",
          })}
          href="/agent-memory"
        >
          <ArrowLeft className="mr-2 size-4" />
          返回 Agent Memory
        </Link>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <History className="size-6 text-primary" />
          检查点管理
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          查看会话的记忆快照检查点，创建新的检查点以保存当前状态
        </p>
      </div>

      {/* 会话选择 + 操作栏 */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="space-y-2">
          <label className="text-sm font-medium">选择会话</label>
          <Select
            value={selectedSessionId}
            onValueChange={(v) => v && setSelectedSessionId(v)}
          >
            <SelectTrigger className="w-full sm:w-80">
              <SelectValue
                placeholder={
                  sessionsQuery.isLoading
                    ? "加载中..."
                    : "请选择会话"
                }
              />
            </SelectTrigger>
            <SelectContent>
              {sessions.map((session) => (
                <SelectItem key={session.id} value={session.id}>
                  <span className="flex items-center gap-2">
                    {session.status === "active" ? (
                      <span className="size-2 shrink-0 rounded-full bg-green-500" />
                    ) : (
                      <span className="size-2 shrink-0 rounded-full bg-slate-400" />
                    )}
                    <span className="truncate">{session.taskTitle}</span>
                  </span>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="icon"
            onClick={handleRefresh}
            title="刷新"
            disabled={!selectedSessionId}
          >
            <RefreshCw className="size-4" />
          </Button>
          <Button
            onClick={handleCreateCheckpoint}
            disabled={!selectedSessionId || createMutation.isPending}
          >
            {createMutation.isPending ? (
              <Loader2 className="mr-2 size-4 animate-spin" />
            ) : (
              <Plus className="mr-2 size-4" />
            )}
            创建检查点
          </Button>
        </div>
      </div>

      {/* 选中会话信息 */}
      {selectedSession && (
        <Card>
          <CardContent className="flex flex-wrap items-center gap-x-6 gap-y-2 p-4 text-sm">
            <div className="flex items-center gap-2">
              <span className="text-muted-foreground">会话:</span>
              <span className="font-medium">{selectedSession.taskTitle}</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-muted-foreground">状态:</span>
              {selectedSession.status === "active" ? (
                <Badge className="bg-green-100 text-green-700">活跃</Badge>
              ) : (
                <Badge variant="secondary">已关闭</Badge>
              )}
            </div>
            <div className="flex items-center gap-2">
              <span className="text-muted-foreground">检查点数:</span>
              <span className="font-medium tabular-nums">
                {checkpoints.length}
              </span>
            </div>
          </CardContent>
        </Card>
      )}

      {/* 检查点时间线 */}
      {sessionsQuery.isError ? (
        <div className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
          <AlertCircle className="size-4 shrink-0" />
          加载会话列表时出错，请点击刷新按钮重试。
        </div>
      ) : !selectedSessionId ? (
        <NoSessionSelected />
      ) : checkpointsQuery.isError ? (
        <div className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
          <AlertCircle className="size-4 shrink-0" />
          加载检查点时出错，请点击刷新按钮重试。
        </div>
      ) : checkpointsQuery.isLoading ? (
        <TimelineSkeleton />
      ) : checkpoints.length === 0 ? (
        <EmptyCheckpoints />
      ) : (
        <ol className="relative">
          {checkpoints.map((cp, index) => (
            <TimelineItem
              key={cp.id}
              checkpoint={cp}
              isLast={index === 0}
            />
          ))}
        </ol>
      )}
    </div>
  );
}
