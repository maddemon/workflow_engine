# REST API 概览（REST API Reference）

> 本文档基于当前代码编写，以代码为准。路由前缀与端点取自 `backend/FlowEngine.Host/Controllers/`，以源码为权威来源。未穷举每个方法，按控制器分组概述。

## 1. 公共约定

- **统一前缀**：所有 API 以 `/api/v1` 开头（`RouteConstants.ApiPrefix = "/api"`，版本段 `v1`）。
- **统一错误响应**：由全局异常中间件包装为
  ```json
  { "success": false, "errorCode": "WorkflowNotFound", "message": "工作流不存在", "details": null }
  ```
- **SPA 回退**：非 `/api` 路径回退到 `index.html`，因此所有接口必须位于 `/api/v1` 之下，避免与前端路由冲突。

## 2. 控制器与路由前缀

| 控制器 | 路由前缀 | 职责 |
|--------|----------|------|
| `WorkflowsController` | `/api/v1/workflows` | 工作流 CRUD、版本、dry-run、导出 / 导入 |
| `AiWorkflowsController` | `/api/v1/workflows` | AI 装配：`assemble` / `modify` / `validate` / `confirm` / `reject` / 执行反馈 |
| `ExecutionsController` | `/api/v1` | 执行：触发、取消、查询、按工作流列执行、活跃执行 |
| `AuthController` | `/api/v1/auth` | 注册 / 登录 / 登出 / 当前用户 / API Key 管理 |
| `CredentialsController` | `/api/v1/credentials` | 凭据 CRUD、类型定义、`ensure` 幂等写入 |
| `TriggersController` | `/api/v1/triggers` | 触发器 CRUD（定时 / Webhook / 轮询） |
| `NodeTypesController` | `/api/v1/node-types` | 节点类型目录（前端参数面板按此自动渲染） |
| `NodeCatalogController` | `/api/v1/node-catalog` | AI 节点摘要与详情（供 MCP / Agent 发现） |
| `ProjectsController` | `/api/v1/projects` | 项目 CRUD |
| `FilesController` | `/api/v1/files` | 文件上传 / 下载 / 列表 / 删除 |
| `AuditEventsController` | `/api/v1/audit-events` | 审计事件查询 |
| `UsersController` | `/api/v1/users` | 用户角色分配 / 撤销 |
| `SseController` | `/api/v1` | SSE 实时事件流：`executions/{id}/stream` |

## 3. 主要端点分组

### 3.1 工作流（`WorkflowsController` / `AiWorkflowsController`）
两者共用前缀 `/api/v1/workflows`。
- 列表 / 详情：`GET /`、`GET /{id}`
- 创建 / 更新 / 删除：`POST /`、`PUT /{id}`、`DELETE /{id}`
- 版本：`GET /{id}/versions`、`GET /{id}/versions/{version}`、`GET /{id}/version`
- 试运行：`POST /dry-run`
- 导出 / 导入：`GET /{id}/export`、`POST /export-batch`、`POST /import`、`POST /import-batch`
- AI 装配：`POST /assemble`、`POST /{id}/modify`、`POST /validate`、`POST /{id}/confirm`、`POST /{id}/reject`
- 执行反馈：`GET /{workflowId}/executions/{executionId}/feedback`

### 3.2 执行（`ExecutionsController`）
- 触发：`POST /api/v1/workflows/{workflowId}/execute`
- 取消：`POST /api/v1/executions/{id}/cancel`
- 查询：`GET /api/v1/executions/{id}`
- 列表：`GET /api/v1/workflows/{workflowId}/executions`、`GET /api/v1/workflows/{workflowId}/executions/active`

### 3.3 认证（`AuthController`）
- 注册：`POST /api/v1/auth/register` → **返回 `410 Gone`**（`errorCode: RegistrationDisabled`）。自助注册已永久关闭，账号由管理员 / SSO 统一创建；该端点标 `IgnoreApi = true`，不出现在 Swagger。
- 登录：`POST /api/v1/auth/login` → 返回 JWT 于 **HttpOnly Cookie**（`fe_auth`）
- 登出：`POST /api/v1/auth/logout`
- 当前用户：`GET /api/v1/auth/me`
- API Key：`POST /api/v1/auth/api-keys`、`GET /api/v1/auth/api-keys`、`DELETE /api/v1/auth/api-keys/{id}`

### 3.4 凭据（`CredentialsController`）
- 列表 / 类型 / 详情：`GET /`、`GET /types`、`GET /{id}`
- 写入：`POST /`、`PUT /{id}`、`PUT /ensure`（幂等）
- 删除：`DELETE /{id}`

### 3.5 审计（`AuditEventsController`，`[Authorize(Roles = "Admin")]`）
- 查询：`GET /api/v1/audit-events`，查询参数（`AuditQueryParameters`）：

| 参数 | 含义 |
|------|------|
| `EventType` | 事件类型过滤 |
| `From` / `To` | 起始 / 结束时间（`DateTime?`） |
| `ResourceType` | 资源类型过滤 |
| `ResourceId` | 资源 ID 过滤（`Guid?`） |
| `Offset` | 分页偏移量 |
| `Limit` | 分页大小（默认 50，钳制 1–200） |

- 返回：`{ total, offset, limit, events[] }`（事件文档原样输出为 JSON 节点树）。

### 3.5 实时通道
- SSE：`GET /api/v1/executions/{executionId}/stream`
- WebSocket：`/ws/execution`（执行事件推送，有状态连接）

## 4. 认证与鉴权

- **浏览器登录**：`POST /api/v1/auth/login` 成功后，JWT 写入 **HttpOnly Cookie**（`fe_auth`）；后续请求凭 Cookie 鉴权。
- **CSRF 防护**：携带 `fe_auth` Cookie 的变更请求（POST/PUT/DELETE 等）需附带自定义防伪造请求头；**Bearer / API Key / 匿名请求不受影响**。
- **API Key（编程 / MCP 访问）**：`POST /api/v1/auth/api-keys` 创建，调用时以 `Authorization: Bearer <apiKey>` 携带。MCP 端点 `/mcp` 即要求 Bearer API Key（见 [MCP 工具参考](mcp-tools.md)）。
- **限流**：登录 / 注册 / API 分别限流（见 `appsettings.json` 的 `RateLimiting`），`/health` 在白名单内。

## 5. 健康检查

| 端点 | 类型 | 说明 |
|------|------|------|
| `/health` | liveness | 存活探针 |
| `/health/ready` | readiness | 含数据库连通性探测（`DatabaseHealthCheck`） |
| `/api/v1/health` | liveness | 同 `/health`，置于 API 前缀下 |

> 健康检查在认证/授权前短路返回，且请求访问日志中间件已排除，适合被负载均衡 / 编排探针直接调用。

## 6. 多版本说明

当前仅 `v1`。所有新增端点沿用 `/api/v1`；破坏性变更将升级版本段（待确认是否有 `v2` 规划）。
