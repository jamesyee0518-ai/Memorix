"use client";

import { useState, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import {
  FolderOpen,
  FileText,
  Clock,
  AlertCircle,
  Plus,
  Upload,
  Loader2,
  Boxes,
  CheckCircle2,
  Search,
  MessageCircle,
  ClipboardList,
  FileSearch,
} from "lucide-react";
import { useAuthStore } from "@/stores/auth-store";
import { useTopicStore } from "@/stores/topic-store";
import { sourceApi, documentApi, entityApi, reportApi } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { StatusBadge, getSourceTypeLabel } from "@/components/status-badge";
import { AiStatusBadge, ValueScoreBar } from "@/components/ai-badge";
import {
  ReportStatusBadge,
  ReportTypeBadge,
} from "@/components/report-badge";
import { ImportDialog } from "@/components/import-dialog";
import { TopicFormDialog } from "@/components/topic-form-dialog";
import { ReportGenerateDialog } from "@/components/report-generate-dialog";
import { WorkspaceStatusPanel } from "@/components/workspace-status-panel";
import { HybridDataFlowPanel } from "@/components/hybrid-data-flow-panel";

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

export default function DashboardPage() {
  const { user } = useAuthStore();
  const { topics, fetchTopics } = useTopicStore();
  const [importOpen, setImportOpen] = useState(false);
  const [topicDialogOpen, setTopicDialogOpen] = useState(false);
  const [reportDialogOpen, setReportDialogOpen] = useState(false);

  // 获取专题列表
  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  // 获取最近导入资料
  const { data: recentSources, isLoading: sourcesLoading } = useQuery({
    queryKey: ["sources", "recent"],
    queryFn: () => sourceApi.list(),
  });

  // 获取文档列表
  const { data: documents, isLoading: documentsLoading } = useQuery({
    queryKey: ["documents", "dashboard"],
    queryFn: () => documentApi.list(),
  });

  // 获取实体列表
  const { data: entities } = useQuery({
    queryKey: ["entities", "dashboard"],
    queryFn: () => entityApi.list(),
  });

  // 获取最近报告
  const { data: reports, isLoading: reportsLoading } = useQuery({
    queryKey: ["reports", "dashboard"],
    queryFn: () => reportApi.list(),
  });

  // 计算统计数据
  const topicCount = topics.length;
  const totalSources = topics.reduce((sum, t) => sum + (t.documentCount || 0), 0);
  const pendingCount = topics.reduce((sum, t) => sum + (t.pendingCount || 0), 0);
  const failedCount = topics.reduce((sum, t) => sum + (t.failedCount || 0), 0);
  const documentCount = documents?.total ?? 0;
  const processedCount = (documents?.items ?? []).filter(
    (d) => d.aiStatus === "done"
  ).length;
  const entityCount = entities?.total ?? 0;

  // 构建专题 ID 到名称的映射
  const topicMap = new Map(topics.map((t) => [t.id, t.name]));

  const stats = [
    {
      label: "专题数",
      value: topicCount,
      icon: FolderOpen,
      color: "text-blue-600",
      bg: "bg-blue-50",
    },
    {
      label: "资料数",
      value: totalSources,
      icon: FileText,
      color: "text-green-600",
      bg: "bg-green-50",
    },
    {
      label: "处理中",
      value: pendingCount,
      icon: Clock,
      color: "text-amber-600",
      bg: "bg-amber-50",
    },
    {
      label: "失败数",
      value: failedCount,
      icon: AlertCircle,
      color: "text-red-600",
      bg: "bg-red-50",
    },
    {
      label: "文档数",
      value: documentCount,
      icon: FileText,
      color: "text-indigo-600",
      bg: "bg-indigo-50",
    },
    {
      label: "已处理",
      value: processedCount,
      icon: CheckCircle2,
      color: "text-teal-600",
      bg: "bg-teal-50",
    },
    {
      label: "实体数",
      value: entityCount,
      icon: Boxes,
      color: "text-purple-600",
      bg: "bg-purple-50",
    },
  ];

  const displaySources = (recentSources?.items ?? []).slice(0, 10);
  const displayDocuments = (documents?.items ?? []).slice(0, 10);
  const displayReports = (reports?.items ?? []).slice(0, 5);
  const displayName = user?.nickname ?? "本地用户";

  return (
    <div className="space-y-6">
      {/* 欢迎信息 */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Dashboard</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            欢迎回来，{displayName}！这里是您的知识资产概览。
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => setImportOpen(true)}>
            <Upload className="mr-2 size-4" />
            导入资料
          </Button>
          <Button onClick={() => setTopicDialogOpen(true)}>
            <Plus className="mr-2 size-4" />
            创建专题
          </Button>
        </div>
      </div>

      <WorkspaceStatusPanel compact />

      <HybridDataFlowPanel compact />

      {/* 统计卡片 */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {stats.map((stat) => {
          const Icon = stat.icon;
          return (
            <Card key={stat.label}>
              <CardContent className="flex items-center gap-4 pt-1">
                <div className={`flex size-12 items-center justify-center rounded-lg ${stat.bg}`}>
                  <Icon className={`size-6 ${stat.color}`} />
                </div>
                <div>
                  <p className="text-2xl font-bold">{stat.value}</p>
                  <p className="text-sm text-muted-foreground">{stat.label}</p>
                </div>
              </CardContent>
            </Card>
          );
        })}
      </div>

      {/* 快速入口 */}
      <div className="grid gap-4 sm:grid-cols-3">
        <Link href="/search">
          <Card className="cursor-pointer transition-shadow hover:shadow-md">
            <CardContent className="flex items-center gap-4 pt-1">
              <div className="flex size-12 items-center justify-center rounded-lg bg-indigo-50">
                <Search className="size-6 text-indigo-600" />
              </div>
              <div className="flex-1">
                <p className="font-semibold">快速搜索</p>
                <p className="text-sm text-muted-foreground">
                  跨专题检索知识资料，支持关键词与语义混合检索
                </p>
              </div>
            </CardContent>
          </Card>
        </Link>
        <Link href="/qa">
          <Card className="cursor-pointer transition-shadow hover:shadow-md">
            <CardContent className="flex items-center gap-4 pt-1">
              <div className="flex size-12 items-center justify-center rounded-lg bg-purple-50">
                <MessageCircle className="size-6 text-purple-600" />
              </div>
              <div className="flex-1">
                <p className="font-semibold">智能问答</p>
                <p className="text-sm text-muted-foreground">
                  基于知识库的 AI 问答，回答附带引用来源
                </p>
              </div>
            </CardContent>
          </Card>
        </Link>
        <Card
          className="cursor-pointer transition-shadow hover:shadow-md"
          onClick={() => setReportDialogOpen(true)}
        >
          <CardContent className="flex items-center gap-4 pt-1">
            <div className="flex size-12 items-center justify-center rounded-lg bg-orange-50">
              <FileSearch className="size-6 text-orange-600" />
            </div>
            <div className="flex-1">
              <p className="font-semibold">快速生成报告</p>
              <p className="text-sm text-muted-foreground">
                生成日报、周报或专题研究报告
              </p>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* 最近导入资料 */}
      <Card>
        <CardHeader>
          <CardTitle>最近导入资料</CardTitle>
        </CardHeader>
        <CardContent>
          {sourcesLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displaySources.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <FileText className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">暂无资料，点击上方按钮导入</p>
            </div>
          ) : (
            <Table className="table-fixed min-w-[760px]">
              <colgroup>
                <col className="w-[46%]" />
                <col className="w-[18%]" />
                <col className="w-[10%]" />
                <col className="w-[10%]" />
                <col className="w-[16%]" />
              </colgroup>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>专题</TableHead>
                  <TableHead>类型</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>导入时间</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displaySources.map((source) => (
                  <TableRow key={source.id}>
                    <TableCell className="min-w-0 overflow-hidden">
                      <Link
                        href={`/sources/${source.id}`}
                        className="block truncate font-medium text-primary hover:underline"
                        title={source.title || "未命名"}
                      >
                        {source.title || "未命名"}
                      </Link>
                    </TableCell>
                    <TableCell className="overflow-hidden">
                      <span
                        className="block truncate"
                        title={topicMap.get(source.topicId ?? "") ?? "-"}
                      >
                        {topicMap.get(source.topicId ?? "") ?? "-"}
                      </span>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      {getSourceTypeLabel(source.sourceType)}
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <StatusBadge status={source.status} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-muted-foreground">
                      {formatDate(source.createdAt)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 最近文档 */}
      <Card>
        <CardHeader>
          <CardTitle>最近文档</CardTitle>
        </CardHeader>
        <CardContent>
          {documentsLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displayDocuments.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <FileText className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无文档，触发 AI 处理后将在此显示
              </p>
            </div>
          ) : (
            <Table className="table-fixed min-w-[760px]">
              <colgroup>
                <col className="w-[46%]" />
                <col className="w-[18%]" />
                <col className="w-[10%]" />
                <col className="w-[12%]" />
                <col className="w-[14%]" />
              </colgroup>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>专题</TableHead>
                  <TableHead>AI状态</TableHead>
                  <TableHead>价值评分</TableHead>
                  <TableHead>创建时间</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayDocuments.map((doc) => (
                  <TableRow key={doc.id}>
                    <TableCell className="min-w-0 overflow-hidden">
                      <Link
                        href={`/documents/${doc.id}`}
                        className="block truncate font-medium text-primary hover:underline"
                        title={doc.title || "未命名"}
                      >
                        {doc.title || "未命名"}
                      </Link>
                    </TableCell>
                    <TableCell className="overflow-hidden">
                      <span
                        className="block truncate"
                        title={topicMap.get(doc.topicId ?? "") ?? "-"}
                      >
                        {topicMap.get(doc.topicId ?? "") ?? "-"}
                      </span>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <AiStatusBadge status={doc.aiStatus} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <ValueScoreBar score={doc.valueScore} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-muted-foreground">
                      {formatDate(doc.createdAt)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 最近报告 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="flex items-center gap-2">
              <ClipboardList className="size-4 text-orange-600" />
              最近报告
            </CardTitle>
            <Link
              href="/reports"
              className="text-sm text-primary hover:underline"
            >
              查看全部
            </Link>
          </div>
        </CardHeader>
        <CardContent>
          {reportsLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displayReports.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <ClipboardList className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无报告，点击&ldquo;快速生成报告&rdquo;开始创建
              </p>
            </div>
          ) : (
            <Table className="table-fixed min-w-[760px]">
              <colgroup>
                <col className="w-[40%]" />
                <col className="w-[14%]" />
                <col className="w-[20%]" />
                <col className="w-[10%]" />
                <col className="w-[16%]" />
              </colgroup>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>类型</TableHead>
                  <TableHead>专题</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>创建时间</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayReports.map((report) => (
                  <TableRow key={report.id}>
                    <TableCell className="min-w-0 overflow-hidden">
                      <Link
                        href={`/reports/${report.id}`}
                        className="block truncate font-medium text-primary hover:underline"
                        title={report.title || "未命名报告"}
                      >
                        {report.title || "未命名报告"}
                      </Link>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <ReportTypeBadge reportType={report.reportType} />
                    </TableCell>
                    <TableCell className="overflow-hidden">
                      <span
                        className="block truncate"
                        title={topicMap.get(report.topicId ?? "") ?? "-"}
                      >
                        {topicMap.get(report.topicId ?? "") ?? "-"}
                      </span>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <ReportStatusBadge status={report.status} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-muted-foreground">
                      {formatDate(report.createdAt)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 弹窗 */}
      <ImportDialog
        open={importOpen}
        onOpenChange={setImportOpen}
        onSuccess={() => {
          fetchTopics().catch(() => {});
        }}
      />
      <TopicFormDialog
        open={topicDialogOpen}
        onOpenChange={setTopicDialogOpen}
        onSuccess={() => {
          fetchTopics().catch(() => {});
        }}
      />
      <ReportGenerateDialog
        open={reportDialogOpen}
        onOpenChange={setReportDialogOpen}
      />
    </div>
  );
}
