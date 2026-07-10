"use client";

import { useEffect, useState } from "react";
import { useRouter, usePathname } from "next/navigation";
import Link from "next/link";
import {
  LayoutDashboard,
  FolderOpen,
  FileText,
  Boxes,
  Settings,
  LogOut,
  Loader2,
  User as UserIcon,
  Search,
  MessageCircle,
  ClipboardList,
  KeyRound,
  BarChart3,
  BookOpen,
  MessageSquare,
  Layers,
  Activity,
  Cpu,
  Inbox,
  ChevronDown,
  Tag,
  FileDown,
  Bot,
} from "lucide-react";
import { toast } from "sonner";
import { useAuthStore } from "@/stores/auth-store";
import { useFeedbackStore } from "@/stores/feedback-store";
import { getToken, workspaceApi, ApiRequestError } from "@/lib/api";
import type { Workspace } from "@/lib/types";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { FeedbackDialog } from "@/components/feedback-dialog";
import { MemorixBrand } from "@/components/brand/memorix-brand";

const navItems = [
  { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { href: "/search", label: "搜索", icon: Search },
  { href: "/qa", label: "问答", icon: MessageCircle },
  { href: "/topics", label: "专题", icon: FolderOpen },
  { href: "/documents", label: "文档", icon: FileText },
  { href: "/reports", label: "报告", icon: ClipboardList },
  { href: "/exports", label: "导出", icon: FileDown },
  { href: "/entities", label: "实体", icon: Boxes },
  { href: "/tags", label: "标签", icon: Tag },
  { href: "/settings", label: "设置", icon: Settings },
];

const settingsSubItems = [
  { href: "/settings/workspace", label: "工作区", icon: Layers },
  { href: "/settings/model-config", label: "模型配置", icon: Cpu },
  { href: "/settings/runtime", label: "运行时状态", icon: Activity },
  { href: "/settings/api-keys", label: "API Key", icon: KeyRound },
  { href: "/settings/agents", label: "Agent 接入", icon: Bot },
  { href: "/settings/usage", label: "使用量", icon: BarChart3 },
  { href: "/settings/feedback", label: "我的反馈", icon: MessageSquare },
  { href: "/settings/api-docs", label: "API 文档", icon: BookOpen },
  { href: "/settings/inbox", label: "收件箱", icon: Inbox },
];

export default function AppLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const {
    user,
    isAuthenticated,
    isLocalAnonymous,
    init,
    logout,
    enterLocalAnonymous,
  } = useAuthStore();
  const openFeedback = useFeedbackStore((s) => s.open);
  const [checking, setChecking] = useState(true);
  const [currentWorkspace, setCurrentWorkspace] = useState<Workspace | null>(null);
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [switching, setSwitching] = useState(false);

  useEffect(() => {
    const bootstrap = async () => {
      const token = getToken();
      if (token) {
        try {
          await init();
          setChecking(false);
        } catch {
          router.replace("/login");
        }
        return;
      }

      enterLocalAnonymous();
      const [ws, list] = await Promise.all([
        workspaceApi.getCurrent().catch(() => null),
        workspaceApi.list().catch(() => []),
      ]);
      setCurrentWorkspace(ws);
      setWorkspaces(list);
      setChecking(false);
      if (!ws && pathname !== "/setup") {
        router.replace("/setup");
      }
    };

    bootstrap().catch(() => {
      setChecking(false);
      if (pathname !== "/setup") {
        router.replace("/setup");
      }
    });
  }, [enterLocalAnonymous, init, pathname, router]);

  // 获取当前工作区与工作区列表
  useEffect(() => {
    if (!checking && (isAuthenticated || isLocalAnonymous)) {
      Promise.all([
        workspaceApi.getCurrent().catch(() => null),
        workspaceApi.list().catch(() => []),
      ]).then(([ws, list]) => {
        setCurrentWorkspace(ws);
        setWorkspaces(list);
      });
    }
  }, [checking, isAuthenticated, isLocalAnonymous]);

  // 监听认证状态变化
  useEffect(() => {
    if (
      !checking &&
      !isAuthenticated &&
      !isLocalAnonymous &&
      pathname !== "/setup"
    ) {
      router.replace("/login");
    }
  }, [checking, isAuthenticated, isLocalAnonymous, pathname, router]);

  const handleLogout = async () => {
    await logout();
    router.replace("/login");
  };

  const handleSwitchWorkspace = async (id: string) => {
    if (currentWorkspace?.id === id) return;
    setSwitching(true);
    try {
      await workspaceApi.switch(id);
      toast.success("工作区已切换");
      // 刷新页面以加载新工作区的数据
      window.location.reload();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "切换工作区失败";
      toast.error(message);
    } finally {
      setSwitching(false);
    }
  };

  const workspaceMode = currentWorkspace?.mode ?? "";

  if (checking) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!isAuthenticated && !isLocalAnonymous) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  const displayName = user?.nickname ?? (isLocalAnonymous ? "本地用户" : "用户");
  const displayEmail =
    user?.email ?? (isLocalAnonymous ? "local@knowledge-engine.local" : "");
  const initials = displayName.charAt(0).toUpperCase() || "U";

  return (
    <div className="flex min-h-screen">
      {/* 侧边栏 */}
      <aside className="flex w-60 flex-col bg-[#0D47A1] text-white/80">
        {/* Logo */}
        <div className="flex h-16 items-center gap-2 border-b border-white/15 px-5">
          <MemorixBrand inverted className="min-w-0 flex-1" />
          {workspaceMode && (
            <span className={cn(
              "ml-auto shrink-0 rounded-full px-2 py-0.5 text-xs font-medium",
              workspaceMode === "local" ? "bg-emerald-400/20 text-emerald-200" :
              workspaceMode === "cloud" ? "bg-cyan-300/20 text-cyan-100" :
              "bg-violet-300/20 text-violet-100"
            )}>
              {workspaceMode === "local" ? "本地" : workspaceMode === "cloud" ? "云端" : "混合"}
            </span>
          )}
        </div>

        {/* 工作区切换器 */}
        {currentWorkspace && (
          <div className="border-b border-white/15 px-3 py-2">
            <DropdownMenu>
              <DropdownMenuTrigger
                render={
                  <button
                    type="button"
                    disabled={switching}
                    className="flex w-full items-center gap-2 rounded-lg bg-white/10 px-3 py-2 text-left transition-colors hover:bg-white/15 disabled:cursor-not-allowed disabled:opacity-50"
                  />
                }
              >
                <div className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate text-sm font-medium text-white">
                    {switching ? "切换中..." : currentWorkspace.name}
                  </span>
                  <span
                    className={cn(
                      "mt-0.5 inline-flex w-fit items-center rounded-full px-1.5 py-0.5 text-[10px] font-medium",
                      workspaceMode === "local"
                        ? "bg-emerald-400/20 text-emerald-100"
                        : workspaceMode === "cloud"
                          ? "bg-cyan-300/20 text-cyan-100"
                          : "bg-violet-300/20 text-violet-100"
                    )}
                  >
                    {workspaceMode === "local"
                      ? "本地"
                      : workspaceMode === "cloud"
                        ? "云端"
                        : "混合"}
                  </span>
                </div>
                {switching ? (
                  <Loader2 className="size-4 shrink-0 animate-spin text-white/60" />
                ) : (
                  <ChevronDown className="size-4 shrink-0 text-white/60" />
                )}
              </DropdownMenuTrigger>
              <DropdownMenuContent align="start" className="w-52">
                <DropdownMenuLabel>切换工作区</DropdownMenuLabel>
                <DropdownMenuSeparator />
                {workspaces.length === 0 ? (
                  <div className="px-2 py-3 text-center text-xs text-muted-foreground">
                    暂无其他工作区
                  </div>
                ) : (
                  workspaces.map((ws) => (
                    <DropdownMenuItem
                      key={ws.id}
                      onClick={() => handleSwitchWorkspace(ws.id)}
                    >
                      <div className="flex min-w-0 flex-1 items-center justify-between gap-2">
                        <span className="truncate">{ws.name}</span>
                        {ws.id === currentWorkspace.id && (
                          <span className="shrink-0 text-xs text-primary">
                            当前
                          </span>
                        )}
                      </div>
                    </DropdownMenuItem>
                  ))
                )}
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        )}

        {/* 导航 */}
        <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = pathname === item.href || (item.href !== "/dashboard" && pathname.startsWith(item.href));
            const isSettings = item.href === "/settings";
            const settingsActive = isSettings && active;
            return (
              <div key={item.href}>
                <Link
                  href={item.href}
                  className={cn(
                    "relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                    active
                      ? "bg-white/15 text-white before:absolute before:inset-y-2 before:left-0 before:w-0.5 before:rounded-full before:bg-[#00B8C8]"
                      : "text-white/65 hover:bg-white/10 hover:text-white"
                  )}
                >
                  <Icon className="size-4" />
                  {item.label}
                </Link>
                {/* 设置子菜单 */}
                {isSettings && settingsActive && (
                  <div className="ml-6 mt-1 space-y-0.5 border-l border-white/20 pl-3">
                    {settingsSubItems.map((sub) => {
                      const SubIcon = sub.icon;
                      const subActive = pathname === sub.href;
                      return (
                        <Link
                          key={sub.href}
                          href={sub.href}
                          className={cn(
                            "flex items-center gap-2 rounded-md px-2 py-1.5 text-xs font-medium transition-colors",
                            subActive
                              ? "bg-white/10 text-cyan-200"
                              : "text-white/50 hover:bg-white/10 hover:text-white/80"
                          )}
                        >
                          <SubIcon className="size-3.5" />
                          {sub.label}
                        </Link>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          })}
        </nav>

        {/* 反馈按钮 */}
        <div className="border-t border-white/15 px-3 pt-3">
          <button
            onClick={() => openFeedback()}
            className="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-white/65 transition-colors hover:bg-white/10 hover:text-white"
          >
            <MessageSquare className="size-4" />
            反馈
          </button>
        </div>

        {/* 底部用户信息 */}
        <div className="border-t border-white/15 p-3">
          <DropdownMenu>
            <DropdownMenuTrigger
              render={
                <Button
                  variant="ghost"
                  className="w-full justify-start gap-2 text-white/65 hover:bg-white/10 hover:text-white"
                />
              }
            >
              <Avatar className="size-8">
                <AvatarFallback className="bg-white/15 text-xs text-white">
                  {initials}
                </AvatarFallback>
              </Avatar>
              <div className="flex flex-col items-start overflow-hidden">
                <span className="truncate text-sm text-white">
                  {displayName}
                </span>
                <span className="truncate text-xs text-white/50">
                  {displayEmail}
                </span>
              </div>
            </DropdownMenuTrigger>
            <DropdownMenuContent side="top" align="start" className="w-52">
              <DropdownMenuLabel>我的账户</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={handleLogout}>
                <LogOut className="mr-2 size-4" />
                退出登录
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </aside>

      {/* 主内容区 */}
      <div className="flex flex-1 flex-col overflow-hidden bg-[#F2F4F7]">
        {/* 顶部栏 */}
        <header className="flex h-16 items-center justify-between border-b border-[#DBE2EA] bg-white px-6">
          <div className="flex items-center gap-2">
            <UserIcon className="size-4 text-muted-foreground" />
            <span className="text-sm text-muted-foreground">
              欢迎回来，{displayName}
            </span>
          </div>
          <DropdownMenu>
            <DropdownMenuTrigger
              render={<Button variant="ghost" size="icon" />}
            >
              <Avatar className="size-8">
                <AvatarFallback className="bg-slate-200 text-xs">
                  {initials}
                </AvatarFallback>
              </Avatar>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-52">
              <DropdownMenuLabel>
                <div className="flex flex-col">
                  <span>{displayName}</span>
                  <span className="text-xs font-normal text-muted-foreground">
                    {displayEmail}
                  </span>
                </div>
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={handleLogout}>
                <LogOut className="mr-2 size-4" />
                退出登录
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </header>

        {/* 页面内容 */}
        <main className="flex-1 overflow-y-auto p-6">{children}</main>
      </div>

      {/* 全局反馈弹窗 */}
      <FeedbackDialog />
    </div>
  );
}
