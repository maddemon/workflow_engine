---
description: 后端 C# / .NET 代码规范、目录结构、命名规范、DI、Service 设计、EF Core 数据访问、异常处理、日志、测试。
globs: ["**/*.cs", "**/*.csproj"]
---
> 后端代码规范。所有参与本项目的 AI Agent 与协作者在修改后端代码前必须阅读。

# 后端代码规范

## 1. 技术栈

- C# / .NET
- ASP.NET Core Web API
- Entity Framework Core（统一数据访问层，可对接 SQL Server、PostgreSQL、MySQL、SQLite 等）
- 依赖注入原生容器
- xUnit / NUnit（测试框架待统一）

## 2. 目录结构

后端源码统一放在 `src/backend/` 下：

```
src/backend/
├── FlowEngine.Api/                   # Web API 入口
│   ├── Controllers/                  # 控制器，只负责接收请求、调用 Service、返回 DTO/ActionResult
│   ├── Middlewares/                  # 自定义中间件
│   ├── Program.cs
│   └── appsettings.json
├── FlowEngine.Application/           # 应用层：业务用例编排
│   ├── Services/                     # 业务服务，按领域模块分子目录
│   │   ├── Nodes/
│   │   ├── Workflows/
│   │   └── Executions/
│   ├── Dtos/                         # 请求/响应 DTO
│   ├── Validators/                   # 输入校验（FluentValidation 或原生）
│   └── Mappings/                     # 对象映射配置
├── FlowEngine.Domain/                # 领域层：核心业务模型
│   ├── Entities/                     # 领域实体（POCO，无 EF 依赖）
│   ├── ValueObjects/                 # 值对象
│   ├── Events/                       # 领域事件
│   └── Exceptions/                   # 领域异常
├── FlowEngine.Infrastructure/        # 基础设施层：具体实现
│   ├── Data/                         # DbContext、迁移、查询、EF 配置
│   ├── Plugins/                      # 节点插件加载器
│   ├── Credentials/                  # 凭据存储与加密
│   ├── ExpressionEngine/             # 表达式求值实现
│   └── ExternalServices/             # 外部 API 封装
├── FlowEngine.Plugins/               # 内置节点插件
│   ├── Http/
│   ├── Logic/
│   └── Database/                     # 数据库节点：对接各种数据库读取和写入
├── FlowEngine.Contracts/             # 前后端共享契约（DTO、枚举）
│   ├── NodeTypes/
│   ├── Workflows/
│   └── Common/
└── FlowEngine.Tests/                 # 测试项目
    ├── Unit/
    ├── Integration/
    └── Plugins/
```

### 2.1 各目录职责

| 目录 | 放什么 | 不放什么 |
|------|--------|----------|
| `Controllers/` | 接收 HTTP 请求、参数绑定、调用 Service、返回 DTO/ActionResult | 业务逻辑、DbContext 调用 |
| `Services/` | 业务用例编排、领域对象调用、通过 DbContext 读写数据、事务控制 | 直接操作 HTTP 上下文 |
| `Domain/Entities/` | 纯领域模型、业务规则方法、Data Annotations 元数据 | 导航属性依赖 DbContext |
| `Infrastructure/Data/` | DbContext、迁移、EF 配置、复杂查询封装 | 业务逻辑 |
| `FlowEngine.Plugins/Database/` | 数据库节点实现（SQL 执行、读取、写入） | 核心引擎逻辑 |
| `Contracts/` | 前后端共享的 DTO、枚举 | 业务逻辑、EF 实体 |

### 2.2 领域层与数据访问边界

- 领域实体放在 `Domain/Entities/`，使用 Data Annotations 声明表名、索引、列注释等元数据。
- EF 配置统一通过 Data Annotations 在实体类上完成，禁止在 `Infrastructure/Data/Configurations/` 中使用 Fluent API。
- Service 层直接使用 `DbContext` 进行数据读写，不强制定义 `IRepository` 接口。
- 只有跨多个 Service 复用的复杂查询，才考虑封装到 `Infrastructure/Data/Queries/` 中。

## 3. 命名规范

| 类型 | 命名 | 示例 |
|------|------|------|
| 类/结构体 | PascalCase | `ExecutionEngine` |
| 接口 | PascalCase，前缀 `I` | `INodeRegistry` |
| 方法/属性 | PascalCase | `ExecuteAsync` |
| 局部变量/参数 | camelCase | `executionContext` |
| 私有字段 | `_camelCase` | `_nodeRegistry` |
| 常量 | PascalCase 或 UPPER_SNAKE_CASE | `MaxRetryCount` |
| 异步方法 | 以 `Async` 结尾 | `ExecuteWorkflowAsync` |
| 泛型约束 | `T` + 描述 | `TNode where TNode : INodeType` |

## 4. 依赖注入与构造函数

### 4.1 使用 primary constructor

C# 12+ 优先使用 primary constructor，保持类声明简洁。

```csharp
public class WorkflowService(
    WorkflowDbContext dbContext,
    IExecutionEngine executionEngine,
    ILogger<WorkflowService> logger)
{
    public async Task<WorkflowDto> GetAsync(Guid id)
    {
        logger.LogInformation("Getting workflow {WorkflowId}", id);
        var workflow = await dbContext.Workflows.FindAsync(id);
        return workflow.ToDto();
    }
}
```

### 4.2 Controller 禁止注入 DbContext

Controller 只能注入 Application Service，禁止直接注入 `DbContext`。

正确：
```csharp
[ApiController]
[Route("api/[controller]")]
public class WorkflowsController(IWorkflowService workflowService) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<WorkflowDto> Get(Guid id)
    {
        return await workflowService.GetAsync(id);
    }
}
```

错误：
```csharp
[ApiController]
[Route("api/[controller]")]
public class WorkflowsController(WorkflowDbContext dbContext) : ControllerBase  // ❌ 禁止
{
    [HttpGet("{id}")]
    public async Task<WorkflowDto> Get(Guid id)
    {
        return await dbContext.Workflows.FindAsync(id).ToDto();  // ❌ 业务逻辑泄露到 Controller
    }
}
```

## 5. Service 设计

### 5.1 不需要不必要的抽象

- Service 是具体业务实现，不需要为每个 Service 都定义 `IXxxService` 接口，除非有多实现或单元测试需要 mock。
- 数据访问直接使用 EF Core 的 `DbContext`，不需要再包一层 `IRepository`。
- 如果只有一个实现，直接写具体类并注册到 DI。

正确：
```csharp
public class NodeTypeService(INodeRegistry nodeRegistry)
{
    public IReadOnlyList<NodeTypeDescriptor> ListAll()
    {
        return nodeRegistry.GetAll();
    }
}
```

错误：
```csharp
public interface INodeTypeService  // ❌ 只有一个实现，没必要先定义接口
{
    IReadOnlyList<NodeTypeDescriptor> ListAll();
}

public class NodeTypeService : INodeTypeService
{
    // ...
}
```

### 5.2 Service 直接使用 DbContext

Service 负责业务编排和数据读写，直接注入 `DbContext` 是允许的，也是推荐做法。

```csharp
public class WorkflowService(WorkflowDbContext dbContext)
{
    public async Task CreateAsync(CreateWorkflowDto dto)
    {
        var workflow = dto.ToEntity();
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
    }
}
```

### 5.3 Service 只编排业务，不直接操作基础设施

Service 可以直接使用 DbContext，但不直接写特定数据库的 SQL 方言、不直接操作文件系统、不直接发 HTTP 请求。

## 6. 数据访问层

### 6.1 使用 EF Core 作为统一抽象

- EF Core 是本系统统一的数据访问层，通过不同 Provider 对接 SQL Server、PostgreSQL、MySQL、SQLite 等。
- `DbContext` 定义在 `Infrastructure/Data/`，Service 通过构造函数注入使用。
- 复杂查询可封装在 `Infrastructure/Data/Queries/` 中，以静态方法或查询类形式存在。

### 6.2 不要写特定数据库的 SQL 方言

- 优先使用 LINQ 和 EF Core 的查询能力。
- 必须手写 SQL 时，只使用标准 SQL，避免特定数据库方言，确保可移植性。
- 特定数据库的优化或特殊功能，应封装在 `Infrastructure/Data/{Provider}/` 中，并通过接口暴露。

### 6.3 读写与事务

- 写操作通过 `DbContext.SaveChangesAsync()` 完成。
- 复杂业务用例在 Service 层控制事务边界：
  ```csharp
  await using var transaction = await dbContext.Database.BeginTransactionAsync();
  // ... 多次 SaveChanges
  await transaction.CommitAsync();
  ```
- 高频读取场景可引入只读副本或缓存，但须通过接口抽象，不直接依赖具体中间件。

### 6.4 统一使用 Data Annotations 配置 EF

禁止使用 Fluent API（`IEntityTypeConfiguration<T>`、`modelBuilder.Entity<T>()` 等）配置实体。表名、索引、关系、列注释、数据类型等统一通过 Data Annotations 在实体类上声明。

正确：

```csharp
[Table("workflows", Schema = "flow")]
[Index(nameof(Code), IsUnique = true)]
[Comment("工作流定义")]
public class Workflow
{
    [Key]
    [Column("id")]
    [Comment("主键")]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    [Column("code")]
    [Comment("工作流编码")]
    public string Code { get; set; } = null!;

    [ForeignKey(nameof(Creator))]
    [Column("creator_id")]
    [Comment("创建人 ID")]
    public Guid CreatorId { get; set; }

    public User Creator { get; set; } = null!;
}
```

错误：

```csharp
public class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>  // 禁止
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.ToTable("workflows", "flow");
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Code).HasMaxLength(128).HasComment("工作流编码");
    }
}
```

## 7. 节点数据库能力

### 7.1 Database 节点

- 系统需要提供数据库节点，支持流程节点对各种数据库进行读取和写入。
- 数据库节点放在 `FlowEngine.Plugins/Database/` 下，每个具体数据库类型可作为一个子节点或参数化配置。
- 数据库节点通过 DbContext、ADO.NET 或专用数据库驱动执行 SQL，返回数据批次供下游节点使用。

### 7.2 数据库节点规范

- 数据库连接字符串、凭据通过凭据系统注入，禁止硬编码在节点代码中。
- 节点执行时须校验 SQL，禁止执行危险操作（如 DROP、TRUNCATE）除非显式开启。
- 读取节点返回数据批次，写入节点返回影响行数或执行结果。
- 数据库节点的参数定义应包含：连接凭据、数据库类型、SQL 或操作模式、超时时间等。

## 8. 控制器返回规范

- 统一返回 DTO，不返回领域实体。
- 简单成功响应直接返回 DTO，框架会自动包装为 HTTP 200：
  ```csharp
  [HttpGet("{id}")]
  public async Task<WorkflowDto> Get(Guid id)
  {
      return await workflowService.GetAsync(id);
  }
  ```
- 需要返回特定状态码（201、204、400、404 等）时才使用 `ActionResult<T>`：
  ```csharp
  [HttpPost]
  public async Task<ActionResult<WorkflowDto>> Create(CreateWorkflowDto dto)
  {
      var workflow = await workflowService.CreateAsync(dto);
      return CreatedAtAction(nameof(Get), new { id = workflow.Id }, workflow);
  }
  ```
- 错误响应由统一异常中间件处理，统一格式：
  ```json
  {
    "success": false,
    "errorCode": "WorkflowNotFound",
    "message": "工作流不存在",
    "details": null
  }
  ```

## 9. 异常处理

- 领域异常继承 `DomainException`，Application 层捕获后转换为统一错误响应。
- 控制器层使用统一异常过滤中间件，不每个 Action 都 `try-catch`。
- 禁止在内部随意吞异常。

## 10. 日志

- 使用 `ILogger<T>`，禁止 `Console.WriteLine`。
- 日志中不得输出凭据、Token、私钥等敏感信息。
- 使用结构化日志模板，避免字符串拼接：
  ```csharp
  logger.LogInformation("Executing workflow {WorkflowId}", workflowId);
  ```

## 11. 插件与节点 DLL

- 节点插件 DLL 统一输出到 `plugins/` 目录。
- 加载插件时使用独立 `AssemblyLoadContext`，避免依赖冲突。
- 插件加载失败不影响主程序启动，须记录警告日志。

## 12. 测试

### 12.1 测试策略

采用 **TDD（测试驱动开发）** 模式：先写测试用例，再实现功能。

| 层级 | 覆盖目标 | 测试框架 |
|------|----------|----------|
| 单元测试 | 表达式求值、参数解析、DTO 转换、业务规则 | xUnit v3 |
| 集成测试 | API 端到端、数据库读写、工作流执行 | xUnit + WebApplicationFactory |

### 12.2 必须测试的场景

新增功能或修复 Bug 时，**必须**包含以下测试：

1. **正常路径**：功能按预期工作
2. **边界条件**：空值、空字符串、空集合、零值
3. **类型转换**：`JsonElement` ↔ `string`、枚举反序列化、`null` 处理
4. **异常路径**：无效输入、缺失参数、类型不匹配

### 12.3 测试命名规范

```
{方法名}_{场景}_{预期结果}
```

示例：
- `Resolve_JsonElement_String_Evaluates_Expression`
- `Hydrate_Empty_JsonElement_Sets_Null`
- `Execute_Returns_Object_As_JsonObject`

### 12.4 测试项目结构

```
tests/
├── FlowEngine.Core.Tests/          # 核心实体、值对象、领域事件
├── FlowEngine.Application.Tests/   # DTO 转换、业务服务
├── FlowEngine.Runtime.Tests/       # 表达式引擎、参数解析、节点插件
│   ├── Expressions/                # 表达式求值器测试
│   ├── Registry/                   # 参数解析器、注入器测试
│   └── Plugins/                    # 节点插件测试
└── FlowEngine.TestPlugin/          # 测试用虚拟节点插件
```

### 12.5 运行测试

```powershell
# 运行所有测试
dotnet test

# 运行指定项目
dotnet test tests\FlowEngine.Runtime.Tests

# 运行指定测试类
dotnet test --filter "ExpressionEvaluatorComparisonTests"
```

### 12.6 新增节点插件的测试要求

每个节点插件必须有对应的测试文件，覆盖：
- 正常执行返回正确输出
- 空/缺失参数的错误处理
- `JsonElement` 类型参数的正确转换
- 输出数据格式符合 `DataBatch` → `DataItem` 结构

## 13. 错误示范速查

| 错误 | 正确 |
|------|------|
| Controller 注入 `DbContext` | Controller 注入 Service |
| Service 直接写特定数据库 SQL 方言 | Service 通过 EF Core DbContext 访问数据 |
| 每个 Service 都定义接口 | 只有一个实现时直接写类 |
| 返回领域实体 | 返回 DTO |
| `Console.WriteLine` | `ILogger<T>` |
| 在 Controller 写业务逻辑 | Controller 只负责路由和调用 |
| 随意吞掉异常 | 使用统一异常中间件/过滤器 |
| 使用 Fluent API 配置 EF | 使用 Data Annotations 在实体类上声明 |
| 公共类/方法缺少 `///` 文档注释 | 所有类和方法使用 XML 文档注释 |
| 实体字段缺少 `Comment` 属性 | 字段同时加 `///` 注释和 `[Comment]` |
| 枚举值无中文描述 | 枚举值使用 `[Description]` 标注中文 |

## 14. 注释与文档

### 14.1 关键逻辑注释

复杂算法、业务规则、边界处理等关键逻辑必须编写注释，说明设计意图和注意事项。注释应简洁准确，避免冗余。

### 14.2 类和方法文档注释

所有类和方法必须使用 `///` XML 文档注释，包含功能说明、参数含义和返回值说明。公共 API 还需注明可能抛出的异常。

正确：

```csharp
/// <summary>
/// 工作流执行引擎。
/// </summary>
public class ExecutionEngine
{
    /// <summary>
    /// 执行指定工作流。
    /// </summary>
    /// <param name="workflowId">工作流 ID。</param>
    /// <param name="context">执行上下文。</param>
    /// <returns>执行结果。</returns>
    /// <exception cref="WorkflowNotFoundException">工作流不存在时抛出。</exception>
    public async Task<ExecutionResult> ExecuteAsync(Guid workflowId, ExecutionContext context)
    {
        // ...
    }
}
```

错误：

```csharp
public class ExecutionEngine  // 缺少文档注释
{
    public async Task<ExecutionResult> ExecuteAsync(Guid workflowId, ExecutionContext context)
    {
        // ...
    }
}
```

### 14.3 字段注释与 Comment 属性

实体类和 DTO 中的所有字段/属性必须同时满足：

- 使用 `///` XML 文档注释说明字段含义。
- 使用 `[Comment]` 属性写入数据库元数据。

正确：

```csharp
public class Workflow
{
    /// <summary>
    /// 工作流编码，全局唯一。
    /// </summary>
    [Required]
    [MaxLength(128)]
    [Comment("工作流编码，全局唯一")]
    public string Code { get; set; } = null!;
}
```

错误：

```csharp
public class Workflow
{
    public string Code { get; set; } = null!;  // 缺少注释和 Comment 属性
}
```

### 14.4 Enum 中文描述

枚举值必须使用 `[Description]` 属性标注中文含义，便于序列化、日志和前端展示。

正确：

```csharp
public enum WorkflowStatus
{
    /// <summary>
    /// 草稿。
    /// </summary>
    [Description("草稿")]
    Draft = 0,

    /// <summary>
    /// 已发布。
    /// </summary>
    [Description("已发布")]
    Published = 1,

    /// <summary>
    /// 已停用。
    /// </summary>
    [Description("已停用")]
    Disabled = 2
}
```

错误：

```csharp
public enum WorkflowStatus
{
    Draft = 0,
    Published = 1,
    Disabled = 2
}
```

## 15. 异步与并发

### 15.1 禁止在同步上下文中阻塞异步操作

禁止使用 `.GetAwaiter().GetResult()` 或 `.Result` 阻塞异步任务。在 ASP.NET 的 `SynchronizationContext` 中会导致死锁。

正确：

```csharp
public async Task<CredentialValue> HydrateAsync(INodeType instance, ...)
{
    var credential = await _credentialAccessor.GetCredentialAsync(id, ct);
    return credential;
}
```

错误：

```csharp
public void Hydrate(INodeType instance, ...)
{
    var credential = _credentialAccessor.GetCredentialAsync(id, ct)
        .ConfigureAwait(false).GetAwaiter().GetResult();  // ❌ 死锁风险
}
```

### 15.2 Task.Run 中禁止捕获 Scoped 依赖

`Task.Run` 在独立线程执行，原始 HTTP 请求作用域已销毁。若需使用 Scoped 服务（如 `DbContext`、`IExecutionStore`），必须在 `Task.Run` 内创建新作用域。

正确：

```csharp
_ = Task.Run(async () =>
{
    using var scope = _scopeFactory.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<IExecutionStore>();
    await store.SaveAsync(record, ct);
});
```

错误：

```csharp
_ = Task.Run(async () =>
{
    await _executionStore.SaveAsync(record, ct);  // ❌ 原作用域已销毁
});
```

### 15.3 优先使用 Entity.NewId()

生成主键时优先使用 `Entity.NewId()`（UUIDv7 有序 GUID），而非 `Guid.NewGuid()`。UUIDv7 对 SQLite 索引更友好。

## 16. 查询性能

### 16.1 只读查询使用 AsNoTracking()

不修改实体的查询应使用 `.AsNoTracking()`，避免 EF Change Tracker 的内存开销。

```csharp
var entity = await _context.Workflows
    .AsNoTracking()
    .FirstOrDefaultAsync(x => x.Id == id);
```
