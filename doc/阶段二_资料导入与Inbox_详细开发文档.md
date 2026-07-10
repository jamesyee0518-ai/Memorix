# 阶段二：资料导入与 Inbox 详细开发文档

版本：V0.1  
所属项目：双模式 AI 知识资产引擎  
阶段定位：统一信息源入口，建立从“零散输入”到“可处理 Source”的缓冲层  
适用模式：Local Workspace / Cloud Workspace / Hybrid Workspace  
上游阶段：阶段一：双模式底座改造  
下游阶段：阶段三：文档解析、清洗与 AI 摘要  

---

## 1. 阶段二总目标

阶段二的核心目标不是立即把所有资料处理成高质量知识，而是先建立一个稳定、可追踪、可恢复、可扩展的**资料入口层**。

一句话定义：

> **阶段二负责把用户从桌面端、Web 端、手机端、文件拖拽、URL 粘贴、文本输入等渠道提交的零散信息，统一接入 Inbox，并在用户确认或规则触发后转化为 Source，交给后续处理流水线。**

阶段二完成后，系统应具备以下能力：

1. 用户可以从桌面端导入 URL、文本、PDF、文件；
2. 用户可以从手机端以聊天窗口方式采集 URL、文本、图片、录音、文件；
3. 所有输入先进入 Inbox，而不是直接污染正式知识库；
4. Inbox 可以识别输入类型、状态、来源、所属工作区、建议专题；
5. 用户可以在 Inbox 中查看、筛选、确认、批量处理、归档或删除资料；
6. Local / Cloud / Hybrid 三种工作区模式下，资料导入逻辑保持统一；
7. Hybrid 模式下，手机端资料可先进入云端 Inbox，再由桌面端拉取到本地；
8. Inbox 中的条目可以转化为 source，进入阶段三处理流水线。

---

## 2. 阶段二在整体架构中的位置

阶段二处在“采集入口”和“知识处理流水线”之间。

```text
桌面端 / Web 端 / 手机端 / 文件拖拽 / URL 粘贴 / 分享入口
        ↓
资料导入与 Inbox
        ↓
InboxItem
        ↓
用户确认 / 自动规则 / 批量导入
        ↓
Source
        ↓
阶段三：解析、清洗、AI 摘要
        ↓
Document
        ↓
阶段四：分块、向量化、索引
        ↓
检索 / 问答 / 报告 / Agent
```

### 2.1 为什么不能跳过 Inbox

如果所有资料直接进入正式知识库，会出现以下问题：

1. **低质量资料污染索引**：随手保存的链接、截图、语音片段可能价值不高；
2. **分类混乱**：资料没有确认专题，后续检索和报告容易噪音过大；
3. **失败不可恢复**：上传失败、抓取失败、解析失败无法统一重试；
4. **手机端采集体验差**：用户随时采集，但不一定希望立即处理；
5. **本地与云端同步复杂**：没有中间缓冲层，手机端和本地库直接同步风险高；
6. **无法审计来源**：资料从哪里来、何时进入、谁上传、是否处理过无法追踪。

因此，Inbox 是系统从“信息收集工具”升级为“知识资产引擎”的关键缓冲层。

---

## 3. 阶段二范围边界

### 3.1 本阶段要做

| 模块 | 是否纳入阶段二 | 说明 |
|---|---:|---|
| 桌面端手动文本导入 | 是 | 用户直接输入或粘贴文本 |
| 桌面端 URL 导入 | 是 | 保存链接与基础元数据，深度解析可进入阶段三 |
| 桌面端 PDF / 文件导入 | 是 | 文件入库、生成 file_id、创建 InboxItem |
| 文件拖拽 | 是 | 桌面端 MVP 必做 |
| Inbox 列表 | 是 | 待处理、已导入、失败、归档等视图 |
| Inbox 详情 | 是 | 查看原始输入、附件、状态、错误 |
| Inbox 转 Source | 是 | 阶段二最重要的出口 |
| 手机端聊天式采集 | 是 | 第一版只做采集，不做完整知识库管理 |
| 云端 Inbox | 是 | Hybrid 模式的手机端中转层 |
| 桌面端拉取云端 Inbox | 是 | MVP 只做云端到本地单向拉取 |
| 基础类型识别 | 是 | text / url / image / audio / file / mixed |
| 简单专题建议 | 是 | 规则优先，AI 可选 |
| 批量导入 / 批量归档 | 是 | 提升处理效率 |
| 重试机制 | 是 | 针对上传、拉取、转 source 等动作 |

### 3.2 本阶段不做

| 模块 | 暂不纳入阶段二 | 后续阶段 |
|---|---:|---|
| 深度网页正文抽取 | 不做或只做轻量 | 阶段三 |
| PDF 正文解析与清洗 | 不做或只做基础入库 | 阶段三 |
| 图片 OCR | 可预留状态，不作为 P0 | 阶段三 / P2 |
| 音频转写 | 可预留状态，不作为 P0 | 阶段三 / P2 |
| AI 长摘要 | 不做 | 阶段三 |
| Embedding | 不做 | 阶段四 |
| RAG 问答 | 不做 | 阶段五 |
| 报告生成 | 不做 | 阶段六 |
| 双向同步冲突合并 | 不做 | 阶段后期 |
| 完整移动端知识管理 App | 不做 | 后续 |
| 浏览器插件 | 不做 | P2 |
| 邮件导入 | 不做 | P2 |

### 3.3 本阶段的正确边界

阶段二只回答一个问题：

```text
资料如何稳定、可控、低成本地进入系统？
```

它不急于回答：

```text
资料如何被深度理解？
资料如何被问答？
资料如何生成报告？
```

这些问题分别交给后续阶段。

---

## 4. 用户场景

### 4.1 桌面端导入网页链接

```text
用户在桌面端粘贴一篇网页 URL
  ↓
系统创建 InboxItem，type = url
  ↓
系统自动识别 domain、suggested_title
  ↓
用户选择专题或接受系统建议
  ↓
点击“导入知识库”
  ↓
系统创建 Source，status = queued
  ↓
进入阶段三 URL 解析
```

### 4.2 桌面端拖拽 PDF

```text
用户拖拽 PDF 到桌面端
  ↓
系统保存原始文件到 Local Vault
  ↓
创建 file_object
  ↓
创建 InboxItem，type = file
  ↓
用户在 Inbox 中看到文件名、大小、状态
  ↓
用户确认导入
  ↓
创建 Source，source_type = pdf
```

### 4.3 手机端随手保存资料

```text
用户手机端打开采集窗口
  ↓
粘贴 URL 或输入想法
  ↓
资料进入云端 Inbox
  ↓
桌面端稍后启动
  ↓
桌面端拉取云端 Inbox
  ↓
写入本地 Inbox
  ↓
用户确认导入本地知识库
```

### 4.4 用户不想让手机资料自动进入知识库

```text
手机端采集资料
  ↓
进入 Inbox
  ↓
状态为 pending
  ↓
不会进入 source
  ↓
不会进入向量索引
  ↓
用户手动确认后才导入
```

### 4.5 批量整理 Inbox

```text
用户打开 Inbox
  ↓
筛选：最近 7 天、URL、未处理
  ↓
多选 20 条
  ↓
批量设置专题
  ↓
批量导入
  ↓
成功项进入 Source
  ↓
失败项保留错误信息，可重试
```

---

## 5. 工作区模式下的资料流

### 5.1 Local Workspace

本地工作区中，所有资料默认只进入本机。

```text
Desktop App
  ↓
Local Import Service
  ↓
Local Inbox
  ↓
SQLite inbox_items
  ↓
Local Vault files
  ↓
Inbox 转 Source
  ↓
SQLite sources
```

特点：

1. 不需要登录；
2. 不依赖云端；
3. 文件保存在本地 Vault；
4. URL、文本、PDF 都可以本地创建 InboxItem；
5. 手机端采集不可用，除非用户开启云端 Inbox 或局域网直连。

### 5.2 Cloud Workspace

云端工作区中，资料直接进入云端。

```text
Web / Mobile / Desktop
  ↓
Cloud API
  ↓
Cloud Inbox
  ↓
PostgreSQL inbox_items
  ↓
S3 / MinIO files
  ↓
Inbox 转 Source
  ↓
PostgreSQL sources
```

特点：

1. 需要账号；
2. 支持多设备访问；
3. 文件存储在对象存储；
4. 后续由云端 Worker 处理；
5. 适合轻量用户和团队用户。

### 5.3 Hybrid Workspace

混合工作区中，本地是主库，云端 Inbox 是采集缓冲层。

```text
Mobile Capture
  ↓
Cloud Inbox
  ↓
Desktop Sync Client
  ↓
Local Inbox
  ↓
Local Source
  ↓
Local Processing
```

特点：

1. 手机端资料先进入云端 Inbox；
2. 桌面端拉取后写入本地 Inbox；
3. 原始资料是否保留云端，由用户配置；
4. MVP 只做单向拉取，不做双向同步；
5. 云端 Inbox 明确提示用户启用后才可使用。

---

## 6. 核心领域模型

阶段二核心实体包括：

```text
inbox_item
inbox_attachment
file_object
source
import_job
inbox_event
sync_cursor
capture_session
```

关系图：

```text
workspace
  ├── inbox_item
  │     ├── inbox_attachment
  │     │      └── file_object
  │     ├── import_job
  │     └── inbox_event
  │
  └── source
        └── original_file_id -> file_object
```

### 6.1 InboxItem

InboxItem 是所有输入资料的缓冲记录。

```ts
type InboxInputType =
  | "text"
  | "url"
  | "image"
  | "audio"
  | "file"
  | "mixed";

type InboxStatus =
  | "pending"
  | "classified"
  | "ready_to_import"
  | "importing"
  | "imported"
  | "failed"
  | "archived"
  | "deleted";

interface InboxItem {
  id: string;
  workspaceId: string;
  userId?: string;
  topicId?: string;

  inputType: InboxInputType;
  title?: string;
  contentText?: string;
  sourceUrl?: string;

  status: InboxStatus;
  suggestedTopicId?: string;
  suggestedTitle?: string;
  suggestedTags?: string[];

  createdFrom: "desktop" | "web" | "mobile" | "share_extension" | "system";
  originDeviceId?: string;
  originClientVersion?: string;

  sourceId?: string;
  errorCode?: string;
  errorMessage?: string;
  retryCount: number;

  createdAt: string;
  updatedAt: string;
  importedAt?: string;
  archivedAt?: string;
}
```

### 6.2 InboxAttachment

一条 InboxItem 可以有多个附件。比如用户在手机端发送一段文字加两张截图，此时 input_type = mixed。

```ts
interface InboxAttachment {
  id: string;
  workspaceId: string;
  inboxItemId: string;
  fileId: string;

  role: "primary" | "attachment" | "thumbnail" | "transcript" | "preview";
  filename: string;
  mimeType: string;
  sizeBytes: number;

  createdAt: string;
}
```

### 6.3 FileObject

FileObject 统一描述本地文件和云端对象。

```ts
interface FileObject {
  id: string;
  workspaceId: string;

  storageProvider: "local_fs" | "s3" | "minio";
  bucket?: string;
  objectKey?: string;
  localPath?: string;

  originalFilename: string;
  mimeType: string;
  extension?: string;
  sizeBytes: number;
  sha256?: string;

  createdAt: string;
}
```

### 6.4 Source

Source 是 InboxItem 进入正式处理流水线后的对象。

```ts
interface Source {
  id: string;
  workspaceId: string;
  topicId?: string;
  inboxItemId?: string;

  sourceType: "url" | "text" | "pdf" | "image" | "audio" | "file";
  title?: string;
  url?: string;
  domain?: string;
  author?: string;
  publishedAt?: string;

  originalFileId?: string;
  rawText?: string;
  contentHash?: string;

  status:
    | "pending"
    | "queued"
    | "fetching"
    | "parsing"
    | "cleaning"
    | "document_created"
    | "ai_processing"
    | "chunking"
    | "indexing"
    | "done"
    | "failed"
    | "archived";

  errorMessage?: string;
  retryCount: number;

  createdAt: string;
  updatedAt: string;
}
```

---

## 7. 数据库设计

### 7.1 inbox_items 表

本地 SQLite 和云端 PostgreSQL 均保留此表。字段类型可按数据库差异调整，但语义保持一致。

```sql
CREATE TABLE inbox_items (
    id                    TEXT PRIMARY KEY,
    workspace_id          TEXT NOT NULL,
    user_id               TEXT,
    topic_id              TEXT,

    input_type            TEXT NOT NULL,
    title                 TEXT,
    content_text          TEXT,
    source_url            TEXT,

    status                TEXT NOT NULL DEFAULT 'pending',

    suggested_topic_id    TEXT,
    suggested_title       TEXT,
    suggested_tags        TEXT,

    created_from          TEXT NOT NULL,
    origin_device_id      TEXT,
    origin_client_version TEXT,

    source_id             TEXT,
    error_code            TEXT,
    error_message         TEXT,
    retry_count           INTEGER NOT NULL DEFAULT 0,

    created_at            TEXT NOT NULL,
    updated_at            TEXT NOT NULL,
    imported_at           TEXT,
    archived_at           TEXT
);

CREATE INDEX idx_inbox_workspace_status
ON inbox_items (workspace_id, status, created_at);

CREATE INDEX idx_inbox_workspace_type
ON inbox_items (workspace_id, input_type, created_at);

CREATE INDEX idx_inbox_topic
ON inbox_items (workspace_id, topic_id, created_at);

CREATE INDEX idx_inbox_source
ON inbox_items (source_id);
```

### 7.2 inbox_attachments 表

```sql
CREATE TABLE inbox_attachments (
    id              TEXT PRIMARY KEY,
    workspace_id    TEXT NOT NULL,
    inbox_item_id   TEXT NOT NULL,
    file_id         TEXT NOT NULL,

    role            TEXT NOT NULL DEFAULT 'primary',
    filename        TEXT NOT NULL,
    mime_type       TEXT NOT NULL,
    size_bytes      INTEGER NOT NULL,

    created_at      TEXT NOT NULL,

    FOREIGN KEY (inbox_item_id) REFERENCES inbox_items(id)
);

CREATE INDEX idx_inbox_attachments_item
ON inbox_attachments (inbox_item_id);
```

### 7.3 file_objects 表

```sql
CREATE TABLE file_objects (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,

    storage_provider    TEXT NOT NULL,
    bucket              TEXT,
    object_key          TEXT,
    local_path          TEXT,

    original_filename   TEXT NOT NULL,
    mime_type           TEXT NOT NULL,
    extension           TEXT,
    size_bytes          INTEGER NOT NULL,
    sha256              TEXT,

    created_at          TEXT NOT NULL
);

CREATE INDEX idx_file_workspace
ON file_objects (workspace_id, created_at);

CREATE INDEX idx_file_hash
ON file_objects (workspace_id, sha256);
```

### 7.4 sources 表

阶段二需要创建 Source，但不负责 Source 后续深度处理。

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

CREATE INDEX idx_sources_workspace_status
ON sources (workspace_id, status, created_at);

CREATE INDEX idx_sources_inbox_item
ON sources (inbox_item_id);

CREATE INDEX idx_sources_url
ON sources (workspace_id, url);
```

### 7.5 import_jobs 表

ImportJob 记录 Inbox 转 Source 的执行过程。

```sql
CREATE TABLE import_jobs (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    inbox_item_id       TEXT NOT NULL,
    source_id           TEXT,

    job_type            TEXT NOT NULL,
    status              TEXT NOT NULL DEFAULT 'queued',

    attempt             INTEGER NOT NULL DEFAULT 0,
    max_attempts        INTEGER NOT NULL DEFAULT 3,

    started_at          TEXT,
    finished_at         TEXT,

    error_code          TEXT,
    error_message       TEXT,

    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
);

CREATE INDEX idx_import_jobs_status
ON import_jobs (workspace_id, status, created_at);

CREATE INDEX idx_import_jobs_inbox
ON import_jobs (inbox_item_id);
```

### 7.6 inbox_events 表

InboxEvent 用于审计、调试和后续同步。

```sql
CREATE TABLE inbox_events (
    id              TEXT PRIMARY KEY,
    workspace_id    TEXT NOT NULL,
    inbox_item_id   TEXT NOT NULL,

    event_type      TEXT NOT NULL,
    event_payload   TEXT,

    created_by      TEXT,
    created_at      TEXT NOT NULL
);

CREATE INDEX idx_inbox_events_item
ON inbox_events (inbox_item_id, created_at);
```

事件类型建议：

```text
created
classified
topic_suggested
topic_changed
attachment_added
import_requested
import_started
source_created
import_failed
import_succeeded
archived
deleted
synced_from_cloud
```

### 7.7 sync_cursors 表

用于桌面端拉取云端 Inbox。

```sql
CREATE TABLE sync_cursors (
    id                  TEXT PRIMARY KEY,
    workspace_id        TEXT NOT NULL,
    remote_workspace_id TEXT NOT NULL,

    cursor_type         TEXT NOT NULL,
    cursor_value        TEXT,
    last_synced_at      TEXT,

    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
);

CREATE UNIQUE INDEX uq_sync_cursor
ON sync_cursors (workspace_id, remote_workspace_id, cursor_type);
```

---

## 8. 状态机设计

### 8.1 InboxItem 状态

```text
pending
  ↓
classified
  ↓
ready_to_import
  ↓
importing
  ↓
imported

pending / classified / ready_to_import / importing
  ↓
failed

pending / classified / ready_to_import / failed
  ↓
archived

任意非 deleted 状态
  ↓
deleted
```

### 8.2 状态说明

| 状态 | 含义 | 用户是否可见 | 是否可转 Source |
|---|---|---:|---:|
| pending | 刚进入 Inbox，未识别或未处理 | 是 | 可手动转 |
| classified | 已识别类型，可能有建议专题 | 是 | 可 |
| ready_to_import | 已满足导入条件 | 是 | 可 |
| importing | 正在转 Source | 是 | 否 |
| imported | 已生成 Source | 是 | 否 |
| failed | 处理失败 | 是 | 可重试 |
| archived | 用户归档，不再默认显示 | 是 | 可恢复 |
| deleted | 逻辑删除或物理删除前状态 | 否 | 否 |

### 8.3 Source 状态

阶段二创建 Source 后，默认进入：

```text
pending
  ↓
queued
```

如果阶段三队列已接入，则 Inbox 转 Source 后可以直接把 Source 置为 queued。

---

## 9. 输入类型识别

### 9.1 输入类型枚举

```text
text
url
image
audio
file
mixed
```

### 9.2 识别规则

#### URL

满足以下条件之一：

1. content_text 是合法 URL；
2. source_url 不为空；
3. 文本中包含单个主 URL，且用户操作为“保存链接”。

识别后：

```json
{
  "input_type": "url",
  "source_url": "https://example.com/article",
  "suggested_title": "从 URL meta 或 URL path 推断"
}
```

#### Text

满足：

1. 用户直接输入文本；
2. 不包含文件附件；
3. 不满足 URL 判断。

识别后：

```json
{
  "input_type": "text",
  "content_text": "用户输入内容"
}
```

#### File

满足：

1. 上传 PDF、Word、Markdown、TXT 等文件；
2. 有 file_object；
3. 没有额外复杂混合内容。

识别后：

```json
{
  "input_type": "file",
  "file_id": "xxx",
  "mime_type": "application/pdf"
}
```

#### Image

满足：

1. 附件 MIME type 为 image/*；
2. 没有主要文本或用户明确发送图片。

#### Audio

满足：

1. 附件 MIME type 为 audio/*；
2. 或手机端录音入口提交。

#### Mixed

满足：

1. 同时有文本和多个附件；
2. 同时包含 URL 和图片；
3. 用户一次发送多种信息源。

### 9.3 MVP 类型识别策略

P0 先采用规则识别：

```text
URL 正则
MIME type
文件扩展名
输入入口来源
```

P1 再加入 AI 分类：

```text
根据内容摘要、标题、URL domain 推荐专题和标签
```

---

## 10. 去重策略

### 10.1 为什么需要去重

用户可能多次保存同一链接、重复拖拽同一文件、手机端和桌面端重复提交同一资料。

去重可以避免：

1. 重复处理；
2. 重复占用存储；
3. 重复进入向量索引；
4. 报告中重复引用；
5. Inbox 噪音过多。

### 10.2 URL 去重

URL 去重应做标准化：

```text
去除 utm_source / utm_medium / utm_campaign
去除 fbclid / gclid 等追踪参数
去除末尾 /
域名小写
协议统一
```

建议保存：

```text
source_url_raw
source_url_normalized
```

MVP 可以先只保存 `source_url`，后续增加 `normalized_url`。

### 10.3 文件去重

通过 sha256 判断文件是否重复。

```text
同 workspace_id + sha256 相同
  ↓
提示可能重复
  ↓
允许用户仍然保留
```

### 10.4 文本去重

短文本不强制去重。长文本可通过 content_hash 判断。

```text
content_hash = sha256(normalized_content_text)
```

### 10.5 去重处理方式

MVP 建议不自动删除重复项，而是提示：

```text
检测到可能重复资料：
- 已存在于 Inbox
- 已导入知识库
- 仍然导入
- 合并到已有资料
- 取消
```

P0 可以先实现：

```text
重复 URL 提示，不阻断导入。
重复文件提示，不阻断导入。
```

---

## 11. 资料导入流程

### 11.1 创建 InboxItem 流程

```text
用户提交输入
  ↓
ImportInputValidator
  ↓
FileStorage 保存附件，若有
  ↓
TypeDetector 识别 input_type
  ↓
DuplicateChecker 检查重复
  ↓
TopicSuggestor 推荐专题，P1
  ↓
创建 inbox_items
  ↓
创建 inbox_attachments，若有
  ↓
写入 inbox_events.created
  ↓
返回 InboxItem
```

### 11.2 Inbox 转 Source 流程

```text
用户点击导入 / 规则自动导入
  ↓
创建 import_job
  ↓
InboxItem.status = importing
  ↓
根据 input_type 创建 Source
  ↓
Source.status = pending 或 queued
  ↓
InboxItem.status = imported
  ↓
InboxItem.source_id = source.id
  ↓
写入 inbox_events.import_succeeded
  ↓
阶段三接管 Source
```

### 11.3 URL 转 Source

```text
InboxItem(input_type=url)
  ↓
source_type = url
url = inbox.source_url
title = inbox.suggested_title 或用户输入标题
domain = parseDomain(url)
status = queued
```

### 11.4 Text 转 Source

```text
InboxItem(input_type=text)
  ↓
source_type = text
raw_text = inbox.content_text
title = 用户标题 / 自动截断前 50 字
content_hash = sha256(raw_text)
status = queued
```

### 11.5 PDF / File 转 Source

```text
InboxItem(input_type=file)
  ↓
读取 primary attachment
  ↓
source_type = pdf 或 file
original_file_id = file.id
title = original_filename
status = queued
```

### 11.6 Image / Audio 转 Source

阶段二先允许进入 Source，但阶段三可以根据能力决定是否处理。

```text
image:
  source_type = image
  original_file_id = file.id
  status = queued

audio:
  source_type = audio
  original_file_id = file.id
  status = queued
```

如果 OCR / ASR 尚未实现，可在阶段三置为 failed 或 waiting_capability。

---

## 12. 桌面端 Inbox 功能设计

### 12.1 Inbox 列表页

列表字段：

| 字段 | 说明 |
|---|---|
| 类型 | URL / 文本 / 文件 / 图片 / 音频 |
| 标题 | 用户标题 / 自动标题 |
| 来源 | Desktop / Mobile / Web |
| 专题 | 已选专题 / 建议专题 |
| 状态 | pending / imported / failed 等 |
| 创建时间 | 采集时间 |
| 操作 | 导入 / 编辑 / 归档 / 删除 / 重试 |

筛选项：

```text
全部
待处理
已分类
可导入
已导入
失败
已归档
按类型筛选
按专题筛选
按来源筛选
按时间筛选
```

### 12.2 Inbox 详情页

展示内容：

1. 标题；
2. 输入类型；
3. 原始文本；
4. URL；
5. 附件列表；
6. 文件大小；
7. 建议专题；
8. 用户选择专题；
9. 状态流转记录；
10. 错误信息；
11. 重试按钮；
12. 转 Source 后的 Source 链接。

### 12.3 Inbox 快捷操作

| 操作 | 说明 |
|---|---|
| 导入知识库 | 创建 Source |
| 编辑标题 | 修改 InboxItem.title |
| 设置专题 | 修改 topic_id |
| 归档 | status = archived |
| 删除 | status = deleted 或物理删除 |
| 重试 | failed 状态重新执行 |
| 批量设置专题 | 多选操作 |
| 批量导入 | 多选操作 |
| 批量归档 | 多选操作 |

### 12.4 桌面端导入入口

桌面端至少提供四个入口：

```text
1. 顶部“新建导入”按钮
2. Inbox 页面“添加资料”
3. 文件拖拽区域
4. 快捷键 / 命令面板，后续
```

建议桌面端交互：

```text
[+ 添加资料]
  ├── 粘贴 URL
  ├── 输入文本
  ├── 上传文件
  └── 从云端 Inbox 拉取
```

---

## 13. 手机端聊天式采集窗口

### 13.1 手机端定位

手机端第一版不是完整知识库 App，而是：

> **随手采集入口。**

它只做三件事：

1. 快速提交；
2. 查看是否提交成功；
3. 查看采集状态。

不做：

1. 完整知识库管理；
2. 长文阅读；
3. 高级搜索；
4. 报告编辑；
5. 图谱；
6. 复杂权限管理。

### 13.2 手机端主界面

界面结构：

```text
顶部：
  当前工作区
  云端 Inbox 状态
  设置入口

中部：
  聊天记录式采集历史

底部：
  输入框
  附件按钮
  录音按钮
  发送按钮
```

### 13.3 手机端发送文本

```text
用户输入文本
  ↓
点击发送
  ↓
POST /api/mobile/capture/text
  ↓
创建云端 InboxItem
  ↓
返回“已保存到 Inbox”
```

### 13.4 手机端发送 URL

```text
用户粘贴链接
  ↓
系统识别 URL
  ↓
显示链接卡片，P1
  ↓
发送
  ↓
创建 InboxItem(type=url)
```

### 13.5 手机端上传图片

```text
用户选择图片 / 拍照
  ↓
上传对象存储
  ↓
创建 file_object
  ↓
创建 InboxItem(type=image)
  ↓
创建 inbox_attachment
```

### 13.6 手机端上传录音

```text
用户按住录音
  ↓
本地生成音频文件
  ↓
上传对象存储
  ↓
创建 InboxItem(type=audio)
  ↓
后续阶段做 ASR
```

### 13.7 手机端上传 PDF / 文件

```text
用户选择文件
  ↓
上传对象存储
  ↓
创建 InboxItem(type=file)
  ↓
等待桌面端或云端处理
```

### 13.8 手机端状态文案

```text
已保存到 Inbox
正在上传附件
上传失败，点击重试
等待桌面端拉取
已同步到本地工作区
已导入知识库
处理失败，请在桌面端查看
```

---

## 14. 云端 Inbox 设计

### 14.1 云端 Inbox 作用

云端 Inbox 在 Hybrid 模式中是手机端和本地主库之间的中转层。

```text
Mobile
  ↓
Cloud Inbox
  ↓
Desktop Pull
  ↓
Local Inbox
```

### 14.2 云端 Inbox 不应该承担过多职责

MVP 阶段云端 Inbox 只负责：

1. 接收；
2. 存储；
3. 列表查询；
4. 状态同步；
5. 附件下载；
6. 拉取游标；
7. 基础权限校验。

不负责：

1. 深度解析；
2. 长摘要；
3. 向量化；
4. RAG；
5. 报告；
6. 复杂同步冲突合并。

### 14.3 云端 Inbox 数据保留策略

建议提供三种策略：

| 策略 | 说明 |
|---|---|
| 拉取后保留 | 默认，便于多设备查看 |
| 拉取后删除原始文件 | 保护隐私，保留元数据 |
| 拉取后全部删除 | 高隐私模式 |

MVP 可先实现：

```text
拉取后保留
```

但 UI 必须预留隐私选项。

---

## 15. 桌面端拉取云端 Inbox

### 15.1 拉取前提

用户必须完成：

1. 登录云端账号；
2. 本地 Workspace 绑定 cloud_workspace_id；
3. 显式开启“云端 Inbox 中转”；
4. 选择拉取策略；
5. 选择是否自动导入。

### 15.2 拉取流程

```text
Desktop Sync Client
  ↓
读取 sync_cursor
  ↓
GET /api/inbox/changes?cursor=xxx
  ↓
获取云端 InboxItem 列表
  ↓
逐条下载附件
  ↓
保存到 Local Vault
  ↓
创建本地 file_object
  ↓
创建本地 inbox_item
  ↓
写入 inbox_events.synced_from_cloud
  ↓
更新 sync_cursor
  ↓
回写云端状态，P1
```

### 15.3 拉取策略

| 策略 | 说明 | MVP |
|---|---|---:|
| 手动拉取 | 用户点击“同步云端 Inbox” | 必做 |
| 启动时拉取 | 桌面端启动后自动拉取 | 建议 |
| 定时拉取 | 每 N 分钟拉取一次 | P1 |
| 实时推送 | WebSocket / Push | P2 |

### 15.4 重复拉取防护

本地 InboxItem 需要记录 remote_id：

建议增加字段或 metadata：

```text
remote_inbox_item_id
remote_workspace_id
remote_created_at
```

MVP 可放在 metadata JSON 中，后续再显式字段化。

防重复逻辑：

```text
如果 remote_inbox_item_id 已存在
  ↓
跳过
否则
  ↓
创建本地 InboxItem
```

### 15.5 附件下载失败处理

如果云端 metadata 拉取成功，但附件下载失败：

```text
本地创建 InboxItem
status = failed
error_code = attachment_download_failed
error_message = 具体失败原因
```

用户可重试下载。

---

## 16. API 设计

### 16.1 桌面端本地 API

如果桌面端使用 Local Service，可提供以下本地接口。

#### 创建 InboxItem

```http
POST /local-api/workspaces/{workspaceId}/inbox
Content-Type: application/json
```

请求：

```json
{
  "inputType": "url",
  "title": "可选标题",
  "contentText": null,
  "sourceUrl": "https://example.com/article",
  "topicId": null,
  "createdFrom": "desktop"
}
```

响应：

```json
{
  "id": "inbox_01",
  "status": "classified",
  "inputType": "url",
  "suggestedTitle": "Example Article"
}
```

#### 上传文件并创建 InboxItem

```http
POST /local-api/workspaces/{workspaceId}/inbox/upload
Content-Type: multipart/form-data
```

表单字段：

```text
file
topicId
title
createdFrom
```

#### 查询 Inbox 列表

```http
GET /local-api/workspaces/{workspaceId}/inbox?status=pending&type=url&page=1&pageSize=50
```

#### 查询 Inbox 详情

```http
GET /local-api/workspaces/{workspaceId}/inbox/{inboxItemId}
```

#### 更新 InboxItem

```http
PATCH /local-api/workspaces/{workspaceId}/inbox/{inboxItemId}
```

请求：

```json
{
  "title": "新标题",
  "topicId": "topic_01",
  "status": "classified"
}
```

#### Inbox 转 Source

```http
POST /local-api/workspaces/{workspaceId}/inbox/{inboxItemId}/import
```

请求：

```json
{
  "topicId": "topic_01",
  "autoQueue": true
}
```

响应：

```json
{
  "inboxItemId": "inbox_01",
  "sourceId": "src_01",
  "sourceStatus": "queued"
}
```

#### 批量导入

```http
POST /local-api/workspaces/{workspaceId}/inbox/batch-import
```

请求：

```json
{
  "inboxItemIds": ["inbox_01", "inbox_02"],
  "topicId": "topic_01",
  "autoQueue": true
}
```

#### 归档

```http
POST /local-api/workspaces/{workspaceId}/inbox/{inboxItemId}/archive
```

#### 重试

```http
POST /local-api/workspaces/{workspaceId}/inbox/{inboxItemId}/retry
```

### 16.2 云端 API

#### 手机端创建文本采集

```http
POST /api/workspaces/{workspaceId}/inbox/text
Authorization: Bearer <token>
Content-Type: application/json
```

请求：

```json
{
  "contentText": "今天想到一个知识库产品思路...",
  "topicId": null,
  "clientId": "mobile-ios-xxx"
}
```

#### 手机端创建 URL 采集

```http
POST /api/workspaces/{workspaceId}/inbox/url
Authorization: Bearer <token>
Content-Type: application/json
```

请求：

```json
{
  "sourceUrl": "https://example.com/article",
  "title": null,
  "topicId": null
}
```

#### 手机端上传文件

```http
POST /api/workspaces/{workspaceId}/inbox/upload
Authorization: Bearer <token>
Content-Type: multipart/form-data
```

#### 云端 Inbox 列表

```http
GET /api/workspaces/{workspaceId}/inbox?status=pending&page=1&pageSize=50
Authorization: Bearer <token>
```

#### 桌面端拉取变更

```http
GET /api/workspaces/{workspaceId}/inbox/changes?cursor=xxx&limit=100
Authorization: Bearer <token>
```

响应：

```json
{
  "items": [
    {
      "id": "remote_inbox_01",
      "inputType": "url",
      "title": "文章标题",
      "sourceUrl": "https://example.com/article",
      "status": "pending",
      "createdAt": "2026-07-07T10:00:00Z",
      "attachments": []
    }
  ],
  "nextCursor": "cursor_abc",
  "hasMore": false
}
```

#### 下载附件

```http
GET /api/workspaces/{workspaceId}/files/{fileId}/download-url
Authorization: Bearer <token>
```

响应：

```json
{
  "downloadUrl": "https://signed-url",
  "expiresIn": 600
}
```

#### 回写同步状态

```http
POST /api/workspaces/{workspaceId}/inbox/{inboxItemId}/sync-status
Authorization: Bearer <token>
```

请求：

```json
{
  "status": "synced_to_local",
  "localWorkspaceId": "local_ws_01",
  "syncedAt": "2026-07-07T10:05:00Z"
}
```

---

## 17. 服务层设计

### 17.1 ImportService

负责创建 InboxItem。

```ts
interface ImportService {
  createText(input: CreateTextInboxInput): Promise<InboxItem>;
  createUrl(input: CreateUrlInboxInput): Promise<InboxItem>;
  createFile(input: CreateFileInboxInput): Promise<InboxItem>;
  createMixed(input: CreateMixedInboxInput): Promise<InboxItem>;
}
```

### 17.2 InboxService

负责 Inbox 管理。

```ts
interface InboxService {
  list(input: ListInboxInput): Promise<PagedResult<InboxItem>>;
  get(id: string): Promise<InboxItemDetail>;
  update(id: string, input: UpdateInboxInput): Promise<InboxItem>;
  archive(id: string): Promise<void>;
  delete(id: string): Promise<void>;
  retry(id: string): Promise<void>;
}
```

### 17.3 InboxImportService

负责 Inbox 转 Source。

```ts
interface InboxImportService {
  importOne(input: ImportInboxItemInput): Promise<Source>;
  importBatch(input: ImportInboxBatchInput): Promise<BatchImportResult>;
}
```

### 17.4 CloudInboxSyncService

负责云端 Inbox 到本地 Inbox 的拉取。

```ts
interface CloudInboxSyncService {
  pull(input: PullCloudInboxInput): Promise<PullCloudInboxResult>;
  downloadAttachment(input: DownloadAttachmentInput): Promise<FileObject>;
  updateCursor(input: UpdateCursorInput): Promise<void>;
}
```

### 17.5 TypeDetector

```ts
interface TypeDetector {
  detect(input: RawImportInput): Promise<DetectedInputType>;
}
```

### 17.6 TopicSuggestor

P0 可为空实现，P1 接入规则或 AI。

```ts
interface TopicSuggestor {
  suggest(input: SuggestTopicInput): Promise<SuggestTopicResult>;
}
```

---

## 18. 本地 Vault 文件结构

阶段二需要落地 Inbox 文件。

```text
KnowledgeVault/
  workspaces/
    {workspace_id}/
      inbox/
        {inbox_item_id}/
          original/
            {file_id}_{original_filename}
          preview/
            thumbnail.png
          metadata.json
      sources/
        {source_id}/
          original/
            {file_id}_{original_filename}
```

### 18.1 文件保存规则

1. 文件名必须做安全清洗；
2. 文件真实路径不暴露给 UI；
3. 数据库存 file_id，由 FileStorage 解析路径；
4. 原始文件不可随意覆盖；
5. 同一文件重复导入时可复用 file_object；
6. 删除 InboxItem 时不一定立即删除物理文件，先标记引用关系。

### 18.2 metadata.json 示例

```json
{
  "inboxItemId": "inbox_01",
  "originalFilename": "article.pdf",
  "mimeType": "application/pdf",
  "sizeBytes": 102400,
  "sha256": "xxx",
  "createdFrom": "desktop",
  "createdAt": "2026-07-07T10:00:00Z"
}
```

---

## 19. 权限与隐私设计

### 19.1 用户必须明确知道

在 Inbox 页面或设置页中，需要展示：

```text
当前工作区：本地 / 云端 / 混合
当前资料保存位置：本机 / 云端
手机采集是否启用云端 Inbox：是 / 否
原始文件是否上传云端：是 / 否
拉取后云端是否保留：是 / 否
```

### 19.2 本地模式默认隐私策略

```text
不上传原始资料
不启用云端 Inbox
不要求登录
文件只保存在本地 Vault
```

### 19.3 混合模式启用提示

启用云端 Inbox 时必须提示：

```text
启用后，手机端提交的文本、链接和文件会先上传到云端 Inbox。
桌面端可以将其拉取到本地知识库。
你可以随时关闭该功能。
```

用户需要明确确认。

### 19.4 云端文件安全

1. 文件使用对象存储私有桶；
2. 下载通过短期签名 URL；
3. API 必须校验 workspace 权限；
4. user_id / workspace_id 隔离；
5. 删除工作区时支持清理 Inbox 文件；
6. 日志中不记录完整敏感正文。

### 19.5 本地文件安全

1. 本地 Vault 路径由用户选择；
2. 文件路径不上传；
3. 可选本地加密，后续；
4. 错误日志避免输出完整原文；
5. 本地数据库备份由用户决定。

---

## 20. 错误处理与重试

### 20.1 常见错误码

```text
invalid_input
unsupported_file_type
file_too_large
file_save_failed
duplicate_detected
cloud_auth_failed
cloud_sync_failed
attachment_upload_failed
attachment_download_failed
source_create_failed
workspace_not_found
permission_denied
network_timeout
unknown_error
```

### 20.2 文件大小限制

MVP 建议：

| 类型 | 默认限制 |
|---|---:|
| 文本 | 1 MB |
| URL | 2,000 字符 |
| 图片 | 20 MB |
| 音频 | 100 MB |
| PDF | 200 MB |
| 其他文件 | 200 MB |

限制应做成配置项。

### 20.3 重试策略

| 场景 | 是否自动重试 | 建议 |
|---|---:|---|
| 网络超时 | 是 | 3 次指数退避 |
| 文件保存失败 | 否 | 提示用户 |
| 附件上传失败 | 是 | 断点续传后续做 |
| 附件下载失败 | 是 | 可手动重试 |
| Source 创建失败 | 是 | 记录 import_job |
| 权限失败 | 否 | 重新登录 |

### 20.4 失败状态展示

用户需要看到：

```text
失败原因
失败时间
是否可重试
重试按钮
查看日志，开发模式
```

---

## 21. 日志与可观测性

### 21.1 操作日志

至少记录：

```text
创建 InboxItem
上传附件
识别类型
推荐专题
用户修改专题
请求导入
创建 Source
导入失败
导入成功
云端拉取
附件下载
```

### 21.2 开发调试日志

开发环境需要可查看：

1. ImportService 输入输出；
2. TypeDetector 判断结果；
3. DuplicateChecker 结果；
4. FileStorage 保存路径；
5. Cloud Sync 请求响应；
6. ImportJob 执行结果；
7. Error stack。

### 21.3 用户侧日志

普通用户只看简化信息：

```text
已保存
等待导入
正在导入
导入成功
导入失败
已归档
```

---

## 22. 前端页面规划

### 22.1 桌面端页面

```text
/inbox
/inbox/:id
/import
/settings/cloud-inbox
```

### 22.2 Inbox 列表布局

```text
左侧：
  状态筛选
  类型筛选
  专题筛选
  来源筛选

中间：
  InboxItem 列表

右侧：
  预览面板
  操作按钮
```

### 22.3 导入弹窗

```text
标题：添加资料

Tab 1：粘贴链接
  URL 输入框
  专题选择
  保存到 Inbox / 直接导入

Tab 2：输入文本
  标题
  文本框
  专题选择
  保存到 Inbox / 直接导入

Tab 3：上传文件
  拖拽区域
  专题选择
  保存到 Inbox / 直接导入
```

### 22.4 手机端页面

```text
/mobile/capture
/mobile/history
/mobile/settings
```

手机端 MVP 不建议超过三个主要页面。

---

## 23. 自动导入规则

### 23.1 为什么需要自动导入

对于高频用户，每条资料都手动确认会降低效率。

但自动导入不能默认开启，否则会污染知识库。

### 23.2 MVP 策略

默认：

```text
所有资料进入 Inbox，等待用户确认。
```

可选设置：

```text
可信来源自动导入
指定专题自动导入
桌面端导入时直接导入
手机端只保存到 Inbox
```

### 23.3 自动导入规则表，P1

```sql
CREATE TABLE import_rules (
    id              TEXT PRIMARY KEY,
    workspace_id    TEXT NOT NULL,

    name            TEXT NOT NULL,
    enabled         INTEGER NOT NULL DEFAULT 1,

    match_type      TEXT NOT NULL,
    match_value     TEXT NOT NULL,

    target_topic_id TEXT,
    auto_import     INTEGER NOT NULL DEFAULT 0,

    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL
);
```

示例：

```text
domain = arxiv.org → 专题：AI 论文 → 自动导入
domain = github.com → 专题：开源项目 → 仅建议专题
type = audio → 不自动导入
```

---

## 24. Topic 推荐策略

### 24.1 P0：无 AI 推荐

P0 可以只做：

```text
用户手动选择专题
保留上次选择专题
按 domain 简单匹配专题
```

### 24.2 P1：规则推荐

规则来源：

1. URL domain；
2. 文件名关键词；
3. 文本关键词；
4. 最近常用专题；
5. 用户历史选择。

### 24.3 P2：AI 推荐

AI Prompt 输入：

```json
{
  "title": "输入标题",
  "url": "https://example.com/article",
  "contentPreview": "前 1000 字",
  "availableTopics": [
    {"id": "topic_ai", "name": "AI 产品"},
    {"id": "topic_finance", "name": "财务系统"}
  ]
}
```

输出：

```json
{
  "suggestedTopicId": "topic_ai",
  "confidence": 0.82,
  "reason": "内容主要讨论 AI Agent 产品设计",
  "suggestedTags": ["AI Agent", "产品设计"]
}
```

注意：

1. AI 推荐不能自动替用户最终决定；
2. 用户可关闭；
3. 本地模式下默认使用本地模型或不使用；
4. 不能因为 AI 推荐失败影响资料入 Inbox。

---

## 25. 阶段二与阶段三的接口约定

阶段二输出 Source，阶段三消费 Source。

### 25.1 阶段二必须保证

创建 Source 时：

1. workspace_id 必填；
2. source_type 必填；
3. inbox_item_id 可追溯；
4. URL 类型必须有 url；
5. Text 类型必须有 raw_text；
6. File/PDF/Image/Audio 类型必须有 original_file_id；
7. status 必须为 pending 或 queued；
8. error_message 初始为空；
9. retry_count 初始为 0；
10. created_at / updated_at 完整。

### 25.2 阶段三不能假设

阶段三不能假设：

1. URL 一定可访问；
2. PDF 一定可解析；
3. 图片一定有 OCR；
4. 音频一定能转写；
5. topic_id 一定存在；
6. title 一定准确；
7. 文件一定不是重复；
8. raw_text 一定高质量。

### 25.3 Source 创建事件

创建 Source 后，应写入事件：

```json
{
  "eventType": "source_created",
  "payload": {
    "sourceId": "src_01",
    "sourceType": "url",
    "autoQueue": true
  }
}
```

---

## 26. 开发任务拆分

### 26.1 后端 / 本地服务任务

#### P0

1. 创建 inbox_items 表；
2. 创建 inbox_attachments 表；
3. 创建 file_objects 表；
4. 创建 import_jobs 表；
5. 创建 inbox_events 表；
6. 实现 FileStorage 本地保存；
7. 实现 createTextInboxItem；
8. 实现 createUrlInboxItem；
9. 实现 createFileInboxItem；
10. 实现 listInboxItems；
11. 实现 getInboxItemDetail；
12. 实现 updateInboxItem；
13. 实现 archiveInboxItem；
14. 实现 deleteInboxItem；
15. 实现 importInboxItemToSource；
16. 实现 batchImportInboxItems；
17. 实现 TypeDetector；
18. 实现 DuplicateChecker 基础版；
19. 实现 ImportJob 记录；
20. 对接阶段三 Source 队列。

#### P1

1. 云端 Inbox API；
2. 手机端 Capture API；
3. 桌面端拉取云端 Inbox；
4. sync_cursor；
5. signed URL 下载；
6. 云端拉取后状态回写；
7. TopicSuggestor 规则版；
8. 自动导入规则；
9. 批量重试；
10. 用户隐私配置项。

### 26.2 桌面端任务

#### P0

1. Inbox 列表页；
2. Inbox 详情页；
3. 添加资料弹窗；
4. URL 输入；
5. 文本输入；
6. 文件拖拽上传；
7. 设置专题；
8. 手动导入；
9. 批量导入；
10. 归档 / 删除；
11. 错误状态展示。

#### P1

1. 云端 Inbox 绑定设置页；
2. 手动拉取云端 Inbox；
3. 启动时自动拉取；
4. 隐私提示弹窗；
5. 重复资料提示；
6. 导入进度展示；
7. 最近采集来源统计。

### 26.3 手机端任务

#### P1

1. 登录；
2. 选择工作区；
3. 聊天式采集页面；
4. 文本发送；
5. URL 发送；
6. 图片上传；
7. 文件上传；
8. 录音上传；
9. 采集历史；
10. 上传失败重试；
11. 状态展示。

P0 如果以桌面端闭环为主，手机端可以放在 P1；但如果产品差异化强调手机采集，应尽早开发最小版。

### 26.4 云端任务

#### P1

1. Cloud Inbox 数据表；
2. Cloud FileStorage；
3. Mobile Capture API；
4. Cloud Inbox List API；
5. Changes API；
6. Download URL API；
7. Sync Status API；
8. 权限校验；
9. 文件大小限制；
10. 清理策略预留。

---

## 27. 推荐开发顺序

### 27.1 第一小步：桌面端本地 Inbox 闭环

```text
SQLite 表
  ↓
本地 FileStorage
  ↓
创建 InboxItem
  ↓
Inbox 列表
  ↓
Inbox 转 Source
```

目标：

```text
桌面端可以导入 URL / 文本 / PDF，并进入 Source。
```

### 27.2 第二小步：Inbox 管理能力

```text
详情页
  ↓
编辑标题
  ↓
设置专题
  ↓
批量导入
  ↓
归档 / 删除
  ↓
失败重试
```

目标：

```text
Inbox 能作为真正可用的收件箱，而不是简单数据库表。
```

### 27.3 第三小步：云端 Inbox

```text
Cloud Inbox API
  ↓
Cloud FileStorage
  ↓
手机端文本 / URL 采集
  ↓
云端 Inbox 列表
```

目标：

```text
手机可以把资料送到云端 Inbox。
```

### 27.4 第四小步：Hybrid 拉取

```text
本地绑定云端工作区
  ↓
拉取 changes
  ↓
下载附件
  ↓
写入本地 Inbox
  ↓
更新 cursor
```

目标：

```text
手机端采集资料可以进入本地 Inbox。
```

---

## 28. 验收标准

### 28.1 桌面端本地 Inbox 验收

| 编号 | 验收项 | 通过标准 |
|---|---|---|
| A1 | 创建文本 Inbox | 输入文本后生成 InboxItem |
| A2 | 创建 URL Inbox | 粘贴 URL 后识别为 url |
| A3 | 上传 PDF | 文件保存到 Vault，并生成 file_object |
| A4 | Inbox 列表 | 可按状态、类型查看 |
| A5 | Inbox 详情 | 可查看原始输入和附件 |
| A6 | 设置专题 | 可为 InboxItem 指定 topic |
| A7 | 导入 Source | InboxItem 成功创建 Source |
| A8 | 批量导入 | 多条 InboxItem 可批量转 Source |
| A9 | 失败重试 | failed 状态可重试 |
| A10 | 归档 | archived 后默认列表不显示 |

### 28.2 云端 Inbox 验收

| 编号 | 验收项 | 通过标准 |
|---|---|---|
| B1 | 手机文本采集 | 云端生成 InboxItem |
| B2 | 手机 URL 采集 | 云端生成 url 类型 InboxItem |
| B3 | 手机文件上传 | 对象存储生成 file_object |
| B4 | 权限隔离 | 用户只能访问自己的 workspace |
| B5 | 云端列表 | 可查询云端 Inbox |
| B6 | Changes API | 可按 cursor 增量拉取 |
| B7 | 下载附件 | 可获取短期 signed URL |
| B8 | 状态回写 | 可标记 synced_to_local |

### 28.3 Hybrid 拉取验收

| 编号 | 验收项 | 通过标准 |
|---|---|---|
| C1 | 绑定云端 | 本地 Workspace 能保存 cloud_workspace_id |
| C2 | 手动拉取 | 点击后拉取云端 Inbox |
| C3 | 附件落地 | 云端附件下载到 Local Vault |
| C4 | 防重复 | 同一 remote_inbox_item 不重复创建 |
| C5 | cursor 更新 | 下次只拉取新内容 |
| C6 | 拉取失败 | 失败项有错误信息 |
| C7 | 隐私提示 | 启用前有明确提示 |
| C8 | 拉取后导入 | 本地 InboxItem 可转 Source |

### 28.4 数据一致性验收

1. 每个 InboxItem 必须有 workspace_id；
2. 每个附件必须关联 file_object；
3. 每个 imported InboxItem 必须有 source_id；
4. 每个 Source 如果来自 Inbox，必须有 inbox_item_id；
5. 删除 InboxItem 不应破坏已创建 Source；
6. 文件缺失时页面应展示错误，而不是崩溃；
7. 批量导入部分失败时，成功项和失败项状态必须准确。

---

## 29. 测试用例

### 29.1 单元测试

1. TypeDetector 识别 URL；
2. TypeDetector 识别普通文本；
3. TypeDetector 识别 PDF；
4. FileStorage 保存文件；
5. DuplicateChecker 检测重复 URL；
6. DuplicateChecker 检测重复文件 hash；
7. InboxImportService 创建 URL Source；
8. InboxImportService 创建 Text Source；
9. InboxImportService 创建 File Source；
10. CloudInboxSyncService cursor 更新。

### 29.2 集成测试

1. 文本导入完整链路；
2. URL 导入完整链路；
3. PDF 上传完整链路；
4. Inbox 批量导入；
5. 导入失败回滚；
6. 云端 Inbox 拉取；
7. 附件下载失败；
8. 重复 remote item 跳过；
9. 权限隔离；
10. 拉取中断后恢复。

### 29.3 UI 测试

1. Inbox 空状态；
2. Inbox 列表加载；
3. 筛选状态；
4. 添加 URL；
5. 添加文本；
6. 拖拽文件；
7. 详情页编辑标题；
8. 设置专题；
9. 批量导入；
10. 错误提示。

---

## 30. 安全与边界测试

### 30.1 文件安全

测试：

1. 上传超大文件；
2. 上传无扩展名文件；
3. 上传伪装 MIME type；
4. 文件名包含路径穿越字符；
5. 文件名包含特殊字符；
6. 重复文件；
7. 文件保存中断；
8. 本地 Vault 不可写。

### 30.2 URL 安全

测试：

1. 非法 URL；
2. localhost URL；
3. file:// URL；
4. javascript: URL；
5. 超长 URL；
6. 带大量追踪参数 URL；
7. 重复 URL；
8. 需要登录的网站 URL。

MVP 阶段只保存 URL，不深度抓取，可以降低 SSRF 风险。阶段三做 URL fetching 时必须增加更严格的网络安全策略。

### 30.3 权限安全

测试：

1. 用户 A 访问用户 B 的 Inbox；
2. 未登录访问云端 Inbox；
3. 过期 token；
4. workspace_id 越权；
5. signed URL 过期；
6. 删除后仍访问文件。

---

## 31. 配置项

### 31.1 Workspace 级配置

```json
{
  "inbox": {
    "enabled": true,
    "autoImport": false,
    "defaultTopicId": null,
    "cloudInboxEnabled": false,
    "pullCloudInboxOnStartup": false,
    "cloudRetentionPolicy": "keep_after_pull",
    "maxFileSizeMb": 200,
    "allowedFileTypes": [
      "pdf",
      "txt",
      "md",
      "docx",
      "png",
      "jpg",
      "jpeg",
      "mp3",
      "m4a",
      "wav"
    ]
  }
}
```

### 31.2 客户端配置

```json
{
  "desktop": {
    "dragDropEnabled": true,
    "showDuplicateWarning": true,
    "defaultImportMode": "save_to_inbox"
  },
  "mobile": {
    "captureHistoryDays": 30,
    "uploadOnWifiOnly": false,
    "audioMaxDurationSeconds": 600
  }
}
```

---

## 32. 最小可交付版本定义

### 32.1 阶段二 P0 最小闭环

```text
桌面端：
  创建 InboxItem
  URL / 文本 / PDF 导入
  Inbox 列表
  Inbox 详情
  Inbox 转 Source
  批量导入
  失败重试
```

P0 成功标志：

```text
即使没有手机端，用户已经可以用桌面端把资料稳定放入系统，并转入 Source。
```

### 32.2 阶段二 P1 增强闭环

```text
手机端：
  文本 / URL / 图片 / 文件采集
  云端 Inbox
  桌面端拉取
  本地 Inbox 入库
```

P1 成功标志：

```text
用户可以在手机端随手采集资料，回到桌面端后将其导入本地知识库。
```

### 32.3 阶段二不应拖延到 P2 的能力

以下能力如果缺失，会影响后续所有阶段：

1. InboxItem 与 Source 的追溯关系；
2. 文件对象统一管理；
3. 状态机；
4. 错误重试；
5. 批量导入；
6. workspace_id 强隔离；
7. 本地与云端字段一致。

---

## 33. 关键风险与应对

### 33.1 风险：Inbox 变成垃圾箱

问题：

```text
用户大量保存，但不整理，Inbox 越积越多。
```

应对：

1. 默认显示待处理数量；
2. 支持批量导入和批量归档；
3. 支持自动归档低价值资料，后续；
4. 支持提醒用户定期清理；
5. 支持按来源和时间筛选。

### 33.2 风险：手机端云端 Inbox 引发隐私顾虑

问题：

```text
本地优先用户可能不愿把资料上传云端。
```

应对：

1. 默认关闭云端 Inbox；
2. 启用时明确提示；
3. 支持只上传 URL，不上传文件，后续；
4. 支持拉取后删除云端原始文件，后续；
5. 支持局域网直连，P2。

### 33.3 风险：导入和解析边界混乱

问题：

```text
阶段二做太多解析，会拖慢整体进度。
```

应对：

1. 阶段二只负责接入和转 Source；
2. 深度解析放到阶段三；
3. OCR / ASR 不作为 P0；
4. URL fetching 只做轻量 metadata，正文抽取放阶段三。

### 33.4 风险：本地和云端模型不一致

问题：

```text
Local 和 Cloud 两套 Inbox 数据结构不同，后期同步困难。
```

应对：

1. 统一领域模型；
2. 统一状态枚举；
3. 统一 Source 创建规则；
4. 字段可以不同，但语义不能不同。

### 33.5 风险：文件管理失控

问题：

```text
Inbox 删除、Source 导入、附件复用之间关系复杂。
```

应对：

1. file_object 独立管理；
2. InboxAttachment 只保存引用；
3. Source original_file_id 也保存引用；
4. 不立即物理删除文件；
5. 后续做引用计数和垃圾回收。

---

## 34. 阶段二完成后的系统能力

阶段二完成后，系统应具备以下基础能力：

```text
用户能把资料放进来；
系统知道资料从哪里来；
系统知道资料是什么类型；
系统知道资料当前处于什么状态；
用户能决定哪些资料进入知识库；
系统能把资料转成 Source；
后续阶段能稳定消费 Source；
手机端能作为采集入口接入系统；
本地模式和云端模式不会割裂。
```

---

## 35. 阶段二与后续阶段的衔接

### 35.1 对阶段三的支撑

阶段三可以直接从 Source 表读取待处理资料：

```sql
SELECT *
FROM sources
WHERE workspace_id = ?
  AND status = 'queued'
ORDER BY created_at ASC;
```

### 35.2 对阶段四的支撑

阶段二不直接产生 chunk，但必须保证 Source 到 Document 的来源链路完整。

```text
InboxItem
  ↓
Source
  ↓
Document
  ↓
DocumentChunk
```

### 35.3 对阶段五的支撑

RAG 问答中，引用可以追溯到：

```text
chunk → document → source → inbox_item → created_from
```

这能让用户知道答案来自：

```text
手机采集的某篇文章
桌面拖拽的某个 PDF
手动输入的一段笔记
```

### 35.4 对阶段六的支撑

报告生成可以按资料来源过滤：

```text
最近 7 天从手机端采集的 AI 资料
某专题中由 PDF 导入的资料
某 domain 来源的 URL 资料
```

---

## 36. 推荐实现目录结构

### 36.1 本地 / 桌面端

```text
apps/desktop/
  src/
    pages/
      inbox/
        InboxListPage.tsx
        InboxDetailPage.tsx
        ImportDialog.tsx
    components/
      inbox/
        InboxItemCard.tsx
        InboxFilterPanel.tsx
        InboxPreviewPanel.tsx
        FileDropZone.tsx
    services/
      inboxApi.ts
      importApi.ts

packages/core/
  inbox/
    InboxService.ts
    ImportService.ts
    InboxImportService.ts
    TypeDetector.ts
    DuplicateChecker.ts
    TopicSuggestor.ts
  storage/
    FileStorage.ts
    LocalFileStorage.ts
  repositories/
    KnowledgeRepository.ts
    LocalKnowledgeRepository.ts
```

### 36.2 云端

```text
apps/api/
  Controllers/
    InboxController.cs
    MobileCaptureController.cs
    FilesController.cs
  Services/
    InboxService.cs
    CloudFileStorage.cs
    InboxChangesService.cs
  Models/
    InboxItem.cs
    InboxAttachment.cs
    FileObject.cs
  Workers/
    InboxImportWorker.cs
```

### 36.3 手机端

```text
apps/mobile/
  src/
    screens/
      CaptureScreen.tsx
      CaptureHistoryScreen.tsx
      SettingsScreen.tsx
    components/
      ChatInputBar.tsx
      CaptureMessageBubble.tsx
      AttachmentPicker.tsx
    services/
      captureApi.ts
      uploadApi.ts
```

---

## 37. 里程碑计划

### Milestone 2.1：本地 Inbox 数据层

交付：

1. 数据表迁移；
2. Repository 方法；
3. FileStorage；
4. 单元测试。

完成标准：

```text
可以通过 API 创建、查询、更新 InboxItem。
```

### Milestone 2.2：桌面端导入体验

交付：

1. 添加资料弹窗；
2. URL / 文本 / 文件导入；
3. Inbox 列表；
4. Inbox 详情。

完成标准：

```text
用户可以通过 UI 把资料保存到 Inbox。
```

### Milestone 2.3：Inbox 转 Source

交付：

1. InboxImportService；
2. import_jobs；
3. Source 创建；
4. 批量导入；
5. 失败重试。

完成标准：

```text
用户可以把 Inbox 中的资料转入阶段三队列。
```

### Milestone 2.4：云端 Inbox

交付：

1. Cloud Inbox API；
2. Mobile Capture API；
3. Cloud FileStorage；
4. 权限校验。

完成标准：

```text
手机端或 Web 端可以把资料提交到云端 Inbox。
```

### Milestone 2.5：Hybrid 拉取

交付：

1. 桌面端绑定云端 Workspace；
2. 手动拉取；
3. 附件下载；
4. 本地 Inbox 写入；
5. cursor 更新。

完成标准：

```text
手机端采集资料能进入本地工作区。
```

---

## 38. 阶段二最终验收清单

```text
[ ] 本地工作区可以创建 InboxItem
[ ] 云端工作区可以创建 InboxItem
[ ] Hybrid 工作区可以拉取云端 Inbox
[ ] 文本导入可用
[ ] URL 导入可用
[ ] PDF / 文件导入可用
[ ] 图片 / 音频可进入 Inbox
[ ] 文件可保存到 Local Vault 或对象存储
[ ] Inbox 列表可筛选
[ ] Inbox 详情可查看
[ ] InboxItem 可设置专题
[ ] InboxItem 可归档 / 删除
[ ] InboxItem 可导入为 Source
[ ] 批量导入可用
[ ] 导入失败可重试
[ ] Source 能追溯到 InboxItem
[ ] 云端 Inbox 启用前有隐私提示
[ ] 手机端采集文本可用
[ ] 手机端采集 URL 可用
[ ] 手机端上传文件可用
[ ] 桌面端可拉取云端 Inbox
[ ] 重复拉取不会重复创建
[ ] 错误信息可见
[ ] 操作事件有记录
[ ] 单元测试覆盖核心服务
[ ] 集成测试覆盖导入链路
```

---

## 39. 阶段二结论

阶段二的设计重点不是“智能”，而是“入口稳定、状态清晰、流转可控”。

正确的阶段二应该做到：

```text
先收进来；
先不污染正式知识库；
让用户可控地决定是否导入；
让系统可追踪地转成 Source；
为后续解析、摘要、检索、问答、报告和 Agent 调用建立可靠入口。
```

最终交付形态：

```text
桌面端 Inbox 是资料整理台；
手机端 Capture 是随手采集入口；
云端 Inbox 是混合模式中转站；
Source 是进入知识处理流水线的正式起点。
```

一句话总结：

> **阶段二是双模式知识引擎的“资料闸口”：所有信息先进入 Inbox，经确认、分类、去重和状态管理后，再转化为 Source，进入后续 AI 处理流水线。**
