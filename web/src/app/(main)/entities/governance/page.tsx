"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Loader2, Pause, Play, RefreshCw, ShieldCheck } from "lucide-react";
import { entityGovernanceApi, entityMergeApi, workspaceApi } from "@/lib/api";
import type { EntityGovernanceTask } from "@/lib/types";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

const statusLabel: Record<string, string> = {
  pending: "待处理", running: "处理中", paused: "已暂停", completed: "已完成",
  failed: "失败", rejected: "已拒绝", deferred: "已延后",
};

export default function EntityGovernancePage() {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState("pending");
  const [selected, setSelected] = useState<EntityGovernanceTask | null>(null);
  const workspace = useQuery({ queryKey: ["workspace-current"], queryFn: workspaceApi.getCurrent });
  const workspaceId = workspace.data?.id;
  const tasks = useQuery({
    queryKey: ["entity-governance-tasks", workspaceId, status],
    queryFn: () => entityGovernanceApi.listTasks({
      workspaceId,
      status: status === "all" ? undefined : status,
      limit: 200,
    }),
    enabled: Boolean(workspaceId),
    refetchInterval: 5000,
  });
  const metrics = useQuery({
    queryKey: ["entity-quality-metrics", workspaceId],
    queryFn: () => entityGovernanceApi.qualityMetrics(workspaceId),
    enabled: Boolean(workspaceId),
    refetchInterval: 10000,
  });
  const preview = useQuery({
    queryKey: ["entity-merge-preview", selected?.id],
    queryFn: () => entityMergeApi.preview({
      workspaceId: selected!.workspaceId,
      entityIdA: selected!.subjectEntityId!,
      entityIdB: selected!.candidateEntityId!,
    }),
    enabled: Boolean(selected?.subjectEntityId && selected?.candidateEntityId),
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["entity-governance-tasks"] });
    void queryClient.invalidateQueries({ queryKey: ["entity-quality-metrics"] });
    void queryClient.invalidateQueries({ queryKey: ["entity-merge-preview"] });
  };
  const action = useMutation({
    mutationFn: async (input: { task: EntityGovernanceTask; action: string }) => {
      if (["pause", "resume", "retry"].includes(input.action))
        return entityGovernanceApi.controlTask(input.task.id, input.action as "pause" | "resume" | "retry");
      return entityGovernanceApi.decide(input.task.id, {
        decision: input.action as "MERGE" | "REJECT" | "BLOCK" | "DEFER",
        reason: input.action === "MERGE" ? "治理工作台人工确认同一实体" : "治理工作台人工判定",
        idempotencyKey: crypto.randomUUID(),
      });
    },
    onSuccess: () => { setSelected(null); refresh(); },
  });
  const startScan = useMutation({
    mutationFn: () => entityGovernanceApi.startScan({
      workspaceId: workspaceId!,
      batchSize: 50,
      idempotencyKey: crypto.randomUUID(),
    }),
    onSuccess: refresh,
  });
  const maintenance = useMutation({
    mutationFn: (operation: "ALIAS_MIGRATION" | "HISTORICAL_MENTION_BACKFILL" | "REDIRECT_COMPRESSION" | "ENTITY_REINDEX") =>
      entityGovernanceApi.startMaintenance({
        workspaceId: workspaceId!,
        operation,
        batchSize: 50,
        idempotencyKey: crypto.randomUUID(),
      }),
    onSuccess: refresh,
  });
  const metric = metrics.data;

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-4">
        <div>
          <Link className={buttonVariants({ variant: "ghost", size: "sm", className: "-ml-3 mb-2" })} href="/entities"><ArrowLeft className="mr-2 size-4" />返回实体列表</Link>
          <h1 className="flex items-center gap-2 text-2xl font-bold"><ShieldCheck className="size-6 text-primary" />实体治理工作台</h1>
          <p className="mt-1 text-sm text-muted-foreground">审核重复候选、执行安全合并，并监控存量治理质量。</p>
        </div>
        <div className="flex flex-wrap justify-end gap-2">
          <Button variant="outline" size="sm" onClick={() => maintenance.mutate("ALIAS_MIGRATION")} disabled={!workspaceId || maintenance.isPending}>迁移旧别名</Button>
          <Button variant="outline" size="sm" onClick={() => maintenance.mutate("HISTORICAL_MENTION_BACKFILL")} disabled={!workspaceId || maintenance.isPending}>补齐历史提及</Button>
          <Button size="sm" onClick={() => startScan.mutate()} disabled={!workspaceId || startScan.isPending}>{startScan.isPending && <Loader2 className="mr-2 size-4 animate-spin" />}开始影子扫描</Button>
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
        {[
          ["标准实体", metric?.activeEntityCount ?? 0],
          ["未解析率", `${((metric?.unresolvedRate ?? 0) * 100).toFixed(1)}%`],
          ["待审核", metric?.pendingReviewCount ?? 0],
          ["疑似重复", metric?.duplicateCandidateCount ?? 0],
          ["合并撤销率", `${((metric?.mergeRevertRate ?? 0) * 100).toFixed(1)}%`],
          ["索引待同步", metric?.pendingOutboxCount ?? 0],
        ].map(([label, value]) => <Card key={label}><CardContent className="p-4"><div className="text-xs text-muted-foreground">{label}</div><div className="mt-1 text-xl font-bold tabular-nums">{value}</div></CardContent></Card>)}
      </div>

      <div className="grid min-h-[520px] gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
        <Card>
          <CardHeader className="flex-row items-center justify-between space-y-0">
            <CardTitle>治理任务</CardTitle>
            <div className="flex gap-2">
              <Select value={status} onValueChange={(value) => value && setStatus(value)}><SelectTrigger className="w-32"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="pending">待处理</SelectItem><SelectItem value="running">处理中</SelectItem><SelectItem value="paused">已暂停</SelectItem><SelectItem value="failed">失败</SelectItem><SelectItem value="all">全部状态</SelectItem></SelectContent></Select>
              <Button variant="outline" size="icon" onClick={refresh}><RefreshCw className="size-4" /></Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-2">
            {tasks.isLoading ? <div className="flex justify-center py-16"><Loader2 className="size-6 animate-spin" /></div> :
              (tasks.data ?? []).length === 0 ? <div className="py-16 text-center text-sm text-muted-foreground">当前筛选下没有治理任务</div> :
              (tasks.data ?? []).map((task) => {
                const progress = task.totalItems ? Math.min(100, task.processedItems / task.totalItems * 100) : 0;
                const candidate = task.taskType === "DUPLICATE_CANDIDATE";
                return <button key={task.id} type="button" onClick={() => candidate && setSelected(task)} className={`w-full rounded-lg border p-3 text-left transition hover:bg-muted/40 ${selected?.id === task.id ? "border-primary bg-primary/5" : ""}`}>
                  <div className="flex items-center justify-between gap-3"><span className="font-medium">{candidate ? "重复实体候选" : task.taskType === "ENTITY_MAINTENANCE" ? "存量维护任务" : "重复扫描任务"}</span><span className="rounded-full bg-muted px-2 py-0.5 text-xs">{statusLabel[task.status] ?? task.status}</span></div>
                  {candidate ? <div className="mt-2 text-xs text-muted-foreground">实体 {task.subjectEntityId?.slice(0, 8)} ↔ {task.candidateEntityId?.slice(0, 8)} · 相似分 {(task.score ?? 0).toFixed(3)}</div> :
                    <><div className="mt-2 h-1.5 overflow-hidden rounded-full bg-muted"><div className="h-full bg-primary" style={{ width: `${progress}%` }} /></div><div className="mt-1 flex justify-between text-xs text-muted-foreground"><span>{task.processedItems}/{task.totalItems}</span><span>成功 {task.succeededItems} · 失败 {task.failedItems}</span></div></>}
                  {!candidate && <div className="mt-2 flex gap-1" onClick={(event) => event.stopPropagation()}>{task.status === "running" || task.status === "pending" ? <Button variant="ghost" size="sm" onClick={() => action.mutate({ task, action: "pause" })}><Pause className="mr-1 size-3" />暂停</Button> : null}{task.status === "paused" ? <Button variant="ghost" size="sm" onClick={() => action.mutate({ task, action: "resume" })}><Play className="mr-1 size-3" />继续</Button> : null}{task.status === "failed" ? <Button variant="ghost" size="sm" onClick={() => action.mutate({ task, action: "retry" })}><RefreshCw className="mr-1 size-3" />重试</Button> : null}</div>}
                </button>;
              })}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>合并预览</CardTitle></CardHeader>
          <CardContent>
            {!selected ? <p className="py-16 text-center text-sm text-muted-foreground">选择左侧重复候选以查看迁移影响</p> :
              preview.isLoading ? <div className="flex justify-center py-16"><Loader2 className="size-6 animate-spin" /></div> :
              preview.data ? <div className="space-y-4">
                <div className="rounded-lg bg-muted/50 p-3 text-sm"><div className="text-xs text-muted-foreground">推荐主实体</div><Link className="font-medium text-primary hover:underline" href={`/entities/${preview.data.targetEntityId}`}>{preview.data.targetEntityId}</Link><p className="mt-1 text-xs text-muted-foreground">{preview.data.recommendationReason}</p></div>
                <dl className="grid grid-cols-2 gap-2 text-sm">{[["提及", preview.data.mentionCount], ["别名", preview.data.aliasCount], ["外部 ID", preview.data.externalIdCount], ["文档关联", preview.data.documentAssociationCount], ["关系", preview.data.relationCount], ["自环清理", preview.data.selfLoopCount]].map(([label, value]) => <div key={label} className="rounded border p-2"><dt className="text-xs text-muted-foreground">{label}</dt><dd className="font-semibold">{value}</dd></div>)}</dl>
                {preview.data.hardBlocks.length > 0 && <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-xs text-red-700">阻断原因：{preview.data.hardBlocks.join("、")}</div>}
                <div className="grid grid-cols-2 gap-2"><Button variant="outline" onClick={() => action.mutate({ task: selected, action: "REJECT" })}>不是同一实体</Button><Button variant="outline" onClick={() => action.mutate({ task: selected, action: "BLOCK" })}>禁止以后合并</Button><Button variant="ghost" onClick={() => action.mutate({ task: selected, action: "DEFER" })}>稍后处理</Button><Button onClick={() => action.mutate({ task: selected, action: "MERGE" })} disabled={!preview.data.canExecute || action.isPending}>确认合并</Button></div>
              </div> : <p className="text-sm text-destructive">无法生成合并预览</p>}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
