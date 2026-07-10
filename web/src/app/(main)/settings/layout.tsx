import { SettingsNav } from "@/components/settings-nav";

export default function SettingsLayout({
  children,
}: {
  children: React.ReactNode;
}) {
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
