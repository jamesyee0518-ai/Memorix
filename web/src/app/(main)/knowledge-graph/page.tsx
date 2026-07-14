"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Loader2, Network, ScanSearch, SlidersHorizontal } from "lucide-react";
import { documentApi } from "@/lib/api";
import { buildEntityCooccurrenceGraph, buildTextextureGraph, type ScanMode } from "@/lib/textexture";
import { TextNetwork } from "@/components/knowledge-graph/text-network";
import { Card, CardContent } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

async function loadCorpus() {
  const list = await documentApi.list({ page: 1, pageSize: 60, aiStatus: "done" });
  const details = await Promise.all(list.items.map((item) => documentApi.get(item.id).catch(() => null)));
  return details.filter((item) => item !== null).map((item) => ({
    id: item.id,
    title: item.title,
    text: item.contentText || item.contentMarkdown || item.summary || item.title,
    entities: item.entities || [],
  }));
}

export default function KnowledgeGraphPage() {
  const [mode, setMode] = useState<ScanMode>("both");
  const [viewMode, setViewMode] = useState<"entities" | "text">("entities");
  const [selectedNode, setSelectedNode] = useState<string | null>(null);
  const [windowSize, setWindowSize] = useState(5);
  const [panelOpen, setPanelOpen] = useState(true);
  const { data: corpus = [], isLoading, isError } = useQuery({ queryKey: ["knowledge-graph-corpus"], queryFn: loadCorpus });
  const graph = useMemo(() => viewMode === "entities"
    ? buildEntityCooccurrenceGraph(corpus)
    : buildTextextureGraph(corpus.map((item) => item.text), { mode, windowSize }), [corpus, mode, viewMode, windowSize]);
  const selectedGraphNode = graph.nodes.find((node) => node.id === selectedNode);
  const relatedDocuments = selectedGraphNode?.documentIds
    ? corpus.filter((document) => selectedGraphNode.documentIds?.includes(document.id))
    : [];
  const graphInsights = useMemo(() => {
    const weights = graph.edges.map((edge) => edge.weight);
    return {
      averageWeight: weights.length ? weights.reduce((sum, weight) => sum + weight, 0) / weights.length : 0,
      maxWeight: weights.length ? Math.max(...weights) : 0,
      hubs: graph.nodes.slice(0, 10),
      strongest: graph.edges.slice(0, 5),
    };
  }, [graph]);

  const graphContent = isLoading ? (
    <Card className="flex-1"><CardContent className="flex h-full min-h-[560px] items-center justify-center"><Loader2 className="size-8 animate-spin text-muted-foreground" /></CardContent></Card>
  ) : isError ? (
    <Card className="flex-1"><CardContent className="flex h-full min-h-64 flex-col items-center justify-center text-center"><ScanSearch className="mb-3 size-10 text-muted-foreground" /><p className="font-medium">无法读取文档</p><p className="mt-1 text-sm text-muted-foreground">请确认当前工作区服务正常后刷新页面</p></CardContent></Card>
  ) : graph.nodes.length === 0 ? (
    <Card className="flex-1"><CardContent className="flex h-full min-h-64 flex-col items-center justify-center text-center"><ScanSearch className="mb-3 size-10 text-muted-foreground" /><p className="font-medium">还没有足够的文本关系</p><p className="mt-1 text-sm text-muted-foreground">完成至少一篇文档的 AI 处理后即可生成图谱</p></CardContent></Card>
  ) : <TextNetwork graph={graph} className="min-h-[620px] flex-1" entityMode={viewMode === "entities"} onNodeSelect={setSelectedNode} />;

  return (
    <div className="flex min-h-[calc(100vh-2.5rem)] gap-4">
      <section className="flex min-w-0 flex-1 flex-col gap-4">
        <div>
          <div className="flex items-center gap-2"><Network className="size-6 text-[#007C91]" /><h1 className="text-2xl font-bold">关系图谱</h1></div>
          <p className="mt-1 text-sm text-muted-foreground">基于 Textexture 文本网络算法发现文档中的概念、主题与叙事结构</p>
        </div>
        {graphContent}
      </section>

      <aside className={`sticky top-0 h-fit max-h-[calc(100vh-2.5rem)] shrink-0 overflow-hidden rounded-xl border bg-card shadow-sm transition-[width] duration-200 ${panelOpen ? "w-72" : "w-11"}`}>
        <div className={`flex h-11 shrink-0 items-center border-b ${panelOpen ? "justify-between px-3" : "justify-center"}`}>
          {panelOpen && <div className="flex items-center gap-2"><SlidersHorizontal className="size-4 text-muted-foreground" /><h2 className="text-sm font-semibold">图谱分析</h2></div>}
          <button type="button" onClick={() => setPanelOpen((open) => !open)} title={panelOpen ? "收起控制栏" : "展开控制栏"}
            className="flex size-8 items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground">
            {panelOpen ? <ChevronRight className="size-4" /> : <ChevronLeft className="size-4" />}
          </button>
        </div>
        {panelOpen && (
          <div className="max-h-[calc(100vh-5.25rem)] space-y-4 overflow-y-auto p-3 overscroll-contain">
            <div className="rounded-lg bg-muted/40 p-3">
              <h3 className="mb-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground">显示设置</h3>
            <div className="space-y-2"><label className="text-xs text-muted-foreground">图谱类型</label><Select value={viewMode} onValueChange={(value) => { setViewMode(value as "entities" | "text"); setSelectedNode(null); }}><SelectTrigger className="w-full"><SelectValue>{viewMode === "entities" ? "实体共现网络" : "Textexture 文本网络"}</SelectValue></SelectTrigger><SelectContent><SelectItem value="entities">实体共现网络</SelectItem><SelectItem value="text">Textexture 文本网络</SelectItem></SelectContent></Select></div>
            {viewMode === "text" && <>
            <div className="mt-3 space-y-2"><label className="text-xs text-muted-foreground">扫描模式</label><Select value={mode} onValueChange={(value) => setMode(value as ScanMode)}><SelectTrigger className="w-full"><SelectValue>{mode === "both" ? "综合扫描" : mode === "narrative" ? "Narrative Scan" : "Landscape Scan"}</SelectValue></SelectTrigger><SelectContent><SelectItem value="both">综合扫描</SelectItem><SelectItem value="narrative">Narrative Scan</SelectItem><SelectItem value="landscape">Landscape Scan</SelectItem></SelectContent></Select></div>
            <div className="mt-3 space-y-2"><label className="text-xs text-muted-foreground">扫描窗口</label><Select value={String(windowSize)} onValueChange={(value) => setWindowSize(Number(value))} disabled={mode === "narrative"}><SelectTrigger className="w-full"><SelectValue>窗口 {windowSize} 词</SelectValue></SelectTrigger><SelectContent>{[3, 4, 5, 6, 7, 8].map((size) => <SelectItem key={size} value={String(size)}>窗口 {size} 词</SelectItem>)}</SelectContent></Select></div>
            </>}
            </div>
            <div className="grid grid-cols-3 gap-1.5">
              {[{ label: "文档", value: corpus.length }, { label: "实体", value: graph.nodes.length }, { label: "关系", value: graph.edges.length }].map((item) => <div key={item.label} className="rounded-lg border bg-background p-2 text-center"><div className="text-lg font-bold tabular-nums">{item.value}</div><div className="text-[10px] text-muted-foreground">{item.label}</div></div>)}
            </div>
            <div className="border-t pt-3">
              {selectedGraphNode && viewMode === "entities" && <><h3 className="mb-1 text-sm font-semibold">{selectedGraphNode.label}</h3><p className="mb-3 text-xs text-muted-foreground">相关文章 {relatedDocuments.length} 篇</p><div className="mb-4 space-y-2">{relatedDocuments.map((document) => <Link key={document.id} href={`/documents/${document.id}`} className="block rounded-md bg-muted/60 px-2.5 py-2 text-xs leading-5 hover:bg-muted"><span className="line-clamp-2 font-medium">{document.title}</span></Link>)}</div></>}
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">Hub 实体</h3>
              <div className="space-y-2">{graphInsights.hubs.map((node, index) => <div key={node.id} className="flex items-center gap-2 text-xs"><span className="w-4 text-right font-medium text-primary">{index + 1}</span><span className="min-w-0 flex-1 truncate font-medium">{node.label}</span><div className="h-1.5 w-12 overflow-hidden rounded-full bg-muted"><div className="h-full rounded-full bg-[#00b8c8]" style={{ width: `${Math.max(8, node.degree / Math.max(1, graphInsights.hubs[0]?.degree ?? 1) * 100)}%` }} /></div><span className="w-7 text-right tabular-nums text-muted-foreground">{node.frequency}</span></div>)}</div>
            </div>
            <div className="border-t pt-3">
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">图谱统计</h3>
              <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-xs"><dt className="text-muted-foreground">关系总数</dt><dd className="text-right font-medium tabular-nums">{graph.edges.length}</dd><dt className="text-muted-foreground">平均权重</dt><dd className="text-right font-medium tabular-nums">{graphInsights.averageWeight.toFixed(1)}</dd><dt className="text-muted-foreground">最大权重</dt><dd className="text-right font-medium tabular-nums">{graphInsights.maxWeight.toFixed(1)}</dd><dt className="text-muted-foreground">分析词元</dt><dd className="text-right font-medium tabular-nums">{graph.tokenCount}</dd></dl>
            </div>
            <div className="border-t pt-3">
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">最强关系</h3>
              <div className="space-y-2">{graphInsights.strongest.map((edge) => <div key={`${edge.source}-${edge.target}`} className="rounded-md bg-muted/60 px-2.5 py-2 text-xs"><div className="truncate font-medium">{edge.source} ↔ {edge.target}</div><div className="mt-1 text-[11px] text-primary">w={edge.weight.toFixed(1)}</div></div>)}</div>
            </div>
            <p className="border-t pt-3 text-[11px] leading-4 text-muted-foreground">Narrative Scan 对相邻概念建立权重 3 的边；Landscape Scan 扫描窗口内非相邻概念，权重随距离递减。</p>
          </div>
        )}
      </aside>
    </div>
  );
}
