"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Download,
  Loader2,
  FileDown,
  ExternalLink,
  FolderOpen,
} from "lucide-react";
import { exportApi, ApiRequestError } from "@/lib/api";
import type { ExportStatus } from "@/lib/types";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
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
import { Button } from "@/components/ui/button";
import { ExportStatusBadge } from "@/components/report-badge";

const exportTypeLabels: Record<string, string> = {
  markdown: "Markdown",
  obsidian: "Obsidian",
  json: "JSON",
};

const targetTypeLabels: Record<string, string> = {
  document: "文档",
  report: "报告",
  topic: "专题",
  search: "搜索结果",
};

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

export default function ExportsPage() {
  const [statusFilter, setStatusFilter] = useState<string>("all");

  const { data, isLoading } = useQuery({
    queryKey: ["exports", statusFilter],
    queryFn: () =>
      exportApi.getHistory({
        status: statusFilter !== "all" ? (statusFilter as ExportStatus) : undefined,
      }),
    refetchInterval: (query) => {
      const items = query.state.data?.items ?? [];
      const hasActive = items.some(
        (item) => item.status === "pending" || item.status === "processing"
      );
      return hasActive ? 5000 : false;
    },
  });

  const displayItems = data?.items ?? [];

  const handleDownload = (downloadUrl?: string) => {
    if (!downloadUrl) {
      toast.error("暂无下载链接");
      return;
    }
    window.open(downloadUrl, "_blank");
  };

  const handleOpenDirectory = async (jobId: string) => {
    try {
      await exportApi.openDirectory(jobId);
      toast.success("已打开目录");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "打开目录失败";
      toast.error(message);
    }
  };

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div>
        <h1 className="text-2xl font-bold">导出历史</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          查看所有导出任务记录，下载已完成的导出文件
        </p>
      </div>

      {/* 列表 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>导出任务列表</CardTitle>
            <Select
              value={statusFilter}
              onValueChange={(v) => setStatusFilter(v as string)}
            >
              <SelectTrigger size="sm" className="w-32">
                <SelectValue placeholder="状态筛选" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="pending">等待中</SelectItem>
                <SelectItem value="processing">导出中</SelectItem>
                <SelectItem value="done">已完成</SelectItem>
                <SelectItem value="failed">失败</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displayItems.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <FileDown className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无导出记录，前往报告或文档页面发起导出
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>文件名</TableHead>
                  <TableHead>导出类型</TableHead>
                  <TableHead>目标类型</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>创建时间</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayItems.map((item) => {
                  const isDone = item.status === "done";
                  const isPending =
                    item.status === "pending" || item.status === "processing";
                  return (
                    <TableRow key={item.id}>
                      <TableCell className="max-w-xs">
                        <div className="flex items-center gap-2">
                          <FileDown className="size-4 shrink-0 text-muted-foreground" />
                          <span className="truncate" title={item.fileName}>
                            {item.fileName || `${item.id.slice(0, 8)}...`}
                          </span>
                        </div>
                        {item.errorMessage && (
                          <p className="mt-1 line-clamp-1 text-xs text-red-500">
                            {item.errorMessage}
                          </p>
                        )}
                      </TableCell>
                      <TableCell>
                        <span className="text-sm">
                          {exportTypeLabels[item.exportType] ?? item.exportType}
                        </span>
                      </TableCell>
                      <TableCell>
                        <span className="text-sm">
                          {targetTypeLabels[item.targetType] ?? item.targetType}
                        </span>
                      </TableCell>
                      <TableCell>
                        <ExportStatusBadge status={item.status} />
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {formatDate(item.createdAt)}
                      </TableCell>
                      <TableCell className="text-right">
                        {isPending ? (
                          <Loader2 className="ml-auto size-4 animate-spin text-muted-foreground" />
                        ) : isDone ? (
                          <div className="flex items-center justify-end gap-1">
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              title="下载"
                              onClick={() => handleDownload(item.downloadUrl)}
                              disabled={!item.downloadUrl}
                            >
                              <Download className="size-3.5" />
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              title="打开目录"
                              onClick={() => handleOpenDirectory(item.id)}
                            >
                              <FolderOpen className="size-3.5" />
                            </Button>
                          </div>
                        ) : item.downloadUrl ? (
                          <div className="flex items-center justify-end gap-1">
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              title="下载"
                              onClick={() => handleDownload(item.downloadUrl)}
                            >
                              <ExternalLink className="size-3.5" />
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              title="打开目录"
                              onClick={() => handleOpenDirectory(item.id)}
                            >
                              <FolderOpen className="size-3.5" />
                            </Button>
                          </div>
                        ) : (
                          <span className="text-xs text-muted-foreground">
                            -
                          </span>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
