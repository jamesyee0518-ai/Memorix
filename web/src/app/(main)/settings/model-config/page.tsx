"use client";

import { useEffect, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  Cpu,
  Loader2,
  Save,
  RefreshCw,
  PlugZap,
  CheckCircle2,
  XCircle,
  Eye,
  EyeOff,
} from "lucide-react";
import { workspaceApi, ApiRequestError } from "@/lib/api";
import type {
  Workspace,
  ModelProviderOption,
  UpdateModelSettingsInput,
  ModelTestResult,
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

/** 从 modelConfig 字符串中解析已有的模型设置 */
function parseModelConfig(
  config?: string
): Partial<UpdateModelSettingsInput> {
  if (!config) return {};
  try {
    return JSON.parse(config) as Partial<UpdateModelSettingsInput>;
  } catch {
    return {};
  }
}

export default function ModelConfigPage() {
  const router = useRouter();
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [providers, setProviders] = useState<ModelProviderOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [showApiKey, setShowApiKey] = useState(false);

  // 表单状态
  const [provider, setProvider] = useState("");
  const [baseUrl, setBaseUrl] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [chatModel, setChatModel] = useState("");
  const [embeddingModel, setEmbeddingModel] = useState("");

  // 测试结果
  const [testResult, setTestResult] = useState<ModelTestResult | null>(null);

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
        const existing = parseModelConfig(ws.modelConfig);
        setProvider(existing.provider ?? ws.modelProvider ?? "");
        setBaseUrl(existing.baseUrl ?? "");
        setApiKey(existing.apiKey ?? "");
        setChatModel(existing.chatModel ?? "");
        setEmbeddingModel(existing.embeddingModel ?? "");
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
    fetchWorkspace();
  }, [fetchWorkspace]);

  // 选择提供者时自动填充默认 Base URL
  const handleProviderChange = (value: string) => {
    setProvider(value);
    const option = providers.find((p) => p.provider === value);
    if (option?.defaultBaseUrl && !baseUrl) {
      setBaseUrl(option.defaultBaseUrl);
    }
  };

  const handleSave = async () => {
    if (!workspace) return;
    if (!provider) {
      toast.error("请选择模型提供者");
      return;
    }
    setSaving(true);
    try {
      const data: UpdateModelSettingsInput = {
        provider,
        baseUrl: baseUrl.trim() || undefined,
        apiKey: apiKey.trim() || undefined,
        chatModel: chatModel.trim() || undefined,
        embeddingModel: embeddingModel.trim() || undefined,
      };
      const updated = await workspaceApi.updateModelSettings(
        workspace.id,
        data
      );
      setWorkspace(updated);
      toast.success("模型配置已保存");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "保存模型配置失败";
      toast.error(message);
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    if (!workspace) return;
    setTesting(true);
    setTestResult(null);
    try {
      const result = await workspaceApi.testModel(workspace.id);
      setTestResult(result);
      if (result.status === "ok" || result.status === "success") {
        toast.success("模型连接测试成功");
      } else {
        toast.error("模型连接测试失败");
      }
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "测试连接失败";
      toast.error(message);
      setTestResult({
        status: "error",
        provider,
        chatModel,
        embeddingModel,
        error: message,
      });
    } finally {
      setTesting(false);
    }
  };

  // 提供者选项
  const providerOptions =
    providers.length > 0
      ? providers.map((p) => ({
          value: p.provider,
          label: p.label,
        }))
      : [
          { value: "lmstudio", label: "LM Studio" },
          { value: "ollama", label: "Ollama" },
          { value: "openai", label: "OpenAI" },
          { value: "anthropic", label: "Anthropic" },
          { value: "custom", label: "自定义" },
        ];

  const selectedProvider = providers.find((p) => p.provider === provider);
  const requiresApiKey = selectedProvider?.requiresApiKey ?? true;

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
          <h2 className="text-lg font-semibold">模型配置</h2>
          <p className="text-sm text-muted-foreground">
            配置工作区使用的 AI 模型提供者与参数
          </p>
        </div>
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Cpu className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无工作区</p>
            <p className="mt-1 text-sm text-muted-foreground">
              您还没有创建工作区，请先完成初始化设置
            </p>
            <Button className="mt-4" onClick={() => router.push("/setup")}>
              <Cpu className="mr-2 size-4" />
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
          <h2 className="text-lg font-semibold">模型配置</h2>
          <p className="text-sm text-muted-foreground">
            配置工作区使用的 AI 模型提供者与参数
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={fetchWorkspace}>
          <RefreshCw className="mr-2 size-4" />
          刷新
        </Button>
      </div>

      {/* 工作区信息 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Cpu className="size-4 text-primary" />
            当前工作区
          </CardTitle>
          <CardDescription>模型配置将应用到以下工作区</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center justify-between border-b border-border/50 py-2.5 last:border-0">
            <span className="text-sm text-muted-foreground">工作区名称</span>
            <span className="text-sm font-medium">{workspace.name}</span>
          </div>
          <div className="flex items-center justify-between border-b border-border/50 py-2.5 last:border-0">
            <span className="text-sm text-muted-foreground">运行模式</span>
            <Badge variant="secondary">
              {workspace.mode === "local"
                ? "本地模式"
                : workspace.mode === "cloud"
                  ? "云端模式"
                  : "混合模式"}
            </Badge>
          </div>
          <div className="flex items-center justify-between py-2.5">
            <span className="text-sm text-muted-foreground">当前提供者</span>
            <span className="text-sm font-medium">
              {workspace.modelProvider || "-"}
            </span>
          </div>
        </CardContent>
      </Card>

      {/* 模型配置表单 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <PlugZap className="size-4 text-primary" />
            模型参数
          </CardTitle>
          <CardDescription>
            选择模型提供者并填写连接参数
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* 模型提供者 */}
          <div className="space-y-2">
            <Label htmlFor="provider">
              模型提供者 <span className="text-destructive">*</span>
            </Label>
            <Select value={provider} onValueChange={(v) => handleProviderChange(v as string)}>
              <SelectTrigger id="provider" className="w-full">
                <SelectValue placeholder="请选择模型提供者" />
              </SelectTrigger>
              <SelectContent>
                {providerOptions.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* Base URL */}
          <div className="space-y-2">
            <Label htmlFor="base-url">Base URL</Label>
            <Input
              id="base-url"
              value={baseUrl}
              onChange={(e) => setBaseUrl(e.target.value)}
              placeholder="例如：http://localhost:1234/v1"
            />
            <p className="text-xs text-muted-foreground">
              模型服务的 API 基础地址，本地服务通常以 /v1 结尾
            </p>
          </div>

          {/* API Key */}
          {requiresApiKey && (
            <div className="space-y-2">
              <Label htmlFor="api-key">API Key</Label>
              <div className="relative">
                <Input
                  id="api-key"
                  type={showApiKey ? "text" : "password"}
                  value={apiKey}
                  onChange={(e) => setApiKey(e.target.value)}
                  placeholder="请输入 API Key"
                  className="pr-10"
                />
                <button
                  type="button"
                  onClick={() => setShowApiKey(!showApiKey)}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                >
                  {showApiKey ? (
                    <EyeOff className="size-4" />
                  ) : (
                    <Eye className="size-4" />
                  )}
                </button>
              </div>
              <p className="text-xs text-muted-foreground">
                用于访问模型服务的密钥，本地服务通常无需填写
              </p>
            </div>
          )}

          {/* Chat 模型 */}
          <div className="space-y-2">
            <Label htmlFor="chat-model">Chat 模型名称</Label>
            <Input
              id="chat-model"
              value={chatModel}
              onChange={(e) => setChatModel(e.target.value)}
              placeholder="例如：gpt-4o、qwen2.5-7b-instruct"
            />
            <p className="text-xs text-muted-foreground">
              用于对话、摘要、分析等任务的模型
            </p>
          </div>

          {/* Embedding 模型 */}
          <div className="space-y-2">
            <Label htmlFor="embedding-model">Embedding 模型名称</Label>
            <Input
              id="embedding-model"
              value={embeddingModel}
              onChange={(e) => setEmbeddingModel(e.target.value)}
              placeholder="例如：text-embedding-3-small、bge-m3"
            />
            <p className="text-xs text-muted-foreground">
              用于向量化与语义检索的嵌入模型
            </p>
          </div>
        </CardContent>
      </Card>

      {/* 测试连接结果 */}
      {testResult && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              {testResult.status === "ok" ||
              testResult.status === "success" ? (
                <CheckCircle2 className="size-4 text-green-600" />
              ) : (
                <XCircle className="size-4 text-red-600" />
              )}
              连接测试结果
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-1">
            <div className="flex items-center justify-between border-b border-border/50 py-2.5 last:border-0">
              <span className="text-sm text-muted-foreground">状态</span>
              <Badge
                className={cn(
                  testResult.status === "ok" ||
                    testResult.status === "success"
                    ? "bg-green-100 text-green-700"
                    : "bg-red-100 text-red-700"
                )}
              >
                {testResult.status === "ok" ||
                testResult.status === "success"
                  ? "成功"
                  : "失败"}
              </Badge>
            </div>
            <div className="flex items-center justify-between border-b border-border/50 py-2.5 last:border-0">
              <span className="text-sm text-muted-foreground">提供者</span>
              <span className="text-sm font-medium">
                {testResult.provider || "-"}
              </span>
            </div>
            {testResult.chatModel && (
              <div className="flex items-center justify-between border-b border-border/50 py-2.5 last:border-0">
                <span className="text-sm text-muted-foreground">
                  Chat 模型
                </span>
                <span className="text-sm font-medium">
                  {testResult.chatModel}
                </span>
              </div>
            )}
            {testResult.embeddingModel && (
              <div className="flex items-center justify-between border-b border-border/50 py-2.5 last:border-0">
                <span className="text-sm text-muted-foreground">
                  Embedding 模型
                </span>
                <span className="text-sm font-medium">
                  {testResult.embeddingModel}
                </span>
              </div>
            )}
            {testResult.error && (
              <div className="mt-2 rounded-lg border border-red-200 bg-red-50 p-3">
                <p className="text-sm text-red-700">
                  {testResult.error}
                </p>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {/* 操作按钮 */}
      <div className="flex justify-end gap-2">
        <Button
          variant="outline"
          onClick={handleTest}
          disabled={testing || saving}
        >
          {testing ? (
            <Loader2 className="mr-2 size-4 animate-spin" />
          ) : (
            <PlugZap className="mr-2 size-4" />
          )}
          测试连接
        </Button>
        <Button onClick={handleSave} disabled={saving || testing}>
          {saving ? (
            <Loader2 className="mr-2 size-4 animate-spin" />
          ) : (
            <Save className="mr-2 size-4" />
          )}
          保存配置
        </Button>
      </div>
    </div>
  );
}
