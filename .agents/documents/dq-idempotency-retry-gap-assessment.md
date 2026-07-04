# 数据质量检查 / 幂等键 / 超时重试 / 连接池 —— 实施计划

## 总览

用户确认全部实施。按依赖关系和优先级排序为 6 个任务：

| 序号 | 任务 | 优先级 | 涉及项目 |
|------|------|--------|----------|
| T1 | 节点执行超时生效 | P0 (Bug) | Core, Runtime |
| T2 | 全局默认超时/重试配置 | P1 | Core, Host |
| T3 | 增强重试策略（退避方式 + 可重试错误码过滤） | P3 | Core, Runtime |
| T4 | 执行级持久化幂等 + Webhook/手动触发幂等 | P0 | Core, Application, Host |
| T5 | DataQuality 节点 | P1 | Plugins.Standard |
| T6 | HTTP 连接池（IHttpClientFactory） | P2 | Runtime, Host |

---

## T1: 节点执行超时生效

**问题**：`NodeDefinition.Timeout` 已建模，但 `WorkflowExecutor.ExecuteNodeWithRetryAsync` 中完全未使用该字段。节点执行卡死时无超时保护。

**改动**：

### 1.1 修改 `WorkflowExecutor.ExecuteNodeWithRetryAsync`
- 文件：[WorkflowExecutor.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Runtime/Executor/WorkflowExecutor.cs) 第 261-353 行
- 在重试循环内，若 `node.Timeout` 有值，创建 `CancellationTokenSource` 链接 `cancellationToken`：
```csharp
CancellationTokenSource? timeoutCts = null;
CancellationToken effectiveToken;
if (node.Timeout.HasValue && node.Timeout.Value > TimeSpan.Zero)
{
    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(node.Timeout.Value);
    effectiveToken = timeoutCts.Token;
}
else
{
    effectiveToken = cancellationToken;
}
try
{
    result = await nodeType.ExecuteAsync(context, effectiveToken).ConfigureAwait(false);
}
catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
{
    // 节点超时
    var timeoutError = new NodeError
    {
        Code = "Timeout",
        Message = $"节点执行超时（{node.Timeout!.Value.TotalSeconds}s）。",
        NodeDefinitionId = node.Id
    };
    result = new NodeExecutionResult
    {
        Success = false,
        Error = timeoutError,
        Output = new DataBatch { Items = [new DataItem { Success = false, Error = timeoutError }] }
    };
}
finally
{
    timeoutCts?.Dispose();
}
```
- 超时后不重试（同取消语义），直接返回超时错误结果，由上层 ErrorStrategy 处理

### 1.2 验证
- 在 `WorkflowExecutorTests` 中添加超时测试：配置节点 `Timeout = 100ms`，节点执行耗时 500ms，断言返回 `Error.Code == "Timeout"`

---

## T2: 全局默认超时/重试配置

**问题**：节点未配置 Timeout/RetryPolicy 时无默认值，无法统一管控。

**改动**：

### 2.1 新增 `EngineDefaultsOptions`
- 文件：新建 `backend/FlowEngine.Core/Configuration/EngineDefaultsOptions.cs`
```csharp
namespace FlowEngine.Core.Configuration;

public class EngineDefaultsOptions
{
    public const string SectionName = "EngineDefaults";

    /// <summary>默认节点超时（秒），null 表示不限。</summary>
    public int? DefaultTimeoutSeconds { get; set; }

    /// <summary>默认最大重试次数。</summary>
    public int DefaultMaxRetries { get; set; } = 0;

    /// <summary>默认基础延迟（秒）。</summary>
    public int DefaultBaseDelaySeconds { get; set; } = 1;

    /// <summary>默认最大延迟（秒）。</summary>
    public int DefaultMaxDelaySeconds { get; set; } = 60;
}
```

### 2.2 注册配置
- 文件：[ServiceCollectionExtensions.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Host/ServiceCollectionExtensions.cs)
- 在 `AddFlowEngine` 中添加：`services.Configure<EngineDefaultsOptions>(configuration.GetSection(EngineDefaultsOptions.SectionName));`

### 2.3 修改 `WorkflowExecutor`
- 注入 `IOptions<EngineDefaultsOptions>`
- `ExecuteNodeWithRetryAsync` 中：
  - 超时：`node.Timeout ?? (defaults.DefaultTimeoutSeconds.HasValue ? TimeSpan.FromSeconds(defaults.DefaultTimeoutSeconds.Value) : null)`
  - 重试：`node.RetryPolicy?.MaxRetries ?? (node.ErrorStrategy == ErrorStrategy.Retry ? defaults.DefaultMaxRetries : 0)`

### 2.4 更新 appsettings.json
- 文件：[appsettings.json](file:///d:/Repos/flow_engine/backend/FlowEngine.Host/appsettings.json)
- 添加：
```json
"EngineDefaults": {
    "DefaultTimeoutSeconds": null,
    "DefaultMaxRetries": 0,
    "DefaultBaseDelaySeconds": 1,
    "DefaultMaxDelaySeconds": 60
}
```

### 2.5 验证
- 单元测试：不配置节点 Timeout，但配置 `EngineDefaults.DefaultTimeoutSeconds = 1`，节点执行超时后返回 Timeout 错误

---

## T3: 增强重试策略

**问题**：退避算法硬编码为指数退避，不支持线性/固定间隔；所有失败都重试，无法过滤。

**改动**：

### 3.1 新增 `BackoffStrategy` 枚举
- 文件：新建 `backend/FlowEngine.Core/Enums/BackoffStrategy.cs`
```csharp
namespace FlowEngine.Core.Enums;

public enum BackoffStrategy
{
    /// <summary>指数退避：delay = baseDelay * 2^attempt</summary>
    Exponential,
    /// <summary>线性退避：delay = baseDelay * (attempt + 1)</summary>
    Linear,
    /// <summary>固定间隔：delay = baseDelay</summary>
    Fixed
}
```

### 3.2 扩展 `RetryPolicy`
- 文件：[RetryPolicy.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Entities/RetryPolicy.cs)
- 新增属性：
  - `public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.Exponential;`
  - `public List<string>? RetryableErrorCodes { get; set; }` — 仅这些错误码会重试，null/空表示所有错误都重试

### 3.3 修改 `CalculateBackoff`
- 文件：[WorkflowExecutor.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Runtime/Executor/WorkflowExecutor.cs) 第 546-566 行
- 根据 `policy.BackoffStrategy` 计算：
  - `Exponential`：现有逻辑
  - `Linear`：`delay = baseDelay * (attempt + 1)`
  - `Fixed`：`delay = baseDelay`

### 3.4 修改 `ExecuteNodeWithRetryAsync`
- 在 `if (result.Success || attempt == maxRetries)` 之前，检查 `RetryableErrorCodes`：
```csharp
if (!result.Success && policy?.RetryableErrorCodes?.Count > 0)
{
    var errorCode = result.Error?.Code ?? string.Empty;
    if (!policy.RetryableErrorCodes.Contains(errorCode))
    {
        return result; // 不可重试的错误码，直接返回
    }
}
```

### 3.5 验证
- 单元测试：
  - 配置 `BackoffStrategy.Linear`，验证 delay 按 baseDelay * (attempt+1) 递增
  - 配置 `RetryableErrorCodes = ["Timeout"]`，节点返回非 Timeout 错误时不重试

---

## T4: 执行级持久化幂等

**问题**：GAP-20。当前幂等保护仅 IMemoryCache（5 分钟 TTL，重启丢失），Webhook/手动触发无幂等。

**改动**：

### 4.1 新增 `ExecutionDedup` 实体
- 文件：新建 `backend/FlowEngine.Core/Entities/ExecutionDedup.cs`
```csharp
[Table("execution_dedup", Schema = "flow")]
[Comment("执行幂等去重表")]
public class ExecutionDedup
{
    [Key]
    [Column("idempotency_key")]
    [MaxLength(512)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Column("execution_id")]
    public Guid ExecutionId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}
```

### 4.2 注册到 DbContext
- 文件：[FlowEngineDbContext.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Data/FlowEngineDbContext.cs)
- 添加 `DbSet<ExecutionDedup> ExecutionDedups => Set<ExecutionDedup>();`
- 在 `OnModelCreating` 中配置唯一索引：`modelBuilder.Entity<ExecutionDedup>().HasIndex(e => e.IdempotencyKey).IsUnique();`

### 4.3 新增 `IExecutionIdempotencyService` 接口
- 文件：新建 `backend/FlowEngine.Core/Abstractions/IExecutionIdempotencyService.cs`
```csharp
public interface IExecutionIdempotencyService
{
    /// <summary>尝试获取或注册幂等键。若 key 已存在且未过期，返回已有 ExecutionId；否则注册新记录。</summary>
    Task<Guid?> TryGetOrRegisterAsync(string idempotencyKey, Guid executionId, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>清理过期记录。</summary>
    Task CleanupExpiredAsync(CancellationToken ct = default);
}
```

### 4.4 实现 `ExecutionIdempotencyService`
- 文件：新建 `backend/FlowEngine.Application/Executions/ExecutionIdempotencyService.cs`
- 使用数据库级 `INSERT ... ON CONFLICT DO NOTHING` 语义：
  - 先查 key 是否存在（未过期），存在则返回已有 ExecutionId
  - 不存在则 INSERT，若并发冲突则再查一次
- 依赖 `FlowEngineDbContext`

### 4.5 扩展 `TriggerSettings`
- 文件：[Trigger.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Entities/Trigger.cs) 中 `TriggerSettings` 类
- 新增属性：
  - `public string? IdempotencyKeyTemplate { get; set; }` — 表达式模板，如 `webhook-{headers.x-request-id}`
  - `public int? IdempotencyTtlSeconds { get; set; }` — 幂等键 TTL（默认 3600）

### 4.6 修改 `WebhookHandler`
- 文件：[WebhookHandler.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Host/Webhooks/WebhookHandler.cs)
- 注入 `IExecutionIdempotencyService`
- 在 `_engine.StartAsync` 之前：
  1. 若 `trigger.Settings.IdempotencyKeyTemplate` 非空，从请求中提取幂等键（支持 `{headers.x-request-id}`, `{body.fieldName}` 等模板变量）
  2. 调用 `TryGetOrRegisterAsync`，若返回已有 ExecutionId，直接返回 200 + 已有执行信息（不重复触发）

### 4.7 修改 `PollTriggerJob`
- 文件：[PollTriggerJob.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Host/Jobs/PollTriggerJob.cs)
- 注入 `IExecutionIdempotencyService`
- 将现有 `IMemoryCache` 幂等逻辑替换为持久化幂等：
  - 保留 `ComputeIdempotencyKey` 计算逻辑
  - 用 `TryGetOrRegisterAsync` 替代 `cache.TryGetValue/cache.Set`
  - 可保留 IMemoryCache 作为短期热点缓存（性能优化），但持久化层作为权威来源

### 4.8 修改手动触发 API
- 找到手动触发的 Controller/Service，添加可选 `IdempotencyKey` 参数
- 调用 `TryGetOrRegisterAsync` 判重

### 4.9 注册 DI
- 文件：[ServiceCollectionExtensions.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Host/ServiceCollectionExtensions.cs)
- `services.AddScoped<IExecutionIdempotencyService, ExecutionIdempotencyService>();`

### 4.10 添加过期清理
- 在 `ExecutionCleanupHostedService` 中周期调用 `CleanupExpiredAsync`

### 4.11 验证
- 单元测试：
  - `TryGetOrRegisterAsync` 首次注册返回 null
  - 同一 key 二次调用返回已有 ExecutionId
  - TTL 过期后可重新注册
  - Webhandler 幂等键命中时返回已有执行信息

---

## T5: DataQuality 节点

**问题**：编排引擎感知数据问题的唯一窗口。无专用 DQ 节点，无法开箱即用做数据质量校验。

**改动**：

### 5.1 新增 `DataQualityNode`
- 文件：新建 `plugins/FlowEngine.Plugins.Standard/DataQualityNode.cs`
- 参照现有节点模式（如 [DeduplicateNode.cs](file:///d:/Repos/flow_engine/plugins/FlowEngine.Plugins.Standard/DeduplicateNode.cs)）：
```csharp
public sealed class DataQualityNode : INodeType
{
    public string TypeName => "dataQuality";
    public string DisplayName => "Data Quality";
    public string Category => "Core";
    public string Icon => "shield-check";
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>校验规则列表（JSON 数组），每项包含 type + 参数。</summary>
    [Description("Validation rules as JSON array. Each rule: { type, ...params }")]
    [Hint(PresentationHint.Script, "language", ScriptLanguage.JavaScript, "returnType", "array")]
    public string Rules { get; set; } = "[]";

    /// <summary>校验失败时是否仍然传递数据（false = 不传递）。</summary>
    [Description("Whether to pass data through on validation failure.")]
    public bool PassOnFailure { get; set; } = false;

    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new() { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new() { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    public bool DefaultIsEntry => false;

    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct = default)
    {
        // 解析 Rules JSON → 逐条校验 → 汇总报告 → 失败时生成 NodeError
    }
}
```

### 5.2 支持的校验规则类型
| 类型 | 参数 | 说明 |
|------|------|------|
| `rowCount` | `min`, `max` | 行数阈值 |
| `fieldNotNull` | `field` | 字段非空 |
| `fieldPattern` | `field`, `pattern` | 正则匹配 |
| `fieldRange` | `field`, `min`, `max` | 数值范围 |
| `customExpression` | `expression` | 自定义 JS 表达式 |

### 5.3 校验逻辑
1. 从 `context.Inputs["input"]` 获取输入 DataBatch
2. 解析 `Rules` JSON 为规则列表
3. 逐条校验，收集失败项
4. 生成校验报告对象：
```json
{
  "totalRules": 3,
  "passedRules": 2,
  "failedRules": 1,
  "failures": [{ "type": "rowCount", "message": "行数 50 < 最小值 100" }],
  "inputItemCount": 50
}
```
5. 若有失败规则且 `PassOnFailure == false`：
   - 返回 `Success = false`，`Error.Code = "DataQualityCheckFailed"`
   - 输出仍包含校验报告（下游可引用）
6. 若 `PassOnFailure == true`：返回 `Success = true`，但输出附带校验报告

### 5.4 验证
- 单元测试：
  - rowCount 规则：行数 < min 时失败
  - fieldNotNull 规则：字段为 null 时失败
  - 多规则混合：部分通过部分失败
  - PassOnFailure = true 时不阻断

---

## T6: HTTP 连接池

**问题**：[HttpExecutionHelper.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Runtime/Http/HttpExecutionHelper.cs) 使用 `static readonly HttpClient`，无连接池管理、无超时配置。

**改动**：

### 6.1 新增 `IHttpClientPool` 接口
- 文件：新建 `backend/FlowEngine.Runtime/Http/IHttpClientPool.cs`
```csharp
public interface IHttpClientPool
{
    HttpClient GetClient(string? name = null);
}
```

### 6.2 实现 `HttpClientPool`
- 文件：新建 `backend/FlowEngine.Runtime/Http/HttpClientPool.cs`
- 内部使用 `IHttpClientFactory`，在 `ServiceCollectionExtensions` 中注册 `HttpClient` 并配置默认超时、最大连接数

### 6.3 修改 `HttpExecutionHelper`
- 文件：[HttpExecutionHelper.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Runtime/Http/HttpExecutionHelper.cs)
- `SendAndBuildResultAsync` 新增 `HttpClient` 参数（替代 static `SharedClient`）
- 移除 `static readonly HttpClient SharedClient`

### 6.4 修改 `HttpRequestNode` 和 `HttpToolNode`
- 注入 `IHttpClientPool`（通过 NodeExecutionContext 或构造函数）
- 调用 `HttpExecutionHelper.SendAndBuildResultAsync` 时传入 pool 获取的 client

### 6.5 注册 DI
- 文件：[ServiceCollectionExtensions.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Host/ServiceCollectionExtensions.cs)
- `services.AddHttpClient();`
- `services.AddSingleton<IHttpClientPool, HttpClientPool>();`

### 6.6 验证
- 现有 HTTP 节点测试应继续通过
- 验证 HttpClient 不再是 static 单例，而是从池中获取

---

## 实施顺序与依赖

```
T1 (超时生效) ──→ T2 (全局默认) ──→ T3 (增强重试)
T4 (持久化幂等)  [独立，可与 T1-T3 并行]
T5 (DQ 节点)    [独立，可与 T1-T4 并行]
T6 (HTTP 连接池) [依赖 T3 不大，可并行]
```

建议执行顺序：T1 → T2 → T4 → T3 → T5 → T6

---

## 验证步骤

每个任务完成后：
1. `dotnet build` 编译通过
2. `dotnet test` 全部测试通过
3. 不引入新编译警告
4. 完成后发起 SubAgent Code Review
