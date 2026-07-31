"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Check, Clock3, XCircle } from "lucide-react";
import { billingApi, workspaceApi } from "@/lib/api";
import type { RechargeOrderResponse } from "@/lib/types";
import {
  BillingLoading,
  BillingPageHeader,
  BillingPanel,
  formatCredits,
  formatMinorAmount,
} from "@/components/billing/billing-ui";

export default function PaymentResultPage() {
  const [order, setOrder] = useState<RechargeOrderResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState("");

  useEffect(() => {
    let disposed = false;
    void (async () => {
      try {
        const workspace = await workspaceApi.getCurrent();
        if (!workspace) throw new Error("请先选择工作区");
        const params = new URLSearchParams(window.location.search);
        const orderId = params.get("orderId");
        const orderNo = params.get("out_trade_no") ?? params.get("orderNo");
        let result: RechargeOrderResponse | undefined;
        if (orderId) {
          result = await billingApi.refreshRechargeOrder(orderId, workspace.id);
        } else {
          const list = await billingApi.rechargeOrders(workspace.id);
          result = list.items.find((item) => !orderNo || item.orderNo === orderNo);
          if (result && !["PAID", "CLOSED", "FAILED"].includes(result.status)) {
            result = await billingApi.refreshRechargeOrder(result.id, workspace.id);
          }
        }
        if (!result) throw new Error("未找到对应的充值订单");
        if (!disposed) setOrder(result);
      } catch (reason) {
        if (!disposed) setMessage(reason instanceof Error ? reason.message : "订单状态查询失败");
      } finally {
        if (!disposed) setLoading(false);
      }
    })();
    return () => {
      disposed = true;
    };
  }, []);

  return (
    <div className="space-y-7 pb-10">
      <BillingPageHeader title="支付结果" description="支付结果以支付平台异步通知和服务端查询为准。" />
      {loading ? (
        <BillingLoading />
      ) : message ? (
        <BillingPanel>
          <p className="font-medium text-destructive">无法确认支付结果</p>
          <p className="mt-2 text-sm text-muted-foreground">{message}</p>
        </BillingPanel>
      ) : order ? (
        <BillingPanel className="mx-auto max-w-xl text-center">
          <ResultIcon status={order.status} />
          <h2 className="mt-5 text-2xl font-semibold">
            {order.status === "PAID"
              ? "充值成功"
              : order.status === "FAILED" || order.status === "CLOSED"
                ? "支付未完成"
                : "正在确认支付"}
          </h2>
          <p className="mt-2 text-sm text-muted-foreground">
            {formatMinorAmount(order.amountMinor, order.currency)} · {order.productName}
          </p>
          {order.status === "PAID" ? (
            <p className="mt-3 text-sm text-emerald-600">
              {formatCredits(order.paidCredits + order.bonusCredits)} 算力点已入账
            </p>
          ) : (
            <p className="mt-3 text-sm text-muted-foreground">
              若已完成付款，请稍候在充值记录中刷新状态。
            </p>
          )}
          <div className="mt-7 flex flex-wrap justify-center gap-3">
            <Link
              href="/billing/recharge"
              className="inline-flex h-10 items-center rounded-full bg-foreground px-5 text-sm font-medium text-background"
            >
              返回充值记录
            </Link>
            <Link
              href="/billing"
              className="inline-flex h-10 items-center rounded-full bg-muted px-5 text-sm font-medium"
            >
              返回计费中心
            </Link>
          </div>
        </BillingPanel>
      ) : null}
    </div>
  );
}

function ResultIcon({ status }: { status: string }) {
  if (status === "PAID") {
    return (
      <span className="mx-auto flex size-16 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
        <Check className="size-8" />
      </span>
    );
  }
  if (status === "FAILED" || status === "CLOSED") {
    return (
      <span className="mx-auto flex size-16 items-center justify-center rounded-full bg-red-100 text-red-700">
        <XCircle className="size-8" />
      </span>
    );
  }
  return (
    <span className="mx-auto flex size-16 items-center justify-center rounded-full bg-amber-100 text-amber-700">
      <Clock3 className="size-8" />
    </span>
  );
}
