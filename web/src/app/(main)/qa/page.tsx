"use client";

import { useEffect, useState, useRef, useCallback } from "react";
import Link from "next/link";
import { toast } from "sonner";
import {
  MessageCircle,
  Send,
  Loader2,
  Brain,
  ExternalLink,
  AlertTriangle,
  User as UserIcon,
  FileText,
  BookOpen,
  ThumbsUp,
  ThumbsDown,
  Bug,
  ChevronDown,
  ChevronRight,
} from "lucide-react";
import { qaApi, feedbackApi, ApiRequestError } from "@/lib/api";
import { useTopicStore } from "@/stores/topic-store";
import type { QaSession, Citation } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Markdown } from "@/components/markdown";
import { ConversationList } from "@/components/conversation-list";
import { cn } from "@/lib/utils";

// ===== 类型 =====

interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  citations?: Citation[];
  retrieval?: { retrievedCount: number; usedCount: number };
  isInsufficient?: boolean;
  confidence?: number;
  debugInfo?: {
    queryPlan?: string;
    contextTokens?: number;
    retrievedTitles?: string[];
    systemPrompt?: string;
  };
}

// ===== 置信度配置 =====

function getConfidenceConfig(confidence: number) {
  if (confidence >= 0.7) {
    return {
      label: "高置信度",
      className:
        "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300",
    };
  }
  if (confidence >= 0.4) {
    return {
      label: "中置信度",
      className:
        "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300",
    };
  }
  return {
    label: "低置信度",
    className:
      "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  };
}

// ===== 引用来源卡片 =====

function CitationCard({ citation }: { citation: Citation }) {
  return (
    <Link
      href={`/documents/${citation.documentId}?chunkId=${citation.chunkId}`}
      className="block rounded-lg border bg-muted/30 p-3 transition-colors hover:border-primary hover:bg-muted/50"
    >
      <div className="flex items-start gap-3">
        <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-bold text-primary-foreground">
          {citation.index}
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <FileText className="size-3.5 shrink-0 text-muted-foreground" />
            <p className="truncate text-sm font-medium text-primary">
              {citation.title || "无标题"}
            </p>
          </div>
          {citation.sourceUrl && (
            <a
              href={citation.sourceUrl}
              target="_blank"
              rel="noopener noreferrer"
              onClick={(e) => e.stopPropagation()}
              className="mt-0.5 flex items-center gap-1 truncate text-xs text-muted-foreground hover:text-primary hover:underline"
            >
              <ExternalLink className="size-3 shrink-0" />
              <span className="truncate">{citation.sourceUrl}</span>
            </a>
          )}
          <p className="mt-1 line-clamp-2 text-xs leading-relaxed text-muted-foreground">
            {citation.snippet}
          </p>
        </div>
      </div>
    </Link>
  );
}

// ===== 消息气泡 =====

interface MessageBubbleProps {
  message: ChatMessage;
  feedbackedMessages: Record<string, boolean>;
  onFeedback: (messageId: string, isPositive: boolean) => void;
}

function MessageBubble({
  message,
  feedbackedMessages,
  onFeedback,
}: MessageBubbleProps) {
  const isUser = message.role === "user";
  const [showDebug, setShowDebug] = useState(false);
  const [showSystemPrompt, setShowSystemPrompt] = useState(false);

  const hasDebugInfo = !!message.debugInfo;
  const feedbackDone = feedbackedMessages[message.id] !== undefined;
  const feedbackValue = feedbackedMessages[message.id];

  if (isUser) {
    return (
      <div className="flex justify-end">
        <div className="flex max-w-[75%] items-start gap-3">
          <div className="rounded-2xl rounded-tr-sm bg-primary px-4 py-2.5 text-sm text-primary-foreground">
            <p className="whitespace-pre-wrap leading-relaxed">{message.content}</p>
          </div>
          <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-primary/10">
            <UserIcon className="size-4 text-primary" />
          </div>
        </div>
      </div>
    );
  }

  // AI 回答
  return (
    <div className="flex justify-start">
      <div className="flex max-w-[85%] items-start gap-3">
        <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-blue-500 to-purple-600">
          <Brain className="size-4 text-white" />
        </div>
        <div className="min-w-0 flex-1 space-y-3">
          {message.isInsufficient ? (
            // 资料不足提示
            <div className="rounded-2xl rounded-tl-sm border border-amber-300 bg-amber-50 p-4 dark:border-amber-700 dark:bg-amber-900/20">
              <div className="flex items-start gap-2">
                <AlertTriangle className="mt-0.5 size-5 shrink-0 text-amber-600 dark:text-amber-400" />
                <div>
                  <p className="text-sm font-medium text-amber-800 dark:text-amber-200">
                    资料不足
                  </p>
                  <p className="mt-1 text-sm text-amber-700 dark:text-amber-300">
                    {message.content}
                  </p>
                  <p className="mt-2 text-xs text-amber-600 dark:text-amber-400">
                    建议先导入更多相关资料，或尝试更换关键词后再次提问。
                  </p>
                </div>
              </div>
            </div>
          ) : (
            <div className="rounded-2xl rounded-tl-sm border bg-white px-4 py-3 dark:bg-slate-900">
              <Markdown content={message.content} />
            </div>
          )}

          {/* 置信度徽章 */}
          {!message.isInsufficient &&
            message.confidence !== undefined &&
            message.confidence !== null && (
              <div className="flex items-center gap-2">
                {(() => {
                  const config = getConfidenceConfig(message.confidence!);
                  return (
                    <span
                      className={cn(
                        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium",
                        config.className
                      )}
                    >
                      {config.label}
                    </span>
                  );
                })()}
                <span className="text-xs text-muted-foreground">
                  置信度 {Math.round(message.confidence! * 100)}%
                </span>
              </div>
            )}

          {/* 检索信息 */}
          {message.retrieval && !message.isInsufficient && (
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <BookOpen className="size-3" />
              <span>
                检索到 {message.retrieval.retrievedCount} 条资料，引用 {message.retrieval.usedCount} 条
              </span>
            </div>
          )}

          {/* 引用来源 */}
          {message.citations && message.citations.length > 0 && !message.isInsufficient && (
            <div className="space-y-2">
              <p className="text-xs font-medium text-muted-foreground">引用来源</p>
              <div className="grid gap-2">
                {message.citations.map((citation) => (
                  <CitationCard key={`${citation.documentId}-${citation.index}`} citation={citation} />
                ))}
              </div>
            </div>
          )}

          {/* 回答反馈按钮 */}
          {!message.isInsufficient && (
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">回答是否有帮助？</span>
              <Button
                variant="outline"
                size="xs"
                className={cn(
                  feedbackDone && feedbackValue === true
                    ? "border-green-500 text-green-600"
                    : ""
                )}
                disabled={feedbackDone}
                onClick={() => onFeedback(message.id, true)}
              >
                <ThumbsUp className="size-3" />
                有帮助
              </Button>
              <Button
                variant="outline"
                size="xs"
                className={cn(
                  feedbackDone && feedbackValue === false
                    ? "border-red-500 text-red-600"
                    : ""
                )}
                disabled={feedbackDone}
                onClick={() => onFeedback(message.id, false)}
              >
                <ThumbsDown className="size-3" />
                无帮助
              </Button>
              {feedbackDone && (
                <span className="text-xs text-muted-foreground">已反馈</span>
              )}
            </div>
          )}

          {/* 调试信息面板 */}
          {hasDebugInfo && (
            <div className="rounded-lg border bg-muted/30">
              <button
                type="button"
                onClick={() => setShowDebug((v) => !v)}
                className="flex w-full items-center gap-1.5 px-3 py-2 text-xs font-medium text-muted-foreground hover:text-foreground"
              >
                {showDebug ? (
                  <ChevronDown className="size-3.5" />
                ) : (
                  <ChevronRight className="size-3.5" />
                )}
                <Bug className="size-3.5" />
                调试信息
              </button>
              {showDebug && message.debugInfo && (
                <div className="space-y-3 border-t px-3 py-3 text-xs">
                  {/* Query Plan */}
                  {message.debugInfo.queryPlan && (
                    <div>
                      <p className="font-medium text-foreground">Query Plan</p>
                      <pre className="mt-1 whitespace-pre-wrap rounded border bg-background p-2 text-muted-foreground">
                        {message.debugInfo.queryPlan}
                      </pre>
                    </div>
                  )}

                  {/* Retrieved Titles */}
                  {message.debugInfo.retrievedTitles &&
                    message.debugInfo.retrievedTitles.length > 0 && (
                      <div>
                        <p className="font-medium text-foreground">
                          检索文档标题
                        </p>
                        <ul className="mt-1 space-y-0.5">
                          {message.debugInfo.retrievedTitles.map((title, idx) => (
                            <li
                              key={idx}
                              className="flex items-center gap-1.5 text-muted-foreground"
                            >
                              <FileText className="size-3 shrink-0" />
                              {title}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                  {/* Context Tokens */}
                  {message.debugInfo.contextTokens !== undefined && (
                    <div>
                      <p className="font-medium text-foreground">
                        Context Token 数
                      </p>
                      <p className="mt-0.5 text-muted-foreground">
                        {message.debugInfo.contextTokens} tokens
                      </p>
                    </div>
                  )}

                  {/* System Prompt (collapsible) */}
                  {message.debugInfo.systemPrompt && (
                    <div>
                      <button
                        type="button"
                        onClick={() => setShowSystemPrompt((v) => !v)}
                        className="flex items-center gap-1.5 font-medium text-foreground hover:underline"
                      >
                        {showSystemPrompt ? (
                          <ChevronDown className="size-3.5" />
                        ) : (
                          <ChevronRight className="size-3.5" />
                        )}
                        System Prompt
                      </button>
                      {showSystemPrompt && (
                        <pre className="mt-1 max-h-60 overflow-y-auto whitespace-pre-wrap rounded border bg-background p-2 text-muted-foreground">
                          {message.debugInfo.systemPrompt}
                        </pre>
                      )}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ===== 加载中消息 =====

function LoadingMessage() {
  return (
    <div className="flex justify-start">
      <div className="flex items-start gap-3">
        <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-blue-500 to-purple-600">
          <Brain className="size-4 text-white" />
        </div>
        <div className="flex items-center gap-2 rounded-2xl rounded-tl-sm border bg-white px-4 py-3 dark:bg-slate-900">
          <Loader2 className="size-4 animate-spin text-primary" />
          <span className="text-sm text-muted-foreground">
            正在检索资料并生成回答...
          </span>
        </div>
      </div>
    </div>
  );
}

// ===== 主页面 =====

export default function QaPage() {
  const { topics, fetchTopics } = useTopicStore();

  const [topicId, setTopicId] = useState<string>("");
  const [sessionId, setSessionId] = useState<string>("");
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [feedbackedMessages, setFeedbackedMessages] = useState<
    Record<string, boolean>
  >({});

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  // 自动选择第一个专题
  useEffect(() => {
    if (topics.length > 0 && !topicId) {
      setTopicId(topics[0].id);
    }
  }, [topics, topicId]);

  // 自动滚动到底部
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading]);

  const handleSend = useCallback(async () => {
    const question = input.trim();
    if (!question) return;
    if (!topicId) {
      toast.error("请先选择专题");
      return;
    }
    if (loading) return;

    // 添加用户消息
    const userMsg: ChatMessage = {
      id: `user-${Date.now()}`,
      role: "user",
      content: question,
    };
    setMessages((prev) => [...prev, userMsg]);
    setInput("");
    setLoading(true);

    try {
      // 首次提问时自动创建会话
      let currentSessionId = sessionId;
      if (!currentSessionId) {
        const session: QaSession = await qaApi.createSession({
          topicId,
          title: question.slice(0, 50),
        });
        currentSessionId = session.id;
        setSessionId(session.id);
      }

      // 提问
      const response = await qaApi.ask({
        sessionId: currentSessionId,
        topicId,
        question,
        retrieval: { searchType: "hybrid", topK: 10 },
      });

      // 判断是否资料不足（citations 为空且 answer 包含特定提示）
      const isInsufficient =
        response.citations.length === 0 &&
        response.retrieval.usedCount === 0;

      const assistantMsg: ChatMessage = {
        id: response.messageId,
        role: "assistant",
        content: response.answer,
        citations: response.citations,
        retrieval: response.retrieval,
        isInsufficient,
        confidence: response.confidence,
        debugInfo: response.debugInfo,
      };
      setMessages((prev) => [...prev, assistantMsg]);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "回答生成失败，请重试";
      const errorMsg: ChatMessage = {
        id: `error-${Date.now()}`,
        role: "assistant",
        content: `抱歉，处理您的问题时出现错误：${message}`,
        isInsufficient: false,
      };
      setMessages((prev) => [...prev, errorMsg]);
    } finally {
      setLoading(false);
      inputRef.current?.focus();
    }
  }, [input, topicId, sessionId, loading]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleNewChat = () => {
    setSessionId("");
    setMessages([]);
    setInput("");
    setLoading(false);
    setFeedbackedMessages({});
    inputRef.current?.focus();
  };

  // 加载历史会话消息
  const handleSelectSession = useCallback(async (sid: string) => {
    setSessionId(sid);
    setLoading(false);
    setFeedbackedMessages({});
    try {
      const msgs = await qaApi.getMessages(sid);
      const chatMsgs: ChatMessage[] = msgs
        .filter((m) => m.role === "user" || m.role === "assistant")
        .map((m) => ({
          id: m.id,
          role: m.role as "user" | "assistant",
          content: m.content,
          citations: m.citations,
        }));
      setMessages(chatMsgs);
    } catch {
      toast.error("加载会话消息失败");
      setMessages([]);
    }
    inputRef.current?.focus();
  }, []);

  // 提交回答反馈
  const handleFeedback = async (messageId: string, isPositive: boolean) => {
    if (feedbackedMessages[messageId] !== undefined) return;
    // 立即标记，防止重复点击
    setFeedbackedMessages((prev) => ({ ...prev, [messageId]: isPositive }));
    try {
      await feedbackApi.create({
        feedbackType: "qa_feedback",
        module: "qa",
        title: isPositive ? "有帮助" : "无帮助",
        content: "",
        severity: isPositive ? "normal" : "high",
        relatedEntityId: messageId,
      });
      toast.success(isPositive ? "感谢您的反馈" : "已记录您的反馈");
    } catch {
      // 失败时回退状态
      setFeedbackedMessages((prev) => {
        const next = { ...prev };
        delete next[messageId];
        return next;
      });
      toast.error("反馈提交失败，请重试");
    }
  };

  return (
    <div className="flex h-[calc(100vh-4rem)]">
      {/* 左侧：会话历史侧边栏 */}
      <aside className="hidden w-[280px] shrink-0 border-r bg-white dark:bg-slate-900 md:block">
        <ConversationList
          topicId={topicId || undefined}
          selectedSessionId={sessionId}
          onSelectSession={handleSelectSession}
          onNewChat={handleNewChat}
        />
      </aside>

      {/* 右侧：聊天区 */}
      <div className="flex flex-1 flex-col">
        {/* 顶部栏 */}
        <div className="flex items-center justify-between border-b bg-white px-4 py-3 dark:bg-slate-900">
          <div className="flex items-center gap-3">
            <h1 className="text-lg font-bold">智能问答</h1>
            <Select value={topicId} onValueChange={(v) => setTopicId(v as string)}>
              <SelectTrigger size="sm" className="w-48">
                <SelectValue placeholder="选择专题" />
              </SelectTrigger>
              <SelectContent>
                {topics.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {messages.length > 0 && (
            <Button variant="outline" size="sm" onClick={handleNewChat}>
              新对话
            </Button>
          )}
        </div>

        {/* 消息流区域 */}
        <div className="flex-1 overflow-y-auto px-4 py-4">
          {messages.length === 0 ? (
            <div className="flex h-full flex-col items-center justify-center text-center">
              <div className="flex size-16 items-center justify-center rounded-2xl bg-gradient-to-br from-blue-500 to-purple-600">
                <MessageCircle className="size-8 text-white" />
              </div>
              <h2 className="mt-4 text-xl font-bold">知识问答助手</h2>
              <p className="mt-2 max-w-md text-sm text-muted-foreground">
                基于您的知识库进行智能问答。选择专题后，直接输入问题，
                系统将检索相关资料并生成带有引用来源的回答。
              </p>
              <div className="mt-6 grid gap-2 sm:grid-cols-2">
                {[
                  "最近有哪些重要趋势？",
                  "总结这个专题的核心观点",
                  "有哪些值得关注的风险？",
                  "有哪些商业机会？",
                ].map((suggestion) => (
                  <button
                    key={suggestion}
                    type="button"
                    onClick={() => {
                      setInput(suggestion);
                      inputRef.current?.focus();
                    }}
                    className="rounded-lg border bg-white px-4 py-2.5 text-left text-sm text-muted-foreground transition-colors hover:border-primary hover:text-foreground dark:bg-slate-900"
                  >
                    {suggestion}
                  </button>
                ))}
              </div>
            </div>
          ) : (
            <div className="mx-auto max-w-3xl space-y-6">
              {messages.map((msg) => (
                <MessageBubble
                  key={msg.id}
                  message={msg}
                  feedbackedMessages={feedbackedMessages}
                  onFeedback={handleFeedback}
                />
              ))}
              {loading && <LoadingMessage />}
              <div ref={messagesEndRef} />
            </div>
          )}
        </div>

        {/* 底部输入区 */}
        <div className="border-t bg-white px-4 py-3 dark:bg-slate-900">
          <div className="mx-auto flex max-w-3xl items-center gap-2">
            <Input
              ref={inputRef}
              placeholder={topicId ? "输入您的问题..." : "请先选择专题"}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={handleKeyDown}
              disabled={loading || !topicId}
              className="h-10 flex-1 text-base"
            />
            <Button
              size="lg"
              onClick={handleSend}
              disabled={loading || !input.trim() || !topicId}
            >
              {loading ? (
                <Loader2 className="size-4 animate-spin" />
              ) : (
                <Send className="size-4" />
              )}
            </Button>
          </div>
          <p className="mx-auto mt-2 max-w-3xl text-center text-xs text-muted-foreground">
            回答基于知识库资料检索生成，请结合引用来源核实关键信息
          </p>
        </div>
      </div>
    </div>
  );
}
