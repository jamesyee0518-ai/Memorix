# Memorix 语音能力集成开发计划 V2.0

> 基于《Memorix语音能力集成完整开发方案 V2.0》与当前项目代码现状制定。  
> 核心原则：系统绑定能力协议（Capability Contract），不绑定具体模型或供应商。  
> FunASR、faster-whisper、whisper.cpp、Fish-Speech、GLM-ASR 等仅为官方预置 Provider 实现。

---

## 一、现状与差距分析

### 1.1 当前语音能力

项目当前仅有一个基础的音频转写实现，位于 `MediaProcessingService.cs` 中，通过 `Process.Start()` 调用本地 `whisper` CLI，输出纯文本，无分段、无时间戳、无 VAD、无说话人分离。录音采集在 Mobile 端（expo-av）和 Web 端（MediaRecorder）均已实现，录音文件通过 `MobileCaptureController` 的 upload 端点上传至 Inbox。

计费系统中已定义 `AUDIO_SECOND` 用量常量（`BillingConstants.cs`），但未在 `appsettings.json` 中配置费率。

### 1.2 差距清单

| V2.0 要求 | 当前状态 | 差距 |
|----------|---------|------|
| Capability Contract（能力协议） | 无 | 无 `AsrProvider`/`TtsProvider` 接口和 Descriptor 声明体系 |
| Provider Adapter SDK | 无 | 无适配器注册和发现机制，Whisper 调用硬编码在 MediaProcessingService |
| Policy Router（策略路由） | RuntimeRouter 仅路由 LLM/Embedding | 不感知 Execution Mode、Credential Mode、数据隐私等级 |
| 四层解耦（Execution/Credential/Provider/Model） | 无 | 无 ExecutionMode、CredentialMode 枚举和组合配置 |
| BYOK 凭证管理 | 无 | 无 provider_credential 表，无加密存储和验证机制 |
| Segment UUID 与统一时间轴 | DocumentChunk 仅有文本偏移量 | 无音频时间戳字段，无 transcription_segment 实体 |
| Media Preparation Layer | 无 | 无 VAD、Audio Cache、FFmpeg 标准化流水线 |
| TTS（Provider 化） | 完全缺失 | 无 `TtsProvider` 接口 |
| Post-ASR Correction | 完全缺失 | 无纠错模块 |
| 移动端设备能力检测 | 完全缺失 | 无 Device Capability Detector |
| 转录版本与合并 | 完全缺失 | 无版本树 |
| 安全与隐私分级 | SensitivityLevel 字段存在 | 无 STRICT_LOCAL 强制执行，无 Provider 数据声明 |
| Model Registry | 完全缺失 | 无模型注册中心 |
| Benchmark | 完全缺失 | 无评测体系 |
| Prompt Registry | 部分存在 | 有 `ISummaryPromptManager` 但无版本化/A/B Test |
| DAG Pipeline | 线性流水线 | `DocumentPipeline` 为线性处理，无 DAG |
| Provider Usage Metering | 有基础计费框架 | 无 provider_usage_record，无音频用量计量 |
| WebSocket 流式 | 仅 REST | 无 WebSocket 支持 |
| Docker 语音模型容器 | 无 | docker-compose.yml 仅含 postgres/redis/minio |

### 1.3 可复用的现有基础设施

- **RuntimeRouter**（`RuntimeRouter.cs`）：已有本地/云端模型路由机制，但其职责范围需扩展至音频能力路由，或由新建的 Policy Router 替代
- **UnifiedModelProvider**：已封装 LLM + Embedding 统一调用，其设计模式可参考但不应直接复用（音频能力需要更丰富的 Descriptor 和路由策略）
- **MediaProcessingWorker**：已有后台轮询 Worker 处理 audio/image 类型 InboxItem，可在其基础上接入 Policy Router
- **IKnowledgeRepository**：约 60 个方法的统一仓储抽象，本地/云端双模式，可扩展 Segment 相关操作
- **DocumentPipeline**：已有完整的文档处理流水线，可在其中插入语音处理节点
- **IAppDbContext**：约 50 个 DbSet，EF Core 双数据库（SQLite/PostgreSQL）模式
- **BillingConstants.AudioSecond**：已定义音频计费常量
- **Document.SensitivityLevel**：已有 `public/normal/private/sensitive/restricted` 字段，可映射至 V2.0 数据分级
- **CredentialStore / ICredentialStore**：已有凭证存储抽象（PlatformCredentialStore），可扩展支持 BYOK 凭证
- **WorkspaceBinding / CloudAccountBinding**：已有工作区与云账号绑定体系，可扩展支持 Provider 凭证绑定

---

## 二、核心架构设计

### 2.1 架构定位

Memorix 核心只依赖以下稳定组件，绝不绑定具体模型或供应商：

```
Memorix Audio Capability
        ↓
统一能力协议 Capability Contract
        ↓
策略路由 Policy Router
        ↓
Provider Adapter
   ├── Local Provider（whisper.cpp / FunASR Local / Piper）
   ├── BYOK Provider（GLM-ASR / Azure / 阿里云 / 腾讯云）
   └── Platform Cloud Provider（Memorix 托管）
```

FunASR、faster-whisper、whisper.cpp、Fish-Speech、GLM-ASR 等都只是某类能力的 Provider 实现。Provider 升级、替换或新增不应要求修改业务代码。

### 2.2 总体架构

```
┌──────────────────────────────────────────────────────────────┐
│                       Memorix Clients                        │
│ Web / Desktop / iOS / Android                               │
│ 录音、上传、离线队列、转录编辑、播放、朗读、语音问答          │
└──────────────────────────────┬───────────────────────────────┘
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
└─────────────┘   └─────────────┘  └─────────────┘   └─────────────┘
              │                                  │
              └────────────────┬─────────────────┘
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

### 2.3 四层解耦模型

必须将以下四个概念彻底分开，业务代码不依赖任何单一维度：

| 层级 | 枚举值 | 含义 |
|------|--------|------|
| Execution Mode | `LOCAL_DEVICE` `LOCAL_LAN_NODE` `MEMORIX_CLOUD` `THIRD_PARTY_CLOUD` | 能力实际运行位置 |
| Credential Mode | `NO_CREDENTIAL` `USER_BYOK` `TENANT_BYOK` `PLATFORM_MANAGED` | 凭证由谁提供 |
| Provider | `FUNASR` `FASTER_WHISPER` `WHISPER_CPP` `ZHIPU_GLM` `FISH_SPEECH` `PIPER` `SYSTEM_TTS` | 能力的具体实现者 |
| Model | `glm-asr-2512` `whisper-large-v3` `paraformer-zh` `whisper-base-q5_1` | Provider 下的具体模型 |

一个任务通过这四个维度的组合确定执行方式，例如：

```json
{
  "capability": "audio.transcription",
  "execution_mode": "THIRD_PARTY_CLOUD",
  "credential_mode": "TENANT_BYOK",
  "provider": "ZHIPU_GLM",
  "model": "glm-asr-2512"
}
```

### 2.4 核心设计原则

1. **系统绑定能力，不绑定模型** — 业务代码只依赖 Capability Contract，FunASR/Whisper/Fish-Speech/GLM 等仅为 Provider 实现，可替换、可新增
2. **Segment 为一等公民** — 所有音频处理产物（转写、摘要、实体、待办）统一引用 `segment_uuid`，禁止依赖 `start_ms/end_ms` 建立业务关系
3. **隐私优先** — STRICT_LOCAL 模式禁止任何云端传输，所有 Provider 必须声明 `sends_audio_off_device` 和 `stores_provider_data`
4. **策略路由** — 路由顺序固定：隐私等级 → 执行位置 → 凭证可用性 → Provider 能力 → 语言适配 → 健康成本 → 用户指定
5. **云端降级需授权** — 未经用户授权，不得从本地自动切换到第三方云端
6. **双数据库兼容** — 所有新增实体同时支持 SQLite（桌面端）和 PostgreSQL（云端），JSONB 字段在 SQLite 中使用 TEXT + JSON 序列化

---

## 三、Capability Contract

Capability Contract 是整个语音能力体系最核心的稳定层。业务代码只依赖 Contract，不依赖任何 Provider 实现。

### 3.1 ASR Provider Contract

在 `src/KnowledgeEngine.Application/Interfaces/` 下新增：

```csharp
// IAsrProvider.cs
public interface IAsrProvider
{
    Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct);
    Task<ValidationResult> ValidateRequestAsync(AsrTranscriptionRequest request, CancellationToken ct);
    Task<AsrTranscriptionResult> TranscribeAsync(AsrTranscriptionRequest request, CancellationToken ct);
    IAsyncEnumerable<AsrPartialResult>? TranscribeStream(AsrStreamingRequest request, CancellationToken ct);
    Task<CostEstimate>? EstimateCostAsync(AsrTranscriptionRequest request, CancellationToken ct);
    Task CancelAsync(string providerTaskId, CancellationToken ct);
    Task<ProviderHealth> HealthCheckAsync(CancellationToken ct);
}
```

```csharp
// AsrProviderDescriptor.cs — 声明 Provider 的能力边界
public class AsrProviderDescriptor
{
    public string ProviderId { get; set; }
    public string ModelId { get; set; }
    public List<ExecutionMode> ExecutionModes { get; set; }
    public List<CredentialMode> CredentialModes { get; set; }
    public List<string> SupportedLanguages { get; set; }

    public bool SupportsStreaming { get; set; }
    public bool SupportsBatch { get; set; }
    public bool SupportsVad { get; set; }
    public bool SupportsPunctuation { get; set; }
    public bool SupportsDiarization { get; set; }
    public bool SupportsHotwords { get; set; }
    public bool SupportsWordTimestamp { get; set; }
    public bool SupportsSegmentTimestamp { get; set; }

    public long? MaxFileBytes { get; set; }
    public long? MaxAudioDurationMs { get; set; }
    public List<string> AcceptedMimeTypes { get; set; }

    // 隐私声明
    public bool SendsAudioOffDevice { get; set; }
    public ProviderDataRetention StoresProviderData { get; set; }  // UNKNOWN/NO/TEMPORARY/YES

    public string? PricingUnit { get; set; }  // REQUEST/SECOND/MINUTE/TOKEN
}
```

### 3.2 TTS Provider Contract

```csharp
// ITtsProvider.cs
public interface ITtsProvider
{
    Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken ct);
    Task<ValidationResult> ValidateRequestAsync(TtsRequest request, CancellationToken ct);
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct);
    IAsyncEnumerable<AudioChunk>? SynthesizeStream(TtsStreamRequest request, CancellationToken ct);
    Task<List<VoiceProfile>> ListVoicesAsync(CancellationToken ct);
    Task<CostEstimate>? EstimateCostAsync(TtsRequest request, CancellationToken ct);
    Task<ProviderHealth> HealthCheckAsync(CancellationToken ct);
}
```

### 3.3 Capability 枚举定义

在 `src/KnowledgeEngine.Domain/Enums/` 下新增：

```csharp
public enum ExecutionMode
{
    LOCAL_DEVICE,
    LOCAL_LAN_NODE,
    MEMORIX_CLOUD,
    THIRD_PARTY_CLOUD
}

public enum CredentialMode
{
    NO_CREDENTIAL,
    USER_BYOK,
    TENANT_BYOK,
    PLATFORM_MANAGED
}

public enum ProviderDataRetention
{
    UNKNOWN,
    NO,
    TEMPORARY,
    YES
}

public enum DataClassification
{
    PUBLIC,
    INTERNAL,
    PRIVATE,
    STRICT_LOCAL
}
```

### 3.4 音频能力拆分

语音能力拆为独立 Capability，单个任务可组合不同 Provider：

| Capability 标识 | 说明 | 可选 Provider 示例 |
|----------------|------|-------------------|
| `audio.vad` | 语音活动检测 | FunASR Local, Silero |
| `audio.transcription` | 语音转文字 | whisper.cpp, FunASR, faster-whisper, GLM-ASR |
| `audio.diarization` | 说话人分离 | CAM++, pyannote |
| `audio.punctuation` | 标点恢复 | FunASR CT-Transformer |
| `audio.correction` | ASR 后纠错 | Memorix Local LLM |
| `audio.synthesis` | 文字转语音 | Fish-Speech, Piper, System TTS, GLM-TTS |

一个会议转录任务的 Provider 组合示例：

```
VAD：FunASR Local
ASR：GLM-ASR Cloud (TENANT_BYOK)
Diarization：CAM++ Local
Correction：Memorix Local LLM
Summary：Tenant BYOK LLM
```

---

## 四、Provider Adapter SDK 与预置实现

### 4.1 Provider 注册与发现

新增 `IProviderRegistry` 接口，位于 `src/KnowledgeEngine.Application/Interfaces/`：

```csharp
public interface IProviderRegistry
{
    Task RegisterAsync(IAsrProvider provider, CancellationToken ct);
    Task RegisterAsync(ITtsProvider provider, CancellationToken ct);
    Task<List<IAsrProvider>> GetAsrProvidersAsync(CancellationToken ct);
    Task<List<ITtsProvider>> GetTtsProvidersAsync(CancellationToken ct);
    Task<List<IAsrProvider>> FindAsrProvidersAsync(ProviderFilter filter, CancellationToken ct);
    Task<List<ITtsProvider>> FindTtsProvidersAsync(ProviderFilter filter, CancellationToken ct);
}
```

Provider 在 DI 注册时自动注册到 `ProviderRegistry`，运行时通过 `FindAsrProvidersAsync` 按条件筛选。

### 4.2 官方预置 Provider 实现

以下 Provider 作为官方预置实现，位于 `src/KnowledgeEngine.Infrastructure/Audio/Providers/`，但它们不是系统架构的组成部分，可以替换、禁用或新增：

| Provider | Capability | ExecutionMode | CredentialMode | 定位 |
|----------|-----------|---------------|----------------|------|
| WhisperCppAsrProvider | `audio.transcription` | LOCAL_DEVICE | NO_CREDENTIAL | 移动端/桌面端离线 ASR |
| FunAsrAsrProvider | `audio.transcription` | LOCAL_DEVICE / LOCAL_LAN_NODE | NO_CREDENTIAL | 本地中文 ASR |
| FasterWhisperAsrProvider | `audio.transcription` | LOCAL_DEVICE / LOCAL_LAN_NODE | NO_CREDENTIAL | 本地/私有多语言 ASR |
| ZhipuGlmAsrProvider | `audio.transcription` | THIRD_PARTY_CLOUD | USER_BYOK / TENANT_BYOK / PLATFORM_MANAGED | 第三方云 ASR |
| FunAsrVadProvider | `audio.vad` | LOCAL_DEVICE | NO_CREDENTIAL | 本地 VAD |
| FunAsrPunctuationProvider | `audio.punctuation` | LOCAL_DEVICE | NO_CREDENTIAL | 本地标点恢复 |
| FishSpeechTtsProvider | `audio.synthesis` | LOCAL_DEVICE / LOCAL_LAN_NODE | NO_CREDENTIAL | 本地高质量 TTS |
| PiperTtsProvider | `audio.synthesis` | LOCAL_DEVICE | NO_CREDENTIAL | 轻量级本地 TTS |
| SystemTtsProvider | `audio.synthesis` | LOCAL_DEVICE | NO_CREDENTIAL | 系统 TTS 降级 |
| CloudTtsProvider | `audio.synthesis` | THIRD_PARTY_CLOUD / MEMORIX_CLOUD | BYOK / PLATFORM_MANAGED | 云端 TTS |

### 4.3 WhisperCpp Provider 示例实现

将现有 `MediaProcessingService.cs` 中的 `RunTranscriptionAsync` 逻辑提取为 `WhisperCppAsrProvider`：

```csharp
public class WhisperCppAsrProvider : IAsrProvider
{
    public async Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        return new AsrProviderDescriptor
        {
            ProviderId = "whisper_cpp",
            ModelId = Environment.GetEnvironmentVariable("MEMORIX_WHISPER_MODEL") ?? "base",
            ExecutionModes = new() { ExecutionMode.LOCAL_DEVICE },
            CredentialModes = new() { CredentialMode.NO_CREDENTIAL },
            SupportedLanguages = new() { "zh", "en", "ja", "ko", "fr", "de" },
            SupportsStreaming = false,
            SupportsBatch = true,
            SupportsVad = true,
            SupportsPunctuation = false,
            SupportsDiarization = false,
            SupportsHotwords = false,
            SupportsWordTimestamp = true,
            SupportsSegmentTimestamp = true,
            SendsAudioOffDevice = false,
            StoresProviderData = ProviderDataRetention.NO,
            PricingUnit = "REQUEST"
        };
    }
    // ... 其他方法实现
}
```

### 4.4 GLM-ASR 云端 Provider 示例实现

```csharp
public class ZhipuGlmAsrProvider : IAsrProvider
{
    public async Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        return new AsrProviderDescriptor
        {
            ProviderId = "zhipu",
            ModelId = "glm-asr-2512",
            ExecutionModes = new() { ExecutionMode.THIRD_PARTY_CLOUD },
            CredentialModes = new()
            {
                CredentialMode.USER_BYOK,
                CredentialMode.TENANT_BYOK,
                CredentialMode.PLATFORM_MANAGED
            },
            SupportsStreaming = true,
            SupportsBatch = true,
            SupportsVad = false,
            SupportsDiarization = false,
            SupportsHotwords = true,
            SendsAudioOffDevice = true,
            StoresProviderData = ProviderDataRetention.TEMPORARY,
            PricingUnit = "SECOND"
        };
    }
    // ... 通过 HTTP API 调用智谱 GLM-ASR
}
```

---

## 五、Policy Router

Policy Router 是能力调度的核心决策器，替代原方案中简单的 AsrRouter/TtsRouter。

### 5.1 路由策略

路由顺序必须严格为：

```
1. 检查数据隐私等级（STRICT_LOCAL → 仅本地 Provider）
2. 检查允许的执行位置（LOCAL_DEVICE / LOCAL_LAN_NODE / CLOUD）
3. 检查凭证模式和凭证可用性（BYOK → 检查 credential 有效性）
4. 检查 Provider 能力和文件限制（MaxFileBytes, MaxAudioDurationMs, AcceptedMimeTypes）
5. 检查语言和场景适配（SupportedLanguages, SupportsDiarization 等）
6. 检查健康状态、延迟和成本（HealthCheck, CostEstimate）
7. 检查用户显式指定（用户选择了某个 Provider/Model）
8. 执行自动路由或降级
```

用户显式指定的优先级高于自动路由，但仍不得突破安全策略（步骤 1-3）。

### 5.2 路由示例

| 场景 | 隐私等级 | 执行位置 | 凭证模式 | 路由结果 |
|------|---------|---------|---------|---------|
| 隐私敏感资料 | STRICT_LOCAL | LOCAL_DEVICE | NO_CREDENTIAL | whisper.cpp / FunASR Local / Local TTS |
| 企业私有 + 租户凭证 | PRIVATE | THIRD_PARTY_CLOUD | TENANT_BYOK | 租户配置的 GLM-ASR |
| 普通资料 + 无用户凭证 | INTERNAL | MEMORIX_CLOUD | PLATFORM_MANAGED | Memorix 平台托管 Provider |
| 长会议 + 云端限制 | PRIVATE | THIRD_PARTY_CLOUD | TENANT_BYOK | VAD 切片后调用或选择支持长音频的 Provider |
| 多人会议 + 说话人识别 | INTERNAL | LOCAL_LAN_NODE | NO_CREDENTIAL | FunASR ASR + CAM++ Diarization |

### 5.3 实现位置

新增 `AudioPolicyRouter`，位于 `src/KnowledgeEngine.Infrastructure/Audio/`：

```csharp
public class AudioPolicyRouter
{
    private readonly IProviderRegistry _registry;
    private readonly ICredentialManager _credentialManager;
    private readonly IModelRegistry _modelRegistry;

    public async Task<IAsrProvider> ResolveAsrProviderAsync(
        AsrRoutingContext context, CancellationToken ct)
    {
        // 1. 过滤隐私等级
        // 2. 过滤执行位置
        // 3. 过滤凭证可用性
        // 4. 过滤 Provider 能力限制
        // 5. 按语言和场景排序
        // 6. 按健康和成本排序
        // 7. 应用用户显式指定
        // 8. 返回最优 Provider 或触发降级
    }
}
```

### 5.4 降级策略

降级不得未经授权从本地切换到云端。用户需预先选择失败策略：

| 失败策略 | 行为 |
|---------|------|
| `STOP` | 失败后停止，等待用户处理 |
| `LOCAL_FALLBACK` | 失败后切换到本地 Provider |
| `PLATFORM_FALLBACK` | 失败后切换到平台云端并计费 |

---

## 六、BYOK 凭证管理

### 6.1 provider_credential 实体

在 `src/KnowledgeEngine.Domain/Entities/` 下新增：

```csharp
public class ProviderCredential
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string OwnerType { get; set; }       // user / tenant
    public Guid OwnerId { get; set; }
    public string ProviderId { get; set; }       // zhipu / azure / aliyun
    public string CredentialType { get; set; }   // api_key / oauth_token / bearer
    public string EncryptedSecret { get; set; }  // AES 加密后的凭证
    public string KeyVersion { get; set; }        // 加密密钥版本
    public string Status { get; set; }           // active / disabled / expired
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 6.2 ICredentialManager

在 `src/KnowledgeEngine.Application/Interfaces/` 下新增：

```csharp
public interface ICredentialManager
{
    Task<ProviderCredential> StoreAsync(StoreCredentialRequest request, CancellationToken ct);
    Task<string> GetSecretAsync(Guid credentialId, CancellationToken ct);  // 临时解密
    Task<bool> VerifyAsync(Guid credentialId, CancellationToken ct);
    Task DisableAsync(Guid credentialId, CancellationToken ct);
    Task RotateAsync(Guid credentialId, CancellationToken ct);
}
```

### 6.3 安全要求

- 凭证使用 AES-GCM 加密存储，密钥由 `ICredentialStore` 管理（复用现有 `PlatformCredentialStore`）
- 与租户隔离，不同租户的凭证不可互访
- 不写入普通日志，不返回前端
- 支持测试连接、轮换、禁用、删除和过期
- 任务执行时临时解密，用后即弃

### 6.4 与现有系统集成

- 复用 `ICredentialStore` 抽象，扩展支持音频 Provider 凭证
- `CloudAccountBinding` 已有云账号绑定体系，可关联 `ProviderCredential`
- `WorkspaceBinding` 可扩展支持 Workspace 级别的 Provider 凭证配置

---

## 七、Media Preparation Layer

所有音频在进入 ASR 之前，先经过统一的媒体预处理流水线：

```
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

### 7.1 Audio Cache

缓存键：`source_sha256 + sample_rate + channels + normalize_version`，避免重复执行 FFmpeg 转码和标准化。

新增 `IAudioCacheService` 接口和 `AudioCacheService` 实现（`src/KnowledgeEngine.Infrastructure/Audio/`），标准化参数为 16kHz / mono / pcm_s16le WAV。

### 7.2 VAD 统一时间轴

VAD 的物理切片是所有后续能力的时间基线。禁止简单将不同 Provider 返回的时间戳直接拼接为最终时间轴。

VAD → Physical Segment → ASR → Speaker → Correction → Summary/Entity/Todo

### 7.3 Segment UUID

每个物理片段必须拥有稳定的 `segment_uuid`。以下对象全部引用 `segment_uuid`：

- Speaker, Summary, Entity, Todo, Quote, Knowledge Graph
- Citation, Meeting Decision, Risk, Open Question

禁止使用 `start_ms/end_ms` 作为业务关系主键。

---

## 八、核心数据模型

### 8.1 新增实体

#### audio_asset

```csharp
public class AudioAsset
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string OriginalFilePath { get; set; }
    public string? NormalizedFilePath { get; set; }
    public string SourceSha256 { get; set; }
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; }
    public long DurationMs { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string DataClassification { get; set; }  // PUBLIC/INTERNAL/PRIVATE/STRICT_LOCAL
    public DateTime CreatedAt { get; set; }
}
```

#### transcription_job

```csharp
public class TranscriptionJob
{
    public Guid Id { get; set; }
    public Guid AudioAssetId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid UserId { get; set; }

    // 四层解耦
    public string ExecutionMode { get; set; }
    public string CredentialMode { get; set; }
    public string ProviderId { get; set; }
    public string ModelId { get; set; }
    public string FallbackPolicy { get; set; }    // STOP / LOCAL_FALLBACK / PLATFORM_FALLBACK

    // 成本
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }

    public string Status { get; set; }             // pending/running/completed/failed
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

#### transcription_segment

```csharp
public class TranscriptionSegment
{
    public Guid Id { get; set; }
    public Guid TranscriptionJobId { get; set; }
    public Guid? DocumentId { get; set; }
    public string SegmentUuid { get; set; }         // 稳定 UUID，外部引用用
    public long SourceStartMs { get; set; }
    public long SourceEndMs { get; set; }
    public string ProviderId { get; set; }
    public string ModelId { get; set; }
    public decimal Confidence { get; set; }
    public string? SpeakerKey { get; set; }
    public string Text { get; set; }
    public string Version { get; set; }              // RAW_MODEL/POST_PROCESSED/SERVER_RETRANSCRIBED/USER_EDITED/MERGED/PUBLISHED
    public DateTime CreatedAt { get; set; }
}
```

#### provider_usage_record

```csharp
public class ProviderUsageRecord
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Capability { get; set; }          // audio.transcription / audio.synthesis
    public string ProviderId { get; set; }
    public string ModelId { get; set; }
    public string CredentialMode { get; set; }
    public string ExecutionMode { get; set; }
    public long DurationMs { get; set; }
    public int RequestCount { get; set; }
    public decimal? InputUnits { get; set; }
    public decimal? OutputUnits { get; set; }
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public string Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 8.2 现有实体扩展

| 实体 | 新增字段 | 说明 |
|------|---------|------|
| Document | `AudioAssetId?`, `AudioDurationMs?`, `AudioLanguage?`, `AudioSegmentCount?`, `AsrStatus?`, `AsrProcessedAt?` | 关联音频资产和转写状态 |
| DocumentChunk | `AudioSegmentUuid?` | 可选关联音频段 UUID |
| InboxItem | `AudioAssetId?` | 关联音频资产 |

### 8.3 数据库迁移

遵循现有 `scripts/migrations/` 目录约定，每个实体提供 PostgreSQL 和 SQLite 双版本。

---

## 九、安全与隐私

### 9.1 数据分级

| 等级 | 说明 | 约束 |
|------|------|------|
| PUBLIC | 公开资料 | 无限制 |
| INTERNAL | 内部资料 | 默认走本地或平台托管 |
| PRIVATE | 私有资料 | 需用户显式允许云端 |
| STRICT_LOCAL | 严格本地 | 禁止上传原音、禁止调用云端 Provider、禁止自动同步、禁止第三方 TTS |

### 9.2 Provider 隐私声明

所有 Provider 必须通过 Descriptor 声明：

| 声明字段 | 含义 |
|---------|------|
| `SendsAudioOffDevice` | 音频是否离开设备 |
| `StoresProviderData` | Provider 是否存储数据（UNKNOWN/NO/TEMPORARY/YES） |
| `DataRegion` | 数据处理区域 |
| `RetentionPolicy` | 数据保留策略 |

Policy Router 在路由时检查这些声明，STRICT_LOCAL 数据只能路由到 `SendsAudioOffDevice=false` 的 Provider。

### 9.3 云端降级授权

未经用户授权，不得从本地自动切换到第三方云端。降级策略需用户预先配置：

- 失败后停止
- 失败后切换本地
- 失败后切换平台云端并计费

---

## 十、实施计划

### P0：Capability Contract 与采集转录

**目标**：建立 Capability Contract、Provider Adapter SDK、Policy Router、BYOK 凭证管理、Media Preparation Layer 和 Segment UUID，将现有硬编码 Whisper 调用重构为标准 Provider 实现。

**预估工期**：5-6 周

#### 第 1-2 周：Capability Contract 与枚举体系

| 交付物 | 路径 | 说明 |
|--------|------|------|
| ExecutionMode / CredentialMode / DataClassification 枚举 | `Domain/Enums/` | 四层解耦枚举 |
| IAsrProvider 接口 + AsrProviderDescriptor | `Application/Interfaces/` | ASR 能力协议 |
| ITtsProvider 接口 + TtsProviderDescriptor | `Application/Interfaces/` | TTS 能力协议 |
| IProviderRegistry 接口 | `Application/Interfaces/` | Provider 注册与发现 |
| ProviderHealth / CostEstimate / ValidationResult DTO | `Application/DTOs/` | Provider 返回值结构 |
| AsrTranscriptionRequest / AsrTranscriptionResult / AsrPartialResult DTO | `Application/DTOs/` | ASR 请求响应结构 |
| TtsRequest / TtsResult / AudioChunk DTO | `Application/DTOs/` | TTS 请求响应结构 |

#### 第 2-3 周：官方预置 Provider 实现

| 交付物 | 路径 | 说明 |
|--------|------|------|
| WhisperCppAsrProvider | `Infrastructure/Audio/Providers/` | 从 MediaProcessingService 提取，支持 JSON 输出和时间戳 |
| FunAsrAsrProvider | `Infrastructure/Audio/Providers/` | FunASR HTTP API 适配器 |
| FunAsrVadProvider | `Infrastructure/Audio/Providers/` | VAD 能力 Provider |
| FunAsrPunctuationProvider | `Infrastructure/Audio/Providers/` | 标点恢复 Provider |
| ProviderRegistry 实现 | `Infrastructure/Audio/` | DI 自动注册 + 运行时发现 |
| MediaProcessingService 重构 | `Application/Services/` | 移除硬编码，委托 Provider |

#### 第 3-4 周：Policy Router 与 BYOK

| 交付物 | 路径 | 说明 |
|--------|------|------|
| AudioPolicyRouter | `Infrastructure/Audio/` | 8 步路由策略实现 |
| ProviderCredential 实体 | `Domain/Entities/` | BYOK 凭证存储 |
| ICredentialManager 接口 | `Application/Interfaces/` | 凭证管理抽象 |
| CredentialManager 实现 | `Infrastructure/Audio/` | 加密存储 + 临时解密 + 验证 |
| FallbackPolicy 处理 | `Infrastructure/Audio/` | 降级策略执行（需用户授权云端降级） |

#### 第 4-5 周：Media Preparation Layer 与 Segment

| 交付物 | 路径 | 说明 |
|--------|------|------|
| IAudioCacheService + 实现 | `Infrastructure/Audio/` | SHA256 去重 + FFmpeg 标准化缓存 |
| VadService | `Infrastructure/Audio/` | VAD 物理切片，统一时间轴 |
| AudioAsset 实体 | `Domain/Entities/` | 音频资产元数据 |
| TranscriptionJob 实体 | `Domain/Entities/` | 转录任务（含四层解耦字段） |
| TranscriptionSegment 实体 | `Domain/Entities/` | 转录分段（含 segment_uuid） |
| ProviderUsageRecord 实体 | `Domain/Entities/` | Provider 用量记录 |
| Document 扩展字段 | `Domain/Entities/` | 音频元数据字段 |
| 数据库迁移脚本 | `scripts/migrations/` | 双版本 SQL |

#### 第 5-6 周：集成与端到端验证

| 交付物 | 路径 | 说明 |
|--------|------|------|
| AudioCaptureController | `Api/Controllers/` | 音频上传 API（支持 Provider 选择和隐私声明） |
| TranscriptionController | `Api/Controllers/` | 转录任务管理 API |
| TranscriptionHub (WebSocket) | `Api/Hubs/` | 流式 ASR 端点 |
| docker-compose 更新 | `docker-compose.yml` | 新增 FunASR 可选容器 |
| appsettings 配置 | `appsettings.json` | AudioSettings + Provider 配置 |
| DI 注册更新 | `Infrastructure/DependencyInjection.cs` | 注册所有新增服务 |
| 端到端测试 | - | 本地模式 + STRICT_LOCAL 验证 |

### P1：TTS、纠错与设备检测

**目标**：实现 TTS Provider 体系、Post-ASR 纠错、移动端设备检测和 Provider 用量计量。

**预估工期**：5-6 周

#### 第 1-2 周：TTS Provider 体系

| 交付物 | 路径 | 说明 |
|--------|------|------|
| FishSpeechTtsProvider | `Infrastructure/Audio/Providers/` | Fish-Speech 分块流式 Provider |
| PiperTtsProvider | `Infrastructure/Audio/Providers/` | Piper 轻量级本地 Provider |
| SystemTtsProvider | `Infrastructure/Audio/Providers/` | 系统 TTS 降级 Provider |
| TTS Sentence Splitter | `Infrastructure/Audio/` | 20-80 字分块 |
| TtsHub (WebSocket) | `Api/Hubs/` | 流式 TTS 端点 |
| TTS 自动降级 | `Infrastructure/Audio/` | GPU/队列/TTFB 监控 + 降级链 |
| 声音克隆治理 | `Domain/Entities/` | VoiceCloneConsent 实体（默认关闭） |

#### 第 2-3 周：Post-ASR Correction

| 交付物 | 路径 | 说明 |
|--------|------|------|
| IPostAsrCorrectionService 接口 | `Application/Interfaces/` | 纠错能力抽象 |
| PostAsrCorrectionService | `Infrastructure/Audio/` | 纠错实现（品牌词/人名/术语/缩写/同音字/用户词典/知识库上下文） |
| CorrectionDictionary 实体 | `Domain/Entities/` | 用户/租户纠错词典 |
| TerminologyService 扩展 | `Application/Services/` | 支持音频纠错场景 |
| 版本保留 | `Infrastructure/Audio/` | 纠错生成新版本，禁止覆盖原始输出 |

#### 第 3-4 周：移动端设备检测与双模式

| 交付物 | 路径 | 说明 |
|--------|------|------|
| IDeviceCapabilityDetector 接口 | `Application/Interfaces/` | 设备检测抽象 |
| Mobile 设备检测 | `mobile/src/device-capability/` | RN 设备能力检测（CPU/RAM/Storage/Thermal/Battery） |
| Mobile 双模式识别 | `mobile/src/App.tsx` | 批处理（默认） + 实时低精度 |
| 移动端降级策略 | `mobile/src/` | 设备不足时仅录音 → 联网后 BYOK/平台云转录 |

#### 第 4-5 周：Provider 用量计量与成本估算

| 交付物 | 路径 | 说明 |
|--------|------|------|
| ProviderUsageMeteringService | `Infrastructure/Audio/` | 用量记录写入 provider_usage_record |
| CostEstimator | `Infrastructure/Audio/` | 基于 Descriptor.PricingUnit 和用量估算成本 |
| UsageController | `Api/Controllers/` | 用量查询 API |
| BillingConstants.AudioSecond 费率配置 | `appsettings.json` | 配置 AUDIO_SECOND 计费费率 |
| BYOK 失败策略实现 | `Infrastructure/Audio/` | 401/403/余额不足/限流等场景处理 |

#### 第 5-6 周：集成测试

| 交付物 | 说明 |
|--------|------|
| 本地模式端到端验证 | 严格验证无任何云端传输 |
| BYOK 模式端到端验证 | USER_BYOK 和 TENANT_BYOK 独立工作 |
| Provider 切换验证 | 同一任务切换不同 Provider，业务数据结构不变 |
| 降级授权验证 | 云端降级需用户预先授权 |

### P2：平台治理能力

**目标**：建立 Model Registry、Benchmark、Prompt Registry 和企业策略中心。

**预估工期**：4-5 周

| 交付物 | 路径 | 说明 |
|--------|------|------|
| ModelRegistry 实体 + Controller | `Domain/` + `Api/Controllers/` | 统一模型注册中心（含文件/时长/格式限制、定价、区域可用性、健康状态） |
| Policy Router 集成 ModelRegistry | `Infrastructure/Audio/` | 路由器从 ModelRegistry 读取模型列表 |
| BenchmarkResult 实体 + Service | `Domain/` + `Infrastructure/Benchmark/` | 评测中心（CER/WER/RTF/GPU/Memory/TTFB/Throughput + 专有名词准确率/时间戳偏差/说话人准确率/用户修改率/单位成本） |
| BenchmarkController | `Api/Controllers/` | 评测 API + 排名报告（最快/最准/最低成本/最适合中文/最适合移动端/最适合多人会议） |
| PromptRegistry 实体 | `Domain/Entities/` | 提示词版本管理（含 Evaluation Set / Provider Compatibility） |
| PromptABTest 实体 | `Domain/Entities/` | A/B 测试 |
| PromptRegistryController | `Api/Controllers/` | 提示词管理 API（发布/回滚/A-B Test） |
| AISummaryService 改造 | `Infrastructure/Processing/` | 接入 PromptRegistry，禁止 Prompt 写死在业务代码 |
| 平台托管计费 | `Infrastructure/Billing/` | PLATFORM_MANAGED 模式的计量、配额、结算 |
| 企业策略中心 | `Api/Controllers/` | 企业级 Provider 策略、配额、审计配置 |
| 数据库迁移脚本 | `scripts/migrations/` | 双版本 SQL |

### P3：DAG 流水线与高级架构

**目标**：DAG 化内容处理流水线、转录版本合并、局域网算力节点和多云容灾。

**预估工期**：5-7 周

#### 第 1-2 周：DAG Pipeline

| 交付物 | 路径 | 说明 |
|--------|------|------|
| IPipelineNode 接口 | `Application/Interfaces/` | DAG 节点抽象（含 Schema/幂等/重试/超时/并行/版本/Provider 指定/成本估算/数据分类策略） |
| DagPipelineEngine | `Infrastructure/Pipeline/` | 拓扑排序 + 并行执行 |
| AsrNode / CorrectionNode / EmbeddingNode / SummaryNode / EntityNode / TodoNode / TranslationNode / DiarizationNode / KnowledgeGraphNode | `Infrastructure/Pipeline/Nodes/` | 现有步骤封装为 DAG 节点 |
| DocumentPipeline 改造 | `Infrastructure/Processing/` | 委托 DagPipelineEngine，保持 IDocumentPipeline 接口不变 |

#### 第 3 周：版本与合并

| 交付物 | 路径 | 说明 |
|--------|------|------|
| TranscriptionVersion 实体 | `Domain/Entities/` | 版本树：RAW_MODEL → POST_PROCESSED → SERVER_RETRANSCRIBED → USER_EDITED → MERGED → PUBLISHED |
| VersionMergeService | `Infrastructure/Audio/` | 三方合并（Base / Local Edit / Server Retranscription），用户编辑不被静默覆盖 |

#### 第 4-5 周：局域网算力节点与 gRPC

| 交付物 | 路径 | 说明 |
|--------|------|------|
| LanNodeDiscovery | `Infrastructure/Audio/` | 局域网算力节点发现与注册 |
| LanNodeProvider | `Infrastructure/Audio/Providers/` | 委托至局域网节点的 Provider 实现 |
| gRPC 服务定义（可选） | `KnowledgeEngine.Grpc/` | `.proto` 文件：AsrService / TtsService / PipelineService |
| gRPC 开关 | `appsettings.json` | 配置控制是否启用 gRPC |

#### 第 5-7 周：Provider Marketplace 与多云容灾

| 交付物 | 路径 | 说明 |
|--------|------|------|
| ProviderMarketplace | `Api/Controllers/` | Provider 市场（浏览/安装/卸载/评分） |
| MultiCloudFailover | `Infrastructure/Audio/` | 多云容灾与自动切换（仍受隐私策略约束） |
| 全面回归测试 | - | 验收标准 12 项全量验证 |

---

## 十一、验收标准

1. 本地模式不产生任何云端传输
2. USER_BYOK、TENANT_BYOK 和 PLATFORM_MANAGED 可独立工作
3. 同一 ASR 任务可切换不同 Provider，而不改变业务数据结构
4. Provider 限制通过 Descriptor 动态生效
5. GLM-ASR 等云模型可通过 Adapter 接入，不直接出现在业务代码中
6. 长音频可经 VAD 切片后调用受限 Provider
7. 用户编辑不会被二次识别结果静默覆盖
8. 云端降级必须经过用户授权
9. 所有费用和调用量可追踪（provider_usage_record）
10. 所有摘要、结论和待办可回溯到 `segment_uuid`
11. Provider 下线或更换不影响知识库核心数据
12. 严格本地策略具有自动化测试和审计证据

---

## 十二、Docker 与部署方案

### 12.1 Provider 容器（可选，非架构依赖）

以下容器为 Provider 的可选运行时，系统在没有它们时仍可工作（降级到其他 Provider）：

| 服务 | 镜像 | 端口 | 阶段 | 说明 |
|------|------|------|------|------|
| FunASR | `registry.cn-hangzhou.aliyuncs.com/funasr_repo/funasr` | 10095 | P0 | 本地中文 ASR Provider（可选） |
| Fish-Speech | `fishaudio/fish-speech` | 8080 | P1 | 本地高质量 TTS Provider（可选，需 GPU） |
| Piper | `rhasspy/wyoming-piper` | 10200 | P1 | 轻量级 TTS Provider |
| FFmpeg | 内置于 API 容器 | - | P0 | 音频标准化 |

### 12.2 桌面端打包

Tauri 打包策略：
- FFmpeg 随应用打包（跨平台二进制）
- Whisper 模型文件首次使用时提示下载
- Piper 模型随应用打包（体积小）
- FunASR 和 Fish-Speech 默认不启用，作为可选增强
- 所有 Provider 通过 Descriptor 声明能力，运行时自动适配

---

## 十三、风险与缓解

### 13.1 架构风险

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| Provider Descriptor 配置遗漏 | 路由器选择错误 Provider | Provider 注册时强制校验 Descriptor 必填字段 |
| BYOK 凭证泄露 | 安全事故 | AES-GCM 加密 + 租户隔离 + 不记日志 + 临时解密 |
| STRICT_LOCAL 策略绕过 | 隐私泄露 | Policy Router 第一优先级检查 + 自动化测试 + 审计日志 |
| Provider 下线 | 对应能力不可用 | ProviderRegistry 健康检查 + 自动降级到其他 Provider |
| DAG Pipeline 重构影响现有功能 | 文档处理流程中断 | 保持 `IDocumentPipeline` 接口不变，渐进式迁移 |

### 13.2 兼容性保障

1. **向后兼容** — 所有新增字段使用可空类型，现有数据不受影响
2. **双数据库** — 每个迁移脚本同时提供 PostgreSQL 和 SQLite 版本
3. **渐进式迁移** — `MediaProcessingService` 重构后，旧路径的 InboxItem 仍能正常处理
4. **功能开关** — 所有新功能通过 `appsettings.json` 配置开关控制，可随时关闭
5. **Provider 可选** — 系统在没有 FunASR/Fish-Speech 容器时仍可工作，自动降级到 Whisper/Piper

---

## 十四、实施时间线

```
2026 Q3-Q4
├── 8月-9月上   P0: Capability Contract + Provider SDK + Policy Router + BYOK + Media Prep + Segment（5-6 周）
│               ├── 第1-2周: Capability Contract + 枚举体系 + DTO
│               ├── 第2-3周: 官方预置 Provider 实现（WhisperCpp/FunASR）+ ProviderRegistry
│               ├── 第3-4周: Policy Router + BYOK 凭证管理
│               ├── 第4-5周: Media Preparation Layer + Segment 实体 + 迁移脚本
│               └── 第5-6周: API + WebSocket + 集成测试 + 端到端验证
│
├── 9月-10月    P1: TTS Provider + Post-ASR 纠错 + 设备检测 + 用量计量（5-6 周）
│               ├── 第1-2周: TTS Provider 体系 + 分块流式 + 降级 + 声音克隆治理
│               ├── 第2-3周: Post-ASR Correction + 用户词典 + 版本保留
│               ├── 第3-4周: 移动端设备检测 + 双模式 + 降级策略
│               ├── 第4-5周: Provider Usage Metering + Cost Estimator + BYOK 失败策略
│               └── 第5-6周: 集成测试（本地/BYOK/降级授权验证）
│
├── 10月-11月   P2: Model Registry + Benchmark + Prompt Registry + 企业策略（4-5 周）
│               ├── 第1-2周: ModelRegistry + Policy Router 集成 + Benchmark
│               ├── 第2-3周: Benchmark 排名报告 + PromptRegistry + A/B Test
│               ├── 第3-4周: AISummaryService 改造 + 平台托管计费 + 企业策略中心
│               └── 第4-5周: 集成测试 + 文档编写
│
└── 11月-12月   P3: DAG Pipeline + Version Merge + LAN 节点 + 多云容灾（5-7 周）
                ├── 第1-2周: DAG 引擎 + 节点封装
                ├── 第3周: Version Merge Engine + 三方合并
                ├── 第4-5周: 局域网算力节点 + gRPC（可选）
                ├── 第5-6周: Provider Marketplace + 多云容灾
                └── 第6-7周: 全面回归测试（验收标准 12 项）
```

### 里程碑

| 里程碑 | 预计完成 | 交付标准 |
|--------|---------|---------|
| M1: P0 完成 | 2026-09 上旬 | 音频上传后通过 Policy Router 选择 Provider 转写，生成带 segment_uuid 的分段，Audio Cache 生效，BYOK 凭证可管理，STRICT_LOCAL 有自动化测试 |
| M2: P1 完成 | 2026-10 中旬 | TTS 流式播放 + 自动降级，转写文本自动纠错，移动端双模式，Provider 用量可追踪，云端降级需授权 |
| M3: P2 完成 | 2026-11 中旬 | 模型通过 Registry 管理，Benchmark 自动生成排名，Prompt 版本化与 A/B Test，企业策略中心可用 |
| M4: P3 完成 | 2026-12 底 | DAG 流水线 + 版本合并 + 局域网节点 + Provider Marketplace + 多云容灾，验收标准 12 项全通过 |

---

## 十五、技术选型定位

以下技术选型不再是系统固定依赖，而是官方预置参考实现。Provider 可替换，不影响核心架构：

| 能力场景 | 官方预置 Provider | 替代方案 |
|---------|-----------------|---------|
| 本地中文 ASR | FunASR Provider | faster-whisper, GLM-ASR Cloud |
| 本地/私有多语言 ASR | faster-whisper Provider | whisper.cpp, FunASR |
| 移动端/桌面端离线 ASR | whisper.cpp Provider | faster-whisper, FunASR Local |
| 第三方云 ASR | GLM-ASR Provider | Azure Speech, 阿里云, 腾讯云 |
| 本地高质量 TTS | Fish-Speech Provider | 云端 TTS |
| 轻量级本地 TTS | Piper / System TTS | Fish-Speech |
| 第三方云 TTS | GLM-TTS 或其他兼容 Provider | Azure, 阿里云 |

Memorix 核心只依赖：

```
Capability Contract
Policy Router
Provider Adapter SDK
Credential Manager
Model Registry
Usage Metering
DAG Pipeline
```

业务代码直接绑定 FunASR、Whisper、Fish-Speech 或 GLM 是严格禁止的。
