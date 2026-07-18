import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

// ===== AI 状态标签 =====

const aiStatusConfig: Record<string, { label: string; className: string }> = {
  pending: {
    label: "待处理",
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
  processing: {
    label: "处理中",
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

export function AiStatusBadge({
  status,
  className,
}: {
  status: string;
  className?: string;
}) {
  const config = aiStatusConfig[status] ?? {
    label: status,
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  };
  return (
    <Badge
      variant="outline"
      className={cn("border-transparent font-medium", config.className, className)}
    >
      {config.label}
    </Badge>
  );
}

export function getAiStatusLabel(status: string): string {
  return aiStatusConfig[status]?.label ?? status;
}

// ===== 价值评分进度条 =====

function getScoreColor(score: number): string {
  if (score < 40) return "bg-red-500";
  if (score <= 70) return "bg-yellow-500";
  return "bg-green-500";
}

function getScoreTextColor(score: number): string {
  if (score < 40) return "text-red-600";
  if (score <= 70) return "text-yellow-600";
  return "text-green-600";
}

export function ValueScoreBar({
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
          className={cn("h-full rounded-full transition-all", getScoreColor(clamped))}
          style={{ width: `${clamped}%` }}
        />
      </div>
      {showLabel && (
        <span className={cn("text-sm font-medium", getScoreTextColor(clamped))}>
          {clamped}
        </span>
      )}
    </div>
  );
}

// ===== 实体类型标签 =====

const entityTypeConfig: Record<string, { label: string; className: string }> = {
  company: {
    label: "公司",
    className: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  product: {
    label: "产品",
    className:
      "bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300",
  },
  person: {
    label: "人物",
    className:
      "bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300",
  },
  technology: {
    label: "技术",
    className:
      "bg-cyan-100 text-cyan-700 dark:bg-cyan-900/40 dark:text-cyan-300",
  },
  event: {
    label: "事件",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
  market: {
    label: "市场",
    className:
      "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
  },
  organization: {
    label: "组织",
    className:
      "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  },
};

export function EntityTypeBadge({
  entityType,
  className,
}: {
  entityType: string;
  className?: string;
}) {
  const config = entityTypeConfig[entityType] ?? {
    label: entityType,
    className: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
  };
  return (
    <Badge
      variant="outline"
      className={cn("border-transparent font-medium", config.className, className)}
    >
      {config.label}
    </Badge>
  );
}

export function getEntityTypeLabel(entityType: string): string {
  return entityTypeConfig[entityType]?.label ?? entityType;
}

export const ENTITY_TYPES = [
  "company",
  "product",
  "person",
  "technology",
  "event",
  "market",
  "organization",
] as const;

// ===== 标签类型着色 =====

const tagTypeConfig: Record<string, string> = {
  category: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  topic: "bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300",
  sentiment:
    "bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300",
  keyword: "bg-cyan-100 text-cyan-700 dark:bg-cyan-900/40 dark:text-cyan-300",
  industry:
    "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
  custom: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
};

export function TagBadge({
  name,
  type,
  className,
}: {
  name: string;
  type?: string;
  className?: string;
}) {
  const colorClass = (type && tagTypeConfig[type]) || tagTypeConfig.custom;
  return (
    <Badge
      variant="outline"
      className={cn("border-transparent font-medium", colorClass, className)}
    >
      {name}
    </Badge>
  );
}
