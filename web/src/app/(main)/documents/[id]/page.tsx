"use client";

import { useState, useEffect, useRef } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import Link from "next/link";
import {
  ArrowLeft,
  Loader2,
  FileText,
  RefreshCw,
  AlertCircle,
  Tag as TagIcon,
  Boxes,
  Cpu,
  Clock,
  Hash,
  Lightbulb,
  TrendingUp,
  Wrench,
  ShieldAlert,
  Target,
  Recycle,
  Gauge,
  Quote,
  ListChecks,
  FileDown,
  Globe,
  User,
  Calendar,
  CheckCircle,
  XCircle,
  Plus,
  X,
  Scissors,
  Zap,
  Users,
  Database,
} from "lucide-react";
import { documentApi, sourceApi, exportApi, tagApi, entityApi, actionApi, ApiRequestError } from "@/lib/api";
import type { DocumentTagItem } from "@/lib/types";
import { Markdown } from "@/components/markdown";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  AiStatusBadge,
  ValueScoreBar,
  EntityTypeBadge,
} from "@/components/ai-badge";
import {
  Tabs,
  TabsList,
  TabsTrigger,
  TabsContent,
} from "@/components/ui/tabs";
import { ExportStatusDialog } from "@/components/export-status-dialog";
import { ChunkDebugger } from "@/components/chunk-debugger";
import { IndexStatePanel } from "@/components/index-state-panel";

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

function parseJsonArray<T>(jsonStr?: string): T[] {
  if (!jsonStr) return [];
  try {
    const parsed = JSON.parse(jsonStr);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

const ERROR_CODE_MESSAGES: Record<string, string> = {
  FETCH_TIMEOUT: "请求超时，目标网站响应时间过长。",
  FETCH_FORBIDDEN: "网页禁止访问，目标网站可能禁止自动抓取。",
  FETCH_NOT_FOUND: "网页不存在（404），链接可能已失效。",
  FETCH_TOO_LARGE: "网页内容过大，超过系统限制。",
  PARSE_EMPTY_CONTENT: "未能从资料中提取到有效内容。",
  PARSE_UNSUPPORTED_TYPE: "不支持的资料类型。",
  PARSE_PDF_SCANNED: "该 PDF 可能是扫描版，当前版本暂未启用 OCR，无法完整解析。",
  PARSE_PDF_TOO_LARGE: "PDF 文件过大，超过系统限制。",
  CLEAN_FAILED: "内容清洗失败，请重试。",
  AI_MODEL_UNAVAILABLE: "AI 模型不可用，请检查模型配置。",
  AI_TIMEOUT: "AI 处理超时，请稍后重试。",
  AI_INVALID_JSON: "AI 返回格式不符合要求，可点击重试。",
  AI_CONTENT_TOO_LONG: "文档内容过长，超出 AI 处理限制。",
  DOCUMENT_CREATE_FAILED: "文档创建失败。",
  UNKNOWN_ERROR: "未知错误，请重试或联系支持。",
};

function getFriendlyErrorMessage(errorMessage?: string): string {
  if (!errorMessage) return "处理失败，请重试。";
  // 检查是否以错误码开头 [ERROR_CODE] message
  const match = errorMessage.match(/^\[([A-Z_]+)\]\s*(.*)$/);
  if (match) {
    const code = match[1];
    const detail = match[2];
    const friendly = ERROR_CODE_MESSAGES[code];
    return friendly ? `${friendly}${detail ? `（${detail}）` : ""}` : errorMessage;
  }
  return errorMessage;
}

// ===== 信号项类型 =====

interface SignalObject {
  type?: string;
  description?: string;
  confidence?: number;
}
type SignalItem = string | SignalObject;

interface KeyPointItem {
  text?: string;
  importance?: string | number;
}

// ===== 信号列表组件 =====

function SignalList({
  items,
  emptyText = "暂无数据",
}: {
  items: SignalItem[];
  emptyText?: string;
}) {
  if (items.length === 0) {
    return <p className="text-sm text-muted-foreground">{emptyText}</p>;
  }
  return (
    <ul className="space-y-2">
      {items.map((item, idx) => (
        <li
          key={idx}
          className="flex items-start gap-2 rounded-lg border bg-muted/30 p-3"
        >
          {typeof item !== "string" && item.type && (
            <span className="mt-0.5 shrink-0 rounded bg-blue-100 px-1.5 py-0.5 text-xs font-medium text-blue-700 dark:bg-blue-900/40 dark:text-blue-300">
              {item.type}
            </span>
          )}
          <span className="flex-1 text-sm">
            {typeof item === "string" ? item : item.description}
          </span>
          {typeof item !== "string" && item.confidence !== undefined && item.confidence !== null && (
            <span className="shrink-0 text-xs text-muted-foreground">
              置信度: {Math.round(item.confidence * 100)}%
            </span>
          )}
        </li>
      ))}
    </ul>
  );
}

// ===== 多阶段处理状态徽章 =====

function ProcessingStageBadge({ label, status }: { label: string; status: string }) {
  const colorMap: Record<string, string> = {
    pending: "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
    processing: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
    done: "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
    failed: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
    skipped: "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300",
  };
  const colorClass = colorMap[status] || colorMap.pending;
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${colorClass}`}>
      {label}: {status}
    </span>
  );
}

// ===== 处理日志组件 =====

function ProcessingLogs({ documentId }: { documentId: string }) {
  const { data: logs, isLoading } = useQuery({
    queryKey: ["processing-logs", documentId],
    queryFn: () => documentApi.getProcessingLogs(documentId),
    enabled: !!documentId,
  });

  if (isLoading) {
    return <Loader2 className="size-5 animate-spin text-muted-foreground" />;
  }

  if (!logs || logs.length === 0) {
    return <p className="text-sm text-muted-foreground">暂无处理日志</p>;
  }

  const statusColor: Record<string, string> = {
    started: "text-blue-600",
    success: "text-green-600",
    failed: "text-red-600",
    skipped: "text-yellow-600",
    retrying: "text-orange-600",
  };

  return (
    <div className="space-y-2">
      {logs.map((log) => (
        <div key={log.id} className="flex items-start gap-3 rounded-lg border p-3">
          <div className="flex-1">
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{log.stepName}</span>
              <span className={`text-xs font-medium ${statusColor[log.status] || "text-muted-foreground"}`}>
                {log.status}
              </span>
              {log.durationMs !== undefined && log.durationMs !== null && (
                <span className="text-xs text-muted-foreground">
                  {log.durationMs}ms
                </span>
              )}
            </div>
            {log.message && (
              <p className="mt-1 text-xs text-muted-foreground">{log.message}</p>
            )}
            {log.errorCode && (
              <p className="mt-1 text-xs font-mono text-red-600">
                {log.errorCode}: {log.message}
              </p>
            )}
            <p className="mt-1 text-xs text-muted-foreground">
              {formatDate(log.startedAt || log.createdAt)}
            </p>
          </div>
        </div>
      ))}
    </div>
  );
}

// ===== 主页面 =====

export default function DocumentDetailPage() {
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const documentId = params.id as string;

  const chunkIdFromQuery = searchParams.get("chunkId");

  const [exportOpen, setExportOpen] = useState(false);
  const [exportJobId, setExportJobId] = useState<string | null>(null);

  // 控制当前激活的 Tab（支持从 QA 引用跳转时切换到分块调试）
  const [activeTab, setActiveTab] = useState<string>("analysis");

  // Tag management state
  const [documentTags, setDocumentTags] = useState<DocumentTagItem[]>([]);
  const [showAddTag, setShowAddTag] = useState(false);
  const [newTagName, setNewTagName] = useState("");
  const previousPipelineState = useRef<string | null>(null);

  // Fetch document tags separately for interactive management
  const { data: documentTagsData, refetch: refetchTags } = useQuery({
    queryKey: ["document-tags", documentId],
    queryFn: () => tagApi.getDocumentTags(documentId),
    enabled: !!documentId,
  });

  useEffect(() => {
    if (documentTagsData) setDocumentTags(documentTagsData);
  }, [documentTagsData]);

  const { data: document, isLoading } = useQuery({
    queryKey: ["document", documentId],
    queryFn: () => documentApi.get(documentId),
    enabled: !!documentId,
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data) return false;
      const activeStatuses = new Set(["pending", "processing", "queued", "stale", "indexing"]);
      const hasActiveStage = [
        data.parseStatus,
        data.cleanStatus,
        data.aiStatus,
        data.chunkStatus,
        data.embeddingStatus,
        data.indexStatus,
        data.tagStatus,
        data.entityStatus,
      ].some((status) => status && activeStatuses.has(status));
      return hasActiveStage ? 3000 : false;
    },
  });

  // 后台阶段状态变化后，联动刷新依赖面板并给出完成/失败反馈。
  useEffect(() => {
    if (!document) return;
    const state = [
      document.parseStatus,
      document.cleanStatus,
      document.aiStatus,
      document.chunkStatus,
      document.embeddingStatus,
      document.indexStatus,
      document.tagStatus,
      document.entityStatus,
    ].join("|");

    if (previousPipelineState.current && previousPipelineState.current !== state) {
      queryClient.invalidateQueries({ queryKey: ["index-state"] });
      queryClient.invalidateQueries({ queryKey: ["document-chunks", documentId] });
      queryClient.invalidateQueries({ queryKey: ["processing-logs", documentId] });
      queryClient.invalidateQueries({ queryKey: ["document-tags", documentId] });

      const stages = state.split("|");
      if (stages.some((status) => status === "failed")) {
        toast.error("处理阶段失败，请查看处理日志");
      } else if (document.aiStatus === "done"
        && document.chunkStatus === "done"
        && document.indexStatus === "done") {
        toast.success("文档处理与索引已完成");
      }
    }
    previousPipelineState.current = state;
  }, [document, documentId, queryClient]);

  // 从 QA 引用跳转过来时，切换到"分块调试"Tab 并滚动到对应分块
  useEffect(() => {
    if (!chunkIdFromQuery) return;
    setActiveTab("chunks");
    // 等待 Tab 切换与分块列表渲染后再滚动
    const timer = setTimeout(() => {
      const el = window.document.getElementById(`chunk-${chunkIdFromQuery}`);
      if (el) {
        el.scrollIntoView({ behavior: "smooth", block: "center" });
      }
    }, 600);
    return () => clearTimeout(timer);
  }, [chunkIdFromQuery]);

  const handleRefresh = () => {
    queryClient.invalidateQueries({ queryKey: ["document", documentId] });
    toast.success("已刷新");
  };

  const handleRetry = async () => {
    if (!document?.sourceId) return;
    try {
      await sourceApi.processSource(document.sourceId);
      toast.success("已重新触发 AI 处理");
      queryClient.invalidateQueries({ queryKey: ["document", documentId] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "重试失败";
      toast.error(message);
    }
  };

  const handleResummarize = async () => {
    try {
      await documentApi.resummarize(documentId);
      toast.success("已触发重新摘要");
      queryClient.invalidateQueries({ queryKey: ["document", documentId] });
    } catch (err) {
      const message = err instanceof ApiRequestError ? err.message : "操作失败";
      toast.error(message);
    }
  };

  const handleExportMarkdown = async () => {
    if (!document) return;
    try {
      const res = await exportApi.documentMarkdown({
        documentId: document.id,
        includeAiSummary: true,
        includeMetadata: true,
      });
      setExportJobId(res.exportJobId);
      setExportOpen(true);
      toast.success("导出任务已创建");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "导出失败，请重试";
      toast.error(message);
    }
  };

  // ===== Tag management handlers =====

  const handleConfirmTag = async (tagId: string) => {
    try {
      await tagApi.confirmDocumentTag(documentId, tagId);
      toast.success("标签已确认");
      refetchTags();
    } catch (err) {
      const message = err instanceof ApiRequestError ? err.message : "操作失败";
      toast.error(message);
    }
  };

  const handleDeleteTag = async (tagId: string) => {
    try {
      await tagApi.deleteDocumentTag(documentId, tagId);
      toast.success("标签已删除");
      refetchTags();
    } catch (err) {
      const message = err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    }
  };

  const handleAddTag = async () => {
    if (!newTagName.trim()) return;
    try {
      await tagApi.addDocumentTag(documentId, { name: newTagName, source: "manual" });
      toast.success("标签已添加");
      setNewTagName("");
      setShowAddTag(false);
      refetchTags();
    } catch (err) {
      const message = err instanceof ApiRequestError ? err.message : "添加失败";
      toast.error(message);
    }
  };

  // ===== Entity management handlers =====

  const handleDeleteEntity = async (entityId: string) => {
    try {
      await entityApi.deleteDocumentEntity(documentId, entityId);
      toast.success("实体已删除");
      queryClient.invalidateQueries({ queryKey: ["document", documentId] });
    } catch (err) {
      const message = err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    }
  };

  // ===== Phase 4 action handlers =====

  const handleAction = async (action: string) => {
    try {
      switch (action) {
        case "regenerate-tags":
          await actionApi.regenerateTags(documentId);
          break;
        case "regenerate-entities":
          await actionApi.regenerateEntities(documentId);
          break;
        case "rechunk":
          await actionApi.rechunk(documentId);
          break;
        case "reembed":
          await actionApi.reembed(documentId);
          break;
        case "rebuild-index":
          await actionApi.rebuildIndex();
          break;
      }
      toast.success("操作已触发");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["document", documentId] }),
        queryClient.invalidateQueries({ queryKey: ["index-state"] }),
        queryClient.invalidateQueries({ queryKey: ["document-chunks", documentId] }),
        queryClient.invalidateQueries({ queryKey: ["processing-logs", documentId] }),
      ]);
    } catch (err) {
      const message = err instanceof ApiRequestError ? err.message : "操作失败";
      toast.error(message);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!document) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <FileText className="mb-4 size-12 text-muted-foreground/50" />
        <p className="text-lg font-medium">文档不存在</p>
        <Button
          variant="outline"
          className="mt-4"
          onClick={() => router.back()}
        >
          返回
        </Button>
      </div>
    );
  }

  const keyPoints = parseJsonArray<KeyPointItem>(document.keyPoints);
  const businessSignals = parseJsonArray<SignalItem>(document.businessSignals);
  const technicalSignals = parseJsonArray<SignalItem>(document.technicalSignals);
  const risks = parseJsonArray<SignalItem>(document.risks);
  const opportunities = parseJsonArray<SignalItem>(document.opportunities);
  const reusableMaterials = parseJsonArray<string | Record<string, unknown>>(
    document.reusableMaterials
  );

  // 按类型分组实体
  const entityGroups = document.entities.reduce<
    Record<string, typeof document.entities>
  >((acc, entity) => {
    if (!acc[entity.entityType]) {
      acc[entity.entityType] = [];
    }
    acc[entity.entityType].push(entity);
    return acc;
  }, {});

  const isProcessing =
    document.aiStatus === "pending" || document.aiStatus === "processing";
  const isFailed = document.aiStatus === "failed";

  return (
    <div className="space-y-6">
      {/* 返回按钮 */}
      <Button variant="ghost" size="sm" onClick={() => router.back()}>
        <ArrowLeft className="mr-2 size-4" />
        返回
      </Button>

      {/* 文档头部信息 */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between">
            <div className="flex-1">
              <CardTitle className="text-xl">{document.title}</CardTitle>
              <div className="mt-3 flex flex-wrap items-center gap-3">
                <AiStatusBadge status={document.aiStatus} />
                {document.valueScore !== undefined && (
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-muted-foreground">价值评分</span>
                    <ValueScoreBar score={document.valueScore} />
                  </div>
                )}
                {document.qualityScore !== undefined && (
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-muted-foreground">质量评分</span>
                    <ValueScoreBar score={document.qualityScore} />
                  </div>
                )}
              </div>
              {/* 多阶段处理状态 */}
              <div className="flex flex-wrap items-center gap-2 mt-2">
                <ProcessingStageBadge label="解析" status={document.parseStatus || "pending"} />
                <ProcessingStageBadge label="清洗" status={document.cleanStatus || "pending"} />
                <ProcessingStageBadge label="AI摘要" status={document.aiStatus} />
                <ProcessingStageBadge label="分块" status={document.chunkStatus || "pending"} />
                <ProcessingStageBadge label="索引" status={document.indexStatus || "pending"} />
              </div>
            </div>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={handleExportMarkdown}
              >
                <FileDown className="mr-1.5 size-3.5" />
                导出Markdown
              </Button>
              {isProcessing && (
                <Button variant="outline" size="sm" onClick={handleRefresh}>
                  <RefreshCw className="mr-1.5 size-3.5" />
                  刷新
                </Button>
              )}
              {isFailed && (
                <Button variant="outline" size="sm" onClick={handleRetry}>
                  <RefreshCw className="mr-1.5 size-3.5" />
                  重试处理
                </Button>
              )}
              {!isProcessing && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleResummarize}
                >
                  <RefreshCw className="mr-1.5 size-3.5" />
                  重新摘要
                </Button>
              )}
              {!isProcessing && (
                <>
                  <Button variant="outline" size="sm" onClick={() => handleAction("regenerate-tags")}>
                    <TagIcon className="mr-1.5 size-3.5" /> 重推标签
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => handleAction("regenerate-entities")}>
                    <Users className="mr-1.5 size-3.5" /> 重抽实体
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => handleAction("rechunk")}>
                    <Scissors className="mr-1.5 size-3.5" /> 重建分块
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => handleAction("reembed")}>
                    <Zap className="mr-1.5 size-3.5" /> 重新向量化
                  </Button>
                  <Button variant="destructive" size="sm" onClick={() => handleAction("rebuild-index")}>
                    <Database className="mr-1.5 size-3.5" /> 重建索引
                  </Button>
                </>
              )}
            </div>
          </div>
        </CardHeader>
      </Card>

      {/* 工作区索引状态 */}
      <IndexStatePanel />

      {/* 处理中状态提示 */}
      {isProcessing && (
        <Card>
          <CardContent className="flex items-center gap-3 py-6">
            <Loader2 className="size-5 animate-spin text-blue-600" />
            <div>
              <p className="text-sm font-medium text-blue-700 dark:text-blue-300">
                AI 正在处理中...
              </p>
              <p className="text-xs text-muted-foreground">
                文档正在接受 AI 分析，请稍候。页面将自动刷新。
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {/* 处理失败状态提示 */}
      {isFailed && (
        <Card>
          <CardContent className="flex items-start gap-3 py-6">
            <AlertCircle className="mt-0.5 size-5 text-red-600" />
            <div className="flex-1">
              <p className="text-sm font-medium text-red-700 dark:text-red-300">
                AI 处理失败
              </p>
              {document.aiErrorMessage && (
                <p className="mt-1 rounded bg-red-50 p-2 text-xs font-mono text-red-600 dark:bg-red-900/20 dark:text-red-400">
                  {getFriendlyErrorMessage(document.aiErrorMessage)}
                </p>
              )}
              <p className="mt-1 text-xs text-muted-foreground">
                请点击右上角&quot;重试处理&quot;重新触发。
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {/* 主内容区 - Tabs */}
      <Tabs value={activeTab} onValueChange={(v) => setActiveTab(v as string)}>
        <TabsList>
          <TabsTrigger value="analysis">AI 分析结果</TabsTrigger>
          <TabsTrigger value="content">原始内容</TabsTrigger>
          <TabsTrigger value="meta">标签与实体</TabsTrigger>
          <TabsTrigger value="info">处理信息</TabsTrigger>
          <TabsTrigger value="logs">处理日志</TabsTrigger>
          <TabsTrigger value="chunks">分块调试</TabsTrigger>
        </TabsList>

        {/* Tab: AI 分析结果 */}
        <TabsContent value="analysis">
          <div className="space-y-4">
            {/* 1. 中文摘要 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <FileText className="size-4 text-blue-600" />
                  中文摘要
                </CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm leading-relaxed">
                  {document.summary || "暂无摘要"}
                </p>
              </CardContent>
            </Card>

            {/* 2. 一句话结论 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Quote className="size-4 text-purple-600" />
                  一句话结论
                </CardTitle>
              </CardHeader>
              <CardContent>
                <blockquote className="border-l-4 border-purple-400 bg-purple-50 py-2 pl-4 text-sm italic dark:bg-purple-900/20">
                  {document.oneSentenceConclusion || "暂无结论"}
                </blockquote>
              </CardContent>
            </Card>

            {/* 3. 关键要点 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <ListChecks className="size-4 text-green-600" />
                  关键要点
                </CardTitle>
              </CardHeader>
              <CardContent>
                {keyPoints.length === 0 ? (
                  <p className="text-sm text-muted-foreground">暂无要点</p>
                ) : (
                  <ul className="space-y-2">
                    {keyPoints.map((point, idx) => (
                      <li
                        key={idx}
                        className="flex items-start gap-3 rounded-lg border bg-muted/30 p-3"
                      >
                        <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-green-100 text-xs font-bold text-green-700 dark:bg-green-900/40 dark:text-green-300">
                          {idx + 1}
                        </span>
                        <div className="flex-1">
                          <p className="text-sm">{point.text}</p>
                          {point.importance !== undefined && (
                            <span className="mt-1 inline-block text-xs text-muted-foreground">
                              重要性: {String(point.importance)}
                            </span>
                          )}
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>

            {/* 4. 商业信号 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <TrendingUp className="size-4 text-blue-600" />
                  商业信号
                </CardTitle>
              </CardHeader>
              <CardContent>
                <SignalList items={businessSignals} emptyText="暂无商业信号" />
              </CardContent>
            </Card>

            {/* 5. 技术信号 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Wrench className="size-4 text-cyan-600" />
                  技术信号
                </CardTitle>
              </CardHeader>
              <CardContent>
                <SignalList items={technicalSignals} emptyText="暂无技术信号" />
              </CardContent>
            </Card>

            {/* 6. 风险 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <ShieldAlert className="size-4 text-red-600" />
                  风险
                </CardTitle>
              </CardHeader>
              <CardContent>
                <SignalList items={risks} emptyText="暂无风险信号" />
              </CardContent>
            </Card>

            {/* 7. 机会 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Target className="size-4 text-orange-600" />
                  机会
                </CardTitle>
              </CardHeader>
              <CardContent>
                <SignalList items={opportunities} emptyText="暂无机会信号" />
              </CardContent>
            </Card>

            {/* 8. 可复用素材 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Recycle className="size-4 text-teal-600" />
                  可复用素材
                </CardTitle>
              </CardHeader>
              <CardContent>
                {reusableMaterials.length === 0 ? (
                  <p className="text-sm text-muted-foreground">暂无可复用素材</p>
                ) : (
                  <ul className="space-y-2">
                    {reusableMaterials.map((item, idx) => (
                      <li
                        key={idx}
                        className="rounded-lg border bg-muted/30 p-3 text-sm"
                      >
                        <pre className="whitespace-pre-wrap font-sans">
                          {typeof item === "string" ? item : JSON.stringify(item, null, 2)}
                        </pre>
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>

            {/* 推荐标签 */}
            {document.recommendedTags && (
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <TagIcon className="size-4 text-indigo-600" />
                    推荐标签
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  {(() => {
                    const tags = parseJsonArray<string>(document.recommendedTags);
                    return tags.length === 0 ? (
                      <p className="text-sm text-muted-foreground">暂无推荐标签</p>
                    ) : (
                      <div className="flex flex-wrap gap-2">
                        {tags.map((tag, idx) => (
                          <span key={idx} className="inline-flex items-center rounded-lg border bg-muted/30 px-3 py-1 text-sm">
                            {tag}
                          </span>
                        ))}
                      </div>
                    );
                  })()}
                </CardContent>
              </Card>
            )}

            {/* 9. 价值评分 + 质量评分 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Gauge className="size-4 text-indigo-600" />
                  评分总览
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="grid gap-6 sm:grid-cols-2">
                  <div>
                    <p className="mb-2 text-xs font-medium text-muted-foreground">
                      价值评分
                    </p>
                    <ValueScoreBar
                      score={document.valueScore}
                      className="w-full"
                    />
                  </div>
                  <div>
                    <p className="mb-2 text-xs font-medium text-muted-foreground">
                      质量评分
                    </p>
                    <ValueScoreBar
                      score={document.qualityScore}
                      className="w-full"
                    />
                  </div>
                </div>
              </CardContent>
            </Card>

            {/* 深度处理建议 */}
            {document.shouldDeepProcess !== undefined && (
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Lightbulb className="size-4 text-yellow-600" />
                    深度处理建议
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  <div className={`flex items-center gap-3 rounded-lg border p-4 ${
                    document.shouldDeepProcess
                      ? "bg-green-50 dark:bg-green-900/20"
                      : "bg-gray-50 dark:bg-gray-900/20"
                  }`}>
                    <div className={`flex size-10 shrink-0 items-center justify-center rounded-full ${
                      document.shouldDeepProcess
                        ? "bg-green-100 text-green-600 dark:bg-green-900/40 dark:text-green-300"
                        : "bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400"
                    }`}>
                      {document.shouldDeepProcess ? (
                        <CheckCircle className="size-5" />
                      ) : (
                        <XCircle className="size-5" />
                      )}
                    </div>
                    <div>
                      <p className="text-sm font-medium">
                        {document.shouldDeepProcess
                          ? "建议进入深度处理"
                          : "不建议深度处理"}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {document.shouldDeepProcess
                          ? "该资料质量较高，适合进入后续分块、向量化和深度分析。"
                          : "该资料质量或价值较低，可保存但不必优先处理。"}
                      </p>
                    </div>
                  </div>
                </CardContent>
              </Card>
            )}
          </div>
        </TabsContent>

        {/* Tab: 原始内容 */}
        <TabsContent value="content">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">正文内容</CardTitle>
              <CardDescription>
                {document.wordCount ? `约 ${document.wordCount} 字` : ""}
                {document.language ? ` · ${document.language}` : ""}
                {document.readingTimeMinutes ? ` · 约 ${document.readingTimeMinutes} 分钟阅读` : ""}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="max-h-[600px] overflow-y-auto rounded-lg border bg-background p-4">
                {document.contentMarkdown ? (
                  <Markdown content={document.contentMarkdown} />
                ) : (
                  <pre className="whitespace-pre-wrap text-sm leading-relaxed">
                    {document.contentText || "暂无内容"}
                  </pre>
                )}
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        {/* Tab: 标签与实体 */}
        <TabsContent value="meta">
          <div className="space-y-4">
            {/* 标签管理 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <TagIcon className="size-4 text-purple-600" />
                  标签
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <span className="text-sm text-muted-foreground">
                      {documentTags.length > 0 ? `${documentTags.length} 个标签` : ""}
                    </span>
                    <Button size="sm" variant="outline" onClick={() => setShowAddTag(true)}>
                      <Plus className="mr-1 size-3" /> 添加标签
                    </Button>
                  </div>

                  {/* 标签列表 */}
                  {documentTags.length === 0 ? (
                    <p className="text-sm text-muted-foreground">暂无标签</p>
                  ) : (
                    <div className="flex flex-wrap gap-2">
                      {documentTags.map((tag) => (
                        <div key={tag.id} className="group flex items-center gap-1 rounded-lg border px-3 py-1">
                          <span className="text-sm">{tag.name}</span>
                          {tag.confidence !== undefined && tag.confidence !== null && (
                            <span className="text-xs text-muted-foreground">
                              {(tag.confidence * 100).toFixed(0)}%
                            </span>
                          )}
                          {tag.source === "ai" && !tag.isConfirmed && (
                            <Badge variant="outline" className="ml-1 text-[10px]">待确认</Badge>
                          )}
                          {tag.isConfirmed && (
                            <CheckCircle className="size-3 text-green-600" />
                          )}
                          {tag.source === "ai" && !tag.isConfirmed && (
                            <button
                              onClick={() => handleConfirmTag(tag.tagId || tag.id)}
                              className="ml-1 text-xs text-blue-600 opacity-0 group-hover:opacity-100"
                            >
                              确认
                            </button>
                          )}
                          <button
                            onClick={() => handleDeleteTag(tag.tagId || tag.id)}
                            className="text-xs text-red-600 opacity-0 group-hover:opacity-100"
                          >
                            <X className="size-3" />
                          </button>
                        </div>
                      ))}
                    </div>
                  )}

                  {/* 添加标签输入框 */}
                  {showAddTag && (
                    <div className="flex gap-2">
                      <Input
                        placeholder="输入标签名称"
                        value={newTagName}
                        onChange={(e) => setNewTagName(e.target.value)}
                        onKeyDown={(e) => e.key === "Enter" && handleAddTag()}
                      />
                      <Button size="sm" onClick={handleAddTag}>添加</Button>
                      <Button size="sm" variant="outline" onClick={() => setShowAddTag(false)}>取消</Button>
                    </div>
                  )}
                </div>
              </CardContent>
            </Card>

            {/* 实体列表 */}
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Boxes className="size-4 text-cyan-600" />
                  抽取实体
                </CardTitle>
              </CardHeader>
              <CardContent>
                {document.entities.length === 0 ? (
                  <p className="text-sm text-muted-foreground">暂无实体</p>
                ) : (
                  <div className="space-y-4">
                    {Object.entries(entityGroups).map(([type, entities]) => (
                      <div key={type}>
                        <div className="mb-2 flex items-center gap-2">
                          <EntityTypeBadge entityType={type} />
                          <span className="text-xs text-muted-foreground">
                            ({entities.length})
                          </span>
                        </div>
                        <div className="flex flex-wrap gap-2">
                          {entities.map((entity) => (
                            <div
                              key={entity.id}
                              className="group inline-flex items-center gap-1.5 rounded-lg border bg-muted/30 px-3 py-1.5 text-sm"
                            >
                              <Link
                                href={`/entities/${entity.id}`}
                                className="transition-colors hover:underline"
                              >
                                {entity.name}
                              </Link>
                              {entity.mentionCount !== undefined && (
                                <span className="text-xs text-muted-foreground">
                                  x{entity.mentionCount}
                                </span>
                              )}
                              <button
                                onClick={() => handleDeleteEntity(entity.id)}
                                className="text-xs text-red-600 opacity-0 group-hover:opacity-100"
                              >
                                <X className="size-3" />
                              </button>
                            </div>
                          ))}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        {/* Tab: 处理信息 */}
        <TabsContent value="info">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <Cpu className="size-4 text-indigo-600" />
                AI 处理信息
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="flex items-center gap-3 rounded-lg border p-3">
                  <Cpu className="size-4 text-muted-foreground" />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">
                      AI 模型
                    </p>
                    <p className="mt-0.5 text-sm">
                      {document.aiModel || "-"}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-3 rounded-lg border p-3">
                  <Hash className="size-4 text-muted-foreground" />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">
                      Prompt 版本
                    </p>
                    <p className="mt-0.5 text-sm">
                      {document.promptVersion || "-"}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-3 rounded-lg border p-3">
                  <Clock className="size-4 text-muted-foreground" />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">
                      处理时间
                    </p>
                    <p className="mt-0.5 text-sm">
                      {formatDate(document.processedAt)}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-3 rounded-lg border p-3">
                  <Lightbulb className="size-4 text-muted-foreground" />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">
                      AI 状态
                    </p>
                    <div className="mt-0.5">
                      <AiStatusBadge status={document.aiStatus} />
                    </div>
                  </div>
                </div>
              </div>

              <Separator className="my-4" />

              <div className="grid gap-4 sm:grid-cols-3">
                <div className="rounded-lg border p-3 text-center">
                  <p className="text-xs font-medium text-muted-foreground">
                    创建时间
                  </p>
                  <p className="mt-1 text-sm">
                    {formatDate(document.createdAt)}
                  </p>
                </div>
                <div className="rounded-lg border p-3 text-center">
                  <p className="text-xs font-medium text-muted-foreground">
                    更新时间
                  </p>
                  <p className="mt-1 text-sm">
                    {formatDate(document.updatedAt)}
                  </p>
                </div>
                <div className="rounded-lg border p-3 text-center">
                  <p className="text-xs font-medium text-muted-foreground">
                    字数
                  </p>
                  <p className="mt-1 text-sm">
                    {document.wordCount ?? "-"}
                  </p>
                </div>
              </div>

              {/* 来源信息 */}
              <div className="grid gap-4 sm:grid-cols-2 mt-4">
                {document.sourceType && (
                  <div className="flex items-center gap-3 rounded-lg border p-3">
                    <FileText className="size-4 text-muted-foreground" />
                    <div>
                      <p className="text-xs font-medium text-muted-foreground">来源类型</p>
                      <p className="mt-0.5 text-sm">{document.sourceType}</p>
                    </div>
                  </div>
                )}
                {document.sourceDomain && (
                  <div className="flex items-center gap-3 rounded-lg border p-3">
                    <Globe className="size-4 text-muted-foreground" />
                    <div>
                      <p className="text-xs font-medium text-muted-foreground">来源域名</p>
                      <p className="mt-0.5 text-sm">{document.sourceDomain}</p>
                    </div>
                  </div>
                )}
                {document.author && (
                  <div className="flex items-center gap-3 rounded-lg border p-3">
                    <User className="size-4 text-muted-foreground" />
                    <div>
                      <p className="text-xs font-medium text-muted-foreground">作者</p>
                      <p className="mt-0.5 text-sm">{document.author}</p>
                    </div>
                  </div>
                )}
                {document.publishedAt && (
                  <div className="flex items-center gap-3 rounded-lg border p-3">
                    <Calendar className="size-4 text-muted-foreground" />
                    <div>
                      <p className="text-xs font-medium text-muted-foreground">发布时间</p>
                      <p className="mt-0.5 text-sm">{formatDate(document.publishedAt)}</p>
                    </div>
                  </div>
                )}
              </div>

              {/* 解析器信息 */}
              <div className="grid gap-4 sm:grid-cols-3 mt-4">
                {document.parserName && (
                  <div className="rounded-lg border p-3 text-center">
                    <p className="text-xs font-medium text-muted-foreground">解析器</p>
                    <p className="mt-1 text-sm">{document.parserName}</p>
                  </div>
                )}
                {document.parserVersion && (
                  <div className="rounded-lg border p-3 text-center">
                    <p className="text-xs font-medium text-muted-foreground">解析器版本</p>
                    <p className="mt-1 text-sm">{document.parserVersion}</p>
                  </div>
                )}
                {document.cleanerVersion && (
                  <div className="rounded-lg border p-3 text-center">
                    <p className="text-xs font-medium text-muted-foreground">清洗器版本</p>
                    <p className="mt-1 text-sm">{document.cleanerVersion}</p>
                  </div>
                )}
                {document.valueScoreReason && (
                  <div className="rounded-lg border p-3 text-center">
                    <p className="text-xs font-medium text-muted-foreground">评分理由</p>
                    <p className="mt-1 text-xs text-muted-foreground">{document.valueScoreReason}</p>
                  </div>
                )}
              </div>

              {document.sourceId && (
                <div className="mt-4 flex justify-end">
                  <Link href={`/sources/${document.sourceId}`}>
                    <Button variant="outline" size="sm">
                      查看原始资料
                    </Button>
                  </Link>
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        {/* Tab: 处理日志 */}
        <TabsContent value="logs">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <Clock className="size-4 text-indigo-600" />
                处理日志
              </CardTitle>
              <CardDescription>文档处理各阶段记录</CardDescription>
            </CardHeader>
            <CardContent>
              <ProcessingLogs documentId={documentId} />
            </CardContent>
          </Card>
        </TabsContent>

        {/* Tab: 分块调试 */}
        <TabsContent value="chunks">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <Scissors className="size-4 text-indigo-600" />
                分块调试
              </CardTitle>
              <CardDescription>文档分块详情与 embedding 状态</CardDescription>
            </CardHeader>
            <CardContent>
              <ChunkDebugger
                documentId={documentId}
                highlightChunkId={chunkIdFromQuery ?? undefined}
              />
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>

      {/* 导出状态弹窗 */}
      <ExportStatusDialog
        open={exportOpen}
        onOpenChange={setExportOpen}
        exportJobId={exportJobId}
      />
    </div>
  );
}
