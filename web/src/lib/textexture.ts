export type ScanMode = "narrative" | "landscape" | "both";

export interface TextGraphNode {
  id: string;
  label: string;
  frequency: number;
  degree: number;
  category?: "company" | "product" | "person" | "technology";
  documentIds?: string[];
}

export interface TextGraphEdge {
  source: string;
  target: string;
  weight: number;
  narrativeWeight: number;
  landscapeWeight: number;
}

export interface TextGraph {
  nodes: TextGraphNode[];
  edges: TextGraphEdge[];
  tokenCount: number;
}

export interface EntityGraphDocument {
  id: string;
  entities: Array<{ name: string; entityType: string; mentionCount?: number }>;
}

function entityCategory(value: string): TextGraphNode["category"] | null {
  const type = value.toLocaleLowerCase();
  if (/^(org|organization)$|company|organisation|brand|公司|组织|企业|机构/.test(type)) return "company";
  if (/product|software|service|platform|app|产品|软件|服务|平台|应用/.test(type)) return "product";
  if (/^(per|person)$|people|人物|人名|创始人|作者/.test(type)) return "person";
  if (/technology|tech|framework|language|model|protocol|algorithm|技术|框架|语言|模型|协议|算法/.test(type)) return "technology";
  return null;
}

export function buildEntityCooccurrenceGraph(documents: EntityGraphDocument[], maxNodes = 100): TextGraph {
  const nodes = new Map<string, TextGraphNode>();
  const edges = new Map<string, TextGraphEdge>();

  for (const document of documents) {
    const entities = [...new Map(document.entities.map((entity) => [entity.name.trim().toLocaleLowerCase(), entity])).values()]
      .map((entity) => ({ ...entity, id: entity.name.trim().toLocaleLowerCase(), category: entityCategory(entity.entityType) }))
      .filter((entity): entity is typeof entity & { category: NonNullable<TextGraphNode["category"]> } => Boolean(entity.name && entity.category));
    for (const entity of entities) {
      const id = entity.id;
      const existing = nodes.get(id) ?? { id, label: entity.name.trim(), frequency: 0, degree: 0, category: entity.category, documentIds: [] };
      existing.frequency += Math.max(1, entity.mentionCount ?? 1);
      if (!existing.documentIds?.includes(document.id)) existing.documentIds?.push(document.id);
      nodes.set(id, existing);
    }
    for (let i = 0; i < entities.length; i += 1) {
      for (let j = i + 1; j < entities.length; j += 1) {
        const [source, target] = entities[i].id < entities[j].id ? [entities[i].id, entities[j].id] : [entities[j].id, entities[i].id];
        if (source === target) continue;
        const key = `${source}\u0000${target}`;
        const edge = edges.get(key) ?? { source, target, weight: 0, narrativeWeight: 0, landscapeWeight: 0 };
        edge.weight += 1; edge.landscapeWeight += 1;
        edges.set(key, edge);
      }
    }
  }

  const selected = new Set([...nodes.values()].sort((a, b) => b.frequency - a.frequency).slice(0, maxNodes).map((node) => node.id));
  const keptEdges = [...edges.values()].filter((edge) => selected.has(edge.source) && selected.has(edge.target)).sort((a, b) => b.weight - a.weight).slice(0, maxNodes * 6);
  for (const edge of keptEdges) {
    const source = nodes.get(edge.source); const target = nodes.get(edge.target);
    if (source) source.degree += edge.weight;
    if (target) target.degree += edge.weight;
  }
  const keptNodes = [...nodes.values()].filter((node) => selected.has(node.id) && node.degree > 0).sort((a, b) => b.degree - a.degree);
  return { nodes: keptNodes, edges: keptEdges, tokenCount: keptNodes.reduce((sum, node) => sum + node.frequency, 0) };
}

const STOP_WORDS = new Set([
  "的", "了", "和", "是", "在", "与", "及", "或", "而", "也", "都", "就", "被", "把",
  "对", "中", "为", "从", "到", "由", "上", "下", "个", "这", "那", "一个", "一种",
  "我们", "你们", "他们", "可以", "可能", "需要", "通过", "进行", "以及", "如果", "但是",
  "the", "a", "an", "and", "or", "but", "to", "of", "in", "on", "for", "with", "is",
  "are", "was", "were", "be", "been", "this", "that", "it", "as", "at", "by", "from",
]);

function normalizeToken(value: string): string | null {
  const token = value.toLocaleLowerCase().replace(/^[_-]+|[_-]+$/g, "").trim();
  if (!token || STOP_WORDS.has(token) || /^\d+(?:[.,]\d+)?$/.test(token)) return null;
  if (/^[a-z]$/i.test(token)) return null;
  return token;
}

export function tokenizeText(text: string, locale = "zh-CN"): string[][] {
  const sentences = text.split(/(?:[。！？!?；;\n]+|\.{2,})/).filter(Boolean);
  const segmenter = new Intl.Segmenter(locale, { granularity: "word" });

  return sentences.map((sentence) => {
    const tokens: string[] = [];
    for (const part of segmenter.segment(sentence)) {
      if (!part.isWordLike) continue;
      const token = normalizeToken(part.segment);
      if (token) tokens.push(token);
    }
    return tokens;
  }).filter((tokens) => tokens.length > 0);
}

export function buildTextextureGraph(
  texts: string[],
  options: { mode?: ScanMode; windowSize?: number; maxNodes?: number } = {},
): TextGraph {
  const mode = options.mode ?? "both";
  const windowSize = Math.max(3, Math.min(12, options.windowSize ?? 5));
  const maxNodes = Math.max(10, options.maxNodes ?? 70);
  const frequencies = new Map<string, number>();
  const edges = new Map<string, TextGraphEdge>();
  const sequences = texts.flatMap((text) => tokenizeText(text));

  for (const sequence of sequences) {
    for (const token of sequence) frequencies.set(token, (frequencies.get(token) ?? 0) + 1);
    for (let index = 0; index < sequence.length; index += 1) {
      const source = sequence[index];
      const limit = Math.min(sequence.length, index + windowSize);
      for (let next = index + 1; next < limit; next += 1) {
        const target = sequence[next];
        if (source === target) continue;
        const distance = next - index;
        const narrativeWeight = distance === 1 && mode !== "landscape" ? 3 : 0;
        const landscapeWeight = distance > 1 && mode !== "narrative" ? 1 / (distance - 1) : 0;
        if (narrativeWeight + landscapeWeight === 0) continue;
        const [a, b] = source < target ? [source, target] : [target, source];
        const key = `${a}\u0000${b}`;
        const edge = edges.get(key) ?? {
          source: a, target: b, weight: 0, narrativeWeight: 0, landscapeWeight: 0,
        };
        edge.narrativeWeight += narrativeWeight;
        edge.landscapeWeight += landscapeWeight;
        edge.weight = edge.narrativeWeight + edge.landscapeWeight;
        edges.set(key, edge);
      }
    }
  }

  const selected = new Set([...frequencies.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .slice(0, maxNodes)
    .map(([token]) => token));
  const keptEdges = [...edges.values()]
    .filter((edge) => selected.has(edge.source) && selected.has(edge.target))
    .sort((a, b) => b.weight - a.weight)
    .slice(0, Math.max(120, maxNodes * 4));
  const degrees = new Map<string, number>();
  for (const edge of keptEdges) {
    degrees.set(edge.source, (degrees.get(edge.source) ?? 0) + edge.weight);
    degrees.set(edge.target, (degrees.get(edge.target) ?? 0) + edge.weight);
  }
  const nodes = [...selected]
    .filter((id) => degrees.has(id))
    .map((id) => ({ id, label: id, frequency: frequencies.get(id) ?? 0, degree: degrees.get(id) ?? 0 }))
    .sort((a, b) => b.degree - a.degree);

  return { nodes, edges: keptEdges, tokenCount: [...frequencies.values()].reduce((sum, n) => sum + n, 0) };
}
