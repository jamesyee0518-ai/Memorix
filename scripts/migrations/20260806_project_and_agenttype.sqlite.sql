-- ============================================================================
-- Memorix Agent Memory – Project identity + AgentType (SQLite)
-- Creates the projects table and adds project_id / agent_type columns.
--
-- Note: SQLite ALTER TABLE ADD COLUMN does not support IF NOT EXISTS.
-- The application's EnsureCreated() handles initial schema creation for new
-- databases; this script is for migrating existing databases. If run twice
-- the ADD COLUMN statements will error harmlessly (column already exists).
-- ============================================================================

PRAGMA foreign_keys = ON;

-- ── 1. projects ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS projects (
    Id          TEXT PRIMARY KEY,
    ProjectKey  TEXT NOT NULL,
    RepoName    TEXT NOT NULL,
    GitRemote   TEXT NULL,
    LocalRoot   TEXT NULL,
    CreatedAt   TEXT NOT NULL,
    UpdatedAt   TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_projects_project_key ON projects(ProjectKey);

-- ── 2. agent_memory_sessions: add project_id ────────────────────────────
-- SQLite does not support ADD COLUMN IF NOT EXISTS — guard at application level.
-- This is safe to run on a fresh DB (EnsureCreated already includes it) or
-- skip; for existing DBs without the column, uncomment the next line:
-- ALTER TABLE agent_memory_sessions ADD COLUMN ProjectId TEXT NULL;
-- CREATE INDEX IF NOT EXISTS ix_ams_project_id ON agent_memory_sessions(ProjectId);

-- ── 3. agent_profiles: add agent_type ───────────────────────────────────
-- Same caveat as above for SQLite. For existing DBs, uncomment:
-- ALTER TABLE agent_profiles ADD COLUMN AgentType TEXT NOT NULL DEFAULT 'unknown';
