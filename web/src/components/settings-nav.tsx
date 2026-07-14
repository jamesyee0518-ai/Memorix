"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  User,
  KeyRound,
  BarChart3,
  MessageSquare,
  BookOpen,
  Layers,
  Activity,
  Cpu,
  Inbox,
  CloudOff,
  Smartphone,
  Bell,
  Bot,
  Users,
  MessagesSquare,
  Rocket,
  Languages,
} from "lucide-react";
import { cn } from "@/lib/utils";

const settingsNavItems = [
  { href: "/settings", label: "账户信息", icon: User, exact: true },
  { href: "/settings/workspace", label: "工作区", icon: Layers },
  { href: "/settings/model-config", label: "模型配置", icon: Cpu },
  { href: "/settings/terminology", label: "术语库", icon: Languages },
  { href: "/settings/runtime", label: "运行时状态", icon: Activity },
  { href: "/settings/api-keys", label: "API Key 管理", icon: KeyRound },
  { href: "/settings/agents", label: "Agent 接入", icon: Bot },
  { href: "/settings/beta-users", label: "内测用户", icon: Users },
  { href: "/settings/usage", label: "使用量", icon: BarChart3 },
  { href: "/settings/feedback", label: "我的反馈", icon: MessageSquare },
  { href: "/settings/feedback-admin", label: "反馈管理", icon: MessagesSquare },
  { href: "/settings/release-notes", label: "版本说明", icon: Rocket },
  { href: "/settings/api-docs", label: "API 文档", icon: BookOpen },
  { href: "/settings/inbox", label: "收件箱", icon: Inbox },
  { href: "/settings/cloud-inbox", label: "云端收件箱", icon: CloudOff },
  { href: "/settings/mobile-devices", label: "移动设备", icon: Smartphone },
  { href: "/settings/push-notifications", label: "推送审计", icon: Bell },
];

export function SettingsNav() {
  const pathname = usePathname();

  return (
    <nav className="flex flex-wrap gap-1 border-b pb-3">
      {settingsNavItems.map((item) => {
        const Icon = item.icon;
        const active = item.exact
          ? pathname === item.href
          : pathname === item.href || pathname.startsWith(item.href + "/");
        return (
          <Link
            key={item.href}
            href={item.href}
            className={cn(
              "flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
              active
                ? "bg-primary/10 text-primary"
                : "text-muted-foreground hover:bg-muted hover:text-foreground"
            )}
          >
            <Icon className="size-4" />
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}
