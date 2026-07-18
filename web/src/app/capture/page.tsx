"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { toast } from "sonner";
import {
  ArrowUp,
  CheckCircle2,
  FileUp,
  Inbox,
  LinkIcon,
  Loader2,
  MessageSquareText,
  Mic,
  Smartphone,
  Square,
} from "lucide-react";
import { ApiRequestError, mobileCaptureApi, topicApi } from "@/lib/api";
import type { InboxItem, Topic } from "@/lib/types";
import { getMobileCaptureClientId } from "@/lib/mobile-capture-client";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";

type CaptureMode = "text" | "url" | "file" | "audio";

function formatDate(dateStr?: string) {
  if (!dateStr) return "-";
  return new Date(dateStr).toLocaleString("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export default function CapturePage() {
  const [mode, setMode] = useState<CaptureMode>("text");
  const [topics, setTopics] = useState<Topic[]>([]);
  const [topicId, setTopicId] = useState("none");
  const [contentText, setContentText] = useState("");
  const [sourceUrl, setSourceUrl] = useState("");
  const [urlTitle, setUrlTitle] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [clientId, setClientId] = useState<string | undefined>();
  const [submitting, setSubmitting] = useState(false);
  const [recentItems, setRecentItems] = useState<InboxItem[]>([]);
  const [isRecording, setIsRecording] = useState(false);
  const [recordingUrl, setRecordingUrl] = useState<string | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const recordingChunksRef = useRef<Blob[]>([]);

  useEffect(() => {
    const id = getMobileCaptureClientId();
    setClientId(id);
    if (id) {
      mobileCaptureApi
        .bindDevice({
          clientId: id,
          deviceName: navigator.userAgent.includes("Mobile") ? "Mobile Browser" : "Browser",
          platform: navigator.platform || "web",
        })
        .catch(() => {
          // Binding is best-effort for mobile web; capture can still proceed with clientId.
        });
    }
    topicApi
      .list()
      .then((res) => setTopics(res.items))
      .catch(() => {
        setTopics([]);
      });
  }, []);

  useEffect(() => {
    return () => {
      if (recordingUrl) URL.revokeObjectURL(recordingUrl);
      mediaRecorderRef.current?.stream.getTracks().forEach((track) => track.stop());
    };
  }, [recordingUrl]);

  const selectedTopicId = topicId === "none" ? undefined : topicId;
  const selectedTopicName = useMemo(
    () => topics.find((t) => t.id === selectedTopicId)?.name,
    [topics, selectedTopicId]
  );

  const resetForm = () => {
    setContentText("");
    setSourceUrl("");
    setUrlTitle("");
    setFile(null);
    if (recordingUrl) URL.revokeObjectURL(recordingUrl);
    setRecordingUrl(null);
  };

  const startRecording = async () => {
    if (!navigator.mediaDevices?.getUserMedia) {
      toast.error("当前浏览器不支持录音");
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const recorder = new MediaRecorder(stream);
      recordingChunksRef.current = [];
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) recordingChunksRef.current.push(event.data);
      };
      recorder.onstop = () => {
        const mimeType = recorder.mimeType || "audio/webm";
        const blob = new Blob(recordingChunksRef.current, { type: mimeType });
        const extension = mimeType.includes("mp4") ? "m4a" : "webm";
        const recordedFile = new File([blob], `recording-${Date.now()}.${extension}`, {
          type: mimeType,
        });
        if (recordingUrl) URL.revokeObjectURL(recordingUrl);
        setRecordingUrl(URL.createObjectURL(blob));
        setFile(recordedFile);
        stream.getTracks().forEach((track) => track.stop());
      };
      mediaRecorderRef.current = recorder;
      recorder.start();
      setIsRecording(true);
    } catch {
      toast.error("无法访问麦克风，请检查浏览器权限");
    }
  };

  const stopRecording = () => {
    const recorder = mediaRecorderRef.current;
    if (!recorder || recorder.state === "inactive") return;
    recorder.stop();
    setIsRecording(false);
  };

  const handleSubmit = async () => {
    setSubmitting(true);
    try {
      let item: InboxItem;
      if (mode === "text") {
        if (!contentText.trim()) {
          toast.error("请输入要保存的内容");
          return;
        }
        item = await mobileCaptureApi.text({
          contentText: contentText.trim(),
          topicId: selectedTopicId,
          clientId,
        });
      } else if (mode === "url") {
        if (!sourceUrl.trim()) {
          toast.error("请输入链接地址");
          return;
        }
        item = await mobileCaptureApi.url({
          sourceUrl: sourceUrl.trim(),
          title: urlTitle.trim() || undefined,
          topicId: selectedTopicId,
          clientId,
        });
      } else {
        if (!file) {
          toast.error(mode === "audio" ? "请选择或录制一段音频" : "请选择要上传的文件");
          return;
        }
        item = await mobileCaptureApi.upload(file, selectedTopicId, clientId);
      }

      setRecentItems((items) => [item, ...items].slice(0, 6));
      resetForm();
      toast.success("已进入 Inbox");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "提交失败，请稍后重试";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  const modes = [
    { value: "text" as const, label: "文字", icon: MessageSquareText },
    { value: "url" as const, label: "链接", icon: LinkIcon },
    { value: "file" as const, label: "文件", icon: FileUp },
    { value: "audio" as const, label: "录音", icon: Mic },
  ];

  return (
    <main className="min-h-screen bg-slate-950 text-white">
      <div className="mx-auto flex min-h-screen w-full max-w-md flex-col px-4 py-5">
        <header className="mb-5 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="flex size-10 items-center justify-center rounded-xl bg-blue-500/15 text-blue-300">
              <Smartphone className="size-5" />
            </div>
            <div>
              <h1 className="text-lg font-semibold">发送到知识库</h1>
              <p className="text-xs text-slate-400">
                文本、链接和文件会先进入 Inbox
              </p>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="border-slate-700 bg-slate-900 text-slate-200 hover:bg-slate-800"
            render={<Link href="/capture/status" />}
          >
            状态
          </Button>
        </header>

        <section className="mb-4 rounded-xl border border-slate-800 bg-slate-900/80 p-3">
          <div className="grid grid-cols-4 gap-2">
            {modes.map((item) => {
              const Icon = item.icon;
              const active = mode === item.value;
              return (
                <button
                  key={item.value}
                  type="button"
                  onClick={() => setMode(item.value)}
                  className={cn(
                    "flex h-10 items-center justify-center gap-1.5 rounded-lg text-sm font-medium transition-colors",
                    active
                      ? "bg-white text-slate-950"
                      : "bg-slate-800 text-slate-300 hover:bg-slate-700"
                  )}
                >
                  <Icon className="size-4" />
                  {item.label}
                </button>
              );
            })}
          </div>
        </section>

        <Card className="border-slate-800 bg-slate-900 text-slate-100">
          <CardHeader>
            <CardTitle className="text-base">
              {mode === "text"
                ? "记录一段想法"
                : mode === "url"
                  ? "保存一个链接"
                  : mode === "audio"
                    ? "上传一段录音"
                    : "上传一个文件"}
            </CardTitle>
            <CardDescription className="text-slate-400">
              {selectedTopicName
                ? `将建议归入「${selectedTopicName}」`
                : "可以稍后在桌面端归类和导入"}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label className="text-slate-300">专题</Label>
              <Select
                value={topicId}
                onValueChange={(value) => setTopicId(value ?? "none")}
              >
                <SelectTrigger className="border-slate-700 bg-slate-950 text-slate-100">
                  <SelectValue placeholder="选择专题" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">稍后归类</SelectItem>
                  {topics.map((topic) => (
                    <SelectItem key={topic.id} value={topic.id}>
                      {topic.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            {mode === "text" && (
              <div className="space-y-2">
                <Label className="text-slate-300">内容</Label>
                <Textarea
                  value={contentText}
                  onChange={(e) => setContentText(e.target.value)}
                  placeholder="粘贴一段资料，或记录一个想法..."
                  className="min-h-36 border-slate-700 bg-slate-950 text-base text-slate-100 placeholder:text-slate-500"
                />
              </div>
            )}

            {mode === "url" && (
              <div className="space-y-3">
                <div className="space-y-2">
                  <Label className="text-slate-300">链接</Label>
                  <Input
                    value={sourceUrl}
                    onChange={(e) => setSourceUrl(e.target.value)}
                    placeholder="https://..."
                    inputMode="url"
                    className="border-slate-700 bg-slate-950 text-base text-slate-100 placeholder:text-slate-500"
                  />
                </div>
                <div className="space-y-2">
                  <Label className="text-slate-300">标题</Label>
                  <Input
                    value={urlTitle}
                    onChange={(e) => setUrlTitle(e.target.value)}
                    placeholder="可选"
                    className="border-slate-700 bg-slate-950 text-slate-100 placeholder:text-slate-500"
                  />
                </div>
              </div>
            )}

            {(mode === "file" || mode === "audio") && (
              <div className="space-y-3">
                <Label className="text-slate-300">
                  {mode === "audio" ? "录音" : "文件"}
                </Label>
                {mode === "audio" && (
                  <div className="rounded-lg border border-slate-800 bg-slate-950 p-3">
                    <div className="flex items-center gap-2">
                      <Button
                        type="button"
                        variant={isRecording ? "destructive" : "outline"}
                        className="flex-1 border-slate-700 bg-slate-900 text-slate-100 hover:bg-slate-800"
                        onClick={isRecording ? stopRecording : startRecording}
                      >
                        {isRecording ? (
                          <Square className="mr-2 size-4" />
                        ) : (
                          <Mic className="mr-2 size-4" />
                        )}
                        {isRecording ? "停止录音" : "开始录音"}
                      </Button>
                    </div>
                    {recordingUrl && (
                      <audio controls src={recordingUrl} className="mt-3 w-full" />
                    )}
                  </div>
                )}
                <Input
                  type="file"
                  accept={mode === "audio" ? "audio/*" : undefined}
                  capture={mode === "audio" ? true : undefined}
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                  className="border-slate-700 bg-slate-950 text-slate-100 file:text-slate-200"
                />
                {file && (
                  <p className="text-xs text-slate-400">
                    已选择：{file.name}
                  </p>
                )}
                {mode === "audio" && (
                  <p className="text-xs text-slate-500">
                    录音会先进入 Inbox，后续等待转写和摘要。
                  </p>
                )}
              </div>
            )}

            <Button
              size="lg"
              onClick={handleSubmit}
              disabled={submitting}
              className="h-11 w-full"
            >
              {submitting ? (
                <Loader2 className="mr-2 size-4 animate-spin" />
              ) : (
                <ArrowUp className="mr-2 size-4" />
              )}
              {submitting ? "发送中..." : "发送到 Inbox"}
            </Button>
            <Button
              variant="ghost"
              className="h-10 w-full text-slate-300 hover:bg-slate-800 hover:text-white"
              render={<Link href="/capture/status" />}
            >
              查看采集状态
            </Button>
          </CardContent>
        </Card>

        <section className="mt-5 flex-1">
          <div className="mb-2 flex items-center justify-between">
            <h2 className="flex items-center gap-2 text-sm font-medium text-slate-200">
              <Inbox className="size-4" />
              最近采集
            </h2>
            <Badge className="bg-slate-800 text-slate-300">
              {recentItems.length} 条
            </Badge>
          </div>
          {recentItems.length === 0 ? (
            <div className="rounded-xl border border-dashed border-slate-800 px-4 py-8 text-center text-sm text-slate-500">
              发送后的资料会显示在这里
            </div>
          ) : (
            <div className="space-y-2">
              {recentItems.map((item) => (
                <div
                  key={item.id}
                  className="rounded-xl border border-slate-800 bg-slate-900/70 px-3 py-3"
                >
                  <div className="flex items-start gap-2">
                    <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-emerald-400" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-medium text-slate-100">
                        {item.title ||
                          item.sourceUrl ||
                          item.contentText ||
                          item.fileName ||
                          "未命名资料"}
                      </p>
                      <p className="mt-1 text-xs text-slate-500">
                        {item.inputType} · {item.status} ·{" "}
                        {formatDate(item.createdAt)}
                      </p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>
    </main>
  );
}
