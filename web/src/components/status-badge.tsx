import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { SourceStatus } from "@/lib/types";

const statusConfig: Record<
  SourceStatus,
  { label: string; className: string }
> = {
  pending: {
    label: "待处理",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  queued: {
    label: "队列中",
    className: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  saved: {
    label: "已保存",
    className:
      "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
  },
  failed: {
    label: "失败",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
  archived: {
    label: "已归档",
    className: "bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400",
  },
};

export function StatusBadge({
  status,
  className,
}: {
  status: SourceStatus;
  className?: string;
}) {
  const config = statusConfig[status] ?? statusConfig.pending;
  return (
    <Badge
      variant="outline"
      className={cn("border-transparent font-medium", config.className, className)}
    >
      {config.label}
    </Badge>
  );
}

/** 获取状态标签文本 */
export function getStatusLabel(status: SourceStatus): string {
  return statusConfig[status]?.label ?? status;
}

/** 来源类型标签 */
const sourceTypeLabels: Record<string, string> = {
  url: "URL",
  text: "文本",
  pdf: "PDF",
};

export function getSourceTypeLabel(type: string): string {
  return sourceTypeLabels[type] ?? type;
}
