"use client";

import { useState } from "react";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  Loader2,
  BrainCircuit,
  Search,
  Plus,
  Check,
  X,
  Archive,
  RotateCcw,
  ChevronDown,
  ChevronRight,
  Package,
  History,
  Star,
  Percent,
  Calendar,
  KeyRound,
  Clock,
  Layers,
  Sparkles,
  FileText,
} from "lucide-react";
import { agentMemoryApi, ApiRequestError } from "@/lib/api";
import type {
  AgentMemoryItem,
  AgentMemoryEvidence,
  AgentMemoryCheckpoint,
  ContextPackDto,
  ContextLayerDto,
  CaptureMemoryInput,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Tabs,
  TabsList,
  TabsTrigger,
  TabsContent,
} from "@/components/ui/tabs";
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

// ===== 工具函数 =====

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function truncate(text: string | undefined, max = 200): string {
  if (!text) return "";
  return text.length > max ? text.slice(0, max) + "…" : text;
}

// ===== 标签与状态映射 =====

const KIND_LABELS: Record<string, string> = {
  fact: "事实",
  preference: "偏好",
  decision: "决策",
  goal: "目标",
  entity: "实体",
  event: "事件",
  relationship: "关系",
  note: "笔记",
  reflection: "反思",
};

function getKindLabel(kind: string): string {
  return KIND_LABELS[kind] ?? kind;
}

const ADMISSION_LABELS: Record<string, string> = {
  candidate: "候选",
  qualified: "合格",
  confirmed: "已确认",
  rejected: "已拒绝",
};

const ADMISSION_BADGE_CLASS: Record<string, string> = {
  candidate:
    "border-transparent bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  qualified:
    "border-transparent bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  confirmed:
    "border-transparent bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
  rejected:
    "border-transparent bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
};

function AdmissionBadge({ state }: { state: string }) {
  return (
    <Badge
      variant="outline"
      className={ADMISSION_BADGE_CLASS[state] ?? ""}
    >
      {ADMISSION_LABELS[state] ?? state}
    </Badge>
  );
}

const STATUS_LABELS: Record<string, string> = {
  active: "活跃",
  archived: "已归档",
  forgotten: "已遗忘",
};

const SESSION_STATUS_LABELS: Record<string, string> = {
  active: "进行中",
  closed: "已关闭",
};

const DELIVERY_STATE_LABELS: Record<string, string> = {
  draft: "草稿",
  ready: "就绪",
  delivered: "已投递",
  stale: "过期",
};

// ===== 重要性星星 =====

function ImportanceStars({ importance }: { importance: number }) {
  const clamped = Math.max(0, Math.min(10, importance));
  return (
    <div className="flex items-center gap-0.5" title={`重要性 ${clamped}/10`}>
      {[1, 2, 3, 4, 5].map((i) => {
        const filled = (clamped / 10) * 5 >= i - 0.5;
        return (
          <Star
            key={i}
            className={
              filled
                ? "size-3.5 fill-amber-400 text-amber-400"
                : "size-3.5 text-muted-foreground/40"
            }
          />
        );
      })}
      <span className="ml-1 text-xs text-muted-foreground">
        {clamped}/10
      </span>
    </div>
  );
}

// ===== 会话信息骨架 =====

function SessionHeaderSkeleton() {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-3">
          <Skeleton className="h-6 w-64" />
          <Skeleton className="h-5 w-16 rounded-full" />
        </div>
        <Skeleton className="mt-2 h-4 w-96" />
      </CardHeader>
      <CardContent>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-16 w-full" />
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

// ===== 上下文包图层卡片 =====

function ContextLayerCard({ layer }: { layer: ContextLayerDto }) {
  return (
    <Card size="sm">
      <CardHeader>
        <div className="flex items-start justify-between gap-2">
          <CardTitle className="text-sm">{layer.title || "未命名"}</CardTitle>
          <Badge variant="secondary" className="shrink-0">
            {layer.type}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-2">
        {layer.content && (
          <p className="whitespace-pre-wrap break-words text-xs leading-relaxed text-muted-foreground">
            {layer.content}
          </p>
        )}
        <div className="flex flex-wrap items-center gap-2">
          {layer.confidence != null && (
            <Badge variant="outline" className="gap-1">
              <Percent className="size-3" />
              {Math.round(layer.confidence * 100)}%
            </Badge>
          )}
          {layer.admissionState && (
            <AdmissionBadge state={layer.admissionState} />
          )}
          {layer.evidenceRef && (
            <span className="text-xs text-muted-foreground">
              证据: {truncate(layer.evidenceRef, 40)}
            </span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

function ContextLayerSection({
  label,
  layers,
  accent,
}: {
  label: string;
  layers: ContextLayerDto[];
  accent: string;
}) {
  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <span
          className={cnBadge(accent)}
        >
          {label}
        </span>
        <span className="text-xs text-muted-foreground">
          ({layers.length} 项)
        </span>
      </div>
      {layers.length === 0 ? (
        <p className="rounded-lg border border-dashed py-6 text-center text-xs text-muted-foreground">
          暂无内容
        </p>
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {layers.map((layer, idx) => (
            <ContextLayerCard key={idx} layer={layer} />
          ))}
        </div>
      )}
    </div>
  );
}

// badge-like inline label for layer headers
function cnBadge(accent: string) {
  return `inline-flex h-5 items-center rounded-full px-2 text-xs font-medium ${accent}`;
}

// ===== 记忆条目卡片 =====

interface MemoryItemCardProps {
  item: AgentMemoryItem;
  sessionId: string;
}

function MemoryItemCard({ item, sessionId }: MemoryItemCardProps) {
  const queryClient = useQueryClient();
  const [expanded, setExpanded] = useState(false);

  // 懒加载证据
  const { data: evidence, isLoading: evidenceLoading } = useQuery({
    queryKey: ["agent-memory", "evidence", item.id],
    queryFn: () => agentMemoryApi.getEvidence(item.id),
    enabled: expanded,
  });

  const invalidateItems = () => {
    queryClient.invalidateQueries({ queryKey: ["agent-memory", "items", sessionId] });
  };

  const confirmMutation = useMutation({
    mutationFn: (action: "confirm" | "reject") =>
      agentMemoryApi.confirmMemory(item.id, action),
    onSuccess: () => {
      toast.success("操作成功");
      invalidateItems();
    },
    onError: (err) => {
      toast.error(err instanceof ApiRequestError ? err.message : "操作失败");
    },
  });

  const archiveMutation = useMutation({
    mutationFn: () => agentMemoryApi.archiveMemory(item.id),
    onSuccess: () => {
      toast.success("已归档");
      invalidateItems();
    },
    onError: (err) => {
      toast.error(err instanceof ApiRequestError ? err.message : "归档失败");
    },
  });

  const restoreMutation = useMutation({
    mutationFn: () => agentMemoryApi.restoreMemory(item.id),
    onSuccess: () => {
      toast.success("已恢复");
      invalidateItems();
    },
    onError: (err) => {
      toast.error(err instanceof ApiRequestError ? err.message : "恢复失败");
    },
  });

  const showConfirmReject =
    item.admissionState === "candidate" ||
    item.admissionState === "qualified";
  const showArchive = item.admissionState === "confirmed" && item.status === "active";
  const showRestore = item.status === "archived";

  return (
    <Card size="sm">
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <CardTitle className="text-sm">{item.title}</CardTitle>
              <Badge variant="secondary">{getKindLabel(item.kind)}</Badge>
              <AdmissionBadge state={item.admissionState} />
              {item.status !== "active" && (
                <Badge variant="outline" className="border-transparent bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-300">
                  {STATUS_LABELS[item.status] ?? item.status}
                </Badge>
              )}
            </div>
            {item.summary && (
              <CardDescription className="mt-1">
                {item.summary}
              </CardDescription>
            )}
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {/* 指标行 */}
        <div className="flex flex-wrap items-center gap-4 text-xs text-muted-foreground">
          <span className="inline-flex items-center gap-1">
            <Percent className="size-3.5" />
            置信度 {Math.round((item.confidence ?? 0) * 100)}%
          </span>
          <span className="inline-flex items-center gap-1">
            <Star className="size-3.5" />
            <ImportanceStars importance={item.importance ?? 0} />
          </span>
          <span className="inline-flex items-center gap-1">
            <Calendar className="size-3.5" />
            {formatDate(item.createdAt)}
          </span>
        </div>

        {/* 内容 */}
        {item.content && (
          <p className="whitespace-pre-wrap break-words text-xs leading-relaxed text-foreground/80">
            {expanded ? item.content : truncate(item.content)}
          </p>
        )}

        {/* 证据展开区 */}
        {expanded && (
          <div className="rounded-lg border bg-muted/30 p-3">
            <p className="mb-2 flex items-center gap-1.5 text-xs font-medium">
              <Sparkles className="size-3.5" />
              证据链
            </p>
            {evidenceLoading ? (
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <Loader2 className="size-3.5 animate-spin" />
                加载中...
              </div>
            ) : evidence && evidence.length > 0 ? (
              <ul className="space-y-1.5">
                {evidence.map((ev: AgentMemoryEvidence) => (
                  <li
                    key={ev.id}
                    className="flex flex-wrap items-center gap-2 text-xs"
                  >
                    <Badge variant="outline">{ev.evidenceKind}</Badge>
                    <span className="truncate text-muted-foreground">
                      {ev.referenceId}
                    </span>
                    {ev.relation && (
                      <span className="text-muted-foreground/70">
                        · {ev.relation}
                      </span>
                    )}
                    <span className="ml-auto text-muted-foreground/60">
                      {formatDate(ev.capturedAt)}
                    </span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-xs text-muted-foreground">暂无证据</p>
            )}
          </div>
        )}

        <Separator />

        {/* 操作按钮 */}
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setExpanded((v) => !v)}
          >
            {expanded ? (
              <ChevronDown className="mr-1 size-3.5" />
            ) : (
              <ChevronRight className="mr-1 size-3.5" />
            )}
            详情
          </Button>

          {showConfirmReject && (
            <>
              <Button
                size="sm"
                disabled={confirmMutation.isPending}
                onClick={() => confirmMutation.mutate("confirm")}
              >
                {confirmMutation.isPending && confirmMutation.variables === "confirm" ? (
                  <Loader2 className="mr-1 size-3.5 animate-spin" />
                ) : (
                  <Check className="mr-1 size-3.5" />
                )}
                确认
              </Button>
              <Button
                variant="destructive"
                size="sm"
                disabled={confirmMutation.isPending}
                onClick={() => confirmMutation.mutate("reject")}
              >
                {confirmMutation.isPending && confirmMutation.variables === "reject" ? (
                  <Loader2 className="mr-1 size-3.5 animate-spin" />
                ) : (
                  <X className="mr-1 size-3.5" />
                )}
                拒绝
              </Button>
            </>
          )}

          {showArchive && (
            <Button
              variant="outline"
              size="sm"
              disabled={archiveMutation.isPending}
              onClick={() => archiveMutation.mutate()}
            >
              {archiveMutation.isPending ? (
                <Loader2 className="mr-1 size-3.5 animate-spin" />
              ) : (
                <Archive className="mr-1 size-3.5" />
              )}
              归档
            </Button>
          )}

          {showRestore && (
            <Button
              variant="outline"
              size="sm"
              disabled={restoreMutation.isPending}
              onClick={() => restoreMutation.mutate()}
            >
              {restoreMutation.isPending ? (
                <Loader2 className="mr-1 size-3.5 animate-spin" />
              ) : (
                <RotateCcw className="mr-1 size-3.5" />
              )}
              恢复
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

// ===== 捕获记忆弹窗 =====

interface CaptureDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sessionId: string;
}

function CaptureDialog({ open, onOpenChange, sessionId }: CaptureDialogProps) {
  const queryClient = useQueryClient();
  const [kind, setKind] = useState("fact");
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  const [summary, setSummary] = useState("");
  const [importance, setImportance] = useState("5");
  const [confidence, setConfidence] = useState("0.8");
  const [visibility, setVisibility] = useState("private");

  const resetForm = () => {
    setKind("fact");
    setTitle("");
    setContent("");
    setSummary("");
    setImportance("5");
    setConfidence("0.8");
    setVisibility("private");
  };

  const captureMutation = useMutation({
    mutationFn: (data: CaptureMemoryInput) =>
      agentMemoryApi.captureMemory(data),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["agent-memory", "items", sessionId],
      });
    },
  });

  const handleSubmit = async () => {
    if (!title.trim()) {
      toast.error("请输入标题");
      return;
    }
    const confidenceVal = Number(confidence);
    const importanceVal = Number(importance);
    if (Number.isNaN(confidenceVal) || confidenceVal < 0 || confidenceVal > 1) {
      toast.error("置信度需在 0-1 之间");
      return;
    }
    if (Number.isNaN(importanceVal) || importanceVal < 1 || importanceVal > 10) {
      toast.error("重要性需在 1-10 之间");
      return;
    }
    try {
      await captureMutation.mutateAsync({
        sessionId,
        kind,
        title: title.trim(),
        content: content.trim() || undefined,
        summary: summary.trim() || undefined,
        confidence: confidenceVal,
        importance: importanceVal,
        visibility,
      });
      toast.success("记忆已捕获");
      resetForm();
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof ApiRequestError ? err.message : "捕获失败");
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(v) => {
        if (!v) resetForm();
        onOpenChange(v);
      }}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>捕获记忆</DialogTitle>
          <DialogDescription>
            手动写入一条记忆条目到当前会话。
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="capture-kind">类型</Label>
              <Select value={kind} onValueChange={(v) => setKind(v as string)}>
                <SelectTrigger id="capture-kind" className="w-full">
                  <SelectValue placeholder="选择类型" />
                </SelectTrigger>
                <SelectContent>
                  {Object.entries(KIND_LABELS).map(([value, label]) => (
                    <SelectItem key={value} value={value}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="capture-visibility">可见性</Label>
              <Select value={visibility} onValueChange={(v) => setVisibility(v as string)}>
                <SelectTrigger id="capture-visibility" className="w-full">
                  <SelectValue placeholder="选择可见性" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="private">私有</SelectItem>
                  <SelectItem value="workspace">工作区</SelectItem>
                  <SelectItem value="public">公开</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="capture-title">标题</Label>
            <Input
              id="capture-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="简短描述这条记忆"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="capture-summary">摘要（可选）</Label>
            <Input
              id="capture-summary"
              value={summary}
              onChange={(e) => setSummary(e.target.value)}
              placeholder="一句话总结"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="capture-content">内容（可选）</Label>
            <Textarea
              id="capture-content"
              value={content}
              onChange={(e) => setContent(e.target.value)}
              placeholder="详细内容"
              className="min-h-24"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="capture-importance">重要性 (1-10)</Label>
              <Input
                id="capture-importance"
                type="number"
                min={1}
                max={10}
                value={importance}
                onChange={(e) => setImportance(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="capture-confidence">置信度 (0-1)</Label>
              <Input
                id="capture-confidence"
                type="number"
                min={0}
                max={1}
                step={0.05}
                value={confidence}
                onChange={(e) => setConfidence(e.target.value)}
              />
            </div>
          </div>
        </div>

        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            取消
          </DialogClose>
          <Button
            onClick={handleSubmit}
            disabled={captureMutation.isPending}
          >
            {captureMutation.isPending && (
              <Loader2 className="mr-1.5 size-3.5 animate-spin" />
            )}
            捕获
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ===== 检查点卡片 =====

function CheckpointCard({ checkpoint }: { checkpoint: AgentMemoryCheckpoint }) {
  return (
    <Card size="sm">
      <CardHeader>
        <div className="flex items-start justify-between gap-2">
          <CardTitle className="text-sm">
            {checkpoint.summary || `检查点 #${checkpoint.version}`}
          </CardTitle>
          <Badge variant="outline" className="shrink-0">
            {DELIVERY_STATE_LABELS[checkpoint.deliveryState] ??
              checkpoint.deliveryState}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-2 text-xs text-muted-foreground">
        <div className="flex flex-wrap gap-4">
          <span className="inline-flex items-center gap-1">
            <Layers className="size-3.5" />
            序列 {checkpoint.fromSequence} - {checkpoint.toSequence}
          </span>
          <span className="inline-flex items-center gap-1">
            <Percent className="size-3.5" />
            预估 Token {checkpoint.tokenEstimate}
          </span>
          <span className="inline-flex items-center gap-1">
            <Calendar className="size-3.5" />
            {formatDate(checkpoint.createdAt)}
          </span>
        </div>
        {checkpoint.openLoopsJson && (
          <div className="rounded-md bg-muted/40 p-2">
            <span className="font-medium text-foreground/70">开放循环:</span>{" "}
            <span className="break-words">{checkpoint.openLoopsJson}</span>
          </div>
        )}
        {checkpoint.decisionsJson && (
          <div className="rounded-md bg-muted/40 p-2">
            <span className="font-medium text-foreground/70">决策:</span>{" "}
            <span className="break-words">{checkpoint.decisionsJson}</span>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

// ===== 主页面 =====

export default function AgentMemorySessionDetailPage() {
  const params = useParams();
  const router = useRouter();
  const queryClient = useQueryClient();
  const sessionId = params.id as string;

  const [activeTab, setActiveTab] = useState<string>("items");
  const [searchQuery, setSearchQuery] = useState("");
  const [captureOpen, setCaptureOpen] = useState(false);
  const [contextPack, setContextPack] = useState<ContextPackDto | null>(null);

  // 会话详情
  const { data: session, isLoading: sessionLoading } = useQuery({
    queryKey: ["agent-memory", "session", sessionId],
    queryFn: () => agentMemoryApi.getSession(sessionId),
    enabled: !!sessionId,
  });

  // 记忆条目搜索
  const { data: memoryItems, isLoading: itemsLoading } = useQuery({
    queryKey: ["agent-memory", "items", sessionId, searchQuery],
    queryFn: () =>
      agentMemoryApi.searchMemory({
        query: searchQuery,
        sessionId,
        limit: 50,
        offset: 0,
      }),
    enabled: !!sessionId,
  });

  // 检查点列表
  const { data: checkpoints, isLoading: checkpointsLoading } = useQuery({
    queryKey: ["agent-memory", "checkpoints", sessionId],
    queryFn: () => agentMemoryApi.listCheckpoints(sessionId),
    enabled: !!sessionId,
  });

  // 生成上下文包
  const contextMutation = useMutation({
    mutationFn: () => agentMemoryApi.getContext(sessionId),
    onSuccess: (data) => {
      setContextPack(data);
      toast.success("上下文包已生成");
    },
    onError: (err) => {
      toast.error(err instanceof ApiRequestError ? err.message : "生成上下文包失败");
    },
  });

  // 创建检查点
  const checkpointMutation = useMutation({
    mutationFn: () => agentMemoryApi.createCheckpoint(sessionId),
    onSuccess: () => {
      toast.success("检查点已创建");
      queryClient.invalidateQueries({
        queryKey: ["agent-memory", "checkpoints", sessionId],
      });
    },
    onError: (err) => {
      toast.error(err instanceof ApiRequestError ? err.message : "创建检查点失败");
    },
  });

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    queryClient.invalidateQueries({
      queryKey: ["agent-memory", "items", sessionId, searchQuery],
    });
  };

  if (sessionLoading) {
    return (
      <div className="space-y-6">
        <Button variant="ghost" size="sm" onClick={() => router.push("/agent-memory")}>
          <ArrowLeft className="mr-2 size-4" />
          返回 Agent 记忆
        </Button>
        <SessionHeaderSkeleton />
      </div>
    );
  }

  if (!session) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <BrainCircuit className="mb-4 size-12 text-muted-foreground/50" />
        <p className="text-lg font-medium">会话不存在</p>
        <Button
          variant="outline"
          className="mt-4"
          onClick={() => router.push("/agent-memory")}
        >
          返回 Agent 记忆
        </Button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* 返回链接 */}
      <Button variant="ghost" size="sm" onClick={() => router.push("/agent-memory")}>
        <ArrowLeft className="mr-2 size-4" />
        返回 Agent 记忆
      </Button>

      {/* 会话信息头 */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <CardTitle className="text-xl">{session.taskTitle}</CardTitle>
                <Badge
                  variant={session.status === "active" ? "default" : "secondary"}
                >
                  {SESSION_STATUS_LABELS[session.status] ?? session.status}
                </Badge>
              </div>
              <CardDescription className="mt-1 flex items-center gap-1.5">
                <KeyRound className="size-3.5" />
                {session.externalSessionKey}
              </CardDescription>
            </div>
            <Button size="sm" onClick={() => setCaptureOpen(true)}>
              <Plus className="mr-1.5 size-3.5" />
              捕获记忆
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <Calendar className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  开始时间
                </p>
                <p className="mt-0.5 text-sm">{formatDate(session.startedAt)}</p>
              </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <Clock className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  最近活跃
                </p>
                <p className="mt-0.5 text-sm">{formatDate(session.lastActiveAt)}</p>
              </div>
            </div>
            {session.agentProfileId && (
              <div className="flex items-center gap-3 rounded-lg border p-3">
                <BrainCircuit className="size-4 text-muted-foreground" />
                <div className="min-w-0">
                  <p className="text-xs font-medium text-muted-foreground">
                    Agent Profile
                  </p>
                  <p className="mt-0.5 truncate text-sm">
                    {session.agentProfileId}
                  </p>
                </div>
              </div>
            )}
            {session.topicId && (
              <div className="flex items-center gap-3 rounded-lg border p-3">
                <FileText className="size-4 text-muted-foreground" />
                <div className="min-w-0">
                  <p className="text-xs font-medium text-muted-foreground">
                    关联专题
                  </p>
                  <Link
                    href={`/topics/${session.topicId}`}
                    className="mt-0.5 block truncate text-sm text-primary hover:underline"
                  >
                    {session.topicId}
                  </Link>
                </div>
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      {/* Tabs: 记忆条目 / 上下文包 */}
      <Tabs value={activeTab} onValueChange={(v) => setActiveTab(v as string)}>
        <TabsList>
          <TabsTrigger value="items">记忆条目</TabsTrigger>
          <TabsTrigger value="context">上下文包</TabsTrigger>
        </TabsList>

        {/* 记忆条目 */}
        <TabsContent value="items" className="space-y-4">
          {/* 搜索栏 */}
          <form onSubmit={handleSearch} className="flex gap-2">
            <div className="relative flex-1">
              <Search className="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="搜索记忆条目（标题、内容、摘要）..."
                className="pl-8"
              />
            </div>
            <Button type="submit" variant="outline" size="default">
              <Search className="mr-1.5 size-3.5" />
              搜索
            </Button>
            <Button type="button" size="default" onClick={() => setCaptureOpen(true)}>
              <Plus className="mr-1.5 size-3.5" />
              捕获记忆
            </Button>
          </form>

          {/* 列表 */}
          {itemsLoading ? (
            <div className="grid gap-3 md:grid-cols-2">
              {[0, 1, 2, 3].map((i) => (
                <Card key={i} size="sm">
                  <CardHeader>
                    <Skeleton className="h-5 w-3/4" />
                    <Skeleton className="mt-1 h-4 w-1/2" />
                  </CardHeader>
                  <CardContent className="space-y-2">
                    <Skeleton className="h-3 w-full" />
                    <Skeleton className="h-3 w-5/6" />
                    <Skeleton className="h-8 w-40" />
                  </CardContent>
                </Card>
              ))}
            </div>
          ) : memoryItems && memoryItems.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2">
              {memoryItems.map((item) => (
                <MemoryItemCard key={item.id} item={item} sessionId={sessionId} />
              ))}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center">
              <BrainCircuit className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                {searchQuery ? "未找到匹配的记忆条目" : "暂无记忆条目，点击「捕获记忆」添加"}
              </p>
            </div>
          )}
        </TabsContent>

        {/* 上下文包 */}
        <TabsContent value="context" className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h3 className="text-base font-medium">上下文包</h3>
              <p className="text-xs text-muted-foreground">
                按三层结构（L1/L2/L3）组装投递给 Agent 的上下文。
              </p>
            </div>
            <Button
              onClick={() => contextMutation.mutate()}
              disabled={contextMutation.isPending}
            >
              {contextMutation.isPending ? (
                <Loader2 className="mr-1.5 size-3.5 animate-spin" />
              ) : (
                <Package className="mr-1.5 size-3.5" />
              )}
              生成上下文包
            </Button>
          </div>

          {contextMutation.isPending && !contextPack ? (
            <div className="space-y-4">
              <Skeleton className="h-20 w-full" />
              <Skeleton className="h-40 w-full" />
              <Skeleton className="h-40 w-full" />
            </div>
          ) : contextPack ? (
            <div className="space-y-5">
              {/* Token 预算 */}
              <Card size="sm">
                <CardContent className="flex items-center gap-6 py-3">
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-medium text-muted-foreground">
                      Token 预算
                    </span>
                    <span className="text-sm font-semibold">
                      {contextPack.tokenBudget.toLocaleString()}
                    </span>
                  </div>
                  <Separator orientation="vertical" className="h-6" />
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-medium text-muted-foreground">
                      已用 Token
                    </span>
                    <span className="text-sm font-semibold">
                      {contextPack.tokenUsed.toLocaleString()}
                    </span>
                  </div>
                  {contextPack.tokenBudget > 0 && (
                    <div className="ml-auto flex items-center gap-2">
                      <div className="h-2 w-32 overflow-hidden rounded-full bg-muted">
                        <div
                          className="h-full rounded-full bg-primary"
                          style={{
                            width: `${Math.min(
                              100,
                              (contextPack.tokenUsed / contextPack.tokenBudget) * 100
                            )}%`,
                          }}
                        />
                      </div>
                      <span className="text-xs text-muted-foreground">
                        {Math.round(
                          (contextPack.tokenUsed / contextPack.tokenBudget) * 100
                        )}
                        %
                      </span>
                    </div>
                  )}
                </CardContent>
              </Card>

              <ContextLayerSection
                label="L1 · 核心记忆"
                layers={contextPack.L1 ?? []}
                accent="bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300"
              />
              <ContextLayerSection
                label="L2 · 相关记忆"
                layers={contextPack.L2 ?? []}
                accent="bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300"
              />
              <ContextLayerSection
                label="L3 · 背景记忆"
                layers={contextPack.L3 ?? []}
                accent="bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300"
              />
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center">
              <Package className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                点击「生成上下文包」按钮组装上下文
              </p>
            </div>
          )}
        </TabsContent>
      </Tabs>

      {/* 检查点 */}
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <History className="size-4 text-muted-foreground" />
              <CardTitle className="text-base">检查点</CardTitle>
              <span className="text-sm font-normal text-muted-foreground">
                ({checkpoints?.length ?? 0})
              </span>
            </div>
            <Button
              size="sm"
              onClick={() => checkpointMutation.mutate()}
              disabled={checkpointMutation.isPending}
            >
              {checkpointMutation.isPending ? (
                <Loader2 className="mr-1.5 size-3.5 animate-spin" />
              ) : (
                <Plus className="mr-1.5 size-3.5" />
              )}
              创建检查点
            </Button>
          </div>
          <CardDescription>
            为会话创建快照，便于回溯开放循环与决策。
          </CardDescription>
        </CardHeader>
        <CardContent>
          {checkpointsLoading ? (
            <div className="grid gap-3 md:grid-cols-2">
              {[0, 1].map((i) => (
                <Skeleton key={i} className="h-28 w-full" />
              ))}
            </div>
          ) : checkpoints && checkpoints.length > 0 ? (
            <ScrollArea className="max-h-[480px]">
              <div className="grid gap-3 pr-2 md:grid-cols-2">
                {checkpoints.map((cp) => (
                  <CheckpointCard key={cp.id} checkpoint={cp} />
                ))}
              </div>
            </ScrollArea>
          ) : (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <History className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无检查点，点击「创建检查点」生成快照
              </p>
            </div>
          )}
        </CardContent>
      </Card>

      {/* 捕获记忆弹窗 */}
      <CaptureDialog
        open={captureOpen}
        onOpenChange={setCaptureOpen}
        sessionId={sessionId}
      />
    </div>
  );
}
