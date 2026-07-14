"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Boxes, Loader2, Search } from "lucide-react";
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
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");

  const { data: entities, isLoading } = useQuery({
    queryKey: ["entities", entityTypeFilter, search],
    queryFn: () =>
      entityApi.list({
        entityType: entityTypeFilter !== "all" ? entityTypeFilter : undefined,
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
      <div>
        <h1 className="text-2xl font-bold">实体管理</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          浏览从文档中抽取的实体信息
        </p>
      </div>

      {/* 筛选器 */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>实体列表</CardTitle>
            <div className="flex gap-2">
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
