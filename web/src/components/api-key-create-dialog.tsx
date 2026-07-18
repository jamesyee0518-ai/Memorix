"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import {
  Loader2,
  Plus,
  Copy,
  Check,
  AlertTriangle,
  CheckCircle2,
} from "lucide-react";
import { useTopicStore } from "@/stores/topic-store";
import { apiKeyApi, ApiRequestError } from "@/lib/api";
import type {
  PermissionScope,
  CreateApiKeyResponse,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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

const permissionScopeOptions: {
  value: PermissionScope;
  label: string;
  description: string;
}[] = [
  {
    value: "search_only",
    label: "仅搜索",
    description: "只能调用搜索接口",
  },
  {
    value: "qa_only",
    label: "仅问答",
    description: "只能调用问答接口",
  },
  {
    value: "full_read",
    label: "完整读取",
    description: "可调用所有读取接口",
  },
];

const allActions = [
  { value: "topics:list", label: "专题列表" },
  { value: "search:query", label: "搜索查询" },
  { value: "qa:ask", label: "问答提问" },
  { value: "documents:read", label: "文档读取" },
  { value: "reports:read", label: "报告读取" },
];

interface ApiKeyCreateDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
}

export function ApiKeyCreateDialog({
  open,
  onOpenChange,
  onSuccess,
}: ApiKeyCreateDialogProps) {
  const { topics, fetchTopics } = useTopicStore();

  // 表单状态
  const [name, setName] = useState("");
  const [permissionScope, setPermissionScope] =
    useState<PermissionScope>("full_read");
  const [allowedTopicIds, setAllowedTopicIds] = useState<string[]>([]);
  const [allowedActions, setAllowedActions] = useState<string[]>([
    "topics:list",
    "search:query",
    "qa:ask",
    "documents:read",
    "reports:read",
  ]);
  const [expiresAt, setExpiresAt] = useState("");
  const [submitting, setSubmitting] = useState(false);

  // 创建成功后的 Key 显示
  const [createdKey, setCreatedKey] = useState<CreateApiKeyResponse | null>(
    null
  );
  const [copied, setCopied] = useState(false);

  // 加载专题列表
  useEffect(() => {
    if (open && topics.length === 0) {
      fetchTopics().catch(() => {});
    }
  }, [open, topics.length, fetchTopics]);

  // 关闭时重置
  useEffect(() => {
    if (!open) {
      setName("");
      setPermissionScope("full_read");
      setAllowedTopicIds([]);
      setAllowedActions([
        "topics:list",
        "search:query",
        "qa:ask",
        "documents:read",
        "reports:read",
      ]);
      setExpiresAt("");
      setCreatedKey(null);
      setCopied(false);
    }
  }, [open]);

  const toggleTopic = (topicId: string) => {
    setAllowedTopicIds((prev) =>
      prev.includes(topicId)
        ? prev.filter((id) => id !== topicId)
        : [...prev, topicId]
    );
  };

  const toggleAction = (action: string) => {
    setAllowedActions((prev) =>
      prev.includes(action)
        ? prev.filter((a) => a !== action)
        : [...prev, action]
    );
  };

  const handleCopy = async () => {
    if (!createdKey) return;
    try {
      await navigator.clipboard.writeText(createdKey.apiKey);
      setCopied(true);
      toast.success("已复制到剪贴板");
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error("复制失败，请手动复制");
    }
  };

  const handleSubmit = async () => {
    if (!name.trim()) {
      toast.error("请输入 API Key 名称");
      return;
    }

    setSubmitting(true);
    try {
      const response = await apiKeyApi.create({
        name: name.trim(),
        permissionScope,
        allowedTopicIds: allowedTopicIds.length > 0 ? allowedTopicIds : undefined,
        allowedActions: allowedActions.length > 0 ? allowedActions : undefined,
        expiresAt: expiresAt || undefined,
      });
      setCreatedKey(response);
      onSuccess();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "创建 API Key 失败";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(v) => {
        onOpenChange(v);
      }}
    >
      <DialogContent className="sm:max-w-lg">
        {createdKey ? (
          // 创建成功 - 显示明文 Key
          <>
            <DialogHeader>
              <DialogTitle className="flex items-center gap-2">
                <CheckCircle2 className="size-5 text-green-600" />
                API Key 创建成功
              </DialogTitle>
              <DialogDescription>
                请立即复制并妥善保存您的 API Key，关闭后将无法再次查看。
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-3">
              {/* 安全警告 */}
              <div className="flex items-start gap-2 rounded-lg border border-amber-300 bg-amber-50 p-3">
                <AlertTriangle className="mt-0.5 size-4 shrink-0 text-amber-600" />
                <p className="text-sm text-amber-800">
                  此 API Key 明文仅显示一次，关闭弹窗后将无法再次获取。请立即复制并保存到安全的地方。
                </p>
              </div>

              {/* Key 显示 */}
              <div className="space-y-2">
                <Label>API Key</Label>
                <div className="flex items-center gap-2 rounded-lg border border-green-300 bg-green-50 p-3">
                  <code className="flex-1 break-all text-sm font-mono text-green-800">
                    {createdKey.apiKey}
                  </code>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={handleCopy}
                    className="shrink-0"
                  >
                    {copied ? (
                      <Check className="mr-1 size-3.5 text-green-600" />
                    ) : (
                      <Copy className="mr-1 size-3.5" />
                    )}
                    {copied ? "已复制" : "复制"}
                  </Button>
                </div>
              </div>

              {/* Key 信息 */}
              <div className="grid grid-cols-2 gap-3 text-sm">
                <div>
                  <span className="text-muted-foreground">名称：</span>
                  <span className="font-medium">{createdKey.name}</span>
                </div>
                <div>
                  <span className="text-muted-foreground">前缀：</span>
                  <code className="font-mono">{createdKey.keyPrefix}...</code>
                </div>
              </div>
            </div>

            <DialogFooter>
              <Button onClick={() => onOpenChange(false)}>我已保存</Button>
            </DialogFooter>
          </>
        ) : (
          // 创建表单
          <>
            <DialogHeader>
              <DialogTitle className="flex items-center gap-2">
                <Plus className="size-5" />
                创建 API Key
              </DialogTitle>
              <DialogDescription>
                创建用于 Agent API 调用的密钥
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-4">
              {/* 名称 */}
              <div className="space-y-2">
                <Label htmlFor="apikey-name">
                  名称 <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="apikey-name"
                  placeholder="例如：生产环境调用密钥"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  maxLength={50}
                />
              </div>

              {/* 权限范围 */}
              <div className="space-y-2">
                <Label>权限范围</Label>
                <Select
                  value={permissionScope}
                  onValueChange={(v) =>
                    setPermissionScope(v as PermissionScope)
                  }
                >
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="请选择权限范围" />
                  </SelectTrigger>
                  <SelectContent>
                    {permissionScopeOptions.map((opt) => (
                      <SelectItem key={opt.value} value={opt.value}>
                        {opt.label} - {opt.description}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              {/* 可访问专题 */}
              <div className="space-y-2">
                <Label>可访问专题（留空表示全部）</Label>
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
                        <input
                          type="checkbox"
                          checked={allowedTopicIds.includes(topic.id)}
                          onChange={() => toggleTopic(topic.id)}
                          className="size-4 rounded border-input accent-primary"
                        />
                        <span className="truncate">{topic.name}</span>
                      </label>
                    ))
                  )}
                </div>
              </div>

              {/* 可调用能力 */}
              <div className="space-y-2">
                <Label>可调用能力</Label>
                <div className="grid grid-cols-2 gap-1.5">
                  {allActions.map((action) => (
                    <label
                      key={action.value}
                      className="flex cursor-pointer items-center gap-2 rounded px-1 py-1 text-sm hover:bg-muted"
                    >
                      <input
                        type="checkbox"
                        checked={allowedActions.includes(action.value)}
                        onChange={() => toggleAction(action.value)}
                        className="size-4 rounded border-input accent-primary"
                      />
                      <span className="truncate">{action.label}</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* 过期时间 */}
              <div className="space-y-2">
                <Label htmlFor="apikey-expires">过期时间（可选）</Label>
                <Input
                  id="apikey-expires"
                  type="date"
                  value={expiresAt}
                  onChange={(e) => setExpiresAt(e.target.value)}
                />
                <p className="text-xs text-muted-foreground">
                  留空表示永不过期
                </p>
              </div>
            </div>

            <DialogFooter>
              <DialogClose render={<Button variant="outline" type="button" />}>
                取消
              </DialogClose>
              <Button onClick={handleSubmit} disabled={submitting}>
                {submitting && (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                )}
                创建
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
