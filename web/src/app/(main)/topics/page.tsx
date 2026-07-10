"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  Plus,
  MoreVertical,
  Pencil,
  Trash2,
  FolderOpen,
  Loader2,
  FileText,
  Clock,
  AlertCircle,
} from "lucide-react";
import { useTopicStore } from "@/stores/topic-store";
import { ApiRequestError } from "@/lib/api";
import type { Topic } from "@/lib/types";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
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
import { TopicFormDialog } from "@/components/topic-form-dialog";

function formatDate(dateStr: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleDateString("zh-CN");
}

export default function TopicsPage() {
  const router = useRouter();
  const { topics, isLoading, fetchTopics, deleteTopic } = useTopicStore();
  const [createOpen, setCreateOpen] = useState(false);
  const [editTopic, setEditTopic] = useState<Topic | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Topic | null>(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    fetchTopics().catch(() => {
      toast.error("加载专题列表失败");
    });
  }, [fetchTopics]);

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await deleteTopic(deleteTarget.id);
      toast.success("专题已删除");
      setDeleteTarget(null);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">专题管理</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            管理您的知识研究专题
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="mr-2 size-4" />
          创建专题
        </Button>
      </div>

      {/* 专题列表 */}
      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : topics.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <FolderOpen className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无专题</p>
            <p className="mt-1 text-sm text-muted-foreground">
              创建您的第一个专题，开始管理知识资料
            </p>
            <Button className="mt-4" onClick={() => setCreateOpen(true)}>
              <Plus className="mr-2 size-4" />
              创建专题
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {topics.map((topic) => (
            <Card
              key={topic.id}
              className="cursor-pointer transition-shadow hover:shadow-md"
            >
              <CardHeader>
                <div className="flex items-start justify-between">
                  <div
                    className="flex-1"
                    onClick={() => router.push(`/topics/${topic.id}`)}
                  >
                    <CardTitle className="hover:text-primary">
                      {topic.name}
                    </CardTitle>
                    {topic.domain && (
                      <span className="mt-1 inline-block rounded bg-blue-50 px-2 py-0.5 text-xs text-blue-600">
                        {topic.domain}
                      </span>
                    )}
                  </div>
                  <DropdownMenu>
                    <DropdownMenuTrigger
                      render={
                        <Button variant="ghost" size="icon-sm" type="button" />
                      }
                    >
                      <MoreVertical className="size-4" />
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem
                        onClick={() => setEditTopic(topic)}
                      >
                        <Pencil className="mr-2 size-4" />
                        编辑
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        variant="destructive"
                        onClick={() => setDeleteTarget(topic)}
                      >
                        <Trash2 className="mr-2 size-4" />
                        删除
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </CardHeader>
              <CardContent
                onClick={() => router.push(`/topics/${topic.id}`)}
              >
                <CardDescription className="mb-4 line-clamp-2 min-h-[2.5rem]">
                  {topic.description || "暂无描述"}
                </CardDescription>
                <div className="flex items-center gap-4 text-sm">
                  <span className="flex items-center gap-1 text-muted-foreground">
                    <FileText className="size-3.5" />
                    {topic.documentCount ?? 0} 资料
                  </span>
                  {topic.pendingCount > 0 && (
                    <span className="flex items-center gap-1 text-amber-600">
                      <Clock className="size-3.5" />
                      {topic.pendingCount} 处理中
                    </span>
                  )}
                  {topic.failedCount > 0 && (
                    <span className="flex items-center gap-1 text-red-600">
                      <AlertCircle className="size-3.5" />
                      {topic.failedCount} 失败
                    </span>
                  )}
                </div>
                <p className="mt-3 text-xs text-muted-foreground">
                  创建于 {formatDate(topic.createdAt)}
                </p>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {/* 创建/编辑弹窗 */}
      <TopicFormDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onSuccess={() => fetchTopics().catch(() => {})}
      />
      <TopicFormDialog
        open={!!editTopic}
        onOpenChange={(v) => !v && setEditTopic(null)}
        topic={editTopic}
        onSuccess={() => fetchTopics().catch(() => {})}
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
              确定要删除专题「{deleteTarget?.name}」吗？此操作不可恢复，关联的资料也将被删除。
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
