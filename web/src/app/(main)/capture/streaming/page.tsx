"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { HubConnectionBuilder, LogLevel, type HubConnection } from "@microsoft/signalr";
import { toast } from "sonner";
import {
  Loader2,
  Radio,
  ArrowLeft,
  Mic,
  Square,
  Wifi,
  WifiOff,
  AlertCircle,
  Trash2,
} from "lucide-react";
import { getToken, API_ORIGIN, ApiRequestError } from "@/lib/api";
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";

type ConnectionState =
  | "disconnected"
  | "connecting"
  | "connected"
  | "error";

interface TranscriptSegment {
  id: string;
  text: string;
  speaker?: string;
  isFinal: boolean;
  timestamp: number;
}

const CHUNK_INTERVAL_MS = 200;
const TARGET_SAMPLE_RATE = 16000;

export default function StreamingPage() {
  // Configuration
  const [meetingId, setMeetingId] = useState("");
  const [language, setLanguage] = useState("zh");
  const [enablePunctuation, setEnablePunctuation] = useState(true);

  // Connection & recording state
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("disconnected");
  const [isRecording, setIsRecording] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [recordingDuration, setRecordingDuration] = useState(0);

  // Transcript state
  const [partialText, setPartialText] = useState("");
  const [segments, setSegments] = useState<TranscriptSegment[]>([]);

  // Refs
  const hubConnectionRef = useRef<HubConnection | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioStreamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const chunkIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const chunkBufferRef = useRef<Float32Array[]>([]);
  const durationTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const segmentsEndRef = useRef<HTMLDivElement>(null);

  // Auto-scroll
  useEffect(() => {
    segmentsEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [segments, partialText]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      cleanupResources();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const cleanupResources = useCallback(() => {
    if (chunkIntervalRef.current) {
      clearInterval(chunkIntervalRef.current);
      chunkIntervalRef.current = null;
    }
    if (durationTimerRef.current) {
      clearInterval(durationTimerRef.current);
      durationTimerRef.current = null;
    }
    if (processorRef.current) {
      processorRef.current.disconnect();
      processorRef.current = null;
    }
    if (audioContextRef.current) {
      audioContextRef.current.close().catch(() => {});
      audioContextRef.current = null;
    }
    if (audioStreamRef.current) {
      audioStreamRef.current.getTracks().forEach((track) => track.stop());
      audioStreamRef.current = null;
    }
    if (hubConnectionRef.current) {
      hubConnectionRef.current.stop().catch(() => {});
      hubConnectionRef.current = null;
    }
    chunkBufferRef.current = [];
  }, []);

  // Convert Float32Array to Int16 PCM (16kHz mono)
  const downsampleAndEncode = useCallback(
    (buffer: Float32Array): ArrayBuffer => {
      const targetLength = Math.round(
        buffer.length * (TARGET_SAMPLE_RATE / (audioContextRef.current?.sampleRate ?? 48000)),
      );
      const result = new Int16Array(targetLength);
      for (let i = 0; i < targetLength; i++) {
        const idx = (i * buffer.length) / targetLength;
        const nextIdx = Math.min(idx + 1, buffer.length - 1);
        const frac = idx - Math.floor(idx);
        const sample =
          buffer[Math.floor(idx)] * (1 - frac) + buffer[nextIdx] * frac;
        const clamped = Math.max(-1, Math.min(1, sample));
        result[i] = clamped < 0 ? clamped * 0x8000 : clamped * 0x7fff;
      }
      return result.buffer;
    },
    [],
  );

  const handleStart = async () => {
    setErrorMessage(null);

    const token = getToken();
    if (!token) {
      toast.error("请先登录后再使用流式转写");
      return;
    }

    if (!meetingId.trim()) {
      toast.error("请输入会议 ID");
      return;
    }

    try {
      // 1. Build SignalR connection
      setConnectionState("connecting");
      const connection = new HubConnectionBuilder()
        .withUrl(`${API_ORIGIN}/hubs/meeting`, {
          accessTokenFactory: () => token,
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      // Register event handlers
      connection.on("PartialResult", (text: string) => {
        setPartialText(text);
      });

      connection.on("TranscriptionComplete", (data: { text?: string; speakerKey?: string }) => {
        if (data.text) {
          setSegments((prev) => [
            ...prev,
            {
              id: `seg-${Date.now()}-${prev.length}`,
              text: data.text!,
              speaker: data.speakerKey,
              isFinal: true,
              timestamp: Date.now(),
            },
          ]);
          setPartialText("");
        }
      });

      connection.on("HubError", (message: string) => {
        setErrorMessage(message);
        toast.error(`Hub 错误：${message}`);
      });

      connection.onreconnecting(() => setConnectionState("connecting"));
      connection.onreconnected(() => setConnectionState("connected"));
      connection.onclose(() => setConnectionState("disconnected"));

      hubConnectionRef.current = connection;

      // 2. Start connection
      await connection.start();
      setConnectionState("connected");

      // 3. Join meeting
      await connection.invoke("JoinMeeting", meetingId.trim(), {
        language: language === "auto" ? "" : language,
        enablePunctuation,
      });

      // 4. Get microphone access
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          sampleRate: TARGET_SAMPLE_RATE,
          echoCancellation: true,
          noiseSuppression: true,
        },
      });
      audioStreamRef.current = stream;

      // 5. Set up AudioContext for PCM processing
      const audioContext = new AudioContext({
        sampleRate: TARGET_SAMPLE_RATE,
      });
      audioContextRef.current = audioContext;

      const source = audioContext.createMediaStreamSource(stream);
      const processor = audioContext.createScriptProcessor(4096, 1, 1);
      processorRef.current = processor;

      processor.onaudioprocess = (event) => {
        const input = event.inputBuffer.getChannelData(0);
        // Clone the data because it gets reused
        chunkBufferRef.current.push(new Float32Array(input));
      };

      source.connect(processor);
      processor.connect(audioContext.destination);

      // 6. Send audio chunks at regular intervals
      chunkIntervalRef.current = setInterval(() => {
        if (
          chunkBufferRef.current.length === 0 ||
          !hubConnectionRef.current ||
          hubConnectionRef.current.state !== "Connected"
        )
          return;

        // Merge buffered chunks
        const totalLength = chunkBufferRef.current.reduce(
          (sum, chunk) => sum + chunk.length,
          0,
        );
        const merged = new Float32Array(totalLength);
        let offset = 0;
        for (const chunk of chunkBufferRef.current) {
          merged.set(chunk, offset);
          offset += chunk.length;
        }
        chunkBufferRef.current = [];

        const pcmBuffer = downsampleAndEncode(merged);
        hubConnectionRef.current
          .invoke("SendAudioChunk", meetingId.trim(), pcmBuffer)
          .catch(() => {
            // Silently ignore chunk send errors to avoid spamming toasts
          });
      }, CHUNK_INTERVAL_MS);

      // 7. Start duration timer
      setRecordingDuration(0);
      durationTimerRef.current = setInterval(() => {
        setRecordingDuration((prev) => prev + 1);
      }, 1000);

      setIsRecording(true);
      toast.success("流式转写已开始");
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "启动流式转写失败";
      setErrorMessage(message);
      setConnectionState("error");
      toast.error(message);
      cleanupResources();
    }
  };

  const handleStop = async () => {
    try {
      // Stop audio processing
      if (chunkIntervalRef.current) {
        clearInterval(chunkIntervalRef.current);
        chunkIntervalRef.current = null;
      }
      if (durationTimerRef.current) {
        clearInterval(durationTimerRef.current);
        durationTimerRef.current = null;
      }
      if (processorRef.current) {
        processorRef.current.disconnect();
        processorRef.current = null;
      }
      if (audioContextRef.current) {
        await audioContextRef.current.close().catch(() => {});
        audioContextRef.current = null;
      }
      if (audioStreamRef.current) {
        audioStreamRef.current.getTracks().forEach((track) => track.stop());
        audioStreamRef.current = null;
      }
      chunkBufferRef.current = [];

      // Leave meeting and stop connection
      if (hubConnectionRef.current) {
        try {
          await hubConnectionRef.current.invoke(
            "LeaveMeeting",
            meetingId.trim(),
          );
        } catch {
          // Ignore errors during leave
        }
        await hubConnectionRef.current.stop().catch(() => {});
        hubConnectionRef.current = null;
      }

      setConnectionState("disconnected");
      setIsRecording(false);
      setPartialText("");
      toast.success("流式转写已停止");
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "停止失败";
      toast.error(message);
    }
  };

  const handleClearTranscript = () => {
    setSegments([]);
    setPartialText("");
  };

  const formatDuration = (seconds: number): string => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`;
  };

  const connectionStatusConfig: Record<
    ConnectionState,
    { label: string; className: string; icon: typeof Wifi }
  > = {
    disconnected: {
      label: "未连接",
      className: "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300",
      icon: WifiOff,
    },
    connecting: {
      label: "连接中",
      className: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
      icon: Loader2,
    },
    connected: {
      label: "已连接",
      className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
      icon: Wifi,
    },
    error: {
      label: "连接错误",
      className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
      icon: AlertCircle,
    },
  };

  const statusConfig = connectionStatusConfig[connectionState];
  const StatusIcon = statusConfig.icon;

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" render={<Link href="/capture" />}>
            <ArrowLeft className="mr-1.5 size-4" />
            采集中心
          </Button>
        </div>
        <h1 className="mt-2 text-2xl font-bold">流式转写</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          实时流式语音转写，基于 WebSocket 低延迟传输，边录边出文字
        </p>
      </div>

      {/* 连接状态 */}
      <div className="flex items-center justify-between rounded-lg border bg-muted/30 px-4 py-3">
        <div className="flex items-center gap-2">
          <StatusIcon
            className={cn(
              "size-4",
              connectionState === "connecting" && "animate-spin",
            )}
          />
          <span className="text-sm font-medium">{statusConfig.label}</span>
          {isRecording && (
            <Badge
              variant="outline"
              className="bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300"
            >
              <span className="mr-1 inline-block size-1.5 animate-pulse rounded-full bg-red-500" />
              录制中 {formatDuration(recordingDuration)}
            </Badge>
          )}
        </div>
        <Radio className="size-4 text-muted-foreground" />
      </div>

      {errorMessage && (
        <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
          <AlertCircle className="mt-0.5 size-4 shrink-0" />
          <span>{errorMessage}</span>
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-[320px_1fr]">
        {/* 配置与控制面板 */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">录音配置</CardTitle>
            <CardDescription>开始前设置会议 ID 和语言</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="meeting-id">会议 ID</Label>
              <Input
                id="meeting-id"
                value={meetingId}
                onChange={(e) => setMeetingId(e.target.value)}
                placeholder="输入或粘贴会议 ID"
                disabled={isRecording}
              />
            </div>

            <div className="space-y-2">
              <Label>识别语言</Label>
              <Select
                value={language}
                onValueChange={(v) => setLanguage(v as string)}
                disabled={isRecording}
              >
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

            <div className="flex items-center justify-between">
              <Label htmlFor="punctuation" className="text-sm">
                自动标点
              </Label>
              <input
                id="punctuation"
                type="checkbox"
                checked={enablePunctuation}
                onChange={(e) => setEnablePunctuation(e.target.checked)}
                disabled={isRecording}
                className="size-4 rounded border-input"
              />
            </div>

            <div className="space-y-2 pt-2">
              {!isRecording ? (
                <Button
                  className="w-full"
                  onClick={handleStart}
                  disabled={connectionState === "connecting" || !meetingId.trim()}
                >
                  {connectionState === "connecting" ? (
                    <Loader2 className="mr-2 size-4 animate-spin" />
                  ) : (
                    <Mic className="mr-2 size-4" />
                  )}
                  开始录音
                </Button>
              ) : (
                <Button
                  variant="destructive"
                  className="w-full"
                  onClick={handleStop}
                >
                  <Square className="mr-2 size-4" />
                  停止录音
                </Button>
              )}
            </div>

            <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700 dark:bg-amber-950/30 dark:text-amber-300">
              实时流式转写需要 WebSocket 连接到{" "}
              <code className="font-mono">{API_ORIGIN}/hubs/meeting</code>{" "}
              服务端 Hub。请确保服务端已启用 SignalR 端点。
            </p>
          </CardContent>
        </Card>

        {/* 实时转写显示区 */}
        <Card className="flex flex-col">
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle className="text-base">实时转写</CardTitle>
                <CardDescription>
                  {segments.length > 0
                    ? `共 ${segments.length} 段`
                    : "点击「开始录音」后，转写结果将实时显示在此处"}
                </CardDescription>
              </div>
              {segments.length > 0 && !isRecording && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleClearTranscript}
                >
                  <Trash2 className="mr-1.5 size-3.5" />
                  清空
                </Button>
              )}
            </div>
          </CardHeader>
          <CardContent className="flex-1">
            <div className="min-h-[300px] rounded-lg border bg-muted/20 p-4">
              {segments.length === 0 && !partialText ? (
                <div className="flex h-full min-h-[260px] flex-col items-center justify-center text-center">
                  <Mic className="mb-3 size-10 text-muted-foreground/40" />
                  <p className="text-sm text-muted-foreground">
                    转写内容将显示在这里
                  </p>
                  <p className="mt-1 text-xs text-muted-foreground/70">
                    支持实时部分结果显示和最终分段结果
                  </p>
                </div>
              ) : (
                <div className="space-y-3">
                  {segments.map((seg) => (
                    <div key={seg.id} className="text-sm leading-relaxed">
                      {seg.speaker && (
                        <span className="mr-2 inline-block rounded bg-primary/10 px-1.5 py-0.5 text-xs font-medium text-primary">
                          {seg.speaker}
                        </span>
                      )}
                      <span>{seg.text}</span>
                    </div>
                  ))}
                  {partialText && (
                    <div className="text-sm leading-relaxed text-muted-foreground italic">
                      {partialText}
                      <span className="ml-0.5 inline-block h-4 w-0.5 animate-pulse bg-muted-foreground align-middle" />
                    </div>
                  )}
                  <div ref={segmentsEndRef} />
                </div>
              )}
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
