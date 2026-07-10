"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import {
  Plus,
  MoreVertical,
  Ban,
  Trash2,
  KeyRound,
  Loader2,
  Eye,
  EyeOff,
} from "lucide-react";
import { apiKeyApi, ApiRequestError } from "@/lib/api";
import { useTopicStore } from "@/stores/topic-store";
import type { ApiKeyListItem, PermissionScope } from "@/lib/types";
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
import { ApiKeyCreateDialog } from "@/components/api-key-create-dialog";

const scopeLabels: Record<PermissionScope, string> = {
  search_only: "仅搜索",
  qa_only: "仅问答",
  full_read: "完整读取",
};

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

export default function ApiKeysPage() {
  const { topics, fetchTopics } = useTopicStore();
  const [apiKeys, setApiKeys] = useState<ApiKeyListItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [disableTarget, setDisableTarget] = useState<ApiKeyListItem | null>(
    null
  );
  const [deleteTarget, setDeleteTarget] = useState<ApiKeyListItem | null>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set());

  const fetchApiKeys = async () => {
    setIsLoading(true);
    try {
      const list = await apiKeyApi.list();
      setApiKeys(list);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载 API Key 列表失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchApiKeys();
    if (topics.length === 0) {
      fetchTopics().catch(() => {});
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const handleDisable = async () => {
    if (!disableTarget) return;
    setActionLoading(true);
    try {
      await apiKeyApi.disable(disableTarget.id);
      toast.success("API Key 已禁用");
      setDisableTarget(null);
      fetchApiKeys();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "禁用失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setActionLoading(true);
    try {
      await apiKeyApi.delete(deleteTarget.id);
      toast.success("API Key 已删除");
      setDeleteTarget(null);
      fetchApiKeys();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    } finally {
      setActionLoading(false);
    }
  };

  const toggleTopics = (id: string) => {
    setExpandedTopics((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const getTopicNames = (topicIds?: string[]): string[] => {
    if (!topicIds || topicIds.length === 0) return [];
    return topics
      .filter((t) => topicIds.includes(t.id))
      .map((t) => t.name);
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">API Key 管理</h2>
          <p className="text-sm text-muted-foreground">
            管理用于 Agent API 调用的密钥
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="mr-2 size-4" />
          创建 API Key
        </Button>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : apiKeys.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <KeyRound className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无 API Key</p>
            <p className="mt-1 text-sm text-muted-foreground">
              创建您的第一个 API Key，开始使用 Agent API
            </p>
            <Button className="mt-4" onClick={() => setCreateOpen(true)}>
              <Plus className="mr-2 size-4" />
              创建 API Key
            </Button>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>前缀</TableHead>
                <TableHead>权限范围</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>允许专题</TableHead>
                <TableHead>最后使用</TableHead>
                <TableHead>创建时间</TableHead>
                <TableHead>过期时间</TableHead>
                <TableHead className="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {apiKeys.map((key) => {
                const topicNames = getTopicNames(key.allowedTopicIds);
                const expanded = expandedTopics.has(key.id);
                return (
                  <TableRow key={key.id}>
                    <TableCell className="font-medium">{key.name}</TableCell>
                    <TableCell>
                      <code className="font-mono text-xs">
                        {key.keyPrefix}...
                      </code>
                    </TableCell>
                    <TableCell>
                      <Badge variant="secondary">
                        {scopeLabels[key.permissionScope] ?? key.permissionScope}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {key.status === "active" ? (
                        <Badge className="bg-green-100 text-green-700">
                          启用
                        </Badge>
                      ) : (
                        <Badge variant="secondary">已禁用</Badge>
                      )}
                    </TableCell>
                    <TableCell>
                      {key.allowedTopicIds && key.allowedTopicIds.length > 0 ? (
                        <div className="flex items-center gap-1">
                          <span className="text-xs">
                            {expanded
                              ? topicNames.join("、")
                              : `${key.allowedTopicIds.length} 个专题`}
                          </span>
                          <Button
                            variant="ghost"
                            size="icon-xs"
                            onClick={() => toggleTopics(key.id)}
                          >
                            {expanded ? (
                              <EyeOff className="size-3.5" />
                            ) : (
                              <Eye className="size-3.5" />
                            )}
                          </Button>
                        </div>
                      ) : (
                        <span className="text-xs text-muted-foreground">
                          全部
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(key.lastUsedAt)}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(key.createdAt)}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(key.expiresAt)}
                    </TableCell>
                    <TableCell className="text-right">
                      <DropdownMenu>
                        <DropdownMenuTrigger
                          render={
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              type="button"
                            />
                          }
                        >
                          <MoreVertical className="size-4" />
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {key.status === "active" && (
                            <DropdownMenuItem
                              onClick={() => setDisableTarget(key)}
                            >
                              <Ban className="mr-2 size-4" />
                              禁用
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleteTarget(key)}
                          >
                            <Trash2 className="mr-2 size-4" />
                            删除
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      {/* 创建弹窗 */}
      <ApiKeyCreateDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onSuccess={fetchApiKeys}
      />

      {/* 禁用确认 */}
      <Dialog
        open={!!disableTarget}
        onOpenChange={(v) => !v && setDisableTarget(null)}
      >
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>确认禁用</DialogTitle>
            <DialogDescription>
              确定要禁用 API Key「{disableTarget?.name}」吗？禁用后使用该 Key 的请求将被拒绝。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button
              variant="destructive"
              onClick={handleDisable}
              disabled={actionLoading}
            >
              {actionLoading && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              确认禁用
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
            <DialogTitle>确认删除</DialogTitle>
            <DialogDescription>
              确定要删除 API Key「{deleteTarget?.name}」吗？此操作不可恢复。
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
              删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
