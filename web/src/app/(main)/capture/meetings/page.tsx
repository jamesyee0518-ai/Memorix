"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Plus,
  Loader2,
  Mic,
  Calendar,
  ArrowLeft,
} from "lucide-react";
import { meetingApi, ApiRequestError } from "@/lib/api";
import type { MeetingDto } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
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
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";

const statusConfig: Record<
  string,
  { label: string; className: string }
> = {
  scheduled: {
    label: "待开始",
    className: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  },
  recording: {
    label: "录制中",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
  paused: {
    label: "已暂停",
    className: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  completed: {
    label: "已完成",
    className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  },
  finished: {
    label: "已结束",
    className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  },
};

function getStatusBadge(status: string) {
  const config = statusConfig[status] ?? {
    label: status || "未知",
    className: "bg-muted text-muted-foreground",
  };
  return (
    <Badge variant="outline" className={config.className}>
      {config.label}
    </Badge>
  );
}

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

export default function MeetingsPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");

  const { data: meetings, isLoading } = useQuery({
    queryKey: ["meetings"],
    queryFn: () => meetingApi.list({ limit: 100 }),
  });

  const createMutation = useMutation({
    mutationFn: () =>
      meetingApi.create({
        title: title.trim(),
        description: description.trim() || undefined,
      }),
    onSuccess: () => {
      toast.success("会议已创建");
      queryClient.invalidateQueries({ queryKey: ["meetings"] });
      setCreateOpen(false);
      setTitle("");
      setDescription("");
    },
    onError: (error) => {
      const message =
        error instanceof ApiRequestError ? error.message : "创建会议失败";
      toast.error(message);
    },
  });

  const handleCreate = () => {
    if (!title.trim()) {
      toast.error("请输入会议标题");
      return;
    }
    createMutation.mutate();
  };

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <div className="flex items-center gap-2">
            <Button
              variant="ghost"
              size="sm"
              render={<Link href="/capture" />}
            >
              <ArrowLeft className="mr-1.5 size-4" />
              采集中心
            </Button>
          </div>
          <h1 className="mt-2 text-2xl font-bold">会议录制</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            创建和管理会议，跟踪录制状态
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="mr-2 size-4" />
          新建会议
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>会议列表</CardTitle>
          <CardDescription>
            共 {meetings?.length ?? 0} 场会议
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : !meetings || meetings.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <Mic className="mb-4 size-12 text-muted-foreground/50" />
              <p className="text-lg font-medium">暂无会议</p>
              <p className="mt-1 text-sm text-muted-foreground">
                点击「新建会议」开始创建
              </p>
              <Button className="mt-4" onClick={() => setCreateOpen(true)}>
                <Plus className="mr-2 size-4" />
                新建会议
              </Button>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>会议标题</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>创建时间</TableHead>
                  <TableHead>描述</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {meetings.map((meeting: MeetingDto) => (
                  <TableRow key={meeting.id}>
                    <TableCell className="font-medium">
                      {meeting.title}
                    </TableCell>
                    <TableCell>{getStatusBadge(meeting.status)}</TableCell>
                    <TableCell>
                      <span className="flex items-center gap-1.5 text-sm text-muted-foreground">
                        <Calendar className="size-3.5" />
                        {formatDate(meeting.createdAt)}
                      </span>
                    </TableCell>
                    <TableCell className="max-w-xs truncate text-sm text-muted-foreground">
                      {meeting.description || "-"}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 新建会议弹窗 */}
      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>新建会议</DialogTitle>
            <DialogDescription>
              创建一场新会议，创建后可以开始录制音频
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="meeting-title">会议标题</Label>
              <Input
                id="meeting-title"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="请输入会议标题"
                autoFocus
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="meeting-desc">会议描述（可选）</Label>
              <Textarea
                id="meeting-desc"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="简要描述会议内容..."
                className="min-h-20"
              />
            </div>
          </div>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button
              onClick={handleCreate}
              disabled={createMutation.isPending || !title.trim()}
            >
              {createMutation.isPending ? (
                <Loader2 className="mr-2 size-4 animate-spin" />
              ) : (
                <Plus className="mr-2 size-4" />
              )}
              创建
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
