"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  FileText,
  Loader2,
  Search,
  RefreshCw,
  Brain,
  Filter,
  AlertCircle,
  ChevronDown,
  ChevronRight,
} from "lucide-react";
import { agentMemoryApi } from "@/lib/api";
import type { AgentMemoryItem, AgentMemoryEvidence } from "@/lib/types";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import { cn } from "@/lib/utils";

// ===== 工具函数 =====

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

function formatConfidence(confidence: number): string {
  return `${Math.round(confidence * 100)}%`;
}

const ADMISSION_LABELS: Record<string, string> = {
  candidate: "候选",
  qualified: "已合格",
  confirmed: "已确认",
  rejected: "已拒绝",
};

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

// ===== 证据卡片组件 =====

interface EvidenceCardProps {
  item: AgentMemoryItem;
  evidence: AgentMemoryEvidence[];
  evidenceLoading: boolean;
  expanded: boolean;
  onToggleExpand: () => void;
}

function EvidenceCard({
  item,
  evidence,
  evidenceLoading,
  expanded,
  onToggleExpand,
}: EvidenceCardProps) {
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
            置信度{" "}
            <span className="font-medium text-foreground">
              {formatConfidence(item.confidence)}
            </span>
          </span>
          <span>
            重要性 <span className="font-medium text-foreground">{item.importance}/10</span>
          </span>
          <span className="inline-flex items-center gap-1">
            <FileText className="size-3" />
            证据{" "}
            <span className="font-medium text-foreground">{evidence.length}</span>
          </span>
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

        {/* 证据列表 */}
        {expanded && evidenceLoading && (
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <Loader2 className="size-3 animate-spin" />
            正在加载证据...
          </div>
        )}

        {expanded && !evidenceLoading && evidence.length > 0 && (
          <div className="space-y-2">
            <p className="text-xs font-medium text-muted-foreground">
              证据来源（{evidence.length} 条）
            </p>
            {evidence.map((ev) => (
              <div
                key={ev.id}
                className="rounded-md border bg-background px-3 py-2 text-xs"
              >
                <div className="flex items-center gap-2">
                  <Badge variant="secondary" className="shrink-0 text-[10px]">
                    {ev.evidenceKind}
                  </Badge>
                  {ev.relation && (
                    <span className="text-muted-foreground">
                      关系: {ev.relation}
                    </span>
                  )}
                  <span className="ml-auto shrink-0 text-muted-foreground">
                    {formatDate(ev.capturedAt)}
                  </span>
                </div>
                <div className="mt-1.5 flex items-center gap-2 text-muted-foreground">
                  <span className="font-medium text-foreground/70">引用 ID:</span>
                  <code className="rounded bg-muted px-1.5 py-0.5 text-[11px]">
                    {ev.referenceId}
                  </code>
                  {ev.locator && (
                    <>
                      <span className="font-medium text-foreground/70">定位:</span>
                      <code className="truncate rounded bg-muted px-1.5 py-0.5 text-[11px]">
                        {ev.locator}
                      </code>
                    </>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        {/* 未展开时的摘要 */}
        {!expanded && evidence.length > 0 && (
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <FileText className="size-3" />
            <span>
              {evidence.length} 条证据 · 最新: {formatDate(evidence[0]?.capturedAt)}
            </span>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

// ===== 加载骨架屏 =====

function EvidenceCardSkeleton() {
  return (
    <Card>
      <CardHeader className="space-y-2">
        <div className="flex items-start justify-between gap-3">
          <div className="flex-1 space-y-2">
            <div className="h-5 w-3/4 animate-pulse rounded bg-muted" />
            <div className="h-4 w-full animate-pulse rounded bg-muted" />
          </div>
          <div className="flex gap-1.5">
            <div className="h-5 w-12 animate-pulse rounded-full bg-muted" />
            <div className="h-5 w-14 animate-pulse rounded-full bg-muted" />
          </div>
        </div>
        <div className="flex gap-4">
          <div className="h-3 w-24 animate-pulse rounded bg-muted" />
          <div className="h-3 w-20 animate-pulse rounded bg-muted" />
          <div className="h-3 w-28 animate-pulse rounded bg-muted" />
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="h-px w-full bg-muted" />
        <div className="h-4 w-1/2 animate-pulse rounded bg-muted" />
        <div className="h-12 w-full animate-pulse rounded bg-muted" />
      </CardContent>
    </Card>
  );
}

// ===== 空状态 =====

function EmptyState({ hasItems }: { hasItems: boolean }) {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center">
      <div className="flex size-16 items-center justify-center rounded-full bg-muted">
        <FileText className="size-8 text-muted-foreground" />
      </div>
      <h3 className="mt-4 text-lg font-semibold">
        {hasItems ? "暂无证据" : "暂无记忆条目"}
      </h3>
      <p className="mt-1 text-sm text-muted-foreground">
        {hasItems
          ? "当前筛选条件下没有包含证据的记忆条目。"
          : "Agent 尚未捕获任何记忆条目，请稍后再来查看。"}
      </p>
    </div>
  );
}

// ===== 主页面 =====

export default function EvidencePage() {
  const queryClient = useQueryClient();

  // 筛选状态
  const [searchQuery, setSearchQuery] = useState("");
  const [onlyWithEvidence, setOnlyWithEvidence] = useState(true);

  // 展开状态
  const [expandedItems, setExpandedItems] = useState<Set<string>>(new Set());

  // ===== 数据查询 =====

  // 获取记忆条目
  const itemsQuery = useQuery({
    queryKey: ["evidence-page", "items"],
    queryFn: () =>
      agentMemoryApi.searchMemory({ query: "", limit: 100 }),
  });

  const items = itemsQuery.data;

  // 批量获取所有条目的证据
  const evidenceQuery = useQuery({
    queryKey: [
      "evidence-page",
      "all-evidence",
      items?.map((i) => i.id).join(",") ?? "",
    ],
    queryFn: async () => {
      if (!items || items.length === 0) return {} as Record<string, AgentMemoryEvidence[]>;
      const results = await Promise.all(
        items.map(async (item) => {
          const ev = await agentMemoryApi.getEvidence(item.id);
          return [item.id, ev] as const;
        }),
      );
      return Object.fromEntries(results) as Record<
        string,
        AgentMemoryEvidence[]
      >;
    },
    enabled: !!items && items.length > 0,
  });

  const evidenceMap = evidenceQuery.data ?? {};

  // ===== 自动展开有证据的条目 =====
  useEffect(() => {
    if (evidenceQuery.data) {
      const newExpanded = new Set<string>();
      for (const [itemId, evs] of Object.entries(evidenceQuery.data)) {
        if (evs.length > 0) {
          newExpanded.add(itemId);
        }
      }
      // 仅在初次加载时自动展开
      setExpandedItems((prev) => (prev.size === 0 ? newExpanded : prev));
    }
  }, [evidenceQuery.data]);

  // ===== 筛选 =====
  const filteredItems = useMemo(() => {
    if (!items) return [];
    let result = items;
    if (onlyWithEvidence) {
      result = result.filter(
        (item) => (evidenceMap[item.id]?.length ?? 0) > 0,
      );
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
  }, [items, onlyWithEvidence, searchQuery, evidenceMap]);

  // ===== 统计 =====
  const totalItems = items?.length ?? 0;
  const itemsWithEvidence = useMemo(
    () =>
      items?.filter(
        (item) => (evidenceMap[item.id]?.length ?? 0) > 0,
      ).length ?? 0,
    [items, evidenceMap],
  );
  const totalEvidence = useMemo(
    () =>
      Object.values(evidenceMap).reduce(
        (sum, evs) => sum + evs.length,
        0,
      ),
    [evidenceMap],
  );

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

  const handleRefresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["evidence-page"] });
  };

  const isLoading = itemsQuery.isLoading || (itemsQuery.isSuccess && evidenceQuery.isLoading);

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
          <FileText className="size-6 text-primary" />
          证据管理
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          查看所有包含证据来源的记忆条目，了解每条记忆的支撑信息
        </p>
      </div>

      {/* 统计卡片 */}
      <div className="grid gap-3 sm:grid-cols-3">
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <Brain className="size-3.5" />
              记忆条目总数
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums">
              {totalItems}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <FileText className="size-3.5 text-blue-600" />
              含证据条目
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums text-blue-600">
              {itemsWithEvidence}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <FileText className="size-3.5 text-green-600" />
              证据总数
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums text-green-600">
              {totalEvidence}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* 筛选栏 */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant={onlyWithEvidence ? "default" : "outline"}
            size="sm"
            onClick={() => setOnlyWithEvidence((v) => !v)}
          >
            <Filter className="mr-1.5 size-3.5" />
            {onlyWithEvidence ? "仅显示有证据" : "显示全部"}
          </Button>
        </div>

        <div className="flex items-center gap-2">
          <div className="relative">
            <Search className="absolute left-2 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
            <Input
              placeholder="搜索记忆条目..."
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

      {/* 证据列表 */}
      {itemsQuery.isError ? (
        <div className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
          <AlertCircle className="size-4 shrink-0" />
          加载记忆条目时出错，请点击刷新按钮重试。
        </div>
      ) : isLoading ? (
        <div className="grid gap-3 lg:grid-cols-2">
          {Array.from({ length: 4 }).map((_, i) => (
            <EvidenceCardSkeleton key={i} />
          ))}
        </div>
      ) : filteredItems.length === 0 ? (
        <EmptyState hasItems={totalItems > 0} />
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {filteredItems.map((item) => (
            <EvidenceCard
              key={item.id}
              item={item}
              evidence={evidenceMap[item.id] ?? []}
              evidenceLoading={
                evidenceQuery.isLoading && !evidenceMap[item.id]
              }
              expanded={expandedItems.has(item.id)}
              onToggleExpand={() => handleToggleExpand(item.id)}
            />
          ))}
        </div>
      )}
    </div>
  );
}
