# 任务：可观测性补齐（plan-audit-02-observability-hardening）

> 由 `code-audit-report-2026-07-24.md` 派生，对应 `plan-audit-02-observability-hardening.md`。
> **不开发新业务功能**，仅补齐已确认可观测性缺口。

## 目标
补齐审计确认的可观测性缺口：审计事件未完整发布（凭据访问、执行开始/节点完成/节点错误）、DB 节点执行零日志、无结构化日志接收端/分布式追踪/健康检查探针、审计序列化失败静默丢事件、无请求日志中间件、WebSocket 推送日志不足。

## 待完成项（对应计划 3 阶段）
- [x] **阶段一 审计事件完整性**
  - OBS-1：凭据运行时解析路径 `PublishAsync(new CredentialAccessedEvent(...))`。
  - OBS-2：`WorkflowExecutor` 发布 `WorkflowStartedEvent`/`NodeExecutedEvent`/`NodeErrorEvent`。
  - E-1：`AuditLogFileSink.SerializeEvent` 失败 `LogError` + 写死信队列。
- [x] **阶段二 执行与节点日志**
  - OBS-3：`DbExecutor` 记脱敏 SQL + 影响行数 + 耗时，失败含 SQL 文本（不泄露参数值）。
  - OBS-7：`WebSocketEventPushService` 结构化日志 + 广播成功/失败计数。
- [x] **阶段三 接收端/追踪/健康/指标/请求日志**
  - O-1：集成 Serilog → 接收端（保留 Console）。
  - O-2：集成 OpenTelemetry，ASP.NET Core + HttpClient 仪表。
  - O-6：`AddHealthChecks` + liveness/readiness + DB 探针；`Meter` 暴露执行/节点/失败计数。
  - E-4：请求日志中间件（排除 `/health`）。

## 完成标准
- [x] 凭据访问、执行开始、节点完成/错误均入审计（OBS-1/OBS-2）。
- [x] 审计序列化失败进入死信而非静默丢失（E-1）。
- [x] DB 节点执行产生脱敏日志（OBS-3）。
- [x] WS 推送具备结构化日志（OBS-7）。
- [x] 日志可发送至接收端（O-1）。
- [x] 分布式追踪可用（O-2）。
- [x] 真实健康检查 + 指标端点可用（O-6）。
- [x] 请求日志中间件生效（E-4）。
- [x] 全量测试通过，`dotnet build` 无错。

## 全局约束
- 仅实现计划内项，不扩写范围。
- TDD：先写失败测试（正常/边界/异常），再实现至通过。后端 xUnit v3。
- 不提交代码（git commit）。改动留工作区。
- 遵循 `backend-code-rules.md`：结构化日志、敏感值（凭据/Token/SQL 参数）绝不落日志或客户端；异常经统一中间件。
- O-1/O-2 等新依赖：优先用已引入的包；若需新 NuGet 包应轻量且与现有栈一致（Serilog / OpenTelemetry 为审计计划既定方向）。

## 主要修改记录
计划内绝大部分实现（OBS-1/OBS-2/OBS-3/OBS-7、E-1、O-1/O-2/O-6/E-4 的源代码与现有测试）已在工作区就绪。本轮补齐如下缺口，遵循最小改动原则：

- `backend/FlowEngine.Infrastructure/Audit/AuditLogFileSink.cs`
  - 修复缺陷：构造函数注入的 `_serializer`（文档声明用于验证死信逻辑的测试钩子）从未被 `SerializeEvent` 使用，导致死信路径无法经测试触发。将 `SerializeEvent` 改为实例方法并在 `_serializer != null` 时优先调用（其抛异常或返回 null 均走死信）。生产路径不传 serializer，行为不变。
- `tests/FlowEngine.Infrastructure.Tests/Audit/AuditLogFileSinkTests.cs`
  - 新增 `OnEventAsync_SerializationFailure_WritesDeadLetterInsteadOfSilentDrop`（E-1 死信落盘验证）：注入抛异常的序列化器，断言事件写入 `deadletter/audit-deadletter-*.ndjson` 且主审计文件不包含该事件 ID。
- `tests/FlowEngine.Runtime.Tests/Plugins/DbExecutorTests.cs`
  - 新增 `ExecuteNonQueryAsync_LogsSanitizedSqlWithoutParameterValues`（OBS-3 脱敏验证）：执行含敏感参数值的 INSERT，断言日志含 SQL 文本与 `@pN` 占位符、且绝不出现参数明文值。
- `tests/FlowEngine.Runtime.Tests/Credentials/CredentialAuditAccessorTests.cs`（新增文件）
  - OBS-1 凭据访问审计验证：`GetCredentialAsync`/`GetCredentialByNameAsync` 成功解析后发布 `CredentialAccessedEvent`（含 CredentialId/ExecutionId/NodeDefinitionId/AccessType=Resolve），缺失凭据不发布，且事件不携带凭据明文。

### 验证结果
- `dotnet build FlowEngine.sln`：0 警告 / 0 错误。
- 可观测性相关测试（xUnit v3）全部通过：
  - `AuditLogFileSinkTests`（含 E-1 死信）：8/8。
  - `DbExecutorTests` + `CredentialAuditAccessorTests`（OBS-3 / OBS-1）：9/9。
  - Host 可观测性套件（`HealthChecksTests`/`OpenTelemetryTests`/`SerilogConfigurationTests`/`RequestLoggingMiddlewareTests`）：8/8。
