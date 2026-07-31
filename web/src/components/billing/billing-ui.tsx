import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import type { BillingUsagePointResponse } from "@/lib/types";

export function formatCredits(value: number): string {
  return new Intl.NumberFormat("zh-CN", {
    maximumFractionDigits: value < 100 ? 2 : 0,
  }).format(value);
}

export function formatAmount(value: number, currency = "CNY"): string {
  return new Intl.NumberFormat("zh-CN", {
    style: "currency",
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

export function formatMinorAmount(value: number, currency = "CNY"): string {
  return formatAmount(value / 100, currency);
}

export function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

export function BillingPageHeader({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h1 className="text-3xl font-semibold tracking-tight">{title}</h1>
        <p className="mt-2 text-sm text-muted-foreground">{description}</p>
      </div>
      {action}
    </div>
  );
}

export function BillingPanel({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={cn(
        "rounded-[26px] border border-border/40 bg-muted/45 p-5 shadow-none sm:p-7",
        className
      )}
    >
      {children}
    </section>
  );
}

export function MetricCard({
  label,
  value,
  detail,
  className,
}: {
  label: string;
  value: ReactNode;
  detail?: ReactNode;
  className?: string;
}) {
  return (
    <BillingPanel className={cn("min-h-36", className)}>
      <p className="text-sm font-medium text-muted-foreground">{label}</p>
      <div className="mt-4 text-3xl font-semibold tracking-tight">{value}</div>
      {detail ? <div className="mt-3 text-xs text-muted-foreground">{detail}</div> : null}
    </BillingPanel>
  );
}

type TrendMetric = "amount" | "credits" | "requests" | "tokens";

export function UsageChart({
  points,
  metric,
  kind = "bars",
  color = "#3b82f6",
  height = 260,
}: {
  points: BillingUsagePointResponse[];
  metric: TrendMetric;
  kind?: "bars" | "area";
  color?: string;
  height?: number;
}) {
  const values = points.map((point) => Number(point[metric]) || 0);
  const maxValue = Math.max(1, ...values);
  const width = 900;
  const chartTop = 20;
  const chartBottom = 210;
  const chartHeight = chartBottom - chartTop;
  const left = 55;
  const right = 885;
  const plotWidth = right - left;
  const step = plotWidth / Math.max(points.length, 1);
  const y = (value: number) => chartBottom - (value / maxValue) * chartHeight;
  const labelFormatter = (value: number) =>
    new Intl.NumberFormat("zh-CN", { notation: value >= 100000 ? "compact" : "standard" }).format(
      value
    );
  const line = points
    .map((point, index) => {
      const x = left + step * index + step / 2;
      return `${index === 0 ? "M" : "L"} ${x.toFixed(2)} ${y(Number(point[metric]) || 0).toFixed(
        2
      )}`;
    })
    .join(" ");
  const area =
    points.length > 0
      ? `${line} L ${(left + step * (points.length - 1) + step / 2).toFixed(
          2
        )} ${chartBottom} L ${(left + step / 2).toFixed(2)} ${chartBottom} Z`
      : "";

  return (
    <div className="w-full overflow-hidden">
      <svg
        viewBox={`0 0 ${width} 240`}
        role="img"
        aria-label="用量趋势图"
        className="w-full"
        style={{ height }}
        preserveAspectRatio="none"
      >
        <defs>
          <linearGradient id={`billing-${metric}-${kind}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={color} stopOpacity="0.55" />
            <stop offset="100%" stopColor={color} stopOpacity="0.08" />
          </linearGradient>
        </defs>
        {[0, 0.5, 1].map((ratio) => {
          const gridY = chartBottom - ratio * chartHeight;
          return (
            <g key={ratio}>
              <line
                x1={left}
                x2={right}
                y1={gridY}
                y2={gridY}
                stroke="currentColor"
                className="text-border"
              />
              <text
                x={left - 10}
                y={gridY + 5}
                textAnchor="end"
                className="fill-muted-foreground text-[12px]"
              >
                {labelFormatter(maxValue * ratio)}
              </text>
            </g>
          );
        })}
        {kind === "bars"
          ? points.map((point, index) => {
              const value = Number(point[metric]) || 0;
              const barWidth = Math.max(3, Math.min(24, step * 0.62));
              const barX = left + step * index + (step - barWidth) / 2;
              const barY = y(value);
              return (
                <rect
                  key={point.date}
                  x={barX}
                  y={barY}
                  width={barWidth}
                  height={Math.max(1, chartBottom - barY)}
                  rx={Math.min(5, barWidth / 3)}
                  fill={color}
                  opacity="0.92"
                >
                  <title>
                    {new Date(point.date).toLocaleDateString("zh-CN")} · {labelFormatter(value)}
                  </title>
                </rect>
              );
            })
          : null}
        {kind === "area" && area ? (
          <>
            <path d={area} fill={`url(#billing-${metric}-${kind})`} />
            <path d={line} fill="none" stroke={color} strokeWidth="3" />
          </>
        ) : null}
        {points.length ? (
          <>
            <text x={left} y="234" className="fill-muted-foreground text-[12px]">
              {new Date(points[0].date).toLocaleDateString("zh-CN", {
                month: "numeric",
                day: "numeric",
              })}
            </text>
            <text x={right} y="234" textAnchor="end" className="fill-muted-foreground text-[12px]">
              {new Date(points[points.length - 1].date).toLocaleDateString("zh-CN", {
                month: "numeric",
                day: "numeric",
              })}
            </text>
          </>
        ) : null}
      </svg>
    </div>
  );
}

export function BillingEmpty({
  title,
  description,
}: {
  title: string;
  description: string;
}) {
  return (
    <div className="flex min-h-48 flex-col items-center justify-center text-center">
      <p className="font-medium">{title}</p>
      <p className="mt-2 max-w-sm text-sm text-muted-foreground">{description}</p>
    </div>
  );
}

export function BillingLoading() {
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {[0, 1, 2].map((item) => (
        <div key={item} className="h-36 animate-pulse rounded-[26px] bg-muted" />
      ))}
    </div>
  );
}
