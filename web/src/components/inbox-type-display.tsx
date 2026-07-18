import {
  AudioLines,
  File,
  FileText,
  Files,
  ImageIcon,
  Link2,
  type LucideIcon,
} from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

export type InboxTypeMeta = {
  key: string;
  label: string;
  detail: string;
  processingText: string;
  badgeClassName: string;
  iconClassName: string;
  Icon: LucideIcon;
};

type InboxDisplayInput = {
  inputType?: string;
  itemType?: string;
  fileName?: string;
  mimeType?: string;
};

const EXTENSION_TYPE_MAP: Record<string, "image" | "audio" | "pdf" | "file"> = {
  jpg: "image",
  jpeg: "image",
  png: "image",
  gif: "image",
  webp: "image",
  heic: "image",
  heif: "image",
  wav: "audio",
  mp3: "audio",
  m4a: "audio",
  aac: "audio",
  flac: "audio",
  ogg: "audio",
  pdf: "pdf",
};

function inferFileKind(fileName?: string, mimeType?: string): "image" | "audio" | "pdf" | "file" {
  if (mimeType?.startsWith("image/")) return "image";
  if (mimeType?.startsWith("audio/")) return "audio";
  if (mimeType === "application/pdf") return "pdf";

  const extension = fileName?.split(".").pop()?.toLowerCase();
  return extension ? EXTENSION_TYPE_MAP[extension] ?? "file" : "file";
}

export function getInboxTypeMeta(input: InboxDisplayInput): InboxTypeMeta {
  const rawType = (input.inputType || input.itemType || "").toLowerCase();
  const type = rawType === "file" ? inferFileKind(input.fileName, input.mimeType) : rawType;

  switch (type) {
    case "text":
    case "note":
      return {
        key: type || "text",
        label: type === "note" ? "笔记" : "文本",
        detail: "可直接导入为文本资料",
        processingText: "导入后会进入清洗、切块和索引流程。",
        badgeClassName: "bg-slate-100 text-slate-700",
        iconClassName: "text-slate-600",
        Icon: FileText,
      };
    case "url":
      return {
        key: "url",
        label: "链接",
        detail: "导入后抓取网页内容",
        processingText: "导入后会抓取网页、抽取正文，并生成可检索资料。",
        badgeClassName: "bg-blue-100 text-blue-700",
        iconClassName: "text-blue-600",
        Icon: Link2,
      };
    case "image":
      return {
        key: "image",
        label: "图片",
        detail: "等待 OCR 和摘要",
        processingText: "导入后会先做 OCR 识别，再进入摘要、切块和索引流程。",
        badgeClassName: "bg-emerald-100 text-emerald-700",
        iconClassName: "text-emerald-600",
        Icon: ImageIcon,
      };
    case "audio":
      return {
        key: "audio",
        label: "录音",
        detail: "等待转写和摘要",
        processingText: "导入后会先做语音转写，再进入摘要、切块和索引流程。",
        badgeClassName: "bg-violet-100 text-violet-700",
        iconClassName: "text-violet-600",
        Icon: AudioLines,
      };
    case "pdf":
      return {
        key: "pdf",
        label: "PDF",
        detail: "等待文档解析",
        processingText: "导入后会解析 PDF 文本和结构，再进入切块与索引流程。",
        badgeClassName: "bg-rose-100 text-rose-700",
        iconClassName: "text-rose-600",
        Icon: FileText,
      };
    case "mixed":
      return {
        key: "mixed",
        label: "混合",
        detail: "包含多种输入",
        processingText: "导入前建议确认内容，系统会按附件类型分别解析再汇总。",
        badgeClassName: "bg-cyan-100 text-cyan-700",
        iconClassName: "text-cyan-600",
        Icon: Files,
      };
    case "file":
      return {
        key: "file",
        label: "文件",
        detail: "等待文件解析",
        processingText: "导入后会解析文件内容，再进入清洗、切块和索引流程。",
        badgeClassName: "bg-amber-100 text-amber-700",
        iconClassName: "text-amber-600",
        Icon: File,
      };
    default:
      return {
        key: rawType || "unknown",
        label: rawType || "未知",
        detail: "等待识别",
        processingText: "导入后会根据内容类型进入相应处理流程。",
        badgeClassName: "bg-muted text-muted-foreground",
        iconClassName: "text-muted-foreground",
        Icon: File,
      };
  }
}

export function InboxTypeBadge({
  inputType,
  itemType,
  fileName,
  mimeType,
  className,
}: InboxDisplayInput & { className?: string }) {
  const meta = getInboxTypeMeta({ inputType, itemType, fileName, mimeType });
  const Icon = meta.Icon;

  return (
    <Badge className={cn(meta.badgeClassName, className)}>
      <Icon className="mr-1 size-3" />
      {meta.label}
    </Badge>
  );
}

export function InboxProcessingHint({
  inputType,
  itemType,
  fileName,
  mimeType,
  status,
  compact = false,
  className,
}: InboxDisplayInput & {
  status?: string;
  compact?: boolean;
  className?: string;
}) {
  const meta = getInboxTypeMeta({ inputType, itemType, fileName, mimeType });

  if (status === "imported" || status === "done") {
    return (
      <p className={cn("text-xs text-green-700", className)}>
        已完成：{meta.label}内容已进入资料库。
      </p>
    );
  }

  if (status === "failed") {
    return (
      <p className={cn("text-xs text-red-600", className)}>
        处理失败：可重试或先编辑内容后再导入。
      </p>
    );
  }

  return (
    <p className={cn("text-xs text-muted-foreground", className)}>
      {compact ? meta.detail : meta.processingText}
    </p>
  );
}
