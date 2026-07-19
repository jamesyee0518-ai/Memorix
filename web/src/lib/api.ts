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
  Tag,
  Terminology,
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
  Workspace,
  CreateWorkspaceInput,
  InitLocalWorkspaceInput,
  UpdateWorkspaceInput,
  WorkspaceModeOption,
  ModelProviderOption,
  CloudInboxSettings,
  UpdateCloudInboxSettingsInput,
  CloudInboxStatus,
  CloudInboxPullInput,
  CloudInboxPullResult,
  CloudInboxSyncLog,
  CloudAccountBinding,
  WorkspaceBinding,
  OAuthStartInput,
  OAuthStartResult,
  OAuthStatus,
  CreateWorkspaceBindingInput,
  MobileDevice,
  PushNotification,
  LocalConfig,
  RuntimeHealth,
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

// ===== 标签 API =====

export const terminologyApi = {
  list(query?: string): Promise<Terminology[]> {
    return request<Terminology[]>({ method: "GET", url: "/terminology", params: { query } });
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
  health(): Promise<RuntimeHealth> {
    return request<RuntimeHealth>({ method: "GET", url: "/runtime/health" });
  },
  detectLocalModels(): Promise<LocalModelDetection> {
    return request<LocalModelDetection>({
      method: "GET",
      url: "/runtime/local-models",
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
