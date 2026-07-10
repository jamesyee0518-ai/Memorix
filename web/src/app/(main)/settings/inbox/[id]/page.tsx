"use client";

import { useState, useEffect, useCallback } from "react";
import { useParams, useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  ArrowLeft,
  Loader2,
  Link2,
  FileText,
  Download,
  RotateCcw,
  Archive,
  Pencil,
  AlertCircle,
  CheckCircle2,
  FolderInput,
  Paperclip,
  Lightbulb,
  History,
  Clock,
  Tag,
  ExternalLink,
} from "lucide-react";
import {
  inboxApi,
  fileApi,
  ApiRequestError,
} from "@/lib/api";
import { useTopicStore } from "@/stores/topic-store";
import type {
  InboxItem,
  InboxAttachment,
  InboxEvent,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  getInboxTypeMeta,
  InboxProcessingHint,
  InboxTypeBadge,
} from "@/components/inbox-type-display";

// ===== 常量 =====

const statusLabels: Record<string, string> = {
  pending: "待处理",
  imported: "已导入",
  processing: "处理中",
  done: "已完成",
  failed: "失败",
  archived: "已归档",
};

function statusBadgeClass(status: string): string {
  switch (status) {
    case "pending":
      return "bg-amber-100 text-amber-700";
    case "imported":
    case "done":
      return "bg-green-100 text-green-700";
    case "processing":
      return "bg-blue-100 text-blue-700";
    case "failed":
      return "bg-red-100 text-red-700";
    case "archived":
      return "bg-slate-100 text-slate-600";
    default:
      return "bg-slate-100 text-slate-600";
  }
}

const eventTypeLabels: Record<string, string> = {
  created: "创建",
  updated: "更新",
  status_changed: "状态变更",
  imported: "导入",
  archived: "归档",
  retried: "重试",
  processing: "处理中",
  failed: "失败",
  suggestion_generated: "建议生成",
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

function formatFileSize(bytes?: number): string {
  if (!bytes && bytes !== 0) return "-";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
}

// ===== 主组件 =====

export default function InboxDetailPage() {
  const params = useParams();
  const router = useRouter();
  const inboxId = params.id as string;
  const { topics, fetchTopics } = useTopicStore();

  const [item, setItem] = useState<InboxItem | null>(null);
  const [attachments, setAttachments] = useState<InboxAttachment[]>([]);
  const [events, setEvents] = useState<InboxEvent[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [downloadingFileId, setDownloadingFileId] = useState<string | null>(null);

  // 编辑弹窗
  const [editOpen, setEditOpen] = useState(false);
  const [editTitle, setEditTitle] = useState("");
  const [editContent, setEditContent] = useState("");
  const [editSourceUrl, setEditSourceUrl] = useState("");
  const [editTopicId, setEditTopicId] = useState("");

  // 导入弹窗
  const [importOpen, setImportOpen] = useState(false);
  const [importTopicId, setImportTopicId] = useState("");

  // 归档确认
  const [archiveOpen, setArchiveOpen] = useState(false);

  // ===== 数据加载 =====

  const fetchDetail = useCallback(async () => {
    setIsLoading(true);
    try {
      const [detail, attachs, evts] = await Promise.all([
        inboxApi.get(inboxId),
        inboxApi.getAttachments(inboxId).catch(() => [] as InboxAttachment[]),
        inboxApi.getEvents(inboxId).catch(() => [] as InboxEvent[]),
      ]);
      setItem(detail);
      setAttachments(attachs);
      setEvents(evts);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载收件箱详情失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  }, [inboxId]);

  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  useEffect(() => {
    fetchDetail();
  }, [fetchDetail]);

  // ===== 操作处理 =====

  const topicNameById = useCallback(
    (topicId?: string): string | undefined => {
      if (!topicId) return undefined;
      return topics.find((t) => t.id === topicId)?.name;
    },
    [topics]
  );

  const handleDownload = async (attachment: InboxAttachment) => {
    if (!attachment.fileId) {
      toast.error("无文件可下载");
      return;
    }
    setDownloadingFileId(attachment.id);
    try {
      const res = await fileApi.getDownloadUrl(attachment.fileId);
      window.open(res.url, "_blank");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "获取下载链接失败";
      toast.error(message);
    } finally {
      setDownloadingFileId(null);
    }
  };

  const handleImport = async () => {
    setActionLoading(true);
    try {
      await inboxApi.import(inboxId, importTopicId || undefined);
      toast.success("已导入到资料库");
      setImportOpen(false);
      setImportTopicId("");
      fetchDetail();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "导入失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleRetry = async () => {
    setActionLoading(true);
    try {
      await inboxApi.retry(inboxId);
      toast.success("已重新提交处理");
      fetchDetail();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "重试失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleArchive = async () => {
    setActionLoading(true);
    try {
      await inboxApi.archive(inboxId);
      toast.success("已归档");
      setArchiveOpen(false);
      router.push("/settings/inbox");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "归档失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const openEditDialog = () => {
    if (!item) return;
    setEditTitle(item.title || "");
    setEditContent(item.contentText || "");
    setEditSourceUrl(item.sourceUrl || "");
    setEditTopicId(item.topicId || "");
    setEditOpen(true);
  };

  const handleEditSave = async () => {
    setActionLoading(true);
    try {
      await inboxApi.update(inboxId, {
        title: editTitle.trim() || undefined,
        contentText: editContent.trim() || undefined,
        sourceUrl: editSourceUrl.trim() || undefined,
        topicId: editTopicId || undefined,
      });
      toast.success("已保存修改");
      setEditOpen(false);
      fetchDetail();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "保存失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  // ===== 渲染 =====

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!item) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <FileText className="mb-4 size-12 text-muted-foreground/50" />
        <p className="text-lg font-medium">收件箱项不存在</p>
        <Button
          variant="outline"
          className="mt-4"
          onClick={() => router.push("/settings/inbox")}
        >
          返回列表
        </Button>
      </div>
    );
  }

  const effectiveType = item.inputType || item.itemType;
  const typeMeta = getInboxTypeMeta({
    inputType: item.inputType,
    itemType: item.itemType,
    fileName: item.fileName || item.title,
  });
  const ItemIcon = typeMeta.Icon;
  const suggestedTopicName = topicNameById(item.suggestedTopicId);
  const currentTopicName = topicNameById(item.topicId);

  return (
    <div className="space-y-6">
      {/* 返回按钮 */}
      <Button
        variant="ghost"
        size="sm"
        onClick={() => router.push("/settings/inbox")}
      >
        <ArrowLeft className="mr-2 size-4" />
        返回列表
      </Button>

      {/* 基本信息卡片 */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between">
            <div className="flex-1">
              <div className="flex items-center gap-2">
                <ItemIcon className={`size-5 ${typeMeta.iconClassName}`} />
                <CardTitle className="text-xl">
                  {item.title || "无标题"}
                </CardTitle>
              </div>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <InboxTypeBadge
                  inputType={item.inputType}
                  itemType={item.itemType}
                  fileName={item.fileName || item.title}
                />
                <Badge className={statusBadgeClass(item.status)}>
                  {statusLabels[item.status] ?? item.status}
                </Badge>
                {currentTopicName && (
                  <Badge variant="outline">{currentTopicName}</Badge>
                )}
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              {item.status !== "imported" && item.status !== "done" && (
                <Button
                  variant="default"
                  size="sm"
                  onClick={() => {
                    setImportTopicId(item.topicId || item.suggestedTopicId || "");
                    setImportOpen(true);
                  }}
                  disabled={actionLoading}
                >
                  <FolderInput className="mr-1.5 size-3.5" />
                  导入到资料
                </Button>
              )}
              {item.status === "failed" && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleRetry}
                  disabled={actionLoading}
                >
                  <RotateCcw className="mr-1.5 size-3.5" />
                  重试
                </Button>
              )}
              <Button
                variant="outline"
                size="sm"
                onClick={openEditDialog}
                disabled={actionLoading}
              >
                <Pencil className="mr-1.5 size-3.5" />
                编辑
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setArchiveOpen(true)}
                disabled={actionLoading}
              >
                <Archive className="mr-1.5 size-3.5" />
                归档
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* 基本信息网格 */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <p className="text-xs font-medium text-muted-foreground">输入类型</p>
              <div className="mt-1">
                <InboxTypeBadge
                  inputType={item.inputType}
                  itemType={item.itemType}
                  fileName={item.fileName || item.title}
                />
              </div>
            </div>
            <div>
              <p className="text-xs font-medium text-muted-foreground">当前状态</p>
              <p className="mt-1">
                <Badge className={statusBadgeClass(item.status)}>
                  {statusLabels[item.status] ?? item.status}
                </Badge>
              </p>
            </div>
            <div>
              <p className="text-xs font-medium text-muted-foreground">创建时间</p>
              <p className="mt-1 text-sm">{formatDate(item.createdAt)}</p>
            </div>
            <div>
              <p className="text-xs font-medium text-muted-foreground">更新时间</p>
              <p className="mt-1 text-sm">{formatDate(item.updatedAt)}</p>
            </div>
            {item.processedAt && (
              <div>
                <p className="text-xs font-medium text-muted-foreground">处理时间</p>
                <p className="mt-1 text-sm">{formatDate(item.processedAt)}</p>
              </div>
            )}
            {item.importedAt && (
              <div>
                <p className="text-xs font-medium text-muted-foreground">导入时间</p>
                <p className="mt-1 text-sm">{formatDate(item.importedAt)}</p>
              </div>
            )}
          </div>

          <div className="rounded-lg border bg-muted/30 p-3">
            <p className="text-sm font-medium">{typeMeta.detail}</p>
            <InboxProcessingHint
              inputType={item.inputType}
              itemType={item.itemType}
              fileName={item.fileName || item.title}
              status={item.status}
              className="mt-1"
            />
          </div>

          {/* 错误信息 */}
          {item.status === "failed" && (item.errorMessage || item.errorCode) && (
            <div className="flex items-start gap-2 rounded-lg bg-red-50 p-3 dark:bg-red-900/20">
              <AlertCircle className="mt-0.5 size-4 shrink-0 text-red-600" />
              <div>
                <p className="text-sm font-medium text-red-700 dark:text-red-400">
                  处理失败
                </p>
                {item.errorCode && (
                  <p className="mt-1 text-xs text-red-600 dark:text-red-400">
                    错误代码：{item.errorCode}
                  </p>
                )}
                {item.errorMessage && (
                  <p className="mt-1 text-xs text-red-600 dark:text-red-400">
                    {item.errorMessage}
                  </p>
                )}
              </div>
            </div>
          )}

          {/* 成功信息 */}
          {(item.status === "imported" || item.status === "done") && (
            <div className="flex items-start gap-2 rounded-lg bg-green-50 p-3 dark:bg-green-900/20">
              <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-green-600" />
              <div>
                <p className="text-sm font-medium text-green-700 dark:text-green-400">
                  处理完成
                </p>
                <p className="mt-1 text-xs text-green-600 dark:text-green-400">
                  资料已成功导入并处理
                </p>
              </div>
            </div>
          )}

          {/* 已导入资料链接 */}
          {item.sourceId && (
            <div className="flex items-center justify-between rounded-lg border bg-muted/30 p-4">
              <div className="flex items-center gap-3">
                <div className="flex size-10 items-center justify-center rounded-lg bg-green-50">
                  <CheckCircle2 className="size-5 text-green-600" />
                </div>
                <div>
                  <p className="text-sm font-medium">已导入为 Source</p>
                  <p className="text-xs text-muted-foreground">
                    资料ID：{item.sourceId}
                  </p>
                </div>
              </div>
              <Button
                variant="outline"
                size="sm"
                onClick={() => router.push(`/sources/${item.sourceId}`)}
              >
                <ExternalLink className="mr-1.5 size-3.5" />
                查看资料详情
              </Button>
            </div>
          )}

          <Separator />

          {/* 文本内容 */}
          {effectiveType === "text" && item.contentText && (
            <div>
              <p className="mb-2 text-xs font-medium text-muted-foreground">
                文本内容
              </p>
              <div className="max-h-96 overflow-y-auto rounded-lg border bg-muted/30 p-4">
                <pre className="whitespace-pre-wrap text-sm">
                  {item.contentText}
                </pre>
              </div>
            </div>
          )}

          {/* URL 链接 */}
          {effectiveType === "url" && item.sourceUrl && (
            <div>
              <p className="mb-2 text-xs font-medium text-muted-foreground">
                来源链接
              </p>
              <a
                href={item.sourceUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="flex items-center gap-1 text-sm text-blue-600 hover:underline"
              >
                <Link2 className="size-4 shrink-0" />
                <span className="break-all">{item.sourceUrl}</span>
              </a>
            </div>
          )}

          {/* 文件信息 */}
          {(effectiveType === "file" ||
            typeMeta.key === "image" ||
            typeMeta.key === "audio" ||
            typeMeta.key === "pdf") && (
            <div>
              <p className="mb-2 text-xs font-medium text-muted-foreground">
                文件信息
              </p>
              <div className="flex items-center justify-between rounded-lg border p-4">
                <div className="flex items-center gap-3">
                  <div className="flex size-10 items-center justify-center rounded-lg bg-muted">
                    <ItemIcon className={`size-5 ${typeMeta.iconClassName}`} />
                  </div>
                  <div>
                    <p className="text-sm font-medium">
                      {item.fileName || "未命名文件"}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {formatFileSize(item.fileSize)}
                      {item.filePath && ` · ${item.filePath}`}
                    </p>
                    <InboxProcessingHint
                      inputType={item.inputType}
                      itemType={item.itemType}
                      fileName={item.fileName || item.title}
                      status={item.status}
                      compact
                      className="mt-1"
                    />
                  </div>
                </div>
                {item.fileId && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() =>
                      handleDownload({
                        id: "main",
                        fileId: item.fileId!,
                        filename: item.fileName || "file",
                        sizeBytes: item.fileSize || 0,
                        mimeType: "",
                        role: "main",
                        inboxItemId: item.id,
                        workspaceId: item.workspaceId,
                        createdAt: item.createdAt,
                      })
                    }
                  >
                    <Download className="mr-1.5 size-3.5" />
                    下载
                  </Button>
                )}
              </div>
            </div>
          )}

          {/* 来源 URL（非 URL 类型也可能有） */}
          {effectiveType !== "url" && item.sourceUrl && (
            <div>
              <p className="mb-2 text-xs font-medium text-muted-foreground">
                来源链接
              </p>
              <a
                href={item.sourceUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="flex items-center gap-1 text-sm text-blue-600 hover:underline"
              >
                <Link2 className="size-4 shrink-0" />
                <span className="break-all">{item.sourceUrl}</span>
              </a>
            </div>
          )}
        </CardContent>
      </Card>

      {/* 附件区域 */}
      {attachments.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Paperclip className="size-4 text-muted-foreground" />
              附件
            </CardTitle>
            <CardDescription>
              共 {attachments.length} 个附件
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {attachments.map((attachment) => {
              const attachmentMeta = getInboxTypeMeta({
                inputType: "file",
                fileName: attachment.filename,
                mimeType: attachment.mimeType,
              });
              const AttachmentIcon = attachmentMeta.Icon;

              return (
                <div
                  key={attachment.id}
                  className="flex items-center justify-between rounded-lg border p-3"
                >
                  <div className="flex items-center gap-3 overflow-hidden">
                    <div className="flex size-9 items-center justify-center rounded-lg bg-muted">
                      <AttachmentIcon className={`size-4 ${attachmentMeta.iconClassName}`} />
                    </div>
                    <div className="overflow-hidden">
                      <p className="truncate text-sm font-medium">
                        {attachment.filename}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {attachmentMeta.label}
                        {" · "}
                        {formatFileSize(attachment.sizeBytes)}
                        {attachment.mimeType && ` · ${attachment.mimeType}`}
                        {" · "}
                        {formatDate(attachment.createdAt)}
                      </p>
                    </div>
                  </div>
                  {attachment.fileId && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleDownload(attachment)}
                      disabled={downloadingFileId === attachment.id}
                    >
                      {downloadingFileId === attachment.id ? (
                        <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                      ) : (
                        <Download className="mr-1.5 size-3.5" />
                      )}
                      下载
                    </Button>
                  )}
                </div>
              );
            })}
          </CardContent>
        </Card>
      )}

      {/* 建议信息 */}
      {(item.suggestedTitle || (item.suggestedTags && item.suggestedTags.length > 0) || item.suggestedTopicId) && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Lightbulb className="size-4 text-amber-500" />
              建议信息
            </CardTitle>
            <CardDescription>
              系统自动生成的分类和处理建议
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {item.suggestedTitle && (
              <div>
                <p className="text-xs font-medium text-muted-foreground">建议标题</p>
                <p className="mt-1 text-sm">{item.suggestedTitle}</p>
              </div>
            )}
            {item.suggestedTags && item.suggestedTags.length > 0 && (
              <div>
                <p className="mb-1 text-xs font-medium text-muted-foreground">建议标签</p>
                <div className="flex flex-wrap gap-1.5">
                  {item.suggestedTags.map((tag, idx) => (
                    <Badge key={idx} variant="secondary">
                      <Tag className="mr-1 size-3" />
                      {tag}
                    </Badge>
                  ))}
                </div>
              </div>
            )}
            {item.suggestedTopicId && (
              <div>
                <p className="text-xs font-medium text-muted-foreground">建议专题</p>
                <div className="mt-1 flex items-center gap-2">
                  <Badge variant="outline">
                    {suggestedTopicName || item.suggestedTopicId}
                  </Badge>
                  {item.topicId !== item.suggestedTopicId && (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={async () => {
                        try {
                          await inboxApi.update(item.id, {
                            topicId: item.suggestedTopicId,
                          });
                          toast.success("已采纳建议专题");
                          fetchDetail();
                        } catch (err) {
                          const message =
                            err instanceof ApiRequestError
                              ? err.message
                              : "采纳失败";
                          toast.error(message);
                        }
                      }}
                    >
                      采纳
                    </Button>
                  )}
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {/* 状态历史 */}
      {events.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <History className="size-4 text-muted-foreground" />
              状态历史
            </CardTitle>
            <CardDescription>
              收件箱项的事件记录时间线
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div className="relative space-y-4 pl-6">
              {/* 时间线竖线 */}
              <div className="absolute left-2 top-1 bottom-1 w-px bg-border" />
              {events.map((evt, idx) => (
                <div key={evt.id} className="relative">
                  {/* 时间线圆点 */}
                  <div
                    className={`absolute -left-[18px] top-1 size-3 rounded-full border-2 border-background ${
                      idx === 0
                        ? "bg-primary"
                        : "bg-muted-foreground/40"
                    }`}
                  />
                  <div className="space-y-0.5">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium">
                        {eventTypeLabels[evt.eventType] ?? evt.eventType}
                      </span>
                      {idx === 0 && (
                        <Badge variant="secondary" className="text-xs">
                          最新
                        </Badge>
                      )}
                    </div>
                    {evt.eventPayload && (
                      <p className="text-xs text-muted-foreground">
                        {evt.eventPayload}
                      </p>
                    )}
                    <div className="flex items-center gap-1 text-xs text-muted-foreground">
                      <Clock className="size-3" />
                      {formatDate(evt.createdAt)}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* 编辑弹窗 */}
      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Pencil className="size-5" />
              编辑收件箱项
            </DialogTitle>
            <DialogDescription>
              修改收件箱项的标题、内容和专题
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="detail-edit-title">标题</Label>
              <Input
                id="detail-edit-title"
                value={editTitle}
                onChange={(e) => setEditTitle(e.target.value)}
                placeholder="请输入标题"
                maxLength={200}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="detail-edit-content">内容</Label>
              <Textarea
                id="detail-edit-content"
                value={editContent}
                onChange={(e) => setEditContent(e.target.value)}
                placeholder="请输入内容文本"
                rows={5}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="detail-edit-url">来源 URL</Label>
              <Input
                id="detail-edit-url"
                value={editSourceUrl}
                onChange={(e) => setEditSourceUrl(e.target.value)}
                placeholder="https://example.com/article"
              />
            </div>
            <div className="space-y-2">
              <Label>所属专题</Label>
              <Select
                value={editTopicId}
                onValueChange={(v) => setEditTopicId(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="无" />
                </SelectTrigger>
                <SelectContent>
                  {topics.map((t) => (
                    <SelectItem key={t.id} value={t.id}>
                      {t.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button onClick={handleEditSave} disabled={actionLoading}>
              {actionLoading && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              保存
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 导入弹窗 */}
      <Dialog open={importOpen} onOpenChange={setImportOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <FolderInput className="size-5" />
              导入到资料库
            </DialogTitle>
            <DialogDescription>
              选择目标专题，将「{item.title || "无标题"}」导入到资料库
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label>目标专题</Label>
            <Select
              value={importTopicId}
              onValueChange={(v) => setImportTopicId(v as string)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="请选择专题" />
              </SelectTrigger>
              <SelectContent>
                {topics.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button onClick={handleImport} disabled={actionLoading}>
              {actionLoading && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              确认导入
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 归档确认弹窗 */}
      <Dialog open={archiveOpen} onOpenChange={setArchiveOpen}>
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>确认归档</DialogTitle>
            <DialogDescription>
              确定要归档收件箱项「{item.title || "无标题"}」吗？归档后将不再显示在默认列表中。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button
              variant="destructive"
              onClick={handleArchive}
              disabled={actionLoading}
            >
              {actionLoading && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              确认归档
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
