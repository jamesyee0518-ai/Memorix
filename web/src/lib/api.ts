import axios, {
  type AxiosInstance,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from "axios";
import type {
  ApiResponse,
  LoginResponse,
  RegisterRequest,
  RegisterResponse,
  User,
  Topic,
  TopicDetail,
  TopicResponse,
  TopicCreateRequest,
  TopicUpdateRequest,
  Source,
  SourceDetail,
  SourceListParams,
  PagedResult,
  UrlImportRequest,
  TextImportRequest,
  DownloadUrlResponse,
  Job,
  DocumentListItem,
  DocumentDetail,
  DocumentTagItem,
  DocumentEntityItem,
  EntityListItem,
  EntityDetail,
  EntityAlias,
  EntityGraph,
  EntityGraphDocument,
  EntityGovernanceTask,
  EntityMergePreview,
  EntityMergeHistoryItem,
  EntityQualityMetrics,
  Tag,
  Terminology,
  TerminologyStats,
  TerminologyCandidate,
  TerminologyBulkResult,
  TerminologyConflict,
  TerminologyUsage,
  DocumentChunkItem,
  ChunkLocalization,
  ChunkEnrichment,
  MultilingualBatchJob,
  AiJobListItem,
  SearchRequest,
  SearchResult,
  QaSession,
  QaAnswerResponse,
  QaMessage,
  ReportListItem,
  ReportDetail,
  CreateReportResponse,
  ExportJobResponse,
  ExportJobDetail,
  ExportJobItem,
  ExportHistoryParams,
  ReportJobStatus,
  UpdateReportInput,
  SearchFilters,
  CreateApiKeyRequest,
  CreateApiKeyResponse,
  ApiKeyListItem,
  CreateFeedbackRequest,
  FeedbackResponse,
  FeedbackListItem,
  Feedback,
  FeedbackStats,
  BetaUser,
  InviteBetaUserInput,
  UpdateBetaUserInput,
  ReleaseNote,
  ReleaseNoteInput,
  UsageResponse,
  BillingSummaryResponse,
  BillingOverviewResponse,
  BillingUsageResponse,
  BillingBillsResponse,
  BillingPricingResponse,
  RechargeCatalogResponse,
  CreateRechargeOrderInput,
  RechargeOrderResponse,
  RechargeOrderListResponse,
  Workspace,
  CreateWorkspaceInput,
  InitLocalWorkspaceInput,
  UpdateWorkspaceInput,
  WorkspaceModeOption,
  DesktopCapabilities,
  ModelProviderOption,
  CloudInboxSettings,
  UpdateCloudInboxSettingsInput,
  CloudInboxStatus,
  CloudInboxPullInput,
  CloudInboxPullResult,
  CloudInboxSyncLog,
  CloudAccountBinding,
  CloudWorkspaceDiscovery,
  DesktopCloudConnectionStatus,
  DesktopRuntimeState,
  WorkspaceBinding,
  OAuthStartInput,
  OAuthStartResult,
  OAuthStatus,
  CreateWorkspaceBindingInput,
  MobileDevice,
  PushNotification,
  LocalConfig,
  RuntimeHealth,
  WorkspaceRuntimeHealth,
  UpdateSafetyStatus,
  LocalModelDetection,
  UpdateModelSettingsInput,
  ModelTestResult,
  InboxItem,
  UpdateInboxItemInput,
  InboxListParams,
  InboxAttachment,
  InboxEvent,
  ProcessingLogItem,
  ProcessingStatusResponse,
  VectorIndexState,
  ChunkEmbeddingInfo,
  AgentProfile,
  AgentInvocationLog,
  McpConfig,
  AgentToolDefinition,
  AgentMemorySession,
  AgentMemoryItem,
  AgentMemoryEvidence,
  AgentMemoryFeedback,
  AgentMemoryAccessLog,
  AgentMemoryCheckpoint,
  CaptureMemoryInput,
  SearchMemoryInput,
  ContextPackDto,
  MemoryQualityMetrics,
  AgentMemoryHandoff,
  CreateHandoffInput,
  GetHandoffsInput,
  IngestEventBatch,
  IngestResult,
  AgentMemoryTurn,
  Project,
  MeetingDto,
  MeetingSpeaker,
  MeetingMinutes,
  MeetingActionItem,
  RecordingSession,
  MeetingAsset,
  TranscriptDto,
  TranscriptSegment,
  ProcessingTaskStatus,
  AudioAssetDto,
  AudioUploadResponse,
  TranscriptionJobDto,
  TranscriptionSegmentDto,
  TranscriptionStatusResponse,
  AsrProviderDescriptor,
  TtsResult,
  TtsProviderDescriptor,
  VoiceProfile,
  ProviderHealth,
  ModelRegistry,
  RegisterModelRequest,
  CredentialDto,
  StoreCredentialRequest,
  PromptRegistry,
  CreatePromptRequest,
  CorrectionDictionaryDto,
  AddCorrectionEntryRequest,
  CorrectionResult,
  LanNode,
  RegisterLanNodeRequest,
  ProviderMarketplaceEntry,
  BenchmarkResult,
  RankingEntry,
} from "./types";

const currentPort =
  typeof window !== "undefined" ? Number(window.location.port) : Number.NaN;
const isDesktopPort = currentPort >= 43120 && currentPort <= 43218;
const configuredApiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL?.replace(/\/+$/, "");
export const API_BASE_URL = isDesktopPort
  ? `http://127.0.0.1:${currentPort + 1}/api`
  : configuredApiBaseUrl || "/api";
export const API_ORIGIN = API_BASE_URL.replace(/\/api$/, "");
const TOKEN_KEY = "access_token";

/** 获取 localStorage 中的 token */
export function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem(TOKEN_KEY);
}

/** 存储 token */
export function setToken(token: string): void {
  if (typeof window === "undefined") return;
  localStorage.setItem(TOKEN_KEY, token);
}

/** 清除 token */
export function clearToken(): void {
  if (typeof window === "undefined") return;
  localStorage.removeItem(TOKEN_KEY);
}

/** 自定义错误类 */
export class ApiRequestError extends Error {
  code: string;
  status: number;
  constructor(message: string, code: string, status: number) {
    super(message);
    this.name = "ApiRequestError";
    this.code = code;
    this.status = status;
  }
}

/** 创建 axios 实例 */
const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000,
  headers: {
    "Content-Type": "application/json",
  },
});

/** 请求拦截器：自动添加 Authorization header */
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = getToken();
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

/** 原始响应体（兼容成功和错误格式） */
interface RawApiResponse {
  success: boolean;
  data?: unknown;
  error?: { code: string; message: string };
  traceId?: string;
}

/** 响应拦截器：统一处理返回格式和错误 */
apiClient.interceptors.response.use(
  (response: AxiosResponse<RawApiResponse>) => {
    const body = response.data;
    if (body.success === false) {
      throw new ApiRequestError(
        body.error?.message || "请求失败",
        body.error?.code || "UNKNOWN",
        response.status
      );
    }
    return response;
  },
  (error) => {
    // 网络错误或超时
    if (!error.response) {
      if (error.code === "ECONNABORTED") {
        return Promise.reject(
          new ApiRequestError("请求超时，请稍后重试", "TIMEOUT", 0)
        );
      }
      return Promise.reject(
        new ApiRequestError("网络连接失败，请检查网络", "NETWORK_ERROR", 0)
      );
    }

    const { status, data, config } = error.response;

    // 401 未授权：登录请求返回错误；已有 token 时才强制回登录页。
    if (status === 401) {
      const requestUrl = config?.url ?? "";
      const isAuthLoginRequest = requestUrl.endsWith("/auth/login");
      if (isAuthLoginRequest && data && data.success === false) {
        return Promise.reject(
          new ApiRequestError(
            data.error?.message || "登录失败，请检查邮箱和密码",
            data.error?.code || "AUTH_ERROR",
            401
          )
        );
      }

      const hadToken = Boolean(getToken());
      clearToken();
      if (
        hadToken &&
        typeof window !== "undefined" &&
        !window.location.pathname.startsWith("/login")
      ) {
        window.location.href = "/login";
      }
      return Promise.reject(
        new ApiRequestError("登录已过期，请重新登录", "UNAUTHORIZED", 401)
      );
    }

    // 解析后端错误格式
    if (data && data.success === false) {
      return Promise.reject(
        new ApiRequestError(
          data.error?.message || "请求失败",
          data.error?.code || "UNKNOWN",
          status
        )
      );
    }

    // 其他 HTTP 错误
    const messageMap: Record<number, string> = {
      400: "请求参数错误",
      403: "没有权限执行此操作",
      404: "资源不存在",
      409: "资源已存在",
      422: "数据验证失败",
      429: "请求过于频繁，请稍后重试",
      500: "服务器内部错误",
      502: "网关错误",
      503: "服务暂时不可用",
    };

    return Promise.reject(
      new ApiRequestError(
        messageMap[status] || `请求失败 (${status})`,
        "HTTP_ERROR",
        status
      )
    );
  }
);

/** 通用请求方法：返回 data 部分 */
async function request<T>(config: Parameters<AxiosInstance["request"]>[0]): Promise<T> {
  const response = await apiClient.request<ApiResponse<T>>(config);
  return response.data.data;
}

// ===== 认证 API =====

export const authApi = {
  login(email: string, password: string): Promise<LoginResponse> {
    return request<LoginResponse>({
      method: "POST",
      url: "/auth/login",
      data: { email, password },
    });
  },

  register(data: RegisterRequest): Promise<RegisterResponse> {
    return request<RegisterResponse>({
      method: "POST",
      url: "/auth/register",
      data,
    });
  },

  me(): Promise<User> {
    return request<User>({
      method: "GET",
      url: "/auth/me",
    });
  },

  logout(): Promise<void> {
    return request<void>({
      method: "POST",
      url: "/auth/logout",
    });
  },
};

// ===== 专题 API =====

export const topicApi = {
  list(): Promise<PagedResult<Topic>> {
    return request<PagedResult<Topic>>({
      method: "GET",
      url: "/topics",
    });
  },

  get(id: string): Promise<TopicDetail> {
    return request<TopicDetail>({
      method: "GET",
      url: `/topics/${id}`,
    });
  },

  create(data: TopicCreateRequest): Promise<TopicResponse> {
    return request<TopicResponse>({
      method: "POST",
      url: "/topics",
      data,
    });
  },

  update(id: string, data: TopicUpdateRequest): Promise<TopicResponse> {
    return request<TopicResponse>({
      method: "PUT",
      url: `/topics/${id}`,
      data,
    });
  },

  delete(id: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/topics/${id}`,
    });
  },
};

// ===== 资料 API =====

export const sourceApi = {
  list(params?: SourceListParams): Promise<PagedResult<Source>> {
    return request<PagedResult<Source>>({
      method: "GET",
      url: "/sources",
      params,
    });
  },

  get(id: string): Promise<SourceDetail> {
    return request<SourceDetail>({
      method: "GET",
      url: `/sources/${id}`,
    });
  },

  importUrl(data: UrlImportRequest): Promise<Source> {
    return request<Source>({
      method: "POST",
      url: "/sources/url",
      data,
    });
  },

  importText(data: TextImportRequest): Promise<Source> {
    return request<Source>({
      method: "POST",
      url: "/sources/text",
      data,
    });
  },

  importFile(
    topicId: string,
    file: File,
    title?: string
  ): Promise<Source> {
    const formData = new FormData();
    formData.append("topicId", topicId);
    formData.append("file", file);
    if (title) {
      formData.append("title", title);
    }
    return request<Source>({
      method: "POST",
      url: "/sources/file",
      data: formData,
      headers: { "Content-Type": "multipart/form-data" },
    });
  },

  delete(id: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/sources/${id}`,
    });
  },

  retry(id: string): Promise<Source> {
    return request<Source>({
      method: "POST",
      url: `/sources/${id}/retry`,
    });
  },

  processSource(id: string): Promise<void> {
    return request<void>({
      method: "POST",
      url: `/sources/${id}/process`,
    });
  },
};

// ===== 文档 API =====

export const documentApi = {
  list(params?: {
    topicId?: string;
    aiStatus?: string;
    page?: number;
    pageSize?: number;
  }): Promise<PagedResult<DocumentListItem>> {
    return request<PagedResult<DocumentListItem>>({
      method: "GET",
      url: "/documents",
      params,
    });
  },

  get(id: string): Promise<DocumentDetail> {
    return request<DocumentDetail>({
      method: "GET",
      url: `/documents/${id}`,
    });
  },

  getEntities(id: string): Promise<EntityListItem[]> {
    return request<EntityListItem[]>({
      method: "GET",
      url: `/documents/${id}/entities`,
    });
  },

  getProcessingStatus(id: string): Promise<ProcessingStatusResponse> {
    return request<ProcessingStatusResponse>({
      method: "GET",
      url: `/documents/${id}/processing-status`,
    });
  },

  getProcessingLogs(id: string): Promise<ProcessingLogItem[]> {
    return request<ProcessingLogItem[]>({
      method: "GET",
      url: `/documents/${id}/processing-logs`,
    });
  },

  resummarize(id: string): Promise<boolean> {
    return request<boolean>({
      method: "POST",
      url: `/documents/${id}/resummarize`,
    });
  },
  updateLocalizedMetadata(id: string, data: { titleZh: string; summaryZh: string; keywordsZh?: string[]; approved?: boolean }): Promise<boolean> {
    return request<boolean>({ method: "PUT", url: `/documents/${id}/localized-metadata`, data });
  },
};

// ===== 实体 API =====

export const entityApi = {
  list(params?: {
    entityType?: string;
    search?: string;
    status?: string;
  }): Promise<PagedResult<EntityListItem>> {
    return request<PagedResult<EntityListItem>>({
      method: "GET",
      url: "/entities",
      params,
    });
  },

  get(id: string): Promise<EntityDetail> {
    return request<EntityDetail>({
      method: "GET",
      url: `/entities/${id}`,
    });
  },

  addAlias(id: string, data: {
    alias: string;
    languageCode?: string;
    aliasType?: string;
    isVerified?: boolean;
    confidence?: number;
  }): Promise<EntityAlias> {
    return request<EntityAlias>({
      method: "POST",
      url: `/entities/${id}/aliases`,
      data,
    });
  },

  deleteAlias(id: string, aliasId: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/entities/${id}/aliases/${aliasId}`,
    });
  },

  // Document entities
  getDocumentEntities(documentId: string): Promise<DocumentEntityItem[]> {
    return request<DocumentEntityItem[]>({
      method: "GET",
      url: `/documents/${documentId}/entities`,
    });
  },

  deleteDocumentEntity(documentId: string, entityId: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/documents/${documentId}/entities/${entityId}`,
    });
  },
};

export const knowledgeGraphApi = {
  getEntities(params?: {
    workspaceId?: string;
    entityType?: string;
    language?: string;
    limit?: number;
  }): Promise<EntityGraph> {
    return request<EntityGraph>({
      method: "GET",
      url: "/knowledgegraph/entities",
      params,
    });
  },

  getNeighbors(id: string, params?: { language?: string; limit?: number }): Promise<EntityGraph> {
    return request<EntityGraph>({
      method: "GET",
      url: `/knowledgegraph/entities/${id}/neighbors`,
      params,
    });
  },

  getDocuments(id: string, params?: { language?: string; limit?: number }): Promise<EntityGraphDocument[]> {
    return request<EntityGraphDocument[]>({
      method: "GET",
      url: `/knowledgegraph/entities/${id}/documents`,
      params,
    });
  },
};

export const entityGovernanceApi = {
  listTasks(params?: {
    workspaceId?: string;
    status?: string;
    taskType?: string;
    limit?: number;
  }): Promise<EntityGovernanceTask[]> {
    return request<EntityGovernanceTask[]>({
      method: "GET",
      url: "/entitygovernance/tasks",
      params,
    });
  },

  qualityMetrics(workspaceId?: string): Promise<EntityQualityMetrics> {
    return request<EntityQualityMetrics>({
      method: "GET",
      url: "/entitygovernance/quality-metrics",
      params: { workspaceId },
    });
  },

  startScan(data: {
    workspaceId: string;
    entityType?: string;
    batchSize?: number;
    idempotencyKey: string;
  }): Promise<EntityGovernanceTask> {
    return request<EntityGovernanceTask>({
      method: "POST",
      url: "/entityresolution/scan",
      data,
      headers: { "Idempotency-Key": data.idempotencyKey },
    });
  },

  controlTask(id: string, action: "pause" | "resume" | "retry"): Promise<EntityGovernanceTask> {
    return request<EntityGovernanceTask>({
      method: "POST",
      url: `/entityresolution/jobs/${id}/${action}`,
    });
  },

  decide(id: string, data: {
    decision: "MERGE" | "REJECT" | "BLOCK" | "DEFER";
    reason: string;
    idempotencyKey: string;
  }): Promise<EntityGovernanceTask> {
    return request<EntityGovernanceTask>({
      method: "POST",
      url: `/entitygovernance/tasks/${id}/decision`,
      data,
      headers: { "Idempotency-Key": data.idempotencyKey },
    });
  },

  startMaintenance(data: {
    workspaceId: string;
    operation: "ALIAS_MIGRATION" | "HISTORICAL_MENTION_BACKFILL" | "REDIRECT_COMPRESSION" | "ENTITY_REINDEX";
    batchSize?: number;
    idempotencyKey: string;
  }): Promise<EntityGovernanceTask> {
    return request<EntityGovernanceTask>({
      method: "POST",
      url: "/entitygovernance/maintenance",
      data,
      headers: { "Idempotency-Key": data.idempotencyKey },
    });
  },
};

export const entityMergeApi = {
  preview(data: {
    workspaceId: string;
    entityIdA: string;
    entityIdB: string;
  }): Promise<EntityMergePreview> {
    return request<EntityMergePreview>({
      method: "POST",
      url: "/entities/merge-preview",
      data,
    });
  },

  history(params?: { workspaceId?: string; limit?: number }): Promise<EntityMergeHistoryItem[]> {
    return request<EntityMergeHistoryItem[]>({
      method: "GET",
      url: "/entities/merge-history",
      params,
    });
  },

  revert(mergeId: string, idempotencyKey: string): Promise<unknown> {
    return request({
      method: "POST",
      url: `/entities/merges/${mergeId}/revert`,
      data: { idempotencyKey },
      headers: { "Idempotency-Key": idempotencyKey },
    });
  },
};

// ===== 标签 API =====

export const terminologyApi = {
  list(params?: {
    query?: string;
    sourceLanguage?: string;
    targetLanguage?: string;
    domain?: string;
    reviewStatus?: string;
    page?: number;
    pageSize?: number;
  }): Promise<PagedResult<Terminology>> {
    return request<PagedResult<Terminology>>({ method: "GET", url: "/terminology", params });
  },
  create(data: Omit<Terminology, "id" | "createdAt" | "updatedAt">): Promise<Terminology> {
    return request<Terminology>({ method: "POST", url: "/terminology", data });
  },
  update(id: string, data: Omit<Terminology, "id" | "createdAt" | "updatedAt">): Promise<Terminology> {
    return request<Terminology>({ method: "PUT", url: `/terminology/${id}`, data });
  },
  delete(id: string): Promise<boolean> {
    return request<boolean>({ method: "DELETE", url: `/terminology/${id}` });
  },
  bulk(items: Array<Omit<Terminology, "id" | "createdAt" | "updatedAt">>): Promise<TerminologyBulkResult> {
    return request<TerminologyBulkResult>({
      method: "POST", url: "/terminology/bulk", data: { items, skipConflicts: true },
    });
  },
  review(id: string, status: "draft" | "pending" | "approved" | "rejected"): Promise<Terminology> {
    return request<Terminology>({
      method: "POST", url: `/terminology/${id}/review`, data: { status },
    });
  },
  stats(): Promise<TerminologyStats> {
    return request<TerminologyStats>({ method: "GET", url: "/terminology/stats" });
  },
  conflicts(): Promise<TerminologyConflict[]> {
    return request<TerminologyConflict[]>({ method: "GET", url: "/terminology/conflicts" });
  },
  usage(terminologyIds: string[]): Promise<TerminologyUsage[]> {
    return request<TerminologyUsage[]>({
      method: "POST", url: "/terminology/usage", data: { terminologyIds },
    });
  },
  extract(data: { topicId?: string; documentLimit?: number; candidateLimit?: number }): Promise<TerminologyCandidate[]> {
    return request<TerminologyCandidate[]>({ method: "POST", url: "/terminology/extract", data });
  },
  async exportCsv(): Promise<Blob> {
    const response = await apiClient.get("/terminology/export", { responseType: "blob" });
    return response.data as Blob;
  },
};

export const tagApi = {
  async list(params?: { type?: string }): Promise<Tag[]> {
    const result = await request<PagedResult<Tag>>({
      method: "GET",
      url: "/tags",
      params: { ...params, pageSize: 100 },
    });
    return result.items;
  },

  get(id: string): Promise<Tag> {
    return request<Tag>({
      method: "GET",
      url: `/tags/${id}`,
    });
  },

  create(data: {
    name: string;
    type?: string;
    description?: string;
    color?: string;
  }): Promise<Tag> {
    return request<Tag>({
      method: "POST",
      url: "/tags",
      data,
    });
  },

  update(id: string, data: {
    name?: string;
    description?: string;
    color?: string;
    isArchived?: boolean;
  }): Promise<Tag> {
    return request<Tag>({
      method: "PUT",
      url: `/tags/${id}`,
      data,
    });
  },

  delete(id: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/tags/${id}`,
    });
  },

  // Document tags
  getDocumentTags(documentId: string): Promise<DocumentTagItem[]> {
    return request<DocumentTagItem[]>({
      method: "GET",
      url: `/documents/${documentId}/tags`,
    });
  },

  addDocumentTag(documentId: string, data: {
    name: string;
    type?: string;
    source?: string;
    confidence?: number;
  }): Promise<DocumentTagItem> {
    return request<DocumentTagItem>({
      method: "POST",
      url: `/documents/${documentId}/tags`,
      data,
    });
  },

  confirmDocumentTag(documentId: string, tagId: string): Promise<void> {
    return request<void>({
      method: "POST",
      url: `/documents/${documentId}/tags/${tagId}/confirm`,
    });
  },

  deleteDocumentTag(documentId: string, tagId: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/documents/${documentId}/tags/${tagId}`,
    });
  },
};

// ===== AI任务 API =====

export const aiJobApi = {
  list(params?: { status?: string }): Promise<PagedResult<AiJobListItem>> {
    return request<PagedResult<AiJobListItem>>({
      method: "GET",
      url: "/ai-jobs",
      params,
    });
  },
};

// ===== 文件 API =====

export const fileApi = {
  getDownloadUrl(fileId: string): Promise<DownloadUrlResponse> {
    return request<DownloadUrlResponse>({
      method: "GET",
      url: `/files/${fileId}/download-url`,
    });
  },
};

// ===== 任务 API =====

export const jobApi = {
  list(): Promise<Job[]> {
    return request<Job[]>({
      method: "GET",
      url: "/jobs",
    });
  },

  get(id: string): Promise<Job> {
    return request<Job>({
      method: "GET",
      url: `/jobs/${id}`,
    });
  },
};

// ===== 搜索 API =====

export const searchApi = {
  search(data: SearchRequest): Promise<SearchResult> {
    return request<SearchResult>({
      method: "POST",
      url: "/search",
      data,
    });
  },
};

// ===== 问答 API =====

export const qaApi = {
  createSession(data: {
    topicId: string;
    title?: string;
  }): Promise<QaSession> {
    return request<QaSession>({
      method: "POST",
      url: "/qa/sessions",
      data,
    });
  },

  getSessions(topicId?: string): Promise<PagedResult<QaSession>> {
    return request<PagedResult<QaSession>>({
      method: "GET",
      url: "/qa/sessions",
      params: { topicId },
    });
  },

  ask(data: {
    sessionId: string;
    topicId: string;
    query: string;
    retrieval?: { searchType: string; topK: number };
  }): Promise<QaAnswerResponse> {
    return request<QaAnswerResponse>({
      method: "POST",
      url: "/qa/ask",
      data,
    });
  },

  getMessages(sessionId: string): Promise<QaMessage[]> {
    return request<QaMessage[]>({
      method: "GET",
      url: `/qa/sessions/${sessionId}/messages`,
    });
  },

  deleteSession(sessionId: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/qa/sessions/${sessionId}`,
    });
  },
};

// ===== 报告 API =====

export const reportApi = {
  createDaily(data: {
    topicId: string;
    date: string;
  }): Promise<CreateReportResponse> {
    return request<CreateReportResponse>({
      method: "POST",
      url: "/reports/daily",
      data,
    });
  },

  createWeekly(data: {
    topicId: string;
    startDate: string;
    endDate: string;
  }): Promise<CreateReportResponse> {
    return request<CreateReportResponse>({
      method: "POST",
      url: "/reports/weekly",
      data,
    });
  },

  createTopic(data: {
    topicId: string;
    title: string;
    question: string;
    filters?: {
      dateFrom?: string;
      dateTo?: string;
      minValueScore?: number;
      tagIds?: string[];
      entityIds?: string[];
      sourceTypes?: string[];
    };
    depth?: string;
    language?: string;
    template?: string;
  }): Promise<CreateReportResponse> {
    return request<CreateReportResponse>({
      method: "POST",
      url: "/reports/topic",
      data,
    });
  },

  list(params?: {
    topicId?: string;
    reportType?: string;
    page?: number;
    pageSize?: number;
  }): Promise<PagedResult<ReportListItem>> {
    return request<PagedResult<ReportListItem>>({
      method: "GET",
      url: "/reports",
      params,
    });
  },

  get(id: string): Promise<ReportDetail> {
    return request<ReportDetail>({
      method: "GET",
      url: `/reports/${id}`,
    });
  },

  regenerate(id: string): Promise<CreateReportResponse> {
    return request<CreateReportResponse>({
      method: "POST",
      url: `/reports/${id}/regenerate`,
    });
  },

  update(reportId: string, data: UpdateReportInput): Promise<ReportDetail> {
    return request<ReportDetail>({
      method: "PUT",
      url: `/reports/${reportId}`,
      data,
    });
  },

  archive(reportId: string): Promise<void> {
    return request<void>({
      method: "POST",
      url: `/reports/${reportId}/archive`,
    });
  },

  delete(reportId: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/reports/${reportId}`,
    });
  },

  getJobStatus(jobId: string): Promise<ReportJobStatus> {
    return request<ReportJobStatus>({
      method: "GET",
      url: `/reports/jobs/${jobId}`,
    });
  },
};

// ===== 导出 API =====

export const exportApi = {
  documentMarkdown(data: {
    documentId: string;
    includeAiSummary?: boolean;
    includeMetadata?: boolean;
  }): Promise<ExportJobResponse> {
    return request<ExportJobResponse>({
      method: "POST",
      url: "/exports/document/markdown",
      data,
    });
  },

  reportMarkdown(data: { reportId: string }): Promise<ExportJobResponse> {
    return request<ExportJobResponse>({
      method: "POST",
      url: "/exports/report/markdown",
      data,
    });
  },

  topicObsidian(data: {
    topicId: string;
    includeDocuments?: boolean;
    includeReports?: boolean;
    includeAiSummary?: boolean;
  }): Promise<ExportJobResponse> {
    return request<ExportJobResponse>({
      method: "POST",
      url: "/exports/topic/obsidian",
      data,
    });
  },

  searchJson(data: {
    topicId?: string;
    query: string;
    filters?: SearchFilters;
  }): Promise<ExportJobResponse> {
    return request<ExportJobResponse>({
      method: "POST",
      url: "/exports/search/json",
      data,
    });
  },

  getJob(id: string): Promise<ExportJobDetail> {
    return request<ExportJobDetail>({
      method: "GET",
      url: `/exports/${id}`,
    });
  },

  reportJson(reportId: string): Promise<ExportJobResponse> {
    return request<ExportJobResponse>({
      method: "POST",
      url: "/exports/report/json",
      data: { reportId },
    });
  },

  getHistory(params?: ExportHistoryParams): Promise<PagedResult<ExportJobItem>> {
    return request<PagedResult<ExportJobItem>>({
      method: "GET",
      url: "/exports",
      params,
    });
  },

  openDirectory: async (jobId: string) => {
    return apiClient.post(`/exports/${jobId}/open-directory`);
  },
};

export { apiClient };

// ===== API Key API =====

export const apiKeyApi = {
  create(data: CreateApiKeyRequest): Promise<CreateApiKeyResponse> {
    return request<CreateApiKeyResponse>({
      method: "POST",
      url: "/api-keys",
      data,
    });
  },

  list(): Promise<ApiKeyListItem[]> {
    return request<ApiKeyListItem[]>({
      method: "GET",
      url: "/api-keys",
    });
  },

  disable(id: string): Promise<void> {
    return request<void>({
      method: "POST",
      url: `/api-keys/${id}/disable`,
    });
  },

  delete(id: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/api-keys/${id}`,
    });
  },
};

// ===== 反馈 API =====

export const feedbackApi = {
  create(data: CreateFeedbackRequest): Promise<FeedbackResponse> {
    return request<FeedbackResponse>({
      method: "POST",
      url: "/feedback",
      data,
    });
  },

  list(): Promise<FeedbackListItem[]> {
    return request<FeedbackListItem[]>({
      method: "GET",
      url: "/feedback",
    });
  },

  // ===== 管理端接口 =====

  listAll(params?: {
    status?: string;
    type?: string;
    severity?: string;
    page?: number;
    pageSize?: number;
  }): Promise<PagedResult<Feedback>> {
    return request<PagedResult<Feedback>>({
      method: "GET",
      url: "/feedback/all",
      params,
    });
  },

  update(
    id: string,
    data: { status?: string; priority?: string }
  ): Promise<Feedback> {
    return request<Feedback>({
      method: "PUT",
      url: `/feedback/${id}`,
      data,
    });
  },

  stats(): Promise<FeedbackStats> {
    return request<FeedbackStats>({
      method: "GET",
      url: "/feedback/stats",
    });
  },
};

// ===== 内测用户 API =====

export const betaUserApi = {
  list(params?: {
    status?: string;
    page?: number;
    pageSize?: number;
  }): Promise<PagedResult<BetaUser>> {
    return request<PagedResult<BetaUser>>({
      method: "GET",
      url: "/beta-users",
      params,
    });
  },

  get(id: string): Promise<BetaUser> {
    return request<BetaUser>({
      method: "GET",
      url: `/beta-users/${id}`,
    });
  },

  invite(data: InviteBetaUserInput): Promise<BetaUser> {
    return request<BetaUser>({
      method: "POST",
      url: "/beta-users",
      data,
    });
  },

  update(id: string, data: UpdateBetaUserInput): Promise<BetaUser> {
    return request<BetaUser>({
      method: "PUT",
      url: `/beta-users/${id}`,
      data,
    });
  },

  activate(id: string): Promise<BetaUser> {
    return request<BetaUser>({
      method: "POST",
      url: `/beta-users/${id}/activate`,
    });
  },

  pause(id: string): Promise<BetaUser> {
    return request<BetaUser>({
      method: "POST",
      url: `/beta-users/${id}/pause`,
    });
  },

  delete(id: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/beta-users/${id}`,
    });
  },
};

// ===== 版本发布说明 API =====

export const releaseNoteApi = {
  list(): Promise<ReleaseNote[]> {
    return request<ReleaseNote[]>({
      method: "GET",
      url: "/release-notes",
    });
  },

  get(id: string): Promise<ReleaseNote> {
    return request<ReleaseNote>({
      method: "GET",
      url: `/release-notes/${id}`,
    });
  },

  create(data: ReleaseNoteInput): Promise<ReleaseNote> {
    return request<ReleaseNote>({
      method: "POST",
      url: "/release-notes",
      data,
    });
  },

  update(id: string, data: ReleaseNoteInput): Promise<ReleaseNote> {
    return request<ReleaseNote>({
      method: "PUT",
      url: `/release-notes/${id}`,
      data,
    });
  },

  publish(id: string): Promise<ReleaseNote> {
    return request<ReleaseNote>({
      method: "POST",
      url: `/release-notes/${id}/publish`,
    });
  },
};

// ===== 使用量 API =====

export const usageApi = {
  get(): Promise<UsageResponse> {
    return request<UsageResponse>({
      method: "GET",
      url: "/usage",
    });
  },
};

export const billingApi = {
  summary(workspaceId: string): Promise<BillingSummaryResponse> {
    return request<BillingSummaryResponse>({
      method: "GET",
      url: "/billing/summary",
      params: { workspaceId },
    });
  },

  overview(workspaceId: string): Promise<BillingOverviewResponse> {
    return request<BillingOverviewResponse>({
      method: "GET",
      url: "/billing/overview",
      params: { workspaceId },
    });
  },

  usage(workspaceId: string, from?: string, to?: string): Promise<BillingUsageResponse> {
    return request<BillingUsageResponse>({
      method: "GET",
      url: "/billing/usage",
      params: { workspaceId, from, to },
    });
  },

  bills(workspaceId: string, from?: string, to?: string): Promise<BillingBillsResponse> {
    return request<BillingBillsResponse>({
      method: "GET",
      url: "/billing/bills",
      params: { workspaceId, from, to },
    });
  },

  pricing(workspaceId: string): Promise<BillingPricingResponse> {
    return request<BillingPricingResponse>({
      method: "GET",
      url: "/billing/pricing",
      params: { workspaceId },
    });
  },

  rechargeCatalog(): Promise<RechargeCatalogResponse> {
    return request<RechargeCatalogResponse>({
      method: "GET",
      url: "/billing/recharge/catalog",
    });
  },

  createRechargeOrder(data: CreateRechargeOrderInput): Promise<RechargeOrderResponse> {
    return request<RechargeOrderResponse>({
      method: "POST",
      url: "/billing/recharge/orders",
      data,
    });
  },

  rechargeOrders(workspaceId: string): Promise<RechargeOrderListResponse> {
    return request<RechargeOrderListResponse>({
      method: "GET",
      url: "/billing/recharge/orders",
      params: { workspaceId },
    });
  },

  rechargeOrder(orderId: string, workspaceId: string): Promise<RechargeOrderResponse> {
    return request<RechargeOrderResponse>({
      method: "GET",
      url: `/billing/recharge/orders/${orderId}`,
      params: { workspaceId },
    });
  },

  refreshRechargeOrder(orderId: string, workspaceId: string): Promise<RechargeOrderResponse> {
    return request<RechargeOrderResponse>({
      method: "POST",
      url: `/billing/recharge/orders/${orderId}/refresh`,
      params: { workspaceId },
    });
  },

  closeRechargeOrder(orderId: string, workspaceId: string): Promise<RechargeOrderResponse> {
    return request<RechargeOrderResponse>({
      method: "POST",
      url: `/billing/recharge/orders/${orderId}/close`,
      params: { workspaceId },
    });
  },

  confirmFakeRecharge(orderId: string, workspaceId: string): Promise<RechargeOrderResponse> {
    return request<RechargeOrderResponse>({
      method: "POST",
      url: `/billing/recharge/orders/${orderId}/fake-confirm`,
      params: { workspaceId },
    });
  },
};

// ===== 工作区 API =====

export const workspaceApi = {
  list(): Promise<Workspace[]> {
    return request<Workspace[]>({ method: "GET", url: "/workspaces" });
  },

  getCurrent(): Promise<Workspace | null> {
    return request<Workspace | null>({ method: "GET", url: "/workspaces/current" });
  },

  get(id: string): Promise<Workspace> {
    return request<Workspace>({ method: "GET", url: `/workspaces/${id}` });
  },

  create(data: CreateWorkspaceInput): Promise<Workspace> {
    return request<Workspace>({ method: "POST", url: "/workspaces", data });
  },

  initLocal(data: InitLocalWorkspaceInput): Promise<Workspace> {
    return request<Workspace>({ method: "POST", url: "/workspaces/init-local", data });
  },

  update(id: string, data: UpdateWorkspaceInput): Promise<Workspace> {
    return request<Workspace>({ method: "PUT", url: `/workspaces/${id}`, data });
  },

  switch(id: string): Promise<{ workspaceId: string }> {
    return request<{ workspaceId: string }>({ method: "POST", url: `/workspaces/${id}/switch` });
  },

  delete(id: string): Promise<{ deleted: boolean }> {
    return request<{ deleted: boolean }>({ method: "DELETE", url: `/workspaces/${id}` });
  },

  getModes(): Promise<WorkspaceModeOption[]> {
    return request<WorkspaceModeOption[]>({ method: "GET", url: "/workspaces/modes" });
  },

  getModelProviders(): Promise<ModelProviderOption[]> {
    return request<ModelProviderOption[]>({ method: "GET", url: "/workspaces/model-providers" });
  },

  getConfig(): Promise<LocalConfig> {
    return request<LocalConfig>({ method: "GET", url: "/workspaces/config" });
  },

  updateModelSettings(id: string, data: UpdateModelSettingsInput): Promise<Workspace> {
    return request<Workspace>({ method: "PUT", url: `/workspaces/${id}/model-settings`, data });
  },

  testModel(id: string): Promise<ModelTestResult> {
    return request<ModelTestResult>({ method: "POST", url: `/workspaces/${id}/test-model` });
  },
};

// ===== 媒体创作任务 API =====

export type MediaRoutePreference = "local_first" | "byok" | "platform_cloud";

export interface MediaJob {
  id: string;
  billingJobId?: string;
  /** Executor-provided artifact metadata. No media bytes or storage URL are exposed here. */
  outputJson?: string;
  workspaceId: string;
  capability: string;
  status: "created" | "quoted" | "queued" | "leased" | "running" | "uploading" | "completed" | "failed" | "cancelled";
  route: MediaRoutePreference;
  cancellationRequested: boolean;
  errorCode?: string;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface CreateMediaJobInput {
  workspaceId: string;
  capability: string;
  routePreference?: MediaRoutePreference;
  parameters: Record<string, unknown>;
  inputAssetIds?: string[];
}

export const mediaJobApi = {
  create(data: CreateMediaJobInput): Promise<MediaJob> {
    return request<MediaJob>({ method: "POST", url: "/media/jobs", data });
  },
  list(workspaceId?: string): Promise<MediaJob[]> {
    return request<MediaJob[]>({ method: "GET", url: "/media/jobs", params: workspaceId ? { workspaceId } : undefined });
  },
  cancel(id: string): Promise<MediaJob> {
    return request<MediaJob>({ method: "POST", url: `/media/jobs/${id}/cancel` });
  },
  retry(id: string): Promise<MediaJob> {
    return request<MediaJob>({ method: "POST", url: `/media/jobs/${id}/retry` });
  },
};

// ===== 桌面运行能力 API =====

export const desktopRuntimeApi = {
  getCapabilities(): Promise<DesktopCapabilities> {
    return request<DesktopCapabilities>({
      method: "GET",
      url: "/desktop/capabilities",
    });
  },

  getCloudConnection(): Promise<DesktopCloudConnectionStatus> {
    return request<DesktopCloudConnectionStatus>({
      method: "GET",
      url: "/desktop/cloud-connection",
    });
  },

  getState(): Promise<DesktopRuntimeState> {
    return request<DesktopRuntimeState>({
      method: "GET",
      url: "/desktop/state",
    });
  },
};

// ===== Cloud Inbox API =====

export const cloudInboxApi = {
  getSettings(): Promise<CloudInboxSettings> {
    return request<CloudInboxSettings>({
      method: "GET",
      url: "/cloud-inbox/settings",
    });
  },

  updateSettings(
    data: UpdateCloudInboxSettingsInput
  ): Promise<CloudInboxSettings> {
    return request<CloudInboxSettings>({
      method: "PUT",
      url: "/cloud-inbox/settings",
      data,
    });
  },

  getStatus(): Promise<CloudInboxStatus> {
    return request<CloudInboxStatus>({
      method: "GET",
      url: "/cloud-inbox/status",
    });
  },

  pull(data: CloudInboxPullInput): Promise<CloudInboxPullResult> {
    return request<CloudInboxPullResult>({
      method: "POST",
      url: "/cloud-inbox/pull",
      data,
    });
  },

  listLogs(limit = 10): Promise<CloudInboxSyncLog[]> {
    return request<CloudInboxSyncLog[]>({
      method: "GET",
      url: "/cloud-inbox/logs",
      params: { limit },
    });
  },

  retryScheduledPull(): Promise<{ queued: boolean }> {
    return request<{ queued: boolean }>({
      method: "POST",
      url: "/cloud-inbox/schedule/retry",
    });
  },

  cancelScheduledPull(): Promise<{ cancelled: boolean }> {
    return request<{ cancelled: boolean }>({
      method: "POST",
      url: "/cloud-inbox/schedule/cancel",
    });
  },
};

// ===== Cloud Account / Workspace Binding API =====

export const bindingApi = {
  listCloudAccounts(): Promise<CloudAccountBinding[]> {
    return request<CloudAccountBinding[]>({
      method: "GET",
      url: "/bindings/cloud-accounts",
    });
  },

  listCloudWorkspaces(id: string): Promise<CloudWorkspaceDiscovery> {
    return request<CloudWorkspaceDiscovery>({
      method: "GET",
      url: `/bindings/cloud-accounts/${encodeURIComponent(id)}/workspaces`,
    });
  },

  listWorkspaceBindings(workspaceId?: string): Promise<WorkspaceBinding[]> {
    return request<WorkspaceBinding[]>({
      method: "GET",
      url: "/bindings/workspaces",
      params: workspaceId ? { workspaceId } : undefined,
    });
  },

  createWorkspaceBinding(
    data: CreateWorkspaceBindingInput
  ): Promise<WorkspaceBinding> {
    return request<WorkspaceBinding>({
      method: "POST",
      url: "/bindings/workspaces",
      data,
    });
  },

  unbindWorkspace(id: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/bindings/workspaces/${encodeURIComponent(id)}`,
    });
  },

  unbindCloudAccount(id: string): Promise<void> {
    return request<void>({
      method: "DELETE",
      url: `/bindings/cloud-accounts/${encodeURIComponent(id)}`,
    });
  },
};

export const oauthApi = {
  start(data: OAuthStartInput): Promise<OAuthStartResult> {
    return request<OAuthStartResult>({
      method: "POST",
      url: "/oauth/start",
      data,
    });
  },

  status(sessionId: string): Promise<OAuthStatus> {
    return request<OAuthStatus>({
      method: "GET",
      url: `/oauth/status/${encodeURIComponent(sessionId)}`,
    });
  },
};

// ===== Inbox API =====

export const inboxApi = {
  // 列表（支持筛选）
  list(params?: InboxListParams): Promise<InboxItem[]> {
    return request<InboxItem[]>({
      method: "GET",
      url: "/inbox",
      params: params
        ? {
            status: params.status,
            inputType: params.inputType,
            topicId: params.topicId,
            limit: params.limit,
            offset: params.offset,
          }
        : undefined,
    });
  },

  // 获取单个条目
  get(id: string): Promise<InboxItem> {
    return request<InboxItem>({ method: "GET", url: `/inbox/${id}` });
  },

  // 创建文本条目
  createText(data: {
    title?: string;
    contentText: string;
    topicId?: string;
  }): Promise<InboxItem> {
    return request<InboxItem>({
      method: "POST",
      url: "/inbox",
      data: { inputType: "text", ...data },
    });
  },

  // 创建 URL 条目
  createUrl(data: {
    sourceUrl: string;
    title?: string;
    topicId?: string;
  }): Promise<InboxItem> {
    return request<InboxItem>({
      method: "POST",
      url: "/inbox",
      data: { inputType: "url", ...data },
    });
  },

  // 上传文件到收件箱
  upload(file: File, topicId?: string): Promise<InboxItem> {
    const formData = new FormData();
    formData.append("file", file);
    if (topicId) formData.append("topicId", topicId);
    return request<InboxItem>({
      method: "POST",
      url: "/inbox/upload",
      data: formData,
      headers: { "Content-Type": "multipart/form-data" },
    });
  },

  // 更新条目
  update(id: string, data: UpdateInboxItemInput): Promise<InboxItem> {
    return request<InboxItem>({
      method: "PATCH",
      url: `/inbox/${id}`,
      data,
    });
  },

  // 更新状态
  updateStatus(
    id: string,
    status: string,
    errorMessage?: string
  ): Promise<{ id: string; status: string }> {
    return request<{ id: string; status: string }>({
      method: "PUT",
      url: `/inbox/${id}/status`,
      data: { status, errorMessage },
    });
  },

  // 导入单个条目到资料库
  import(id: string, topicId?: string): Promise<InboxItem> {
    return request<InboxItem>({
      method: "POST",
      url: `/inbox/${id}/import`,
      data: { topicId },
    });
  },

  // 批量导入
  batchImport(
    inboxItemIds: string[],
    topicId?: string
  ): Promise<{ imported: number }> {
    return request<{ imported: number }>({
      method: "POST",
      url: "/inbox/batch-import",
      data: { inboxItemIds, topicId },
    });
  },

  // 归档单个条目
  archive(id: string): Promise<{ id: string; archived: boolean }> {
    return request<{ id: string; archived: boolean }>({
      method: "POST",
      url: `/inbox/${id}/archive`,
    });
  },

  // 批量归档
  batchArchive(inboxItemIds: string[]): Promise<{ archived: number }> {
    return request<{ archived: number }>({
      method: "POST",
      url: "/inbox/batch-archive",
      data: { inboxItemIds },
    });
  },

  // 重试失败条目
  retry(id: string): Promise<InboxItem> {
    return request<InboxItem>({
      method: "POST",
      url: `/inbox/${id}/retry`,
    });
  },

  // 删除（永久）
  delete(id: string): Promise<{ id: string; archived: boolean }> {
    return request<{ id: string; archived: boolean }>({
      method: "DELETE",
      url: `/inbox/${id}`,
    });
  },

  // 获取条目事件
  getEvents(id: string): Promise<InboxEvent[]> {
    return request<InboxEvent[]>({
      method: "GET",
      url: `/inbox/${id}/events`,
    });
  },

  // 获取条目附件
  getAttachments(id: string): Promise<InboxAttachment[]> {
    return request<InboxAttachment[]>({
      method: "GET",
      url: `/inbox/${id}/attachments`,
    });
  },
};

// ===== Mobile Devices API =====

export const mobileDevicesApi = {
  list(): Promise<MobileDevice[]> {
    return request<MobileDevice[]>({
      method: "GET",
      url: "/mobile/devices",
    });
  },

  deactivate(clientId: string): Promise<{ deactivated: boolean; clientId: string }> {
    return request<{ deactivated: boolean; clientId: string }>({
      method: "POST",
      url: "/mobile/devices/deactivate",
      data: { clientId },
    });
  },

  createPairingCode(): Promise<{ code: string; expiresAt: string }> {
    return request<{ code: string; expiresAt: string }>({
      method: "POST",
      url: "/mobile/devices/pairing-code",
    });
  },
};

// ===== Push Notifications API =====

export const pushNotificationsApi = {
  list(params?: { status?: string; limit?: number }): Promise<PushNotification[]> {
    return request<PushNotification[]>({
      method: "GET",
      url: "/mobile/push-notifications",
      params,
    });
  },
};

// ===== Mobile Capture API =====

export const mobileCaptureApi = {
  bindDevice(data: {
    clientId: string;
    deviceName?: string;
    platform?: string;
    pushToken?: string;
  }): Promise<{
    device: {
      id: string;
      workspaceId: string;
      clientId: string;
      deviceName?: string;
      platform?: string;
      pushToken?: string;
      status: string;
      lastSeenAt?: string;
      boundAt: string;
      createdAt: string;
      updatedAt: string;
    };
    deviceAccessToken: string;
    refreshToken: string;
    expiresAt: string;
    refreshTokenExpiresAt: string;
  }> {
    return request({
      method: "POST",
      url: "/mobile/devices/bind",
      data,
    });
  },

  text(data: {
    contentText: string;
    topicId?: string;
    clientId?: string;
  }): Promise<InboxItem> {
    return request<InboxItem>({
      method: "POST",
      url: "/mobile/capture/text",
      data,
    });
  },

  url(data: {
    sourceUrl: string;
    title?: string;
    topicId?: string;
    clientId?: string;
  }): Promise<InboxItem> {
    return request<InboxItem>({
      method: "POST",
      url: "/mobile/capture/url",
      data,
    });
  },

  upload(file: File, topicId?: string, clientId?: string): Promise<InboxItem> {
    const formData = new FormData();
    formData.append("file", file);
    if (topicId) formData.append("topicId", topicId);
    if (clientId) formData.append("clientId", clientId);
    return request<InboxItem>({
      method: "POST",
      url: "/mobile/capture/upload",
      data: formData,
      headers: { "Content-Type": "multipart/form-data" },
    });
  },

  listStatus(clientId: string, limit = 50): Promise<InboxItem[]> {
    return request<InboxItem[]>({
      method: "GET",
      url: "/mobile/capture/status",
      params: { clientId, limit },
    });
  },
};

// ===== Runtime API =====

export const runtimeApi = {
  workspaceHealth(): Promise<WorkspaceRuntimeHealth> {
    return request<WorkspaceRuntimeHealth>({
      method: "GET",
      url: "/runtime/workspace-health",
    });
  },
  platformHealth(): Promise<RuntimeHealth> {
    return request<RuntimeHealth>({
      method: "GET",
      url: "/runtime/platform-health",
    });
  },
  health(): Promise<RuntimeHealth> {
    return request<RuntimeHealth>({ method: "GET", url: "/runtime/health" });
  },
  detectLocalModels(): Promise<LocalModelDetection> {
    return request<LocalModelDetection>({
      method: "GET",
      url: "/runtime/local-models",
    });
  },
  updateSafety(): Promise<UpdateSafetyStatus> {
    return request<UpdateSafetyStatus>({
      method: "GET",
      url: "/runtime/update-safety",
    });
  },
};

// ===== 分块 API =====

export const chunkApi = {
  getDocumentChunks(documentId: string): Promise<DocumentChunkItem[]> {
    return request<DocumentChunkItem[]>({
      method: "GET",
      url: `/documents/${documentId}/chunks`,
    });
  },

  getChunk(chunkId: string): Promise<DocumentChunkItem> {
    return request<DocumentChunkItem>({
      method: "GET",
      url: `/documents/chunks/${chunkId}`,
    });
  },

  translate(chunkId: string, force = false): Promise<ChunkLocalization> {
    return request<ChunkLocalization>({
      method: "POST",
      url: `/chunks/${chunkId}/translate`,
      data: { languageCode: "zh-CN", force, translationType: "machine" },
    });
  },

  getLocalizations(chunkId: string): Promise<ChunkLocalization[]> {
    return request<ChunkLocalization[]>({
      method: "GET",
      url: `/chunks/${chunkId}/localizations`,
    });
  },

  review(
    chunkId: string,
    localizationId: string,
    data: { headingLocalized?: string; contentLocalized: string; approved: boolean }
  ): Promise<ChunkLocalization> {
    return request<ChunkLocalization>({
      method: "POST",
      url: `/chunks/${chunkId}/localizations/${localizationId}/review`,
      data,
    });
  },

  enrich(chunkId: string, force = false): Promise<ChunkEnrichment> {
    return request<ChunkEnrichment>({
      method: "POST",
      url: `/chunks/${chunkId}/enrich`,
      data: { force },
    });
  },

  getEnrichments(chunkId: string): Promise<ChunkEnrichment[]> {
    return request<ChunkEnrichment[]>({
      method: "GET",
      url: `/chunks/${chunkId}/enrichments`,
    });
  },

  translateDocument(documentId: string, force = false): Promise<MultilingualBatchJob> {
    return request<MultilingualBatchJob>({
      method: "POST",
      url: `/documents/${documentId}/translate-chunks`,
      data: { force, maxChunks: 500 },
    });
  },

  enrichDocument(documentId: string, force = false): Promise<MultilingualBatchJob> {
    return request<MultilingualBatchJob>({
      method: "POST",
      url: `/documents/${documentId}/enrich-chunks`,
      data: { force, maxChunks: 500 },
    });
  },

  rebuildMultiVectors(documentId: string): Promise<MultilingualBatchJob> {
    return request<MultilingualBatchJob>({
      method: "POST", url: `/documents/${documentId}/rebuild-multi-vectors`, data: { maxChunks: 500 },
    });
  },

  getDocumentJobs(documentId: string): Promise<MultilingualBatchJob[]> {
    return request<MultilingualBatchJob[]>({ method: "GET", url: `/documents/${documentId}/batch-jobs` });
  },

  controlJob(jobId: string, action: "pause" | "resume" | "retry"): Promise<MultilingualBatchJob> {
    return request<MultilingualBatchJob>({ method: "POST", url: `/multilingual-jobs/${jobId}/${action}` });
  },
};

// ===== 文档操作 API =====

export const actionApi = {
  regenerateTags(documentId: string): Promise<boolean> {
    return request<boolean>({
      method: "POST",
      url: `/documents/${documentId}/actions/regenerate-tags`,
    });
  },

  regenerateEntities(documentId: string): Promise<boolean> {
    return request<boolean>({
      method: "POST",
      url: `/documents/${documentId}/actions/regenerate-entities`,
    });
  },

  rechunk(documentId: string): Promise<boolean> {
    return request<boolean>({
      method: "POST",
      url: `/documents/${documentId}/actions/rechunk`,
    });
  },

  reembed(documentId: string): Promise<boolean> {
    return request<boolean>({
      method: "POST",
      url: `/documents/${documentId}/actions/reembed`,
    });
  },

  rebuildIndex(): Promise<boolean> {
    return request<boolean>({
      method: "POST",
      url: "/workspaces/actions/rebuild-index",
    });
  },
};

// ===== 索引状态 API =====

export const indexApi = {
  getState(): Promise<VectorIndexState> {
    return request<VectorIndexState>({
      method: "GET",
      url: "/workspaces/actions/index-state",
    });
  },
};

// ===== 分块向量信息 API =====

export const chunkEmbeddingApi = {
  get(chunkId: string): Promise<ChunkEmbeddingInfo> {
    return request<ChunkEmbeddingInfo>({
      method: "GET",
      url: `/documents/chunks/${chunkId}/embedding`,
    });
  },
};

// ===== Agent Profile API =====

export const agentApi = {
  listProfiles: async (): Promise<AgentProfile[]> => {
    return request<AgentProfile[]>({ method: "GET", url: "/agent-profiles" });
  },

  getProfile: async (id: string): Promise<AgentProfile> => {
    return request<AgentProfile>({ method: "GET", url: `/agent-profiles/${id}` });
  },

  createProfile: async (data: Partial<AgentProfile>): Promise<AgentProfile> => {
    return request<AgentProfile>({ method: "POST", url: "/agent-profiles", data });
  },

  updateProfile: async (id: string, data: Partial<AgentProfile>): Promise<AgentProfile> => {
    return request<AgentProfile>({ method: "PUT", url: `/agent-profiles/${id}`, data });
  },

  deleteProfile: async (id: string): Promise<void> => {
    return request<void>({ method: "DELETE", url: `/agent-profiles/${id}` });
  },

  generateMcpConfig: async (profileId: string): Promise<McpConfig> => {
    return request<McpConfig>({ method: "GET", url: `/agent-profiles/${profileId}/mcp-config` });
  },

  testConnection: async (profileId: string): Promise<{ success: boolean; message: string; tools?: AgentToolDefinition[] }> => {
    return request<{ success: boolean; message: string; tools?: AgentToolDefinition[] }>({
      method: "POST",
      url: `/agent-profiles/${profileId}/test`,
    });
  },

  listTools: async (profileId?: string): Promise<AgentToolDefinition[]> => {
    return request<AgentToolDefinition[]>({
      method: "GET",
      url: "/agent-profiles/tools",
      params: profileId ? { profileId } : undefined,
    });
  },

  getInvocationLogs: async (params?: {
    page?: number;
    pageSize?: number;
    toolName?: string;
    status?: string;
  }): Promise<PagedResult<AgentInvocationLog>> => {
    return request<PagedResult<AgentInvocationLog>>({
      method: "GET",
      url: "/agent-profiles/logs",
      params,
    });
  },
};

// ===== Agent Memory API =====

export const agentMemoryApi = {
  // Sessions
  listSessions: async (limit = 50, offset = 0): Promise<AgentMemorySession[]> => {
    return request<AgentMemorySession[]>({
      method: "GET",
      url: "/agent-memory/sessions",
      params: { limit, offset },
    });
  },

  getSession: async (id: string): Promise<AgentMemorySession> => {
    return request<AgentMemorySession>({
      method: "GET",
      url: `/agent-memory/sessions/${id}`,
    });
  },

  createSession: async (data: {
    externalSessionKey: string;
    taskTitle: string;
    agentProfileId?: string;
    topicId?: string;
  }): Promise<AgentMemorySession> => {
    return request<AgentMemorySession>({
      method: "POST",
      url: "/agent-memory/sessions",
      data,
    });
  },

  closeSession: async (id: string): Promise<void> => {
    return request<void>({
      method: "POST",
      url: `/agent-memory/sessions/${id}/close`,
    });
  },

  // Memory items
  searchMemory: async (data: SearchMemoryInput): Promise<AgentMemoryItem[]> => {
    return request<AgentMemoryItem[]>({
      method: "POST",
      url: "/agent-memory/search",
      data,
    });
  },

  getMemoryItem: async (id: string): Promise<AgentMemoryItem> => {
    return request<AgentMemoryItem>({
      method: "GET",
      url: `/agent-memory/items/${id}`,
    });
  },

  captureMemory: async (data: CaptureMemoryInput): Promise<AgentMemoryItem> => {
    return request<AgentMemoryItem>({
      method: "POST",
      url: "/agent-memory/items",
      data,
    });
  },

  confirmMemory: async (id: string, action: "confirm" | "reject", note?: string): Promise<AgentMemoryItem> => {
    return request<AgentMemoryItem>({
      method: "POST",
      url: `/agent-memory/items/${id}/confirm`,
      data: { action, note },
    });
  },

  archiveMemory: async (id: string): Promise<void> => {
    return request<void>({
      method: "POST",
      url: `/agent-memory/items/${id}/archive`,
    });
  },

  restoreMemory: async (id: string): Promise<void> => {
    return request<void>({
      method: "POST",
      url: `/agent-memory/items/${id}/restore`,
    });
  },

  forgetMemory: async (id: string): Promise<void> => {
    return request<void>({
      method: "DELETE",
      url: `/agent-memory/items/${id}`,
    });
  },

  // Evidence
  getEvidence: async (memoryItemId: string): Promise<AgentMemoryEvidence[]> => {
    return request<AgentMemoryEvidence[]>({
      method: "GET",
      url: `/agent-memory/items/${memoryItemId}/evidence`,
    });
  },

  // Feedback
  getFeedback: async (memoryItemId: string): Promise<AgentMemoryFeedback[]> => {
    return request<AgentMemoryFeedback[]>({
      method: "GET",
      url: `/agent-memory/items/${memoryItemId}/feedback`,
    });
  },

  // Context pack
  getContext: async (sessionId: string, maxTokens?: number): Promise<ContextPackDto> => {
    return request<ContextPackDto>({
      method: "POST",
      url: `/agent-memory/sessions/${sessionId}/context`,
      params: maxTokens ? { maxTokens } : undefined,
    });
  },

  // Access logs
  getAccessLogs: async (params?: {
    sessionId?: string;
    memoryItemId?: string;
    limit?: number;
    offset?: number;
  }): Promise<AgentMemoryAccessLog[]> => {
    return request<AgentMemoryAccessLog[]>({
      method: "GET",
      url: "/agent-memory/access-log",
      params,
    });
  },

  // Checkpoints
  createCheckpoint: async (sessionId: string): Promise<AgentMemoryCheckpoint> => {
    return request<AgentMemoryCheckpoint>({
      method: "POST",
      url: `/agent-memory/sessions/${sessionId}/checkpoint`,
    });
  },

  listCheckpoints: async (sessionId: string): Promise<AgentMemoryCheckpoint[]> => {
    return request<AgentMemoryCheckpoint[]>({
      method: "GET",
      url: `/agent-memory/sessions/${sessionId}/checkpoints`,
    });
  },

  // Metrics
  getMetrics: async (): Promise<MemoryQualityMetrics> => {
    return request<MemoryQualityMetrics>({
      method: "GET",
      url: "/agent-memory/metrics",
    });
  },

  // Health
  getHealth: async (): Promise<{ status: string; total_sessions: number; total_items: number; total_checkpoints: number }> => {
    return request({
      method: "GET",
      url: "/agent-memory/health",
    });
  },

  // ── Handoffs (stage 2) ──
  createHandoff: async (data: CreateHandoffInput): Promise<AgentMemoryHandoff> => {
    return request<AgentMemoryHandoff>({
      method: "POST",
      url: "/agent-memory/handoffs",
      data,
    });
  },

  getHandoffs: async (params?: GetHandoffsInput): Promise<AgentMemoryHandoff[]> => {
    return request<AgentMemoryHandoff[]>({
      method: "GET",
      url: "/agent-memory/handoffs",
      params: params ?? { status: "open" },
    });
  },

  acceptHandoff: async (handoffId: string, toSessionId: string): Promise<AgentMemoryHandoff> => {
    return request<AgentMemoryHandoff>({
      method: "POST",
      url: `/agent-memory/handoffs/${handoffId}/accept`,
      data: { toSessionId },
    });
  },

  completeHandoff: async (handoffId: string, resultSummary: string): Promise<AgentMemoryHandoff> => {
    return request<AgentMemoryHandoff>({
      method: "POST",
      url: `/agent-memory/handoffs/${handoffId}/complete`,
      data: { resultSummary },
    });
  },

  // ── Ingest (stage 3) ──
  ingestEvents: async (batch: IngestEventBatch): Promise<IngestResult> => {
    return request<IngestResult>({
      method: "POST",
      url: "/agent-memory/ingest",
      data: batch,
    });
  },

  // ── Turns / Actions (stage 3 — view collected events) ──
  listTurns: async (sessionId: string): Promise<AgentMemoryTurn[]> => {
    return request<AgentMemoryTurn[]>({
      method: "GET",
      url: `/agent-memory/sessions/${sessionId}/turns`,
    });
  },

  // ── Projects (stage 1) ──
  listProjects: async (): Promise<Project[]> => {
    return request<Project[]>({
      method: "GET",
      url: "/agent-memory/projects",
    });
  },
};

// ===== Meeting API =====

export const meetingApi = {
  list: async (params?: { workspaceId?: string; limit?: number; offset?: number }): Promise<MeetingDto[]> => {
    return request<MeetingDto[]>({ method: "GET", url: "/v1/meetings", params });
  },

  get: async (meetingId: string): Promise<MeetingDto> => {
    return request<MeetingDto>({ method: "GET", url: `/v1/meetings/${meetingId}` });
  },

  create: async (data: { title: string; description?: string }): Promise<MeetingDto> => {
    return request<MeetingDto>({ method: "POST", url: "/v1/meetings", data });
  },

  update: async (meetingId: string, data: { title?: string; description?: string }): Promise<MeetingDto> => {
    return request<MeetingDto>({ method: "PATCH", url: `/v1/meetings/${meetingId}`, data });
  },

  finish: async (meetingId: string): Promise<void> => {
    return request<void>({ method: "POST", url: `/v1/meetings/${meetingId}/finish` });
  },

  delete: async (meetingId: string): Promise<void> => {
    return request<void>({ method: "DELETE", url: `/v1/meetings/${meetingId}` });
  },

  // Speakers
  getSpeakers: async (meetingId: string): Promise<MeetingSpeaker[]> => {
    return request<MeetingSpeaker[]>({ method: "GET", url: `/v1/meetings/${meetingId}/speakers` });
  },

  updateSpeaker: async (meetingId: string, speakerId: string, data: { displayName?: string; identityStatus?: string }): Promise<MeetingSpeaker> => {
    return request<MeetingSpeaker>({ method: "PATCH", url: `/v1/meetings/${meetingId}/speakers/${speakerId}`, data });
  },

  // Recording
  startRecording: async (meetingId: string, data?: { title?: string }): Promise<RecordingSession> => {
    return request<RecordingSession>({ method: "POST", url: `/v1/meetings/${meetingId}/recording/start`, data: data ?? {} });
  },

  pauseRecording: async (meetingId: string): Promise<RecordingSession> => {
    return request<RecordingSession>({ method: "POST", url: `/v1/meetings/${meetingId}/recording/pause` });
  },

  resumeRecording: async (meetingId: string): Promise<RecordingSession> => {
    return request<RecordingSession>({ method: "POST", url: `/v1/meetings/${meetingId}/recording/resume` });
  },

  stopRecording: async (meetingId: string): Promise<RecordingSession> => {
    return request<RecordingSession>({ method: "POST", url: `/v1/meetings/${meetingId}/recording/stop` });
  },

  getRecordingStatus: async (meetingId: string): Promise<RecordingSession | null> => {
    return request<RecordingSession | null>({ method: "GET", url: `/v1/meetings/${meetingId}/recording/status` });
  },

  // Assets
  getAssets: async (meetingId: string): Promise<MeetingAsset[]> => {
    return request<MeetingAsset[]>({ method: "GET", url: `/v1/meetings/${meetingId}/assets` });
  },

  uploadAsset: async (meetingId: string, file: File): Promise<MeetingAsset> => {
    const formData = new FormData();
    formData.append("file", file);
    return request<MeetingAsset>({
      method: "POST",
      url: `/v1/meetings/${meetingId}/assets`,
      data: formData,
      headers: { "Content-Type": "multipart/form-data" },
    });
  },

  // Minutes
  getMinutes: async (meetingId: string): Promise<MeetingMinutes[]> => {
    return request<MeetingMinutes[]>({ method: "GET", url: `/v1/meetings/${meetingId}/minutes` });
  },

  generateMinutes: async (meetingId: string, data?: { language?: string; style?: string }): Promise<MeetingMinutes> => {
    return request<MeetingMinutes>({ method: "POST", url: `/v1/meetings/${meetingId}/minutes/generate`, data: data ?? {} });
  },

  setOfficialMinutes: async (meetingId: string, minutesId: string): Promise<MeetingMinutes> => {
    return request<MeetingMinutes>({ method: "POST", url: `/v1/meetings/${meetingId}/minutes/${minutesId}/set-official` });
  },

  publishMinutes: async (meetingId: string, minutesId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/v1/meetings/${meetingId}/minutes/${minutesId}/publish` });
  },

  publishAll: async (meetingId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/v1/meetings/${meetingId}/publish` });
  },

  // Action items
  getActionItems: async (meetingId: string): Promise<MeetingActionItem[]> => {
    return request<MeetingActionItem[]>({ method: "GET", url: `/v1/meetings/${meetingId}/action-items` });
  },

  confirmActionItem: async (actionItemId: string, data: { action: string; note?: string }): Promise<MeetingActionItem> => {
    return request<MeetingActionItem>({ method: "POST", url: `/v1/meetings/action-items/${actionItemId}/confirm`, data });
  },

  // Transcripts
  getTranscripts: async (meetingId: string): Promise<TranscriptDto[]> => {
    return request<TranscriptDto[]>({ method: "GET", url: `/v1/meetings/${meetingId}/transcripts` });
  },

  getTranscript: async (transcriptId: string): Promise<{ segments: TranscriptSegment[] } & TranscriptDto> => {
    return request({ method: "GET", url: `/v1/transcripts/${transcriptId}` });
  },

  setOfficialTranscript: async (meetingId: string, transcriptId: string): Promise<void> => {
    return request<void>({ method: "POST", url: `/v1/meetings/${meetingId}/transcripts/${transcriptId}/set-official` });
  },

  // Processing pipeline
  getProcessingStatus: async (meetingId: string): Promise<unknown> => {
    return request({ method: "GET", url: `/v1/meetings/${meetingId}/processing/status` });
  },

  getProcessingTasks: async (meetingId: string): Promise<ProcessingTaskStatus[]> => {
    return request<ProcessingTaskStatus[]>({ method: "GET", url: `/v1/meetings/${meetingId}/processing/tasks` });
  },

  retryProcessingTask: async (taskId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/v1/meetings/processing/tasks/${taskId}/retry` });
  },
};

// ===== Audio / Transcription API =====

export const audioApi = {
  upload: async (file: File, params?: {
    title?: string;
    topicId?: string;
    language?: string;
    enableVad?: boolean;
    enableSpeakerDiarization?: boolean;
    enablePunctuation?: boolean;
    autoStart?: boolean;
  }): Promise<AudioUploadResponse> => {
    const formData = new FormData();
    formData.append("file", file);
    if (params?.title) formData.append("title", params.title);
    if (params?.topicId) formData.append("topicId", params.topicId);
    if (params?.language) formData.append("language", params.language);
    formData.append("enableVad", String(params?.enableVad ?? true));
    formData.append("enableSpeakerDiarization", String(params?.enableSpeakerDiarization ?? false));
    formData.append("enablePunctuation", String(params?.enablePunctuation ?? true));
    formData.append("autoStart", String(params?.autoStart ?? true));
    return request<AudioUploadResponse>({
      method: "POST",
      url: "/audio/upload",
      data: formData,
      headers: { "Content-Type": "multipart/form-data" },
    });
  },

  getAsset: async (assetId: string): Promise<AudioAssetDto> => {
    return request<AudioAssetDto>({ method: "GET", url: `/audio/assets/${assetId}` });
  },

  listAssets: async (params?: { limit?: number; offset?: number }): Promise<AudioAssetDto[]> => {
    return request<AudioAssetDto[]>({ method: "GET", url: "/audio/assets", params });
  },
};

export const transcriptionApi = {
  listJobs: async (params?: { status?: string; limit?: number; offset?: number }): Promise<TranscriptionJobDto[]> => {
    return request<TranscriptionJobDto[]>({ method: "GET", url: "/transcription/jobs", params });
  },

  getJob: async (jobId: string): Promise<TranscriptionStatusResponse> => {
    return request<TranscriptionStatusResponse>({ method: "GET", url: `/transcription/jobs/${jobId}` });
  },

  cancelJob: async (jobId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/transcription/jobs/${jobId}/cancel` });
  },

  getSegments: async (jobId: string): Promise<TranscriptionSegmentDto[]> => {
    return request<TranscriptionSegmentDto[]>({ method: "GET", url: `/transcription/jobs/${jobId}/segments` });
  },

  editSegment: async (segmentId: string, text: string): Promise<unknown> => {
    return request({ method: "PUT", url: `/transcription/segments/${segmentId}`, data: { text } });
  },

  mergeSegment: async (segmentId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/transcription/segments/${segmentId}/merge` });
  },

  mergeAllSegments: async (jobId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/transcription/jobs/${jobId}/merge-all` });
  },

  listProviders: async (): Promise<AsrProviderDescriptor[]> => {
    return request<AsrProviderDescriptor[]>({ method: "GET", url: "/transcription/providers" });
  },
};

// ===== TTS API =====

export const ttsApi = {
  synthesize: async (data: {
    text: string;
    voiceId?: string;
    language?: string;
    speed?: number;
    pitch?: number;
    preferredProviderId?: string;
  }): Promise<TtsResult> => {
    return request<TtsResult>({ method: "POST", url: "/tts/synthesize", data });
  },

  listProviders: async (): Promise<TtsProviderDescriptor[]> => {
    return request<TtsProviderDescriptor[]>({ method: "GET", url: "/tts/providers" });
  },

  listVoices: async (providerId?: string): Promise<VoiceProfile[]> => {
    return request<VoiceProfile[]>({ method: "GET", url: "/tts/voices", params: providerId ? { providerId } : undefined });
  },

  preview: async (data: {
    text: string;
    voiceId?: string;
    preferredProviderId?: string;
  }): Promise<TtsResult> => {
    return request<TtsResult>({ method: "POST", url: "/tts/preview", data });
  },

  healthCheck: async (providerId: string): Promise<ProviderHealth> => {
    return request<ProviderHealth>({ method: "GET", url: `/tts/health/${providerId}` });
  },
};

// ===== Model Registry API =====

export const modelRegistryApi = {
  list: async (params?: { capability?: string; providerId?: string; enabledOnly?: boolean }): Promise<ModelRegistry[]> => {
    return request<ModelRegistry[]>({ method: "GET", url: "/audio/models", params });
  },

  get: async (id: string): Promise<ModelRegistry> => {
    return request<ModelRegistry>({ method: "GET", url: `/audio/models/${id}` });
  },

  register: async (data: RegisterModelRequest): Promise<ModelRegistry> => {
    return request<ModelRegistry>({ method: "POST", url: "/audio/models", data });
  },

  update: async (id: string, data: RegisterModelRequest): Promise<ModelRegistry> => {
    return request<ModelRegistry>({ method: "PUT", url: `/audio/models/${id}`, data });
  },

  disable: async (id: string): Promise<unknown> => {
    return request({ method: "DELETE", url: `/audio/models/${id}` });
  },
};

// ===== Provider Credentials (BYOK) API =====

export const providerCredentialApi = {
  list: async (): Promise<CredentialDto[]> => {
    return request<CredentialDto[]>({ method: "GET", url: "/provider-credentials" });
  },

  store: async (data: StoreCredentialRequest): Promise<CredentialDto> => {
    return request<CredentialDto>({ method: "POST", url: "/provider-credentials", data });
  },

  verify: async (credentialId: string): Promise<{ credentialId: string; valid: boolean }> => {
    return request({ method: "POST", url: `/provider-credentials/${credentialId}/verify` });
  },

  disable: async (credentialId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/provider-credentials/${credentialId}/disable` });
  },

  rotate: async (credentialId: string): Promise<unknown> => {
    return request({ method: "POST", url: `/provider-credentials/${credentialId}/rotate` });
  },
};

// ===== Prompt Registry API =====

export const promptRegistryApi = {
  getActive: async (key: string, language?: string): Promise<PromptRegistry> => {
    return request<PromptRegistry>({ method: "GET", url: `/prompts/${key}/active`, params: language ? { language } : undefined });
  },

  listVersions: async (key: string): Promise<PromptRegistry[]> => {
    return request<PromptRegistry[]>({ method: "GET", url: `/prompts/${key}/versions` });
  },

  create: async (data: CreatePromptRequest): Promise<PromptRegistry> => {
    return request<PromptRegistry>({ method: "POST", url: "/prompts", data });
  },

  publish: async (id: string): Promise<PromptRegistry> => {
    return request<PromptRegistry>({ method: "POST", url: `/prompts/${id}/publish` });
  },

  archive: async (id: string): Promise<PromptRegistry> => {
    return request<PromptRegistry>({ method: "POST", url: `/prompts/${id}/archive` });
  },
};

// ===== Correction Dictionary API =====

export const correctionApi = {
  correct: async (data: { text: string; workspaceId?: string }): Promise<CorrectionResult> => {
    return request<CorrectionResult>({ method: "POST", url: "/correction/correct", data });
  },

  listEntries: async (params?: { workspaceId?: string; category?: string }): Promise<CorrectionDictionaryDto[]> => {
    return request<CorrectionDictionaryDto[]>({ method: "GET", url: "/correction/dictionary", params });
  },

  addEntry: async (data: AddCorrectionEntryRequest): Promise<CorrectionDictionaryDto> => {
    return request<CorrectionDictionaryDto>({ method: "POST", url: "/correction/dictionary", data });
  },

  deleteEntry: async (id: string): Promise<unknown> => {
    return request({ method: "DELETE", url: `/correction/dictionary/${id}` });
  },
};

// ===== LAN Nodes API =====

export const lanNodeApi = {
  list: async (): Promise<LanNode[]> => {
    return request<LanNode[]>({ method: "GET", url: "/audio/lan-nodes" });
  },

  discover: async (): Promise<LanNode[]> => {
    return request<LanNode[]>({ method: "POST", url: "/audio/lan-nodes/discover" });
  },

  register: async (data: RegisterLanNodeRequest): Promise<LanNode> => {
    return request<LanNode>({ method: "POST", url: "/audio/lan-nodes/register", data });
  },

  unregister: async (id: string): Promise<unknown> => {
    return request({ method: "DELETE", url: `/audio/lan-nodes/${id}` });
  },
};

// ===== Marketplace API =====

export const marketplaceApi = {
  browse: async (params?: { capability?: string; providerId?: string }): Promise<ProviderMarketplaceEntry[]> => {
    return request<ProviderMarketplaceEntry[]>({ method: "GET", url: "/audio/marketplace", params });
  },

  install: async (id: string): Promise<ProviderMarketplaceEntry> => {
    return request<ProviderMarketplaceEntry>({ method: "POST", url: `/audio/marketplace/${id}/install` });
  },

  uninstall: async (id: string): Promise<unknown> => {
    return request({ method: "DELETE", url: `/audio/marketplace/${id}/install` });
  },

  rate: async (id: string, rating: number): Promise<unknown> => {
    return request({ method: "POST", url: `/audio/marketplace/${id}/rate`, data: { rating } });
  },
};

// ===== Benchmark API =====

export const benchmarkApi = {
  run: async (modelRegistryId: string, datasetName?: string): Promise<BenchmarkResult> => {
    return request<BenchmarkResult>({ method: "POST", url: "/audio/benchmark/run", data: { modelRegistryId, datasetName } });
  },

  getResults: async (params?: { modelRegistryId?: string; benchmarkName?: string }): Promise<BenchmarkResult[]> => {
    return request<BenchmarkResult[]>({ method: "GET", url: "/audio/benchmark/results", params });
  },

  getRankings: async (category: string): Promise<RankingEntry[]> => {
    return request<RankingEntry[]>({ method: "GET", url: `/audio/benchmark/rankings/${category}` });
  },
};
