"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  AlertTriangle, CheckCircle2, Download, Languages, Loader2, Pencil, Plus, Search,
  Sparkles, Trash2, Upload,
} from "lucide-react";
import { toast } from "sonner";
import { terminologyApi, ApiRequestError } from "@/lib/api";
import type {
  Terminology, TerminologyCandidate, TerminologyConflict, TerminologyStats,
} from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

type TermInput = Omit<Terminology, "id" | "createdAt" | "updatedAt">;

const emptyTerm: TermInput = {
  sourceLanguage: "en", sourceTerm: "", targetLanguage: "zh-CN", targetTerm: "",
  aliases: "", domain: "", priority: 0, reviewStatus: "pending", version: "v1",
};
const PAGE_SIZE = 20;

export default function TerminologyPage() {
  const [terms, setTerms] = useState<Terminology[]>([]);
  const [stats, setStats] = useState<TerminologyStats | null>(null);
  const [usage, setUsage] = useState<Record<string, { documentCount: number; chunkCount: number }>>({});
  const [query, setQuery] = useState("");
  const [reviewStatus, setReviewStatus] = useState("");
  const [domain, setDomain] = useState("");
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [open, setOpen] = useState(false);
  const [extractOpen, setExtractOpen] = useState(false);
  const [conflictOpen, setConflictOpen] = useState(false);
  const [conflicts, setConflicts] = useState<TerminologyConflict[]>([]);
  const [extracting, setExtracting] = useState(false);
  const [candidates, setCandidates] = useState<TerminologyCandidate[]>([]);
  const [editing, setEditing] = useState<Terminology | null>(null);
  const [form, setForm] = useState<TermInput>(emptyTerm);
  const fileInput = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [result, nextStats] = await Promise.all([
        terminologyApi.list({
          query: query || undefined, reviewStatus: reviewStatus || undefined,
          domain: domain || undefined, page, pageSize: PAGE_SIZE,
        }),
        terminologyApi.stats(),
      ]);
      setTerms(result.items);
      setTotal(result.total);
      setTotalPages(result.totalPages);
      setStats(nextStats);
      const termUsage = result.items.length ? await terminologyApi.usage(result.items.map((term) => term.id)) : [];
      setUsage(Object.fromEntries(termUsage.map((item) => [item.terminologyId, item])));
    } catch (error) {
      toast.error(error instanceof ApiRequestError ? error.message : "术语库加载失败");
    } finally {
      setLoading(false);
    }
  }, [domain, page, query, reviewStatus]);

  useEffect(() => { void load(); }, [load]);

  const startCreate = (candidate?: TerminologyCandidate) => {
    setEditing(null);
    setForm(candidate ? {
      ...emptyTerm, sourceTerm: candidate.sourceTerm,
      targetTerm: candidate.suggestedTargetTerm ?? "", domain: candidate.domain ?? "",
    } : emptyTerm);
    setExtractOpen(false);
    setOpen(true);
  };
  const startEdit = (term: Terminology) => {
    setEditing(term);
    setForm({
      workspaceId: term.workspaceId, sourceLanguage: term.sourceLanguage, sourceTerm: term.sourceTerm,
      targetLanguage: term.targetLanguage, targetTerm: term.targetTerm, aliases: term.aliases ?? "",
      domain: term.domain ?? "", priority: term.priority, reviewStatus: term.reviewStatus, version: term.version,
    });
    setOpen(true);
  };

  const save = async () => {
    if (!form.sourceTerm.trim() || !form.targetTerm.trim()) return toast.error("源术语和中文术语不能为空");
    setSaving(true);
    try {
      if (editing) await terminologyApi.update(editing.id, form);
      else await terminologyApi.create(form);
      toast.success(editing ? "术语已更新，相关文档已加入增量重处理队列" : "术语已添加");
      setOpen(false);
      await load();
    } catch (error) {
      toast.error(error instanceof ApiRequestError ? error.message : "术语保存失败");
    } finally {
      setSaving(false);
    }
  };

  const remove = async (term: Terminology) => {
    if (!window.confirm(`确定删除术语“${term.sourceTerm} → ${term.targetTerm}”吗？相关文档将重新处理。`)) return;
    try {
      await terminologyApi.delete(term.id);
      toast.success("术语已删除，相关文档已加入增量重处理队列");
      await load();
    } catch (error) {
      toast.error(error instanceof ApiRequestError ? error.message : "术语删除失败");
    }
  };

  const review = async (term: Terminology, status: "approved" | "rejected" | "pending") => {
    try {
      await terminologyApi.review(term.id, status);
      toast.success(status === "approved" ? "术语已批准" : status === "rejected" ? "术语已拒绝" : "术语已转为待审核");
      await load();
    } catch (error) {
      toast.error(error instanceof ApiRequestError ? error.message : "审核状态更新失败");
    }
  };

  const extract = async () => {
    setExtracting(true);
    setExtractOpen(true);
    try {
      setCandidates(await terminologyApi.extract({ documentLimit: 200, candidateLimit: 80 }));
    } catch (error) {
      toast.error(error instanceof ApiRequestError ? error.message : "自动抽取失败");
    } finally {
      setExtracting(false);
    }
  };

  const showConflicts = async () => {
    try {
      setConflicts(await terminologyApi.conflicts());
      setConflictOpen(true);
    } catch (error) {
      toast.error(error instanceof ApiRequestError ? error.message : "冲突列表加载失败");
    }
  };

  const exportCsv = async () => {
    try {
      const blob = await terminologyApi.exportCsv();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `terminology-${new Date().toISOString().slice(0, 10)}.csv`;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("术语导出失败");
    }
  };

  const importCsv = async (file: File) => {
    try {
      const rows = parseCsv(await file.text());
      if (rows.length < 2) throw new Error("CSV 中没有可导入的数据");
      const headers = rows[0].map((x) => x.trim().toLowerCase());
      const value = (row: string[], name: string) => row[headers.indexOf(name)] ?? "";
      const items: TermInput[] = rows.slice(1).filter((row) => row.some(Boolean)).map((row) => ({
        sourceLanguage: value(row, "source_language") || "en",
        sourceTerm: value(row, "source_term"),
        targetLanguage: value(row, "target_language") || "zh-CN",
        targetTerm: value(row, "target_term"),
        aliases: value(row, "aliases"), domain: value(row, "domain"),
        priority: Number(value(row, "priority")) || 0,
        reviewStatus: value(row, "review_status") || "pending",
        version: value(row, "version") || "v1",
      })).filter((item) => item.sourceTerm && item.targetTerm);
      const result = await terminologyApi.bulk(items);
      toast.success(`导入完成：新增 ${result.created}，更新 ${result.updated}，跳过 ${result.skipped}`);
      if (result.errors.length) toast.warning(result.errors.slice(0, 3).join("；"));
      setPage(1);
      await load();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "术语导入失败");
    } finally {
      if (fileInput.current) fileInput.current.value = "";
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold"><Languages className="size-5" />术语库</h2>
          <p className="text-sm text-muted-foreground">术语按当前工作区隔离；只有“已批准”术语会参与中文化、质量校验和检索。</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <input ref={fileInput} type="file" accept=".csv,text/csv" className="hidden"
            onChange={(event) => event.target.files?.[0] && void importCsv(event.target.files[0])} />
          <Button variant="outline" onClick={() => fileInput.current?.click()}><Upload className="mr-2 size-4" />导入 CSV</Button>
          <Button variant="outline" onClick={() => void exportCsv()}><Download className="mr-2 size-4" />导出</Button>
          <Button variant="outline" onClick={() => void extract()}><Sparkles className="mr-2 size-4" />自动抽取</Button>
          <Button onClick={() => startCreate()}><Plus className="mr-2 size-4" />添加术语</Button>
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
        <Stat label="全部术语" value={stats?.total ?? 0} />
        <Stat label="已批准" value={stats?.approved ?? 0} tone="text-emerald-600" />
        <Stat label="待审核" value={stats?.pendingReview ?? 0} tone="text-amber-600" />
        <Stat label="映射冲突" value={stats?.conflicts ?? 0} tone={stats?.conflicts ? "text-red-600" : undefined} />
        <Stat label="重处理任务" value={stats?.pendingReprocessJobs ?? 0} tone="text-blue-600" />
      </div>
      {(stats?.conflicts ?? 0) > 0 && <div className="flex items-center justify-between rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
        <span className="flex items-center gap-2"><AlertTriangle className="size-4" />检测到历史术语映射冲突，冲突术语不会自动合并。</span>
        <Button size="sm" variant="outline" onClick={() => void showConflicts()}>查看冲突</Button>
      </div>}

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">术语映射</CardTitle>
          <CardDescription>修改映射或审核状态后，受影响文档会自动进入可暂停、可重试的后台重处理队列。</CardDescription>
          <form className="grid gap-2 pt-2 md:grid-cols-[minmax(240px,1fr)_160px_160px_auto]"
            onSubmit={(event) => { event.preventDefault(); setPage(1); void load(); }}>
            <div className="relative"><Search className="absolute left-3 top-2.5 size-4 text-muted-foreground" />
              <Input value={query} onChange={(e) => setQuery(e.target.value)}
                placeholder="搜索源术语、中文术语或别名" className="pl-9" /></div>
            <select value={reviewStatus} onChange={(e) => { setReviewStatus(e.target.value); setPage(1); }}
              className="h-9 rounded-md border bg-background px-3 text-sm">
              <option value="">全部审核状态</option><option value="approved">已批准</option>
              <option value="pending">待审核</option><option value="draft">草稿</option><option value="rejected">已拒绝</option>
            </select>
            <select value={domain} onChange={(e) => { setDomain(e.target.value); setPage(1); }}
              className="h-9 rounded-md border bg-background px-3 text-sm">
              <option value="">全部领域</option>
              {Object.keys(stats?.domains ?? {}).filter((x) => x !== "通用").map((item) =>
                <option key={item} value={item}>{item}</option>)}
            </select>
            <Button type="submit" variant="outline">搜索</Button>
          </form>
        </CardHeader>
        <CardContent>
          {loading ? <div className="flex justify-center py-16"><Loader2 className="size-6 animate-spin text-muted-foreground" /></div> :
          terms.length === 0 ? <div className="py-16 text-center text-sm text-muted-foreground">暂无匹配术语</div> :
          <div className="overflow-x-auto"><Table>
            <TableHeader><TableRow><TableHead>源术语</TableHead><TableHead>规范译法</TableHead>
              <TableHead>领域 / 别名</TableHead><TableHead>使用</TableHead><TableHead>优先级</TableHead><TableHead>审核状态</TableHead>
              <TableHead>版本</TableHead><TableHead className="w-40 text-right">操作</TableHead></TableRow></TableHeader>
            <TableBody>{terms.map((term) => <TableRow key={term.id}>
              <TableCell><div className="font-medium">{term.sourceTerm}</div><div className="text-xs text-muted-foreground">{term.sourceLanguage}</div></TableCell>
              <TableCell><div className="font-medium">{term.targetTerm}</div><div className="text-xs text-muted-foreground">{term.targetLanguage}</div></TableCell>
              <TableCell className="max-w-72"><div>{term.domain || "通用"}</div><div className="truncate text-xs text-muted-foreground">{formatAliases(term.aliases)}</div></TableCell>
              <TableCell><div>{usage[term.id]?.documentCount ?? 0} 篇</div><div className="text-xs text-muted-foreground">{usage[term.id]?.chunkCount ?? 0} 个分块</div></TableCell>
              <TableCell><Badge variant="secondary">{term.priority}</Badge></TableCell>
              <TableCell><StatusBadge status={term.reviewStatus} /></TableCell><TableCell>{term.version}</TableCell>
              <TableCell><div className="flex justify-end gap-1">
                {term.reviewStatus !== "approved" && <Button size="icon-sm" variant="ghost" title="批准" onClick={() => void review(term, "approved")}><CheckCircle2 className="size-4 text-emerald-600" /></Button>}
                {term.reviewStatus === "approved" && <Button size="sm" variant="ghost" onClick={() => void review(term, "pending")}>撤回</Button>}
                <Button size="icon-sm" variant="ghost" onClick={() => startEdit(term)}><Pencil className="size-4" /></Button>
                <Button size="icon-sm" variant="ghost" className="text-destructive" onClick={() => void remove(term)}><Trash2 className="size-4" /></Button>
              </div></TableCell>
            </TableRow>)}</TableBody>
          </Table></div>}
          <div className="mt-4 flex items-center justify-between text-sm text-muted-foreground">
            <span>共 {total} 条</span><div className="flex items-center gap-2">
              <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage((x) => x - 1)}>上一页</Button>
              <span>{page} / {Math.max(1, totalPages)}</span>
              <Button size="sm" variant="outline" disabled={page >= totalPages} onClick={() => setPage((x) => x + 1)}>下一页</Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent><DialogHeader><DialogTitle>{editing ? "编辑术语" : "添加术语"}</DialogTitle></DialogHeader>
          <div className="grid gap-4 py-2 sm:grid-cols-2">
            <Field label="源语言"><Input value={form.sourceLanguage} onChange={(e) => setForm({ ...form, sourceLanguage: e.target.value })} /></Field>
            <Field label="目标语言"><Input value={form.targetLanguage} onChange={(e) => setForm({ ...form, targetLanguage: e.target.value })} /></Field>
            <Field label="源术语"><Input value={form.sourceTerm} onChange={(e) => setForm({ ...form, sourceTerm: e.target.value })} /></Field>
            <Field label="规范译法"><Input value={form.targetTerm} onChange={(e) => setForm({ ...form, targetTerm: e.target.value })} /></Field>
            <Field label="别名（逗号分隔）"><Input value={form.aliases} onChange={(e) => setForm({ ...form, aliases: e.target.value })} /></Field>
            <Field label="领域"><Input value={form.domain} onChange={(e) => setForm({ ...form, domain: e.target.value })} /></Field>
            <Field label="优先级"><Input type="number" value={form.priority} onChange={(e) => setForm({ ...form, priority: Number(e.target.value) || 0 })} /></Field>
            <Field label="版本"><Input value={form.version} onChange={(e) => setForm({ ...form, version: e.target.value })} /></Field>
            <Field label="审核状态"><select value={form.reviewStatus}
              onChange={(e) => setForm({ ...form, reviewStatus: e.target.value })}
              className="h-9 w-full rounded-md border bg-background px-3 text-sm">
              <option value="pending">待审核</option><option value="draft">草稿</option>
              <option value="approved">已批准</option><option value="rejected">已拒绝</option>
            </select></Field>
          </div>
          <DialogFooter><Button variant="outline" onClick={() => setOpen(false)}>取消</Button>
            <Button onClick={() => void save()} disabled={saving}>{saving && <Loader2 className="mr-2 size-4 animate-spin" />}保存</Button></DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={extractOpen} onOpenChange={setExtractOpen}>
        <DialogContent className="max-h-[80vh] overflow-hidden sm:max-w-3xl">
          <DialogHeader><DialogTitle>自动抽取候选术语</DialogTitle></DialogHeader>
          <div className="min-h-40 overflow-y-auto">
            {extracting ? <div className="flex justify-center py-16"><Loader2 className="size-6 animate-spin" /></div> :
            candidates.length === 0 ? <div className="py-16 text-center text-sm text-muted-foreground">没有发现新的候选术语</div> :
            <Table><TableHeader><TableRow><TableHead>候选术语</TableHead><TableHead>领域</TableHead><TableHead>出现次数</TableHead><TableHead /></TableRow></TableHeader>
              <TableBody>{candidates.map((candidate) => <TableRow key={candidate.sourceTerm}>
                <TableCell className="font-medium">{candidate.sourceTerm}</TableCell><TableCell>{candidate.domain || "通用"}</TableCell>
                <TableCell>{candidate.occurrences}</TableCell><TableCell className="text-right"><Button size="sm" variant="outline" onClick={() => startCreate(candidate)}>加入术语库</Button></TableCell>
              </TableRow>)}</TableBody></Table>}
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={conflictOpen} onOpenChange={setConflictOpen}>
        <DialogContent className="max-h-[80vh] overflow-hidden sm:max-w-3xl">
          <DialogHeader><DialogTitle>术语映射冲突</DialogTitle></DialogHeader>
          <div className="min-h-32 space-y-3 overflow-y-auto">
            {conflicts.map((conflict) => <div key={`${conflict.sourceLanguage}:${conflict.sourceTerm}:${conflict.targetLanguage}`} className="rounded-lg border p-3">
              <div className="font-medium">{conflict.sourceTerm} <span className="text-xs text-muted-foreground">{conflict.sourceLanguage} → {conflict.targetLanguage}</span></div>
              <div className="mt-2 flex flex-wrap gap-2">{conflict.terms.map((term) =>
                <Badge key={term.id} variant={term.reviewStatus === "approved" ? "destructive" : "secondary"}>
                  {term.targetTerm} · {term.reviewStatus}
                </Badge>)}</div>
            </div>)}
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="space-y-1.5"><Label>{label}</Label>{children}</div>;
}
function Stat({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return <Card><CardContent className="p-4"><div className="text-xs text-muted-foreground">{label}</div><div className={`mt-1 text-2xl font-semibold ${tone ?? ""}`}>{value}</div></CardContent></Card>;
}
function StatusBadge({ status }: { status: string }) {
  const labels: Record<string, string> = { approved: "已批准", pending: "待审核", draft: "草稿", rejected: "已拒绝" };
  return <Badge variant={status === "approved" ? "default" : status === "rejected" ? "destructive" : "secondary"}>{labels[status] ?? status}</Badge>;
}
function formatAliases(value?: string) {
  if (!value) return "无别名";
  try { return (JSON.parse(value) as string[]).join("、"); } catch { return value; }
}
function parseCsv(text: string): string[][] {
  const rows: string[][] = []; let row: string[] = []; let cell = ""; let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (char === "\"") {
      if (quoted && text[i + 1] === "\"") { cell += "\""; i++; } else quoted = !quoted;
    } else if (char === "," && !quoted) { row.push(cell); cell = ""; }
    else if ((char === "\n" || char === "\r") && !quoted) {
      if (char === "\r" && text[i + 1] === "\n") i++;
      row.push(cell); rows.push(row); row = []; cell = "";
    } else cell += char;
  }
  if (cell.length || row.length) { row.push(cell); rows.push(row); }
  if (rows[0]?.[0]) rows[0][0] = rows[0][0].replace(/^\uFEFF/, "");
  return rows;
}
