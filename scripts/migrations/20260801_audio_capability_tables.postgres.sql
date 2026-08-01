-- ============================================================================
-- Memorix Audio Capability Integration – Phase 6 Schema Migration (PostgreSQL)
-- Creates 15 audio-related tables and extends documents/document_chunks/inbox_items.
-- Idempotent: safe to re-run on existing databases.
-- ============================================================================

BEGIN;

-- ── 1. audio_assets ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS audio_assets (
    id                   uuid PRIMARY KEY,
    source_id            uuid NOT NULL,
    workspace_id         uuid NULL,
    user_id              uuid NULL,
    original_file_path   varchar(2048) NOT NULL,
    normalized_file_path varchar(2048) NULL,
    source_sha256        varchar(128)  NOT NULL,
    file_size_bytes      bigint        NOT NULL DEFAULT 0,
    mime_type            varchar(255)  NOT NULL DEFAULT 'audio/wav',
    duration_ms          bigint        NOT NULL DEFAULT 0,
    sample_rate          integer       NOT NULL DEFAULT 0,
    channels             integer       NOT NULL DEFAULT 0,
    data_classification  varchar(50)   NOT NULL DEFAULT 'INTERNAL',
    allows_off_device    boolean       NOT NULL DEFAULT true,
    created_at           timestamptz   NOT NULL DEFAULT now(),
    updated_at           timestamptz   NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_audio_assets_sha256      ON audio_assets(source_sha256);
CREATE INDEX IF NOT EXISTS ix_audio_assets_ws_created  ON audio_assets(workspace_id, created_at);

-- ── 2. transcription_jobs ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS transcription_jobs (
    id                       uuid PRIMARY KEY,
    audio_asset_id           uuid NOT NULL,
    workspace_id             uuid NULL,
    user_id                  uuid NOT NULL,
    execution_mode           varchar(50)  NOT NULL DEFAULT 'LOCAL_DEVICE',
    credential_mode          varchar(50)  NOT NULL DEFAULT 'NO_CREDENTIAL',
    provider_id              varchar(100) NOT NULL,
    model_id                 varchar(100) NOT NULL,
    fallback_policy          varchar(50)  NOT NULL DEFAULT 'STOP',
    language                 varchar(20)  NULL,
    enable_vad               boolean      NOT NULL DEFAULT true,
    enable_speaker_diarization boolean    NOT NULL DEFAULT false,
    enable_punctuation       boolean      NOT NULL DEFAULT true,
    hotwords                 text         NULL,
    estimated_cost           numeric(12,6) NULL,
    actual_cost              numeric(12,6) NULL,
    status                   varchar(50)  NOT NULL DEFAULT 'pending',
    error_message            varchar(2000) NULL,
    document_id              uuid NULL,
    segment_count            integer NULL,
    created_at               timestamptz  NOT NULL DEFAULT now(),
    started_at               timestamptz  NULL,
    completed_at             timestamptz  NULL
);
CREATE INDEX IF NOT EXISTS ix_tj_asset         ON transcription_jobs(audio_asset_id);
CREATE INDEX IF NOT EXISTS ix_tj_ws_status    ON transcription_jobs(workspace_id, status);
CREATE INDEX IF NOT EXISTS ix_tj_user_created ON transcription_jobs(user_id, created_at);

-- ── 3. transcription_segments ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS transcription_segments (
    id                   uuid PRIMARY KEY,
    transcription_job_id uuid NOT NULL,
    document_id          uuid NULL,
    workspace_id         uuid NULL,
    segment_uuid         varchar(64)  NOT NULL,
    source_start_ms      bigint       NOT NULL DEFAULT 0,
    source_end_ms        bigint       NOT NULL DEFAULT 0,
    provider_id          varchar(100) NOT NULL,
    model_id             varchar(100) NOT NULL,
    confidence           numeric(8,6) NOT NULL DEFAULT 0,
    speaker_key          varchar(100) NULL,
    text                 text         NOT NULL DEFAULT '',
    version              varchar(50)  NOT NULL DEFAULT 'RAW_MODEL',
    segment_index        integer      NOT NULL DEFAULT 0,
    created_at           timestamptz  NOT NULL DEFAULT now(),
    updated_at           timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_ts_job          ON transcription_segments(transcription_job_id);
CREATE INDEX IF NOT EXISTS ix_ts_uuid_version ON transcription_segments(segment_uuid, version);
CREATE INDEX IF NOT EXISTS ix_ts_document     ON transcription_segments(document_id);

-- ── 4. transcription_versions ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS transcription_versions (
    id                   uuid PRIMARY KEY,
    transcription_job_id uuid NOT NULL,
    segment_uuid         varchar(64)  NOT NULL,
    version              varchar(50)  NOT NULL DEFAULT 'RAW_MODEL',
    parent_version_id    uuid NULL,
    text                 text         NOT NULL DEFAULT '',
    provider_id          varchar(100) NOT NULL,
    model_id             varchar(100) NOT NULL,
    created_by           varchar(200) NULL,
    created_at           timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_tv_job           ON transcription_versions(transcription_job_id);
CREATE INDEX IF NOT EXISTS ix_tv_uuid_version  ON transcription_versions(segment_uuid, version);
CREATE INDEX IF NOT EXISTS ix_tv_parent         ON transcription_versions(parent_version_id);

-- ── 5. provider_credentials ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS provider_credentials (
    id                uuid PRIMARY KEY,
    tenant_id         uuid NULL,
    owner_type        varchar(50)  NOT NULL DEFAULT 'user',
    owner_id          uuid NOT NULL,
    provider_id       varchar(100) NOT NULL,
    credential_type   varchar(50)  NOT NULL DEFAULT 'api_key',
    encrypted_secret  text         NOT NULL,
    key_version       varchar(20)  NOT NULL DEFAULT 'v1',
    status            varchar(50)  NOT NULL DEFAULT 'active',
    last_verified_at  timestamptz  NULL,
    expires_at        timestamptz  NULL,
    label             varchar(200) NULL,
    created_at        timestamptz  NOT NULL DEFAULT now(),
    updated_at        timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_pc_provider_owner ON provider_credentials(provider_id, owner_type, owner_id, status);
CREATE INDEX IF NOT EXISTS ix_pc_tenant         ON provider_credentials(tenant_id);

-- ── 6. provider_usage_records ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS provider_usage_records (
    id                   uuid PRIMARY KEY,
    tenant_id            uuid NULL,
    user_id              uuid NOT NULL,
    workspace_id         uuid NULL,
    capability           varchar(100) NOT NULL,
    provider_id          varchar(100) NOT NULL,
    model_id             varchar(100) NOT NULL,
    credential_mode      varchar(50)  NOT NULL DEFAULT 'NO_CREDENTIAL',
    execution_mode       varchar(50)  NOT NULL DEFAULT 'LOCAL_DEVICE',
    duration_ms          bigint       NOT NULL DEFAULT 0,
    request_count        integer      NOT NULL DEFAULT 1,
    input_units          numeric(12,4) NULL,
    output_units         numeric(12,4) NULL,
    estimated_cost       numeric(12,6) NULL,
    actual_cost          numeric(12,6) NULL,
    status               varchar(50)  NOT NULL DEFAULT 'success',
    error_message        varchar(2000) NULL,
    transcription_job_id uuid NULL,
    created_at           timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_pur_user_created ON provider_usage_records(user_id, created_at);
CREATE INDEX IF NOT EXISTS ix_pur_tenant      ON provider_usage_records(tenant_id, created_at);
CREATE INDEX IF NOT EXISTS ix_pur_job          ON provider_usage_records(transcription_job_id);

-- ── 7. voice_clone_consents ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS voice_clone_consents (
    id              uuid PRIMARY KEY,
    user_id         uuid NOT NULL,
    voice_id        varchar(200) NOT NULL,
    consent_status  varchar(50)  NOT NULL DEFAULT 'pending',
    consent_method  varchar(100) NULL,
    granted_at      timestamptz  NULL,
    revoked_at      timestamptz  NULL,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_vcc_user_voice  ON voice_clone_consents(user_id, voice_id);
CREATE INDEX IF NOT EXISTS ix_vcc_status      ON voice_clone_consents(consent_status);

-- ── 8. correction_dictionaries ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS correction_dictionaries (
    id              uuid PRIMARY KEY,
    workspace_id    uuid NULL,
    original_text   varchar(500) NOT NULL,
    corrected_text  varchar(500) NOT NULL,
    category        varchar(50)  NOT NULL DEFAULT 'custom',
    language        varchar(20)  NULL,
    created_by      varchar(100) NULL,
    is_active       boolean      NOT NULL DEFAULT true,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_cd_ws_cat_active ON correction_dictionaries(workspace_id, category, is_active);
CREATE INDEX IF NOT EXISTS ix_cd_workspace     ON correction_dictionaries(workspace_id);

-- ── 9. model_registries ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS model_registries (
    id                       uuid PRIMARY KEY,
    provider_id              varchar(100) NOT NULL,
    model_id                 varchar(200) NOT NULL,
    display_name             varchar(500) NOT NULL,
    capability               varchar(100) NOT NULL,
    execution_modes          varchar(500) NOT NULL DEFAULT '',
    credential_modes         varchar(500) NOT NULL DEFAULT '',
    supported_languages      varchar(1000) NOT NULL DEFAULT '',
    max_file_bytes           bigint NULL,
    max_audio_duration_ms    bigint NULL,
    accepted_mime_types      varchar(1000) NOT NULL DEFAULT '',
    supports_streaming       boolean      NOT NULL DEFAULT false,
    supports_batch           boolean      NOT NULL DEFAULT true,
    supports_vad             boolean      NOT NULL DEFAULT false,
    supports_punctuation     boolean      NOT NULL DEFAULT false,
    supports_diarization     boolean      NOT NULL DEFAULT false,
    supports_hotwords       boolean      NOT NULL DEFAULT false,
    supports_word_timestamp  boolean      NOT NULL DEFAULT false,
    supports_segment_timestamp boolean   NOT NULL DEFAULT true,
    sends_audio_off_device   boolean      NOT NULL DEFAULT false,
    stores_provider_data     boolean      NOT NULL DEFAULT false,
    pricing_unit             varchar(50)  NULL,
    data_region              varchar(50)  NULL,
    retention_policy         varchar(500) NULL,
    is_enabled               boolean      NOT NULL DEFAULT true,
    health_status            varchar(50)  NOT NULL DEFAULT 'unknown',
    last_health_check_at     timestamptz  NULL,
    created_at               timestamptz  NOT NULL DEFAULT now(),
    updated_at               timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_mr_provider_model ON model_registries(provider_id, model_id);
CREATE INDEX IF NOT EXISTS ix_mr_capability     ON model_registries(capability);
CREATE INDEX IF NOT EXISTS ix_mr_enabled_cap    ON model_registries(is_enabled, capability);

-- ── 10. benchmark_results ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS benchmark_results (
    id                    uuid PRIMARY KEY,
    model_registry_id     uuid NOT NULL,
    benchmark_name        varchar(200) NOT NULL,
    cer                   numeric(10,6) NOT NULL DEFAULT 0,
    wer                   numeric(10,6) NOT NULL DEFAULT 0,
    rtf                   numeric(10,6) NOT NULL DEFAULT 0,
    gpu_memory_mb         integer NULL,
    cpu_memory_mb         integer NULL,
    ttfb                  bigint  NOT NULL DEFAULT 0,
    throughput            numeric(12,4) NOT NULL DEFAULT 0,
    proper_noun_accuracy  numeric(8,6) NULL,
    timestamp_deviation_ms numeric(12,4) NULL,
    speaker_accuracy      numeric(8,6) NULL,
    user_modification_rate numeric(8,6) NULL,
    unit_cost             numeric(12,6) NOT NULL DEFAULT 0,
    evaluated_at          timestamptz NOT NULL DEFAULT now(),
    dataset_name          varchar(200) NULL,
    notes                 text NULL,
    created_at            timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_br_model     ON benchmark_results(model_registry_id);
CREATE INDEX IF NOT EXISTS ix_br_bench     ON benchmark_results(benchmark_name, evaluated_at);
CREATE INDEX IF NOT EXISTS ix_br_dataset   ON benchmark_results(dataset_name);

-- ── 11. prompt_registries ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS prompt_registries (
    id                    uuid PRIMARY KEY,
    prompt_key            varchar(200) NOT NULL,
    version               varchar(50)  NOT NULL,
    title                 varchar(500) NOT NULL,
    description           text NULL,
    system_prompt         text NOT NULL,
    user_prompt_template  text NOT NULL,
    language              varchar(20)  NULL,
    provider_compatibility varchar(1000) NOT NULL DEFAULT '',
    evaluation_score      double precision NULL,
    is_active             boolean      NOT NULL DEFAULT false,
    status                varchar(50)  NOT NULL DEFAULT 'draft',
    published_at          timestamptz  NULL,
    created_by            varchar(200) NOT NULL DEFAULT '',
    created_at            timestamptz  NOT NULL DEFAULT now(),
    updated_at            timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_pr_key_status_active ON prompt_registries(prompt_key, status, is_active);
CREATE INDEX IF NOT EXISTS ix_pr_language          ON prompt_registries(language);

-- ── 12. prompt_ab_tests ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS prompt_ab_tests (
    id                   uuid PRIMARY KEY,
    prompt_key           varchar(200) NOT NULL,
    name                 varchar(500) NOT NULL,
    variant_a_id         uuid NOT NULL,
    variant_b_id         uuid NOT NULL,
    traffic_split_percent integer NOT NULL DEFAULT 0,
    status               varchar(50)  NOT NULL DEFAULT 'created',
    winner_variant_id    uuid NULL,
    start_date           timestamptz  NOT NULL DEFAULT now(),
    end_date             timestamptz  NULL,
    created_by           varchar(200) NOT NULL DEFAULT '',
    created_at           timestamptz  NOT NULL DEFAULT now(),
    updated_at           timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_pat_key    ON prompt_ab_tests(prompt_key);
CREATE INDEX IF NOT EXISTS ix_pat_status  ON prompt_ab_tests(status);

-- ── 13. enterprise_policies ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS enterprise_policies (
    id            uuid PRIMARY KEY,
    workspace_id  uuid NULL,
    policy_name   varchar(500) NOT NULL,
    policy_type   varchar(100) NOT NULL DEFAULT 'provider_restriction',
    rules_json    text         NOT NULL DEFAULT '{}',
    priority      integer      NOT NULL DEFAULT 0,
    is_enabled    boolean      NOT NULL DEFAULT true,
    created_by    varchar(200) NOT NULL DEFAULT '',
    created_at    timestamptz  NOT NULL DEFAULT now(),
    updated_at    timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_ep_ws_type_enabled ON enterprise_policies(workspace_id, policy_type, is_enabled);
CREATE INDEX IF NOT EXISTS ix_ep_type            ON enterprise_policies(policy_type);

-- ── 14. lan_nodes ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS lan_nodes (
    id                   uuid PRIMARY KEY,
    node_name            varchar(200) NOT NULL,
    endpoint_url         varchar(500) NOT NULL,
    node_status          varchar(50)  NOT NULL DEFAULT 'online',
    capabilities         varchar(1000) NOT NULL DEFAULT '',
    provider_ids         varchar(1000) NOT NULL DEFAULT '',
    available_gpu_memory bigint NULL,
    cpu_cores            integer NULL,
    last_heartbeat_at    timestamptz NULL,
    registered_at        timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_ln_endpoint ON lan_nodes(endpoint_url);
CREATE INDEX IF NOT EXISTS ix_ln_status         ON lan_nodes(node_status);

-- ── 15. provider_marketplace_entries ────────────────────────────────────
CREATE TABLE IF NOT EXISTS provider_marketplace_entries (
    id                  uuid PRIMARY KEY,
    name                varchar(200) NOT NULL,
    description         text NULL,
    provider_id         varchar(100) NOT NULL,
    capability          varchar(100) NOT NULL,
    execution_mode      varchar(50)  NOT NULL DEFAULT '',
    credential_mode     varchar(50)  NOT NULL DEFAULT '',
    supported_languages varchar(1000) NOT NULL DEFAULT '',
    pricing_unit        varchar(50)  NULL,
    is_official         boolean      NOT NULL DEFAULT false,
    version             varchar(50)  NOT NULL DEFAULT '',
    rating              numeric(3,2) NOT NULL DEFAULT 0,
    install_count       bigint       NOT NULL DEFAULT 0,
    is_installed        boolean      NOT NULL DEFAULT false,
    author_name         varchar(200) NOT NULL DEFAULT '',
    author_url          varchar(500) NULL,
    tags_json           jsonb        NOT NULL DEFAULT '[]'::jsonb,
    created_at          timestamptz  NOT NULL DEFAULT now(),
    updated_at          timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_pme_capability  ON provider_marketplace_entries(capability);
CREATE INDEX IF NOT EXISTS ix_pme_provider   ON provider_marketplace_entries(provider_id);
CREATE INDEX IF NOT EXISTS ix_pme_installed   ON provider_marketplace_entries(is_installed);

-- ── 16. Extend existing tables ──────────────────────────────────────────
-- documents: audio transcript metadata
ALTER TABLE documents ADD COLUMN IF NOT EXISTS audio_asset_id uuid NULL;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS transcription_job_id uuid NULL;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS audio_duration_ms bigint NULL;

-- document_chunks: link to transcription segment
ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS segment_uuid varchar(64) NULL;
CREATE INDEX IF NOT EXISTS ix_dc_segment_uuid ON document_chunks(segment_uuid);

-- inbox_items: audio source type support
ALTER TABLE inbox_items ADD COLUMN IF NOT EXISTS audio_asset_id uuid NULL;
ALTER TABLE inbox_items ADD COLUMN IF NOT EXISTS audio_duration_ms bigint NULL;

-- ── Foreign keys ────────────────────────────────────────────────────────
DO $$ BEGIN
    ALTER TABLE transcription_jobs ADD CONSTRAINT fk_tj_audio_asset
        FOREIGN KEY (audio_asset_id) REFERENCES audio_assets(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE transcription_segments ADD CONSTRAINT fk_ts_job
        FOREIGN KEY (transcription_job_id) REFERENCES transcription_jobs(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE transcription_versions ADD CONSTRAINT fk_tv_job
        FOREIGN KEY (transcription_job_id) REFERENCES transcription_jobs(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE benchmark_results ADD CONSTRAINT fk_br_model
        FOREIGN KEY (model_registry_id) REFERENCES model_registries(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE prompt_ab_tests ADD CONSTRAINT fk_pat_variant_a
        FOREIGN KEY (variant_a_id) REFERENCES prompt_registries(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE prompt_ab_tests ADD CONSTRAINT fk_pat_variant_b
        FOREIGN KEY (variant_b_id) REFERENCES prompt_registries(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

COMMIT;
