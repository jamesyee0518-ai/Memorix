import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Memorix | 你的私人 AI 记忆体",
  description: "Memorix by HiqerTech. 你的私人 AI 记忆体，用于研究工作流、报告和 Agent 记忆。"
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="zh-CN" className="dark">
      <body>{children}</body>
    </html>
  );
}
