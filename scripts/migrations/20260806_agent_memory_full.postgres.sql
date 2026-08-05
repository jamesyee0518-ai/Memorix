-- ============================================================================
-- Memorix Agent Memory — Full schema (PostgreSQL)
-- Creates ALL agent memory tables + projects + adds agent_type to agent_profiles.
-- Executed against cloud PostgreSQL: 150.158.122.2:5433/memorix
-- Date: 2026-08-06
-- Idempotent: uses CREATE TABLE IF NOT EXISTS / ADD COLUMN IF NOT EXISTS.
-- ============================================================================

BEGIN;

-- ── 1. projects ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS projects (
    id          uuid PRIMARY KEY,
    project_key varchar(64)  NOT NULL,
    repo_name   varchar(500) NOT NULL,
    git_remote  varchar(2048) NULL,
    local_root  varchar(2048) NULL,
    created_at  timestamptz  NOT NULL,
    updated_at  timestamptz  NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_projects_project_key ON projects(project_key);

-- ── 2. agent_memory_sessions ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_sessions (
    id                   uuid PRIMARY KEY,
    workspace_id         uuid NULL,
    user_id              uuid NOT NULL,
    agent_profile_id     uuid NULL,
    external_session_key varchar(500) NOT NULL,
    task_title           varchar(1000) NOT NULL,
    status               varchar(50) NOT NULL DEFAULT 'active',
    started_at           timestamptz NOT NULL,
    last_active_at       timestamptz NOT NULL,
    closed_at            timestamptz NULL,
    topic_id             uuid NULL,
    project_id           uuid NULL
);
CREATE INDEX IF NOT EXISTS ix_ams_workspace_user_status ON agent_memory_sessions(workspace_id, user_id, status);
CREATE INDEX IF NOT EXISTS ix_ams_external_session_key ON agent_memory_sessions(external_session_key);
CREATE INDEX IF NOT EXISTS ix_ams_user_last_active ON agent_memory_sessions(user_id, last_active_at);
CREATE INDEX IF NOT EXISTS ix_ams_project_id ON agent_memory_sessions(project_id);

-- ── 3. agent_memory_items ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_items (
    id               uuid PRIMARY KEY,
    session_id       uuid NULL,
    workspace_id     uuid NOT NULL,
    owner_user_id    uuid NOT NULL,
    agent_profile_id uuid NULL,
    kind             varchar(50) NOT NULL,
    title            varchar(1000) NOT NULL,
    content          text NULL,
    summary          text NULL,
    admission_state  varchar(50) NOT NULL DEFAULT 'Ephemeral',
    confidence       numeric(5,4) NOT NULL DEFAULT 0,
    visibility       varchar(50) NOT NULL DEFAULT 'Agent',
    importance       integer NOT NULL DEFAULT 5,
    freshness_at     timestamptz NULL,
    status           varchar(50) NOT NULL DEFAULT 'Active',
    superseded_by_id uuid NULL,
    supersedes_id    uuid NULL,
    created_at       timestamptz NOT NULL,
    updated_at       timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ami_workspace_owner_status ON agent_memory_items(workspace_id, owner_user_id, status);
CREATE INDEX IF NOT EXISTS ix_ami_session_admission ON agent_memory_items(session_id, admission_state);
CREATE INDEX IF NOT EXISTS ix_ami_superseded_by ON agent_memory_items(superseded_by_id);
CREATE INDEX IF NOT EXISTS ix_ami_workspace ON agent_memory_items(workspace_id);

-- ── 4. agent_memory_evidences ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_evidences (
    id              uuid PRIMARY KEY,
    memory_item_id  uuid NOT NULL,
    evidence_kind   varchar(50) NOT NULL,
    reference_id    varchar(500) NOT NULL,
    locator         varchar(2000) NULL,
    relation        varchar(200) NULL,
    snapshot_hash   varchar(128) NULL,
    captured_at     timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ame_memory_item ON agent_memory_evidences(memory_item_id);
CREATE INDEX IF NOT EXISTS ix_ame_kind_ref ON agent_memory_evidences(evidence_kind, reference_id);

-- ── 5. agent_memory_feedbacks ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_feedbacks (
    id             uuid PRIMARY KEY,
    memory_item_id uuid NOT NULL,
    user_id        uuid NOT NULL,
    action         varchar(50) NOT NULL,
    note           text NULL,
    created_at     timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_amf_memory_item ON agent_memory_feedbacks(memory_item_id);
CREATE INDEX IF NOT EXISTS ix_amf_user_created ON agent_memory_feedbacks(user_id, created_at);

-- ── 6. agent_memory_access_logs ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_access_logs (
    id               uuid PRIMARY KEY,
    memory_item_id   uuid NULL,
    session_id       uuid NULL,
    agent_profile_id uuid NULL,
    action           varchar(50) NOT NULL,
    trace_id         varchar(200) NULL,
    created_at       timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_amal_memory_item ON agent_memory_access_logs(memory_item_id);
CREATE INDEX IF NOT EXISTS ix_amal_session ON agent_memory_access_logs(session_id);
CREATE INDEX IF NOT EXISTS ix_amal_created_at ON agent_memory_access_logs(created_at);

-- ── 7. agent_memory_checkpoints ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_checkpoints (
    id               uuid PRIMARY KEY,
    session_id       uuid NOT NULL,
    from_sequence    integer NOT NULL,
    to_sequence      integer NOT NULL,
    summary          text NULL,
    open_loops_json  text NULL,
    decisions_json   text NULL,
    token_estimate   integer NOT NULL,
    delivery_state   varchar(50) NOT NULL DEFAULT 'pending',
    created_at       timestamptz NOT NULL,
    version          integer NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_amc_session ON agent_memory_checkpoints(session_id);
CREATE INDEX IF NOT EXISTS ix_amc_session_version ON agent_memory_checkpoints(session_id, version);

-- ── 8. agent_memory_handoffs ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_handoffs (
    id                uuid PRIMARY KEY,
    project_id        uuid NULL,
    from_session_id   uuid NOT NULL,
    to_session_id     uuid NULL,
    from_agent        varchar(50)  NOT NULL,
    to_agent          varchar(50)  NULL,
    task              varchar(2000) NOT NULL,
    status            varchar(50)  NOT NULL DEFAULT 'open',
    context_refs_json text         NULL,
    git_branch        varchar(500) NULL,
    commit_sha        varchar(64)  NULL,
    result_summary    text         NULL,
    created_at        timestamptz  NOT NULL,
    accepted_at       timestamptz  NULL,
    completed_at      timestamptz  NULL,
    CONSTRAINT fk_handoff_from_session FOREIGN KEY (from_session_id)
        REFERENCES agent_memory_sessions(id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_handoffs_project_status ON agent_memory_handoffs(project_id, status);
CREATE INDEX IF NOT EXISTS ix_handoffs_to_agent_status ON agent_memory_handoffs(to_agent, status);
CREATE INDEX IF NOT EXISTS ix_handoffs_from_session ON agent_memory_handoffs(from_session_id);

-- ── 9. agent_memory_turns ───────────────────────────────────────────────
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

-- ── 10. agent_memory_actions ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS agent_memory_actions (
    id              uuid PRIMARY KEY,
    turn_id         uuid NOT NULL,
    action_kind     varchar(50) NOT NULL,
    tool_name       varchar(200) NULL,
    tool_input_json text NULL,
    tool_result     text NULL,
    file_path       varchar(2000) NULL,
    command         text NULL,
    success         boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL,
    CONSTRAINT fk_action_turn FOREIGN KEY (turn_id)
        REFERENCES agent_memory_turns(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_actions_turn ON agent_memory_actions(turn_id);
CREATE INDEX IF NOT EXISTS ix_actions_file_path ON agent_memory_actions(file_path);

-- ── 11. ingest_offsets ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS ingest_offsets (
    id          uuid PRIMARY KEY,
    source      varchar(1000) NOT NULL,
    "offset"    varchar(200) NOT NULL,
    checksum    varchar(128) NULL,
    ingested_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_ingest_source ON ingest_offsets(source);

-- ── 12. agent_profiles: add agent_type column ───────────────────────────
ALTER TABLE agent_profiles ADD COLUMN IF NOT EXISTS agent_type varchar(50) NOT NULL DEFAULT 'unknown';

COMMIT;
