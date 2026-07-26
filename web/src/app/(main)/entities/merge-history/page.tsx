"use client";

import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, History, Loader2, RotateCcw } from "lucide-react";
import { entityMergeApi, workspaceApi } from "@/lib/api";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export default function EntityMergeHistoryPage() {
  const queryClient = useQueryClient();
  const workspace = useQuery({ queryKey: ["workspace-current"], queryFn: workspaceApi.getCurrent });
  const history = useQuery({
    queryKey: ["entity-merge-history", workspace.data?.id],
    queryFn: () => entityMergeApi.history({ workspaceId: workspace.data?.id, limit: 200 }),
    enabled: Boolean(workspace.data?.id),
  });
  const revert = useMutation({
    mutationFn: (id: string) => entityMergeApi.revert(id, crypto.randomUUID()),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["entity-merge-history"] }),
  });

  return <div className="space-y-5">
    <div>
      <Link className={buttonVariants({ variant: "ghost", size: "sm", className: "-ml-3 mb-2" })} href="/entities"><ArrowLeft className="mr-2 size-4" />返回实体列表</Link>
      <h1 className="flex items-center gap-2 text-2xl font-bold"><History className="size-6 text-primary" />实体合并历史</h1>
      <p className="mt-1 text-sm text-muted-foreground">查看合并原因、操作方式和撤销状态。存在合并后新增数据时，撤销会自动转入拆分任务。</p>
    </div>
    <Card>
      <CardHeader><CardTitle>审计记录</CardTitle></CardHeader>
      <CardContent>
        {history.isLoading ? <div className="flex justify-center py-16"><Loader2 className="size-6 animate-spin" /></div> :
          (history.data ?? []).length === 0 ? <div className="py-16 text-center text-sm text-muted-foreground">暂无合并记录</div> :
          <Table><TableHeader><TableRow><TableHead>源实体</TableHead><TableHead>主实体</TableHead><TableHead>原因</TableHead><TableHead>方式</TableHead><TableHead>状态</TableHead><TableHead>时间</TableHead><TableHead className="text-right">操作</TableHead></TableRow></TableHeader><TableBody>{(history.data ?? []).map((item) => <TableRow key={item.mergeId}><TableCell><Link className="text-primary hover:underline" href={`/entities/${item.sourceEntityId}`}>{item.sourceEntityId.slice(0, 8)}</Link></TableCell><TableCell><Link className="text-primary hover:underline" href={`/entities/${item.targetEntityId}`}>{item.targetEntityId.slice(0, 8)}</Link></TableCell><TableCell className="max-w-xs truncate">{item.reason}</TableCell><TableCell>{item.method}</TableCell><TableCell>{item.status === "completed" ? "已完成" : item.status === "reverted" ? "已撤销" : item.status}</TableCell><TableCell>{new Date(item.createdAt).toLocaleString("zh-CN")}</TableCell><TableCell className="text-right">{item.status === "completed" && <Button variant="outline" size="sm" disabled={revert.isPending} onClick={() => revert.mutate(item.mergeId)}><RotateCcw className="mr-1 size-3" />撤销</Button>}</TableCell></TableRow>)}</TableBody></Table>}
      </CardContent>
    </Card>
  </div>;
}
