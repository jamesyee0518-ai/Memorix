"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Plus, Power, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { modelRegistryApi, ApiRequestError } from "@/lib/api";
import type { ModelRegistry, RegisterModelRequest } from "@/lib/types";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const emptyForm: RegisterModelRequest = {
  providerId: "",
  modelId: "",
  displayName: "",
  capability: "tts",
  isEnabled: true,
};

export default function ModelRegistryPage() {
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<RegisterModelRequest>(emptyForm);
  const queryClient = useQueryClient();

  const models = useQuery({
    queryKey: ["model-registry-list"],
    queryFn: () => modelRegistryApi.list(),
  });

  const register = useMutation({
    mutationFn: (data: RegisterModelRequest) => modelRegistryApi.register(data),
    onSuccess: () => {
      toast.success("模型已注册");
      setOpen(false);
      setForm(emptyForm);
      queryClient.invalidateQueries({ queryKey: ["model-registry-list"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "注册模型失败"),
  });

  const toggle = useMutation({
    mutationFn: ({ id, data }: { id: string; data: ModelRegistry }) =>
      modelRegistryApi.update(id, { ...data, isEnabled: !data.isEnabled }),
    onSuccess: () => {
      toast.success("状态已更新");
      queryClient.invalidateQueries({ queryKey: ["model-registry-list"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "更新失败"),
  });

  const disable = useMutation({
    mutationFn: (id: string) => modelRegistryApi.disable(id),
    onSuccess: () => {
      toast.success("模型已删除");
      queryClient.invalidateQueries({ queryKey: ["model-registry-list"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "删除失败"),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">模型注册表</h2>
          <p className="text-sm text-muted-foreground">管理已注册的音频与 TTS 模型</p>
        </div>
        <Button onClick={() => { setForm(emptyForm); setOpen(true); }}>
          <Plus className="mr-2 size-4" />注册模型
        </Button>
      </div>

      <Card>
        <CardContent className="p-0">
          {models.isLoading ? (
            <div className="flex justify-center py-12">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : (models.data?.length ?? 0) === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground">
              暂无已注册的模型
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>提供商</TableHead>
                  <TableHead>模型 ID</TableHead>
                  <TableHead>显示名称</TableHead>
                  <TableHead>能力</TableHead>
                  <TableHead>启用</TableHead>
                  <TableHead>健康</TableHead>
                  <TableHead>操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {models.data!.map((m) => (
                  <TableRow key={m.id}>
                    <TableCell className="font-medium">{m.providerId}</TableCell>
                    <TableCell>{m.modelId}</TableCell>
                    <TableCell>{m.displayName}</TableCell>
                    <TableCell>
                      <Badge variant="outline">{m.capability}</Badge>
                    </TableCell>
                    <TableCell>
                      <Badge variant={m.isEnabled ? "default" : "secondary"}>
                        {m.isEnabled ? "启用" : "禁用"}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Badge
                        variant={
                          m.healthStatus === "healthy" ? "default" :
                          m.healthStatus === "unhealthy" ? "destructive" : "secondary"
                        }
                      >
                        {m.healthStatus}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={toggle.isPending}
                          onClick={() => toggle.mutate({ id: m.id, data: m })}
                        >
                          <Power className="mr-1 size-3.5" />
                          {m.isEnabled ? "禁用" : "启用"}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={disable.isPending}
                          onClick={() => disable.mutate(m.id)}
                        >
                          <Trash2 className="size-3.5 text-destructive" />
                        </Button>
                      </div>
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
            <DialogTitle>注册模型</DialogTitle>
            <DialogDescription>填写模型信息以将其注册到模型注册表</DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label>提供商 ID</Label>
              <Input
                value={form.providerId}
                onChange={(e) => setForm({ ...form, providerId: e.target.value })}
                placeholder="例如 openai、azure、local"
              />
            </div>
            <div className="space-y-2">
              <Label>模型 ID</Label>
              <Input
                value={form.modelId}
                onChange={(e) => setForm({ ...form, modelId: e.target.value })}
                placeholder="例如 tts-1、speech-01"
              />
            </div>
            <div className="space-y-2">
              <Label>显示名称</Label>
              <Input
                value={form.displayName}
                onChange={(e) => setForm({ ...form, displayName: e.target.value })}
                placeholder="例如 OpenAI TTS-1"
              />
            </div>
            <div className="space-y-2">
              <Label>能力</Label>
              <Select
                value={form.capability}
                onValueChange={(v) => setForm({ ...form, capability: v ?? "tts" })}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="tts">TTS</SelectItem>
                  <SelectItem value="asr">ASR</SelectItem>
                  <SelectItem value="chat">Chat</SelectItem>
                  <SelectItem value="embedding">Embedding</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>取消</Button>
            <Button
              disabled={!form.providerId.trim() || !form.modelId.trim() || register.isPending}
              onClick={() => register.mutate(form)}
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
