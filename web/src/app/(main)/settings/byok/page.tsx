"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRound, Loader2, Plus, RefreshCw, RotateCw, Ban } from "lucide-react";
import { toast } from "sonner";
import { providerCredentialApi, ApiRequestError } from "@/lib/api";
import type { CredentialDto, StoreCredentialRequest } from "@/lib/types";
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

const emptyForm: StoreCredentialRequest = {
  providerId: "",
  secret: "",
  credentialType: "api_key",
  label: "",
};

function formatDate(value?: string) {
  if (!value) return "-";
  return new Date(value).toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

export default function ByokPage() {
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<StoreCredentialRequest>(emptyForm);
  const queryClient = useQueryClient();

  const credentials = useQuery({
    queryKey: ["provider-credentials"],
    queryFn: providerCredentialApi.list,
  });

  const store = useMutation({
    mutationFn: (data: StoreCredentialRequest) => providerCredentialApi.store(data),
    onSuccess: () => {
      toast.success("凭证已添加");
      setOpen(false);
      setForm(emptyForm);
      queryClient.invalidateQueries({ queryKey: ["provider-credentials"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "添加凭证失败"),
  });

  const verify = useMutation({
    mutationFn: (id: string) => providerCredentialApi.verify(id),
    onSuccess: (data: unknown) => {
      const result = data as { valid: boolean };
      if (result.valid) toast.success("凭证验证通过");
      else toast.warning("凭证验证未通过");
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "验证失败"),
  });

  const disable = useMutation({
    mutationFn: (id: string) => providerCredentialApi.disable(id),
    onSuccess: () => {
      toast.success("凭证已禁用");
      queryClient.invalidateQueries({ queryKey: ["provider-credentials"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "禁用失败"),
  });

  const rotate = useMutation({
    mutationFn: (id: string) => providerCredentialApi.rotate(id),
    onSuccess: () => {
      toast.success("凭证轮换已触发");
      queryClient.invalidateQueries({ queryKey: ["provider-credentials"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "轮换失败"),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">BYOK 凭证管理</h2>
          <p className="text-sm text-muted-foreground">管理自有密钥（Bring Your Own Key）</p>
        </div>
        <Button onClick={() => { setForm(emptyForm); setOpen(true); }}>
          <Plus className="mr-2 size-4" />添加凭证
        </Button>
      </div>

      <Card>
        <CardContent className="p-0">
          {credentials.isLoading ? (
            <div className="flex justify-center py-12">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : (credentials.data?.length ?? 0) === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground">
              <KeyRound className="mx-auto mb-3 size-8 opacity-40" />
              暂无凭证
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>提供商</TableHead>
                  <TableHead>类型</TableHead>
                  <TableHead>标签</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>最后验证</TableHead>
                  <TableHead>操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {credentials.data!.map((c: CredentialDto) => (
                  <TableRow key={c.id}>
                    <TableCell className="font-medium">{c.providerId}</TableCell>
                    <TableCell>
                      <Badge variant="outline">{c.credentialType}</Badge>
                    </TableCell>
                    <TableCell>{c.label ?? "-"}</TableCell>
                    <TableCell>
                      <Badge
                        variant={
                          c.status === "active" ? "default" :
                          c.status === "disabled" ? "destructive" : "secondary"
                        }
                      >
                        {c.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {formatDate(c.lastVerifiedAt)}
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={verify.isPending}
                          onClick={() => verify.mutate(c.id)}
                        >
                          <RefreshCw className="mr-1 size-3.5" />验证
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={rotate.isPending}
                          onClick={() => rotate.mutate(c.id)}
                        >
                          <RotateCw className="mr-1 size-3.5" />轮换
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={disable.isPending}
                          onClick={() => disable.mutate(c.id)}
                        >
                          <Ban className="mr-1 size-3.5" />禁用
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
            <DialogTitle>添加凭证</DialogTitle>
            <DialogDescription>填写提供商与密钥信息</DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label>提供商</Label>
              <Select
                value={form.providerId}
                onValueChange={(v) => setForm({ ...form, providerId: v ?? "" })}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="请选择提供商" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="openai">OpenAI</SelectItem>
                  <SelectItem value="azure">Azure</SelectItem>
                  <SelectItem value="anthropic">Anthropic</SelectItem>
                  <SelectItem value="minimax">MiniMax</SelectItem>
                  <SelectItem value="volcengine">火山引擎</SelectItem>
                  <SelectItem value="custom">自定义</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>类型</Label>
              <Select
                value={form.credentialType}
                onValueChange={(v) => setForm({ ...form, credentialType: v ?? "api_key" })}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="api_key">API Key</SelectItem>
                  <SelectItem value="oauth_token">OAuth Token</SelectItem>
                  <SelectItem value="bearer_token">Bearer Token</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>标签</Label>
              <Input
                value={form.label}
                onChange={(e) => setForm({ ...form, label: e.target.value })}
                placeholder="例如 生产环境 OpenAI Key"
              />
            </div>
            <div className="space-y-2">
              <Label>密钥</Label>
              <Input
                type="password"
                value={form.secret}
                onChange={(e) => setForm({ ...form, secret: e.target.value })}
                placeholder="sk-..."
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>取消</Button>
            <Button
              disabled={!form.providerId || !form.secret.trim() || store.isPending}
              onClick={() => store.mutate(form)}
            >
              {store.isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
              添加
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
