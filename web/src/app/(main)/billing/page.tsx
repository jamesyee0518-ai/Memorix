"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowRight, CreditCard, ReceiptText, Sparkles, WalletCards } from "lucide-react";
import { billingApi, workspaceApi } from "@/lib/api";
import type { BillingOverviewResponse } from "@/lib/types";
import {
  BillingLoading,
  BillingPageHeader,
  BillingPanel,
  MetricCard,
  formatAmount,
  formatCredits,
} from "@/components/billing/billing-ui";

export default function BillingOverviewPage() {
  const [overview, setOverview] = useState<BillingOverviewResponse | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let disposed = false;

    void (async () => {
      try {
        const workspace = await workspaceApi.getCurrent();
        if (!workspace) {
          throw new Error("请先选择工作区");
        }
        const response = await billingApi.overview(workspace.id);
        if (!disposed) setOverview(response);
      } catch (reason) {
        if (!disposed) {
          setError(reason instanceof Error ? reason.message : "计费概览加载失败");
        }
      }
    })();

    return () => {
      disposed = true;
    };
  }, []);

  return (
    <div className="space-y-7 pb-10">
      <BillingPageHeader
        title="计费中心"
        description="余额、云算力消费与财务账单以服务端数据为准。"
        action={
          <Link
            href="/billing/recharge"
            className="inline-flex h-10 items-center gap-2 rounded-full bg-foreground px-5 text-sm font-medium text-background transition-opacity hover:opacity-85"
          >
            <CreditCard className="size-4" />
            充值
          </Link>
        }
      />

      {error ? (
        <BillingPanel>
          <p className="font-medium text-destructive">无法加载计费数据</p>
          <p className="mt-2 text-sm text-muted-foreground">{error}</p>
        </BillingPanel>
      ) : !overview ? (
        <BillingLoading />
      ) : (
        <>
          <div className="grid gap-4 lg:grid-cols-2">
            <BillingPanel className="relative overflow-hidden bg-foreground text-background">
              <div className="absolute -right-12 -top-16 size-52 rounded-full bg-primary/30 blur-3xl" />
              <div className="relative">
                <div className="flex items-center gap-2 text-sm text-background/65">
                  <WalletCards className="size-4" />
                  可用算力点
                </div>
                <div className="mt-5 text-5xl font-semibold tracking-tight">
                  {formatCredits(overview.availableCredits)}
                </div>
                <p className="mt-3 text-sm text-background/60">
                  已预占 {formatCredits(overview.reservedCredits)} 点 · 数据更新于{" "}
                  {new Date(overview.asOf).toLocaleTimeString("zh-CN", {
                    hour: "2-digit",
                    minute: "2-digit",
                  })}
                </p>
              </div>
            </BillingPanel>

            <BillingPanel>
              <p className="text-sm font-medium text-muted-foreground">本月云算力消费</p>
              <div className="mt-5 flex flex-wrap items-baseline gap-x-4 gap-y-2">
                <span className="text-4xl font-semibold tracking-tight">
                  {formatCredits(overview.monthCredits)}
                </span>
                <span className="text-lg text-muted-foreground">算力点</span>
              </div>
              <p className="mt-3 text-sm text-muted-foreground">
                约 {formatAmount(overview.monthAmount, overview.currency)} ·{" "}
                {formatCredits(overview.monthRequests)} 次请求
              </p>
            </BillingPanel>
          </div>

          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <MetricCard
              label="套餐额度"
              value={formatCredits(overview.planAvailableCredits)}
              detail="按周期发放的剩余额度"
            />
            <MetricCard
              label="充值余额"
              value={formatCredits(overview.topUpAvailableCredits)}
              detail="在线充值获得，长期有效"
            />
            <MetricCard
              label="赠送额度"
              value={formatCredits(overview.promotionAvailableCredits)}
              detail="优先使用临近到期额度"
            />
            <MetricCard
              label="本月 Token"
              value={formatCredits(overview.monthTokens)}
              detail="输入、输出及缓存 Token 合计"
            />
          </div>

          <div className="grid gap-4 lg:grid-cols-3">
            <QuickLink
              href="/billing/usage"
              icon={<Sparkles className="size-5" />}
              title="查看用量"
              description="按日期查看请求、Token 与算力点趋势。"
            />
            <QuickLink
              href="/billing/bills"
              icon={<ReceiptText className="size-5" />}
              title="查看账单"
              description="核对云算力扣费、充值与订单状态。"
            />
            <QuickLink
              href="/billing/pricing"
              icon={<WalletCards className="size-5" />}
              title="了解价格"
              description="查看当前计价版本和各模型计费规则。"
            />
          </div>

          {!overview.paymentEnabled ? (
            <BillingPanel className="border-amber-200 bg-amber-50/70 dark:border-amber-900 dark:bg-amber-950/20">
              <p className="font-medium">在线充值尚未开放</p>
              <p className="mt-1 text-sm text-muted-foreground">
                管理员完成支付商户配置并启用后，微信支付与支付宝入口会自动开放。
              </p>
            </BillingPanel>
          ) : null}
        </>
      )}
    </div>
  );
}

function QuickLink({
  href,
  icon,
  title,
  description,
}: {
  href: string;
  icon: React.ReactNode;
  title: string;
  description: string;
}) {
  return (
    <Link href={href}>
      <BillingPanel className="group h-full transition-colors hover:bg-muted/80">
        <div className="flex items-center justify-between">
          <div className="flex size-10 items-center justify-center rounded-2xl bg-background">
            {icon}
          </div>
          <ArrowRight className="size-4 text-muted-foreground transition-transform group-hover:translate-x-1" />
        </div>
        <h2 className="mt-5 font-semibold">{title}</h2>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
      </BillingPanel>
    </Link>
  );
}
