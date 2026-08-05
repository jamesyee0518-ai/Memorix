"use client";

import { use } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowLeft,
  Loader2,
  Code2,
  Terminal,
  FileEdit,
  Wrench,
  User,
  Bot,
  Activity,
} from "lucide-react";
import { agentMemoryApi } from "@/lib/api";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

const ACTION_ICONS: Record<string, typeof Wrench> = {
  tool: Wrench,
  edit: FileEdit,
  command: Terminal,
};

const ACTION_COLORS: Record<string, string> = {
  tool: "text-purple-500",
  edit: "text-green-500",
  command: "text-orange-500",
};

export default function SessionEventsPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = use(params);

  const { data: session } = useQuery({
    queryKey: ["agent-memory-session", id],
    queryFn: () => agentMemoryApi.getSession(id),
  });

  const { data: turns, isLoading } = useQuery({
    queryKey: ["agent-memory-turns", id],
    queryFn: () => agentMemoryApi.listTurns(id),
  });

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <Link
          href="/agent-memory"
          className="inline-flex items-center text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4 mr-1" />
          返回
        </Link>
      </div>

      <div>
        <h1 className="text-2xl font-bold flex items-center gap-2">
          <Activity className="h-6 w-6 text-primary" />
          采集事件流
        </h1>
        <p className="text-muted-foreground text-sm mt-1">
          {session?.taskTitle ?? id} — 从 agent hook 采集的原始回合与动作
        </p>
      </div>

      {/* Stats */}
      <div className="flex gap-4">
        <Badge variant="secondary">
          {turns?.length ?? 0} 个回合
        </Badge>
        <Badge variant="secondary">
          {turns?.reduce((sum, t) => sum + (t.actionsCount ?? 0), 0) ?? 0} 个动作
        </Badge>
        {session?.externalSessionKey && (
          <Badge variant="outline">
            {session.externalSessionKey.split(":")[0]}
          </Badge>
        )}
      </div>

      {/* Turns timeline */}
      {isLoading ? (
        <div className="flex justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : !turns || turns.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-muted-foreground">
            <Activity className="h-10 w-10 mb-3 opacity-50" />
            <p>暂无采集事件</p>
            <p className="text-xs mt-1">
              确保 agent hook 已安装并指向 Memorix ingest 端点
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-4">
          {turns.map((turn) => (
            <Card key={turn.id}>
              <CardHeader className="pb-3">
                <div className="flex items-center justify-between">
                  <CardTitle className="text-sm flex items-center gap-2">
                    <Badge variant="outline">Turn {turn.seq}</Badge>
                    <Badge variant={turn.status === "completed" ? "secondary" : "default"}>
                      {turn.status === "completed" ? "已完成" : "进行中"}
                    </Badge>
                  </CardTitle>
                  <span className="text-xs text-muted-foreground">
                    {new Date(turn.createdAt).toLocaleString("zh-CN")}
                  </span>
                </div>
              </CardHeader>
              <CardContent className="space-y-3 pt-0">
                {/* User message */}
                {turn.userMessage && (
                  <div className="flex gap-2">
                    <User className="h-4 w-4 shrink-0 text-blue-500 mt-0.5" />
                    <div className="flex-1 min-w-0">
                      <p className="text-xs font-medium text-muted-foreground mb-1">用户</p>
                      <p className="text-sm whitespace-pre-wrap break-words bg-blue-50 dark:bg-blue-950/30 rounded-md p-2">
                        {turn.userMessage}
                      </p>
                    </div>
                  </div>
                )}

                {/* Assistant message */}
                {turn.assistantMessage && (
                  <div className="flex gap-2">
                    <Bot className="h-4 w-4 shrink-0 text-green-500 mt-0.5" />
                    <div className="flex-1 min-w-0">
                      <p className="text-xs font-medium text-muted-foreground mb-1">Agent</p>
                      <p className="text-sm whitespace-pre-wrap break-words bg-green-50 dark:bg-green-950/30 rounded-md p-2 max-h-48 overflow-y-auto">
                        {turn.assistantMessage}
                      </p>
                    </div>
                  </div>
                )}

                {/* Actions */}
                {turn.actions && turn.actions.length > 0 && (
                  <div className="space-y-1.5">
                    <p className="text-xs font-medium text-muted-foreground">
                      动作 ({turn.actions.length})
                    </p>
                    {turn.actions.map((action) => {
                      const Icon = ACTION_ICONS[action.actionKind] ?? Code2;
                      const color = ACTION_COLORS[action.actionKind] ?? "text-muted-foreground";
                      return (
                        <div
                          key={action.id}
                          className="flex items-start gap-2 rounded-md border p-2 text-xs"
                        >
                          <Icon className={`h-3.5 w-3.5 shrink-0 mt-0.5 ${color}`} />
                          <div className="flex-1 min-w-0">
                            {action.toolName && (
                              <span className="font-mono font-medium">{action.toolName}</span>
                            )}
                            {action.filePath && (
                              <span className="text-muted-foreground ml-1">
                                → {action.filePath.split("/").pop()}
                              </span>
                            )}
                            {action.command && (
                              <code className="block font-mono text-muted-foreground mt-1 bg-muted px-1.5 py-0.5 rounded break-all">
                                {action.command}
                              </code>
                            )}
                            {action.toolResult && (
                              <p className="text-muted-foreground mt-1 max-h-24 overflow-y-auto whitespace-pre-wrap break-words">
                                {action.toolResult.slice(0, 500)}
                                {action.toolResult.length > 500 && "..."}
                              </p>
                            )}
                          </div>
                          <Badge
                            variant="outline"
                            className={
                              action.success
                                ? "text-green-600 border-green-300"
                                : "text-red-600 border-red-300"
                            }
                          >
                            {action.success ? "成功" : "失败"}
                          </Badge>
                        </div>
                      );
                    })}
                  </div>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
