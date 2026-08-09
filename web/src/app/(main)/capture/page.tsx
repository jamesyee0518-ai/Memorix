"use client";

import Link from "next/link";
import {
  Mic,
  FileClock,
  AudioLines,
  Radio,
  MessageCircle,
  ArrowRight,
} from "lucide-react";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";

const subPages = [
  {
    href: "/capture/meetings",
    title: "会议录制",
    description: "创建和管理会议，启动录音并跟踪录制状态",
    icon: Mic,
    color: "bg-blue-500/10 text-blue-600 dark:bg-blue-500/20 dark:text-blue-400",
  },
  {
    href: "/capture/minutes",
    title: "会议纪要",
    description: "为会议生成 AI 纪要，查看摘要、决策和待办事项",
    icon: FileClock,
    color: "bg-purple-500/10 text-purple-600 dark:bg-purple-500/20 dark:text-purple-400",
  },
  {
    href: "/capture/transcribe",
    title: "音频转写",
    description: "上传音频文件进行批量转写，查看和管理转写任务",
    icon: AudioLines,
    color: "bg-emerald-500/10 text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-400",
  },
  {
    href: "/capture/streaming",
    title: "流式转写",
    description: "实时流式语音转写，基于 WebSocket 低延迟传输",
    icon: Radio,
    color: "bg-amber-500/10 text-amber-600 dark:bg-amber-500/20 dark:text-amber-400",
  },
  {
    href: "/capture/voice-qa",
    title: "语音问答",
    description: "通过语音输入进行知识库问答，支持文字与语音双模式",
    icon: MessageCircle,
    color: "bg-rose-500/10 text-rose-600 dark:bg-rose-500/20 dark:text-rose-400",
  },
];

export default function CaptureCenterPage() {
  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <h1 className="text-2xl font-bold">采集中心</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          统一管理会议录制、音频转写、流式转写和语音问答等采集能力
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {subPages.map((page) => {
          const Icon = page.icon;
          return (
            <Link key={page.href} href={page.href} className="group">
              <Card className="h-full cursor-pointer transition-all hover:shadow-md hover:border-primary/40">
                <CardHeader>
                  <div className="flex items-start justify-between">
                    <div
                      className={`flex size-11 items-center justify-center rounded-xl ${page.color}`}
                    >
                      <Icon className="size-5" />
                    </div>
                    <ArrowRight className="size-4 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100" />
                  </div>
                  <CardTitle className="mt-3 text-base">{page.title}</CardTitle>
                  <CardDescription className="text-sm">
                    {page.description}
                  </CardDescription>
                </CardHeader>
              </Card>
            </Link>
          );
        })}
      </div>
    </div>
  );
}
