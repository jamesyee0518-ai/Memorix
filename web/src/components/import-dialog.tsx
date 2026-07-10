"use client";

import { useEffect, useState, useCallback, useRef } from "react";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import {
  Loader2,
  Upload,
  Link2,
  FileText,
  FileType,
  X,
  CheckCircle2,
} from "lucide-react";
import { useTopicStore } from "@/stores/topic-store";
import { sourceApi, inboxApi, ApiRequestError } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";

// ===== 表单 Schema =====

const urlSchema = z.object({
  topicId: z.string().optional(),
  url: z.string().url("请输入有效的 URL"),
  title: z.string().optional(),
});
type UrlForm = z.infer<typeof urlSchema>;

const textSchema = z.object({
  topicId: z.string().optional(),
  title: z.string().min(1, "请输入标题"),
  content: z.string().min(1, "请输入正文内容"),
});
type TextForm = z.infer<typeof textSchema>;

const fileSchema = z.object({
  topicId: z.string().optional(),
  title: z.string().optional(),
});
type FileForm = z.infer<typeof fileSchema>;

// ===== 导入模式 =====

type ImportMode = "direct" | "inbox";

// ===== 导入弹窗组件 =====

interface ImportDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultTopicId?: string;
  onSuccess?: () => void;
}

export function ImportDialog({
  open,
  onOpenChange,
  defaultTopicId,
  onSuccess,
}: ImportDialogProps) {
  const { topics, fetchTopics } = useTopicStore();
  const [activeTab, setActiveTab] = useState<string>("url");
  const [submitting, setSubmitting] = useState(false);
  const [mode, setMode] = useState<ImportMode>("direct");

  // 文件上传状态
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // 加载专题列表
  useEffect(() => {
    if (open && topics.length === 0) {
      fetchTopics().catch(() => {});
    }
  }, [open, topics.length, fetchTopics]);

  // 重置状态
  useEffect(() => {
    if (open) {
      setSelectedFile(null);
      setDragOver(false);
    }
  }, [open]);

  // URL 表单
  const urlForm = useForm<UrlForm>({
    resolver: zodResolver(urlSchema),
    defaultValues: { topicId: defaultTopicId ?? "", url: "", title: "" },
  });

  // 文本表单
  const textForm = useForm<TextForm>({
    resolver: zodResolver(textSchema),
    defaultValues: { topicId: defaultTopicId ?? "", title: "", content: "" },
  });

  // 文件表单
  const fileForm = useForm<FileForm>({
    resolver: zodResolver(fileSchema),
    defaultValues: { topicId: defaultTopicId ?? "", title: "" },
  });
  const urlTopicId = useWatch({ control: urlForm.control, name: "topicId" });
  const textTopicId = useWatch({ control: textForm.control, name: "topicId" });
  const fileTopicId = useWatch({ control: fileForm.control, name: "topicId" });

  // 当 defaultTopicId 变化时更新表单
  useEffect(() => {
    if (defaultTopicId) {
      urlForm.setValue("topicId", defaultTopicId);
      textForm.setValue("topicId", defaultTopicId);
      fileForm.setValue("topicId", defaultTopicId);
    }
  }, [defaultTopicId, urlForm, textForm, fileForm]);

  // ===== 文件处理 =====

  const handleFileSelect = useCallback((file: File | undefined) => {
    if (!file) return;
    if (file.type !== "application/pdf" && !file.name.toLowerCase().endsWith(".pdf")) {
      toast.error("仅支持 PDF 文件");
      return;
    }
    if (file.size > 50 * 1024 * 1024) {
      toast.error("文件大小不能超过 50MB");
      return;
    }
    setSelectedFile(file);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setDragOver(false);
      const file = e.dataTransfer.files?.[0];
      handleFileSelect(file);
    },
    [handleFileSelect]
  );

  const handleFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    handleFileSelect(e.target.files?.[0]);
  };

  // ===== 提交处理 =====

  const handleSuccess = () => {
    toast.success(mode === "inbox" ? "已存入收件箱" : "导入成功");
    onOpenChange(false);
    onSuccess?.();
  };

  const handleError = (err: unknown) => {
    if (err instanceof ApiRequestError) {
      if (err.code === "DUPLICATE" || err.status === 409) {
        toast.error("该资料已存在，请勿重复导入");
      } else {
        toast.error(err.message);
      }
    } else {
      toast.error(mode === "inbox" ? "存入收件箱失败，请重试" : "导入失败，请重试");
    }
  };

  const onUrlSubmit = async (data: UrlForm) => {
    if (mode === "direct" && !data.topicId) {
      toast.error("请选择专题");
      return;
    }
    setSubmitting(true);
    try {
      if (mode === "inbox") {
        await inboxApi.createUrl({
          sourceUrl: data.url,
          title: data.title || undefined,
          topicId: data.topicId || undefined,
        });
      } else {
        await sourceApi.importUrl({
          topicId: data.topicId!,
          url: data.url,
          title: data.title || undefined,
        });
      }
      handleSuccess();
    } catch (err) {
      handleError(err);
    } finally {
      setSubmitting(false);
    }
  };

  const onTextSubmit = async (data: TextForm) => {
    if (mode === "direct" && !data.topicId) {
      toast.error("请选择专题");
      return;
    }
    setSubmitting(true);
    try {
      if (mode === "inbox") {
        await inboxApi.createText({
          title: data.title,
          contentText: data.content,
          topicId: data.topicId || undefined,
        });
      } else {
        await sourceApi.importText({
          topicId: data.topicId!,
          title: data.title,
          content: data.content,
        });
      }
      handleSuccess();
    } catch (err) {
      handleError(err);
    } finally {
      setSubmitting(false);
    }
  };

  const onFileSubmit = async (data: FileForm) => {
    if (!selectedFile) {
      toast.error("请选择 PDF 文件");
      return;
    }
    if (mode === "direct" && !data.topicId) {
      toast.error("请选择专题");
      return;
    }
    setSubmitting(true);
    try {
      if (mode === "inbox") {
        await inboxApi.upload(selectedFile, data.topicId || undefined);
      } else {
        await sourceApi.importFile(
          data.topicId!,
          selectedFile,
          data.title || undefined
        );
      }
      handleSuccess();
    } catch (err) {
      handleError(err);
    } finally {
      setSubmitting(false);
    }
  };

  // ===== 专题选择器 =====

  const TopicSelect = ({
    value,
    onChange,
    error,
  }: {
    value?: string;
    onChange: (v: string) => void;
    error?: string;
  }) => (
    <div className="space-y-2">
      <Label>
        所属专题
        {mode === "inbox" && (
          <span className="ml-1 text-xs text-muted-foreground">（可选）</span>
        )}
      </Label>
      <Select value={value ?? ""} onValueChange={(v) => onChange(v as string)}>
        <SelectTrigger className="w-full">
          <SelectValue
            placeholder={
              mode === "inbox" ? "稍后在收件箱中分类" : "请选择专题"
            }
          />
        </SelectTrigger>
        <SelectContent>
          {topics.map((t) => (
            <SelectItem key={t.id} value={t.id}>
              {t.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>导入资料</DialogTitle>
          <DialogDescription>
            支持 URL 链接、文本内容和 PDF 文件三种导入方式
          </DialogDescription>
        </DialogHeader>

        {/* 导入模式切换 */}
        <div className="flex items-center gap-2 rounded-lg border bg-muted/40 p-1">
          <button
            type="button"
            onClick={() => setMode("direct")}
            className={cn(
              "flex-1 rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
              mode === "direct"
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground"
            )}
          >
            直接导入
          </button>
          <button
            type="button"
            onClick={() => setMode("inbox")}
            className={cn(
              "flex-1 rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
              mode === "inbox"
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground"
            )}
          >
            存入收件箱
          </button>
        </div>
        {mode === "inbox" && (
          <p className="text-xs text-muted-foreground">
            存入收件箱后，可在收件箱页面稍后分类和导入
          </p>
        )}

        <Tabs
          value={activeTab}
          onValueChange={(v) => setActiveTab(v as string)}
        >
          <TabsList className="w-full">
            <TabsTrigger value="url" className="flex-1">
              <Link2 className="mr-1 size-3.5" />
              URL
            </TabsTrigger>
            <TabsTrigger value="text" className="flex-1">
              <FileText className="mr-1 size-3.5" />
              文本
            </TabsTrigger>
            <TabsTrigger value="file" className="flex-1">
              <FileType className="mr-1 size-3.5" />
              PDF
            </TabsTrigger>
          </TabsList>

          {/* URL Tab */}
          <TabsContent value="url" className="mt-4">
            <form
              onSubmit={urlForm.handleSubmit(onUrlSubmit)}
              className="space-y-4"
            >
              <TopicSelect
                value={urlTopicId}
                onChange={(v) => urlForm.setValue("topicId", v)}
                error={urlForm.formState.errors.topicId?.message}
              />
              <div className="space-y-2">
                <Label htmlFor="url-input">URL 地址</Label>
                <Input
                  id="url-input"
                  placeholder="https://example.com/article"
                  {...urlForm.register("url")}
                />
                {urlForm.formState.errors.url && (
                  <p className="text-xs text-destructive">
                    {urlForm.formState.errors.url.message}
                  </p>
                )}
              </div>
              <div className="space-y-2">
                <Label htmlFor="url-title">标题（可选）</Label>
                <Input
                  id="url-title"
                  placeholder="自定义标题"
                  {...urlForm.register("title")}
                />
              </div>
              <Button
                type="submit"
                className="w-full"
                disabled={submitting}
              >
                {submitting && <Loader2 className="mr-2 size-4 animate-spin" />}
                {mode === "inbox" ? "存入收件箱" : "导入 URL"}
              </Button>
            </form>
          </TabsContent>

          {/* 文本 Tab */}
          <TabsContent value="text" className="mt-4">
            <form
              onSubmit={textForm.handleSubmit(onTextSubmit)}
              className="space-y-4"
            >
              <TopicSelect
                value={textTopicId}
                onChange={(v) => textForm.setValue("topicId", v)}
                error={textForm.formState.errors.topicId?.message}
              />
              <div className="space-y-2">
                <Label htmlFor="text-title">标题</Label>
                <Input
                  id="text-title"
                  placeholder="请输入标题"
                  {...textForm.register("title")}
                />
                {textForm.formState.errors.title && (
                  <p className="text-xs text-destructive">
                    {textForm.formState.errors.title.message}
                  </p>
                )}
              </div>
              <div className="space-y-2">
                <Label htmlFor="text-content">正文内容</Label>
                <Textarea
                  id="text-content"
                  placeholder="请输入或粘贴文本内容"
                  rows={6}
                  {...textForm.register("content")}
                />
                {textForm.formState.errors.content && (
                  <p className="text-xs text-destructive">
                    {textForm.formState.errors.content.message}
                  </p>
                )}
              </div>
              <Button
                type="submit"
                className="w-full"
                disabled={submitting}
              >
                {submitting && <Loader2 className="mr-2 size-4 animate-spin" />}
                {mode === "inbox" ? "存入收件箱" : "导入文本"}
              </Button>
            </form>
          </TabsContent>

          {/* PDF Tab */}
          <TabsContent value="file" className="mt-4">
            <form
              onSubmit={fileForm.handleSubmit(onFileSubmit)}
              className="space-y-4"
            >
              <TopicSelect
                value={fileTopicId}
                onChange={(v) => fileForm.setValue("topicId", v)}
                error={fileForm.formState.errors.topicId?.message}
              />
              <div className="space-y-2">
                <Label>PDF 文件</Label>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".pdf,application/pdf"
                  className="hidden"
                  onChange={handleFileInputChange}
                />
                {selectedFile ? (
                  <div className="flex items-center justify-between rounded-lg border bg-muted/50 p-3">
                    <div className="flex items-center gap-2 overflow-hidden">
                      <CheckCircle2 className="size-5 shrink-0 text-green-500" />
                      <div className="overflow-hidden">
                        <p className="truncate text-sm font-medium">
                          {selectedFile.name}
                        </p>
                        <p className="text-xs text-muted-foreground">
                          {(selectedFile.size / 1024 / 1024).toFixed(2)} MB
                        </p>
                      </div>
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => setSelectedFile(null)}
                    >
                      <X className="size-4" />
                    </Button>
                  </div>
                ) : (
                  <div
                    onClick={() => fileInputRef.current?.click()}
                    onDragOver={(e) => {
                      e.preventDefault();
                      setDragOver(true);
                    }}
                    onDragLeave={() => setDragOver(false)}
                    onDrop={handleDrop}
                    className={cn(
                      "flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed p-8 transition-colors",
                      dragOver
                        ? "border-primary bg-primary/5"
                        : "border-muted-foreground/30 hover:border-primary/50"
                    )}
                  >
                    <Upload className="size-8 text-muted-foreground" />
                    <div className="text-center">
                      <p className="text-sm font-medium">
                        点击或拖拽 PDF 文件到此处
                      </p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        仅支持 PDF 格式，最大 50MB
                      </p>
                    </div>
                  </div>
                )}
              </div>
              <div className="space-y-2">
                <Label htmlFor="file-title">标题（可选）</Label>
                <Input
                  id="file-title"
                  placeholder="自定义标题"
                  {...fileForm.register("title")}
                />
              </div>
              <Button
                type="submit"
                className="w-full"
                disabled={submitting || !selectedFile}
              >
                {submitting && <Loader2 className="mr-2 size-4 animate-spin" />}
                {mode === "inbox" ? "存入收件箱" : "上传 PDF"}
              </Button>
            </form>
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}
