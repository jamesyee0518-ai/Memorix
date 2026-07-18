"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { toast } from "sonner";
import {
  Search as SearchIcon,
  Loader2,
  SlidersHorizontal,
  ChevronDown,
  ChevronUp,
  ExternalLink,
  Calendar,
  Globe,
  AlertCircle,
  SearchX,
  FileDown,
} from "lucide-react";
import { searchApi, exportApi, tagApi, entityApi, ApiRequestError } from "@/lib/api";
import { useTopicStore } from "@/stores/topic-store";
import type {
  SearchResult,
  SearchResultItem,
  ScoreDetail,
  Tag,
  EntityListItem,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { ExportStatusDialog } from "@/components/export-status-dialog";
import { cn } from "@/lib/utils";

// ===== 常量 =====

const SEARCH_TYPES = [
  { value: "hybrid", label: "混合检索" },
  { value: "keyword", label: "关键词" },
  { value: "vector", label: "语义" },
] as const;

const SOURCE_TYPES = [
  { value: "url", label: "URL", color: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300" },
  { value: "pdf", label: "PDF", color: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300" },
  { value: "text", label: "文本", color: "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300" },
];

function getSourceTypeStyle(type?: string): string {
  const found = SOURCE_TYPES.find((s) => s.value === type);
  return found?.color ?? "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300";
}

function getSourceTypeLabel(type?: string): string {
  const found = SOURCE_TYPES.find((s) => s.value === type);
  return found?.label ?? type ?? "未知";
}

// ===== 工具函数 =====

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleDateString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
}

function getScoreColor(score: number): string {
  if (score < 0.4) return "bg-red-500";
  if (score < 0.7) return "bg-yellow-500";
  return "bg-green-500";
}

function getScoreTextColor(score: number): string {
  if (score < 0.4) return "text-red-600";
  if (score < 0.7) return "text-yellow-600";
  return "text-green-600";
}

// ===== 分数详情条形图 =====

function ScoreDetailBars({ detail }: { detail?: ScoreDetail }) {
  if (!detail) return null;

  const bars = [
    { label: "关键词", value: detail.keywordScore },
    { label: "语义", value: detail.vectorScore },
    { label: "时效性", value: detail.freshnessScore },
    { label: "价值", value: detail.valueScore },
    { label: "元数据", value: detail.metadataScore ?? 0 },
  ];

  return (
    <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1.5 rounded-lg bg-muted/30 p-3">
      {bars.map((bar) => {
        const pct = Math.round(bar.value * 100);
        return (
          <div key={bar.label} className="flex items-center gap-2">
            <span className="w-12 shrink-0 text-xs text-muted-foreground">
              {bar.label}
            </span>
            <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-gray-200 dark:bg-gray-700">
              <div
                className={cn("h-full rounded-full", getScoreColor(bar.value))}
                style={{ width: `${pct}%` }}
              />
            </div>
            <span className={cn("w-8 shrink-0 text-right text-xs font-medium", getScoreTextColor(bar.value))}>
              {pct}%
            </span>
          </div>
        );
      })}
    </div>
  );
}

// ===== 关键词高亮 =====

function HighlightText({ text, query }: { text: string; query: string }) {
  if (!query.trim()) return <>{text}</>;

  const keywords = query.trim().split(/\s+/).filter((k) => k.length > 0);
  if (keywords.length === 0) return <>{text}</>;

  // Build a regex that matches any keyword (case-insensitive)
  const escaped = keywords.map((k) => k.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));
  const regex = new RegExp(`(${escaped.join("|")})`, "gi");

  const parts = text.split(regex);
  return (
    <>
      {parts.map((part, i) => {
        const isMatch = keywords.some((k) => part.toLowerCase() === k.toLowerCase());
        return isMatch ? (
          <mark key={i} className="rounded bg-yellow-200 px-0.5 dark:bg-yellow-800/60">
            {part}
          </mark>
        ) : (
          <span key={i}>{part}</span>
        );
      })}
    </>
  );
}

// ===== 搜索结果卡片 =====

function SearchResultCard({ item, query }: { item: SearchResultItem; query: string }) {
  const [showDetail, setShowDetail] = useState(false);

  return (
    <Card className="transition-shadow hover:shadow-md">
      <CardContent className="space-y-3 pt-1">
        {/* 标题行 */}
        <div className="flex items-start justify-between gap-3">
          <Link
            href={`/documents/${item.documentId}`}
            className="flex-1 text-base font-semibold text-primary hover:underline"
          >
            {item.title || "无标题"}
          </Link>
          <div className="flex shrink-0 items-center gap-2">
            <div className="flex items-center gap-1.5">
              <span className="text-xs text-muted-foreground">匹配度</span>
              <span className={cn("text-sm font-bold", getScoreTextColor(item.score))}>
                {Math.round(item.score * 100)}%
              </span>
            </div>
          </div>
        </div>

        {/* 来源信息 */}
        <div className="flex flex-wrap items-center gap-2 text-xs">
          <Badge variant="outline" className={cn("border-transparent font-medium", getSourceTypeStyle(item.sourceType))}>
            {getSourceTypeLabel(item.sourceType)}
          </Badge>
          {item.sourceDomain && (
            <span className="flex items-center gap-1 text-muted-foreground">
              <Globe className="size-3" />
              {item.sourceDomain}
            </span>
          )}
          {item.publishedAt && (
            <span className="flex items-center gap-1 text-muted-foreground">
              <Calendar className="size-3" />
              {formatDate(item.publishedAt)}
            </span>
          )}
          {item.valueScore !== undefined && item.valueScore !== null && (
            <span className="flex items-center gap-1">
              <span className="text-muted-foreground">价值评分</span>
              <span className={cn("font-medium", getScoreTextColor(item.valueScore / 100))}>
                {item.valueScore}
              </span>
            </span>
          )}
          {item.contentLanguage && <Badge variant="secondary">{item.contentLanguage}</Badge>}
          {item.matchChannels?.map((channel) => (
            <Badge key={channel} variant="outline">
              {channel === "fts_zh" ? "中文全文" : channel === "vector" ? "跨语言向量" : "关键词"}
            </Badge>
          ))}
        </div>

        {/* 命中片段 */}
        <p className="line-clamp-3 text-sm leading-relaxed text-muted-foreground">
          <HighlightText text={item.snippet || ""} query={query} />
        </p>
        {item.localizedSnippet && item.originalSnippet && item.localizedSnippet !== item.originalSnippet && (
          <details className="text-xs text-muted-foreground">
            <summary className="cursor-pointer select-none">查看外文原文证据</summary>
            <p className="mt-2 line-clamp-4 leading-relaxed">{item.originalSnippet}</p>
          </details>
        )}

        {/* 底部操作 */}
        <div className="flex items-center justify-between border-t pt-2">
          {item.sourceUrl ? (
            <a
              href={item.sourceUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-1 text-xs text-primary hover:underline"
            >
              <ExternalLink className="size-3" />
              查看原文
            </a>
          ) : (
            <span />
          )}
          {item.scoreDetail && (
            <Button
              variant="ghost"
              size="xs"
              onClick={() => setShowDetail(!showDetail)}
            >
              分数详情
              {showDetail ? (
                <ChevronUp className="ml-1 size-3" />
              ) : (
                <ChevronDown className="ml-1 size-3" />
              )}
            </Button>
          )}
        </div>

        {/* 分数详情 */}
        {showDetail && <ScoreDetailBars detail={item.scoreDetail} />}
      </CardContent>
    </Card>
  );
}

// ===== 主页面 =====

export default function SearchPage() {
  const { topics, fetchTopics } = useTopicStore();

  const [query, setQuery] = useState("");
  const [searchType, setSearchType] = useState<string>("hybrid");
  const [topicId, setTopicId] = useState<string>("all");
  const [selectedSourceTypes, setSelectedSourceTypes] = useState<string[]>([]);
  const [minValueScore, setMinValueScore] = useState<number>(0);
  const [dateFrom, setDateFrom] = useState<string>("");
  const [dateTo, setDateTo] = useState<string>("");
  const [showFilters, setShowFilters] = useState(false);

  // 标签与实体筛选
  const [selectedTagIds, setSelectedTagIds] = useState<string[]>([]);
  const [selectedEntityIds, setSelectedEntityIds] = useState<string[]>([]);
  const [availableTags, setAvailableTags] = useState<Tag[]>([]);
  const [availableEntities, setAvailableEntities] = useState<EntityListItem[]>([]);

  const [result, setResult] = useState<SearchResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasSearched, setHasSearched] = useState(false);

  // 导出状态
  const [exportOpen, setExportOpen] = useState(false);
  const [exportJobId, setExportJobId] = useState<string | null>(null);

  useEffect(() => {
    fetchTopics().catch(() => {});
    tagApi.list().then(setAvailableTags).catch(() => {});
    entityApi.list().then((res) => setAvailableEntities(res.items)).catch(() => {});
  }, [fetchTopics]);

  const toggleSourceType = (type: string) => {
    setSelectedSourceTypes((prev) =>
      prev.includes(type) ? prev.filter((t) => t !== type) : [...prev, type]
    );
  };

  const toggleTag = (tagId: string) => {
    setSelectedTagIds((prev) =>
      prev.includes(tagId) ? prev.filter((t) => t !== tagId) : [...prev, tagId]
    );
  };

  const toggleEntity = (entityId: string) => {
    setSelectedEntityIds((prev) =>
      prev.includes(entityId) ? prev.filter((t) => t !== entityId) : [...prev, entityId]
    );
  };

  const handleSearch = useCallback(async () => {
    if (!query.trim()) {
      toast.error("请输入搜索关键词");
      return;
    }

    setLoading(true);
    setError(null);
    setHasSearched(true);

    try {
      const filters: Record<string, unknown> = {};
      if (selectedSourceTypes.length > 0) filters.sourceTypes = selectedSourceTypes;
      if (minValueScore > 0) filters.minValueScore = minValueScore;
      if (dateFrom) filters.dateFrom = dateFrom;
      if (dateTo) filters.dateTo = dateTo;
      if (selectedTagIds.length > 0) filters.tagIds = selectedTagIds;
      if (selectedEntityIds.length > 0) filters.entityIds = selectedEntityIds;

      const data = await searchApi.search({
        query: query.trim(),
        searchType: searchType as "keyword" | "vector" | "hybrid",
        topicId: topicId !== "all" ? topicId : undefined,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
        limit: 20,
        fusionMode: "rrf",
        evidenceMode: "bilingual",
      });
      setResult(data);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "搜索失败，请重试";
      setError(message);
      setResult(null);
    } finally {
      setLoading(false);
    }
  }, [query, searchType, topicId, selectedSourceTypes, minValueScore, dateFrom, dateTo, selectedTagIds, selectedEntityIds]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSearch();
    }
  };

  const handleReset = () => {
    setQuery("");
    setSearchType("hybrid");
    setTopicId("all");
    setSelectedSourceTypes([]);
    setMinValueScore(0);
    setDateFrom("");
    setDateTo("");
    setSelectedTagIds([]);
    setSelectedEntityIds([]);
    setResult(null);
    setHasSearched(false);
    setError(null);
  };

  const handleExportJson = async () => {
    if (!query.trim()) {
      toast.error("请先输入搜索关键词");
      return;
    }
    try {
      const filters: {
        sourceTypes?: string[];
        dateFrom?: string;
        dateTo?: string;
        minValueScore?: number;
      } = {};
      if (selectedSourceTypes.length > 0) filters.sourceTypes = selectedSourceTypes;
      if (minValueScore > 0) filters.minValueScore = minValueScore;
      if (dateFrom) filters.dateFrom = dateFrom;
      if (dateTo) filters.dateTo = dateTo;

      const res = await exportApi.searchJson({
        query: query.trim(),
        topicId: topicId !== "all" ? topicId : undefined,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
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

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div>
        <h1 className="text-2xl font-bold">搜索</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          跨专题检索知识资料，支持关键词、语义与混合检索
        </p>
      </div>

      {/* 搜索框 */}
      <Card>
        <CardContent className="space-y-4 pt-1">
          {/* 大搜索输入 */}
          <div className="flex gap-2">
            <div className="relative flex-1">
              <SearchIcon className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                placeholder="输入搜索关键词..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={handleKeyDown}
                className="h-10 pl-9 text-base"
              />
            </div>
            <Button
              size="lg"
              onClick={handleSearch}
              disabled={loading || !query.trim()}
            >
              {loading ? (
                <Loader2 className="mr-2 size-4 animate-spin" />
              ) : (
                <SearchIcon className="mr-2 size-4" />
              )}
              搜索
            </Button>
          </div>

          {/* 搜索类型 + 过滤器切换 */}
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex items-center gap-2">
              <span className="text-xs font-medium text-muted-foreground">检索方式</span>
              <Select value={searchType} onValueChange={(v) => setSearchType(v as string)}>
                <SelectTrigger size="sm" className="w-28">
                  <SelectValue>
                    {SEARCH_TYPES.find((item) => item.value === searchType)?.label ?? "混合检索"}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {SEARCH_TYPES.map((t) => (
                    <SelectItem key={t.value} value={t.value}>
                      {t.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <Button
              variant="outline"
              size="sm"
              onClick={() => setShowFilters(!showFilters)}
            >
              <SlidersHorizontal className="mr-1.5 size-3.5" />
              过滤器
              {(selectedSourceTypes.length > 0 || minValueScore > 0 || dateFrom || dateTo || topicId !== "all" || selectedTagIds.length > 0 || selectedEntityIds.length > 0) && (
                <span className="ml-1.5 flex size-1.5 rounded-full bg-primary" />
              )}
              {showFilters ? (
                <ChevronUp className="ml-1 size-3" />
              ) : (
                <ChevronDown className="ml-1 size-3" />
              )}
            </Button>

            {hasSearched && (
              <Button variant="ghost" size="sm" onClick={handleReset}>
                重置
              </Button>
            )}
          </div>

          {/* 过滤器区域 */}
          {showFilters && (
            <div className="space-y-4 rounded-lg border bg-muted/30 p-4">
              {/* 专题 + 来源类型 */}
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label className="text-xs">专题筛选</Label>
                  <Select value={topicId} onValueChange={(v) => setTopicId(v as string)}>
                    <SelectTrigger className="w-full">
                      <SelectValue placeholder="全部专题">
                        {topicId === "all"
                          ? "全部专题"
                          : topics.find((topic) => topic.id === topicId)?.name ?? "未知专题"}
                      </SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">全部专题</SelectItem>
                      {topics.map((t) => (
                        <SelectItem key={t.id} value={t.id}>
                          {t.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-2">
                  <Label className="text-xs">来源类型</Label>
                  <div className="flex flex-wrap gap-2">
                    {SOURCE_TYPES.map((st) => {
                      const active = selectedSourceTypes.includes(st.value);
                      return (
                        <button
                          key={st.value}
                          type="button"
                          onClick={() => toggleSourceType(st.value)}
                          className={cn(
                            "rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors",
                            active
                              ? cn(st.color, "border-transparent")
                              : "border-border bg-background text-muted-foreground hover:bg-muted"
                          )}
                        >
                          {st.label}
                        </button>
                      );
                    })}
                  </div>
                </div>
              </div>

              {/* 价值评分 + 时间范围 */}
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label className="text-xs">
                    最低价值评分：<span className="font-bold text-foreground">{minValueScore}</span>
                  </Label>
                  <input
                    type="range"
                    min={0}
                    max={100}
                    step={10}
                    value={minValueScore}
                    onChange={(e) => setMinValueScore(Number(e.target.value))}
                    className="w-full accent-primary"
                  />
                </div>

                <div className="space-y-2">
                  <Label className="text-xs">时间范围</Label>
                  <div className="flex items-center gap-2">
                    <Input
                      type="date"
                      value={dateFrom}
                      onChange={(e) => setDateFrom(e.target.value)}
                      className="flex-1"
                    />
                    <span className="text-xs text-muted-foreground">至</span>
                    <Input
                      type="date"
                      value={dateTo}
                      onChange={(e) => setDateTo(e.target.value)}
                      className="flex-1"
                    />
                  </div>
                </div>
              </div>

              {/* 标签 + 实体筛选 */}
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label className="text-xs">
                    标签筛选
                    {selectedTagIds.length > 0 && (
                      <span className="ml-1 text-muted-foreground">（已选 {selectedTagIds.length}）</span>
                    )}
                  </Label>
                  <div className="flex flex-wrap gap-2">
                    {availableTags.length === 0 ? (
                      <span className="text-xs text-muted-foreground">暂无标签</span>
                    ) : (
                      availableTags.map((tag) => {
                        const active = selectedTagIds.includes(tag.id);
                        return (
                          <button
                            key={tag.id}
                            type="button"
                            onClick={() => toggleTag(tag.id)}
                            className={cn(
                              "rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors",
                              active
                                ? "border-transparent bg-primary text-primary-foreground"
                                : "border-border bg-background text-muted-foreground hover:bg-muted"
                            )}
                          >
                            {tag.displayName || tag.name}
                          </button>
                        );
                      })
                    )}
                  </div>
                </div>

                <div className="space-y-2">
                  <Label className="text-xs">
                    实体筛选
                    {selectedEntityIds.length > 0 && (
                      <span className="ml-1 text-muted-foreground">（已选 {selectedEntityIds.length}）</span>
                    )}
                  </Label>
                  <div className="flex flex-wrap gap-2">
                    {availableEntities.length === 0 ? (
                      <span className="text-xs text-muted-foreground">暂无实体</span>
                    ) : (
                      availableEntities.map((entity) => {
                        const active = selectedEntityIds.includes(entity.id);
                        return (
                          <button
                            key={entity.id}
                            type="button"
                            onClick={() => toggleEntity(entity.id)}
                            className={cn(
                              "rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors",
                              active
                                ? "border-transparent bg-primary text-primary-foreground"
                                : "border-border bg-background text-muted-foreground hover:bg-muted"
                            )}
                          >
                            {entity.name}
                          </button>
                        );
                      })
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* 搜索结果 */}
      {loading && (
        <div className="flex flex-col items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-primary" />
          <p className="mt-3 text-sm text-muted-foreground">正在检索...</p>
        </div>
      )}

      {!loading && error && (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-12 text-center">
            <AlertCircle className="mb-3 size-10 text-red-500" />
            <p className="text-sm font-medium text-red-600">{error}</p>
            <Button variant="outline" className="mt-4" onClick={handleSearch}>
              重试
            </Button>
          </CardContent>
        </Card>
      )}

      {!loading && !error && hasSearched && result && (
        <>
          {/* 结果统计 */}
          <div className="flex items-center justify-between">
            <p className="text-sm text-muted-foreground">
              找到 <span className="font-bold text-foreground">{result.total}</span> 条结果
              {result.query && (
                <span>（关键词：&ldquo;{result.query}&rdquo;）</span>
              )}
            </p>
            <div className="flex items-center gap-2">
              <Badge variant="secondary">
                {SEARCH_TYPES.find((t) => t.value === result.searchType)?.label ?? result.searchType}
              </Badge>
              <Button
                variant="outline"
                size="sm"
                onClick={handleExportJson}
              >
                <FileDown className="mr-1.5 size-3.5" />
                导出JSON
              </Button>
            </div>
          </div>

          {/* 结果列表 */}
          {result.items.length === 0 ? (
            <Card>
              <CardContent className="flex flex-col items-center justify-center py-16 text-center">
                <SearchX className="mb-3 size-12 text-muted-foreground/50" />
                <p className="text-lg font-medium">未找到相关结果</p>
                <p className="mt-1 text-sm text-muted-foreground">
                  尝试更换关键词或调整过滤条件
                </p>
              </CardContent>
            </Card>
          ) : (
            <div className="space-y-3">
              {result.items.map((item, idx) => (
                <SearchResultCard key={`${item.documentId}-${item.chunkId}-${idx}`} item={item} query={query} />
              ))}
            </div>
          )}
        </>
      )}

      {/* 初始空状态 */}
      {!loading && !error && !hasSearched && (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <SearchIcon className="mb-3 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">开始搜索您的知识库</p>
            <p className="mt-1 text-sm text-muted-foreground">
              输入关键词，支持混合检索（关键词 + 语义）
            </p>
          </CardContent>
        </Card>
      )}

      {/* 导出状态弹窗 */}
      <ExportStatusDialog
        open={exportOpen}
        onOpenChange={setExportOpen}
        exportJobId={exportJobId}
      />
    </div>
  );
}
