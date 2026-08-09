"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { Gauge, Loader2, MonitorPlay, Play, Sparkles, Trophy, XCircle } from "lucide-react";
import { toast } from "sonner";
import {
  API_ORIGIN,
  benchmarkApi,
  getToken,
  mediaJobApi,
  modelRegistryApi,
  workspaceApi,
  ApiRequestError,
  type MediaJob,
} from "@/lib/api";
import type { BenchmarkResult, RankingEntry } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const statusLabel: Record<MediaJob["status"], string> = {
  created: "已创建", quoted: "待确认", queued: "排队中", leased: "已派发", running: "生成中",
  uploading: "整理产物", completed: "已完成", failed: "失败", cancelled: "已取消",
};

function formatDate(value: string) {
  return new Date(value).toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

export default function BenchmarkPage() {
  const [selectedModelId, setSelectedModelId] = useState("");
  const queryClient = useQueryClient();

  const workspace = useQuery({ queryKey: ["current-workspace"], queryFn: workspaceApi.getCurrent });

  const jobs = useQuery({
    queryKey: ["media-jobs", workspace.data?.id],
    queryFn: () => mediaJobApi.list(workspace.data?.id),
    enabled: Boolean(workspace.data?.id),
    refetchInterval: (query) =>
      query.state.data?.some((job) => ["queued", "leased", "running", "uploading"].includes(job.status)) ? 3000 : false,
  });

  const results = useQuery({
    queryKey: ["benchmark-results"],
    queryFn: () => benchmarkApi.getResults(),
  });

  const rankings = useQuery({
    queryKey: ["benchmark-rankings", "fastest"],
    queryFn: () => benchmarkApi.getRankings("fastest"),
  });

  const models = useQuery({
    queryKey: ["model-registry-list"],
    queryFn: () => modelRegistryApi.list(),
  });

  const runBenchmark = useMutation({
    mutationFn: () => benchmarkApi.run(selectedModelId),
    onSuccess: () => {
      toast.success("基准测试已启动");
      queryClient.invalidateQueries({ queryKey: ["benchmark-results"] });
      queryClient.invalidateQueries({ queryKey: ["benchmark-rankings"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "启动基准测试失败"),
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
      .withUrl(`${API_ORIGIN}/hubs/media-jobs`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on("MediaJobEvent", () => {
      void queryClient.invalidateQueries({ queryKey: ["media-jobs"] });
    });
    void connection
      .start()
      .then(() => Promise.all(activeIds.split(",").map((id) => connection.invoke("Subscribe", id))))
      .catch(() => {
        // 轮询作为 WebSocket 不可用时的后备
      });
    return () => { void connection.stop(); };
  }, [activeIds, queryClient]);

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-bold">基准测试</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          查看媒体任务队列与实时状态，运行模型基准测试并对比排名。
        </p>
      </div>

      {/* 运行基准测试 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Gauge className="size-5 text-primary" />
            运行基准测试
          </CardTitle>
          <CardDescription>选择一个已注册的模型并运行基准测试</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1 space-y-2">
              <label className="text-sm font-medium">选择模型</label>
              {models.isLoading ? (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 className="size-4 animate-spin" /> 加载模型列表…
                </div>
              ) : (
                <Select value={selectedModelId} onValueChange={(v) => setSelectedModelId(v ?? "")}>
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="请选择模型" />
                  </SelectTrigger>
                  <SelectContent>
                    {models.data?.map((m) => (
                      <SelectItem key={m.id} value={m.id}>
                        {m.displayName} ({m.providerId}/{m.modelId})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>
            <Button
              disabled={!selectedModelId || runBenchmark.isPending}
              onClick={() => runBenchmark.mutate()}
            >
              {runBenchmark.isPending ? (
                <Loader2 className="mr-2 size-4 animate-spin" />
              ) : (
                <Sparkles className="mr-2 size-4" />
              )}
              运行基准测试
            </Button>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* 基准测试结果 */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Play className="size-5 text-primary" />
              基准测试结果
            </CardTitle>
            <CardDescription>最近的基准测试运行记录</CardDescription>
          </CardHeader>
          <CardContent>
            {results.isLoading ? (
              <div className="flex justify-center py-8">
                <Loader2 className="size-6 animate-spin text-muted-foreground" />
              </div>
            ) : (results.data?.length ?? 0) === 0 ? (
              <div className="py-10 text-center text-sm text-muted-foreground">
                <Play className="mx-auto mb-3 size-9 opacity-40" />
                暂无基准测试结果
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>名称</TableHead>
                    <TableHead>吞吐量</TableHead>
                    <TableHead>RTF</TableHead>
                    <TableHead>状态</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {results.data!.map((r: BenchmarkResult) => (
                    <TableRow key={r.id}>
                      <TableCell className="font-medium">{r.benchmarkName}</TableCell>
                      <TableCell>{r.throughput?.toFixed(2) ?? "-"}</TableCell>
                      <TableCell>{r.rtf?.toFixed(3) ?? "-"}</TableCell>
                      <TableCell>
                        <Badge variant={r.status === "completed" ? "default" : "secondary"}>
                          {r.status}
                        </Badge>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        {/* 排名 */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Trophy className="size-5 text-primary" />
              模型排名
            </CardTitle>
            <CardDescription>按速度排序（fastest）</CardDescription>
          </CardHeader>
          <CardContent>
            {rankings.isLoading ? (
              <div className="flex justify-center py-8">
                <Loader2 className="size-6 animate-spin text-muted-foreground" />
              </div>
            ) : (rankings.data?.length ?? 0) === 0 ? (
              <div className="py-10 text-center text-sm text-muted-foreground">
                <Trophy className="mx-auto mb-3 size-9 opacity-40" />
                暂无排名数据
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>#</TableHead>
                    <TableHead>模型</TableHead>
                    <TableHead>分数</TableHead>
                    <TableHead>指标</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rankings.data!.map((r: RankingEntry) => (
                    <TableRow key={r.modelRegistryId}>
                      <TableCell className="font-bold">{r.rank}</TableCell>
                      <TableCell>
                        <div className="font-medium">{r.displayName}</div>
                        <div className="text-xs text-muted-foreground">{r.providerId}/{r.modelId}</div>
                      </TableCell>
                      <TableCell className="font-medium">{r.score.toFixed(2)}</TableCell>
                      <TableCell>
                        <Badge variant="outline">{r.metric}</Badge>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      </div>

      {/* 媒体任务队列 */}
      <Card>
        <CardHeader>
          <CardTitle>媒体任务队列</CardTitle>
          <CardDescription>实时媒体任务状态，SignalR 推送更新</CardDescription>
        </CardHeader>
        <CardContent>
          {jobs.isLoading ? (
            <div className="flex justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : (jobs.data?.length ?? 0) === 0 ? (
            <div className="py-10 text-center text-sm text-muted-foreground">
              <MonitorPlay className="mx-auto mb-3 size-9 opacity-40" />
              还没有媒体任务
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>能力</TableHead>
                  <TableHead>路由</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>创建时间</TableHead>
                  <TableHead>操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {jobs.data!.map((job) => (
                  <TableRow key={job.id}>
                    <TableCell className="font-medium">{job.capability}</TableCell>
                    <TableCell>
                      <Badge variant="outline">{job.route}</Badge>
                    </TableCell>
                    <TableCell>
                      <Badge variant={job.status === "completed" ? "default" : job.status === "failed" ? "destructive" : "secondary"}>
                        {statusLabel[job.status]}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">{formatDate(job.createdAt)}</TableCell>
                    <TableCell>
                      <div className="flex gap-1">
                        {active.some((item) => item.id === job.id) && (
                          <Button variant="ghost" size="sm" disabled={cancel.isPending} onClick={() => cancel.mutate(job.id)}>
                            <XCircle className="mr-1 size-3.5" />取消
                          </Button>
                        )}
                        {["failed", "cancelled"].includes(job.status) && (
                          <Button variant="ghost" size="sm" disabled={retry.isPending} onClick={() => retry.mutate(job.id)}>
                            重试
                          </Button>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
