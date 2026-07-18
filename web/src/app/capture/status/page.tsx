"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import {
  AlertCircle,
  ArrowLeft,
  CheckCircle2,
  Clock,
  Inbox,
  Loader2,
  RefreshCw,
  Smartphone,
} from "lucide-react";

import { ApiRequestError, mobileCaptureApi } from "@/lib/api";
import type { InboxItem } from "@/lib/types";
import { getMobileCaptureClientId } from "@/lib/mobile-capture-client";
import { InboxProcessingHint, InboxTypeBadge } from "@/components/inbox-type-display";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { cn } from "@/lib/utils";

function formatDate(dateStr?: string) {
  if (!dateStr) return "-";
  return new Date(dateStr).toLocaleString("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function statusLabel(status: string) {
  switch (status) {
    case "pending":
      return "待处理";
    case "processing":
      return "处理中";
    case "imported":
    case "done":
      return "已导入";
    case "failed":
      return "失败";
    case "archived":
      return "已归档";
    default:
      return status || "未知";
  }
}

function statusClass(status: string) {
  switch (status) {
    case "imported":
    case "done":
      return "bg-emerald-500/15 text-emerald-300";
    case "processing":
      return "bg-blue-500/15 text-blue-300";
    case "failed":
      return "bg-red-500/15 text-red-300";
    case "archived":
      return "bg-slate-700 text-slate-300";
    default:
      return "bg-amber-500/15 text-amber-300";
  }
}

function statusIcon(status: string) {
  if (status === "imported" || status === "done") return CheckCircle2;
  if (status === "failed") return AlertCircle;
  return Clock;
}

function itemTitle(item: InboxItem) {
  return (
    item.title ||
    item.sourceUrl ||
    item.contentText ||
    item.fileName ||
    "未命名资料"
  );
}

export default function CaptureStatusPage() {
  const [clientId, setClientId] = useState<string | undefined>();
  const [items, setItems] = useState<InboxItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadStatus = useCallback(async (id: string, quiet = false) => {
    if (quiet) {
      setRefreshing(true);
    } else {
      setLoading(true);
    }
    setError(null);
    try {
      const result = await mobileCaptureApi.listStatus(id, 50);
      setItems(result);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载采集状态失败";
      setError(message);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    const id = getMobileCaptureClientId();
    setClientId(id);
    if (id) {
      loadStatus(id);
    } else {
      setLoading(false);
      setError("无法读取当前设备 ID");
    }
  }, [loadStatus]);

  const summary = useMemo(() => {
    return {
      total: items.length,
      pending: items.filter((item) => item.status === "pending").length,
      imported: items.filter((item) => item.status === "imported" || item.status === "done").length,
      failed: items.filter((item) => item.status === "failed").length,
    };
  }, [items]);

  return (
    <main className="min-h-screen bg-slate-950 text-white">
      <div className="mx-auto flex min-h-screen w-full max-w-md flex-col px-4 py-5">
        <header className="mb-5 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="flex size-10 items-center justify-center rounded-xl bg-blue-500/15 text-blue-300">
              <Smartphone className="size-5" />
            </div>
            <div>
              <h1 className="text-lg font-semibold">采集状态</h1>
              <p className="text-xs text-slate-400">
                查看当前手机送入 Inbox 的资料进度
              </p>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="border-slate-700 bg-slate-900 text-slate-200 hover:bg-slate-800"
            render={<Link href="/capture" />}
          >
            <ArrowLeft className="mr-1.5 size-3.5" />
            返回
          </Button>
        </header>

        <section className="mb-4 grid grid-cols-4 gap-2">
          {[
            ["全部", summary.total],
            ["待处理", summary.pending],
            ["已导入", summary.imported],
            ["失败", summary.failed],
          ].map(([label, value]) => (
            <div key={label} className="rounded-xl border border-slate-800 bg-slate-900 px-2 py-3 text-center">
              <p className="text-lg font-semibold">{value}</p>
              <p className="mt-0.5 text-xs text-slate-500">{label}</p>
            </div>
          ))}
        </section>

        <Card className="flex-1 border-slate-800 bg-slate-900 text-slate-100">
          <CardHeader>
            <div className="flex items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Inbox className="size-4 text-blue-300" />
                  最近采集
                </CardTitle>
                <CardDescription className="text-slate-400">
                  {clientId ? "按当前设备追踪，刷新页面也会保留" : "正在读取设备标识"}
                </CardDescription>
              </div>
              <Button
                variant="outline"
                size="sm"
                className="border-slate-700 bg-slate-950 text-slate-200 hover:bg-slate-800"
                disabled={!clientId || refreshing}
                onClick={() => clientId && loadStatus(clientId, true)}
              >
                {refreshing ? (
                  <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                ) : (
                  <RefreshCw className="mr-1.5 size-3.5" />
                )}
                刷新
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            {loading ? (
              <div className="flex items-center justify-center gap-2 py-12 text-sm text-slate-400">
                <Loader2 className="size-4 animate-spin" />
                正在读取状态...
              </div>
            ) : error ? (
              <div className="rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-4 text-sm text-red-200">
                {error}
              </div>
            ) : items.length === 0 ? (
              <div className="rounded-xl border border-dashed border-slate-800 px-4 py-10 text-center">
                <Inbox className="mx-auto size-8 text-slate-600" />
                <p className="mt-3 text-sm text-slate-300">还没有采集记录</p>
                <p className="mt-1 text-xs text-slate-500">
                  从手机发送文本、链接或文件后会显示在这里
                </p>
                <Button className="mt-4" render={<Link href="/capture" />}>
                  去采集
                </Button>
              </div>
            ) : (
              <div className="space-y-3">
                {items.map((item) => {
                  const StatusIcon = statusIcon(item.status);
                  return (
                    <article
                      key={item.id}
                      className="rounded-xl border border-slate-800 bg-slate-950/70 px-3 py-3"
                    >
                      <div className="flex items-start gap-3">
                        <div
                          className={cn(
                            "mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg",
                            item.status === "failed"
                              ? "bg-red-500/15 text-red-300"
                              : item.status === "imported" || item.status === "done"
                                ? "bg-emerald-500/15 text-emerald-300"
                                : "bg-amber-500/15 text-amber-300"
                          )}
                        >
                          <StatusIcon className="size-4" />
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <InboxTypeBadge
                              inputType={item.inputType}
                              itemType={item.itemType}
                              fileName={item.fileName || item.title}
                            />
                            <Badge className={statusClass(item.status)}>
                              {statusLabel(item.status)}
                            </Badge>
                          </div>
                          <p className="mt-2 line-clamp-2 text-sm font-medium text-slate-100">
                            {itemTitle(item)}
                          </p>
                          <p className="mt-1 text-xs text-slate-500">
                            {formatDate(item.createdAt)}
                          </p>
                          <InboxProcessingHint
                            inputType={item.inputType}
                            itemType={item.itemType}
                            fileName={item.fileName || item.title}
                            status={item.status}
                            compact
                            className="mt-2 text-slate-400"
                          />
                          {item.status === "failed" && item.errorMessage && (
                            <p className="mt-2 rounded-lg bg-red-500/10 px-2 py-1 text-xs text-red-200">
                              {item.errorMessage}
                            </p>
                          )}
                          <div className="mt-3 flex flex-wrap gap-2">
                            <Button
                              variant="outline"
                              size="sm"
                              className="h-8 border-slate-700 bg-slate-900 text-xs text-slate-200 hover:bg-slate-800"
                              render={<Link href={`/settings/inbox/${item.id}`} />}
                            >
                              桌面端处理
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              className="h-8 text-xs text-slate-400 hover:bg-slate-900 hover:text-slate-100"
                              render={<Link href="/settings/inbox" />}
                            >
                              打开 Inbox
                            </Button>
                          </div>
                        </div>
                      </div>
                    </article>
                  );
                })}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </main>
  );
}
