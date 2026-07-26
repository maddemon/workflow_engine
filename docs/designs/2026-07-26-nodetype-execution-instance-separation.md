# 补充设计：INodeType 类型实例与执行实例分离 + 执行能力注入

## 0. 与既有设计文档的关系

`2026-07-25-execution-pipeline-refactoring.md` 已规划了 `INodeType` 三重身份问题的整体管线化重构，并给出 `NodeBase : INodeType, INodeHandler` 的目标形态、`ExecutionMode` 改为可配置的 `protected set`，以及逐节点迁移路线。

本文不重复上述内容，聚焦若干**未被该文档覆盖**的具体缺陷并给出可增量实施的最小修复：

1. **实例隔离已实现但存在漏洞**：主执行链路经 `NodeRegistry.Get/TryGet/CreateInstance` 每次返回新实例（`NodeRegistry.cs:79` 的 `Activator.CreateInstance`，CON-3 注释明确为避免共享单例串扰），因此**主路径不存在共享单例数据竞争**。但 `SubExecutionService.ExecuteSubAsync` 接收调用方传入的 `INodeType` 实例并在其上执行（`SubExecutionService.cs:38`），一旦该实例被并发/递归复用即形成真实竞争——当前因该方法尚未被调用而潜伏，属必须在接线前消除的设计陷阱。
2. **能力字段大杂烩**：`NodeBase` 上挂着一整组"每运行注入的能力成员"（`LlmClient`/`Engine`/`HttpExecutionService`/`SubExecutionService`/`ToolResolverService`/`NodeContext`/`Logger`/`Registry`/`GetCredentialAsync`/`GuardSsrf`/`CreateChildContextAsync`/`EvaluateItemAsync`…），把本应"节点自取所需"的依赖硬塞进所有节点的基类。
3. **参数双表示**：节点声明的 typed property（如 `MaxNestingDepth`，含默认值、供面板配置）与运行期字符串键字典 `ResolvedParameters`/`RawParameters` 并存，节点被迫 `CoerceInt(GetResolvedParameter("maxNestingDepth"))` + 手搓 clamp，而非直接读属性（见 §4.3）。

本文方案与 25 日文档目标态完全兼容，可作为其安全前置。

## 1. 问题定位

### 1.1 调用链（现状核对）

```
主路径（每运行独立实例，已隔离）：
InitializeStage.cs:34   var nodeType = nodeRegistry.Get(node.TypeName);   // TryGet→Activator.CreateInstance：每次返回新实例
       │  context.NodeType = nodeType   —— 流入后续阶段
       ▼
ResolutionStage.cs:35   // 经步骤 4 重构：ResolutionStage 已退化为 `return next()`，不再写服务；能力注入改在
       ▼             ExecutionStage（NodeExecutionContext 经 contextFactory.CreateAsync 创建之后）与
                     SubExecutionService / 直接执行适配层（NodeBase.INodeType.ExecuteAsync → InjectCapabilities）完成。
       ▼
ExecutionStage.cs:116  retryExecutor.ExecuteNodeWithRetryAsync(node, nodeType, nodeExecContext, ct)
       ▼
RetryExecutor.cs:72     result = await nodeType.ExecuteAsync(context, effectiveToken);  // 在本运行实例上执行（重试循环复用同一实例）
       ▼
NodeBase.cs:219-223     _rawContext = context; Engine = context.GetOrCreateEngine();   // 写实例字段（本实例内，无跨运行串扰）

子执行路径（真实竞争陷阱，目前未被调用）：
某节点 ExecuteAsync ──（传入自身 nodeType）──▶ SubExecutionService.ExecuteSubAsync(nodeType=调用方实例)
                                              SubExecutionService.cs:38  return await nodeType.ExecuteAsync(context, ct);
                                              // 复用调用方同一实例；若子执行与调用方并发/递归，_rawContext/Engine/LlmClient 被互相覆盖
```

### 1.2 现状核对与真实缺陷

核对结论：`NodeRegistry.Get()` **不**返回共享单例。`Get()` 委托 `TryGet()`（`NodeRegistry.cs:60→69`），后者经 `Activator.CreateInstance(type)` 返回**每次调用的新实例**（`NodeRegistry.cs:79`）；`_instances` 字典（`NodeRegistry.cs:14`）仅在 `Register` 时写入、仅被 `GetAll()` 读取，执行热路径并不触碰它。`Register` 时缓存实例、但 `Get/TryGet/CreateInstance` 一律克隆——这是 CON-3 注释明确设计的并发防护，主路径因此**无共享单例竞争**。

由此，原"主路径用单例导致数据竞争"的判断不成立。真实缺陷如下：

- **（真实竞争陷阱）`SubExecutionService.ExecuteSubAsync` 复用调用方实例**：`SubExecutionService.cs:38` 在调用方传入的 `nodeType` 实例上 `ExecuteAsync`，而该实例正是调用方节点自身用于执行的对象。一旦子执行与调用方并发（或递归子执行复用调用方实例），`_rawContext`/`Engine`/`LlmClient` 等实例字段会被互相覆盖。该路径当前因 `ExecuteSubAsync` 尚无调用方而潜伏，但设计上必须在接线前消除（见 §4.1.B）。
- **（轻微）`RetryExecutor` 跨重试复用同一实例**：`RetryExecutor.cs:72` 在重试循环中复用同一 `nodeType` 实例，`NodeBase.cs:219-223` 在每次重试覆盖 `_rawContext`/`Engine`。当前基本良性（单节点串行重试），但 `_rawContext` 每次被覆盖——属代码异味；本文暂不修复仅记录，后续若重试引入并行能力则需独立实例。
- **（效率）`Get()` 每次克隆带来反射与 Hydrate 开销**：`Get()` 每次都 `Activator.CreateInstance` → 触发 `NodeBase` 构造函数反射 `[NodeMeta]`/`[Port]`（`NodeBase.cs:162-191`），即使纯元数据消费方（`OutputRouter`、`WorkflowSchedulerKernel` 的端口探测）也付出克隆+反射成本。见 §4.1.A 的 `_metaCache` 优化。
- **（能力/参数问题见 §1.4 / §4.3）**：能力大杂烩与参数双表示并不依赖"单例"即可成立——它们是 base 充当服务定位器、Hydrator 不校验导致的独立缺陷。

### 1.3 连带问题：ExecutionMode 不可配置

`NodeBase.cs:210` 硬编码 `ExecutionMode.OnceForAll`，`[NodeMeta]` 无该字段；而 `InitializeStage.cs:35-37` 却读 `nodeType.ExecutionMode` 分支处理 `OncePerItem`。想声明 `OncePerItem` 的节点无从下手（与 25 日文档 §4.4 的 `protected set` 方案一致，本文给出具体落点）。

### 1.4 能力字段大杂烩（与 1.2 同源）

`NodeBase` 当前暴露约 20 个"每运行注入"的节点面向成员（见 §4.2 表格）。这些问题同源：**类型/实例不分 + base 充当能力服务定位器**。只修 `LlmClient` 不解决问题——需用一套机制一次性覆盖全部能力成员，而非逐字段打补丁（且 C# 单继承决定了"每能力一个基类"在多能力节点上无解，见 §4.2）。

## 2. 根因

`NodeBase` 同时是"类型定义（TypeName/Ports，类型级不变事实）"与"执行处理器（持有上下文/引擎/各能力）"。主执行路径已由注册表每次克隆实例来保证隔离，但两处仍违反"每运行独立实例"原则：`SubExecutionService` 复用调用方实例（真实竞争陷阱）、`RetryExecutor` 跨重试复用同一实例（轻微）。更深层的是 base 充当能力服务定位器、Hydrator 只 coerce 不校验——这两点与"是否单例"无关，是独立的设计债务，需各自消除。

## 3. 目标

1. **消除并发数据竞争**：每次执行使用独立的节点实例，单例只承载类型级元数据。
2. **ExecutionMode 可配置**：节点能声明自身执行模式，且 `InitializeStage` 读取生效。
3. **统一移除 base 上的能力成员与方法 helper**：用单一 `[Inject]` 特性，按属性类型从 DI 容器 + 运行上下文注入节点所需能力（评审修正：不再经 `CapabilityRegistry` 从 `NodeExecutionContext` 取数），一次性替代 `NodeBase` 上所有"每运行注入"的节点面向成员与辅助方法，而非逐字段修补。
4. **统一参数模型绑定**：typed property 作为"已解析+已强转+已校验"的唯一直源，移除 `GetResolvedParameter`/`GetRawParameter`/`ReadResolvedParameter`/`CoerceInt` 等字符串键字典读取，节点直接读属性。
5. **不重写现有插件节点**：保持 `NodeBase` 作为子类基类、`INodeHandler.ExecuteAsync(NodeInput, ct)` 作为子类重写入口；节点只需声明自己需要的能力/参数。
6. **兼容 25 日文档目标态**：本文是后续"类型/处理器彻底分离"的可增量前置。

## 4. 设计

### 4.1 最小修复（本文交付）

**A. 元数据静态缓存，消除克隆反射开销**

`NodeBase` 构造函数当前每次都反射 `[NodeMeta]`/`[Port]`（`NodeBase.cs:162-191`）。若每运行克隆实例，反射会在热路径重复。改为按 `Type` 缓存元数据：

```csharp
private static readonly ConcurrentDictionary<Type, (NodeMetaAttribute Meta, PortDefinition[] Ports)> _metaCache = new();

protected NodeBase()
{
    var (meta, ports) = _metaCache.GetOrAdd(GetType(), t => (t.GetCustomAttribute<NodeMetaAttribute>()!, BuildPortsFromAttributes(t)));
    _meta = meta; _ports = ports;   // 不再每次反射
}
```

**B. 强制"每运行独立实例"原则（修正原方案：主路径已满足，缺口在子执行路径）**

原方案"把 `Get()` 改为 `CreateInstance`"是**无效改动**：`Get()` 本就经 `TryGet`→`Activator.CreateInstance` 返回每调用新实例（`NodeRegistryTests.Get_ReturnsDistinctInstancesPerCall` 已断言）。主执行链路已经在用每运行实例，无需改动。真正的缺口是：

- **`SubExecutionService.ExecuteSubAsync` 必须取得自己的每运行实例，禁止复用调用方实例**：它当前接收 `nodeType`（调用方实例）并在其上 `ExecuteAsync`（`SubExecutionService.cs:38`），是真实竞争陷阱。改为内部经 `nodeRegistry.CreateInstance(node.TypeName)`（或等价工厂）取得全新实例后再 `ExecuteAsync`，调用方实例仅用于读取类型元数据。这样子执行与调用方各自的 `_rawContext`/`Engine`/`LlmClient` 互不串扰。
- `RetryExecutor` 重试循环复用同一 `nodeType` 实例，当前基本良性（单节点串行重试），但 `_rawContext` 每次被覆盖——属代码异味；本文暂不修复仅记录，后续若重试引入并行能力则需独立实例。
- `NodeRegistry.Get()` 保留，**明确仅用于元数据读取**（端口探测、`GetDescriptor` 之外的轻量场景）。注意：`Get()` 返回的是**每调用新克隆**，在其结果上 `ExecuteAsync` **并发安全**（无共享状态），但会**跳过 `ResolutionStage` 注入**导致能力缺失（功能缺陷而非并发问题），故不推荐直接调用——仅作开发期软约束，不加硬拦截。
- `NodeRegistry.GetAll()` 返回 `_instances` **非克隆单例**，在其结果上 `ExecuteAsync` 才是真正的共享单例竞争（与本文最初误判同源）。故须在 `NodeApiComplianceAnalyzer` 新增规则：对 `GetAll()` 返回值禁止调用 `.ExecuteAsync(...)`（静态分析拦截，命中即报错）；该规则纳入步骤 2 验收。待 §4.4 将 `INodeType` 与 `INodeHandler` 拆分后，此规则自然消失。
- `GetAll()` 返回 `_instances.Values`（注册时的原始单例，**不克隆**），仅供框架层枚举/目录（当前仅 `CatalogService` 用于目录列举），**绝不可用于执行**；`GetDescriptor` 系列从该单例读元数据（只读，安全）。若日后误将 `GetAll()` 用于获取可执行实例，将引入与本文最初误判相同的竞争——同样禁止。

> ⚠️ **反向护栏（评审要点）**：`NodeRegistry.cs:50` 注释"缓存无状态节点实例，避免每次获取都反射创建"具有误导性，可能诱使后续把 `Get()` "优化"为返回 `_instances` 单例。这恰好会**亲手引入**本文最初误以为已有的并发竞争——此改动**绝对禁止**。当前 `Get/TryGet/CreateInstance` 一律 `Activator.CreateInstance` 克隆，是 CON-3 设计的并发防护，不得退化为单例返回。

**C. ExecutionMode 可配置**

在 `[NodeMeta]` 增加可选字段（或保留 `protected virtual ExecutionMode ExecutionMode` 让子类覆盖，二选一，推荐前者以保证声明式一致）：

```csharp
// NodeMetaAttribute 增加：
public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.OnceForAll;

// NodeBase：
ExecutionMode INodeType.ExecutionMode => _meta.ExecutionMode;
```

`InitializeStage.cs:35` 读 `nodeType.ExecutionMode` 的逻辑不变，现在能正确取到节点声明值。

### 4.2 执行能力注入：单 `[Inject]` 特性 + 能力注册表

**原则**：`NodeBase` 上**零**节点面向的能力成员，也**零**方法 helper。执行能力通过单一特性按属性类型注入——节点只写一个带 `[Inject]` 的属性，直接当普通属性用，不关心来源，也不继承任何接口/基类。

**机制一：单一注入特性**

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute
{
    /// <summary>可选名称，属性类型不足以区分多个同类能力时指定具体来源。</summary>
    public string? Name { get; set; }
    /// <summary>为 true 时上下文取不到即快速失败（默认 false，由节点自行判空）。</summary>
    public bool Required { get; set; }
}
```

**机制二：注入源 = DI 容器 + 运行上下文（评审修正：不再从 `NodeExecutionContext` 上帝对象取数）**

代码库正在拆除 `NodeExecutionContext` 上帝对象：`NodeExecutionContextFactory` 已标 `[Obsolete("...新代码应优先经管线阶段与独立 DI 服务...获取能力，而非经此工厂产出的上帝对象索取")]`，且 `ResolutionStage` 已用 DI 注入 `IHttpExecutionService`/`ISubExecutionService`。因此 `[Inject]` 解析分**两源**，**不再建全局可变注册表**，也不从 `ctx` 统一取数：

- **DI 容器**：服务类能力（应用级无状态单例）经 `IServiceProvider` 按属性类型解析；未注册类型即返回 null，由 `Required`/类型白名单在绑定阶段快速失败。
- **运行上下文（每运行/每节点值）**：以下类型**不是 DI 单例**，必须取自本运行的 `NodeExecutionContext`——它们由框架按节点/按执行解析并可能携带执行态（如审计）：`ILlmClient`（每节点，由 `ExecutionStage.cs:97` 按节点解析）、`IExecutionLogger`（每运行）、`ICredentialAccessor`（每执行，已被 `CredentialAuditAccessor` 包裹，走 ctx 才能触发凭据审计）、`JsEngine`、`NodeContext`、`NodeExecutionContext`（按需 opt-in 逃生口）。这并非把 `ctx` 当服务定位器统一取数，而是显式枚举这 6 个确属每运行/每节点的属性。

预注册能力（节选）：

| 属性类型 | 来源 |
|---|---|
| `ILlmClient` | 运行上下文：`ctx.LlmClient`（每节点；**未注册进 DI**，仅 `ILlmClientFactory` 注册于 DI） |
| `IExecutionLogger` | 运行上下文：`ctx.Logger`（每运行；`FlowEngine.IExecutionLogger` ≠ DI 的 `ILogger<NodeBase>`） |
| `ICredentialAccessor` | 运行上下文：`ctx.Credentials`（每执行，含 `CredentialAuditAccessor` 审计；走 DI 基础访问器会绕过凭据审计） |
| `ILlmClientFactory` | DI |
| `IHttpExecutionService` | DI（`ResolutionStage` 已注入） |
| `ISubExecutionService` | DI（`ResolutionStage` 已注入） |
| `IToolResolver` | DI |
| `INodeRegistry` | DI（ctx 暴露同一 DI 单例，等价） |
| `INodeExecutionContextFactory` | DI（同上） |
| `JsEngine` | 运行上下文：`context.GetOrCreateEngine()` |
| `NodeContext` | 运行上下文：`context.NodeContext` |
| `NodeExecutionContext` | 运行上下文：`context`（按需 opt-in 逃生口） |

**注入点：共享 `NodeCapabilityInjector` 例程（不再新建全局注册表）**

将上面的 `[Inject]` 扫描与 `ResolveCapability` 抽成**共享 helper**（`NodeCapabilityInjector.Inject(NodeBase node, IServiceProvider? sp, NodeExecutionContext ctx)`），由三处复用（步骤 4 已落地，注入点实际为 **ExecutionStage** 而非原方案描述的 ResolutionStage——`NodeExecutionContext` 由 `ExecutionStage` 内部的 `contextFactory.CreateAsync` 创建，ctx 派生能力此前尚不存在）：
- `ExecutionStage` 在 `nodeExecContext` 创建并解析 `LlmClient` 之后，对 `NodeBase` 节点调用该 helper（生产管线主注入点）；
- `SubExecutionService.ExecuteSubAsync` 在内部 `CreateInstance` 取得每运行实例后，调用**同一** helper，保证子执行与主路径注入语义完全一致；
- 直接执行 / 非管线路径：`NodeBase.INodeType.ExecuteAsync` 适配层在写入 `_rawContext` 后调用 `InjectCapabilities(context)` 等价补注入（ctx 派生能力取自 `_rawContext`，DI 能力仅当 `context.NodeRegistry` 非空时映射，避免清空管线已注入的值）。

helper 内部：DI 类型走 `IServiceProvider`（支持 `Name`→`GetKeyedService`），每运行/每节点类型走当前 `NodeExecutionContext`；未知/缺失即按 `Required`/类型白名单在绑定阶段快速失败；反射按 `Type` 缓存（仅一次）。这消除了原 `CapabilityRegistry` 的全局可变映射，与代码库去上帝对象方向一致。

```csharp
// 伪代码：框架侧注入例程（替换原 CapabilityRegistry 调用，位于 ResolutionStage/BindServices）
foreach (var (prop, attr) in _injectProps.GetOrAdd(nodeType.GetType(), Scan))
{
    var v = ResolveCapability(prop.PropertyType, attr.Name, serviceProvider, runContext);
    if (v is null)
    {
        if (attr.Required) throw new NodeExecutionException("CapabilityMissing", $"类型 {prop.PropertyType} 未注入");
        continue; // 节点自行判空
    }
    prop.SetValue(nodeInstance, v);
}

object? ResolveCapability(Type t, string? name, IServiceProvider sp, NodeExecutionContext ctx)
{
    // 1) 每运行/每节点上下文直供值（非 DI 单例）
    if (t == typeof(JsEngine)) return ctx.GetOrCreateEngine();
    if (t == typeof(NodeContext)) return ctx.NodeContext;
    if (t == typeof(NodeExecutionContext)) return ctx;
    if (t == typeof(ILlmClient)) return ctx.LlmClient;              // 每节点
    if (t == typeof(IExecutionLogger)) return ctx.Logger;          // 每运行
    if (t == typeof(ICredentialAccessor)) return ctx.Credentials;  // 每执行（含审计）
    // 2) 其余按类型从 DI 容器解析（Name 指定 keyed 多来源，否则按类型）
    return name is not null ? sp.GetKeyedService(t, name) : sp.GetService(t);
}
```

**节点示例**

```csharp
public sealed class AgentNode : NodeBase
{
    [Inject] public ILlmClient? LlmClient { get; private set; }
    [Inject] public ISubExecutionService? Sub { get; private set; }
    [Inject] public IToolResolver? Tools { get; private set; }
    [Inject] public JsEngine? Engine { get; private set; }

    protected override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (LlmClient is null) throw new NodeExecutionException(MissingLlmClient, "...");
        // 直接用 LlmClient，不关心怎么来的
    }
}
```

`Name` 仅在同类型需区分多来源时使用（如 `[Inject(Name="tool")] IToolResolver ToolResolver`）；正常情况不写。`Required` 用于 `HttpService` 等必填能力，绑定阶段即失败，比执行中途判空定位更准。

**方法 helper 的归属（为何 NodeBase 不应持有它们）**

`NodeBase` 现有一组方法 helper，核查后均不属于节点/基类，应各回各家；移除后 `NodeBase` 连方法 helper 也不剩：

| 当前 helper | 真正归属 | 节点调用方式 |
|---|---|---|
| `EvaluateItemAsync` / `EvaluateContextAsync` | `Script` / `JsEngine`（脚本求值是 Script 在 Engine 上的操作；`ScriptEvaluationExtensions` 已存在） | `[Inject] JsEngine Engine` → `script.EvaluateAsync(Engine, item, idx, ct)`（扩展方法改吃 Engine） |
| `GetCredentialAsync` | `ICredentialAccessor`（`ctx.Credentials` 即此） | `[Inject] ICredentialAccessor Creds` → `await Creds.ResolveAsync(id, ct)`（扩展方法下沉到接口） |
| `GuardSsrf` | 安全/HTTP 子系统（`HttpExecutionService` 内部已在用） | `[Inject] IHttpExecutionService Http` → `Http.GuardSsrf(url)` 或新增 `ISsrfGuard` |
| `TryParseJson` | 通用 JSON 工具，与节点/上下文无关 | 静态 `JsonHelper.TryParse<T>(...)`，无需注入 |
| `CreateChildContextAsync` | `INodeExecutionContextFactory`（类型已存在） | `[Inject] INodeExecutionContextFactory Factory` → `await Factory.CreateAsync(...)` |

由此 `NodeBase` 最终只剩：类型级元数据 + 抽象 `ExecuteAsync` + 生命周期钩子 + 一段**私有、缓存反射**的注入例程（框架职责，非能力成员）。原始 `ExecutionContext` 不再作为 base 成员，需要时由节点显式 `[Inject] NodeExecutionContext Ctx` opt-in。

### 4.3 参数模型绑定：typed property 作为唯一直源

**问题（为何节点被迫绕路）**

节点声明了 typed property（如 `SubAgentToolNode.MaxNestingDepth`，含默认值、供面板配置），运行期却不用它，而是 `CoerceInt(GetResolvedParameter("maxNestingDepth"))` + clamp（见 `SubAgentToolNode.cs:217-228`）。核查后这**不是冗余，是被迫**，根因属框架缺陷（与"是否单例"无关）：

1. **Hydrator 只 coerce、不校验**：`ParameterHydrator` 确实作用于每运行实例（主路径 `Get()` 返回的就是新实例，Hydrate 在其上执行），但它仅做类型转换，不 clamp。节点要的 `[1,10]` 约束只能自己在 `ResolveMaxNestingDepth` 里手搓；且 `CoerceInt` 在 `SubAgentToolNode`/`SubWorkflowToolNode` 各抄一份。由于 typed property 已被 Hydrate 赋了未 clamp 的 coerced 值，节点无法信任它，被迫回退到读原始字典再手搓 clamp。
2. **缺"绑定期校验"环节**：没有 `[Range]`/`[Required]` 之类的声明式约束在绑定时生效，节点只好手动 reconciliation（取字典→强转→clamp→回退默认值）。

根因：**参数"双表示"**——字符串键字典 `ResolvedParameters`/`RawParameters` 被当作运行期事实来源，typed property 虽被 Hydrate 但未经验证，被当成"不可信的装饰"；缺"绑定期校验"，节点只好手动兜底。

> 说明（评审要点）：此处的冗余是**整洁度/一致性**问题（节点未信任已被水合的 typed property、且 hydrator 未承担 clamp），**并非并发 bug**——typed property 已被水合为本 run 值。因此 §4.3 的改进目标是"去重 + 声明式校验"，而非"修复并发"。

**设计：模型绑定只做一次，落在每运行实例，且含校验**

- `ParameterHydrator` 必须作用在**每运行实例**（接 §4.1 per-run-instance 修复）：执行前把已解析值写入该实例的 typed property，节点执行时读到的就是本 run 的值。
- 校验/约束标注（如 `[Range(1, 10)]`、`[Required]`）由 hydrator 或校验阶段执行 → `this.MaxNestingDepth` 自动被 clamp，无需节点手搓。
- `CoerceInt` 等强转并入 hydrator 的 converter，节点侧删除。
- 节点代码从 `ResolveMaxNestingDepth()`/`ResolveMaxIterations()`/`ResolveMemoryEnabled()`/`ResolveMemoryWindowSize()` 一组方法，简化为直接读属性。

```csharp
// 目标态节点（示意）：
public sealed class SubAgentToolNode : NodeBase
{
    [Range(1, 10)] public int MaxNestingDepth { get; set; } = 3;
    // ...
    protected override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (NestingLevel >= MaxNestingDepth) throw new NodeExecutionException("MaxNestingDepthExceeded", ...);
        // 直接读属性，已 coerce+clamp，无需 GetResolvedParameter/CoerceInt
    }
}
```

**这些 helper 的必要性判定**

| 成员 | 对自身 typed 参数 | 残留必要性 |
|---|---|---|
| `GetResolvedParameter` / `GetRawParameter` | 不必要——模型绑定消除 | 移除 |
| `ReadResolvedParameter(ctx, key)` | 不必要（自身参数） | 仅正当用于"子/迭代上下文"读取（如 `PaginateNode` 从 `iterContext` 读 `url`/`bodyExpression`），语义异于"我的参数"；保留为窄口径外部上下文读取 API |
| `CoerceInt` | 不必要 | 删，并入 hydrator converter |
| `TryParseJson` | 不必要（参数声明为 `JsonObject`/模型即由 hydrator 转换） | 仅运行期真要解动态 JSON 串时；其余转静态 `JsonHelper`（§4.2） |

由此 `NodeBase` 不再暴露参数字典读取方法；自身参数一律经 typed property，跨上下文读取走单独窄 API。

### 4.4 目标态（后续，由 25 日文档承载，仅示意）

将"类型"与"处理器"在接口层面彻底分开，单例只实现 `INodeType`（纯元数据），执行时由框架创建独立 `INodeHandler` 实例持有上下文/引擎/服务。本文 4.1 的每运行实例化 + 4.2 的能力注入 + 4.3 的参数模型绑定已使该目标态在行为上等价（base 无执行态字段/无参数字典/节点自取能力），故可作为其安全前置。

## 5. 实施步骤

| 步骤 | 内容 | 验收 |
|------|------|------|
| 1 | `_metaCache` 静态缓存；构造函数改读缓存 | 单测：连续创建同类型实例属性一致；反射次数不随实例数线性增长 |
| 2 | `SubExecutionService.ExecuteSubAsync` 内部改经 `nodeRegistry.CreateInstance(node.TypeName)` 取得每运行实例，不再复用调用方传入的 `nodeType`；新实例创建后由 `SubExecutionService` **额外调用一次 `BindServices`（过渡期）**——此时 `[Inject]` 尚未实现，而子实例不走 `ResolutionStage`、不会自动获得 `HttpExecutionService`/`SubExecutionService`/`ToolResolverService` 注入，子节点（`AgentNode`/`SubAgentToolNode` 等）依赖这三者；`SubExecutionService` 需补充注入 `IHttpExecutionService`/`IToolResolver`（或经 `IServiceProvider` 解析）以完成该过渡调用；`Get()` 加"仅元数据、禁止用于执行"注释，并在 `NodeApiComplianceAnalyzer` 新增规则拦截对 `GetAll()` 返回值调用 `.ExecuteAsync(...)`（真正共享单例危险点） | 单测：`ExecuteSubAsync` 使用的实例与调用方实例非同一引用；并行子执行互不串改上下文；**新实例经 `BindServices` 注入的 `HttpExecutionService`/`SubExecutionService`/`ToolResolverService` 均非空，与主路径一致**；**`NodeApiComplianceAnalyzer` 规则生效，对 `GetAll()` 返回值调用 `ExecuteAsync` 被拦截** |
| 3 | `[NodeMeta]` 增加 `ExecutionMode`；`NodeBase` 改读 `_meta.ExecutionMode` | 单测：声明 `OncePerItem` 的节点 `InitializeStage` 算出 `runCount>1` |
| 4 | 引入 `[Inject]` 特性；抽离共享 `NodeCapabilityInjector`（扫描 `[Inject]` 属性，按 §4.2 两源解析），由 `ResolutionStage` 与 `SubExecutionService` 共用（移除 `CapabilityRegistry`）；`NodeBase` 删除全部能力成员、`BindServices` 与方法 helper；**子实例改由共享 `NodeCapabilityInjector` 注入（替换步骤 2 的过渡 `BindServices`），确认可删除该过渡调用** | 编译通过；框架层单测：DI 未注册类型注入即抛；`Required` 在缺失时快速失败；**确认步骤 2 的过渡 `BindServices` 已移除，且子实例经 `NodeCapabilityInjector` 注入状态不变（三服务非空）** |
| 5 | 将方法 helper 下沉：`ResolveCredentialAsync`→`ICredentialAccessor`、`GuardSsrf`→`IHttpExecutionService`/新增 `ISsrfGuard`、`CreateChildContextAsync`→`INodeExecutionContextFactory`、`TryParseJson`→静态 `JsonHelper`、`ScriptEvaluationExtensions` 改吃 `JsEngine`；同步现有调用点 | 各扩展方法/接口单测通过 |
| 6 | 逐节点迁移：需要能力的节点加 `[Inject]` 属性声明（Agent/LLM/SubAgentTool/HTTP/Filter/JS/Paginate/DB 等） | 各节点测试通过；依赖关系由"base 全局字段"变为"节点自声明类型" |
| 7 | 参数模型绑定：`ParameterHydrator` 改跑每运行实例；支持 `[Range]`/`[Required]` 校验约束；`NodeBase` 删除 `GetResolvedParameter`/`GetRawParameter`/`ReadResolvedParameter`/`CoerceInt`；节点只读 typed property（`PaginateNode` 等跨上下文读取改窄 API） | 单测：参数经 typed property 取到已 coerce+已 clamp 值；删 `GetResolvedParameter` 后编译通过 |
| 8 | 并发回归测试：并行执行同类型节点，断言各自上下文/引擎/能力/参数独立 | 测试通过，无字段串改 |
| 9 | **并发反向护栏（先于任何改动落地）**：并行两次 `InitializeStage`/`ExecutionStage` 执行同一类型，断言拿到**不同实例引用**；并断言 `SubExecutionService` 子执行实例与调用方实例不同引用。该测试当前即应通过，专门防止有人基于错误前提把 `Get()` 改成 `_instances` 单例 | 测试通过（作为后续所有改动的回归护栏，禁止退化为单例） |

## 6. 验收总标准

- 同一节点类型在两条并行分支/两个并发工作流执行时，互不影响上下文、引擎、凭据解析、各能力。
- `ExecutionMode` 由节点声明驱动 `runCount`，`OncePerItem` 类节点可正确逐项运行。
- 每运行实例化不引起可观测的反射/内存回归（`_metaCache` 命中）。
- `NodeBase` 上**不再存在任何**节点面向的执行能力成员，**也不再有任何**方法 helper（`EvaluateItemAsync`/`GetCredentialAsync`/`GuardSsrf`/`TryParseJson`/`CreateChildContextAsync` 等均已下沉到各自所有者）；`NodeBase` 只剩类型级元数据、抽象 `ExecuteAsync`、生命周期钩子与一段私有注入例程。
- 需要能力的节点均经 `[Inject]` 按属性类型声明，且可任意组合多能力；无 `ICapabilityBinder` 接口、无 `*CapableNodeBase` 基类。
- 参数经 typed property 作为"已解析+已强转+已校验"的唯一直源；`GetResolvedParameter`/`GetRawParameter`/`ReadResolvedParameter`/`CoerceInt` 从 `NodeBase` 移除，节点直接读属性（如 `MaxNestingDepth`）；仅跨上下文读取保留窄 API。
- 现有插件节点改动仅限于"加 `[Inject]` 属性声明"与"读 typed property 替代参数字典"，无业务逻辑重写。

## 7. 风险与待定

| 风险 | 影响 | 应对 |
|------|------|------|
| `CreateInstance` 经 `Activator.CreateInstance` 构造，若某节点构造函数有副作用会暴露 | 中 | 步骤 1 缓存元数据已去反射主开销；构造副作用本就不该存在，发现即修 |
| 调用方将 `Get()` 返回值缓存进共享字段并在并行路径复用该引用 | 低 | `Get()` 每次返回新实例，一次性 `Get().ExecuteAsync` 并发安全（仅跳过注入、功能残缺，非并发问题）；真正危险是 `GetAll()` 返回共享单例，已由 `NodeApiComplianceAnalyzer` 硬拦截其 `ExecuteAsync`。`Get()` 缓存复用风险当前无场景，仅记录 |
| `[Inject]` 反射注入成本 | 低 | 按 `Type` 缓存 `PropertyInfo[]`，运行时仅一次字典查 + `SetValue`；可在注册期校验属性可写性 |
| 未知注入类型/名称 | 低 | DI `GetService(type)` 返回 null → 由 `Required`/类型白名单在绑定阶段快速失败，比执行中途判空定位更准；移除全局可变 `CapabilityRegistry` |
| 能力迁移遗漏：某节点用了 base 的 `Logger`/`Registry`/`GetCredentialAsync` 等成员 | 中 | 删除 base 成员/helper 后编译器直接报错，天然兜底；同步更新 `NodeApiComplianceAnalyzer` 的禁用标识符 |
| 方法 helper 下沉改动面 | 中 | `ResolveCredentialAsync`/`GuardSsrf`/`CreateChildContextAsync` 原为 `NodeExecutionContext` 扩展方法，需下沉到 `ICredentialAccessor`/`IHttpExecutionService`/`INodeExecutionContextFactory` 并同步其内部调用点；`TryParseJson` 转静态 `JsonHelper` |
| `LlmNode` 回写 `ExecutionContext.LlmClient` 是否冗余 | 低 | `ExecutionStage.cs:125` 已写入 `session.NodeLlmClients`；迁移时核实能否改为 `[Inject] NodeExecutionContext` opt-in 或删除 |
| `BindServices` 与 `_rawContext` 双注入机制仍并存 | 低 | 步骤 4 一并删除 `BindServices`，统一到 `[Inject]` |
| 把 `Get()` 改为返回 `_instances` 单例（被 `NodeRegistry.cs:50` 注释误导的"优化"） | 高 | **绝对禁止**；保留 `Get/TryGet/CreateInstance` 一律 `Activator.CreateInstance` 克隆（CON-3 防护）；在 `Get()` 上加 XML 注释明确"禁止返回 `_instances`"，Code Review 拦截此类 PR |
| `RetryExecutor` 跨重试复用同一 `nodeType` 实例（`_rawContext` 每次被覆盖） | 低 | 当前单节点串行重试基本良性，仅记录（见 §1.2/§4.1.B）；后续重试若引入并行能力，需改为每重试独立实例 |
| 参数模型绑定改动面 | 中 | `ParameterHydrator` 已作用于每运行实例（`Get()` 返回新实例），本次仅需新增 `[Range]`/`[Required]` 校验与 clamp 逻辑（无需改动"跑在哪个实例"）；`GetResolvedParameter` 删除后，`PaginateNode` 的 `ReadResolvedParameter(iterContext, ...)` 改为窄口径外部上下文读取 API；`SubAgentToolNode`/`SubWorkflowToolNode` 的 `ResolveMax*`/`CoerceInt` 整组删除 |

## 8. 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
|------|--------|----------|------------|
| 2026-07-26 | Agent | 新增：定位执行单例数据竞争，给出每运行实例化 + 元数据缓存 + ExecutionMode 配置的最小修复方案 | 补充设计 |
| 2026-07-26 | Agent | 扩充：新增"执行能力注入"设计，以 `ICapabilityBinder` + 能力记录统一移除 `NodeBase` 上全部能力成员（取代 LlmClient 单点方案） | 补充设计 |
| 2026-07-26 | Agent | 修订"执行能力注入"为单 `[Inject]` 特性 + `CapabilityRegistry` 按类型注入（取代 `ICapabilityBinder`/能力记录），并补入方法 helper 归属表（求值/凭据/SSRF/JSON/子上下文各回各家） | 补充设计 |
| 2026-07-26 | Agent | 新增"参数模型绑定"节（§4.3）：typed property 作为已解析+已强转+已校验唯一直源，移除 `GetResolvedParameter`/`GetRawParameter`/`ReadResolvedParameter`/`CoerceInt`，`ParameterHydrator` 改跑每运行实例并支持 `[Range]`/`[Required]` | 补充设计 |
| 2026-07-26 | Agent | 采纳外部评审修正：原文"Get() 返回共享单例→主路径数据竞争"前提不成立（Get→TryGet→Activator.CreateInstance 每调用新实例，_instances 仅 GetAll 用）；删除 §4.1.B 的无效"改 CreateInstance"步骤，改为在 SubExecutionService 强制每运行实例；§1.2/§4.3 修正"Hydration 跑在单例"等错误子断言，定位真实缺陷为 SubExecutionService 复用调用方实例（潜伏竞争）+ RetryExecutor 跨重试复用 + Get() 克隆反射开销 | 补充设计 |
| 2026-07-26 | Agent | 采纳 Hy3 评审：①补充"禁止把 Get() 改为返回 _instances 单例"的反向护栏（§4.1.B/§7，避免被 line 50 注释误导而亲手引入竞争）；②§4.3 明确"参数双表示"是整洁度/一致性问题而非并发 bug；③§5 步骤 9 新增并发反向护栏测试（并行执行断言实例引用相异）；④新增与 25 日文档目标态兼容性待核对项。§4.2 注入源方向（是否改用 DI 容器而非 CapabilityRegistry-from-ctx）待与用户确认 | 补充设计 |
| 2026-07-26 | Agent | 据用户确认，§4.2 注入源定为"DI 容器 + 运行上下文"：移除 `CapabilityRegistry`-from-ctx，改为 `ResolutionStage`/`BindServices` 按 `[Inject]` 属性类型从 `IServiceProvider`（服务类能力）与运行上下文（JsEngine/NodeContext/NodeExecutionContext）解析，与 `NodeExecutionContextFactory` [Obsolete] 去上帝对象方向一致；同步更新 §3 目标 3、§5 步骤 4、§7 风险行 | 补充设计 |
| 2026-07-26 | Agent | 采纳二次评审（deepseek）5 项修正：①§5 步骤 2 采用选项 B——SubExecutionService 新实例额外调用过渡 `BindServices`（需补充注入 IHttpExecutionService/IToolResolver），步骤 4 验收确认可移除该过渡调用；②§4.1.B 明确"禁止 ExecuteAsync"仅靠注释不足，改由 `NodeApiComplianceAnalyzer` 静态拦截，并纳入步骤 2 验收；③统一 §1.2/§4.1.B 对 RetryExecutor 的语气为"暂不修复仅记录"，§7 补风险行；④§7 风险行"复用 Get() 实例"改写为"调用方缓存引用进共享字段"（降为低）；⑤补 `GetAll()` 返回非克隆单例、禁止用于执行的说明。按用户指示移除 25 日文档兼容性待核对项 | 补充设计 |
| 2026-07-26 | Agent | 采纳 Hy3 三次评审（关键缺陷）：①**修正 §4.2 能力来源**——经代码核实 `ILlmClient`（ExecutionStage.cs:97 每节点解析、未注册 DI）、`IExecutionLogger`（ctx.Logger，≠DI 的 ILogger<NodeBase>）、`ICredentialAccessor`（ctx.Credentials 已被 CredentialAuditAccessor 包裹）均为每运行/每节点上下文属性，非 DI 单例；将三者从 DI 列移入上下文源，ContextProvided 集合扩为 6 型，ResolveCapability 同步按 ctx 取值；②合规分析器规则从 `Get()` 改为 `GetAll()`（Get() 返回克隆、执行并发安全仅跳过注入；GetAll() 返回共享单例才是真竞争）；③Name 走 `GetKeyedService`、统一 Required 语义；④抽离共享 `NodeCapabilityInjector` 由 ResolutionStage 与 SubExecutionService 共用 | 补充设计 |
| 2026-07-26 | Agent | 落地步骤 2/3/4：SubExecutionService 内部经 nodeRegistry.CreateInstance 取得每运行实例并共用 NodeCapabilityInjector 注入（消除复用调用方实例的真实并发竞争）；[NodeMeta] 增加 ExecutionMode，NodeBase 改读 _meta.ExecutionMode；引入 [Inject] 特性与共享 NodeCapabilityInjector（ctx 派生 6 型 + DI 两源），NodeBase 严格全量迁移至零能力成员；NodeApiComplianceAnalyzer 新增 FE0002 拦截 GetAll().ExecuteAsync；并修正 §4.2 注入点为 **ExecutionStage**（原 ResolutionStage 在步骤 4 已退化为 `return next()`，`NodeExecutionContext` 由 ExecutionStage 内部 `contextFactory.CreateAsync` 创建，ctx 派生能力须在其后注入） | plan-feat/nodetype-execution-instance-separation |
| 2026-07-26 | Agent | 落地步骤 5：方法 helper 下沉——求值经 `[Inject] NodeExecutionContext Ctx` + `ScriptEvaluationExtensions`（保持吃 Ctx，未改吃 JsEngine，避免重写脚本引擎内部）；凭据经 `[Inject] ICredentialAccessor` + 新增 `ResolveAsync` 默认方法（复刻 Guid/名称分派，catch 返回 null）；SSRF 经 `Ctx.GuardSsrf`（未新增 ISsrfGuard，复用既有扩展）；JSON 经静态 `JsonHelper.TryParse<T>`；子上下文经 `[Inject] INodeExecutionContextFactory`（NodeExecutionContextFactory.CreateAsync 自置 `ContextFactory = this`，故 ctx 派生可解析）；`IsInvokedByAgent`/`ShellExecutionEnabled`/`NestingLevel`/`LoadWorkflowAsync` 改为 `Ctx.*`。FE0001 禁用集移除 `IsAgentInvocation`/`AllowShellExecution`/`NestingDepth`/`WorkflowLoader`（经 `[Inject] Ctx` 显式 opt-in 已合法）。NodeBase 仅剩 `GetRawParameter`/`GetResolvedParameter`/`ReadResolvedParameter`（属步骤 7 参数模型绑定） | plan-feat/nodetype-execution-instance-separation |
| 2026-07-26 | Agent | 落地步骤 7：参数模型绑定——`ParameterHydrator` 在写入 typed property 前按 `[Range]` clamp 数值类型、`[Required]` 仅记 warning；`NodeBase` 删除 `GetRawParameter`/`GetResolvedParameter` 并连带删除已无引用的 `_rawContext` 字段与赋值，保留静态 `ReadResolvedParameter(ctx, key)` 作为跨上下文窄 API（偏离 §5 步骤 7 的"一并移除"，§4.3 已允许）；`SubAgentToolNode`/`SubWorkflowToolNode`/`WebSearchToolNode`/`PaginateNode` 改为读 typed property 并删除 `Resolve*`/`CoerceInt`/`GetConfig`。新增 `ParameterHydratorTests` 的 2 个 `[Range]` clamp 单测；`PaginateNodeTests` 4 用例改设 typed property。`dotnet test` 全绿（Runtime 933，较基线 +2） | plan-feat/nodetype-execution-instance-separation |
