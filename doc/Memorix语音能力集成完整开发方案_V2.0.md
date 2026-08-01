# Memorix 语音能力集成完整开发方案 V2.0

> 文档版本：V2.0  
> 编制日期：2026-08-01  
> 适用范围：Memorix 本地模式、BYOK 模式、Memorix 云端模式及混合模式  
> 核心架构：Capability Contract + Provider Adapter + Policy Router  
> 参考实现：FunASR、faster-whisper、whisper.cpp、Fish-Speech、GLM-ASR 等  
> 设计原则：系统绑定能力协议，不绑定具体模型或供应商。

---

# 1. 项目背景

Memorix 的核心定位是本地优先、可扩展、可混合部署的知识资产系统。其基础链路包括：

```text
信息源接入
  → 内容解析
  → 清洗与结构化
  → 摘要、实体、标签和关联
  → 全文及向量索引
  → 检索、问答、阅读和导出
```

语音能力接入后，需要支持以下场景：

1. 用户通过手机、桌面端或浏览器录音采集灵感、会议、访谈和备忘。
2. 用户上传音频或视频，自动生成带时间戳的转录文本。
3. 转录内容经过纠错、结构化、摘要、实体识别和标签生成后进入知识库。
4. 用户可从文本跳转到原始音频对应位置。
5. 用户可将知识文章转换为语音。
6. 用户可通过语音提问，并获得文本或语音回答。
7. 用户可选择本地模型、BYOK 第三方云模型或 Memorix 平台托管能力。
8. 隐私敏感资料可强制只在本地设备或局域网节点处理。
9. 移动端离线时仍可完成录音、草稿识别和任务暂存。

本方案不将语音能力设计成独立业务中心，而是把它作为 Memorix 的内容输入类型、处理工具和输出方式。

---

# 2. 核心架构决策

## 2.1 系统绑定能力，不绑定模型

Memorix 不应写死：

```text
中文 ASR = FunASR
多语言 ASR = faster-whisper
离线 ASR = whisper.cpp
TTS = Fish-Speech
```

正确设计为：

```text
Memorix Audio Capability
        ↓
统一能力协议 Capability Contract
        ↓
策略路由 Policy Router
        ↓
Provider Adapter
        ↓
本地模型 / BYOK 云服务 / Memorix 云服务
```

FunASR、faster-whisper、whisper.cpp、Fish-Speech 和 GLM-ASR 等，都只是某类能力的 Provider 实现。

## 2.2 三种运行模式

### 本地模式

```text
LOCAL_DEVICE
LOCAL_LAN_NODE
```

特点：

- 文件不离开设备或局域网。
- 不需要第三方云端凭证。
- 适合 STRICT_LOCAL。
- 可使用 whisper.cpp、FunASR Local、faster-whisper Local、Fish-Speech Local、Piper 或系统 TTS。

### BYOK 模式

```text
USER_BYOK
TENANT_BYOK
```

特点：

- 用户或租户提供 API Key。
- Memorix 负责调度、结果归一化和知识入库。
- 调用成本由用户或租户承担。
- 可接入 GLM-ASR、Azure Speech、阿里云、腾讯云或其他兼容 Provider。

### Memorix 云端模式

```text
PLATFORM_MANAGED
```

特点：

- Memorix 平台提供模型能力和凭证。
- 可调用第三方云服务，也可调用 Memorix 自建推理集群。
- 平台负责计量、配额、结算和服务治理。

---

# 3. 四层解耦模型

必须将以下概念分开：

## 3.1 Execution Mode

```text
LOCAL_DEVICE
LOCAL_LAN_NODE
MEMORIX_CLOUD
THIRD_PARTY_CLOUD
```

表示能力实际运行位置。

## 3.2 Credential Mode

```text
NO_CREDENTIAL
USER_BYOK
TENANT_BYOK
PLATFORM_MANAGED
```

表示凭证由谁提供。

## 3.3 Provider

示例：

```text
FUNASR
FASTER_WHISPER
WHISPER_CPP
ZHIPU_GLM
FISH_SPEECH
PIPER
SYSTEM_TTS
```

## 3.4 Model

示例：

```text
glm-asr-2512
whisper-large-v3
paraformer-zh
whisper-base-q5_1
fish-speech-x
```

示例配置：

```json
{
  "capability": "audio.transcription",
  "execution_mode": "THIRD_PARTY_CLOUD",
  "credential_mode": "TENANT_BYOK",
  "provider": "ZHIPU_GLM",
  "model": "glm-asr-2512"
}
```

本地配置：

```json
{
  "capability": "audio.transcription",
  "execution_mode": "LOCAL_DEVICE",
  "credential_mode": "NO_CREDENTIAL",
  "provider": "WHISPER_CPP",
  "model": "whisper-base-q5_1"
}
```

---

# 4. 建设目标

## P0：采集、转录与入库

- 支持浏览器、桌面端和移动端录音。
- 支持上传常见音频和视频格式。
- 支持本地、BYOK 和平台托管三种 Provider 路由。
- 支持 VAD、标点恢复、时间戳、说话人识别和热词。
- 支持转录校对、版本保留和一键入库。
- 支持原音频与文本片段双向定位。
- 支持严格本地模式。

## P1：文章朗读与多语言输出

- 支持 Fish-Speech、Piper、系统 TTS 及云端 TTS Provider。
- 支持全文、章节、选中文本和问答答案朗读。
- 支持流式输出、缓存、续播和降级。
- 支持翻译后朗读。

## P2：语音问答与会议提取

- 支持语音提问。
- 支持会议摘要、议题、结论、待办、决策和风险提取。
- 支持说话人映射和人工命名。
- 支持从结构化结果回溯转录片段和原始录音。

## P3：完整本地与企业治理

- 支持本地模型、局域网节点、私有部署和平台云混合路由。
- 支持企业级策略、配额、审计、计费和 Provider 治理。

---

# 5. 总体架构

```text
┌──────────────────────────────────────────────────────────────┐
│                       Memorix Clients                        │
│ Web / Desktop / iOS / Android                               │
│ 录音、上传、离线队列、转录编辑、播放、朗读、语音问答          │
└──────────────────────────────┬───────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                     Memorix API Gateway                      │
│ 鉴权、租户、限流、幂等、追踪、文件签名、协议转换              │
└──────────────────────────────┬───────────────────────────────┘
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                 Audio Capability Orchestrator                │
│ Capability Registry / Policy Router / Cost / Health / SLA    │
└───────────────┬──────────────────────────────┬───────────────┘
                │                              │
                ▼                              ▼
┌──────────────────────────┐       ┌──────────────────────────┐
│ Local Tool Gateway       │       │ Cloud Provider Gateway   │
│ 本地设备 / 局域网节点      │       │ BYOK / Platform Managed  │
└─────────────┬────────────┘       └─────────────┬────────────┘
              │                                  │
       ┌──────┴──────────┐                ┌──────┴──────────┐
       ▼                 ▼                ▼                 ▼
┌─────────────┐   ┌─────────────┐  ┌─────────────┐   ┌─────────────┐
│ ASR Adapter │   │ TTS Adapter │  │ ASR Adapter │   │ TTS Adapter │
│ whisper.cpp │   │ Fish/Piper  │  │ GLM/其他云端 │   │ 云端 TTS    │
│ FunASR      │   │ System TTS  │  │             │   │             │
└──────┬──────┘   └──────┬──────┘  └──────┬──────┘   └──────┬──────┘
       └─────────────────┴────────────────┴───────────────────┘
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                   Media Preparation Layer                    │
│ FFmpeg / VAD / Physical Segment / Audio Cache / Normalize    │
└──────────────────────────────┬───────────────────────────────┘
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                   DAG Content Pipeline                       │
│ ASR → Correction → Entity / Summary / Todo / Embedding       │
└──────────────────────────────┬───────────────────────────────┘
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                 Memorix Knowledge Storage                    │
│ 对象存储、元数据、版本、分段、向量、全文索引、知识图谱          │
└──────────────────────────────────────────────────────────────┘
```

---

# 6. Capability Contract

## 6.1 ASR Provider

```typescript
interface AsrProvider {
  getDescriptor(): Promise<AsrProviderDescriptor>;

  validateRequest(
    request: AsrTranscriptionRequest
  ): Promise<ValidationResult>;

  transcribe(
    request: AsrTranscriptionRequest
  ): Promise<AsrTranscriptionResult>;

  transcribeStream?(
    request: AsrStreamingRequest
  ): AsyncIterable<AsrPartialResult>;

  estimateCost?(
    request: AsrTranscriptionRequest
  ): Promise<CostEstimate>;

  cancel?(providerTaskId: string): Promise<void>;

  healthCheck(): Promise<ProviderHealth>;
}
```

```typescript
interface AsrProviderDescriptor {
  providerId: string;
  modelId: string;

  executionModes: ExecutionMode[];
  credentialModes: CredentialMode[];

  supportedLanguages: string[];

  supportsStreaming: boolean;
  supportsBatch: boolean;
  supportsVad: boolean;
  supportsPunctuation: boolean;
  supportsDiarization: boolean;
  supportsHotwords: boolean;
  supportsWordTimestamp: boolean;
  supportsSegmentTimestamp: boolean;

  maxFileBytes?: number;
  maxAudioDurationMs?: number;
  acceptedMimeTypes: string[];

  sendsAudioOffDevice: boolean;
  storesProviderData: "UNKNOWN" | "NO" | "TEMPORARY" | "YES";

  pricingUnit?: "REQUEST" | "SECOND" | "MINUTE" | "TOKEN";
}
```

## 6.2 TTS Provider

```typescript
interface TtsProvider {
  getDescriptor(): Promise<TtsProviderDescriptor>;

  validateRequest(
    request: TtsRequest
  ): Promise<ValidationResult>;

  synthesize(
    request: TtsRequest
  ): Promise<TtsResult>;

  synthesizeStream?(
    request: TtsStreamRequest
  ): AsyncIterable<AudioChunk>;

  listVoices(): Promise<VoiceProfile[]>;

  estimateCost?(request: TtsRequest): Promise<CostEstimate>;

  healthCheck(): Promise<ProviderHealth>;
}
```

---

# 7. Provider 路由策略

路由顺序必须为：

```text
1. 检查数据隐私等级
2. 检查允许的执行位置
3. 检查凭证模式和凭证可用性
4. 检查 Provider 能力和文件限制
5. 检查语言和场景适配
6. 检查健康状态、延迟和成本
7. 检查用户显式指定
8. 执行自动路由或降级
```

示例：

```text
STRICT_LOCAL
→ 仅允许 whisper.cpp / Local FunASR / Local TTS

PRIVATE + TENANT_BYOK
→ 优先使用租户配置的 GLM-ASR

普通资料 + 无用户凭证
→ 使用 Memorix 平台托管 Provider

长会议 + 云端 Provider 单文件限制
→ VAD 切片后调用
或选择支持长音频的 Provider

多人会议 + 要求说话人识别
→ ASR Provider + 独立 Diarization Provider
```

用户显式指定优先级高于自动路由，但仍不得突破安全策略。

---

# 8. GLM-ASR 等云端 Provider 接入方式

云端 ASR 统一通过 Adapter 接入：

```text
Audio Orchestrator
  → Policy Router
  → Cloud Provider Gateway
  → ZhipuGlmAsrAdapter
```

GLM-ASR 不应直接出现在业务代码中。

示例 Descriptor：

```json
{
  "providerId": "zhipu",
  "modelId": "glm-asr-2512",
  "executionModes": ["THIRD_PARTY_CLOUD"],
  "credentialModes": [
    "USER_BYOK",
    "TENANT_BYOK",
    "PLATFORM_MANAGED"
  ],
  "supportsStreaming": true,
  "supportsBatch": true,
  "supportsVad": false,
  "supportsDiarization": false,
  "supportsHotwords": true,
  "sendsAudioOffDevice": true
}
```

具体文件大小、时长、支持格式、热词和流式参数必须由 Provider Descriptor 配置，不得写死在业务层。

---

# 9. Media Preparation Layer

所有长音频、会议和访谈先经过统一媒体预处理：

```text
原始音频
  → 文件校验
  → SHA-256 去重
  → FFmpeg 标准化
  → Audio Cache
  → VAD
  → Physical Segment
  → Provider 能力限制检查
  → ASR
```

## 9.1 Audio Cache

缓存键：

```text
source_sha256
+ sample_rate
+ channels
+ normalize_version
```

避免重复执行 FFmpeg 转码和标准化。

## 9.2 VAD 统一时间轴

VAD 的物理切片是所有后续能力的时间基线：

```text
VAD
→ Physical Segment
→ ASR
→ Speaker
→ Correction
→ Summary / Entity / Todo
```

禁止简单将不同 Provider 返回的时间戳直接拼接为最终时间轴。

---

# 10. Segment UUID

每个物理片段必须拥有稳定的 `segment_uuid`。

```json
{
  "segment_uuid": "seg_01H...",
  "source_start_ms": 32000,
  "source_end_ms": 58700,
  "provider": "zhipu",
  "model": "glm-asr-2512",
  "text": "...",
  "status": "COMPLETED"
}
```

以下对象全部引用 `segment_uuid`：

- Speaker
- Summary
- Entity
- Todo
- Quote
- Knowledge Graph
- Citation
- Meeting Decision
- Risk
- Open Question

禁止使用 `start_ms/end_ms` 作为业务关系主键。

---

# 11. ASR 与 Diarization 解耦

语音能力拆为独立 Capability：

```text
audio.vad
audio.transcription
audio.diarization
audio.punctuation
audio.correction
audio.synthesis
```

单个任务可组合不同 Provider：

```text
VAD：FunASR Local
ASR：GLM-ASR Cloud
Diarization：CAM++ Local
Correction：Memorix Local LLM
Summary：Tenant BYOK LLM
```

这使本地、BYOK 和平台托管能力可以混合使用。

---

# 12. Post-ASR Correction

识别结果不能直接进入 LLM 摘要。

标准流程：

```text
ASR
→ Normalization
→ Post-ASR Correction
→ Entity Normalization
→ LLM
```

纠错内容：

- 品牌词
- 人名
- 产品名
- 专业术语
- 缩写
- 同音字
- 中英混合词
- 用户词典
- 租户词典
- 当前知识库上下文

所有纠错都必须生成新版本，禁止覆盖原始模型输出。

---

# 13. 转录版本与合并

首期不引入 CRDT。

版本树：

```text
RAW_MODEL
→ POST_PROCESSED
→ SERVER_RETRANSCRIBED
→ USER_EDITED
→ MERGED
→ PUBLISHED
```

采用三方合并：

```text
Base Version
Local User Edit
Server Retranscription
```

规则：

- 未编辑段可自动替换。
- 用户已编辑段默认保留用户文本。
- 同一 `segment_uuid` 出现冲突时进入差异合并。
- 用户编辑永远不能被自动识别结果静默覆盖。

---

# 14. 移动端策略

## 14.1 Device Capability Detector

检测：

- CPU
- RAM
- Storage
- Thermal
- Battery
- 系统后台能力

推荐模型：

```text
tiny
base
small
```

移动端仅允许经过验证的量化模型，例如 Q4_0、Q5_1。

## 14.2 双模式识别

### 实时低精度

```text
边录边转
```

仅用于短语音或会议实时字幕。

### 录完批处理

```text
录音完成
→ 后台分段
→ 批量识别
```

作为默认模式，以降低发热、耗电和 OOM 风险。

## 14.3 降级策略

设备能力不足：

```text
仅录音
→ 保存本地任务
→ 联网后 BYOK/平台云转录
或等待局域网节点处理
```

---

# 15. TTS 性能与自动降级

## 15.1 Sentence Chunking

禁止全文一次性生成。

```text
Document
→ Sentence Splitter
→ 20~80 字 Chunk
→ TTS Provider
→ Audio Chunk
→ Player
```

## 15.2 Pipeline Streaming

```text
Chunk 1 生成
→ 立即播放

Chunk 2 生成中
Chunk 3 排队
```

## 15.3 自动降级

触发条件：

- GPU 利用率超过阈值
- 队列长度超过阈值
- TTFB 超过阈值
- Provider 不可用
- 用户设备资源不足

示例降级：

```text
Fish-Speech
→ Piper
→ System TTS
→ Cloud TTS（仅用户允许）
```

未经用户授权，不得从本地自动切到第三方云端。

---

# 16. BYOK 凭证管理

## 16.1 provider_credential

```text
provider_credential
├── id
├── tenant_id
├── owner_type
├── owner_id
├── provider_id
├── credential_type
├── encrypted_secret
├── key_version
├── status
├── last_verified_at
├── expires_at
└── created_at
```

要求：

- 加密存储。
- 与租户隔离。
- 不写普通日志。
- 不返回前端。
- 支持测试连接。
- 支持轮换、禁用、删除和过期。
- 任务执行时临时解密。
- 支持用户和租户级凭证。

## 16.2 BYOK 失败策略

可能失败：

```text
401 / 403
余额不足
限流
模型无权限
地域不可用
服务下线
```

用户必须预先选择：

```text
失败后停止
失败后切换本地
失败后切换平台云端并计费
```

系统不得未经授权自动使用平台付费账号。

---

# 17. Model Registry

统一管理：

- Provider
- Model
- Version
- SHA-256
- License
- GPU
- RAM
- Languages
- Capability
- Download URL
- 最大文件限制
- 最大时长限制
- 支持格式
- 定价单位
- 数据保留策略
- 区域可用性
- 健康状态

模型升级不应要求修改业务代码。

---

# 18. Benchmark

建立统一评测体系：

- CER
- WER
- RTF
- GPU
- Memory
- TTFB
- Throughput
- 专有名词准确率
- 时间戳偏差
- 说话人识别准确率
- 用户修改率
- 单位成本

自动生成：

```text
最快
最准
最低成本
最适合中文
最适合移动端
最适合多人会议
```

路由器可引用 Benchmark 结果，但不能越过安全与隐私策略。

---

# 19. Prompt Registry

会议摘要、访谈提取、问答总结等 Prompt 必须版本化：

- Prompt ID
- Version
- Owner
- Publish Time
- Rollback
- Evaluation Set
- A/B Test
- Provider Compatibility

禁止将 Prompt 写死在业务代码中。

---

# 20. DAG 内容流水线

```text
Audio
→ VAD
→ ASR
→ Correction
├→ Embedding
├→ Summary
├→ Entity
├→ Todo
├→ Translation
├→ Diarization Merge
└→ Knowledge Graph
```

节点支持：

- 输入输出 Schema
- 幂等键
- 重试
- 超时
- 并行
- 版本
- Provider 指定
- 本地/云端约束
- 成本估算
- 数据分类策略

未来 OCR、图片、视频和字幕处理可以直接挂载。

---

# 21. 核心数据模型

## audio_asset

保留原始文件、标准化文件和隐私策略。

## transcription_job

新增：

```text
execution_mode
credential_mode
provider_id
model_id
fallback_policy
estimated_cost
actual_cost
```

## transcription_segment

必须包含：

```text
segment_uuid
source_start_ms
source_end_ms
provider_id
model_id
confidence
speaker_key
text
version
```

## provider_usage_record

```text
provider_usage_record
├── id
├── tenant_id
├── user_id
├── capability
├── provider_id
├── model_id
├── credential_mode
├── execution_mode
├── duration_ms
├── request_count
├── input_units
├── output_units
├── estimated_cost
├── actual_cost
├── status
└── created_at
```

---

# 22. API 与实时通信

## 对外

- REST：任务创建、查询、管理、发布。
- WebSocket：流式 ASR、流式 TTS、进度和事件。

## 对内

- HTTP 或 gRPC 均可。
- 规模较大时，GPU 推理服务优先 gRPC。
- 不应在首期强制所有内部服务改为 gRPC。

---

# 23. 安全与隐私

数据分级：

```text
PUBLIC
INTERNAL
PRIVATE
STRICT_LOCAL
```

STRICT_LOCAL：

- 禁止上传原音。
- 禁止调用云端 Provider。
- 禁止自动同步转录。
- 禁止第三方 TTS。
- 只能调用本地设备或已授权局域网节点。

所有 Provider 必须声明：

```text
sends_audio_off_device
stores_provider_data
data_region
retention_policy
```

---

# 24. 声音克隆治理

声音克隆默认关闭。

启用要求：

- 用户显式授权。
- Proof of Voice。
- 随机文本朗读验证。
- 记录授权来源。
- 团队管理员许可。
- 合成音频植入水印或可审计元数据。
- 支持音色删除和使用记录。
- 禁止从共享会议中直接提取他人音色。

---

# 25. 页面与交互

## Provider 设置

用户可配置：

```text
默认模式：
- 本地优先
- BYOK 优先
- 平台云优先
- 自动

ASR Provider：
- 自动
- 本地 FunASR
- 本地 whisper.cpp
- GLM-ASR BYOK
- 平台托管 ASR

失败策略：
- 停止
- 本地降级
- 平台降级并计费
```

## 上传时显式选择

- 仅保存音频。
- 本地转录。
- BYOK 转录。
- 平台云转录。
- 自动选择。
- 开启说话人识别。
- 添加热词。
- 转录后自动摘要。
- 原音是否允许离开设备。

---

# 26. 实施计划

## P0

- Capability Contract
- Provider Adapter SDK
- Policy Router
- FunASR / faster-whisper / whisper.cpp Adapter
- GLM-ASR BYOK Adapter
- Segment UUID
- Media Preparation Layer
- Audio Cache
- BYOK Credential Manager
- 转录版本与发布

## P1

- Post-ASR Correction
- Fish-Speech Streaming
- Piper/System TTS 降级
- Device Capability Detector
- Provider Usage Metering
- Cost Estimator

## P2

- Model Registry
- Benchmark
- Prompt Registry
- 平台托管计费
- 企业策略中心

## P3

- DAG Pipeline
- 局域网算力节点
- 内部 gRPC
- Provider Marketplace
- 多云容灾与自动切换

---

# 27. 验收标准

1. 本地模式不产生任何云端传输。
2. USER_BYOK、TENANT_BYOK 和 PLATFORM_MANAGED 可独立工作。
3. 同一 ASR 任务可切换不同 Provider，而不改变业务数据结构。
4. Provider 限制通过 Descriptor 动态生效。
5. GLM-ASR 等云模型可通过 Adapter 接入。
6. 长音频可经 VAD 切片后调用受限 Provider。
7. 用户编辑不会被二次识别结果覆盖。
8. 云端降级必须经过用户授权。
9. 所有费用和调用量可追踪。
10. 所有摘要、结论和待办可回溯到 `segment_uuid`。
11. Provider 下线或更换不影响知识库核心数据。
12. 严格本地策略具有自动化测试和审计证据。

---

# 28. 最终技术选型定位

以下不再是固定依赖，而是官方参考实现：

```text
本地中文 ASR：
FunASR Provider

本地/私有多语言 ASR：
faster-whisper Provider

移动端/桌面端离线 ASR：
whisper.cpp Provider

第三方云 ASR：
GLM-ASR Provider
其他兼容 Provider

本地高质量 TTS：
Fish-Speech Provider

轻量级本地 TTS：
Piper / System TTS

第三方云 TTS：
GLM-TTS 或其他兼容 Provider
```

Memorix 核心只依赖：

```text
Capability Contract
Policy Router
Provider Adapter
Credential Manager
Model Registry
Usage Metering
DAG Pipeline
```

---

# 29. 最终结论

Memorix 的本地、BYOK 和云端模式不会与当前语音能力开发冲突。

真正需要避免的是：

```text
业务代码直接绑定 FunASR、Whisper、Fish-Speech 或 GLM
```

最终架构必须是：

```text
Memorix
→ Capability Contract
→ Policy Router
→ Provider Adapter
   ├── Local Provider
   ├── BYOK Provider
   └── Platform Cloud Provider
```

这样既能保持本地优先与隐私控制，也能接入 GLM-ASR 等云端能力，并为后续更换模型、增加供应商、进行成本路由和多云容灾保留完整空间。
