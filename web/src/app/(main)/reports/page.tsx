"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ClipboardList,
  Loader2,
  CalendarDays,
  CalendarRange,
  FileSearch,
  Eye,
  FileDown,
  RefreshCw,
  Trash2,
  ChevronLeft,
  ChevronRight,
  Search,
} from "lucide-react";
import { reportApi, exportApi, ApiRequestError } from "@/lib/api";
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
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  ReportStatusBadge,
  ReportTypeBadge,
  QualityScoreBar,
} from "@/components/report-badge";
import { ReportGenerateDialog } from "@/components/report-generate-dialog";
import { ExportStatusDialog } from "@/components/export-status-dialog";

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

function formatDateShort(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleDateString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
}

export default function ReportsPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { topics, fetchTopics } = useTopicStore();

  const [topicFilter, setTopicFilter] = useState<string>("all");
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [searchQuery, setSearchQuery] = useState("");
  const [generateOpen, setGenerateOpen] = useState(false);
  const [generateTab, setGenerateTab] = useState<string>("daily");

  // 分页
  const [page, setPage] = useState(1);
  const [pageSize] = useState(10);

  // 导出状态弹窗
  const [exportOpen, setExportOpen] = useState(false);
  const [exportJobId, setExportJobId] = useState<string | null>(null);

  // 重新生成中
  const [regeneratingId, setRegeneratingId] = useState<string | null>(null);

  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  // 筛选条件变化时重置页码
  useEffect(() => {
    setPage(1);
  }, [topicFilter, typeFilter, searchQuery]);

  const { data: reports, isLoading } = useQuery({
    queryKey: ["reports", topicFilter, typeFilter, page, pageSize],
    queryFn: () =>
      reportApi.list({
        topicId: topicFilter !== "all" ? topicFilter : undefined,
        reportType: typeFilter !== "all" ? typeFilter : undefined,
        page,
        pageSize,
      }),
  });

  const topicMap = new Map(topics.map((t) => [t.id, t.name]));
  const allReports = reports?.items ?? [];
  const trimmedSearch = searchQuery.trim().toLowerCase();
  const displayReports = trimmedSearch
    ? allReports.filter((r) =>
        (r.title || "").toLowerCase().includes(trimmedSearch)
      )
    : allReports;
  const totalPages = reports?.totalPages ?? 1;
  const total = reports?.total ?? 0;

  // 打开生成弹窗
  const openGenerateDialog = (tab: string) => {
    setGenerateTab(tab);
    setGenerateOpen(true);
  };

  // 导出 Markdown
  const handleExport = async (reportId: string) => {
    try {
      const res = await exportApi.reportMarkdown({ reportId });
      setExportJobId(res.exportJobId);
      setExportOpen(true);
      toast.success("导出任务已创建");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "导出失败，请重试";
      toast.error(message);
    }
  };

  // 重新生成
  const handleRegenerate = async (reportId: string) => {
    setRegeneratingId(reportId);
    try {
      await reportApi.regenerate(reportId);
      toast.success("报告重新生成中...");
      queryClient.invalidateQueries({ queryKey: ["reports"] });
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "重新生成失败";
      toast.error(message);
    } finally {
      setRegeneratingId(null);
    }
  };

  // 删除（暂无 API，提示用户）
  const handleDelete = () => {
    toast.info("暂不支持删除报告");
  };

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">报告管理</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            生成和管理日报、周报及专题研究报告
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => openGenerateDialog("daily")}
          >
            <CalendarDays className="mr-1.5 size-3.5" />
            生成日报
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => openGenerateDialog("weekly")}
          >
            <CalendarRange className="mr-1.5 size-3.5" />
            生成周报
          </Button>
          <Button size="sm" onClick={() => openGenerateDialog("topic")}>
            <FileSearch className="mr-1.5 size-3.5" />
            生成专题报告
          </Button>
        </div>
      </div>

      {/* 筛选器 + 列表 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>报告列表</CardTitle>
            <div className="flex gap-2">
              <div className="relative">
                <Search className="absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
                <Input
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="搜索报告标题..."
                  className="h-8 w-48 pl-8 text-sm"
                />
              </div>
              <Select
                value={topicFilter}
                onValueChange={(v) => setTopicFilter(v as string)}
              >
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
              <Select
                value={typeFilter}
                onValueChange={(v) => setTypeFilter(v as string)}
              >
                <SelectTrigger size="sm" className="w-32">
                  <SelectValue placeholder="报告类型" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部类型</SelectItem>
                  <SelectItem value="daily">日报</SelectItem>
                  <SelectItem value="weekly">周报</SelectItem>
                  <SelectItem value="topic">专题报告</SelectItem>
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
          ) : displayReports.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <ClipboardList className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无报告，点击上方按钮生成报告
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>类型</TableHead>
                  <TableHead>专题</TableHead>
                  <TableHead>时间范围</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>质量评分</TableHead>
                  <TableHead>创建时间</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayReports.map((report) => (
                  <TableRow key={report.id}>
                    <TableCell className="max-w-xs">
                      <button
                        className="truncate font-medium text-primary hover:underline"
                        onClick={() => router.push(`/reports/${report.id}`)}
                      >
                        {report.title || "未命名报告"}
                      </button>
                    </TableCell>
                    <TableCell>
                      <ReportTypeBadge reportType={report.reportType} />
                    </TableCell>
                    <TableCell>
                      {topicMap.get(report.topicId ?? "") ?? "-"}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {report.startDate || report.endDate
                        ? `${formatDateShort(report.startDate)} ~ ${formatDateShort(report.endDate)}`
                        : "-"}
                    </TableCell>
                    <TableCell>
                      <ReportStatusBadge status={report.status} />
                    </TableCell>
                    <TableCell>
                      <QualityScoreBar score={report.qualityScore} />
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(report.createdAt)}
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title="查看"
                          onClick={() =>
                            router.push(`/reports/${report.id}`)
                          }
                        >
                          <Eye className="size-3.5" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title="导出Markdown"
                          onClick={() => handleExport(report.id)}
                        >
                          <FileDown className="size-3.5" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title="重新生成"
                          disabled={regeneratingId === report.id}
                          onClick={() => handleRegenerate(report.id)}
                        >
                          {regeneratingId === report.id ? (
                            <Loader2 className="size-3.5 animate-spin" />
                          ) : (
                            <RefreshCw className="size-3.5" />
                          )}
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title="删除"
                          onClick={handleDelete}
                        >
                          <Trash2 className="size-3.5 text-muted-foreground" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 分页控件 */}
      {!isLoading && displayReports.length > 0 && (
        <div className="flex items-center justify-between">
          <p className="text-sm text-muted-foreground">
            共 {total} 条，第 {page}/{totalPages} 页
          </p>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              <ChevronLeft className="mr-1 size-3.5" />
              上一页
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            >
              下一页
              <ChevronRight className="ml-1 size-3.5" />
            </Button>
          </div>
        </div>
      )}

      {/* 弹窗 */}
      <ReportGenerateDialog
        open={generateOpen}
        onOpenChange={setGenerateOpen}
        defaultTab={generateTab}
      />
      <ExportStatusDialog
        open={exportOpen}
        onOpenChange={setExportOpen}
        exportJobId={exportJobId}
      />
    </div>
  );
}
