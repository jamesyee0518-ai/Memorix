"use client";

import { useCallback, useEffect, useState } from "react";
import { toast } from "sonner";
import {
  Activity,
  AlertCircle,
  Brain,
  CheckCircle2,
  Cloud,
  Database,
  HardDrive,
  ListChecks,
  Loader2,
  RefreshCw,
  Server,
  ShieldCheck,
  Sparkles,
  type LucideIcon,
} from "lucide-react";
import { runtimeApi, ApiRequestError } from "@/lib/api";
import type { RuntimeHealth, WorkspaceRuntimeHealth } from "@/lib/types";
import { useAuthStore } from "@/stores/auth-store";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { cn } from "@/lib/utils";

type StatusTone = "healthy" | "warning" | "error" | "neutral";

const workspaceItems: Array<{
  key: keyof WorkspaceRuntimeHealth;
  label: string;
  icon: LucideIcon;
}> = [
  { key: "knowledgeStorage", label: "知识库存储", icon: Database },
  { key: "fileStorage", label: "文件存储", icon: HardDrive },
  { key: "backgroundProcessing", label: "后台处理", icon: ListChecks },
  { key: "aiService", label: "AI 模型服务", icon: Brain },
  { key: "embeddingService", label: "向量模型服务", icon: Sparkles },
  { key: "cloudSync", label: "云端同步", icon: Cloud },
];

const platformItems: Array<{
  key: keyof RuntimeHealth;
  label: string;
  icon: LucideIcon;
}> = [
  { key: "database", label: "数据库", icon: Database },
  { key: "fileStorage", label: "文件存储", icon: HardDrive },
  { key: "jobQueue", label: "全局任务队列", icon: ListChecks },
  { key: "llmService", label: "LLM 服务", icon: Brain },
  { key: "embeddingService", label: "Embedding 服务", icon: Sparkles },
  { key: "ollama", label: "Ollama", icon: Server },
  { key: "lmStudio", label: "LM Studio", icon: Server },
  { key: "cloudApi", label: "云端 API", icon: Cloud },
];

function statusMeta(status?: string): { label: string; tone: StatusTone } {
  const normalized = status?.toLowerCase() ?? "unknown";
  if (["ok", "healthy", "up", "connected", "running", "available"].includes(normalized)) {
    return { label: "正常", tone: "healthy" };
  }
  if (["warning", "degraded"].includes(normalized)) {
    return { label: "部分受限", tone: "warning" };
  }
  if (["not_configured", "unknown"].includes(normalized)) {
    return { label: normalized === "not_configured" ? "未配置" : "未知", tone: "neutral" };
  }
  return { label: "异常", tone: "error" };
}

function toneClasses(tone: StatusTone) {
  if (tone === "healthy") return "bg-emerald-50 text-emerald-700 border-emerald-200";
  if (tone === "warning") return "bg-amber-50 text-amber-700 border-amber-200";
  if (tone === "error") return "bg-red-50 text-red-700 border-red-200";
  return "bg-slate-50 text-slate-600 border-slate-200";
}

function formatDate(dateStr?: string): string {
  return dateStr ? new Date(dateStr).toLocaleString("zh-CN") : "-";
}

function StatusTile({ label, status, icon: Icon, showRaw = false }: {
  label: string;
  status?: string;
  icon: LucideIcon;
  showRaw?: boolean;
}) {
  const meta = statusMeta(status);
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border p-3">
      <div className="flex min-w-0 items-center gap-3">
        <div className={cn("flex size-9 shrink-0 items-center justify-center rounded-lg border", toneClasses(meta.tone))}>
          <Icon className="size-4" />
        </div>
        <div className="min-w-0">
          <p className="text-sm font-medium">{label}</p>
          {showRaw && <p className="truncate text-xs text-muted-foreground" title={status}>{status ?? "unknown"}</p>}
        </div>
      </div>
      <Badge variant="outline" className={cn("shrink-0", toneClasses(meta.tone))}>{meta.label}</Badge>
    </div>
  );
}

export default function RuntimePage() {
  const role = useAuthStore((state) => state.user?.role);
  const isAdmin = role === "platform_admin";
  const [workspaceHealth, setWorkspaceHealth] = useState<WorkspaceRuntimeHealth | null>(null);
  const [platformHealth, setPlatformHealth] = useState<RuntimeHealth | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const fetchHealth = useCallback(async () => {
    setIsLoading(true);
    try {
      const [workspace, platform] = await Promise.all([
        runtimeApi.workspaceHealth(),
        isAdmin ? runtimeApi.platformHealth().catch(() => null) : Promise.resolve(null),
      ]);
      setWorkspaceHealth(workspace);
      setPlatformHealth(platform);
    } catch (err) {
      const message = err instanceof ApiRequestError ? err.message : "加载运行时状态失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  }, [isAdmin]);

  useEffect(() => {
    fetchHealth();
  }, [fetchHealth]);

  if (isLoading) {
    return <div className="flex items-center justify-center py-16"><Loader2 className="size-8 animate-spin text-muted-foreground" /></div>;
  }

  if (!workspaceHealth) {
    return (
      <div className="space-y-4">
        <div>
          <h2 className="text-lg font-semibold">运行时状态</h2>
          <p className="text-sm text-muted-foreground">检查当前工作区的核心能力是否可用</p>
        </div>
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Activity className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">无法获取工作区状态</p>
            <Button className="mt-4" onClick={fetchHealth}><RefreshCw className="mr-2 size-4" />重新加载</Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  const overall = statusMeta(workspaceHealth.overall);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">运行时状态</h2>
          <p className="text-sm text-muted-foreground">普通用户查看工作区自检；管理员可继续查看平台级诊断</p>
        </div>
        <Button variant="outline" size="sm" onClick={fetchHealth}><RefreshCw className="mr-2 size-4" />刷新</Button>
      </div>

      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle className="flex items-center gap-2 text-base"><ShieldCheck className="size-4 text-primary" />工作区自检</CardTitle>
              <CardDescription>仅展示当前用户可操作的脱敏状态，不暴露服务器地址和内部错误。</CardDescription>
            </div>
            <Badge variant="outline" className={toneClasses(overall.tone)}>{overall.label}</Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-3 text-sm sm:grid-cols-3">
            <div><p className="text-xs text-muted-foreground">当前工作区</p><p className="mt-1 font-medium">{workspaceHealth.workspaceName ?? "未配置"}</p></div>
            <div><p className="text-xs text-muted-foreground">工作区模式</p><p className="mt-1 font-medium">{workspaceHealth.workspaceMode ?? "未配置"}</p></div>
            <div><p className="text-xs text-muted-foreground">检查时间</p><p className="mt-1 font-medium">{formatDate(workspaceHealth.checkedAt)}</p></div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            {workspaceItems.map((item) => (
              <StatusTile key={item.key} label={item.label} icon={item.icon} status={String(workspaceHealth[item.key] ?? "unknown")} />
            ))}
          </div>
          {workspaceHealth.issues.length === 0 ? (
            <div className="flex items-start gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
              <CheckCircle2 className="mt-0.5 size-4 shrink-0" />当前工作区未发现需要处理的问题。
            </div>
          ) : (
            <div className="space-y-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
              {workspaceHealth.issues.map((issue) => <div key={issue} className="flex items-start gap-2"><AlertCircle className="mt-0.5 size-4 shrink-0" /><span>{issue}</span></div>)}
            </div>
          )}
        </CardContent>
      </Card>

      {isAdmin && (
        <Card>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2 text-base"><Server className="size-4 text-primary" />平台诊断</CardTitle>
                <CardDescription>管理员专用：查看平台共享数据库、全局队列和服务端模型状态。</CardDescription>
              </div>
              <Badge variant="secondary">仅平台管理员可见</Badge>
            </div>
          </CardHeader>
          <CardContent>
            {platformHealth ? (
              <div className="grid gap-3 sm:grid-cols-2">
                {platformItems.map((item) => (
                  <StatusTile key={item.key} label={item.label} icon={item.icon} status={String(platformHealth[item.key] ?? "unknown")} showRaw />
                ))}
              </div>
            ) : (
              <div className="flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-3 text-sm text-amber-800">
                <AlertCircle className="size-4" />平台诊断暂时不可用，工作区自检不受影响。
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
