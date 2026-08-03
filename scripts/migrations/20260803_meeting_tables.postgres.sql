-- ============================================================================
-- Memorix Meeting Service – Schema Migration (PostgreSQL)
-- Creates 6 meeting-related tables and extends audio_assets with meeting link.
-- Idempotent: safe to re-run on existing databases.
-- ============================================================================

BEGIN;

-- ── 1. meetings ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS meetings (
    id                             uuid PRIMARY KEY,
    workspace_id                   uuid NULL,
    topic_id                       uuid NULL,
    title                          varchar(500)  NOT NULL,
    description                    text          NULL,
    language                       varchar(20)   NOT NULL DEFAULT 'zh-CN',
    status                         varchar(50)   NOT NULL DEFAULT 'CREATED',
    started_at                     timestamptz   NULL,
    ended_at                       timestamptz   NULL,
    duration_ms                    bigint        NOT NULL DEFAULT 0,
    created_by                     uuid          NOT NULL,
    processing_preset              varchar(50)   NOT NULL DEFAULT 'LOCAL_FIRST',
    data_classification            varchar(50)   NOT NULL DEFAULT 'INTERNAL',
    allow_audio_upload             boolean       NOT NULL DEFAULT false,
    allow_text_upload              boolean       NOT NULL DEFAULT true,
    official_transcript_version_id uuid NULL,
    official_minutes_version_id    uuid NULL,
    created_at                     timestamptz   NOT NULL DEFAULT now(),
    updated_at                     timestamptz   NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_meetings_ws_created   ON meetings(workspace_id, created_at);
CREATE INDEX IF NOT EXISTS ix_meetings_user_created ON meetings(created_by, created_at);
CREATE INDEX IF NOT EXISTS ix_meetings_status        ON meetings(status);
CREATE INDEX IF NOT EXISTS ix_meetings_topic         ON meetings(topic_id);

-- ── 2. meeting_speakers ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS meeting_speakers (
    id                 uuid PRIMARY KEY,
    meeting_id         uuid NOT NULL,
    speaker_key        varchar(100) NOT NULL,
    global_speaker_id  varchar(100) NOT NULL,
    display_name       varchar(200) NULL,
    participant_id     uuid NULL,
    identity_status    varchar(50)  NOT NULL DEFAULT 'UNCONFIRMED',
    embedding_ref      varchar(500) NULL,
    confidence         numeric(8,6) NOT NULL DEFAULT 0,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    updated_at         timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_ms_meeting      ON meeting_speakers(meeting_id);
CREATE INDEX IF NOT EXISTS ix_ms_global_id    ON meeting_speakers(meeting_id, global_speaker_id);
CREATE INDEX IF NOT EXISTS ix_ms_identity      ON meeting_speakers(identity_status);

-- ── 3. recording_chunks ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS recording_chunks (
    id                   uuid PRIMARY KEY,
    meeting_id           uuid NOT NULL,
    asset_id             uuid NULL,
    sequence_no          integer      NOT NULL DEFAULT 0,
    start_ms             bigint       NOT NULL DEFAULT 0,
    end_ms               bigint       NOT NULL DEFAULT 0,
    local_uri            varchar(2048) NOT NULL,
    codec                varchar(50)  NOT NULL DEFAULT 'pcm_s16le',
    sample_rate          integer      NOT NULL DEFAULT 16000,
    channels             integer      NOT NULL DEFAULT 1,
    file_size            bigint       NOT NULL DEFAULT 0,
    checksum             varchar(128) NULL,
    write_status         varchar(50)  NOT NULL DEFAULT 'WRITING',
    recovery_status      varchar(50)  NULL,
    timeline_gap_before_ms bigint     NOT NULL DEFAULT 0,
    created_at           timestamptz  NOT NULL DEFAULT now(),
    finalized_at         timestamptz  NULL
);
CREATE INDEX IF NOT EXISTS ix_rc_meeting_seq  ON recording_chunks(meeting_id, sequence_no);
CREATE INDEX IF NOT EXISTS ix_rc_write_status ON recording_chunks(write_status);
CREATE INDEX IF NOT EXISTS ix_rc_asset         ON recording_chunks(asset_id);

-- ── 4. meeting_minutes_versions ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS meeting_minutes_versions (
    id                      uuid PRIMARY KEY,
    meeting_id              uuid NOT NULL,
    version_no              integer      NOT NULL DEFAULT 1,
    transcript_version_id   uuid NULL,
    template_id             uuid NULL,
    summary                 text         NULL,
    content_json            text         NOT NULL DEFAULT '{}',
    provider                varchar(100) NOT NULL DEFAULT '',
    model                   varchar(100) NOT NULL DEFAULT '',
    status                  varchar(50)  NOT NULL DEFAULT 'DRAFT',
    created_by              varchar(200) NULL,
    created_at              timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_mmv_meeting       ON meeting_minutes_versions(meeting_id);
CREATE INDEX IF NOT EXISTS ix_mmv_meeting_status ON meeting_minutes_versions(meeting_id, status);
CREATE INDEX IF NOT EXISTS ix_mmv_transcript     ON meeting_minutes_versions(transcript_version_id);

-- ── 5. action_items ─────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS action_items (
    id                   uuid PRIMARY KEY,
    meeting_id           uuid NOT NULL,
    minutes_version_id   uuid NULL,
    task_text            text         NOT NULL,
    owner_text           varchar(200) NULL,
    owner_user_id        uuid NULL,
    due_date             timestamptz  NULL,
    priority             varchar(50)  NOT NULL DEFAULT 'MEDIUM',
    confidence           numeric(8,6) NOT NULL DEFAULT 0,
    confirmation_status  varchar(50)  NOT NULL DEFAULT 'PENDING_CONFIRMATION',
    task_id              uuid NULL,
    source_segment_ids   text         NULL,
    created_at           timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_ai_meeting    ON action_items(meeting_id);
CREATE INDEX IF NOT EXISTS ix_ai_status      ON action_items(confirmation_status);
CREATE INDEX IF NOT EXISTS ix_ai_minutes     ON action_items(minutes_version_id);

-- ── 6. pseudonym_mappings ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pseudonym_mappings (
    id                 uuid PRIMARY KEY,
    meeting_id         uuid NOT NULL,
    scope              varchar(50)  NOT NULL DEFAULT 'MEETING',
    entity_type        varchar(50)  NOT NULL,
    placeholder        varchar(200) NOT NULL,
    encrypted_original text         NOT NULL,
    normalized_hash    varchar(128) NOT NULL,
    mapping_version    integer      NOT NULL DEFAULT 1,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    expires_at         timestamptz  NULL
);
CREATE INDEX IF NOT EXISTS ix_pm_meeting         ON pseudonym_mappings(meeting_id);
CREATE INDEX IF NOT EXISTS ix_pm_placeholder      ON pseudonym_mappings(meeting_id, placeholder);
CREATE INDEX IF NOT EXISTS ix_pm_normalized_hash  ON pseudonym_mappings(normalized_hash);

-- ── 7. Extend existing tables ───────────────────────────────────────────
-- audio_assets: link to meeting (only if the table exists)
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'audio_assets') THEN
        ALTER TABLE audio_assets ADD COLUMN IF NOT EXISTS meeting_id uuid NULL;
        CREATE INDEX IF NOT EXISTS ix_audio_assets_meeting ON audio_assets(meeting_id);
    END IF;
END $$;

-- ── 8. Foreign keys ──────────────────────────────────────────────────────
DO $$ BEGIN
    ALTER TABLE meeting_speakers ADD CONSTRAINT fk_ms_meeting
        FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE recording_chunks ADD CONSTRAINT fk_rc_meeting
        FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE meeting_minutes_versions ADD CONSTRAINT fk_mmv_meeting
        FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE action_items ADD CONSTRAINT fk_ai_meeting
        FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE pseudonym_mappings ADD CONSTRAINT fk_pm_meeting
        FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'audio_assets') THEN
        ALTER TABLE audio_assets ADD CONSTRAINT fk_aa_meeting
            FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE SET NULL;
    END IF;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

COMMIT;
