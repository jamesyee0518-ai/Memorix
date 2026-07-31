"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  Activity,
  CreditCard,
  LayoutDashboard,
  ReceiptText,
  Tags,
} from "lucide-react";
import { cn } from "@/lib/utils";

const items = [
  { href: "/billing", label: "概览", icon: LayoutDashboard, exact: true },
  { href: "/billing/usage", label: "用量", icon: Activity },
  { href: "/billing/recharge", label: "充值", icon: CreditCard },
  { href: "/billing/bills", label: "账单", icon: ReceiptText },
  { href: "/billing/pricing", label: "价格", icon: Tags },
];

export function BillingNav() {
  const pathname = usePathname();

  return (
    <nav className="flex flex-wrap gap-1 border-b pb-3">
      {items.map((item) => {
        const active = item.exact
          ? pathname === item.href
          : pathname === item.href || pathname.startsWith(`${item.href}/`);
        const Icon = item.icon;

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
