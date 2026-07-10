"use client";

import { useParams, useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import {
  ArrowLeft,
  Loader2,
  Boxes,
  FileText,
  Calendar,
  Settings2,
} from "lucide-react";
import { entityApi } from "@/lib/api";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { EntityTypeBadge, AiStatusBadge } from "@/components/ai-badge";

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export default function EntityDetailPage() {
  const params = useParams();
  const router = useRouter();
  const entityId = params.id as string;

  const { data: entity, isLoading } = useQuery({
    queryKey: ["entity", entityId],
    queryFn: () => entityApi.get(entityId),
    enabled: !!entityId,
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!entity) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <Boxes className="mb-4 size-12 text-muted-foreground/50" />
        <p className="text-lg font-medium">实体不存在</p>
        <Button
          variant="outline"
          className="mt-4"
          onClick={() => router.back()}
        >
          返回
        </Button>
      </div>
    );
  }

  // 解析元数据
  let metadataObj: Record<string, unknown> | null = null;
  if (entity.metadata) {
    try {
      metadataObj = JSON.parse(entity.metadata);
    } catch {
      metadataObj = null;
    }
  }

  return (
    <div className="space-y-6">
      {/* 返回按钮 */}
      <Button variant="ghost" size="sm" onClick={() => router.back()}>
        <ArrowLeft className="mr-2 size-4" />
        返回
      </Button>

      {/* 实体基本信息 */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between">
            <div className="flex-1">
              <div className="flex items-center gap-3">
                <CardTitle className="text-xl">{entity.name}</CardTitle>
                <EntityTypeBadge entityType={entity.entityType} />
              </div>
              {entity.normalizedName && (
                <CardDescription className="mt-1">
                  规范化名称: {entity.normalizedName}
                </CardDescription>
              )}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* 描述 */}
          {entity.description && (
            <div>
              <p className="mb-1 text-xs font-medium text-muted-foreground">
                描述
              </p>
              <p className="text-sm leading-relaxed">{entity.description}</p>
            </div>
          )}

          {/* 基本信息网格 */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <Calendar className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  创建时间
                </p>
                <p className="mt-0.5 text-sm">{formatDate(entity.createdAt)}</p>
              </div>
            </div>
            <div className="flex items-center gap-3 rounded-lg border p-3">
              <Calendar className="size-4 text-muted-foreground" />
              <div>
                <p className="text-xs font-medium text-muted-foreground">
                  更新时间
                </p>
                <p className="mt-0.5 text-sm">{formatDate(entity.updatedAt)}</p>
              </div>
            </div>
          </div>

          {/* 元数据 */}
          {metadataObj && (
            <div>
              <p className="mb-2 flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
                <Settings2 className="size-3.5" />
                元数据
              </p>
              <div className="rounded-lg border bg-muted/30 p-3">
                <pre className="whitespace-pre-wrap text-sm">
                  {JSON.stringify(metadataObj, null, 2)}
                </pre>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* 关联文档列表 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <FileText className="size-4 text-blue-600" />
            关联文档
            <span className="text-sm font-normal text-muted-foreground">
              ({entity.relatedDocuments.length})
            </span>
          </CardTitle>
        </CardHeader>
        <CardContent>
          {entity.relatedDocuments.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <FileText className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无关联文档
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>AI状态</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {entity.relatedDocuments.map((doc) => (
                  <TableRow key={doc.id}>
                    <TableCell>
                      <Link
                        href={`/documents/${doc.id}`}
                        className="truncate font-medium text-primary hover:underline"
                      >
                        {doc.title || "未命名"}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <AiStatusBadge status={doc.aiStatus} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
