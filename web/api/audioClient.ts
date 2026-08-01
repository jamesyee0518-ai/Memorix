/**
 * Audio capability API client for the Memorix web frontend.
 *
 * Uses the same apiRequest wrapper pattern as mobile/src/api/client.ts:
 *   - Automatically attaches the Bearer token from localStorage
 *   - Sends the X-Memorix-Client-Version header
 *   - Handles FormData (multipart uploads) without forcing Content-Type
 *   - Unwraps the { success, data, error } envelope
 *   - On 401, attempts a token refresh and retries once
 *
 * All functions map directly to the backend controllers:
 *   TranscriptionController      -> api/transcription/*
 *   AudioCaptureController       -> api/audio/*
 *   TtsController                -> api/tts/*
 *   ProviderCredentialController -> api/provider-credentials/*
 *   AudioUsageController         -> api/audio/usage/*
 *   ModelRegistryController      -> api/audio/models/*
 *   BenchmarkController          -> api/audio/benchmark/*
 *   PromptRegistryController     -> api/prompts/*
 *   MarketplaceController        -> api/audio/marketplace/*
 *   CorrectionController         -> api/correction/*
 *   LanNodeController            -> api/audio/lan-nodes/*
 *   DeviceCapabilityController   -> api/device/*
 */

import { API_BASE_URL, CLIENT_VERSION } from "../config";
import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setAccessToken,
  setRefreshToken,
} from "../storage/auth";
import type {
  AddCorrectionEntryRequest,
  AsrProviderDescriptor,
  AsrRoutingContext,
  AudioAsset,
  AudioUploadParams,
  AudioUploadResponse,
  BenchmarkResult,
  CancelJobResponse,
  CapabilityUsageBreakdown,
  CorrectionDictionaryEntry,
  CorrectionResult,
  CorrectionRequest,
  CreateABTestRequest,
  CreateJobResponse,
  CreatePromptRequest,
  CreateTranscriptionJobRequest,
  DailyUsageBreakdown,
  DeviceCapabilityReport,
  DeviceCapabilityResult,
  DisableCredentialResponse,
  DisableModelResponse,
  EditSegmentRequest,
  EditSegmentResponse,
  LanNode,
  MergeAllSegmentsResponse,
  MergeSegmentResponse,
  ModelRegistry,
  PromptABTest,
  PromptRegistry,
  ProviderCredential,
  ProviderHealth,
  ProviderMarketplaceEntry,
  ProviderUsageBreakdown,
  ProviderUsageRecord,
  PublishSegmentResponse,
  RankingEntry,
  RegisterLanNodeRequest,
  RegisterModelRequest,
  RotateCredentialResponse,
  RoutingDecision,
  RunBenchmarkRequest,
  AudioUsageSummary,
  StoreCredentialRequest,
  TtsProviderDescriptor,
  TtsRequest,
  TtsResult,
  TranscriptionJob,
  TranscriptionSegment,
  TranscriptionStatusResponse,
  TranscriptionVersion,
  UpdateCorrectionEntryRequest,
  VerifyCredentialResponse,
  VoiceProfile,
} from "../types/audio";

// ═══════════════════════════════════════════════════════════════════════════
// CORE: apiRequest wrapper (mirrors mobile/src/api/client.ts)
// ═══════════════════════════════════════════════════════════════════════════

type ApiEnvelope<T> = {
  success: boolean;
  data?: T;
  error?: { code: string; message: string };
};

/**
 * Performs an authenticated API request, unwrapping the response envelope.
 * On 401, attempts a token refresh and retries the request once.
 */
export async function apiRequest<T>(
  path: string,
  init: RequestInit = {},
  retrying = false,
): Promise<T> {
  const token = getAccessToken();
  const headers: Record<string, string> = {
    Accept: "application/json",
    "X-Memorix-Client-Version": CLIENT_VERSION,
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };

  // Do not set Content-Type for FormData; the browser sets the boundary.
  if (!(init.body instanceof FormData)) {
    headers["Content-Type"] = "application/json";
  }

  // Merge caller-provided headers (caller wins).
  if (init.headers) {
    Object.assign(headers, init.headers as Record<string, string>);
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers,
  });

  // Attempt token refresh on 401 (but not for the refresh endpoint itself).
  if (response.status === 401 && !retrying && path !== "/auth/refresh") {
    const refreshed = await refreshAuthToken().catch(() => false);
    if (refreshed) {
      return apiRequest<T>(path, init, true);
    }
  }

  const body = (await response.json()) as ApiEnvelope<T>;

  if (!response.ok || body.success === false) {
    throw new Error(body.error?.message ?? `Request failed: ${response.status}`);
  }

  return body.data as T;
}

/**
 * Refreshes the access token using the stored refresh token.
 * Returns true on success, false otherwise.
 */
async function refreshAuthToken(): Promise<boolean> {
  const refreshToken = getRefreshToken();
  if (!refreshToken) return false;

  const response = await fetch(`${API_BASE_URL}/auth/refresh`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-Memorix-Client-Version": CLIENT_VERSION,
    },
    body: JSON.stringify({ refreshToken }),
  });

  const body = (await response.json()) as ApiEnvelope<{
    accessToken: string;
    refreshToken: string;
  }>;

  if (!response.ok || body.success === false || !body.data) {
    clearTokens();
    return false;
  }

  setAccessToken(body.data.accessToken);
  setRefreshToken(body.data.refreshToken);
  return true;
}

// ═══════════════════════════════════════════════════════════════════════════
// HELPER: build query string from key-value pairs
// ═══════════════════════════════════════════════════════════════════════════

function buildQuery(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== "") {
      search.append(key, String(value));
    }
  }
  const qs = search.toString();
  return qs ? `?${qs}` : "";
}

// ═══════════════════════════════════════════════════════════════════════════
// TRANSCRIPTION API  (TranscriptionController: api/transcription/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Creates a new transcription job for an existing audio asset.
 * POST api/transcription/jobs
 */
export function createJob(request: CreateTranscriptionJobRequest): Promise<CreateJobResponse> {
  return apiRequest<CreateJobResponse>("/transcription/jobs", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Gets the status and optional segments of a transcription job.
 * GET api/transcription/jobs/{jobId}
 */
export function getJobStatus(jobId: string): Promise<TranscriptionStatusResponse> {
  return apiRequest<TranscriptionStatusResponse>(`/transcription/jobs/${jobId}`);
}

/**
 * Cancels an in-progress transcription job.
 * POST api/transcription/jobs/{jobId}/cancel
 */
export function cancelJob(jobId: string): Promise<CancelJobResponse> {
  return apiRequest<CancelJobResponse>(`/transcription/jobs/${jobId}/cancel`, {
    method: "POST",
  });
}

/**
 * Lists transcription jobs for the current user.
 * GET api/transcription/jobs?status=&limit=&offset=
 */
export function listJobs(params?: {
  status?: string;
  limit?: number;
  offset?: number;
}): Promise<TranscriptionJob[]> {
  const query = buildQuery({
    status: params?.status,
    limit: params?.limit,
    offset: params?.offset,
  });
  return apiRequest<TranscriptionJob[]>(`/transcription/jobs${query}`);
}

/**
 * Gets transcription segments for a job.
 * GET api/transcription/jobs/{jobId}/segments
 */
export function getSegments(jobId: string): Promise<TranscriptionSegment[]> {
  return apiRequest<TranscriptionSegment[]>(`/transcription/jobs/${jobId}/segments`);
}

/**
 * Lists all available ASR providers and their capability descriptors.
 * GET api/transcription/providers
 */
export function listProviders(): Promise<AsrProviderDescriptor[]> {
  return apiRequest<AsrProviderDescriptor[]>("/transcription/providers");
}

/**
 * Explains the routing decision for a given ASR context (for debugging and UI).
 * POST api/transcription/routing/explain
 */
export function explainRouting(context: AsrRoutingContext): Promise<RoutingDecision> {
  return apiRequest<RoutingDecision>("/transcription/routing/explain", {
    method: "POST",
    body: JSON.stringify(context),
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// SEGMENT EDITING  (TranscriptionController: api/transcription/segments/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Updates the text of a single transcription segment (creates a USER_EDITED version).
 * PUT api/transcription/segments/{segmentId}
 */
export function editSegment(
  segmentId: string,
  request: EditSegmentRequest,
): Promise<EditSegmentResponse> {
  return apiRequest<EditSegmentResponse>(`/transcription/segments/${segmentId}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

/**
 * Gets the full version history for a segment (version tree).
 * GET api/transcription/segments/{segmentId}/versions
 */
export function getSegmentVersions(segmentId: string): Promise<TranscriptionVersion[]> {
  return apiRequest<TranscriptionVersion[]>(`/transcription/segments/${segmentId}/versions`);
}

/**
 * Performs a three-way merge for a segment, reconciling user edits
 * with server re-transcription against the original baseline.
 * POST api/transcription/segments/{segmentId}/merge
 */
export function mergeSegment(segmentId: string): Promise<MergeSegmentResponse> {
  return apiRequest<MergeSegmentResponse>(`/transcription/segments/${segmentId}/merge`, {
    method: "POST",
  });
}

/**
 * Bulk merge all segments for a transcription job.
 * POST api/transcription/jobs/{jobId}/merge-all
 */
export function mergeAllSegments(jobId: string): Promise<MergeAllSegmentsResponse> {
  return apiRequest<MergeAllSegmentsResponse>(`/transcription/jobs/${jobId}/merge-all`, {
    method: "POST",
  });
}

/**
 * Publishes the current version of a segment (marks it as PUBLISHED).
 * POST api/transcription/segments/{segmentId}/publish
 */
export function publishSegment(segmentId: string): Promise<PublishSegmentResponse> {
  return apiRequest<PublishSegmentResponse>(`/transcription/segments/${segmentId}/publish`, {
    method: "POST",
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// AUDIO CAPTURE  (AudioCaptureController: api/audio/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Uploads an audio file via multipart form data and optionally starts transcription.
 * POST api/audio/upload
 *
 * The backend accepts form fields: file, title, topicId, language,
 * enableVad, enableSpeakerDiarization, enablePunctuation, hotwordsJson,
 * dataClassification, preferredProviderId, preferredModelId,
 * fallbackPolicy, autoStart.
 */
export function uploadAudio(params: AudioUploadParams): Promise<AudioUploadResponse> {
  const formData = new FormData();
  formData.append("file", params.file);

  if (params.title) formData.append("title", params.title);
  if (params.topicId) formData.append("topicId", params.topicId);
  if (params.language) formData.append("language", params.language);

  formData.append("enableVad", String(params.enableVad));
  formData.append("enableSpeakerDiarization", String(params.enableSpeakerDiarization));
  formData.append("enablePunctuation", String(params.enablePunctuation));

  if (params.hotwords && params.hotwords.length > 0) {
    formData.append("hotwordsJson", JSON.stringify(params.hotwords));
  }

  formData.append("dataClassification", params.dataClassification);

  if (params.preferredProviderId) {
    formData.append("preferredProviderId", params.preferredProviderId);
  }
  if (params.preferredModelId) {
    formData.append("preferredModelId", params.preferredModelId);
  }

  formData.append("fallbackPolicy", params.fallbackPolicy);
  formData.append("autoStart", String(params.autoStart));

  return apiRequest<AudioUploadResponse>("/audio/upload", {
    method: "POST",
    body: formData,
  });
}

/**
 * Gets audio asset metadata by ID.
 * GET api/audio/assets/{assetId}
 */
export function getAudioAsset(assetId: string): Promise<AudioAsset> {
  return apiRequest<AudioAsset>(`/audio/assets/${assetId}`);
}

/**
 * Lists audio assets for the current user.
 * GET api/audio/assets?limit=&offset=
 */
export function listAudioAssets(params?: {
  limit?: number;
  offset?: number;
}): Promise<AudioAsset[]> {
  const query = buildQuery({
    limit: params?.limit,
    offset: params?.offset,
  });
  return apiRequest<AudioAsset[]>(`/audio/assets${query}`);
}

// ═══════════════════════════════════════════════════════════════════════════
// TTS  (TtsController: api/tts/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Synthesizes text to an audio file.
 * POST api/tts/synthesize
 */
export function synthesize(request: TtsRequest): Promise<TtsResult> {
  return apiRequest<TtsResult>("/tts/synthesize", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Lists all available TTS providers and their capability descriptors.
 * GET api/tts/providers
 */
export function listTtsProviders(): Promise<TtsProviderDescriptor[]> {
  return apiRequest<TtsProviderDescriptor[]>("/tts/providers");
}

/**
 * Lists available voice profiles for a specific TTS provider.
 * GET api/tts/voices?providerId=
 */
export function listVoices(providerId?: string): Promise<VoiceProfile[]> {
  const query = buildQuery({ providerId });
  return apiRequest<VoiceProfile[]>(`/tts/voices${query}`);
}

/**
 * Previews TTS with a short text sample (free, not recorded for billing).
 * POST api/tts/preview
 */
export function previewTts(request: TtsRequest): Promise<TtsResult> {
  return apiRequest<TtsResult>("/tts/preview", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Checks the health of a specific TTS provider.
 * GET api/tts/health/{providerId}
 */
export function ttsHealthCheck(providerId: string): Promise<ProviderHealth> {
  return apiRequest<ProviderHealth>(`/tts/health/${providerId}`);
}

// ═══════════════════════════════════════════════════════════════════════════
// BYOK CREDENTIALS  (ProviderCredentialController: api/provider-credentials/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Lists all credentials for the current user. Never returns the encrypted secret.
 * GET api/provider-credentials
 */
export function listCredentials(): Promise<ProviderCredential[]> {
  return apiRequest<ProviderCredential[]>("/provider-credentials");
}

/**
 * Stores a new provider credential with AES-GCM encryption.
 * POST api/provider-credentials
 */
export function createCredential(
  request: Omit<StoreCredentialRequest, "ownerType" | "ownerId">,
): Promise<ProviderCredential> {
  return apiRequest<ProviderCredential>("/provider-credentials", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Deletes (disables) a credential without removing it permanently.
 * POST api/provider-credentials/{credentialId}/disable
 */
export function deleteCredential(
  credentialId: string,
): Promise<DisableCredentialResponse> {
  return apiRequest<DisableCredentialResponse>(
    `/provider-credentials/${credentialId}/disable`,
    { method: "POST" },
  );
}

/**
 * Verifies a credential by making a lightweight test call.
 * POST api/provider-credentials/{credentialId}/verify
 */
export function verifyCredential(
  credentialId: string,
): Promise<VerifyCredentialResponse> {
  return apiRequest<VerifyCredentialResponse>(
    `/provider-credentials/${credentialId}/verify`,
    { method: "POST" },
  );
}

/**
 * Rotates the encryption key for a credential.
 * POST api/provider-credentials/{credentialId}/rotate
 */
export function rotateCredential(
  credentialId: string,
): Promise<RotateCredentialResponse> {
  return apiRequest<RotateCredentialResponse>(
    `/provider-credentials/${credentialId}/rotate`,
    { method: "POST" },
  );
}

// ═══════════════════════════════════════════════════════════════════════════
// AUDIO USAGE  (AudioUsageController: api/audio/usage/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Gets usage records for the current user within an optional date range.
 * GET api/audio/usage?from=&to=&limit=&offset=
 */
export function getUsage(params?: {
  from?: string;
  to?: string;
  limit?: number;
  offset?: number;
}): Promise<ProviderUsageRecord[]> {
  const query = buildQuery({
    from: params?.from,
    to: params?.to,
    limit: params?.limit,
    offset: params?.offset,
  });
  return apiRequest<ProviderUsageRecord[]>(`/audio/usage${query}`);
}

/**
 * Gets a usage summary for the current user: total cost, duration, requests,
 * and per-provider breakdown.
 * GET api/audio/usage/summary?from=&to=
 */
export function getUsageSummary(params?: {
  from?: string;
  to?: string;
}): Promise<AudioUsageSummary> {
  const query = buildQuery({
    from: params?.from,
    to: params?.to,
  });
  return apiRequest<AudioUsageSummary>(`/audio/usage/summary${query}`);
}

/**
 * Gets a per-provider usage breakdown for the current user.
 * GET api/audio/usage/by-provider?from=&to=
 */
export function getUsageByProvider(params?: {
  from?: string;
  to?: string;
}): Promise<ProviderUsageBreakdown[]> {
  const query = buildQuery({
    from: params?.from,
    to: params?.to,
  });
  return apiRequest<ProviderUsageBreakdown[]>(`/audio/usage/by-provider${query}`);
}

/**
 * Gets a per-capability usage breakdown for the current user.
 * GET api/audio/usage/by-capability?from=&to=
 */
export function getUsageByCapability(params?: {
  from?: string;
  to?: string;
}): Promise<CapabilityUsageBreakdown[]> {
  const query = buildQuery({
    from: params?.from,
    to: params?.to,
  });
  return apiRequest<CapabilityUsageBreakdown[]>(
    `/audio/usage/by-capability${query}`,
  );
}

/**
 * Gets daily usage chart data for the current user.
 * GET api/audio/usage/daily?from=&to=
 */
export function getDailyUsage(params?: {
  from?: string;
  to?: string;
}): Promise<DailyUsageBreakdown[]> {
  const query = buildQuery({
    from: params?.from,
    to: params?.to,
  });
  return apiRequest<DailyUsageBreakdown[]>(`/audio/usage/daily${query}`);
}

// ═══════════════════════════════════════════════════════════════════════════
// MODEL REGISTRY  (ModelRegistryController: api/audio/models/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Lists registered models with optional filters.
 * GET api/audio/models?capability=&providerId=&enabledOnly=
 */
export function listModels(params?: {
  capability?: string;
  providerId?: string;
  enabledOnly?: boolean;
}): Promise<ModelRegistry[]> {
  const query = buildQuery({
    capability: params?.capability,
    providerId: params?.providerId,
    enabledOnly: params?.enabledOnly,
  });
  return apiRequest<ModelRegistry[]>(`/audio/models${query}`);
}

/**
 * Gets a single model registration by ID.
 * GET api/audio/models/{id}
 */
export function getModel(id: string): Promise<ModelRegistry> {
  return apiRequest<ModelRegistry>(`/audio/models/${id}`);
}

/**
 * Registers a new model in the registry.
 * POST api/audio/models
 */
export function createModel(request: RegisterModelRequest): Promise<ModelRegistry> {
  return apiRequest<ModelRegistry>("/audio/models", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Updates an existing model registration.
 * PUT api/audio/models/{id}
 */
export function updateModel(
  id: string,
  request: RegisterModelRequest,
): Promise<ModelRegistry> {
  return apiRequest<ModelRegistry>(`/audio/models/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

/**
 * Disables a model registration (soft delete).
 * DELETE api/audio/models/{id}
 */
export function deleteModel(id: string): Promise<DisableModelResponse> {
  return apiRequest<DisableModelResponse>(`/audio/models/${id}`, {
    method: "DELETE",
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// BENCHMARK  (BenchmarkController: api/audio/benchmark/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Runs a benchmark on the specified model.
 * POST api/audio/benchmark/run
 */
export function runBenchmark(request: RunBenchmarkRequest): Promise<BenchmarkResult> {
  return apiRequest<BenchmarkResult>("/audio/benchmark/run", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Retrieves benchmark results with optional filters.
 * GET api/audio/benchmark/results?modelRegistryId=&benchmarkName=
 */
export function listResults(params?: {
  modelRegistryId?: string;
  benchmarkName?: string;
}): Promise<BenchmarkResult[]> {
  const query = buildQuery({
    modelRegistryId: params?.modelRegistryId,
    benchmarkName: params?.benchmarkName,
  });
  return apiRequest<BenchmarkResult[]>(`/audio/benchmark/results${query}`);
}

/**
 * Produces a ranked leaderboard of models for the given category.
 * Valid categories: fastest, most_accurate, lowest_cost, best_chinese,
 * best_mobile, best_meeting.
 * GET api/audio/benchmark/rankings/{category}
 */
export function getRankings(
  category: string,
): Promise<RankingEntry[]> {
  return apiRequest<RankingEntry[]>(
    `/audio/benchmark/rankings/${category}`,
  );
}

// ═══════════════════════════════════════════════════════════════════════════
// PROMPT REGISTRY  (PromptRegistryController: api/prompts/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Gets the active (published) prompt for the given key.
 * GET api/prompts/{key}/active?language=
 */
export function getPrompt(
  key: string,
  language?: string,
): Promise<PromptRegistry> {
  const query = buildQuery({ language });
  return apiRequest<PromptRegistry>(`/prompts/${key}/active${query}`);
}

/**
 * Lists all versions of a prompt by key.
 * GET api/prompts/{key}/versions
 */
export function listPrompts(key: string): Promise<PromptRegistry[]> {
  return apiRequest<PromptRegistry[]>(`/prompts/${key}/versions`);
}

/**
 * Creates a new prompt version in draft status.
 * POST api/prompts
 */
export function createPrompt(request: CreatePromptRequest): Promise<PromptRegistry> {
  return apiRequest<PromptRegistry>("/prompts", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Updates an existing prompt version.
 * PUT api/prompts/{id}
 */
export function updatePrompt(
  id: string,
  request: Partial<CreatePromptRequest>,
): Promise<PromptRegistry> {
  return apiRequest<PromptRegistry>(`/prompts/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

/**
 * Publishes a draft prompt, activating it and archiving the previous active version.
 * POST api/prompts/{id}/publish
 */
export function publishPrompt(id: string): Promise<PromptRegistry> {
  return apiRequest<PromptRegistry>(`/prompts/${id}/publish`, {
    method: "POST",
  });
}

/**
 * Archives a published prompt, deactivating it.
 * POST api/prompts/{id}/archive
 */
export function archivePrompt(id: string): Promise<PromptRegistry> {
  return apiRequest<PromptRegistry>(`/prompts/${id}/archive`, {
    method: "POST",
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// PROMPT A/B TESTS  (PromptRegistryController: api/prompts/abtest/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Lists all A/B tests.
 * GET api/prompts/abtest
 */
export function listTests(): Promise<PromptABTest[]> {
  return apiRequest<PromptABTest[]>("/prompts/abtest");
}

/**
 * Creates a new A/B test in "created" status.
 * POST api/prompts/abtest
 */
export function createTest(request: CreateABTestRequest): Promise<PromptABTest> {
  return apiRequest<PromptABTest>("/prompts/abtest", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Updates an existing A/B test configuration.
 * PUT api/prompts/abtest/{id}
 */
export function updateTest(
  id: string,
  request: Partial<CreateABTestRequest>,
): Promise<PromptABTest> {
  return apiRequest<PromptABTest>(`/prompts/abtest/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

/**
 * Starts an A/B test, transitioning it to "running" status.
 * POST api/prompts/abtest/{id}/start
 */
export function startTest(id: string): Promise<PromptABTest> {
  return apiRequest<PromptABTest>(`/prompts/abtest/${id}/start`, {
    method: "POST",
  });
}

/**
 * Completes an A/B test, recording the winning variant.
 * POST api/prompts/abtest/{id}/complete
 */
export function completeTest(
  id: string,
  winnerVariantId: string,
): Promise<{ testId: string; winnerVariantId: string; status: string }> {
  return apiRequest(`/prompts/abtest/${id}/complete`, {
    method: "POST",
    body: JSON.stringify({ winnerVariantId }),
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// MARKETPLACE  (MarketplaceController: api/audio/marketplace/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Browses marketplace entries with optional capability and provider filters.
 * GET api/audio/marketplace?capability=&providerId=
 */
export function listEntries(params?: {
  capability?: string;
  providerId?: string;
}): Promise<ProviderMarketplaceEntry[]> {
  const query = buildQuery({
    capability: params?.capability,
    providerId: params?.providerId,
  });
  return apiRequest<ProviderMarketplaceEntry[]>(`/audio/marketplace${query}`);
}

/**
 * Installs a marketplace entry by ID.
 * POST api/audio/marketplace/{id}/install
 */
export function installEntry(id: string): Promise<ProviderMarketplaceEntry> {
  return apiRequest<ProviderMarketplaceEntry>(`/audio/marketplace/${id}/install`, {
    method: "POST",
  });
}

/**
 * Uninstalls a marketplace entry by ID.
 * DELETE api/audio/marketplace/{id}/install
 */
export function uninstallEntry(
  id: string,
): Promise<{ id: string; status: string }> {
  return apiRequest(`/audio/marketplace/${id}/install`, {
    method: "DELETE",
  });
}

/**
 * Rates a marketplace entry. Rating must be between 0 and 5.
 * POST api/audio/marketplace/{id}/rate
 */
export function rateEntry(
  id: string,
  rating: number,
): Promise<{ id: string; rating: number }> {
  return apiRequest(`/audio/marketplace/${id}/rate`, {
    method: "POST",
    body: JSON.stringify({ rating }),
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// CORRECTION DICTIONARY  (CorrectionController: api/correction/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Lists correction dictionary entries, optionally filtered by category.
 * GET api/correction/dictionary?workspaceId=&category=
 */
export function listCorrectionEntries(params?: {
  workspaceId?: string;
  category?: string;
}): Promise<CorrectionDictionaryEntry[]> {
  const query = buildQuery({
    workspaceId: params?.workspaceId,
    category: params?.category,
  });
  return apiRequest<CorrectionDictionaryEntry[]>(`/correction/dictionary${query}`);
}

/**
 * Adds a new entry to the correction dictionary.
 * POST api/correction/dictionary
 */
export function addCorrectionEntry(
  request: AddCorrectionEntryRequest,
): Promise<CorrectionDictionaryEntry> {
  return apiRequest<CorrectionDictionaryEntry>("/correction/dictionary", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Updates an existing correction dictionary entry.
 * PUT api/correction/dictionary/{id}
 */
export function updateCorrectionEntry(
  id: string,
  request: UpdateCorrectionEntryRequest,
): Promise<CorrectionDictionaryEntry> {
  return apiRequest<CorrectionDictionaryEntry>(`/correction/dictionary/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

/**
 * Deletes (deactivates) a correction dictionary entry by ID.
 * DELETE api/correction/dictionary/{id}
 */
export function deleteCorrectionEntry(
  id: string,
): Promise<{ id: string; deleted: boolean }> {
  return apiRequest(`/correction/dictionary/${id}`, {
    method: "DELETE",
  });
}

/**
 * Corrects transcription text using workspace dictionary entries and
 * built-in correction rules.
 * POST api/correction/correct
 */
export function correctText(request: CorrectionRequest): Promise<CorrectionResult> {
  return apiRequest<CorrectionResult>("/correction/correct", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// LAN NODES  (LanNodeController: api/audio/lan-nodes/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Lists all registered LAN nodes.
 * GET api/audio/lan-nodes
 */
export function listNodes(): Promise<LanNode[]> {
  return apiRequest<LanNode[]>("/audio/lan-nodes");
}

/**
 * Registers a LAN node at the given endpoint URL.
 * POST api/audio/lan-nodes/register
 */
export function registerNode(request: RegisterLanNodeRequest): Promise<LanNode> {
  return apiRequest<LanNode>("/audio/lan-nodes/register", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/**
 * Updates the status of a LAN node (e.g. online, offline, health_checking).
 * PUT api/audio/lan-nodes/{id}/status
 */
export function updateNodeStatus(
  id: string,
  nodeStatus: string,
): Promise<LanNode> {
  return apiRequest<LanNode>(`/audio/lan-nodes/${id}/status`, {
    method: "PUT",
    body: JSON.stringify({ nodeStatus }),
  });
}

/**
 * Triggers LAN node discovery by probing configured endpoints.
 * POST api/audio/lan-nodes/discover
 */
export function discoverNodes(): Promise<LanNode[]> {
  return apiRequest<LanNode[]>("/audio/lan-nodes/discover", {
    method: "POST",
  });
}

/**
 * Unregisters (removes) a LAN node by ID.
 * DELETE api/audio/lan-nodes/{id}
 */
export function unregisterNode(
  id: string,
): Promise<{ id: string; status: string }> {
  return apiRequest(`/audio/lan-nodes/${id}`, {
    method: "DELETE",
  });
}

// ═══════════════════════════════════════════════════════════════════════════
// DEVICE CAPABILITY  (DeviceCapabilityController: api/device/*)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Detects server-side device capabilities by inferring from the host environment.
 * GET api/device/capability
 */
export function getCapabilities(): Promise<DeviceCapabilityResult> {
  return apiRequest<DeviceCapabilityResult>("/device/capability");
}

/**
 * Reports client-side device capabilities and receives a server-determined
 * recommendation for audio processing mode.
 * POST api/device/capability
 */
export function reportCapabilities(
  report: DeviceCapabilityReport,
): Promise<DeviceCapabilityResult> {
  return apiRequest<DeviceCapabilityResult>("/device/capability", {
    method: "POST",
    body: JSON.stringify(report),
  });
}
