"use client";

import { useState, Fragment } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  MessagesSquare,
  Loader2,
  ChevronDown,
  ChevronRight,
  Inbox,
  Clock,
  CheckCircle2,
  XCircle,
} from "lucide-react";
import { feedbackApi, ApiRequestError } from "@/lib/api";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
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
import { Markdown } from "@/components/markdown";

// ===== 配置映射 =====

const feedbackTypeLabels: Record<string, string> = {
  bug: "Bug 反馈",
  ux: "体验建议",
  feature: "功能需求",
  quality: "内容质量",
  performance: "性能问题",
  pricing: "价格与套餐",
  general: "其他",
  qa_feedback: "问答反馈",
};

const severityLabels: Record<string, string> = {
  critical: "严重",
  high: "高",
  medium: "中",
  low: "低",
  normal: "一般",
};

const severityClassNames: Record<string, string> = {
  critical: "bg-red-100 text-red-700",
  high: "bg-orange-100 text-orange-700",
  medium: "bg-yellow-100 text-yellow-700",
  low: "bg-blue-100 text-blue-700",
  normal: "bg-gray-100 text-gray-600",
};

const statusLabels: Record<string, string> = {
  open: "待处理",
  in_progress: "处理中",
  resolved: "已解决",
  closed: "已关闭",
};

const statusClassNames: Record<string, string> = {
  open: "bg-blue-100 text-blue-700",
  in_progress: "bg-yellow-100 text-yellow-700",
  resolved: "bg-green-100 text-green-700",
  closed: "bg-gray-100 text-gray-600",
};

const priorityLabels: Record<string, string> = {
  urgent: "紧急",
  high: "高",
  medium: "中",
  low: "低",
  normal: "一般",
};

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

export default function FeedbackAdminPage() {
  const queryClient = useQueryClient();
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [severityFilter, setSeverityFilter] = useState<string>("all");
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // 获取反馈列表
  const { data, isLoading } = useQuery({
    queryKey: ["feedback-admin", statusFilter, typeFilter, severityFilter],
    queryFn: () =>
      feedbackApi.listAll({
        status: statusFilter !== "all" ? statusFilter : undefined,
        type: typeFilter !== "all" ? typeFilter : undefined,
        severity: severityFilter !== "all" ? severityFilter : undefined,
      }),
  });

  const feedbacks = data?.items ?? [];

  // 获取统计
  const { data: statsData } = useQuery({
    queryKey: ["feedback-stats"],
    queryFn: () => feedbackApi.stats(),
  });

  const stats = statsData ?? {
    total: 0,
    open: 0,
    inProgress: 0,
    resolved: 0,
    closed: 0,
  };

  // 更新反馈
  const updateMutation = useMutation({
    mutationFn: ({
      id,
      data,
    }: {
      id: string;
      data: { status?: string; priority?: string };
    }) => feedbackApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["feedback-admin"] });
      queryClient.invalidateQueries({ queryKey: ["feedback-stats"] });
      toast.success("反馈已更新");
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "更新失败";
      toast.error(message);
    },
  });

  const statCards = [
    {
      label: "总反馈数",
      value: stats.total,
      icon: MessagesSquare,
      color: "text-blue-600",
      bg: "bg-blue-50",
    },
    {
      label: "待处理",
      value: stats.open,
      icon: Inbox,
      color: "text-purple-600",
      bg: "bg-purple-50",
    },
    {
      label: "处理中",
      value: stats.inProgress,
      icon: Clock,
      color: "text-yellow-600",
      bg: "bg-yellow-50",
    },
    {
      label: "已解决",
      value: stats.resolved,
      icon: CheckCircle2,
      color: "text-green-600",
      bg: "bg-green-50",
    },
    {
      label: "已关闭",
      value: stats.closed,
      icon: XCircle,
      color: "text-gray-600",
      bg: "bg-gray-50",
    },
  ];

  const toggleExpand = (id: string) => {
    setExpandedId((prev) => (prev === id ? null : id));
  };

  return (
    <div className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold">反馈管理</h2>
        <p className="text-sm text-muted-foreground">
          查看和管理所有用户提交的反馈
        </p>
      </div>

      {/* 统计卡片 */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
        {statCards.map((card) => {
          const Icon = card.icon;
          return (
            <Card key={card.label}>
              <CardContent className="flex flex-col items-center gap-2 py-4 text-center">
                <div className={`flex size-10 items-center justify-center rounded-lg ${card.bg}`}>
                  <Icon className={`size-5 ${card.color}`} />
                </div>
                <span className="text-2xl font-bold">{card.value}</span>
                <span className="text-xs text-muted-foreground">
                  {card.label}
                </span>
              </CardContent>
            </Card>
          );
        })}
      </div>

      {/* 筛选器 */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">类型：</span>
          <Select
            value={typeFilter}
            onValueChange={(v) => setTypeFilter(v as string)}
          >
            <SelectTrigger className="w-36">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部</SelectItem>
              <SelectItem value="bug">Bug 反馈</SelectItem>
              <SelectItem value="ux">体验建议</SelectItem>
              <SelectItem value="feature">功能需求</SelectItem>
              <SelectItem value="quality">内容质量</SelectItem>
              <SelectItem value="performance">性能问题</SelectItem>
              <SelectItem value="pricing">价格与套餐</SelectItem>
              <SelectItem value="general">其他</SelectItem>
              <SelectItem value="qa_feedback">问答反馈</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">严重度：</span>
          <Select
            value={severityFilter}
            onValueChange={(v) => setSeverityFilter(v as string)}
          >
            <SelectTrigger className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部</SelectItem>
              <SelectItem value="critical">严重</SelectItem>
              <SelectItem value="high">高</SelectItem>
              <SelectItem value="medium">中</SelectItem>
              <SelectItem value="low">低</SelectItem>
              <SelectItem value="normal">一般</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">状态：</span>
          <Select
            value={statusFilter}
            onValueChange={(v) => setStatusFilter(v as string)}
          >
            <SelectTrigger className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部</SelectItem>
              <SelectItem value="open">待处理</SelectItem>
              <SelectItem value="in_progress">处理中</SelectItem>
              <SelectItem value="resolved">已解决</SelectItem>
              <SelectItem value="closed">已关闭</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      {/* 反馈列表 */}
      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : feedbacks.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <MessagesSquare className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无反馈</p>
            <p className="mt-1 text-sm text-muted-foreground">
              当用户提交反馈后，将在此处显示
            </p>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-8" />
                <TableHead>标题</TableHead>
                <TableHead>类型</TableHead>
                <TableHead>严重度</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>优先级</TableHead>
                <TableHead>模块</TableHead>
                <TableHead>提交时间</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {feedbacks.map((fb) => {
                const expanded = expandedId === fb.id;
                return (
                  <Fragment key={fb.id}>
                    <TableRow
                      className="cursor-pointer hover:bg-muted/50"
                      onClick={() => toggleExpand(fb.id)}
                    >
                      <TableCell className="w-8">
                        {expanded ? (
                          <ChevronDown className="size-4 text-muted-foreground" />
                        ) : (
                          <ChevronRight className="size-4 text-muted-foreground" />
                        )}
                      </TableCell>
                      <TableCell className="font-medium">
                        {fb.title}
                      </TableCell>
                      <TableCell>
                        <Badge variant="secondary">
                          {feedbackTypeLabels[fb.feedbackType] ?? fb.feedbackType}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <Badge className={severityClassNames[fb.severity] ?? "bg-gray-100 text-gray-600"}>
                          {severityLabels[fb.severity] ?? fb.severity}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <Badge className={statusClassNames[fb.status] ?? "bg-gray-100 text-gray-600"}>
                          {statusLabels[fb.status] ?? fb.status}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <Badge variant="outline">
                          {priorityLabels[fb.priority] ?? fb.priority}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {fb.module || "-"}
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {formatDate(fb.createdAt)}
                      </TableCell>
                    </TableRow>
                    {expanded && (
                      <TableRow key={`${fb.id}-detail`}>
                        <TableCell colSpan={8} className="bg-muted/30">
                          <div className="space-y-4 py-4">
                            {/* 反馈内容 */}
                            {fb.content && (
                              <div>
                                <h4 className="mb-2 text-sm font-medium">反馈内容</h4>
                                <div className="rounded-lg border bg-background p-3">
                                  <Markdown content={fb.content} />
                                </div>
                              </div>
                            )}

                            {/* 内联更新 */}
                            <div className="flex flex-wrap items-end gap-4">
                              <div className="space-y-1.5">
                                <label className="text-xs font-medium text-muted-foreground">
                                  更新状态
                                </label>
                                <Select
                                  value={fb.status}
                                  onValueChange={(v) => {
                                    updateMutation.mutate({
                                      id: fb.id,
                                      data: { status: v as string },
                                    });
                                  }}
                                >
                                  <SelectTrigger className="w-36">
                                    <SelectValue />
                                  </SelectTrigger>
                                  <SelectContent>
                                    <SelectItem value="open">待处理</SelectItem>
                                    <SelectItem value="in_progress">处理中</SelectItem>
                                    <SelectItem value="resolved">已解决</SelectItem>
                                    <SelectItem value="closed">已关闭</SelectItem>
                                  </SelectContent>
                                </Select>
                              </div>

                              <div className="space-y-1.5">
                                <label className="text-xs font-medium text-muted-foreground">
                                  更新优先级
                                </label>
                                <Select
                                  value={fb.priority}
                                  onValueChange={(v) => {
                                    updateMutation.mutate({
                                      id: fb.id,
                                      data: { priority: v as string },
                                    });
                                  }}
                                >
                                  <SelectTrigger className="w-36">
                                    <SelectValue />
                                  </SelectTrigger>
                                  <SelectContent>
                                    <SelectItem value="urgent">紧急</SelectItem>
                                    <SelectItem value="high">高</SelectItem>
                                    <SelectItem value="medium">中</SelectItem>
                                    <SelectItem value="low">低</SelectItem>
                                    <SelectItem value="normal">一般</SelectItem>
                                  </SelectContent>
                                </Select>
                              </div>

                              {updateMutation.isPending && (
                                <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                                  <Loader2 className="size-4 animate-spin" />
                                  更新中...
                                </div>
                              )}
                            </div>

                            {/* 元数据 */}
                            <div className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-muted-foreground">
                              <span>反馈 ID: {fb.id}</span>
                              <span>用户 ID: {fb.userId}</span>
                              {fb.betaUserId && (
                                <span>内测用户 ID: {fb.betaUserId}</span>
                              )}
                              {fb.relatedEntityType && (
                                <span>关联类型: {fb.relatedEntityType}</span>
                              )}
                              <span>更新时间: {formatDate(fb.updatedAt)}</span>
                            </div>
                          </div>
                        </TableCell>
                      </TableRow>
                    )}
                  </Fragment>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}
