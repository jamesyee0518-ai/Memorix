"use client";

import * as React from "react";
import { cn } from "@/lib/utils";

/**
 * 轻量级 Markdown 渲染组件
 * 支持：标题、粗体、斜体、行内代码、代码块、引用、有序/无序列表、链接、图片、删除线、表格、段落、分隔线
 * 不依赖外部库，避免安装 react-markdown 失败的问题
 */

/** 渲染行内格式：粗体、斜体、行内代码、链接、图片、删除线 */
function renderInline(text: string, keyPrefix: string): React.ReactNode[] {
  const nodes: React.ReactNode[] = [];
  // 使用正则拆分行内元素
  const regex =
    /(\*\*([^*]+)\*\*)|(\*([^*]+)\*)|(`([^`]+)`)|(!\[([^\]]*)\]\(([^)]+)\))|(\[([^\]]+)\]\(([^)]+)\))|(~~([^~]+)~~)/g;
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  let idx = 0;

  while ((match = regex.exec(text)) !== null) {
    // 前面的普通文本
    if (match.index > lastIndex) {
      nodes.push(text.slice(lastIndex, match.index));
    }
    if (match[1]) {
      // 粗体
      nodes.push(
        <strong key={`${keyPrefix}-b-${idx}`} className="font-semibold">
          {match[2]}
        </strong>
      );
    } else if (match[3]) {
      // 斜体
      nodes.push(
        <em key={`${keyPrefix}-i-${idx}`} className="italic">
          {match[4]}
        </em>
      );
    } else if (match[5]) {
      // 行内代码
      nodes.push(
        <code
          key={`${keyPrefix}-c-${idx}`}
          className="rounded bg-muted px-1.5 py-0.5 font-mono text-[0.85em] text-foreground"
        >
          {match[6]}
        </code>
      );
    } else if (match[7]) {
      // 图片
      nodes.push(
        // eslint-disable-next-line @next/next/no-img-element -- Markdown images can be arbitrary remote URLs without known dimensions.
        <img
          key={`${keyPrefix}-img-${idx}`}
          src={match[9]}
          alt={match[8]}
          className="max-w-full rounded-lg"
        />
      );
    } else if (match[10]) {
      // 链接
      nodes.push(
        <a
          key={`${keyPrefix}-a-${idx}`}
          href={match[12]}
          target="_blank"
          rel="noopener noreferrer"
          className="text-primary underline underline-offset-2 hover:text-primary/80"
        >
          {match[11]}
        </a>
      );
    } else if (match[13]) {
      // 删除线
      nodes.push(
        <del
          key={`${keyPrefix}-del-${idx}`}
          className="text-muted-foreground line-through"
        >
          {match[14]}
        </del>
      );
    }
    lastIndex = regex.lastIndex;
    idx++;
  }
  // 剩余文本
  if (lastIndex < text.length) {
    nodes.push(text.slice(lastIndex));
  }
  return nodes;
}

/** 判断一行是否为表格分隔行（如 |---|---| 或 | :---: | ---: |） */
function isTableSeparator(line: string): boolean {
  const t = line.trim();
  // 仅包含管道符、冒号、连字符和空白，且至少包含一个连字符
  return /^\|[\s:|-]*$/.test(t) && t.includes("-");
}

/** 解析表格行，返回各单元格内容（去除首尾管道符后按 | 拆分） */
function parseTableRow(line: string): string[] {
  const t = line.trim();
  const inner = t.replace(/^\|/, "").replace(/\|$/, "");
  return inner.split("|").map((c) => c.trim());
}

export function Markdown({
  content,
  className,
}: {
  content: string;
  className?: string;
}) {
  if (!content) {
    return null;
  }

  const lines = content.split("\n");
  const blocks: React.ReactNode[] = [];
  let i = 0;
  let listItems: string[] = [];
  let listType: "ul" | "ol" | null = null;

  const flushList = (key: string) => {
    if (listItems.length === 0) return;
    if (listType === "ol") {
      blocks.push(
        <ol key={key} className="ml-5 list-decimal space-y-1">
          {listItems.map((item, idx) => (
            <li key={idx}>{renderInline(item, `${key}-${idx}`)}</li>
          ))}
        </ol>
      );
    } else {
      blocks.push(
        <ul key={key} className="ml-5 list-disc space-y-1">
          {listItems.map((item, idx) => (
            <li key={idx}>{renderInline(item, `${key}-${idx}`)}</li>
          ))}
        </ul>
      );
    }
    listItems = [];
    listType = null;
  };

  while (i < lines.length) {
    const line = lines[i];

    // 代码块 ```
    if (line.trim().startsWith("```")) {
      flushList(`list-${i}`);
      const codeLines: string[] = [];
      i++;
      while (i < lines.length && !lines[i].trim().startsWith("```")) {
        codeLines.push(lines[i]);
        i++;
      }
      i++; // 跳过结束 ```
      blocks.push(
        <pre
          key={`code-${i}`}
          className="overflow-x-auto rounded-lg bg-slate-900 p-4 text-sm text-slate-100"
        >
          <code className="font-mono">{codeLines.join("\n")}</code>
        </pre>
      );
      continue;
    }

    // 标题
    const headingMatch = line.match(/^(#{1,6})\s+(.*)$/);
    if (headingMatch) {
      flushList(`list-${i}`);
      const level = headingMatch[1].length;
      const text = headingMatch[2];
      const sizes = [
        "text-xl font-bold",
        "text-lg font-bold",
        "text-base font-semibold",
        "text-sm font-semibold",
        "text-sm font-semibold",
        "text-sm font-semibold",
      ];
      const headingClass = sizes[level - 1] || sizes[5];
      blocks.push(
        React.createElement(
          `h${level}`,
          { key: `h-${i}`, className: cn(headingClass, "mt-3 first:mt-0") },
          renderInline(text, `h-${i}`)
        )
      );
      i++;
      continue;
    }

    // 引用
    if (line.trim().startsWith("> ")) {
      flushList(`list-${i}`);
      const quoteLines: string[] = [line.trim().slice(2)];
      i++;
      while (i < lines.length && lines[i].trim().startsWith("> ")) {
        quoteLines.push(lines[i].trim().slice(2));
        i++;
      }
      blocks.push(
        <blockquote
          key={`quote-${i}`}
          className="border-l-4 border-primary/40 bg-primary/5 py-2 pl-4 text-sm italic"
        >
          {quoteLines.map((q, idx) => (
            <p key={idx}>{renderInline(q, `q-${i}-${idx}`)}</p>
          ))}
        </blockquote>
      );
      continue;
    }

    // 分隔线
    if (/^(-{3,}|\*{3,})$/.test(line.trim())) {
      flushList(`list-${i}`);
      blocks.push(
        <hr key={`hr-${i}`} className="border-t border-border" />
      );
      i++;
      continue;
    }

    // 表格：当前行以 | 开头，且下一行是分隔行
    if (
      line.trim().startsWith("|") &&
      i + 1 < lines.length &&
      isTableSeparator(lines[i + 1])
    ) {
      flushList(`list-${i}`);
      const headers = parseTableRow(line);
      i += 2; // 跳过表头和分隔行
      const rows: string[][] = [];
      while (i < lines.length && lines[i].trim().startsWith("|")) {
        rows.push(parseTableRow(lines[i]));
        i++;
      }
      blocks.push(
        <table key={`table-${i}`} className="w-full border-collapse">
          <thead>
            <tr>
              {headers.map((h, idx) => (
                <th
                  key={idx}
                  className="border border-border px-3 py-2 font-semibold"
                >
                  {renderInline(h, `th-${i}-${idx}`)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, ridx) => (
              <tr key={ridx}>
                {row.map((cell, cidx) => (
                  <td
                    key={cidx}
                    className="border border-border px-3 py-2"
                  >
                    {renderInline(cell, `td-${i}-${ridx}-${cidx}`)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      );
      continue;
    }

    // 有序列表
    const olMatch = line.match(/^\d+\.\s+(.*)$/);
    if (olMatch) {
      if (listType && listType !== "ol") flushList(`list-${i}`);
      listType = "ol";
      listItems.push(olMatch[1]);
      i++;
      continue;
    }

    // 无序列表
    const ulMatch = line.match(/^[-*]\s+(.*)$/);
    if (ulMatch) {
      if (listType && listType !== "ul") flushList(`list-${i}`);
      listType = "ul";
      listItems.push(ulMatch[1]);
      i++;
      continue;
    }

    // 空行
    if (line.trim() === "") {
      flushList(`list-${i}`);
      i++;
      continue;
    }

    // 普通段落
    flushList(`list-${i}`);
    const paraLines: string[] = [line];
    i++;
    while (
      i < lines.length &&
      lines[i].trim() !== "" &&
      !lines[i].trim().startsWith("```") &&
      !lines[i].match(/^(#{1,6})\s+/) &&
      !lines[i].trim().startsWith("> ") &&
      !lines[i].match(/^\d+\.\s+/) &&
      !lines[i].match(/^[-*]\s+/) &&
      !/^(-{3,}|\*{3,})$/.test(lines[i].trim()) &&
      !(
        lines[i].trim().startsWith("|") &&
        i + 1 < lines.length &&
        isTableSeparator(lines[i + 1])
      )
    ) {
      paraLines.push(lines[i]);
      i++;
    }
    blocks.push(
      <p key={`p-${i}`} className="leading-relaxed">
        {renderInline(paraLines.join(" "), `p-${i}`)}
      </p>
    );
  }

  flushList("list-final");

  return (
    <div className={cn("space-y-2 text-sm", className)}>{blocks}</div>
  );
}
