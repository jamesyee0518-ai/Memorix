"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, FileText, Loader2, Plus, Search, Send } from "lucide-react";
import { toast } from "sonner";
import { promptRegistryApi, ApiRequestError } from "@/lib/api";
import type { PromptRegistry, CreatePromptRequest } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

const emptyForm: CreatePromptRequest = {
  promptKey: "",
  systemPrompt: "",
  userPromptTemplate: "",
  title: "",
  description: "",
};

export default function PromptsPage() {
  const [searchKey, setSearchKey] = useState("");
  const [activeKey, setActiveKey] = useState("");
  const [selectedVersionId, setSelectedVersionId] = useState("");
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<CreatePromptRequest>(emptyForm);
  const queryClient = useQueryClient();

  const versions = useQuery({
    queryKey: ["prompt-versions", activeKey],
    queryFn: () => promptRegistryApi.listVersions(activeKey),
    enabled: Boolean(activeKey),
  });

  const selected = versions.data?.find((v) => v.id === selectedVersionId) ?? versions.data?.[0] ?? null;

  const create = useMutation({
    mutationFn: (data: CreatePromptRequest) => promptRegistryApi.create(data),
    onSuccess: () => {
      toast.success("新版本已创建");
      setOpen(false);
      setForm(emptyForm);
      if (activeKey) queryClient.invalidateQueries({ queryKey: ["prompt-versions", activeKey] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "创建版本失败"),
  });

  const publish = useMutation({
    mutationFn: (id: string) => promptRegistryApi.publish(id),
    onSuccess: () => {
      toast.success("已发布");
      if (activeKey) queryClient.invalidateQueries({ queryKey: ["prompt-versions", activeKey] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "发布失败"),
  });

  const archive = useMutation({
    mutationFn: (id: string) => promptRegistryApi.archive(id),
    onSuccess: () => {
      toast.success("已归档");
      if (activeKey) queryClient.invalidateQueries({ queryKey: ["prompt-versions", activeKey] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "归档失败"),
  });

  const handleSearch = () => {
    if (!searchKey.trim()) return toast.error("请输入 Prompt Key");
    setActiveKey(searchKey.trim());
    setSelectedVersionId("");
  };

  return (
    <div className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold">Prompt 模板管理</h2>
        <p className="text-sm text-muted-foreground">管理 Prompt 版本、发布与归档</p>
      </div>

      <div className="grid gap-4 lg:grid-cols-[300px_1fr]">
        {/* 左侧：Prompt Key 列表 */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Prompt Key</CardTitle>
            <CardDescription>输入 Key 查看所有版本</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="flex gap-2">
              <Input
                value={searchKey}
                onChange={(e) => setSearchKey(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && handleSearch()}
                placeholder="例如 qa.answer"
              />
              <Button size="icon" onClick={handleSearch}>
                <Search className="size-4" />
              </Button>
            </div>
            {versions.isLoading && (
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <Loader2 className="size-4 animate-spin" /> 加载中…
              </div>
            )}
            {versions.data && versions.data.length > 0 && (
              <div className="space-y-1">
                {versions.data.map((v: PromptRegistry) => (
                  <button
                    key={v.id}
                    onClick={() => setSelectedVersionId(v.id)}
                    className={`flex w-full items-center justify-between rounded-lg border px-3 py-2 text-left text-sm transition-colors hover:bg-muted/50 ${
                      (selected?.id ?? "") === v.id ? "border-primary bg-primary/5" : ""
                    }`}
                  >
                    <span className="font-medium">v{v.version}</span>
                    <Badge variant="outline">{v.status}</Badge>
                  </button>
                ))}
              </div>
            )}
            {activeKey && (versions.data?.length ?? 0) === 0 && !versions.isLoading && (
              <p className="text-sm text-muted-foreground">未找到版本记录</p>
            )}
          </CardContent>
        </Card>

        {/* 右侧：详情 */}
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle className="text-base">版本详情</CardTitle>
                <CardDescription>
                  {activeKey ? `Key: ${activeKey}` : "请先在左侧搜索 Prompt Key"}
                </CardDescription>
              </div>
              <Button
                variant="outline"
                size="sm"
                disabled={!activeKey}
                onClick={() => { setForm({ ...emptyForm, promptKey: activeKey }); setOpen(true); }}
              >
                <Plus className="mr-2 size-4" />新建版本
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            {!activeKey ? (
              <div className="flex flex-col items-center justify-center py-16 text-center text-sm text-muted-foreground">
                <FileText className="mb-3 size-10 opacity-40" />
                在左侧输入 Prompt Key 并搜索以查看详情
              </div>
            ) : !selected ? (
              <div className="py-12 text-center text-sm text-muted-foreground">暂无数据</div>
            ) : (
              <div className="space-y-4">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant="secondary">v{selected.version}</Badge>
                  <Badge variant={selected.status === "published" ? "default" : "outline"}>
                    {selected.status}
                  </Badge>
                  {selected.title && <span className="text-sm font-medium">{selected.title}</span>}
                </div>
                {selected.description && (
                  <p className="text-sm text-muted-foreground">{selected.description}</p>
                )}
                <div className="space-y-2">
                  <Label>System Prompt</Label>
                  <pre className="max-h-60 overflow-auto whitespace-pre-wrap rounded-lg border bg-muted/30 p-3 text-sm">
                    {selected.systemPrompt}
                  </pre>
                </div>
                {selected.userPromptTemplate && (
                  <div className="space-y-2">
                    <Label>User Template</Label>
                    <pre className="max-h-40 overflow-auto whitespace-pre-wrap rounded-lg border bg-muted/30 p-3 text-sm">
                      {selected.userPromptTemplate}
                    </pre>
                  </div>
                )}
                {selected.language && (
                  <div className="flex items-center gap-2 text-sm">
                    <span className="text-muted-foreground">语言:</span>
                    <Badge variant="outline">{selected.language}</Badge>
                  </div>
                )}
                <div className="flex gap-2">
                  <Button
                    size="sm"
                    disabled={selected.status === "published" || publish.isPending}
                    onClick={() => publish.mutate(selected.id)}
                  >
                    <Send className="mr-2 size-3.5" />发布
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={selected.status === "archived" || archive.isPending}
                    onClick={() => archive.mutate(selected.id)}
                  >
                    <Archive className="mr-2 size-3.5" />归档
                  </Button>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>新建 Prompt 版本</DialogTitle>
            <DialogDescription>为 {form.promptKey} 创建新版本</DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label>标题</Label>
              <Input
                value={form.title}
                onChange={(e) => setForm({ ...form, title: e.target.value })}
                placeholder="版本标题"
              />
            </div>
            <div className="space-y-2">
              <Label>System Prompt</Label>
              <Textarea
                value={form.systemPrompt}
                onChange={(e) => setForm({ ...form, systemPrompt: e.target.value })}
                placeholder="系统提示词…"
                className="min-h-24"
              />
            </div>
            <div className="space-y-2">
              <Label>User Template</Label>
              <Textarea
                value={form.userPromptTemplate}
                onChange={(e) => setForm({ ...form, userPromptTemplate: e.target.value })}
                placeholder="用户模板（可选）…"
                className="min-h-20"
              />
            </div>
            <div className="space-y-2">
              <Label>语言</Label>
              <Input
                value={form.language ?? ""}
                onChange={(e) => setForm({ ...form, language: e.target.value })}
                placeholder="例如 zh-CN"
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>取消</Button>
            <Button
              disabled={!form.systemPrompt.trim() || create.isPending}
              onClick={() => create.mutate(form)}
            >
              {create.isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
              创建
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
