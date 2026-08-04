"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Plus,
  Brain,
  Eye,
  Lock,
  ClipboardCheck,
  Inbox,
  Loader2,
  MessageSquareText,
  Clock,
  KeyRound,
  CalendarClock,
  Archive,
  CheckCircle2,
  FileStack,
} from "lucide-react";
import { agentMemoryApi, ApiRequestError } from "@/lib/api";
import type { AgentMemorySession } from "@/lib/types";
import { cn } from "@/lib/utils";
import { Button, buttonVariants } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";

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

// ===== 子页面导航 Tab =====

const TAB_ITEMS = [
  { href: "/agent-memory", label: "会话列表", icon: MessageSquareText, exact: true },
  { href: "/agent-memory/candidates", label: "候选审核", icon: ClipboardCheck },
  { href: "/agent-memory/archive", label: "归档与日志", icon: Archive },
];

function AgentMemoryTabs() {
  return (
    <nav className="flex flex-wrap gap-1 border-b pb-3">
      {TAB_ITEMS.map((item) => {
        const Icon = item.icon;
        return (
          <Link
            key={item.href}
            href={item.href}
            aria-disabled={item.href !== "/agent-memory"}
            className={cn(
              "flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
              item.href === "/agent-memory"
                ? "bg-primary/10 text-primary"
                : "text-muted-foreground hover:bg-muted hover:text-foreground"
            )}
          >
            <Icon className="size-4" />
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}

// ===== 指标摘要卡片 =====

interface MetricCardProps {
  label: string;
  value: number | string;
  icon: React.ComponentType<{ className?: string }>;
  color: string;
  bg: string;
}

function MetricCard({ label, value, icon: Icon, color, bg }: MetricCardProps) {
  return (
    <Card>
      <CardContent className="flex items-center gap-4 pt-1">
        <div className={cn("flex size-12 items-center justify-center rounded-lg", bg)}>
          <Icon className={cn("size-6", color)} />
        </div>
        <div>
          <p className="text-2xl font-bold">{value}</p>
          <p className="text-sm text-muted-foreground">{label}</p>
        </div>
      </CardContent>
    </Card>
  );
}

function MetricsSkeleton() {
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {Array.from({ length: 4 }).map((_, i) => (
        <Card key={i}>
          <CardContent className="flex items-center gap-4 pt-1">
            <div className="size-12 animate-pulse rounded-lg bg-muted" />
            <div className="space-y-2">
              <div className="h-6 w-16 animate-pulse rounded bg-muted" />
              <div className="h-4 w-20 animate-pulse rounded bg-muted" />
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

// ===== 会话卡片骨架屏 =====

function SessionCardSkeleton() {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between">
          <div className="flex-1 space-y-2">
            <div className="h-5 w-2/3 animate-pulse rounded bg-muted" />
            <div className="h-4 w-1/3 animate-pulse rounded bg-muted" />
          </div>
          <div className="h-5 w-14 animate-pulse rounded-full bg-muted" />
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="h-4 w-1/2 animate-pulse rounded bg-muted" />
        <div className="flex gap-4">
          <div className="h-4 w-24 animate-pulse rounded bg-muted" />
          <div className="h-4 w-24 animate-pulse rounded bg-muted" />
        </div>
        <div className="flex gap-2 pt-2">
          <div className="h-8 w-24 animate-pulse rounded-lg bg-muted" />
          <div className="h-8 w-24 animate-pulse rounded-lg bg-muted" />
        </div>
      </CardContent>
    </Card>
  );
}

// ===== 新建会话对话框 =====

interface CreateSessionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
}

function CreateSessionDialog({
  open,
  onOpenChange,
  onSuccess,
}: CreateSessionDialogProps) {
  const queryClient = useQueryClient();
  const [taskTitle, setTaskTitle] = useState("");
  const [externalSessionKey, setExternalSessionKey] = useState("");

  const createMutation = useMutation({
    mutationFn: (data: { taskTitle: string; externalSessionKey: string }) =>
      agentMemoryApi.createSession(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["agent-memory-sessions"] });
      queryClient.invalidateQueries({ queryKey: ["agent-memory-metrics"] });
      toast.success("会话已创建");
      onOpenChange(false);
      setTaskTitle("");
      setExternalSessionKey("");
      onSuccess();
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "创建会话失败";
      toast.error(message);
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!taskTitle.trim() || !externalSessionKey.trim()) {
      toast.error("请填写任务标题和会话标识");
      return;
    }
    createMutation.mutate({
      taskTitle: taskTitle.trim(),
      externalSessionKey: externalSessionKey.trim(),
    });
  };

  const handleOpenChange = (v: boolean) => {
    if (!v) {
      setTaskTitle("");
      setExternalSessionKey("");
    }
    onOpenChange(v);
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>新建会话</DialogTitle>
          <DialogDescription>
            创建一个新的 Agent 记忆会话，用于管理和跟踪 Agent 的记忆条目。
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="task-title">任务标题</Label>
            <Input
              id="task-title"
              placeholder="请输入任务标题"
              value={taskTitle}
              onChange={(e) => setTaskTitle(e.target.value)}
              disabled={createMutation.isPending}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="session-key">会话标识 (externalSessionKey)</Label>
            <Input
              id="session-key"
              placeholder="请输入唯一会话标识"
              value={externalSessionKey}
              onChange={(e) => setExternalSessionKey(e.target.value)}
              disabled={createMutation.isPending}
            />
          </div>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              创建
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ===== 关闭会话确认对话框 =====

interface CloseSessionDialogProps {
  session: AgentMemorySession | null;
  onClose: () => void;
}

function CloseSessionDialog({ session, onClose }: CloseSessionDialogProps) {
  const queryClient = useQueryClient();

  const closeMutation = useMutation({
    mutationFn: (id: string) => agentMemoryApi.closeSession(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["agent-memory-sessions"] });
      queryClient.invalidateQueries({ queryKey: ["agent-memory-metrics"] });
      toast.success("会话已关闭");
      onClose();
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "关闭会话失败";
      toast.error(message);
    },
  });

  return (
    <Dialog
      open={!!session}
      onOpenChange={(v) => !v && onClose()}
    >
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>确认关闭会话</DialogTitle>
          <DialogDescription>
            确定要关闭会话「{session?.taskTitle}」吗？关闭后将不再接受新的记忆条目，但已有记忆仍可查询。
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            取消
          </DialogClose>
          <Button
            variant="destructive"
            onClick={() => {
              if (session) closeMutation.mutate(session.id);
            }}
            disabled={closeMutation.isPending}
          >
            {closeMutation.isPending && (
              <Loader2 className="mr-2 size-4 animate-spin" />
            )}
            关闭会话
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ===== 主页面 =====

export default function AgentMemoryPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const [closeTarget, setCloseTarget] = useState<AgentMemorySession | null>(
    null
  );

  // 获取会话列表
  const { data: sessions, isLoading: sessionsLoading } = useQuery({
    queryKey: ["agent-memory-sessions"],
    queryFn: () => agentMemoryApi.listSessions(),
  });

  // 获取指标
  const { data: metrics, isLoading: metricsLoading } = useQuery({
    queryKey: ["agent-memory-metrics"],
    queryFn: () => agentMemoryApi.getMetrics(),
  });

  const totalSessions = sessions?.length ?? 0;

  const metricCards: MetricCardProps[] = [
    {
      label: "总会话数",
      value: totalSessions,
      icon: MessageSquareText,
      color: "text-blue-600",
      bg: "bg-blue-50",
    },
    {
      label: "记忆条目总数",
      value: metrics?.totalMemoryItems ?? 0,
      icon: FileStack,
      color: "text-indigo-600",
      bg: "bg-indigo-50",
    },
    {
      label: "已确认条目",
      value: metrics?.confirmedItems ?? 0,
      icon: CheckCircle2,
      color: "text-green-600",
      bg: "bg-green-50",
    },
    {
      label: "候选条目",
      value: metrics?.candidateItems ?? 0,
      icon: Inbox,
      color: "text-amber-600",
      bg: "bg-amber-50",
    },
  ];

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Agent记忆</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            管理 Agent 会话与记忆条目
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="mr-2 size-4" />
          新建会话
        </Button>
      </div>

      {/* 指标摘要卡片 */}
      {metricsLoading ? (
        <MetricsSkeleton />
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {metricCards.map((card) => (
            <MetricCard key={card.label} {...card} />
          ))}
        </div>
      )}

      {/* Tab 导航 */}
      <AgentMemoryTabs />

      {/* 会话列表 */}
      {sessionsLoading ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <SessionCardSkeleton key={i} />
          ))}
        </div>
      ) : !sessions || sessions.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Brain className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无会话</p>
            <p className="mt-1 text-sm text-muted-foreground">
              创建您的第一个 Agent 记忆会话，开始管理记忆条目
            </p>
            <Button className="mt-4" onClick={() => setCreateOpen(true)}>
              <Plus className="mr-2 size-4" />
              新建会话
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {sessions.map((session) => (
            <Card
              key={session.id}
              className="flex flex-col transition-shadow hover:shadow-md"
            >
              <CardHeader>
                <div className="flex items-start justify-between gap-2">
                  <CardTitle className="line-clamp-2 flex-1">
                    {session.taskTitle}
                  </CardTitle>
                  {session.status === "active" ? (
                    <Badge className="shrink-0 bg-green-100 text-green-700">
                      活跃
                    </Badge>
                  ) : (
                    <Badge variant="secondary" className="shrink-0">
                      已关闭
                    </Badge>
                  )}
                </div>
              </CardHeader>
              <CardContent className="flex flex-1 flex-col">
                <div className="space-y-2 text-sm">
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <KeyRound className="size-3.5 shrink-0" />
                    <span className="truncate" title={session.externalSessionKey}>
                      {session.externalSessionKey}
                    </span>
                  </div>
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <Clock className="size-3.5 shrink-0" />
                    <span>开始：{formatDate(session.startedAt)}</span>
                  </div>
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <CalendarClock className="size-3.5 shrink-0" />
                    <span>最近：{formatDate(session.lastActiveAt)}</span>
                  </div>
                  {session.closedAt && (
                    <div className="flex items-center gap-2 text-muted-foreground">
                      <Lock className="size-3.5 shrink-0" />
                      <span>关闭：{formatDate(session.closedAt)}</span>
                    </div>
                  )}
                </div>

                {/* 操作按钮 */}
                <div className="mt-4 flex gap-2 pt-2">
                  <Link
                    href={`/agent-memory/${session.id}`}
                    className={buttonVariants({ variant: "outline", size: "sm" })}
                  >
                    <Eye className="mr-1.5 size-3.5" />
                    查看详情
                  </Link>
                  {session.status === "active" && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setCloseTarget(session)}
                    >
                      <Lock className="mr-1.5 size-3.5" />
                      关闭会话
                    </Button>
                  )}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {/* 新建会话对话框 */}
      <CreateSessionDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onSuccess={() => {}}
      />

      {/* 关闭会话确认对话框 */}
      <CloseSessionDialog
        session={closeTarget}
        onClose={() => setCloseTarget(null)}
      />
    </div>
  );
}
