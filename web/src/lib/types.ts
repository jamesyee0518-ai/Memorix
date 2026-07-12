// ===== API 响应格式 =====

export interface ApiResponse<T> {
  success: boolean;
  data: T;
  traceId?: string;
}

export interface ApiError {
  success: false;
  error: {
    code: string;
    message: string;
  };
  traceId?: string;
}

// ===== 分页结果 =====

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ===== 认证 =====

export interface User {
  userId: string;
  email: string;
  nickname: string;
  avatarUrl?: string;
  planCode: string;
}

export interface LoginResponse {
  userId: string;
  email: string;
  nickname: string;
  avatarUrl?: string;
  planCode: string;
  token: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  nickname: string;
}

export interface RegisterResponse {
  userId: string;
  email: string;
  nickname: string;
  token: string;
}

// ===== 专题 =====

export interface Topic {
  id: string;
  name: string;
  description?: string;
  domain?: string;
  documentCount: number;
  pendingCount: number;
  failedCount: number;
  createdAt: string;
}

export interface TopicDetail {
  id: string;
  userId: string;
  name: string;
  description?: string;
  domain?: string;
  visibility: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  stats: {
    documentCount: number;
    pendingCount: number;
    failedCount: number;
    doneCount: number;
    totalCount: number;
  };
}

export interface TopicResponse {
  id: string;
  userId: string;
  name: string;
  description?: string;
  domain?: string;
  visibility: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface TopicCreateRequest {
  name: string;
  description?: string;
  domain?: string;
}

export interface TopicUpdateRequest {
  name?: string;
  description?: string;
  domain?: string;
}

// ===== 资料导入 =====

export type SourceType =
  | "url"
  | "text"
  | "pdf"
  | "markdown"
  | "text_file"
  | "word"
  | "spreadsheet"
  | "csv";

export type SourceStatus =
  | "pending"
  | "queued"
  | "saved"
  | "failed"
  | "archived";

export interface Source {
  id: string;
  topicId?: string;
  sourceType: SourceType;
  title?: string;
  url?: string;
  domain?: string;
  status: SourceStatus;
  errorMessage?: string;
  retryCount: number;
  importedAt: string;
  createdAt: string;
}

export interface SourceDetail {
  id: string;
  userId: string;
  topicId?: string;
  sourceType: SourceType;
  title?: string;
  url?: string;
  domain?: string;
  author?: string;
  publishedAt?: string;
  importedAt: string;
  originalFileId?: string;
  rawText?: string;
  contentHash?: string;
  status: SourceStatus;
  errorMessage?: string;
  retryCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface SourceListParams {
  topicId?: string;
  status?: SourceStatus;
  sourceType?: SourceType;
}

export interface UrlImportRequest {
  topicId: string;
  url: string;
  title?: string;
}

export interface TextImportRequest {
  topicId: string;
  title: string;
  content: string;
}

// ===== 文件 =====

export interface DownloadUrlResponse {
  url: string;
  expiresAt?: string;
}

// ===== 任务 =====

export interface Job {
  id: string;
  type?: string;
  status?: string;
  sourceId?: string;
  topicId?: string;
  createdAt?: string;
  updatedAt?: string;
}

// ===== 文档 =====

export interface DocumentListItem {
  id: string;
  sourceId: string;
  topicId?: string;
  title: string;
  summary?: string;
  aiStatus: string;
  valueScore?: number;
  qualityScore?: number;
  sourceType?: string;
  sourceDomain?: string;
  parseStatus?: string;
  cleanStatus?: string;
  indexStatus?: string;
  createdAt: string;
}

export interface DocumentTagItem {
  id: string;
  tagId?: string;
  name: string;
  type?: string;
  source?: string;
  confidence?: number;
  reason?: string;
  isConfirmed?: boolean;
  confirmedBy?: string;
  confirmedAt?: string;
  createdAt: string;
}

export interface DocumentEntityItem {
  id: string;
  entityId?: string;
  name: string;
  entityType: string;
  confidence?: number;
  mentionCount?: number;
  importance?: string;
  role?: string;
  sentiment?: string;
  firstMention?: string;
  mentionExamples?: string;
}

export interface DocumentDetail {
  id: string;
  sourceId: string;
  topicId?: string;
  title: string;
  contentMarkdown?: string;
  contentText?: string;
  language?: string;
  wordCount?: number;
  summary?: string;
  oneSentenceConclusion?: string;
  keyPoints?: string;
  businessSignals?: string;
  technicalSignals?: string;
  risks?: string;
  opportunities?: string;
  reusableMaterials?: string;
  valueScore?: number;
  qualityScore?: number;
  aiStatus: string;
  aiModel?: string;
  promptVersion?: string;
  processedAt?: string;

  // Phase 3: Source metadata
  sourceType?: string;
  sourceUrl?: string;
  sourceDomain?: string;
  author?: string;
  publishedAt?: string;
  recommendedTags?: string;

  // Phase 3: Scoring
  valueScoreReason?: string;
  shouldDeepProcess?: boolean;

  // Phase 3: Multi-stage status
  parseStatus?: string;
  cleanStatus?: string;
  chunkStatus?: string;
  indexStatus?: string;
  tagStatus?: string;
  entityStatus?: string;
  embeddingStatus?: string;

  // Phase 3: Parser metadata
  parserName?: string;
  parserVersion?: string;
  cleanerVersion?: string;

  // Phase 3: AI raw output
  aiRawOutput?: string;
  aiErrorMessage?: string;

  // Phase 3: Reading time
  readingTimeMinutes?: number;

  createdAt: string;
  updatedAt: string;
  tags: DocumentTagItem[];
  entities: DocumentEntityItem[];
}

// ===== 处理日志 =====

export interface ProcessingLogItem {
  id: string;
  sourceId?: string;
  documentId?: string;
  stepName: string;
  status: string;
  message?: string;
  errorCode?: string;
  startedAt?: string;
  finishedAt?: string;
  durationMs?: number;
  createdAt: string;
}

// ===== 处理状态 =====

export interface ProcessingStatusResponse {
  parseStatus: string;
  cleanStatus: string;
  aiStatus: string;
  chunkStatus: string;
  indexStatus: string;
  aiErrorMessage?: string;
}

// ===== 实体 =====

export interface EntityListItem {
  id: string;
  name: string;
  entityType: string;
  description?: string;
  aliases?: string;
  source?: string;
  usageCount?: number;
  documentCount: number;
  isVerified?: boolean;
  isArchived?: boolean;
}

export interface EntityDetail {
  id: string;
  name: string;
  normalizedName?: string;
  entityType: string;
  description?: string;
  aliases?: string;
  source?: string;
  usageCount?: number;
  isVerified?: boolean;
  isArchived?: boolean;
  metadata?: string;
  createdAt: string;
  updatedAt: string;
  relatedDocuments: { id: string; title: string; aiStatus: string }[];
}

// ===== 标签 =====

export interface Tag {
  id: string;
  workspaceId?: string;
  name: string;
  normalizedName?: string;
  displayName?: string;
  type?: string;
  tagType?: string;
  description?: string;
  color?: string;
  aliases?: string;
  source?: string;
  usageCount?: number;
  documentCount?: number;
  isSystem?: boolean;
  isArchived?: boolean;
  createdAt: string;
  updatedAt?: string;
}

// ===== AI任务 =====

export interface AiJobListItem {
  id: string;
  jobType: string;
  targetType: string;
  targetId: string;
  status: string;
  model?: string;
  inputTokens?: number;
  outputTokens?: number;
  errorMessage?: string;
  retryCount: number;
  createdAt: string;
  startedAt?: string;
  finishedAt?: string;
}

// ===== 搜索 =====

export interface SearchFilters {
  sourceTypes?: string[];
  tagIds?: string[];
  dateFrom?: string;
  dateTo?: string;
  minValueScore?: number;
}

export interface SearchRequest {
  topicId?: string;
  query: string;
  searchType: "keyword" | "vector" | "hybrid";
  filters?: SearchFilters;
  limit?: number;
}

export interface ScoreDetail {
  keywordScore: number;
  vectorScore: number;
  freshnessScore: number;
  valueScore: number;
  metadataScore?: number;
}

export interface SearchResultItem {
  documentId: string;
  chunkId: string;
  title: string;
  snippet: string;
  sourceType?: string;
  sourceUrl?: string;
  sourceDomain?: string;
  publishedAt?: string;
  valueScore?: number;
  score: number;
  scoreDetail?: ScoreDetail;
}

export interface SearchResult {
  query: string;
  searchType: string;
  total: number;
  items: SearchResultItem[];
}

// ===== 问答 =====

export interface QaSession {
  id: string;
  topicId?: string;
  title?: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface Citation {
  index: number;
  documentId: string;
  chunkId: string;
  title: string;
  sourceUrl?: string;
  snippet: string;
}

export interface RetrievalInfo {
  retrievedCount: number;
  usedCount: number;
}

export interface QaAnswerResponse {
  answer: string;
  citations: Citation[];
  retrieval: RetrievalInfo;
  messageId: string;
  confidence?: number;
  debugInfo?: {
    queryPlan?: string;
    contextTokens?: number;
    retrievedTitles?: string[];
    systemPrompt?: string;
  };
}

export interface QaMessage {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  citations?: Citation[];
  createdAt: string;
}

// ===== 报告 =====

export type ReportType = "daily" | "weekly" | "topic";
export type ReportStatus = "pending" | "processing" | "done" | "failed" | "archived";

export interface ReportListItem {
  id: string;
  topicId?: string;
  reportType: ReportType;
  title: string;
  startDate?: string;
  endDate?: string;
  status: ReportStatus;
  qualityScore?: number;
  createdAt: string;
}

export interface ReportCitation {
  index: number;
  documentId: string;
  title: string;
  sourceUrl?: string;
  snippet: string;
}

export interface ReportDetail {
  id: string;
  topicId?: string;
  reportType: ReportType;
  title: string;
  contentMarkdown: string;
  query?: string;
  startDate?: string;
  endDate?: string;
  citations: ReportCitation[];
  sourceDocumentIds: string[];
  status: ReportStatus;
  qualityScore?: number;
  generatedByModel?: string;
  createdAt: string;
}

export interface CreateReportResponse {
  reportJobId: string;
  status: string;
}

// ===== 导出 =====

export type ExportType = "markdown" | "obsidian" | "json";
export type ExportStatus = "pending" | "processing" | "done" | "failed";

export interface ExportJobResponse {
  exportJobId: string;
  status: string;
}

export interface ExportJobDetail {
  id: string;
  exportType: ExportType;
  targetType: string;
  status: ExportStatus;
  fileId?: string;
  downloadUrl?: string;
  createdAt: string;
}

/** 导出任务列表项（导出历史） */
export interface ExportJobItem {
  id: string;
  exportType: ExportType;
  targetType: string;
  status: ExportStatus;
  fileName?: string;
  downloadUrl?: string;
  errorMessage?: string;
  createdAt: string;
  finishedAt?: string;
}

/** 导出历史列表查询参数 */
export interface ExportHistoryParams {
  status?: ExportStatus;
  page?: number;
  pageSize?: number;
}

/** 报告任务状态（轮询报告生成进度） */
export interface ReportJobStatus {
  id: string;
  status: string;
  progress: number;
  currentStep?: string;
  reportId?: string;
  errorMessage?: string;
}

/** 报告更新请求体 */
export interface UpdateReportInput {
  title?: string;
  contentMarkdown?: string;
}

// ===== API Key =====

export type PermissionScope = "search_only" | "qa_only" | "full_read";
export type ApiKeyStatus = "active" | "disabled";

export interface CreateApiKeyRequest {
  name: string;
  permissionScope: PermissionScope;
  allowedTopicIds?: string[];
  allowedActions?: string[];
  expiresAt?: string;
}

export interface CreateApiKeyResponse {
  id: string;
  name: string;
  apiKey: string;
  keyPrefix: string;
  permissionScope: PermissionScope;
  allowedTopicIds?: string[];
  expiresAt?: string;
}

export interface ApiKeyListItem {
  id: string;
  name: string;
  keyPrefix: string;
  permissionScope: PermissionScope;
  allowedTopicIds?: string[];
  status: ApiKeyStatus;
  lastUsedAt?: string;
  createdAt: string;
  expiresAt?: string;
}

// ===== 反馈 =====

export type FeedbackType =
  | "bug"
  | "ux"
  | "feature"
  | "quality"
  | "performance"
  | "pricing"
  | "general"
  | "qa_feedback";
export type FeedbackSeverity = "critical" | "high" | "medium" | "low" | "normal";
export type FeedbackStatus = "open" | "in_progress" | "resolved" | "closed";

export interface CreateFeedbackRequest {
  feedbackType: FeedbackType;
  module?: string;
  severity?: FeedbackSeverity;
  title: string;
  content?: string;
  relatedEntityType?: string;
  relatedEntityId?: string;
}

export interface FeedbackResponse {
  feedbackId: string;
  status: string;
}

export interface FeedbackListItem {
  id: string;
  feedbackType: FeedbackType;
  module?: string;
  severity?: FeedbackSeverity;
  title: string;
  content?: string;
  status: FeedbackStatus;
  priority: string;
  createdAt: string;
}

/** 反馈（管理端完整视图） */
export interface Feedback {
  id: string;
  userId: string;
  betaUserId: string | null;
  feedbackType: string;
  module: string | null;
  severity: string;
  title: string;
  content: string;
  relatedEntityType: string | null;
  relatedEntityId: string | null;
  status: string;
  priority: string;
  createdAt: string;
  updatedAt: string;
}

/** 反馈统计 */
export interface FeedbackStats {
  total: number;
  open: number;
  inProgress: number;
  resolved: number;
  closed: number;
}

// ===== 内测用户 (Beta User) =====

export type BetaUserStatus = "invited" | "activated" | "paused" | "churned" | "blocked";

export interface BetaUser {
  id: string;
  userId: string | null;
  email: string;
  name: string | null;
  userType: string;
  betaGroup: string | null;
  inviteCode: string | null;
  status: BetaUserStatus;
  onboardedAt: string | null;
  lastFeedbackAt: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

/** 邀请内测用户请求 */
export interface InviteBetaUserInput {
  email: string;
  name?: string;
  betaGroup?: string;
  platform?: string;
}

/** 更新内测用户请求 */
export interface UpdateBetaUserInput {
  status?: BetaUserStatus;
  notes?: string;
  betaGroup?: string;
}

// ===== 版本发布说明 (Release Note) =====

export type ReleaseNoteChannel = "alpha" | "beta" | "rc" | "stable";

export interface ReleaseNote {
  id: string;
  version: string;
  title: string;
  channel: ReleaseNoteChannel;
  contentMarkdown: string;
  highlights: string[] | null;
  knownIssues: string[] | null;
  isPublished: boolean;
  publishedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

/** 创建/更新版本发布说明请求 */
export interface ReleaseNoteInput {
  version: string;
  title: string;
  channel: ReleaseNoteChannel;
  contentMarkdown: string;
  highlights?: string[];
  knownIssues?: string[];
  isPublished?: boolean;
}

// ===== 使用量 =====

export interface UsageDaily {
  importedCount: number;
  documentCount: number;
  searchCount: number;
  qaCount: number;
  reportCount: number;
  exportCount: number;
  apiCallCount: number;
  agentCallCount: number;
  agentSearchCount: number;
  agentQaCount: number;
  agentWriteCount: number;
  agentSuccessCount: number;
  agentFailedCount: number;
  inputTokens: number;
  outputTokens: number;
}

export interface UsageTrendItem {
  usageDate: string;
  searchCount: number;
  qaCount: number;
  reportCount: number;
  apiCallCount: number;
  agentCallCount: number;
}

export interface UsageResponse {
  today: UsageDaily;
  last7Days: UsageTrendItem[];
  totals: {
    documentCount: number;
    searchCount: number;
    qaCount: number;
    reportCount: number;
    apiCallCount: number;
    agentCallCount: number;
  };
}

// ===== 工作区 (Workspace) =====

export type WorkspaceMode = "local" | "cloud" | "hybrid";

export interface Workspace {
  id: string;
  name: string;
  mode: WorkspaceMode;
  storageProvider: string;
  fileProvider: string;
  jobProvider: string;
  modelProvider: string;
  localDbPath?: string;
  localVaultPath?: string;
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
  syncEnabled: boolean;
  inboxEnabled: boolean;
  modelConfig?: string;
  userId?: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateWorkspaceInput {
  name: string;
  mode: WorkspaceMode;
  storageProvider?: string;
  fileProvider?: string;
  jobProvider?: string;
  modelProvider?: string;
  localDbPath?: string;
  localVaultPath?: string;
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
  syncEnabled?: boolean;
  inboxEnabled?: boolean;
  modelConfig?: string;
}

export interface InitLocalWorkspaceInput {
  name: string;
  vaultPath: string;
  modelProvider?: string;
  modelConfig?: string;
}

export interface UpdateWorkspaceInput {
  name?: string;
  modelProvider?: string;
  modelConfig?: string;
  syncEnabled?: boolean;
  inboxEnabled?: boolean;
  localVaultPath?: string;
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
}

export type CloudInboxPullStrategy = "manual" | "onStartup" | "scheduled";
export type CloudInboxRetention = "keep" | "deleteOriginal" | "deleteAll";

export interface CloudInboxSettings {
  enabled: boolean;
  pullStrategy: CloudInboxPullStrategy;
  retention: CloudInboxRetention;
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
  syncEnabled: boolean;
}

export interface UpdateCloudInboxSettingsInput {
  enabled: boolean;
  pullStrategy: CloudInboxPullStrategy;
  retention: CloudInboxRetention;
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
}

export interface CloudInboxStatus {
  enabled: boolean;
  connected: boolean;
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
  lastPulledAt?: string;
  pendingRemoteCount: number;
}

export interface CloudInboxPullInput {
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
  authToken: string;
  retention: CloudInboxRetention;
}

export interface CloudInboxPullResult {
  pulledCount: number;
  failedCount: number;
  nextCursor?: string;
  pulledAt: string;
}

export interface CloudInboxSyncLog {
  id: string;
  workspaceId: string;
  direction: "pull" | string;
  status: "success" | "partial" | "failed";
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
  retention: CloudInboxRetention;
  pulledCount: number;
  failedCount: number;
  nextCursor?: string;
  errorMessage?: string;
  startedAt: string;
  finishedAt: string;
  durationMs: number;
  createdAt: string;
}

export interface MobileDevice {
  id: string;
  workspaceId: string;
  clientId: string;
  deviceName?: string;
  platform?: string;
  pushToken?: string;
  refreshTokenExpiresAt?: string;
  status: "active" | "revoked" | string;
  lastSeenAt?: string;
  boundAt: string;
  createdAt: string;
  updatedAt: string;
}

export interface PushNotification {
  id: string;
  workspaceId: string;
  clientId: string;
  pushToken: string;
  title: string;
  body: string;
  dataJson?: string;
  status: "pending" | "sent" | "failed" | string;
  attempt: number;
  maxAttempts: number;
  providerResponse?: string;
  errorMessage?: string;
  nextAttemptAt?: string;
  sentAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface WorkspaceModeOption {
  mode: WorkspaceMode;
  label: string;
  description: string;
  available: boolean;
}

export interface ModelProviderOption {
  provider: string;
  label: string;
  defaultBaseUrl?: string;
  requiresApiKey: boolean;
}

export interface LocalConfig {
  currentWorkspaceId: string;
  workspaces: Array<{
    id: string;
    name: string;
    mode: string;
    localDbPath?: string;
    localVaultPath?: string;
  }>;
  appVersion: string;
}

// ===== Runtime Health =====

export interface RuntimeHealth {
  database: string;
  fileStorage: string;
  jobQueue: string;
  llmService: string;
  embeddingService: string;
  ollama: string;
  lmStudio: string;
  cloudApi: string;
  overall: string;
  workspaceMode?: string;
  checkedAt: string;
}

export interface LocalModelDetection {
  ollama: LocalModelProviderDetection;
  lmStudio: LocalModelProviderDetection;
  checkedAt: string;
}

export interface LocalModelProviderDetection {
  available: boolean;
  status: string;
  endpoint: string;
}

// ===== Model Settings =====

export interface UpdateModelSettingsInput {
  provider: string;
  baseUrl?: string;
  apiKey?: string;
  chatModel?: string;
  embeddingModel?: string;
}

export interface ModelTestResult {
  status: string;
  provider: string;
  chatModel?: string;
  embeddingModel?: string;
  error?: string;
}

// ===== Inbox Items =====

export interface InboxAttachment {
  id: string;
  workspaceId: string;
  inboxItemId: string;
  fileId: string;
  role: string;
  filename: string;
  mimeType: string;
  sizeBytes: number;
  createdAt: string;
}

export interface FileObject {
  id: string;
  workspaceId: string;
  storageProvider: string;
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

export interface ImportJob {
  id: string;
  workspaceId: string;
  inboxItemId: string;
  sourceId?: string;
  jobType: string;
  status: string;
  attempt: number;
  maxAttempts: number;
  startedAt?: string;
  finishedAt?: string;
  errorCode?: string;
  errorMessage?: string;
  createdAt: string;
  updatedAt: string;
}

export interface InboxEvent {
  id: string;
  workspaceId: string;
  inboxItemId: string;
  eventType: string;
  eventPayload?: string;
  createdBy?: string;
  createdAt: string;
}

export interface InboxItem {
  id: string;
  workspaceId: string;
  userId?: string;
  topicId?: string;
  inputType: string;      // "text" | "url" | "file" | "mixed"
  itemType: string;       // legacy alias
  title?: string;
  contentText?: string;
  sourceUrl?: string;
  filePath?: string;
  status: string;         // "pending" | "imported" | "processing" | "done" | "failed" | "archived"
  suggestedTopicId?: string;
  suggestedTitle?: string;
  suggestedTags?: string[];
  createdFrom?: string;
  originDeviceId?: string;
  originClientVersion?: string;
  sourceId?: string;
  errorCode?: string;
  errorMessage?: string;
  fileId?: string;
  fileName?: string;
  fileSize?: number;
  processedAt?: string;
  retryCount: number;
  createdAt: string;
  updatedAt: string;
  importedAt?: string;
  archivedAt?: string;
  attachments?: InboxAttachment[];
}

export interface CreateInboxItemInput {
  inputType?: string;
  title?: string;
  contentText?: string;
  sourceUrl?: string;
  topicId?: string;
  createdFrom?: string;
  originDeviceId?: string;
  originClientVersion?: string;
}

export interface CreateInboxUrlInput {
  sourceUrl: string;
  title?: string;
  topicId?: string;
}

export interface CreateInboxTextInput {
  title: string;
  contentText: string;
  topicId?: string;
}

export interface UpdateInboxItemInput {
  title?: string;
  contentText?: string;
  sourceUrl?: string;
  topicId?: string;
  itemType?: string;
  suggestedTopicId?: string;
  suggestedTitle?: string;
  suggestedTags?: string[];
}

export interface BatchImportInput {
  inboxItemIds: string[];
  topicId?: string;
}

export interface BatchArchiveInput {
  inboxItemIds: string[];
}

export interface InboxListParams {
  status?: string;
  inputType?: string;
  topicId?: string;
  limit?: number;
  offset?: number;
}

// ===== 分块 (Chunk) =====

export interface DocumentChunkItem {
  id: string;
  documentId: string;
  chunkIndex: number;
  chunkUid?: string;
  chunkTitle?: string;
  headingPath?: string;
  sectionLevel?: number;
  content: string;
  contentMarkdown?: string;
  contentHash?: string;
  tokenCount?: number;
  charCount?: number;
  startOffset?: number;
  endOffset?: number;
  prevChunkId?: string;
  nextChunkId?: string;
  embeddingStatus: string;
  embeddingModel?: string;
  indexStatus?: string;
  metadata?: string;
  createdAt: string;
  updatedAt?: string;
}

// ===== Chunk Embedding =====

export interface ChunkEmbeddingInfo {
  id: string;
  chunkId: string;
  provider: string;
  model: string;
  modelVersion?: string;
  dimension?: number;
  status: string;
  errorMessage?: string;
  retryCount: number;
  chunkContentHash?: string;
  createdAt: string;
  updatedAt?: string;
}

// ===== 向量索引状态 =====

export interface VectorIndexState {
  id: string;
  workspaceId: string;
  provider: string;
  model: string;
  dimension?: number;
  indexBackend: string;
  totalChunks: number;
  indexedChunks: number;
  failedChunks: number;
  staleChunks: number;
  status: string;
  lastRebuiltAt?: string;
  createdAt: string;
  updatedAt?: string;
}

// ===== Agent Profile =====

export interface AgentProfile {
  id: string;
  name: string;
  description?: string;
  allowedToolNames?: string[];
  allowedTopicIds?: string[];
  allowSensitiveDocuments: boolean;
  maxResultsPerCall: number;
  rateLimitPerMinute: number;
  dailyQuota: number;
  apiKeyId?: string;
  transport: string;
  mcpServerPath?: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface AgentInvocationLog {
  id: string;
  agentProfileId?: string;
  transport: string;
  toolName: string;
  status: string;
  resultCount?: number;
  latencyMs: number;
  errorCode?: string;
  errorMessage?: string;
  createdAt: string;
}

export interface McpConfig {
  mcpServers: {
    memorix: {
      command: string;
      args?: string[];
      env?: Record<string, string>;
    };
  };
}

export interface AgentToolDefinition {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
}
