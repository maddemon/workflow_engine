# 清理：去除不必要的接口

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 删除项目后端代码中不必要的接口：死接口、同项目内无需跨项目抽象的接口、通过集中注册消除的结构性重复。

**判定标准（引用 backend-code-rules.md §5.1）：**
> 不需要不必要的抽象。如果只有一个实现，直接写具体类并注册到 DI。

## 删/留决策

| 决策 | 数 | 接口 |
|------|----|------|
| **保留**（跨项目边界） | 11 | `IUserStore`、`IUserContext`、`ITokenService`、`ITokenBlacklist`、`IPasswordHasher`、`INodeRegistry`、`IWorkflowLoader`、`INodeExecutionContextFactory`、`IExecutionIdempotencyService`、`IScheduleManager`、`IAuditLogReader` |
| **保留**（多实现理由） | 8 | `IEventBus`、`IFileStorage`、`IHttpClientPool`、`ICredentialEncryptionService`、`ICryptoKeyProvider`、`ILlmClientFactory`、`ILlmClient`、`ICredentialAccessor` |
| **保留**（核心抽象） | 4 | `INodeType`、`IEngine`、`IDomainEvent`、`IEntity<T>` |
| **保留**（计划原定删除，实测保留） | 4 | `IResourceAuthorizationService`、`IAuthorizationGuard`、`IOAuth2TokenService`、`IWebhookHandler` |
| **删除**（实际完成） | 9 | `IDynamicParameters`、`IAiDefinitionProvider`、`IPasswordValidator`、`IUserRoleService`、`IAuthorizationService`、`IScriptCache`、`ISecretMasker`、`IHttpExecutionService`、`ICredentialTypeRegistry` |

> **实施偏差记录：** 原计划删除 13 个接口，实际删除 9 个。其中 `IResourceAuthorizationService`、`IAuthorizationGuard`（含 2 个默认接口方法，删除需改造 8+ 测试桩）、`IOAuth2TokenService`、`IWebhookHandler` 因测试桩/跨测试复用摩擦较大，决定保留为接口（见 Task 3d/3e/4c/4d 标注）。Task 2 的实现方案由原计划「`NodeAiDefinitionRegistry` 静态字典」调整为「`INodeType` 默认接口方法」，理由见 Task 2。

---
## 任务分解

### Task 1: 删除死接口 `IDynamicParameters`

**范围：** `backend/FlowEngine.Core/Abstractions/IDynamicParameters.cs` — 0 消费者、0 实现

- [ ] 删除 `backend/FlowEngine.Core/Abstractions/IDynamicParameters.cs`
- [ ] `dotnet build` 确认无编译错误

---

### Task 2: 消除 `IAiDefinitionProvider`（合并到 `INodeType` 默认接口方法）

**问题：** 当前 9 个节点各自实现 `IAiDefinitionProvider`，每个 `GetAiDefinition` 都是一行 `AiDefinitionHelpers.Def(...)`，仅参数不同。接口无独立语义，全部实现的结构相同。

**方案（实施时调整，与原计划的注册表方案不同）：** 在 `INodeType` 上新增默认接口方法
`AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) => null;`，将 AI 定义内聚到节点自身（与 `ExecuteAsync` 同构）。9 个标准节点各自 `override` 该方法返回自己的 AI 定义；未实现的节点回退到 `null`，由 `NodeDefinitionAdapter` 自动推导。

> 与原计划（`NodeAiDefinitionRegistry` 静态字典）的差异：本方案不引入集中注册表，定义与节点共存，去除 `: IAiDefinitionProvider` 样板代码即可，避免了「注册表判断是否有覆盖 + 节点实现覆盖」的双重冗余。代价是 `INodeType`（Core/Abstractions）反向依赖 `FlowEngine.Core.Ai` 命名空间——AI 由「可选独立接口」变为「核心抽象的一部分」，属可接受权衡。

**涉及文件：**

| 操作 | 文件 | 说明 |
|------|------|------|
| 移动 + 改 | `plugins/.../AiDefinitionHelpers.cs` → `backend/FlowEngine.Core/Ai/AiDefinitionHelpers.cs` | 仅引用 `Core.Ai`，移动安全 |
| 修改 | `backend/FlowEngine.Core/Abstractions/INodeType.cs` | 新增默认接口方法 `GetAiDefinition`（返回 null） |
| 修改 | `backend/FlowEngine.Core/Ai/NodeDefinitionAdapter.cs` | `nodeType as IAiDefinitionProvider` → `nodeType.GetAiDefinition(descriptor)` |
| 删除 | `backend/FlowEngine.Core/Abstractions/IAiDefinitionProvider.cs` | |
| 修改（9 节点） | `HttpRequestNode.cs`、`IfNode.cs`、`JSNode.cs`、`LlmNode.cs`、`ManualTriggerNode.cs`、`ScheduleTriggerNode.cs`、`SetNode.cs`、`SwitchNode.cs`、`WebhookNode.cs` | 删 `IAiDefinitionProvider` 声明，改为 `override INodeType.GetAiDefinition` |
| 修改 | `tests/.../CatalogServiceTests.cs` | `TestOverrideNode` 改为 `override INodeType.GetAiDefinition` 验证覆盖机制 |

**`INodeType.cs` 新增默认方法：**
```csharp
/// <summary>
/// 返回 AI-native 节点定义。重写此方法可提供比自动推导更丰富的语义信息。
/// 默认返回 null，由 NodeDefinitionAdapter 回退到自动推导。
/// </summary>
AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) => null;
```

**`NodeDefinitionAdapter.cs` 变更细节：**
```csharp
// 原代码（第 43-44 行）：
var providerOverride = nodeType as IAiDefinitionProvider;
AiNodeDefinition? overrideDef = providerOverride?.GetAiDefinition(descriptor);

// 改为（覆盖优先级：INodeType.GetAiDefinition() > 自动推导）：
AiNodeDefinition? overrideDef = nodeType.GetAiDefinition(descriptor);
```

- [ ] 将 `AiDefinitionHelpers.cs` 移至 `backend/FlowEngine.Core/Ai/`，更新 namespace
- [ ] 在 `INodeType.cs` 新增默认接口方法 `GetAiDefinition`
- [ ] 修改 `NodeDefinitionAdapter.cs` 43-44 行
- [ ] 删除 `IAiDefinitionProvider.cs`
- [ ] 9 个节点改为 `override INodeType.GetAiDefinition` 返回各自 AI 定义
- [ ] 更新 `CatalogServiceTests.cs` 中的 `TestOverrideNode`
- [ ] `dotnet build` 确认无编译错误

---

### Task 3: 删除 Application 层同项目接口（5 个）

**范围：** `FlowEngine.Application` 中接口和实现在同一项目的 5 个接口。

**注意：** `IResourceAuthorizationService` 有默认接口方法（`DecideAsync`、`CanAccessProjectAsync`），删除接口前需将这些方法移至 `ResourceAuthorizationService`；所有测试桩类需补充这些方法。

#### 3a: `IPasswordValidator`

| 变更 | 文件 |
|------|------|
| 删除 | `backend/FlowEngine.Application/Identity/IPasswordValidator.cs` |
| 修改 | `PasswordValidator.cs` — 删 `: IPasswordValidator` 声明，类加文档注释 |
| 修改 DI | `Host/ServiceCollectionExtensions.cs`: `AddScoped<IPasswordValidator, PasswordValidator>()` → `AddScoped<PasswordValidator>()` |
| 修改消费者 | `AuthenticationService.cs` — `IPasswordValidator` → `PasswordValidator` |

已确认：无测试引用。`ApplicationBuilderExtensions.cs` 第 191 行 `new PasswordValidator(minLength: 12)` 已直接使用类。

#### 3b: `IUserRoleService`

| 变更 | 文件 |
|------|------|
| 删除声明 | `UserRoleService.cs` — 类文件中内联的接口声明（第 12 行），删 interface 声明 |
| 修改 | `UserRoleService.cs` — 删 `: IUserRoleService` |
| 修改 DI | `Host/ServiceCollectionExtensions.cs` |
| 修改消费者 | `UsersController.cs` — `IUserRoleService` → `UserRoleService` |

已确认：无测试引用。

#### 3c: `IAuthorizationService`

| 变更 | 文件 |
|------|------|
| 删除 | `backend/FlowEngine.Application/Authorization/IAuthorizationService.cs` |
| 修改 | `AuthorizationService.cs` — 删 `: IAuthorizationService` |
| 修改 DI | `Host/ServiceCollectionExtensions.cs` |
| 修改消费者 | `ResourceAuthorizationService.cs`、`AuthorizationGuard.cs`、`RbacAuthorizationMiddleware.cs` |
| 修改测试 | `AuthorizationServiceTests.cs`（_sut 字段） |
| 修改测试 | `AuthorizationMiddlewareTests.cs` — 测试桩 `TestAuthorizationService` 删 `: IAuthorizationService` |

#### 3d: `IResourceAuthorizationService` ⚠️ 有默认接口方法 — **实际保留**

**保留理由（与原计划相反）：** 此接口的 `DecideAsync` 与 `CanAccessProjectAsync` 为**默认接口方法**。删除接口需将这 2 个方法体复制到 `ResourceAuthorizationService` 并改造 **8+ 个测试桩**（6 个 `Stub*`、2 个 `Fake*`、3 个 `RoleBased*` + 1 个 Moq），摩擦大、收益低（仅消除一个同项目接口）。经评估，保留为接口更符合成本收益，故本任务**未执行删除**。

> 若后续仍要删除，需：
> 1. 将 `DecideAsync`/`CanAccessProjectAsync` 方法体复制到 `ResourceAuthorizationService.cs`；
> 2. 所有测试桩类（`Stub*`/`Fake*`/`RoleBased*` + Moq）补充这两个方法的实现（全 return true 或保持角色逻辑）。

#### 3e: `IAuthorizationGuard` — **实际保留**

**保留理由（与原计划相反）：** 该接口被 9 个生产者（`ProjectService`/`CredentialService`/`WorkflowService`/`ExecutionService`/`AuthorizedOperationHandler`/`WorkflowDryRunService`/`FileService`/`TriggerService`/`SseController`）依赖，并有 5 个测试桩（`FakeAuthorizationGuard`、`Permissive`/`Unauthenticated`/`Denying`、`StubCoreAuthorizationGuard`）及 `AuthorizationGuardFactory` 返回类型依赖。删除摩擦大、收益低，故**未执行删除**，保留为接口。

- [x] 3a: 删除 `IPasswordValidator`
- [x] 3b: 删除 `IUserRoleService`
- [x] 3c: 删除 `IAuthorizationService`
- [x] 3d: 保留 `IResourceAuthorizationService`（含默认方法迁移成本过高，见上）
- [x] 3e: 保留 `IAuthorizationGuard`（测试桩/工厂摩擦大，见上）
- [x] `dotnet build` + `dotnet test` 确认全部通过

---

### Task 4: 删除 Infrastructure 同项目接口（实际删除 4 个，保留 2 个）

**范围：** 接口和实现在同一项目中的 6 个接口。所有接口均无默认方法，变更简单：删接口、改 DI、替换消费者类型引用。`IOAuth2TokenService`/`IWebhookHandler` 经评估保留（见各 Task）。

#### 4a: `IScriptCache`（Core/Scripting → Core/Scripting）

| 变更 | 文件 |
|------|------|
| 删除 | `backend/FlowEngine.Core/Scripting/IScriptCache.cs` |
| 修改 | `ScriptCache.cs` — 删 `: IScriptCache` |
| 修改 DI | `Core/DependencyInjection/ServiceCollectionExtensions.cs` — `AddSingleton<IScriptCache, ScriptCache>()` → `AddSingleton<ScriptCache>()` |
| 修改消费者 | `ParameterResolver.cs`、`ScriptParameterPreEvaluator.cs`、`NodeExecutionContextFactory.cs`、`ScriptEvaluationExtensions.cs`、`NodeExecutionContext.cs` |
| 修改测试 | `ServiceCollectionExtensionsTests.cs` — `GetRequiredService<IScriptCache>()` → `GetRequiredService<ScriptCache>()` |

#### 4b: `ISecretMasker`（Runtime/Security → Runtime/Security）

| 变更 | 文件 |
|------|------|
| 删除 | `backend/FlowEngine.Runtime/Security/ISecretMasker.cs` |
| 修改 | `SecretMasker.cs` — 删 `: ISecretMasker` |
| 修改 DI | `Host/ServiceCollectionExtensions.cs` — 第 203 行 |
| 修改消费者 | `WorkflowSchedulerKernel.cs`、`WorkflowExecutor.cs`、`WorkflowDryRunService.cs` |
| 修改测试 | `SecretMaskerTests.cs` — 字段类型 `ISecretMasker` → `SecretMasker` |
| 修改测试 | `WorkflowDryRunServiceTests.cs` — 局部变量 `ISecretMasker` → `SecretMasker` |

#### 4c: `IOAuth2TokenService`（Runtime/Credentials → Runtime/Credentials）— **实际保留**

**保留理由（与原计划相反）：** 被 `OAuth2CredentialAccessor`、`NodeExecutionContextFactory` 及 2 个测试桩（`StubOAuth2TokenService`/`FakeOAuth2TokenService`）依赖。删除摩擦大于收益，故**未执行删除**，保留为接口。

#### 4d: `IWebhookHandler`（Host/Webhooks → Host/Webhooks）— **实际保留**

**保留理由（与原计划相反）：** 仅被 `WebhookRoutingMiddleware` 依赖，且 `WebhookRoutingMiddlewareTests` 用 `Mock<IWebhookHandler>` 做测试桩——保留接口使 Moq 可继续工作，删除需改为具体类。故**未执行删除**，保留为接口。

#### 4e: `ICredentialTypeRegistry`（Core/Credentials → Core/Credentials）

| 变更 | 文件 |
|------|------|
| 删除 | `backend/FlowEngine.Core/Credentials/ICredentialTypeRegistry.cs` |
| 修改 | `CredentialTypeRegistry.cs` — 删 `: ICredentialTypeRegistry` |
| 修改 DI | `Host/ServiceCollectionExtensions.cs` |
| 修改消费者 | `CredentialService.cs`、`CredentialsController.cs` |

已确认：无测试引用。

#### 4f: `IHttpExecutionService`（Core/Abstractions → Core/Http，同项目）

| 变更 | 文件 |
|------|------|
| 删除 | `backend/FlowEngine.Core/Abstractions/IHttpExecutionService.cs` |
| 修改 | `HttpExecutionService.cs` — 删 `: IHttpExecutionService` |
| 修改消费者 | `WebSearchToolNode.cs`（plugin）、`PaginateNode.cs`（plugin）、`HttpNodeExecution.cs`（plugin） — `IHttpExecutionService` → `HttpExecutionService` |

注意：接口和实现在 `FlowEngine.Core` 同一项目中，插件引用 Core 项目，所以直接引用 `HttpExecutionService` 类无跨项目问题。

已确认：无测试引用。

- [x] 4a: 删除 `IScriptCache`
- [x] 4b: 删除 `ISecretMasker`
- [x] 4c: 保留 `IOAuth2TokenService`（测试桩/消费者摩擦大，见上）
- [x] 4d: 保留 `IWebhookHandler`（Moq 测试桩依赖，见上）
- [x] 4e: 删除 `ICredentialTypeRegistry`
- [x] 4f: 删除 `IHttpExecutionService`
- [ ] `dotnet build` + `dotnet test` 确认全部通过

---
## 验证

- [ ] 每个 Task 执行 `dotnet build` 确认编译通过
- [ ] Task 3 和 4 执行 `dotnet test` 确认测试通过
- [ ] 全部完成后 `dotnet build`（无 warning 新增）+ `dotnet test`（全部通过）
- [ ] Final code review