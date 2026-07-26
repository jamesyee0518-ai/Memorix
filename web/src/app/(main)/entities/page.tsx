"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Boxes, History, Loader2, Search, ShieldCheck } from "lucide-react";
import { entityApi } from "@/lib/api";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Input } from "@/components/ui/input";
import { buttonVariants } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { EntityTypeBadge, ENTITY_TYPES, getEntityTypeLabel } from "@/components/ai-badge";

export default function EntitiesPage() {
  const router = useRouter();
  const [entityTypeFilter, setEntityTypeFilter] = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<string>("active");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");

  const { data: entities, isLoading } = useQuery({
    queryKey: ["entities", entityTypeFilter, statusFilter, search],
    queryFn: () =>
      entityApi.list({
        entityType: entityTypeFilter !== "all" ? entityTypeFilter : undefined,
        status: statusFilter !== "all" ? statusFilter : undefined,
        search: search || undefined,
      }),
  });

  const handleSearch = () => {
    setSearch(searchInput);
  };

  const displayEntities = entities?.items ?? [];

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">实体管理</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            浏览从文档中抽取的实体信息
          </p>
        </div>
        <div className="flex gap-2">
          <Link className={buttonVariants({ variant: "outline", size: "sm" })} href="/entities/merge-history"><History className="mr-2 size-4" />合并历史</Link>
          <Link className={buttonVariants({ size: "sm" })} href="/entities/governance"><ShieldCheck className="mr-2 size-4" />实体治理</Link>
        </div>
      </div>

      {/* 筛选器 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>实体列表</CardTitle>
            <div className="flex gap-2">
              <Select value={statusFilter} onValueChange={(value) => value && setStatusFilter(value)}>
                <SelectTrigger size="sm" className="w-32"><SelectValue>{statusFilter === "active" ? "标准实体" : statusFilter === "merged" ? "已合并实体" : "全部状态"}</SelectValue></SelectTrigger>
                <SelectContent><SelectItem value="active">标准实体</SelectItem><SelectItem value="merged">已合并实体</SelectItem><SelectItem value="all">全部状态</SelectItem></SelectContent>
              </Select>
              <div className="relative">
                <Input
                  placeholder="搜索实体..."
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  onKeyDown={(e) => e.key === "Enter" && handleSearch()}
                  className="w-48 pr-8"
                />
                <Search className="absolute right-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              </div>
              <Select
                value={entityTypeFilter}
                onValueChange={(v) => setEntityTypeFilter(v as string)}
              >
                <SelectTrigger size="sm" className="w-32">
                  <SelectValue placeholder="类型筛选">
                    {entityTypeFilter === "all"
                      ? "全部类型"
                      : getEntityTypeLabel(entityTypeFilter)}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部类型</SelectItem>
                  {ENTITY_TYPES.map((type) => (
                    <SelectItem key={type} value={type}>
                      {getEntityTypeLabel(type)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : displayEntities.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <Boxes className="mb-3 size-10 text-muted-foreground/50" />
              <p className="text-sm text-muted-foreground">
                暂无实体，AI 处理文档后将自动抽取实体
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>类型</TableHead>
                  <TableHead>描述</TableHead>
                  <TableHead>关联文档数</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayEntities.map((entity) => (
                  <TableRow
                    key={entity.id}
                    className="cursor-pointer"
                    onClick={() => router.push(`/entities/${entity.id}`)}
                  >
                    <TableCell className="font-medium">
                      {entity.name}
                    </TableCell>
                    <TableCell>
                      <EntityTypeBadge entityType={entity.entityType} />
                    </TableCell>
                    <TableCell className="max-w-md">
                      <span className="line-clamp-1 text-muted-foreground">
                        {entity.description || "-"}
                      </span>
                    </TableCell>
                    <TableCell>
                      <span className="font-medium">
                        {entity.documentCount}
                      </span>
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
