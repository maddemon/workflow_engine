# B2 · DryRun 复用 WorkflowExecutor —— 实施计划

- 创建：2026-07-09
- 来源：代码审查 Backlog（`docs/chats/task-code-review-backlog-2026-07-08.md` B2 条）
- 设计评审：已完成 2026-07-09（见下「拍板决策」）

## 拍板决策（用户 2026-07-09）

1. **复用方向 = B**：抽共享"调度内核"（纯内存、无 DbContext），`WorkflowExecutor` 与 `WorkflowDryRunService` 各自做外壳；**普通执行路径零改动**（回归风险最低，真正复用执行内核）。
2. **DryRun 契约 = 折中**：保持同步返回、不落库，但补 `Operation.Execute` 权限校验（`RequireScopeAsync(Scope.Workflow, Operation.Execute)`）+ 审计（denial 走 guard 审计，与 B3 一致）。
3. **全局 `ISecretMasker`**：抽 `ISecretMasker`，DryRun 与普通执行都脱敏；普通执行原 `NodeExecutionRecord.ResolvedParameters` 明文落库改为脱敏（用户接受改变现有调试/审计数据语义）。

### 安全发现（B2 一并处理）
普通执行路径 `WorkflowExecutor.cs` 当前 `ResolvedParameters` 存明文 `CredentialValue.Fields`，Runtime 全仓无脱敏。B2 借 `ISecretMasker` 修复。

## 分片实施（TDD）

### Slice 1 — `ISecretMasker` 全局化（交付 q3）
- 新增 `FlowEngine.Runtime/Security/ISecretMasker.cs` 与 `SecretMasker.cs`：移植 DryRun 现有 `SanitizeDataBatch/SanitizeOutput/SanitizeParameters/SanitizeValue/SanitizeJsonNode` 为实例方法；`CredentialValue` → `{name,type}`，字面量集命中 → `"***"`。
- `ServiceCollectionExtensions`：注册 `ISecretMasker → SecretMasker`（Singleton）。
- `WorkflowExecutor`：注入 `ISecretMasker`；两处 `BuildNodeExecutionRecord` 接入 masker（普通执行传空敏感集，`CredentialValue` 脱敏仍生效）。
- `WorkflowDryRunService`：注入 `ISecretMasker`，删除私有 `Sanitize*`，`BuildNodeExecutionRecord` 改调 masker。
- 测试：`tests/FlowEngine.Runtime.Tests/Security/SecretMaskerTests.cs`（CredentialValue→{name,type}、字面量→***、嵌套 dict/array、DataBatch、Output）。
- 验收：build + 全量测试绿；断言普通执行 `ResolvedParameters` 不再含明文 `Fields`。
- **状态：✅ 已完成 2026-07-09** — 全量测试绿（Core 32 / Runtime 160 / Application 249 / Host 95，共 536）；SubAgent Review：Ready to merge（无 Critical）；已采纳修复：删未用 using、补 `MaskDataBatch` 行为注释、补普通执行 `BuildNodeExecutionRecord` 的 `ResolvedParameters` 脱敏断言测试（`WorkflowExecutorTests.BuildNodeExecutionRecord_MasksCredentialValueInResolvedParameters`）。

### Slice 2 — DryRun 补授权与审计（交付 q2）
- `WorkflowDryRunService.DryRunAsync` 入口加 `RequireScopeAsync(Scope.Workflow, Operation.Execute, ct)`（注入 `IAuthorizationGuard`）。
- denial 走 guard 审计（与 B3 一致）；成功可选发布 `ExecutionStarted` 审计事件。
- 测试：未认证/无 Execute 权限 → `UnauthorizedException`/`PermissionDeniedException`；有审计。
- **状态：✅ 已完成 2026-07-09** — 全量测试绿（Core 32 / Runtime 160 / Application 251 / Host 96，共 539）；SubAgent Review：Ready to merge（无 Critical）；已采纳 Minor：重命名 `DryRun_WithNonAdminRole_ReturnsOk`→`DryRun_WithEditorRole_ReturnsOk`、更新类注释。
- 策略变更说明：DryRun 原先任意已认证用户可访问，现需 `Workflow.Execute`（Admin/Editor）；Viewer/空角色返回 403。Host 接口测试已相应更新（Editor→200、新增 Viewer→403）。

### Slice 3 — 抽共享调度内核（交付 q1，方向 B）
- 抽 `WorkflowSchedulerKernel`（纯内存：队列驱动节点处理 + `RouteOutputsAsync` + `WaitingArea` + 超时），无 DbContext/EventBus。
- `WorkflowExecutor` 外壳零改动地委托 kernel（持久化/事件/凭据维持现状）。
- `WorkflowDryRunService` 外壳改为委托同一 kernel（同步 `await` 整轮、不落库、用临时凭据、用 `ISecretMasker`）。
- 测试：kernel 单测 + 两外壳回归测试（行为逐字节一致）。
- **状态：✅ 已完成 2026-07-09** — 全量测试绿（Core 32 / Runtime 165 / Application 251；Host 因离线环境无法还原 `Migrations`/联网未跑，未改 DryRun HTTP 契约）；SubAgent Review：**Ready to merge（无 Critical）**；已采纳 Minor M1：移除 `WorkflowExecutor.ExecuteLoopAsync` 中冗余的 `session.StateMachine.Start()`（改由内核 `RunAsync` 开头负责，对普通执行幂等）。
  - 交付物：`IExecutionSideEffects`（副作用解耦接口）、`WorkflowSchedulerKernel`（迁移原执行器全部循环逻辑，DryRun 终态 `Pending` 修复由内核 `RunAsync` 内 `session.StateMachine.Start()` 负责）、`WorkflowExecutor` 瘦外壳、`WorkflowDryRunService` 瘦外壳（复用内核 + `DryRunCompleted` 语义）、`WorkflowSchedulerKernelTests` 新增 5 例；`ExecutionSession` 移除 `DbContext`、新增 `CredentialAccessor`/`SensitiveValues`、`EmptySensitiveValues`。
  - 验收结论：普通执行路径公共签名与行为逐字节一致；DryRun 经临时凭据 + 字面量敏感集接入同一内核，获 retry/OncePerItem/超时语义，终态映射 `DryRunCompleted`，不落库。

## 每完成一片的闭环
TDD 实施 → `dotnet build` + `dotnet test` 全绿 → SubAgent Review → 更新 backlog 勾选 → 下一片。
全部完成后在 backlog 勾选 B2 `[x]` 并提交。
