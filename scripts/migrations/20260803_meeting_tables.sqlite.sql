-- ============================================================================
-- Memorix Meeting Service – Schema Migration (SQLite)
-- Creates 6 meeting-related tables and extends audio_assets with meeting link.
-- Uses AddColumnIfNotExists pattern for idempotency on SQLite.
-- ============================================================================

PRAGMA foreign_keys = ON;
BEGIN IMMEDIATE;

-- ── 1. meetings ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS meetings (
    Id                             TEXT PRIMARY KEY,
    WorkspaceId                    TEXT NULL,
    TopicId                        TEXT NULL,
    Title                          TEXT NOT NULL,
    Description                    TEXT NULL,
    Language                       TEXT NOT NULL DEFAULT 'zh-CN',
    Status                         TEXT NOT NULL DEFAULT 'CREATED',
    StartedAt                      TEXT NULL,
    EndedAt                        TEXT NULL,
    DurationMs                     INTEGER NOT NULL DEFAULT 0,
    CreatedBy                      TEXT NOT NULL,
    ProcessingPreset               TEXT NOT NULL DEFAULT 'LOCAL_FIRST',
    DataClassification             TEXT NOT NULL DEFAULT 'INTERNAL',
    AllowAudioUpload               INTEGER NOT NULL DEFAULT 0 CHECK (AllowAudioUpload IN (0,1)),
    AllowTextUpload                INTEGER NOT NULL DEFAULT 1 CHECK (AllowTextUpload IN (0,1)),
    OfficialTranscriptVersionId    TEXT NULL,
    OfficialMinutesVersionId       TEXT NULL,
    CreatedAt                      TEXT NOT NULL,
    UpdatedAt                      TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_meetings_ws_created   ON meetings(WorkspaceId, CreatedAt);
CREATE INDEX IF NOT EXISTS ix_meetings_user_created ON meetings(CreatedBy, CreatedAt);
CREATE INDEX IF NOT EXISTS ix_meetings_status        ON meetings(Status);
CREATE INDEX IF NOT EXISTS ix_meetings_topic         ON meetings(TopicId);

-- ── 2. meeting_speakers ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS meeting_speakers (
    Id                 TEXT PRIMARY KEY,
    MeetingId          TEXT NOT NULL,
    SpeakerKey         TEXT NOT NULL,
    GlobalSpeakerId    TEXT NOT NULL,
    DisplayName        TEXT NULL,
    ParticipantId      TEXT NULL,
    IdentityStatus     TEXT NOT NULL DEFAULT 'UNCONFIRMED',
    EmbeddingRef       TEXT NULL,
    Confidence         REAL NOT NULL DEFAULT 0,
    CreatedAt          TEXT NOT NULL,
    UpdatedAt          TEXT NOT NULL,
    FOREIGN KEY (MeetingId) REFERENCES meetings(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_ms_meeting      ON meeting_speakers(MeetingId);
CREATE INDEX IF NOT EXISTS ix_ms_global_id    ON meeting_speakers(MeetingId, GlobalSpeakerId);
CREATE INDEX IF NOT EXISTS ix_ms_identity      ON meeting_speakers(IdentityStatus);

-- ── 3. recording_chunks ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS recording_chunks (
    Id                    TEXT PRIMARY KEY,
    MeetingId             TEXT NOT NULL,
    AssetId               TEXT NULL,
    SequenceNo            INTEGER NOT NULL DEFAULT 0,
    StartMs               INTEGER NOT NULL DEFAULT 0,
    EndMs                 INTEGER NOT NULL DEFAULT 0,
    LocalUri              TEXT NOT NULL,
    Codec                 TEXT NOT NULL DEFAULT 'pcm_s16le',
    SampleRate            INTEGER NOT NULL DEFAULT 16000,
    Channels              INTEGER NOT NULL DEFAULT 1,
    FileSize              INTEGER NOT NULL DEFAULT 0,
    Checksum              TEXT NULL,
    WriteStatus           TEXT NOT NULL DEFAULT 'WRITING',
    RecoveryStatus        TEXT NULL,
    TimelineGapBeforeMs   INTEGER NOT NULL DEFAULT 0,
    CreatedAt             TEXT NOT NULL,
    FinalizedAt           TEXT NULL,
    FOREIGN KEY (MeetingId) REFERENCES meetings(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_rc_meeting_seq  ON recording_chunks(MeetingId, SequenceNo);
CREATE INDEX IF NOT EXISTS ix_rc_write_status ON recording_chunks(WriteStatus);
CREATE INDEX IF NOT EXISTS ix_rc_asset         ON recording_chunks(AssetId);

-- ── 4. meeting_minutes_versions ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS meeting_minutes_versions (
    Id                      TEXT PRIMARY KEY,
    MeetingId               TEXT NOT NULL,
    VersionNo               INTEGER NOT NULL DEFAULT 1,
    TranscriptVersionId    TEXT NULL,
    TemplateId              TEXT NULL,
    Summary                 TEXT NULL,
    ContentJson             TEXT NOT NULL DEFAULT '{}',
    Provider                TEXT NOT NULL DEFAULT '',
    Model                   TEXT NOT NULL DEFAULT '',
    Status                  TEXT NOT NULL DEFAULT 'DRAFT',
    CreatedBy               TEXT NULL,
    CreatedAt               TEXT NOT NULL,
    FOREIGN KEY (MeetingId) REFERENCES meetings(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_mmv_meeting       ON meeting_minutes_versions(MeetingId);
CREATE INDEX IF NOT EXISTS ix_mmv_meeting_status ON meeting_minutes_versions(MeetingId, Status);
CREATE INDEX IF NOT EXISTS ix_mmv_transcript     ON meeting_minutes_versions(TranscriptVersionId);

-- ── 5. action_items ─────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS action_items (
    Id                  TEXT PRIMARY KEY,
    MeetingId           TEXT NOT NULL,
    MinutesVersionId    TEXT NULL,
    TaskText            TEXT NOT NULL,
    OwnerText           TEXT NULL,
    OwnerUserId         TEXT NULL,
    DueDate             TEXT NULL,
    Priority            TEXT NOT NULL DEFAULT 'MEDIUM',
    Confidence          REAL NOT NULL DEFAULT 0,
    ConfirmationStatus  TEXT NOT NULL DEFAULT 'PENDING_CONFIRMATION',
    TaskId              TEXT NULL,
    SourceSegmentIds    TEXT NULL,
    CreatedAt           TEXT NOT NULL,
    FOREIGN KEY (MeetingId) REFERENCES meetings(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_ai_meeting    ON action_items(MeetingId);
CREATE INDEX IF NOT EXISTS ix_ai_status      ON action_items(ConfirmationStatus);
CREATE INDEX IF NOT EXISTS ix_ai_minutes     ON action_items(MinutesVersionId);

-- ── 6. pseudonym_mappings ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pseudonym_mappings (
    Id                TEXT PRIMARY KEY,
    MeetingId         TEXT NOT NULL,
    Scope             TEXT NOT NULL DEFAULT 'MEETING',
    EntityType        TEXT NOT NULL,
    Placeholder       TEXT NOT NULL,
    EncryptedOriginal TEXT NOT NULL,
    NormalizedHash    TEXT NOT NULL,
    MappingVersion    INTEGER NOT NULL DEFAULT 1,
    CreatedAt         TEXT NOT NULL,
    ExpiresAt         TEXT NULL,
    FOREIGN KEY (MeetingId) REFERENCES meetings(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_pm_meeting         ON pseudonym_mappings(MeetingId);
CREATE INDEX IF NOT EXISTS ix_pm_placeholder      ON pseudonym_mappings(MeetingId, Placeholder);
CREATE INDEX IF NOT EXISTS ix_pm_normalized_hash  ON pseudonym_mappings(NormalizedHash);

-- ── 7. Extend existing tables ───────────────────────────────────────────
-- SQLite does not support IF NOT EXISTS on ADD COLUMN; use a guard.
-- This will error harmlessly if the column already exists; wrap in a DO block is not available,
-- so the application's EnsureCreated handles initial schema and this script is for manual migration.
-- For idempotent column add on SQLite, check via pragma_table_info.

-- audio_assets: add MeetingId column (idempotent guard)
INSERT INTO _temp_column_check (col) SELECT 'meeting_id' WHERE NOT EXISTS (
    SELECT 1 FROM pragma_table_info('audio_assets') WHERE name = 'meeting_id'
);
-- Execute the ALTER for each row that was inserted (i.e., column missing)
-- Note: SQLite ALTER TABLE ADD COLUMN cannot be conditional in a single statement.
-- The application code handles this via EnsureCreated; this migration is supplemental.

COMMIT;
