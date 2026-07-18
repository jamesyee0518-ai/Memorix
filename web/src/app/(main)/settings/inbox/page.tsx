"use client";

import { useEffect, useState, useCallback, useRef } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  Inbox,
  Loader2,
  Plus,
  RefreshCw,
  Archive,
  Link2,
  FileText,
  Upload,
  X,
  CheckCircle2,
  RotateCcw,
  Pencil,
  Download,
  FolderInput,
  XCircle,
  Trash2,
  Tag,
} from "lucide-react";
import { inboxApi, ApiRequestError } from "@/lib/api";
import { useTopicStore } from "@/stores/topic-store";
import type { InboxItem } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Card,
  CardContent,
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
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { cn } from "@/lib/utils";
import {
  getInboxTypeMeta,
  InboxProcessingHint,
  InboxTypeBadge,
} from "@/components/inbox-type-display";

// ===== 常量 =====

type StatusFilter = "all" | "pending" | "imported" | "processing" | "failed" | "archived";
type TypeFilter = "all" | "text" | "url" | "file" | "image" | "audio" | "mixed";
type SourceFilter = "all" | "desktop" | "mobile" | "web";

const statusFilters: { value: StatusFilter; label: string }[] = [
  { value: "all", label: "全部" },
  { value: "pending", label: "待处理" },
  { value: "imported", label: "已导入" },
  { value: "processing", label: "处理中" },
  { value: "failed", label: "失败" },
  { value: "archived", label: "已归档" },
];

const typeFilters: { value: TypeFilter; label: string }[] = [
  { value: "all", label: "全部" },
  { value: "text", label: "文本" },
  { value: "url", label: "链接" },
  { value: "file", label: "文件" },
  { value: "image", label: "图片" },
  { value: "audio", label: "录音" },
  { value: "mixed", label: "混合" },
];

const sourceFilters: { value: SourceFilter; label: string }[] = [
  { value: "all", label: "全部" },
  { value: "desktop", label: "桌面端" },
  { value: "mobile", label: "手机端" },
  { value: "web", label: "Web端" },
];

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

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

function formatFileSize(bytes?: number): string {
  if (!bytes) return "-";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
}

// ===== 主组件 =====

export default function InboxPage() {
  const router = useRouter();
  const { topics, fetchTopics } = useTopicStore();

  const [items, setItems] = useState<InboxItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [typeFilter, setTypeFilter] = useState<TypeFilter>("all");
  const [topicFilter, setTopicFilter] = useState<string>("all");
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>("all");

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [actionLoading, setActionLoading] = useState(false);

  // 创建弹窗
  const [createOpen, setCreateOpen] = useState(false);
  const [createTab, setCreateTab] = useState<string>("text");
  const [submitting, setSubmitting] = useState(false);

  // 创建 - 文本
  const [textTitle, setTextTitle] = useState("");
  const [textContent, setTextContent] = useState("");
  const [textTopicId, setTextTopicId] = useState("");

  // 创建 - URL
  const [urlTitle, setUrlTitle] = useState("");
  const [urlSource, setUrlSource] = useState("");
  const [urlTopicId, setUrlTopicId] = useState("");

  // 创建 - 文件上传
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileTopicId, setFileTopicId] = useState("");
  const [dragOver, setDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // 编辑弹窗
  const [editTarget, setEditTarget] = useState<InboxItem | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [editContent, setEditContent] = useState("");
  const [editSourceUrl, setEditSourceUrl] = useState("");
  const [editTopicId, setEditTopicId] = useState("");

  // 归档确认
  const [archiveTarget, setArchiveTarget] = useState<InboxItem | null>(null);

  // 导入弹窗（单个）
  const [importTarget, setImportTarget] = useState<InboxItem | null>(null);
  const [importTopicId, setImportTopicId] = useState("");

  // 批量导入弹窗
  const [batchImportOpen, setBatchImportOpen] = useState(false);
  const [batchImportTopicId, setBatchImportTopicId] = useState("");

  // 批量设置专题弹窗
  const [batchSetTopicOpen, setBatchSetTopicOpen] = useState(false);
  const [batchSetTopicId, setBatchSetTopicId] = useState("");

  // 删除确认
  const [deleteTarget, setDeleteTarget] = useState<InboxItem | null>(null);

  // ===== 数据加载 =====

  const fetchItems = useCallback(async () => {
    setIsLoading(true);
    try {
      const params: Record<string, string | undefined> = {};
      if (statusFilter !== "all") params.status = statusFilter;
      if (typeFilter !== "all") params.inputType = typeFilter;
      if (topicFilter !== "all") params.topicId = topicFilter;
      let list = await inboxApi.list(
        Object.keys(params).length > 0 ? params : undefined
      );
      // 后端暂不支持 createdFrom 筛选，客户端过滤
      if (sourceFilter !== "all") {
        list = list.filter((i) => i.createdFrom === sourceFilter);
      }
      setItems(list);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载收件箱列表失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  }, [statusFilter, typeFilter, topicFilter, sourceFilter]);

  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  useEffect(() => {
    fetchItems();
  }, [fetchItems]);

  // ===== 选择相关 =====

  const allSelected = items.length > 0 && selectedIds.size === items.length;
  const someSelected = selectedIds.size > 0 && selectedIds.size < items.length;

  const toggleSelectAll = () => {
    if (allSelected) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(items.map((i) => i.id)));
    }
  };

  const toggleSelect = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const clearSelection = () => setSelectedIds(new Set());

  const topicNameById = useCallback(
    (topicId?: string): string | undefined => {
      if (!topicId) return undefined;
      const topic = topics.find((t) => t.id === topicId);
      return topic?.name;
    },
    [topics]
  );

  // ===== 创建处理 =====

  const resetCreateForm = () => {
    setTextTitle("");
    setTextContent("");
    setTextTopicId("");
    setUrlTitle("");
    setUrlSource("");
    setUrlTopicId("");
    setSelectedFile(null);
    setFileTopicId("");
    setDragOver(false);
  };

  const handleCreateOpenChange = (open: boolean) => {
    setCreateOpen(open);
    if (!open) resetCreateForm();
  };

  const handleFileSelect = useCallback((file: File | undefined) => {
    if (!file) return;
    setSelectedFile(file);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setDragOver(false);
      const file = e.dataTransfer.files?.[0];
      handleFileSelect(file);
    },
    [handleFileSelect]
  );

  const handleFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    handleFileSelect(e.target.files?.[0]);
  };

  const handleCreateText = async () => {
    if (!textContent.trim()) {
      toast.error("请输入内容文本");
      return;
    }
    setSubmitting(true);
    try {
      await inboxApi.createText({
        title: textTitle.trim() || undefined,
        contentText: textContent.trim(),
        topicId: textTopicId || undefined,
      });
      toast.success("已创建文本收件箱项");
      handleCreateOpenChange(false);
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "创建失败";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleCreateUrl = async () => {
    if (!urlSource.trim()) {
      toast.error("请输入 URL");
      return;
    }
    setSubmitting(true);
    try {
      await inboxApi.createUrl({
        sourceUrl: urlSource.trim(),
        title: urlTitle.trim() || undefined,
        topicId: urlTopicId || undefined,
      });
      toast.success("已创建链接收件箱项");
      handleCreateOpenChange(false);
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "创建失败";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleCreateFile = async () => {
    if (!selectedFile) {
      toast.error("请选择文件");
      return;
    }
    setSubmitting(true);
    try {
      await inboxApi.upload(selectedFile, fileTopicId || undefined);
      toast.success("已上传文件到收件箱");
      handleCreateOpenChange(false);
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "上传失败";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  // ===== 单行操作 =====

  const handleImport = async () => {
    if (!importTarget) return;
    setActionLoading(true);
    try {
      await inboxApi.import(importTarget.id, importTopicId || undefined);
      toast.success("已导入到资料库");
      setImportTarget(null);
      setImportTopicId("");
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "导入失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleRetry = async (item: InboxItem) => {
    setActionLoading(true);
    try {
      await inboxApi.retry(item.id);
      toast.success("已重新提交处理");
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "重试失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleArchive = async () => {
    if (!archiveTarget) return;
    setActionLoading(true);
    try {
      await inboxApi.archive(archiveTarget.id);
      toast.success("已归档");
      setArchiveTarget(null);
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "归档失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  // ===== 编辑处理 =====

  const openEditDialog = (item: InboxItem) => {
    setEditTarget(item);
    setEditTitle(item.title || "");
    setEditContent(item.contentText || "");
    setEditSourceUrl(item.sourceUrl || "");
    setEditTopicId(item.topicId || "");
  };

  const handleEditSave = async () => {
    if (!editTarget) return;
    setActionLoading(true);
    try {
      await inboxApi.update(editTarget.id, {
        title: editTitle.trim() || undefined,
        contentText: editContent.trim() || undefined,
        sourceUrl: editSourceUrl.trim() || undefined,
        topicId: editTopicId || undefined,
      });
      toast.success("已保存修改");
      setEditTarget(null);
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "保存失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  // ===== 批量操作 =====

  const handleBatchImport = async () => {
    if (selectedIds.size === 0) return;
    setActionLoading(true);
    try {
      const result = await inboxApi.batchImport(
        Array.from(selectedIds),
        batchImportTopicId || undefined
      );
      toast.success(`已批量导入 ${result.imported} 项`);
      setBatchImportOpen(false);
      setBatchImportTopicId("");
      clearSelection();
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "批量导入失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleBatchArchive = async () => {
    if (selectedIds.size === 0) return;
    setActionLoading(true);
    try {
      const result = await inboxApi.batchArchive(Array.from(selectedIds));
      toast.success(`已批量归档 ${result.archived} 项`);
      clearSelection();
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "批量归档失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleBatchSetTopic = async () => {
    if (selectedIds.size === 0 || !batchSetTopicId) return;
    setActionLoading(true);
    try {
      const ids = Array.from(selectedIds);
      await Promise.all(
        ids.map((id) =>
          inboxApi.update(id, { topicId: batchSetTopicId })
        )
      );
      toast.success(`已为 ${ids.length} 项设置专题`);
      setBatchSetTopicOpen(false);
      setBatchSetTopicId("");
      clearSelection();
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "批量设置专题失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setActionLoading(true);
    try {
      await inboxApi.delete(deleteTarget.id);
      toast.success("已删除");
      setDeleteTarget(null);
      fetchItems();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  // ===== 渲染 =====

  return (
    <div className="space-y-4">
      {/* 页头 */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">收件箱</h2>
          <p className="text-sm text-muted-foreground">
            快速收集与管理待处理的资料
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={fetchItems}>
            <RefreshCw className="mr-2 size-4" />
            刷新
          </Button>
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 size-4" />
            新建
          </Button>
        </div>
      </div>

      {/* 筛选栏 */}
      <div className="flex flex-wrap items-center gap-3">
        {/* 状态筛选 */}
        <div className="flex flex-wrap gap-1">
          {statusFilters.map((filter) => (
            <button
              key={filter.value}
              onClick={() => setStatusFilter(filter.value)}
              className={cn(
                "rounded-lg px-3 py-1.5 text-sm font-medium transition-colors",
                statusFilter === filter.value
                  ? "bg-primary/10 text-primary"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )}
            >
              {filter.label}
            </button>
          ))}
        </div>

        <div className="h-6 w-px bg-border" />

        {/* 类型筛选 */}
        <Select
          value={typeFilter}
          onValueChange={(v) => setTypeFilter(v as TypeFilter)}
        >
          <SelectTrigger className="w-28" size="sm">
            <SelectValue placeholder="类型" />
          </SelectTrigger>
          <SelectContent>
            {typeFilters.map((opt) => (
              <SelectItem key={opt.value} value={opt.value}>
                {opt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        {/* 专题筛选 */}
        <Select
          value={topicFilter}
          onValueChange={(v) => setTopicFilter(v as string)}
        >
          <SelectTrigger className="w-36" size="sm">
            <SelectValue placeholder="专题" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部专题</SelectItem>
            {topics.map((t) => (
              <SelectItem key={t.id} value={t.id}>
                {t.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        {/* 来源筛选 */}
        <Select
          value={sourceFilter}
          onValueChange={(v) => setSourceFilter(v as SourceFilter)}
        >
          <SelectTrigger className="w-28" size="sm">
            <SelectValue placeholder="来源" />
          </SelectTrigger>
          <SelectContent>
            {sourceFilters.map((opt) => (
              <SelectItem key={opt.value} value={opt.value}>
                {opt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        {/* 统计 */}
        <div className="ml-auto text-xs text-muted-foreground">
          共 {items.length} 项
          {selectedIds.size > 0 && `，已选 ${selectedIds.size} 项`}
        </div>
      </div>

      {/* 批量操作栏 */}
      {selectedIds.size > 0 && (
        <div className="flex items-center gap-2 rounded-lg border bg-muted/50 px-4 py-2">
          <span className="text-sm font-medium">
            已选择 {selectedIds.size} 项
          </span>
          <div className="ml-auto flex items-center gap-2">
            <Button
              size="sm"
              variant="default"
              onClick={() => setBatchImportOpen(true)}
              disabled={actionLoading}
            >
              <Download className="mr-1.5 size-3.5" />
              批量导入
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => setBatchSetTopicOpen(true)}
              disabled={actionLoading}
            >
              <Tag className="mr-1.5 size-3.5" />
              批量设置专题
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={handleBatchArchive}
              disabled={actionLoading}
            >
              <Archive className="mr-1.5 size-3.5" />
              批量归档
            </Button>
            <Button
              size="sm"
              variant="ghost"
              onClick={clearSelection}
              disabled={actionLoading}
            >
              <XCircle className="mr-1.5 size-3.5" />
              取消选择
            </Button>
          </div>
        </div>
      )}

      {/* 列表 */}
      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : items.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Inbox className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">收件箱为空</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {statusFilter === "all" && typeFilter === "all" && topicFilter === "all"
                ? "创建第一个收件箱项，快速收集资料"
                : "当前筛选条件下没有收件箱项"}
            </p>
            <Button className="mt-4" onClick={() => setCreateOpen(true)}>
              <Plus className="mr-2 size-4" />
              新建收件箱项
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-3">
          {/* 全选行 */}
          <div className="flex items-center gap-2 px-1">
            <Checkbox
              checked={allSelected}
              indeterminate={someSelected}
              onCheckedChange={toggleSelectAll}
            />
            <span className="text-xs text-muted-foreground">
              {allSelected ? "取消全选" : "全选"}
            </span>
          </div>

          {items.map((item) => {
            const effectiveType = item.inputType || item.itemType;
            const typeMeta = getInboxTypeMeta({
              inputType: item.inputType,
              itemType: item.itemType,
              fileName: item.fileName || item.title,
            });
            const ItemIcon = typeMeta.Icon;
            const isSelected = selectedIds.has(item.id);
            const topicName = topicNameById(item.topicId);
            return (
              <Card key={item.id} className={cn(isSelected && "ring-2 ring-primary/40")}>
                <CardContent className="p-4">
                  <div className="flex items-start gap-3">
                    {/* 选择框 */}
                    <div className="pt-0.5">
                      <Checkbox
                        checked={isSelected}
                        onCheckedChange={() => toggleSelect(item.id)}
                      />
                    </div>

                    {/* 主要内容 */}
                    <button
                      type="button"
                      className="min-w-0 flex-1 space-y-1.5 text-left"
                      onClick={() => router.push(`/settings/inbox/${item.id}`)}
                    >
                      {/* 标题行 */}
                      <div className="flex items-center gap-2">
                        <ItemIcon className={cn("size-4 shrink-0", typeMeta.iconClassName)} />
                        <h3 className="truncate text-sm font-medium hover:text-primary">
                          {item.title || "无标题"}
                        </h3>
                      </div>

                      {/* 标签行 */}
                      <div className="flex flex-wrap items-center gap-2">
                        <InboxTypeBadge
                          inputType={item.inputType}
                          itemType={item.itemType}
                          fileName={item.fileName || item.title}
                        />
                        <Badge className={statusBadgeClass(item.status)}>
                          {statusLabels[item.status] ?? item.status}
                        </Badge>
                        {topicName && (
                          <Badge variant="outline">{topicName}</Badge>
                        )}
                        {item.createdFrom && (
                          <span className="text-xs text-muted-foreground">
                            来源：{item.createdFrom}
                          </span>
                        )}
                      </div>

                      <InboxProcessingHint
                        inputType={item.inputType}
                        itemType={item.itemType}
                        fileName={item.fileName || item.title}
                        status={item.status}
                        compact
                      />

                      {/* 内容预览 */}
                      {item.contentText && (
                        <p className="line-clamp-2 text-xs text-muted-foreground">
                          {item.contentText}
                        </p>
                      )}

                      {/* 来源链接 */}
                      {item.sourceUrl && (
                        <a
                          href={item.sourceUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="inline-flex items-center gap-1 text-xs text-blue-600 hover:underline"
                          onClick={(e) => e.stopPropagation()}
                        >
                          <Link2 className="size-3" />
                          <span className="truncate">{item.sourceUrl}</span>
                        </a>
                      )}

                      {/* 文件信息 */}
                      {(effectiveType === "file" || typeMeta.key === "image" || typeMeta.key === "audio" || typeMeta.key === "pdf") && item.fileName && (
                        <p className="text-xs text-muted-foreground">
                          文件：{item.fileName}
                          {item.fileSize && ` (${formatFileSize(item.fileSize)})`}
                        </p>
                      )}

                      {/* 错误信息 */}
                      {item.status === "failed" && item.errorMessage && (
                        <p className="text-xs text-red-600">
                          错误：{item.errorMessage}
                        </p>
                      )}

                      {/* 时间 */}
                      <p className="text-xs text-muted-foreground">
                        创建时间：{formatDate(item.createdAt)}
                      </p>
                    </button>

                    {/* 操作按钮 */}
                    <div className="flex shrink-0 flex-wrap items-center justify-end gap-1">
                      {item.status !== "imported" && item.status !== "done" && (
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={actionLoading}
                          onClick={() => {
                            setImportTarget(item);
                            setImportTopicId(item.topicId || "");
                          }}
                        >
                          <Download className="mr-1 size-3.5" />
                          导入
                        </Button>
                      )}
                      {item.status === "failed" && (
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={actionLoading}
                          onClick={() => handleRetry(item)}
                        >
                          <RotateCcw className="mr-1 size-3.5" />
                          重试
                        </Button>
                      )}
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={actionLoading}
                        onClick={() => openEditDialog(item)}
                      >
                        <Pencil className="mr-1 size-3.5" />
                        编辑
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={actionLoading}
                        onClick={() => setArchiveTarget(item)}
                      >
                        <Archive className="mr-1 size-3.5" />
                        归档
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={actionLoading}
                        onClick={() => setDeleteTarget(item)}
                      >
                        <Trash2 className="mr-1 size-3.5" />
                        删除
                      </Button>
                    </div>
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}

      {/* 创建弹窗 */}
      <Dialog open={createOpen} onOpenChange={handleCreateOpenChange}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Plus className="size-5" />
              新建收件箱项
            </DialogTitle>
            <DialogDescription>
              快速收集资料到收件箱，稍后处理
            </DialogDescription>
          </DialogHeader>

          <Tabs
            value={createTab}
            onValueChange={(v) => setCreateTab(v as string)}
          >
            <TabsList className="w-full">
              <TabsTrigger value="text" className="flex-1">
                <FileText className="mr-1 size-3.5" />
                文本
              </TabsTrigger>
              <TabsTrigger value="url" className="flex-1">
                <Link2 className="mr-1 size-3.5" />
                链接
              </TabsTrigger>
              <TabsTrigger value="file" className="flex-1">
                <Upload className="mr-1 size-3.5" />
                文件上传
              </TabsTrigger>
            </TabsList>

            {/* 文本 Tab */}
            <TabsContent value="text" className="mt-4 space-y-4">
              <div className="space-y-2">
                <Label htmlFor="create-text-title">标题（可选）</Label>
                <Input
                  id="create-text-title"
                  value={textTitle}
                  onChange={(e) => setTextTitle(e.target.value)}
                  placeholder="请输入标题"
                  maxLength={200}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="create-text-content">内容</Label>
                <Textarea
                  id="create-text-content"
                  value={textContent}
                  onChange={(e) => setTextContent(e.target.value)}
                  placeholder="请输入或粘贴文本内容"
                  rows={5}
                />
              </div>
              <div className="space-y-2">
                <Label>所属专题（可选）</Label>
                <Select
                  value={textTopicId}
                  onValueChange={(v) => setTextTopicId(v as string)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="稍后在收件箱中分类" />
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
              <Button
                className="w-full"
                onClick={handleCreateText}
                disabled={submitting}
              >
                {submitting && (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                )}
                创建文本
              </Button>
            </TabsContent>

            {/* URL Tab */}
            <TabsContent value="url" className="mt-4 space-y-4">
              <div className="space-y-2">
                <Label htmlFor="create-url-source">URL 地址</Label>
                <Input
                  id="create-url-source"
                  value={urlSource}
                  onChange={(e) => setUrlSource(e.target.value)}
                  placeholder="https://example.com/article"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="create-url-title">标题（可选）</Label>
                <Input
                  id="create-url-title"
                  value={urlTitle}
                  onChange={(e) => setUrlTitle(e.target.value)}
                  placeholder="自定义标题"
                  maxLength={200}
                />
              </div>
              <div className="space-y-2">
                <Label>所属专题（可选）</Label>
                <Select
                  value={urlTopicId}
                  onValueChange={(v) => setUrlTopicId(v as string)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="稍后在收件箱中分类" />
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
              <Button
                className="w-full"
                onClick={handleCreateUrl}
                disabled={submitting}
              >
                {submitting && (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                )}
                创建链接
              </Button>
            </TabsContent>

            {/* 文件上传 Tab */}
            <TabsContent value="file" className="mt-4 space-y-4">
              <div className="space-y-2">
                <Label>文件</Label>
                <input
                  ref={fileInputRef}
                  type="file"
                  className="hidden"
                  onChange={handleFileInputChange}
                />
                {selectedFile ? (
                  <div className="flex items-center justify-between rounded-lg border bg-muted/50 p-3">
                    <div className="flex items-center gap-2 overflow-hidden">
                      <CheckCircle2 className="size-5 shrink-0 text-green-500" />
                      <div className="overflow-hidden">
                        <p className="truncate text-sm font-medium">
                          {selectedFile.name}
                        </p>
                        <p className="text-xs text-muted-foreground">
                          {formatFileSize(selectedFile.size)}
                        </p>
                      </div>
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => setSelectedFile(null)}
                    >
                      <X className="size-4" />
                    </Button>
                  </div>
                ) : (
                  <div
                    onClick={() => fileInputRef.current?.click()}
                    onDragOver={(e) => {
                      e.preventDefault();
                      setDragOver(true);
                    }}
                    onDragLeave={() => setDragOver(false)}
                    onDrop={handleDrop}
                    className={cn(
                      "flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed p-8 transition-colors",
                      dragOver
                        ? "border-primary bg-primary/5"
                        : "border-muted-foreground/30 hover:border-primary/50"
                    )}
                  >
                    <Upload className="size-8 text-muted-foreground" />
                    <div className="text-center">
                      <p className="text-sm font-medium">
                        点击或拖拽文件到此处
                      </p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        支持各类文档文件
                      </p>
                    </div>
                  </div>
                )}
              </div>
              <div className="space-y-2">
                <Label>所属专题（可选）</Label>
                <Select
                  value={fileTopicId}
                  onValueChange={(v) => setFileTopicId(v as string)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="稍后在收件箱中分类" />
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
              <Button
                className="w-full"
                onClick={handleCreateFile}
                disabled={submitting || !selectedFile}
              >
                {submitting && (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                )}
                上传文件
              </Button>
            </TabsContent>
          </Tabs>
        </DialogContent>
      </Dialog>

      {/* 编辑弹窗 */}
      <Dialog
        open={!!editTarget}
        onOpenChange={(v) => !v && setEditTarget(null)}
      >
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
              <Label htmlFor="edit-title">标题</Label>
              <Input
                id="edit-title"
                value={editTitle}
                onChange={(e) => setEditTitle(e.target.value)}
                placeholder="请输入标题"
                maxLength={200}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="edit-content">内容</Label>
              <Textarea
                id="edit-content"
                value={editContent}
                onChange={(e) => setEditContent(e.target.value)}
                placeholder="请输入内容文本"
                rows={5}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="edit-url">来源 URL</Label>
              <Input
                id="edit-url"
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

      {/* 单个导入弹窗 */}
      <Dialog
        open={!!importTarget}
        onOpenChange={(v) => !v && setImportTarget(null)}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <FolderInput className="size-5" />
              导入到资料库
            </DialogTitle>
            <DialogDescription>
              选择目标专题，将「{importTarget?.title || "无标题"}」导入到资料库
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

      {/* 批量导入弹窗 */}
      <Dialog
        open={batchImportOpen}
        onOpenChange={setBatchImportOpen}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Download className="size-5" />
              批量导入到资料库
            </DialogTitle>
            <DialogDescription>
              将选中的 {selectedIds.size} 项导入到资料库，可选择目标专题
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label>目标专题（可选）</Label>
            <Select
              value={batchImportTopicId}
              onValueChange={(v) => setBatchImportTopicId(v as string)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="不指定专题" />
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
            <Button onClick={handleBatchImport} disabled={actionLoading}>
              {actionLoading && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              确认批量导入
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 归档确认 */}
      <Dialog
        open={!!archiveTarget}
        onOpenChange={(v) => !v && setArchiveTarget(null)}
      >
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>确认归档</DialogTitle>
            <DialogDescription>
              确定要归档收件箱项「{archiveTarget?.title || "无标题"}」吗？归档后将不再显示在默认列表中。
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

      {/* 批量设置专题弹窗 */}
      <Dialog
        open={batchSetTopicOpen}
        onOpenChange={setBatchSetTopicOpen}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Tag className="size-5" />
              批量设置专题
            </DialogTitle>
            <DialogDescription>
              将选中的 {selectedIds.size} 项设置到指定专题
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label>目标专题</Label>
            <Select
              value={batchSetTopicId}
              onValueChange={(v) => setBatchSetTopicId(v as string)}
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
            <Button
              onClick={handleBatchSetTopic}
              disabled={actionLoading || !batchSetTopicId}
            >
              {actionLoading && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              确认设置
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 删除确认 */}
      <Dialog
        open={!!deleteTarget}
        onOpenChange={(v) => !v && setDeleteTarget(null)}
      >
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Trash2 className="size-5 text-destructive" />
              确认删除
            </DialogTitle>
            <DialogDescription>
              确定要删除这条收件箱记录吗？此操作不可撤销。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button
              variant="destructive"
              onClick={handleDelete}
              disabled={actionLoading}
            >
              {actionLoading && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              确认删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
