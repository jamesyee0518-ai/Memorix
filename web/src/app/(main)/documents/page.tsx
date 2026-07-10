"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { FileText, Loader2 } from "lucide-react";
import { documentApi } from "@/lib/api";
import { useTopicStore } from "@/stores/topic-store";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { AiStatusBadge, ValueScoreBar } from "@/components/ai-badge";
import { Badge } from "@/components/ui/badge";

function formatDate(dateStr: string): string {
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

function MiniStageBadge({ label, status }: { label: string; status?: string }) {
  if (!status) return null;
  const colors: Record<string, string> = {
    pending: "bg-gray-200 text-gray-500 dark:bg-gray-700 dark:text-gray-400",
    processing: "bg-blue-200 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
    done: "bg-green-200 text-green-700 dark:bg-green-900/40 dark:text-green-300",
    failed: "bg-red-200 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  };
  return (
    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-medium ${colors[status] || colors.pending}`}>
      {label}
    </span>
  );
}

export default function DocumentsPage() {
  const router = useRouter();
  const { topics, fetchTopics } = useTopicStore();
  const [topicFilter, setTopicFilter] = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<string>("all");

  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  const { data: documents, isLoading } = useQuery({
    queryKey: ["documents", topicFilter, statusFilter],
    queryFn: () =>
      documentApi.list({
        topicId: topicFilter !== "all" ? topicFilter : undefined,
        aiStatus: statusFilter !== "all" ? statusFilter : undefined,
      }),
  });

  const topicMap = new Map(topics.map((t) => [t.id, t.name]));
  const displayDocs = documents?.items ?? [];

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div>
        <h1 className="text-2xl font-bold">文档管理</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          浏览 AI 处理后的文档及分析结果
        </p>
      </div>

      {/* 筛选器 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>文档列表</CardTitle>
            <div className="flex gap-2">
              <Select value={topicFilter} onValueChange={(v) => setTopicFilter(v as string)}>
                <SelectTrigger size="sm" className="w-40">
                  <SelectValue placeholder="专题筛选" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部专题</SelectItem>
                  {topics.map((topic) => (
                    <SelectItem key={topic.id} value={topic.id}>
                      {topic.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as string)}>
                <SelectTrigger size="sm" className="w-32">
                  <SelectValue placeholder="AI状态" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部状态</SelectItem>
                  <SelectItem value="pending">待处理</SelectItem>
                  <SelectItem value="processing">处理中</SelectItem>
                  <SelectItem value="done">已完成</SelectItem>
                  <SelectItem value="failed">失败</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displayDocs.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <FileText className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无文档，请先导入资料并触发 AI 处理
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>摘要</TableHead>
                  <TableHead>专题</TableHead>
                  <TableHead>AI状态</TableHead>
                  <TableHead>价值评分</TableHead>
                  <TableHead>质量评分</TableHead>
                  <TableHead>来源</TableHead>
                  <TableHead>处理状态</TableHead>
                  <TableHead>创建时间</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayDocs.map((doc) => (
                  <TableRow
                    key={doc.id}
                    className="cursor-pointer"
                    onClick={() => router.push(`/documents/${doc.id}`)}
                  >
                    <TableCell className="max-w-xs font-medium">
                      {doc.title || "未命名"}
                    </TableCell>
                    <TableCell className="max-w-sm">
                      <span className="line-clamp-1 text-muted-foreground">
                        {doc.summary || "-"}
                      </span>
                    </TableCell>
                    <TableCell>
                      {topicMap.get(doc.topicId ?? "") ?? "-"}
                    </TableCell>
                    <TableCell>
                      <AiStatusBadge status={doc.aiStatus} />
                    </TableCell>
                    <TableCell>
                      <ValueScoreBar score={doc.valueScore} />
                    </TableCell>
                    <TableCell>
                      {doc.qualityScore !== undefined && doc.qualityScore !== null ? (
                        <span className="text-sm font-medium">{doc.qualityScore}</span>
                      ) : (
                        <span className="text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      {doc.sourceType ? (
                        <Badge variant="outline" className="text-xs">
                          {doc.sourceType}
                        </Badge>
                      ) : (
                        <span className="text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        <MiniStageBadge label="解析" status={doc.parseStatus} />
                        <MiniStageBadge label="清洗" status={doc.cleanStatus} />
                        <MiniStageBadge label="AI" status={doc.aiStatus} />
                        <MiniStageBadge label="索引" status={doc.indexStatus} />
                      </div>
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(doc.createdAt)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
