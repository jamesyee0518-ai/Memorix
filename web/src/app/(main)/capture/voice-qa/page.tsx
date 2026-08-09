"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { toast } from "sonner";
import {
  ArrowLeft,
  Send,
  Loader2,
  Mic,
  MicOff,
  Brain,
  User as UserIcon,
  MessageCircle,
  Volume2,
} from "lucide-react";
import { qaApi, ApiRequestError } from "@/lib/api";
import { useTopicStore } from "@/stores/topic-store";
import type { QaSession, Citation } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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

// ===== Types =====

interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  citations?: Citation[];
  isInsufficient?: boolean;
  isError?: boolean;
}

// Minimal SpeechRecognition type declarations for the Web Speech API
interface SpeechRecognitionAlternative {
  transcript: string;
  confidence: number;
}

interface SpeechRecognitionResult {
  isFinal: boolean;
  length: number;
  item(index: number): SpeechRecognitionAlternative;
  [index: number]: SpeechRecognitionAlternative;
}

interface SpeechRecognitionResultList {
  length: number;
  item(index: number): SpeechRecognitionResult;
  [index: number]: SpeechRecognitionResult;
}

interface SpeechRecognitionEvent extends Event {
  resultIndex: number;
  results: SpeechRecognitionResultList;
}

interface SpeechRecognitionErrorEvent extends Event {
  error: string;
  message?: string;
}

interface SpeechRecognitionInstance extends EventTarget {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  maxAlternatives: number;
  start(): void;
  stop(): void;
  abort(): void;
  onstart: ((this: SpeechRecognitionInstance, ev: Event) => void) | null;
  onend: ((this: SpeechRecognitionInstance, ev: Event) => void) | null;
  onerror:
    | ((
        this: SpeechRecognitionInstance,
        ev: SpeechRecognitionErrorEvent,
      ) => void)
    | null;
  onresult:
    | ((
        this: SpeechRecognitionInstance,
        ev: SpeechRecognitionEvent,
      ) => void)
    | null;
}

type SpeechRecognitionConstructor = new () => SpeechRecognitionInstance;

declare global {
  interface Window {
    SpeechRecognition?: SpeechRecognitionConstructor;
    webkitSpeechRecognition?: SpeechRecognitionConstructor;
  }
}

// ===== Main Component =====

export default function VoiceQaPage() {
  const { topics, fetchTopics } = useTopicStore();

  const [topicId, setTopicId] = useState("");
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [isListening, setIsListening] = useState(false);
  const [interimText, setInterimText] = useState("");

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const recognitionRef = useRef<SpeechRecognitionInstance | null>(null);
  const shouldSendOnEndRef = useRef(false);
  const handleSendRef = useRef<() => void>(() => {});

  useEffect(() => {
    fetchTopics().catch(() => {});
  }, [fetchTopics]);

  // Auto-select first topic
  useEffect(() => {
    if (topics.length > 0 && !topicId) {
      setTopicId(topics[0].id);
    }
  }, [topics, topicId]);

  // Auto-scroll to bottom
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading]);

  // ===== Speech Recognition =====

  const getSpeechRecognition = useCallback(():
    | SpeechRecognitionConstructor
    | null => {
    if (typeof window === "undefined") return null;
    return window.SpeechRecognition ?? window.webkitSpeechRecognition ?? null;
  }, []);

  const startListening = useCallback(() => {
    const SpeechRecognitionCtor = getSpeechRecognition();
    if (!SpeechRecognitionCtor) {
      toast.error("当前浏览器不支持语音识别，请使用 Chrome 或 Edge");
      return;
    }

    // Stop existing recognition if running
    if (recognitionRef.current) {
      recognitionRef.current.abort();
      recognitionRef.current = null;
    }

    const recognition = new SpeechRecognitionCtor();
    recognition.lang = "zh-CN";
    recognition.continuous = false;
    recognition.interimResults = true;
    recognition.maxAlternatives = 1;

    recognition.onstart = () => {
      setIsListening(true);
      setInterimText("");
    };

    recognition.onresult = (event: SpeechRecognitionEvent) => {
      let finalTranscript = "";
      let interimTranscript = "";

      for (let i = event.resultIndex; i < event.results.length; i++) {
        const result = event.results[i];
        const transcript = result.item(0).transcript;
        if (result.isFinal) {
          finalTranscript += transcript;
        } else {
          interimTranscript += transcript;
        }
      }

      if (interimTranscript) {
        setInterimText(interimTranscript);
      }

      if (finalTranscript) {
        setInterimText("");
        const trimmed = finalTranscript.trim();
        if (trimmed) {
          setInput(trimmed);
          shouldSendOnEndRef.current = true;
        }
      }
    };

    recognition.onerror = (event: SpeechRecognitionErrorEvent) => {
      setIsListening(false);
      setInterimText("");
      if (event.error !== "aborted" && event.error !== "no-speech") {
        toast.error(`语音识别错误：${event.error}`);
      }
    };

    recognition.onend = () => {
      setIsListening(false);
      setInterimText("");

      // Auto-send if we got final results
      if (shouldSendOnEndRef.current) {
        shouldSendOnEndRef.current = false;
        // Use setTimeout to ensure input state is updated before sending
        setTimeout(() => {
          handleSendRef.current();
        }, 100);
      }
    };

    recognitionRef.current = recognition;

    try {
      recognition.start();
    } catch {
      toast.error("无法启动语音识别，请重试");
      setIsListening(false);
    }
  }, [getSpeechRecognition]);

  const stopListening = useCallback(() => {
    if (recognitionRef.current) {
      recognitionRef.current.stop();
      setIsListening(false);
    }
  }, []);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (recognitionRef.current) {
        recognitionRef.current.abort();
      }
    };
  }, []);

  // ===== QA Logic =====

  const handleSend = useCallback(async () => {
    const question = input.trim();
    if (!question) return;
    if (!topicId) {
      toast.error("请先选择专题");
      return;
    }
    if (loading) return;

    // Add user message
    const userMsg: ChatMessage = {
      id: `user-${Date.now()}`,
      role: "user",
      content: question,
    };
    setMessages((prev) => [...prev, userMsg]);
    setInput("");
    setLoading(true);

    try {
      // Create session on first question
      let currentSessionId = sessionId;
      if (!currentSessionId) {
        const session: QaSession = await qaApi.createSession({
          topicId,
          title: question.slice(0, 50),
        });
        currentSessionId = session.id;
        setSessionId(session.id);
      }

      // Ask the question
      const response = await qaApi.ask({
        sessionId: currentSessionId,
        topicId,
        query: question,
        retrieval: { searchType: "hybrid", topK: 10 },
      });

      const isInsufficient =
        response.citations.length === 0 &&
        response.retrieval.usedCount === 0;

      const assistantMsg: ChatMessage = {
        id: response.messageId,
        role: "assistant",
        content: response.answer,
        citations: response.citations,
        isInsufficient,
      };
      setMessages((prev) => [...prev, assistantMsg]);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "回答生成失败，请重试";
      const errorMsg: ChatMessage = {
        id: `error-${Date.now()}`,
        role: "assistant",
        content: `处理您的问题时出现错误：${message}`,
        isError: true,
      };
      setMessages((prev) => [...prev, errorMsg]);
    } finally {
      setLoading(false);
      inputRef.current?.focus();
    }
  }, [input, topicId, sessionId, loading]);

  // Keep the ref in sync so the speech recognition callback always calls the latest handleSend
  useEffect(() => {
    handleSendRef.current = handleSend;
  }, [handleSend]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleNewChat = () => {
    setSessionId(null);
    setMessages([]);
    setInput("");
    setLoading(false);
    inputRef.current?.focus();
  };

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      {/* Header */}
      <div>
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" render={<Link href="/capture" />}>
            <ArrowLeft className="mr-1.5 size-4" />
            采集中心
          </Button>
        </div>
        <h1 className="mt-2 text-2xl font-bold">语音问答</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          通过语音或文字输入进行知识库问答，支持实时语音识别
        </p>
      </div>

      <div className="flex h-[calc(100vh-16rem)] flex-col">
        {/* Top bar with topic selector */}
        <div className="mb-3 flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <span className="text-sm text-muted-foreground">专题：</span>
            <Select
              value={topicId}
              onValueChange={(v) => setTopicId(v as string)}
            >
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

        {/* Messages area */}
        <div className="flex-1 overflow-y-auto rounded-xl border bg-white dark:bg-slate-900">
          {messages.length === 0 ? (
            <div className="flex h-full flex-col items-center justify-center px-6 text-center">
              <div className="flex size-16 items-center justify-center rounded-2xl bg-gradient-to-br from-rose-500 to-pink-600">
                <MessageCircle className="size-8 text-white" />
              </div>
              <h2 className="mt-4 text-xl font-bold">语音问答助手</h2>
              <p className="mt-2 max-w-md text-sm text-muted-foreground">
                点击麦克风按钮用语音提问，或直接输入文字。系统将基于知识库检索并生成回答。
              </p>
              <div className="mt-6 flex items-center gap-2 text-xs text-muted-foreground">
                <Mic className="size-3.5" />
                <span>支持语音输入</span>
                <span className="mx-1">·</span>
                <Volume2 className="size-3.5" />
                <span>基于知识库检索</span>
              </div>
            </div>
          ) : (
            <div className="space-y-4 p-4">
              {messages.map((msg) => (
                <MessageBubble key={msg.id} message={msg} />
              ))}
              {loading && <LoadingMessage />}
              <div ref={messagesEndRef} />
            </div>
          )}
        </div>

        {/* Input area */}
        <div className="mt-3 flex items-center gap-2">
          <Input
            ref={inputRef}
            value={isListening ? interimText || input : input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={
              isListening
                ? "正在聆听..."
                : topicId
                  ? "输入问题，或点击麦克风语音输入..."
                  : "请先选择专题"
            }
            disabled={loading || !topicId}
            className="flex-1"
          />
          <Button
            variant={isListening ? "destructive" : "outline"}
            size="icon"
            onClick={isListening ? stopListening : startListening}
            disabled={loading || !topicId}
            title={isListening ? "停止语音输入" : "开始语音输入"}
          >
            {isListening ? (
              <MicOff className="size-4" />
            ) : (
              <Mic className="size-4" />
            )}
          </Button>
          <Button
            size="icon"
            onClick={handleSend}
            disabled={loading || !input.trim() || !topicId}
            title="发送"
          >
            {loading ? (
              <Loader2 className="size-4 animate-spin" />
            ) : (
              <Send className="size-4" />
            )}
          </Button>
        </div>

        <p className="mt-2 text-center text-xs text-muted-foreground">
          回答基于知识库资料检索生成，请结合引用来源核实关键信息
        </p>
      </div>
    </div>
  );
}

// ===== Message Bubble =====

function MessageBubble({ message }: { message: ChatMessage }) {
  const isUser = message.role === "user";

  if (isUser) {
    return (
      <div className="flex justify-end">
        <div className="flex max-w-[75%] items-start gap-3">
          <div className="rounded-2xl rounded-tr-sm bg-primary px-4 py-2.5 text-sm text-primary-foreground">
            <p className="whitespace-pre-wrap leading-relaxed">
              {message.content}
            </p>
          </div>
          <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-primary/10">
            <UserIcon className="size-4 text-primary" />
          </div>
        </div>
      </div>
    );
  }

  // Assistant message
  return (
    <div className="flex justify-start">
      <div className="flex max-w-[85%] items-start gap-3">
        <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-blue-500 to-purple-600">
          <Brain className="size-4 text-white" />
        </div>
        <div className="min-w-0 flex-1 space-y-2">
          {message.isError ? (
            <div className="rounded-2xl rounded-tl-sm border border-red-200 bg-red-50 px-4 py-3 dark:border-red-900 dark:bg-red-950/40">
              <p className="text-sm text-red-700 dark:text-red-300">
                {message.content}
              </p>
            </div>
          ) : message.isInsufficient ? (
            <div className="rounded-2xl rounded-tl-sm border border-amber-200 bg-amber-50 px-4 py-3 dark:border-amber-800 dark:bg-amber-950/30">
              <p className="text-sm font-medium text-amber-800 dark:text-amber-200">
                资料不足
              </p>
              <p className="mt-1 text-sm text-amber-700 dark:text-amber-300">
                {message.content}
              </p>
            </div>
          ) : (
            <div className="rounded-2xl rounded-tl-sm border bg-white px-4 py-3 dark:bg-slate-900">
              <p className="whitespace-pre-wrap text-sm leading-relaxed">
                {message.content}
              </p>
            </div>
          )}

          {/* Citations */}
          {message.citations && message.citations.length > 0 && !message.isInsufficient && (
            <div className="space-y-1.5">
              <p className="text-xs font-medium text-muted-foreground">
                引用来源
              </p>
              {message.citations.map((citation, idx) => (
                <div
                  key={`${citation.documentId}-${idx}`}
                  className="rounded-lg border bg-muted/30 p-2.5 text-xs"
                >
                  <div className="flex items-start gap-2">
                    <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-primary text-[10px] font-bold text-primary-foreground">
                      {citation.index}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-medium text-primary">
                        {citation.titleZh || citation.title || "查看文档"}
                      </p>
                      <p className="mt-0.5 line-clamp-2 text-muted-foreground">
                        {citation.displaySnippet || citation.snippet}
                      </p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ===== Loading Message =====

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
