"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import {
  MessageSquare,
  Loader2,
  Plus,
  Bug,
  Lightbulb,
  Sparkles,
  ShieldCheck,
  Zap,
  DollarSign,
  HelpCircle,
} from "lucide-react";
import { feedbackApi, ApiRequestError } from "@/lib/api";
import { useFeedbackStore } from "@/stores/feedback-store";
import type {
  FeedbackListItem,
  FeedbackType,
  FeedbackStatus,
  FeedbackSeverity,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
} from "@/components/ui/card";

const feedbackTypeLabels: Record<FeedbackType, string> = {
  bug: "Bug 反馈",
  ux: "体验建议",
  feature: "功能需求",
  quality: "内容质量",
  performance: "性能问题",
  pricing: "价格与套餐",
  general: "其他",
  qa_feedback: "问答反馈",
};

const feedbackTypeIcons: Record<FeedbackType, typeof Bug> = {
  bug: Bug,
  ux: Lightbulb,
  feature: Sparkles,
  quality: ShieldCheck,
  performance: Zap,
  pricing: DollarSign,
  general: HelpCircle,
  qa_feedback: MessageSquare,
};

const severityLabels: Record<FeedbackSeverity, string> = {
  critical: "严重",
  high: "高",
  medium: "中",
  low: "低",
  normal: "一般",
};

const statusConfig: Record<
  FeedbackStatus,
  { label: string; className: string }
> = {
  open: { label: "待处理", className: "bg-blue-100 text-blue-700" },
  in_progress: { label: "处理中", className: "bg-yellow-100 text-yellow-700" },
  resolved: { label: "已解决", className: "bg-green-100 text-green-700" },
  closed: { label: "已关闭", className: "bg-gray-100 text-gray-600" },
};

function formatDate(dateStr: string): string {
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

export default function FeedbackListPage() {
  const openFeedback = useFeedbackStore((s) => s.open);
  const [feedbackList, setFeedbackList] = useState<FeedbackListItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const fetchFeedback = async () => {
    setIsLoading(true);
    try {
      const list = await feedbackApi.list();
      setFeedbackList(list);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载反馈列表失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchFeedback();
  }, []);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">我的反馈</h2>
          <p className="text-sm text-muted-foreground">
            查看您提交的反馈记录及处理状态
          </p>
        </div>
        <Button onClick={() => openFeedback()}>
          <Plus className="mr-2 size-4" />
          提交反馈
        </Button>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : feedbackList.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <MessageSquare className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无反馈记录</p>
            <p className="mt-1 text-sm text-muted-foreground">
              遇到问题或有建议？提交反馈帮助我们改进
            </p>
            <Button
              className="mt-4"
              onClick={() => openFeedback()}
            >
              <Plus className="mr-2 size-4" />
              提交反馈
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-3">
          {feedbackList.map((item) => {
            const Icon = feedbackTypeIcons[item.feedbackType] ?? HelpCircle;
            const status = statusConfig[item.status];
            return (
              <Card key={item.id}>
                <CardHeader className="pb-2">
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex items-start gap-3">
                      <div className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-muted">
                        <Icon className="size-4 text-muted-foreground" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <h3 className="text-sm font-semibold">
                          {item.title}
                        </h3>
                        <div className="mt-1 flex flex-wrap items-center gap-2">
                          <Badge variant="secondary">
                            {feedbackTypeLabels[item.feedbackType]}
                          </Badge>
                          {item.module && (
                            <Badge variant="outline">{item.module}</Badge>
                          )}
                          {item.severity && (
                            <Badge variant="outline">
                              {severityLabels[item.severity]}
                            </Badge>
                          )}
                          <Badge className={status.className}>
                            {status.label}
                          </Badge>
                        </div>
                      </div>
                    </div>
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {formatDate(item.createdAt)}
                    </span>
                  </div>
                </CardHeader>
                {item.content && (
                  <CardContent>
                    <p className="whitespace-pre-wrap text-sm text-muted-foreground">
                      {item.content}
                    </p>
                  </CardContent>
                )}
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
