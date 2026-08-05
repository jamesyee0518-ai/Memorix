-- ============================================================================
-- Memorix Agent Memory – Handoff table (PostgreSQL)
-- Creates the agent_memory_handoffs table for point-to-point agent handoffs.
-- Idempotent: safe to re-run on existing databases.
-- ============================================================================

BEGIN;

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

COMMIT;
