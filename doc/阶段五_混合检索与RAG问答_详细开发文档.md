# 阶段五：混合检索与 RAG 问答详细开发文档

版本：V0.1  
所属项目：双模式 AI 知识资产引擎  
所属阶段：阶段五  
前置阶段：阶段一「双模式底座改造」、阶段二「资料导入与 Inbox」、阶段三「文档解析、清洗与 AI 摘要」、阶段四「标签、实体、分块与向量化」  
阶段目标：让用户能够在本地 / 云端 / 混合工作区中，对已经结构化的知识资产进行高质量检索、基于来源的问答、可追溯引用和检索调试。

---

## 1. 阶段定位

阶段五不是简单做一个“搜索框”，也不是直接把用户问题丢给大模型，而是要构建知识资产引擎的核心使用闭环：

```text
用户提问 / 搜索
  ↓
理解查询意图
  ↓
混合检索召回相关知识片段
  ↓
重排序与上下文构建
  ↓
RAG 问答生成
  ↓
引用来源与证据回溯
  ↓
用户反馈与检索调优
```

阶段五完成后，系统应具备以下能力：

1. 用户可以在知识库中进行关键词检索；
2. 用户可以进行语义检索；
3. 用户可以结合专题、标签、实体、时间、来源类型进行过滤；
4. 用户可以向知识库提问，并得到基于资料来源的回答；
5. 回答必须带引用，用户可以追溯到原文片段；
6. 无可靠资料时，系统应明确说明“不足以回答”；
7. 开发者可以查看检索调试信息，用于优化召回质量；
8. 本地模式、云端模式、混合模式共用统一 SearchEngine 与 RAGService 抽象。

---

## 2. 本阶段与前后阶段关系

### 2.1 输入依赖

阶段五依赖阶段四已完成的数据：

```text
sources
  ↓
documents
  ↓
document_chunks
  ↓
tags / entities
  ↓
embeddings / vector index
```

必要前置条件：

1. `documents` 已经有清洗后的正文；
2. `document_chunks` 已经完成分块；
3. 每个 chunk 至少包含文本内容；
4. 部分或全部 chunk 已生成 embedding；
5. 标签、实体、时间、来源等元数据可用于过滤；
6. 本地 / 云端检索适配层已完成基础实现。

### 2.2 输出结果

阶段五输出：

1. 搜索结果列表；
2. RAG 问答答案；
3. 引用来源；
4. 检索快照；
5. 问答历史；
6. 用户反馈；
7. 调试日志。

### 2.3 后续阶段依赖

阶段六「报告与导出」将复用阶段五能力：

```text
专题报告生成
  ↓
先检索相关资料
  ↓
构建多文档上下文
  ↓
生成报告
  ↓
输出引用来源
```

阶段七「MCP / Agent 接入」也将直接复用：

```text
MCP search_memory
MCP ask_memory
Cloud Agent Search API
Cloud Agent QA API
```

---

## 3. 阶段目标与非目标

### 3.1 阶段目标

本阶段目标是构建一个可用、可解释、可调试的混合检索与 RAG 问答系统。

核心目标：

| 目标 | 说明 |
|---|---|
| 混合检索 | 支持关键词检索、向量检索、元数据过滤和混合排序 |
| RAG 问答 | 基于检索结果生成回答，而不是凭空回答 |
| 引用追溯 | 回答中的关键结论必须能追溯到 chunk / document / source |
| 防幻觉 | 无资料、低置信度、来源冲突时应明确说明 |
| 双模式兼容 | Local SQLite / Local VectorIndex 与 Cloud PostgreSQL / pgvector 共用接口 |
| 可调试 | 开发者能看到召回、打分、过滤、上下文构建过程 |
| 可复用 | 后续报告、MCP、Agent API 可以复用本阶段能力 |

### 3.2 本阶段不做

阶段五暂不做以下内容：

1. 不做复杂知识图谱推理；
2. 不做多轮 Agent 自动任务执行；
3. 不做企业级权限矩阵下的复杂检索授权；
4. 不做跨工作区联合检索，除非用户明确选择；
5. 不做全量互联网搜索；
6. 不做完全自动事实校验；
7. 不做复杂多模态问答，图片 OCR 可先作为文本进入知识库；
8. 不做超长报告生成，那属于阶段六。

---

## 4. 核心功能清单

### 4.1 P0 必须完成

1. 关键词检索；
2. 向量检索；
3. 混合检索排序；
4. 专题过滤；
5. 标签过滤；
6. 时间过滤；
7. 来源类型过滤；
8. Top K chunk 召回；
9. ContextBuilder 上下文构建；
10. RAG 问答；
11. 引用来源；
12. 问答历史入库；
13. 无资料拒答；
14. 本地模式可运行；
15. 云端模式接口兼容。

### 4.2 P1 重要功能

1. 实体过滤；
2. 域名 / 来源过滤；
3. 价值评分过滤；
4. 新鲜度排序；
5. 检索调试面板；
6. 回答反馈；
7. 引用点击回原文；
8. 多轮问答上下文；
9. 查询改写；
10. 结果重排序。

### 4.3 P2 后续增强

1. Cross-Encoder ReRanker；
2. HyDE 检索增强；
3. 多查询扩展；
4. 图谱辅助检索；
5. 多模态检索；
6. 跨工作区检索；
7. 团队权限感知检索；
8. 检索评测数据集；
9. 自动引用质量评估；
10. 查询意图分类模型。

---

## 5. 检索总体架构

### 5.1 架构图

```text
Search / Ask UI
  ↓
SearchController / AskController
  ↓
QueryUnderstandingService
  ↓
SearchEngine
  ├── KeywordSearch
  ├── VectorSearch
  ├── MetadataFilter
  ├── HybridRanker
  └── ReRanker，可选
  ↓
SearchResultBuilder
  ↓
ContextBuilder
  ↓
RAGService
  ↓
ModelProvider
  ↓
CitationBuilder
  ↓
AnswerResponse / SearchResponse
```

### 5.2 本地模式架构

```text
Desktop App
  ↓
Local Search API
  ↓
SQLite FTS / LIKE / BM25
  + Local Vector Index
  + SQLite Metadata Filter
  ↓
Local RAG Service
  ↓
Ollama / LM Studio / User API Key
```

### 5.3 云端模式架构

```text
Web / Mobile / Desktop
  ↓
Cloud API
  ↓
PostgreSQL Full Text Search
  + pgvector
  + SQL Metadata Filter
  ↓
Cloud RAG Service
  ↓
Cloud Model API / User API Key
```

### 5.4 混合模式架构

```text
Desktop Local Workspace
  ↓
本地主知识库检索
  ↓
可选同步云端摘要 / 索引
  ↓
手机端采集内容拉取后进入本地索引
```

MVP 阶段建议：

```text
混合模式优先检索本地库。
云端 Inbox 未拉取内容不参与本地问答。
```

---

## 6. SearchEngine 抽象设计

### 6.1 统一接口

```ts
interface SearchEngine {
  search(input: SearchInput): Promise<SearchResponse>
  keywordSearch(input: KeywordSearchInput): Promise<SearchHit[]>
  vectorSearch(input: VectorSearchInput): Promise<SearchHit[]>
  hybridSearch(input: HybridSearchInput): Promise<SearchResponse>
}
```

### 6.2 SearchInput

```ts
type SearchInput = {
  workspaceId: string
  query: string

  mode?: "keyword" | "vector" | "hybrid"

  topicIds?: string[]
  tagIds?: string[]
  entityIds?: string[]
  sourceTypes?: string[]
  domains?: string[]

  dateFrom?: string
  dateTo?: string

  minValueScore?: number
  minQualityScore?: number

  limit?: number
  offset?: number

  includeChunks?: boolean
  includeDocuments?: boolean
  includeDebug?: boolean
}
```

### 6.3 SearchResponse

```ts
type SearchResponse = {
  query: string
  mode: "keyword" | "vector" | "hybrid"
  total: number
  results: SearchResult[]
  filters: AppliedSearchFilters
  debug?: SearchDebugInfo
}
```

### 6.4 SearchResult

```ts
type SearchResult = {
  documentId: string
  sourceId: string
  chunkId?: string

  title: string
  snippet: string
  sourceType: string
  sourceUrl?: string
  sourceDomain?: string
  topicId?: string

  tags?: string[]
  entities?: string[]

  publishedAt?: string
  createdAt: string

  keywordScore?: number
  vectorScore?: number
  freshnessScore?: number
  valueScore?: number
  metadataScore?: number
  finalScore: number

  highlight?: string
}
```

---

## 7. 查询理解 QueryUnderstanding

### 7.1 目标

用户输入往往不是标准关键词，而是自然语言问题，例如：

```text
最近关于本地 AI Agent 的文章有哪些？
这几篇资料对知识库产品有什么启发？
帮我找一下跨境电商保税仓相关内容。
```

系统需要先做轻量查询理解。

### 7.2 MVP 规则版查询理解

MVP 不必一开始就引入复杂模型，可先做规则和简单 LLM 辅助。

识别内容：

| 识别项 | 示例 |
|---|---|
| 查询关键词 | 本地 AI Agent、知识库产品、保税仓 |
| 时间意图 | 最近、上周、2026 年 6 月 |
| 来源意图 | PDF、网页、录音 |
| 专题意图 | 某个 topic 名称 |
| 问答意图 | “为什么”“如何”“有什么启发” |
| 搜索意图 | “找一下”“有哪些”“列出” |

### 7.3 QueryPlan

```ts
type QueryPlan = {
  originalQuery: string
  normalizedQuery: string
  searchQuery: string
  answerQuestion?: string

  intent: "search" | "qa" | "compare" | "summarize" | "unknown"

  extractedKeywords: string[]
  expandedKeywords?: string[]

  filters: {
    topicIds?: string[]
    tagIds?: string[]
    entityIds?: string[]
    sourceTypes?: string[]
    dateFrom?: string
    dateTo?: string
  }

  requireAnswer: boolean
  requireCitations: boolean
}
```

### 7.4 查询改写

MVP 可支持轻量改写：

```text
用户问题：本地 AI 知识库怎么做？
检索查询：本地 AI 知识库 知识管理 RAG Obsidian MCP
```

实现方式：

1. 默认使用原始 query；
2. 对中文进行分词或关键词提取；
3. 使用标签和实体词库补充同义词；
4. 可选调用 LLM 生成 3 个检索 query；
5. 多 query 召回后合并去重。

---

## 8. 关键词检索设计

### 8.1 关键词检索目标

关键词检索适合：

1. 精确词匹配；
2. 人名、公司名、产品名；
3. URL、域名；
4. 文档标题；
5. 标签和实体；
6. 用户明确知道关键词的场景。

### 8.2 本地关键词检索

建议本地 SQLite 使用以下方案：

```text
SQLite FTS5
  ↓
documents_fts
  ↓
document_chunks_fts
```

FTS 表建议：

```sql
CREATE VIRTUAL TABLE document_chunks_fts USING fts5(
    chunk_id UNINDEXED,
    document_id UNINDEXED,
    workspace_id UNINDEXED,
    title,
    content,
    tags,
    entities,
    tokenize = 'unicode61'
);
```

说明：

1. 中文分词能力有限时，可先使用字符级 / ngram 辅助；
2. 后续可接入 jieba / tantivy / Meilisearch；
3. MVP 先以可用为主，不追求搜索引擎级体验。

### 8.3 云端关键词检索

云端 PostgreSQL 可使用：

```text
PostgreSQL Full Text Search
或
PostgreSQL trigram + ILIKE
或
后续独立搜索引擎
```

MVP 建议：

1. 英文资料使用 `to_tsvector`；
2. 中文资料先使用 `ILIKE` + trigram；
3. 后续再接入 Meilisearch / OpenSearch。

### 8.4 关键词打分

关键词得分来源：

```text
标题命中 > 标签命中 > 实体命中 > 正文命中 > 摘要命中
```

建议加权：

| 命中位置 | 权重 |
|---|---:|
| title | 1.00 |
| tag | 0.90 |
| entity | 0.85 |
| summary | 0.75 |
| chunk content | 0.70 |
| raw text | 0.50 |

---

## 9. 向量检索设计

### 9.1 向量检索目标

向量检索适合：

1. 语义相近但关键词不同的内容；
2. 用户自然语言问题；
3. 跨语言查询；
4. 主题性检索；
5. RAG 上下文召回。

### 9.2 向量检索流程

```text
用户 query
  ↓
生成 query embedding
  ↓
向量索引检索 Top K
  ↓
元数据过滤
  ↓
返回候选 chunks
```

### 9.3 本地向量索引

本地可选方案：

| 方案 | 优点 | 风险 |
|---|---|---|
| sqlite-vec | 与 SQLite 集成度高 | 生态仍在发展 |
| sqlite-vss | SQLite 内嵌向量检索 | 部署兼容性需验证 |
| LanceDB | 本地向量库体验好 | 需要额外文件管理 |
| hnswlib | 性能较好 | 需要自建元数据映射 |
| FAISS | 成熟 | 打包和跨平台复杂 |

MVP 建议：

```text
优先：sqlite-vec 或 LanceDB
备选：hnswlib + SQLite metadata
```

### 9.4 云端向量索引

云端建议使用：

```text
PostgreSQL + pgvector
```

表字段：

```sql
ALTER TABLE document_chunks
ADD COLUMN embedding_vector vector(1024);
```

维度根据 embedding 模型调整。

### 9.5 向量相似度

常用相似度：

```text
cosine similarity
inner product
L2 distance
```

建议：

```text
embedding 已归一化：使用 inner product
embedding 未归一化：使用 cosine distance
```

### 9.6 向量检索参数

```ts
type VectorSearchInput = {
  workspaceId: string
  query: string
  queryEmbedding?: number[]
  topK: number
  filters?: MetadataFilters
  minSimilarity?: number
}
```

默认值：

```text
topK = 40
minSimilarity = 0.25
```

RAG 构建上下文时，再从候选中选择 8-15 个 chunk。

---

## 10. 元数据过滤设计

### 10.1 过滤维度

必须支持：

| 过滤项 | 字段 |
|---|---|
| 工作区 | workspace_id |
| 专题 | topic_id |
| 标签 | tag_id / tag_name |
| 实体 | entity_id / entity_name |
| 来源类型 | source_type |
| 时间 | published_at / created_at |
| 域名 | source_domain |
| 价值评分 | value_score |
| 质量评分 | quality_score |
| 处理状态 | index_status / ai_status |

### 10.2 过滤原则

1. 工作区过滤必须始终生效；
2. 用户指定专题时，只检索该专题；
3. 用户未指定专题时，可检索当前工作区全部资料；
4. 失败、归档、未索引资料默认不参与问答；
5. 低质量资料可参与搜索，但默认不作为 RAG 主要上下文；
6. 权限过滤优先于所有排序。

### 10.3 MetadataFilters

```ts
type MetadataFilters = {
  workspaceId: string
  topicIds?: string[]
  tagIds?: string[]
  entityIds?: string[]
  sourceTypes?: string[]
  domains?: string[]
  dateFrom?: string
  dateTo?: string
  minValueScore?: number
  minQualityScore?: number
  includeArchived?: boolean
}
```

---

## 11. 混合排序 Hybrid Ranking

### 11.1 基础公式

推荐初始公式：

```text
final_score = keyword_score * 0.35
            + vector_score * 0.40
            + freshness_score * 0.10
            + value_score_norm * 0.10
            + metadata_score * 0.05
```

说明：

| 分数 | 说明 |
|---|---|
| keyword_score | 关键词匹配程度 |
| vector_score | 语义相似度 |
| freshness_score | 时间新鲜度 |
| value_score_norm | 文档价值评分归一化 |
| metadata_score | 标签、实体、专题等元数据匹配 |

### 11.2 分数归一化

所有分数统一转换为 0-1。

```ts
function normalizeScore(score: number, min: number, max: number): number {
  if (max === min) return 0
  return Math.max(0, Math.min(1, (score - min) / (max - min)))
}
```

### 11.3 新鲜度评分

可采用半衰期模型：

```text
freshness_score = exp(-age_days / half_life_days)
```

默认：

```text
half_life_days = 180
```

也可以按资料类型调整：

| 类型 | 半衰期 |
|---|---:|
| AI 资讯 | 60 天 |
| 技术文档 | 180 天 |
| 法务合同 | 730 天 |
| 会计知识 | 1095 天 |
| 个人笔记 | 365 天 |

### 11.4 元数据得分

```text
metadata_score = topic_match * 0.40
               + tag_match * 0.30
               + entity_match * 0.20
               + source_type_match * 0.10
```

### 11.5 去重策略

问题：一个文档中多个相邻 chunk 同时命中，会导致结果被同一篇文章刷屏。

处理：

1. 同一 document 最多进入搜索结果 3 个 chunk；
2. RAG 上下文中，同一 document 最多使用 2 个 chunk，除非用户指定“只看这篇文档”；
3. 相邻 chunk 可合并为一个引用片段；
4. 相似度过高的 chunk 去重。

### 11.6 排序调试信息

每条结果应可展开查看：

```json
{
  "keyword_score": 0.72,
  "vector_score": 0.81,
  "freshness_score": 0.64,
  "value_score_norm": 0.90,
  "metadata_score": 0.70,
  "final_score": 0.765
}
```

---

## 12. ReRanker 重排序

### 12.1 MVP 策略

MVP 可先不引入重模型 ReRanker，采用规则重排：

1. 标题强命中上调；
2. 标签强命中上调；
3. 低质量内容下调；
4. 过短 chunk 下调；
5. 来源重复下调；
6. 用户当前打开专题优先。

### 12.2 P1 / P2 策略

后续可加入：

1. Cross-Encoder ReRanker；
2. LLM relevance judge；
3. BGE reranker；
4. Cohere rerank；
5. 自定义本地 reranker。

### 12.3 重排序接口

```ts
interface ReRanker {
  rerank(input: ReRankInput): Promise<SearchHit[]>
}

type ReRankInput = {
  query: string
  hits: SearchHit[]
  topN: number
  modelProvider?: string
}
```

---

## 13. RAG 问答总体流程

### 13.1 流程图

```text
用户提问
  ↓
QueryUnderstanding
  ↓
HybridSearch Top 40
  ↓
Filter + ReRank Top 12
  ↓
ContextBuilder
  ↓
PromptBuilder
  ↓
ModelProvider.chat
  ↓
CitationBuilder
  ↓
Answer + Citations
  ↓
QA History 入库
```

### 13.2 AskInput

```ts
type AskInput = {
  workspaceId: string
  question: string

  topicIds?: string[]
  tagIds?: string[]
  entityIds?: string[]
  sourceTypes?: string[]
  dateFrom?: string
  dateTo?: string

  conversationId?: string
  includeHistory?: boolean

  answerStyle?: "concise" | "detailed" | "report" | "bullet"
  language?: "zh" | "en" | "auto"

  maxContextChunks?: number
  includeDebug?: boolean
}
```

### 13.3 AskResponse

```ts
type AskResponse = {
  answerId: string
  conversationId: string
  question: string
  answer: string

  citations: Citation[]
  usedChunks: UsedChunk[]

  confidence: "high" | "medium" | "low" | "insufficient"
  limitations?: string[]

  debug?: RagDebugInfo

  createdAt: string
}
```

---

## 14. ContextBuilder 上下文构建

### 14.1 目标

ContextBuilder 负责把检索结果整理成适合 LLM 使用的上下文。

不是简单拼接 Top K，而要做到：

1. 保留来源信息；
2. 控制 token；
3. 去重；
4. 合并相邻 chunk；
5. 保留标题、日期、作者、标签；
6. 标记 chunk 编号，方便引用。

### 14.2 输入

```ts
type ContextBuilderInput = {
  question: string
  hits: SearchHit[]
  maxTokens: number
  maxChunks: number
  mergeAdjacentChunks: boolean
  includeMetadata: boolean
}
```

### 14.3 输出

```ts
type BuiltContext = {
  contextText: string
  usedChunks: UsedChunk[]
  tokenCount: number
  omittedChunks: number
}
```

### 14.4 上下文格式

建议格式：

```text
[资料 1]
chunk_id: chk_001
标题: xxx
来源: https://example.com/article
发布日期: 2026-06-01
标签: AI Agent, RAG
内容:
这里是片段正文……

[资料 2]
chunk_id: chk_002
标题: yyy
来源: PDF: 某某报告.pdf
页码: 12
内容:
这里是片段正文……
```

### 14.5 Token 控制

默认上下文预算：

| 模型上下文 | 检索上下文预算 |
|---|---:|
| 8K | 4K |
| 32K | 16K |
| 128K | 60K |
| 256K | 120K |

MVP 建议：

```text
默认 max_context_tokens = 12000
默认 max_context_chunks = 12
```

---

## 15. Prompt 设计

### 15.1 系统 Prompt 原则

必须明确约束：

1. 只能基于提供资料回答；
2. 不得编造来源；
3. 无资料时说明不足；
4. 重要结论必须引用；
5. 来源冲突时说明差异；
6. 不确定时明确标注。

### 15.2 中文问答 Prompt 模板

```text
你是一个严谨的知识库问答助手。

请严格基于【资料上下文】回答用户问题。

规则：
1. 不要使用资料外的信息进行扩展，除非明确说明这是一般性推断。
2. 如果资料不足以回答，请直接说明“当前知识库资料不足以回答该问题”。
3. 如果不同资料存在冲突，请指出冲突点。
4. 回答中涉及关键判断、数据、结论时，必须标注引用编号，例如：[资料 1]。
5. 不要编造不存在的引用。
6. 回答应结构清晰，优先使用中文。

【用户问题】
{{question}}

【资料上下文】
{{context}}

请输出：
1. 直接回答；
2. 依据；
3. 不确定性或限制，如果有。
```

### 15.3 不足资料 Prompt

当召回结果低于阈值时，不应强行问答。

判断条件：

```text
Top 1 final_score < 0.35
或
可用 chunks 数量 = 0
或
全部 chunks 低于 min_similarity
```

返回：

```text
当前知识库资料不足以可靠回答这个问题。

可以尝试：
1. 放宽专题或时间过滤；
2. 换一种关键词搜索；
3. 先导入更多相关资料；
4. 查看可能相关的搜索结果。
```

---

## 16. CitationBuilder 引用构建

### 16.1 引用目标

引用不是装饰，而是防幻觉和可追溯的核心。

每条引用应指向：

```text
document_id
chunk_id
source_id
source_url / file_path
标题
页码，可选
段落位置，可选
原文片段
```

### 16.2 Citation 数据结构

```ts
type Citation = {
  citationId: string
  label: string

  documentId: string
  chunkId: string
  sourceId: string

  title: string
  sourceType: string
  sourceUrl?: string
  sourceFileId?: string
  pageNumber?: number

  quotedText: string
  startOffset?: number
  endOffset?: number

  relevanceScore: number
}
```

### 16.3 引用显示形式

回答中显示：

```text
本地优先模式的核心价值是隐私和可控性，这使其更适合开发者、研究者和重隐私用户。[1]
```

回答下方显示：

```text
[1] 《双模式 AI 知识资产引擎完整开发文档》 - 第 3.1 节 Local Workspace
```

### 16.4 引用点击行为

点击引用后：

```text
打开文档阅读页
  ↓
定位到对应 chunk
  ↓
高亮引用片段
  ↓
显示原始来源
```

### 16.5 引用质量要求

1. 回答中引用编号必须存在；
2. 不允许引用未使用的 chunk；
3. 不允许同一句话后堆叠过多引用；
4. 引用片段应足以支撑结论；
5. 引用和答案明显不相关时允许用户反馈。

---

## 17. 防幻觉设计

### 17.1 防幻觉原则

RAG 问答必须遵守：

```text
有资料才回答；
资料不足就拒答；
资料冲突就说明；
推断必须标注；
引用必须可追溯。
```

### 17.2 低置信度场景

以下场景应降低置信度：

1. 召回结果少；
2. Top Score 低；
3. 资料时间过旧；
4. 资料互相矛盾；
5. 问题要求的范围超过知识库内容；
6. 用户问的是实时信息；
7. 用户要求预测或决策建议。

### 17.3 Confidence 计算

```text
confidence_score = top_score * 0.40
                 + avg_top5_score * 0.25
                 + citation_coverage * 0.20
                 + source_diversity * 0.10
                 + freshness_fit * 0.05
```

映射：

| 分数 | 置信度 |
|---|---|
| >= 0.75 | high |
| 0.55 - 0.75 | medium |
| 0.35 - 0.55 | low |
| < 0.35 | insufficient |

### 17.4 回答限制声明

低置信度时回答应包含：

```text
基于当前知识库资料，可以初步判断……
但资料数量有限 / 时间较旧 / 来源集中，因此结论需要谨慎使用。
```

---

## 18. 问答历史与会话模型

### 18.1 conversations 表

```sql
CREATE TABLE conversations (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    topic_id            TEXT,

    title               TEXT,
    status              TEXT NOT NULL DEFAULT 'active',

    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
);
```

### 18.2 qa_messages 表

```sql
CREATE TABLE qa_messages (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    conversation_id     TEXT NOT NULL,

    role                TEXT NOT NULL,
    content             TEXT NOT NULL,

    citations_json      TEXT,
    used_chunks_json    TEXT,
    retrieval_snapshot_id TEXT,

    confidence          TEXT,
    model_provider      TEXT,
    model_name          TEXT,
    prompt_version      TEXT,

    created_at          TEXT NOT NULL
);
```

### 18.3 多轮问答策略

MVP 建议：

1. 保留最近 6 轮对话；
2. 每轮仍然重新检索；
3. 用户追问时，将上一轮问题和答案作为 query rewrite 的参考；
4. 不允许仅凭上一轮模型答案继续编造；
5. 引用仍必须来自当前检索结果。

### 18.4 会话标题生成

首轮问答后自动生成标题：

```text
问题：本地优先知识库和云端知识库怎么取舍？
标题：本地优先与云端知识库取舍
```

---

## 19. 检索快照与调试日志

### 19.1 为什么需要 retrieval_snapshot

RAG 问答需要可复盘。

同一个问题在不同时间可能得到不同回答，因为：

1. 新资料加入；
2. embedding 模型变化；
3. 排序算法变化；
4. prompt 变化；
5. 用户修改标签或专题。

因此每次问答都应保存检索快照。

### 19.2 retrieval_snapshots 表

```sql
CREATE TABLE retrieval_snapshots (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    conversation_id     TEXT,
    question            TEXT NOT NULL,

    query_plan_json     TEXT,
    filters_json        TEXT,
    search_results_json TEXT,
    selected_chunks_json TEXT,
    scoring_json        TEXT,

    search_engine_version TEXT,
    embedding_model     TEXT,
    reranker_model      TEXT,

    created_at          TEXT NOT NULL
);
```

### 19.3 Debug 信息

```ts
type RagDebugInfo = {
  queryPlan: QueryPlan
  keywordHits: number
  vectorHits: number
  mergedHits: number
  selectedChunks: number
  omittedChunks: number
  contextTokenCount: number
  modelName: string
  promptVersion: string
  scores: Array<{
    chunkId: string
    keywordScore: number
    vectorScore: number
    finalScore: number
  }>
}
```

### 19.4 前端调试面板

开发者模式中显示：

1. 原始问题；
2. 改写 query；
3. 过滤条件；
4. 关键词召回列表；
5. 向量召回列表；
6. 合并后 Top K；
7. 每个 chunk 分数；
8. 最终上下文；
9. prompt 版本；
10. 模型输出原文。

---

## 20. 数据库设计补充

### 20.1 search_logs 表

```sql
CREATE TABLE search_logs (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    user_id             TEXT,

    query               TEXT NOT NULL,
    mode                TEXT NOT NULL,
    filters_json        TEXT,

    result_count        INTEGER NOT NULL DEFAULT 0,
    latency_ms          INTEGER,

    created_at          TEXT NOT NULL
);
```

### 20.2 answer_feedback 表

```sql
CREATE TABLE answer_feedback (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    qa_message_id       TEXT NOT NULL,

    rating              TEXT NOT NULL,
    feedback_text       TEXT,
    issue_type          TEXT,

    created_at          TEXT NOT NULL
);
```

`rating` 枚举：

```text
up
down
neutral
```

`issue_type` 枚举：

```text
wrong_answer
missing_citation
bad_citation
not_enough_detail
too_verbose
irrelevant_sources
hallucination
other
```

### 20.3 saved_searches 表，P1

```sql
CREATE TABLE saved_searches (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    user_id             TEXT,

    name                TEXT NOT NULL,
    query               TEXT NOT NULL,
    filters_json        TEXT,

    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
);
```

---

## 21. API 设计

### 21.1 搜索 API

```http
POST /api/workspaces/{workspaceId}/search
```

Request：

```json
{
  "query": "本地 AI Agent",
  "mode": "hybrid",
  "topicIds": ["topic_001"],
  "tagIds": ["tag_ai_agent"],
  "sourceTypes": ["url", "pdf"],
  "dateFrom": "2026-01-01",
  "limit": 20,
  "includeDebug": false
}
```

Response：

```json
{
  "query": "本地 AI Agent",
  "mode": "hybrid",
  "total": 18,
  "results": [
    {
      "documentId": "doc_001",
      "chunkId": "chk_001",
      "title": "本地 Agent 工作流研究",
      "snippet": "……",
      "sourceType": "url",
      "sourceUrl": "https://example.com",
      "finalScore": 0.83
    }
  ]
}
```

### 21.2 问答 API

```http
POST /api/workspaces/{workspaceId}/ask
```

Request：

```json
{
  "question": "本地优先知识库相比云端知识库的优势是什么？",
  "topicIds": ["topic_001"],
  "answerStyle": "detailed",
  "includeHistory": true,
  "includeDebug": false
}
```

Response：

```json
{
  "answerId": "ans_001",
  "conversationId": "conv_001",
  "answer": "本地优先知识库的主要优势是隐私、可控性和本地 Agent 集成……",
  "confidence": "high",
  "citations": [
    {
      "citationId": "cit_001",
      "label": "1",
      "documentId": "doc_001",
      "chunkId": "chk_001",
      "title": "双模式 AI 知识资产引擎完整开发文档",
      "quotedText": "本地工作区适合重隐私用户、本地模型用户、开发者、研究者。"
    }
  ]
}
```

### 21.3 获取会话列表

```http
GET /api/workspaces/{workspaceId}/conversations
```

### 21.4 获取会话详情

```http
GET /api/workspaces/{workspaceId}/conversations/{conversationId}
```

### 21.5 提交回答反馈

```http
POST /api/workspaces/{workspaceId}/answers/{answerId}/feedback
```

Request：

```json
{
  "rating": "down",
  "issueType": "bad_citation",
  "feedbackText": "引用和结论不太相关"
}
```

### 21.6 获取检索快照

```http
GET /api/workspaces/{workspaceId}/retrieval-snapshots/{snapshotId}
```

仅开发者模式或本地调试模式开放。

---

## 22. 前端页面设计

### 22.1 搜索页

页面功能：

1. 搜索输入框；
2. 搜索模式选择：关键词 / 语义 / 混合；
3. 筛选器：专题、标签、实体、时间、来源类型；
4. 搜索结果列表；
5. 结果摘要；
6. 高亮片段；
7. 分数展示，可选；
8. 点击打开文档。

### 22.2 问答页

页面功能：

1. 类 ChatGPT 对话界面；
2. 当前工作区 / 专题显示；
3. 可选筛选条件；
4. 回答正文；
5. 引用卡片；
6. 置信度提示；
7. 继续追问；
8. 反馈按钮；
9. 重新生成；
10. 查看检索过程，开发者模式。

### 22.3 引用卡片

引用卡片显示：

```text
[1] 文档标题
来源类型：URL / PDF / 文本
时间：2026-06-01
片段：……
按钮：打开原文 / 查看上下文 / 复制引用
```

### 22.4 检索调试页

面向开发者和高级用户：

1. QueryPlan；
2. Hybrid Score；
3. Top K chunks；
4. Context token 数；
5. Prompt；
6. Model output；
7. Citation mapping。

---

## 23. 本地与云端实现差异

### 23.1 本地实现

| 模块 | 本地方案 |
|---|---|
| 关键词检索 | SQLite FTS5 / LIKE |
| 向量检索 | sqlite-vec / LanceDB / hnswlib |
| 元数据过滤 | SQLite SQL |
| RAG 模型 | Ollama / LM Studio / 用户 API Key |
| 日志 | SQLite |
| 引用跳转 | 本地 Vault 文件路径 |

### 23.2 云端实现

| 模块 | 云端方案 |
|---|---|
| 关键词检索 | PostgreSQL FTS / trigram / ILIKE |
| 向量检索 | pgvector |
| 元数据过滤 | PostgreSQL SQL |
| RAG 模型 | Cloud Model API / 用户 API Key |
| 日志 | PostgreSQL |
| 引用跳转 | 签名 URL / Web 阅读页 |

### 23.3 统一接口要求

业务层只依赖：

```text
SearchEngine
RAGService
ContextBuilder
CitationBuilder
ModelProvider
```

不得在 UI 层直接判断 SQLite、pgvector、LanceDB 等底层实现。

---

## 24. 权限与隐私

### 24.1 本地模式

默认策略：

1. 检索只发生在本地；
2. 问答可使用本地模型；
3. 若用户选择云端模型，必须提示资料片段将发送给模型 API；
4. 不上传 retrieval_snapshot；
5. 不上传问答历史，除非用户开启同步。

### 24.2 云端模式

要求：

1. 所有检索必须按 workspace_id 隔离；
2. 后续团队模式下需按 user_id / role / permission 过滤；
3. API Key 加密存储；
4. 问答日志可由用户删除；
5. 支持导出问答历史。

### 24.3 混合模式

默认：

1. 本地工作区资料不上传；
2. 手机端 Inbox 内容拉取后才参与本地检索；
3. 如果启用云端摘要同步，必须明确说明哪些内容同步；
4. 引用原文若仅在本地，不生成云端可访问链接。

---

## 25. 性能要求

### 25.1 搜索性能

MVP 目标：

| 数据量 | 搜索响应目标 |
|---|---:|
| 1,000 文档 / 20,000 chunks | < 1 秒 |
| 10,000 文档 / 200,000 chunks | < 3 秒 |
| 50,000 文档 / 1,000,000 chunks | 后续优化 |

### 25.2 RAG 问答性能

目标：

| 步骤 | 时间目标 |
|---|---:|
| 查询理解 | < 500ms，不调用模型时 |
| 检索 | < 2s |
| 上下文构建 | < 500ms |
| 模型生成 | 取决于模型 |
| 引用构建 | < 500ms |

### 25.3 本地性能注意

1. 不要每次搜索全表扫描；
2. embedding 索引必须增量更新；
3. metadata filter 应尽量前置；
4. 搜索日志不要无限增长；
5. retrieval_snapshot 可定期清理或压缩。

---

## 26. 失败处理

### 26.1 常见失败

| 场景 | 处理 |
|---|---|
| embedding 模型不可用 | 降级关键词检索 |
| 向量索引损坏 | 提示重建索引 |
| 模型调用失败 | 保留搜索结果，提示问答失败 |
| 无召回结果 | 返回资料不足提示 |
| 上下文过长 | 自动裁剪并提示 |
| 引用构建失败 | 回答不发布或标记引用异常 |
| 权限不足 | 不返回相关资料 |

### 26.2 降级策略

```text
Hybrid Search
  ↓ 如果向量失败
Keyword Search
  ↓ 如果关键词也无结果
返回无结果提示
```

```text
RAG Ask
  ↓ 检索成功但模型失败
返回搜索结果 + 提示模型不可用
```

---

## 27. 测试方案

### 27.1 单元测试

1. QueryUnderstanding 测试；
2. KeywordSearch 测试；
3. VectorSearch 测试；
4. HybridRanker 测试；
5. MetadataFilter 测试；
6. ContextBuilder 测试；
7. CitationBuilder 测试；
8. Confidence 计算测试。

### 27.2 集成测试

1. 导入文档后可被搜索；
2. 标签过滤正确；
3. 专题过滤正确；
4. 时间过滤正确；
5. 向量召回相关内容；
6. RAG 回答带引用；
7. 无资料时拒答；
8. 问答历史正确入库；
9. 检索快照可复盘。

### 27.3 检索质量测试

建立小型测试集：

```text
query
expected_document_ids
expected_chunk_ids
must_include_terms
should_not_include_documents
```

指标：

| 指标 | 说明 |
|---|---|
| Recall@K | 目标资料是否在 Top K 中 |
| MRR | 第一个正确结果排名 |
| Citation Accuracy | 引用是否支撑回答 |
| Answer Groundedness | 回答是否基于资料 |
| Refusal Accuracy | 无资料时是否拒答 |

### 27.4 人工验收问题示例

1. “这个项目为什么要本地优先？”
2. “云端 Inbox 的作用是什么？”
3. “手机端第一版要不要做完整知识库管理？”
4. “本地模式和云端模式的数据存储有什么区别？”
5. “MVP 为什么不做双向同步？”
6. “有哪些隐私风险？”
7. “阶段四向量化完成后，阶段五怎么使用这些 chunk？”

---

## 28. 开发任务拆解

### 28.1 后端 / 本地服务任务

| 编号 | 任务 | 优先级 |
|---|---|---|
| S5-BE-001 | 定义 SearchEngine 接口 | P0 |
| S5-BE-002 | 实现 SearchInput / SearchResponse DTO | P0 |
| S5-BE-003 | 实现 SQLite FTS / 本地关键词检索 | P0 |
| S5-BE-004 | 实现本地向量检索适配 | P0 |
| S5-BE-005 | 实现 PostgreSQL / pgvector 检索适配 | P0 |
| S5-BE-006 | 实现 MetadataFilter | P0 |
| S5-BE-007 | 实现 HybridRanker | P0 |
| S5-BE-008 | 实现 ContextBuilder | P0 |
| S5-BE-009 | 实现 RAGService | P0 |
| S5-BE-010 | 实现 CitationBuilder | P0 |
| S5-BE-011 | 实现 conversations / qa_messages 表 | P0 |
| S5-BE-012 | 实现 retrieval_snapshots 表 | P1 |
| S5-BE-013 | 实现 answer_feedback 表 | P1 |
| S5-BE-014 | 实现检索调试信息 | P1 |
| S5-BE-015 | 实现降级策略 | P0 |

### 28.2 前端任务

| 编号 | 任务 | 优先级 |
|---|---|---|
| S5-FE-001 | 搜索页 UI | P0 |
| S5-FE-002 | 搜索筛选器 | P0 |
| S5-FE-003 | 搜索结果列表 | P0 |
| S5-FE-004 | 问答页 UI | P0 |
| S5-FE-005 | 引用卡片 | P0 |
| S5-FE-006 | 文档定位与高亮 | P1 |
| S5-FE-007 | 会话列表 | P1 |
| S5-FE-008 | 回答反馈按钮 | P1 |
| S5-FE-009 | 检索调试面板 | P1 |
| S5-FE-010 | 置信度提示 | P0 |

### 28.3 模型与 Prompt 任务

| 编号 | 任务 | 优先级 |
|---|---|---|
| S5-AI-001 | 定义 RAG Prompt v1 | P0 |
| S5-AI-002 | 定义拒答 Prompt | P0 |
| S5-AI-003 | 定义查询改写 Prompt，可选 | P1 |
| S5-AI-004 | 定义回答风格模板 | P1 |
| S5-AI-005 | 定义引用约束规则 | P0 |
| S5-AI-006 | 定义 groundedness 检查，可选 | P2 |

---

## 29. 验收标准

### 29.1 搜索验收

1. 用户可以输入关键词搜索；
2. 用户可以切换关键词 / 语义 / 混合模式；
3. 搜索结果显示标题、摘要片段、来源、时间、分数；
4. 可以按专题过滤；
5. 可以按标签过滤；
6. 可以按来源类型过滤；
7. 可以按时间过滤；
8. 点击结果可以打开对应文档；
9. 本地模式不依赖云端；
10. 云端模式按 workspace_id 隔离。

### 29.2 RAG 问答验收

1. 用户可以对当前工作区提问；
2. 回答基于检索结果生成；
3. 回答必须带引用；
4. 引用可以点击查看原文；
5. 无资料时系统拒答；
6. 低置信度时有提示；
7. 问答历史可查看；
8. 同一会话可继续追问；
9. 检索快照可记录；
10. 模型失败时有明确错误提示。

### 29.3 防幻觉验收

1. 问一个知识库没有的问题，系统不得编造；
2. 问一个资料中有明确答案的问题，回答应引用正确来源；
3. 问一个资料冲突的问题，系统应提示冲突；
4. 删除相关文档后，同一问题不应继续引用已删除资料；
5. 修改 chunk 内容后，重建索引后回答应更新。

---

## 30. 风险与应对

### 30.1 检索质量不稳定

风险：关键词和向量召回都可能不准确。

应对：

1. 混合检索，不依赖单一方式；
2. 加入标签、实体、专题过滤；
3. 保存检索日志；
4. 建立小型评测集；
5. 后续引入 ReRanker。

### 30.2 RAG 回答幻觉

风险：模型可能基于常识补充资料外内容。

应对：

1. Prompt 明确限制；
2. 无资料时拒答；
3. 引用强约束；
4. 保存 used_chunks；
5. 用户反馈 bad citation。

### 30.3 本地向量库部署复杂

风险：sqlite-vec、LanceDB、hnswlib 跨平台打包可能有问题。

应对：

1. 抽象 VectorStore；
2. 先支持一种最稳方案；
3. 失败时降级关键词检索；
4. 提供重建索引功能。

### 30.4 查询速度变慢

风险：资料量增加后，搜索和问答变慢。

应对：

1. 索引表；
2. metadata filter 前置；
3. Top K 限制；
4. 分页；
5. 缓存 query embedding；
6. 日志定期清理。

### 30.5 引用无法准确定位

风险：PDF 页码、网页段落、Markdown offset 不准确。

应对：

1. chunk 保存 start_offset / end_offset；
2. PDF 解析时保存 pageNumber；
3. 引用定位失败时退回文档页；
4. 保留 quotedText。

---

## 31. 推荐实施顺序

### 第一步：搜索最小闭环

```text
SearchInput
  ↓
KeywordSearch
  ↓
VectorSearch
  ↓
HybridRanker
  ↓
SearchResult UI
```

### 第二步：RAG 最小闭环

```text
用户提问
  ↓
Hybrid Search
  ↓
ContextBuilder
  ↓
ModelProvider
  ↓
Answer + Citations
```

### 第三步：追溯与历史

```text
CitationBuilder
  ↓
qa_messages
  ↓
conversations
  ↓
retrieval_snapshots
```

### 第四步：调试和优化

```text
Debug Panel
  ↓
Feedback
  ↓
Evaluation Set
  ↓
Ranking 调参
```

---

## 32. 阶段完成后的产品效果

阶段五完成后，用户体验应达到：

```text
用户已经导入大量资料
  ↓
可以像搜索引擎一样查资料
  ↓
可以像问专家一样问自己的知识库
  ↓
每个回答都有来源
  ↓
每个来源都能点回原文
  ↓
回答不会脱离资料乱编
  ↓
知识库开始真正变成可复用资产
```

一句话总结：

> 阶段五的核心不是“接一个大模型问答”，而是建立一个可检索、可引用、可拒答、可调试的知识库问答系统，让资料真正从“存起来”变成“能被可靠调用”。
