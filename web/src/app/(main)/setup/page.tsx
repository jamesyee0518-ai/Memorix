"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  BookOpen,
  Check,
  Clock,
  Cloud,
  Database,
  FolderOpen,
  HardDrive,
  Layers,
  Loader2,
  Radar,
  Sparkles,
} from "lucide-react";
import { ApiRequestError, runtimeApi, topicApi, workspaceApi } from "@/lib/api";
import type { WorkspaceMode, WorkspaceModeOption } from "@/lib/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { isDesktopApp, selectDesktopDirectory } from "@/lib/desktop";
import { MemorixBrand } from "@/components/brand/memorix-brand";

const modeIcons: Record<WorkspaceMode, typeof HardDrive> = {
  local: HardDrive,
  cloud: Cloud,
  hybrid: Layers,
};

const modeDescriptions: Record<WorkspaceMode, string> = {
  local: "数据完全存储在本地，隐私优先，适合个人知识管理",
  cloud: "数据存储在云端，随时随地访问，适合团队协作",
  hybrid: "本地与云端混合，兼顾隐私与便捷",
};

const modelProviderOptions = [
  { value: "lmstudio", label: "LM Studio" },
  { value: "ollama", label: "Ollama" },
  { value: "openai", label: "OpenAI" },
  { value: "anthropic", label: "Anthropic" },
  { value: "custom", label: "自定义" },
];

const steps = [
  { title: "选择模式", icon: Layers },
  { title: "存储位置", icon: Database },
  { title: "模型配置", icon: Sparkles },
  { title: "首个专题", icon: BookOpen },
];

export default function SetupPage() {
  const router = useRouter();
  const [modes, setModes] = useState<WorkspaceModeOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [currentStep, setCurrentStep] = useState(0);
  const [selectedMode, setSelectedMode] = useState<WorkspaceMode | null>(null);

  const [workspaceName, setWorkspaceName] = useState("");
  const [vaultPath, setVaultPath] = useState("");
  const [modelProvider, setModelProvider] = useState("lmstudio");
  const [topicName, setTopicName] = useState("默认知识库");
  const [topicDescription, setTopicDescription] =
    useState("用于收集和整理第一批知识资料。");
  const [submitting, setSubmitting] = useState(false);
  const [isDesktop, setIsDesktop] = useState(false);

  const [isDetecting, setIsDetecting] = useState(false);
  const [hasDetected, setHasDetected] = useState(false);
  const [detectedOllama, setDetectedOllama] = useState(false);
  const [detectedLmStudio, setDetectedLmStudio] = useState(false);

  useEffect(() => {
    setIsDesktop(isDesktopApp());
  }, []);

  useEffect(() => {
    const fetchModes = async () => {
      try {
        const data = await workspaceApi.getModes();
        setModes(data);
      } catch (err) {
        const message =
          err instanceof ApiRequestError ? err.message : "加载模式列表失败";
        toast.error(message);
      } finally {
        setIsLoading(false);
      }
    };
    fetchModes();
  }, []);

  const handleSelectVault = async () => {
    try {
      const selected = await selectDesktopDirectory(vaultPath);
      if (selected) setVaultPath(selected);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "选择目录失败");
    }
  };

  const displayModes: WorkspaceModeOption[] = useMemo(
    () =>
      modes.length > 0
        ? modes
        : (["local", "cloud", "hybrid"] as WorkspaceMode[]).map((m) => ({
            mode: m,
            label:
              m === "local"
                ? "本地模式"
                : m === "cloud"
                  ? "云端模式"
                  : "混合模式",
            description: modeDescriptions[m],
            available: true,
          })),
    [modes]
  );

  const handleSelectMode = (mode: WorkspaceMode) => {
    setSelectedMode(mode);
    if (!workspaceName) {
      if (mode === "local") setWorkspaceName("我的本地工作区");
      else if (mode === "cloud") setWorkspaceName("我的云端工作区");
      else setWorkspaceName("我的混合工作区");
    }
  };

  const handleDetectLocalModels = async () => {
    setIsDetecting(true);
    setHasDetected(false);
    setDetectedOllama(false);
    setDetectedLmStudio(false);

    try {
      const result = await runtimeApi.detectLocalModels();
      const ollamaOk = result.ollama.available;
      const lmStudioOk = result.lmStudio.available;

      setDetectedOllama(ollamaOk);
      setDetectedLmStudio(lmStudioOk);
      setHasDetected(true);

      if (ollamaOk) setModelProvider("ollama");
      else if (lmStudioOk) setModelProvider("lmstudio");
    } catch (err) {
      setDetectedOllama(false);
      setDetectedLmStudio(false);
      setHasDetected(true);
      const message =
        err instanceof ApiRequestError ? err.message : "本地模型检测失败";
      toast.error(message);
    } finally {
      setIsDetecting(false);
    }
  };

  const validateStep = () => {
    if (currentStep === 0 && !selectedMode) {
      toast.error("请选择工作区模式");
      return false;
    }
    if (currentStep === 1) {
      if (!workspaceName.trim()) {
        toast.error("请输入工作区名称");
        return false;
      }
      if (selectedMode === "local" && !vaultPath.trim()) {
        toast.error("请输入 Vault 路径");
        return false;
      }
    }
    if (currentStep === 3 && !topicName.trim()) {
      toast.error("请输入第一个专题名称");
      return false;
    }
    return true;
  };

  const handleNext = () => {
    if (!validateStep()) return;
    setCurrentStep((step) => Math.min(step + 1, steps.length - 1));
  };

  const handleBack = () => {
    setCurrentStep((step) => Math.max(step - 1, 0));
  };

  const handleCreate = async () => {
    if (!selectedMode || !validateStep()) return;

    setSubmitting(true);
    try {
      if (selectedMode === "local") {
        await workspaceApi.initLocal({
          name: workspaceName.trim(),
          vaultPath: vaultPath.trim(),
          modelProvider,
        });
      } else {
        await workspaceApi.create({
          name: workspaceName.trim(),
          mode: selectedMode,
          modelProvider,
        });
      }

      await topicApi.create({
        name: topicName.trim(),
        description: topicDescription.trim() || undefined,
      });

      toast.success("工作区已准备好");
      router.push("/dashboard");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "创建工作区失败";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="text-center">
        <MemorixBrand className="mx-auto mb-3" />
        <h1 className="text-2xl font-bold">欢迎使用 Memorix</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          按步骤完成本地优先工作区、模型和首个专题配置
        </p>
      </div>

      <div className="grid gap-3 sm:grid-cols-4">
        {steps.map((step, index) => {
          const Icon = step.icon;
          const active = index === currentStep;
          const done = index < currentStep;
          return (
            <div
              key={step.title}
              className={cn(
                "flex items-center gap-3 rounded-lg border bg-white px-3 py-2",
                active && "border-primary bg-primary/5",
                done && "border-emerald-200 bg-emerald-50"
              )}
            >
              <div
                className={cn(
                  "flex size-8 items-center justify-center rounded-full bg-muted text-muted-foreground",
                  active && "bg-primary text-primary-foreground",
                  done && "bg-emerald-600 text-white"
                )}
              >
                {done ? <Check className="size-4" /> : <Icon className="size-4" />}
              </div>
              <div className="min-w-0">
                <p className="text-xs text-muted-foreground">Step {index + 1}</p>
                <p className="truncate text-sm font-medium">{step.title}</p>
              </div>
            </div>
          );
        })}
      </div>

      {currentStep === 0 && (
        <div className="grid gap-4 sm:grid-cols-3">
          {displayModes.map((option) => {
            const Icon = modeIcons[option.mode] ?? HardDrive;
            const selected = selectedMode === option.mode;
            return (
              <Card
                key={option.mode}
                className={cn(
                  "cursor-pointer transition-all hover:shadow-md",
                  selected && "ring-2 ring-primary",
                  !option.available && "opacity-50"
                )}
                onClick={() => option.available && handleSelectMode(option.mode)}
              >
                <CardContent className="flex flex-col items-center gap-3 py-6 text-center">
                  <div
                    className={cn(
                      "flex size-12 items-center justify-center rounded-xl",
                      selected
                        ? "bg-primary/10 text-primary"
                        : "bg-muted text-muted-foreground"
                    )}
                  >
                    <Icon className="size-6" />
                  </div>
                  <div>
                    <h3 className="flex items-center justify-center gap-1.5 font-semibold">
                      {option.label}
                      {option.mode === "cloud" && (
                        <Badge className="border-amber-500/30 bg-amber-500/10 text-amber-600">
                          <Clock className="size-3" />
                          即将支持
                        </Badge>
                      )}
                      {selected && <Check className="size-4 text-primary" />}
                    </h3>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {option.description}
                    </p>
                  </div>
                  {!option.available && (
                    <span className="text-xs text-muted-foreground">
                      暂不可用
                    </span>
                  )}
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}

      {currentStep === 1 && selectedMode && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">配置工作区</CardTitle>
            <CardDescription>
              {selectedMode === "local"
                ? "本地模式会把数据库和文件保存在你指定的 Vault 路径。"
                : selectedMode === "cloud"
                  ? "云端模式会把数据保存到云端工作区。"
                  : "混合模式会以本地主库为核心，可选连接云端 Inbox。"}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {selectedMode === "cloud" && (
              <div className="flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-700">
                <AlertTriangle className="mt-0.5 size-4 shrink-0" />
                <span>云端模式即将支持，当前版本建议优先使用本地模式。</span>
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="setup-name">
                工作区名称 <span className="text-destructive">*</span>
              </Label>
              <Input
                id="setup-name"
                placeholder="请输入工作区名称"
                value={workspaceName}
                onChange={(e) => setWorkspaceName(e.target.value)}
                maxLength={50}
              />
            </div>

            {selectedMode === "local" && (
              <div className="space-y-2">
                <Label htmlFor="setup-vault">
                  <span className="flex items-center gap-1.5">
                    <FolderOpen className="size-3.5" />
                    Vault 路径 <span className="text-destructive">*</span>
                  </span>
                </Label>
                <div className="flex gap-2">
                  <Input
                    id="setup-vault"
                    placeholder="例如：/Users/username/Documents/MyVault"
                    value={vaultPath}
                    onChange={(e) => setVaultPath(e.target.value)}
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
                <p className="text-xs text-muted-foreground">
                  数据库、原始文件、导出内容会保存在此路径下。
                </p>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {currentStep === 2 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">配置模型</CardTitle>
            <CardDescription>
              本地数据可以搭配本地模型，也可以使用你自己的云端 API Key。
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {selectedMode === "local" && (
              <div className="space-y-3 rounded-lg border border-border/50 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <p className="text-sm font-medium">本地模型服务检测</p>
                    <p className="text-xs text-muted-foreground">
                      自动检测已运行的 Ollama 或 LM Studio 服务。
                    </p>
                  </div>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={handleDetectLocalModels}
                    disabled={isDetecting}
                  >
                    {isDetecting ? (
                      <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                    ) : (
                      <Radar className="mr-1.5 size-3.5" />
                    )}
                    {isDetecting ? "检测中..." : "检测本地模型服务"}
                  </Button>
                </div>

                {hasDetected && !isDetecting && (
                  <div className="space-y-2">
                    {detectedOllama && (
                      <Badge className="border-green-500/30 bg-green-500/10 text-green-600">
                        <Check className="size-3" />
                        Ollama 已检测到 (端口 11434)
                      </Badge>
                    )}
                    {detectedLmStudio && (
                      <Badge className="border-green-500/30 bg-green-500/10 text-green-600">
                        <Check className="size-3" />
                        LM Studio 已检测到 (端口 1234)
                      </Badge>
                    )}
                    {!detectedOllama && !detectedLmStudio && (
                      <div className="flex items-center gap-2 rounded-md bg-amber-500/10 px-3 py-2 text-sm text-amber-600">
                        <AlertTriangle className="size-4 shrink-0" />
                        <span>
                          未检测到本地模型服务，可稍后在设置中继续配置。
                        </span>
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}

            <div className="space-y-2">
              <Label>模型提供者</Label>
              <Select
                value={modelProvider}
                onValueChange={(v) => setModelProvider(v as string)}
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
              <p className="text-xs text-muted-foreground">
                后续可在模型配置页修改模型地址、API Key、Chat 模型和 Embedding 模型。
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {currentStep === 3 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">创建第一个专题</CardTitle>
            <CardDescription>
              专题用于组织资料、限定问答范围，也是后续报告生成的基础。
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="setup-topic-name">
                专题名称 <span className="text-destructive">*</span>
              </Label>
              <Input
                id="setup-topic-name"
                placeholder="例如：AI 产品研究"
                value={topicName}
                onChange={(e) => setTopicName(e.target.value)}
                maxLength={50}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="setup-topic-description">专题说明</Label>
              <Input
                id="setup-topic-description"
                placeholder="这个专题用来收集什么资料？"
                value={topicDescription}
                onChange={(e) => setTopicDescription(e.target.value)}
                maxLength={200}
              />
            </div>
          </CardContent>
        </Card>
      )}

      <div className="flex justify-between gap-2">
        <Button
          variant="outline"
          onClick={handleBack}
          disabled={currentStep === 0 || submitting}
        >
          <ArrowLeft className="mr-2 size-4" />
          上一步
        </Button>
        {currentStep < steps.length - 1 ? (
          <Button onClick={handleNext}>
            下一步
            <ArrowRight className="ml-2 size-4" />
          </Button>
        ) : (
          <Button size="lg" onClick={handleCreate} disabled={submitting}>
            {submitting ? (
              <Loader2 className="mr-2 size-4 animate-spin" />
            ) : (
              <Check className="mr-2 size-4" />
            )}
            {submitting ? "创建中..." : "创建并进入 Dashboard"}
          </Button>
        )}
      </div>
    </div>
  );
}
