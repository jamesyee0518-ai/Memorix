"use client";

import { useEffect, useState, useCallback, useRef } from "react";
import {
  Loader2,
  CheckCircle2,
  AlertCircle,
  Download,
  FileDown,
  FolderOpen,
} from "lucide-react";
import { exportApi, ApiRequestError } from "@/lib/api";
import { toast } from "sonner";
import type { ExportJobDetail } from "@/lib/types";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { ExportStatusBadge } from "@/components/report-badge";

interface ExportStatusDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  exportJobId: string | null;
}

export function ExportStatusDialog({
  open,
  onOpenChange,
  exportJobId,
}: ExportStatusDialogProps) {
  const [job, setJob] = useState<ExportJobDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const fetchJob = useCallback(async () => {
    if (!exportJobId) return;
    try {
      const data = await exportApi.getJob(exportJobId);
      setJob(data);
      setError(null);
      // 当状态为 done 或 failed 时停止轮询
      if (data.status === "done" || data.status === "failed") {
        if (intervalRef.current) {
          clearInterval(intervalRef.current);
          intervalRef.current = null;
        }
      }
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "获取导出状态失败";
      setError(message);
    }
  }, [exportJobId]);

  // 初始加载 + 轮询
  useEffect(() => {
    if (!open || !exportJobId) {
      setJob(null);
      setError(null);
      setLoading(false);
      return;
    }

    setLoading(true);
    fetchJob().finally(() => setLoading(false));

    intervalRef.current = setInterval(() => {
      fetchJob();
    }, 3000);

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    };
  }, [open, exportJobId, fetchJob]);

  const isPending = !job || job.status === "pending" || job.status === "processing";
  const isDone = job?.status === "done";
  const isFailed = job?.status === "failed";

  const handleDownload = () => {
    if (job?.downloadUrl) {
      window.open(job.downloadUrl, "_blank");
    }
  };

  const handleOpenDirectory = async () => {
    const jobId = job?.id ?? exportJobId;
    if (!jobId) return;
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
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <FileDown className="size-5 text-primary" />
            导出状态
          </DialogTitle>
          <DialogDescription>
            查看导出任务进度，完成后可下载文件
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {/* 状态展示 */}
          <div className="flex items-center justify-between rounded-lg border bg-muted/30 p-4">
            <div className="flex items-center gap-3">
              {loading && !job ? (
                <Loader2 className="size-5 animate-spin text-muted-foreground" />
              ) : isPending ? (
                <Loader2 className="size-5 animate-spin text-blue-600" />
              ) : isDone ? (
                <CheckCircle2 className="size-5 text-green-600" />
              ) : isFailed ? (
                <AlertCircle className="size-5 text-red-600" />
              ) : (
                <Loader2 className="size-5 animate-spin text-muted-foreground" />
              )}
              <div>
                <p className="text-sm font-medium">
                  {loading && !job
                    ? "正在查询..."
                    : isPending
                      ? "导出任务进行中..."
                      : isDone
                        ? "导出完成"
                        : isFailed
                          ? "导出失败"
                          : "处理中..."}
                </p>
                <p className="text-xs text-muted-foreground">
                  {exportJobId && `任务ID: ${exportJobId.slice(0, 8)}...`}
                </p>
              </div>
            </div>
            {job && <ExportStatusBadge status={job.status} />}
          </div>

          {/* 导出信息 */}
          {job && (
            <div className="grid grid-cols-2 gap-3 text-sm">
              <div className="rounded-lg border p-3">
                <p className="text-xs font-medium text-muted-foreground">
                  导出类型
                </p>
                <p className="mt-1 capitalize">{job.exportType}</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="text-xs font-medium text-muted-foreground">
                  目标类型
                </p>
                <p className="mt-1">{job.targetType}</p>
              </div>
            </div>
          )}

          {/* 错误信息 */}
          {error && (
            <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 p-3 dark:border-red-900 dark:bg-red-900/20">
              <AlertCircle className="mt-0.5 size-4 shrink-0 text-red-600" />
              <p className="text-sm text-red-700 dark:text-red-300">{error}</p>
            </div>
          )}

          {/* 操作按钮 */}
          <div className="flex justify-end gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => onOpenChange(false)}
            >
              {isDone ? "关闭" : "隐藏"}
            </Button>
            {isDone && job?.downloadUrl && (
              <Button size="sm" onClick={handleDownload}>
                <Download className="mr-1.5 size-3.5" />
                下载文件
              </Button>
            )}
            {isDone && !job?.downloadUrl && job?.fileId && (
              <Button size="sm" onClick={handleDownload}>
                <Download className="mr-1.5 size-3.5" />
                获取下载链接
              </Button>
            )}
            {isDone && (
              <Button variant="outline" size="sm" onClick={handleOpenDirectory}>
                <FolderOpen className="mr-1.5 size-3.5" />
                打开目录
              </Button>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
