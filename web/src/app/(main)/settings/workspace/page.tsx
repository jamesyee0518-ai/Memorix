"use client";

import { useEffect, useState, useCallback, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  Layers,
  Loader2,
  Save,
  HardDrive,
  RefreshCw,
  Settings2,
  Database,
  DatabaseZap,
  FolderOpen,
} from "lucide-react";
import { workspaceApi, ApiRequestError } from "@/lib/api";
import type {
  Workspace,
  WorkspaceMode,
  ModelProviderOption,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import {
  isDesktopApp,
  openDesktopDirectory,
  selectDesktopDirectory,
} from "@/lib/desktop";

const modeLabels: Record<WorkspaceMode, string> = {
  local: "本地模式",
  cloud: "云端模式",
  hybrid: "混合模式",
};

const defaultModelProviders = [
  { value: "lmstudio", label: "LM Studio" },
  { value: "ollama", label: "Ollama" },
  { value: "openai", label: "OpenAI" },
  { value: "anthropic", label: "Anthropic" },
  { value: "custom", label: "自定义" },
];

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

function InfoRow({
  label,
  value,
  icon,
}: {
  label: string;
  value?: string | null;
  icon?: ReactNode;
}) {
  return (
    <div className="flex items-center justify-between border-b border-border/50 py-2.5 last:border-0">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="flex items-center gap-1.5 text-sm font-medium">
        {icon}
        {value || "-"}
      </span>
    </div>
  );
}

function ToggleSwitch({
  checked,
  disabled,
  onChange,
}: {
  checked: boolean;
  disabled?: boolean;
  onChange: () => void;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => !disabled && onChange()}
      className={cn(
        "relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors disabled:cursor-not-allowed disabled:opacity-50",
        checked ? "bg-primary" : "bg-input"
      )}
    >
      <span
        className={cn(
          "pointer-events-none inline-block size-5 transform rounded-full bg-background shadow-lg ring-0 transition-transform",
          checked ? "translate-x-5" : "translate-x-0"
        )}
      />
    </button>
  );
}

export default function WorkspacePage() {
  const router = useRouter();
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [providers, setProviders] = useState<ModelProviderOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  // 编辑状态
  const [editName, setEditName] = useState("");
  const [editModelProvider, setEditModelProvider] = useState("");
  const [editSyncEnabled, setEditSyncEnabled] = useState(false);
  const [editInboxEnabled, setEditInboxEnabled] = useState(false);
  const [editVaultPath, setEditVaultPath] = useState("");
  const [isEditing, setIsEditing] = useState(false);
  const [isDesktop, setIsDesktop] = useState(false);

  const fetchWorkspace = useCallback(async () => {
    setIsLoading(true);
    try {
      const [ws, provs] = await Promise.all([
        workspaceApi.getCurrent(),
        workspaceApi.getModelProviders().catch(() => []),
      ]);
      setWorkspace(ws);
      setProviders(provs);
      if (ws) {
        setEditName(ws.name);
        setEditModelProvider(ws.modelProvider);
        setEditSyncEnabled(ws.syncEnabled);
        setEditInboxEnabled(ws.inboxEnabled);
        setEditVaultPath(ws.localVaultPath ?? "");
      }
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载工作区信息失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    setIsDesktop(isDesktopApp());
  }, []);

  useEffect(() => {
    fetchWorkspace();
  }, [fetchWorkspace]);

  const handleSave = async () => {
    if (!workspace) return;
    if (!editName.trim()) {
      toast.error("请输入工作区名称");
      return;
    }
    setSaving(true);
    try {
      const updated = await workspaceApi.update(workspace.id, {
        name: editName.trim(),
        modelProvider: editModelProvider,
        syncEnabled: editSyncEnabled,
        inboxEnabled: editInboxEnabled,
        localVaultPath: editVaultPath.trim() || undefined,
      });
      setWorkspace(updated);
      setIsEditing(false);
      toast.success("工作区信息已更新");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "更新工作区失败";
      toast.error(message);
    } finally {
      setSaving(false);
    }
  };

  const handleCancel = () => {
    if (workspace) {
      setEditName(workspace.name);
      setEditModelProvider(workspace.modelProvider);
      setEditSyncEnabled(workspace.syncEnabled);
      setEditInboxEnabled(workspace.inboxEnabled);
      setEditVaultPath(workspace.localVaultPath ?? "");
    }
    setIsEditing(false);
  };

  const handleSelectVault = async () => {
    try {
      const selected = await selectDesktopDirectory(editVaultPath);
      if (selected) setEditVaultPath(selected);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "选择目录失败");
    }
  };

  const handleOpenVault = async () => {
    if (!workspace?.localVaultPath) return;
    try {
      await openDesktopDirectory(workspace.localVaultPath);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "打开 Vault 失败");
    }
  };

  // 模型提供者选项：优先使用 API 返回，否则使用默认值
  const modelProviderOptions =
    providers.length > 0
      ? providers.map((p) => ({ value: p.provider, label: p.label }))
      : defaultModelProviders;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!workspace) {
    return (
      <div className="space-y-4">
        <div>
          <h2 className="text-lg font-semibold">工作区</h2>
          <p className="text-sm text-muted-foreground">管理您的工作区配置</p>
        </div>
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Layers className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无工作区</p>
            <p className="mt-1 text-sm text-muted-foreground">
              您还没有创建工作区，请先完成初始化设置
            </p>
            <Button className="mt-4" onClick={() => router.push("/setup")}>
              <Layers className="mr-2 size-4" />
              创建工作区
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">工作区</h2>
          <p className="text-sm text-muted-foreground">
            管理您的工作区配置与模型提供者
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={fetchWorkspace}>
          <RefreshCw className="mr-2 size-4" />
          刷新
        </Button>
      </div>

      {/* 基本信息 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Layers className="size-4 text-primary" />
            基本信息
          </CardTitle>
          <CardDescription>工作区名称与运行模式</CardDescription>
        </CardHeader>
        <CardContent className="space-y-1">
          {isEditing ? (
            <div className="space-y-3">
              <div className="space-y-2">
                <Label htmlFor="ws-name">工作区名称</Label>
                <Input
                  id="ws-name"
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                  placeholder="请输入工作区名称"
                />
              </div>
              <div className="flex items-center justify-between border-b border-border/50 py-2.5">
                <span className="text-sm text-muted-foreground">运行模式</span>
                <Badge variant="secondary">
                  {modeLabels[workspace.mode] ?? workspace.mode}
                </Badge>
              </div>
            </div>
          ) : (
            <>
              <InfoRow label="工作区名称" value={workspace.name} />
              <InfoRow
                label="运行模式"
                value={modeLabels[workspace.mode] ?? workspace.mode}
              />
              <InfoRow label="工作区 ID" value={workspace.id} />
              <InfoRow
                label="创建时间"
                value={formatDate(workspace.createdAt)}
              />
              <InfoRow
                label="更新时间"
                value={formatDate(workspace.updatedAt)}
              />
            </>
          )}
        </CardContent>
      </Card>

      {/* 存储与提供者 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <HardDrive className="size-4 text-primary" />
            存储与提供者
          </CardTitle>
          <CardDescription>
            工作区使用的存储、文件、任务及模型提供者
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-1">
          <InfoRow label="存储提供者" value={workspace.storageProvider} />
          <InfoRow
            label="数据库类型"
            value={workspace.mode === "cloud" ? "PostgreSQL" : "SQLite"}
            icon={
              workspace.mode === "cloud" ? (
                <DatabaseZap className="size-3.5 text-primary" />
              ) : (
                <Database className="size-3.5 text-primary" />
              )
            }
          />
          <InfoRow label="文件提供者" value={workspace.fileProvider} />
          <InfoRow label="任务提供者" value={workspace.jobProvider} />
          {isEditing ? (
            <div className="space-y-2 border-b border-border/50 py-2.5">
              <Label>模型提供者</Label>
              <Select
                value={editModelProvider}
                onValueChange={(v) => setEditModelProvider(v as string)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="请选择模型提供者" />
                </SelectTrigger>
                <SelectContent>
                  {modelProviderOptions.map((opt) => (
                    <SelectItem key={opt.value} value={opt.value}>
                      {opt.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : (
            <InfoRow label="模型提供者" value={workspace.modelProvider} />
          )}
          {workspace.localDbPath && (
            <InfoRow label="本地数据库路径" value={workspace.localDbPath} />
          )}
          {(workspace.mode === "local" || workspace.mode === "hybrid") &&
            (isEditing ? (
              <div className="space-y-2 border-b border-border/50 py-2.5">
                <Label htmlFor="workspace-vault">本地 Vault 路径</Label>
                <div className="flex gap-2">
                  <Input
                    id="workspace-vault"
                    value={editVaultPath}
                    onChange={(event) => setEditVaultPath(event.target.value)}
                    placeholder="选择或输入 Vault 目录"
                  />
                  {isDesktop && (
                    <Button
                      type="button"
                      variant="outline"
                      size="icon"
                      title="选择 Vault 目录"
                      onClick={handleSelectVault}
                    >
                      <FolderOpen className="size-4" />
                    </Button>
                  )}
                </div>
              </div>
            ) : (
              <div className="flex items-center justify-between gap-3 border-b border-border/50 py-2.5">
                <span className="text-sm text-muted-foreground">本地 Vault 路径</span>
                <div className="flex min-w-0 items-center gap-2">
                  <span className="truncate text-sm font-medium" title={workspace.localVaultPath ?? ""}>
                    {workspace.localVaultPath || "-"}
                  </span>
                  {isDesktop && workspace.localVaultPath && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      title="打开 Vault"
                      onClick={handleOpenVault}
                    >
                      <FolderOpen className="size-3.5" />
                    </Button>
                  )}
                </div>
              </div>
            ))}
          {workspace.cloudApiBaseUrl && (
            <InfoRow label="云端 API 地址" value={workspace.cloudApiBaseUrl} />
          )}
          {workspace.cloudWorkspaceId && (
            <InfoRow label="云端工作区 ID" value={workspace.cloudWorkspaceId} />
          )}
        </CardContent>
      </Card>

      {/* 同步与收件箱 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Settings2 className="size-4 text-primary" />
            同步与收件箱
          </CardTitle>
          <CardDescription>控制数据同步与收件箱功能</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium">数据同步</p>
              <p className="text-xs text-muted-foreground">
                启用后将在本地与云端之间同步数据
              </p>
            </div>
            <ToggleSwitch
              checked={editSyncEnabled}
              disabled={!isEditing}
              onChange={() => setEditSyncEnabled(!editSyncEnabled)}
            />
          </div>
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium">收件箱</p>
              <p className="text-xs text-muted-foreground">
                启用后可通过收件箱快速收集资料
              </p>
            </div>
            <ToggleSwitch
              checked={editInboxEnabled}
              disabled={!isEditing}
              onChange={() => setEditInboxEnabled(!editInboxEnabled)}
            />
          </div>
        </CardContent>
      </Card>

      {/* 操作按钮 */}
      <div className="flex justify-end gap-2">
        {isEditing ? (
          <>
            <Button
              variant="outline"
              onClick={handleCancel}
              disabled={saving}
            >
              取消
            </Button>
            <Button onClick={handleSave} disabled={saving}>
              {saving ? (
                <Loader2 className="mr-2 size-4 animate-spin" />
              ) : (
                <Save className="mr-2 size-4" />
              )}
              保存
            </Button>
          </>
        ) : (
          <Button onClick={() => setIsEditing(true)}>
            <Settings2 className="mr-2 size-4" />
            编辑工作区
          </Button>
        )}
      </div>
    </div>
  );
}
