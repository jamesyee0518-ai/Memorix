"use client";

import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { AudioLines, Loader2, Play, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { ttsApi, ApiRequestError } from "@/lib/api";
import type { TtsResult } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export default function TtsPage() {
  const [text, setText] = useState("");
  const [providerId, setProviderId] = useState<string>("");
  const [voiceId, setVoiceId] = useState<string>("");
  const [speed, setSpeed] = useState(1);
  const [pitch, setPitch] = useState(0);
  const [result, setResult] = useState<TtsResult | null>(null);

  const providers = useQuery({
    queryKey: ["tts-providers"],
    queryFn: ttsApi.listProviders,
  });

  const voices = useQuery({
    queryKey: ["tts-voices", providerId],
    queryFn: () => ttsApi.listVoices(providerId || undefined),
    enabled: providers.data !== undefined,
  });

  const synthesize = useMutation({
    mutationFn: () =>
      ttsApi.synthesize({
        text,
        voiceId: voiceId || undefined,
        speed,
        pitch,
        preferredProviderId: providerId || undefined,
      }),
    onSuccess: (data) => {
      setResult(data);
      toast.success("语音合成完成");
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "语音合成失败"),
  });

  const preview = useMutation({
    mutationFn: () =>
      ttsApi.preview({
        text: text.slice(0, 200),
        voiceId: voiceId || undefined,
        preferredProviderId: providerId || undefined,
      }),
    onSuccess: (data) => {
      setResult(data);
      toast.success("试听已生成");
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "试听失败"),
  });

  const audioSrc = result
    ? `${result.audioFilePath.startsWith("http") ? "" : "/api"}${result.audioFilePath}`
    : null;

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-bold">TTS 语音合成</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          输入文本并选择提供商与音色，生成高质量语音。
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* 左侧：输入面板 */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <AudioLines className="size-5 text-primary" />
              合成参数
            </CardTitle>
            <CardDescription>填写文本并选择语音参数</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="tts-text">输入文本</Label>
              <Textarea
                id="tts-text"
                value={text}
                onChange={(e) => setText(e.target.value)}
                placeholder="请输入需要合成的文本…"
                className="min-h-28"
              />
            </div>

            <div className="space-y-2">
              <Label>提供商</Label>
              {providers.isLoading ? (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 className="size-4 animate-spin" /> 加载中…
                </div>
              ) : (
                <Select value={providerId} onValueChange={(v) => { const val = v ?? ""; setProviderId(val === "__all__" ? "" : val); setVoiceId(""); }}>
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="自动选择" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__all__">自动选择</SelectItem>
                    {providers.data?.map((p) => (
                      <SelectItem key={p.providerId} value={p.providerId}>
                        {p.displayName}
                        {p.isLocal && " (本地)"}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>

            <div className="space-y-2">
              <Label>音色</Label>
              {voices.isLoading ? (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 className="size-4 animate-spin" /> 加载中…
                </div>
              ) : (
                <Select value={voiceId} onValueChange={(v) => setVoiceId(v === "__default__" ? "" : (v ?? ""))}>
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="默认音色" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__default__">默认音色</SelectItem>
                    {voices.data?.map((v) => (
                      <SelectItem key={v.voiceId} value={v.voiceId}>
                        {v.name} ({v.language}){v.gender ? ` · ${v.gender}` : ""}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="tts-speed">语速 ({speed.toFixed(1)}x)</Label>
              <input
                id="tts-speed"
                type="range"
                min={0.5}
                max={2}
                step={0.1}
                value={speed}
                onChange={(e) => setSpeed(Number(e.target.value))}
                className="w-full"
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="tts-pitch">音调 ({pitch > 0 ? `+${pitch}` : pitch})</Label>
              <input
                id="tts-pitch"
                type="range"
                min={-12}
                max={12}
                step={1}
                value={pitch}
                onChange={(e) => setPitch(Number(e.target.value))}
                className="w-full"
              />
            </div>

            <div className="flex gap-2">
              <Button
                className="flex-1"
                disabled={!text.trim() || synthesize.isPending}
                onClick={() => synthesize.mutate()}
              >
                {synthesize.isPending ? (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                ) : (
                  <Sparkles className="mr-2 size-4" />
                )}
                合成
              </Button>
              <Button
                variant="outline"
                disabled={!text.trim() || preview.isPending}
                onClick={() => preview.mutate()}
              >
                {preview.isPending ? (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                ) : (
                  <Play className="mr-2 size-4" />
                )}
                试听
              </Button>
            </div>
          </CardContent>
        </Card>

        {/* 右侧：结果面板 */}
        <Card>
          <CardHeader>
            <CardTitle>合成结果</CardTitle>
            <CardDescription>音频播放与元数据</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {result ? (
              <>
                <div className="rounded-lg border bg-muted/30 p-4">
                  <audio controls src={audioSrc ?? undefined} className="w-full">
                    您的浏览器不支持音频播放。
                  </audio>
                </div>
                <div className="space-y-2">
                  <div className="flex items-center justify-between border-b border-border/50 py-2">
                    <span className="text-sm text-muted-foreground">提供商</span>
                    <Badge variant="secondary">{result.providerId}</Badge>
                  </div>
                  {result.modelId && (
                    <div className="flex items-center justify-between border-b border-border/50 py-2">
                      <span className="text-sm text-muted-foreground">模型</span>
                      <span className="text-sm font-medium">{result.modelId}</span>
                    </div>
                  )}
                  {result.voiceId && (
                    <div className="flex items-center justify-between border-b border-border/50 py-2">
                      <span className="text-sm text-muted-foreground">音色</span>
                      <span className="text-sm font-medium">{result.voiceId}</span>
                    </div>
                  )}
                  <div className="flex items-center justify-between border-b border-border/50 py-2">
                    <span className="text-sm text-muted-foreground">时长</span>
                    <span className="text-sm font-medium">{(result.durationMs / 1000).toFixed(2)} 秒</span>
                  </div>
                  <div className="flex items-center justify-between py-2">
                    <span className="text-sm text-muted-foreground">预估费用</span>
                    <span className="text-sm font-medium">{result.estimatedCost.toFixed(4)} 积分</span>
                  </div>
                </div>
              </>
            ) : (
              <div className="flex flex-col items-center justify-center py-16 text-center text-sm text-muted-foreground">
                <AudioLines className="mb-3 size-10 opacity-40" />
                尚无合成结果，请在左侧输入文本并点击「合成」。
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
