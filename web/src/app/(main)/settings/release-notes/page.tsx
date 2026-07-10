"use client";

import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Plus,
  Rocket,
  Loader2,
  ChevronDown,
  ChevronRight,
  CheckCircle2,
  Eye,
  EyeOff,
  Sparkles,
  AlertTriangle,
  Calendar,
} from "lucide-react";
import { releaseNoteApi, ApiRequestError } from "@/lib/api";
import type {
  ReleaseNote,
  ReleaseNoteChannel,
  ReleaseNoteInput,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
} from "@/components/ui/card";
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Markdown } from "@/components/markdown";

// ===== 配置映射 =====

const channelConfig: Record<
  ReleaseNoteChannel,
  { label: string; className: string }
> = {
  alpha: { label: "Alpha", className: "bg-red-100 text-red-700" },
  beta: { label: "Beta", className: "bg-purple-100 text-purple-700" },
  rc: { label: "RC", className: "bg-orange-100 text-orange-700" },
  stable: { label: "Stable", className: "bg-green-100 text-green-700" },
};

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

export default function ReleaseNotesPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<ReleaseNote | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // 获取版本列表
  const { data: notes, isLoading } = useQuery({
    queryKey: ["release-notes"],
    queryFn: () => releaseNoteApi.list(),
  });

  // 发布
  const publishMutation = useMutation({
    mutationFn: (id: string) => releaseNoteApi.publish(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["release-notes"] });
      toast.success("版本已发布");
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "发布失败";
      toast.error(message);
    },
  });

  // 取消发布（通过 update 设置 isPublished=false）
  const unpublishMutation = useMutation({
    mutationFn: (note: ReleaseNote) =>
      releaseNoteApi.update(note.id, {
        version: note.version,
        title: note.title,
        channel: note.channel,
        contentMarkdown: note.contentMarkdown,
        highlights: note.highlights ?? undefined,
        knownIssues: note.knownIssues ?? undefined,
        isPublished: false,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["release-notes"] });
      toast.success("已取消发布");
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "取消发布失败";
      toast.error(message);
    },
  });

  const toggleExpand = (id: string) => {
    setExpandedId((prev) => (prev === id ? null : id));
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">版本发布说明</h2>
          <p className="text-sm text-muted-foreground">
            管理产品版本更新说明，向用户展示新功能和改进
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="mr-2 size-4" />
          创建新版本
        </Button>
      </div>

      {/* 版本列表 */}
      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : !notes || notes.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Rocket className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无版本发布说明</p>
            <p className="mt-1 text-sm text-muted-foreground">
              创建第一个版本说明，告知用户最新更新内容
            </p>
            <Button className="mt-4" onClick={() => setCreateOpen(true)}>
              <Plus className="mr-2 size-4" />
              创建新版本
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-3">
          {notes.map((note) => {
            const channel = channelConfig[note.channel];
            const expanded = expandedId === note.id;
            return (
              <Card key={note.id}>
                <CardHeader className="pb-2">
                  <div className="flex items-start justify-between gap-3">
                    <div
                      className="flex flex-1 cursor-pointer items-start gap-3"
                      onClick={() => toggleExpand(note.id)}
                    >
                      <div className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-muted">
                        {expanded ? (
                          <ChevronDown className="size-4 text-muted-foreground" />
                        ) : (
                          <ChevronRight className="size-4 text-muted-foreground" />
                        )}
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <h3 className="text-sm font-semibold">
                            v{note.version}
                          </h3>
                          <Badge className={channel.className}>
                            {channel.label}
                          </Badge>
                          {note.isPublished ? (
                            <Badge className="bg-green-100 text-green-700">
                              <CheckCircle2 className="mr-1 size-3" />
                              已发布
                            </Badge>
                          ) : (
                            <Badge variant="secondary">草稿</Badge>
                          )}
                        </div>
                        <p className="mt-1 text-sm font-medium">
                          {note.title}
                        </p>
                        <div className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
                          <Calendar className="size-3" />
                          {note.isPublished
                            ? `发布于 ${formatDate(note.publishedAt)}`
                            : `创建于 ${formatDate(note.createdAt)}`}
                        </div>
                      </div>
                    </div>

                    {/* 操作按钮 */}
                    <div className="flex shrink-0 items-center gap-1.5">
                      {note.isPublished ? (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => unpublishMutation.mutate(note)}
                          disabled={unpublishMutation.isPending}
                        >
                          <EyeOff className="mr-1.5 size-3.5" />
                          取消发布
                        </Button>
                      ) : (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => publishMutation.mutate(note.id)}
                          disabled={publishMutation.isPending}
                        >
                          <Eye className="mr-1.5 size-3.5" />
                          发布
                        </Button>
                      )}
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setEditTarget(note)}
                      >
                        编辑
                      </Button>
                    </div>
                  </div>
                </CardHeader>

                {/* 亮点和已知问题 */}
                <CardContent className="space-y-3">
                  {/* 亮点 */}
                  {note.highlights && note.highlights.length > 0 && (
                    <div>
                      <div className="mb-1.5 flex items-center gap-1.5">
                        <Sparkles className="size-3.5 text-green-600" />
                        <span className="text-xs font-medium text-green-700">
                          亮点
                        </span>
                      </div>
                      <ul className="ml-5 list-disc space-y-0.5 text-sm">
                        {note.highlights.map((h, idx) => (
                          <li key={idx}>{h}</li>
                        ))}
                      </ul>
                    </div>
                  )}

                  {/* 已知问题 */}
                  {note.knownIssues && note.knownIssues.length > 0 && (
                    <div>
                      <div className="mb-1.5 flex items-center gap-1.5">
                        <AlertTriangle className="size-3.5 text-orange-600" />
                        <span className="text-xs font-medium text-orange-700">
                          已知问题
                        </span>
                      </div>
                      <ul className="ml-5 list-disc space-y-0.5 text-sm">
                        {note.knownIssues.map((issue, idx) => (
                          <li key={idx}>{issue}</li>
                        ))}
                      </ul>
                    </div>
                  )}

                  {/* 展开查看完整内容 */}
                  {expanded && note.contentMarkdown && (
                    <div className="mt-3 rounded-lg border bg-muted/30 p-3">
                      <h4 className="mb-2 text-xs font-medium text-muted-foreground">
                        完整内容
                      </h4>
                      <Markdown content={note.contentMarkdown} />
                    </div>
                  )}
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}

      {/* 创建/编辑对话框 */}
      <ReleaseNoteDialog
        open={createOpen || !!editTarget}
        onOpenChange={(v) => {
          if (!v) {
            setCreateOpen(false);
            setEditTarget(null);
          }
        }}
        editTarget={editTarget}
        onSuccess={() => {
          queryClient.invalidateQueries({ queryKey: ["release-notes"] });
        }}
      />
    </div>
  );
}

// ===== 创建/编辑版本说明对话框 =====

function ReleaseNoteDialog({
  open,
  onOpenChange,
  editTarget,
  onSuccess,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  editTarget: ReleaseNote | null;
  onSuccess: () => void;
}) {
  const isEdit = !!editTarget;
  const [version, setVersion] = useState("");
  const [title, setTitle] = useState("");
  const [channel, setChannel] = useState<ReleaseNoteChannel>("beta");
  const [contentMarkdown, setContentMarkdown] = useState("");
  const [highlightsText, setHighlightsText] = useState("");
  const [knownIssuesText, setKnownIssuesText] = useState("");

  // 当编辑目标变化时，填充表单
  const lastEditId = editTarget?.id;
  const [syncedId, setSyncedId] = useState<string | null>(null);
  if (lastEditId && syncedId !== lastEditId) {
    setSyncedId(lastEditId);
    setVersion(editTarget.version);
    setTitle(editTarget.title);
    setChannel(editTarget.channel);
    setContentMarkdown(editTarget.contentMarkdown);
    setHighlightsText((editTarget.highlights ?? []).join("\n"));
    setKnownIssuesText((editTarget.knownIssues ?? []).join("\n"));
  }
  // 当对话框关闭时，重置状态
  if (!open && syncedId !== null) {
    setSyncedId(null);
    setVersion("");
    setTitle("");
    setChannel("beta");
    setContentMarkdown("");
    setHighlightsText("");
    setKnownIssuesText("");
  }

  const createMutation = useMutation({
    mutationFn: (data: ReleaseNoteInput) => releaseNoteApi.create(data),
    onSuccess: () => {
      toast.success("版本说明已创建");
      onOpenChange(false);
      onSuccess();
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "创建失败";
      toast.error(message);
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: ReleaseNoteInput }) =>
      releaseNoteApi.update(id, data),
    onSuccess: () => {
      toast.success("版本说明已更新");
      onOpenChange(false);
      onSuccess();
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "更新失败";
      toast.error(message);
    },
  });

  const handleSubmit = () => {
    if (!version.trim()) {
      toast.error("请输入版本号");
      return;
    }
    if (!title.trim()) {
      toast.error("请输入标题");
      return;
    }
    if (!contentMarkdown.trim()) {
      toast.error("请输入内容");
      return;
    }

    const highlights = highlightsText
      .split("\n")
      .map((s) => s.trim())
      .filter((s) => s.length > 0);

    const knownIssues = knownIssuesText
      .split("\n")
      .map((s) => s.trim())
      .filter((s) => s.length > 0);

    const data: ReleaseNoteInput = {
      version: version.trim(),
      title: title.trim(),
      channel,
      contentMarkdown: contentMarkdown.trim(),
      highlights: highlights.length > 0 ? highlights : undefined,
      knownIssues: knownIssues.length > 0 ? knownIssues : undefined,
    };

    if (isEdit && editTarget) {
      updateMutation.mutate({ id: editTarget.id, data });
    } else {
      createMutation.mutate(data);
    }
  };

  const isPending = createMutation.isPending || updateMutation.isPending;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {isEdit ? "编辑版本说明" : "创建新版本说明"}
          </DialogTitle>
          <DialogDescription>
            {isEdit
              ? "修改版本更新信息"
              : "创建新的版本发布说明，告知用户更新内容"}
          </DialogDescription>
        </DialogHeader>

        <div className="max-h-[60vh] space-y-4 overflow-y-auto">
          {/* 版本号 */}
          <div className="space-y-2">
            <Label>
              版本号 <span className="text-destructive">*</span>
            </Label>
            <Input
              placeholder="例如：1.2.0"
              value={version}
              onChange={(e) => setVersion(e.target.value)}
            />
          </div>

          {/* 标题 */}
          <div className="space-y-2">
            <Label>
              标题 <span className="text-destructive">*</span>
            </Label>
            <Input
              placeholder="例如：性能优化与新功能"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </div>

          {/* 渠道 */}
          <div className="space-y-2">
            <Label>发布渠道</Label>
            <Select
              value={channel}
              onValueChange={(v) => setChannel(v as ReleaseNoteChannel)}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="alpha">Alpha - 内部测试</SelectItem>
                <SelectItem value="beta">Beta - 公开测试</SelectItem>
                <SelectItem value="rc">RC - 候选发布</SelectItem>
                <SelectItem value="stable">Stable - 正式发布</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {/* 内容 */}
          <div className="space-y-2">
            <Label>
              内容（Markdown）<span className="text-destructive">*</span>
            </Label>
            <Textarea
              placeholder="支持 Markdown 格式的更新内容..."
              value={contentMarkdown}
              onChange={(e) => setContentMarkdown(e.target.value)}
              className="min-h-32"
            />
          </div>

          {/* 亮点 */}
          <div className="space-y-2">
            <Label>亮点（每行一条，可选）</Label>
            <Textarea
              placeholder={"新增搜索功能\n优化导入性能"}
              value={highlightsText}
              onChange={(e) => setHighlightsText(e.target.value)}
              className="min-h-20"
            />
          </div>

          {/* 已知问题 */}
          <div className="space-y-2">
            <Label>已知问题（每行一条，可选）</Label>
            <Textarea
              placeholder={"某些 PDF 可能解析失败"}
              value={knownIssuesText}
              onChange={(e) => setKnownIssuesText(e.target.value)}
              className="min-h-20"
            />
          </div>
        </div>

        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            取消
          </DialogClose>
          <Button onClick={handleSubmit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
            {isEdit ? "保存修改" : "创建"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
