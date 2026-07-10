"use client";

import { useEffect, useState } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import Link from "next/link";
import {
  ArrowLeft,
  Loader2,
  ClipboardList,
  RefreshCw,
  AlertCircle,
  FileDown,
  Copy,
  Cpu,
  Clock,
  FolderOpen,
  CalendarRange,
  Gauge,
  ExternalLink,
  FileText,
  Pencil,
  Archive,
} from "lucide-react";
import { reportApi, exportApi, ApiRequestError } from "@/lib/api";
import type { ReportJobStatus } from "@/lib/types";
import { useTopicStore } from "@/stores/topic-store";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Markdown } from "@/components/markdown";
import {
  ReportStatusBadge,
  ReportTypeBadge,
  QualityScoreBar,
} from "@/components/report-badge";
import { ExportStatusDialog } from "@/components/export-status-dialog";

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

function formatDateShort(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleDateString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
}

// 报告生成步骤中文标签映射
const REPORT_STEP_LABELS: Record<string, string> = {
  planning: "规划中",
  retrieving: "检索资料中",
  building_context: "构建上下文中",
  generating: "生成报告中",
  evaluating: "质量评估中",
  saving: "保存中",
  done: "完成",
};

// 报告生成进度组件
function ReportProgressBar({ jobId }: { jobId: string }) {
  const [jobStatus, setJobStatus] = useState<ReportJobStatus | null>(null);

  useEffect(() => {
    if (!jobId) return;

    let active = true;
    let interval: ReturnType<typeof setInterval> | null = null;

    const poll = async () => {
      try {
        const data = await reportApi.getJobStatus(jobId);
        if (!active) return;
        setJobStatus(data);
        // 状态为 done 或 failed 时停止轮询
        if (data.status === "done" || data.status === "failed") {
          if (interval) {
            clearInterval(interval);
            interval = null;
          }
        }
      } catch {
        // 忽略轮询错误
      }
    };

    poll();
    interval = setInterval(poll, 3000);

    return () => {
      active = false;
      if (interval) clearInterval(interval);
    };
  }, [jobId]);

  const progress = jobStatus?.progress ?? 0;
  const currentStep = jobStatus?.currentStep;
  const stepLabel = currentStep
    ? REPORT_STEP_LABELS[currentStep] ?? currentStep
    : "准备中...";

  return (
    <Card>
      <CardContent className="py-6">
        <div className="mb-4 flex items-center gap-3">
          <Loader2 className="size-5 animate-spin text-blue-600" />
          <div className="flex-1">
            <p className="text-sm font-medium text-blue-700 dark:text-blue-300">
              报告正在生成中...
            </p>
            <p className="text-xs text-muted-foreground">
              当前步骤：{stepLabel}
            </p>
          </div>
          <span className="text-lg font-bold text-blue-600">{progress}%</span>
        </div>
        {/* 进度条 */}
        <div className="h-2 w-full overflow-hidden rounded-full bg-gray-200 dark:bg-gray-700">
          <div
            className="h-full rounded-full bg-blue-600 transition-all duration-500"
            style={{ width: `${Math.max(2, progress)}%` }}
          />
        </div>
      </CardContent>
    </Card>
  );
}

export default function ReportDetailPage() {
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const { topics, fetchTopics } = useTopicStore();
  const reportId = params.id as string;
  const jobId = searchParams.get("jobId");

  const [regenerating, setRegenerating] = useState(false);
  const [exportOpen, setExportOpen] = useState(false);
  const [exportJobId, setExportJobId] = useState<string | null>(null);

  // 编辑对话框状态
  const [editOpen, setEditOpen] = useState(false);
  const [editTitle, setEditTitle] = useState("");
  const [editContent, setEditContent] = useState("");
  const [saving, setSaving] = useState(false);

  // 归档确认对话框状态
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [archiving, setArchiving] = useState(false);

  // 获取专题列表用于映射名称
  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  const { data: report, isLoading } = useQuery({
    queryKey: ["report", reportId],
    queryFn: () => reportApi.get(reportId),
    enabled: !!reportId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (status === "pending" || status === "processing") {
        return 5000;
      }
      return false;
    },
  });

  // 专题名称映射
  const topicMap = new Map(topics.map((t) => [t.id, t.name]));
  const topicName = report?.topicId
    ? topicMap.get(report.topicId) ?? "-"
    : "-";

  // 导出 Markdown
  const handleExport = async () => {
    if (!report) return;
    try {
      const res = await exportApi.reportMarkdown({ reportId: report.id });
      setExportJobId(res.exportJobId);
      setExportOpen(true);
      toast.success("导出任务已创建");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "导出失败，请重试";
      toast.error(message);
    }
  };

  // 重新生成
  const handleRegenerate = async () => {
    if (!report) return;
    setRegenerating(true);
    try {
      await reportApi.regenerate(report.id);
      toast.success("报告重新生成中...");
      queryClient.invalidateQueries({ queryKey: ["report", reportId] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "重新生成失败";
      toast.error(message);
    } finally {
      setRegenerating(false);
    }
  };

  // 复制全文
  const handleCopyAll = async () => {
    if (!report?.contentMarkdown) {
      toast.error("报告内容为空");
      return;
    }
    try {
      await navigator.clipboard.writeText(report.contentMarkdown);
      toast.success("已复制报告全文到剪贴板");
    } catch {
      toast.error("复制失败，请手动复制");
    }
  };

  // 归档报告
  const handleArchive = async () => {
    if (!report) return;
    setArchiving(true);
    try {
      await reportApi.archive(report.id);
      toast.success("报告已归档");
      setArchiveOpen(false);
      queryClient.invalidateQueries({ queryKey: ["report", reportId] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "归档失败，请重试";
      toast.error(message);
    } finally {
      setArchiving(false);
    }
  };

  // 打开编辑对话框
  const handleEditOpen = () => {
    if (!report) return;
    setEditTitle(report.title);
    setEditContent(report.contentMarkdown ?? "");
    setEditOpen(true);
  };

  // 保存编辑
  const handleEditSave = async () => {
    if (!report) return;
    if (!editTitle.trim()) {
      toast.error("标题不能为空");
      return;
    }
    setSaving(true);
    try {
      await reportApi.update(report.id, {
        title: editTitle.trim(),
        contentMarkdown: editContent,
      });
      toast.success("报告已保存");
      setEditOpen(false);
      queryClient.invalidateQueries({ queryKey: ["report", reportId] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "保存失败，请重试";
      toast.error(message);
    } finally {
      setSaving(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!report) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <ClipboardList className="mb-4 size-12 text-muted-foreground/50" />
        <p className="text-lg font-medium">报告不存在</p>
        <Button
          variant="outline"
          className="mt-4"
          onClick={() => router.push("/reports")}
        >
          返回报告列表
        </Button>
      </div>
    );
  }

  const isProcessing =
    report.status === "pending" || report.status === "processing";
  const isFailed = report.status === "failed";

  return (
    <div className="space-y-6">
      {/* 返回按钮 */}
      <Button variant="ghost" size="sm" onClick={() => router.push("/reports")}>
        <ArrowLeft className="mr-2 size-4" />
        返回报告列表
      </Button>

      {/* 报告头部信息 */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between">
            <div className="flex-1">
              <div className="mb-2 flex items-center gap-2">
                <ReportTypeBadge reportType={report.reportType} />
                <ReportStatusBadge status={report.status} />
              </div>
              <CardTitle className="text-xl">{report.title}</CardTitle>
              {report.query && (
                <CardDescription className="mt-2">
                  研究问题：{report.query}
                </CardDescription>
              )}
            </div>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={handleExport}
                disabled={isProcessing}
              >
                <FileDown className="mr-1.5 size-3.5" />
                导出Markdown
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={handleEditOpen}
                disabled={isProcessing}
              >
                <Pencil className="mr-1.5 size-3.5" />
                编辑
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={handleRegenerate}
                disabled={regenerating}
              >
                {regenerating ? (
                  <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                ) : (
                  <RefreshCw className="mr-1.5 size-3.5" />
                )}
                重新生成
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={handleCopyAll}
                disabled={isProcessing}
              >
                <Copy className="mr-1.5 size-3.5" />
                复制全文
              </Button>
              {report.status !== "archived" && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setArchiveOpen(true)}
                  disabled={isProcessing}
                >
                  <Archive className="mr-1.5 size-3.5" />
                  归档
                </Button>
              )}
            </div>
          </div>
        </CardHeader>
      </Card>

      {/* 处理中状态提示 */}
      {isProcessing && (
        jobId ? (
          <ReportProgressBar jobId={jobId} />
        ) : (
          <Card>
            <CardContent className="flex items-center gap-3 py-6">
              <Loader2 className="size-5 animate-spin text-blue-600" />
              <div>
                <p className="text-sm font-medium text-blue-700 dark:text-blue-300">
                  报告正在生成中...
                </p>
                <p className="text-xs text-muted-foreground">
                  AI 正在生成报告内容，页面将每 5 秒自动刷新。
                </p>
              </div>
            </CardContent>
          </Card>
        )
      )}

      {/* 失败状态提示 */}
      {isFailed && (
        <Card>
          <CardContent className="flex items-start gap-3 py-6">
            <AlertCircle className="mt-0.5 size-5 text-red-600" />
            <div className="flex-1">
              <p className="text-sm font-medium text-red-700 dark:text-red-300">
                报告生成失败
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                报告生成未能完成，请点击右上角&ldquo;重新生成&rdquo;重试。
              </p>
            </div>
            <Button
              variant="outline"
              size="sm"
              onClick={handleRegenerate}
              disabled={regenerating}
            >
              {regenerating ? (
                <Loader2 className="mr-1.5 size-3.5 animate-spin" />
              ) : (
                <RefreshCw className="mr-1.5 size-3.5" />
              )}
              重试
            </Button>
          </CardContent>
        </Card>
      )}

      {/* 基础信息 */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">基础信息</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 sm:grid-cols-3">
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <ClipboardList className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  报告类型
                </p>
                <p className="mt-0.5">
                  <ReportTypeBadge reportType={report.reportType} />
                </p>
              </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <FolderOpen className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  所属专题
                </p>
                <p className="mt-0.5 text-sm">{topicName}</p>
              </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <CalendarRange className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  时间范围
                </p>
                <p className="mt-0.5 text-sm">
                  {report.startDate || report.endDate
                    ? `${formatDateShort(report.startDate)} ~ ${formatDateShort(report.endDate)}`
                    : "-"}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <Cpu className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  生成模型
                </p>
                <p className="mt-0.5 text-sm">
                  {report.generatedByModel || "-"}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <Clock className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  创建时间
                </p>
                <p className="mt-0.5 text-sm">
                  {formatDate(report.createdAt)}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <Gauge className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  质量评分
                </p>
                <p className="mt-0.5">
                  <QualityScoreBar score={report.qualityScore} />
                </p>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* 正文 + 来源列表 */}
      <div className="grid gap-6 lg:grid-cols-[1fr_320px]">
        {/* 报告正文 */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">报告正文</CardTitle>
          </CardHeader>
          <CardContent>
            {isProcessing ? (
              <div className="flex flex-col items-center justify-center py-16 text-center">
                <Loader2 className="mb-3 size-8 animate-spin text-blue-600" />
                <p className="text-sm text-muted-foreground">
                  报告内容生成中，请稍候...
                </p>
              </div>
            ) : isFailed ? (
              <div className="flex flex-col items-center justify-center py-16 text-center">
                <AlertCircle className="mb-3 size-10 text-red-500" />
                <p className="text-sm text-muted-foreground">
                  报告生成失败，暂无内容
                </p>
              </div>
            ) : report.contentMarkdown ? (
              <div className="rounded-lg border bg-muted/20 p-6">
                <Markdown content={report.contentMarkdown} />
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-16 text-center">
                <FileText className="mb-3 size-10 text-muted-foreground/50" />
                <p className="text-sm text-muted-foreground">
                  暂无报告内容
                </p>
              </div>
            )}
          </CardContent>
        </Card>

        {/* 来源列表 */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">
              来源列表
              {report.citations.length > 0 && (
                <span className="ml-2 text-sm font-normal text-muted-foreground">
                  ({report.citations.length})
                </span>
              )}
            </CardTitle>
          </CardHeader>
          <CardContent>
            {report.citations.length === 0 ? (
              <p className="text-sm text-muted-foreground">暂无来源信息</p>
            ) : (
              <div className="space-y-3">
                {report.citations.map((citation) => (
                  <div
                    key={citation.index}
                    className="rounded-lg border bg-muted/20 p-3 transition-colors hover:bg-muted/40"
                  >
                    <div className="flex items-start gap-3">
                      <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-bold text-primary-foreground">
                        {citation.index}
                      </span>
                      <div className="min-w-0 flex-1">
                        <Link
                          href={`/documents/${citation.documentId}`}
                          className="block truncate text-sm font-medium text-primary hover:underline"
                        >
                          {citation.title || "无标题文档"}
                        </Link>
                        {citation.sourceUrl && (
                          <a
                            href={citation.sourceUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="mt-1 flex items-center gap-1 truncate text-xs text-muted-foreground hover:text-primary"
                          >
                            <ExternalLink className="size-3 shrink-0" />
                            <span className="truncate">{citation.sourceUrl}</span>
                          </a>
                        )}
                        {citation.snippet && (
                          <p className="mt-1.5 line-clamp-3 text-xs leading-relaxed text-muted-foreground">
                            {citation.snippet}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}

            {report.sourceDocumentIds.length > 0 && (
              <>
                <Separator className="my-4" />
                <div>
                  <p className="mb-2 text-xs font-medium text-muted-foreground">
                    引用文档 ID
                  </p>
                  <div className="flex flex-wrap gap-1.5">
                    {report.sourceDocumentIds.map((docId) => (
                      <Link
                        key={docId}
                        href={`/documents/${docId}`}
                        className="rounded bg-muted px-2 py-0.5 font-mono text-xs text-muted-foreground hover:text-primary"
                      >
                        {docId.slice(0, 8)}...
                      </Link>
                    ))}
                  </div>
                </div>
              </>
            )}
          </CardContent>
        </Card>
      </div>

      {/* 导出状态弹窗 */}
      <ExportStatusDialog
        open={exportOpen}
        onOpenChange={setExportOpen}
        exportJobId={exportJobId}
      />

      {/* 编辑报告弹窗 */}
      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent className="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>编辑报告</DialogTitle>
            <DialogDescription>
              修改报告标题和 Markdown 正文内容，保存后将立即生效。
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <div className="space-y-2">
              <Label htmlFor="edit-title">标题</Label>
              <Input
                id="edit-title"
                value={editTitle}
                onChange={(e) => setEditTitle(e.target.value)}
                placeholder="请输入报告标题"
                disabled={saving}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="edit-content">正文（Markdown）</Label>
              <Textarea
                id="edit-content"
                value={editContent}
                onChange={(e) => setEditContent(e.target.value)}
                placeholder="请输入 Markdown 格式的报告正文"
                className="min-h-[400px] font-mono text-sm"
                disabled={saving}
              />
            </div>
          </div>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button onClick={handleEditSave} disabled={saving}>
              {saving && <Loader2 className="mr-2 size-4 animate-spin" />}
              保存
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 归档确认弹窗 */}
      <Dialog open={archiveOpen} onOpenChange={setArchiveOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Archive className="size-5 text-primary" />
              确认归档
            </DialogTitle>
            <DialogDescription>
              归档后该报告将标记为已归档状态。确定要归档此报告吗？
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button onClick={handleArchive} disabled={archiving}>
              {archiving && <Loader2 className="mr-2 size-4 animate-spin" />}
              确认归档
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
