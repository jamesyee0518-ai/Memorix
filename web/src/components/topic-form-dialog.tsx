"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Loader2 } from "lucide-react";
import { useTopicStore } from "@/stores/topic-store";
import { ApiRequestError } from "@/lib/api";
import type { Topic } from "@/lib/types";
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

const topicSchema = z.object({
  name: z.string().min(1, "请输入专题名称").max(100, "名称不能超过100个字符"),
  description: z.string().max(500, "描述不能超过500个字符").optional(),
  domain: z.string().max(100, "领域不能超过100个字符").optional(),
});

type TopicForm = z.infer<typeof topicSchema>;

interface TopicFormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  topic?: Topic | null;
  onSuccess?: () => void;
}

export function TopicFormDialog({
  open,
  onOpenChange,
  topic,
  onSuccess,
}: TopicFormDialogProps) {
  const { createTopic, updateTopic } = useTopicStore();
  const [submitting, setSubmitting] = useState(false);
  const isEdit = !!topic;

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<TopicForm>({
    resolver: zodResolver(topicSchema),
  });

  useEffect(() => {
    if (open) {
      reset({
        name: topic?.name ?? "",
        description: topic?.description ?? "",
        domain: topic?.domain ?? "",
      });
    }
  }, [open, topic, reset]);

  const onSubmit = async (data: TopicForm) => {
    setSubmitting(true);
    try {
      if (isEdit && topic) {
        await updateTopic(topic.id, {
          name: data.name,
          description: data.description || undefined,
          domain: data.domain || undefined,
        });
        toast.success("专题更新成功");
      } else {
        await createTopic({
          name: data.name,
          description: data.description || undefined,
          domain: data.domain || undefined,
        });
        toast.success("专题创建成功");
      }
      onOpenChange(false);
      onSuccess?.();
    } catch (err) {
      const message =
        err instanceof ApiRequestError
          ? err.message
          : isEdit
            ? "更新失败，请重试"
            : "创建失败，请重试";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{isEdit ? "编辑专题" : "创建专题"}</DialogTitle>
          <DialogDescription>
            {isEdit
              ? "修改专题信息"
              : "创建一个新的知识管理专题"}
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="topic-name">专题名称</Label>
              <Input
                id="topic-name"
                placeholder="例如：AI 芯片产业研究"
                {...register("name")}
              />
              {errors.name && (
                <p className="text-xs text-destructive">
                  {errors.name.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="topic-desc">描述（可选）</Label>
              <Textarea
                id="topic-desc"
                placeholder="简要描述该专题的研究方向"
                rows={3}
                {...register("description")}
              />
              {errors.description && (
                <p className="text-xs text-destructive">
                  {errors.description.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="topic-domain">领域（可选）</Label>
              <Input
                id="topic-domain"
                placeholder="例如：人工智能、半导体"
                {...register("domain")}
              />
              {errors.domain && (
                <p className="text-xs text-destructive">
                  {errors.domain.message}
                </p>
              )}
            </div>
          </div>
          <DialogFooter className="mt-6">
            <DialogClose
              render={<Button variant="outline" type="button" />}
            >
              取消
            </DialogClose>
            <Button type="submit" disabled={submitting}>
              {submitting && <Loader2 className="mr-2 size-4 animate-spin" />}
              {isEdit ? "保存" : "创建"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
