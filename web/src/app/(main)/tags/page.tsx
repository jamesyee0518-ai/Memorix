"use client";

import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Tags, Loader2, Search, Plus, Pencil, Archive } from "lucide-react";
import { tagApi, ApiRequestError } from "@/lib/api";
import type { Tag } from "@/lib/types";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
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
import { TagBadge } from "@/components/ai-badge";

// ===== 标签类型配置 =====

const TAG_TYPES = [
  "category",
  "topic",
  "sentiment",
  "keyword",
  "industry",
  "custom",
] as const;

const tagTypeLabels: Record<string, string> = {
  category: "分类",
  topic: "主题",
  sentiment: "情感",
  keyword: "关键词",
  industry: "行业",
  custom: "自定义",
};

function getTagTypeLabel(type?: string): string {
  if (!type) return "自定义";
  return tagTypeLabels[type] ?? type;
}

const sourceLabels: Record<string, string> = {
  manual: "手动",
  ai: "AI",
  user: "用户",
  system: "系统",
};

function getSourceLabel(source?: string): string {
  if (!source) return "-";
  return sourceLabels[source] ?? source;
}

function getStatusBadge(tag: Tag) {
  if (tag.isArchived) {
    return (
      <Badge variant="outline" className="border-transparent bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-300">
        已归档
      </Badge>
    );
  }
  if (tag.isSystem) {
    return (
      <Badge variant="outline" className="border-transparent bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300">
        系统
      </Badge>
    );
  }
  return (
    <Badge variant="outline" className="border-transparent bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300">
      活跃
    </Badge>
  );
}

// ===== 标签表单弹窗 =====

interface TagFormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  tag?: Tag | null;
}

function TagFormDialog({ open, onOpenChange, tag }: TagFormDialogProps) {
  const queryClient = useQueryClient();
  const isEdit = !!tag;
  const [submitting, setSubmitting] = useState(false);
  const [name, setName] = useState("");
  const [type, setType] = useState<string>("custom");
  const [color, setColor] = useState<string>("#6366f1");
  const [description, setDescription] = useState<string>("");

  useEffect(() => {
    if (open) {
      setName(tag?.name ?? "");
      setType(tag?.tagType ?? tag?.type ?? "custom");
      setColor(tag?.color ?? "#6366f1");
      setDescription(tag?.description ?? "");
    }
  }, [open, tag]);

  const createMutation = useMutation({
    mutationFn: (data: { name: string; type?: string; description?: string; color?: string }) =>
      tagApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: { name?: string; description?: string; color?: string } }) =>
      tagApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });

  const handleSubmit = async () => {
    if (!name.trim()) {
      toast.error("请输入标签名称");
      return;
    }
    setSubmitting(true);
    try {
      if (isEdit && tag) {
        await updateMutation.mutateAsync({
          id: tag.id,
          data: {
            name: name.trim(),
            description: description.trim() || undefined,
            color,
          },
        });
        toast.success("标签更新成功");
      } else {
        await createMutation.mutateAsync({
          name: name.trim(),
          type,
          description: description.trim() || undefined,
          color,
        });
        toast.success("标签创建成功");
      }
      onOpenChange(false);
    } catch (err) {
      const message =
        err instanceof ApiRequestError
          ? err.message
          : isEdit
            ? "更新失败，请重试"
            : "创建失败，请重试";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{isEdit ? "编辑标签" : "创建标签"}</DialogTitle>
          <DialogDescription>
            {isEdit ? "修改标签信息" : "创建一个新的标签用于文档分类"}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="tag-name">标签名称</Label>
            <Input
              id="tag-name"
              placeholder="例如：人工智能"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          {!isEdit && (
            <div className="space-y-2">
              <Label htmlFor="tag-type">标签类型</Label>
              <Select value={type} onValueChange={(v) => setType(v as string)}>
                <SelectTrigger id="tag-type" className="w-full">
                  <SelectValue placeholder="选择类型" />
                </SelectTrigger>
                <SelectContent>
                  {TAG_TYPES.map((t) => (
                    <SelectItem key={t} value={t}>
                      {getTagTypeLabel(t)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
          <div className="space-y-2">
            <Label htmlFor="tag-color">颜色</Label>
            <div className="flex items-center gap-2">
              <input
                id="tag-color"
                type="color"
                value={color}
                onChange={(e) => setColor(e.target.value)}
                className="h-8 w-12 cursor-pointer rounded border border-input bg-background p-1"
              />
              <Input
                value={color}
                onChange={(e) => setColor(e.target.value)}
                className="w-32 font-mono text-sm"
                placeholder="#6366f1"
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="tag-desc">描述（可选）</Label>
            <Textarea
              id="tag-desc"
              placeholder="简要描述该标签的用途"
              rows={3}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>
        </div>
        <DialogFooter className="mt-6">
          <DialogClose render={<Button variant="outline" type="button" />}>
            取消
          </DialogClose>
          <Button type="button" onClick={handleSubmit} disabled={submitting}>
            {submitting && <Loader2 className="mr-2 size-4 animate-spin" />}
            {isEdit ? "保存" : "创建"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ===== 标签管理页面 =====

export default function TagsPage() {
  const queryClient = useQueryClient();
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingTag, setEditingTag] = useState<Tag | null>(null);

  const { data: tags, isLoading } = useQuery({
    queryKey: ["tags", typeFilter],
    queryFn: () =>
      tagApi.list({
        type: typeFilter !== "all" ? typeFilter : undefined,
      }),
  });

  const archiveMutation = useMutation({
    mutationFn: (id: string) => tagApi.update(id, { isArchived: true }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });

  const handleSearch = () => {
    setSearch(searchInput);
  };

  const handleCreate = () => {
    setEditingTag(null);
    setDialogOpen(true);
  };

  const handleEdit = (tag: Tag) => {
    setEditingTag(tag);
    setDialogOpen(true);
  };

  const handleArchive = async (tag: Tag) => {
    if (!confirm(`确定要归档标签「${tag.name}」吗？归档后该标签将不再显示在列表中。`)) {
      return;
    }
    try {
      await archiveMutation.mutateAsync(tag.id);
      toast.success("标签已归档");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "归档失败，请重试";
      toast.error(message);
    }
  };

  const allTags = tags ?? [];
  const displayTags = search
    ? allTags.filter((t) =>
        t.name.toLowerCase().includes(search.toLowerCase())
      )
    : allTags;

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">标签管理</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            管理文档标签，用于分类与检索
          </p>
        </div>
        <Button onClick={handleCreate}>
          <Plus className="mr-1 size-4" />
          新建标签
        </Button>
      </div>

      {/* 筛选器 + 列表 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>标签列表</CardTitle>
            <div className="flex gap-2">
              <div className="relative">
                <Input
                  placeholder="搜索标签..."
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  onKeyDown={(e) => e.key === "Enter" && handleSearch()}
                  className="w-48 pr-8"
                />
                <Search className="absolute right-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              </div>
              <Select
                value={typeFilter}
                onValueChange={(v) => setTypeFilter(v as string)}
              >
                <SelectTrigger size="sm" className="w-32">
                  <SelectValue placeholder="类型筛选" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部类型</SelectItem>
                  {TAG_TYPES.map((t) => (
                    <SelectItem key={t} value={t}>
                      {getTagTypeLabel(t)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displayTags.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <Tags className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                {search || typeFilter !== "all"
                  ? "没有符合条件的标签"
                  : "暂无标签，点击右上角「新建标签」开始创建"}
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>类型</TableHead>
                  <TableHead>使用次数</TableHead>
                  <TableHead>文档数</TableHead>
                  <TableHead>来源</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayTags.map((tag) => (
                  <TableRow key={tag.id}>
                    <TableCell className="font-medium">
                      <div className="flex items-center gap-2">
                        {tag.color && (
                          <span
                            className="inline-block size-3 shrink-0 rounded-full border border-black/10"
                            style={{ backgroundColor: tag.color }}
                          />
                        )}
                        {tag.name}
                      </div>
                    </TableCell>
                    <TableCell>
                      <TagBadge
                        name={getTagTypeLabel(tag.tagType ?? tag.type)}
                        type={tag.tagType ?? tag.type}
                      />
                    </TableCell>
                    <TableCell>
                      <span className="font-medium">
                        {tag.usageCount ?? 0}
                      </span>
                    </TableCell>
                    <TableCell>
                      <span className="font-medium">
                        {tag.documentCount ?? 0}
                      </span>
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {getSourceLabel(tag.source)}
                    </TableCell>
                    <TableCell>{getStatusBadge(tag)}</TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          onClick={() => handleEdit(tag)}
                          title="编辑"
                        >
                          <Pencil className="size-3.5" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          onClick={() => handleArchive(tag)}
                          disabled={tag.isSystem}
                          title="归档"
                        >
                          <Archive className="size-3.5" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 创建/编辑弹窗 */}
      <TagFormDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        tag={editingTag}
      />
    </div>
  );
}
