-- ============================================================================
-- Memorix Agent Memory – Raw event tables (SQLite)
-- Creates agent_memory_turns, agent_memory_actions, ingest_offsets.
-- ============================================================================

PRAGMA foreign_keys = ON;

-- ── 1. agent_memory_turns ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_turns (
    Id               TEXT PRIMARY KEY,
    SessionId        TEXT NOT NULL,
    Seq              INTEGER NOT NULL,
    UserMessage      TEXT NULL,
    AssistantMessage TEXT NULL,
    ActionsCount     INTEGER NOT NULL DEFAULT 0,
    TokensTotal      INTEGER NULL,
    Status           TEXT NOT NULL DEFAULT 'active',
    CreatedAt        TEXT NOT NULL,

    FOREIGN KEY (SessionId) REFERENCES agent_memory_sessions(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_turns_session_seq ON agent_memory_turns(SessionId, Seq);

-- ── 2. agent_memory_actions ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_actions (
    Id              TEXT PRIMARY KEY,
    TurnId          TEXT NOT NULL,
    ActionKind      TEXT NOT NULL,
    ToolName        TEXT NULL,
    ToolInputJson   TEXT NULL,
    ToolResult      TEXT NULL,
    FilePath        TEXT NULL,
    Command         TEXT NULL,
    Success         INTEGER NOT NULL DEFAULT 1,
    CreatedAt       TEXT NOT NULL,

    FOREIGN KEY (TurnId) REFERENCES agent_memory_turns(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_actions_turn ON agent_memory_actions(TurnId);
CREATE INDEX IF NOT EXISTS ix_actions_file_path ON agent_memory_actions(FilePath);

-- ── 3. ingest_offsets ───────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS ingest_offsets (
    Id         TEXT PRIMARY KEY,
    Source     TEXT NOT NULL,
    Offset     TEXT NOT NULL,
    Checksum   TEXT NULL,
    IngestedAt TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_ingest_source ON ingest_offsets(Source);
