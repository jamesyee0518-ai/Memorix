"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  Brain,
  Check,
  CheckCheck,
  X,
  XCircle,
  FileText,
  AlertCircle,
  Search,
  RefreshCw,
  Loader2,
  ChevronDown,
  ChevronRight,
  ShieldCheck,
} from "lucide-react";
import { agentMemoryApi } from "@/lib/api";
import type {
  AgentMemoryItem,
  AgentMemoryEvidence,
  MemoryQualityMetrics,
} from "@/lib/types";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

// ===== 常量 =====

type AdmissionFilter = "candidate" | "qualified";

const KIND_OPTIONS = [
  { value: "all", label: "全部类型" },
  { value: "fact", label: "事实" },
  { value: "preference", label: "偏好" },
  { value: "instruction", label: "指令" },
  { value: "event", label: "事件" },
  { value: "relationship", label: "关系" },
  { value: "entity", label: "实体" },
  { value: "summary", label: "摘要" },
  { value: "decision", label: "决策" },
];

const KIND_LABELS: Record<string, string> = {
  fact: "事实",
  preference: "偏好",
  instruction: "指令",
  event: "事件",
  relationship: "关系",
  entity: "实体",
  summary: "摘要",
  decision: "决策",
};

// ===== 工具函数 =====

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

function formatConfidence(confidence: number): string {
  return `${Math.round(confidence * 100)}%`;
}

function admissionBadgeClass(state: string): string {
  switch (state) {
    case "candidate":
      return "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300";
    case "qualified":
      return "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300";
    case "confirmed":
      return "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300";
    case "rejected":
      return "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300";
    default:
      return "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400";
  }
}

const ADMISSION_LABELS: Record<string, string> = {
  candidate: "候选",
  qualified: "已合格",
  confirmed: "已确认",
  rejected: "已拒绝",
};

// ===== 候选记忆卡片组件 =====

interface CandidateCardProps {
  item: AgentMemoryItem;
  evidence: AgentMemoryEvidence[] | undefined;
  evidenceLoading: boolean;
  expanded: boolean;
  actionMode: "confirm" | "reject" | null;
  note: string;
  isProcessing: boolean;
  onToggleExpand: () => void;
  onStartAction: (action: "confirm" | "reject") => void;
  onCancelAction: () => void;
  onNoteChange: (note: string) => void;
  onSubmitAction: () => void;
}

function CandidateCard({
  item,
  evidence,
  evidenceLoading,
  expanded,
  actionMode,
  note,
  isProcessing,
  onToggleExpand,
  onStartAction,
  onCancelAction,
  onNoteChange,
  onSubmitAction,
}: CandidateCardProps) {
  const evidenceCount = evidence?.length ?? item.evidence?.length ?? 0;

  return (
    <Card>
      <CardHeader className="space-y-2">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1 space-y-1">
            <CardTitle className="flex items-start gap-2 text-base leading-snug">
              <button
                type="button"
                onClick={onToggleExpand}
                className="mt-0.5 shrink-0 text-muted-foreground hover:text-foreground"
                aria-label={expanded ? "收起内容" : "展开内容"}
              >
                {expanded ? (
                  <ChevronDown className="size-4" />
                ) : (
                  <ChevronRight className="size-4" />
                )}
              </button>
              <span className="break-words">{item.title}</span>
            </CardTitle>
            {item.summary && (
              <p className="line-clamp-2 text-sm text-muted-foreground">
                {item.summary}
              </p>
            )}
          </div>
          <div className="flex shrink-0 flex-wrap items-center justify-end gap-1.5">
            <Badge variant="outline">
              {KIND_LABELS[item.kind] ?? item.kind}
            </Badge>
            <Badge
              className={cn(
                "border-transparent",
                admissionBadgeClass(item.admissionState),
              )}
            >
              {ADMISSION_LABELS[item.admissionState] ?? item.admissionState}
            </Badge>
          </div>
        </div>

        {/* 指标行 */}
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
          <span className="inline-flex items-center gap-1">
            <Brain className="size-3" />
            置信度 <span className="font-medium text-foreground">{formatConfidence(item.confidence)}</span>
          </span>
          <span>
            重要性 <span className="font-medium text-foreground">{item.importance}/10</span>
          </span>
          {evidenceCount > 0 && (
            <span className="inline-flex items-center gap-1">
              <FileText className="size-3" />
              证据 <span className="font-medium text-foreground">{evidenceCount}</span>
            </span>
          )}
          <span>创建于 {formatDate(item.createdAt)}</span>
        </div>
      </CardHeader>

      <CardContent className="space-y-3">
        {/* 展开内容 */}
        {expanded && item.content && (
          <div className="rounded-lg border bg-muted/30 p-3">
            <p className="text-xs font-medium text-muted-foreground">记忆内容</p>
            <p className="mt-1 whitespace-pre-wrap text-sm leading-relaxed">
              {item.content}
            </p>
          </div>
        )}

        {/* 展开证据列表 */}
        {expanded && evidence && evidence.length > 0 && (
          <div className="space-y-1.5">
            <p className="text-xs font-medium text-muted-foreground">证据来源</p>
            {evidence.map((ev) => (
              <div
                key={ev.id}
                className="flex items-center gap-2 rounded-md border bg-background px-2.5 py-1.5 text-xs"
              >
                <FileText className="size-3 shrink-0 text-muted-foreground" />
                <span className="truncate text-muted-foreground">
                  {ev.evidenceKind}
                  {ev.relation ? ` · ${ev.relation}` : ""}
                  {ev.locator ? ` · ${ev.locator}` : ""}
                </span>
                <span className="ml-auto shrink-0 text-muted-foreground">
                  {formatDate(ev.capturedAt)}
                </span>
              </div>
            ))}
          </div>
        )}

        {expanded && evidenceLoading && (
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <Loader2 className="size-3 animate-spin" />
            正在加载证据...
          </div>
        )}

        <Separator />

        {/* 操作区 */}
        {actionMode ? (
          <div className="space-y-2">
            <div className="space-y-1.5">
              <Label htmlFor={`note-${item.id}`} className="text-xs">
                备注（可选）
              </Label>
              <Textarea
                id={`note-${item.id}`}
                placeholder={
                  actionMode === "confirm"
                    ? "确认理由（可选）..."
                    : "拒绝理由（可选）..."
                }
                value={note}
                onChange={(e) => onNoteChange(e.target.value)}
                className="min-h-[60px] text-sm"
                disabled={isProcessing}
              />
            </div>
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant={actionMode === "confirm" ? "default" : "destructive"}
                onClick={onSubmitAction}
                disabled={isProcessing}
              >
                {isProcessing ? (
                  <Loader2 className="mr-1 size-3.5 animate-spin" />
                ) : actionMode === "confirm" ? (
                  <Check className="mr-1 size-3.5" />
                ) : (
                  <X className="mr-1 size-3.5" />
                )}
                {actionMode === "confirm" ? "确认记忆" : "拒绝记忆"}
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={onCancelAction}
                disabled={isProcessing}
              >
                取消
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="outline"
              className="border-green-300 text-green-700 hover:bg-green-50 hover:text-green-800 dark:border-green-700 dark:text-green-400 dark:hover:bg-green-900/20"
              onClick={() => onStartAction("confirm")}
            >
              <Check className="size-3.5" />
              确认
            </Button>
            <Button
              size="sm"
              variant="outline"
              className="border-red-300 text-red-700 hover:bg-red-50 hover:text-red-800 dark:border-red-700 dark:text-red-400 dark:hover:bg-red-900/20"
              onClick={() => onStartAction("reject")}
            >
              <X className="size-3.5" />
              拒绝
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

// ===== 加载骨架屏 =====

function CandidateCardSkeleton() {
  return (
    <Card>
      <CardHeader className="space-y-2">
        <div className="flex items-start justify-between gap-3">
          <div className="flex-1 space-y-2">
            <Skeleton className="h-5 w-3/4" />
            <Skeleton className="h-4 w-full" />
          </div>
          <div className="flex gap-1.5">
            <Skeleton className="h-5 w-12 rounded-full" />
            <Skeleton className="h-5 w-14 rounded-full" />
          </div>
        </div>
        <div className="flex gap-4">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-3 w-20" />
          <Skeleton className="h-3 w-28" />
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <Skeleton className="h-px w-full" />
        <div className="flex gap-2">
          <Skeleton className="h-7 w-16" />
          <Skeleton className="h-7 w-16" />
        </div>
      </CardContent>
    </Card>
  );
}

// ===== 空状态 =====

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center">
      <div className="flex size-16 items-center justify-center rounded-full bg-muted">
        <Brain className="size-8 text-muted-foreground" />
      </div>
      <h3 className="mt-4 text-lg font-semibold">暂无候选记忆</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        Agent 尚未捕获需要审核的候选记忆，请稍后再来查看。
      </p>
    </div>
  );
}

// ===== 主页面 =====

export default function CandidateReviewPage() {
  const queryClient = useQueryClient();

  // 筛选状态
  const [admissionFilter, setAdmissionFilter] =
    useState<AdmissionFilter>("candidate");
  const [kindFilter, setKindFilter] = useState<string>("all");
  const [searchQuery, setSearchQuery] = useState("");

  // 展开状态
  const [expandedItems, setExpandedItems] = useState<Set<string>>(new Set());

  // 操作状态：per-item
  const [actionModes, setActionModes] = useState<
    Record<string, "confirm" | "reject" | null>
  >({});
  const [actionNotes, setActionNotes] = useState<Record<string, string>>({});

  // 正在处理的 item id（单个操作）
  const [processingId, setProcessingId] = useState<string | null>(null);

  // 批量处理状态
  const [batchAction, setBatchAction] = useState<"confirm" | "reject" | null>(
    null,
  );

  // ===== 数据查询 =====

  // 获取候选 + 合格的记忆（分两次查询合并）
  const candidatesQuery = useQuery({
    queryKey: ["agent-memory-search", "candidate"],
    queryFn: () =>
      agentMemoryApi.searchMemory({
        query: "",
        admissionState: "candidate",
        limit: 200,
        offset: 0,
      }),
  });

  const qualifiedQuery = useQuery({
    queryKey: ["agent-memory-search", "qualified"],
    queryFn: () =>
      agentMemoryApi.searchMemory({
        query: "",
        admissionState: "qualified",
        limit: 200,
        offset: 0,
      }),
  });

  const metricsQuery = useQuery({
    queryKey: ["agent-memory-metrics"],
    queryFn: () => agentMemoryApi.getMetrics(),
  });

  // 当前筛选下的所有 items
  const allItems = useMemo(() => {
    const items: AgentMemoryItem[] = [];
    if (admissionFilter === "candidate" || admissionFilter === "qualified") {
      const data =
        admissionFilter === "candidate"
          ? candidatesQuery.data
          : qualifiedQuery.data;
      if (data) items.push(...data);
    }
    return items;
  }, [admissionFilter, candidatesQuery.data, qualifiedQuery.data]);

  // 应用 kind 和搜索过滤
  const filteredItems = useMemo(() => {
    let result = allItems;
    if (kindFilter !== "all") {
      result = result.filter((item) => item.kind === kindFilter);
    }
    const q = searchQuery.trim().toLowerCase();
    if (q) {
      result = result.filter(
        (item) =>
          item.title.toLowerCase().includes(q) ||
          item.summary?.toLowerCase().includes(q) ||
          item.content?.toLowerCase().includes(q),
      );
    }
    return result;
  }, [allItems, kindFilter, searchQuery]);

  const isLoading =
    (admissionFilter === "candidate" && candidatesQuery.isLoading) ||
    (admissionFilter === "qualified" && qualifiedQuery.isLoading);

  // ===== 证据查询（按需） =====

  // 为展开的 item 获取证据
  const evidenceQueries = useQuery({
    queryKey: [
      "agent-memory-evidence",
      Array.from(expandedItems).sort().join(","),
    ],
    queryFn: async () => {
      const ids = Array.from(expandedItems);
      if (ids.length === 0) return {};
      const results = await Promise.all(
        ids.map(async (id) => {
          const ev = await agentMemoryApi.getEvidence(id);
          return [id, ev] as const;
        }),
      );
      return Object.fromEntries(results) as Record<
        string,
        AgentMemoryEvidence[]
      >;
    },
    enabled: expandedItems.size > 0,
  });

  // ===== Mutation =====

  const confirmMutation = useMutation({
    mutationFn: async ({
      id,
      action,
      note,
    }: {
      id: string;
      action: "confirm" | "reject";
      note?: string;
    }) => agentMemoryApi.confirmMemory(id, action, note),
    onSuccess: (data, variables) => {
      toast.success(
        variables.action === "confirm"
          ? `已确认：${data.title}`
          : `已拒绝：${data.title}`,
      );
      // 清除该 item 的操作状态
      setActionModes((prev) => {
        const next = { ...prev };
        delete next[variables.id];
        return next;
      });
      setActionNotes((prev) => {
        const next = { ...prev };
        delete next[variables.id];
        return next;
      });
      // 从展开集合中移除
      setExpandedItems((prev) => {
        const next = new Set(prev);
        next.delete(variables.id);
        return next;
      });
      // 刷新数据
      void queryClient.invalidateQueries({
        queryKey: ["agent-memory-search"],
      });
      void queryClient.invalidateQueries({
        queryKey: ["agent-memory-metrics"],
      });
      void queryClient.invalidateQueries({
        queryKey: ["agent-memory-evidence"],
      });
    },
    onError: (err) => {
      toast.error(
        err instanceof Error ? err.message : "操作失败，请重试",
      );
    },
    onSettled: () => {
      setProcessingId(null);
    },
  });

  // ===== 事件处理 =====

  const handleToggleExpand = (id: string) => {
    setExpandedItems((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const handleStartAction = (id: string, action: "confirm" | "reject") => {
    setActionModes((prev) => ({ ...prev, [id]: action }));
  };

  const handleCancelAction = (id: string) => {
    setActionModes((prev) => {
      const next = { ...prev };
      delete next[id];
      return next;
    });
    setActionNotes((prev) => {
      const next = { ...prev };
      delete next[id];
      return next;
    });
  };

  const handleNoteChange = (id: string, note: string) => {
    setActionNotes((prev) => ({ ...prev, [id]: note }));
  };

  const handleSubmitAction = (id: string) => {
    const mode = actionModes[id];
    if (!mode) return;
    const note = actionNotes[id]?.trim() || undefined;
    setProcessingId(id);
    confirmMutation.mutate({ id, action: mode, note });
  };

  // 批量操作
  const handleBatchAction = async (action: "confirm" | "reject") => {
    const items = filteredItems;
    if (items.length === 0) return;
    setBatchAction(action);
    let successCount = 0;
    let failCount = 0;
    for (const item of items) {
      try {
        await agentMemoryApi.confirmMemory(item.id, action);
        successCount++;
      } catch {
        failCount++;
      }
    }
    setBatchAction(null);
    if (successCount > 0) {
      toast.success(
        action === "confirm"
          ? `已批量确认 ${successCount} 条记忆`
          : `已批量拒绝 ${successCount} 条记忆`,
      );
    }
    if (failCount > 0) {
      toast.error(`${failCount} 条记忆操作失败`);
    }
    void queryClient.invalidateQueries({
      queryKey: ["agent-memory-search"],
    });
    void queryClient.invalidateQueries({
      queryKey: ["agent-memory-metrics"],
    });
  };

  const handleRefresh = () => {
    void queryClient.invalidateQueries({
      queryKey: ["agent-memory-search"],
    });
    void queryClient.invalidateQueries({
      queryKey: ["agent-memory-metrics"],
    });
  };

  // ===== 渲染 =====

  const metrics: MemoryQualityMetrics | undefined = metricsQuery.data;
  const evidenceMap = evidenceQueries.data ?? {};

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
          <ShieldCheck className="size-6 text-primary" />
          候选审核
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          审核 Agent 捕获的候选记忆，确认或拒绝每条记忆条目
        </p>
      </div>

      {/* 统计卡片 */}
      <div className="grid gap-3 sm:grid-cols-3">
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <Brain className="size-3.5" />
              候选记忆总数
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums">
              {metrics?.candidateItems ?? "-"}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <Check className="size-3.5 text-green-600" />
              已确认
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums text-green-600">
              {metrics?.confirmedItems ?? "-"}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <XCircle className="size-3.5 text-red-600" />
              已拒绝
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums text-red-600">
              {metrics?.rejectedItems ?? "-"}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* 筛选栏 */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap items-center gap-2">
          <Tabs
            value={admissionFilter}
            onValueChange={(v) => setAdmissionFilter(v as AdmissionFilter)}
          >
            <TabsList>
              <TabsTrigger value="candidate">
                候选
              </TabsTrigger>
              <TabsTrigger value="qualified">
                已合格
              </TabsTrigger>
            </TabsList>
          </Tabs>

          <Select
            value={kindFilter}
            onValueChange={(v) => v && setKindFilter(v)}
          >
            <SelectTrigger className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {KIND_OPTIONS.map((opt) => (
                <SelectItem key={opt.value} value={opt.value}>
                  {opt.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-center gap-2">
          <div className="relative">
            <Search className="absolute left-2 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
            <Input
              placeholder="搜索候选记忆..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-48 pl-7 sm:w-56"
            />
          </div>
          <Button
            variant="outline"
            size="icon"
            onClick={handleRefresh}
            title="刷新"
          >
            <RefreshCw className="size-4" />
          </Button>
        </div>
      </div>

      {/* 批量操作栏 */}
      {filteredItems.length > 0 && (
        <div className="flex items-center gap-2 rounded-lg border bg-muted/30 px-4 py-2.5">
          <span className="text-sm text-muted-foreground">
            当前可见 <span className="font-medium text-foreground">{filteredItems.length}</span> 条
          </span>
          <Separator orientation="vertical" className="mx-1 h-5" />
          <Button
            size="sm"
            variant="outline"
            className="border-green-300 text-green-700 hover:bg-green-50 hover:text-green-800 disabled:opacity-50 dark:border-green-700 dark:text-green-400 dark:hover:bg-green-900/20"
            onClick={() => handleBatchAction("confirm")}
            disabled={batchAction !== null}
          >
            {batchAction === "confirm" ? (
              <Loader2 className="mr-1 size-3.5 animate-spin" />
            ) : (
              <CheckCheck className="mr-1 size-3.5" />
            )}
            全部确认
          </Button>
          <Button
            size="sm"
            variant="outline"
            className="border-red-300 text-red-700 hover:bg-red-50 hover:text-red-800 disabled:opacity-50 dark:border-red-700 dark:text-red-400 dark:hover:bg-red-900/20"
            onClick={() => handleBatchAction("reject")}
            disabled={batchAction !== null}
          >
            {batchAction === "reject" ? (
              <Loader2 className="mr-1 size-3.5 animate-spin" />
            ) : (
              <XCircle className="mr-1 size-3.5" />
            )}
            全部拒绝
          </Button>
        </div>
      )}

      {/* 候选记忆列表 */}
      {isLoading ? (
        <div className="grid gap-3 lg:grid-cols-2">
          {Array.from({ length: 4 }).map((_, i) => (
            <CandidateCardSkeleton key={i} />
          ))}
        </div>
      ) : filteredItems.length === 0 ? (
        <EmptyState />
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {filteredItems.map((item) => (
            <CandidateCard
              key={item.id}
              item={item}
              evidence={evidenceMap[item.id]}
              evidenceLoading={
                expandedItems.has(item.id) && evidenceQueries.isLoading
              }
              expanded={expandedItems.has(item.id)}
              actionMode={actionModes[item.id] ?? null}
              note={actionNotes[item.id] ?? ""}
              isProcessing={processingId === item.id}
              onToggleExpand={() => handleToggleExpand(item.id)}
              onStartAction={(action) => handleStartAction(item.id, action)}
              onCancelAction={() => handleCancelAction(item.id)}
              onNoteChange={(n) => handleNoteChange(item.id, n)}
              onSubmitAction={() => handleSubmitAction(item.id)}
            />
          ))}
        </div>
      )}

      {/* 错误提示 */}
      {candidatesQuery.isError || qualifiedQuery.isError ? (
        <div className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
          <AlertCircle className="size-4 shrink-0" />
          加载候选记忆时出错，请点击刷新按钮重试。
        </div>
      ) : null}
    </div>
  );
}
