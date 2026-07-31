"use client";

import { useAuthStore } from "@/stores/auth-store";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import { WorkspaceStatusPanel } from "@/components/workspace-status-panel";
import { HybridDataFlowPanel } from "@/components/hybrid-data-flow-panel";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { useDesktopUpdate } from "@/components/desktop-update-provider";
import { Download, RefreshCw, ShieldCheck } from "lucide-react";

const UPDATE_STATUS_LABELS = {
  idle: "等待检查",
  checking: "正在检查",
  "up-to-date": "已是最新版本",
  available: "发现新版本",
  downloading: "正在下载",
  ready: "可以安装",
  installing: "正在安装",
  error: "更新失败",
} as const;

export default function SettingsPage() {
  const { user, isLocalAnonymous } = useAuthStore();
  const desktopUpdate = useDesktopUpdate();
  const displayName = user?.nickname ?? (isLocalAnonymous ? "本地用户" : "-");
  const displayEmail =
    user?.email ?? (isLocalAnonymous ? "local@knowledge-engine.local" : "-");
  const planCode = user?.planCode ?? (isLocalAnonymous ? "本地模式" : "免费版");

  return (
    <div className="space-y-6">
      <WorkspaceStatusPanel />

      <HybridDataFlowPanel />

      {desktopUpdate.isDesktop && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="size-4 text-primary" />
              桌面端更新
            </CardTitle>
            <CardDescription>
              更新包通过签名验证后安装，安装前会备份本地数据库。
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">当前版本</div>
                <div className="mt-1 font-medium">
                  {desktopUpdate.currentVersion || "读取中"}
                </div>
              </div>
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">更新状态</div>
                <div className="mt-1 font-medium">
                  {UPDATE_STATUS_LABELS[desktopUpdate.status]}
                </div>
              </div>
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">上次检查</div>
                <div className="mt-1 font-medium">
                  {desktopUpdate.lastCheckedAt
                    ? new Date(desktopUpdate.lastCheckedAt).toLocaleString("zh-CN")
                    : "尚未检查"}
                </div>
              </div>
            </div>

            {desktopUpdate.targetVersion && (
              <div className="rounded-lg bg-primary/5 p-3 text-sm">
                可用版本：<span className="font-medium">{desktopUpdate.targetVersion}</span>
                {desktopUpdate.progress != null && (
                  <span className="ml-3 text-muted-foreground">
                    下载进度 {desktopUpdate.progress}%
                  </span>
                )}
              </div>
            )}

            {desktopUpdate.error && (
              <div className="rounded-lg bg-destructive/10 p-3 text-sm text-destructive">
                {desktopUpdate.error}
              </div>
            )}

            <div className="space-y-3">
              <label className="flex items-center gap-3 text-sm">
                <Checkbox
                  checked={desktopUpdate.autoCheck}
                  onCheckedChange={(checked) => desktopUpdate.setAutoCheck(checked === true)}
                />
                启动后自动检查更新
              </label>
              <label className="flex items-center gap-3 text-sm">
                <Checkbox
                  checked={desktopUpdate.autoDownload}
                  onCheckedChange={(checked) => desktopUpdate.setAutoDownload(checked === true)}
                />
                发现新版本后自动下载，安装仍需确认
              </label>
            </div>

            <div className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                disabled={
                  desktopUpdate.status === "checking" ||
                  desktopUpdate.status === "downloading" ||
                  desktopUpdate.status === "installing"
                }
                onClick={() => void desktopUpdate.checkForUpdates()}
              >
                <RefreshCw
                  className={`mr-2 size-4 ${
                    desktopUpdate.status === "checking" ? "animate-spin" : ""
                  }`}
                />
                检查更新
              </Button>
              {desktopUpdate.status === "available" && (
                <Button onClick={() => void desktopUpdate.downloadUpdate()}>
                  <Download className="mr-2 size-4" />
                  下载更新
                </Button>
              )}
              {desktopUpdate.status === "ready" && (
                <Button onClick={() => void desktopUpdate.installUpdate()}>
                  安装并重启
                </Button>
              )}
            </div>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">账户信息</CardTitle>
          <CardDescription>您的注册信息</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">昵称</span>
            <span className="text-sm font-medium">{displayName}</span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">邮箱</span>
            <span className="text-sm font-medium">{displayEmail}</span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">套餐</span>
            <span className="text-sm font-medium">
              {planCode}
            </span>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
