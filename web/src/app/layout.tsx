import type { Metadata } from "next";
import "./globals.css";
import { Providers } from "@/components/providers";

export const metadata: Metadata = {
  title: "Memorix - 个人的 AI 记忆体",
  description: "Memorix 双模式知识引擎 - 本地优先的个人 AI 记忆体",
  icons: {
    icon: "/brand/memorix-mark.svg",
    apple: "/brand/memorix-app-icon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="zh-CN" className="h-full antialiased">
      <body className="min-h-full flex flex-col">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
