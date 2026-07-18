"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Users, MessagesSquare, Rocket, Bell } from "lucide-react";
import { useAuthStore } from "@/stores/auth-store";
import { cn } from "@/lib/utils";

const operationsNav = [
  { href: "/operations/beta-users", label: "内测用户", icon: Users },
  { href: "/operations/feedback", label: "反馈管理", icon: MessagesSquare },
  { href: "/operations/release-notes", label: "版本说明", icon: Rocket },
  { href: "/operations/push-notifications", label: "推送审计", icon: Bell },
];

export default function OperationsLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const role = useAuthStore((state) => state.user?.role ?? "user");
  const [isDesktop, setIsDesktop] = useState(false);
  const [environmentChecked, setEnvironmentChecked] = useState(false);
  const canOperate = role === "platform_admin" || role === "operator";

  useEffect(() => {
    const desktop = "__TAURI_INTERNALS__" in window;
    setIsDesktop(desktop);
    setEnvironmentChecked(true);
    if (desktop || !canOperate) router.replace(desktop ? "/dashboard" : "/settings");
  }, [canOperate, router]);

  if (!environmentChecked || isDesktop || !canOperate) return null;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">运营中心</h1>
        <p className="mt-1 text-sm text-muted-foreground">管理内测、用户反馈、版本发布与推送记录</p>
      </div>
      <nav className="flex flex-wrap gap-1 border-b pb-3">
        {operationsNav.map((item) => {
          const Icon = item.icon;
          const active = pathname === item.href || pathname.startsWith(`${item.href}/`);
          return (
            <Link key={item.href} href={item.href} className={cn(
              "flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
              active ? "bg-primary/10 text-primary" : "text-muted-foreground hover:bg-muted hover:text-foreground"
            )}>
              <Icon className="size-4" />{item.label}
            </Link>
          );
        })}
      </nav>
      {children}
    </div>
  );
}
