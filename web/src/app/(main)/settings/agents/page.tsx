"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Plus,
  MoreVertical,
  Trash2,
  Pencil,
  Settings2,
  Plug,
  Bot,
  Loader2,
  Copy,
  Check,
  Download,
  FileText,
  ExternalLink,
} from "lucide-react";
import { agentApi, ApiRequestError } from "@/lib/api";
import type {
  AgentProfile,
  AgentToolDefinition,
  McpConfig,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import { AgentProfileDialog } from "@/components/agent-profile-dialog";

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

export default function AgentProfilesPage() {
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<AgentProfile | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AgentProfile | null>(null);
  const [mcpTarget, setMcpTarget] = useState<AgentProfile | null>(null);
  const [testTarget, setTestTarget] = useState<AgentProfile | null>(null);

  // 获取 Profile 列表
  const { data: profiles, isLoading } = useQuery({
    queryKey: ["agent-profiles"],
    queryFn: () => agentApi.listProfiles(),
  });

  // 删除 Mutation
  const deleteMutation = useMutation({
    mutationFn: (id: string) => agentApi.deleteProfile(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["agent-profiles"] });
      toast.success("Agent Profile 已删除");
      setDeleteTarget(null);
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    },
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Agent 接入</h2>
          <p className="text-sm text-muted-foreground">
            管理 Agent Profile，配置 MCP 接入和工具权限
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Link href="/settings/agents/logs">
            <Button variant="outline" size="sm">
              <FileText className="mr-2 size-4" />
              调用日志
            </Button>
          </Link>
          <Button onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 size-4" />
            新建 Profile
          </Button>
        </div>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : !profiles || profiles.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Bot className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无 Agent Profile</p>
            <p className="mt-1 text-sm text-muted-foreground">
              创建您的第一个 Agent Profile，开始使用 MCP 接入
            </p>
            <Button className="mt-4" onClick={() => setCreateOpen(true)}>
              <Plus className="mr-2 size-4" />
              新建 Profile
            </Button>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>描述</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>工具数</TableHead>
                <TableHead>传输方式</TableHead>
                <TableHead>创建时间</TableHead>
                <TableHead className="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {profiles.map((profile) => (
                <TableRow key={profile.id}>
                  <TableCell className="font-medium">{profile.name}</TableCell>
                  <TableCell className="max-w-[200px] truncate text-sm text-muted-foreground">
                    {profile.description || "-"}
                  </TableCell>
                  <TableCell>
                    {profile.status === "active" ? (
                      <Badge className="bg-green-100 text-green-700">启用</Badge>
                    ) : (
                      <Badge variant="secondary">已禁用</Badge>
                    )}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline">
                      {profile.allowedToolNames?.length ?? 0} 个
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <code className="text-xs">{profile.transport}</code>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatDate(profile.createdAt)}
                  </TableCell>
                  <TableCell className="text-right">
                    <DropdownMenu>
                      <DropdownMenuTrigger
                        render={
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            type="button"
                          />
                        }
                      >
                        <MoreVertical className="size-4" />
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem
                          onClick={() => setEditTarget(profile)}
                        >
                          <Pencil className="mr-2 size-4" />
                          编辑
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          onClick={() => setMcpTarget(profile)}
                        >
                          <Settings2 className="mr-2 size-4" />
                          MCP 配置
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          onClick={() => setTestTarget(profile)}
                        >
                          <Plug className="mr-2 size-4" />
                          连接测试
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          variant="destructive"
                          onClick={() => setDeleteTarget(profile)}
                        >
                          <Trash2 className="mr-2 size-4" />
                          删除
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      {/* 新建/编辑弹窗 */}
      <AgentProfileDialog
        open={createOpen || !!editTarget}
        onOpenChange={(v) => {
          if (!v) {
            setCreateOpen(false);
            setEditTarget(null);
          }
        }}
        profile={editTarget}
        onSuccess={() => {
          queryClient.invalidateQueries({ queryKey: ["agent-profiles"] });
        }}
      />

      {/* 删除确认 */}
      <Dialog
        open={!!deleteTarget}
        onOpenChange={(v) => !v && setDeleteTarget(null)}
      >
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>确认删除</DialogTitle>
            <DialogDescription>
              确定要删除 Agent Profile「{deleteTarget?.name}」吗？此操作不可恢复。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button
              variant="destructive"
              onClick={() => {
                if (deleteTarget) deleteMutation.mutate(deleteTarget.id);
              }}
              disabled={deleteMutation.isPending}
            >
              {deleteMutation.isPending && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MCP 配置弹窗 */}
      <McpConfigDialog
        profile={mcpTarget}
        onClose={() => setMcpTarget(null)}
      />

      {/* 连接测试弹窗 */}
      <TestConnectionDialog
        profile={testTarget}
        onClose={() => setTestTarget(null)}
      />
    </div>
  );
}

// ===== MCP 配置对话框 =====

function McpConfigDialog({
  profile,
  onClose,
}: {
  profile: AgentProfile | null;
  onClose: () => void;
}) {
  const [config, setConfig] = useState<McpConfig | null>(null);
  const [loading, setLoading] = useState(false);
  const [copied, setCopied] = useState(false);

  // 获取配置
  useQuery({
    queryKey: ["mcp-config", profile?.id],
    queryFn: async () => {
      if (!profile) return null;
      setLoading(true);
      try {
        const result = await agentApi.generateMcpConfig(profile.id);
        setConfig(result);
        return result;
      } catch (err) {
        const message =
          err instanceof ApiRequestError ? err.message : "获取 MCP 配置失败";
        toast.error(message);
        return null;
      } finally {
        setLoading(false);
      }
    },
    enabled: !!profile,
  });

  const configJson = config ? JSON.stringify(config, null, 2) : "";

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(configJson);
      setCopied(true);
      toast.success("已复制到剪贴板");
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error("复制失败，请手动复制");
    }
  };

  const handleDownload = () => {
    const blob = new Blob([configJson], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "claude_desktop_config.json";
    a.click();
    URL.revokeObjectURL(url);
    toast.success("配置文件已下载");
  };

  return (
    <Dialog
      open={!!profile}
      onOpenChange={(v) => !v && onClose()}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>MCP 配置 - {profile?.name}</DialogTitle>
          <DialogDescription>
            将此配置添加到 Claude Desktop 的 claude_desktop_config.json 文件中
          </DialogDescription>
        </DialogHeader>

        {loading ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="size-6 animate-spin text-muted-foreground" />
          </div>
        ) : config ? (
          <div className="space-y-3">
            <div className="rounded-lg border bg-slate-900 p-3">
              <pre className="max-h-64 overflow-auto text-xs text-slate-200">
                <code>{configJson}</code>
              </pre>
            </div>

            <div className="flex items-start gap-2 rounded-lg border border-blue-200 bg-blue-50 p-3">
              <ExternalLink className="mt-0.5 size-4 shrink-0 text-blue-600" />
              <p className="text-sm text-blue-800">
                将此配置添加到 Claude Desktop 的{" "}
                <code className="font-mono text-xs">claude_desktop_config.json</code>{" "}
                文件中，重启 Claude Desktop 后即可使用。
              </p>
            </div>
          </div>
        ) : (
          <p className="py-8 text-center text-sm text-muted-foreground">
            无法获取配置
          </p>
        )}

        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            关闭
          </DialogClose>
          {config && (
            <>
              <Button variant="outline" onClick={handleDownload}>
                <Download className="mr-2 size-4" />
                下载配置文件
              </Button>
              <Button onClick={handleCopy}>
                {copied ? (
                  <Check className="mr-2 size-4" />
                ) : (
                  <Copy className="mr-2 size-4" />
                )}
                {copied ? "已复制" : "复制配置"}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ===== 连接测试对话框 =====

function TestConnectionDialog({
  profile,
  onClose,
}: {
  profile: AgentProfile | null;
  onClose: () => void;
}) {
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<{
    success: boolean;
    message: string;
    tools?: AgentToolDefinition[];
  } | null>(null);

  const handleTest = async () => {
    if (!profile) return;
    setTesting(true);
    setResult(null);
    try {
      const res = await agentApi.testConnection(profile.id);
      setResult(res);
      if (res.success) {
        toast.success("连接测试成功");
      } else {
        toast.error("连接测试失败");
      }
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "连接测试失败";
      toast.error(message);
      setResult({ success: false, message });
    } finally {
      setTesting(false);
    }
  };

  // 自动触发测试
  useEffect(() => {
    if (profile) {
      handleTest();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [profile?.id]);

  return (
    <Dialog
      open={!!profile}
      onOpenChange={(v) => !v && onClose()}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>连接测试 - {profile?.name}</DialogTitle>
          <DialogDescription>
            测试 Agent Profile 的连接并查看可用工具列表
          </DialogDescription>
        </DialogHeader>

        {testing ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="mr-2 size-6 animate-spin text-muted-foreground" />
            <span className="text-sm text-muted-foreground">正在测试连接...</span>
          </div>
        ) : result ? (
          <div className="space-y-3">
            <div
              className={`flex items-center gap-2 rounded-lg p-3 ${
                result.success
                  ? "bg-green-50 text-green-800"
                  : "bg-red-50 text-red-800"
              }`}
            >
              {result.success ? (
                <Check className="size-4" />
              ) : (
                <Plug className="size-4" />
              )}
              <span className="text-sm font-medium">{result.message}</span>
            </div>

            {result.tools && result.tools.length > 0 && (
              <div className="space-y-2">
                <p className="text-sm font-medium">可用工具 ({result.tools.length})</p>
                <div className="space-y-1.5">
                  {result.tools.map((tool) => (
                    <div
                      key={tool.name}
                      className="rounded-lg border p-2"
                    >
                      <code className="text-xs font-medium">{tool.name}</code>
                      <p className="mt-0.5 text-xs text-muted-foreground">
                        {tool.description}
                      </p>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        ) : (
          <p className="py-8 text-center text-sm text-muted-foreground">
            准备测试...
          </p>
        )}

        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            关闭
          </DialogClose>
          <Button onClick={handleTest} disabled={testing}>
            {testing && <Loader2 className="mr-2 size-4 animate-spin" />}
            重新测试
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
