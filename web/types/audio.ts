/**
 * TypeScript type definitions for the Memorix audio capability platform.
 *
 * These types mirror the backend DTOs, domain entities, and enums defined in:
 *   - KnowledgeEngine.Application/DTOs/AudioDtos.cs
 *   - KnowledgeEngine.Domain/Entities/*.cs
 *   - KnowledgeEngine.Domain/Enums/AudioEnums.cs
 *   - KnowledgeEngine.Api/Controllers/*.cs (controller-level request DTOs)
 *   - KnowledgeEngine.Api/Hubs/TranscriptionHub.cs
 *   - KnowledgeEngine.Application/Interfaces/IAudioCacheService.cs
 *   - KnowledgeEngine.Application/Interfaces/IBenchmarkService.cs
 *   - KnowledgeEngine.Application/Interfaces/IDeviceCapabilityDetector.cs
 *   - KnowledgeEngine.Application/Interfaces/IPostAsrCorrectionService.cs
 */

// ═══════════════════════════════════════════════════════════════════════════
// ENUM TYPES (mirrors Domain/Enums/AudioEnums.cs)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Where the capability actually executes.
 * C# enum: ExecutionMode
 */
export type ExecutionMode =
  | "LOCAL_DEVICE"
  | "LOCAL_LAN_NODE"
  | "MEMORIX_CLOUD"
  | "THIRD_PARTY_CLOUD";

/**
 * Who provides the credential for the capability.
 * C# enum: CredentialMode
 */
export type CredentialMode =
  | "NO_CREDENTIAL"
  | "USER_BYOK"
  | "TENANT_BYOK"
  | "PLATFORM_MANAGED";

/**
 * Whether the provider stores user data after processing.
 * C# enum: ProviderDataRetention
 */
export type ProviderDataRetention =
  | "UNKNOWN"
  | "NO"
  | "TEMPORARY"
  | "YES";

/**
 * Data sensitivity classification for privacy routing.
 * C# enum: DataClassification
 */
export type DataClassification =
  | "PUBLIC"
  | "INTERNAL"
  | "PRIVATE"
  | "STRICT_LOCAL";

// ═══════════════════════════════════════════════════════════════════════════
// ENUM CONSTANT OBJECTS (mirrors Domain/Enums/AudioEnums.cs static classes)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Transcription segment version labels.
 * C# static class: SegmentVersions
 */
export const SegmentVersions = {
  RawModel: "RAW_MODEL",
  PostProcessed: "POST_PROCESSED",
  ServerRetranscribed: "SERVER_RETRANSCRIBED",
  UserEdited: "USER_EDITED",
  Merged: "MERGED",
  Published: "PUBLISHED",
} as const;

export type SegmentVersion =
  (typeof SegmentVersions)[keyof typeof SegmentVersions];

/**
 * Execution mode constants.
 * C# enum: ExecutionMode
 */
export const ExecutionModes = {
  LocalDevice: "LOCAL_DEVICE",
  LocalLanNode: "LOCAL_LAN_NODE",
  MemorixCloud: "MEMORIX_CLOUD",
  ThirdPartyCloud: "THIRD_PARTY_CLOUD",
} as const;

/**
 * Credential mode constants.
 * C# enum: CredentialMode
 */
export const CredentialModes = {
  NoCredential: "NO_CREDENTIAL",
  UserByok: "USER_BYOK",
  TenantByok: "TENANT_BYOK",
  PlatformManaged: "PLATFORM_MANAGED",
} as const;

/**
 * Fallback policy when a provider fails.
 * C# static class: FallbackPolicies
 */
export const FallbackPolicies = {
  Stop: "STOP",
  LocalFallback: "LOCAL_FALLBACK",
  PlatformFallback: "PLATFORM_FALLBACK",
} as const;

export type FallbackPolicy =
  (typeof FallbackPolicies)[keyof typeof FallbackPolicies];

/**
 * Transcription job status values.
 * C# static class: TranscriptionJobStatuses
 */
export const TranscriptionJobStatuses = {
  Pending: "pending",
  Running: "running",
  Completed: "completed",
  Failed: "failed",
  Cancelled: "cancelled",
} as const;

export type TranscriptionJobStatus =
  (typeof TranscriptionJobStatuses)[keyof typeof TranscriptionJobStatuses];

/**
 * Provider credential status values.
 * C# static class: CredentialStatuses
 */
export const CredentialStatuses = {
  Active: "active",
  Disabled: "disabled",
  Expired: "expired",
} as const;

export type CredentialStatus =
  (typeof CredentialStatuses)[keyof typeof CredentialStatuses];

/**
 * Provider credential owner types.
 * C# static class: CredentialOwnerTypes
 */
export const CredentialOwnerTypes = {
  User: "user",
  Tenant: "tenant",
} as const;

export type CredentialOwnerType =
  (typeof CredentialOwnerTypes)[keyof typeof CredentialOwnerTypes];

/**
 * Pricing unit types for provider cost estimation.
 * C# static class: PricingUnits
 */
export const PricingUnits = {
  Request: "REQUEST",
  Second: "SECOND",
  Minute: "MINUTE",
  Token: "TOKEN",
} as const;

export type PricingUnit =
  (typeof PricingUnits)[keyof typeof PricingUnits];

/**
 * Audio capability identifiers.
 * C# static class: AudioCapabilities
 */
export const AudioCapabilities = {
  Vad: "audio.vad",
  Transcription: "audio.transcription",
  Diarization: "audio.diarization",
  Punctuation: "audio.punctuation",
  Correction: "audio.correction",
  Synthesis: "audio.synthesis",
} as const;

export type AudioCapability =
  (typeof AudioCapabilities)[keyof typeof AudioCapabilities];

/**
 * Model registry health status values.
 * C# static class: ModelRegistryStatuses
 */
export const ModelRegistryStatuses = {
  Healthy: "healthy",
  Degraded: "degraded",
  Unhealthy: "unhealthy",
  Unknown: "unknown",
} as const;

export type ModelRegistryStatus =
  (typeof ModelRegistryStatuses)[keyof typeof ModelRegistryStatuses];

/**
 * Prompt registry lifecycle status values.
 * C# static class: PromptRegistryStatuses
 */
export const PromptRegistryStatuses = {
  Draft: "draft",
  Published: "published",
  Archived: "archived",
} as const;

export type PromptRegistryStatus =
  (typeof PromptRegistryStatuses)[keyof typeof PromptRegistryStatuses];

/**
 * Prompt A/B test lifecycle status values.
 * C# static class: PromptABTestStatuses
 */
export const PromptABTestStatuses = {
  Created: "created",
  Running: "running",
  Completed: "completed",
} as const;

export type PromptABTestStatus =
  (typeof PromptABTestStatuses)[keyof typeof PromptABTestStatuses];

/**
 * LAN node status values.
 * C# static class: LanNodeStatuses
 */
export const LanNodeStatuses = {
  Online: "online",
  Offline: "offline",
  HealthChecking: "health_checking",
} as const;

export type LanNodeStatus =
  (typeof LanNodeStatuses)[keyof typeof LanNodeStatuses];

/**
 * Benchmark ranking category constants.
 * C# static class: BenchmarkRankings
 */
export const BenchmarkRankings = {
  Fastest: "fastest",
  MostAccurate: "most_accurate",
  LowestCost: "lowest_cost",
  BestChinese: "best_chinese",
  BestMobile: "best_mobile",
  BestMeeting: "best_meeting",
} as const;

export type BenchmarkRankingCategory =
  (typeof BenchmarkRankings)[keyof typeof BenchmarkRankings];

/**
 * Marketplace entry install-state metadata values.
 * C# static class: MarketplaceEntryStatuses
 */
export const MarketplaceEntryStatuses = {
  Installed: "installed",
  NotInstalled: "not_installed",
  Pending: "pending",
} as const;

// ═══════════════════════════════════════════════════════════════════════════
// AUDIO ASSET (mirrors Domain/Entities/AudioAsset.cs + AudioAssetDto)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Represents a raw or normalized audio file with privacy classification.
 */
export interface AudioAsset {
  id: string;
  sourceId: string;
  workspaceId: string | null;
  /** Original file path or object key. */
  originalFilePath: string;
  /** Path to the FFmpeg-normalized WAV file (16kHz, mono, pcm_s16le). */
  normalizedFilePath: string | null;
  /** SHA-256 of the original file for deduplication. */
  sourceSha256: string;
  fileSizeBytes: number;
  mimeType: string;
  /** Audio duration in milliseconds. */
  durationMs: number;
  sampleRate: number;
  channels: number;
  /** PUBLIC / INTERNAL / PRIVATE / STRICT_LOCAL */
  dataClassification: string;
  /** Whether audio is allowed to leave the device. */
  allowsOffDevice: boolean;
  createdAt: string;
  updatedAt: string;
}

// ═══════════════════════════════════════════════════════════════════════════
// TRANSCRIPTION (mirrors TranscriptionJobDto, TranscriptionSegmentDto,
//                TranscriptionVersion entity)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * A transcription job record.
 */
export interface TranscriptionJob {
  id: string;
  audioAssetId: string;
  workspaceId: string | null;
  userId: string;
  executionMode: string;
  credentialMode: string;
  providerId: string;
  modelId: string;
  fallbackPolicy: string;
  language: string | null;
  enableVad: boolean;
  enableSpeakerDiarization: boolean;
  enablePunctuation: boolean;
  hotwords: string | null;
  estimatedCost: number | null;
  actualCost: number | null;
  status: string;
  errorMessage: string | null;
  documentId: string | null;
  segmentCount: number | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

/**
 * A single transcription segment within a job.
 */
export interface TranscriptionSegment {
  id: string;
  transcriptionJobId: string;
  documentId: string | null;
  segmentUuid: string;
  sourceStartMs: number;
  sourceEndMs: number;
  providerId: string;
  modelId: string;
  confidence: number;
  speakerKey: string | null;
  text: string;
  version: string;
  segmentIndex: number;
  createdAt: string;
}

/**
 * Immutable record of a single transcription-segment text version within the
 * version tree. Versions progress: RAW_MODEL -> POST_PROCESSED ->
 * SERVER_RETRANSCRIBED / USER_EDITED -> MERGED -> PUBLISHED.
 */
export interface TranscriptionVersion {
  id: string;
  transcriptionJobId: string;
  /** Stable segment UUID this version belongs to. Never changes across versions. */
  segmentUuid: string;
  /** Version label (see SegmentVersions). */
  version: string;
  /** Parent version in the version tree. Root versions have null. */
  parentVersionId: string | null;
  text: string;
  providerId: string;
  modelId: string;
  /** Identifier of the user or system process that created this version. */
  createdBy: string | null;
  createdAt: string;
}

// ═══════════════════════════════════════════════════════════════════════════
// PROVIDER CREDENTIAL (mirrors CredentialDto, StoreCredentialRequest)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * BYOK credential DTO. Never contains the encrypted secret.
 */
export interface ProviderCredential {
  id: string;
  providerId: string;
  credentialType: string;
  ownerType: string;
  ownerId: string;
  label: string | null;
  status: string;
  lastVerifiedAt: string | null;
  expiresAt: string | null;
  createdAt: string;
}

/**
 * Request to store a new provider credential.
 */
export interface StoreCredentialRequest {
  providerId: string;
  credentialType: string;
  secret: string;
  ownerType: string;
  ownerId: string;
  tenantId: string | null;
  label: string | null;
  expiresAt: string | null;
}

// ═══════════════════════════════════════════════════════════════════════════
// PROVIDER USAGE (mirrors ProviderUsageRecord entity + ProviderUsageRecordDto)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Records each provider invocation for billing, audit, and cost analysis.
 */
export interface ProviderUsageRecord {
  id: string;
  tenantId: string | null;
  userId: string;
  workspaceId: string | null;
  /** audio.transcription / audio.synthesis / audio.vad / audio.diarization */
  capability: string;
  providerId: string;
  modelId: string;
  /** NO_CREDENTIAL / USER_BYOK / TENANT_BYOK / PLATFORM_MANAGED */
  credentialMode: string;
  /** LOCAL_DEVICE / LOCAL_LAN_NODE / MEMORIX_CLOUD / THIRD_PARTY_CLOUD */
  executionMode: string;
  /** Audio duration processed, in milliseconds. */
  durationMs: number;
  requestCount: number;
  inputUnits: number | null;
  outputUnits: number | null;
  estimatedCost: number | null;
  actualCost: number | null;
  /** success / failed / partial */
  status: string;
  errorMessage: string | null;
  /** Link to the transcription job if applicable. */
  transcriptionJobId: string | null;
  createdAt: string;
}

/**
 * Aggregated usage summary for a user within a date range.
 */
export interface AudioUsageSummary {
  /** Total cost across all providers (uses ActualCost when available, else EstimatedCost). */
  totalCost: number;
  /** Total audio duration processed, in milliseconds. */
  totalDurationMs: number;
  /** Total number of provider requests. */
  totalRequests: number;
  /** Total number of usage records. */
  recordCount: number;
  /** Start of the summary period. */
  from: string;
  /** End of the summary period. */
  to: string;
  /** Per-provider breakdown. */
  byProvider: ProviderUsageBreakdown[];
}

/**
 * Usage breakdown for a single provider.
 */
export interface ProviderUsageBreakdown {
  providerId: string;
  cost: number;
  durationMs: number;
  requestCount: number;
  recordCount: number;
}

/**
 * Usage breakdown for a single capability (e.g. transcription, synthesis).
 */
export interface CapabilityUsageBreakdown {
  capability: string;
  cost: number;
  durationMs: number;
  requestCount: number;
  recordCount: number;
}

/**
 * Daily usage aggregation for charting.
 */
export interface DailyUsageBreakdown {
  /** The calendar date (UTC) for this aggregation bucket. */
  date: string;
  cost: number;
  durationMs: number;
  requestCount: number;
  recordCount: number;
}

// ═══════════════════════════════════════════════════════════════════════════
// MODEL REGISTRY (mirrors ModelRegistry entity + RegisterModelRequest)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Unified model registration entity for the audio capability platform.
 */
export interface ModelRegistry {
  id: string;
  providerId: string;
  modelId: string;
  displayName: string;
  /** audio.transcription / audio.synthesis / audio.vad / audio.punctuation */
  capability: string;
  /** Comma-separated execution modes. */
  executionModes: string;
  /** Comma-separated credential modes. */
  credentialModes: string;
  /** Comma-separated supported languages. Empty means all languages. */
  supportedLanguages: string;
  maxFileBytes: number | null;
  maxAudioDurationMs: number | null;
  /** Comma-separated accepted MIME types. */
  acceptedMimeTypes: string;
  supportsStreaming: boolean;
  supportsBatch: boolean;
  supportsVad: boolean;
  supportsPunctuation: boolean;
  supportsDiarization: boolean;
  supportsHotwords: boolean;
  supportsWordTimestamp: boolean;
  supportsSegmentTimestamp: boolean;
  sendsAudioOffDevice: boolean;
  storesProviderData: boolean;
  pricingUnit: string | null;
  dataRegion: string | null;
  retentionPolicy: string | null;
  isEnabled: boolean;
  healthStatus: string;
  lastHealthCheckAt: string | null;
  createdAt: string;
  updatedAt: string;
}

/**
 * Request payload for registering or updating a model registry entry.
 */
export interface RegisterModelRequest {
  providerId: string;
  modelId: string;
  displayName: string | null;
  capability: string;
  /** Comma-separated execution modes. */
  executionModes: string | null;
  /** Comma-separated credential modes. */
  credentialModes: string | null;
  /** Comma-separated supported languages. */
  supportedLanguages: string | null;
  maxFileBytes: number | null;
  maxAudioDurationMs: number | null;
  /** Comma-separated accepted MIME types. */
  acceptedMimeTypes: string | null;
  supportsStreaming: boolean;
  supportsBatch: boolean;
  supportsVad: boolean;
  supportsPunctuation: boolean;
  supportsDiarization: boolean;
  supportsHotwords: boolean;
  supportsWordTimestamp: boolean;
  supportsSegmentTimestamp: boolean;
  sendsAudioOffDevice: boolean;
  storesProviderData: boolean;
  pricingUnit: string | null;
  dataRegion: string | null;
  retentionPolicy: string | null;
  isEnabled: boolean;
  healthStatus: string | null;
}

// ═══════════════════════════════════════════════════════════════════════════
// BENCHMARK (mirrors BenchmarkResult entity, RankingEntry, RunBenchmarkRequest)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Benchmark evaluation result for a registered model.
 */
export interface BenchmarkResult {
  id: string;
  modelRegistryId: string;
  benchmarkName: string;
  /** Character Error Rate (lower is better). */
  cer: number;
  /** Word Error Rate (lower is better). */
  wer: number;
  /** Real-Time Factor: processing_time / audio_duration (lower is better). */
  rtf: number;
  /** Peak GPU memory usage in MB, or null if not applicable. */
  gpuMemoryMb: number | null;
  /** Peak CPU memory usage in MB, or null if not measured. */
  cpuMemoryMb: number | null;
  /** Time to First Byte in milliseconds (lower is better). */
  ttfb: number;
  /** Throughput in segments per second (higher is better). */
  throughput: number;
  /** Proper noun accuracy (0-1), or null if not measured. */
  properNounAccuracy: number | null;
  /** Timestamp deviation in milliseconds (lower is better), or null if not measured. */
  timestampDeviationMs: number | null;
  /** Speaker diarization accuracy (0-1), or null if not measured. */
  speakerAccuracy: number | null;
  /** User modification rate (0-1): fraction of segments edited by users (lower is better). */
  userModificationRate: number | null;
  /** Cost per unit (matches the model's pricing unit). */
  unitCost: number;
  evaluatedAt: string;
  /** Name of the evaluation dataset, or null if not specified. */
  datasetName: string | null;
  /** Free-form notes about the benchmark run. */
  notes: string | null;
  createdAt: string;
}

/**
 * A single entry in a benchmark ranking leaderboard.
 */
export interface RankingEntry {
  modelRegistryId: string;
  providerId: string;
  modelId: string;
  displayName: string;
  /** The metric value used for ranking (e.g. throughput, CER, unit cost). */
  score: number;
  /** Name of the metric used for ranking (e.g. "throughput", "cer", "unit_cost"). */
  metric: string;
  /** 1-based rank position. */
  rank: number;
}

/**
 * Request payload for triggering a benchmark run.
 */
export interface RunBenchmarkRequest {
  modelRegistryId: string;
  /** Name of the evaluation dataset (e.g. "aishell-1", "commonvoice-chinese"). */
  datasetName: string | null;
}

// ═══════════════════════════════════════════════════════════════════════════
// PROMPT REGISTRY (mirrors PromptRegistry entity + CreatePromptRequest)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Versioned prompt registry entry for AI capabilities.
 */
export interface PromptRegistry {
  id: string;
  /** Logical prompt key, e.g. "summary.default", "entity.extract". */
  promptKey: string;
  /** Semantic version string, e.g. "1.0.0", "1.1.0". */
  version: string;
  title: string;
  description: string | null;
  /** The system prompt text sent to the LLM. */
  systemPrompt: string;
  /** User prompt template with placeholders (e.g. {{title}}, {{content}}). */
  userPromptTemplate: string;
  /** Optional language code filter (e.g. "zh-CN", "en"). Null = language-agnostic. */
  language: string | null;
  /** Comma-separated list of provider IDs this prompt is compatible with. */
  providerCompatibility: string;
  /** Optional evaluation score (0-100). */
  evaluationScore: number | null;
  /** Whether this prompt version is the active one for its key. */
  isActive: boolean;
  /** Lifecycle status: draft / published / archived. */
  status: string;
  /** Timestamp when this prompt was published. Null while in draft. */
  publishedAt: string | null;
  /** User or system that created this prompt version. */
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * Request to create a new prompt version.
 */
export interface CreatePromptRequest {
  promptKey: string;
  version: string | null;
  title: string | null;
  description: string | null;
  systemPrompt: string;
  userPromptTemplate: string | null;
  language: string | null;
  providerCompatibility: string | null;
}

// ═══════════════════════════════════════════════════════════════════════════
// PROMPT A/B TEST (mirrors PromptABTest entity + request DTOs)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * A/B test configuration for comparing two prompt registry versions.
 */
export interface PromptABTest {
  id: string;
  promptKey: string;
  name: string;
  /** Reference to the Variant A PromptRegistry (control). */
  variantAId: string;
  /** Reference to the Variant B PromptRegistry (challenger). */
  variantBId: string;
  /** Percentage of traffic routed to Variant B (0-100). */
  trafficSplitPercent: number;
  /** Lifecycle status: created / running / completed. */
  status: string;
  /** The winning variant after test completion. Null until completed. */
  winnerVariantId: string | null;
  startDate: string;
  endDate: string | null;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * Request to create a new A/B test.
 */
export interface CreateABTestRequest {
  name: string | null;
  variantAId: string;
  variantBId: string;
  trafficSplitPercent: number;
}

/**
 * Request to complete an A/B test by recording the winning variant.
 */
export interface CompleteABTestRequest {
  winnerVariantId: string;
}

// ═══════════════════════════════════════════════════════════════════════════
// PROVIDER DESCRIPTORS (mirrors AsrProviderDescriptor, TtsProviderDescriptor)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Describes an ASR (speech-to-text) provider's capabilities and constraints.
 */
export interface AsrProviderDescriptor {
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
  maxFileBytes: number | null;
  maxAudioDurationMs: number | null;
  acceptedMimeTypes: string[];
  sendsAudioOffDevice: boolean;
  storesProviderData: ProviderDataRetention;
  dataRegion: string | null;
  retentionPolicy: string | null;
  pricingUnit: string | null;
}

/**
 * Describes a TTS (text-to-speech) provider's capabilities and constraints.
 */
export interface TtsProviderDescriptor {
  providerId: string;
  modelId: string;
  executionModes: ExecutionMode[];
  credentialModes: CredentialMode[];
  supportedLanguages: string[];
  supportsStreaming: boolean;
  supportsBatch: boolean;
  supportsVoiceCloning: boolean;
  supportsSpeedControl: boolean;
  supportsPitchControl: boolean;
  outputFormats: string[];
  supportedSampleRates: number[];
  sendsAudioOffDevice: boolean;
  storesProviderData: ProviderDataRetention;
  dataRegion: string | null;
  retentionPolicy: string | null;
  pricingUnit: string | null;
}

// ═══════════════════════════════════════════════════════════════════════════
// ROUTING (mirrors AsrRoutingContext, RoutingDecision)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Context used by the audio policy router to resolve the best ASR provider.
 */
export interface AsrRoutingContext {
  dataClassification: DataClassification;
  preferredExecutionMode: ExecutionMode | null;
  preferredCredentialMode: CredentialMode | null;
  preferredProviderId: string | null;
  preferredModelId: string | null;
  language: string | null;
  enableVad: boolean;
  enableSpeakerDiarization: boolean;
  enablePunctuation: boolean;
  enableHotwords: boolean;
  enableWordTimestamp: boolean;
  fileSizeBytes: number;
  durationMs: number;
  mimeType: string;
  fallbackPolicy: string;
  userId: string | null;
  workspaceId: string | null;
  tenantId: string | null;
}

/**
 * Routing decision explanation for debugging and UI display.
 */
export interface RoutingDecision {
  selectedProviderId: string;
  selectedModelId: string;
  executionMode: string;
  credentialMode: string;
  steps: string[];
  eliminatedProviders: string[];
  fallbackReason: string | null;
}

// ═══════════════════════════════════════════════════════════════════════════
// COMMON DTOs (mirrors ProviderHealth, CostEstimate, VoiceProfile, etc.)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Health status of a provider.
 */
export interface ProviderHealth {
  providerId: string;
  isHealthy: boolean;
  latencyMs: number;
  statusMessage: string | null;
  checkedAt: string;
}

/**
 * Cost estimate for a provider invocation.
 */
export interface CostEstimate {
  providerId: string;
  modelId: string;
  pricingUnit: string;
  units: number;
  estimatedCost: number;
  currency: string;
}

/**
 * Voice profile for TTS providers.
 */
export interface VoiceProfile {
  voiceId: string;
  name: string;
  language: string | null;
  gender: string | null;
  previewUrl: string | null;
  isClonable: boolean;
  metadata: Record<string, unknown>;
}

/**
 * Audio chunk for streaming transcription.
 */
export interface AudioChunk {
  sessionId: string;
  data: Uint8Array;
  chunkIndex: number;
  isFinal: boolean;
  format: string;
  sampleRate: number;
}

/**
 * Partial transcription result from streaming ASR.
 */
export interface AsrPartialResult {
  sessionId: string;
  partialText: string;
  finalText: string | null;
  startMs: number | null;
  endMs: number | null;
  isFinal: boolean;
  segmentIndex: number;
}

/**
 * A single word with timing information from ASR.
 */
export interface AsrWord {
  startMs: number;
  endMs: number;
  text: string;
  confidence: number;
}

/**
 * A single ASR segment with timing, text, and confidence.
 */
export interface AsrSegment {
  segmentUuid: string;
  startMs: number;
  endMs: number;
  text: string;
  confidence: number;
  speakerKey: string | null;
  words: AsrWord[] | null;
  segmentIndex: number;
}

// ═══════════════════════════════════════════════════════════════════════════
// REQUEST / RESPONSE TYPES
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Request to create a transcription job for an existing audio asset.
 */
export interface CreateTranscriptionJobRequest {
  audioAssetId: string;
  language: string | null;
  enableVad: boolean;
  enableSpeakerDiarization: boolean;
  enablePunctuation: boolean;
  hotwords: string[] | null;
  dataClassification: DataClassification;
  preferredProviderId: string | null;
  preferredModelId: string | null;
  fallbackPolicy: string;
}

/**
 * Request to edit a transcription segment's text.
 */
export interface EditSegmentRequest {
  text: string;
}

/**
 * Response from creating a transcription job.
 */
export interface CreateJobResponse {
  jobId: string;
  status: string;
}

/**
 * Response from cancelling a transcription job.
 */
export interface CancelJobResponse {
  jobId: string;
  status: string;
}

/**
 * Full transcription status response including optional segments.
 */
export interface TranscriptionStatusResponse {
  jobId: string;
  status: string;
  errorMessage: string | null;
  segmentCount: number | null;
  providerId: string | null;
  modelId: string | null;
  estimatedCost: number | null;
  actualCost: number | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  segments: TranscriptionSegment[] | null;
}

/**
 * Response from editing a segment.
 */
export interface EditSegmentResponse {
  segmentId: string;
  segmentUuid: string;
  versionId: string;
  version: string;
  text: string;
}

/**
 * Response from merging a segment.
 */
export interface MergeSegmentResponse {
  segmentId: string;
  segmentUuid: string;
  mergedVersionId: string;
  mergedText: string;
  parentVersionId: string | null;
}

/**
 * Response from bulk merging all segments in a job.
 */
export interface MergeAllSegmentsResponse {
  jobId: string;
  totalSegments: number;
  merged: number;
  skipped: number;
  failed: number;
  results: Array<{
    segmentUuid: string;
    status: string;
    versionId?: string;
    error?: string;
  }>;
}

/**
 * Response from publishing a segment.
 */
export interface PublishSegmentResponse {
  segmentId: string;
  segmentUuid: string;
  publishedVersionId: string;
}

/**
 * TTS synthesis request.
 */
export interface TtsRequest {
  text: string;
  language: string | null;
  voiceId: string | null;
  speed: number;
  pitch: number;
  outputFormat: string;
  sampleRate: number;
  dataClassification: DataClassification;
  preferredExecutionMode: ExecutionMode | null;
  preferredCredentialMode: CredentialMode | null;
  preferredProviderId: string | null;
  preferredModelId: string | null;
  fallbackPolicy: string;
  userId: string | null;
  workspaceId: string | null;
  tenantId: string | null;
}

/**
 * TTS synthesis result.
 */
export interface TtsResult {
  providerId: string;
  modelId: string;
  outputFilePath: string;
  outputFormat: string;
  durationMs: number;
  fileSizeBytes: number;
  estimatedCost: number | null;
  voiceId: string | null;
  metadata: Record<string, unknown>;
}

/**
 * Audio upload request parameters (multipart form data).
 */
export interface AudioUploadParams {
  file: File | Blob;
  title: string | null;
  topicId: string | null;
  language: string | null;
  enableVad: boolean;
  enableSpeakerDiarization: boolean;
  enablePunctuation: boolean;
  hotwords: string[] | null;
  dataClassification: DataClassification;
  preferredProviderId: string | null;
  preferredModelId: string | null;
  fallbackPolicy: string;
  autoStart: boolean;
}

/**
 * Response from uploading an audio file.
 */
export interface AudioUploadResponse {
  audioAssetId: string;
  transcriptionJobId: string;
  status: string;
  estimatedDuration: string | null;
}

/**
 * Credential verification response.
 */
export interface VerifyCredentialResponse {
  credentialId: string;
  valid: boolean;
}

/**
 * Credential disable response.
 */
export interface DisableCredentialResponse {
  credentialId: string;
  status: string;
}

/**
 * Credential rotation response.
 */
export interface RotateCredentialResponse {
  credentialId: string;
  rotated: boolean;
}

/**
 * Model disable response.
 */
export interface DisableModelResponse {
  id: string;
  disabled: boolean;
}

// ═══════════════════════════════════════════════════════════════════════════
// MARKETPLACE (mirrors ProviderMarketplaceEntry + RateEntryRequest)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * A provider entry in the marketplace catalog.
 */
export interface ProviderMarketplaceEntry {
  id: string;
  name: string;
  description: string;
  providerId: string;
  capability: string;
  executionMode: string;
  credentialMode: string;
  supportedLanguages: string;
  pricingUnit: string;
  isOfficial: boolean;
  version: string;
  rating: number;
  installCount: number;
  isInstalled: boolean;
  authorName: string;
  authorUrl: string | null;
  tagsJson: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * Request to rate a marketplace entry.
 */
export interface RateEntryRequest {
  rating: number;
}

// ═══════════════════════════════════════════════════════════════════════════
// CORRECTION (mirrors CorrectionDictionary, CorrectionRequest, CorrectionResult)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Correction dictionary entry for post-ASR text correction.
 */
export interface CorrectionDictionaryEntry {
  id: string;
  workspaceId: string | null;
  originalText: string;
  correctedText: string;
  /** brand / person / term / abbreviation / homophone / custom */
  category: string;
  language: string | null;
  createdBy: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

/**
 * Request to add a correction dictionary entry.
 */
export interface AddCorrectionEntryRequest {
  workspaceId: string | null;
  original: string;
  corrected: string;
  category: string | null;
}

/**
 * Request to update a correction dictionary entry.
 */
export interface UpdateCorrectionEntryRequest {
  original: string | null;
  corrected: string | null;
  category: string | null;
  language: string | null;
  isActive: boolean | null;
}

/**
 * Request for post-ASR text correction.
 */
export interface CorrectionRequest {
  text: string;
  workspaceId: string | null;
  language: string | null;
  segmentUuids: string[] | null;
  context: string | null;
}

/**
 * Result of a post-ASR correction operation.
 */
export interface CorrectionResult {
  correctedText: string;
  changes: CorrectionChange[];
  appliedDictionaryEntries: number;
}

/**
 * A single correction applied to the text.
 */
export interface CorrectionChange {
  original: string;
  corrected: string;
  category: string;
}

// ═══════════════════════════════════════════════════════════════════════════
// LAN NODE (mirrors LanNode entity + RegisterLanNodeRequest)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * A LAN compute node that can execute audio capabilities.
 */
export interface LanNode {
  id: string;
  nodeName: string;
  endpointUrl: string;
  nodeStatus: string;
  /** Comma-separated capability identifiers. */
  capabilities: string;
  /** Comma-separated provider IDs available on the node. */
  providerIds: string;
  availableGpuMemory: number | null;
  cpuCores: number | null;
  lastHeartbeatAt: string | null;
  registeredAt: string;
  updatedAt: string;
}

/**
 * Request to register a LAN node.
 */
export interface RegisterLanNodeRequest {
  endpoint: string;
}

// ═══════════════════════════════════════════════════════════════════════════
// DEVICE CAPABILITY (mirrors DeviceCapabilityResult, DeviceCapabilityReport)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Result of device capability detection or client-reported capability analysis.
 */
export interface DeviceCapabilityResult {
  cpuCores: number;
  memoryMb: number;
  gpuAvailable: boolean;
  gpuName: string | null;
  availableStorageMb: number;
  thermalState: string;
  batteryLevel: number | null;
  supportsLocalAsr: boolean;
  supportsLocalTts: boolean;
  /** Recommended processing mode: "batch", "realtime", or "offline". */
  recommendedMode: string;
  recommendationReason: string;
}

/**
 * Client-reported device capability report.
 */
export interface DeviceCapabilityReport {
  cpuCores: number;
  memoryMb: number;
  gpuAvailable: boolean;
  gpuName: string | null;
  availableStorageMb: number;
  thermalState: string;
  batteryLevel: number | null;
  deviceModel: string | null;
  osVersion: string | null;
  appVersion: string | null;
}

// ═══════════════════════════════════════════════════════════════════════════
// SIGNALR HUB MESSAGE TYPES (mirrors TranscriptionHub.cs)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Request to start a streaming transcription session via SignalR.
 */
export interface StartSessionRequest {
  language: string | null;
  enablePunctuation: boolean;
  hotwords: string[] | null;
  preferredProviderId: string | null;
  sampleRate: number;
}

/**
 * Audio chunk message sent from the client to the TranscriptionHub.
 */
export interface AudioChunkMessage {
  chunkIndex: number;
  data: Uint8Array;
  format: string;
  sampleRate: number;
  isFinal: boolean;
}

/**
 * SessionStarted event payload from the hub.
 */
export interface SessionStartedEvent {
  sessionId: string;
  providerId: string;
  modelId: string;
  supportsStreaming: boolean;
  message?: string;
}

/**
 * PartialResult event payload from the hub.
 */
export interface PartialResultEvent {
  sessionId: string;
  partialText: string;
  finalText: string | null;
  startMs: number | null;
  endMs: number | null;
  isFinal: boolean;
  segmentIndex: number;
  speakerKey?: string;
  confidence?: number;
  segmentUuid?: string;
}

/**
 * TranscriptionComplete event payload from the hub.
 */
export interface TranscriptionCompleteEvent {
  sessionId: string;
  status: string;
  fullText?: string;
  language?: string;
  durationMs?: number;
  segmentCount?: number;
  providerId?: string;
  modelId?: string;
}

/**
 * ChunkReceived event payload from the hub.
 */
export interface ChunkReceivedEvent {
  chunkIndex: number;
  sessionId: string;
  totalBuffered: number;
}

/**
 * Error event payload from the hub.
 */
export interface HubErrorEvent {
  message: string;
}

/**
 * SessionEnded event payload from the hub.
 */
export interface SessionEndedEvent {
  sessionId: string;
}
