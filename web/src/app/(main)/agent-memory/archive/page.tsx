"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Archive,
  ArchiveRestore,
  Trash2,
  Eye,
  Clock,
  Shield,
  ArrowLeft,
  Search,
  ChevronLeft,
  ChevronRight,
  Loader2,
} from "lucide-react";
import { agentMemoryApi } from "@/lib/api";
import type {
  AgentMemoryItem,
  AgentMemoryAccessLog,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

// ===== 类型与常量 =====

type ArchivedMemoryItem = AgentMemoryItem & { archivedAt?: string };

const admissionBadgeMap: Record<
  string,
  { label: string; className: string }
> = {
  candidate: { label: "候选", className: "bg-blue-100 text-blue-700" },
  qualified: { label: "合格", className: "bg-cyan-100 text-cyan-700" },
  confirmed: { label: "已确认", className: "bg-green-100 text-green-700" },
  rejected: { label: "已拒绝", className: "bg-red-100 text-red-700" },
};

const kindBadgeMap: Record<string, { label: string; className: string }> = {
  fact: { label: "事实", className: "bg-violet-100 text-violet-700" },
  preference: { label: "偏好", className: "bg-amber-100 text-amber-700" },
  decision: { label: "决策", className: "bg-indigo-100 text-indigo-700" },
  plan: { label: "计划", className: "bg-teal-100 text-teal-700" },
  note: { label: "备注", className: "bg-slate-100 text-slate-700" },
  summary: { label: "摘要", className: "bg-purple-100 text-purple-700" },
};

const ARCHIVE_FETCH_LIMIT = 100;

// ===== 工具函数 =====

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  if (Number.isNaN(d.getTime())) return "-";
  return d.toLocaleString("zh-CN");
}

function truncateId(id?: string, len = 8): string {
  if (!id) return "-";
  return id.length > len ? `${id.slice(0, len)}…` : id;
}

function getBadge(
  map: Record<string, { label: string; className: string }>,
  key: string
) {
  return map[key] ?? { label: key, className: "bg-muted text-muted-foreground" };
}

// ===== 归档记忆 Tab =====

function ArchivedMemoryTab() {
  const queryClient = useQueryClient();
  const [searchInput, setSearchInput] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [forgetTarget, setForgetTarget] = useState<ArchivedMemoryItem | null>(
    null
  );

  const { data, isLoading } = useQuery({
    queryKey: ["agent-memory-archived", searchQuery],
    queryFn: () =>
      agentMemoryApi.searchMemory({
        query: searchQuery,
        admissionState: "confirmed",
        limit: ARCHIVE_FETCH_LIMIT,
        offset: 0,
      }),
  });

  const archivedItems: ArchivedMemoryItem[] = (data ?? []).filter(
    (item) => item.status === "archived"
  );

  const restoreMutation = useMutation({
    mutationFn: (id: string) => agentMemoryApi.restoreMemory(id),
    onSuccess: () => {
      toast.success("记忆已恢复为活跃状态");
      queryClient.invalidateQueries({ queryKey: ["agent-memory-archived"] });
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "恢复失败");
    },
  });

  const forgetMutation = useMutation({
    mutationFn: (id: string) => agentMemoryApi.forgetMemory(id),
    onSuccess: () => {
      toast.success("记忆已永久删除");
      setForgetTarget(null);
      queryClient.invalidateQueries({ queryKey: ["agent-memory-archived"] });
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "删除失败");
    },
  });

  const handleSearch = () => {
    setSearchQuery(searchInput.trim());
  };

  return (
    <div className="space-y-4">
      {/* 统计 */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2 rounded-lg border bg-muted/40 px-3 py-1.5">
          <Archive className="size-4 text-muted-foreground" />
          <span className="text-sm text-muted-foreground">归档记忆</span>
          <Badge variant="secondary">{archivedItems.length}</Badge>
        </div>
      </div>

      {/* 搜索栏 */}
      <div className="flex items-center gap-2">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") handleSearch();
            }}
            placeholder="搜索归档记忆标题或内容…"
            className="pl-8"
          />
        </div>
        <Button variant="outline" size="sm" onClick={handleSearch}>
          <Search className="mr-1 size-4" />
          搜索
        </Button>
        {searchQuery && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setSearchInput("");
              setSearchQuery("");
            }}
          >
            清除
          </Button>
        )}
      </div>

      <Separator />

      {/* 内容 */}
      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Card key={i}>
              <CardHeader>
                <div className="h-4 w-1/3 animate-pulse rounded bg-muted" />
                <div className="mt-2 h-3 w-1/2 animate-pulse rounded bg-muted" />
              </CardHeader>
              <CardContent>
                <div className="h-3 w-full animate-pulse rounded bg-muted" />
                <div className="mt-2 h-3 w-2/3 animate-pulse rounded bg-muted" />
              </CardContent>
            </Card>
          ))}
        </div>
      ) : archivedItems.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Archive className="mb-3 size-10 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无归档记忆</p>
            <p className="mt-1 text-sm text-muted-foreground">
              被归档的记忆条目将显示在这里，可执行恢复或永久删除操作
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-3">
          {archivedItems.map((item) => {
            const kindBadge = getBadge(kindBadgeMap, item.kind);
            const admissionBadge = getBadge(
              admissionBadgeMap,
              item.admissionState
            );
            return (
              <Card key={item.id}>
                <CardHeader>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="space-y-1">
                      <CardTitle className="text-base">
                        {item.title}
                      </CardTitle>
                      <CardDescription className="flex flex-wrap items-center gap-2">
                        <Badge className={kindBadge.className}>
                          {kindBadge.label}
                        </Badge>
                        <Badge className={admissionBadge.className}>
                          {admissionBadge.label}
                        </Badge>
                        <span className="text-xs">
                          置信度 {(item.confidence * 100).toFixed(0)}%
                        </span>
                        <Separator orientation="vertical" className="h-3" />
                        <span className="text-xs">
                          重要性 {item.importance}
                        </span>
                      </CardDescription>
                    </div>
                  </div>
                </CardHeader>
                {item.summary && (
                  <CardContent>
                    <p className="text-sm text-muted-foreground line-clamp-3">
                      {item.summary}
                    </p>
                  </CardContent>
                )}
                <CardFooter className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
                    <span className="flex items-center gap-1">
                      <Clock className="size-3" />
                      创建于 {formatDate(item.createdAt)}
                    </span>
                    <span className="flex items-center gap-1">
                      <Archive className="size-3" />
                      归档于 {formatDate(item.archivedAt)}
                    </span>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => restoreMutation.mutate(item.id)}
                      disabled={restoreMutation.isPending}
                    >
                      <ArchiveRestore className="mr-1 size-4" />
                      恢复
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      onClick={() => setForgetTarget(item)}
                    >
                      <Trash2 className="mr-1 size-4" />
                      永久删除
                    </Button>
                  </div>
                </CardFooter>
              </Card>
            );
          })}
        </div>
      )}

      {/* 永久删除确认弹窗 */}
      <Dialog
        open={forgetTarget !== null}
        onOpenChange={(open) => {
          if (!open) setForgetTarget(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>确认永久删除</DialogTitle>
            <DialogDescription>
              此操作不可撤销。该记忆条目将被从系统中彻底移除，无法恢复。
            </DialogDescription>
          </DialogHeader>
          {forgetTarget && (
            <div className="rounded-lg border bg-muted/40 p-3 text-sm">
              <p className="font-medium">{forgetTarget.title}</p>
              <p className="mt-1 text-xs text-muted-foreground">
                ID: {forgetTarget.id}
              </p>
            </div>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setForgetTarget(null)}>
              取消
            </Button>
            <Button
              variant="destructive"
              onClick={() => {
                if (forgetTarget) forgetMutation.mutate(forgetTarget.id);
              }}
              disabled={forgetMutation.isPending}
            >
              {forgetMutation.isPending && (
                <Loader2 className="mr-1 size-4 animate-spin" />
              )}
              确认删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// ===== 访问日志 Tab =====

function AccessLogTab() {
  const [sessionIdInput, setSessionIdInput] = useState("");
  const [sessionId, setSessionId] = useState("");
  const [memoryItemIdInput, setMemoryItemIdInput] = useState("");
  const [memoryItemId, setMemoryItemId] = useState("");
  const [limit, setLimit] = useState(50);
  const [offset, setOffset] = useState(0);

  const { data, isLoading } = useQuery({
    queryKey: [
      "agent-memory-access-logs",
      sessionId,
      memoryItemId,
      limit,
      offset,
    ],
    queryFn: () =>
      agentMemoryApi.getAccessLogs({
        sessionId: sessionId || undefined,
        memoryItemId: memoryItemId || undefined,
        limit,
        offset,
      }),
  });

  const logs: AgentMemoryAccessLog[] = data ?? [];
  const hasMore = logs.length >= limit;
  const canPrev = offset > 0;

  const applyFilters = () => {
    setSessionId(sessionIdInput.trim());
    setMemoryItemId(memoryItemIdInput.trim());
    setOffset(0);
  };

  const resetFilters = () => {
    setSessionIdInput("");
    setMemoryItemIdInput("");
    setSessionId("");
    setMemoryItemId("");
    setOffset(0);
  };

  return (
    <div className="space-y-4">
      {/* 统计 */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2 rounded-lg border bg-muted/40 px-3 py-1.5">
          <Shield className="size-4 text-muted-foreground" />
          <span className="text-sm text-muted-foreground">访问日志</span>
          <Badge variant="secondary">{logs.length}</Badge>
        </div>
        <span className="text-xs text-muted-foreground">
          （当前页条数，最多 {limit} 条/页）
        </span>
      </div>

      {/* 筛选 */}
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Session ID</span>
          <Input
            value={sessionIdInput}
            onChange={(e) => setSessionIdInput(e.target.value)}
            placeholder="可选，按会话筛选"
            className="w-56"
          />
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Memory Item ID</span>
          <Input
            value={memoryItemIdInput}
            onChange={(e) => setMemoryItemIdInput(e.target.value)}
            placeholder="可选，按记忆条目筛选"
            className="w-56"
          />
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">每页条数</span>
          <Select
            value={String(limit)}
            onValueChange={(v) => {
              setLimit(Number(v));
              setOffset(0);
            }}
          >
            <SelectTrigger className="w-28">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="50">50</SelectItem>
              <SelectItem value="100">100</SelectItem>
              <SelectItem value="200">200</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <Button variant="outline" size="sm" onClick={applyFilters}>
          <Search className="mr-1 size-4" />
          筛选
        </Button>
        <Button variant="ghost" size="sm" onClick={resetFilters}>
          重置
        </Button>
      </div>

      <Separator />

      {/* 表格 */}
      {isLoading ? (
        <Card>
          <div className="space-y-2 p-4">
            {Array.from({ length: 6 }).map((_, i) => (
              <div
                key={i}
                className="h-8 w-full animate-pulse rounded bg-muted"
              />
            ))}
          </div>
        </Card>
      ) : logs.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Eye className="mb-3 size-10 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无访问日志</p>
            <p className="mt-1 text-sm text-muted-foreground">
              当记忆条目被访问时，审计日志将显示在这里
            </p>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-28">操作</TableHead>
                  <TableHead>Memory Item</TableHead>
                  <TableHead>Session</TableHead>
                  <TableHead>Agent Profile</TableHead>
                  <TableHead>Trace ID</TableHead>
                  <TableHead className="w-44">时间</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {logs.map((log) => (
                  <TableRow key={log.id}>
                    <TableCell>
                      <Badge variant="secondary" className="font-mono text-xs">
                        {log.action}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <code className="text-xs text-muted-foreground">
                        {truncateId(log.memoryItemId)}
                      </code>
                    </TableCell>
                    <TableCell>
                      <code className="text-xs text-muted-foreground">
                        {truncateId(log.sessionId)}
                      </code>
                    </TableCell>
                    <TableCell>
                      <code className="text-xs text-muted-foreground">
                        {truncateId(log.agentProfileId)}
                      </code>
                    </TableCell>
                    <TableCell>
                      <code className="text-xs text-muted-foreground">
                        {truncateId(log.traceId)}
                      </code>
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(log.createdAt)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Card>
      )}

      {/* 分页 */}
      <div className="flex items-center justify-center gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={() => setOffset((o) => Math.max(0, o - limit))}
          disabled={!canPrev || isLoading}
        >
          <ChevronLeft className="size-4" />
          上一页
        </Button>
        <span className="text-sm text-muted-foreground">
          偏移 {offset} - {offset + logs.length}
        </span>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setOffset((o) => o + limit)}
          disabled={!hasMore || isLoading}
        >
          下一页
          <ChevronRight className="size-4" />
        </Button>
      </div>
    </div>
  );
}

// ===== 页面入口 =====

export default function AgentMemoryArchivePage() {
  const [activeTab, setActiveTab] = useState("archive");

  return (
    <div className="space-y-6">
      {/* 头部 */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">归档与日志</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            查看已归档的记忆条目与访问审计日志
          </p>
        </div>
        <Link href="/agent-memory">
          <Button variant="outline" size="sm">
            <ArrowLeft className="mr-2 size-4" />
            返回记忆中心
          </Button>
        </Link>
      </div>

      <Tabs
        value={activeTab}
        onValueChange={(v) => setActiveTab(v as string)}
      >
        <TabsList>
          <TabsTrigger value="archive">
            <Archive className="size-4" />
            归档记忆
          </TabsTrigger>
          <TabsTrigger value="logs">
            <Shield className="size-4" />
            访问日志
          </TabsTrigger>
        </TabsList>

        <TabsContent value="archive" className="mt-4">
          <ArchivedMemoryTab />
        </TabsContent>

        <TabsContent value="logs" className="mt-4">
          <AccessLogTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
