"use client";

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Check, Loader2, Plus, Trash2 } from "lucide-react";
import { entityApi } from "@/lib/api";
import type { EntityAlias } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export function EntityAliasEditor({
  entityId,
  aliases,
  onChanged,
}: {
  entityId: string;
  aliases: EntityAlias[];
  onChanged: () => void;
}) {
  const [value, setValue] = useState("");
  const add = useMutation({
    mutationFn: () => entityApi.addAlias(entityId, {
      alias: value.trim(),
      aliasType: "MANUAL",
      isVerified: true,
    }),
    onSuccess: () => { setValue(""); onChanged(); },
  });
  const remove = useMutation({
    mutationFn: (aliasId: string) => entityApi.deleteAlias(entityId, aliasId),
    onSuccess: onChanged,
  });
  return <div className="space-y-3">
    <div className="flex gap-2">
      <Input value={value} onChange={(event) => setValue(event.target.value)} placeholder="添加中文名、英文名或缩写" onKeyDown={(event) => { if (event.key === "Enter" && value.trim()) add.mutate(); }} />
      <Button onClick={() => add.mutate()} disabled={!value.trim() || add.isPending}>{add.isPending ? <Loader2 className="size-4 animate-spin" /> : <Plus className="size-4" />}添加</Button>
    </div>
    <div className="flex flex-wrap gap-2">
      {aliases.map((alias) => <span key={alias.id} className="inline-flex items-center gap-1 rounded-full border bg-muted/30 py-1 pl-2.5 pr-1 text-xs">
        {alias.alias}
        {alias.isVerified && <Check className="size-3 text-emerald-600" />}
        <button type="button" className="rounded-full p-1 text-muted-foreground hover:bg-destructive/10 hover:text-destructive" title="删除别名" onClick={() => remove.mutate(alias.id)} disabled={remove.isPending}><Trash2 className="size-3" /></button>
      </span>)}
      {aliases.length === 0 && <span className="text-xs text-muted-foreground">暂无别名</span>}
    </div>
  </div>;
}
