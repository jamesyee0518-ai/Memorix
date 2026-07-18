import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { ReportType, ReportStatus, ExportStatus } from "@/lib/types";

// ===== 报告状态标签 =====

const reportStatusConfig: Record<
  ReportStatus,
  { label: string; className: string }
> = {
  pending: {
    label: "等待中",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  processing: {
    label: "生成中",
    className:
      "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  done: {
    label: "已完成",
    className:
      "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
  },
  failed: {
    label: "失败",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
  archived: {
    label: "已归档",
    className:
      "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  },
};

export function ReportStatusBadge({
  status,
  className,
}: {
  status: ReportStatus;
  className?: string;
}) {
  const config = reportStatusConfig[status] ?? reportStatusConfig.pending;
  return (
    <Badge
      variant="outline"
      className={cn(
        "border-transparent font-medium",
        config.className,
        className
      )}
    >
      {config.label}
    </Badge>
  );
}

export function getReportStatusLabel(status: ReportStatus): string {
  return reportStatusConfig[status]?.label ?? status;
}

// ===== 报告类型标签 =====

const reportTypeConfig: Record<
  ReportType,
  { label: string; className: string }
> = {
  daily: {
    label: "日报",
    className:
      "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  weekly: {
    label: "周报",
    className:
      "bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300",
  },
  topic: {
    label: "专题报告",
    className:
      "bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300",
  },
};

export function ReportTypeBadge({
  reportType,
  className,
}: {
  reportType: ReportType;
  className?: string;
}) {
  const config = reportTypeConfig[reportType] ?? {
    label: reportType,
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  };
  return (
    <Badge
      variant="outline"
      className={cn(
        "border-transparent font-medium",
        config.className,
        className
      )}
    >
      {config.label}
    </Badge>
  );
}

export function getReportTypeLabel(reportType: ReportType): string {
  return reportTypeConfig[reportType]?.label ?? reportType;
}

// ===== 导出状态标签 =====

const exportStatusConfig: Record<
  ExportStatus,
  { label: string; className: string }
> = {
  pending: {
    label: "等待中",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  processing: {
    label: "导出中",
    className:
      "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  done: {
    label: "已完成",
    className:
      "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
  },
  failed: {
    label: "失败",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
};

export function ExportStatusBadge({
  status,
  className,
}: {
  status: ExportStatus;
  className?: string;
}) {
  const config = exportStatusConfig[status] ?? exportStatusConfig.pending;
  return (
    <Badge
      variant="outline"
      className={cn(
        "border-transparent font-medium",
        config.className,
        className
      )}
    >
      {config.label}
    </Badge>
  );
}

export function getExportStatusLabel(status: ExportStatus): string {
  return exportStatusConfig[status]?.label ?? status;
}

// ===== 质量评分进度条 =====

function getQualityScoreColor(score: number): string {
  if (score < 40) return "bg-red-500";
  if (score <= 70) return "bg-yellow-500";
  return "bg-green-500";
}

function getQualityScoreTextColor(score: number): string {
  if (score < 40) return "text-red-600";
  if (score <= 70) return "text-yellow-600";
  return "text-green-600";
}

export function QualityScoreBar({
  score,
  showLabel = true,
  className,
}: {
  score?: number;
  showLabel?: boolean;
  className?: string;
}) {
  if (score === undefined || score === null) {
    return <span className="text-sm text-muted-foreground">-</span>;
  }
  const clamped = Math.max(0, Math.min(100, score));
  return (
    <div className={cn("flex items-center gap-2", className)}>
      <div className="h-2 w-20 overflow-hidden rounded-full bg-gray-200 dark:bg-gray-700">
        <div
          className={cn(
            "h-full rounded-full transition-all",
            getQualityScoreColor(clamped)
          )}
          style={{ width: `${clamped}%` }}
        />
      </div>
      {showLabel && (
        <span
          className={cn(
            "text-sm font-medium",
            getQualityScoreTextColor(clamped)
          )}
        >
          {clamped}
        </span>
      )}
    </div>
  );
}
