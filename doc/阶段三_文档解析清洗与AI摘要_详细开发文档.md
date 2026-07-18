# 阶段三：文档解析、清洗与 AI 摘要详细开发文档

版本：V0.1  
所属项目：双模式 AI 知识资产引擎  
所属阶段：阶段三  
阶段主题：文档解析、清洗与 AI 摘要  
上游阶段：阶段二：资料导入与 Inbox  
下游阶段：阶段四：标签、实体、分块与向量化  

---

## 1. 阶段定位

阶段三的核心目标是：

> **把阶段二产生的 source 原始资料，转化为结构清晰、内容干净、可阅读、可摘要、可进入后续检索与 RAG 流水线的标准 document。**

阶段二解决“资料如何进入系统”，阶段三解决“资料进入系统后如何被理解和整理”。

本阶段不是简单把文件内容读出来，而是要建立一个稳定、可追踪、可重试、可扩展的文档处理流水线，使不同来源的 URL、PDF、纯文本、Markdown、后续图片 OCR、音频转写结果，都能被统一转化为标准文档对象。

---

## 2. 阶段目标

### 2.1 核心目标

1. 从 `sources` 表中读取待处理资料；
2. 按 source_type 调用不同解析器；
3. 提取标题、作者、来源、发布时间、正文、附件等元数据；
4. 清洗广告、导航、页眉页脚、重复段落、无效字符；
5. 标准化为 Markdown 与纯文本；
6. 创建 `documents` 记录；
7. 调用 AI 生成摘要、一句话结论、关键要点、推荐标签、价值评分；
8. 记录 AI 原始输出、模型、Prompt 版本、错误信息；
9. 为阶段四分块、Embedding、索引提供稳定输入。

### 2.2 阶段完成后的效果

用户导入 URL、PDF 或文本后，系统可以在文档详情页看到：

```text
标题
来源
正文 Markdown
AI 摘要
一句话结论
关键要点
推荐标签
价值评分
质量评分
处理状态
错误信息
```

---

## 3. 阶段边界

### 3.1 本阶段必须做

1. URL 正文解析；
2. PDF 文本解析；
3. 纯文本解析；
4. Markdown 标准化；
5. 内容清洗；
6. 文档入库；
7. AI 摘要；
8. 质量评分；
9. 失败重试；
10. 处理日志；
11. 桌面端摘要展示。

### 3.2 本阶段预留但不强制完成

1. 图片 OCR；
2. 音频转写；
3. 视频字幕提取；
4. 网页截图保存；
5. 表格结构化抽取；
6. 多模态摘要；
7. 深度研究报告生成。

这些能力可以先以 Processor 接口形式预留，具体实现放到后续版本。

### 3.3 本阶段不做

1. 不做向量化；
2. 不做混合检索；
3. 不做 RAG 问答；
4. 不做 Obsidian 导出；
5. 不做复杂知识图谱；
6. 不做 Agent API；
7. 不做团队协作文档编辑。

---

## 4. 上下游关系

### 4.1 上游输入

阶段二会产生：

```text
inbox_item
  ↓
source
```

本阶段从 `sources` 表中读取 `status = queued` 或 `status = pending` 的记录。

### 4.2 本阶段输出

阶段三输出：

```text
source
  ↓
document
  ↓
AI 摘要字段
  ↓
等待阶段四分块与向量化
```

### 4.3 下游依赖

阶段四依赖以下字段：

1. `documents.id`；
2. `documents.content_markdown`；
3. `documents.content_text`；
4. `documents.title`；
5. `documents.topic_id`；
6. `documents.summary`；
7. `documents.recommended_tags`；
8. `documents.quality_score`；
9. `documents.value_score`。

---

## 5. 总体处理流程

```text
source.queued
  ↓
ParseWorker 领取任务
  ↓
SourceProcessorFactory 选择解析器
  ├── URLProcessor
  ├── PDFProcessor
  ├── TextProcessor
  ├── MarkdownProcessor
  ├── ImageOCRProcessor，后续
  └── AudioTranscribeProcessor，后续
  ↓
RawContentExtracted
  ↓
ContentCleaner
  ↓
MarkdownNormalizer
  ↓
MetadataExtractor
  ↓
DocumentBuilder
  ↓
documents 入库
  ↓
AISummaryWorker
  ↓
AI 摘要 / 关键要点 / 标签 / 评分
  ↓
document.ai_status = done
  ↓
等待阶段四处理
```

---

## 6. 核心设计原则

### 6.1 原始资料不可覆盖

原始文件、原始 HTML、原始文本必须保留，清洗后的内容单独保存。

```text
raw_content：原始抽取内容
cleaned_content：清洗后内容
content_markdown：标准 Markdown
content_text：纯文本
```

原因：

1. 便于重新清洗；
2. 便于回溯错误；
3. 便于升级解析器后重跑；
4. 便于用户核对来源。

### 6.2 每一步都要可重试

解析失败、清洗失败、AI 摘要失败，不能影响其他任务。

必须支持：

```text
单条重试
批量重试
从失败步骤继续
更换模型后重跑 AI 摘要
升级 Prompt 后重跑 AI 摘要
```

### 6.3 本地和云端共用同一套流水线抽象

本地模式与云端模式可以使用不同运行时，但必须共享：

1. 相同领域模型；
2. 相同状态机；
3. 相同 Processor 接口；
4. 相同 AI 输出 JSON Schema；
5. 相同 Prompt 版本管理。

### 6.4 摘要必须结构化

AI 摘要不能只保存一段自然语言，而必须保存结构化字段：

```text
summary
one_sentence_conclusion
key_points
recommended_tags
value_score
value_score_reason
should_deep_process
```

### 6.5 允许低质量资料进入，但必须被识别

不是所有资料都值得深度加工。系统应给出 `quality_score` 和 `value_score`，供后续排序、过滤和报告生成使用。

---

## 7. 模块拆分

阶段三建议拆分为以下模块：

```text
DocumentProcessingModule
  ├── ParseWorker
  ├── SourceProcessorFactory
  ├── URLProcessor
  ├── PDFProcessor
  ├── TextProcessor
  ├── MarkdownProcessor
  ├── ContentCleaner
  ├── MarkdownNormalizer
  ├── MetadataExtractor
  ├── DocumentBuilder
  ├── AISummaryWorker
  ├── SummaryPromptManager
  ├── QualityScorer
  └── ProcessingLogService
```

---

## 8. 核心数据模型

## 8.1 sources 表补充说明

阶段三主要消费 `sources` 表。

```sql
CREATE TABLE sources (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    topic_id            TEXT,
    inbox_item_id       TEXT,

    source_type         TEXT NOT NULL,
    title               TEXT,
    url                 TEXT,
    domain              TEXT,
    author              TEXT,
    published_at        TEXT,

    original_file_id    TEXT,
    raw_text            TEXT,
    content_hash        TEXT,

    status              TEXT NOT NULL DEFAULT 'pending',
    error_message       TEXT,
    retry_count         INTEGER NOT NULL DEFAULT 0,

    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
);
```

### 8.1.1 source_type 枚举

```text
url
pdf
text
markdown
image
audio
file
mixed
```

### 8.1.2 sources.status 阶段三状态

```text
pending
queued
fetching
parsing
cleaning
document_created
ai_processing
done
failed
archived
```

---

## 8.2 documents 表

```sql
CREATE TABLE documents (
    id                          TEXT PRIMARY KEY,
    workspace_id                TEXT NOT NULL,
    source_id                   TEXT NOT NULL,
    topic_id                    TEXT,

    title                       TEXT NOT NULL,
    source_type                 TEXT NOT NULL,
    source_url                  TEXT,
    source_domain               TEXT,
    author                      TEXT,
    published_at                TEXT,

    content_markdown            TEXT,
    content_text                TEXT,
    language                    TEXT,
    word_count                  INTEGER,
    reading_time_minutes        INTEGER,

    summary                     TEXT,
    one_sentence_conclusion     TEXT,
    key_points                  TEXT,
    recommended_tags            TEXT,

    quality_score               INTEGER,
    value_score                 INTEGER,
    value_score_reason          TEXT,
    should_deep_process         INTEGER NOT NULL DEFAULT 1,

    parse_status                TEXT NOT NULL DEFAULT 'pending',
    clean_status                TEXT NOT NULL DEFAULT 'pending',
    ai_status                   TEXT NOT NULL DEFAULT 'pending',
    chunk_status                TEXT NOT NULL DEFAULT 'pending',
    index_status                TEXT NOT NULL DEFAULT 'pending',

    parser_name                 TEXT,
    parser_version              TEXT,
    cleaner_version             TEXT,
    ai_model                    TEXT,
    prompt_version              TEXT,
    ai_raw_output               TEXT,
    ai_error_message            TEXT,

    created_at                  TEXT NOT NULL,
    updated_at                  TEXT NOT NULL
);
```

### 8.2.1 字段说明

| 字段 | 说明 |
|---|---|
| `content_markdown` | 标准化后的 Markdown 正文 |
| `content_text` | 去除 Markdown 标记后的纯文本 |
| `summary` | 300-800 字中文摘要 |
| `one_sentence_conclusion` | 一句话结论 |
| `key_points` | JSON 数组，关键要点 |
| `recommended_tags` | JSON 数组，AI 推荐标签 |
| `quality_score` | 内容质量评分，0-100 |
| `value_score` | 知识价值评分，0-100 |
| `should_deep_process` | 是否建议进入深度处理 |
| `ai_raw_output` | AI 原始返回，便于排查 |

---

## 8.3 document_processing_logs 表

为了支持排错、重试、性能分析，建议新增处理日志表。

```sql
CREATE TABLE document_processing_logs (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    source_id           TEXT,
    document_id         TEXT,

    step_name           TEXT NOT NULL,
    status              TEXT NOT NULL,
    message             TEXT,
    error_code          TEXT,
    error_stack         TEXT,

    input_snapshot      TEXT,
    output_snapshot     TEXT,

    started_at          TEXT,
    finished_at         TEXT,
    duration_ms         INTEGER,

    created_at          TEXT NOT NULL
);
```

### 8.3.1 step_name 枚举

```text
fetch_url
parse_url
parse_pdf
parse_text
clean_content
normalize_markdown
extract_metadata
create_document
ai_summarize
quality_score
```

### 8.3.2 status 枚举

```text
started
success
failed
skipped
retrying
```

---

## 8.4 document_versions 表，可选

如果后续需要支持重新清洗和摘要版本对比，可以预留文档版本表。

```sql
CREATE TABLE document_versions (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    document_id         TEXT NOT NULL,
    version_no          INTEGER NOT NULL,

    content_markdown    TEXT,
    content_text        TEXT,
    summary             TEXT,
    ai_model            TEXT,
    prompt_version      TEXT,
    change_reason       TEXT,

    created_at          TEXT NOT NULL
);
```

MVP 可暂不启用，但建议保留设计。

---

## 9. TypeScript 核心接口设计

### 9.1 SourceProcessor 接口

```ts
interface SourceProcessor {
  support(source: Source): boolean
  parse(input: ParseInput): Promise<ParseOutput>
}

interface ParseInput {
  workspaceId: string
  source: Source
  fileBuffer?: Buffer
  rawText?: string
}

interface ParseOutput {
  title?: string
  author?: string
  publishedAt?: string
  domain?: string
  language?: string
  rawText: string
  rawHtml?: string
  markdown?: string
  metadata?: Record<string, any>
  assets?: ParsedAsset[]
}

interface ParsedAsset {
  type: 'image' | 'table' | 'attachment'
  name?: string
  fileId?: string
  url?: string
  metadata?: Record<string, any>
}
```

---

### 9.2 ContentCleaner 接口

```ts
interface ContentCleaner {
  clean(input: CleanInput): Promise<CleanOutput>
}

interface CleanInput {
  sourceType: string
  title?: string
  rawText: string
  rawHtml?: string
  markdown?: string
  metadata?: Record<string, any>
}

interface CleanOutput {
  cleanedMarkdown: string
  cleanedText: string
  removedBlocks?: RemovedBlock[]
  qualityWarnings?: string[]
}

interface RemovedBlock {
  type: 'ad' | 'nav' | 'footer' | 'duplicate' | 'empty' | 'script' | 'style' | 'noise'
  text: string
}
```

---

### 9.3 DocumentBuilder 接口

```ts
interface DocumentBuilder {
  build(input: BuildDocumentInput): Promise<Document>
}

interface BuildDocumentInput {
  workspaceId: string
  source: Source
  parsed: ParseOutput
  cleaned: CleanOutput
}
```

---

### 9.4 AISummaryService 接口

```ts
interface AISummaryService {
  summarize(input: SummaryInput): Promise<SummaryOutput>
}

interface SummaryInput {
  workspaceId: string
  documentId: string
  title: string
  contentMarkdown: string
  contentText: string
  sourceType: string
  language?: string
  modelConfig: ModelConfig
}

interface SummaryOutput {
  summary: string
  oneSentenceConclusion: string
  keyPoints: KeyPoint[]
  valueScore: number
  valueScoreReason: string
  recommendedTags: string[]
  shouldDeepProcess: boolean
  rawOutput: string
  model: string
  promptVersion: string
}

interface KeyPoint {
  text: string
  importance: 'high' | 'medium' | 'low'
  evidence?: string
}
```

---

## 10. URL 解析设计

### 10.1 URLProcessor 目标

URLProcessor 负责从网页中提取正文和元数据。

输入：

```text
source_type = url
source.url 不为空
```

输出：

```text
标题
作者
发布时间
域名
正文 Markdown
正文纯文本
原始 HTML，可选保存
```

---

### 10.2 URL 解析流程

```text
读取 source.url
  ↓
检查 URL 合法性
  ↓
请求网页 HTML
  ↓
处理编码
  ↓
抽取正文
  ↓
抽取标题 / 作者 / 时间 / 域名
  ↓
转换 Markdown
  ↓
进入清洗模块
```

---

### 10.3 URL 请求策略

必须设置：

```text
timeout = 20s
max_redirects = 5
user_agent = KnowledgeEngineBot/0.1
max_html_size = 10MB
```

异常处理：

| 异常 | 处理 |
|---|---|
| 404 | 标记 failed |
| 403 | 提示网页禁止访问 |
| 超时 | 可重试 |
| 编码异常 | 尝试自动识别 |
| 正文过短 | 标记 low_quality |
| 需要登录 | 标记 auth_required |

---

### 10.4 正文抽取策略

MVP 推荐采用组合策略：

```text
Readability 规则抽取
  ↓
HTML 主体清洗
  ↓
Markdown 转换
  ↓
正文长度校验
```

本地桌面端可使用：

```text
Mozilla Readability
Turndown
Cheerio
```

后端 .NET 可使用：

```text
HtmlAgilityPack
ReadSharp / ReadabilitySharp
ReverseMarkdown
```

---

### 10.5 URL 元数据抽取

优先级：

```text
OpenGraph
  ↓
Twitter Card
  ↓
JSON-LD
  ↓
HTML meta
  ↓
正文标题推断
```

字段：

```text
title
author
published_at
domain
site_name
canonical_url
cover_image
```

---

## 11. PDF 解析设计

### 11.1 PDFProcessor 目标

PDFProcessor 负责从 PDF 文件中抽取文本和基本结构。

输入：

```text
source_type = pdf 或 file
original_file_id 不为空
```

输出：

```text
标题
页数
正文文本
按页 Markdown
元数据
质量提示
```

---

### 11.2 PDF 解析流程

```text
读取 PDF 文件
  ↓
检查文件大小和页数
  ↓
抽取 metadata
  ↓
逐页抽取文本
  ↓
合并段落
  ↓
识别标题层级，轻量版
  ↓
生成 Markdown
  ↓
进入清洗模块
```

---

### 11.3 PDF 限制策略

MVP 建议限制：

```text
单个 PDF 最大 100MB
单个 PDF 最大 300 页
超过限制进入 waiting_manual_confirm
```

原因：

1. 避免本地模型或云端 Worker 被大文件拖死；
2. 避免长文档摘要成本过高；
3. 后续可做分段摘要和递归摘要。

---

### 11.4 PDF 类型识别

```text
可复制文本 PDF
  → 直接文本抽取

扫描版 PDF
  → 标记 needs_ocr
  → OCR 后续处理

混合 PDF
  → 文本抽取 + OCR 预留
```

MVP 阶段扫描版 PDF 不强制 OCR，只需给出状态提示：

```text
该 PDF 可能为扫描版，当前版本暂未启用 OCR，无法完整解析。
```

---

### 11.5 PDF Markdown 结构

```markdown
# 文档标题

> 来源：PDF
> 页数：32

## 第 1 页

页面文本内容……

## 第 2 页

页面文本内容……
```

如果能识别目录或标题层级，则使用真实标题替代页码标题。

---

## 12. 文本与 Markdown 解析设计

### 12.1 TextProcessor

适用于：

```text
用户直接输入的文本
手机端聊天窗口提交的文字
剪贴板长文本
```

处理流程：

```text
读取 source.raw_text
  ↓
判断语言
  ↓
去除首尾空白
  ↓
合并异常换行
  ↓
推断标题
  ↓
转为 Markdown
```

标题推断规则：

1. 如果第一行较短且像标题，则作为标题；
2. 如果没有标题，使用前 30 个字符生成标题；
3. 如果用户已指定 suggested_title，则优先使用。

---

### 12.2 MarkdownProcessor

适用于：

```text
.md 文件
Obsidian 笔记
用户粘贴 Markdown
```

处理原则：

1. 保留标题结构；
2. 保留列表、代码块、引用；
3. 清理无效 frontmatter；
4. 提取 YAML frontmatter 元数据；
5. 生成纯文本用于 AI 摘要。

---

## 13. 内容清洗设计

### 13.1 清洗目标

ContentCleaner 的目标不是改写内容，而是删除噪音、修复格式、提升可读性。

必须避免：

1. 不得改变原文观点；
2. 不得添加 AI 解释；
3. 不得压缩成摘要；
4. 不得删除正文关键段落。

---

### 13.2 清洗规则

基础规则：

```text
删除空行堆叠
删除重复空格
删除脚本和样式
删除导航菜单
删除广告块
删除相关推荐
删除版权页脚
删除社交分享按钮
删除明显重复段落
修复异常换行
统一 Markdown 标题层级
统一列表格式
```

---

### 13.3 重复段落识别

```text
按段落切分
  ↓
标准化段落文本
  ↓
计算 hash
  ↓
连续重复或全局重复删除
```

保留策略：

1. 第一次出现保留；
2. 标题重复不一定删除；
3. 表格行不参与普通重复判断。

---

### 13.4 低质量内容识别

如果出现以下情况，应降低 quality_score：

| 情况 | 处理 |
|---|---|
| 正文少于 200 字 | low_quality |
| 广告占比过高 | low_quality |
| 大量乱码 | parse_failed 或 low_quality |
| 只有目录无正文 | low_quality |
| PDF 扫描版无文字 | needs_ocr |
| 网页需要登录 | auth_required |
| 重复内容超过 50% | low_quality |

---

## 14. Markdown 标准化设计

### 14.1 标准 Markdown 目标

系统内部统一使用 Markdown 作为主文档格式。

好处：

1. 便于阅读；
2. 便于导出 Obsidian；
3. 便于分块；
4. 便于 RAG 引用；
5. 便于版本管理。

---

### 14.2 标准结构

```markdown
# {title}

> 来源：{source_type}
> 链接：{source_url}
> 作者：{author}
> 发布时间：{published_at}

---

正文内容……
```

### 14.3 标题层级规范

```text
文档标题：#
正文一级标题：##
正文二级标题：###
正文三级标题：####
```

如果网页正文已有多个 `#`，需要整体下移一级，避免与文档标题冲突。

---

### 14.4 代码块处理

代码块必须保留：

````markdown
```ts
const a = 1
```
````

不得在清洗阶段破坏代码缩进。

---

### 14.5 表格处理

MVP 允许将表格转为 Markdown 表格。

如果表格过复杂：

```text
保留为纯文本
或标记 table_extraction_limited
```

后续可增加结构化表格抽取。

---

## 15. AI 摘要设计

### 15.1 AI 摘要目标

AI 摘要不是为了替代原文，而是为了帮助用户快速判断资料价值。

AI 摘要应回答：

1. 这篇资料讲什么？
2. 最重要的结论是什么？
3. 哪些要点值得保存？
4. 是否值得进入深度处理？
5. 推荐归入哪些标签？
6. 对当前知识库的价值有多高？

---

### 15.2 摘要输入控制

如果文档较短：

```text
全文输入模型
```

如果文档较长：

```text
先截取标题、开头、目录、关键段落、结尾
或后续进入递归摘要
```

MVP 建议限制：

```text
summary_input_max_tokens = 12000
```

超出后采用：

```text
前 40% + 中间 30% + 后 30%
```

后续版本再做 map-reduce 摘要。

---

### 15.3 AI 输出 JSON Schema

```json
{
  "summary": "300-800 字中文摘要",
  "one_sentence_conclusion": "一句话结论",
  "key_points": [
    {
      "text": "关键要点",
      "importance": "high",
      "evidence": "来自原文的依据，可选"
    }
  ],
  "value_score": 85,
  "value_score_reason": "评分理由",
  "recommended_tags": ["AI Agent", "企业服务"],
  "should_deep_process": true
}
```

---

### 15.4 Prompt 模板

```text
你是一个知识资产整理助手。请基于用户提供的文档内容，生成结构化中文摘要。

要求：
1. 必须忠实于原文，不得编造原文没有的信息。
2. 不要输出 Markdown，不要输出解释，只输出严格 JSON。
3. summary 使用中文，长度 300-800 字。
4. one_sentence_conclusion 用一句话概括最重要结论。
5. key_points 输出 3-8 条，每条包含 text、importance、evidence。
6. value_score 为 0-100 的整数，表示该资料对长期知识资产的价值。
7. recommended_tags 输出 3-8 个标签。
8. should_deep_process 表示是否值得进入后续分块、向量化和深度分析。

评分参考：
- 90-100：高价值资料，适合长期保存和深度研究。
- 70-89：有明显价值，适合进入知识库。
- 50-69：一般资料，可保存但不必优先处理。
- 30-49：低价值资料，可能只是资讯噪音。
- 0-29：无明显知识价值或解析质量很差。

请严格返回如下 JSON：
{
  "summary": "",
  "one_sentence_conclusion": "",
  "key_points": [
    {"text": "", "importance": "high|medium|low", "evidence": ""}
  ],
  "value_score": 0,
  "value_score_reason": "",
  "recommended_tags": [],
  "should_deep_process": true
}

文档标题：{title}
资料类型：{source_type}
正文内容：
{content}
```

---

### 15.5 AI 输出校验

AI 返回后必须校验：

1. 是否为合法 JSON；
2. 是否包含必填字段；
3. value_score 是否为 0-100；
4. key_points 是否为数组；
5. recommended_tags 是否为数组；
6. summary 是否为空；
7. 是否出现明显幻觉提示。

校验失败处理：

```text
第一次失败：尝试 JSON 修复
第二次失败：重新请求模型，使用更严格 Prompt
第三次失败：标记 ai_status = failed
```

---

### 15.6 AI 摘要模型策略

本地模式：

```text
优先使用用户配置的 Ollama / LM Studio 模型
允许用户配置摘要专用模型
```

云端模式：

```text
使用云端默认模型
或用户自带 API Key
```

混合模式：

```text
本地资料可选择本地模型处理
云端 Inbox 资料可选择云端预处理或桌面端拉取后本地处理
```

---

## 16. 质量评分设计

### 16.1 quality_score 与 value_score 区别

| 分数 | 含义 | 来源 |
|---|---|---|
| quality_score | 解析质量、文本完整性、噪音比例 | 系统规则为主 |
| value_score | 知识价值、信息密度、长期保存价值 | AI 判断为主 |

### 16.2 quality_score 规则

基础分：100。

扣分项：

| 情况 | 扣分 |
|---|---:|
| 正文少于 200 字 | -40 |
| 正文少于 500 字 | -20 |
| 出现大量乱码 | -40 |
| 重复段落比例 > 30% | -20 |
| 重复段落比例 > 50% | -40 |
| 标题缺失 | -10 |
| URL 解析失败后仅保留片段 | -30 |
| PDF 文本抽取页数不足 | -30 |
| 扫描版 PDF 无 OCR | -60 |
| 广告或导航噪音明显 | -20 |

最低为 0，最高为 100。

---

### 16.3 should_deep_process 决策

建议规则：

```text
quality_score >= 60 且 value_score >= 50
  → should_deep_process = true

quality_score < 40
  → should_deep_process = false

value_score < 30
  → should_deep_process = false
```

用户可以手动覆盖。

---

## 17. 状态机设计

### 17.1 source 状态流转

```text
queued
  ↓
fetching
  ↓
parsing
  ↓
cleaning
  ↓
document_created
  ↓
ai_processing
  ↓
done
```

失败路径：

```text
fetching / parsing / cleaning / ai_processing
  ↓
failed
  ↓
retry
  ↓
queued 或 ai_processing
```

---

### 17.2 document 状态流转

```text
parse_status: pending → processing → done / failed
clean_status: pending → processing → done / failed
ai_status: pending → processing → done / failed
chunk_status: pending，等待阶段四
index_status: pending，等待阶段四
```

---

## 18. Job Queue 设计

### 18.1 任务类型

```text
parse_source
clean_source
create_document
summarize_document
retry_failed_source
resummarize_document
```

### 18.2 本地队列

本地模式可使用 SQLite 任务表。

```sql
CREATE TABLE local_jobs (
    id              TEXT PRIMARY KEY,
    workspace_id    TEXT NOT NULL,
    job_type        TEXT NOT NULL,
    payload         TEXT NOT NULL,
    status          TEXT NOT NULL DEFAULT 'pending',
    priority        INTEGER NOT NULL DEFAULT 5,
    retry_count     INTEGER NOT NULL DEFAULT 0,
    max_retries     INTEGER NOT NULL DEFAULT 3,
    error_message   TEXT,
    scheduled_at    TEXT,
    started_at      TEXT,
    finished_at     TEXT,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL
);
```

### 18.3 云端队列

云端模式可使用：

```text
Redis + Worker Service
Hangfire
BullMQ
Quartz
```

考虑你当前项目已有 .NET 后端能力，云端建议优先：

```text
ASP.NET Core Worker Service + PostgreSQL / Redis
```

---

## 19. API 设计

## 19.1 解析 source

```http
POST /api/workspaces/{workspaceId}/sources/{sourceId}/process
```

请求：

```json
{
  "steps": ["parse", "clean", "summarize"],
  "force": false
}
```

响应：

```json
{
  "jobId": "job_001",
  "status": "queued"
}
```

---

## 19.2 获取处理状态

```http
GET /api/workspaces/{workspaceId}/sources/{sourceId}/processing-status
```

响应：

```json
{
  "sourceId": "src_001",
  "sourceStatus": "ai_processing",
  "documentId": "doc_001",
  "parseStatus": "done",
  "cleanStatus": "done",
  "aiStatus": "processing",
  "errorMessage": null
}
```

---

## 19.3 获取文档详情

```http
GET /api/workspaces/{workspaceId}/documents/{documentId}
```

响应：

```json
{
  "id": "doc_001",
  "title": "文档标题",
  "sourceType": "url",
  "sourceUrl": "https://example.com/article",
  "contentMarkdown": "# 文档标题\n\n正文……",
  "summary": "中文摘要……",
  "oneSentenceConclusion": "一句话结论……",
  "keyPoints": [],
  "recommendedTags": [],
  "qualityScore": 90,
  "valueScore": 85,
  "aiStatus": "done"
}
```

---

## 19.4 重试处理

```http
POST /api/workspaces/{workspaceId}/sources/{sourceId}/retry
```

请求：

```json
{
  "fromStep": "ai_summarize"
}
```

---

## 19.5 重新生成摘要

```http
POST /api/workspaces/{workspaceId}/documents/{documentId}/resummarize
```

请求：

```json
{
  "modelProvider": "ollama",
  "modelName": "qwen3.6:27b",
  "promptVersion": "summary_v1"
}
```

---

## 20. 前端页面设计

## 20.1 文档处理状态页

位置：

```text
Workspace → Inbox / Sources → 处理状态
```

展示字段：

1. 标题；
2. 来源类型；
3. 当前状态；
4. 解析状态；
5. 清洗状态；
6. AI 摘要状态；
7. 错误信息；
8. 重试按钮；
9. 查看日志按钮。

---

## 20.2 文档详情页

布局建议：

```text
左侧：文档正文 Markdown
右侧：AI 摘要卡片
顶部：标题 / 来源 / 标签 / 评分
底部：处理日志 / 原始资料入口
```

AI 摘要卡片包含：

```text
一句话结论
摘要
关键要点
推荐标签
价值评分
是否建议深度处理
```

---

## 20.3 错误提示设计

示例：

```text
网页无法访问：目标网站返回 403，可能禁止自动抓取。

PDF 解析失败：该文件可能是扫描版 PDF，当前版本暂未启用 OCR。

AI 摘要失败：模型返回格式不符合 JSON Schema，可点击重试。
```

---

## 21. 本地模式实现建议

### 21.1 本地处理链路

```text
Tauri Desktop
  ↓
Local Core Service
  ↓
SQLite local_jobs
  ↓
ParseWorker
  ↓
Local Vault
  ↓
Ollama / LM Studio
  ↓
SQLite documents
```

### 21.2 本地文件路径

```text
KnowledgeVault/
  workspaces/
    {workspace_id}/
      sources/
        {source_id}/
          original.pdf
          raw.html
          raw.txt
          cleaned.md
      documents/
        {document_id}/
          content.md
          summary.json
```

### 21.3 本地优先注意事项

1. 不强制联网；
2. URL 解析需要联网，但文件和文本解析不需要；
3. 用户可选择云端模型，但必须明确提示内容会发送到外部模型；
4. 本地模型不可用时，AI 摘要状态应为 `waiting_model`，而不是直接失败。

---

## 22. 云端模式实现建议

### 22.1 云端处理链路

```text
Cloud API
  ↓
PostgreSQL sources
  ↓
Redis / Worker
  ↓
S3 / MinIO 读取文件
  ↓
Parser / Cleaner
  ↓
Cloud Model Provider
  ↓
PostgreSQL documents
```

### 22.2 云端注意事项

1. Worker 必须限流；
2. 大文件必须限制大小；
3. 模型调用必须记录 token 和费用；
4. 用户 API Key 不应明文保存；
5. 不同 workspace 必须隔离；
6. 处理失败不能阻塞队列。

---

## 23. 混合模式实现建议

混合模式推荐：

```text
手机端 → 云端 Inbox → 桌面端拉取 → 本地解析和摘要
```

阶段三在混合模式下的建议：

1. 云端 Inbox 只保存原始输入；
2. 桌面端拉取后创建本地 source；
3. 文档解析和 AI 摘要默认在本地执行；
4. 用户可选择是否将摘要同步回云端；
5. 原文和文件默认不同步回云端。

---

## 24. 错误码设计

```text
FETCH_TIMEOUT
FETCH_FORBIDDEN
FETCH_NOT_FOUND
FETCH_TOO_LARGE
PARSE_EMPTY_CONTENT
PARSE_UNSUPPORTED_TYPE
PARSE_PDF_SCANNED
PARSE_PDF_TOO_LARGE
CLEAN_FAILED
AI_MODEL_UNAVAILABLE
AI_TIMEOUT
AI_INVALID_JSON
AI_CONTENT_TOO_LONG
DOCUMENT_CREATE_FAILED
UNKNOWN_ERROR
```

---

## 25. 重试策略

### 25.1 自动重试

适合自动重试：

```text
网络超时
模型超时
临时队列失败
数据库短暂异常
```

建议：

```text
max_retries = 3
retry_delay = 30s, 2min, 10min
```

### 25.2 不自动重试

不适合自动重试：

```text
网页 404
文件格式不支持
PDF 过大
扫描版 PDF 无 OCR
需要登录的网页
```

这类错误应提示用户处理。

---

## 26. 安全与隐私要求

### 26.1 本地模式

1. 原始资料默认只存在本地 Vault；
2. 本地模型处理不上传内容；
3. 使用云端模型时必须弹出提示；
4. 处理日志不得泄露 API Key；
5. 本地数据库不得保存明文云端密钥。

### 26.2 云端模式

1. 文件使用签名 URL；
2. Worker 只能访问授权 workspace；
3. 日志只保存必要摘要，不保存敏感全文；
4. 用户删除文档时，应清理 source、document、文件和后续索引；
5. 支持导出和删除工作区。

---

## 27. 性能要求

MVP 建议目标：

| 类型 | 目标 |
|---|---:|
| 纯文本解析 | 1 秒内 |
| 普通网页解析 | 3-10 秒 |
| 20 页以内 PDF 解析 | 5-20 秒 |
| AI 摘要 | 依模型而定，需异步处理 |
| 单工作区并发处理 | 本地 1-2 个，云端按队列扩展 |
| 单文档 Markdown 正文 | 建议小于 5MB |
| AI 摘要输入 | 建议小于 12k tokens |

---

## 28. 开发任务拆分

## 28.1 后端 / Local Core

### P0

1. 新增 `documents` 表；
2. 新增 `document_processing_logs` 表；
3. 新增 `local_jobs` 表，若阶段一未完成；
4. 实现 SourceProcessor 接口；
5. 实现 URLProcessor；
6. 实现 PDFProcessor 基础版；
7. 实现 TextProcessor；
8. 实现 ContentCleaner；
9. 实现 MarkdownNormalizer；
10. 实现 DocumentBuilder；
11. 实现 AISummaryService；
12. 实现 AI JSON 校验；
13. 实现处理日志；
14. 实现失败重试。

### P1

1. MarkdownProcessor；
2. PDF 扫描版检测；
3. 文档版本表；
4. 摘要重生成；
5. Prompt 版本管理；
6. 质量评分规则可配置；
7. 长文档分段摘要。

### P2

1. OCRProcessor；
2. AudioTranscribeProcessor；
3. 表格结构化抽取；
4. 网页截图归档；
5. 多模型摘要对比；
6. 文档差异对比。

---

## 28.2 前端

### P0

1. Source 处理状态列表；
2. 文档详情页；
3. AI 摘要卡片；
4. 错误提示；
5. 重试按钮；
6. 原始资料入口；
7. Markdown 阅读区域。

### P1

1. 处理日志抽屉；
2. 摘要重生成按钮；
3. 模型选择；
4. Prompt 版本显示；
5. 质量评分解释；
6. 是否进入深度处理手动开关。

---

## 28.3 测试

### 单元测试

1. URL 元数据抽取；
2. PDF 文本抽取；
3. 文本标题推断；
4. Markdown 标准化；
5. 重复段落清理；
6. AI JSON 校验；
7. quality_score 计算。

### 集成测试

1. URL → document → summary；
2. PDF → document → summary；
3. Text → document → summary；
4. 解析失败 → 重试；
5. AI 输出非法 JSON → 修复或失败；
6. 本地模型不可用 → waiting_model。

---

## 29. 验收标准

### 29.1 URL 解析验收

1. 用户导入 URL 后可以生成 document；
2. document 包含标题、来源链接、正文 Markdown；
3. 广告、导航、页脚大部分被清理；
4. 可以生成 AI 摘要；
5. 失败时有明确错误信息；
6. 可以手动重试。

### 29.2 PDF 解析验收

1. 用户导入普通 PDF 后可以生成 document；
2. PDF 文本能按页或段落进入 Markdown；
3. 扫描版 PDF 能识别为 needs_ocr 或 low_quality；
4. 可以生成 AI 摘要；
5. 超大 PDF 会提示用户，而不是拖垮系统。

### 29.3 文本解析验收

1. 用户粘贴长文本后可以生成 document；
2. 系统能自动推断标题；
3. 可以生成 Markdown；
4. 可以生成 AI 摘要；
5. 质量评分合理。

### 29.4 AI 摘要验收

1. AI 输出严格保存为结构化字段；
2. 摘要不能为空；
3. value_score 为 0-100；
4. key_points 至少 3 条，除非文档过短；
5. recommended_tags 至少 3 个，除非文档过短；
6. AI 原始输出可追踪；
7. Prompt 版本可追踪。

### 29.5 本地模式验收

1. 不登录也能解析本地文本和 PDF；
2. 本地 Vault 中能看到原始文件和 cleaned.md；
3. 本地 SQLite 中有 document 记录；
4. 本地模型可用时能生成摘要；
5. 本地模型不可用时状态可恢复。

### 29.6 混合模式验收

1. 从云端 Inbox 拉取的 source 能在本地解析；
2. 默认不把原文回传云端；
3. 用户可选择同步摘要；
4. 用户能看到处理发生在本地还是云端。

---

## 30. 关键风险与应对

### 30.1 网页解析质量不稳定

风险：不同网站结构差异大，正文抽取容易失败。

应对：

1. 使用 Readability 类库作为基础；
2. 保存 raw_html，便于后续重跑；
3. 对失败 URL 提供手动粘贴正文能力；
4. 后续增加站点规则。

---

### 30.2 PDF 复杂度高

风险：扫描版、双栏、表格、图文混排会导致解析差。

应对：

1. MVP 优先支持可复制文本 PDF；
2. 扫描版标记 needs_ocr；
3. 表格复杂时先保留纯文本；
4. 后续增加 OCR 和版面分析。

---

### 30.3 AI 摘要格式不稳定

风险：模型可能输出非 JSON 或夹带解释。

应对：

1. 严格 Prompt；
2. JSON Schema 校验；
3. 自动修复一次；
4. 失败后允许重试；
5. 保存 ai_raw_output 便于调试。

---

### 30.4 本地模型性能不稳定

风险：用户硬件差异大，本地摘要可能很慢。

应对：

1. AI 摘要异步执行；
2. 支持跳过 AI 摘要；
3. 支持本地数据 + 云端模型；
4. 支持摘要专用小模型；
5. 显示处理队列状态。

---

### 30.5 长文档摘要质量下降

风险：长文档超过模型上下文，截断会丢失信息。

应对：

1. MVP 使用输入长度限制；
2. 超长文档标记 long_document；
3. 后续引入分段摘要；
4. 阶段四完成分块后可基于 chunk 做递归摘要。

---

## 31. 推荐实现顺序

```text
第 1 步：完成 documents / logs / jobs 表
第 2 步：实现 TextProcessor，跑通最小闭环
第 3 步：实现 AI 摘要 JSON 输出与校验
第 4 步：实现 URLProcessor
第 5 步：实现 ContentCleaner + MarkdownNormalizer
第 6 步：实现 PDFProcessor 基础版
第 7 步：实现文档详情页和摘要展示
第 8 步：实现错误日志与重试
第 9 步：接入本地模型配置
第 10 步：补充质量评分和处理状态 UI
```

最小可用闭环建议先做：

```text
Text source
  ↓
TextProcessor
  ↓
MarkdownNormalizer
  ↓
DocumentBuilder
  ↓
AISummaryService
  ↓
Document Detail Page
```

这个闭环最稳定，完成后再扩展 URL 和 PDF。

---

## 32. 与后续阶段的衔接

阶段三完成后，阶段四可以直接消费：

```text
documents.content_markdown
documents.content_text
documents.summary
documents.recommended_tags
documents.quality_score
documents.value_score
```

阶段四不应再关心原始 URL、PDF、HTML 的解析细节，而只处理标准化 document。

---

## 33. 阶段三总结

阶段三是整个知识资产引擎的“资料消化系统”。

如果阶段二解决的是“把资料收进来”，那么阶段三解决的是：

```text
资料是否读得懂；
正文是否干净；
结构是否统一；
摘要是否可信；
价值是否可判断；
后续是否可检索、可问答、可报告。
```

本阶段必须优先保证：

1. 解析结果可追踪；
2. 清洗过程可重跑；
3. AI 摘要结构化；
4. 失败状态可恢复；
5. 本地和云端共用同一套处理抽象。

一句话总结：

> **阶段三的核心不是“把文件读出来”，而是把混乱资料转化为可信、干净、结构化、可长期复用的知识文档。**
