"use client";

import { useState } from "react";
import {
  BookOpen,
  KeyRound,
  Shield,
  AlertCircle,
  FolderOpen,
  Search,
  MessageCircle,
  FileText,
  ClipboardList,
} from "lucide-react";
import { CodeBlock } from "@/components/code-block";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { API_BASE_URL } from "@/lib/api";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const API_BASE = API_BASE_URL;

// 错误码表格数据
const errorCodes = [
  { code: "SUCCESS", httpStatus: "200", description: "请求成功" },
  { code: "VALIDATION_ERROR", httpStatus: "400", description: "请求参数验证失败" },
  { code: "UNAUTHORIZED", httpStatus: "401", description: "未授权或认证失败" },
  { code: "FORBIDDEN", httpStatus: "403", description: "没有权限访问该资源" },
  { code: "NOT_FOUND", httpStatus: "404", description: "请求的资源不存在" },
  { code: "CONFLICT", httpStatus: "409", description: "资源冲突" },
  { code: "RATE_LIMITED", httpStatus: "429", description: "请求频率超限" },
  { code: "INTERNAL_ERROR", httpStatus: "500", description: "服务器内部错误" },
];

// 接口定义
interface ApiEndpoint {
  id: string;
  icon: typeof FolderOpen;
  title: string;
  method: "GET" | "POST";
  path: string;
  description: string;
  params?: { name: string; type: string; required: boolean; description: string }[];
  bodyExample?: string;
  responseExample: string;
}

const endpoints: ApiEndpoint[] = [
  {
    id: "topics",
    icon: FolderOpen,
    title: "Topics API - 获取专题列表",
    method: "GET",
    path: "/api/agent/topics",
    description: "获取当前 API Key 可访问的专题列表。",
    responseExample: `{
  "success": true,
  "data": {
    "items": [
      {
        "id": "topic_abc123",
        "name": "AI 产业研究",
        "description": "人工智能产业动态研究",
        "domain": "artificial-intelligence",
        "documentCount": 42,
        "createdAt": "2025-01-15T08:00:00Z"
      }
    ],
    "total": 1
  }
}`,
  },
  {
    id: "search",
    icon: Search,
    title: "Search API - 搜索文档",
    method: "POST",
    path: "/api/agent/search",
    description: "在指定专题中搜索相关文档片段，支持关键词、向量和混合搜索。",
    params: [
      { name: "topicId", type: "string", required: true, description: "专题 ID" },
      { name: "query", type: "string", required: true, description: "搜索关键词" },
      { name: "searchType", type: "string", required: false, description: "搜索类型：keyword | vector | hybrid（默认 hybrid）" },
      { name: "limit", type: "number", required: false, description: "返回结果数量，默认 10，最大 50" },
    ],
    bodyExample: `{
  "topicId": "topic_abc123",
  "query": "大模型推理优化",
  "searchType": "hybrid",
  "limit": 5
}`,
    responseExample: `{
  "success": true,
  "data": {
    "query": "大模型推理优化",
    "searchType": "hybrid",
    "total": 5,
    "items": [
      {
        "documentId": "doc_xyz789",
        "chunkId": "chunk_001",
        "title": "LLM 推理加速技术综述",
        "snippet": "本文综述了当前主流的大模型推理优化方法...",
        "sourceType": "url",
        "sourceDomain": "arxiv.org",
        "score": 0.92
      }
    ]
  }
}`,
  },
  {
    id: "qa",
    icon: MessageCircle,
    title: "QA API - 智能问答",
    method: "POST",
    path: "/api/agent/qa",
    description: "基于专题知识库回答问题，返回带引用来源的答案。",
    params: [
      { name: "topicId", type: "string", required: true, description: "专题 ID" },
      { name: "question", type: "string", required: true, description: "用户问题" },
      { name: "topK", type: "number", required: false, description: "检索文档数量，默认 5" },
    ],
    bodyExample: `{
  "topicId": "topic_abc123",
  "question": "当前大模型推理优化有哪些主要方向？",
  "topK": 5
}`,
    responseExample: `{
  "success": true,
  "data": {
    "answer": "当前大模型推理优化主要分为以下几个方向：\\n1. 量化技术...\\n2. KV Cache 优化...\\n3. 注意力机制优化...",
    "citations": [
      {
        "index": 1,
        "documentId": "doc_xyz789",
        "title": "LLM 推理加速技术综述",
        "sourceUrl": "https://arxiv.org/...",
        "snippet": "量化技术通过降低模型参数精度..."
      }
    ],
    "retrieval": {
      "retrievedCount": 5,
      "usedCount": 3
    }
  }
}`,
  },
  {
    id: "document",
    icon: FileText,
    title: "Document API - 获取文档详情",
    method: "GET",
    path: "/api/agent/documents/{id}",
    description: "获取指定文档的详细内容，包括 AI 生成的摘要、关键点等。",
    params: [
      { name: "id", type: "string", required: true, description: "文档 ID（路径参数）" },
    ],
    responseExample: `{
  "success": true,
  "data": {
    "id": "doc_xyz789",
    "title": "LLM 推理加速技术综述",
    "summary": "本文综述了量化、KV Cache 优化等推理加速方法...",
    "contentMarkdown": "# LLM 推理加速技术综述\\n\\n## 1. 引言...",
    "keyPoints": "1. 量化技术可减少 50% 以上显存占用\\n2. KV Cache 优化显著降低延迟",
    "valueScore": 85,
    "wordCount": 3200,
    "createdAt": "2025-01-10T12:00:00Z"
  }
}`,
  },
  {
    id: "documents-chunks",
    icon: FileText,
    title: "Document Chunks API - 获取文档分块",
    method: "GET",
    path: "/api/agent/documents/{id}/chunks",
    description: "获取指定文档的分块列表，用于精确检索文档片段。",
    params: [
      { name: "id", type: "string", required: true, description: "文档 ID（路径参数）" },
    ],
    responseExample: `{
  "success": true,
  "data": {
    "items": [
      {
        "chunkId": "chunk_001",
        "content": "大模型推理优化是当前 AI 领域的重要研究方向...",
        "tokenCount": 128,
        "position": 0
      }
    ],
    "total": 12
  }
}`,
  },
  {
    id: "reports",
    icon: ClipboardList,
    title: "Reports API - 获取报告列表",
    method: "GET",
    path: "/api/agent/reports",
    description: "获取报告列表，支持按专题筛选。",
    params: [
      { name: "topicId", type: "string", required: false, description: "专题 ID（查询参数，用于筛选）" },
    ],
    responseExample: `{
  "success": true,
  "data": {
    "items": [
      {
        "id": "report_001",
        "topicId": "topic_abc123",
        "reportType": "daily",
        "title": "2025-01-15 日报",
        "status": "done",
        "qualityScore": 88,
        "createdAt": "2025-01-15T18:00:00Z"
      }
    ],
    "total": 1
  }
}`,
  },
  {
    id: "report-detail",
    icon: ClipboardList,
    title: "Report Detail API - 获取报告详情",
    method: "GET",
    path: "/api/agent/reports/{id}",
    description: "获取指定报告的完整内容，包括 Markdown 正文和引用来源。",
    params: [
      { name: "id", type: "string", required: true, description: "报告 ID（路径参数）" },
    ],
    responseExample: `{
  "success": true,
  "data": {
    "id": "report_001",
    "title": "2025-01-15 日报",
    "reportType": "daily",
    "contentMarkdown": "# 每日研究动态\\n\\n## 核心发现...",
    "citations": [
      {
        "index": 1,
        "documentId": "doc_xyz789",
        "title": "LLM 推理加速技术综述",
        "snippet": "..."
      }
    ],
    "status": "done",
    "qualityScore": 88,
    "createdAt": "2025-01-15T18:00:00Z"
  }
}`,
  },
];

const methodColors: Record<string, string> = {
  GET: "bg-blue-100 text-blue-700",
  POST: "bg-green-100 text-green-700",
};

export default function ApiDocsPage() {
  const [activeEndpoint, setActiveEndpoint] = useState(endpoints[0].id);

  const current = endpoints.find((e) => e.id === activeEndpoint)!;

  // 生成示例代码
  const generateCurl = (ep: ApiEndpoint): string => {
    const fullUrl = `${API_BASE}${ep.path.replace(/\{[^}]+\}/g, "REPLACE_WITH_ID")}`;
    if (ep.method === "GET") {
      const queryStr = ep.params?.some((p) => !p.required && p.name !== "id")
        ? ` \\${ep.params
            ?.filter((p) => !p.required && p.name !== "id")
            .map((p) => `\n  -G ${fullUrl} \\\n  -d "${p.name}=VALUE"`)
            .join("")}`
        : "";
      return `curl -X GET "${fullUrl}" \\
  -H "Authorization: Bearer YOUR_API_KEY" \\
  -H "Content-Type: application/json"${queryStr}`;
    }
    return `curl -X POST "${fullUrl}" \\
  -H "Authorization: Bearer YOUR_API_KEY" \\
  -H "Content-Type: application/json" \\
  -d '${ep.bodyExample ?? "{}"}'`;
  };

  const generateNode = (ep: ApiEndpoint): string => {
    const fullUrl = `${API_BASE}${ep.path.replace(/\{[^}]+\}/g, "REPLACE_WITH_ID")}`;
    if (ep.method === "GET") {
      return `const res = await fetch("${fullUrl}", {
  method: "GET",
  headers: {
    "Authorization": "Bearer YOUR_API_KEY",
    "Content-Type": "application/json",
  },
});

const data = await res.json();
console.log(data);`;
    }
    return `const res = await fetch("${fullUrl}", {
  method: "POST",
  headers: {
    "Authorization": "Bearer YOUR_API_KEY",
    "Content-Type": "application/json",
  },
  body: JSON.stringify(${ep.bodyExample ?? "{}"}),
});

const data = await res.json();
console.log(data);`;
  };

  const generatePython = (ep: ApiEndpoint): string => {
    const fullUrl = `${API_BASE}${ep.path.replace(/\{[^}]+\}/g, "REPLACE_WITH_ID")}`;
    if (ep.method === "GET") {
      return `import requests

url = "${fullUrl}"
headers = {
    "Authorization": "Bearer YOUR_API_KEY",
    "Content-Type": "application/json",
}

response = requests.get(url, headers=headers)
print(response.json())`;
    }
    return `import requests

url = "${fullUrl}"
headers = {
    "Authorization": "Bearer YOUR_API_KEY",
    "Content-Type": "application/json",
}
payload = ${ep.bodyExample ?? "{}"}

response = requests.post(url, json=payload, headers=headers)
print(response.json())`;
  };

  return (
    <div className="space-y-6">
      <div>
        <h2 className="flex items-center gap-2 text-lg font-semibold">
          <BookOpen className="size-5" />
          API 文档
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Agent API 接口文档与使用说明
        </p>
      </div>

      {/* 鉴权方式 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Shield className="size-4 text-blue-600" />
            鉴权方式
          </CardTitle>
          <CardDescription>Agent API 使用 Bearer Token 鉴权</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-sm text-muted-foreground">
            所有 Agent API 请求都需要在 HTTP Header 中携带 API Key 进行身份验证：
          </p>
          <CodeBlock
            title="Authorization Header"
            code={`Authorization: Bearer YOUR_API_KEY`}
          />
          <div className="rounded-lg border border-blue-200 bg-blue-50 p-3">
            <p className="text-sm text-blue-800">
              请将 <code className="rounded bg-blue-100 px-1">YOUR_API_KEY</code> 替换为您创建的 API Key。
              API Key 可在「API Key 管理」页面创建。
            </p>
          </div>
        </CardContent>
      </Card>

      {/* API Key 创建说明 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <KeyRound className="size-4 text-green-600" />
            创建 API Key
          </CardTitle>
          <CardDescription>如何获取您的 API Key</CardDescription>
        </CardHeader>
        <CardContent className="space-y-2">
          <ol className="list-inside list-decimal space-y-1.5 text-sm text-muted-foreground">
            <li>前往「设置 -&gt; API Key 管理」页面</li>
            <li>点击「创建 API Key」按钮</li>
            <li>填写名称、选择权限范围和可访问专题</li>
            <li>创建成功后，立即复制并保存明文 Key（仅显示一次）</li>
            <li>在 API 请求的 Header 中使用该 Key</li>
          </ol>
          <div className="mt-3 rounded-lg border border-amber-200 bg-amber-50 p-3">
            <p className="flex items-start gap-2 text-sm text-amber-800">
              <AlertCircle className="mt-0.5 size-4 shrink-0" />
              <span>
                API Key 明文仅在创建时显示一次，请务必妥善保存。如遗失需重新创建。
              </span>
            </p>
          </div>
        </CardContent>
      </Card>

      {/* 错误码说明 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <AlertCircle className="size-4 text-orange-600" />
            错误码说明
          </CardTitle>
          <CardDescription>API 返回的错误码及含义</CardDescription>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>错误码</TableHead>
                <TableHead>HTTP 状态码</TableHead>
                <TableHead>说明</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {errorCodes.map((err) => (
                <TableRow key={err.code}>
                  <TableCell>
                    <code className="font-mono text-xs">{err.code}</code>
                  </TableCell>
                  <TableCell>{err.httpStatus}</TableCell>
                  <TableCell className="text-muted-foreground">
                    {err.description}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {/* Agent API 接口文档 */}
      <div>
        <h3 className="mb-3 text-sm font-medium text-muted-foreground">
          Agent API 接口
        </h3>

        {/* 接口列表 */}
        <div className="mb-4 flex flex-wrap gap-2">
          {endpoints.map((ep) => {
            const Icon = ep.icon;
            const active = ep.id === activeEndpoint;
            return (
              <button
                key={ep.id}
                onClick={() => setActiveEndpoint(ep.id)}
                className={`flex items-center gap-2 rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors ${
                  active
                    ? "border-primary bg-primary/10 text-primary"
                    : "border-border text-muted-foreground hover:bg-muted"
                }`}
              >
                <Icon className="size-3.5" />
                {ep.title.split(" - ")[1] ?? ep.title}
              </button>
            );
          })}
        </div>

        {/* 当前接口详情 */}
        <Card>
          <CardHeader>
            <div className="flex items-center gap-3">
              <Badge className={methodColors[current.method]}>
                {current.method}
              </Badge>
              <code className="font-mono text-sm font-semibold">
                {current.path}
              </code>
            </div>
            <CardDescription className="mt-1">
              {current.description}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {/* 请求参数 */}
            {current.params && current.params.length > 0 && (
              <div>
                <h4 className="mb-2 text-sm font-semibold">请求参数</h4>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>参数名</TableHead>
                      <TableHead>类型</TableHead>
                      <TableHead>必填</TableHead>
                      <TableHead>说明</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {current.params.map((param) => (
                      <TableRow key={param.name}>
                        <TableCell>
                          <code className="font-mono text-xs">
                            {param.name}
                          </code>
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          {param.type}
                        </TableCell>
                        <TableCell>
                          {param.required ? (
                            <Badge variant="destructive">是</Badge>
                          ) : (
                            <Badge variant="secondary">否</Badge>
                          )}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          {param.description}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}

            {/* 请求体示例 */}
            {current.bodyExample && (
              <div>
                <h4 className="mb-2 text-sm font-semibold">请求体示例</h4>
                <CodeBlock title="JSON" code={current.bodyExample} />
              </div>
            )}

            {/* 返回示例 */}
            <div>
              <h4 className="mb-2 text-sm font-semibold">返回示例</h4>
              <CodeBlock title="Response" code={current.responseExample} />
            </div>

            {/* 代码示例 */}
            <div>
              <h4 className="mb-2 text-sm font-semibold">代码示例</h4>
              <div className="space-y-3">
                <div>
                  <p className="mb-1.5 text-xs font-medium text-muted-foreground">
                    curl
                  </p>
                  <CodeBlock title="curl" code={generateCurl(current)} />
                </div>
                <div>
                  <p className="mb-1.5 text-xs font-medium text-muted-foreground">
                    Node.js
                  </p>
                  <CodeBlock title="Node.js" code={generateNode(current)} />
                </div>
                <div>
                  <p className="mb-1.5 text-xs font-medium text-muted-foreground">
                    Python
                  </p>
                  <CodeBlock title="Python" code={generatePython(current)} />
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
