"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Activity, AlertCircle, CheckCircle2, Loader2, XCircle } from "lucide-react";
import { aiJobApi } from "@/lib/api";
import type { AiJobListItem } from "@/lib/types";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
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

function formatDate(value: string) {
  return new Date(value).toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

function statusBadge(status: string) {
  const variant =
    status === "completed" || status === "done" ? "default" :
    status === "failed" ? "destructive" :
    status === "running" ? "default" :
    "secondary";
  return <Badge variant={variant as "default" | "destructive" | "secondary"}>{status}</Badge>;
}

const STATUS_OPTIONS = [
  { value: "__all__", label: "全部状态" },
  { value: "pending", label: "待处理" },
  { value: "queued", label: "排队中" },
  { value: "running", label: "运行中" },
  { value: "completed", label: "已完成" },
  { value: "done", label: "已完成 (done)" },
  { value: "failed", label: "失败" },
];

export default function TaskMonitorPage() {
  const [statusFilter, setStatusFilter] = useState("__all__");

  const jobs = useQuery({
    queryKey: ["ai-jobs", statusFilter],
    queryFn: () =>
      aiJobApi.list(statusFilter === "__all__" ? undefined : { status: statusFilter }),
    refetchInterval: 5000,
  });

  const allJobs = jobs.data?.items ?? [];

  const summary = {
    total: allJobs.length,
    running: allJobs.filter((j) => j.status === "running").length,
    completed: allJobs.filter((j) => ["completed", "done"].includes(j.status)).length,
    failed: allJobs.filter((j) => j.status === "failed").length,
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">任务监控</h2>
          <p className="text-sm text-muted-foreground">AI 任务实时监控，每 5 秒自动刷新</p>
        </div>
        <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v ?? "__all__")}>
          <SelectTrigger className="w-40">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {STATUS_OPTIONS.map((opt) => (
              <SelectItem key={opt.value} value={opt.value}>
                {opt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {/* 汇总卡片 */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardContent className="flex items-center gap-3 p-4">
            <div className="rounded-lg bg-primary/10 p-2 text-primary">
              <Activity className="size-5" />
            </div>
            <div>
              <p className="text-2xl font-bold">{summary.total}</p>
              <p className="text-xs text-muted-foreground">总计</p>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center gap-3 p-4">
            <div className="rounded-lg bg-blue-500/10 p-2 text-blue-500">
              <Loader2 className="size-5" />
            </div>
            <div>
              <p className="text-2xl font-bold">{summary.running}</p>
              <p className="text-xs text-muted-foreground">运行中</p>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center gap-3 p-4">
            <div className="rounded-lg bg-green-500/10 p-2 text-green-500">
              <CheckCircle2 className="size-5" />
            </div>
            <div>
              <p className="text-2xl font-bold">{summary.completed}</p>
              <p className="text-xs text-muted-foreground">已完成</p>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center gap-3 p-4">
            <div className="rounded-lg bg-red-500/10 p-2 text-red-500">
              <XCircle className="size-5" />
            </div>
            <div>
              <p className="text-2xl font-bold">{summary.failed}</p>
              <p className="text-xs text-muted-foreground">失败</p>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* 任务列表 */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">AI 任务列表</CardTitle>
          <CardDescription>实时状态（每 5 秒刷新）</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          {jobs.isLoading ? (
            <div className="flex justify-center py-12">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : allJobs.length === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground">
              <AlertCircle className="mx-auto mb-3 size-8 opacity-40" />
              暂无任务
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>类型</TableHead>
                  <TableHead>目标</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>模型</TableHead>
                  <TableHead>Tokens</TableHead>
                  <TableHead>创建时间</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {allJobs.map((job: AiJobListItem) => (
                  <TableRow key={job.id}>
                    <TableCell className="font-medium">{job.jobType}</TableCell>
                    <TableCell>
                      <div className="text-sm">{job.targetType}</div>
                      <div className="text-xs text-muted-foreground truncate max-w-32">{job.targetId}</div>
                    </TableCell>
                    <TableCell>{statusBadge(job.status)}</TableCell>
                    <TableCell className="text-sm">{job.model ?? "-"}</TableCell>
                    <TableCell className="text-sm">
                      {(job.inputTokens ?? 0) + (job.outputTokens ?? 0) > 0
                        ? `${job.inputTokens ?? 0} / ${job.outputTokens ?? 0}`
                        : "-"}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {formatDate(job.createdAt)}
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
