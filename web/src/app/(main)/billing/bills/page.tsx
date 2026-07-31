"use client";

import { useEffect, useMemo, useState } from "react";
import { Download } from "lucide-react";
import { billingApi, workspaceApi } from "@/lib/api";
import type { BillingBillsResponse } from "@/lib/types";
import {
  BillingEmpty,
  BillingLoading,
  BillingPageHeader,
  BillingPanel,
  formatCredits,
  formatDateTime,
  formatMinorAmount,
} from "@/components/billing/billing-ui";
import { cn } from "@/lib/utils";

type BillFilter = "ALL" | "CHARGE" | "RECHARGE";

export default function BillsPage() {
  const [data, setData] = useState<BillingBillsResponse | null>(null);
  const [filter, setFilter] = useState<BillFilter>("ALL");
  const [error, setError] = useState("");

  useEffect(() => {
    let disposed = false;
    void (async () => {
      try {
        const workspace = await workspaceApi.getCurrent();
        if (!workspace) throw new Error("请先选择工作区");
        const to = new Date();
        const from = new Date(to);
        from.setDate(from.getDate() - 89);
        from.setHours(0, 0, 0, 0);
        const response = await billingApi.bills(workspace.id, from.toISOString(), to.toISOString());
        if (!disposed) setData(response);
      } catch (reason) {
        if (!disposed) {
          setError(reason instanceof Error ? reason.message : "账单加载失败");
        }
      }
    })();
    return () => {
      disposed = true;
    };
  }, []);

  const items = useMemo(
    () => data?.items.filter((item) => filter === "ALL" || item.type === filter) ?? [],
    [data, filter]
  );

  const exportCsv = () => {
    if (!data) return;
    const rows = [
      ["时间", "类型", "说明", "业务单号", "算力点", "金额", "币种", "状态"],
      ...items.map((item) => [
        item.occurredAt,
        item.type,
        item.title,
        item.reference,
        item.credits,
        item.amountMinor == null ? "" : item.amountMinor / 100,
        item.currency,
        item.status,
      ]),
    ];
    const csv = rows
      .map((row) => row.map((cell) => `"${String(cell).replaceAll('"', '""')}"`).join(","))
      .join("\n");
    const url = URL.createObjectURL(new Blob([`\uFEFF${csv}`], { type: "text/csv;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `memorix-bills-${new Date().toISOString().slice(0, 10)}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-7 pb-10">
      <BillingPageHeader
        title="账单"
        description="查看最近 90 天的云算力消费与充值流水。"
        action={
          <button
            type="button"
            disabled={!data}
            onClick={exportCsv}
            className="inline-flex h-10 items-center gap-2 rounded-full bg-foreground px-5 text-sm font-medium text-background disabled:opacity-35"
          >
            <Download className="size-4" />
            导出
          </button>
        }
      />

      {error ? (
        <BillingPanel>
          <p className="font-medium text-destructive">无法加载账单</p>
          <p className="mt-2 text-sm text-muted-foreground">{error}</p>
        </BillingPanel>
      ) : !data ? (
        <BillingLoading />
      ) : (
        <>
          <div className="flex flex-wrap gap-2">
            {(
              [
                ["ALL", "全部"],
                ["CHARGE", "消费"],
                ["RECHARGE", "充值"],
              ] as const
            ).map(([value, label]) => (
              <button
                key={value}
                type="button"
                onClick={() => setFilter(value)}
                className={cn(
                  "h-10 rounded-full px-4 text-sm font-medium",
                  filter === value ? "bg-foreground text-background" : "bg-muted hover:bg-muted/75"
                )}
              >
                {label}
              </button>
            ))}
          </div>

          <BillingPanel className="overflow-hidden p-0">
            <div className="flex items-center justify-between px-5 py-5 sm:px-7">
              <div>
                <h2 className="font-semibold">财务流水</h2>
                <p className="mt-1 text-xs text-muted-foreground">
                  共 {items.length} 条 · 服务端财务真值
                </p>
              </div>
            </div>
            {items.length === 0 ? (
              <BillingEmpty title="暂无账单" description="该筛选条件下没有充值或消费流水。" />
            ) : (
              <div className="overflow-x-auto border-t">
                <table className="w-full min-w-[860px] text-left text-sm">
                  <thead className="bg-background/65 text-xs text-muted-foreground">
                    <tr>
                      <th className="px-5 py-3 font-medium sm:px-7">时间</th>
                      <th className="px-4 py-3 font-medium">类型</th>
                      <th className="px-4 py-3 font-medium">说明</th>
                      <th className="px-4 py-3 font-medium">业务单号</th>
                      <th className="px-4 py-3 text-right font-medium">算力点</th>
                      <th className="px-4 py-3 text-right font-medium">金额</th>
                      <th className="px-5 py-3 text-right font-medium sm:px-7">状态</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((item) => (
                      <tr key={`${item.type}-${item.id}`} className="border-t border-border/60">
                        <td className="px-5 py-4 whitespace-nowrap sm:px-7">
                          {formatDateTime(item.occurredAt)}
                        </td>
                        <td className="px-4 py-4">
                          <span
                            className={cn(
                              "rounded-full px-2.5 py-1 text-xs",
                              item.type === "RECHARGE"
                                ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300"
                                : "bg-sky-100 text-sky-700 dark:bg-sky-950 dark:text-sky-300"
                            )}
                          >
                            {item.type === "RECHARGE" ? "充值" : "消费"}
                          </span>
                        </td>
                        <td className="px-4 py-4">{item.title}</td>
                        <td className="max-w-56 truncate px-4 py-4 font-mono text-xs text-muted-foreground">
                          {item.reference}
                        </td>
                        <td
                          className={cn(
                            "px-4 py-4 text-right font-medium tabular-nums",
                            item.credits > 0 ? "text-emerald-600" : ""
                          )}
                        >
                          {item.credits > 0 ? "+" : ""}
                          {formatCredits(item.credits)}
                        </td>
                        <td className="px-4 py-4 text-right tabular-nums">
                          {item.amountMinor == null
                            ? "—"
                            : formatMinorAmount(item.amountMinor, item.currency)}
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
      )}
    </div>
  );
}
