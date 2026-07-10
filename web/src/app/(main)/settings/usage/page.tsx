"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import {
  FileText,
  Search,
  MessageCircle,
  ClipboardList,
  Download,
  Code2,
  Loader2,
  TrendingUp,
  ArrowDownToLine,
  ArrowUpFromLine,
  Database,
  Bot,
} from "lucide-react";
import { usageApi, ApiRequestError } from "@/lib/api";
import type { UsageResponse } from "@/lib/types";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

function formatDate(dateStr: string): string {
  const d = new Date(dateStr);
  return `${d.getMonth() + 1}/${d.getDate()}`;
}

export default function UsagePage() {
  const [usage, setUsage] = useState<UsageResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const fetchUsage = async () => {
      setIsLoading(true);
      try {
        const data = await usageApi.get();
        setUsage(data);
      } catch (err) {
        const message =
          err instanceof ApiRequestError ? err.message : "加载使用量数据失败";
        toast.error(message);
      } finally {
        setIsLoading(false);
      }
    };
    fetchUsage();
  }, []);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!usage) {
    return (
      <Card>
        <CardContent className="flex flex-col items-center justify-center py-16 text-center">
          <TrendingUp className="mb-4 size-12 text-muted-foreground/50" />
          <p className="text-lg font-medium">暂无使用量数据</p>
        </CardContent>
      </Card>
    );
  }

  const todayCards = [
    {
      label: "文档数",
      value: usage.today.documentCount,
      icon: FileText,
      color: "text-blue-600",
      bg: "bg-blue-50",
    },
    {
      label: "搜索次数",
      value: usage.today.searchCount,
      icon: Search,
      color: "text-green-600",
      bg: "bg-green-50",
    },
    {
      label: "问答次数",
      value: usage.today.qaCount,
      icon: MessageCircle,
      color: "text-purple-600",
      bg: "bg-purple-50",
    },
    {
      label: "报告数",
      value: usage.today.reportCount,
      icon: ClipboardList,
      color: "text-orange-600",
      bg: "bg-orange-50",
    },
    {
      label: "导出数",
      value: usage.today.exportCount,
      icon: Download,
      color: "text-cyan-600",
      bg: "bg-cyan-50",
    },
    {
      label: "API 调用数",
      value: usage.today.apiCallCount,
      icon: Code2,
      color: "text-pink-600",
      bg: "bg-pink-50",
    },
    {
      label: "Agent 调用数",
      value: usage.today.agentCallCount,
      icon: Bot,
      color: "text-rose-600",
      bg: "bg-rose-50",
    },
  ];

  const totalCards = [
    {
      label: "总文档数",
      value: usage.totals.documentCount,
      icon: FileText,
    },
    {
      label: "总搜索次数",
      value: usage.totals.searchCount,
      icon: Search,
    },
    {
      label: "总问答次数",
      value: usage.totals.qaCount,
      icon: MessageCircle,
    },
    {
      label: "总报告数",
      value: usage.totals.reportCount,
      icon: ClipboardList,
    },
    {
      label: "总 API 调用数",
      value: usage.totals.apiCallCount,
      icon: Code2,
    },
    {
      label: "总 Agent 调用数",
      value: usage.totals.agentCallCount,
      icon: Bot,
    },
  ];

  // 计算趋势图最大值用于柱状图高度
  const trendMax = Math.max(
    ...usage.last7Days.map(
      (d) =>
        d.searchCount +
        d.qaCount +
        d.reportCount +
        d.apiCallCount +
        d.agentCallCount
    ),
    1
  );

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-lg font-semibold">使用量统计</h2>
        <p className="text-sm text-muted-foreground">
          查看您的资源使用情况
        </p>
      </div>

      {/* 今日统计 */}
      <div>
        <h3 className="mb-3 text-sm font-medium text-muted-foreground">
          今日统计
        </h3>
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
          {todayCards.map((card) => {
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
      </div>

      {/* Token 使用量 */}
      <div>
        <h3 className="mb-3 text-sm font-medium text-muted-foreground">
          Token 使用量（今日）
        </h3>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Card>
            <CardContent className="flex items-center gap-4 py-4">
              <div className="flex size-12 items-center justify-center rounded-lg bg-indigo-50">
                <ArrowDownToLine className="size-6 text-indigo-600" />
              </div>
              <div>
                <p className="text-sm text-muted-foreground">输入 Token</p>
                <p className="text-2xl font-bold">
                  {usage.today.inputTokens.toLocaleString()}
                </p>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent className="flex items-center gap-4 py-4">
              <div className="flex size-12 items-center justify-center rounded-lg bg-emerald-50">
                <ArrowUpFromLine className="size-6 text-emerald-600" />
              </div>
              <div>
                <p className="text-sm text-muted-foreground">输出 Token</p>
                <p className="text-2xl font-bold">
                  {usage.today.outputTokens.toLocaleString()}
                </p>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>

      {/* 最近 7 天趋势 */}
      <div>
        <h3 className="mb-3 text-sm font-medium text-muted-foreground">
          最近 7 天趋势
        </h3>
        <Card>
          <CardHeader>
            <CardTitle className="text-base">每日使用趋势</CardTitle>
            <CardDescription>搜索、问答、报告、API 调用、Agent 调用次数</CardDescription>
          </CardHeader>
          <CardContent>
            {/* 柱状图 */}
            <div className="mb-6 flex h-48 items-end justify-between gap-2">
              {usage.last7Days.map((day) => {
                const total =
                  day.searchCount +
                  day.qaCount +
                  day.reportCount +
                  day.apiCallCount +
                  day.agentCallCount;
                const heightPercent = (total / trendMax) * 100;
                return (
                  <div
                    key={day.usageDate}
                    className="flex flex-1 flex-col items-center gap-1"
                  >
                    <span className="text-xs font-medium text-muted-foreground">
                      {total}
                    </span>
                    <div className="flex w-full flex-1 items-end">
                      <div
                        className="flex w-full flex-col-reverse overflow-hidden rounded-t"
                        style={{ height: `${Math.max(heightPercent, 2)}%` }}
                      >
                        {day.agentCallCount > 0 && (
                          <div
                            className="bg-rose-400"
                            style={{
                              flexGrow: day.agentCallCount,
                            }}
                          />
                        )}
                        {day.apiCallCount > 0 && (
                          <div
                            className="bg-pink-400"
                            style={{
                              flexGrow: day.apiCallCount,
                            }}
                          />
                        )}
                        {day.reportCount > 0 && (
                          <div
                            className="bg-orange-400"
                            style={{
                              flexGrow: day.reportCount,
                            }}
                          />
                        )}
                        {day.qaCount > 0 && (
                          <div
                            className="bg-purple-400"
                            style={{
                              flexGrow: day.qaCount,
                            }}
                          />
                        )}
                        {day.searchCount > 0 && (
                          <div
                            className="bg-green-400"
                            style={{
                              flexGrow: day.searchCount,
                            }}
                          />
                        )}
                      </div>
                    </div>
                    <span className="text-xs text-muted-foreground">
                      {formatDate(day.usageDate)}
                    </span>
                  </div>
                );
              })}
            </div>

            {/* 图例 */}
            <div className="mb-4 flex flex-wrap gap-4 text-xs">
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded bg-green-400" />
                搜索
              </span>
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded bg-purple-400" />
                问答
              </span>
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded bg-orange-400" />
                报告
              </span>
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded bg-pink-400" />
                API 调用
              </span>
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded bg-rose-400" />
                Agent 调用
              </span>
            </div>

            {/* 趋势表格 */}
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>日期</TableHead>
                  <TableHead>搜索</TableHead>
                  <TableHead>问答</TableHead>
                  <TableHead>报告</TableHead>
                  <TableHead>API 调用</TableHead>
                  <TableHead>Agent 调用</TableHead>
                  <TableHead>合计</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {usage.last7Days.map((day) => (
                  <TableRow key={day.usageDate}>
                    <TableCell className="font-medium">
                      {day.usageDate}
                    </TableCell>
                    <TableCell>{day.searchCount}</TableCell>
                    <TableCell>{day.qaCount}</TableCell>
                    <TableCell>{day.reportCount}</TableCell>
                    <TableCell>{day.apiCallCount}</TableCell>
                    <TableCell>{day.agentCallCount}</TableCell>
                    <TableCell className="font-medium">
                      {day.searchCount +
                        day.qaCount +
                        day.reportCount +
                        day.apiCallCount +
                        day.agentCallCount}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>

      {/* 总计统计 */}
      <div>
        <h3 className="mb-3 text-sm font-medium text-muted-foreground">
          总计统计
        </h3>
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
          {totalCards.map((card) => {
            const Icon = card.icon;
            return (
              <Card key={card.label}>
                <CardContent className="flex flex-col items-center gap-2 py-4 text-center">
                  <div className="flex size-10 items-center justify-center rounded-lg bg-slate-100">
                    <Icon className="size-5 text-slate-600" />
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
      </div>

      {/* 导入统计补充 */}
      <Card>
        <CardContent className="flex items-center gap-4 py-4">
          <div className="flex size-12 items-center justify-center rounded-lg bg-amber-50">
            <Database className="size-6 text-amber-600" />
          </div>
          <div>
            <p className="text-sm text-muted-foreground">今日导入资料数</p>
            <p className="text-2xl font-bold">{usage.today.importedCount}</p>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
