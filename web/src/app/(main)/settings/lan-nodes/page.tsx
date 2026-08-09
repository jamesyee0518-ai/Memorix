"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Network, Plus, Radar, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { lanNodeApi, ApiRequestError } from "@/lib/api";
import type { LanNode, RegisterLanNodeRequest } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

function formatDate(value?: string) {
  if (!value) return "-";
  return new Date(value).toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

export default function LanNodesPage() {
  const [open, setOpen] = useState(false);
  const [endpoint, setEndpoint] = useState("");
  const queryClient = useQueryClient();

  const nodes = useQuery({
    queryKey: ["lan-nodes"],
    queryFn: lanNodeApi.list,
  });

  const discover = useMutation({
    mutationFn: lanNodeApi.discover,
    onSuccess: (data) => {
      toast.success(`发现 ${data.length} 个节点`);
      queryClient.invalidateQueries({ queryKey: ["lan-nodes"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "发现节点失败"),
  });

  const register = useMutation({
    mutationFn: (data: RegisterLanNodeRequest) => lanNodeApi.register(data),
    onSuccess: () => {
      toast.success("节点已注册");
      setOpen(false);
      setEndpoint("");
      queryClient.invalidateQueries({ queryKey: ["lan-nodes"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "注册节点失败"),
  });

  const unregister = useMutation({
    mutationFn: (id: string) => lanNodeApi.unregister(id),
    onSuccess: () => {
      toast.success("节点已注销");
      queryClient.invalidateQueries({ queryKey: ["lan-nodes"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "注销失败"),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">LAN 节点管理</h2>
          <p className="text-sm text-muted-foreground">管理局域网内的音频处理节点</p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            disabled={discover.isPending}
            onClick={() => discover.mutate()}
          >
            {discover.isPending ? (
              <Loader2 className="mr-2 size-4 animate-spin" />
            ) : (
              <Radar className="mr-2 size-4" />
            )}
            发现节点
          </Button>
          <Button onClick={() => setOpen(true)}>
            <Plus className="mr-2 size-4" />注册节点
          </Button>
        </div>
      </div>

      <Card>
        <CardContent className="p-0">
          {nodes.isLoading ? (
            <div className="flex justify-center py-12">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : (nodes.data?.length ?? 0) === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground">
              <Network className="mx-auto mb-3 size-8 opacity-40" />
              暂无节点，点击「发现节点」或手动注册
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>端点</TableHead>
                  <TableHead>名称</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>能力</TableHead>
                  <TableHead>最后在线</TableHead>
                  <TableHead>操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {nodes.data!.map((n: LanNode) => (
                  <TableRow key={n.id}>
                    <TableCell className="font-medium">{n.endpoint}</TableCell>
                    <TableCell>{n.nodeName ?? "-"}</TableCell>
                    <TableCell>
                      <Badge
                        variant={
                          n.status === "online" ? "default" :
                          n.status === "offline" ? "secondary" : "outline"
                        }
                      >
                        {n.status}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {n.capabilities ? (
                        <div className="flex flex-wrap gap-1">
                          {n.capabilities.split(",").map((cap) => (
                            <Badge key={cap} variant="outline">{cap.trim()}</Badge>
                          ))}
                        </div>
                      ) : (
                        "-"
                      )}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {formatDate(n.lastSeenAt)}
                    </TableCell>
                    <TableCell>
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={unregister.isPending}
                        onClick={() => unregister.mutate(n.id)}
                      >
                        <Trash2 className="mr-1 size-3.5" />注销
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>注册节点</DialogTitle>
            <DialogDescription>输入局域网节点的端点地址</DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label>端点地址</Label>
              <Input
                value={endpoint}
                onChange={(e) => setEndpoint(e.target.value)}
                placeholder="例如 http://192.168.1.100:8080"
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>取消</Button>
            <Button
              disabled={!endpoint.trim() || register.isPending}
              onClick={() => register.mutate({ endpoint })}
            >
              {register.isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
              注册
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
