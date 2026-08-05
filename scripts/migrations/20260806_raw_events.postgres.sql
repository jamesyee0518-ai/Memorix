-- ============================================================================
-- Memorix Agent Memory – Raw event tables (PostgreSQL)
-- Creates agent_memory_turns, agent_memory_actions, ingest_offsets.
-- Idempotent: safe to re-run on existing databases.
-- ============================================================================

BEGIN;

-- ── 1. agent_memory_turns ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_turns (
    id                uuid PRIMARY KEY,
    session_id        uuid NOT NULL,
    seq               integer NOT NULL,
    user_message      text NULL,
    assistant_message text NULL,
    actions_count     integer NOT NULL DEFAULT 0,
    tokens_total      integer NULL,
    status            varchar(50) NOT NULL DEFAULT 'active',
    created_at        timestamptz NOT NULL,

    CONSTRAINT fk_turn_session FOREIGN KEY (session_id)
        REFERENCES agent_memory_sessions(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_turns_session_seq ON agent_memory_turns(session_id, seq);

-- ── 2. agent_memory_actions ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_actions (
    id               uuid PRIMARY KEY,
    turn_id          uuid NOT NULL,
    action_kind      varchar(50) NOT NULL,
    tool_name        varchar(200) NULL,
    tool_input_json  text NULL,
    tool_result      text NULL,
    file_path        varchar(2000) NULL,
    command          text NULL,
    success          boolean NOT NULL DEFAULT true,
    created_at       timestamptz NOT NULL,

    CONSTRAINT fk_action_turn FOREIGN KEY (turn_id)
        REFERENCES agent_memory_turns(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_actions_turn ON agent_memory_actions(turn_id);
CREATE INDEX IF NOT EXISTS ix_actions_file_path ON agent_memory_actions(file_path);

-- ── 3. ingest_offsets ───────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS ingest_offsets (
    id          uuid PRIMARY KEY,
    source      varchar(1000) NOT NULL,
    "offset"    varchar(200) NOT NULL,
    checksum    varchar(128) NULL,
    ingested_at timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_ingest_source ON ingest_offsets(source);

COMMIT;
