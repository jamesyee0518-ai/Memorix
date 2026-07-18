"use client";

import { useMemo, useState } from "react";
import { Maximize2, Minus, Plus } from "lucide-react";
import type { TextGraph, TextGraphNode } from "@/lib/textexture";
import { cn } from "@/lib/utils";

interface PositionedNode extends TextGraphNode {
  x: number;
  y: number;
}

function layoutGraph(graph: TextGraph): PositionedNode[] {
  const width = 1000;
  const height = 640;
  const nodes = graph.nodes.map((node, index) => {
    const angle = index * 2.399963;
    const radius = 55 + 30 * Math.sqrt(index);
    return { ...node, x: width / 2 + Math.cos(angle) * radius, y: height / 2 + Math.sin(angle) * radius };
  });
  const nodeIndexes = new Map(nodes.map((node, index) => [node.id, index]));
  const velocity = nodes.map(() => ({ x: 0, y: 0 }));
  for (let iteration = 0; iteration < 220; iteration += 1) {
    const cooling = Math.max(0.08, 1 - iteration / 220);
    const forces = nodes.map((node) => ({
      x: (width / 2 - node.x) * 0.0015,
      y: (height / 2 - node.y) * 0.0015,
    }));

    for (let i = 0; i < nodes.length; i += 1) {
      for (let j = i + 1; j < nodes.length; j += 1) {
        const dx = nodes[i].x - nodes[j].x;
        const dy = nodes[i].y - nodes[j].y;
        const distance = Math.max(12, Math.hypot(dx, dy));
        const repulsion = 1800 / (distance * distance);
        const fx = dx / distance * repulsion;
        const fy = dy / distance * repulsion;
        forces[i].x += fx; forces[i].y += fy;
        forces[j].x -= fx; forces[j].y -= fy;
      }
    }

    for (const edge of graph.edges) {
      const aIndex = nodeIndexes.get(edge.source);
      const bIndex = nodeIndexes.get(edge.target);
      if (aIndex === undefined || bIndex === undefined) continue;
      const a = nodes[aIndex];
      const b = nodes[bIndex];
      const dx = b.x - a.x;
      const dy = b.y - a.y;
      const distance = Math.max(1, Math.hypot(dx, dy));
      const spring = Math.max(-2.5, Math.min(2.5,
        (distance - 115) * 0.0025 * Math.min(3, Math.sqrt(edge.weight)),
      ));
      const fx = dx / distance * spring;
      const fy = dy / distance * spring;
      forces[aIndex].x += fx; forces[aIndex].y += fy;
      forces[bIndex].x -= fx; forces[bIndex].y -= fy;
    }

    for (let index = 0; index < nodes.length; index += 1) {
      velocity[index].x = (velocity[index].x + forces[index].x * cooling) * 0.82;
      velocity[index].y = (velocity[index].y + forces[index].y * cooling) * 0.82;
      const speed = Math.max(1, Math.hypot(velocity[index].x, velocity[index].y));
      const scale = Math.min(7, speed) / speed;
      nodes[index].x += velocity[index].x * scale;
      nodes[index].y += velocity[index].y * scale;
    }
  }

  const minX = Math.min(...nodes.map((node) => node.x));
  const maxX = Math.max(...nodes.map((node) => node.x));
  const minY = Math.min(...nodes.map((node) => node.y));
  const maxY = Math.max(...nodes.map((node) => node.y));
  const spanX = Math.max(1, maxX - minX);
  const spanY = Math.max(1, maxY - minY);
  for (const node of nodes) {
    node.x = 70 + (node.x - minX) / spanX * (width - 140);
    node.y = 55 + (node.y - minY) / spanY * (height - 110);
  }
  return nodes;
}

const categoryColors = { company: "#ff6b6b", product: "#45c9c3", person: "#ffd966", technology: "#948bf0" } as const;

export function TextNetwork({ graph, className, onNodeSelect, entityMode = false }: { graph: TextGraph; className?: string; onNodeSelect?: (nodeId: string | null) => void; entityMode?: boolean }) {
  const nodes = useMemo(() => layoutGraph(graph), [graph]);
  const nodeMap = useMemo(() => new Map(nodes.map((node) => [node.id, node])), [nodes]);
  const [selected, setSelected] = useState<string | null>(null);
  const [zoom, setZoom] = useState(1);
  const neighbors = useMemo(() => {
    if (!selected) return new Set<string>();
    const result = new Set([selected]);
    graph.edges.forEach((edge) => {
      if (edge.source === selected) result.add(edge.target);
      if (edge.target === selected) result.add(edge.source);
    });
    return result;
  }, [graph.edges, selected]);
  const maxDegree = Math.max(1, ...nodes.map((node) => node.degree));
  const maxWeight = Math.max(1, ...graph.edges.map((edge) => edge.weight));

  return (
    <div className={cn("relative overflow-hidden rounded-xl border bg-[#071827]", className)}>
      <svg viewBox="0 0 1000 640" className="h-full min-h-[520px] w-full" role="img" aria-label="文档概念关系网络">
        <defs>
          <radialGradient id="graph-bg"><stop offset="0" stopColor="#123d54" /><stop offset="1" stopColor="#071827" /></radialGradient>
          <filter id="node-glow"><feGaussianBlur stdDeviation="3" result="blur" /><feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
        </defs>
        <rect width="1000" height="640" fill="url(#graph-bg)" />
        <g transform={`translate(500 320) scale(${zoom}) translate(-500 -320)`}>
        <g>
          {graph.edges.map((edge) => {
            const source = nodeMap.get(edge.source);
            const target = nodeMap.get(edge.target);
            if (!source || !target) return null;
            const active = !selected || (neighbors.has(edge.source) && neighbors.has(edge.target));
            return <line key={`${edge.source}-${edge.target}`} x1={source.x} y1={source.y} x2={target.x} y2={target.y}
              stroke={edge.narrativeWeight > 0 ? "#48d6e5" : "#8ba7bc"}
              strokeOpacity={active ? 0.22 + 0.5 * edge.weight / maxWeight : 0.035}
              strokeWidth={0.5 + 3 * edge.weight / maxWeight} />;
          })}
        </g>
        <g>
          {nodes.map((node, index) => {
            const radius = 7 + 15 * Math.sqrt(node.degree / maxDegree);
            const active = !selected || neighbors.has(node.id);
            return (
              <g key={node.id} transform={`translate(${node.x} ${node.y})`} onClick={() => { const next = selected === node.id ? null : node.id; setSelected(next); onNodeSelect?.(next); }}
                className="cursor-pointer" opacity={active ? 1 : 0.18}>
                <circle r={radius} fill={node.category ? categoryColors[node.category] : index < 8 ? "#00b8c8" : "#3478b8"} stroke="#f8fafc" strokeOpacity=".8"
                  strokeWidth={selected === node.id ? 3 : 1} filter={index < 8 ? "url(#node-glow)" : undefined} />
                <text y={radius + 14} textAnchor="middle" fill="#e8f7fb" fontSize={index < 12 ? 13 : 11}
                  fontWeight={index < 8 ? 650 : 450} className="pointer-events-none select-none">{node.label}</text>
                <title>{`${node.label} · 出现 ${node.frequency} 次 · 连接强度 ${node.degree.toFixed(1)}`}</title>
              </g>
            );
          })}
        </g>
        </g>
      </svg>
      <div className="absolute left-3 top-3 flex items-center gap-3 rounded-md bg-slate-950/65 px-3 py-2 text-[11px] text-slate-300 backdrop-blur">
        {entityMode ? <><span className="flex items-center gap-1.5"><i className="size-2.5 rounded-full bg-[#ff6b6b]" />公司</span><span className="flex items-center gap-1.5"><i className="size-2.5 rounded-full bg-[#45c9c3]" />产品</span><span className="flex items-center gap-1.5"><i className="size-2.5 rounded-full bg-[#ffd966]" />人物</span><span className="flex items-center gap-1.5"><i className="size-2.5 rounded-full bg-[#948bf0]" />技术</span></> : <><span className="flex items-center gap-1.5"><i className="size-2.5 rounded-full bg-[#00b8c8]" />核心概念</span><span className="flex items-center gap-1.5"><i className="size-2.5 rounded-full bg-[#3478b8]" />普通概念</span><span className="flex items-center gap-1.5"><i className="h-px w-4 bg-[#48d6e5]" />叙事关系</span><span className="flex items-center gap-1.5"><i className="h-px w-4 bg-[#8ba7bc]" />景观关系</span></>}
      </div>
      <div className="absolute bottom-3 right-3 flex items-center rounded-md border border-white/10 bg-slate-950/70 p-1 text-slate-200 backdrop-blur">
        <button type="button" title="缩小" onClick={() => setZoom((value) => Math.max(0.65, value - 0.15))} className="flex size-8 items-center justify-center rounded hover:bg-white/10"><Minus className="size-4" /></button>
        <span className="w-11 text-center text-[11px] tabular-nums">{Math.round(zoom * 100)}%</span>
        <button type="button" title="放大" onClick={() => setZoom((value) => Math.min(2, value + 0.15))} className="flex size-8 items-center justify-center rounded hover:bg-white/10"><Plus className="size-4" /></button>
        <button type="button" title="复位" onClick={() => { setZoom(1); setSelected(null); onNodeSelect?.(null); }} className="flex size-8 items-center justify-center rounded hover:bg-white/10"><Maximize2 className="size-4" /></button>
      </div>
      <div className="absolute bottom-3 left-3 rounded-md bg-slate-950/65 px-3 py-2 text-xs text-slate-300 backdrop-blur">
        点击概念可聚焦相邻关系，再次点击取消
      </div>
    </div>
  );
}
