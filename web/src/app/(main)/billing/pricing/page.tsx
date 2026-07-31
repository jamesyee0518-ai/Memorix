"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowRight } from "lucide-react";
import { billingApi, workspaceApi } from "@/lib/api";
import type { BillingPricingResponse, RechargeCatalogResponse } from "@/lib/types";
import {
  BillingEmpty,
  BillingLoading,
  BillingPageHeader,
  BillingPanel,
  formatCredits,
  formatMinorAmount,
} from "@/components/billing/billing-ui";

export default function PricingPage() {
  const [pricing, setPricing] = useState<BillingPricingResponse | null>(null);
  const [catalog, setCatalog] = useState<RechargeCatalogResponse | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let disposed = false;
    void (async () => {
      try {
        const workspace = await workspaceApi.getCurrent();
        if (!workspace) throw new Error("请先选择工作区");
        const [pricingResponse, catalogResponse] = await Promise.all([
          billingApi.pricing(workspace.id),
          billingApi.rechargeCatalog(),
        ]);
        if (!disposed) {
          setPricing(pricingResponse);
          setCatalog(catalogResponse);
        }
      } catch (reason) {
        if (!disposed) {
          setError(reason instanceof Error ? reason.message : "价格信息加载失败");
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
        title="价格"
        description="主单位为算力点（Credits）；金额换算以任务结算时生效的价格版本为准。"
        action={
          <Link
            href="/billing/recharge"
            className="inline-flex h-10 items-center gap-2 rounded-full bg-foreground px-5 text-sm font-medium text-background"
          >
            选择充值套餐
            <ArrowRight className="size-4" />
          </Link>
        }
      />

      {error ? (
        <BillingPanel>
          <p className="font-medium text-destructive">无法加载价格信息</p>
          <p className="mt-2 text-sm text-muted-foreground">{error}</p>
        </BillingPanel>
      ) : !pricing || !catalog ? (
        <BillingLoading />
      ) : (
        <>
          <BillingPanel className="bg-foreground text-background">
            <div className="grid gap-6 sm:grid-cols-3">
              <div>
                <p className="text-sm text-background/60">当前价格方案</p>
                <p className="mt-3 text-2xl font-semibold">{pricing.planCode}</p>
              </div>
              <div>
                <p className="text-sm text-background/60">版本</p>
                <p className="mt-3 text-2xl font-semibold">V{pricing.version}</p>
              </div>
              <div>
                <p className="text-sm text-background/60">生效时间</p>
                <p className="mt-3 text-lg font-semibold">
                  {pricing.effectiveFrom
                    ? new Date(pricing.effectiveFrom).toLocaleString("zh-CN")
                    : "尚未发布"}
                </p>
              </div>
            </div>
            {pricing.isShadowPricing ? (
              <p className="mt-5 border-t border-background/15 pt-4 text-xs text-background/55">
                当前处于影子计价阶段：可查看价格，但不会从用户余额实际扣减。
              </p>
            ) : null}
          </BillingPanel>

          <section>
            <h2 className="text-lg font-semibold">充值套餐</h2>
            {catalog.products.length === 0 ? (
              <BillingPanel className="mt-4">
                <BillingEmpty title="暂无可售套餐" description="充值套餐尚未配置。" />
              </BillingPanel>
            ) : (
              <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                {catalog.products.map((product) => (
                  <BillingPanel key={product.id}>
                    <p className="text-sm text-muted-foreground">{product.displayName}</p>
                    <p className="mt-4 text-3xl font-semibold">
                      {formatMinorAmount(product.amountMinor, product.currency)}
                    </p>
                    <p className="mt-3 text-sm font-medium">
                      {formatCredits(product.paidCredits)} 算力点
                    </p>
                    {product.bonusCredits > 0 ? (
                      <p className="mt-1 text-sm text-emerald-600">
                        赠送 {formatCredits(product.bonusCredits)} 点
                      </p>
                    ) : null}
                    <p className="mt-5 text-xs leading-5 text-muted-foreground">
                      {product.description}
                    </p>
                  </BillingPanel>
                ))}
              </div>
            )}
          </section>

          <BillingPanel className="overflow-hidden p-0">
            <div className="px-5 py-5 sm:px-7">
              <h2 className="font-semibold">计费规则</h2>
              <p className="mt-1 text-xs text-muted-foreground">
                匹配模型专属规则；无专属规则时使用同计量类型的默认规则。
              </p>
            </div>
            {pricing.rules.length === 0 ? (
              <BillingEmpty title="暂无已发布价格" description="价格版本发布后会在此展示。" />
            ) : (
              <div className="overflow-x-auto border-t">
                <table className="w-full min-w-[820px] text-left text-sm">
                  <thead className="bg-background/65 text-xs text-muted-foreground">
                    <tr>
                      <th className="px-5 py-3 font-medium sm:px-7">计量类型</th>
                      <th className="px-4 py-3 font-medium">供应商</th>
                      <th className="px-4 py-3 font-medium">模型</th>
                      <th className="px-4 py-3 text-right font-medium">计量单位</th>
                      <th className="px-4 py-3 text-right font-medium">单位数量</th>
                      <th className="px-5 py-3 text-right font-medium sm:px-7">算力点费率</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pricing.rules.map((rule, index) => (
                      <tr
                        key={`${rule.meterType}-${rule.providerId}-${rule.modelId}-${index}`}
                        className="border-t border-border/60"
                      >
                        <td className="px-5 py-4 font-medium sm:px-7">
                          {meterName(rule.meterType)}
                        </td>
                        <td className="px-4 py-4 text-muted-foreground">
                          {rule.providerId || "通用"}
                        </td>
                        <td className="px-4 py-4 text-muted-foreground">
                          {rule.modelId || "全部"}
                        </td>
                        <td className="px-4 py-4 text-right">{rule.unit}</td>
                        <td className="px-4 py-4 text-right tabular-nums">
                          {formatCredits(rule.unitSize)}
                        </td>
                        <td className="px-5 py-4 text-right font-medium tabular-nums sm:px-7">
                          {formatCredits(rule.creditRate)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </BillingPanel>

          <p className="text-xs leading-6 text-muted-foreground">
            实际金额和 Token 会受模型、缓存命中、工具调用与供应商账单修正影响。任务创建时会预占算力点，
            完成后按实际用量结算并退回多余预占。
          </p>
        </>
      )}
    </div>
  );
}

function meterName(value: string) {
  const labels: Record<string, string> = {
    INPUT_TOKEN: "输入 Token",
    OUTPUT_TOKEN: "输出 Token",
    CACHE_READ_TOKEN: "缓存读取",
    CACHE_WRITE_TOKEN: "缓存写入",
    REASONING_TOKEN: "推理 Token",
    EMBEDDING_TOKEN: "向量 Token",
    IMAGE: "图片",
    AUDIO_SECOND: "音频时长",
    VIDEO_SECOND: "视频时长",
    TOOL_CALL: "工具调用",
  };
  return labels[value] ?? value;
}
