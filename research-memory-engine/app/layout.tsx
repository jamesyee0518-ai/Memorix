import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Memorix｜本地优先的多语言 AI 知识资产引擎",
  description: "导入网页、PDF、图片、录音与笔记，自动识别多语言内容并建立中文索引。用中文检索和问答全球资料，生成报告，并通过 MCP 接入 Agent。"
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="zh-CN" className="dark">
      <body>{children}</body>
    </html>
  );
}
