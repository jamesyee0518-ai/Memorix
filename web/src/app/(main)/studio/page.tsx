"use client";

import Link from "next/link";
import { AudioLines, Gauge } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

export default function StudioPage() {
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <h1 className="text-2xl font-bold">工作室</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          选择一个工具开始创作，或对模型进行基准测试。
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <Link href="/studio/tts" className="group">
          <Card className="h-full transition-colors group-hover:border-primary/50">
            <CardHeader>
              <div className="flex items-center gap-3">
                <div className="rounded-lg bg-primary/10 p-2 text-primary">
                  <AudioLines className="size-5" />
                </div>
                <div>
                  <CardTitle>TTS 合成</CardTitle>
                  <CardDescription>文本转语音，支持多提供商与多音色</CardDescription>
                </div>
              </div>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">
                输入文本，选择提供商与音色，调整语速与音调后生成音频。支持试听与完整合成。
              </p>
            </CardContent>
          </Card>
        </Link>

        <Link href="/studio/benchmark" className="group">
          <Card className="h-full transition-colors group-hover:border-primary/50">
            <CardHeader>
              <div className="flex items-center gap-3">
                <div className="rounded-lg bg-primary/10 p-2 text-primary">
                  <Gauge className="size-5" />
                </div>
                <div>
                  <CardTitle>基准测试</CardTitle>
                  <CardDescription>对注册模型运行基准测试并查看排名</CardDescription>
                </div>
              </div>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">
                查看媒体任务队列与实时状态，运行基准测试，对比各模型的吞吐量、延迟与准确率。
              </p>
            </CardContent>
          </Card>
        </Link>
      </div>
    </div>
  );
}
