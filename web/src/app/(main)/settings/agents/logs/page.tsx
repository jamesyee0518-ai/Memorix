"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  Loader2,
  ArrowLeft,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { agentApi } from "@/lib/api";
import type { AgentInvocationLog } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
} from "@/components/ui/card";
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

const toolOptions = [
  { value: "list_topics", label: "list_topics" },
  { value: "search_memory", label: "search_memory" },
  { value: "ask_memory", label: "ask_memory" },
  { value: "get_document", label: "get_document" },
  { value: "get_report", label: "get_report" },
];

const statusOptions = [
  { value: "success", label: "成功" },
  { value: "failed", label: "失败" },
  { value: "denied", label: "拒绝" },
  { value: "rate_limited", label: "限流" },
];

const statusBadgeMap: Record<string, { label: string; className: string }> = {
  success: { label: "成功", className: "bg-green-100 text-green-700" },
  failed: { label: "失败", className: "bg-red-100 text-red-700" },
  denied: { label: "拒绝", className: "bg-orange-100 text-orange-700" },
  rate_limited: { label: "限流", className: "bg-yellow-100 text-yellow-700" },
};

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

const PAGE_SIZE = 20;

export default function AgentLogsPage() {
  const [page, setPage] = useState(1);
  const [toolName, setToolName] = useState<string>("");
  const [status, setStatus] = useState<string>("");

  const { data, isLoading } = useQuery({
    queryKey: ["agent-logs", page, toolName, status],
    queryFn: () =>
      agentApi.getInvocationLogs({
        page,
        pageSize: PAGE_SIZE,
        toolName: toolName || undefined,
        status: status || undefined,
      }),
  });

  const logs: AgentInvocationLog[] = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = data?.totalPages ?? 0;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">调用日志</h2>
          <p className="text-sm text-muted-foreground">
            Agent Profile 的工具调用历史记录
          </p>
        </div>
        <Link href="/settings/agents">
          <Button variant="outline" size="sm">
            <ArrowLeft className="mr-2 size-4" />
            返回 Agent 列表
          </Button>
        </Link>
      </div>

      {/* 筛选 */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">工具名：</span>
          <Select
            value={toolName}
            onValueChange={(v) => {
              setToolName(v === "all" || v === null ? "" : v);
              setPage(1);
            }}
          >
            <SelectTrigger className="w-44">
              <SelectValue placeholder="全部" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部</SelectItem>
              {toolOptions.map((opt) => (
                <SelectItem key={opt.value} value={opt.value}>
                  {opt.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">状态：</span>
          <Select
            value={status}
            onValueChange={(v) => {
              setStatus(v === "all" || v === null ? "" : v);
              setPage(1);
            }}
          >
            <SelectTrigger className="w-32">
              <SelectValue placeholder="全部" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部</SelectItem>
              {statusOptions.map((opt) => (
                <SelectItem key={opt.value} value={opt.value}>
                  {opt.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <span className="ml-auto text-sm text-muted-foreground">
          共 {total} 条记录
        </span>
      </div>

      {/* 表格 */}
      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : logs.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <p className="text-lg font-medium">暂无调用日志</p>
            <p className="mt-1 text-sm text-muted-foreground">
              当 Agent Profile 被调用时，日志将显示在这里
            </p>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>时间</TableHead>
                <TableHead>工具名</TableHead>
                <TableHead>传输方式</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>结果数</TableHead>
                <TableHead>延迟</TableHead>
                <TableHead>错误信息</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {logs.map((log) => {
                const badge = statusBadgeMap[log.status] ?? {
                  label: log.status,
                  className: "",
                };
                return (
                  <TableRow key={log.id}>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(log.createdAt)}
                    </TableCell>
                    <TableCell>
                      <code className="text-xs font-mono">{log.toolName}</code>
                    </TableCell>
                    <TableCell>
                      <code className="text-xs">{log.transport}</code>
                    </TableCell>
                    <TableCell>
                      <Badge className={badge.className}>{badge.label}</Badge>
                    </TableCell>
                    <TableCell className="text-sm">
                      {log.resultCount ?? "-"}
                    </TableCell>
                    <TableCell className="text-sm">
                      {log.latencyMs} ms
                    </TableCell>
                    <TableCell className="max-w-[300px] truncate text-xs text-red-600">
                      {log.errorMessage || "-"}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      {/* 分页 */}
      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
          >
            <ChevronLeft className="size-4" />
          </Button>
          <span className="text-sm text-muted-foreground">
            第 {page} / {totalPages} 页
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
          >
            <ChevronRight className="size-4" />
          </Button>
        </div>
      )}
    </div>
  );
}
