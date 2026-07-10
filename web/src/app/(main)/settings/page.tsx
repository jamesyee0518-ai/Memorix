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

export default function SettingsPage() {
  const { user, isLocalAnonymous } = useAuthStore();
  const displayName = user?.nickname ?? (isLocalAnonymous ? "本地用户" : "-");
  const displayEmail =
    user?.email ?? (isLocalAnonymous ? "local@knowledge-engine.local" : "-");
  const planCode = user?.planCode ?? (isLocalAnonymous ? "本地模式" : "免费版");

  return (
    <div className="space-y-6">
      <WorkspaceStatusPanel />

      <HybridDataFlowPanel />

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
