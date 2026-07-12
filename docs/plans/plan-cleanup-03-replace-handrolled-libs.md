# 开发计划：替换手搓实现为成熟第三方库（plan-cleanup-03-replace-handrolled-libs）

> **说明：** 本计划是跨阶段技术债清理，遵循 `plan-000-overview.md` §3.1 中"基础设施/交叉清理计划"的命名规范。所有定位均来自对 `backend/` 与 `tests/` 的静态核查，不依赖硬编码行号。

## 1. 概述

当前系统在多处有自己手搓的实现，而 .NET 生态已存在成熟、经过生产验证的第三方库。替换这些实现可减少维护成本、降低 bug 率、统一架构模式。

### 1.1 覆盖范围

按替换优先级和风险分三类：

| 类别 | 判定标准 | 包含项 |
|------|----------|--------|
| **快速取胜** | 模块边界清晰、替换风险低、开关可控、收益立竿见影 | DTO 映射、FluentValidation、Stateless、**Dapper** |
| **中期替换** | 涉及一定依赖关系调整、需协调多模块改动 | MediatR→EventBus、Audit.NET、RateLimiting |
| **谨慎评估** | 深度绑定现有架构、替换可能引发连锁影响；替代库与当前抽象层的适配成本高 | PluginLoader→McMaster、RBAC→Casbin.NET、OAuth2→IdentityModel |

### 1.2 不覆盖范围

- 核心执行引擎（`FlowEngine.Runtime.Executor`）——这是产品的核心差异化逻辑，不应用通用工作流库替代。
- Jint 已封装的 Scripting 层（`FlowEngine.Core.Scripting`）——Jint 是成熟库，现有封装已合理。
- 前端代码——前端已在使用 Mantine、ahooks、Zustand 等成熟库，不再重复审查。
- CLI 代码——Commander、jose、chalk 等已满足需求。
- 系统自身的数据访问（`FlowEngineDbContext` / EF Core）——Dapper **不替换** EF Core。Dapper 只用在 **插件层**（`plugins/FlowEngine.Plugins.Standard/Data/`），用于对 **用户目标数据库** 执行原始 SQL 的场景。

## 2. 交付物清单

| 交付物 | 类型 |
|--------|------|
| Mapster 引入并替换全部手动 `MapToDto`/`ToDto`/`ToEntity` | NuGet + 代码（10 个 Service + Mapper 文件） |
| FluentValidation 引入并替换零散的 `if` 参数校验 | NuGet + 代码（注册 + Validator 类） |
| Stateless 替换 ExecutionStateMachine | NuGet + 代码 |
| **Dapper 替换 DbExecutor 手搓 ADO.NET** | **NuGet + 代码（DbExecutor + DbReaderNode）+ 测试** |
| MediatR 替换 InMemoryEventBus | NuGet + 代码（Handler 拆分、注册、测试适配） |
| Audit.NET 替换自定义审计日志 | NuGet + 代码（Sink 配置、事件模型适配） |
| `System.Threading.RateLimiting` 替换手写限流 | 代码（.NET 内置，无需 NuGet） |
| 三份深度评估报告（Plugin/McMaster、RBAC/Casbin、OAuth2/IdentityModel） | 文档（本计划附录） |
| `dotnet build` + `dotnet test` 全绿 | 验证 |

## 3. 开发阶段

### 阶段一：快速取胜（低风险，高收益）

**目标：** 用最少的工作量消灭最常见的重复样板代码。

---

#### 任务 1.1：DTO 映射 → Mapster

**分析：** 当前 6 个 Service 类（`ProjectService`、`WorkflowService`、`TriggerService`、`ExecutionService`、`CredentialService`、`FileService`）各有私有 `MapToDto` 方法，另有一个专用 `WorkflowMapper` 类。总计 45+ 处手写赋值代码。每新增一个 DTO 字段就需修改 N 处映射点，容易遗漏。

**Mapster 评价：**
- 比 AutoMapper 快 5~10x（基于 Roslyn 源码生成 vs 运行时反射）
- API 简洁：`source.Adapt<Destination>()` 一行代替几十行
- 支持自定义映射、忽略字段、条件映射
- 无运行时注册开销（Mapster 默认即可工作）
- NuGet: `Mapster`

```csharp
// 现状（WorkflowService.cs:299）
private static WorkflowDto MapToDto(Workflow workflow, ...)
{
    return new WorkflowDto
    {
        Id = workflow.Id.ToString(),
        Name = workflow.Name,
        Description = workflow.Description,
        // ... 20+ 字段手写
    };
}

// 替换后
using Mapster;
// 在 Service 中
workflow.Adapt<WorkflowDto>();
// 或使用映射配置（如需特殊逻辑）
```

**涉及文件（修改）：**
- `backend/FlowEngine.Application/Workflows/WorkflowMapper.cs` — 整文件替换为 TypeAdapterConfig
- `backend/FlowEngine.Application/Workflows/WorkflowService.cs` — 删除 `MapToDto`，改用 `.Adapt<>()`
- `backend/FlowEngine.Application/Projects/ProjectService.cs` — 同上
- `backend/FlowEngine.Application/Executions/ExecutionService.cs` — 同上
- `backend/FlowEngine.Application/Triggers/TriggerService.cs` — 同上
- `backend/FlowEngine.Application/Credentials/CredentialService.cs` — 同上（注意脱敏逻辑保留）
- `backend/FlowEngine.Application/Files/FileService.cs` — 同上
- `backend/FlowEngine.Application/Workflows/WorkflowImportService.cs` — 更新映射调用
- `backend/FlowEngine.Application/Workflows/WorkflowExportService.cs` — 同上
- `backend/FlowEngine.Application/Workflows/WorkflowDryRunService.cs` — 更新 `MapToDto`
- `backend/FlowEngine.Core/FlowEngine.Core.csproj` — 加 Mapster 引用（或放在 Application 层）

**验收：** `dotnet test` 通过，手写 DTO 映射为零。

---

#### 任务 1.2：输入校验 → FluentValidation

**分析：** 当前校验零散分布在各处：
- `DataAnnotations` 在 DTO 上做基本校验
- Service 方法内 `if (string.IsNullOrWhiteSpace(...)) return (false, error)` 模式
- Controller 有少量手写校验

FluentValidation 分离校验逻辑到独立的 Validator 类，支持依赖注入、条件规则、级联错误。

**涉及文件（新建 + 修改）：**
- `backend/FlowEngine.Application/Validators/` — 新建目录，每个 DTO 一个 Validator
  - `CreateWorkflowDtoValidator.cs`
  - `UpdateWorkflowDtoValidator.cs`
  - `CreateProjectDtoValidator.cs`
  - `CreateCredentialDtoValidator.cs`
  - `AssignRoleRequestValidator.cs`
  - 等
- `backend/FlowEngine.Application/FlowEngine.Application.csproj` — 加 `FluentValidation` + `FluentValidation.DependencyInjectionExtensions`
- `backend/FlowEngine.Host/ServiceCollectionExtensions.cs` — 注册 `services.AddValidatorsFromAssemblyContaining<>()`
- `backend/FlowEngine.Application/Identity/UserRoleService.cs` — 删除 `AssignRoleAsync` 内手写 if 校验
- `backend/FlowEngine.Application/Workflows/WorkflowService.cs` — 同上
- `backend/FlowEngine.Application/Projects/ProjectService.cs` — 同上
- 其他 Service — 逐处删除手写 if 校验

**验收：** 所有零散 `if (string.IsNullOrWhiteSpace(...)) return (false, ...)` 从 Service 逻辑层消除，改为 Validator 集中管理。

---

#### 任务 1.3：执行状态机 → Stateless

**分析：** `ExecutionStateMachine`（92 行）是简单 `switch` 加 if 守卫。当前仅有 5 个状态和 4 个转换，状态图清晰。Stateless 让状态转换可读、可测试、可扩展（未来如需状态持久化、异步触发器更有优势）。

**涉及文件：**
- `backend/FlowEngine.Runtime/FlowEngine.Runtime.csproj` — 加 `Stateless`
- `backend/FlowEngine.Runtime/Executor/ExecutionStateMachine.cs` — 重构

```csharp
// 当前
public sealed class ExecutionStateMachine
{
    public ExecutionStatus Status { get; private set; }

    public void Start()
    {
        if (Status == ExecutionStatus.Pending)
            Status = ExecutionStatus.Running;
    }

    public void Complete()
    {
        if (Status == ExecutionStatus.Running)
            Status = ExecutionStatus.Completed;
    }
    // ...
}

// 替换后
using Stateless;

public sealed class ExecutionStateMachine
{
    private readonly StateMachine<ExecutionStatus, ExecutionTrigger> _machine;

    public ExecutionStatus Status => _machine.State;

    public ExecutionStateMachine(ExecutionStatus initialStatus = ExecutionStatus.Pending)
    {
        _machine = new StateMachine<ExecutionStatus, ExecutionTrigger>(() => initialStatus, s => { /* setter if mutable needed */ });

        _machine.Configure(ExecutionStatus.Pending)
            .Permit(ExecutionTrigger.Start, ExecutionStatus.Running);

        _machine.Configure(ExecutionStatus.Running)
            .Permit(ExecutionTrigger.Complete, ExecutionStatus.Completed)
            .Permit(ExecutionTrigger.Fail, ExecutionStatus.Failed)
            .Permit(ExecutionTrigger.Cancel, ExecutionStatus.Cancelled);

        _machine.Configure(ExecutionStatus.Pending)
            .Permit(ExecutionTrigger.Cancel, ExecutionStatus.Cancelled);
    }

    public void Start() => _machine.Fire(ExecutionTrigger.Start);
    public void Complete() => _machine.Fire(ExecutionTrigger.Complete);
    public void Fail() => _machine.Fire(ExecutionTrigger.Fail);
    public void Cancel() => _machine.Fire(ExecutionTrigger.Cancel);
}
```

**涉及文件（修改）：**
- `backend/FlowEngine.Runtime/Executor/ExecutionStateMachine.cs`

**验收：** `dotnet test` 通过，行为不变（Pending→Running 等转换的 if 守卫语义等价）。

---

#### 任务 1.4：手搓 ADO.NET → Dapper（插件层）

**分析：** `DbExecutor`（92 行）是手写 ADO.NET 封装：
- `ExecuteNonQueryAsync` / `ExecuteScalarAsync` 各自循环 `CreateParameter` → `@p0` / `@p1` / `@p{i}` → `command.Parameters.Add`
- 无 `QueryAsync<T>` 方法，未来 `DbReaderNode` 需手写 `ExecuteReader` + 逐行映射
- 连接/事务生命周期管理是合理的，保留

**Dapper 解决：**
- `connection.ExecuteAsync(sql, new { p0, p1, ... })` — 替换手写参数循环
- `connection.QueryAsync<T>(sql, new { ... })` — 自动结果映射（Dynamic / 强类型）
- `connection.QueryFirstOrDefaultAsync<T>` — 单行查询
- `connection.QueryMultipleAsync` — 多结果集
- 参数自动处理（无需 `command.CreateParameter`，无需指定类型）

**策略：** 不废弃 `DbExecutor`，而是在其内部用 Dapper 替换原始 ADO.NET 调用，并新增 `QueryAsync<T>` 方法。保持 `DbExecutor` 作为生命周期管理（连接打开、事务提交/回滚）的边界。

```csharp
// 现状（DbExecutor.cs:36-67）
public async Task<int> ExecuteNonQueryAsync(string sql, IReadOnlyList<object?> parameters, CancellationToken ct)
{
    await using var command = _connection.CreateCommand();
    command.Transaction = _transaction;
    command.CommandText = sql;
    for (var i = 0; i < parameters.Count; i++)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@p{i}";
        parameter.Value = parameters[i] ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
    return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
}

// 替换后
using Dapper;

public async Task<int> ExecuteNonQueryAsync(string sql, IReadOnlyList<object?> parameters, CancellationToken ct)
{
    var dynamicParams = new DynamicParameters();
    for (var i = 0; i < parameters.Count; i++)
        dynamicParams.Add($"@p{i}", parameters[i]);
    return await _connection.ExecuteAsync(new CommandDefinition(
        sql, dynamicParams, _transaction, cancellationToken: ct)).ConfigureAwait(false);
}

// 新增：读取方法（供未来 DbReaderNode 使用）
public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, IReadOnlyList<object?>? parameters, CancellationToken ct)
{
    DynamicParameters? dynamicParams = null;
    if (parameters is { Count: > 0 })
    {
        dynamicParams = new DynamicParameters();
        for (var i = 0; i < parameters.Count; i++)
            dynamicParams.Add($"@p{i}", parameters[i]);
    }
    var result = await _connection.QueryAsync<T>(new CommandDefinition(
        sql, dynamicParams, _transaction, cancellationToken: ct)).ConfigureAwait(false);
    return result.AsList();
}
```

**Dapper 与现有设计的关系（重要）：**

```
┌─────────────────────────────────────────────┐
│          应用层（Application）                  │
│  WorkflowService / ProjectService / ...       │
│  ORM: EF Core (FlowEngineDbContext)           │
│  Dapper: ❌ 不引入                           │
├─────────────────────────────────────────────┤
│          插件层（Plugins）                    │
│  DbUpsertNode / 未来 DbReaderNode            │
│  连接: DbConnectionFactory → 各 Provider     │
│  执行: DbExecutor (Dapper 内部替代 ADO.NET)   │
│  Dapper: ✅ 引入                             │
└─────────────────────────────────────────────┘
```

**涉及文件（修改）：**
- `plugins/FlowEngine.Plugins.Standard/FlowEngine.Plugins.Standard.csproj` — 加 `Dapper`
- `plugins/FlowEngine.Plugins.Standard/Data/DbExecutor.cs` — 重构内部实现 + 新增 `QueryAsync<T>`

**验收：** `dotnet test` 通过（含 `DbUpsertNodeTests`），`ExecuteNonQueryAsync` / `ExecuteScalarAsync` 行为不变。

---

### 阶段二：中期替换（中等风险，需协调多模块）

**目标：** 替换系统级基础设施组件，提升统一性和可维护性。建议按依赖顺序逐一执行。

---

#### 任务 2.1：事件总线 → MediatR

**分析：** `InMemoryEventBus`（174 行）是手写的 Channel + 类型字典 + 订阅分发，功能完备但缺少 MediatR 的核心能力：
- 行为管道（日志、事务、性能监控等横切关注点可插拔）
- 通知处理器的并行/顺序/异常隔离策略
- 请求/响应模式（CQRS）
- 开箱即用的测试支持

MediatR 是 .NET 领域事件的事实标准，社区活跃（~12k stars），且与 DI 容器无缝集成。

**迁移策略（保留现有 IEventBus 接口，渐进式迁移）：**
1. 加 MediatR NuGet
2. 将现有 `AuditEvent` 及其子类的事件处理器从 `InMemoryEventBus.Subscribe` 回调改为 `INotificationHandler<T>` 
3. 新增 MediatR 发布入口，与现有 IEventBus 并行运行
4. 待全部消费者迁移完成后，标记 `InMemoryEventBus` 为 `[Obsolete]`
5. 最终删除

```csharp
// 现状：EventBus 订阅
bus.Subscribe<WorkflowStartedEvent>(async (e, ct) => {
    await auditLog.WriteAsync(e, ct);
});

// 替换后：MediatR 通知处理器
public class WorkflowStartedAuditHandler(IAuditLogWriter auditLog)
    : INotificationHandler<WorkflowStartedEvent>
{
    public async Task Handle(WorkflowStartedEvent notification, CancellationToken ct)
    {
        await auditLog.WriteAsync(notification, ct);
    }
}

// 发布
await mediator.Publish(new WorkflowStartedEvent { ... }, ct);
```

**涉及文件：**
- `backend/FlowEngine.Core/FlowEngine.Core.csproj` — 加 `MediatR` + `MediatR.Contracts`
- `backend/FlowEngine.Core/Events/InMemoryEventBus.cs` — 加 `[Obsolete]`
- `backend/FlowEngine.Core/Abstractions/IEventBus.cs` — 保留（MediatR 适配实现）
- 各事件处理方（`AuditLogFileSink`、`WebSocketEventPushService` 等）— 逐一改为 `INotificationHandler`
- `backend/FlowEngine.Host/ServiceCollectionExtensions.cs` — `services.AddMediatR(cfg => ...)`
- 测试桩 — `StubEventBus`、`CapturingEventBus` 等可保持

**验收：** `dotnet test` 通过，所有 EventBus 消费端适配为 MediatR 处理器。

---

#### 任务 2.2：审计日志 → Audit.NET

**分析：** 当前审计日志基于 EventBus + `AuditLogFileSink` 手写 NDJSON 文件写入 + `AuditLogReader` 读取。Audit.NET 提供更完整的能力：
- 20+ 输出 Sink（文件、DB、Elasticsearch、Seq 等）
- 内置 JSON 序列化控制、脱敏、上下文数据
- 自定义事件字段和范围
- 与 ASP.NET Core 中间件集成

**迁移策略：** 将现有 `AuditEvent` 事件适配为 Audit.NET 的 `AuditEvent`，保留 `IAuditLogReader` 接口但用 Audit.NET 的查询能力实现。

**涉及文件（修改）：**
- `backend/FlowEngine.Infrastructure/FlowEngine.Infrastructure.csproj` — 加 `Audit.NET` + `Audit.NET.Sqlite`（或文件 Sink）
- `backend/FlowEngine.Core/Events/AuditEvent.cs` — 适配 Audit.NET 事件模型
- `backend/FlowEngine.Infrastructure/Audit/AuditLogFileSink.cs` — 替换为 Audit.NET 配置
- `backend/FlowEngine.Infrastructure/Audit/AuditLogReader.cs` — 替换实现
- `backend/FlowEngine.Host/ServiceCollectionExtensions.cs` — 注册 Audit.NET
- `backend/FlowEngine.Application/Audit/IAuditLogReader.cs` — 保持接口

**验收：** `dotnet test` 通过，审计日志写入和查询行为不变。

---

#### 任务 2.3：限流中间件 → System.Threading.RateLimiting

**分析：** 当前 `RateLimitMiddleware`（100+ 行）用 `ConcurrentDictionary` + 时间窗口算法手写限流。.NET 7+ 内置 `System.Threading.RateLimiting` 命名空间（项目目标 net10.0，无需额外 NuGet），提供：
- 令牌桶、滑动窗口、并发限流器等算法
- `PartitionedRateLimiter` 对每个客户端/IP 独立限流
- 与 ASP.NET Core 中间件(`UseRateLimiter`)集成

**涉及文件（修改）：**
- `backend/FlowEngine.Host/Middlewares/RateLimitMiddleware.cs` — 重写为 `System.Threading.RateLimiting`
- `backend/FlowEngine.Application/RateLimiting/RateLimitOptions.cs` — 适配新配置格式
- `backend/FlowEngine.Host/ServiceCollectionExtensions.cs` — 更新注册
- `backend/FlowEngine.Host/ApplicationBuilderExtensions.cs` — 更新中间件注册

```csharp
// System.Threading.RateLimiting 方案示例
using System.Threading.RateLimiting;

// 注册
services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("Login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromSeconds(60);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

// 中间件
app.UseRateLimiter();
```

**验收：** `dotnet test` 通过，限流行为不变。

---

### 阶段三：深度评估（谨慎评估 3 项）

**目标：** 对 3 项与现有架构深度绑定的替换做出"换 / 不换 / 如何换"的决策。

---

#### 评估 A：插件加载 → McMaster.NET.Extensions.Plugins（或 MEF）

**当前实现：**
- `PluginLoader`（173 行）：扫描 `plugins/` 目录中所有 DLL → 用独立 `AssemblyLoadContext` 加载 → 反射查找 `INodeType` 实现
- `PluginLoadContext`（46 行）：自定义 ALC，委托默认上下文加载共享程序集，避免类型标识不一致
- 正在被 `plugin-cleanup-plan.md` 中 `INodeRegistry` 的 `GetNodeTypes()` 直接引用

**McMaster.NET.Extensions.Plugins 评估：**

| 维度 | 评价 |
|------|------|
| **功能匹配** | 提供了 `PluginLoader` 直接扫描目录 + 基于 `IServiceCollection` 的插件注册。但设计目标偏向 ASP.NET 中间件/服务插件，不是节点类型发现。当前场景需要的是「从 DLL 反射加载所有 `INodeType` 实现」，McMaster 更侧重「注册到 DI 容器」。 |
| **独立 ALC** | McMaster 内置 ALC 隔离，且会自动处理共享框架引用，比手写 `PluginLoadContext` 健壮。 |
| **TFM 兼容性检查** | McMaster 无内置 TFM 兼容性检查。当前系统有 `IsFrameworkCompatible` 专门拦截 .NETStandard 2.0 等不兼容插件。如果换 McMaster，此功能需额外实现。 |
| **测试覆盖** | 当前 `PluginLoader` 无单元测试；McMaster 本身经过测试但迁移需验证全部现有插件加载场景。 |
| **API 差异** | 当前 API：`new PluginLoader(dir, logger).LoadNodes()` → `List<INodeType>`<br>McMaster API：`loader.LoadDefaultPlugins()` → `Assembly` 列表，需额外反射。 |
| **可收集性** | 当前 `isCollectible: true` 支持卸载。McMaster 默认也可收集。 |
| **System.Composition（MEF2）** | System.Composition 已在传递依赖中。MEF2 是基于契约的插件模型，支持按 `[Export]`/`[Import]` 组装。但需要插件项目添加 `[Export(typeof(INodeType))]` 特性。现有插件没有此特性，需全部改动。 |

**决策建议：不替换。**

**理由：**
1. 当前实现（170 行 + 46 行）已覆盖全部需求（DLL 扫描 → ALC 隔离 → TFM 兼容检查 → 反射加载），是成熟稳定的样板代码。
2. McMaster 的主要价值点在 DI 集成（自动调用 `AddXxx`），与当前场景不匹配。
3. System.Composition（MEF2）需要为每个节点类加 `[Export]` 特性，侵入性大。
4. 当前实现正好在 `FlowEngine.Runtime.Registry` 中，未来如需冷热替换可在同一位置原地增强，无需用库。

**不替换保留，但建议补充：**
- 为 `PluginLoader` 补充单元测试（集成测试：准备测试 DLL → 加载 → 验证类型发现）
- `PluginLoadContext` 异常处理已覆盖 5 种场景，保持现有

---

#### 评估 B：RBAC 授权 → Casbin.NET

**当前实现：**
- `IAuthorizationService`（17 行）：`HasPermission(roles, scope, operation)` 判断权限
- `AuthorizationService` 实现：根据权限矩阵查表判断
- `IResourceAuthorizationService`（69 行）：资源级授权接口，含 6 个 `CanAccess*` 方法
- `AuthorizationGuard`：统一鉴权门面，组合角色判断 + 资源归属 + 审计日志
- 底层：`Role` 枚举 + `Operation` 枚举 + 权限矩阵硬编码

**Casbin.NET 评估：**

| 维度 | 评价 |
|------|------|
| **功能匹配** | Casbin 支持 RBAC、ABAC、RESTful 资源模式。当前只有 RBAC，Casbin 完全覆盖。 |
| **策略存储** | Casbin 支持文件、数据库、Redis 等多种适配器。当前策略硬编码在 `AuthorizationService` 中。Casbin 的策略可热更新。 |
| **API 差异** | 当前：`authService.HasPermission(roles, scope, operation)` → `bool`<br>Casbin：`enforcer.Enforce(subject, domain, object, action)` → `bool`。需要一层适配包装。 |
| **资源级授权** | Casbin 的 RESTful 策略模式可以表达资源级权限（如 `/workflows/:id`），但当前 `ResourceAuthorizationService` 的归属校验（OwnsProjectAsync）是自定义逻辑，Casbin 不处理归属。 |
| **迁移工作** | 需重新定义策略模型（model.conf + policy.csv/DB），替换 `AuthorizationService` 实现，测试覆盖。`AuthorizationGuard`、`ResourceAuthorizationService`、鉴权中间件都需要调整。 |
| **测试影响** | 大量测试创建了 `TestAuthorizationService`、`StubResourceAuthorizationService` 等桩。迁移需要这些桩适配 Casbin。 |

**决策建议：不替换（暂时保留，中期可考虑）。**

**理由：**
1. 现有实现已完整覆盖 Beta 阶段需求：角色定义（Admin/Editor/Viewer）+ Scope（6 种资源）+ Operation（Read/Write/Execute/Delete）+ 资源归属校验 + 凭据脱敏。
2. 当前接口（`IAuthorizationService` + `IResourceAuthorizationService`）是深度合适的抽象——调用方只需 `HasPermission` / `CanAccess*`，实现逻辑可以随时替换。
3. Casbin 的主要价值（策略热更新、多模型切换、Adapter 持久化）当前阶段不需要。权限矩阵高频变动的需求尚不存在。
4. 迁移成本高：测试桩散布 9+ 个文件，`AuthorizationGuard` 复合逻辑包含审计事件、Decorator 模式，改造风险较大。

**如果未来需要替换，推荐过渡路径：**
- 保持 `IAuthorizationService` 接口不变
- 新增 `CasbinAuthorizationService` 实现，从现有 `AuthorizationService` 复制策略
- 用配置开关切换实现，AB 测试后再移除旧实现
- 现有测试桩保持供单元测试使用

---

#### 评估 C：OAuth2 令牌服务 → IdentityModel

**当前实现：**
- `OAuth2TokenService`（414 行）：手写 OAuth2 客户端凭据/token 请求、缓存、重试、参数映射、响应解析
- `IOAuth2TokenService` 接口
- 支持：自定义参数映射 (`ParamNameMap`)、业务错误码路径 (`ResponseErrorPath`)、令牌路径 (`TokenPath`)、Query/Body 参数位置、可配置重试

**IdentityModel 评估：**

| 维度 | 评价 |
|------|------|
| **功能匹配** | IdentityModel 是 .NET OAuth2/OpenID Connect 的标准客户端库。覆盖客户端凭据、授权码、刷新令牌等流程。但**不支持**自定义参数映射、自定义令牌响应路径等灵活配置。 |
| **API 差异** | IdentityModel：`new HttpClient().RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest { ... })`<br>当前：`new OAuth2TokenService(httpFactory).GetTokenAsync(new OAuth2TokenRequest { ... })` |
| **灵活性** | 当前实现是为对接第三方 OAuth2（钉钉、飞书等）而设计的，这些平台有非标准参数名（如 `appkey`/`appsecret` 代替 `client_id`/`client_secret`）、非标准响应路径（`errcode`/`errmsg` 业务错误封装在 200 响应体中）。IdentityModel 假设标准 OAuth2 行为，对这些非标准场景支持不足。 |
| **可维护性** | 当前 414 行自行维护。IdentityModel 减少维护但增加灵活性约束。 |
| **依赖冲突** | IdentityModel (`System.IdentityModel.Tokens.Jwt` 8.19.1) 已作为传递依赖存在（通过 JwtBearer），无需额外引用。 |

**决策建议：不替换。保留当前实现，但缩小范围。**

**理由：**
1. 当前实现的灵活性是必需的——对接钉钉、飞书等国内平台时，参数名映射和业务错误码检测是刚需。IdentityModel 的标准 OAuth2 流程不支持这些。
2. 如果只对标准 OAuth2（GitHub、Google、Azure AD），IdentityModel 是更好选择。但系统需要支持非标准平台。
3. 414 行代码中约 200 行用于处理非标准场景（`ApplyNameMap`、`NavigatePath`、`ResponseErrorPath`、`ExtractStringToken` 等），换 IdentityModel 要么丢失功能，要么手写更多包装层。

**建议保留但改进：**
- 将 `OAuth2TokenService` 从 `FlowEngine.Runtime` 移到 `FlowEngine.Application`（凭据/认证属于应用层）
- 抽象出标准 OAuth2 路径（用 IdentityModel）和非标准路径（保留当前实现），通过策略模式选择
- 补充单元测试（当前 `OAuth2TokenService` 无测试文件）

---

## 4. 阶段依赖图

```mermaid
flowchart LR
    subgraph 快速取胜
        T1[1.1 Mapster]
        T2[1.2 FluentValidation]
        T3[1.3 Stateless]
        T4[1.4 Dapper]
    end

    subgraph 中期替换
        T4[2.1 MediatR]
        T5[2.2 Audit.NET]
        T6[2.3 RateLimiting]
    end

    subgraph 深度评估
        E1[3A Plugin 评估]
        E2[3B RBAC 评估]
        E3[3C OAuth2 评估]
    end

    T1 --> T4
    T2 --> T5
    T3 --> T6

    T4 --> T5
    T4 --> E1
    T5 --> E2
    T6 --> E3
```

各快速取胜任务互不依赖，可并行执行。MediatR 先于 Audit.NET（后者依赖事件总线）。深度评估 3 项互不依赖，可作为独立审查并行完成。

## 5. 风险与待定项

| 风险 | 影响 | 应对 |
|------|------|------|
| Mapster 在某些复杂 DTO 映射上与手写行为不一致 | 数据错误 | 替换后跑全部 `dotnet test` + 手动验证关键路径；自定义映射用显式 `TypeAdapterConfig` |
| Dapper 引入后 DbExecutor 的事务/连接管理需保持 | 事务不回滚或跨连接 | Dapper `CommandDefinition` 传入 `_transaction`，保持现有生命周期 |
| FluentValidation 未覆盖所有校验场景 | 校验缺失 | 逐 DTO 对照现有校验逻辑编写 Validator，不得遗漏 |
| MediatR 事务/异常行为与 InMemoryEventBus 不同 | 业务行为偏差 | 先并行运行对比，预留开关可回退 |
| Audit.NET 替换后查询接口不兼容 | AuditLogReader 调用方需适配 | 保留 `IAuditLogReader` 接口不变，只换实现 |
| 插件加载不替换（评估结论）但未来有新需求 | 边界成本累积 | `PluginLoader` 只有 170 行，未来如有新需求直接原地增强，替换不是唯一出路 |
| Casbin.RBAC 不替换但权限逻辑持续增长 | 权限矩阵越滚越大 | 接受当前 `AuthorizationService` 的简单实现，直到出现「热更新策略」或「多租户」等强需求 |
| OAuth2TokenService 无测试 | 回归风险 | 补充单元测试作为本计划一部分 |

## 6. 验收总标准

- [ ] 全部任务完成且 `dotnet build` 通过
- [ ] `dotnet test` 全绿，无回归
- [ ] 手写 DTO 映射从 Service 层消除（可使用 `.Adapt<>()` 或保留少量特殊映射在同一文件中标注 `// custom mapping`）
- [ ] 零散 `if (p == null) return error` 校验从 Service 逻辑层消除（改为 FluentValidation Validator 或保留的 DataAnnotations）
- [ ] 插件层手写 `CreateParameter`/`@p{i}` 循环消除，改为 Dapper 调用
- [ ] `DbExecutor` 新增 `QueryAsync<T>` 供给 `DbReaderNode` 等未来读节点使用
- [ ] 3 份深度评估文档完成并归档
- [ ] 原计划不替换的模块已补充测试或缩小范围
- [ ] `[Obsolete]` 标记已标注到待删除代码上，待独立清理计划再删除
