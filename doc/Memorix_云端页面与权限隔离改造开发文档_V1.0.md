# Memorix 云端页面与权限隔离改造开发文档

> 版本：V1.0  
> 日期：2026-07-17  
> 范围：云端 Web、API、运营端、平台端、Workspace 多租户隔离

## 1. 改造目标

建立“平台权限 + Workspace 权限”两级授权体系，确保普通用户不能访问运营接口，任何用户不能读取、修改或删除其他用户的 Workspace，并使前端页面可见性与后端真实授权一致。

## 2. 当前问题

1. `User`、JWT 和前端用户对象没有平台角色；
2. 内测用户、反馈管理、版本发布仅使用 `[Authorize]`；
3. 所有登录用户均可看到运营菜单；
4. Workspace 创建时未写入所有者；
5. `UserId == null` 的 Workspace 被所有云端用户查询；
6. Workspace 详情、更新、删除缺少统一所有权检查；
7. 当前 Workspace 使用服务器全局配置，存在串用户风险；
8. 详细运行时状态允许匿名访问。

## 3. 权限模型

### 3.1 平台角色

| 角色 | 能力 |
|---|---|
| `platform_admin` | 平台全部管理能力、角色管理和系统配置 |
| `operator` | 内测用户、反馈、版本说明和运营数据 |
| `support` | 后续支持工单与受控只读排障 |
| `user` | 普通知识库功能 |

首期使用 `users.role` 单角色字段，后续再升级为 `user_roles` 多角色表。

### 3.2 Workspace 角色

首期以 `Workspace.UserId` 表示 Owner，实现严格所有者隔离。第二期增加：

```text
workspace_members(workspace_id, user_id, role, status)
```

角色为 `owner/admin/editor/viewer`。

## 4. 页面边界

### 用户端

`/dashboard`、搜索、问答、专题、文档、图谱、报告、导出、实体、标签，以及个人设置、本人 API Key、Agent、用量、反馈、Inbox 和移动设备。

### 运营端

运营能力使用独立路由，只向 `operator/platform_admin` 显示并授权：

```text
/operations/beta-users
/operations/feedback
/operations/release-notes
/operations/push-notifications
```

旧 `/settings/*` 运营路由仅作兼容跳转。本地桌面模式不展示运营中心。

### 平台端

后续新增 `/platform/users`、`/platform/roles`、`/platform/workspaces`、`/platform/jobs`、`/platform/audit-logs`。

## 5. 后端实施

### AUTH-P0-001 平台角色

- `users.role`，默认 `user`；
- JWT 写入标准 Role Claim；
- 登录、注册和 `/auth/me` 返回 role；
- 本地桌面身份使用 `platform_admin`，保持本地功能完整。

### AUTH-P0-002 Policy

建立：

```text
PlatformAdmin
PlatformOperator
```

运营写接口必须由服务端 Policy 控制，不能只隐藏菜单。

### AUTH-P0-003 运营接口

- BetaUser：`PlatformOperator`；
- Feedback `/all`、`/stats`、更新：`PlatformOperator`；
- ReleaseNotes：读取已发布版本为登录用户，创建、更新、发布为 `PlatformOperator`；
- Push audit：`PlatformOperator`；
- Runtime 详细状态：`PlatformAdmin`。

### AUTH-P0-004 Workspace 隔离

- 创建时强制 `UserId = CurrentUser.UserId`；
- 云端列表只返回当前用户 Workspace；
- 详情、更新、删除、切换均验证所有者；
- 不再把 `UserId == null` 当作云端公共 Workspace；
- 非所有者统一返回 404/403，避免枚举资源。

### AUTH-P0-005 当前 Workspace

HTTP 用户配置按 `userId` 分文件隔离；云端最终应迁移到数据库 UserPreference。后台 Worker 不依赖用户请求中的全局“当前 Workspace”，而按任务记录的 WorkspaceId 工作。

## 6. 前端实施

1. User/LoginResponse 增加 `role`；
2. 运营菜单只对 `operator/platform_admin` 显示；
3. 直接访问运营页面时显示 403 或跳转；
4. API 403 显示“无权访问”，不转换成登录失效；
5. 前端权限仅改善体验，服务端 Policy 是安全边界。

## 7. 数据迁移

```sql
ALTER TABLE users ADD COLUMN role varchar(50) NOT NULL DEFAULT 'user';
CREATE INDEX ix_users_role ON users(role);
```

历史 `UserId IS NULL` Workspace 不可直接公开。上线前必须由管理员执行归属修复；无法确认归属的记录标记隔离，不进入普通用户列表。

平台管理员通过生产配置 `Platform:AdminEmails` 初始化。该配置只负责将明确列出的已有用户提升为管理员，不允许注册请求自行指定角色。

## 8. 验收标准

- [ ] 普通用户调用运营接口返回 403；
- [ ] operator 可管理内测用户、反馈和版本说明；
- [ ] 普通用户看不到运营菜单；
- [ ] A 用户不能读取、修改、删除或切换到 B 的 Workspace；
- [ ] 新建 Workspace 必须有 Owner；
- [ ] 两个用户的当前 Workspace 互不影响；
- [ ] JWT 和 `/auth/me` 返回一致角色；
- [ ] 详细 Runtime 状态不再匿名暴露；
- [ ] 原有业务测试、Web 构建和 lint 通过。

## 9. 后续阶段

1. 建立 `workspace_members` 与 Owner/Admin/Editor/Viewer；
2. 建立多角色和细粒度 Permission；
3. 独立运营端与平台端布局；
4. 管理员操作审计、MFA 和敏感操作二次确认；
5. PostgreSQL Row Level Security 作为纵深防御；
6. 将所有依赖全局 ConfigService 的云端请求改为显式 WorkspaceId。
