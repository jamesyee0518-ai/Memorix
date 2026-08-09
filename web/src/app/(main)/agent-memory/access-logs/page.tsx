"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  ScrollText,
  Loader2,
  Search,
  RefreshCw,
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  Filter,
  Inbox,
} from "lucide-react";
import { agentMemoryApi } from "@/lib/api";
import type { AgentMemoryAccessLog } from "@/lib/types";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Card,
  CardContent,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
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
    second: "2-digit",
  });
}

function truncateId(id?: string): string {
  if (!id) return "-";
  if (id.length <= 12) return id;
  return `${id.slice(0, 8)}...`;
}

function actionBadgeClass(action: string): string {
  const a = action.toLowerCase();
  if (a.includes("read") || a.includes("search") || a.includes("query")) {
    return "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300";
  }
  if (a.includes("write") || a.includes("capture") || a.includes("create")) {
    return "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300";
  }
  if (a.includes("confirm") || a.includes("accept")) {
    return "bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300";
  }
  if (a.includes("archive") || a.includes("restore")) {
    return "bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300";
  }
  if (a.includes("forget") || a.includes("delete") || a.includes("reject")) {
    return "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300";
  }
  return "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400";
}

// ===== 常量 =====

const PAGE_SIZE_OPTIONS = [10, 20, 50];

// ===== 加载骨架屏 =====

function TableSkeleton() {
  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            {Array.from({ length: 6 }).map((_, i) => (
              <TableHead key={i}>
                <div className="h-4 w-20 animate-pulse rounded bg-muted" />
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 8 }).map((_, rowIdx) => (
            <TableRow key={rowIdx}>
              {Array.from({ length: 6 }).map((_, colIdx) => (
                <TableCell key={colIdx}>
                  <div className="h-4 w-full animate-pulse rounded bg-muted" />
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

// ===== 空状态 =====

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center">
      <div className="flex size-16 items-center justify-center rounded-full bg-muted">
        <Inbox className="size-8 text-muted-foreground" />
      </div>
      <h3 className="mt-4 text-lg font-semibold">暂无访问日志</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        没有符合条件的访问日志记录，请调整筛选条件或稍后再来查看。
      </p>
    </div>
  );
}

// ===== 分页组件 =====

interface PaginationProps {
  currentPage: number;
  totalPages: number;
  totalItems: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

function Pagination({
  currentPage,
  totalPages,
  totalItems,
  pageSize,
  onPageChange,
  onPageSizeChange,
}: PaginationProps) {
  const startItem = totalItems === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const endItem = Math.min(currentPage * pageSize, totalItems);

  return (
    <div className="flex flex-col items-center justify-between gap-3 sm:flex-row">
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <span>
          共 <span className="font-medium text-foreground">{totalItems}</span>{" "}
          条记录，显示第 {startItem} - {endItem} 条
        </span>
        <Select
          value={String(pageSize)}
          onValueChange={(v) => onPageSizeChange(Number(v))}
        >
          <SelectTrigger className="h-8 w-[90px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PAGE_SIZE_OPTIONS.map((size) => (
              <SelectItem key={size} value={String(size)}>
                {size} 条/页
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={() => onPageChange(currentPage - 1)}
          disabled={currentPage <= 1}
        >
          <ChevronLeft className="mr-1 size-4" />
          上一页
        </Button>
        <span className="text-sm text-muted-foreground">
          第 <span className="font-medium text-foreground">{currentPage}</span>{" "}
          / {Math.max(totalPages, 1)} 页
        </span>
        <Button
          variant="outline"
          size="sm"
          onClick={() => onPageChange(currentPage + 1)}
          disabled={currentPage >= totalPages}
        >
          下一页
          <ChevronRight className="ml-1 size-4" />
        </Button>
      </div>
    </div>
  );
}

// ===== 主页面 =====

export default function AccessLogsPage() {
  const queryClient = useQueryClient();

  // 筛选状态
  const [sessionIdFilter, setSessionIdFilter] = useState("");
  const [actionFilter, setActionFilter] = useState("all");

  // 分页状态
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  // ===== 数据查询 =====

  const logsQuery = useQuery({
    queryKey: ["access-logs-page", "logs"],
    queryFn: () => agentMemoryApi.getAccessLogs({ limit: 100 }),
  });

  const logs = logsQuery.data ?? [];

  // 提取所有不重复的 action 类型
  const uniqueActions = useMemo(() => {
    const set = new Set<string>();
    logs.forEach((log) => {
      if (log.action) set.add(log.action);
    });
    return Array.from(set).sort();
  }, [logs]);

  // ===== 客户端筛选 =====
  const filteredLogs = useMemo(() => {
    let result = logs;

    // Session ID 筛选
    const sid = sessionIdFilter.trim().toLowerCase();
    if (sid) {
      result = result.filter(
        (log) => log.sessionId?.toLowerCase().includes(sid),
      );
    }

    // Action 筛选
    if (actionFilter !== "all") {
      result = result.filter((log) => log.action === actionFilter);
    }

    return result;
  }, [logs, sessionIdFilter, actionFilter]);

  // ===== 客户端分页 =====
  const totalPages = Math.ceil(filteredLogs.length / pageSize);

  // 筛选条件变化时重置页码
  useEffect(() => {
    if (currentPage > totalPages) {
      setCurrentPage(1);
    }
  }, [currentPage, totalPages]);

  const paginatedLogs = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return filteredLogs.slice(start, start + pageSize);
  }, [filteredLogs, currentPage, pageSize]);

  // ===== 事件处理 =====
  const handleRefresh = () => {
    void queryClient.invalidateQueries({
      queryKey: ["access-logs-page", "logs"],
    });
  };

  const handlePageChange = (page: number) => {
    setCurrentPage(page);
  };

  const handlePageSizeChange = (size: number) => {
    setPageSize(size);
    setCurrentPage(1);
  };

  const handleClearFilters = () => {
    setSessionIdFilter("");
    setActionFilter("all");
    setCurrentPage(1);
  };

  const hasActiveFilters = sessionIdFilter.trim() !== "" || actionFilter !== "all";

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
          <ScrollText className="size-6 text-primary" />
          访问日志
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          查看 Agent 记忆系统的所有访问操作记录，支持按会话和操作类型筛选
        </p>
      </div>

      {/* 统计卡片 */}
      <div className="grid gap-3 sm:grid-cols-3">
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <ScrollText className="size-3.5" />
              日志总数
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums">
              {logs.length}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <Filter className="size-3.5 text-blue-600" />
              筛选结果
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums text-blue-600">
              {filteredLogs.length}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <Inbox className="size-3.5 text-green-600" />
              操作类型数
            </div>
            <div className="mt-1 text-2xl font-bold tabular-nums text-green-600">
              {uniqueActions.length}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* 筛选栏 */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex flex-1 flex-col gap-3 sm:flex-row sm:items-end">
          <div className="space-y-2">
            <label className="text-sm font-medium">会话 ID 筛选</label>
            <div className="relative">
              <Search className="absolute left-2 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
              <Input
                placeholder="输入会话 ID..."
                value={sessionIdFilter}
                onChange={(e) => {
                  setSessionIdFilter(e.target.value);
                  setCurrentPage(1);
                }}
                className="w-full pl-7 sm:w-64"
              />
            </div>
          </div>

          <div className="space-y-2">
            <label className="text-sm font-medium">操作类型</label>
            <Select
              value={actionFilter}
              onValueChange={(v) => {
                setActionFilter(v ?? "all");
                setCurrentPage(1);
              }}
            >
              <SelectTrigger className="w-full sm:w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部操作</SelectItem>
                {uniqueActions.map((action) => (
                  <SelectItem key={action} value={action}>
                    {action}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {hasActiveFilters && (
            <Button
              variant="ghost"
              size="sm"
              onClick={handleClearFilters}
              className="text-muted-foreground"
            >
              清除筛选
            </Button>
          )}
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

      {/* 日志表格 */}
      {logsQuery.isError ? (
        <div className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
          <AlertCircle className="size-4 shrink-0" />
          加载访问日志时出错，请点击刷新按钮重试。
        </div>
      ) : logsQuery.isLoading ? (
        <TableSkeleton />
      ) : filteredLogs.length === 0 ? (
        <EmptyState />
      ) : (
        <>
          <div className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[180px]">时间</TableHead>
                  <TableHead className="w-[150px]">会话 ID</TableHead>
                  <TableHead className="w-[150px]">记忆条目 ID</TableHead>
                  <TableHead className="w-[150px]">Agent Profile ID</TableHead>
                  <TableHead className="w-[120px]">操作</TableHead>
                  <TableHead>Trace ID</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedLogs.map((log: AgentMemoryAccessLog) => (
                  <TableRow key={log.id}>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(log.createdAt)}
                    </TableCell>
                    <TableCell>
                      {log.sessionId ? (
                        <code
                          className="text-xs text-foreground"
                          title={log.sessionId}
                        >
                          {truncateId(log.sessionId)}
                        </code>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      {log.memoryItemId ? (
                        <code
                          className="text-xs text-foreground"
                          title={log.memoryItemId}
                        >
                          {truncateId(log.memoryItemId)}
                        </code>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      {log.agentProfileId ? (
                        <code
                          className="text-xs text-foreground"
                          title={log.agentProfileId}
                        >
                          {truncateId(log.agentProfileId)}
                        </code>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <Badge
                        className={cn(
                          "border-transparent text-[10px]",
                          actionBadgeClass(log.action),
                        )}
                      >
                        {log.action}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {log.traceId ? (
                        <code
                          className="text-xs text-muted-foreground"
                          title={log.traceId}
                        >
                          {truncateId(log.traceId)}
                        </code>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>

          {/* 分页 */}
          <Pagination
            currentPage={currentPage}
            totalPages={totalPages}
            totalItems={filteredLogs.length}
            pageSize={pageSize}
            onPageChange={handlePageChange}
            onPageSizeChange={handlePageSizeChange}
          />
        </>
      )}
    </div>
  );
}
