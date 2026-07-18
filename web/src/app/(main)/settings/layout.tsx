"use client";

import { SettingsNav } from "@/components/settings-nav";
import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";

export default function SettingsLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const pathname = usePathname();
  const router = useRouter();
  const legacyOperationsRoutes: Record<string, string> = {
    "/settings/beta-users": "/operations/beta-users",
    "/settings/feedback-admin": "/operations/feedback",
    "/settings/release-notes": "/operations/release-notes",
    "/settings/push-notifications": "/operations/push-notifications",
  };
  const redirectTarget = legacyOperationsRoutes[pathname];

  useEffect(() => {
    if (redirectTarget) router.replace(redirectTarget);
  }, [redirectTarget, router]);

  if (redirectTarget) return null;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">设置</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          管理您的账户、API Key、使用量等
        </p>
      </div>
      <SettingsNav />
      {children}
    </div>
  );
}
