"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Loader2, Star, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { marketplaceApi, ApiRequestError } from "@/lib/api";
import type { ProviderMarketplaceEntry } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

function StarRating({
  value,
  onRate,
  disabled,
}: {
  value: number;
  onRate?: (rating: number) => void;
  disabled?: boolean;
}) {
  const [hover, setHover] = useState(0);
  const display = hover || value;
  return (
    <div className="flex items-center gap-0.5">
      {[1, 2, 3, 4, 5].map((star) => (
        <button
          key={star}
          type="button"
          disabled={disabled}
          onMouseEnter={() => setHover(star)}
          onMouseLeave={() => setHover(0)}
          onClick={() => onRate?.(star)}
          className="disabled:cursor-default"
        >
          <Star
            className={cn(
              "size-4 transition-colors",
              star <= display ? "fill-amber-400 text-amber-400" : "text-muted-foreground"
            )}
          />
        </button>
      ))}
    </div>
  );
}

export default function MarketplacePage() {
  const queryClient = useQueryClient();

  const entries = useQuery({
    queryKey: ["marketplace"],
    queryFn: () => marketplaceApi.browse(),
  });

  const install = useMutation({
    mutationFn: (id: string) => marketplaceApi.install(id),
    onSuccess: () => {
      toast.success("安装成功");
      queryClient.invalidateQueries({ queryKey: ["marketplace"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "安装失败"),
  });

  const uninstall = useMutation({
    mutationFn: (id: string) => marketplaceApi.uninstall(id),
    onSuccess: () => {
      toast.success("已卸载");
      queryClient.invalidateQueries({ queryKey: ["marketplace"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "卸载失败"),
  });

  const rate = useMutation({
    mutationFn: ({ id, rating }: { id: string; rating: number }) =>
      marketplaceApi.rate(id, rating),
    onSuccess: () => {
      toast.success("评分已提交");
      queryClient.invalidateQueries({ queryKey: ["marketplace"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "评分失败"),
  });

  return (
    <div className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold">提供商市场</h2>
        <p className="text-sm text-muted-foreground">浏览并安装音频与 AI 提供商</p>
      </div>

      {entries.isLoading ? (
        <div className="flex justify-center py-16">
          <Loader2 className="size-6 animate-spin text-muted-foreground" />
        </div>
      ) : (entries.data?.length ?? 0) === 0 ? (
        <div className="py-16 text-center text-sm text-muted-foreground">
          暂无可用提供商
        </div>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {entries.data!.map((entry: ProviderMarketplaceEntry) => (
            <Card key={entry.id} className="flex flex-col">
              <CardHeader>
                <div className="flex items-start justify-between gap-2">
                  <div className="flex items-center gap-3">
                    {entry.iconUrl ? (
                      <img
                        src={entry.iconUrl}
                        alt={entry.displayName}
                        className="size-10 rounded-lg object-cover"
                      />
                    ) : (
                      <div className="flex size-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
                        <Download className="size-5" />
                      </div>
                    )}
                    <div>
                      <CardTitle className="text-base">{entry.displayName}</CardTitle>
                      <Badge variant="outline" className="mt-1">{entry.capability}</Badge>
                    </div>
                  </div>
                </div>
              </CardHeader>
              <CardContent className="flex flex-1 flex-col gap-3">
                <p className="flex-1 text-sm text-muted-foreground line-clamp-3">
                  {entry.description ?? "暂无描述"}
                </p>
                <div className="flex items-center justify-between">
                  <StarRating
                    value={Math.round(entry.rating)}
                    onRate={(rating) => rate.mutate({ id: entry.id, rating })}
                    disabled={rate.isPending}
                  />
                  <span className="text-xs text-muted-foreground">
                    {entry.installCount} 次安装
                  </span>
                </div>
                {entry.isInstalled ? (
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={uninstall.isPending}
                    onClick={() => uninstall.mutate(entry.id)}
                  >
                    <Trash2 className="mr-2 size-3.5" />
                    卸载
                  </Button>
                ) : (
                  <Button
                    size="sm"
                    disabled={install.isPending}
                    onClick={() => install.mutate(entry.id)}
                  >
                    {install.isPending ? (
                      <Loader2 className="mr-2 size-3.5 animate-spin" />
                    ) : (
                      <Download className="mr-2 size-3.5" />
                    )}
                    安装
                  </Button>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
