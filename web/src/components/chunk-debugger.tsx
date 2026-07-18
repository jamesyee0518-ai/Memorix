"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Loader2,
  AlertCircle,
  ChevronRight,
  ChevronDown,
  RefreshCw,
  Scissors,
  FileText,
  Languages,
  CheckCircle2,
  Sparkles,
  Zap,
} from "lucide-react";
import { chunkApi, actionApi, chunkEmbeddingApi, ApiRequestError } from "@/lib/api";
import type { DocumentChunkItem } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";

// ===== 向量化状态徽章 =====

const embeddingStatusConfig: Record<
  string,
  { label: string; className: string }
> = {
  pending: {
    label: "待处理",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  processing: {
    label: "向量化中",
    className:
      "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  done: {
    label: "已完成",
    className:
      "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
  },
  failed: {
    label: "失败",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
  stale: {
    label: "已过期",
    className:
      "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300",
  },
};

function EmbeddingStatusBadge({ status }: { status: string }) {
  const config = embeddingStatusConfig[status] ?? {
    label: status,
    className:
      "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  };
  return (
    <Badge
      variant="outline"
      className={cn("border-transparent font-medium", config.className)}
    >
      {config.label}
    </Badge>
  );
}

function parseStringList(value?: string): string[] {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === "string") : [];
  } catch {
    return value.split(/[，,；;\n]/).map((item) => item.trim()).filter(Boolean);
  }
}

// ===== 单个分块行 =====

function ChunkRow({
  chunk,
  documentId,
  highlightChunkId,
}: {
  chunk: DocumentChunkItem;
  documentId: string;
  highlightChunkId?: string;
}) {
  const isHighlighted = !!highlightChunkId && highlightChunkId === chunk.id;
  const [expanded, setExpanded] = useState(isHighlighted);
  const [reembedding, setReembedding] = useState(false);
  const [translating, setTranslating] = useState(false);
  const [reviewing, setReviewing] = useState(false);
  const [enriching, setEnriching] = useState(false);
  const [editingLocalization, setEditingLocalization] = useState(false);
  const [localizedHeading, setLocalizedHeading] = useState("");
  const [localizedContent, setLocalizedContent] = useState("");
  const queryClient = useQueryClient();
  const title =
    chunk.chunkTitle || chunk.headingPath || `分块 ${chunk.chunkIndex}`;

  const isFailed = chunk.embeddingStatus === "failed";

  // 展开且向量化失败时，按需获取 embedding 详情
  const { data: embeddingInfo } = useQuery({
    queryKey: ["chunk-embedding", chunk.id],
    queryFn: () => chunkEmbeddingApi.get(chunk.id),
    enabled: expanded && isFailed,
  });

  const { data: localizations, refetch: refetchLocalizations } = useQuery({
    queryKey: ["chunk-localizations", chunk.id],
    queryFn: () => chunkApi.getLocalizations(chunk.id),
    enabled: expanded,
  });
  const localization = localizations?.[0];
  const { data: enrichments, refetch: refetchEnrichments } = useQuery({
    queryKey: ["chunk-enrichments", chunk.id],
    queryFn: () => chunkApi.getEnrichments(chunk.id),
    enabled: expanded,
  });
  const enrichment = enrichments?.[0];

  const handleTranslate = async (force = false) => {
    setTranslating(true);
    try {
      await chunkApi.translate(chunk.id, force);
      await refetchLocalizations();
      queryClient.invalidateQueries({ queryKey: ["document-chunks", documentId] });
      toast.success(force ? "已重新生成中文译文" : "已生成中文译文");
    } catch (err) {
      toast.error(err instanceof ApiRequestError ? err.message : "翻译失败");
    } finally {
      setTranslating(false);
    }
  };

  const beginReview = () => {
    if (!localization) return;
    setLocalizedHeading(localization.headingLocalized || "");
    setLocalizedContent(localization.contentLocalized || "");
    setEditingLocalization(true);
  };

  const handleReview = async () => {
    if (!localization || !localizedContent.trim()) return;
    setReviewing(true);
    try {
      await chunkApi.review(chunk.id, localization.id, {
        headingLocalized: localizedHeading.trim() || undefined,
        contentLocalized: localizedContent.trim(),
        approved: true,
      });
      await refetchLocalizations();
      setEditingLocalization(false);
      toast.success("译文已审校并用于检索与引用");
    } catch (err) {
      toast.error(err instanceof ApiRequestError ? err.message : "审校保存失败");
    } finally {
      setReviewing(false);
    }
  };

  const handleEnrich = async () => {
    setEnriching(true);
    try {
      await chunkApi.enrich(chunk.id, !!enrichment);
      await refetchEnrichments();
      toast.success(enrichment ? "已重新生成知识增强" : "已生成知识增强");
    } catch (err) {
      toast.error(err instanceof ApiRequestError ? err.message : "知识增强失败");
    } finally {
      setEnriching(false);
    }
  };

  const handleReembed = async () => {
    setReembedding(true);
    try {
      await actionApi.reembed(documentId);
      toast.success("已触发重新向量化");
      queryClient.invalidateQueries({
        queryKey: ["document-chunks", documentId],
      });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "操作失败";
      toast.error(message);
    } finally {
      setReembedding(false);
    }
  };

  return (
    <div
      id={`chunk-${chunk.id}`}
      className={cn(
        "rounded-lg border",
        isHighlighted && "border-primary ring-2 ring-primary/40"
      )}
    >
      {/* 摘要行 */}
      <div className="flex items-center gap-3 p-3">
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="flex flex-1 items-center gap-3 text-left"
          aria-label={expanded ? "收起" : "展开"}
        >
          {expanded ? (
            <ChevronDown className="size-4 shrink-0 text-muted-foreground" />
          ) : (
            <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
          )}
          <span className="w-8 shrink-0 text-center text-sm font-medium text-muted-foreground">
            {chunk.chunkIndex}
          </span>
          <span className="flex-1 truncate text-sm font-medium" title={title}>
            {title}
          </span>
        </button>

        <div className="flex shrink-0 items-center gap-3 text-xs text-muted-foreground">
          <span>{chunk.tokenCount ?? "-"} token</span>
          <span>{chunk.charCount ?? "-"} 字符</span>
        </div>

        <EmbeddingStatusBadge status={chunk.embeddingStatus} />

        <Button
          variant="ghost"
          size="xs"
          onClick={() => setExpanded((v) => !v)}
        >
          {expanded ? "收起" : "展开"}
        </Button>
      </div>

      {/* 展开内容 */}
      {expanded && (
        <div className="border-t bg-muted/30 p-3">
          <div className="mb-2 flex flex-wrap gap-3 text-xs text-muted-foreground">
            {chunk.chunkUid && <span>UID: {chunk.chunkUid}</span>}
            {chunk.sectionLevel !== undefined && (
              <span>层级: L{chunk.sectionLevel}</span>
            )}
            {chunk.startOffset !== undefined && chunk.endOffset !== undefined && (
              <span>
                偏移: {chunk.startOffset} - {chunk.endOffset}
              </span>
            )}
            {chunk.embeddingModel && (
              <span>模型: {chunk.embeddingModel}</span>
            )}
            {chunk.detectedLanguage && <span>语言: {chunk.detectedLanguage}</span>}
            {chunk.processingRoute && <span>处理路线: {chunk.processingRoute}</span>}
            {chunk.indexStatus && <span>索引: {chunk.indexStatus}</span>}
            {chunk.contentHash && (
              <span className="font-mono">
                Hash: {chunk.contentHash.slice(0, 12)}...
              </span>
            )}
          </div>
          <div className="rounded border bg-background p-3">
            <div className="mb-2 text-xs font-medium text-muted-foreground">原文</div>
            <pre className="max-h-80 overflow-y-auto whitespace-pre-wrap text-sm leading-relaxed">
              {chunk.contentOriginal || chunk.contentMarkdown || chunk.content}
            </pre>
          </div>

          <div className="mt-3 rounded-lg border bg-background p-3">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <Languages className="size-4 text-primary" />
                <span className="text-sm font-medium">中文译文</span>
                {localization && (
                  <>
                    <Badge variant="secondary">
                      {localization.reviewStatus === "approved" ? "人工审校" : "机器翻译"}
                    </Badge>
                    {localization.qualityScore !== undefined && (
                      <Badge variant="outline">质量 {Math.round(localization.qualityScore)}</Badge>
                    )}
                  </>
                )}
              </div>
              <div className="flex items-center gap-2">
                {localization && !editingLocalization && (
                  <Button size="xs" variant="outline" onClick={beginReview}>审校</Button>
                )}
                <Button
                  size="xs"
                  variant={localization ? "ghost" : "default"}
                  onClick={() => handleTranslate(!!localization)}
                  disabled={translating}
                >
                  {translating && <Loader2 className="mr-1.5 size-3 animate-spin" />}
                  {localization ? "重新翻译" : "翻译此段"}
                </Button>
              </div>
            </div>

            {editingLocalization && localization ? (
              <div className="space-y-2">
                <Input
                  value={localizedHeading}
                  onChange={(event) => setLocalizedHeading(event.target.value)}
                  placeholder="中文标题（可选）"
                />
                <Textarea
                  value={localizedContent}
                  onChange={(event) => setLocalizedContent(event.target.value)}
                  rows={8}
                  placeholder="中文译文"
                />
                <div className="flex justify-end gap-2">
                  <Button size="xs" variant="ghost" onClick={() => setEditingLocalization(false)}>
                    取消
                  </Button>
                  <Button size="xs" onClick={handleReview} disabled={reviewing || !localizedContent.trim()}>
                    {reviewing ? <Loader2 className="mr-1.5 size-3 animate-spin" /> : <CheckCircle2 className="mr-1.5 size-3" />}
                    审核通过
                  </Button>
                </div>
              </div>
            ) : localization ? (
              <div>
                {localization.headingLocalized && (
                  <p className="mb-2 text-sm font-semibold">{localization.headingLocalized}</p>
                )}
                <p className="max-h-80 overflow-y-auto whitespace-pre-wrap text-sm leading-relaxed">
                  {localization.contentLocalized}
                </p>
                {localization.qualityIssues && (
                  <p className="mt-2 text-xs text-amber-600">质量提示：{localization.qualityIssues}</p>
                )}
              </div>
            ) : (
              <p className="text-xs text-muted-foreground">
                尚未生成中文译文。译文会自动写入中文全文索引，并在双语问答中作为可追溯证据展示。
              </p>
            )}
          </div>

          <div className="mt-3 rounded-lg border bg-background p-3">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <Sparkles className="size-4 text-primary" />
                <span className="text-sm font-medium">中文知识增强</span>
                {enrichment && <Badge variant="outline">{enrichment.status === "done" ? "已完成" : enrichment.status}</Badge>}
              </div>
              <Button size="xs" variant={enrichment ? "ghost" : "outline"} onClick={handleEnrich} disabled={enriching}>
                {enriching && <Loader2 className="mr-1.5 size-3 animate-spin" />}
                {enrichment ? "重新生成" : "生成增强"}
              </Button>
            </div>
            {enrichment?.status === "done" ? (
              <div className="space-y-3 text-sm">
                {enrichment.summary && <div><p className="mb-1 text-xs font-medium text-muted-foreground">摘要</p><p>{enrichment.summary}</p></div>}
                {parseStringList(enrichment.keywords).length > 0 && (
                  <div className="flex flex-wrap gap-1.5">
                    {parseStringList(enrichment.keywords).map((item) => <Badge key={item} variant="secondary">{item}</Badge>)}
                  </div>
                )}
                {parseStringList(enrichment.entities).length > 0 && (
                  <div><p className="mb-1 text-xs font-medium text-muted-foreground">实体</p><p>{parseStringList(enrichment.entities).join(" · ")}</p></div>
                )}
                {parseStringList(enrichment.facts).length > 0 && (
                  <div><p className="mb-1 text-xs font-medium text-muted-foreground">事实</p><ul className="list-disc space-y-1 pl-5">{parseStringList(enrichment.facts).map((item) => <li key={item}>{item}</li>)}</ul></div>
                )}
                {parseStringList(enrichment.hypotheticalQuestions).length > 0 && (
                  <div><p className="mb-1 text-xs font-medium text-muted-foreground">可回答问题</p><ul className="list-disc space-y-1 pl-5">{parseStringList(enrichment.hypotheticalQuestions).map((item) => <li key={item}>{item}</li>)}</ul></div>
                )}
              </div>
            ) : (
              <p className="text-xs text-muted-foreground">提炼摘要、关键词、实体、事实和可回答问题，并写入中文检索索引。</p>
            )}
          </div>

          {/* 向量化失败详情 */}
          {isFailed && (
            <div className="mt-3 rounded-lg border border-red-200 bg-red-50 p-3 dark:border-red-900/50 dark:bg-red-900/20">
              <div className="mb-2 flex items-center gap-2">
                <AlertCircle className="size-4 text-red-600" />
                <span className="text-sm font-medium text-red-700 dark:text-red-300">
                  向量化失败详情
                </span>
              </div>
              <div className="space-y-1 text-xs text-red-600 dark:text-red-400">
                <p>
                  错误信息:{" "}
                  {embeddingInfo?.errorMessage || "暂无详细错误信息"}
                </p>
                {embeddingInfo && (
                  <p>重试次数: {embeddingInfo.retryCount}</p>
                )}
              </div>
              <Button
                variant="outline"
                size="xs"
                className="mt-2 border-red-300 text-red-600 hover:bg-red-100 dark:border-red-800 dark:text-red-400 dark:hover:bg-red-900/40"
                onClick={handleReembed}
                disabled={reembedding}
              >
                {reembedding ? (
                  <Loader2 className="mr-1.5 size-3 animate-spin" />
                ) : (
                  <Zap className="mr-1.5 size-3" />
                )}
                重试向量化
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ===== 主组件 =====

export function ChunkDebugger({
  documentId,
  highlightChunkId,
}: {
  documentId: string;
  highlightChunkId?: string;
}) {
  const queryClient = useQueryClient();
  const [rechunking, setRechunking] = useState(false);
  const [batchAction, setBatchAction] = useState<"translate" | "enrich" | "multi_vector" | null>(null);

  const { data: chunks, isLoading, error } = useQuery({
    queryKey: ["document-chunks", documentId],
    queryFn: () => chunkApi.getDocumentChunks(documentId),
    enabled: !!documentId,
    refetchInterval: (query) => {
      const items = query.state.data ?? [];
      return items.some((item) =>
        item.embeddingStatus === "pending"
        || item.embeddingStatus === "processing"
        || item.embeddingStatus === "stale")
        ? 3000
        : false;
    },
  });
  const { data: batchJobs } = useQuery({
    queryKey: ["document-batch-jobs", documentId],
    queryFn: () => chunkApi.getDocumentJobs(documentId),
    enabled: !!documentId,
    refetchInterval: (query) => (query.state.data ?? []).some((job) =>
      job.status === "pending" || job.status === "running") ? 2000 : false,
  });
  const activeJob = batchJobs?.find((job) => ["pending", "running", "paused"].includes(job.status));
  const visibleJob = activeJob ?? batchJobs?.find((job) => job.failedItems > 0);

  const handleRechunk = async () => {
    setRechunking(true);
    try {
      await actionApi.rechunk(documentId);
      toast.success("已触发重新分块");
      queryClient.invalidateQueries({ queryKey: ["document-chunks", documentId] });
      queryClient.invalidateQueries({ queryKey: ["document", documentId] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "操作失败";
      toast.error(message);
    } finally {
      setRechunking(false);
    }
  };

  const handleRetry = () => {
    queryClient.invalidateQueries({ queryKey: ["document-chunks", documentId] });
  };

  const handleBatch = async (action: "translate" | "enrich" | "multi_vector") => {
    setBatchAction(action);
    try {
      const result = action === "translate" ? await chunkApi.translateDocument(documentId)
        : action === "enrich" ? await chunkApi.enrichDocument(documentId)
        : await chunkApi.rebuildMultiVectors(documentId);
      toast.success(`${action === "translate" ? "全文翻译" : action === "enrich" ? "全文增强" : "多路向量重建"}任务已进入后台队列`);
      queryClient.setQueryData(["document-batch-jobs", documentId], (current: typeof batchJobs) =>
        [result, ...(current ?? []).filter((item) => item.id !== result.id)]);
      queryClient.invalidateQueries({ queryKey: ["document-chunks", documentId] });
      queryClient.invalidateQueries({ queryKey: ["chunk-localizations"] });
      queryClient.invalidateQueries({ queryKey: ["chunk-enrichments"] });
    } catch (err) {
      toast.error(err instanceof ApiRequestError ? err.message : "批量处理失败");
    } finally {
      setBatchAction(null);
    }
  };

  const controlBatchJob = async (action: "pause" | "resume" | "retry") => {
    if (!visibleJob) return;
    try {
      await chunkApi.controlJob(visibleJob.id, action);
      queryClient.invalidateQueries({ queryKey: ["document-batch-jobs", documentId] });
      toast.success(action === "pause" ? "任务将在当前分块完成后暂停" : action === "resume" ? "任务已继续" : "任务已重新排队");
    } catch (err) { toast.error(err instanceof ApiRequestError ? err.message : "任务操作失败"); }
  };

  // 加载中
  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  // 错误
  if (error) {
    const message =
      error instanceof ApiRequestError ? error.message : "加载分块失败";
    return (
      <div className="flex flex-col items-center justify-center gap-3 py-12 text-center">
        <AlertCircle className="size-8 text-red-500" />
        <p className="text-sm text-red-600 dark:text-red-400">{message}</p>
        <Button variant="outline" size="sm" onClick={handleRetry}>
          <RefreshCw className="mr-1.5 size-3.5" />
          重试
        </Button>
      </div>
    );
  }

  // 空状态
  if (!chunks || chunks.length === 0) {
    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <p className="text-sm text-muted-foreground">暂无分块数据</p>
          <Button
            variant="outline"
            size="sm"
            onClick={handleRechunk}
            disabled={rechunking}
          >
            {rechunking ? (
              <Loader2 className="mr-1.5 size-3.5 animate-spin" />
            ) : (
              <Scissors className="mr-1.5 size-3.5" />
            )}
            重新分块
          </Button>
        </div>
        <div className="flex flex-col items-center justify-center gap-2 py-12 text-center">
          <FileText className="size-10 text-muted-foreground/50" />
          <p className="text-sm text-muted-foreground">
            该文档尚未进行分块处理
          </p>
        </div>
      </div>
    );
  }

  // 统计
  const doneCount = chunks.filter((c) => c.embeddingStatus === "done").length;

  return (
    <div className="space-y-4">
      {/* 操作栏 */}
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          共 {chunks.length} 个分块 · 已向量化 {doneCount} 个
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => handleBatch("translate")} disabled={batchAction !== null}>
            {batchAction === "translate" ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : <Languages className="mr-1.5 size-3.5" />}
            全文翻译
          </Button>
          <Button variant="outline" size="sm" onClick={() => handleBatch("enrich")} disabled={batchAction !== null}>
            {batchAction === "enrich" ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : <Sparkles className="mr-1.5 size-3.5" />}
            全文增强
          </Button>
          <Button variant="outline" size="sm" onClick={() => handleBatch("multi_vector")} disabled={batchAction !== null}>
            {batchAction === "multi_vector" ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : <RefreshCw className="mr-1.5 size-3.5" />}
            重建多路向量
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={handleRechunk}
            disabled={rechunking}
          >
            {rechunking ? (
              <Loader2 className="mr-1.5 size-3.5 animate-spin" />
            ) : (
              <Scissors className="mr-1.5 size-3.5" />
            )}
            重新分块
          </Button>
        </div>
      </div>

      {visibleJob && (
        <div className="rounded-lg border bg-muted/30 p-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2 text-sm font-medium">
                {visibleJob.status === "running" && <Loader2 className="size-4 animate-spin" />}
                {visibleJob.jobType === "translate" ? "全文翻译" : visibleJob.jobType === "enrich" ? "全文增强" : "多路向量重建"}
                <Badge variant="outline">{visibleJob.status === "paused" ? "已暂停" : visibleJob.status === "pending" ? "等待中" : visibleJob.status === "done" ? "已完成" : "处理中"}</Badge>
              </div>
              <div className="mt-2 h-2 overflow-hidden rounded-full bg-muted">
                <div className="h-full bg-primary transition-all" style={{ width: `${visibleJob.totalItems ? Math.round(visibleJob.processedItems / visibleJob.totalItems * 100) : 0}%` }} />
              </div>
              <p className="mt-1 text-xs text-muted-foreground">
                {visibleJob.processedItems}/{visibleJob.totalItems} · 成功 {visibleJob.succeededItems} · 失败 {visibleJob.failedItems}
              </p>
            </div>
            <div className="flex gap-2">
              {visibleJob.status === "paused" ? (
                <Button size="xs" variant="outline" onClick={() => controlBatchJob("resume")}>继续</Button>
              ) : (
                visibleJob.status === "running" || visibleJob.status === "pending" ? <Button size="xs" variant="outline" onClick={() => controlBatchJob("pause")}>暂停</Button> : null
              )}
              {visibleJob.failedItems > 0 && visibleJob.status !== "running" && <Button size="xs" variant="ghost" onClick={() => controlBatchJob("retry")}>重试</Button>}
            </div>
          </div>
        </div>
      )}

      {/* 表头 */}
      <div className="flex items-center gap-3 rounded-lg bg-muted/50 px-3 py-2 text-xs font-medium text-muted-foreground">
        <span className="w-4 shrink-0" />
        <span className="w-8 shrink-0 text-center">序号</span>
        <span className="flex-1">标题</span>
        <span className="shrink-0">token / 字符</span>
        <span className="shrink-0">向量化状态</span>
        <span className="w-12 shrink-0 text-center">操作</span>
      </div>

      {/* 分块列表 */}
      <div className="space-y-2">
        {chunks.map((chunk) => (
          <ChunkRow
            key={chunk.id}
            chunk={chunk}
            documentId={documentId}
            highlightChunkId={highlightChunkId}
          />
        ))}
      </div>
    </div>
  );
}
