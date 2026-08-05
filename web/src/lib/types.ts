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
  role: "platform_admin" | "operator" | "support" | "user";
}

export interface LoginResponse {
  userId: string;
  email: string;
  nickname: string;
  avatarUrl?: string;
  planCode: string;
  role: "platform_admin" | "operator" | "support" | "user";
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
  role: "platform_admin" | "operator" | "support" | "user";
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
  page?: number;
  pageSize?: number;
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
  titleOriginal?: string;
  titleZh?: string;
  summaryZh?: string;
  aiStatus: string;
  valueScore?: number;
  qualityScore?: number;
  sourceType?: string;
  sourceDomain?: string;
  parseStatus?: string;
  cleanStatus?: string;
  indexStatus?: string;
  primaryLanguage?: string;
  isMultilingual?: boolean;
  localizationLevel?: string;
  languageDetectStatus?: string;
  localizationStatus?: string;
  localizedAt?: string;
  localizationQualityScore?: number;
  localizationQualityIssues?: string;
  glossaryVersion?: string;
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
  titleOriginal?: string;
  titleZh?: string;
  summaryZh?: string;
  keywordsZh?: string;
  primaryLanguage?: string;
  languageDistribution?: string;
  isMultilingual?: boolean;
  localizationStrategy?: string;
  localizationLevel?: string;
  languageDetectStatus?: string;
  localizationStatus?: string;
  enrichmentStatus?: string;
  fulltextIndexStatus?: string;
  localizationModel?: string;
  localizationPromptVersion?: string;
  localizedAt?: string;
  localizationQualityScore?: number;
  localizationQualityIssues?: string;
  glossaryVersion?: string;
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
  workspaceId: string;
  name: string;
  canonicalName?: string;
  preferredNameZh?: string;
  preferredNameEn?: string;
  abbreviation?: string;
  entityType: string;
  status: string;
  confidence?: number;
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
  userId: string;
  workspaceId: string;
  name: string;
  canonicalName?: string;
  preferredNameZh?: string;
  preferredNameEn?: string;
  abbreviation?: string;
  normalizedName?: string;
  normalizedKey?: string;
  normalizationVersion: string;
  rowVersion: number;
  entityType: string;
  status: string;
  mergedIntoId?: string;
  redirectedFrom?: string;
  confidence?: number;
  sourceCount: number;
  mentionCount: number;
  description?: string;
  aliases: EntityAlias[];
  source?: string;
  usageCount?: number;
  isVerified?: boolean;
  isArchived?: boolean;
  metadata?: string;
  createdAt: string;
  updatedAt: string;
  relatedDocuments: Array<{
    documentId: string;
    title: string;
    mentionCount: number;
    confidence?: number;
    evidence?: string;
  }>;
}

export interface EntityAlias {
  id: string;
  alias: string;
  normalizedAlias: string;
  languageCode?: string;
  aliasType: string;
  sourceType: string;
  confidence?: number;
  isVerified: boolean;
}

export interface EntityGraphNode {
  id: string;
  label: string;
  canonicalName: string;
  entityType: string;
  mentionCount: number;
  sourceCount: number;
  degree: number;
  documentIds: string[];
}

export interface EntityGraphEdge {
  sourceEntityId: string;
  targetEntityId: string;
  relationType: string;
  weight: number;
  evidenceDocumentCount: number;
  evidenceDocumentIds: string[];
}

export interface EntityGraph {
  nodes: EntityGraphNode[];
  edges: EntityGraphEdge[];
  documentCount: number;
}

export interface EntityGraphDocument {
  documentId: string;
  title: string;
  originalMention?: string;
  displayEntityName: string;
  mentionCount: number;
  evidence?: string;
}

export interface EntityGovernanceTask {
  id: string;
  workspaceId: string;
  taskType: string;
  parentTaskId?: string;
  subjectEntityId?: string;
  candidateEntityId?: string;
  mentionId?: string;
  status: string;
  priority: number;
  cursor?: string;
  totalItems: number;
  processedItems: number;
  succeededItems: number;
  failedItems: number;
  score?: number;
  reasonCodes: string[];
  errorMessage?: string;
  retryCount: number;
  createdAt: string;
  updatedAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface EntityMergePreview {
  sourceEntityId: string;
  targetEntityId: string;
  recommendationReason: string;
  sourceVersion: number;
  targetVersion: number;
  mentionCount: number;
  aliasCount: number;
  externalIdCount: number;
  documentAssociationCount: number;
  relationCount: number;
  aliasConflictCount: number;
  externalIdConflictCount: number;
  selfLoopCount: number;
  hardBlocks: string[];
  affectedIndexes: string[];
  estimatedMilliseconds: number;
  canExecute: boolean;
}

export interface EntityMergeHistoryItem {
  mergeId: string;
  workspaceId: string;
  sourceEntityId: string;
  targetEntityId: string;
  reason: string;
  method: string;
  score?: number;
  operatorId?: string;
  status: string;
  createdAt: string;
  completedAt?: string;
  revertedAt?: string;
}

export interface EntityQualityMetrics {
  workspaceId?: string;
  activeEntityCount: number;
  mergedEntityCount: number;
  aliasCount: number;
  mentionCount: number;
  linkedMentionCount: number;
  unresolvedMentionCount: number;
  mentionLinkRate: number;
  unresolvedRate: number;
  pendingReviewCount: number;
  duplicateCandidateCount: number;
  completedMergeCount: number;
  revertedMergeCount: number;
  mergeRevertRate: number;
  estimatedDuplicateRate: number;
  pendingOutboxCount: number;
  failedOutboxCount: number;
  oldestPendingOutboxSeconds?: number;
  entityTypeDistribution: Record<string, number>;
  normalizationVersionDistribution: Record<string, number>;
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

export interface Terminology {
  id: string;
  workspaceId?: string;
  sourceLanguage: string;
  sourceTerm: string;
  targetLanguage: string;
  targetTerm: string;
  aliases?: string;
  domain?: string;
  priority: number;
  reviewStatus: string;
  version: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface TerminologyStats {
  total: number;
  approved: number;
  pendingReview: number;
  rejected: number;
  conflicts: number;
  pendingReprocessJobs: number;
  domains: Record<string, number>;
  languagePairs: Record<string, number>;
}

export interface TerminologyCandidate {
  sourceTerm: string;
  sourceLanguage: string;
  targetLanguage: string;
  suggestedTargetTerm?: string;
  domain?: string;
  occurrences: number;
  documentIds: string[];
}

export interface TerminologyBulkResult {
  created: number;
  updated: number;
  skipped: number;
  reprocessJobsQueued: number;
  errors: string[];
}

export interface TerminologyConflict {
  sourceLanguage: string;
  sourceTerm: string;
  targetLanguage: string;
  terms: Terminology[];
}

export interface TerminologyUsage {
  terminologyId: string;
  documentCount: number;
  chunkCount: number;
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
  language?: string;
  evidenceMode?: "original" | "bilingual";
  fusionMode?: "rrf" | "linear";
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
  fusionScore?: number;
  matchChannels?: string[];
  titleOriginal?: string;
  titleZh?: string;
  originalSnippet?: string;
  localizedSnippet?: string;
  contentLanguage?: string;
  displayContentSource?: string;
  localizationId?: string;
  translationType?: string;
  reviewStatus?: string;
  chunkGroupId?: string;
  section?: string;
  pageStart?: number;
  pageEnd?: number;
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
  sourceDomain?: string;
  sourceType?: string;
  snippet: string;
  score?: number;
  titleOriginal?: string;
  titleZh?: string;
  displaySnippet?: string;
  originalSnippet?: string;
  contentLanguage?: string;
  displayContentSource?: string;
  localizationId?: string;
  translationType?: string;
  reviewStatus?: string;
  chunkGroupId?: string;
  section?: string;
  pageStart?: number;
  pageEnd?: number;
  entities?: Array<{
    entityId: string;
    preferredName: string;
    originalMention: string;
  }>;
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
    originalQuery?: string;
    completedQuery?: string;
    contextTokens?: number;
    retrievedTitles?: string[];
    systemPrompt?: string;
    embeddingDiagnostics?: {
      eligibleChunkCount: number;
      totalEmbeddingCount: number;
      doneCount: number;
      pendingCount: number;
      failedCount: number;
      staleCount: number;
      coverage: number;
      status: string;
      message?: string;
    };
    citationValidationIssues?: string[];
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
  is_financial_truth: false;
  source: "legacy_aggregate";
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

export interface BillingSummaryResponse {
  billingAccountId: string;
  workspaceId: string;
  currency: string;
  grantedCredits: number;
  consumedCredits: number;
  reservedCredits: number;
  availableCredits: number;
  actualAmount: number;
  isFinancialTruth: boolean;
  asOf: string;
}

export interface BillingOverviewResponse {
  billingAccountId: string;
  workspaceId: string;
  accountName: string;
  currency: string;
  grantedCredits: number;
  consumedCredits: number;
  reservedCredits: number;
  availableCredits: number;
  planAvailableCredits: number;
  topUpAvailableCredits: number;
  promotionAvailableCredits: number;
  monthCredits: number;
  monthAmount: number;
  pendingCredits: number;
  monthRequests: number;
  monthTokens: number;
  isFinancialTruth: boolean;
  paymentEnabled: boolean;
  asOf: string;
}

export interface BillingUsagePointResponse {
  date: string;
  credits: number;
  amount: number;
  requests: number;
  tokens: number;
}

export interface BillingUsageItemResponse {
  jobId: string;
  createdAt: string;
  jobType: string;
  model?: string | null;
  executionMode: string;
  billingMode: string;
  status: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  credits: number;
  amount: number;
  currency: string;
}

export interface BillingUsageResponse {
  billingAccountId: string;
  workspaceId: string;
  from: string;
  to: string;
  totalCredits: number;
  totalAmount: number;
  totalRequests: number;
  totalTokens: number;
  currency: string;
  isFinancialTruth: boolean;
  asOf: string;
  trend: BillingUsagePointResponse[];
  items: BillingUsageItemResponse[];
}

export interface BillingBillItemResponse {
  id: string;
  occurredAt: string;
  type: "CHARGE" | "RECHARGE" | string;
  title: string;
  reference: string;
  credits: number;
  amountMinor?: number | null;
  currency: string;
  status: string;
}

export interface BillingBillsResponse {
  billingAccountId: string;
  workspaceId: string;
  currency: string;
  isFinancialTruth: boolean;
  asOf: string;
  items: BillingBillItemResponse[];
}

export interface BillingPriceRuleResponse {
  meterType: string;
  providerId?: string | null;
  modelId?: string | null;
  unit: string;
  unitSize: number;
  creditRate: number;
  saleUnitPrice: number;
  currency: string;
}

export interface BillingPricingResponse {
  pricePlanVersionId?: string | null;
  planCode: string;
  version: number;
  currency: string;
  isShadowPricing: boolean;
  effectiveFrom?: string | null;
  rules: BillingPriceRuleResponse[];
}

export interface PaymentMethodResponse {
  channel: string;
  scene: string;
  displayName: string;
  enabled: boolean;
}

export interface RechargeProductResponse {
  id: string;
  code: string;
  displayName: string;
  description: string;
  currency: string;
  amountMinor: number;
  paidCredits: number;
  bonusCredits: number;
  bonusExpiresInDays?: number | null;
}

export interface RechargeCatalogResponse {
  paymentEnabled: boolean;
  methods: PaymentMethodResponse[];
  products: RechargeProductResponse[];
}

export interface CreateRechargeOrderInput {
  workspaceId: string;
  rechargeProductId: string;
  paymentChannel: string;
  paymentScene: string;
  idempotencyKey: string;
}

export interface RechargeOrderResponse {
  id: string;
  orderNo: string;
  billingAccountId: string;
  workspaceId: string;
  rechargeProductId: string;
  productName: string;
  channel: string;
  channelScene: string;
  currency: string;
  amountMinor: number;
  paidCredits: number;
  bonusCredits: number;
  status: string;
  paymentPayloadType?: string | null;
  paymentPayload?: string | null;
  providerTradeNo?: string | null;
  expiresAt: string;
  paidAt?: string | null;
  fulfilledAt?: string | null;
  createdAt: string;
}

export interface RechargeOrderListResponse {
  items: RechargeOrderResponse[];
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
  workerActive: boolean;
  isRunning: boolean;
  currentPullStartedAt?: string;
  nextPullAt?: string;
  consecutiveFailures: number;
  retryAt?: string;
  lastScheduleError?: string;
}

export interface CloudInboxPullInput {
  cloudApiBaseUrl?: string;
  cloudWorkspaceId?: string;
  authToken?: string;
  cloudAccountBindingId?: string;
  retention: CloudInboxRetention;
}

export interface CloudAccountBinding {
  id: string;
  localProfileId: string;
  cloudUserId: string;
  cloudApiBaseUrl: string;
  accountDisplayName?: string;
  accountEmailMasked?: string;
  bindingStatus: string;
  lastAuthenticatedAt?: string;
}

export interface CloudWorkspaceSummary {
  id: string;
  name: string;
  mode: string;
  role?: string;
}

export interface CloudWorkspaceDiscovery {
  workspaces: CloudWorkspaceSummary[];
  cloudApiVersion?: string;
  compatible: boolean;
  compatibilityMessage?: string;
  capabilities: string[];
}

export interface WorkspaceBinding {
  id: string;
  localWorkspaceId: string;
  cloudAccountBindingId: string;
  cloudWorkspaceId: string;
  syncMode: "none" | "inbox_only" | "full_sync" | string;
  bindingStatus: string;
  primaryDeviceId?: string;
  uploadOriginalFiles: boolean;
  conflictPolicy: string;
  lastInboxCursor?: string;
  lastSyncCursor?: string;
  lastSyncAt?: string;
}

export interface OAuthStartInput {
  authorizationEndpoint: string;
  tokenEndpoint: string;
  userInfoEndpoint?: string;
  clientId: string;
  redirectUri: string;
  scope?: string;
  cloudApiBaseUrl: string;
}

export interface OAuthStartResult {
  sessionId: string;
  authorizationUrl: string;
  expiresAt: string;
}

export interface OAuthStatus {
  status: "pending" | "completed" | "failed" | "expired";
  cloudAccountBindingId?: string;
  errorMessage?: string;
}

export interface CreateWorkspaceBindingInput {
  localWorkspaceId: string;
  cloudAccountBindingId: string;
  cloudWorkspaceId: string;
  syncMode: "none" | "inbox_only" | "full_sync";
  uploadOriginalFiles?: boolean;
  conflictPolicy?: "manual" | "local_wins" | "cloud_wins";
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
  status: "ready" | "beta" | "preview" | "coming_soon" | string;
  badge?: string;
  reason?: string;
  requiresAuthentication: boolean;
  minimumCloudApiVersion?: string;
}

export interface DesktopFeatureCapability {
  feature: string;
  available: boolean;
  status: "ready" | "beta" | "preview" | "coming_soon" | string;
  badge?: string;
  reason?: string;
  requiresAuthentication: boolean;
}

export interface DesktopCapabilities {
  modes: WorkspaceModeOption[];
  cloudInbox: DesktopFeatureCapability;
  capabilityVersion: string;
  checkedAt: string;
}

export interface DesktopCloudConnectionStatus {
  status: "connected" | "account_connected" | "not_connected" | string;
  cloudAccountBindingId?: string;
  accountDisplayName?: string;
  accountEmailMasked?: string;
  cloudApiHost?: string;
  cloudWorkspaceId?: string;
  lastAuthenticatedAt?: string;
  requiresReauthentication: boolean;
}

export interface DesktopRuntimeState {
  localWorkspaceId?: string;
  workspaceName?: string;
  mode: WorkspaceMode | "unconfigured";
  routeTarget: "local" | "cloud_gateway" | "none" | string;
  connectionStatus: string;
  cloudWorkspaceId?: string;
  generation: number;
  localFallbackAllowed: boolean;
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

export interface WorkspaceRuntimeHealth {
  workspaceId?: string;
  workspaceName?: string;
  workspaceMode?: string;
  knowledgeStorage: string;
  fileStorage: string;
  backgroundProcessing: string;
  aiService: string;
  embeddingService: string;
  cloudSync: string;
  overall: string;
  issues: string[];
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
  contentOriginal?: string;
  contentNormalized?: string;
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
  detectedLanguage?: string;
  languageConfidence?: number;
  languageDistribution?: string;
  contentType?: string;
  processingRoute?: string;
  localizationRequired?: boolean;
  chunkGroupId?: string;
  parentChunkId?: string;
  paragraphStart?: number;
  paragraphEnd?: number;
  boundingBox?: string;
  pageStart?: number;
  pageEnd?: number;
  createdAt: string;
  updatedAt?: string;
}

export interface ChunkLocalization {
  id: string;
  chunkId: string;
  languageCode: string;
  headingLocalized?: string;
  contentLocalized: string;
  translationType: string;
  model?: string;
  promptVersion: string;
  glossaryVersion?: string;
  qualityScore?: number;
  qualityIssues?: string;
  reviewStatus: string;
  status: string;
  sourceContentHash: string;
  idempotencyKey: string;
  reviewedAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface ChunkEnrichment {
  id: string;
  chunkId: string;
  localizationId?: string;
  languageCode: string;
  summary?: string;
  keywords?: string;
  entities?: string;
  facts?: string;
  hypotheticalQuestions?: string;
  model?: string;
  promptVersion?: string;
  sourceContentHash: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface ChunkBatchResult {
  total: number;
  succeeded: number;
  failed: number;
  errors: string[];
}

export interface MultilingualBatchJob {
  id: string;
  documentId: string;
  jobType: "translate" | "enrich" | "multi_vector";
  status: "pending" | "running" | "paused" | "done" | "failed";
  totalItems: number;
  processedItems: number;
  succeededItems: number;
  failedItems: number;
  currentChunkId?: string;
  errorMessage?: string;
  retryCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface UpdateSafetyStatus {
  safeToInstall: boolean;
  activeJobs: number;
  breakdown: Record<string, number>;
  message: string;
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

// ===== Agent Memory =====

export interface AgentMemorySession {
  id: string;
  workspaceId: string;
  userId: string;
  agentProfileId?: string;
  externalSessionKey: string;
  taskTitle: string;
  status: "active" | "closed";
  startedAt: string;
  lastActiveAt: string;
  closedAt?: string;
  topicId?: string;
  projectId?: string;
}

export interface AgentMemoryItem {
  id: string;
  sessionId?: string;
  workspaceId: string;
  ownerUserId: string;
  agentProfileId?: string;
  kind: string;
  title: string;
  content?: string;
  summary?: string;
  admissionState: "candidate" | "qualified" | "confirmed" | "rejected";
  confidence: number;
  visibility: string;
  importance: number;
  freshnessAt?: string;
  status: "active" | "archived" | "forgotten";
  createdAt: string;
  evidence?: AgentMemoryEvidence[];
}

export interface AgentMemoryEvidence {
  id: string;
  memoryItemId: string;
  evidenceKind: string;
  referenceId: string;
  locator?: string;
  relation?: string;
  capturedAt: string;
}

export interface AgentMemoryFeedback {
  id: string;
  memoryItemId: string;
  userId: string;
  action: string;
  note?: string;
  createdAt: string;
}

export interface AgentMemoryAccessLog {
  id: string;
  memoryItemId?: string;
  sessionId?: string;
  agentProfileId?: string;
  action: string;
  traceId?: string;
  createdAt: string;
}

export interface AgentMemoryCheckpoint {
  id: string;
  sessionId: string;
  fromSequence: number;
  toSequence: number;
  summary?: string;
  openLoopsJson?: string;
  decisionsJson?: string;
  tokenEstimate: number;
  deliveryState: string;
  createdAt: string;
  version: number;
}

export interface CaptureMemoryInput {
  sessionId?: string;
  kind: string;
  title: string;
  content?: string;
  summary?: string;
  confidence?: number;
  visibility: string;
  importance: number;
  evidence?: Array<{
    evidenceKind: string;
    referenceId: string;
    locator?: string;
    relation?: string;
  }>;
}

export interface SearchMemoryInput {
  query: string;
  sessionId?: string;
  topicId?: string;
  kind?: string;
  admissionState?: string;
  limit?: number;
  offset?: number;
}

export interface ContextPackDto {
  sessionId: string;
  tokenBudget: number;
  tokenUsed: number;
  L1: ContextLayerDto[];
  L2: ContextLayerDto[];
  L3: ContextLayerDto[];
}

export interface ContextLayerDto {
  type: string;
  title: string;
  content?: string;
  confidence?: number;
  admissionState?: string;
  evidenceRef?: string;
}

export interface MemoryQualityMetrics {
  totalMemoryItems: number;
  confirmedItems: number;
  candidateItems: number;
  rejectedItems: number;
  recallRate: number;
  adoptionRate: number;
  rejectionRate: number;
  conflictCount: number;
  sanitizationHitRate: number;
  averageConfidence: number;
  p95LatencyMs: number;
  estimatedCostUsd: number;
  embeddingCount: number;
}

// ===== Agent Memory — Handoff, Ingest, Project (stages 1-5) =====

export interface AgentMemoryHandoff {
  id: string;
  projectId?: string;
  fromSessionId: string;
  toSessionId?: string;
  fromAgent: string;
  toAgent?: string;
  task: string;
  status: "open" | "in_progress" | "done" | "cancelled";
  contextRefs?: string[];
  gitBranch?: string;
  commitSha?: string;
  resultSummary?: string;
  createdAt: string;
  acceptedAt?: string;
  completedAt?: string;
}

export interface CreateHandoffInput {
  fromSessionId: string;
  toAgent?: string;
  task: string;
  contextRefs?: string[];
  gitBranch?: string;
  commitSha?: string;
}

export interface GetHandoffsInput {
  projectId?: string;
  toAgent?: string;
  status?: string;
  limit?: number;
}

export interface IngestEventBatch {
  agent: string;
  sessionId: string;
  gitRemote?: string;
  repoName?: string;
  gitBranch?: string;
  commitSha?: string;
  taskTitle?: string;
  events: NormalizedEvent[];
  sourceCursor?: string;
  checksum?: string;
}

export interface NormalizedEvent {
  eventType: string;
  timestamp: string;
  userPrompt?: string;
  aiResponse?: string;
  toolName?: string;
  toolInput?: unknown;
  toolResult?: string;
  filePath?: string;
  command?: string;
  commandOutput?: string;
  tokensTotal?: number;
}

export interface IngestResult {
  sessionId: string;
  turnsCreated: number;
  actionsCreated: number;
  eventsSkipped: number;
  projectId?: string;
  message?: string;
}

export interface AgentMemoryTurn {
  id: string;
  sessionId: string;
  seq: number;
  userMessage?: string;
  assistantMessage?: string;
  actionsCount: number;
  tokensTotal?: number;
  status: "active" | "completed";
  createdAt: string;
  actions?: AgentMemoryAction[];
}

export interface AgentMemoryAction {
  id: string;
  turnId: string;
  actionKind: string;
  toolName?: string;
  toolInputJson?: string;
  toolResult?: string;
  filePath?: string;
  command?: string;
  success: boolean;
  createdAt: string;
}

export interface Project {
  id: string;
  projectKey: string;
  repoName: string;
  gitRemote?: string;
  localRoot?: string;
  createdAt: string;
  updatedAt: string;
}
