-- ============================================================================
-- Memorix Agent Memory – Project identity + AgentType (PostgreSQL)
-- Creates the projects table and adds project_id / agent_type columns.
-- Idempotent: safe to re-run on existing databases.
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

-- ── 2. agent_memory_sessions: add project_id ────────────────────────────
ALTER TABLE agent_memory_sessions ADD COLUMN IF NOT EXISTS project_id uuid NULL;
CREATE INDEX IF NOT EXISTS ix_ams_project_id ON agent_memory_sessions(project_id);

-- ── 3. agent_profiles: add agent_type ───────────────────────────────────
ALTER TABLE agent_profiles ADD COLUMN IF NOT EXISTS agent_type varchar(50) NOT NULL DEFAULT 'unknown';

COMMIT;
