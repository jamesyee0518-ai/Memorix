BEGIN;

CREATE TABLE IF NOT EXISTS summary_templates (
    id uuid PRIMARY KEY,
    user_id uuid NULL,
    name varchar(200) NOT NULL,
    description text NULL,
    system_prompt text NOT NULL,
    user_prompt_template text NOT NULL,
    output_schema_json jsonb NOT NULL,
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    is_system boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS summary_template_fields (
    id uuid PRIMARY KEY,
    template_id uuid NOT NULL REFERENCES summary_templates(id) ON DELETE CASCADE,
    field_key varchar(100) NOT NULL,
    label varchar(200) NOT NULL,
    field_type varchar(30) NOT NULL CHECK (field_type IN ('text','number','boolean','string_array','object_array')),
    description text NULL,
    prompt_instruction text NOT NULL,
    required boolean NOT NULL DEFAULT true,
    sort_order integer NOT NULL DEFAULT 0,
    json_schema_json jsonb NOT NULL,
    display_config_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_summary_template_field UNIQUE (template_id, field_key),
    CONSTRAINT ck_summary_field_key CHECK (field_key ~ '^[a-z][a-z0-9_]*$')
);

ALTER TABLE topics ADD COLUMN IF NOT EXISTS summary_template_id uuid NULL;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS summary_template_id uuid NULL;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS summary_template_version integer NULL;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS summary_schema_snapshot jsonb NULL;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS structured_output_json jsonb NULL;

DO $$ BEGIN
    ALTER TABLE topics ADD CONSTRAINT fk_topics_summary_template
        FOREIGN KEY (summary_template_id) REFERENCES summary_templates(id) ON DELETE SET NULL;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE documents ADD CONSTRAINT fk_documents_summary_template
        FOREIGN KEY (summary_template_id) REFERENCES summary_templates(id) ON DELETE SET NULL;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE INDEX IF NOT EXISTS ix_summary_templates_user_active ON summary_templates(user_id, is_active);
CREATE INDEX IF NOT EXISTS ix_summary_template_fields_template_order ON summary_template_fields(template_id, sort_order);
CREATE INDEX IF NOT EXISTS ix_topics_summary_template ON topics(summary_template_id);
CREATE INDEX IF NOT EXISTS ix_documents_summary_template ON documents(summary_template_id, summary_template_version);

-- 将旧固定列合成为统一输出；不会调用模型或覆盖已有新格式数据。
UPDATE documents
SET structured_output_json = jsonb_strip_nulls(jsonb_build_object(
    'summary', summary,
    'one_sentence_conclusion', one_sentence_conclusion,
    'key_points', CASE WHEN key_points IS NULL THEN NULL ELSE key_points::jsonb END,
    'business_signals', CASE WHEN business_signals IS NULL THEN NULL ELSE business_signals::jsonb END,
    'technical_signals', CASE WHEN technical_signals IS NULL THEN NULL ELSE technical_signals::jsonb END,
    'risks', CASE WHEN risks IS NULL THEN NULL ELSE risks::jsonb END,
    'opportunities', CASE WHEN opportunities IS NULL THEN NULL ELSE opportunities::jsonb END,
    'reusable_materials', CASE WHEN reusable_materials IS NULL THEN NULL ELSE reusable_materials::jsonb END,
    'value_score', value_score,
    'value_score_reason', value_score_reason,
    'recommended_tags', CASE WHEN recommended_tags IS NULL THEN NULL ELSE recommended_tags::jsonb END,
    'should_deep_process', should_deep_process
))
WHERE structured_output_json IS NULL;

COMMIT;

