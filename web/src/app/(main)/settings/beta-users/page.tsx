"use client";

import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Plus,
  MoreVertical,
  Trash2,
  Play,
  Pause,
  Users,
  UserCheck,
  UserPlus,
  UserX,
  Loader2,
  Mail,
} from "lucide-react";
import { betaUserApi, ApiRequestError } from "@/lib/api";
import type {
  BetaUser,
  BetaUserStatus,
  InviteBetaUserInput,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

const statusConfig: Record<
  BetaUserStatus,
  { label: string; className: string }
> = {
  invited: { label: "已邀请", className: "bg-blue-100 text-blue-700" },
  activated: { label: "已激活", className: "bg-green-100 text-green-700" },
  paused: { label: "已暂停", className: "bg-yellow-100 text-yellow-700" },
  churned: { label: "已流失", className: "bg-gray-100 text-gray-600" },
  blocked: { label: "已封禁", className: "bg-red-100 text-red-700" },
};

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN");
}

export default function BetaUsersPage() {
  const queryClient = useQueryClient();
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [inviteOpen, setInviteOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<BetaUser | null>(null);

  // 获取内测用户列表
  const { data, isLoading } = useQuery({
    queryKey: ["beta-users", statusFilter],
    queryFn: () =>
      betaUserApi.list({
        status: statusFilter !== "all" ? statusFilter : undefined,
      }),
  });

  const users = data?.items ?? [];

  // 统计
  const stats = {
    total: users.length,
    activated: users.filter((u) => u.status === "activated").length,
    invited: users.filter((u) => u.status === "invited").length,
    paused: users.filter((u) => u.status === "paused").length,
  };

  // 激活
  const activateMutation = useMutation({
    mutationFn: (id: string) => betaUserApi.activate(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["beta-users"] });
      toast.success("用户已激活");
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "激活失败";
      toast.error(message);
    },
  });

  // 暂停
  const pauseMutation = useMutation({
    mutationFn: (id: string) => betaUserApi.pause(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["beta-users"] });
      toast.success("用户已暂停");
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "暂停失败";
      toast.error(message);
    },
  });

  // 删除
  const deleteMutation = useMutation({
    mutationFn: (id: string) => betaUserApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["beta-users"] });
      toast.success("用户已删除");
      setDeleteTarget(null);
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "删除失败";
      toast.error(message);
    },
  });

  const statCards = [
    {
      label: "总人数",
      value: stats.total,
      icon: Users,
      color: "text-blue-600",
      bg: "bg-blue-50",
    },
    {
      label: "已激活",
      value: stats.activated,
      icon: UserCheck,
      color: "text-green-600",
      bg: "bg-green-50",
    },
    {
      label: "已邀请",
      value: stats.invited,
      icon: UserPlus,
      color: "text-purple-600",
      bg: "bg-purple-50",
    },
    {
      label: "已暂停",
      value: stats.paused,
      icon: UserX,
      color: "text-yellow-600",
      bg: "bg-yellow-50",
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">内测用户管理</h2>
          <p className="text-sm text-muted-foreground">
            管理内测用户邀请、激活状态和分组
          </p>
        </div>
        <Button onClick={() => setInviteOpen(true)}>
          <Plus className="mr-2 size-4" />
          邀请新用户
        </Button>
      </div>

      {/* 统计卡片 */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {statCards.map((card) => {
          const Icon = card.icon;
          return (
            <Card key={card.label}>
              <CardContent className="flex items-center gap-3 py-4">
                <div className={`flex size-10 items-center justify-center rounded-lg ${card.bg}`}>
                  <Icon className={`size-5 ${card.color}`} />
                </div>
                <div>
                  <p className="text-2xl font-bold">{card.value}</p>
                  <p className="text-xs text-muted-foreground">{card.label}</p>
                </div>
              </CardContent>
            </Card>
          );
        })}
      </div>

      {/* 状态筛选 */}
      <div className="flex items-center gap-2">
        <span className="text-sm text-muted-foreground">状态筛选：</span>
        <Select
          value={statusFilter}
          onValueChange={(v) => setStatusFilter(v as string)}
        >
          <SelectTrigger className="w-40">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部</SelectItem>
            <SelectItem value="invited">已邀请</SelectItem>
            <SelectItem value="activated">已激活</SelectItem>
            <SelectItem value="paused">已暂停</SelectItem>
            <SelectItem value="churned">已流失</SelectItem>
            <SelectItem value="blocked">已封禁</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* 用户列表 */}
      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="size-8 animate-spin text-muted-foreground" />
        </div>
      ) : users.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center">
            <Users className="mb-4 size-12 text-muted-foreground/50" />
            <p className="text-lg font-medium">暂无内测用户</p>
            <p className="mt-1 text-sm text-muted-foreground">
              邀请用户参与内测，收集早期反馈
            </p>
            <Button className="mt-4" onClick={() => setInviteOpen(true)}>
              <Plus className="mr-2 size-4" />
              邀请新用户
            </Button>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>邮箱</TableHead>
                <TableHead>名称</TableHead>
                <TableHead>分组</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>平台</TableHead>
                <TableHead>邀请时间</TableHead>
                <TableHead>最后活跃</TableHead>
                <TableHead className="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((user) => {
                const status = statusConfig[user.status];
                return (
                  <TableRow key={user.id}>
                    <TableCell className="font-medium">
                      <div className="flex items-center gap-1.5">
                        <Mail className="size-3.5 text-muted-foreground" />
                        {user.email}
                      </div>
                    </TableCell>
                    <TableCell>{user.name || "-"}</TableCell>
                    <TableCell>
                      {user.betaGroup ? (
                        <Badge variant="outline">{user.betaGroup}</Badge>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <Badge className={status.className}>{status.label}</Badge>
                    </TableCell>
                    <TableCell>
                      <Badge variant="secondary">{user.userType || "-"}</Badge>
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(user.createdAt)}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {formatDate(user.lastFeedbackAt)}
                    </TableCell>
                    <TableCell className="text-right">
                      <DropdownMenu>
                        <DropdownMenuTrigger
                          render={
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              type="button"
                            />
                          }
                        >
                          <MoreVertical className="size-4" />
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {user.status !== "activated" && (
                            <DropdownMenuItem
                              onClick={() => activateMutation.mutate(user.id)}
                            >
                              <Play className="mr-2 size-4" />
                              激活
                            </DropdownMenuItem>
                          )}
                          {user.status !== "paused" && (
                            <DropdownMenuItem
                              onClick={() => pauseMutation.mutate(user.id)}
                            >
                              <Pause className="mr-2 size-4" />
                              暂停
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleteTarget(user)}
                          >
                            <Trash2 className="mr-2 size-4" />
                            删除
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      {/* 邀请用户对话框 */}
      <InviteDialog
        open={inviteOpen}
        onOpenChange={setInviteOpen}
        onSuccess={() => {
          queryClient.invalidateQueries({ queryKey: ["beta-users"] });
        }}
      />

      {/* 删除确认 */}
      <Dialog
        open={!!deleteTarget}
        onOpenChange={(v) => !v && setDeleteTarget(null)}
      >
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>确认删除</DialogTitle>
            <DialogDescription>
              确定要删除内测用户「{deleteTarget?.email}」吗？此操作不可恢复。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" type="button" />}>
              取消
            </DialogClose>
            <Button
              variant="destructive"
              onClick={() => {
                if (deleteTarget) deleteMutation.mutate(deleteTarget.id);
              }}
              disabled={deleteMutation.isPending}
            >
              {deleteMutation.isPending && (
                <Loader2 className="mr-2 size-4 animate-spin" />
              )}
              删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// ===== 邀请用户对话框 =====

function InviteDialog({
  open,
  onOpenChange,
  onSuccess,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  onSuccess: () => void;
}) {
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [betaGroup, setBetaGroup] = useState("");
  const [platform, setPlatform] = useState("web");

  const inviteMutation = useMutation({
    mutationFn: (data: InviteBetaUserInput) => betaUserApi.invite(data),
    onSuccess: () => {
      toast.success("邀请已发送");
      onOpenChange(false);
      onSuccess();
      // 重置表单
      setEmail("");
      setName("");
      setBetaGroup("");
      setPlatform("web");
    },
    onError: (err) => {
      const message =
        err instanceof ApiRequestError ? err.message : "邀请失败";
      toast.error(message);
    },
  });

  const handleSubmit = () => {
    if (!email.trim()) {
      toast.error("请输入邮箱地址");
      return;
    }
    const data: InviteBetaUserInput = {
      email: email.trim(),
    };
    if (name.trim()) data.name = name.trim();
    if (betaGroup.trim()) data.betaGroup = betaGroup.trim();
    if (platform) data.platform = platform;
    inviteMutation.mutate(data);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>邀请新用户</DialogTitle>
          <DialogDescription>
            向新用户发送内测邀请，用户将通过邮件接收邀请码
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {/* 邮箱 */}
          <div className="space-y-2">
            <Label>
              邮箱 <span className="text-destructive">*</span>
            </Label>
            <Input
              type="email"
              placeholder="user@example.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>

          {/* 名称 */}
          <div className="space-y-2">
            <Label>名称（可选）</Label>
            <Input
              placeholder="用户昵称"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </div>

          {/* 分组 */}
          <div className="space-y-2">
            <Label>内测分组（可选）</Label>
            <Input
              placeholder="例如：early-access"
              value={betaGroup}
              onChange={(e) => setBetaGroup(e.target.value)}
            />
          </div>

          {/* 平台 */}
          <div className="space-y-2">
            <Label>平台</Label>
            <Select value={platform} onValueChange={(v) => setPlatform(v as string)}>
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="web">Web</SelectItem>
                <SelectItem value="ios">iOS</SelectItem>
                <SelectItem value="android">Android</SelectItem>
                <SelectItem value="desktop">Desktop</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <DialogFooter>
          <DialogClose render={<Button variant="outline" type="button" />}>
            取消
          </DialogClose>
          <Button
            onClick={handleSubmit}
            disabled={inviteMutation.isPending}
          >
            {inviteMutation.isPending && (
              <Loader2 className="mr-2 size-4 animate-spin" />
            )}
            发送邀请
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
