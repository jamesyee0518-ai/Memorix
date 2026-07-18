"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import {
  Loader2,
  MessageCircle,
  Plus,
  History,
  Trash2,
} from "lucide-react";
import { qaApi } from "@/lib/api";
import type { QaSession } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

// ===== 工具函数 =====

function formatRelativeTime(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMin = Math.floor(diffMs / 60000);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  if (diffMin < 1) return "刚刚";
  if (diffMin < 60) return `${diffMin} 分钟前`;
  if (diffHour < 24) return `${diffHour} 小时前`;
  if (diffDay < 7) return `${diffDay} 天前`;
  return d.toLocaleDateString("zh-CN", {
    month: "2-digit",
    day: "2-digit",
  });
}

// ===== 单个会话项 =====

function ConversationItem({
  session,
  isSelected,
  onSelect,
  onDelete,
  deleting,
}: {
  session: QaSession;
  isSelected: boolean;
  onSelect: (sessionId: string) => void;
  onDelete: (sessionId: string) => void;
  deleting: boolean;
}) {
  return (
    <div
      className={cn(
        "group relative rounded-lg border transition-colors",
        isSelected
          ? "border-primary bg-primary/5"
          : "border-transparent hover:border-border hover:bg-muted/50"
      )}
    >
      <button
        type="button"
        onClick={() => onSelect(session.id)}
        className="flex w-full items-start gap-2 rounded-lg px-3 py-2.5 pr-10 text-left"
      >
        <MessageCircle
          className={cn(
            "mt-0.5 size-4 shrink-0",
            isSelected ? "text-primary" : "text-muted-foreground"
          )}
        />
        <div className="min-w-0 flex-1">
          <p
            className={cn(
              "truncate text-sm font-medium",
              isSelected ? "text-primary" : "text-foreground"
            )}
            title={session.title || "未命名对话"}
          >
            {session.title || "未命名对话"}
          </p>
          <p className="mt-0.5 text-xs text-muted-foreground">
            {formatRelativeTime(session.updatedAt || session.createdAt)}
          </p>
        </div>
      </button>
      <button
        type="button"
        aria-label={`删除对话：${session.title || "未命名对话"}`}
        title="删除对话"
        disabled={deleting}
        onClick={(event) => {
          event.stopPropagation();
          onDelete(session.id);
        }}
        className="absolute right-2 top-1/2 flex size-7 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground opacity-0 transition-all hover:bg-destructive/10 hover:text-destructive focus-visible:opacity-100 disabled:pointer-events-none group-hover:opacity-100 group-focus-within:opacity-100"
      >
        {deleting ? (
          <Loader2 className="size-3.5 animate-spin" />
        ) : (
          <Trash2 className="size-3.5" />
        )}
      </button>
    </div>
  );
}

// ===== 主组件 =====

interface ConversationListProps {
  topicId?: string;
  selectedSessionId?: string;
  onSelectSession: (sessionId: string) => void;
  onNewChat: () => void;
  onSessionDeleted: (deletedSessionId: string, nextSessionId?: string) => void;
}

export function ConversationList({
  topicId,
  selectedSessionId,
  onSelectSession,
  onNewChat,
  onSessionDeleted,
}: ConversationListProps) {
  const queryClient = useQueryClient();
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const { data, isLoading, error } = useQuery({
    queryKey: ["qa-sessions", topicId],
    queryFn: () => qaApi.getSessions(topicId),
    enabled: !!topicId,
  });

  const sessions = data?.items ?? [];

  const handleDelete = async (sessionId: string) => {
    if (!window.confirm("确定删除这条对话吗？对话消息也会一并删除。")) return;
    setDeletingId(sessionId);
    try {
      await qaApi.deleteSession(sessionId);
      const nextSessionId = sessions.find((item) => item.id !== sessionId)?.id;
      await queryClient.invalidateQueries({ queryKey: ["qa-sessions"] });
      onSessionDeleted(sessionId, nextSessionId);
      toast.success("对话已删除");
    } catch {
      toast.error("删除对话失败，请重试");
    } finally {
      setDeletingId(null);
    }
  };

  return (
    <div className="flex h-full flex-col">
      {/* 顶部：新对话按钮 */}
      <div className="border-b p-3">
        <Button
          variant="outline"
          size="sm"
          className="w-full"
          onClick={onNewChat}
        >
          <Plus className="mr-1.5 size-3.5" />
          新对话
        </Button>
      </div>

      {/* 会话列表 */}
      <div className="flex-1 space-y-1 overflow-y-auto p-2">
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="size-5 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <p className="px-3 py-8 text-center text-xs text-muted-foreground">
            加载会话列表失败
          </p>
        ) : sessions.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-2 py-10 text-center">
            <History className="size-8 text-muted-foreground/50" />
            <p className="text-xs text-muted-foreground">
              {topicId ? "暂无历史对话" : "请先选择专题"}
            </p>
          </div>
        ) : (
          sessions.map((session) => (
            <ConversationItem
              key={session.id}
              session={session}
              isSelected={session.id === selectedSessionId}
              onSelect={onSelectSession}
              onDelete={handleDelete}
              deleting={deletingId === session.id}
            />
          ))
        )}
      </div>
    </div>
  );
}
