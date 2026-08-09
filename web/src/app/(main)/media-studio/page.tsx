"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { Clapperboard, Loader2, MonitorPlay, Sparkles, XCircle } from "lucide-react";
import { toast } from "sonner";
import { getToken, mediaJobApi, workspaceApi, type MediaJob, type MediaRoutePreference } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";

const statusLabel: Record<MediaJob["status"], string> = {
  created: "已创建", quoted: "待确认", queued: "排队中", leased: "已派发", running: "生成中",
  uploading: "整理产物", completed: "已完成", failed: "失败", cancelled: "已取消",
};

function artifactLabel(job: MediaJob): string | null {
  if (job.status !== "completed" || !job.outputJson) return null;
  try {
    const output = JSON.parse(job.outputJson) as { artifact?: { filename?: string; size_bytes?: number } };
    const name = output.artifact?.filename;
    if (!name) return "已生成产物元数据，等待受控归档通道启用";
    const size = output.artifact?.size_bytes;
    return `${name}${size ? ` · ${(size / 1024 / 1024).toFixed(1)} MB` : ""}（等待受控归档）`;
  } catch {
    return "已生成产物元数据，等待受控归档通道启用";
  }
}

function formatDate(value: string) {
  return new Date(value).toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

export default function MediaStudioPage() {
  const [prompt, setPrompt] = useState("");
  const [routePreference, setRoutePreference] = useState<MediaRoutePreference>("local_first");
  const queryClient = useQueryClient();
  const workspace = useQuery({ queryKey: ["current-workspace"], queryFn: workspaceApi.getCurrent });
  const jobs = useQuery({
    queryKey: ["media-jobs", workspace.data?.id],
    queryFn: () => mediaJobApi.list(workspace.data?.id),
    enabled: Boolean(workspace.data?.id),
    refetchInterval: (query) => query.state.data?.some((job) => ["queued", "leased", "running", "uploading"].includes(job.status)) ? 3000 : false,
  });
  const create = useMutation({
    mutationFn: () => mediaJobApi.create({
      workspaceId: workspace.data!.id, capability: "video.generate", routePreference,
      parameters: { prompt, duration: 5, steps: 16, aspect: "16:9" },
    }),
    onSuccess: () => {
      toast.success("视频任务已加入队列");
      setPrompt("");
      queryClient.invalidateQueries({ queryKey: ["media-jobs"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : "创建媒体任务失败"),
  });
  const cancel = useMutation({
    mutationFn: mediaJobApi.cancel,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["media-jobs"] }),
    onError: () => toast.error("取消任务失败"),
  });
  const retry = useMutation({
    mutationFn: mediaJobApi.retry,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["media-jobs"] }),
    onError: () => toast.error("重新执行失败"),
  });

  const active = jobs.data?.filter((job) => ["queued", "leased", "running", "uploading"].includes(job.status)) ?? [];
  const activeIds = active.map((job) => job.id).join(",");

  useEffect(() => {
    const token = getToken();
    if (!token || !activeIds) return;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/media-jobs", { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on("MediaJobEvent", () => {
      void queryClient.invalidateQueries({ queryKey: ["media-jobs"] });
    });
    void connection.start().then(() => Promise.all(activeIds.split(",").map((id) => connection.invoke("Subscribe", id)))).catch(() => {
      // The existing polling interval is intentionally retained as a proxy/WebSocket fallback.
    });
    return () => { void connection.stop(); };
  }, [activeIds, queryClient]);

  return <div className="mx-auto max-w-5xl space-y-6">
    <div>
      <h1 className="text-2xl font-bold">创建传播内容</h1>
      <p className="mt-1 text-sm text-muted-foreground">从已沉淀的知识出发，生成可复用的视频内容，并保留完整任务记录。</p>
    </div>

    <Card>
      <CardHeader>
        <div className="flex items-center gap-3"><div className="rounded-lg bg-primary/10 p-2 text-primary"><Clapperboard className="size-5" /></div><div><CardTitle>知识视频草稿</CardTitle><CardDescription>当前使用本地 MiniMax-H3 MLX，5 秒、16 步、16:9。</CardDescription></div></div>
      </CardHeader>
      <CardContent className="space-y-4">
        <label className="block text-sm font-medium">执行方式
          <select className="mt-1 flex h-10 w-full rounded-md border border-input bg-background px-3 text-sm" value={routePreference} onChange={(event) => setRoutePreference(event.target.value as MediaRoutePreference)}>
            <option value="local_first">本地优先（H3 MLX）</option>
            <option value="platform_cloud">平台云端（MiniMax H3）</option>
            <option value="byok">BYOK（需已配置凭据）</option>
          </select>
        </label>
        <Textarea value={prompt} onChange={(event) => setPrompt(event.target.value)} placeholder="描述你希望从知识内容中呈现的画面、节奏和重点…" className="min-h-28" />
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
          <span>参考图与自动归档将在完成受控的服务间资产通道后开放。</span>
          <Button disabled={!prompt.trim() || !workspace.data?.id || create.isPending} onClick={() => create.mutate()}><Sparkles className="mr-2 size-4" />{create.isPending ? "提交中…" : "生成视频"}</Button>
        </div>
      </CardContent>
    </Card>

    <Card>
      <CardHeader><CardTitle>任务队列</CardTitle><CardDescription>媒体生成在本地优先执行；取消会在当前采样步安全生效。</CardDescription></CardHeader>
      <CardContent>
        {jobs.isLoading ? <div className="flex justify-center py-8"><Loader2 className="size-6 animate-spin text-muted-foreground" /></div>
          : (jobs.data?.length ?? 0) === 0 ? <div className="py-10 text-center text-sm text-muted-foreground"><MonitorPlay className="mx-auto mb-3 size-9 opacity-40" />还没有媒体任务。</div>
          : <div className="divide-y">{jobs.data!.map((job) => <div key={job.id} className="flex items-center gap-3 py-3"><MonitorPlay className="size-4 text-muted-foreground" /><div className="min-w-0 flex-1"><p className="text-sm font-medium">视频生成 <span className="ml-2 text-xs font-normal text-muted-foreground">{formatDate(job.createdAt)}</span></p><p className="truncate text-xs text-muted-foreground">{job.errorMessage ?? artifactLabel(job) ?? `本地优先 · ${statusLabel[job.status]}`}</p></div><span className="rounded-full bg-muted px-2 py-1 text-xs">{statusLabel[job.status]}</span>{active.some((item) => item.id === job.id) && <Button variant="ghost" size="sm" disabled={cancel.isPending} onClick={() => cancel.mutate(job.id)}><XCircle className="mr-1 size-3.5" />取消</Button>}{["failed", "cancelled"].includes(job.status) && <Button variant="ghost" size="sm" disabled={retry.isPending} onClick={() => retry.mutate(job.id)}>重新执行</Button>}</div>)}</div>}
      </CardContent>
    </Card>
  </div>;
}
