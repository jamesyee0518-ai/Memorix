-- ============================================================================
-- Memorix Agent Memory – Handoff table (SQLite)
-- Creates the agent_memory_handoffs table for point-to-point agent handoffs.
-- ============================================================================

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS agent_memory_handoffs (
    Id                TEXT PRIMARY KEY,
    ProjectId         TEXT NULL,
    FromSessionId     TEXT NOT NULL,
    ToSessionId       TEXT NULL,
    FromAgent         TEXT NOT NULL,
    ToAgent           TEXT NULL,
    Task              TEXT NOT NULL,
    Status            TEXT NOT NULL DEFAULT 'open',
    ContextRefsJson   TEXT NULL,
    GitBranch         TEXT NULL,
    CommitSha         TEXT NULL,
    ResultSummary     TEXT NULL,
    CreatedAt         TEXT NOT NULL,
    AcceptedAt        TEXT NULL,
    CompletedAt       TEXT NULL,

    FOREIGN KEY (FromSessionId) REFERENCES agent_memory_sessions(Id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_handoffs_project_status ON agent_memory_handoffs(ProjectId, Status);
CREATE INDEX IF NOT EXISTS ix_handoffs_to_agent_status ON agent_memory_handoffs(ToAgent, Status);
CREATE INDEX IF NOT EXISTS ix_handoffs_from_session ON agent_memory_handoffs(FromSessionId);
