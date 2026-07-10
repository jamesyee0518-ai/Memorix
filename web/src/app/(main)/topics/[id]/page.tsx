"use client";

import { useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  Upload,
  Pencil,
  Loader2,
  Trash2,
  RotateCcw,
  ExternalLink,
  FileText,
  MoreVertical,
} from "lucide-react";
import { useTopicStore } from "@/stores/topic-store";
import { sourceApi, ApiRequestError } from "@/lib/api";
import type { SourceStatus, SourceType } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
import { Badge } from "@/components/ui/badge";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import { StatusBadge, getSourceTypeLabel } from "@/components/status-badge";
import { ImportDialog } from "@/components/import-dialog";
import { TopicFormDialog } from "@/components/topic-form-dialog";

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

export default function TopicDetailPage() {
  const params = useParams();
  const router = useRouter();
  const queryClient = useQueryClient();
  const topicId = params.id as string;

  const { currentTopic, isLoading: topicLoading, fetchTopic } = useTopicStore();

  const [importOpen, setImportOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    if (topicId) {
      fetchTopic(topicId).catch(() => {
        toast.error("加载专题详情失败");
      });
    }
  }, [topicId, fetchTopic]);

  // 获取资料列表（带筛选）
  const { data: sources, isLoading: sourcesLoading } = useQuery({
    queryKey: ["sources", "topic", topicId, statusFilter, typeFilter],
    queryFn: () =>
      sourceApi.list({
        topicId: topicId,
        status: statusFilter !== "all" ? (statusFilter as SourceStatus) : undefined,
        sourceType: typeFilter !== "all" ? (typeFilter as SourceType) : undefined,
      }),
    enabled: !!topicId,
  });

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await sourceApi.delete(deleteTarget);
      toast.success("资料已删除");
      setDeleteTarget(null);
      queryClient.invalidateQueries({ queryKey: ["sources"] });
      fetchTopic(topicId).catch(() => {});
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    } finally {
      setDeleting(false);
    }
  };

  const handleRetry = async (sourceId: string) => {
    try {
      await sourceApi.retry(sourceId);
      toast.success("已重新提交处理");
      queryClient.invalidateQueries({ queryKey: ["sources"] });
      fetchTopic(topicId).catch(() => {});
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "重试失败";
      toast.error(message);
    }
  };

  const displaySources = sources?.items ?? [];

  return (
    <div className="space-y-6">
      {/* 返回按钮 */}
      <Button
        variant="ghost"
        size="sm"
        onClick={() => router.push("/topics")}
      >
        <ArrowLeft className="mr-2 size-4" />
        返回专题列表
      </Button>

      {/* 专题信息 */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between">
            <div className="flex-1">
              <CardTitle className="text-xl">
                {topicLoading ? (
                  <Loader2 className="size-5 animate-spin" />
                ) : (
                  currentTopic?.name ?? "加载中..."
                )}
              </CardTitle>
              <div className="mt-2 flex items-center gap-2">
                {currentTopic?.domain && (
                  <Badge variant="secondary">{currentTopic.domain}</Badge>
                )}
              </div>
            </div>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" onClick={() => setEditOpen(true)}>
                <Pencil className="mr-1.5 size-3.5" />
                编辑
              </Button>
              <Button size="sm" onClick={() => setImportOpen(true)}>
                <Upload className="mr-1.5 size-3.5" />
                导入资料
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">
            {currentTopic?.description || "暂无描述"}
          </p>
          {currentTopic?.stats && (
            <div className="mt-4 flex gap-6 text-sm">
              <span>
                总资料：<strong>{currentTopic.stats.totalCount ?? 0}</strong>
              </span>
              <span className="text-amber-600">
                处理中：<strong>{currentTopic.stats.pendingCount ?? 0}</strong>
              </span>
              <span className="text-green-600">
                已完成：<strong>{currentTopic.stats.doneCount ?? 0}</strong>
              </span>
              <span className="text-red-600">
                失败：<strong>{currentTopic.stats.failedCount ?? 0}</strong>
              </span>
            </div>
          )}
        </CardContent>
      </Card>

      {/* 资料列表 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>资料列表</CardTitle>
            <div className="flex gap-2">
              <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as string)}>
                <SelectTrigger size="sm" className="w-32">
                  <SelectValue placeholder="状态筛选" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部状态</SelectItem>
                  <SelectItem value="pending">待处理</SelectItem>
                  <SelectItem value="queued">队列中</SelectItem>
                  <SelectItem value="saved">已保存</SelectItem>
                  <SelectItem value="failed">失败</SelectItem>
                  <SelectItem value="archived">已归档</SelectItem>
                </SelectContent>
              </Select>
              <Select value={typeFilter} onValueChange={(v) => setTypeFilter(v as string)}>
                <SelectTrigger size="sm" className="w-32">
                  <SelectValue placeholder="类型筛选" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部类型</SelectItem>
                  <SelectItem value="url">URL</SelectItem>
                  <SelectItem value="text">文本</SelectItem>
                  <SelectItem value="pdf">PDF</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {sourcesLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displaySources.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <FileText className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无资料，点击「导入资料」按钮添加
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>来源类型</TableHead>
                  <TableHead>来源</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>导入时间</TableHead>
                  <TableHead className="w-10">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displaySources.map((source) => (
                  <TableRow key={source.id}>
                    <TableCell className="max-w-xs">
                      <a
                        href={`/sources/${source.id}`}
                        className="truncate font-medium text-primary hover:underline"
                      >
                        {source.title || "未命名"}
                      </a>
                      {source.status === "failed" && source.errorMessage && (
                        <p className="mt-0.5 truncate text-xs text-destructive">
                          {source.errorMessage}
                        </p>
                      )}
                    </TableCell>
                    <TableCell>{getSourceTypeLabel(source.sourceType)}</TableCell>
                    <TableCell className="max-w-xs">
                      {source.sourceType === "url" && source.url ? (
                        <a
                          href={source.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="flex items-center gap-1 truncate text-xs text-blue-600 hover:underline"
                        >
                          <span className="truncate">{source.url}</span>
                          <ExternalLink className="size-3 shrink-0" />
                        </a>
                      ) : source.sourceType === "pdf" ? (
                        <span className="truncate text-xs text-muted-foreground">
                          PDF 文件
                        </span>
                      ) : (
                        <span className="text-xs text-muted-foreground">文本内容</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <StatusBadge status={source.status} />
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(source.createdAt)}
                    </TableCell>
                    <TableCell>
                      <DropdownMenu>
                        <DropdownMenuTrigger
                          render={
                            <Button variant="ghost" size="icon-sm" type="button" />
                          }
                        >
                          <MoreVertical className="size-4" />
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {source.status === "failed" && (
                            <DropdownMenuItem onClick={() => handleRetry(source.id)}>
                              <RotateCcw className="mr-2 size-4" />
                              重试
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleteTarget(source.id)}
                          >
                            <Trash2 className="mr-2 size-4" />
                            删除
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 弹窗 */}
      <ImportDialog
        open={importOpen}
        onOpenChange={setImportOpen}
        defaultTopicId={topicId}
        onSuccess={() => {
          queryClient.invalidateQueries({ queryKey: ["sources"] });
          fetchTopic(topicId).catch(() => {});
        }}
      />
      <TopicFormDialog
        open={editOpen}
        onOpenChange={setEditOpen}
        topic={
          currentTopic
            ? {
                id: currentTopic.id,
                name: currentTopic.name,
                description: currentTopic.description,
                domain: currentTopic.domain,
                documentCount: currentTopic.stats.totalCount,
                pendingCount: currentTopic.stats.pendingCount,
                failedCount: currentTopic.stats.failedCount,
                createdAt: "",
              }
            : null
        }
        onSuccess={() => fetchTopic(topicId).catch(() => {})}
      />

      {/* 删除确认弹窗 */}
      <Dialog
        open={!!deleteTarget}
        onOpenChange={(v) => !v && setDeleteTarget(null)}
      >
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
