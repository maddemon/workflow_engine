# 任务：后端 Application 模块测试补充

## 目标

将 `FlowEngine.Application` 行覆盖率从 **76.8%**（Task 008 实测，覆盖行 3657 / 4764）拉升至 **82%+**。因用户决策后端整体冲 75%+ 更高标准，本模块需实质性深补（鉴权守卫、工作流校验/映射、服务编排分支），不再"仅补强"（评审点 #8 已据新标准调整）。

**行号说明**：文中 `:行号`（如 `:41`）取自 2026-07-17 版本源码，仅作辅助参考；执行时请以类名 / 方法名 / 签名为准确认当前源码，行号可能因后续改动偏移。

## 目标类与已核实 API（对照源码，禁止臆测）

### WorkflowService
- 命名空间 `FlowEngine.Application.Workflows`，文件 `backend/FlowEngine.Application/Workflows/WorkflowService.cs:21`
- 主构造函数（顺序与类型严格一致）：
  `(FlowEngineDbContext dbContext, WorkflowValidator _workflowValidator, IEventBus eventBus, AuditEventFactory auditFactory, TriggerService _triggerService, IAuthorizationGuard authGuard, AuthorizedOperationHandler handler, WorkflowStatisticsLoader statisticsLoader, WorkflowTriggerSync triggerSync, ILogger<WorkflowService> logger)`
- 公共方法（`CancellationToken` 均有默认值）：
  - `Task<WorkflowDto> CreateAsync(CreateWorkflowDto dto, CancellationToken ct = default)` :41
  - `Task<WorkflowDto?> GetAsync(Guid id, ...)` :74
  - `Task<PagedResult<WorkflowSummaryDto>> GetAllAsync(Guid? projectId = null, int page = 1, int pageSize = 20, ...)` :96
  - `Task<WorkflowDto?> UpdateAsync(Guid id, UpdateWorkflowDto dto, ...)` :138
  - `Task<bool> DeleteAsync(Guid id, ...)` :298
  - 草稿相关：`CreateDraftAsync` / `ConfirmDraftAsync` / `RejectDraftAsync` / `GetVersionAsync` / `GetVersionsAsync`
- 注意：**不存在** `ActivateAsync` / `DeactivateAsync` / `ListAsync`；列表为 `GetAllAsync`；**所有 id 均为 `Guid`**（继承自 `Entity`，非 string）。

### WorkflowModificationService
- 命名空间 `FlowEngine.Application.Workflows`，`.../WorkflowModificationService.cs:21`
- 主构造函数：`(INodeRegistry nodeRegistry, FlowEngineDbContext dbContext, WorkflowValidator workflowValidator, IEventBus eventBus, AuditEventFactory auditFactory, IAuthorizationGuard authGuard)`
- 唯一公共方法：`Task<ModifyWorkflowResult> ModifyAsync(Guid workflowId, ModifyWorkflowRequest request, CancellationToken ct = default)` :38
- 注意：`DeepClone` 为 `private static Workflow DeepClone(Workflow source)` :531，**不可外部调用**；`AddNode` / `RemoveNode` / `UpdateNode` 为 private helper，须经 `ModifyAsync` 间接覆盖。测试须构造 `ModifyWorkflowRequest` 调用 `ModifyAsync`。

### AuthorizationGuard / AuthorizationPolicy
- 命名空间 **`FlowEngine.Application.Authorization`**（原草稿写的 `FlowEngine.Core.Authorization` 错误）。
- `sealed class AuthorizationGuard` :14，方法：
  - `Task RequireAccessAsync(ResourceKind, Guid, Operation, CancellationToken)` :22
  - `Task RequireScopeAsync(Scope, Operation, ct)` :39
  - `Task RequireAdminAsync(Operation, ct)` :51
- `sealed record AuthorizationPolicy` :9（属性 Resource / Access / Scope / AdminPhase / ProjectScoped）
- 注意：**无** `HasPermission` / `GrantedPermissions`（原草稿臆测）。

### WorkflowValidator / WorkflowDraftValidator / WorkflowMapper / WorkflowRepository
- `WorkflowValidator` :14 主构造 `(INodeRegistry registry)`；`ValidationResult Validate(Workflow)` + `void ValidateTriggerNodes(Workflow, List<string> errors)`
- `WorkflowDraftValidator` :34 主构造 `(INodeRegistry, ICredentialAccessor)`；`Task<DraftValidationResult> ValidateAsync(JsonNode? draft, ...)` + 静态 `CollectCredentialReferences` / `CollectMustacheErrors` / `CollectExpressionSyntaxErrors`
- `WorkflowMapper` :13 `static class`，`static void Register()`（Mapster 配置，`[ModuleInitializer]`）
- `WorkflowRepository` :10 **具体类（无 `IWorkflowRepository` 接口）**；`Task<List<string>> FindReferencingCredentialAsync(Guid credentialId, ...)`

## 待完成项

- [ ] **1.1 WorkflowService 核心方法测试**：覆盖 `CreateAsync` / `GetAsync` / `GetAllAsync` / `UpdateAsync` / `DeleteAsync`。使用 InMemory `FlowEngineDbContext`（`Microsoft.EntityFrameworkCore.InMemory`，Application.Tests 已引用）+ 手写 fake 依赖（参考现有 `RecordingEventBus`、`StubIdempotencyService` 写法）。覆盖正常路径与无效输入（`Guid.Empty`、缺字段 DTO）。
- [ ] **1.2 WorkflowModificationService 测试**：经 `ModifyAsync(ModifyWorkflowRequest)` 覆盖增加/删除/更新节点路径（验证 `DeepClone` 被间接调用、返回值 `ModifyWorkflowResult` 正确）。
- [ ] **1.3 AuthorizationGuard 测试**：以 `RequireAccessAsync` / `RequireScopeAsync` / `RequireAdminAsync` 覆盖授权通过与拒绝路径（fake 资源/作用域）。
- [ ] **1.4 校验与映射测试**：`WorkflowValidator.Validate` / `ValidateTriggerNodes`；`WorkflowDraftValidator.ValidateAsync` 及其静态收集方法；`WorkflowMapper.Register` 后 `Workflow→WorkflowDto` 映射；`WorkflowRepository.FindReferencingCredentialAsync`。

## 完成标准

- `dotnet test tests/FlowEngine.Application.Tests` 全绿。
- 不出现 `FluentAssertions` / `Moq` 引用（本模块用 `Assert.*` + InMemory + fake）。
- 所有调用签名与上文核实结果一致；无虚构 API。

- 对应项目 `dotnet build` 通过（无编译错误，新增测试不得引入类型/签名错误）。

## 完成状态

- [ ] 1.1
- [ ] 1.2
- [ ] 1.3
- [ ] 1.4

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`，修正原草稿中 `IWorkflowRepository`、`ActivateAsync/DeactivateAsync`、`HasPermission`、`DeepClone` 公开调用等虚构/错误 API。
