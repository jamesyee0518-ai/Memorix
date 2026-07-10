"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Loader2 } from "lucide-react";
import { useTopicStore } from "@/stores/topic-store";
import { agentApi, ApiRequestError } from "@/lib/api";
import type { AgentProfile } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";

const allTools = [
  { value: "list_topics", label: "list_topics" },
  { value: "search_memory", label: "search_memory" },
  { value: "ask_memory", label: "ask_memory" },
  { value: "get_document", label: "get_document" },
  { value: "get_report", label: "get_report" },
];

interface AgentProfileDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  profile?: AgentProfile | null;
  onSuccess: () => void;
}

export function AgentProfileDialog({
  open,
  onOpenChange,
  profile,
  onSuccess,
}: AgentProfileDialogProps) {
  const { topics, fetchTopics } = useTopicStore();
  const isEdit = !!profile;

  // 表单状态
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [allowedToolNames, setAllowedToolNames] = useState<string[]>([
    "list_topics",
    "search_memory",
    "ask_memory",
    "get_document",
    "get_report",
  ]);
  const [allowedTopicIds, setAllowedTopicIds] = useState<string[]>([]);
  const [maxResultsPerCall, setMaxResultsPerCall] = useState(20);
  const [rateLimitPerMinute, setRateLimitPerMinute] = useState(60);
  const [dailyQuota, setDailyQuota] = useState(1000);
  const [allowSensitiveDocuments, setAllowSensitiveDocuments] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  // 加载专题列表
  useEffect(() => {
    if (open && topics.length === 0) {
      fetchTopics().catch(() => {});
    }
  }, [open, topics.length, fetchTopics]);

  // 打开时填充表单
  useEffect(() => {
    if (open) {
      if (profile) {
        setName(profile.name);
        setDescription(profile.description ?? "");
        setAllowedToolNames(profile.allowedToolNames ?? allTools.map((t) => t.value));
        setAllowedTopicIds(profile.allowedTopicIds ?? []);
        setMaxResultsPerCall(profile.maxResultsPerCall);
        setRateLimitPerMinute(profile.rateLimitPerMinute);
        setDailyQuota(profile.dailyQuota);
        setAllowSensitiveDocuments(profile.allowSensitiveDocuments);
      } else {
        setName("");
        setDescription("");
        setAllowedToolNames(allTools.map((t) => t.value));
        setAllowedTopicIds([]);
        setMaxResultsPerCall(20);
        setRateLimitPerMinute(60);
        setDailyQuota(1000);
        setAllowSensitiveDocuments(false);
      }
    }
  }, [open, profile]);

  const toggleTool = (tool: string) => {
    setAllowedToolNames((prev) =>
      prev.includes(tool) ? prev.filter((t) => t !== tool) : [...prev, tool]
    );
  };

  const toggleTopic = (topicId: string) => {
    setAllowedTopicIds((prev) =>
      prev.includes(topicId)
        ? prev.filter((id) => id !== topicId)
        : [...prev, topicId]
    );
  };

  const handleSubmit = async () => {
    if (!name.trim()) {
      toast.error("请输入 Profile 名称");
      return;
    }

    setSubmitting(true);
    try {
      const data: Partial<AgentProfile> = {
        name: name.trim(),
        description: description.trim() || undefined,
        allowedToolNames,
        allowedTopicIds: allowedTopicIds.length > 0 ? allowedTopicIds : undefined,
        maxResultsPerCall,
        rateLimitPerMinute,
        dailyQuota,
        allowSensitiveDocuments,
        transport: "stdio",
      };

      if (isEdit && profile) {
        await agentApi.updateProfile(profile.id, data);
        toast.success("Agent Profile 已更新");
      } else {
        await agentApi.createProfile(data);
        toast.success("Agent Profile 已创建");
      }
      onOpenChange(false);
      onSuccess();
    } catch (err) {
      const message =
        err instanceof ApiRequestError
          ? err.message
          : isEdit
            ? "更新 Agent Profile 失败"
            : "创建 Agent Profile 失败";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {isEdit ? "编辑 Agent Profile" : "新建 Agent Profile"}
          </DialogTitle>
          <DialogDescription>
            {isEdit
              ? "修改 Agent Profile 的配置信息"
              : "创建新的 Agent Profile，用于 MCP 接入"}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {/* 名称 */}
          <div className="space-y-2">
            <Label htmlFor="agent-name">
              名称 <span className="text-destructive">*</span>
            </Label>
            <Input
              id="agent-name"
              placeholder="例如：Claude Desktop Agent"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={100}
            />
          </div>

          {/* 描述 */}
          <div className="space-y-2">
            <Label htmlFor="agent-description">描述</Label>
            <Textarea
              id="agent-description"
              placeholder="Profile 用途说明（可选）"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          {/* 工具权限 */}
          <div className="space-y-2">
            <Label>工具权限</Label>
            <div className="grid grid-cols-2 gap-2 rounded-lg border p-3">
              {allTools.map((tool) => (
                <label
                  key={tool.value}
                  className="flex cursor-pointer items-center gap-2 text-sm"
                >
                  <Checkbox
                    checked={allowedToolNames.includes(tool.value)}
                    onCheckedChange={() => toggleTool(tool.value)}
                  />
                  <span className="font-mono text-xs">{tool.label}</span>
                </label>
              ))}
            </div>
          </div>

          {/* 专题权限 */}
          <div className="space-y-2">
            <Label>专题权限（留空表示全部）</Label>
            <div className="max-h-32 space-y-1.5 overflow-y-auto rounded-lg border p-2">
              {topics.length === 0 ? (
                <p className="py-2 text-center text-xs text-muted-foreground">
                  暂无专题
                </p>
              ) : (
                topics.map((topic) => (
                  <label
                    key={topic.id}
                    className="flex cursor-pointer items-center gap-2 rounded px-1 py-1 text-sm hover:bg-muted"
                  >
                    <Checkbox
                      checked={allowedTopicIds.includes(topic.id)}
                      onCheckedChange={() => toggleTopic(topic.id)}
                    />
                    <span className="truncate">{topic.name}</span>
                  </label>
                ))
              )}
            </div>
          </div>

          {/* 数值配置 */}
          <div className="grid grid-cols-3 gap-3">
            <div className="space-y-2">
              <Label htmlFor="agent-max-results">最大结果数</Label>
              <Input
                id="agent-max-results"
                type="number"
                min={1}
                max={100}
                value={maxResultsPerCall}
                onChange={(e) => setMaxResultsPerCall(Number(e.target.value))}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="agent-rate-limit">每分钟限制</Label>
              <Input
                id="agent-rate-limit"
                type="number"
                min={1}
                value={rateLimitPerMinute}
                onChange={(e) => setRateLimitPerMinute(Number(e.target.value))}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="agent-daily-quota">每日配额</Label>
              <Input
                id="agent-daily-quota"
                type="number"
                min={1}
                value={dailyQuota}
                onChange={(e) => setDailyQuota(Number(e.target.value))}
              />
            </div>
          </div>

          {/* 敏感文档 */}
          <div className="flex items-center gap-2 rounded-lg border p-3">
            <Checkbox
              checked={allowSensitiveDocuments}
              onCheckedChange={(v) => setAllowSensitiveDocuments(!!v)}
            />
            <div>
              <Label className="cursor-pointer">允许访问敏感文档</Label>
              <p className="text-xs text-muted-foreground">
                开启后 Agent 可读取标记为敏感的文档
              </p>
            </div>
          </div>
        </div>

        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            取消
          </DialogClose>
          <Button onClick={handleSubmit} disabled={submitting}>
            {submitting && <Loader2 className="mr-2 size-4 animate-spin" />}
            {isEdit ? "保存" : "创建"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
