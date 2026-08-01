-- ============================================================================
-- Memorix Audio Capability Integration – Phase 6 Schema Migration (SQLite)
-- Creates 15 audio-related tables and extends documents/document_chunks/inbox_items.
-- Uses AddColumnIfNotExists pattern for idempotency on SQLite.
-- ============================================================================

PRAGMA foreign_keys = ON;
BEGIN IMMEDIATE;

-- ── 1. audio_assets ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS audio_assets (
    Id                    TEXT PRIMARY KEY,
    SourceId              TEXT NOT NULL,
    WorkspaceId           TEXT NULL,
    UserId                TEXT NULL,
    OriginalFilePath      TEXT NOT NULL,
    NormalizedFilePath    TEXT NULL,
    SourceSha256          TEXT NOT NULL,
    FileSizeBytes         INTEGER NOT NULL DEFAULT 0,
    MimeType              TEXT NOT NULL DEFAULT 'audio/wav',
    DurationMs            INTEGER NOT NULL DEFAULT 0,
    SampleRate            INTEGER NOT NULL DEFAULT 0,
    Channels              INTEGER NOT NULL DEFAULT 0,
    DataClassification    TEXT NOT NULL DEFAULT 'INTERNAL',
    AllowsOffDevice       INTEGER NOT NULL DEFAULT 1 CHECK (AllowsOffDevice IN (0,1)),
    CreatedAt             TEXT NOT NULL,
    UpdatedAt             TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_audio_assets_sha256     ON audio_assets(SourceSha256);
CREATE INDEX IF NOT EXISTS ix_audio_assets_ws_created ON audio_assets(WorkspaceId, CreatedAt);

-- ── 2. transcription_jobs ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS transcription_jobs (
    Id                         TEXT PRIMARY KEY,
    AudioAssetId               TEXT NOT NULL,
    WorkspaceId                TEXT NULL,
    UserId                     TEXT NOT NULL,
    ExecutionMode              TEXT NOT NULL DEFAULT 'LOCAL_DEVICE',
    CredentialMode             TEXT NOT NULL DEFAULT 'NO_CREDENTIAL',
    ProviderId                 TEXT NOT NULL,
    ModelId                    TEXT NOT NULL,
    FallbackPolicy             TEXT NOT NULL DEFAULT 'STOP',
    Language                   TEXT NULL,
    EnableVad                  INTEGER NOT NULL DEFAULT 1 CHECK (EnableVad IN (0,1)),
    EnableSpeakerDiarization   INTEGER NOT NULL DEFAULT 0 CHECK (EnableSpeakerDiarization IN (0,1)),
    EnablePunctuation          INTEGER NOT NULL DEFAULT 1 CHECK (EnablePunctuation IN (0,1)),
    Hotwords                   TEXT NULL,
    EstimatedCost              REAL NULL,
    ActualCost                 REAL NULL,
    Status                     TEXT NOT NULL DEFAULT 'pending',
    ErrorMessage               TEXT NULL,
    DocumentId                 TEXT NULL,
    SegmentCount               INTEGER NULL,
    CreatedAt                  TEXT NOT NULL,
    StartedAt                  TEXT NULL,
    CompletedAt                TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_tj_asset         ON transcription_jobs(AudioAssetId);
CREATE INDEX IF NOT EXISTS ix_tj_ws_status    ON transcription_jobs(WorkspaceId, Status);
CREATE INDEX IF NOT EXISTS ix_tj_user_created ON transcription_jobs(UserId, CreatedAt);

-- ── 3. transcription_segments ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS transcription_segments (
    Id                   TEXT PRIMARY KEY,
    TranscriptionJobId   TEXT NOT NULL,
    DocumentId           TEXT NULL,
    WorkspaceId          TEXT NULL,
    SegmentUuid          TEXT NOT NULL,
    SourceStartMs        INTEGER NOT NULL DEFAULT 0,
    SourceEndMs          INTEGER NOT NULL DEFAULT 0,
    ProviderId           TEXT NOT NULL,
    ModelId              TEXT NOT NULL,
    Confidence           REAL NOT NULL DEFAULT 0,
    SpeakerKey           TEXT NULL,
    Text                 TEXT NOT NULL DEFAULT '',
    Version              TEXT NOT NULL DEFAULT 'RAW_MODEL',
    SegmentIndex         INTEGER NOT NULL DEFAULT 0,
    CreatedAt            TEXT NOT NULL,
    UpdatedAt            TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ts_job          ON transcription_segments(TranscriptionJobId);
CREATE INDEX IF NOT EXISTS ix_ts_uuid_version ON transcription_segments(SegmentUuid, Version);
CREATE INDEX IF NOT EXISTS ix_ts_document     ON transcription_segments(DocumentId);

-- ── 4. transcription_versions ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS transcription_versions (
    Id                   TEXT PRIMARY KEY,
    TranscriptionJobId   TEXT NOT NULL,
    SegmentUuid          TEXT NOT NULL,
    Version              TEXT NOT NULL DEFAULT 'RAW_MODEL',
    ParentVersionId      TEXT NULL,
    Text                 TEXT NOT NULL DEFAULT '',
    ProviderId           TEXT NOT NULL,
    ModelId              TEXT NOT NULL,
    CreatedBy            TEXT NULL,
    CreatedAt            TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_tv_job           ON transcription_versions(TranscriptionJobId);
CREATE INDEX IF NOT EXISTS ix_tv_uuid_version  ON transcription_versions(SegmentUuid, Version);
CREATE INDEX IF NOT EXISTS ix_tv_parent         ON transcription_versions(ParentVersionId);

-- ── 5. provider_credentials ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS provider_credentials (
    Id                TEXT PRIMARY KEY,
    TenantId          TEXT NULL,
    OwnerType         TEXT NOT NULL DEFAULT 'user',
    OwnerId           TEXT NOT NULL,
    ProviderId        TEXT NOT NULL,
    CredentialType    TEXT NOT NULL DEFAULT 'api_key',
    EncryptedSecret   TEXT NOT NULL,
    KeyVersion        TEXT NOT NULL DEFAULT 'v1',
    Status            TEXT NOT NULL DEFAULT 'active',
    LastVerifiedAt    TEXT NULL,
    ExpiresAt         TEXT NULL,
    Label             TEXT NULL,
    CreatedAt         TEXT NOT NULL,
    UpdatedAt         TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pc_provider_owner ON provider_credentials(ProviderId, OwnerType, OwnerId, Status);
CREATE INDEX IF NOT EXISTS ix_pc_tenant         ON provider_credentials(TenantId);

-- ── 6. provider_usage_records ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS provider_usage_records (
    Id                   TEXT PRIMARY KEY,
    TenantId             TEXT NULL,
    UserId               TEXT NOT NULL,
    WorkspaceId          TEXT NULL,
    Capability           TEXT NOT NULL,
    ProviderId           TEXT NOT NULL,
    ModelId              TEXT NOT NULL,
    CredentialMode       TEXT NOT NULL DEFAULT 'NO_CREDENTIAL',
    ExecutionMode        TEXT NOT NULL DEFAULT 'LOCAL_DEVICE',
    DurationMs           INTEGER NOT NULL DEFAULT 0,
    RequestCount         INTEGER NOT NULL DEFAULT 1,
    InputUnits           REAL NULL,
    OutputUnits          REAL NULL,
    EstimatedCost        REAL NULL,
    ActualCost           REAL NULL,
    Status               TEXT NOT NULL DEFAULT 'success',
    ErrorMessage         TEXT NULL,
    TranscriptionJobId   TEXT NULL,
    CreatedAt            TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pur_user_created ON provider_usage_records(UserId, CreatedAt);
CREATE INDEX IF NOT EXISTS ix_pur_tenant      ON provider_usage_records(TenantId, CreatedAt);
CREATE INDEX IF NOT EXISTS ix_pur_job          ON provider_usage_records(TranscriptionJobId);

-- ── 7. voice_clone_consents ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS voice_clone_consents (
    Id              TEXT PRIMARY KEY,
    UserId          TEXT NOT NULL,
    VoiceId         TEXT NOT NULL,
    ConsentStatus   TEXT NOT NULL DEFAULT 'pending',
    ConsentMethod   TEXT NULL,
    GrantedAt       TEXT NULL,
    RevokedAt       TEXT NULL,
    CreatedAt       TEXT NOT NULL,
    UpdatedAt       TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_vcc_user_voice  ON voice_clone_consents(UserId, VoiceId);
CREATE INDEX IF NOT EXISTS ix_vcc_status      ON voice_clone_consents(ConsentStatus);

-- ── 8. correction_dictionaries ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS correction_dictionaries (
    Id              TEXT PRIMARY KEY,
    WorkspaceId     TEXT NULL,
    OriginalText    TEXT NOT NULL,
    CorrectedText   TEXT NOT NULL,
    Category        TEXT NOT NULL DEFAULT 'custom',
    Language        TEXT NULL,
    CreatedBy       TEXT NULL,
    IsActive        INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    CreatedAt       TEXT NOT NULL,
    UpdatedAt       TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_cd_ws_cat_active ON correction_dictionaries(WorkspaceId, Category, IsActive);
CREATE INDEX IF NOT EXISTS ix_cd_workspace     ON correction_dictionaries(WorkspaceId);

-- ── 9. model_registries ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS model_registries (
    Id                       TEXT PRIMARY KEY,
    ProviderId               TEXT NOT NULL,
    ModelId                  TEXT NOT NULL,
    DisplayName              TEXT NOT NULL,
    Capability               TEXT NOT NULL,
    ExecutionModes           TEXT NOT NULL DEFAULT '',
    CredentialModes          TEXT NOT NULL DEFAULT '',
    SupportedLanguages       TEXT NOT NULL DEFAULT '',
    MaxFileBytes             INTEGER NULL,
    MaxAudioDurationMs       INTEGER NULL,
    AcceptedMimeTypes        TEXT NOT NULL DEFAULT '',
    SupportsStreaming        INTEGER NOT NULL DEFAULT 0 CHECK (SupportsStreaming IN (0,1)),
    SupportsBatch            INTEGER NOT NULL DEFAULT 1 CHECK (SupportsBatch IN (0,1)),
    SupportsVad              INTEGER NOT NULL DEFAULT 0 CHECK (SupportsVad IN (0,1)),
    SupportsPunctuation      INTEGER NOT NULL DEFAULT 0 CHECK (SupportsPunctuation IN (0,1)),
    SupportsDiarization      INTEGER NOT NULL DEFAULT 0 CHECK (SupportsDiarization IN (0,1)),
    SupportsHotwords         INTEGER NOT NULL DEFAULT 0 CHECK (SupportsHotwords IN (0,1)),
    SupportsWordTimestamp    INTEGER NOT NULL DEFAULT 0 CHECK (SupportsWordTimestamp IN (0,1)),
    SupportsSegmentTimestamp INTEGER NOT NULL DEFAULT 1 CHECK (SupportsSegmentTimestamp IN (0,1)),
    SendsAudioOffDevice      INTEGER NOT NULL DEFAULT 0 CHECK (SendsAudioOffDevice IN (0,1)),
    StoresProviderData       INTEGER NOT NULL DEFAULT 0 CHECK (StoresProviderData IN (0,1)),
    PricingUnit              TEXT NULL,
    DataRegion               TEXT NULL,
    RetentionPolicy          TEXT NULL,
    IsEnabled                INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0,1)),
    HealthStatus             TEXT NOT NULL DEFAULT 'unknown',
    LastHealthCheckAt        TEXT NULL,
    CreatedAt                TEXT NOT NULL,
    UpdatedAt                TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_mr_provider_model ON model_registries(ProviderId, ModelId);
CREATE INDEX IF NOT EXISTS ix_mr_capability     ON model_registries(Capability);
CREATE INDEX IF NOT EXISTS ix_mr_enabled_cap    ON model_registries(IsEnabled, Capability);

-- ── 10. benchmark_results ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS benchmark_results (
    Id                    TEXT PRIMARY KEY,
    ModelRegistryId       TEXT NOT NULL,
    BenchmarkName         TEXT NOT NULL,
    Cer                   REAL NOT NULL DEFAULT 0,
    Wer                   REAL NOT NULL DEFAULT 0,
    Rtf                   REAL NOT NULL DEFAULT 0,
    GpuMemoryMb           INTEGER NULL,
    CpuMemoryMb           INTEGER NULL,
    Ttfb                  INTEGER NOT NULL DEFAULT 0,
    Throughput            REAL NOT NULL DEFAULT 0,
    ProperNounAccuracy    REAL NULL,
    TimestampDeviationMs  REAL NULL,
    SpeakerAccuracy       REAL NULL,
    UserModificationRate  REAL NULL,
    UnitCost              REAL NOT NULL DEFAULT 0,
    EvaluatedAt           TEXT NOT NULL,
    DatasetName           TEXT NULL,
    Notes                 TEXT NULL,
    CreatedAt             TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_br_model     ON benchmark_results(ModelRegistryId);
CREATE INDEX IF NOT EXISTS ix_br_bench     ON benchmark_results(BenchmarkName, EvaluatedAt);
CREATE INDEX IF NOT EXISTS ix_br_dataset   ON benchmark_results(DatasetName);

-- ── 11. prompt_registries ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS prompt_registries (
    Id                    TEXT PRIMARY KEY,
    PromptKey             TEXT NOT NULL,
    Version               TEXT NOT NULL,
    Title                 TEXT NOT NULL,
    Description           TEXT NULL,
    SystemPrompt          TEXT NOT NULL,
    UserPromptTemplate    TEXT NOT NULL,
    Language              TEXT NULL,
    ProviderCompatibility TEXT NOT NULL DEFAULT '',
    EvaluationScore       REAL NULL,
    IsActive              INTEGER NOT NULL DEFAULT 0 CHECK (IsActive IN (0,1)),
    Status                TEXT NOT NULL DEFAULT 'draft',
    PublishedAt           TEXT NULL,
    CreatedBy             TEXT NOT NULL DEFAULT '',
    CreatedAt             TEXT NOT NULL,
    UpdatedAt             TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pr_key_status_active ON prompt_registries(PromptKey, Status, IsActive);
CREATE INDEX IF NOT EXISTS ix_pr_language          ON prompt_registries(Language);

-- ── 12. prompt_ab_tests ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS prompt_ab_tests (
    Id                   TEXT PRIMARY KEY,
    PromptKey            TEXT NOT NULL,
    Name                 TEXT NOT NULL,
    VariantAId           TEXT NOT NULL,
    VariantBId           TEXT NOT NULL,
    TrafficSplitPercent  INTEGER NOT NULL DEFAULT 0,
    Status               TEXT NOT NULL DEFAULT 'created',
    WinnerVariantId      TEXT NULL,
    StartDate            TEXT NOT NULL,
    EndDate              TEXT NULL,
    CreatedBy            TEXT NOT NULL DEFAULT '',
    CreatedAt            TEXT NOT NULL,
    UpdatedAt            TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pat_key    ON prompt_ab_tests(PromptKey);
CREATE INDEX IF NOT EXISTS ix_pat_status  ON prompt_ab_tests(Status);

-- ── 13. enterprise_policies ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS enterprise_policies (
    Id            TEXT PRIMARY KEY,
    WorkspaceId   TEXT NULL,
    PolicyName    TEXT NOT NULL,
    PolicyType    TEXT NOT NULL DEFAULT 'provider_restriction',
    RulesJson     TEXT NOT NULL DEFAULT '{}',
    Priority      INTEGER NOT NULL DEFAULT 0,
    IsEnabled     INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0,1)),
    CreatedBy     TEXT NOT NULL DEFAULT '',
    CreatedAt     TEXT NOT NULL,
    UpdatedAt     TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ep_ws_type_enabled ON enterprise_policies(WorkspaceId, PolicyType, IsEnabled);
CREATE INDEX IF NOT EXISTS ix_ep_type            ON enterprise_policies(PolicyType);

-- ── 14. lan_nodes ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS lan_nodes (
    Id                   TEXT PRIMARY KEY,
    NodeName             TEXT NOT NULL,
    EndpointUrl          TEXT NOT NULL,
    NodeStatus           TEXT NOT NULL DEFAULT 'online',
    Capabilities         TEXT NOT NULL DEFAULT '',
    ProviderIds          TEXT NOT NULL DEFAULT '',
    AvailableGpuMemory   INTEGER NULL,
    CpuCores             INTEGER NULL,
    LastHeartbeatAt      TEXT NULL,
    RegisteredAt         TEXT NOT NULL,
    UpdatedAt            TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_ln_endpoint ON lan_nodes(EndpointUrl);
CREATE INDEX IF NOT EXISTS ix_ln_status         ON lan_nodes(NodeStatus);

-- ── 15. provider_marketplace_entries ────────────────────────────────────
CREATE TABLE IF NOT EXISTS provider_marketplace_entries (
    Id                  TEXT PRIMARY KEY,
    Name                TEXT NOT NULL,
    Description         TEXT NULL,
    ProviderId          TEXT NOT NULL,
    Capability          TEXT NOT NULL,
    ExecutionMode       TEXT NOT NULL DEFAULT '',
    CredentialMode      TEXT NOT NULL DEFAULT '',
    SupportedLanguages  TEXT NOT NULL DEFAULT '',
    PricingUnit         TEXT NULL,
    IsOfficial          INTEGER NOT NULL DEFAULT 0 CHECK (IsOfficial IN (0,1)),
    Version             TEXT NOT NULL DEFAULT '',
    Rating              REAL NOT NULL DEFAULT 0,
    InstallCount        INTEGER NOT NULL DEFAULT 0,
    IsInstalled         INTEGER NOT NULL DEFAULT 0 CHECK (IsInstalled IN (0,1)),
    AuthorName          TEXT NOT NULL DEFAULT '',
    AuthorUrl           TEXT NULL,
    TagsJson            TEXT NOT NULL DEFAULT '[]',
    CreatedAt           TEXT NOT NULL,
    UpdatedAt           TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pme_capability  ON provider_marketplace_entries(Capability);
CREATE INDEX IF NOT EXISTS ix_pme_provider   ON provider_marketplace_entries(ProviderId);
CREATE INDEX IF NOT EXISTS ix_pme_installed   ON provider_marketplace_entries(IsInstalled);

-- ── 16. Extend existing tables ──────────────────────────────────────────
-- SQLite does not support IF NOT EXISTS on ADD COLUMN; each ALTER is a no-op
-- if the column already exists, but errors on re-run. Use the app's
-- SqliteInitializer.AddColumnIfNotExistsAsync for runtime safety.
-- These statements are for fresh-database migration only.

ALTER TABLE documents ADD COLUMN AudioAssetId TEXT NULL;
ALTER TABLE documents ADD COLUMN TranscriptionJobId TEXT NULL;
ALTER TABLE documents ADD COLUMN AudioDurationMs INTEGER NULL;

ALTER TABLE document_chunks ADD COLUMN SegmentUuid TEXT NULL;
CREATE INDEX IF NOT EXISTS ix_dc_segment_uuid ON document_chunks(SegmentUuid);

ALTER TABLE inbox_items ADD COLUMN AudioAssetId TEXT NULL;
ALTER TABLE inbox_items ADD COLUMN AudioDurationMs INTEGER NULL;

COMMIT;
