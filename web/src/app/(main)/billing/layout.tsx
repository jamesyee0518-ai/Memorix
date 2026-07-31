import { BillingNav } from "@/components/billing/billing-nav";

export default function BillingLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-6">
      <BillingNav />
      {children}
    </div>
  );
}
