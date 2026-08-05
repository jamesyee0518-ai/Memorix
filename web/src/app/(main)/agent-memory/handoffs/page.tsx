"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowRightLeft,
  Plus,
  Clock,
  CheckCircle2,
  Loader2,
  GitBranch,
  Inbox,
  CircleDot,
} from "lucide-react";
import { agentMemoryApi, ApiRequestError } from "@/lib/api";
import type { AgentMemoryHandoff, AgentMemorySession, CreateHandoffInput } from "@/lib/types";
import { cn } from "@/lib/utils";
import { Button, buttonVariants } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const STATUS_CONFIG = {
  open: {
    label: "待领取",
    icon: Inbox,
    color: "text-blue-500",
    badge: "bg-blue-100 text-blue-700",
  },
  in_progress: {
    label: "进行中",
    icon: CircleDot,
    color: "text-amber-500",
    badge: "bg-amber-100 text-amber-700",
  },
  done: {
    label: "已完成",
    icon: CheckCircle2,
    color: "text-green-500",
    badge: "bg-green-100 text-green-700",
  },
  cancelled: {
    label: "已取消",
    icon: Clock,
    color: "text-gray-400",
    badge: "bg-gray-100 text-gray-500",
  },
} as const;

export default function HandoffsPage() {
  const [statusFilter, setStatusFilter] = useState<string>("open");
  const [createOpen, setCreateOpen] = useState(false);
  const [completeTarget, setCompleteTarget] = useState<AgentMemoryHandoff | null>(null);
  const queryClient = useQueryClient();

  const { data: handoffs, isLoading } = useQuery({
    queryKey: ["agent-memory-handoffs", statusFilter],
    queryFn: () => agentMemoryApi.getHandoffs({ status: statusFilter, limit: 50 }),
  });

  const { data: sessions } = useQuery({
    queryKey: ["agent-memory-sessions-for-handoff"],
    queryFn: () => agentMemoryApi.listSessions(100, 0),
  });

  const acceptMutation = useMutation({
    mutationFn: ({ handoffId, toSessionId }: { handoffId: string; toSessionId: string }) =>
      agentMemoryApi.acceptHandoff(handoffId, toSessionId),
    onSuccess: () => {
      toast.success("已领取交接任务");
      queryClient.invalidateQueries({ queryKey: ["agent-memory-handoffs"] });
    },
    onError: (err: ApiRequestError) => toast.error(err.message || "领取失败"),
  });

  const completeMutation = useMutation({
    mutationFn: ({ handoffId, resultSummary }: { handoffId: string; resultSummary: string }) =>
      agentMemoryApi.completeHandoff(handoffId, resultSummary),
    onSuccess: () => {
      toast.success("交接已完成");
      setCompleteTarget(null);
      queryClient.invalidateQueries({ queryKey: ["agent-memory-handoffs"] });
    },
    onError: (err: ApiRequestError) => toast.error(err.message || "完成失败"),
  });

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <ArrowRightLeft className="h-6 w-6 text-primary" />
            Agent 交接看板
          </h1>
          <p className="text-muted-foreground text-sm mt-1">
            跨 agent 任务交接:Codex → Claude → Codex 的协同闭环
          </p>
        </div>
        <Dialog open={createOpen} onOpenChange={setCreateOpen}>
          <DialogTrigger
            render={
              <Button>
                <Plus className="h-4 w-4 mr-1" />
                发起交接
              </Button>
            }
          />
          <CreateHandoffDialog
            sessions={sessions ?? []}
            onSuccess={() => {
              setCreateOpen(false);
              queryClient.invalidateQueries({ queryKey: ["agent-memory-handoffs"] });
            }}
          />
        </Dialog>
      </div>

      {/* Status tabs */}
      <div className="flex gap-2">
        {Object.entries(STATUS_CONFIG).map(([key, cfg]) => (
          <button
            key={key}
            onClick={() => setStatusFilter(key)}
            className={cn(
              "rounded-lg px-3 py-1.5 text-sm font-medium transition-colors",
              statusFilter === key
                ? "bg-primary text-primary-foreground"
                : "bg-muted hover:bg-muted/80"
            )}
          >
            {cfg.label}
          </button>
        ))}
      </div>

      {/* Handoff list */}
      {isLoading ? (
        <div className="flex justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : !handoffs || handoffs.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-muted-foreground">
            <Inbox className="h-10 w-10 mb-3 opacity-50" />
            <p>暂无{STATUS_CONFIG[statusFilter as keyof typeof STATUS_CONFIG]?.label}交接</p>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4">
          {handoffs.map((h) => {
            const cfg = STATUS_CONFIG[h.status as keyof typeof STATUS_CONFIG] ?? STATUS_CONFIG.open;
            const StatusIcon = cfg.icon;
            return (
              <Card key={h.id}>
                <CardHeader className="pb-3">
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 mb-1">
                        <StatusIcon className={cn("h-4 w-4 shrink-0", cfg.color)} />
                        <Badge variant="secondary" className={cn("text-xs", cfg.badge)}>
                          {cfg.label}
                        </Badge>
                        <span className="text-xs text-muted-foreground">
                          {h.fromAgent}
                          <ArrowRightLeft className="inline h-3 w-3 mx-1" />
                          {h.toAgent ?? "广播"}
                        </span>
                      </div>
                      <CardTitle className="text-base leading-snug">{h.task}</CardTitle>
                    </div>
                  </div>
                </CardHeader>
                <CardContent className="pt-0 space-y-3">
                  {/* Metadata */}
                  <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                    <span className="flex items-center gap-1">
                      <Clock className="h-3 w-3" />
                      {new Date(h.createdAt).toLocaleString("zh-CN")}
                    </span>
                    {h.gitBranch && (
                      <span className="flex items-center gap-1">
                        <GitBranch className="h-3 w-3" />
                        {h.gitBranch}
                      </span>
                    )}
                    {h.commitSha && (
                      <span className="font-mono">
                        {h.commitSha.slice(0, 8)}
                      </span>
                    )}
                  </div>

                  {/* Context refs */}
                  {h.contextRefs && h.contextRefs.length > 0 && (
                    <div className="text-xs">
                      <span className="text-muted-foreground">上下文引用: </span>
                      {h.contextRefs.map((ref, i) => (
                        <code key={i} className="text-xs bg-muted px-1.5 py-0.5 rounded mr-1">
                          {ref.length > 50 ? ref.slice(0, 50) + "..." : ref}
                        </code>
                      ))}
                    </div>
                  )}

                  {/* Result summary */}
                  {h.resultSummary && (
                    <div className="rounded-md bg-muted/50 p-3 text-sm">
                      <span className="text-xs font-medium text-muted-foreground">审核结果:</span>
                      <p className="mt-1 whitespace-pre-wrap">{h.resultSummary}</p>
                    </div>
                  )}

                  {/* Actions */}
                  <div className="flex gap-2 pt-1">
                    {h.status === "open" && (
                      <SelectSessionToAccept
                        sessions={sessions ?? []}
                        onAccept={(sessionId) =>
                          acceptMutation.mutate({ handoffId: h.id, toSessionId: sessionId })
                        }
                        loading={acceptMutation.isPending}
                      />
                    )}
                    {h.status === "in_progress" && (
                      <Dialog
                        open={completeTarget?.id === h.id}
                        onOpenChange={(open) => !open && setCompleteTarget(null)}
                      >
                        <DialogTrigger
                          render={
                            <Button size="sm" variant="default" onClick={() => setCompleteTarget(h)}>
                              <CheckCircle2 className="h-3.5 w-3.5 mr-1" />
                              完成交接
                            </Button>
                          }
                        />
                        <CompleteHandoffDialog
                          handoff={h}
                          loading={completeMutation.isPending}
                          onComplete={(summary) =>
                            completeMutation.mutate({ handoffId: h.id, resultSummary: summary })
                          }
                        />
                      </Dialog>
                    )}
                    {h.fromSessionId && (
                      <Link
                        href={`/agent-memory/sessions/${h.fromSessionId}/events`}
                        className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
                      >
                        查看事件流
                      </Link>
                    )}
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}

// ─── Create Handoff Dialog ───

function CreateHandoffDialog({
  sessions,
  onSuccess,
}: {
  sessions: AgentMemorySession[];
  onSuccess: () => void;
}) {
  const [fromSessionId, setFromSessionId] = useState("");
  const [toAgent, setToAgent] = useState("");
  const [task, setTask] = useState("");
  const [gitBranch, setGitBranch] = useState("");
  const [commitSha, setCommitSha] = useState("");

  const createMutation = useMutation({
    mutationFn: (input: CreateHandoffInput) => agentMemoryApi.createHandoff(input),
    onSuccess: () => {
      toast.success("交接已创建");
      onSuccess();
    },
    onError: (err: ApiRequestError) => toast.error(err.message || "创建失败"),
  });

  const handleSubmit = () => {
    if (!fromSessionId || !task) {
      toast.error("请选择源会话并填写任务描述");
      return;
    }
    createMutation.mutate({
      fromSessionId,
      toAgent: toAgent || undefined,
      task,
      gitBranch: gitBranch || undefined,
      commitSha: commitSha || undefined,
    });
  };

  return (
    <DialogContent className="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>发起 Agent 交接</DialogTitle>
      </DialogHeader>
      <div className="space-y-4 py-2">
        <div className="space-y-2">
          <Label>源会话 *</Label>
          <Select value={fromSessionId} onValueChange={(v) => { if (v) setFromSessionId(v as string); }}>
            <SelectTrigger>
              <SelectValue placeholder="选择发起交接的会话" />
            </SelectTrigger>
            <SelectContent>
              {sessions.map((s) => (
                <SelectItem key={s.id} value={s.id}>
                  {s.taskTitle} ({s.externalSessionKey.split(":")[0]})
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-2">
          <Label>目标 Agent</Label>
          <Select value={toAgent} onValueChange={(v) => { if (v) setToAgent(v as string); }}>
            <SelectTrigger>
              <SelectValue placeholder="选择目标(留空=广播)" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="claude">Claude</SelectItem>
              <SelectItem value="codex">Codex</SelectItem>
              <SelectItem value="trae">Trae</SelectItem>
              <SelectItem value="cursor">Cursor</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-2">
          <Label>任务描述 *</Label>
          <Textarea
            value={task}
            onChange={(e) => setTask(e.target.value)}
            placeholder="例如:请 Claude 审核多仓销售数据库设计"
            rows={3}
          />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-2">
            <Label>Git 分支</Label>
            <Input value={gitBranch} onChange={(e) => setGitBranch(e.target.value)} placeholder="feature/x" />
          </div>
          <div className="space-y-2">
            <Label>Commit SHA</Label>
            <Input value={commitSha} onChange={(e) => setCommitSha(e.target.value)} placeholder="83ac..." />
          </div>
        </div>
        <Button onClick={handleSubmit} disabled={createMutation.isPending} className="w-full">
          {createMutation.isPending && <Loader2 className="h-4 w-4 mr-1 animate-spin" />}
          创建交接
        </Button>
      </div>
    </DialogContent>
  );
}

// ─── Complete Handoff Dialog ───

function CompleteHandoffDialog({
  handoff,
  loading,
  onComplete,
}: {
  handoff: AgentMemoryHandoff;
  loading: boolean;
  onComplete: (summary: string) => void;
}) {
  const [summary, setSummary] = useState("");

  return (
    <DialogContent className="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>完成交接</DialogTitle>
        <CardDescription>{handoff.task}</CardDescription>
      </DialogHeader>
      <div className="space-y-4 py-2">
        <div className="space-y-2">
          <Label>审核结果 / 总结</Label>
          <Textarea
            value={summary}
            onChange={(e) => setSummary(e.target.value)}
            placeholder="写入交接完成的结果摘要,例如:审核通过,DB 设计无问题"
            rows={5}
          />
        </div>
        <Button
          onClick={() => onComplete(summary)}
          disabled={loading || !summary}
          className="w-full"
        >
          {loading && <Loader2 className="h-4 w-4 mr-1 animate-spin" />}
          提交结果
        </Button>
      </div>
    </DialogContent>
  );
}

// ─── Accept Handoff (inline session selector) ───

function SelectSessionToAccept({
  sessions,
  onAccept,
  loading,
}: {
  sessions: AgentMemorySession[];
  onAccept: (sessionId: string) => void;
  loading: boolean;
}) {
  const [sessionId, setSessionId] = useState("");

  return (
    <div className="flex gap-2 items-center">
      <Select value={sessionId} onValueChange={(v) => { if (v) setSessionId(v as string); }}>
        <SelectTrigger className="h-8 w-auto min-w-[200px]">
          <SelectValue placeholder="选择接收会话" />
        </SelectTrigger>
        <SelectContent>
          {sessions.map((s) => (
            <SelectItem key={s.id} value={s.id}>
              {s.taskTitle} ({s.externalSessionKey.split(":")[0]})
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        size="sm"
        disabled={!sessionId || loading}
        onClick={() => sessionId && onAccept(sessionId)}
      >
        {loading && <Loader2 className="h-3.5 w-3.5 mr-1 animate-spin" />}
        领取
      </Button>
    </div>
  );
}
