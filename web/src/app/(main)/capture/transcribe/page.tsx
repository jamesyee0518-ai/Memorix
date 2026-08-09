"use client";

import { useRef, useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Loader2,
  AudioLines,
  ArrowLeft,
  Upload,
  FileAudio,
  Eye,
} from "lucide-react";
import { audioApi, transcriptionApi, ApiRequestError } from "@/lib/api";
import type {
  TranscriptionJobDto,
  TranscriptionSegmentDto,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";

const jobStatusConfig: Record<
  string,
  { label: string; className: string }
> = {
  pending: {
    label: "等待中",
    className: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  },
  running: {
    label: "转写中",
    className: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  queued: {
    label: "排队中",
    className: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  },
  completed: {
    label: "已完成",
    className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  },
  done: {
    label: "已完成",
    className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  },
  failed: {
    label: "失败",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
  cancelled: {
    label: "已取消",
    className: "bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300",
  },
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

function formatDuration(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

export default function TranscribePage() {
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState("");
  const [language, setLanguage] = useState("zh");
  const [isDragOver, setIsDragOver] = useState(false);
  const [viewJobId, setViewJobId] = useState<string | null>(null);

  // Job list query
  const { data: jobs, isLoading: jobsLoading } = useQuery({
    queryKey: ["transcription-jobs"],
    queryFn: () => transcriptionApi.listJobs({ limit: 50 }),
    refetchInterval: (query) => {
      const items = query.state.data ?? [];
      const hasActive = items.some(
        (job) =>
          job.status === "pending" ||
          job.status === "running" ||
          job.status === "queued",
      );
      return hasActive ? 5000 : false;
    },
  });

  // Segments for the selected job
  const { data: segments, isLoading: segmentsLoading } = useQuery({
    queryKey: ["transcription-segments", viewJobId],
    queryFn: () => transcriptionApi.getSegments(viewJobId!),
    enabled: !!viewJobId,
  });

  // Upload mutation
  const uploadMutation = useMutation({
    mutationFn: () =>
      audioApi.upload(file!, {
        title: title.trim() || file!.name,
        language: language && language !== "auto" ? language : undefined,
        enableVad: true,
        enableSpeakerDiarization: false,
        enablePunctuation: true,
        autoStart: true,
      }),
    onSuccess: () => {
      toast.success("音频已上传，转写任务已自动启动");
      queryClient.invalidateQueries({ queryKey: ["transcription-jobs"] });
      setFile(null);
      setTitle("");
      if (fileInputRef.current) fileInputRef.current.value = "";
    },
    onError: (error) => {
      const message =
        error instanceof ApiRequestError ? error.message : "上传失败";
      toast.error(message);
    },
  });

  // Cancel job mutation
  const cancelMutation = useMutation({
    mutationFn: (jobId: string) => transcriptionApi.cancelJob(jobId),
    onSuccess: () => {
      toast.success("任务已取消");
      queryClient.invalidateQueries({ queryKey: ["transcription-jobs"] });
    },
    onError: (error) => {
      const message =
        error instanceof ApiRequestError ? error.message : "取消失败";
      toast.error(message);
    },
  });

  const handleFileSelect = (selectedFile: File | null) => {
    if (!selectedFile) return;
    if (!selectedFile.type.startsWith("audio/")) {
      toast.error("请选择音频文件");
      return;
    }
    setFile(selectedFile);
    if (!title) setTitle(selectedFile.name);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    const droppedFile = e.dataTransfer.files[0];
    if (droppedFile) handleFileSelect(droppedFile);
  };

  const handleUpload = () => {
    if (!file) {
      toast.error("请先选择音频文件");
      return;
    }
    uploadMutation.mutate();
  };

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" render={<Link href="/capture" />}>
            <ArrowLeft className="mr-1.5 size-4" />
            采集中心
          </Button>
        </div>
        <h1 className="mt-2 text-2xl font-bold">音频转写</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          上传音频文件进行批量转写，查看和管理转写任务
        </p>
      </div>

      {/* 上传区域 */}
      <Card>
        <CardHeader>
          <CardTitle>上传音频</CardTitle>
          <CardDescription>
            支持 mp3、wav、m4a 等常见音频格式，上传后自动启动转写
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* 拖拽区域 */}
          <div
            className={cn(
              "flex flex-col items-center justify-center rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors",
              isDragOver
                ? "border-primary bg-primary/5"
                : "border-muted-foreground/25 hover:border-muted-foreground/40",
            )}
            onDragOver={(e) => {
              e.preventDefault();
              setIsDragOver(true);
            }}
            onDragLeave={() => setIsDragOver(false)}
            onDrop={handleDrop}
          >
            <FileAudio className="mb-3 size-10 text-muted-foreground/50" />
            <p className="text-sm font-medium">
              拖拽音频文件到此处，或点击选择文件
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              支持 MP3、WAV、M4A、FLAC 等格式
            </p>
            <Input
              ref={fileInputRef}
              type="file"
              accept="audio/*"
              className="mt-4 hidden"
              onChange={(e) =>
                handleFileSelect(e.target.files?.[0] ?? null)
              }
            />
            <Button
              variant="outline"
              size="sm"
              className="mt-3"
              onClick={() => fileInputRef.current?.click()}
            >
              <Upload className="mr-2 size-4" />
              选择文件
            </Button>
          </div>

          {file && (
            <div className="rounded-lg border bg-muted/30 p-3">
              <div className="flex items-center gap-2">
                <FileAudio className="size-4 text-muted-foreground" />
                <span className="flex-1 truncate text-sm font-medium">
                  {file.name}
                </span>
                <span className="text-xs text-muted-foreground">
                  {(file.size / 1024 / 1024).toFixed(2)} MB
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setFile(null);
                    if (fileInputRef.current) fileInputRef.current.value = "";
                  }}
                >
                  移除
                </Button>
              </div>
            </div>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="audio-title">标题（可选）</Label>
              <Input
                id="audio-title"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="给这段音频起个名字"
              />
            </div>
            <div className="space-y-2">
              <Label>语言</Label>
              <Select value={language} onValueChange={(v) => setLanguage(v as string)}>
                <SelectTrigger>
                  <SelectValue placeholder="选择语言" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="zh">中文</SelectItem>
                  <SelectItem value="en">英语</SelectItem>
                  <SelectItem value="ja">日语</SelectItem>
                  <SelectItem value="ko">韩语</SelectItem>
                  <SelectItem value="auto">自动检测</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <Button
            onClick={handleUpload}
            disabled={!file || uploadMutation.isPending}
            className="w-full"
          >
            {uploadMutation.isPending ? (
              <Loader2 className="mr-2 size-4 animate-spin" />
            ) : (
              <Upload className="mr-2 size-4" />
            )}
            {uploadMutation.isPending ? "上传中..." : "上传并开始转写"}
          </Button>
        </CardContent>
      </Card>

      {/* 任务列表 */}
      <Card>
        <CardHeader>
          <CardTitle>转写任务</CardTitle>
          <CardDescription>
            共 {jobs?.length ?? 0} 个任务
          </CardDescription>
        </CardHeader>
        <CardContent>
          {jobsLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : !jobs || jobs.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <AudioLines className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无转写任务，上传音频后将自动创建
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>状态</TableHead>
                  <TableHead>语言</TableHead>
                  <TableHead>分段数</TableHead>
                  <TableHead>创建时间</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {jobs.map((job: TranscriptionJobDto) => {
                  const statusConfig =
                    jobStatusConfig[job.status] ?? {
                      label: job.status,
                      className: "bg-muted text-muted-foreground",
                    };
                  const isActive =
                    job.status === "pending" ||
                    job.status === "running" ||
                    job.status === "queued";
                  return (
                    <TableRow key={job.id}>
                      <TableCell>
                        <Badge
                          variant="outline"
                          className={statusConfig.className}
                        >
                          {isActive && (
                            <Loader2 className="mr-1 size-3 animate-spin" />
                          )}
                          {statusConfig.label}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-sm">
                        {job.language || "自动"}
                      </TableCell>
                      <TableCell className="text-sm">
                        {job.segmentCount}
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {formatDate(job.createdAt)}
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={job.segmentCount === 0}
                            onClick={() => setViewJobId(job.id)}
                          >
                            <Eye className="mr-1.5 size-3.5" />
                            查看转写
                          </Button>
                          {isActive && (
                            <Button
                              size="sm"
                              variant="ghost"
                              disabled={cancelMutation.isPending}
                              onClick={() => cancelMutation.mutate(job.id)}
                            >
                              取消
                            </Button>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 查看转写弹窗 */}
      <Dialog
        open={!!viewJobId}
        onOpenChange={(v) => !v && setViewJobId(null)}
      >
        <DialogContent className="max-h-[80vh] sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>转写结果</DialogTitle>
            <DialogDescription>
              查看转写分段内容，包含时间戳和说话人信息
            </DialogDescription>
          </DialogHeader>
          <div className="max-h-[55vh] overflow-y-auto">
            {segmentsLoading ? (
              <div className="flex items-center justify-center py-8">
                <Loader2 className="size-6 animate-spin text-muted-foreground" />
              </div>
            ) : !segments || segments.length === 0 ? (
              <div className="py-8 text-center text-sm text-muted-foreground">
                暂无转写分段数据
              </div>
            ) : (
              <div className="space-y-3">
                {segments.map((seg: TranscriptionSegmentDto) => (
                  <div
                    key={seg.id}
                    className="rounded-lg border p-3 text-sm"
                  >
                    <div className="mb-1 flex items-center gap-2 text-xs text-muted-foreground">
                      <span className="font-mono">
                        {formatDuration(seg.sourceStartMs)}
                        {" - "}
                        {formatDuration(seg.sourceEndMs)}
                      </span>
                      {seg.speakerKey && (
                        <Badge variant="outline" className="text-xs">
                          {seg.speakerKey}
                        </Badge>
                      )}
                      {seg.confidence != null && (
                        <span className="text-muted-foreground">
                          置信度 {(seg.confidence * 100).toFixed(0)}%
                        </span>
                      )}
                    </div>
                    <p className="leading-relaxed">{seg.text}</p>
                  </div>
                ))}
              </div>
            )}
          </div>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              关闭
            </DialogClose>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
