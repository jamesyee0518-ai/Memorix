"use client";

import { useEffect, useMemo, useState } from "react";
import { Check, Copy, ExternalLink, Loader2, QrCode, X } from "lucide-react";
import QRCode from "qrcode";
import { toast } from "sonner";
import { billingApi, workspaceApi } from "@/lib/api";
import type {
  PaymentMethodResponse,
  RechargeCatalogResponse,
  RechargeOrderResponse,
} from "@/lib/types";
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

const terminalStatuses = new Set(["PAID", "CLOSED", "FAILED", "REFUNDED"]);

export default function RechargePage() {
  const [workspaceId, setWorkspaceId] = useState("");
  const [catalog, setCatalog] = useState<RechargeCatalogResponse | null>(null);
  const [orders, setOrders] = useState<RechargeOrderResponse[]>([]);
  const [productId, setProductId] = useState("");
  const [methodKey, setMethodKey] = useState("");
  const [currentOrder, setCurrentOrder] = useState<RechargeOrderResponse | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState("");
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let disposed = false;
    void (async () => {
      try {
        const workspace = await workspaceApi.getCurrent();
        if (!workspace) throw new Error("请先选择工作区");
        const [catalogResponse, orderResponse] = await Promise.all([
          billingApi.rechargeCatalog(),
          billingApi.rechargeOrders(workspace.id),
        ]);
        if (disposed) return;
        setWorkspaceId(workspace.id);
        setCatalog(catalogResponse);
        setOrders(orderResponse.items);
        setProductId(catalogResponse.products[0]?.id ?? "");
        const firstEnabled = catalogResponse.methods.find((method) => method.enabled);
        setMethodKey(firstEnabled ? `${firstEnabled.channel}:${firstEnabled.scene}` : "");
      } catch (reason) {
        if (!disposed) {
          setError(reason instanceof Error ? reason.message : "充值信息加载失败");
        }
      } finally {
        if (!disposed) setLoading(false);
      }
    })();
    return () => {
      disposed = true;
    };
  }, []);

  useEffect(() => {
    let active = true;
    const payload = currentOrder?.paymentPayload;
    if (currentOrder?.paymentPayloadType === "QR_CODE" && payload) {
      void QRCode.toDataURL(payload, {
        width: 260,
        margin: 1,
        color: { dark: "#101114", light: "#ffffff" },
        errorCorrectionLevel: "M",
      }).then((url) => {
        if (active) setQrDataUrl(url);
      });
    } else {
      setQrDataUrl("");
    }
    return () => {
      active = false;
    };
  }, [currentOrder?.paymentPayload, currentOrder?.paymentPayloadType]);

  useEffect(() => {
    if (!currentOrder || terminalStatuses.has(currentOrder.status) || !workspaceId) return;
    const timer = window.setInterval(() => {
      void billingApi
        .refreshRechargeOrder(currentOrder.id, workspaceId)
        .then((updated) => {
          setCurrentOrder(updated);
          setOrders((items) => [updated, ...items.filter((item) => item.id !== updated.id)]);
        })
        .catch(() => {
          // 回调和后台恢复任务仍会继续处理，短暂查询失败不打断支付页。
        });
    }, 3500);
    return () => window.clearInterval(timer);
  }, [currentOrder, workspaceId]);

  const enabledMethods = useMemo(
    () => catalog?.methods.filter((method) => method.enabled) ?? [],
    [catalog]
  );

  const submit = async () => {
    if (!workspaceId || !productId || !methodKey) return;
    const [paymentChannel, paymentScene] = methodKey.split(":");
    setSubmitting(true);
    try {
      const order = await billingApi.createRechargeOrder({
        workspaceId,
        rechargeProductId: productId,
        paymentChannel,
        paymentScene,
        idempotencyKey: crypto.randomUUID(),
      });
      setCurrentOrder(order);
      setOrders((items) => [order, ...items.filter((item) => item.id !== order.id)]);
      if (order.paymentPayloadType === "REDIRECT_URL" && order.paymentPayload) {
        window.open(order.paymentPayload, "_blank", "noopener,noreferrer");
      }
    } catch (reason) {
      toast.error(reason instanceof Error ? reason.message : "创建充值订单失败");
    } finally {
      setSubmitting(false);
    }
  };

  const confirmFake = async () => {
    if (!currentOrder) return;
    setSubmitting(true);
    try {
      const updated = await billingApi.confirmFakeRecharge(currentOrder.id, workspaceId);
      setCurrentOrder(updated);
      setOrders((items) => [updated, ...items.filter((item) => item.id !== updated.id)]);
      toast.success("模拟支付已确认，算力点已入账");
    } catch (reason) {
      toast.error(reason instanceof Error ? reason.message : "确认失败");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-7 pb-10">
      <BillingPageHeader
        title="充值"
        description="选择算力点套餐，通过微信支付或支付宝完成在线充值。"
      />

      {error ? (
        <BillingPanel>
          <p className="font-medium text-destructive">无法加载充值信息</p>
          <p className="mt-2 text-sm text-muted-foreground">{error}</p>
        </BillingPanel>
      ) : loading || !catalog ? (
        <BillingLoading />
      ) : (
        <>
          {!catalog.paymentEnabled ? (
            <BillingPanel className="border-amber-200 bg-amber-50/70 dark:border-amber-900 dark:bg-amber-950/20">
              <p className="font-medium">在线充值暂未启用</p>
              <p className="mt-1 text-sm text-muted-foreground">
                套餐可预览；管理员完成支付配置后即可下单。开发环境可单独启用模拟支付。
              </p>
            </BillingPanel>
          ) : null}

          <section>
            <h2 className="text-lg font-semibold">选择充值套餐</h2>
            {catalog.products.length === 0 ? (
              <BillingPanel className="mt-4">
                <BillingEmpty
                  title="暂无可售套餐"
                  description="请管理员在 Payment:Products 中配置充值套餐。"
                />
              </BillingPanel>
            ) : (
              <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                {catalog.products.map((product) => {
                  const selected = product.id === productId;
                  return (
                    <button
                      type="button"
                      key={product.id}
                      onClick={() => setProductId(product.id)}
                      className={cn(
                        "relative rounded-[26px] border p-6 text-left transition-all",
                        selected
                          ? "border-foreground bg-foreground text-background shadow-lg"
                          : "border-border/50 bg-muted/45 hover:border-foreground/35 hover:bg-muted/70"
                      )}
                    >
                      {selected ? (
                        <span className="absolute right-4 top-4 flex size-6 items-center justify-center rounded-full bg-background text-foreground">
                          <Check className="size-4" />
                        </span>
                      ) : null}
                      <p className={cn("text-sm", selected ? "text-background/65" : "text-muted-foreground")}>
                        {product.displayName}
                      </p>
                      <p className="mt-5 text-3xl font-semibold">
                        {formatMinorAmount(product.amountMinor, product.currency)}
                      </p>
                      <p className={cn("mt-3 text-sm", selected ? "text-background/70" : "text-muted-foreground")}>
                        {formatCredits(product.paidCredits)} 算力点
                        {product.bonusCredits > 0
                          ? ` + 赠送 ${formatCredits(product.bonusCredits)}`
                          : ""}
                      </p>
                      <p className={cn("mt-5 text-xs leading-5", selected ? "text-background/55" : "text-muted-foreground")}>
                        {product.description}
                      </p>
                    </button>
                  );
                })}
              </div>
            )}
          </section>

          <BillingPanel>
            <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
              <div>
                <h2 className="font-semibold">支付方式</h2>
                <div className="mt-3 flex flex-wrap gap-2">
                  {catalog.methods.map((method) => (
                    <PaymentMethodButton
                      key={`${method.channel}:${method.scene}`}
                      method={method}
                      selected={methodKey === `${method.channel}:${method.scene}`}
                      onSelect={() => setMethodKey(`${method.channel}:${method.scene}`)}
                    />
                  ))}
                  {catalog.methods.length === 0 ? (
                    <span className="text-sm text-muted-foreground">尚未配置支付渠道</span>
                  ) : null}
                </div>
              </div>
              <button
                type="button"
                disabled={
                  submitting ||
                  !catalog.paymentEnabled ||
                  !productId ||
                  !methodKey ||
                  enabledMethods.length === 0
                }
                onClick={() => void submit()}
                className="inline-flex h-11 min-w-32 items-center justify-center gap-2 rounded-full bg-foreground px-6 text-sm font-medium text-background transition-opacity hover:opacity-85 disabled:cursor-not-allowed disabled:opacity-35"
              >
                {submitting ? <Loader2 className="size-4 animate-spin" /> : null}
                立即充值
              </button>
            </div>
          </BillingPanel>

          <BillingPanel className="overflow-hidden p-0">
            <div className="px-5 py-5 sm:px-7">
              <h2 className="font-semibold">充值记录</h2>
              <p className="mt-1 text-xs text-muted-foreground">仅展示当前工作区最近 50 笔订单</p>
            </div>
            {orders.length === 0 ? (
              <BillingEmpty title="暂无充值记录" description="完成首笔充值后，订单会显示在这里。" />
            ) : (
              <div className="overflow-x-auto border-t">
                <table className="w-full min-w-[760px] text-left text-sm">
                  <thead className="bg-background/65 text-xs text-muted-foreground">
                    <tr>
                      <th className="px-5 py-3 font-medium sm:px-7">创建时间</th>
                      <th className="px-4 py-3 font-medium">订单号</th>
                      <th className="px-4 py-3 font-medium">套餐</th>
                      <th className="px-4 py-3 font-medium">渠道</th>
                      <th className="px-4 py-3 text-right font-medium">金额</th>
                      <th className="px-5 py-3 text-right font-medium sm:px-7">状态</th>
                    </tr>
                  </thead>
                  <tbody>
                    {orders.map((order) => (
                      <tr
                        key={order.id}
                        className="cursor-pointer border-t border-border/60 hover:bg-background/55"
                        onClick={() => setCurrentOrder(order)}
                      >
                        <td className="px-5 py-4 whitespace-nowrap sm:px-7">
                          {formatDateTime(order.createdAt)}
                        </td>
                        <td className="px-4 py-4 font-mono text-xs">{order.orderNo}</td>
                        <td className="px-4 py-4">{order.productName}</td>
                        <td className="px-4 py-4">{channelName(order.channel)}</td>
                        <td className="px-4 py-4 text-right font-medium">
                          {formatMinorAmount(order.amountMinor, order.currency)}
                        </td>
                        <td className="px-5 py-4 text-right sm:px-7">
                          <OrderStatus status={order.status} />
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

      {currentOrder ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/45 p-4 backdrop-blur-sm">
          <div className="relative w-full max-w-md rounded-[30px] bg-background p-7 shadow-2xl">
            <button
              type="button"
              onClick={() => setCurrentOrder(null)}
              className="absolute right-5 top-5 flex size-9 items-center justify-center rounded-full bg-muted hover:bg-muted/75"
              aria-label="关闭"
            >
              <X className="size-4" />
            </button>
            <div className="pr-10">
              <p className="text-sm text-muted-foreground">{currentOrder.productName}</p>
              <h2 className="mt-2 text-3xl font-semibold">
                {formatMinorAmount(currentOrder.amountMinor, currentOrder.currency)}
              </h2>
            </div>

            {currentOrder.status === "PAID" ? (
              <div className="mt-8 flex flex-col items-center py-5 text-center">
                <span className="flex size-16 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
                  <Check className="size-8" />
                </span>
                <p className="mt-5 text-lg font-semibold">充值成功</p>
                <p className="mt-2 text-sm text-muted-foreground">
                  {formatCredits(currentOrder.paidCredits + currentOrder.bonusCredits)} 算力点已入账
                </p>
              </div>
            ) : currentOrder.paymentPayloadType === "QR_CODE" ? (
              <div className="mt-7 text-center">
                <div className="mx-auto flex size-[280px] items-center justify-center rounded-3xl border bg-white p-3">
                  {qrDataUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={qrDataUrl} alt="微信支付二维码" className="size-full" />
                  ) : (
                    <Loader2 className="size-7 animate-spin text-slate-400" />
                  )}
                </div>
                <p className="mt-4 font-medium">请使用微信扫码支付</p>
                <p className="mt-1 text-xs text-muted-foreground">
                  订单将在 {new Date(currentOrder.expiresAt).toLocaleTimeString("zh-CN")} 过期
                </p>
              </div>
            ) : currentOrder.paymentPayloadType === "REDIRECT_URL" ? (
              <div className="mt-8">
                <p className="text-sm leading-6 text-muted-foreground">
                  支付宝收银台已在新窗口打开。支付完成后，本页面会自动刷新订单状态。
                </p>
                <a
                  href={currentOrder.paymentPayload ?? "#"}
                  target="_blank"
                  rel="noreferrer"
                  className="mt-5 inline-flex h-11 w-full items-center justify-center gap-2 rounded-full bg-[#1677ff] px-5 text-sm font-medium text-white"
                >
                  <ExternalLink className="size-4" />
                  重新打开支付宝
                </a>
              </div>
            ) : currentOrder.paymentPayloadType === "FAKE" ? (
              <div className="mt-8">
                <p className="text-sm text-muted-foreground">
                  这是开发环境模拟订单，不会产生真实资金交易。
                </p>
                <button
                  type="button"
                  disabled={submitting}
                  onClick={() => void confirmFake()}
                  className="mt-5 inline-flex h-11 w-full items-center justify-center gap-2 rounded-full bg-foreground px-5 text-sm font-medium text-background disabled:opacity-40"
                >
                  {submitting ? <Loader2 className="size-4 animate-spin" /> : null}
                  模拟支付成功
                </button>
              </div>
            ) : (
              <div className="mt-8">
                <OrderStatus status={currentOrder.status} />
                <p className="mt-3 text-sm text-muted-foreground">
                  当前订单没有可用的支付载荷，请关闭后重新下单。
                </p>
              </div>
            )}

            <div className="mt-7 flex items-center justify-between border-t pt-4 text-xs text-muted-foreground">
              <span className="truncate font-mono">{currentOrder.orderNo}</span>
              <button
                type="button"
                onClick={() => {
                  void navigator.clipboard.writeText(currentOrder.orderNo);
                  toast.success("订单号已复制");
                }}
                className="ml-3 inline-flex shrink-0 items-center gap-1 hover:text-foreground"
              >
                <Copy className="size-3" />
                复制
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function PaymentMethodButton({
  method,
  selected,
  onSelect,
}: {
  method: PaymentMethodResponse;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      disabled={!method.enabled}
      onClick={onSelect}
      className={cn(
        "inline-flex h-11 items-center gap-2 rounded-full border px-4 text-sm font-medium transition-colors",
        selected ? "border-foreground bg-foreground text-background" : "bg-background",
        !method.enabled && "cursor-not-allowed opacity-35"
      )}
    >
      <QrCode className="size-4" />
      {method.displayName}
      {!method.enabled ? "（未开放）" : ""}
    </button>
  );
}

function OrderStatus({ status }: { status: string }) {
  const paid = status === "PAID";
  const failed = status === "FAILED" || status === "CLOSED" || status === "REFUNDED";
  return (
    <span
      className={cn(
        "inline-flex rounded-full px-2.5 py-1 text-xs font-medium",
        paid && "bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300",
        failed && "bg-muted text-muted-foreground",
        !paid && !failed && "bg-amber-100 text-amber-700 dark:bg-amber-950 dark:text-amber-300"
      )}
    >
      {statusLabel(status)}
    </span>
  );
}

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    CREATED: "待创建支付",
    PAYING: "待支付",
    PAID: "已支付",
    CLOSED: "已关闭",
    FAILED: "失败",
    REFUNDING: "退款中",
    PARTIALLY_REFUNDED: "部分退款",
    REFUNDED: "已退款",
  };
  return labels[status] ?? status;
}

function channelName(channel: string) {
  return { WECHAT: "微信支付", ALIPAY: "支付宝", FAKE: "模拟支付" }[channel] ?? channel;
}
