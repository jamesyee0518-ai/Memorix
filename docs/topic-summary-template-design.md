# 专题级 Summary 模板设计

## 目标

允许用户为每个专题定义摘要提示词和结构化输出字段。文档处理时固定使用当时的模板版本快照，后续修改模板不会改变历史文档的解释方式。

## 数据模型

### summary_templates

模板主表。`topic_id` 为空表示用户级默认模板；专题通过 `topics.summary_template_id` 绑定一个模板。

主要字段：

- `id`：模板 ID。
- `user_id`：所有者。
- `name`、`description`。
- `system_prompt`：角色、质量和事实约束。
- `user_prompt_template`：支持 `{{title}}`、`{{source_type}}`、`{{content}}`。
- `output_schema_json`：完整 JSON Schema，作为服务端校验依据。
- `version`：模板发生实质变化时递增。
- `is_system`、`is_active`。

### summary_template_fields

可视化字段编辑器的数据源，也是生成 JSON Schema、模型提示词和文档详情 UI 的依据。

- `field_key`：稳定机器键，例如 `business_signals`。
- `label`：用户可见名称。
- `field_type`：`text`、`number`、`boolean`、`string_array`、`object_array`。
- `description`：字段语义。
- `prompt_instruction`：针对模型的提取规则。
- `required`：是否必须出现在模型 JSON 中。
- `sort_order`：页面展示顺序。
- `json_schema_json`：该字段的 JSON Schema 片段。
- `display_config_json`：图标、颜色、空状态、展示组件。

### topics.summary_template_id

专题可选绑定模板。为空时使用用户默认模板；仍为空则回退系统 `summary_v2`。

### documents 新增字段

- `summary_template_id`：本次处理使用的模板。
- `summary_template_version`：处理时版本。
- `summary_schema_snapshot`：处理时完整字段与 Schema 快照。
- `structured_output_json`：模型完整结构化输出。

现有 `summary`、`business_signals` 等列继续保留。保存时由 `structured_output_json` 投影写入，兼容现有列表、搜索、报告和 Agent API。

## 后端流程

1. `DocumentPipeline` 根据 `source.TopicId` 获取专题模板。
2. `SummaryTemplateResolver` 按“专题模板 → 用户默认 → 系统默认”解析。
3. `SummaryPromptCompiler` 根据字段表生成严格 JSON 示例、required 列表和字段说明。
4. LLM 返回后使用模板的 JSON Schema 校验。缺字段、类型错误或未知字段进入现有 JSON 修复/严格重试。
5. 保存完整 `structured_output_json`，同时保存模板 ID、版本和 Schema 快照。
6. `StructuredOutputProjector` 将兼容字段写入现有固定列。
7. 文档详情接口返回 `structuredOutput` 和 `summarySchemaSnapshot`，前端按快照动态渲染。

模板更新采用乐观并发：更新请求必须携带 `version`；服务端成功后版本加一。已经处理的文档不自动改变，用户可选择“使用最新版模板重新摘要”。

## API

- `GET /api/summary-templates`：模板列表。
- `POST /api/summary-templates`：创建模板。
- `GET /api/summary-templates/{id}`：模板及字段详情。
- `PUT /api/summary-templates/{id}`：更新模板和字段，版本加一。
- `POST /api/summary-templates/{id}/clone`：复制系统或其他模板。
- `POST /api/summary-templates/{id}/preview`：用示例正文预览 Prompt 和模型 JSON，不落库。
- `PUT /api/topics/{topicId}/summary-template`：绑定或解除模板。
- `POST /api/topics/{topicId}/resummarize`：按新模板批量重摘要，返回批处理任务 ID。

所有模板接口必须校验 `user_id`；系统模板只读，用户修改时自动克隆。

## 前端页面

入口位于“专题详情 → 摘要模板”。页面分三栏：

1. 模板设置：继承默认/选择现有/复制模板，编辑名称、系统提示和用户提示。
2. 字段设计器：拖拽排序，配置字段键、名称、类型、是否必填、提取说明和展示方式。固定兼容字段不能改键或删除，但可以隐藏。
3. 实时预览：左侧示例资料，右侧显示编译后的 Prompt、JSON Schema、模型返回和动态详情卡片。

保存前检查：字段键唯一且符合 `^[a-z][a-z0-9_]*$`、必填字段存在 Schema、模板变量合法、JSON Schema 可编译。专题页面显示当前模板名称和版本；文档页显示实际使用的模板版本，并提供“按最新版重新摘要”。

## 发布顺序

1. 执行数据库迁移。
2. 部署兼容读取代码：优先读 `structured_output_json`，为空时读取旧列。
3. 部署模板解析、编译、校验和投影服务。
4. 部署 API 和前端字段设计器。
5. 可选后台任务将旧文档固定列合成为 `structured_output_json`，不调用模型。

