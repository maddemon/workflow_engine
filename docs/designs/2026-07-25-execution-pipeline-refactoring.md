# 执行引擎管线化重构设计

## 1. 背景与问题

当前节点执行架构中，`INodeType` 承担了"元数据声明 + 业务逻辑 + 部分框架职责"三重身份，而 `NodeProcessor.ProcessAsync()` 是一个 250+ 行的 mega 方法，把参数校验、上下文构造、重试、结果构建、事件发布、输出路由全部揉在一步中。

### 1.1 节点中不应属于节点的逻辑

| 职责 | 当前状态 | 出现在多少节点中 |
|------|----------|------------------|
| Input/Output 端口声明 | 每个节点手动定义几乎相同的端口列表 | 10/12 标准节点 |
| TypeName/DisplayName/Category/Icon 元数据 | 每个节点重复 4 个只读属性 | 全部 |
| 参数非空校验 | 各节点用 if-guard 手动检查 Script 参数 | 5+ 节点 |
| 异常→ErrorResult 转换 | try-catch 写法各异 | 8+ 节点，但 FilterNode 不捕 |
| NodeExecutionResult 手动构造 | `new NodeExecutionResult { Success = true, Output = ... }` | 几乎所有节点 |
| 凭据手动解析 | 节点自己调 `context.ResolveCredentialAsync()` | HTTP 等节点 |
| OncePerItem 循环 | JSNode 自建 `CodeMode` 平行于框架 `ExecutionMode` | JSNode |
| BranchIndex 路由魔法 int | 节点返回 int 表达输出端口 | SwitchNode、IfNode |

### 1.2 执行管线中不应堆在一个方法中的逻辑

`NodeProcessor.ProcessAsync()` 当前混合了：

1. 上下文生命周期管理（NodeContext 清空/复用 + 环路失控保护）
2. 参数预求值（调用 `_contextFactory.CreateAsync`)
3. LLM 客户端解析与注入
4. 节点执行（带重试）
5. JS 引擎释放
6. 执行记录构建与脱敏
7. 事件发布（Started / Executed / Error）
8. 输出累积（累加 accumulatedItems）
9. 内存限流（CapRetainedOutput）
10. 输出路由（RouteOutputsAsync）

### 1.3 NodeExecutionContext 是上帝对象

当前上下文有 20+ 属性，覆盖工作流元数据、输入数据、凭据、日志、基础设施（LLM/HTTP/Registry/Factory/ScriptCache）、流式回调、引擎配置、安全开关。这个对象既是数据载体又是服务定位器。

---

## 2. 设计目标

1. **分离框架职责与节点职责**：节点只关注业务逻辑，框架通过管线处理横切关注点。
2. **声明式替代命令式**：元数据、端口、参数校验由特性/约定自动推导。
3. **管线化（Pipeline）执行模型**：类似 ASP.NET Core 中间件，每个阶段职责单一、可测试、可组合。
4. **渐进式上下文**：节点只看到它需要的上下文信息，不是全部 20+ 属性。
5. **无损迁移**：现有节点可逐类迁移，不搞大爆炸重构。

---

## 3. 总体架构

```
┌──────────────────────────────────────────────────────────────────┐
│                        WorkflowSchedulerKernel                     │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                    NodePipeline                             │  │
│  │  ┌───────────┐ ┌───────────┐ ┌──────────┐ ┌────────────┐  │  │
│  │  │  Validate  │ │  Resolve  │ │ Invoke   │ │  PostProcess│  │  │
│  │  │ Middleware │→│ Middleware │→│ Handler  │→│  Middleware │  │  │
│  │  │ (校验)     │ │ (装配)     │ │ (执行)    │ │ (后处理)    │  │  │
│  │  └───────────┘ └───────────┘ └──────────┘ └────────────┘  │  │
│  └────────────────────────────────────────────────────────────┘  │
│          ▲                                                        │
│          │ 出队 NodeWorkItem                                      │
│    ┌─────┴──────┐                                               │
│    │ExecutionQueue│                                               │
│    └────────────┘                                               │
└──────────────────────────────────────────────────────────────────┘
```

### 3.1 节点生命周期

```
  ┌───────────────────────────────────────────────────────┐
  │                   节点执行生命周期                       │
  │                                                        │
  │  ① InitializeStage                                      │
  │     - 加载/创建 NodeContext（持久化状态）                 │
  │     - 环路失控保护检查                                   │
  │     ↓                                                   │
  │  ② ValidationStage                                      │
  │     - 参数声明式校验（Required/非空/类型）               │
  │     - Script 参数基础校验（非空源码）                    │
  │     - 失败 → ErrorResult，跳过执行                      │
  │     ↓                                                   │
  │  ③ ResolutionStage                                      │
  │     - Script 参数预求值（工厂当前做的那部分）            │
  │     - 凭据解析 + 注入 GlobalVariables                   │
  │     - 全局变量装配（$env/$now/$workflow 等）             │
  │     - LLM 客户端解析（LLM 端口上游查找）                │
  │     - 失败 → ErrorResult，跳过执行                      │
  │     ↓                                                   │
  │  ④ ExecutionStage                                       │
  │     - 调用 NodeHandler.ExecuteAsync                    │
  │     - 重试循环（RetryExecutor）                           │
  │     - 超时控制                                           │
  │     - 失败 → 按 ErrorStrategy 决定下一步                │
  │     ↓                                                   │
  │  ⑤ PostProcessStage                                     │
  │     - 后置条件检查（SuccessWhen 等）                    │
  │     - 输出规范化（DataItem 包装）                       │
  │     - 异常→ErrorResult 统一转换（兜底）                 │
  │     - OncePerItem 累积                                  │
  │     ↓                                                   │
  │  ⑥ RoutingStage                                         │
  │     - 按 PortOutputs / BranchIndex 路由到下游           │
  │     - 等待区聚合                                         │
  │     ↓                                                   │
  │  ⑦ PersistenceStage                                     │
  │     - 构建 NodeExecutionRecord（含脱敏）                │
  │     - 事件发布（Started/Executed/Error）                │
  │     - 输出内存限流（CapRetainedOutput）                 │
  └───────────────────────────────────────────────────────┘
```

### 3.2 管线中间件接口

```csharp
/// <summary>
/// 节点执行管线中间件。
/// </summary>
public interface INodeMiddleware
{
    Task InvokeAsync(
        NodeContext context,
        NodeMiddlewareDelegate next,
        CancellationToken ct);
}

public delegate Task NodeMiddlewareDelegate(NodeContext context, CancellationToken ct);
```

管线由 `NodePipeline` 类组合中间件，替代当前 `NodeProcessor.ProcessAsync`：

```csharp
public sealed class NodePipeline
{
    private readonly List<INodeMiddleware> _middlewares;

    public async Task<NodeResult> RunAsync(
        NodeWorkItem item,
        ExecutionSession session,
        IExecutionSideEffects sideEffects,
        CancellationToken ct)
    {
        var context = new NodeContext(item, session, sideEffects);
        await RunMiddlewareAsync(context, ct);
        return context.Result;
    }
}
```

---

## 4. 关键接口契约

### 4.1 节点处理器（节点唯一要实现的接口）

```csharp
/// <summary>
/// 节点业务处理接口。替代 INodeType 的 ExecuteAsync 重载。
/// 节点只关注：拿输入 → 产生输出。
/// </summary>
public interface INodeHandler
{
    /// <summary>
    /// 执行节点业务逻辑。不负责参数校验、异常转换、路由等横切关注点。
    /// </summary>
    Task<NodeOutput> ExecuteAsync(NodeInput input, CancellationToken ct);
}

/// <summary>
/// 节点输入——精简视图，不含凭据/日志/基础设施。
/// </summary>
public sealed class NodeInput
{
    /// <summary>当前输入批次。</summary>
    public DataBatch InputBatch { get; }

    /// <summary>已解析的参数（Script 已求值）。</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>运行时全局变量（$env/$now/$workflow 等）。</summary>
    public IReadOnlyDictionary<string, object?> Globals { get; }

    /// <summary>当前迭代索引（OncePerItem 时有效）。</summary>
    public int? ItemIndex { get; }
}

/// <summary>
/// 节点输出——节点只返回业务数据，框架包装为 NodeExecutionResult。
/// </summary>
public sealed class NodeOutput
{
    /// <summary>输出数据。</summary>
    public DataBatch Data { get; }

    /// <summary>多端口输出（端口名→数据）。</summary>
    public IReadOnlyDictionary<string, DataBatch>? PortOutputs { get; }

    /// <summary>节点上下文变更（LoopNode 等有状态节点写入）。</summary>
    public IReadOnlyDictionary<string, object?>? ContextChanges { get; }

    // ----- 工厂方法 -----
    public static NodeOutput Data(DataBatch batch);
    public static NodeOutput ToPort(string portName, DataBatch batch);
    public static NodeOutput ToPorts(IReadOnlyDictionary<string, DataBatch> portOutputs);
}
```

### 4.2 节点上下文（管线上下文，替代上帝对象）

```csharp
/// <summary>
/// 节点执行上下文——管线中间件的数据载体。
/// 按职责分段装配，不会一次暴露所有属性。
/// </summary>
public sealed class NodeContext
{
    // ---- 阶段①：InitializeStage 填充 ----
    /// <summary>节点定义。</summary>
    public NodeDefinition NodeDefinition { get; }

    /// <summary>当前节点类型实例。</summary>
    public INodeType NodeType { get; }

    /// <summary>节点级持久化状态（LoopNode 迭代位置等）。</summary>
    public IDictionary<string, object?> NodeState { get; }

    // ---- 阶段②：ValidationStage 填充 ----
    /// <summary>校验错误列表。非空时停止管线。</summary>
    public List<ValidationError> ValidationErrors { get; }

    // ---- 阶段③：ResolutionStage 填充 ----
    /// <summary>已解析参数。</summary>
    public IReadOnlyDictionary<string, object> ResolvedParameters { get; set; }

    /// <summary>凭据访问器。</summary>
    public ICredentialAccessor Credentials { get; set; }

    /// <summary>全局变量。</summary>
    public IReadOnlyDictionary<string, object?> GlobalVariables { get; set; }

    /// <summary>LLM 客户端（可选）。</summary>
    public ILlmClient? LlmClient { get; set; }

    // ---- 阶段④：ExecutionStage 填充 ----
    /// <summary>节点执行的原始结果。</summary>
    public NodeOutput? HandlerOutput { get; set; }

    // ---- 阶段⑤：PostProcessStage 填充 ----
    /// <summary>最终执行结果（框架包装后）。</summary>
    public NodeExecutionResult? Result { get; set; }

    // ---- 跨阶段共享 ----
    /// <summary>当前执行会话。</summary>
    public ExecutionSession Session { get; }

    /// <summary>副作用回调。</summary>
    public IExecutionSideEffects SideEffects { get; }
}
```

### 4.3 节点元数据声明（声明式替代属性重复）

```csharp
/// <summary>
/// 节点元数据特性。框架自动从特性读取 TypeName / DisplayName / Category / Icon，
/// 节点不再需要重复 4 个只读属性。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NodeMetaAttribute : Attribute
{
    public string TypeName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Icon { get; }
    public bool DefaultIsEntry { get; init; }

    public NodeMetaAttribute(string typeName, string displayName,
        string category, string icon);
}

/// <summary>
/// 端口声明特性。框架自动从端口特性合成 Ports 列表，
/// 节点不需要手动定义 IReadOnlyList&lt;PortDefinition&gt;。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PortAttribute : Attribute
{
    public string Name { get; }
    public string DisplayName { get; }
    public PortDirection Direction { get; }
    public PortType Type { get; }

    public PortAttribute(string name, string displayName,
        PortDirection direction, PortType type = PortType.Main);
}
```

### 4.4 NodeBase 基类

```csharp
/// <summary>
/// 节点基类。提供：
/// - 从 NodeMetaAttribute 自动读取元数据
/// - 从 PortAttribute 自动合成 Ports
/// - 将 INodeHandler.ExecuteAsync 适配到 INodeType.ExecuteAsync
/// 子类只需实现 ExecuteAsync(NodeInput, CancellationToken)。
/// </summary>
public abstract class NodeBase : INodeType, INodeHandler
{
    // INodeType 实现 —— 由特性推导，无需子类编写
    public string TypeName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Icon { get; }
    public bool DefaultIsEntry { get; }
    public IReadOnlyList<PortDefinition> Ports { get; }
    public ExecutionMode ExecutionMode { get; protected set; }
    public AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor);

    // INodeHandler 实现 —— 子类重写此方法
    public abstract Task<NodeOutput> ExecuteAsync(NodeInput input, CancellationToken ct);

    // INodeType 显式实现 —— 适配到管线
    async Task<NodeExecutionResult> INodeType.ExecuteAsync(
        NodeExecutionContext context, CancellationToken ct);

    /// <summary>子类可重写以提供额外端口。</summary>
    protected virtual IReadOnlyList<PortDefinition> ExtraPorts => [];
}
```

迁移后的节点写法：

```csharp
[NodeMeta("if", "If", "Core", "shuffle")]
[Port("input", "Input", PortDirection.Input)]
[Port("true", "True", PortDirection.Output)]
[Port("false", "False", PortDirection.Output)]
public sealed class IfNode : NodeBase
{
    [Required]
    [Hint(PresentationHint.Expression)]
    public Script Condition { get; set; } = Script.Empty;

    public override async Task<NodeOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var condition = await input.Parameters["Condition"]
            .EvaluateAsync<bool>(input.Globals, ct);

        return condition
            ? NodeOutput.ToPort("true", input.InputBatch)
            : NodeOutput.ToPort("false", input.InputBatch);
    }
}
```

与当前写法对比——减少了 **~50 行样板代码**（元数据属性、端口声明、try-catch、空校验、结果构造）。

---

## 5. 中间件设计

### 5.1 管线装配

```csharp
public static class NodePipelineBuilder
{
    public static NodePipeline BuildDefault()
    {
        var pipeline = new NodePipeline();
        pipeline.Add<InitializeMiddleware>();    // ① 初始化
        pipeline.Add<ValidationMiddleware>();    // ② 参数校验
        pipeline.Add<ResolutionMiddleware>();    // ③ 预求值
        pipeline.Add<ExecutionMiddleware>();     // ④ 执行业务
        pipeline.Add<PostProcessMiddleware>();   // ⑤ 后处理
        pipeline.Add<RoutingMiddleware>();       // ⑥ 输出路由
        pipeline.Add<PersistenceMiddleware>();   // ⑦ 持久化
        return pipeline;
    }
}
```

### 5.2 各中间件职责

| 中间件 | 职责 | 当前散落在哪 |
|--------|------|-------------|
| `InitializeMiddleware` | 管理 NodeContext 生命周期：非回边激活时清空旧状态，回边激活时保留；环路失控保护检测 | `NodeProcessor.ProcessAsync` L78-119 |
| `ValidationMiddleware` | 扫描节点参数的 `[Required]` 特性、Script 非空源码、类型约束；构造 `ValidationErrors` 列表 | 各节点自己的 if-guard |
| `ResolutionMiddleware` | 调用 Script.EvaluateAsync 预求值；解析凭据并注入 GlobalVariables；装配 `$env/$now/$workflow` 等；查找 LLM 端口上游客户端 | `NodeExecutionContextFactory.CreateAsync` + `NodeProcessor.ResolveLlmClientForNode` |
| `ExecutionMiddleware` | 协调 OncePerItem 循环 → 对每项调用 `INodeHandler.ExecuteAsync`；包裹 `RetryExecutor` 带超时和退避；JS 引擎生命周期管理（创建 + 释放） | `NodeProcessor.ProcessAsync` L130-205 + `RetryExecutor` |
| `PostProcessMiddleware` | `SuccessWhen` 后置条件检查（通用化，不限于 HTTP 节点）；异常→ErrorResult 兜底；OncePerItem 运行输出累积；输出内存限流 | HTTP 节点的 SuccessWhen + `NodeProcessor.CapRetainedOutput` + 各节点的 catch 块 |
| `RoutingMiddleware` | 按 `PortOutputs` / `BranchIndex` 路由到下游节点；等待区聚合；清空上下文 | `OutputRouter.RouteOutputsAsync` |
| `PersistenceMiddleware` | 构建并脱敏 `NodeExecutionRecord`；批量持久化；事件发布（Started/Executed/Error/Completed） | `NodeProcessor` L207-251 + `ExecutorSideEffects` |

### 5.3 短路机制

ValidationMiddleware 或 ResolutionMiddleware 失败时，跳过后续中间件直达 PersistenceMiddleware（以记录失败状态）：

```csharp
public sealed class ValidationMiddleware : INodeMiddleware
{
    public async Task InvokeAsync(NodeContext context, NodeMiddlewareDelegate next, CancellationToken ct)
    {
        var errors = Validate(context.NodeType, context.NodeDefinition);
        if (errors.Count > 0)
        {
            context.ValidationErrors = errors;
            context.Result = BuildValidationErrorResult(errors);
            // 不调用 next —— 短路管线
            return;
        }

        await next(context, ct);
    }
}
```

---

## 6. 迁移策略

### 6.1 分阶段迁移

| 阶段 | 内容 | 影响 |
|------|------|------|
| **阶段一：管线框架搭建** | 实现 `NodePipeline`、`INodeMiddleware`、`NodeContext`、`NodeBase`；不改现有节点 | 新抽象存在但不影响运行 |
| **阶段二：NodeProcessor 拆解** | 将 `ProcessAsync` 按职责提取为中间件；保留现有 `INodeType` 接口作为 ExecutionMiddleware 的适配目标 | 行为不变，内部重构 |
| **阶段三：样板集中消除** | 添加 `NodeMetaAttribute`、`PortAttribute`、`ValidationMiddleware`；提供 `NodeBase` 适配器 | 现有节点不变，新建节点用新写法 |
| **阶段四：逐节点迁移** | 从简单节点（WaitNode、MergeNode）开始迁移到 `NodeBase`，逐步推向复杂节点 | 旧 `INodeType` 接口保留期间可共存 |
| **阶段五：上帝对象分解** | 将 `NodeExecutionContext` 的过剩属性按职责分散到中间件上下文或专用服务 | 只影响内部管线，对外接口不变 |

### 6.2 向后兼容

- `INodeType` 接口保留，通过 `NodeBase` 适配：基类实现 `INodeType.ExecuteAsync`，内部走管线
- 已注册的节点不用一次性全改——新旧接口在注册层可共存
- `NodeExecutionContext` 暂时保留，由 ResolutionMiddleware 填充其子集，逐步废弃过剩属性

### 6.3 迁移优先级

```
简单节点（无状态、无循环） → 路由节点（If/Switch/Filter） → 有状态节点（Loop） → 复杂节点（JS/Agent/HTTP）
                                                                                              
WaitNode  MergeNode    IfNode  SwitchNode  FilterNode    LoopNode     JSNode  AgentNode  HttpRequestNode
  (最低风险)             (中等风险)                     (较高风险)         (最高风险)
```

---

## 7. 风险与待定项

### 7.1 风险

| 风险 | 影响 | 应对 |
|------|------|------|
| 管线中间件调度性能开销 | 每次节点执行多 N 次委托调用 | 中间件数量固定（7 个），委托调用开销 < 0.1μs，可忽略 |
| JSNode 的 CodeMode 与 ExecutionMode 平行 | OncePerItem 两套逻辑 | 将 `RunOnceForEachItem` 语义并入框架 `ExecutionMode`，移除 JSNode 自建循环 |
| BranchIndex 与 PortOutputs 并存 | 路由逻辑两套 | `PostProcessMiddleware` 统一：有 PortOutputs 则用它，否则用 BranchIndex 映射到端口 |
| 现有测试需要适配 | 回归测试成本 | 管线中间件可单独测试（每个中间件是独立的 `InvokeAsync`），集成测试保持现有用例不变 |

### 7.2 待定项

| 事项 | 决策点 |
|------|--------|
| `NodeBase` 是否应强制所有节点继承，或保留纯 `INodeType` 接口路径 | 建议保留两条路径，`NodeBase` 是最佳实践而非强制 |
| `NodeContext` 的属性是否应分层（接口隔离），或用 `IRequiredNodeContext` / `IFullNodeContext` | 倾向接口隔离：`IValidationContext`、`IExecutionContext` 等 |
| JS 引擎生命周期是否应作为独立中间件 | 是，`EngineLifecycleMiddleware`：创建 → 注入 → 释放 |
| 脚本预求值（ResolutionStage）当前由 `NodeExecutionContextFactory` 做的部分，是否整并到 ResolutionMiddleware | 是，消除工厂与管线之间的职责交叉 |

---

## 8. 验收标准

- [ ] 管线中间件可独立单元测试，无节点依赖
- [ ] `NodeBase` + 特性声明可完整替代 `INodeType` 的手动元数据/端口
- [ ] 使用新写法的节点比当前写法减少 ≥40% 代码行数
- [ ] `NodeProcessor.ProcessAsync` 拆解为 ≤7 个中间件，每个 ≤80 行
- [ ] 一个现有节点（如 WaitNode）迁移后全部测试通过
- [ ] NodeExecutionContext 的属性减少 ≥40%
- [ ] 无运行时性能退化（管线 vs 当前路径，microbenchmark ≤5%）
