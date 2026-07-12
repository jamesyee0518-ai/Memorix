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
  Zap,
} from "lucide-react";
import { chunkApi, actionApi, chunkEmbeddingApi, ApiRequestError } from "@/lib/api";
import type { DocumentChunkItem } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
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
            {chunk.indexStatus && <span>索引: {chunk.indexStatus}</span>}
            {chunk.contentHash && (
              <span className="font-mono">
                Hash: {chunk.contentHash.slice(0, 12)}...
              </span>
            )}
          </div>
          <pre className="max-h-80 overflow-y-auto whitespace-pre-wrap rounded border bg-background p-3 text-sm leading-relaxed">
            {chunk.contentMarkdown || chunk.content}
          </pre>

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
