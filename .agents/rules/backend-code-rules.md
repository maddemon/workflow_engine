---
description: 后端 C# / .NET 代码规范、目录结构、命名规范、DI、Service 设计、EF Core 数据访问、异常处理、日志、测试。
globs: ["**/*.cs", "**/*.csproj"]
---
> 后端代码规范。所有修改后端代码的 AI Agent 与协作者必须先阅读。

# 后端代码规范

## 1. 技术栈
- C# 12+ / .NET（当前 `net10.0`）
- ASP.NET Core Web API
- Entity Framework Core（统一数据访问，可对接 SQL Server / PostgreSQL / MySQL / SQLite）
- 原生依赖注入容器
- 测试框架：**xUnit v3**（全仓库统一）

## 2. 目录结构
后端源码在 `backend/`，插件在 `plugins/`，测试在 `tests/`（与 backend 平级）。

> 早期设想过分层 `src/backend/FlowEngine.{Api,Domain,Contracts,Plugins}`，实际落地为扁平的 `backend/FlowEngine.*`（本文件所述结构为权威依据）。

```
backend/
├── FlowEngine.Core/          # 最内层：实体、契约、领域事件、值对象，及下沉的脚本/HTTP/Agent/Tools；并承载 DbContext（EF Core 依赖在此）
│   ├── Abstractions/ Entities/ Events/ ValueObjects/ Scripting/ Http/ Agent/ Tools/
│   ├── Data/                 # FlowEngineDbContext
│   └── …（Ai, Attributes, Authorization, Configuration, Credentials, Dtos, Enums, Exceptions, Expressions, Identity, DependencyInjection）
│   依赖：EF Core + Extensions.Logging + DI + Options + Jint
├── FlowEngine.Runtime/       # 执行引擎：Executor / Expressions / Registry；依赖 Core + Logging
├── FlowEngine.Application/   # 用例编排：Workflows / Executions / Services / Dtos / Validators；依赖 Core + Runtime
├── FlowEngine.Infrastructure/# 适配器：Audit / Ai / Identity / Security / Storage；依赖 Core + Application（实现其接口）
├── FlowEngine.Migrations/    # EF 迁移程序集；依赖 Core + Infrastructure
└── FlowEngine.Host/          # 组合根：Controllers / Webhooks / WebSocketHandlers / Middlewares / Scheduling / Mcp / Program.cs / wwwroot
plugins/FlowEngine.Plugins.Standard/   # 热插拔节点（HTTP/Code/If/Loop/Merge/Agent/LLM/DB 等）；只引用 Core，绝不引用 Application/Runtime/Infrastructure
tests/  FlowEngine.Core.Tests / Application.Tests / Runtime.Tests / Host.Tests / TestPlugin
```

### 2.1 各目录职责
| 目录 | 放什么 | 不放什么 |
|------|--------|----------|
| `Host/Controllers/` | 接收请求、参数绑定、调用 Service、返回 DTO | 业务逻辑、DbContext |
| `Application/Services/` | 用例编排、领域调用、通过 DbContext 读写、事务 | 直接操作 HTTP 上下文 |
| `Core/Entities/` | 纯领域模型、业务规则、Data Annotations 元数据 | 依赖 DbContext 的导航逻辑 |
| `Infrastructure/` | DbContext 适配、持久化/加密/存储/调度实现 | 业务逻辑 |
| `Application/Dtos/` | 前后端 DTO、枚举 | 业务逻辑、EF 实体 |

### 2.2 领域层与数据访问边界
- 实体在 `Core/Entities/`，继承 `Entity` 基类（自动以 UUIDv7 生成 `Id`）；表名/索引/列注释等用 **Data Annotations** 声明。
- EF 配置**优先 Data Annotations**；**Fluent API 仅允许用于 Data Annotations 无法表达之处**（如 `[JsonColumn]` 的 JSON 列转换、程序化唯一索引），见 `FlowEngineDbContext.OnModelCreating`。
- Service 层直接使用 `DbContext` 读写，不强制定义 `IRepository`；仅跨多 Service 复用的复杂查询才封装到 `Infrastructure` 查询类。

## 3. 命名规范
| 类型 | 命名 | 示例 |
|------|------|------|
| 类/结构体 | PascalCase | `ExecutionEngine` |
| 接口 | `I` + PascalCase | `INodeRegistry` |
| 方法/属性 | PascalCase | `ExecuteAsync` |
| 局部变量/参数 | camelCase | `executionContext` |
| 私有字段 | `_camelCase` | `_nodeRegistry` |
| 常量 | PascalCase 或 UPPER_SNAKE_CASE | `MaxRetryCount` |
| 异步方法 | 以 `Async` 结尾 | `ExecuteWorkflowAsync` |
| 泛型约束 | `T` + 描述 | `TNode where TNode : INodeType` |

## 4. 依赖注入与构造函数
- **优先 primary constructor**（C# 12+），保持声明简洁。
- **Controller 禁止注入 DbContext**，只能注入 Application Service。
  ```csharp
  public class WorkflowsController(IWorkflowService s) : ControllerBase
  {
      [HttpGet("{id}")] public async Task<WorkflowDto> Get(Guid id) => await s.GetAsync(id);
  }
  // ❌ 禁止：WorkflowsController(WorkflowDbContext dbContext)
  ```

## 5. Service 设计
- 只有一个实现时**直接写具体类**，不必先定义 `IXxxService` 接口（多实现或需 mock 时才定义）。
- Service 直接注入并使用 `DbContext` 读写数据（推荐做法）。
- Service 只编排业务，**不直接写特定数据库 SQL 方言、不直接操作文件系统、不直接发 HTTP 请求**（这些下沉到 Infrastructure）。

## 6. 数据访问层
- EF Core 为统一数据访问层；`DbContext` 定义在 `Core/Data/`，Service 经构造函数注入使用。
- 优先 LINQ；必须手写 SQL 时只写标准 SQL，避免方言，可移植封装放 `Infrastructure`。
- 写操作经 `SaveChangesAsync()`；复杂用例在 Service 层控制事务：
  ```csharp
  await using var tx = await dbContext.Database.BeginTransactionAsync();
  // … 多次 SaveChanges
  await tx.CommitAsync();
  ```
- 高频只读可引入只读副本/缓存，但须经接口抽象。

## 7. 节点数据库能力
- 数据库节点放 `plugins/`（标准插件 `FlowEngine.Plugins.Standard` 已内置 DB/HTTP 类节点），通过 DbContext / ADO.NET / 驱动执行 SQL，返回数据批次。
- 连接串与凭据经凭据系统注入，**禁止硬编码**；执行前校验 SQL，禁止 DROP/TRUNCATE 等危险操作（除非显式开启）。
- 参数须含：连接凭据、数据库类型、SQL/操作模式、超时时间。

## 8. 控制器返回规范
- 统一返回 DTO，不返回领域实体。
- 简单成功直接返回 DTO（框架包装 200）；需特定状态码时用 `ActionResult<T>`（`CreatedAtAction` 等）。
- 错误由统一异常中间件处理，格式：
  ```json
  { "success": false, "errorCode": "WorkflowNotFound", "message": "工作流不存在", "details": null }
  ```

## 9. 异常处理
- 领域异常继承 `DomainException`，Application 层捕获后转统一错误响应。
- 用统一异常中间件/过滤器，**不每个 Action 都 try-catch**；**禁止随意吞异常**。

## 10. 日志
- 用 `ILogger<T>`，禁止 `Console.WriteLine`。
- 日志不得输出凭据、Token、私钥等敏感信息。
- 用结构化模板，避免字符串拼接：`logger.LogInformation("Executing workflow {WorkflowId}", id);`

## 11. 插件与节点 DLL
- 节点插件 DLL 输出到 `plugins/`，用独立 `AssemblyLoadContext` 加载避免依赖冲突。
- 插件加载失败不影响主程序启动，须记录警告日志。

## 12. 测试
- 采用 **TDD**：先写测试再实现。
- 框架 **xUnit v3**；单元测试覆盖表达式/参数解析/DTO/业务规则，集成测试用 `WebApplicationFactory` 做端到端。
- 必须覆盖：正常路径、边界（空值/空串/空集合/零值）、类型转换（`JsonElement`↔`string`/枚举/`null`）、异常路径。
- 命名 `{方法名}_{场景}_{预期结果}`，如 `Resolve_JsonElement_String_Evaluates_Expression`。
- 每个节点插件须有对应测试：正常输出、空/缺参错误、`JsonElement` 转换、输出符合 `DataBatch`→`DataItem`。
- 运行：`dotnet test` / `dotnet test tests\FlowEngine.Runtime.Tests` / `dotnet test --filter "ExpressionEvaluatorComparisonTests"`。

## 13. 错误示范速查
| 错误 | 正确 |
|------|------|
| Controller 注入 `DbContext` | 注入 Service |
| Service 写特定方言 SQL | 经 EF Core `DbContext` |
| 每个 Service 都定义接口 | 单实现直接写类 |
| 返回领域实体 | 返回 DTO |
| `Console.WriteLine` | `ILogger<T>` |
| Controller 写业务逻辑 | 只路由与调用 |
| 随意吞异常 | 统一异常中间件 |
| 用 Fluent API 配表/索引/列 | 优先 Data Annotations（仅 JSON 列/程序化索引用 Fluent） |
| 缺 `///` 文档注释 | 公共类/方法用 XML 注释 |
| 实体字段缺 `[Comment]` | 同时加 `///` 与 `[Comment]` |
| 枚举无中文描述 | 枚举值用 `[Description]` 标注 |

## 14. 注释与文档
- 复杂算法/业务规则/边界处理须写注释说明意图，简洁准确。
- 所有公共类/方法用 `///` XML 注释（功能、参数、返回值；公共 API 注明可能抛出的异常）。
- 实体/DTO 字段须同时满足：`///` 注释 + `[Comment]` 属性。
- 枚举值用 `[Description]` 标注中文含义。

## 15. 异步与并发
- 禁止 `.GetAwaiter().GetResult()` / `.Result` 阻塞异步（ASP.NET 下死锁风险）。
- `Task.Run` 中禁止捕获已销毁的 Scoped 依赖（`DbContext`/`IExecutionStore` 等），须在内部 `CreateScope()` 后取用。
- 主键优先 UUIDv7 有序 GUID：实体继承 `Entity` 基类会自动以 `Guid.CreateVersion7()` 赋值 `Id`；对 SQLite 索引更友好。临时/瞬态 ID 可用 `Guid.NewGuid()`。

## 16. 查询性能
- 不修改实体的只读查询用 `.AsNoTracking()`，减少 Change Tracker 开销。
