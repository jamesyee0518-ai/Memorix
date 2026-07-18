"use client";

import { useCallback, useEffect, useState } from "react";
import { Languages, Loader2, Pencil, Plus, Search, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { terminologyApi, ApiRequestError } from "@/lib/api";
import type { Terminology } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

type TermInput = Omit<Terminology, "id" | "createdAt" | "updatedAt">;

const emptyTerm: TermInput = {
  sourceLanguage: "en",
  sourceTerm: "",
  targetLanguage: "zh-CN",
  targetTerm: "",
  aliases: "",
  domain: "",
  priority: 0,
  reviewStatus: "approved",
  version: "v1",
};

export default function TerminologyPage() {
  const [terms, setTerms] = useState<Terminology[]>([]);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<Terminology | null>(null);
  const [form, setForm] = useState<TermInput>(emptyTerm);

  const load = useCallback(async (search?: string) => {
    setLoading(true);
    try { setTerms(await terminologyApi.list(search)); }
    catch (error) { toast.error(error instanceof ApiRequestError ? error.message : "术语库加载失败"); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const startCreate = () => { setEditing(null); setForm(emptyTerm); setOpen(true); };
  const startEdit = (term: Terminology) => {
    setEditing(term);
    setForm({ sourceLanguage: term.sourceLanguage, sourceTerm: term.sourceTerm,
      targetLanguage: term.targetLanguage, targetTerm: term.targetTerm, aliases: term.aliases ?? "",
      domain: term.domain ?? "", priority: term.priority, reviewStatus: term.reviewStatus, version: term.version });
    setOpen(true);
  };

  const save = async () => {
    if (!form.sourceTerm.trim() || !form.targetTerm.trim()) return toast.error("源术语和中文术语不能为空");
    setSaving(true);
    try {
      if (editing) await terminologyApi.update(editing.id, form);
      else await terminologyApi.create(form);
      toast.success(editing ? "术语已更新" : "术语已添加");
      setOpen(false); await load(query);
    } catch (error) { toast.error(error instanceof ApiRequestError ? error.message : "术语保存失败"); }
    finally { setSaving(false); }
  };

  const remove = async (term: Terminology) => {
    if (!window.confirm(`确定删除术语“${term.sourceTerm} → ${term.targetTerm}”吗？`)) return;
    try { await terminologyApi.delete(term.id); toast.success("术语已删除"); await load(query); }
    catch (error) { toast.error(error instanceof ApiRequestError ? error.message : "术语删除失败"); }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold"><Languages className="size-5" />术语库</h2>
          <p className="text-sm text-muted-foreground">统一中文标题、摘要、查询扩展与全文索引中的专业词汇。</p>
        </div>
        <Button onClick={startCreate}><Plus className="mr-2 size-4" />添加术语</Button>
      </div>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">术语映射</CardTitle>
          <CardDescription>优先级越高，生成中文元数据时越优先采用该译法。</CardDescription>
          <form className="flex max-w-md gap-2 pt-2" onSubmit={(event) => { event.preventDefault(); void load(query); }}>
            <div className="relative flex-1"><Search className="absolute left-3 top-2.5 size-4 text-muted-foreground" />
              <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="搜索源术语、中文术语或别名" className="pl-9" /></div>
            <Button type="submit" variant="outline">搜索</Button>
          </form>
        </CardHeader>
        <CardContent>
          {loading ? <div className="flex justify-center py-16"><Loader2 className="size-6 animate-spin text-muted-foreground" /></div> :
          terms.length === 0 ? <div className="py-16 text-center text-sm text-muted-foreground">暂无术语，添加后会自动参与中文化和检索。</div> :
          <div className="overflow-x-auto"><Table>
            <TableHeader><TableRow><TableHead>源术语</TableHead><TableHead>中文术语</TableHead><TableHead>领域</TableHead><TableHead>别名</TableHead><TableHead>优先级</TableHead><TableHead>版本</TableHead><TableHead className="w-24 text-right">操作</TableHead></TableRow></TableHeader>
            <TableBody>{terms.map((term) => <TableRow key={term.id}>
              <TableCell><div className="font-medium">{term.sourceTerm}</div><div className="text-xs text-muted-foreground">{term.sourceLanguage}</div></TableCell>
              <TableCell><div className="font-medium">{term.targetTerm}</div><div className="text-xs text-muted-foreground">{term.targetLanguage}</div></TableCell>
              <TableCell>{term.domain || "—"}</TableCell><TableCell className="max-w-64 truncate">{term.aliases || "—"}</TableCell>
              <TableCell><Badge variant="secondary">{term.priority}</Badge></TableCell><TableCell>{term.version}</TableCell>
              <TableCell><div className="flex justify-end gap-1"><Button size="icon-sm" variant="ghost" onClick={() => startEdit(term)}><Pencil className="size-4" /></Button>
                <Button size="icon-sm" variant="ghost" className="text-destructive" onClick={() => void remove(term)}><Trash2 className="size-4" /></Button></div></TableCell>
            </TableRow>)}</TableBody>
          </Table></div>}
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent><DialogHeader><DialogTitle>{editing ? "编辑术语" : "添加术语"}</DialogTitle></DialogHeader>
          <div className="grid gap-4 py-2 sm:grid-cols-2">
            <Field label="源语言"><Input value={form.sourceLanguage} onChange={(e) => setForm({ ...form, sourceLanguage: e.target.value })} /></Field>
            <Field label="目标语言"><Input value={form.targetLanguage} onChange={(e) => setForm({ ...form, targetLanguage: e.target.value })} /></Field>
            <Field label="源术语"><Input value={form.sourceTerm} onChange={(e) => setForm({ ...form, sourceTerm: e.target.value })} /></Field>
            <Field label="中文术语"><Input value={form.targetTerm} onChange={(e) => setForm({ ...form, targetTerm: e.target.value })} /></Field>
            <Field label="别名（逗号分隔）"><Input value={form.aliases} onChange={(e) => setForm({ ...form, aliases: e.target.value })} /></Field>
            <Field label="领域"><Input value={form.domain} onChange={(e) => setForm({ ...form, domain: e.target.value })} /></Field>
            <Field label="优先级"><Input type="number" value={form.priority} onChange={(e) => setForm({ ...form, priority: Number(e.target.value) || 0 })} /></Field>
            <Field label="版本"><Input value={form.version} onChange={(e) => setForm({ ...form, version: e.target.value })} /></Field>
          </div>
          <DialogFooter><Button variant="outline" onClick={() => setOpen(false)}>取消</Button><Button onClick={() => void save()} disabled={saving}>{saving && <Loader2 className="mr-2 size-4 animate-spin" />}保存</Button></DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="space-y-1.5"><Label>{label}</Label>{children}</div>;
}
