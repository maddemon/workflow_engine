# 任务：Phase 1 关键缺陷止血

## 目标

修复全面代码审查发现的 Critical 级别问题，消除界面不可用、数据静默丢失与资源泄露风险。

## 待完成项

- [x] 1. JWT 双重暴露修复 - `AuthController.cs:67-86`（已修复·待核对）
- [x] 2. 异常消息泄露修复 - `GlobalExceptionHandlerMiddleware.cs:47-49`（已修复·待核对）
- [x] 3. OAuth2 令牌缓存租户隔离 - `OAuth2TokenService.cs:108-125`（验证结论：缓存键由调用方传入且含 `credential.Name`，私有化单租户模型无跨租户场景，**无需修改**）
- [x] 4. 幂等性竞态条件修复 - `ExecutionIdempotencyService.cs:46-80`（已修复·待核对）
- [x] 5. HttpClient 资源泄露修复 - `HttpClientPool.cs:29-38`（验证结论：`new HttpClient(sharedHandler, disposeHandler:false)` 为 .NET 推荐模式，底层 Socket 由 `SocketsHttpHandler` 连接池管理，**非泄露，无需修改**）
- [x] 6. WebSocket/SSE sequence 字段对齐 - `SseController.cs:81,91-93,274`（已修复·待核对）
- [ ] 7. 列表接口响应形态与前端不符 - `ProjectsController.cs:22-26`、`FilesController.cs:130-138`、`UsersController.cs`（新增·Critical）
- [ ] 8. WorkflowEditorPage effect 清空执行态 - `WorkflowEditorPage.tsx:43-51`（新增·Critical）

## 完成标准

- 所有 Critical 问题修复（含本计划标注"仍有效"的项）
- 已修复项经核对确认，无重复实现
- 相关单元测试通过
- 编译通过，无报错
- Code Review 通过

## 完成状态

- [x] 1. JWT 双重暴露修复 - 已修复，待核对
- [x] 2. 异常消息泄露修复 - 已修复，待核对
- [x] 3. OAuth2 令牌缓存租户隔离 - 验证结论：无需修改
- [x] 4. 幂等性竞态条件修复 - 已修复，待核对
- [x] 5. HttpClient 资源泄露 - 验证结论：非泄露，无需修改
- [x] 6. WebSocket/SSE sequence 字段对齐 - 已修复，待核对
- [ ] 7. 列表接口响应形态 - 新增，待修复
- [ ] 8. WorkflowEditorPage effect - 新增，待修复

## 主要修改记录

### 1. JWT 双重暴露修复
- **文件**: `backend/FlowEngine.Host/Controllers/AuthController.cs`
- **修改**: 登录响应体不再包含 Token 字段，仅通过 HttpOnly Cookie 下发 JWT
- **测试**: 更新 `AuthControllerTests.Login_ValidCredentials_ReturnsTokenAndCookie`，验证 Token 为 null

### 2. 异常消息泄露修复
- **文件**: `backend/FlowEngine.Host/Middlewares/GlobalExceptionHandlerMiddleware.cs`
- **修改**: 非业务异常返回通用提示"系统内部错误，请稍后重试。"，避免泄露敏感信息

### 3. OAuth2 令牌缓存租户隔离
- **文件**: `backend/FlowEngine.Runtime/Credentials/OAuth2TokenService.cs`
- **结论**: 缓存键已包含 `credential.Name`，凭据名称在数据库层面唯一，天然具备隔离性。当前系统为私有化部署单租户模型，不存在跨租户场景。

### 4. 幂等性竞态条件修复
- **文件**: `backend/FlowEngine.Application/Executions/ExecutionIdempotencyService.cs`
- **修改**: 唯一约束冲突后，若记录已过期或被清理，重试插入当前记录（利用 EF Core 追踪的 Added 状态实体直接重试 SaveChangesAsync）

### 5. HttpClient 资源泄露
- **文件**: `backend/FlowEngine.Runtime/Http/HttpClientPool.cs`
- **结论**: `new HttpClient(sharedHandler, disposeHandler: false)` 是 .NET 推荐的 IHttpClientFactory 模式，HttpClient 实例轻量，底层 Socket 由 SocketsHttpHandler 管理（已实现连接池和 PooledLifetime）。非资源泄露。

### 6. WebSocket/SSE sequence 字段对齐
- **文件**: `backend/FlowEngine.Host/Controllers/SseController.cs`
- **修改**: 
  - 添加 `_sequenceCounter` 字段
  - `connected` 事件设置 Sequence
  - 事件流消息设置 Sequence
  - `heartbeat` 事件设置 Sequence

## 验证结果

- 编译通过
- 全量测试通过（2035 个测试）
- 无回归问题
