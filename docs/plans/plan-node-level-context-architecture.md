# Node-Level Persistent Context 改造方案

> **目标：** 为 Flow Engine 运行时添加节点级持久化上下文机制，支持 LoopNode 等有状态节点跨迭代保持状态，并为未来有状态节点提供通用基础设施。

---

## 1. 问题分析

### 1.1 当前架构的限制

| 组件 | 当前行为 | 问题 |
|------|---------|------|
| `NodeExecutionContext` | 每次执行新建，不持久化 | LoopNode 无法记住迭代位置 |
| `ExecutionSession.Memory` | 全局共享字典 | 不适合节点私有状态，命名冲突风险 |
| `ExecutionSession.SuccessfulOutputs` | 累积所有输出 | 设计用于 `$node` 引用，非状态管理 |
| `LoopNode` | 单窗口语义，不迭代 | 无法实现真正的循环 |
| `NodeExecutionContext` → JS 引擎 | 工厂将全局变量（`$node`、`$input` 等）注入 JS 引擎供表达式求值，但节点自身上下文不在其中 | 读写 `NodeContext` 只能通过 C# 代码，无法在表达式中使用 `$nodeContext.xxx`，限制了灵活性和节点可组合性 |
| `LoopNode.ResetIndex` | 已声明属性但无实现 | 循环完成后收到新输入时，旧上下文残留，无法重置重新开始 |

### 1.2 对比 n8n 的设计

n8n 通过 `this.getContext('node')` 获取节点级持久化上下文：
- 存储在 `runExecutionData.executionData.contextData[node:name]`
- 跨多次调用保持状态（循环回环时）
- 节点内部自行管理状态结构

### 1.3 需要支持的场景

| 场景 | 需要的状态 | 示例节点 |
|------|-----------|---------|
| 循环迭代 | 剩余项目、当前位置 | LoopNode |
| 分页拉取 | 游标、页码、累积结果 | PaginateNode |
| 累积计算 | 中间结果、计数器 | 自定义聚合节点 |
| 重试恢复 | 已尝试项、失败项 | 批量处理节点 |

---

## 2. 架构设计

### 2.1 核心数据结构

```
ExecutionSession
├── NodeContexts: ConcurrentDictionary<string, IDictionary<string, object?>>
│   ├── "loop1" → { "items": [...], "position": 0, "processed": [...] }
│   ├── "paginate1" → { "cursor": "xxx", "page": 1, "hasMore": true }
│   └── ...
└── ... (existing fields)
```

### 2.2 接口设计

```csharp
// NodeExecutionContext 新增属性
public IDictionary<string, object?> NodeContext { get; set; }

// 扩展方法（提供类型安全访问，约束 T 为 class 以避免值类型拆箱语义混乱）
public static class NodeContextExtensions
{
    public static T? Get<T>(this IDictionary<string, object?> context, string key) where T : class;
    public static void Set<T>(this IDictionary<string, object?> context, string key, T value);
    public static bool TryGet<T>(this IDictionary<string, object?> context, string key, out T? value) where T : class;
    // 非泛型访问器：覆盖 int/double/bool 等值类型（见 Task 5，强类型 Get<T> 受 where T : class 约束无法覆盖）
    public static object? GetValue(this IDictionary<string, object?> context, string key);
    public static void SetValue(this IDictionary<string, object?> context, string key, object? value);
}

// 表达式变量名：运行时将 $nodeContext 注入节点执行期 JS 引擎全局变量表（context.GlobalVariables），
// 节点可在表达式中读写：$nodeContext.position、$nodeContext.cursor 等。
// 注入点必须是 NodeExecutionContext.GlobalVariables（由 ExecutionScope.ApplyGlobalVariables 在节点
// 执行期注入运行时引擎），而非工厂内用于参数预求值的临时 js 引擎 / globals 字典——后者不会流入
// 节点 body 表达式（见 Task 8 的实现说明）。
// 注入策略：只要节点有有效 NodeContext（从 session.NodeContexts 获取到的同一实例）即注入，不要求非空，
// 否则首次迭代（字典为空）时表达式无法播种上下文。
//
// 变量作用域：$nodeContext 对应执行节点自身上下文，而非其他节点。如需读取其他节点的上下文，
// 应通过 $node.<name> 读取输出，或由架构设计阶段显式开放跨节点引用。
```

### 2.3 数据流

```
1. ExecutionSession 创建 → 初始化 NodeContexts 字典
2. WorkflowSchedulerKernel.ProcessNodeAsync() →
   a. 从 session.NodeContexts 获取或创建节点上下文
   b. 注入到 NodeExecutionContext.NodeContext
3. NodeExecutionContextFactory.CreateAsync() →
   a. 接收 nodeContext 参数
   b. 设置到返回的 NodeExecutionContext
   c. 将 NodeContext 同一实例注入 `context.GlobalVariables` 作为 $nodeContext
      （见 Task 8：必须走 GlobalVariables 而非工厂临时引擎，节点 body 表达式才可见），
      使表达式 $nodeContext.<key> 可求值
4. 节点执行 → 读写 NodeContext（可通过 C# context.NodeContext 或表达式 $nodeContext）
5. 下次调用同一节点 → 自动获取上次的上下文
```

---

## 3. 实现任务

### Task 1: ExecutionSession 添加 NodeContexts

**文件:**
- Modify: `backend/FlowEngine.Runtime/Executor/ExecutionSession.cs`

**变更:**
```csharp
// 新增属性
public ConcurrentDictionary<string, IDictionary<string, object?>> NodeContexts { get; } 
    = new(StringComparer.OrdinalIgnoreCase);
```

**测试:**
- 验证 NodeContexts 初始化为空字典
- 验证线程安全（ConcurrentDictionary）

---

### Task 2: NodeExecutionContext 添加 NodeContext 属性

**文件:**
- Modify: `backend/FlowEngine.Core/Entities/NodeExecutionContext.cs`

**变更:**
```csharp
/// <summary>
/// 节点级持久化上下文，跨多次调用保持状态。
/// 由运行时注入，节点可读写任意键值对。
/// </summary>
public IDictionary<string, object?> NodeContext { get; set; } 
    = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
```

**测试:**
- 验证默认值为空字典
- 验证可读写

---

### Task 3: NodeExecutionContextFactory 注入 NodeContext

**文件:**
- Modify: `backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs`
- Modify: `backend/FlowEngine.Core/Abstractions/INodeExecutionContextFactory.cs`

**变更:**
```csharp
// INodeExecutionContextFactory 接口新增参数（放在 extraGlobals 之后，仅此一处；Task 8 复用该参数）
Task<NodeExecutionContext> CreateAsync(
    ...,
    ICredentialAccessor? credentialAccessorOverride = null,
    IReadOnlyDictionary<string, object?>? extraGlobals = null,
    IDictionary<string, object?>? nodeContext = null);   // 新增：来自 session.NodeContexts 的同一实例

// NodeExecutionContextFactory 实现（签名同步新增该参数）
public async Task<NodeExecutionContext> CreateAsync(
    ...,
    IReadOnlyDictionary<string, object?>? extraGlobals = null,
    IDictionary<string, object?>? nodeContext = null)
{
    // ... existing code ...

    // $nodeContext 注入到 GlobalVariables 的逻辑见 Task 8（在 BuildBase 之后）。
    return new NodeExecutionContext
    {
        // ... existing properties ...
        NodeContext = nodeContext ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
    };
}
```

**测试:**
- 验证传入的 nodeContext 被正确设置
- 验证 null 时创建空字典

---

### Task 4: WorkflowSchedulerKernel 传递 NodeContext

**文件:**
- Modify: `backend/FlowEngine.Runtime/Executor/WorkflowSchedulerKernel.cs`

**变更:**
```csharp
private async Task<bool> ProcessNodeAsync(...)
{
    // ... existing code ...
    
    for (var runIndex = 0; runIndex < runCount; runIndex++)
    {
        // 获取或创建节点上下文
        var nodeContext = session.NodeContexts.GetOrAdd(
            node.Id,
            _ => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
        
        context = await contextFactory.CreateAsync(
            session.Workflow,
            session.Execution,
            node,
            nodeType,
            runInputs,
            session.SuccessfulOutputs,
            session.LatestBatches,
            runIndex,
            cancellationToken,
            session.CredentialAccessor,
            nodeContext: nodeContext);  // 新增参数
        
        // ... rest of code ...
    }
}
```

**测试:**
- 验证首次调用时创建新上下文
- 验证后续调用时复用同一上下文
- 验证不同节点有独立上下文

---

### Task 5: NodeContextExtensions 工具类

**文件:**
- Create: `backend/FlowEngine.Core/Entities/NodeContextExtensions.cs`

**内容:**
```csharp
namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点上下文扩展方法，提供类型安全的读写操作。
/// </summary>
public static class NodeContextExtensions
{
    /// <summary>获取类型安全的值。T 约束为 class 避免值类型拆箱语义混乱；节点如需 int 存储应自行包装或直接索引。</summary>
    public static T? Get<T>(this IDictionary<string, object?> context, string key) where T : class
    {
        if (context.TryGetValue(key, out var value) && value is T typed)
        {
            return typed;
        }
        return default;
    }
    
    public static void Set<T>(this IDictionary<string, object?> context, string key, T value)
    {
        context[key] = value;
    }
    
    public static bool TryGet<T>(this IDictionary<string, object?> context, string key, out T? value) where T : class
    {
        if (context.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }
    
    public static T GetOrAdd<T>(this IDictionary<string, object?> context, string key, Func<T> factory) where T : class
    {
        if (context.TryGetValue(key, out var value) && value is T typed)
        {
            return typed;
        }
        var newValue = factory();
        context[key] = newValue;
        return newValue;
    }

    /// <summary>非泛型读取：覆盖 int/double/bool 等值类型（强类型 Get&lt;T&gt; 受 where T : class 约束无法覆盖）。缺失或类型不符时返回 null。</summary>
    public static object? GetValue(this IDictionary<string, object?> context, string key)
        => context.TryGetValue(key, out var value) ? value : null;

    /// <summary>非泛型写入：与 GetValue 配对，供值类型状态（计数器、游标、位置、页码）使用。</summary>
    public static void SetValue(this IDictionary<string, object?> context, string key, object? value)
        => context[key] = value;
}
```

> **值类型局限：** `Get<T>`/`TryGet<T>`/`GetOrAdd<T>` 受 `where T : class` 约束，无法覆盖循环位置、计数器、游标、分页页码等最常见的 `int`/`double`/`bool` 状态。此类状态请用新增的非泛型 `GetValue`/`SetValue`，或节点内显式拆箱（`nodeContext["position"] is int pos`）。注意 JS 经 `$nodeContext.xxx = 5` 写入的数值会被 Jint 以 `double` 形式回写，读取侧须用 `is double` 而非 `is int`（见 Task 8 类型备注）。

**测试:**
- 验证 Get/Set/TryGet/GetOrAdd 各场景（引用类型）
- 验证 `GetValue`/`SetValue` 对值类型（int/double/bool）的读写
- 验证 T 为 class 约束：调用 `Get<int>` 应编译错误（以独立 `#if` 片段或文档化约束体现，xUnit 无法直接断言编译错误）
- 验证未找到 key 时 `GetValue` 返回 null
- 验证类型不匹配时返回 `default`（null）

---

### Task 6: LoopNode 改造为真正循环

**文件:**
- Modify: `plugins/FlowEngine.Plugins.Standard/LoopNode.cs`
- Modify: `tests/FlowEngine.Runtime.Tests/Plugins/LoopNodeTests.cs`

**变更:**
```csharp
public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct)
{
    BatchSize = Math.Max(1, BatchSize);
    var inputBatch = context.GetInputBatch();
    var allItems = inputBatch.Items;
    
    // 获取或初始化节点上下文
    var nodeContext = context.NodeContext;
    
    // 首次调用或 ResetIndex 为 true 时：重新初始化状态
    if (!nodeContext.ContainsKey("initialized") || ResetIndex)
    {
        nodeContext["initialized"] = true;
        nodeContext["allItems"] = allItems;  // 存储原始输入
        nodeContext["position"] = 0;
        nodeContext["processedItems"] = new List<DataItem>();
    }
    
    var position = nodeContext["position"] is int pos ? pos : 0;
    var storedItems = nodeContext.Get<List<DataItem>>("allItems") ?? allItems;
    
    // 没有更多项目：走 Done 输出
    if (position >= storedItems.Count)
    {
        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = nodeContext.Get<List<DataItem>>("processedItems") ?? [] },
            BranchIndex = 1  // done
        });
    }
    
    // 取当前批次
    var batchItems = storedItems.Skip(position).Take(BatchSize).ToList();
    nodeContext["position"] = position + batchItems.Count;
    
    // 累积已处理项目
    var processed = nodeContext.Get<List<DataItem>>("processedItems") ?? [];
    processed.AddRange(batchItems);
    nodeContext["processedItems"] = processed;
    
    // 走 Loop 输出
    return Task.FromResult(new NodeExecutionResult
    {
        Success = true,
        Output = new DataBatch { Items = batchItems },
        BranchIndex = 0  // loop
    });
}
```

> **注意：** `Get<int>` 有 `where T : class` 约束，不能直接用于值类型。此处 `position` 用直接类型检查 `nodeContext["position"] is int pos` 替代。节点上下文存储 `object?`，拆箱检查是安全且高效的方案。`NodeContextExtensions` 扩展方法用于 `List<DataItem>`、`string` 等引用类型。

**测试:**
- 验证首次调用返回第一批
- 验证后续调用返回下一批
- 验证全部处理完后走 Done
- 验证 `ResetIndex = true` 时清除旧上下文重新开始
- 验证 `ResetIndex = false`（默认）时复用上下文继续迭代
- 验证新上游输入（非回边路径）到达时，内核清空旧上下文（Task 9），LoopNode 重新初始化而不受旧数据污染
- 验证 `NodeContext` 在 JS 表达式中可通过 `$nodeContext.position` 读写
- 验证 `Get<int>` 编译拒绝（有 `where T : class` 约束）

---

### Task 7: 集成测试

**文件:**
- Create: `tests/FlowEngine.Runtime.Tests/Integration/LoopIntegrationTests.cs`

**测试场景:**
```csharp
[Fact]
public async Task LoopNode_ProcessesAllItemsInBatches()
{
    // Arrange: 创建包含 Loop 节点的工作流
    // Loop (batchSize=2) -> Process -> 回连到 Loop
    // 输入: 5 个项目
    
    // Act: 执行工作流
    
    // Assert:
    // - Loop 节点执行 3 次 (2+2+1)
    // - Process 节点执行 3 次
    // - Done 输出包含所有 5 个处理后的项目
}
```

---

### Task 8: NodeContext 注入 JS 引擎（$nodeContext 表达式支持）

使节点在表达式/脚本中通过 `$nodeContext.xxx` 读写持久化上下文。这是节点可组合性的关键：节点无需写 C# 即可访问上下文。

**文件:**
- Modify: `backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs`

**变更（关键修正）：**

注入点必须是 `context.GlobalVariables`（`ExecutionScope.ApplyGlobalVariables` 在节点执行期把该字典注入运行时引擎），**不是**工厂内用于参数预求值的临时 `js` 引擎与 `globals` 字典——后者不会流入节点 body 表达式（`ScriptEvaluationExtensions`/`PreparedScript.RunAsync` 走 `context.GlobalVariables` 路径）。

`CreateAsync` 签名在 Task 3 已新增 `nodeContext` 参数（仅一处，位于 `extraGlobals` 之后）。在 `BuildBase(...)` 返回之后、构造 `NodeExecutionContext` 之前注入：

```csharp
var globalVariables = ExecutionContextGlobalsBuilder.BuildBase(
    credentialsDict, workflow, execution.Id, rawParameters, environmentWhitelist);

// 节点上下文注入到运行时全局变量 $nodeContext，供节点 body 表达式读写持久化状态。
// 只要 nodeContext 非 null 即注入（不要求非空）：首次迭代字典为空也需可播种，
// 且注入的是 session.NodeContexts 中的同一实例，JS 写回即反映到 C# 侧。
if (nodeContext is not null)
{
    globalVariables["$nodeContext"] = nodeContext;
    js.SetValue("$nodeContext", nodeContext);   // 参数预求值路径也可见
}

return new NodeExecutionContext
{
    // ... existing properties ...
    GlobalVariables = globalVariables,
    NodeContext = nodeContext ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
};
```

> **类型备注：** Jint 中 JS number 为 `double`。`$nodeContext.position = 5` 回写进 `.NET` 字典的是 `double`，C# 侧用 `nodeContext["position"] is int pos` 读取会失败。统一约定：经 `$nodeContext` 写入的数值按 `double` 处理，读取用 `is double`；或改用非泛型 `GetValue`/`SetValue`（见 Task 5）。
>
> **为何不用 `extraGlobals`：** `extraGlobals` 同样只并入工厂临时 `globals`，不进入 `context.GlobalVariables`，故节点运行期不可见，不能复用该机制。

**测试:**
- 验证 `$nodeContext.position` 在**节点 body 表达式**中可读取（区别于仅参数预求值可见）
- 验证 `$nodeContext.counter = 5` 在 JS 中写入后 C# 侧 `context.NodeContext["counter"]` 为 `double` 类型的 5
- 验证 `nodeContext` 为 null 时不注入 `$nodeContext` 变量（节点 body 表达式应报未定义）
- 验证空 `nodeContext` 仍注入（首次迭代可播种上下文）
- 验证 `$nodeContext` 不影响其他节点的全局变量

---

### Task 9: 上下文重置机制（内核级，按回边判定）

控制两种场景的上下文生命周期：

| 场景 | 触发条件 | 行为 |
|------|---------|------|
| 节点收到新上游输入（非环路路径） | 本次激活来自非回边（`IsFeedbackActivation == false`） | 运行时在 `ProcessNodeAsync` 中先移除旧上下文再 `GetOrAdd`，使节点以全新状态开始 |
| 节点被环路回边重新激活 | `IsFeedbackActivation == true` | 复用已有上下文继续迭代（LoopNode 的正常循环依赖此路径） |
| 节点显式重置 | `ResetIndex = true` | 节点在 `ExecuteAsync` 中自行清空上下文（见 Task 6） |

> **判定信号必须用回边，不能用 `SourceNodeId == node.Id`：** 真实回环拓扑里 Loop 端口通常接下游节点（Process → … → 回灌 Loop 输入），LoopNode 被重新入队时其**来源是下游节点而非自身**，因此 `item.SourceNodeId == node.Id` 恒为 false，会把每次回环误判为"新输入"→ 清空上下文 → 循环从 position=0 反复重启、甚至在错误数据上迭代。正确信号是"该次激活是否来自环路回边"。
>
> **与 Task 4 的关系：** Task 4 与 Task 9 都修改 `ProcessNodeAsync`，实现时合并到同一次改动，避免冲突。

**回边计算（会话初始化时一次）：**
```csharp
// 对连接图做 DFS，标记回边（指向 DFS 栈中祖先的边）。
// 结果存入 session.FeedbackEdgeKeys：连接（源节点, 源端口, 目标节点, 目标端口）的键集合。
session.FeedbackEdgeKeys = CycleDetector.ComputeBackEdges(session.Workflow.Connections);
```

**`RouteOutputsAsync` 入队时标记：**
```csharp
var isFeedback = session.FeedbackEdgeKeys.Contains(
    (connection.SourceNodeId, connection.SourcePortName, connection.TargetNodeId, connection.TargetPortName));
await session.Queue.EnqueueAsync(
    new NodeWorkItem(session.Execution.Id, targetNode.Id, inputs, isFeedbackActivation: isFeedback),
    cancellationToken);
```

**`ProcessNodeAsync` 据此重置：**
```csharp
// 非回边激活（新上游输入）：清旧上下文，GetOrAdd 将创建新状态；
// 回边激活（环路继续）：保留上下文，复用既有迭代状态。
if (!item.IsFeedbackActivation)
{
    session.NodeContexts.TryRemove(node.Id, out _);
}

var nodeContext = session.NodeContexts.GetOrAdd(
    node.Id,
    _ => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

// ... existing code ...

context = await contextFactory.CreateAsync(
    // ... existing parameters ...
    nodeContext: nodeContext);
```

**`NodeWorkItem` 调整（替代原 `SourceNodeId` 方案）：**
```csharp
public record NodeWorkItem(
    Guid ExecutionId,
    string NodeInstanceId,
    IReadOnlyDictionary<string, DataBatch> Inputs,
    bool IsFeedbackActivation = false);  // 该次激活是否来自环路回边
```

**测试:**
- 验证首次（非回边）激活创建新上下文（旧数据被清除）
- 验证 Loop 回边激活复用已有上下文、position 持续递增
- 验证工作流中两个不同路径进入同一节点时：非回边路径进入重置上下文、回边路径继续
- 验证 `ResetIndex = true` 时节点自行清空上下文（与回边判定正交）

---

## 4. 其他受益节点

### 4.1 PaginateNode（未来）

```csharp
// 分页拉取场景
nodeContext["cursor"] = "next_cursor_value";
nodeContext["page"] = 1;
nodeContext["hasMore"] = true;
nodeContext["accumulatedItems"] = new List<DataItem>();
```

### 4.2 批量处理节点（未来）

```csharp
// 批量 API 调用
nodeContext["processedIds"] = new HashSet<string>();
nodeContext["failedIds"] = new List<string>();
nodeContext["retryCount"] = 0;
```

### 4.3 聚合节点（未来）

```csharp
// 跨批次聚合
nodeContext["sum"] = 0.0;
nodeContext["count"] = 0;
nodeContext["min"] = double.MaxValue;
nodeContext["max"] = double.MinValue;
```

---

## 5. 迁移与兼容性

### 5.1 向后兼容

- `NodeContext` 默认为空字典，不影响现有节点
- `nodeContext` 参数为可选（默认 null），现有 `CreateAsync` 调用无需修改
- `$nodeContext` 仅在运行时为该节点提供了有效 NodeContext（`nodeContext != null`）时注入 JS 引擎全局变量，且注入的是 session 中的同一实例；无上下文（如内核外构造的上下文）不注入，不干扰现有表达式求值
- 无数据库迁移需求（纯内存状态）

### 5.2 性能考虑

- `ConcurrentDictionary` 保证线程安全
- 节点上下文仅在执行期间存在，不持久化到数据库
- 序列化开销：仅当节点需要持久化时才序列化（当前不需要）
- `SuccessfulOutputs` 在回环拓扑中的行为：`ProcessNodeAsync` 对节点每次成功运行**无条件**将输出累积追加到 `successfulOutputs[node.Name]`（供下游 `$node.<name>` / `$items(<name>)` 读取，BranchIndex 仅标识输出端口、不用于丢弃输出——曾误加的 `BranchIndex != 0` 守卫会静默丢弃 IfNode 的 true 分支 / SwitchNode 的 case 0，已移除）。每个批次仅追加一次，存储复杂度为 **O(N)**，符合预期。以 LoopNode(batchSize=B, N 项) 为例：回环中每批窗口各追加一次（共 N 项），Done 端口再追加累积结果（N 项），下游 `$node.loop1` 取到全部批次与最终结果。

### 5.3 回边检测与 `NodeWorkItem.IsFeedbackActivation`

为支持 Task 9 的精确上下文重置判定，不再使用不可靠的 `SourceNodeId == node.Id`（见 Task 9 说明），改为：

- 会话初始化时由 `CycleDetector.ComputeBackEdges(workflow.Connections)` 计算回边集合，存入 `session.FeedbackEdgeKeys`；
- `NodeWorkItem` 增加 `bool IsFeedbackActivation`（默认 false），由 `RouteOutputsAsync` 按回边集合标记后入队。

```csharp
public record NodeWorkItem(
    Guid ExecutionId,
    string NodeInstanceId,
    IReadOnlyDictionary<string, DataBatch> Inputs,
    bool IsFeedbackActivation = false);  // 该次激活是否来自环路回边
```

`RouteOutputsAsync` 入队时设置（见 Task 9）。`CycleDetector` 为新增小型静态工具（`backend/FlowEngine.Runtime/Executor/CycleDetector.cs`），对连接图做 DFS 标记回边，复杂度 O(V+E)，每个执行仅算一次。

### 5.4 测试策略

- 单元测试：各组件独立测试（含 `NodeContextExtensions` 值类型 `GetValue`/`SetValue`）
- 集成测试：完整工作流执行（含回环拓扑）测试
- 并发测试：验证多节点并行执行时的线程安全
- 表达式测试：验证 `$nodeContext.xxx` 在**节点 body 表达式**中可读写（区别于仅参数预求值可见）
- 类型回写测试：验证 JS `$nodeContext.n = 5` 回写为 `double`，C# 侧按 `double` 读取正确
- Reset 测试：验证 `ResetIndex=true` 时上下文重置；验证回边激活复用、非回边激活重置（Task 9）
- 隔离测试：验证 `SubWorkflowExecutor` 等其它构造 `NodeExecutionContext` 的路径使用独立默认空上下文，不串用父执行上下文

---

## 6. 验收标准

1. LoopNode 能正确迭代处理所有输入项目
2. 节点上下文在同一工作流执行内跨调用保持
3. 不同节点有独立的上下文，互不干扰
4. 在 JS 表达式中可通过 `$nodeContext.xxx` 读写节点自身上下文
5. `ResetIndex = true` 时 LoopNode 重置上下文重新开始
6. 非回边（新上游输入）路径进入节点时重置上下文，回边（环路继续）路径复用旧上下文（Task 9）
7. 现有测试全部通过
8. 新增测试覆盖所有场景
9. 无性能回退

---

## 7. 风险与缓解

| 风险 | 缓解措施 |
|------|---------|
| 内存泄漏 | 上下文随 ExecutionSession 生命周期，执行结束后释放 |
| 序列化复杂度 | 初始版本仅支持简单类型，复杂类型由节点自行处理 |
| 并发冲突 | 使用 ConcurrentDictionary + 节点级隔离；**注意**：每节点的 `IDictionary<string, object?>` 非线程安全，当前内核串行执行保证无竞态；未来引入并行节点执行时需升级到 `ConcurrentDictionary` 或专用锁 |
| 状态不一致 | 节点负责状态管理，运行时仅提供存储 |
| **大数据集内存膨胀** | LoopNode 将 `List<DataItem>` 存储在上下文（`allItems` / `processedItems`），10 万条输入时显著增加驻留内存。缓解：初始版本不做流式优化，文档标注此约束；后续扩展可引入上下文大小上限或懒加载游标方案 |
| **上下文跨路径污染（边界场景）** | 同一节点有回边（环路）与非回边（新上游）两路输入时，非回边入队若复用旧上下文会污染状态。典型 Loop 拓扑（单路径回环）不受影响。缓解：Task 9 按回边集合（`IsFeedbackActivation`）判定，非回边激活先清空上下文 |
| **Production 调试困难** | 节点上下文不记录到 `NodeExecutionRecord`，排查循环问题缺少中间态。缓解：后续扩展可将上下文快照写入执行记录（可选） |

---

## 8. 后续扩展

1. **状态持久化**：支持将节点上下文持久化到数据库，支持断点续传
2. **上下文清理**：提供节点显式清理上下文的机制（节点自行 `NodeContext.Clear()`）
3. **上下文快照**：支持导出/导入节点上下文，写入 `NodeExecutionRecord` 用于调试
4. **监控面板**：可视化节点上下文状态，便于调试
5. **上下文大小上限**：大数据集时限制单节点上下文条目数或总大小，超过时告警或截断
6. **跨节点上下文引用**：显式开放 `$node.<name>.context.xxx` 跨节点读取（需要设计权限/隔离边界）
7. **线程安全升级**：未来引入并行节点执行时，单节点上下文升级为支持并发读写的容器
