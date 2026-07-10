"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import { AlertTriangle, Bell, CheckCircle2, Clock, Loader2, RefreshCw } from "lucide-react";
import { ApiRequestError, pushNotificationsApi } from "@/lib/api";
import type { PushNotification } from "@/lib/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";

type StatusFilter = "all" | "pending" | "sent" | "failed";

const filters: { value: StatusFilter; label: string }[] = [
  { value: "all", label: "全部" },
  { value: "pending", label: "待发送" },
  { value: "sent", label: "已发送" },
  { value: "failed", label: "失败" },
];

function formatDate(dateStr?: string) {
  if (!dateStr) return "-";
  return new Date(dateStr).toLocaleString("zh-CN");
}

function statusLabel(status: string) {
  if (status === "pending") return "待发送";
  if (status === "sent") return "已发送";
  if (status === "failed") return "失败";
  return status;
}

export default function PushNotificationsPage() {
  const [status, setStatus] = useState<StatusFilter>("all");
  const [items, setItems] = useState<PushNotification[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const load = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await pushNotificationsApi.list({
        status: status === "all" ? undefined : status,
        limit: 100,
      });
      setItems(data);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载推送审计失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  }, [status]);

  useEffect(() => {
    load();
  }, [load]);

  const stats = useMemo(
    () => ({
      pending: items.filter((i) => i.status === "pending").length,
      sent: items.filter((i) => i.status === "sent").length,
      failed: items.filter((i) => i.status === "failed").length,
    }),
    [items]
  );

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">推送审计</h2>
          <p className="text-sm text-muted-foreground">
            查看手机推送队列、发送回执和失败重试状态
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={load}>
          <RefreshCw className="mr-2 size-4" />
          刷新
        </Button>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <Card>
          <CardContent className="flex items-center justify-between p-4">
            <div>
              <p className="text-xs text-muted-foreground">待发送</p>
              <p className="mt-1 text-2xl font-semibold">{stats.pending}</p>
            </div>
            <Clock className="size-5 text-amber-600" />
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center justify-between p-4">
            <div>
              <p className="text-xs text-muted-foreground">已发送</p>
              <p className="mt-1 text-2xl font-semibold">{stats.sent}</p>
            </div>
            <CheckCircle2 className="size-5 text-green-600" />
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center justify-between p-4">
            <div>
              <p className="text-xs text-muted-foreground">失败</p>
              <p className="mt-1 text-2xl font-semibold">{stats.failed}</p>
            </div>
            <AlertTriangle className="size-5 text-red-600" />
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <CardTitle className="text-base">通知记录</CardTitle>
              <CardDescription>最近 100 条推送通知</CardDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              {filters.map((item) => (
                <Button
                  key={item.value}
                  variant={status === item.value ? "default" : "outline"}
                  size="sm"
                  onClick={() => setStatus(item.value)}
                >
                  {item.label}
                </Button>
              ))}
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {items.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <Bell className="mb-4 size-12 text-muted-foreground/50" />
              <p className="text-sm font-medium">暂无推送记录</p>
              <p className="mt-1 text-sm text-muted-foreground">
                手机采集导入、OCR 或转写事件触发后会显示在这里
              </p>
            </div>
          ) : (
            <div className="divide-y">
              {items.map((item) => (
                <div key={item.id} className="space-y-2 py-4">
                  <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="truncate text-sm font-medium">{item.title}</p>
                        <Badge
                          variant="secondary"
                          className={cn(
                            item.status === "sent" && "bg-green-100 text-green-700",
                            item.status === "failed" && "bg-red-100 text-red-700",
                            item.status === "pending" && "bg-amber-100 text-amber-700"
                          )}
                        >
                          {statusLabel(item.status)}
                        </Badge>
                        <Badge variant="outline">
                          {item.attempt}/{item.maxAttempts}
                        </Badge>
                      </div>
                      <p className="mt-1 text-sm text-muted-foreground">{item.body}</p>
                    </div>
                    <p className="shrink-0 text-xs text-muted-foreground">
                      {formatDate(item.createdAt)}
                    </p>
                  </div>
                  <div className="grid gap-1 text-xs text-muted-foreground sm:grid-cols-2 lg:grid-cols-4">
                    <span className="truncate">设备：{item.clientId}</span>
                    <span>发送：{formatDate(item.sentAt)}</span>
                    <span>下次重试：{formatDate(item.nextAttemptAt)}</span>
                    <span>更新：{formatDate(item.updatedAt)}</span>
                  </div>
                  {item.errorMessage && (
                    <p className="rounded-md bg-red-50 px-3 py-2 text-xs text-red-700">
                      {item.errorMessage}
                    </p>
                  )}
                  {item.providerResponse && (
                    <pre className="max-h-28 overflow-auto rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">
                      {item.providerResponse}
                    </pre>
                  )}
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
