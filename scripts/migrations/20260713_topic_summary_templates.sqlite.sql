PRAGMA foreign_keys = ON;
BEGIN IMMEDIATE;

CREATE TABLE IF NOT EXISTS summary_templates (
    id TEXT PRIMARY KEY,
    user_id TEXT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    system_prompt TEXT NOT NULL,
    user_prompt_template TEXT NOT NULL,
    output_schema_json TEXT NOT NULL CHECK (json_valid(output_schema_json)),
    version INTEGER NOT NULL DEFAULT 1 CHECK (version > 0),
    is_system INTEGER NOT NULL DEFAULT 0 CHECK (is_system IN (0,1)),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS summary_template_fields (
    id TEXT PRIMARY KEY,
    template_id TEXT NOT NULL REFERENCES summary_templates(id) ON DELETE CASCADE,
    field_key TEXT NOT NULL CHECK (field_key GLOB '[a-z]*'),
    label TEXT NOT NULL,
    field_type TEXT NOT NULL CHECK (field_type IN ('text','number','boolean','string_array','object_array')),
    description TEXT NULL,
    prompt_instruction TEXT NOT NULL,
    required INTEGER NOT NULL DEFAULT 1 CHECK (required IN (0,1)),
    sort_order INTEGER NOT NULL DEFAULT 0,
    json_schema_json TEXT NOT NULL CHECK (json_valid(json_schema_json)),
    display_config_json TEXT NULL CHECK (display_config_json IS NULL OR json_valid(display_config_json)),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE(template_id, field_key)
);

CREATE INDEX IF NOT EXISTS ix_summary_templates_user_active ON summary_templates(user_id, is_active);
CREATE INDEX IF NOT EXISTS ix_summary_template_fields_template_order ON summary_template_fields(template_id, sort_order);

-- 这是一次性、带版本号的迁移；请勿对同一数据库重复执行。
-- 应用内仍使用 SqliteInitializer.AddColumnIfNotExistsAsync 处理重复启动。
ALTER TABLE topics ADD COLUMN summary_template_id TEXT NULL;
ALTER TABLE documents ADD COLUMN summary_template_id TEXT NULL;
ALTER TABLE documents ADD COLUMN summary_template_version INTEGER NULL;
ALTER TABLE documents ADD COLUMN summary_schema_snapshot TEXT NULL;
ALTER TABLE documents ADD COLUMN structured_output_json TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_topics_summary_template ON topics(summary_template_id);
CREATE INDEX IF NOT EXISTS ix_documents_summary_template ON documents(summary_template_id, summary_template_version);

UPDATE documents
SET structured_output_json = json_object(
    'summary', "Summary",
    'one_sentence_conclusion', "OneSentenceConclusion",
    'key_points', CASE WHEN json_valid("KeyPoints") THEN json("KeyPoints") ELSE json('[]') END,
    'business_signals', CASE WHEN json_valid("BusinessSignals") THEN json("BusinessSignals") ELSE json('[]') END,
    'technical_signals', CASE WHEN json_valid("TechnicalSignals") THEN json("TechnicalSignals") ELSE json('[]') END,
    'risks', CASE WHEN json_valid("Risks") THEN json("Risks") ELSE json('[]') END,
    'opportunities', CASE WHEN json_valid("Opportunities") THEN json("Opportunities") ELSE json('[]') END,
    'reusable_materials', CASE WHEN json_valid("ReusableMaterials") THEN json("ReusableMaterials") ELSE json('[]') END,
    'value_score', "ValueScore",
    'value_score_reason', "ValueScoreReason",
    'recommended_tags', CASE WHEN json_valid("RecommendedTags") THEN json("RecommendedTags") ELSE json('[]') END,
    'should_deep_process', json(CASE WHEN "ShouldDeepProcess" = 1 THEN 'true' ELSE 'false' END)
)
WHERE structured_output_json IS NULL;

COMMIT;
