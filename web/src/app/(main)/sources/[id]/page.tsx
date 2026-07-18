"use client";

import { useState } from "react";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  Download,
  Loader2,
  ExternalLink,
  FileText,
  RotateCcw,
  Trash2,
  AlertCircle,
  CheckCircle2,
  Sparkles,
  Brain,
  ChevronRight,
} from "lucide-react";
import { sourceApi, fileApi, documentApi, ApiRequestError } from "@/lib/api";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { StatusBadge, getSourceTypeLabel } from "@/components/status-badge";
import { AiStatusBadge } from "@/components/ai-badge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";

function formatDate(dateStr: string): string {
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

export default function SourceDetailPage() {
  const params = useParams();
  const router = useRouter();
  const queryClient = useQueryClient();
  const sourceId = params.id as string;

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [processing, setProcessing] = useState(false);

  const { data: source, isLoading } = useQuery({
    queryKey: ["source", sourceId],
    queryFn: () => sourceApi.get(sourceId),
    enabled: !!sourceId,
  });

  // 查找关联文档
  const { data: documents } = useQuery({
    queryKey: ["documents", "source", sourceId],
    queryFn: () =>
      documentApi.list(
        source?.topicId ? { topicId: source.topicId } : undefined
      ),
    enabled: !!source,
  });
  const relatedDocument = documents?.items.find(
    (d) => d.sourceId === sourceId
  );

  const handleDownload = async () => {
    if (!source?.originalFileId) {
      toast.error("无文件可下载");
      return;
    }
    setDownloading(true);
    try {
      const res = await fileApi.getDownloadUrl(source.originalFileId);
      window.open(res.url, "_blank");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "获取下载链接失败";
      toast.error(message);
    } finally {
      setDownloading(false);
    }
  };

  const handleRetry = async () => {
    try {
      await sourceApi.retry(sourceId);
      toast.success("已重新提交处理");
      queryClient.invalidateQueries({ queryKey: ["source", sourceId] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "重试失败";
      toast.error(message);
    }
  };

  const handleProcess = async () => {
    setProcessing(true);
    try {
      await sourceApi.processSource(sourceId);
      toast.success("已触发 AI 处理");
      queryClient.invalidateQueries({
        queryKey: ["documents", "source", sourceId],
      });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "触发 AI 处理失败";
      toast.error(message);
    } finally {
      setProcessing(false);
    }
  };

  const handleDelete = async () => {
    setDeleting(true);
    try {
      await sourceApi.delete(sourceId);
      toast.success("资料已删除");
      router.back();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    } finally {
      setDeleting(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!source) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <FileText className="mb-4 size-12 text-muted-foreground/50" />
        <p className="text-lg font-medium">资料不存在</p>
        <Button
          variant="outline"
          className="mt-4"
          onClick={() => router.back()}
        >
          返回
        </Button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* 返回按钮 */}
      <Button
        variant="ghost"
        size="sm"
        onClick={() => router.back()}
      >
        <ArrowLeft className="mr-2 size-4" />
        返回
      </Button>

      {/* 资料信息 */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between">
            <div className="flex-1">
              <CardTitle className="text-xl">{source.title || "未命名"}</CardTitle>
              <CardDescription className="mt-2">
                <StatusBadge status={source.status} />
              </CardDescription>
            </div>
            <div className="flex gap-2">
              {source.status === "failed" && (
                <Button variant="outline" size="sm" onClick={handleRetry}>
                  <RotateCcw className="mr-1.5 size-3.5" />
                  重试
                </Button>
              )}
              <Button
                variant="outline"
                size="sm"
                onClick={() => setDeleteOpen(true)}
              >
                <Trash2 className="mr-1.5 size-3.5" />
                删除
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* 基本信息网格 */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <p className="text-xs font-medium text-muted-foreground">来源类型</p>
              <p className="mt-1 text-sm">{getSourceTypeLabel(source.sourceType)}</p>
            </div>
            <div>
              <p className="text-xs font-medium text-muted-foreground">导入时间</p>
              <p className="mt-1 text-sm">{formatDate(source.createdAt)}</p>
            </div>
            <div>
              <p className="text-xs font-medium text-muted-foreground">当前状态</p>
              <p className="mt-1">
                <StatusBadge status={source.status} />
              </p>
            </div>
            <div>
              <p className="text-xs font-medium text-muted-foreground">来源地址</p>
              {source.sourceType === "url" && source.url ? (
                <a
                  href={source.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="mt-1 flex items-center gap-1 text-sm text-blue-600 hover:underline"
                >
                  <span className="truncate">{source.url}</span>
                  <ExternalLink className="size-3 shrink-0" />
                </a>
              ) : source.sourceType === "pdf" ? (
                <p className="mt-1 text-sm">PDF 文件</p>
              ) : (
                <p className="mt-1 text-sm text-muted-foreground">文本内容</p>
              )}
            </div>
          </div>

          {/* 错误信息 */}
          {source.status === "failed" && source.errorMessage && (
            <div className="flex items-start gap-2 rounded-lg bg-red-50 p-3 dark:bg-red-900/20">
              <AlertCircle className="mt-0.5 size-4 shrink-0 text-red-600" />
              <div>
                <p className="text-sm font-medium text-red-700 dark:text-red-400">
                  处理失败
                </p>
                <p className="mt-1 text-xs text-red-600 dark:text-red-400">
                  {source.errorMessage}
                </p>
              </div>
            </div>
          )}

          {/* 成功信息 */}
          {source.status === "saved" && (
            <div className="flex items-start gap-2 rounded-lg bg-green-50 p-3 dark:bg-green-900/20">
              <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-green-600" />
              <div>
                <p className="text-sm font-medium text-green-700 dark:text-green-400">
                  处理完成
                </p>
                <p className="mt-1 text-xs text-green-600 dark:text-green-400">
                  资料已成功保存并处理
                </p>
              </div>
            </div>
          )}

          <Separator />

          {/* 文件下载 */}
          {source.sourceType === "pdf" && source.originalFileId && (
            <div className="flex items-center justify-between rounded-lg border p-4">
              <div className="flex items-center gap-3">
                <div className="flex size-10 items-center justify-center rounded-lg bg-red-50">
                  <FileText className="size-5 text-red-600" />
                </div>
                <div>
                  <p className="text-sm font-medium">
                    PDF 文件
                  </p>
                  <p className="text-xs text-muted-foreground">PDF 文档</p>
                </div>
              </div>
              <Button
                variant="outline"
                size="sm"
                onClick={handleDownload}
                disabled={downloading}
              >
                {downloading ? (
                  <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                ) : (
                  <Download className="mr-1.5 size-3.5" />
                )}
                下载
              </Button>
            </div>
          )}

          {/* 原始文本 */}
          {source.sourceType === "text" && source.rawText && (
            <div>
              <p className="mb-2 text-xs font-medium text-muted-foreground">
                原始文本
              </p>
              <div className="max-h-96 overflow-y-auto rounded-lg border bg-muted/30 p-4">
                <pre className="whitespace-pre-wrap text-sm">
                  {source.rawText}
                </pre>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* AI 处理区块 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Brain className="size-4 text-indigo-600" />
            AI 处理
          </CardTitle>
          <CardDescription>
            对已保存的资料触发 AI 分析，生成摘要、关键要点、信号抽取等
          </CardDescription>
        </CardHeader>
        <CardContent>
          {/* 有关联文档：显示查看文档按钮 */}
          {relatedDocument ? (
            <div className="flex items-center justify-between rounded-lg border bg-muted/30 p-4">
              <div className="flex items-center gap-3">
                <div className="flex size-10 items-center justify-center rounded-lg bg-indigo-50">
                  <FileText className="size-5 text-indigo-600" />
                </div>
                <div>
                  <p className="text-sm font-medium">
                    {relatedDocument.title || "文档"}
                  </p>
                  <div className="mt-1 flex items-center gap-2">
                    <AiStatusBadge status={relatedDocument.aiStatus} />
                    {relatedDocument.aiStatus === "processing" && (
                      <span className="text-xs text-muted-foreground">
                        AI 正在分析中...
                      </span>
                    )}
                  </div>
                </div>
              </div>
              <Link href={`/documents/${relatedDocument.id}`}>
                <Button variant="outline" size="sm">
                  查看文档
                  <ChevronRight className="ml-1 size-3.5" />
                </Button>
              </Link>
            </div>
          ) : /* 无关联文档且资料已保存：显示开始AI处理按钮 */
          source.status === "saved" ? (
            <div className="flex items-center justify-between rounded-lg border bg-muted/30 p-4">
              <div className="flex items-center gap-3">
                <div className="flex size-10 items-center justify-center rounded-lg bg-amber-50">
                  <Sparkles className="size-5 text-amber-600" />
                </div>
                <div>
                  <p className="text-sm font-medium">开始 AI 处理</p>
                  <p className="text-xs text-muted-foreground">
                    触发 AI 分析以生成文档摘要和结构化信息
                  </p>
                </div>
              </div>
              <Button
                size="sm"
                onClick={handleProcess}
                disabled={processing}
              >
                {processing ? (
                  <>
                    <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                    处理中
                  </>
                ) : (
                  <>
                    <Sparkles className="mr-1.5 size-3.5" />
                    开始 AI 处理
                  </>
                )}
              </Button>
            </div>
          ) : /* 资料处理中：显示等待状态 */
          source.status === "pending" || source.status === "queued" ? (
            <div className="flex items-center justify-center py-6 text-center">
              <div className="text-muted-foreground">
                <Loader2 className="mx-auto mb-2 size-6 animate-spin opacity-50" />
                <p className="text-sm">资料正在处理中，请稍候...</p>
              </div>
            </div>
          ) : /* 资料失败：提示需先重试 */
          source.status === "failed" ? (
            <div className="flex items-center justify-center py-6 text-center">
              <div className="text-muted-foreground">
                <AlertCircle className="mx-auto mb-2 size-6 opacity-50" />
                <p className="text-sm">
                  资料处理失败，请先重试资料导入
                </p>
              </div>
            </div>
          ) : (
            <div className="flex items-center justify-center py-6 text-center">
              <div className="text-muted-foreground">
                <FileText className="mx-auto mb-2 size-6 opacity-30" />
                <p className="text-sm">暂无可用的 AI 处理</p>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* 删除确认弹窗 */}
      <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>确认删除</DialogTitle>
            <DialogDescription>
              确定要删除这份资料吗？此操作不可恢复。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose
              render={<Button variant="outline" type="button" />}
            >
              取消
            </DialogClose>
            <Button
              variant="destructive"
              onClick={handleDelete}
              disabled={deleting}
            >
              {deleting && <Loader2 className="mr-2 size-4 animate-spin" />}
              删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
