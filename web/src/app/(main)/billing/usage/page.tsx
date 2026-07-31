"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Download, RefreshCw } from "lucide-react";
import { billingApi, workspaceApi } from "@/lib/api";
import type { BillingOverviewResponse, BillingUsageResponse } from "@/lib/types";
import {
  BillingEmpty,
  BillingLoading,
  BillingPageHeader,
  BillingPanel,
  MetricCard,
  UsageChart,
  formatAmount,
  formatCredits,
  formatDateTime,
} from "@/components/billing/billing-ui";

type RangeKey = "7d" | "30d" | "month";

function getRange(key: RangeKey) {
  const to = new Date();
  const from = new Date(to);
  if (key === "month") {
    from.setDate(1);
    from.setHours(0, 0, 0, 0);
  } else {
    from.setDate(from.getDate() - (key === "7d" ? 6 : 29));
    from.setHours(0, 0, 0, 0);
  }
  return { from: from.toISOString(), to: to.toISOString() };
}

export default function BillingUsagePage() {
  const [rangeKey, setRangeKey] = useState<RangeKey>("30d");
  const [workspaceId, setWorkspaceId] = useState("");
  const [overview, setOverview] = useState<BillingOverviewResponse | null>(null);
  const [usage, setUsage] = useState<BillingUsageResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const load = useCallback(async (id: string, nextRange: RangeKey) => {
    setLoading(true);
    setError("");
    try {
      const range = getRange(nextRange);
      const [overviewResponse, usageResponse] = await Promise.all([
        billingApi.overview(id),
        billingApi.usage(id, range.from, range.to),
      ]);
      setOverview(overviewResponse);
      setUsage(usageResponse);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "用量数据加载失败");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let disposed = false;
    void (async () => {
      try {
        const workspace = await workspaceApi.getCurrent();
        if (!workspace) throw new Error("请先选择工作区");
        if (disposed) return;
        setWorkspaceId(workspace.id);
        await load(workspace.id, rangeKey);
      } catch (reason) {
        if (!disposed) {
          setError(reason instanceof Error ? reason.message : "用量数据加载失败");
          setLoading(false);
        }
      }
    })();
    return () => {
      disposed = true;
    };
  }, [load, rangeKey]);

  const exportCsv = () => {
    if (!usage) return;
    const rows = [
      [
        "时间",
        "任务类型",
        "模型",
        "执行方式",
        "状态",
        "输入 Token",
        "输出 Token",
        "Token 合计",
        "算力点",
        "金额",
        "币种",
      ],
      ...usage.items.map((item) => [
        item.createdAt,
        item.jobType,
        item.model ?? "",
        item.executionMode,
        item.status,
        item.inputTokens,
        item.outputTokens,
        item.totalTokens,
        item.credits,
        item.amount,
        item.currency,
      ]),
    ];
    const content = rows
      .map((row) =>
        row
          .map((cell) => `"${String(cell).replaceAll('"', '""')}"`)
          .join(",")
      )
      .join("\n");
    const blob = new Blob([`\uFEFF${content}`], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `memorix-usage-${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  const displayedItems = useMemo(() => usage?.items.slice(0, 100) ?? [], [usage]);

  return (
    <div className="space-y-7 pb-10">
      <BillingPageHeader
        title="用量"
        description="所有时间按当前设备时区显示，财务用量最多可能延迟 5 分钟。"
      />

      {error ? (
        <BillingPanel>
          <p className="font-medium text-destructive">无法加载用量</p>
          <p className="mt-2 text-sm text-muted-foreground">{error}</p>
        </BillingPanel>
      ) : loading && !usage ? (
        <BillingLoading />
      ) : overview && usage ? (
        <>
          <div className="grid gap-4 lg:grid-cols-2">
            <BillingPanel>
              <p className="text-sm font-medium">可用算力点</p>
              <div className="mt-5 flex flex-wrap items-baseline gap-3">
                <span className="text-4xl font-semibold tracking-tight">
                  {formatCredits(overview.availableCredits)}
                </span>
                <span className="text-muted-foreground">Credits</span>
              </div>
              <div className="mt-4 flex flex-wrap gap-x-5 gap-y-2 text-xs text-muted-foreground">
                <span>充值 {formatCredits(overview.topUpAvailableCredits)}</span>
                <span>套餐 {formatCredits(overview.planAvailableCredits)}</span>
                <span>赠送 {formatCredits(overview.promotionAvailableCredits)}</span>
              </div>
            </BillingPanel>
            <BillingPanel>
              <p className="text-sm font-medium">累计已使用</p>
              <div className="mt-5 flex flex-wrap items-baseline gap-3">
                <span className="text-4xl font-semibold tracking-tight">
                  {formatCredits(overview.consumedCredits)}
                </span>
                <span className="text-muted-foreground">Credits</span>
              </div>
              <p className="mt-4 text-xs text-muted-foreground">
                本月约 {formatAmount(overview.monthAmount, overview.currency)}
              </p>
            </BillingPanel>
          </div>

          <div className="flex flex-col gap-3 border-t pt-6 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex flex-wrap gap-2">
              <label className="flex h-10 items-center gap-2 rounded-full bg-muted px-4 text-sm">
                <span className="text-muted-foreground">时间</span>
                <select
                  value={rangeKey}
                  onChange={(event) => setRangeKey(event.target.value as RangeKey)}
                  className="bg-transparent font-medium outline-none"
                >
                  <option value="7d">最近 7 天</option>
                  <option value="30d">最近 30 天</option>
                  <option value="month">本月</option>
                </select>
              </label>
              <button
                type="button"
                onClick={() => workspaceId && void load(workspaceId, rangeKey)}
                className="inline-flex h-10 items-center gap-2 rounded-full bg-muted px-4 text-sm font-medium hover:bg-muted/75"
              >
                <RefreshCw className={`size-4 ${loading ? "animate-spin" : ""}`} />
                刷新
              </button>
            </div>
            <button
              type="button"
              onClick={exportCsv}
              className="inline-flex h-10 items-center justify-center gap-2 rounded-full bg-foreground px-5 text-sm font-medium text-background hover:opacity-85"
            >
              <Download className="size-4" />
              导出
            </button>
          </div>

          <div className="grid gap-4 md:grid-cols-3">
            <MetricCard
              label="算力点消耗"
              value={formatCredits(usage.totalCredits)}
              detail={formatAmount(usage.totalAmount, usage.currency)}
            />
            <MetricCard label="API 请求" value={formatCredits(usage.totalRequests)} />
            <MetricCard label="Tokens" value={formatCredits(usage.totalTokens)} />
          </div>

          <BillingPanel>
            <div className="flex items-baseline justify-between gap-3">
              <h2 className="font-semibold">
                消耗趋势{" "}
                <span className="font-normal text-muted-foreground">
                  {formatCredits(usage.totalCredits)} Credits
                </span>
              </h2>
              <span className="text-xs text-muted-foreground">按天</span>
            </div>
            <div className="mt-4">
              <UsageChart points={usage.trend} metric="credits" />
            </div>
          </BillingPanel>

          <div className="grid gap-4 xl:grid-cols-2">
            <BillingPanel>
              <h2 className="font-semibold">
                API 请求{" "}
                <span className="font-normal text-muted-foreground">
                  {formatCredits(usage.totalRequests)}
                </span>
              </h2>
              <div className="mt-4">
                <UsageChart points={usage.trend} metric="requests" kind="area" />
              </div>
            </BillingPanel>
            <BillingPanel>
              <h2 className="font-semibold">
                Tokens{" "}
                <span className="font-normal text-muted-foreground">
                  {formatCredits(usage.totalTokens)}
                </span>
              </h2>
              <div className="mt-4">
                <UsageChart points={usage.trend} metric="tokens" color="#7dd3fc" />
              </div>
            </BillingPanel>
          </div>

          <BillingPanel className="overflow-hidden p-0">
            <div className="flex items-center justify-between px-5 py-5 sm:px-7">
              <div>
                <h2 className="font-semibold">用量明细</h2>
                <p className="mt-1 text-xs text-muted-foreground">当前最多展示 100 条记录</p>
              </div>
              <span className="rounded-full bg-background px-3 py-1 text-xs text-muted-foreground">
                财务真值
              </span>
            </div>
            {displayedItems.length === 0 ? (
              <BillingEmpty title="暂无用量" description="该时间范围内没有产生云算力消费。" />
            ) : (
              <div className="overflow-x-auto border-t">
                <table className="w-full min-w-[920px] text-left text-sm">
                  <thead className="bg-background/65 text-xs text-muted-foreground">
                    <tr>
                      <th className="px-5 py-3 font-medium sm:px-7">时间</th>
                      <th className="px-4 py-3 font-medium">任务</th>
                      <th className="px-4 py-3 font-medium">模型</th>
                      <th className="px-4 py-3 text-right font-medium">输入 Token</th>
                      <th className="px-4 py-3 text-right font-medium">输出 Token</th>
                      <th className="px-4 py-3 text-right font-medium">算力点</th>
                      <th className="px-5 py-3 text-right font-medium sm:px-7">状态</th>
                    </tr>
                  </thead>
                  <tbody>
                    {displayedItems.map((item) => (
                      <tr key={item.jobId} className="border-t border-border/60">
                        <td className="px-5 py-4 whitespace-nowrap sm:px-7">
                          {formatDateTime(item.createdAt)}
                        </td>
                        <td className="px-4 py-4">{item.jobType}</td>
                        <td className="max-w-48 truncate px-4 py-4 text-muted-foreground">
                          {item.model || "—"}
                        </td>
                        <td className="px-4 py-4 text-right tabular-nums">
                          {formatCredits(item.inputTokens)}
                        </td>
                        <td className="px-4 py-4 text-right tabular-nums">
                          {formatCredits(item.outputTokens)}
                        </td>
                        <td className="px-4 py-4 text-right font-medium tabular-nums">
                          {formatCredits(item.credits)}
                        </td>
                        <td className="px-5 py-4 text-right sm:px-7">
                          <span className="rounded-full bg-background px-2.5 py-1 text-xs">
                            {item.status}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </BillingPanel>
        </>
      ) : null}
    </div>
  );
}
