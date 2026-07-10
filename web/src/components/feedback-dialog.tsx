"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Loader2, Send } from "lucide-react";
import { useFeedbackStore } from "@/stores/feedback-store";
import { feedbackApi, ApiRequestError } from "@/lib/api";
import type {
  FeedbackType,
  FeedbackSeverity,
  CreateFeedbackRequest,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
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

const feedbackTypeOptions: { value: FeedbackType; label: string }[] = [
  { value: "bug", label: "Bug 反馈" },
  { value: "ux", label: "体验建议" },
  { value: "feature", label: "功能需求" },
  { value: "quality", label: "内容质量" },
  { value: "performance", label: "性能问题" },
  { value: "pricing", label: "价格与套餐" },
  { value: "general", label: "其他" },
];

const moduleOptions = [
  { value: "import", label: "资料导入" },
  { value: "ai_processing", label: "AI 处理" },
  { value: "search", label: "搜索" },
  { value: "qa", label: "问答" },
  { value: "report", label: "报告" },
  { value: "export", label: "导出" },
  { value: "agent_api", label: "Agent API" },
  { value: "general", label: "通用" },
];

const severityOptions: { value: FeedbackSeverity; label: string }[] = [
  { value: "critical", label: "严重" },
  { value: "high", label: "高" },
  { value: "medium", label: "中" },
  { value: "low", label: "低" },
];

export function FeedbackDialog() {
  const { isOpen, relatedEntityType, relatedEntityId, prefillModule, close } =
    useFeedbackStore();

  const [feedbackType, setFeedbackType] = useState<FeedbackType>("bug");
  const [module, setModule] = useState<string>("");
  const [severity, setSeverity] = useState<FeedbackSeverity>("medium");
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  const [submitting, setSubmitting] = useState(false);

  // 弹窗打开时设置预填值
  useEffect(() => {
    if (isOpen) {
      if (prefillModule) {
        setModule(prefillModule);
      }
    }
  }, [isOpen, prefillModule]);

  // 关闭时重置表单
  useEffect(() => {
    if (!isOpen) {
      setFeedbackType("bug");
      setModule("");
      setSeverity("medium");
      setTitle("");
      setContent("");
    }
  }, [isOpen]);

  const handleSubmit = async () => {
    if (!title.trim()) {
      toast.error("请输入标题");
      return;
    }

    const data: CreateFeedbackRequest = {
      feedbackType,
      title: title.trim(),
      severity,
    };
    if (module) data.module = module;
    if (content.trim()) data.content = content.trim();
    if (relatedEntityType) data.relatedEntityType = relatedEntityType;
    if (relatedEntityId) data.relatedEntityId = relatedEntityId;

    setSubmitting(true);
    try {
      await feedbackApi.create(data);
      toast.success("反馈已提交，感谢您的支持！");
      close();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "提交反馈失败，请重试";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(v) => !v && close()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>提交反馈</DialogTitle>
          <DialogDescription>
            感谢您的反馈，我们会认真对待每一条建议
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {/* 反馈类型 */}
          <div className="space-y-2">
            <Label>反馈类型</Label>
            <Select
              value={feedbackType}
              onValueChange={(v) => setFeedbackType(v as FeedbackType)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="请选择反馈类型" />
              </SelectTrigger>
              <SelectContent>
                {feedbackTypeOptions.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* 模块 */}
          <div className="space-y-2">
            <Label>相关模块（可选）</Label>
            <Select
              value={module}
              onValueChange={(v) => setModule(v as string)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="请选择相关模块" />
              </SelectTrigger>
              <SelectContent>
                {moduleOptions.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* 严重程度 */}
          <div className="space-y-2">
            <Label>严重程度</Label>
            <Select
              value={severity}
              onValueChange={(v) => setSeverity(v as FeedbackSeverity)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="请选择严重程度" />
              </SelectTrigger>
              <SelectContent>
                {severityOptions.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* 标题 */}
          <div className="space-y-2">
            <Label htmlFor="feedback-title">
              标题 <span className="text-destructive">*</span>
            </Label>
            <Input
              id="feedback-title"
              placeholder="请简要描述您的问题或建议"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={100}
            />
          </div>

          {/* 详细描述 */}
          <div className="space-y-2">
            <Label htmlFor="feedback-content">详细描述（可选）</Label>
            <Textarea
              id="feedback-content"
              placeholder="请详细描述您遇到的问题或建议，帮助我们更好地理解"
              value={content}
              onChange={(e) => setContent(e.target.value)}
              rows={4}
              maxLength={2000}
            />
          </div>
        </div>

        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            取消
          </DialogClose>
          <Button onClick={handleSubmit} disabled={submitting}>
            {submitting ? (
              <Loader2 className="mr-2 size-4 animate-spin" />
            ) : (
              <Send className="mr-2 size-4" />
            )}
            提交反馈
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
