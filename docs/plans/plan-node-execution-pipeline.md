# 开发计划：节点执行管线化重构（plan-node-execution-pipeline）

本计划由 `docs/designs/2026-07-25-execution-pipeline-refactoring.md` 提升而来，设计结论（必要性评级、最小上下文原则、DI 服务边界、抛异常替代 `context.ErrorResult`、OnError×RetryExecutor 排序、GetExtraPorts 时机）已固化为本计划的实现约束。关键接口契约见附录 A，n8n 对比与现状代码核查见附录 B；架构设计细节以本计划 + 附录为准，不在 `docs/architecture/` 重复贴实现。

> 交叉引用：本计划与 `docs/plans/mvp/plan-mvp-12-property-parameters.md`（标准节点属性驱动、移除 `GetParameter<T>`）在"节点侧简化"目标上重叠。本计划聚焦执行管线框架层（Stage/NodePipelineContext/NodeBase 基类 + 独立 DI 服务边界），节点侧属性驱动的具体迁移以 `plan-mvp-12-*` 为准，本计划引用其结论，不重复规划。

## 0. 必要性与命名约定

### 0.1 命名约定（与 plan-node-level-context-architecture.md 区分）

本文档与 `plan-node-level-context-architecture.md` 都涉及"上下文"，但语义不同，**命名必须区分**，避免实现者混淆：

| 名称 | 含义 | 归属计划 | 说明 |
|------|------|----------|------|
| `NodeContext` | 节点级持久化字典（LoopNode 跨迭代状态），经 `$nodeContext` 表达式注入 | `plan-node-level-context-architecture.md` | 该计划尚未合并实现；本计划在 `Core/Abstractions` 先定义**最小占位类型**（`IDictionary<string,object?>` 包装），供阶段一编译，待该计划落地后替换 |
| `NodePipelineContext` | 管线各 `IExecutionStage` 之间共享的临时上下文对象（本计划新引入） | 本计划 | 由本文档原 `NodeContext` 改名而来 |
| `NodeExecutionContext` | 现有上帝对象（28 属性） | 现状代码 | 迁移期保留，作为 `INodeType` 适配壳的输入；阶段五收尾 |

**约束**：本计划新增的管线共享对象一律称 `NodePipelineContext`，不得再称 `NodeContext`。节点级持久化状态经 `ExecutionSession.NodeContexts[nodeId]` 访问，由 `NodeBase` 以 protected 形式暴露给节点，不内嵌进 `NodePipelineContext`。

### 0.2 必要性评级说明

本文多处标注 `[必要性:高/中/低]`，区分"必须做"与"可暂缓/可选"：

- 🔴 **高**：痛点明确、收益大、风险低，应优先落地。
- 🟡 **中**：有价值但需权衡，或仅部分节点需要。
- ⚪ **低**：收益不确定 / 成本高，建议暂缓或仅作长期方向。

### 0.3 总体必要性结论

节点侧简化（`NodeBase` + 特性 + 声明式校验 + 消费已求值结果 + 最小上下文原则）**明确必要且低风险，直接做**；`NodeProcessor` 阶段化拆分**建议做**；把阶段包装成"ASP.NET 风格中间件"抽象**必要性低、且会与短路需求冲突，改用语义更诚实的有序 Stage 列表**；`NodePipelineContext` 的接口隔离（管线侧上帝对象）**优先级低于节点侧，可暂缓**。框架服务边界显式化（`HttpClientPool` 等独立 DI 服务）**必要且须补**。

---

## 1. 概述

### 1.1 背景与问题

当前节点执行架构中，`INodeType` 承担"元数据声明 + 业务逻辑 + 部分框架职责"三重身份，而 `NodeProcessor.ProcessAsync()` 是一个 ~256 行的 mega 方法（`NodeProcessor.cs:61-318`），把上下文生命周期、参数预求值、重试、结果构建、事件发布、输出路由、JS 引擎释放全部揉在一步中。

**节点中不应属于节点的逻辑：**

| 职责 | 当前状态 | 出现范围 | 必要性 |
|------|----------|----------|--------|
| Input/Output 端口声明 | 每个节点手动定义几乎相同的端口列表 | 10/12 标准节点 | 🔴 高 |
| TypeName/DisplayName/Category/Icon 元数据 | 每个节点重复 4 个只读属性 | 全部 | 🔴 高 |
| 参数非空校验 | 各节点用 if-guard 手动检查 Script 参数 | 5+ 节点 | 🔴 高 |
| 异常→ErrorResult 转换 | 写法各异（FilterNode 不捕），节点自调 `context.ErrorResult(...)` | 8+ 节点 | 🔴 高（改抛异常） |
| NodeExecutionResult 手动构造 | `new NodeExecutionResult { Success = true, Output = ... }` | 几乎所有节点 | 🟡 中 |
| 凭据手动解析 | 节点自调 `context.ResolveCredentialAsync()` | HTTP 等节点 | 🟡 中 |
| 共享基础设施直接引用 | 节点读 `context.HttpClientPool` / `context.NodeRegistry` / `context.ContextFactory` / `context.WorkflowLoader`（服务定位器） | Agent/SubAgent/Paginate/ReadFile 等 | 🔴 高（改独立服务） |
| OncePerItem 循环 | JSNode 自建 `CodeMode` 平行于框架 `ExecutionMode` | JSNode | 🟡 中 |
| BranchIndex 路由魔法 int | 节点返回 int 表达输出端口 | SwitchNode、IfNode | 🟡 中 |

**NodeExecutionContext 是上帝对象**：现有 **28 个属性**（`NodeExecutionContext.cs:19-208`），既是数据载体又是服务定位器。本计划只承诺在"节点侧"消除上帝对象（`NodeInput` 精简视图 + 最小上下文原则 §A.7）；管线内部 `NodePipelineContext` 仍较大，其接口隔离列为可暂缓项（§5）。更重要的是：大量"服务定位器"属性应成为独立 DI 服务（§A.6/§A.8）。

### 1.2 范围

- 新建 `NodePipeline` + `IExecutionStage` 有序阶段列表，取代 `NodeProcessor.ProcessAsync` 的单体逻辑。
- 新建 `NodeBase` 基类 + `[NodeMeta]`/`[Port]`/`[Required]` 特性，集中消除节点样板（`HintAttribute`/`CredentialAttribute` 复用现有，见 §A.3）。
- 确立最小上下文原则：节点只读自身属性 + `NodeInput` + 复杂节点经 `NodeBase` protected 能力，失败用抛 `NodeExecutionException`，不再向 context 索取或自构错误。
- 将 `HttpClientPool`/`NodeRegistry`/`ContextFactory`/`WorkflowLoader`/`LlmClientFactory`/`ScriptCache`/凭据/日志/安全开关/`Memory` 等显式立为独立 DI 服务，由框架消费，不暴露给节点。
- 引入编译期合规检查固化最小上下文原则（§6）。

### 1.3 不覆盖范围

- 节点级持久化字典（`NodeContext`）的设计归属 `plan-node-level-context-architecture.md`，本计划仅消费（经 Core/Abstractions 占位类型），不重定义其完整语义。
- 表达式引擎、Script 类型求值算法本身（见 `plan-script-eval-facade.md`、`plan-script-type-refactor.md`）。
- `NodePipelineContext` 的细粒度接口隔离（多 `I*Context` 接口）——列为暂缓项（§5），触发条件见 §5.2。
- 前端参数面板渲染逻辑——`ParameterDiscoverer` 已存在且完整工作，本计划仅确保节点消费端贯通。
- 标准节点属性驱动的具体迁移步骤——归属 `plan-mvp-12-property-parameters.md`，本计划不重复规划。

---

## 2. 交付物清单

| 类别 | 交付物 | 必要性 |
|------|--------|--------|
| 代码 | `NodePipeline` / `IExecutionStage`（有序 Stage 列表 + 驱动器短路） | 🔴 高 |
| 代码 | `NodePipelineContext`（管线共享上下文，改名后） | 🔴 高 |
| 代码 | `NodeBase` 基类 + `INodeHandler` / `NodeInput` / `NodeOutput` / `NodeExecutionException` | 🔴 高 |
| 代码 | 声明式特性（**新建**）：`NodeMetaAttribute` / `PortAttribute` / `RequiredAttribute` | 🔴 高 |
| 代码 | 声明式特性（**接续现有，不新建**）：`HintAttribute` / `CredentialAttribute`（位于 `FlowEngine.Core/Attributes/`，已有 `PresentationHint` 枚举与 `CredentialTypes`/`CredentialType` 属性，迁移节点直接复用） | 🔴 高 |
| 代码 | 七阶段实现：`InitializeStage`/`ValidationStage`/`ResolutionStage`/`ExecutionStage`/`PostProcessStage`/`RoutingStage`/`PersistenceStage` | 🔴 高（核心）/ 🟡 中（部分） |
| 代码 | 独立 DI 服务：`IHttpClientPool`+`HttpExecutionService`、`ICredentialService`、`ISubExecutionService`、`IWorkflowLoader`、`ILlmClientFactory`、`IShellExecutionGate`、`IRecursionGuard`、`IWorkflowMemoryService` 等（**层归属见 §A.6**） | 🔴 高 |
| 代码 | `NodeExecutionException` + 统一转换（`ExecutionStage`/`PostProcessStage` 兜底） | 🔴 高 |
| 代码 | 编译期/静态分析合规检查（禁止节点引用 `context.ErrorResult`/`GetParameter`/`HttpClientPool` 等索取式 API） | 🔴 高 |
| 占位类型 | `Core/Abstractions` 中最小 `NodeContext` 占位（跨计划共用，待 `plan-node-level-context-architecture.md` 落地后替换） | 🔴 高（阶段一依赖） |
| 测试 | 各 Stage 独立单元测试（无节点依赖） | 🔴 高 |
| 测试 | `OnErrorAsync` × `RetryExecutor` 交互测试（瞬态耗尽→降级、不可重试不重试） | 🔴 高 |
| 测试 | 动态端口 `GetExtraPorts()` 在 `ParameterHydrator` 之后用真实实例发现的测试 | 🟡 中 |
| 测试 | 逐节点迁移回归测试（WaitNode → ... → HttpRequestNode），现有测试保持不变（§3 阶段四） | 🔴 高 |
| 测试 | 迁移前后 microbenchmark（管线 vs 当前路径，退化 ≤5%） | 🟡 中 |
| 文档 | 更新 `docs/architecture/node-system.md` 反映新执行模型（链接而非贴实现） | 🟡 中 |
| 迁移 | 现有 `NodeExecutionContext` / `NodeExecutionContextFactory` 逐步降级为适配壳（阶段五） | ⚪ 低/暂缓 |

---

## 3. 开发阶段

### 阶段一：管线框架抽象（NodePipeline / IExecutionStage / NodePipelineContext / NodeBase） `[必要性:中]`

- **目标**：建立管线骨架与节点基类，不影响现有节点运行。
- **核心任务**：
  1. 定义 `IExecutionStage` 接口（含 `NodePipelineContext` + `Func<Task> next`）。
  2. 实现 `NodePipeline`（有序 Stage 列表 + 驱动器显式短路，不套用中间件抽象）。
  3. 定义 `NodePipelineContext`（见 §0.1 命名约定；不含节点级 `NodeContext`，经 `ExecutionSession` 引用）。
  4. 在 `Core/Abstractions` 定义最小占位 `NodeContext`（`IDictionary<string,object?>` 包装，跨计划共用）。
  5. 定义 `INodeHandler` / `NodeInput` / `NodeOutput` / `NodeExecutionException`。
  6. 实现 `NodeBase` 基类（从 `[NodeMeta]`/`[Port]` 推导元数据与端口，将 `INodeHandler` 适配到 `INodeType`）。
- **输入**：现状 `NodeProcessor.ProcessAsync`、`NodeExecutionContext`、`INodeType`。
- **输出**：可编译的管线骨架；现有节点仍走旧路径。
- **验收标准**：`dotnet build` 通过；管线可实例化但暂不接入主流程。
- **依赖**：`Core/Abstractions` 最小 `NodeContext` 占位类型（本阶段内定义）。
- **Task 清单**：
  - `IExecutionStage` 接口 + `NodePipeline`（文件：`FlowEngine.Runtime/Execution/Pipeline/`）。
  - `NodePipelineContext`（文件同上；属性见附录 A.2，移除 `NodeState`，改引用 `ExecutionSession.NodeContexts`）。
  - 最小 `NodeContext` 占位（文件：`FlowEngine.Core/Abstractions/`，注"待 plan-node-level-context-architecture.md 替换"）。
  - `INodeHandler` / `NodeInput` / `NodeOutput`（文件：`FlowEngine.Core/Abstractions/` 或 `FlowEngine.Runtime/`）。
  - `NodeExecutionException : DomainException`（文件：`FlowEngine.Core/Exceptions/`）。
  - `NodeBase` 基类 + `NodeMetaAttribute`/`PortAttribute`（文件：`FlowEngine.Plugins.Standard/` 或 Core）。
  - 测试：管线驱动器短路逻辑单元测试（`FlowEngine.Runtime.Tests`）。

### 阶段二：NodeProcessor 阶段化拆解 `[必要性:中]`

- **目标**：将 `NodeProcessor.ProcessAsync`（~256 行）按职责提取为七阶段，行为不变。
- **核心任务**：
  1. 实现 `InitializeStage`（NodePipelineContext 生命周期 + 环路失控保护，对应 `NodeProcessor` L78-119）。
  2. 实现 `ResolutionStage`（确保 Script 预求值 + 装配，对应 `NodeExecutionContextFactory` + `ResolveLlmClientForNode`）。
  3. 实现 `ExecutionStage`（协调 OncePerItem 循环 + `RetryExecutor` 包裹 + `OnErrorAsync` 降级）。
  4. 实现 `PostProcessStage`（SuccessWhen 后置检查 + 异常统一转换 + OncePerItem 累积 + 限流）。
  5. 实现 `RoutingStage`（PortOutputs/BranchIndex 路由，对应 `OutputRouter.RouteOutputsAsync`）。
  6. 实现 `PersistenceStage`（NodeExecutionRecord 构建 + 脱敏 + 事件发布，对应 `NodeProcessor` L207-251 + `SecretMasker`）。
- **输入**：阶段一骨架 + 现状 `NodeProcessor`。
- **输出**：旧 `INodeType` 接口作为 `ExecutionStage` 适配目标，行为等价。
- **验收标准**：`NodePipeline` 接入主流程后，`dotnet test` 全部通过；无行为回归。
- **依赖**：阶段一。
- **Task 清单**：
  - 七阶段实现（文件：`FlowEngine.Runtime/Execution/Stages/`）。
  - `NodeExecutionContextFactory` 通过工厂适配器运行（阶段二不动其行为）。
  - 测试：各阶段独立单元测试（对应 L78-119 / 134-148 / 176-185 / 196-205 / 212-251 / 305-315 行为逐一对照）。
  - 测试：短路机制——`ValidationStage`/`ResolutionStage`/`RoutingStage` 失败均直达 `PersistenceStage`。

### 阶段三：声明式样板集中 + 独立 DI 服务 `[必要性:高]`

- **目标**：节点侧样板集中到 `NodeBase` + 特性；共享基础设施显式立为独立 DI 服务。
- **核心任务**：
  1. 实现 `ValidationStage` 声明式校验（扫描 `[Required]`、Script 非空源码、类型约束）。
  2. **新建** `RequiredAttribute`；**审核并接续现有** `HintAttribute` / `CredentialAttribute`（位于 `FlowEngine.Core/Attributes/`，直接复用其 `PresentationHint` 枚举与 `CredentialTypes` 属性，不新建）。
  3. 落地独立 DI 服务（§A.6，含层归属）：`IHttpClientPool`+`HttpExecutionService`、`ICredentialService`、`ISubExecutionService`、`IWorkflowLoader`、`ILlmClientFactory`、`IShellExecutionGate`、`IRecursionGuard`、`IWorkflowMemoryService`、`ILogger` sink。
  4. 从 `NodeExecutionContextFactory` 产出的上下文移除服务定位器字段（随服务落地）。
  5. `NodeBase` 暴露 protected 节点内部能力（`Engine`/`LlmClient`/`StreamSink`，§A.6）。
- **输入**：阶段二阶段。
- **输出**：新建节点可用 `NodeBase` 新写法；框架服务边界显式。
- **验收标准**：新建 `IfNode`（用 `NodeBase`）全部测试通过；`dotnet build` 通过。
- **依赖**：阶段一、二。
- **Task 清单**：
  - `ValidationStage`（文件：`FlowEngine.Runtime/Execution/Stages/`）。
  - 特性类：`RequiredAttribute`（新建），`HintAttribute`/`CredentialAttribute`（审核现有 API 签名，确认复用）。
  - 独立 DI 服务接口 + 实现（层归属见 §A.6）。
  - `NodeExecutionContextFactory` 服务定位器字段移除（阶段三）。
  - 测试：声明式 `[Required]` 短路 + 类型约束校验。
  - 测试：`HttpExecutionService.SendAsync` 集成（SSRF + 凭据注入 + 重试，节点不直接引用池）。
  - 测试：`CredentialAttribute` 在 `ResolutionStage` 解析为值注入，节点不调 `ResolveCredentialAsync`。

### 阶段四：逐节点迁移（强制 NodeBase） `[必要性:高]`

- **目标**：从简单节点逐步迁移到 `NodeBase`，修掉索取式违规，阶段四后强制 `NodeBase`。
- **核心任务**：
  1. 迁移顺序（风险从低到高）：`WaitNode` → `MergeNode` → `IfNode`/`SwitchNode`/`FilterNode` → `LoopNode` → `JSNode`/`AgentNode`/`HttpRequestNode`。
  2. 迁移期修掉 `GetParameter` 扩展方法（`NodeExecutionContextExtensions.cs:20`，同时查 `ResolvedParameters`/`RawParameters`）、直接读 `context.ResolvedParameters`（`CalculatorToolNode`/`SubAgentToolNode`/`PaginateNode`）、`context.HttpClientPool`（`ReadFileNode`）等违规——逐点 audit（§A.7）。
  3. `AgentNode.CollectTools` 改为调用框架 `ToolResolver` 服务（现状重复 `Core/Agent/ToolResolver.cs:43`）。
  4. JSNode 自建 `CodeMode` 并入框架 `ExecutionMode`，移除平行循环。
  5. 旧 `INodeType` 退化为 `NodeBase` 适配壳（非并存执行路径）。
- **输入**：阶段三新写法 + 独立服务。
- **输出**：标准节点全部走 `NodeBase`；旧接口仅作兼容壳。
- **验收标准**：每个迁移节点原测试全过；新写法代码行数较现状减少 ≥40%（基线基于 12 节点实测算行）；编译期合规检查无违规。
- **依赖**：阶段三。
- **测试策略（明确）**：现有行为测试**保持完全不变**——`NodeBase` 适配 `INodeType`，迁移后节点经引擎调用仍走适配壳，测试套件无需修改；仅当测试直接 `new` 节点并调用 `INodeType.ExecuteAsync(context, ct)` 时，仍由适配壳承载，行为等价。**无需为新写法维护第二套测试**；若个别旧测试直接构造 `NodeExecutionContext` 并调用 `GetParameter`，该测试随适配壳保留或迁移到框架只读上下文，不在节点代码中新增大陆调用。
- **Task 清单**：
  - 每节点一个迁移 Task（含旧测试适配 + 新写法 + 行数对比记录）。
  - 编译期合规检查（Roslyn 分析器或单元测试扫描）：节点不得引用 `context.ErrorResult` / `GetParameter` / `HttpClientPool` / `NodeRegistry` / `ContextFactory` / `WorkflowLoader` / `LlmClientFactory` / `ScriptCache` / `NestingDepth` / `AllowShellExecution` / `IsAgentInvocation` / `ResolveCredentialAsync` / `ResolvedParameters` / `RawParameters`（§A.7/§A.8）。
  - 测试：动态端口 `GetExtraPorts()` 在 `ParameterHydrator` 后真实实例发现（§A.4.1）。
  - 测试：`OnErrorAsync` × `RetryExecutor` 交互（§A.6.1）。

### 阶段五：NodeExecutionContext 上帝对象收尾 `[必要性:低/暂缓]`

- **目标**：将 `NodeExecutionContext` 过剩属性分散或仅保留节点侧，精简 `NodePipelineContext`。
- **核心任务**：
  1. `NodePipelineContext` 接口隔离（`IValidationContext`/`IExecutionContext` 等）——暂缓，触发条件见 §5.2。
  2. `NodeExecutionContextFactory` 精简为轻量适配器（仅 `ParameterHydrator` + 轻量 `NodeExecutionContext`），随后标记 deprecated。
- **输入**：阶段四完成。
- **输出**：旧上帝对象降级。
- **验收标准**：`dotnet build` 通过；无功能回归。
- **依赖**：阶段四。
- **Task 清单**：
  - `NodePipelineContext` 接口隔离（暂缓，按 §5.2 触发条件启动）。
  - `NodeExecutionContextFactory` deprecated 标注与精简。

---

## 4. 阶段依赖图

```
阶段一（管线骨架/NodeBase/NodePipelineContext + 占位 NodeContext）
   │
   ▼
阶段二（NodeProcessor 阶段化拆解，行为不变）
   │
   ▼
阶段三（声明式校验 + 独立 DI 服务，新写法可用）
   │
   ▼
阶段四（逐节点迁移 + 强制 NodeBase + 编译期合规检查）
   │
   ▼
阶段五（上帝对象收尾，暂缓）
```

约束：每阶段保持 `dotnet build` + 现有测试通过，不跨阶段合并大爆炸重构。

---

## 5. 风险与待定项

### 5.1 风险

| 风险 | 影响 | 应对 | 必要性 |
|------|------|------|--------|
| 阶段调度性能开销 | 每次节点执行多 7 次委托 | 阶段数量固定，开销可忽略 | ⚪ 低 |
| JSNode `CodeMode` 与 `ExecutionMode` 平行 | OncePerItem 两套逻辑 | `RunOnceForEachItem` 并入框架 `ExecutionMode`，移除 JSNode 自建循环 | 🟡 中 |
| BranchIndex 与 PortOutputs 并存 | 路由逻辑两套 | `PostProcessStage` 统一（PortOutputs 已存在，泛化即可） | 🟡 中 |
| 现有测试适配成本 | 回归测试成本 | 每阶段可单独测试；集成测试保持现有用例 | 🟡 中 |
| **NodeBase 与 INodeType 双路径共存** | 双倍维护；逃生口绕过校验/后处理 | 阶段四后强制 `NodeBase`，旧接口退化为适配壳 | 🔴 高 |
| **SecretMasker 脱敏时机缺口** | PersistenceStage 脱敏时参数已被改 | `NodePipelineContext.RawParametersSnapshot` 保留 ResolutionStage 前原始参数；沿用对 `RawParameters`/`ResolvedParameters` 双脱敏（`NodeProcessor.cs:461-464`） | 🔴 高 |
| **RoutingStage 失败路径** | 原短路只覆盖 Validate/Resolve | 每阶段失败置 `context.Result` 并短路到 `PersistenceStage` | 🔴 高 |
| **序列化兼容性** | `NodeBase` 子类属性反序列化需兼容现有 workflow JSON | `[JsonIgnore]` 标记非参数属性；`ParameterHydrator` 只处理声明 `[Hint]` 的属性；字段级兼容测试 | 🔴 高 |
| **复杂节点能力缺口** | NodeInput 不含引擎/LLM/HTTP/流式 | 节点内部能力经 `NodeBase` protected（§A.6）；共享基础设施作为独立 DI 服务（§A.8），不污染 NodeInput | 🔴 高 |
| **节点索取式 API 残留** | `GetParameter`（`NodeExecutionContextExtensions.cs:20`）/ 直接读 `context.ResolvedParameters`（`CalculatorToolNode`/`SubAgentToolNode`/`PaginateNode`）/ `context.HttpClientPool`（`ReadFileNode`） | 阶段四迁移期逐点 audit 修掉；用 §6 编译期检查固化（§A.7/§A.8） | 🔴 高 |
| **命名混淆** | `NodeContext`（节点级持久化，`plan-node-level-context-architecture.md`）与本计划管线上下文同名 | 本计划管线上下文改名 `NodePipelineContext`（§0.1），交叉引用；节点级类型先以 Core/Abstractions 占位 | 🔴 高 |
| **跨计划类型未定义** | 节点级 `NodeContext` 类型在代码尚不存在（仅计划描述） | 阶段一在 `Core/Abstractions` 定义最小占位，待 `plan-node-level-context-architecture.md` 落地替换 | 🔴 高 |

### 5.2 待定项

| 事项 | 决策点 | 必要性 |
|------|--------|--------|
| `NodeBase` 是否强制 | 阶段四后强制，旧 `INodeType` 退化为适配壳 | 🔴 高 |
| `NodePipelineContext` 接口隔离 | 倾向暂缓；节点侧精简已达成主要收益。**触发条件**：当 `NodePipelineContext` 属性数达到/超过 `NodeExecutionContext` 当前 28 个，或新增第 5 个以上 `IExecutionStage` 需向其中写入专属属性时，重新评估并启动接口隔离（`IValidationContext`/`IExecutionContext` 等） | ⚪ 低/暂缓（带触发条件） |
| JS 引擎生命周期是否独立阶段 | 是，`EngineLifecycleStage`：创建 → 注入 → 释放 | 🟡 中 |
| OncePerItem 的 `ItemIndex` 语义 | `ItemIndex` 范围 = `Max(各输入端口批次长度)`（`NodeProcessor.cs:74-76`），多端口不等长时禁止按单端口长度推断 | 🔴 高（正确性） |
| 有状态节点状态载体 | `LoopNode` 直接操作节点级 `NodeContext`（经 `NodeBase` protected 引用，§0.1），不引入声明式 `ContextChanges` 写入；`NodeOutput.ContextChanges` 字段保留为可选 | 🟡 中 |
| 框架服务边界显式化 | `HttpClientPool`/`NodeRegistry`/`ContextFactory`/`WorkflowLoader`/`LlmClientFactory`/`ScriptCache`/凭据/日志/`Memory` 作为独立 DI 服务，不在 context 也不在 `NodeBase`（§A.6/§A.8）；层归属见 §A.6 | 🔴 高 |
| `ScriptCache` 引擎内部化 | 表达式求值 `ScriptCache` 由框架引擎/表达式子系统提供，节点不可见 | 🟡 中 |
| `CancellationToken` 从 context 移除 | `ExecuteAsync` 已有 `ct`；节点不得忽略 `ct` 自建 `CancellationTokenSource`（AgentNode 超时 CTS 与 `RetryExecutor` 超时合一） | 🟡 中 |
| 安全开关门禁 | `AllowShellExecution`/`IsAgentInvocation` 由框架 `IShellExecutionGate` 门禁，节点不读 | 🔴 高 |
| `NestingDepth` 递归保护 | 由框架 `IRecursionGuard` 保护，节点不评估 | 🟡 中 |
| `Memory` 跨节点共享状态 | 作为 `IWorkflowMemoryService`，不挂每个节点 context | 🟡 中 |
| `ResolvedParameters`/`RawParameters` 节点读取禁令 | 节点不得直接读（CalculatorToolNode/SubAgentToolNode/PaginateNode 现状违规，迁移期修） | 🔴 高 |
| 校验与绑定顺序 | ValidationStage 在 ParameterHydrator **之前**运行（见 §A.5.1），Hydrator 对缺失可选参数保持宽容 | 🔴 高（正确性） |
| 预求值入口选择 | 阶段调用 Runtime 包装 `ScriptParameterPreEvaluator`，其委托 Core `ScriptParameterPreEvaluatorCore`（见 §A.5.2） | 🟡 中 |

---

## 6. 验收总标准

- [ ] 各阶段可独立单元测试，无节点依赖。
- [ ] `NodeBase` + 特性声明可完整替代 `INodeType` 的手动元数据/端口（`HintAttribute`/`CredentialAttribute` 复用现有，不新建）。
- [ ] 新写法节点比当前写法减少 ≥40% 代码行数（基线须基于真实 12 节点实测算行：`IfNode.ExecuteAsync` ~25 行、`SwitchNode` ~23 行、`HttpNodeExecution.ExecuteAsync` ~63 行）。
- [ ] `NodeProcessor.ProcessAsync` 拆解为 ≤7 个阶段，每个 ≤80 行。
- [ ] 至少一个现有节点（如 WaitNode）迁移后全部测试通过（现有测试保持不变）。
- [ ] `NodeInput` 不含凭据/日志/基础设施（节点侧上帝对象消除）；`NodePipelineContext` 属性数量不作为硬指标（管线侧可暂缓）。
- [ ] 节点消费已求值参数**无魔法字符串、无 `Dictionary<string,object>`、无手动 `EvaluateAsync`**（类型安全，读 `Script.ResolvedValue`；`GetResolved<T>()` 仅在 ResolvedValue 已填充时使用，否则回退 `EvaluateAsync`，见 §A.5.1）。
- [ ] 节点**不调用 `context.ErrorResult` / `GetParameter`（`NodeExecutionContextExtensions` 扩展方法）/ `ResolveCredentialAsync` 等索取式 API**；业务失败用 `NodeExecutionException` 抛出，由框架统一转换为 `NodeExecutionResult`（阶段四 audit 所有调用点）。
- [ ] 节点**不直接引用** `context.HttpClientPool` / `context.NodeRegistry` / `context.ContextFactory` / `context.WorkflowLoader` / `context.LlmClientFactory` / `context.ScriptCache` / `context.NestingDepth` / `context.AllowShellExecution` / `context.IsAgentInvocation` / `context.ResolvedParameters` / `context.RawParameters`（静态分析/编译期检查，违反即构建失败）。
- [ ] `OnErrorAsync` 与 `RetryExecutor` 交互有专门测试（瞬态重试耗尽→降级；不可重试错误不重试直接降级），且降级输出路由语义明确（§A.6.1）。
- [ ] 动态端口在 `ParameterHydrator` 之后用真实实例发现，有专门测试。
- [ ] 无运行时性能退化（管线 vs 当前路径，microbenchmark ≤5%）。

---

## 附录 A：关键接口契约（设计要点）

### A.1 节点处理器（节点唯一要实现的接口） `[必要性:高]`

```csharp
/// <summary>节点业务处理接口。替代 INodeType 的 ExecuteAsync 重载。节点只关注：拿输入 → 产生输出；失败用抛异常表达（见 §A.7）。</summary>
public interface INodeHandler
{
    /// <summary>执行节点业务逻辑。不负责参数校验、异常转换、路由等横切关注点。业务失败直接 throw NodeExecutionException。</summary>
    Task<NodeOutput> ExecuteAsync(NodeInput input, CancellationToken ct);
}

/// <summary>节点输入——精简视图，不含凭据/日志/基础设施。已求值参数不放在字典里：节点通过其自身属性的 Script.ResolvedValue 读取（见 NodeBase.GetResolved）。NodeInput 不提供 Required&lt;T&gt;/GetParameter 等"向 context 索取"的方法；节点失败用抛 NodeExecutionException（见 §A.7）。</summary>
public sealed class NodeInput
{
    public DataBatch InputBatch { get; }            // 当前输入批次
    public IReadOnlyDictionary<string, object?> Globals { get; }  // 运行时全局变量
    public int? ItemIndex { get; }                  // 当前迭代索引（OncePerItem 时有效）
    // 复杂节点所需引擎/LLM/流式能力经 NodeBase protected 成员（§A.6）；共享基础设施作为独立 DI 服务（§A.8）。
}
```

**为什么不用 `IReadOnlyDictionary<string, object> Parameters`？** 原方案 `input.Required<string>("Url")` 魔法字符串 + 运行时 cast，比现状 `Condition.EvaluateAsync<bool>()` **更退步**——丢失编译期类型安全。改进：节点属性经 `ParameterHydrator` 赋值后，ResolutionStage 预求值结果已写回 `Script.ResolvedValue`（现状即如此），节点直接读自身属性：

```csharp
/// <summary>Script 上的类型安全读取：返回 ResolutionStage 预求值的结果。仅在确认 Script.ResolvedValue 已由框架填充时安全使用；否则应回退到 EvaluateAsync&lt;T&gt;()。</summary>
public T GetResolved<T>(this Script script) =>
    script.ResolvedValue is T v ? v : throw new NodeParameterException(script.Name, typeof(T));

var url = Url.GetResolved<string>();   // Script 属性，已预求值（零参数；前提：ResolvedValue 已填充）
var method = Method;                    // 枚举，直接读属性
```

> 重要事实校正：`ScriptEvaluationExtensions.EvaluateAsync<T>()` **已经内置 `ResolvedValue` 短路**（`ScriptEvaluationExtensions.cs:46-49`，逐项重载 :80-83）——进入完整执行路径前会检查 `script.ResolvedValue`，命中则纯 JsonNode 取值，零引擎、零执行。因此"已求值则跳过引擎"**不是本计划的新能力**。
> `GetResolved<T>()` 的**独特价值主张**是**零参数**（无需 `NodeExecutionContext` 与 `CancellationToken`），仅在节点已确认 `ResolvedValue` 由 ResolutionStage 填充的场景下使用；若不能保证（如来自用户输入、需逐项求值），应使用 `EvaluateAsync<T>(context, ct)`，复用其内置短路。两者底层取值逻辑一致，不产生第二套。

`NodeOutput` 工厂方法（纯业务数据，框架负责包装为 `NodeExecutionResult`）：

```csharp
public sealed class NodeOutput
{
    public DataBatch Data { get; }
    public IReadOnlyDictionary<string, DataBatch>? PortOutputs { get; }
    public IReadOnlyDictionary<string, object?>? ContextChanges { get; }
    public static NodeOutput Data(DataBatch batch);
    public static NodeOutput ToPort(string portName, DataBatch batch);
    public static NodeOutput ToPorts(IReadOnlyDictionary<string, DataBatch> portOutputs);
}
```

### A.2 节点管线上下文（NodePipelineContext，替代上帝对象——仅部分） `[必要性:中（管线侧）/高（节点侧）]`

> **命名**：本对象原称 `NodeContext`，为与 `plan-node-level-context-architecture.md` 的节点级持久化 `NodeContext` 区分，改名为 `NodePipelineContext`（见 §0.1）。节点级持久化状态不在此内嵌，经 `ExecutionSession.NodeContexts[nodeId]` 访问（阶段一先以 Core/Abstractions 占位类型）。

```csharp
/// <summary>节点执行管线上下文——各 IExecutionStage 共享的数据载体，按阶段分段填充。本对象对 Stage 仍是较大的共享对象；接口隔离列为可暂缓项（§5，带触发条件）。真正"精简"的视图是节点侧 NodeInput，且节点不向本对象索取数据/服务/构造错误（§A.7）。大量"服务定位器"属性已不在本对象上——它们成为独立 DI 服务（§A.6/§A.8）。</summary>
public sealed class NodePipelineContext
{
    // ---- 阶段①：InitializeStage 填充 ----
    public NodeDefinition NodeDefinition { get; }
    public INodeType NodeType { get; }

    // ---- 阶段②：ValidationStage 填充 ----
    public List<ValidationError> ValidationErrors { get; }

    // ---- 阶段③：ResolutionStage 填充 ----
    public IReadOnlyDictionary<string, object> ResolvedParameters { get; set; }
    public ICredentialAccessor Credentials { get; set; }
    public IReadOnlyDictionary<string, object?> GlobalVariables { get; set; }
    public ILlmClient? LlmClient { get; set; }

    // ---- 阶段④：ExecutionStage 填充 ----
    public NodeOutput? HandlerOutput { get; set; }

    // ---- 阶段⑤：PostProcessStage 填充 ----
    public NodeExecutionResult? Result { get; set; }

    // ---- 跨阶段共享 ----
    public ExecutionSession Session { get; }
    public IExecutionSideEffects SideEffects { get; }

    // 脱敏快照：ResolutionStage 执行前的原始参数（见 §5.1 SecretMasker 时机）
    public IReadOnlyDictionary<string, object> RawParametersSnapshot { get; set; }
}
```

> **`ResolvedParameters` 的定位（回应"字典矛盾"）**：`ResolvedParameters` 与节点属性的 `Script.ResolvedValue` 均由 ResolutionStage 从同一来源写入，是同一求值结果的**两类投影**，不存在独立第二来源；`ResolvedParameters` 仅用于**框架阶段间协作、审计与 `SecretMasker` 脱敏**（`PersistenceStage` 需 `RawParametersSnapshot`/`ResolvedParameters`），**绝不作为节点消费路径**。节点消费一律走自身属性的 `Script.ResolvedValue`（§A.1）。若后续确认仅有 `SecretMasker` 需要该字典，可将其内联进 `PersistenceStage`，消除悬浮的"不要使用"字典。

### A.3 节点元数据声明 `[必要性:高]`

> 注：`HintAttribute` 与 `CredentialAttribute` **已存在**于 `FlowEngine.Core/Attributes/`，分别提供 `PresentationHint` 枚举（`Script`/`Expression` 等）与 `CredentialTypes`/`CredentialType` 属性。本计划**不新建二者**，迁移节点直接复用现有 API。本附录仅示例其用法，不代表需新增定义。

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class NodeMetaAttribute : Attribute
{
    public string TypeName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Icon { get; }
    public bool DefaultIsEntry { get; init; }
    public NodeMetaAttribute(string typeName, string displayName, string category, string icon);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PortAttribute : Attribute
{
    public string Name { get; }
    public string DisplayName { get; }
    public PortDirection Direction { get; }
    public PortType Type { get; }
    public PortAttribute(string name, string displayName, PortDirection direction, PortType type = PortType.Main);
}
```

### A.4 NodeBase 基类 `[必要性:高]`

```csharp
/// <summary>节点基类。提供：从 NodeMetaAttribute 自动读取元数据；从 PortAttribute 自动合成 Ports；将 INodeHandler.ExecuteAsync 适配到 INodeType.ExecuteAsync；复杂节点所需受控能力访问（protected，见 §A.6）。子类只需实现 ExecuteAsync(NodeInput, CancellationToken)。</summary>
public abstract class NodeBase : INodeType, INodeHandler
{
    public string TypeName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Icon { get; }
    public bool DefaultIsEntry { get; }
    public IReadOnlyList<PortDefinition> Ports { get; }
    public ExecutionMode ExecutionMode { get; protected set; }
    public AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor);

    public abstract Task<NodeOutput> ExecuteAsync(NodeInput input, CancellationToken ct);

    async Task<NodeExecutionResult> INodeType.ExecuteAsync(NodeExecutionContext context, CancellationToken ct);

    protected virtual Task OnExecutingAsync(NodeExecutingContext ctx, CancellationToken ct);
    protected virtual Task OnExecutedAsync(NodeExecutedContext ctx, CancellationToken ct);
    protected virtual Task<NodeOutput?> OnErrorAsync(NodeErrorContext ctx, CancellationToken ct);
    protected virtual Task OnRegisterAsync(NodeRegistrationContext ctx, CancellationToken ct);

    /// <summary>注册/UI 发现时调用：用【已 hydrate 的真实实例】生成运行时确定的端口（见 §A.4.1）。</summary>
    protected virtual IReadOnlyList<PortDefinition> GetExtraPorts();

    // 节点内部能力：protected，仅复杂节点子类使用（§A.6）
    protected IScriptEngine Engine { get; internal set; }
    protected ILlmClient? LlmClient { get; internal set; }
    protected IStreamSink? StreamSink { get; internal set; }

    /// <summary>节点级持久化状态（LoopNode 跨迭代）。引用 Core/Abstractions 中的最小 NodeContext 占位类型（跨计划共用，待 plan-node-level-context-architecture.md 落地后替换），非本对象内嵌。</summary>
    protected NodeContext NodeContext { get; internal set; }
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
        var condition = Condition.GetResolved<bool>();   // 类型安全，无字典、不向 context 索取
        return condition
            ? NodeOutput.ToPort("true", input.InputBatch)
            : NodeOutput.ToPort("false", input.InputBatch);
    }
}
```

> **端口路由说明**：`PortOutputs` 是**已存在的机制**（`NodeExecutionResult.PortOutputs`，`FilterNode.cs:94-95` 已在用），本计划是将其泛化，而非新发明。`PostProcessStage` 统一两种模式：节点返回 `PortOutputs` 则用它，否则将 `BranchIndex` 按端口列表索引映射为端口名（`OutputRouter.ResolveSourcePortName`，`OutputRouter.cs:147-160`）。迁移期间两种格式并存，管线负责转换。

**SwitchNode 示例**——动态端口通过 `GetExtraPorts()` 实现（**时机修正见 §A.4.1**）：

```csharp
[NodeMeta("switch", "Switch", "Core", "branch")]
[Port("input", "Input", PortDirection.Input)]
[Port("_default", "Default", PortDirection.Output)]
public sealed class SwitchNode : NodeBase
{
    [Required]
    [Hint(PresentationHint.Script)]
    public Script RoutingExpression { get; set; } = Script.Empty;

    [Hint(PresentationHint.SwitchCases)]
    public List<SwitchCase> Cases { get; set; } = [];

    protected override IReadOnlyList<PortDefinition> GetExtraPorts()
    {
        return Cases.Select((c, i) => new PortDefinition
        {
            Name = $"case{i}",
            DisplayName = c.DisplayName ?? c.Value,
            Direction = PortDirection.Output,
            Type = PortType.Main
        }).ToList();
    }

    public override async Task<NodeOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var value = RoutingExpression.GetResolved<string>();
        var matchIndex = Cases.FindIndex(c => c.Value == value);
        return matchIndex >= 0
            ? NodeOutput.ToPort($"case{matchIndex}", input.InputBatch)
            : NodeOutput.ToPort("_default", input.InputBatch);
    }
}
```

#### A.4.1 GetExtraPorts 的调用时机（修订） `[必要性:中]`

> 重要校正：注册中心按类型注册、`Activator.CreateInstance` 每次返回新实例（`NodeRegistry.cs:77-81`）。若 `GetExtraPorts()` 在"类型注册/UI 渲染"时用裸 `CreateInstance` 调用，`Cases` 只有空默认值 → 拿到 **0 个动态端口**。动态端口依赖**运行时 workflow 实例的参数值**，不是节点类型。因此 `GetExtraPorts()` 必须在 `ParameterHydrator` **之后、用真实 hydrate 过的实例**调用（UI 面板渲染与路由都应以"已 hydrate 的实例端口"为准）。

### A.5 参数绑定机制（Model Binding） `[必要性:高]`

#### A.5.1 现状：绑定基础设施已存在，节点消费环节断裂

| 组件 | 位置 | 已做什么 |
|------|------|---------|
| `ParameterHydrator.HydrateAsync()` | `ParameterHydrator.cs:55` | 将工作流 JSON 参数反序列化到节点实例 public 属性（跳过 `INodeType` 成员、`Ports`、`[IgnoreParameter]`） |
| `ScriptParameterPreEvaluator.PreEvaluateAsync()` | `ScriptParameterPreEvaluator.cs`（Runtime/Executor） | 对 Script 参数执行预求值，结果写回 `Script.ResolvedValue`（非平铺字典） |
| `ScriptParameterPreEvaluatorCore`（提取的核心逻辑） | `ScriptParameterPreEvaluatorCore.cs`（Core/Scripting） | 由 Runtime 包装委托；`ScriptParameterPreEvaluatorCoreTests` 覆盖 |
| `ScriptEvaluationExtensions.EvaluateAsync<T>()` 已有 `ResolvedValue` 短路 | `ScriptEvaluationExtensions.cs:46-49`（逐项重载 :80-83） | 进入完整执行前检查 `script.ResolvedValue`，命中则纯 JsonNode 取值，零引擎、零执行 |

> 排序约定（回应校验/绑定顺序风险）：**ValidationStage 在 `ParameterHydrator` 之前运行**，针对工作流定义中的**原始声明参数**做 `[Required]`/非空源码/类型校验；`ParameterHydrator` 必须对缺失的可选参数保持宽容（不抛），缺失必填由 ValidationStage 捕获。即顺序：Initialize → Validation（原始声明值）→ Resolution（Hydrate + PreEvaluate）。现有 `ParameterHydratorCoverageTests` 覆盖绑定覆盖，ValidationStage 是**叠加**的声明式校验层，不取代 Hydrator。

节点真实现状（校正）：核心流程/数据节点（If/Switch/HTTP）读自身 `Script` 属性 + `.EvaluateAsync<T>`，内部复用 `ResolvedValue` 短路（`ScriptEvaluationExtensions.cs:46-49`）；少数工具/子代理节点直接读 `context.ResolvedParameters` 或调 `GetParameter`（属迁移期待修违规，§A.8）。

#### A.5.2 ResolutionStage 桥梁（修订）

管线要做的**不是重新做绑定和求值**，而是确保预求值已发生、且节点能以类型安全方式消费——预求值结果本就在节点自身属性的 `Script.ResolvedValue` 上：

```
[C# 属性 + 特性] ←── 单一声明源
    ├─→ ParameterDiscoverer（已有）→ ParameterDefinition → 前端面板渲染
    └─→ ParameterHydrator（已有）→ 属性赋值（Script{Source}）
            └─→ ScriptParameterPreEvaluator（Runtime/Executor，已有）→ Script.ResolvedValue（已求值）
                    └─→ ResolutionStage（仅确保上述已完成，可选补充凭据/全局变量/LLM）
                            └─→ 节点 ExecuteAsync 直接读 this.Url.GetResolved<string>()（类型安全，不向 context 索取）
```

> 阶段入口选择：ResolutionStage（位于 Runtime）调用 **`ScriptParameterPreEvaluator`（`FlowEngine.Runtime/Executor/`，运行时包装）**，其内部委托 **`ScriptParameterPreEvaluatorCore`（`FlowEngine.Core/Scripting/`，已提取的核心求值逻辑）**。阶段只引用 Runtime 包装，不直接引用 Core 版本。

**结论**：更优方案是**节点读自己属性上的 `ResolvedValue`**——零字典、零魔法字符串、完全编译期类型安全，且节点体不依赖 context。ResolutionStage 主要负责非 Script 类的装配（凭据值注入、全局变量、LLM 客户端、工具图解析、安全门禁），放进 `NodePipelineContext`（Stage 用）或通过 `NodeBase` 注入，而非塞进 `NodeInput`。

#### A.5.3 迁移后的节点写法（HTTP 示例）

```csharp
[NodeMeta("httpRequest", "HTTP Request", "Core", "globe")]
[Port("input", "Input", Input)]
[Port("output", "Output", Output)]
public class HttpRequestNode : NodeBase
{
    [Required] [Hint(PresentationHint.Expression)] public Script Url { get; set; } = Script.Empty;
    public HttpMethodOption Method { get; set; } = HttpMethodOption.Get;
    [Hint(PresentationHint.Script)] public Script? Headers { get; set; }
    [Credential("apiKey", "oauth2")] public string? CredentialId { get; set; }

    public override async Task<NodeOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var url = Url.GetResolved<string>();
        var headers = Headers?.GetResolved<Dictionary<string, string>?>();
        // 节点只构造请求描述，由独立服务 HttpExecutionService 执行（含 SSRF + 凭据注入 + 重试）；永不直接引用 HttpClientPool
        var response = await HttpExecutionService.SendAsync(BuildRequest(url, Method, headers), ct);
        return NodeOutput.Data(response.ToDataBatch());
    }
}
```

**对比：**

```csharp
// ❌ 当前写法（节点自己持有求值上下文、自己 EvaluateAsync、自己调 context.ErrorResult、自己碰 HttpClientPool）
var url = context.GetParameter<Script>("Url");   // GetParameter 是 NodeExecutionContextExtensions 扩展方法，同时查 ResolvedParameters/RawParameters
if (url is null || string.IsNullOrWhiteSpace(url.Source))
    return context.ErrorResult("MissingUrl", "URL is required.");
var resolvedUrl = await url.EvaluateAsync<string>(context, ct);

// ✅ 框架写法（声明属性 + 消费已求值结果，类型安全；失败用抛异常而非 context.ErrorResult；基础设施是服务不是 context 字段）
[Required] public Script Url { get; set; }
var url = Url.GetResolved<string>();   // 无需求值上下文，无魔法字符串，不向 context 索取
```

### A.6 复杂节点的能力归属（修订新增） `[必要性:中/高]`

> 问题：流式回调、JS 引擎、LLM 客户端、HTTP 池等能力，节点必须能拿到，但不应塞进 `NodeInput`（保持纯业务视图），也不应作为"上帝 context"的字段（服务定位器）。核心原则（直接回应 `context.HttpClientPool` 之类问题）：**能力分两层归属**——
> - **节点内部能力**：与单次节点执行强绑定、仅该节点使用 → 经 `NodeBase` 的 protected 成员注入（`Engine` / `LlmClient` / `StreamSink`）。
> - **共享基础设施服务**：可被多个节点/阶段复用、由框架消费 → 作为**独立 DI 服务**，框架在 ResolutionStage 等阶段代为调用，**不暴露给节点**；节点通过调用服务方法（如 `HttpExecutionService.SendAsync`）间接使用，永远不直接引用 `HttpClientPool` / `NodeRegistry` / `ContextFactory` 等。

**独立 DI 服务（不在 context、不在 NodeBase，由框架消费）——含层归属：**

| 服务 | 取代的 context 字段 | 层归属（接口 / 实现） | 节点如何使用 |
|------|--------------------|----------------------|-------------|
| `IHttpClientPool` + `IHttpExecutionService` | `context.HttpClientPool` | `IHttpClientPool` 接口在 `Core/Abstractions`，`HttpClientPool` 实现在 `Runtime/Http`（现有）；新增 `IHttpExecutionService` 接口在 `Core/Abstractions`，实现 `HttpExecutionService` 在 `Runtime/Http`（包装 `HttpClientPool`，含 SSRF + 凭据注入 + 重试） | 节点构造 `HttpExecutionRequest` 并调 `HttpExecutionService.SendAsync`；**永不直接引用池** |
| `ICredentialService` | `context.Credentials` / `context.ResolveCredentialAsync` | 接口 `Core/Abstractions`；实现 `Infrastructure` | ResolutionStage 解析后注入凭据值；节点不调 `ResolveCredentialAsync` |
| `INodeRegistry` | `context.NodeRegistry` | 接口 `Core/Abstractions`（现有）；实现 `Runtime/Registry` | 仅供 `ToolResolver`/子工作流执行等框架服务使用，节点不引用 |
| `ISubExecutionService`（包装 `ContextFactory`） | `context.ContextFactory` | 接口 `Core/Abstractions`；实现 `Runtime`（包装 `ContextFactory`） | 子工作流/分页节点经框架服务执行，不直达工厂 |
| `IWorkflowLoader` | `context.WorkflowLoader` | 接口 `Core/Abstractions`；实现 `Infrastructure` | 子工作流加载由框架服务完成 |
| `ILlmClientFactory` | `context.LlmClientFactory` | 接口 `Core/Abstractions`；实现 `Infrastructure`（或 `Runtime`） | 仅 LLM 供给类节点经专门框架钩子获取，不暴露为普通 protected |
| `IScriptCache` / 表达式子系统 | `context.ScriptCache` | `Core/Scripting` 内部（引擎使用）；节点不可见 | 引擎内部使用，节点不可见 |
| `IShellExecutionGate` | `context.AllowShellExecution` / `context.IsAgentInvocation` | 接口 `Core/Abstractions`；实现 `Infrastructure` | 框架据配置 + 角色在阶段门禁，节点不读这两个开关 |
| `IRecursionGuard` | `context.NestingDepth` | 接口 `Core/Abstractions`；实现 `Runtime` | 框架递归深度保护，节点不评估 |
| `IWorkflowMemoryService` | `context.Memory` | 接口 `Core/Abstractions`；实现 `Infrastructure` | 跨节点共享状态作为服务，不挂在每个节点 context 上 |
| `ILogger` / 结构化日志 sink | `context.Logger` / `context.EngineLogger` | 框架日志基础设施 | 节点不自己 Log；框架统一记录 |

> 一般规则：接口定义在 `Core/Abstractions`，实现在 `Infrastructure`；实现若需依赖 Runtime 类型（如 `HttpClientPool` 已在 `Runtime/Http`、`ScriptCache` 在 `Core/Scripting`、`IRecursionGuard` 在 `Runtime`），则实现就近放置，不强行下沉。这样 `NodeInput` 保持纯业务数据视图，`NodeBase` 只承载真正节点内部的能力，其余全是框架服务——彻底消除 context 作为服务定位器。工具图解析（`ToolResolver`）、子工作流加载也属此类框架服务，节点（如 `AgentNode`）**不应自己扫描工作流图**（现状 `AgentNode.CollectTools` 重复 `ToolResolver`，应改为调用框架服务）。

### A.7 最小上下文原则（节点不向 context 索取） `[必要性:高]`

1. **节点只读三类东西**：自身声明属性（静态配置 + ResolutionStage 回填的求值结果，如 `Url.GetResolved<string>()`）；统一 `NodeInput`（InputBatch / Globals / ItemIndex）；复杂节点经 `NodeBase` 的 protected 成员获取**节点内部**能力（引擎/LLM/流式，§A.6）。
2. **节点不向 context 索取**：以下成员**从节点可用面移除**——`context.GetParameter<T>()`（即 `NodeExecutionContextExtensions.GetParameter` 扩展方法，`NodeExecutionContextExtensions.cs:20`，同时查询 `ResolvedParameters`/`RawParameters`）、`context.ErrorResult(...)`、`context.ResolveCredentialAsync()`、`context.HttpClientPool`、`context.NodeRegistry`、`context.ContextFactory`、`context.LlmClientFactory`、`context.WorkflowLoader`、`context.NestingDepth`、`context.AllowShellExecution`、`context.IsAgentInvocation`、`context.ResolvedParameters` / `context.RawParameters` 等。节点体里不应出现对这些成员的数据获取、服务引用或错误构造调用——共享基础设施通过独立 DI 服务由框架消费（§A.6/§A.8）。
   - **禁令范围（回应点 3）**：`GetParameter` 是 `NodeExecutionContext` 的**扩展方法**而非实例方法，同时查 `ResolvedParameters` 与 `RawParameters`。**禁令覆盖该扩展方法**：仅允许在 `INodeType` → `NodeBase` 的迁移**适配层**中使用（适配壳需要以旧方式读参数回填），**`NodeBase` 子类及所有新节点禁止调用**。阶段四须逐一 audit 每个调用点（现有 `CalculatorToolNode`/`SubAgentToolNode`/`PaginateNode` 等），迁移后从节点代码移除。
3. **失败用"抛"而非"返回错误结果"**：节点遇业务失败直接 `throw new NodeExecutionException(code, message)`；由 `ExecutionStage`/`PostProcessStage` 统一捕获并转换为统一的 `NodeExecutionResult`，与本项目 `backend-code-rules.md` §9 一致。
4. **与 ValidationStage 配合**：`[Required]` 等声明式校验在阶段②（且早于 Hydrator）短路，节点内连对应的 `throw` 都不需要写。

> 认知模型收敛为：**声明属性 + 读 `NodeInput` + 返回 `NodeOutput` 或抛 `NodeExecutionException`**。没有"上帝 context"要记忆，也没有散落的 `try-catch` / `ErrorResult` 拼装。

```csharp
/// <summary>节点业务失败。由框架统一转换为 NodeExecutionResult，节点只负责抛出。</summary>
public sealed class NodeExecutionException : DomainException
{
    public string ErrorCode { get; }
    public NodeExecutionException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
}
```

### A.8 框架不该做的事（保持节点业务） `[必要性:中]`

框架"上移"横切关注点时，必须明确边界——以下逻辑**不是框架该做的**，应留在节点或作为节点的业务决策：

1. **核心业务变换必须留在节点**：合并算法（MergeNode）、结构化过滤匹配（FilterNode）、JS 代码执行（JSNode）、条件求值（IfNode/SwitchNode）、循环窗口算法（LoopNode）、Agent 工具编排循环——这些是节点的存在理由。
2. **框架提供能力，节点做业务决策**：
   - HTTP：节点仍构造请求描述（`HttpExecutionRequest`：URL/方法/鉴权/Body/`SuccessWhen`），由独立服务 `HttpExecutionService` 执行（含 SSRF + 凭据注入 + 重试）；节点**不碰 `HttpClientPool`**。
   - LLM：节点决定 prompt/消息/迭代；框架解析并提供 `LlmClient`。
   - DB：节点提供 SQL/参数，由 `DbExecutor` 服务执行（现状已是如此）。
3. **不要过度上移**：不要把 LoopNode 的状态机、FilterNode 的匹配逻辑搬进 Stage；框架只负责节点级 `NodeContext` 持久化与 OncePerItem 循环驱动。不要由框架构造 AgentNode 的结果 DTO——仅"异常→NodeExecutionResult"转换归框架。

> 总结边界：**横切关注点（校验/求值/重试/路由/脱敏/安全门禁/工具图解析）上移框架；业务变换与请求构造留在节点；共享基础设施作为独立 DI 服务由框架消费，不暴露给节点。**

---

## 附录 B：行业对比（n8n）与现状核查

### B.1 n8n 架构结论

**n8n 没有中间件管线概念。** 节点自由度极高，但也付出代价：

| 维度 | n8n | 我们的现状 |
|------|-----|-----------|
| 节点接口 | `INodeType` 纯空接口，无预定义方法；执行经 `IExecuteFunctions` 回调 | `INodeType.ExecuteAsync` 已定义框架接口，但节点仍需手动处理横切关注点 |
| 执行引擎 | `WorkflowExecute.run()` ~2874 行 | `NodeProcessor.ProcessAsync()` ~256 行，已比 n8n 轻 |
| 参数绑定 | 节点手动 `this.getNodeParameter("field")` | 节点用属性 + `.EvaluateAsync` 手动求值 |
| 凭据解析 | 节点手动 `await this.getCredentials("type")` | 节点手动 `context.ResolveCredentialAsync()` |
| 执行上下文 | `IExecuteFunctions` 40+ 属性上帝接口 | `NodeExecutionContext` 28 属性上帝对象 |
| OncePerItem | n8n 没有框架级 OncePerItem，节点自行循环 | 我们有 `ExecutionMode`，但 JSNode 自建平行 `CodeMode` 循环 |

> n8n 的 `IExecuteFunctions` 同样是上帝对象，本计划的 `NodePipelineContext` 也较大——不宜用"n8n 是反面教材"的口吻。n8n 节点自由是生态可扩展性的**刻意权衡**，我们作为内部产品追求标准化合理，但不必否定其取舍。

### B.2 关键源码位置（n8n）

```
packages/workflow/src/Interfaces.ts
  → INodeType（line 2089）：纯空接口，无方法签名
  → IExecuteFunctions（line 1108）：40+ 属性
  → INodeProperties（line 1804）：参数描述 schema
packages/core/src/ExecutionEngine/WorkflowExecute.ts
  → run()（~2874 行）：n8n 的"执行管线"，揉合所有逻辑
```

### B.3 对我们的启发

| 发现 | 启发 | 必要性 |
|------|------|--------|
| n8n 没有校验中间件，各节点校验写法不一致 | 我们的 `ValidationStage` 方向正确——统一校验，消除重复 if-guard | 🔴 高 |
| n8n 没有参数绑定，每个节点手动 getNodeParameter | 我们的属性声明 + ResolutionStage 自动求值，将节点从"主动获取"变"被动接收" | 🔴 高 |
| n8n 的 `IExecuteFunctions` 是上帝对象 | 我们向接口隔离演进（`NodeInput` 精简视图 + 最小上下文原则） | 🔴 高（仅节点侧） |
| n8n 的 `INodeType` 自由度极高但无约束 | 我们的 `NodeBase` 基类 + 生命周期钩子在"自由度"和"约束"间更平衡 | 🟡 中 |
| n8n 节点可访问工作流上下文任何数据 | 这条自由度有合理性——保留一条"逃生口"（§5.2 双路径治理） | 🟡 中 |

### B.4 关键洞察：属性声明驱动 UI + 运行时

n8n 核心模式：`INodeProperties[]` 一次声明，同时驱动 UI 渲染和参数绑定。我们的 C# 属性 + 特性已等价于这个模式，且绑定基础设施已经做完（`ParameterDiscoverer` / `ParameterHydrator` / `ScriptParameterPreEvaluator` 均完整工作），只是节点"消费"环节断了。真正要解决的是让节点轻松、类型安全地消费已求值结果，且不再向 context 索取或自构错误（§A.7）。

### B.5 阶段设计（取代"中间件"抽象） `[必要性:中]`

阶段用 `IExecutionStage` + `Func<Task> next` 表达前后衔接，但**不宣称是 ASP.NET 风格中间件**：(1) 七阶段强制有序、不可由节点随意增删；(2) 短路需求（校验/装配失败跳到持久化）在标准 `next` 链里表达不自然。驱动器显式处理短路（§B.5.3）。

```csharp
public interface IExecutionStage
{
    Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct);
}

public sealed class NodePipeline
{
    private readonly IReadOnlyList<IExecutionStage> _stages;
    public async Task<NodeResult> RunAsync(NodeWorkItem item, ExecutionSession session, IExecutionSideEffects sideEffects, CancellationToken ct)
    {
        var context = new NodePipelineContext(item, session, sideEffects);
        await RunStagesAsync(context, ct);   // 驱动器：任一阶段设置 context.Result 即短路到 PersistenceStage
        return context.Result;
    }
}
```

**各阶段职责：**

| 阶段 | 职责 | 当前散落在哪 | 必要性 |
|------|------|-------------|--------|
| `InitializeStage` | NodePipelineContext 生命周期；非回边清空旧状态、回边保留；环路失控保护 | `NodeProcessor` L78-119 | 🔴 高 |
| `ValidationStage` | 扫描 `[Required]`、Script 非空源码、类型约束；构造 `ValidationErrors`（**在 Hydrator 之前**，见 §A.5.1） | 各节点 if-guard | 🔴 高 |
| `ResolutionStage` | 确保 Script 预求值（写 `ResolvedValue`，经 `ScriptParameterPreEvaluator`）；凭据解析为值注入；全局变量装配；LLM 客户端解析；工具图解析（`ToolResolver`）与子工作流加载作为框架服务；安全开关门禁（`IShellExecutionGate`） | `NodeExecutionContextFactory` + `ResolveLlmClientForNode` + `ToolResolver` | 🔴 高 |
| `ExecutionStage` | 协调 OncePerItem 循环 → 调 `INodeHandler.ExecuteAsync`；`RetryExecutor` 包裹；节点异常统一捕获转换；`OnErrorAsync` 降级；JS 引擎生命周期 | `NodeProcessor` L130-205 + `RetryExecutor` | 🔴 高 |
| `PostProcessStage` | `SuccessWhen` 后置检查（泛化）；节点抛出 `NodeExecutionException` → ErrorResult 兜底；OncePerItem 累积；输出限流 | HTTP `SuccessWhen` + `CapRetainedOutput` + 各节点 catch | 🟡 中 |
| `RoutingStage` | 按 `PortOutputs`/`BranchIndex` 路由；等待区聚合 | `OutputRouter.RouteOutputsAsync` | 🟡 中 |
| `PersistenceStage` | 构建并脱敏 `NodeExecutionRecord`；事件发布 | `NodeProcessor` L207-251 + `SecretMasker` | 🔴 高 |

**短路机制（修订） `[必要性:高]`**：`ValidationStage` / `ResolutionStage` / `RoutingStage` 失败时，置 `context.Result` 并**由管线驱动器跳过中间阶段、直达 `PersistenceStage`**。

```csharp
public sealed class ValidationStage : IExecutionStage
{
    public async Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        var errors = Validate(context.NodeType, context.NodeDefinition);
        if (errors.Count > 0)
        {
            context.ValidationErrors = errors;
            context.Result = BuildValidationErrorResult(errors);
            return;   // 不调用 next —— 驱动器检测到 context.Result != null 即短路到 PersistenceStage
        }
        await next();
    }
}
```

驱动器伪代码：

```csharp
foreach (var stage in _stages)
{
    await stage.RunAsync(context, next: () => NextStage(context, ct), ct);
    if (context.Result is not null && stage is not PersistenceStage)
        break;   // 短路：跳过剩余阶段，由 PersistenceStage 收尾
}
```

### B.6 OnErrorAsync 与 RetryExecutor 的顺序（明确化） `[必要性:高（正确性）]`

1. `ExecutionStage` 调用 `RetryExecutor.ExecuteNodeWithRetryAsync`（已有，按可重试错误码过滤 + 退避，`RetryExecutor.cs:154-161`）。
2. **瞬态失败**：RetryExecutor 内部重试；重试**耗尽后仍失败**才进入降级路径。
3. **不可重试错误（如 HTTP 404 / 业务校验失败）**：RetryExecutor 直接判定非重试，立即进入降级路径，**不重试**。
4. 进入降级路径时：若节点 override 了 `OnErrorAsync` 且返回非 null `NodeOutput` → 以降级输出替代 `ErrorResult`；返回 null → 走 `RetryExecutor` 已生成的 `NodeError` → 默认 `ErrorResult`。
5. 兜底异常转换（任何未被 `OnErrorAsync` 接住的异常）→ `PostProcessStage` 统一转 `ErrorResult`。

**降级输出的语义（回应点 4）**：`OnErrorAsync` 返回的非 null `NodeOutput` 视为**正常成功输出**，由 `RoutingStage` **像普通结果一样路由到下游**（不隐藏、不特判下游连接）。区别仅体现在可观测性——`NodeExecutionRecord` 与执行事件会记录其**降级来源（原始异常）**，使监控/审计能区分降级结果；**不在 `NodeOutput` 上增加 `IsDegraded` 标记**（保持 `NodeOutput` 纯粹，避免下游误用）。若业务要求下游能识别降级，由节点自行在输出 `DataBatch` 中携带标记字段。这样 `PostProcessStage` 不会因为节点返回了数据而非错误，就"假装"从未失败——记录层（非路由层）保留了失败痕迹。

> 约束：`OnErrorAsync` 与 `RetryExecutor` 的错误信息不得重复包装；`RetryExecutor` 负责"重试策略 + 最终错误对象"，`OnErrorAsync` 仅负责"是否降级为有效输出"。节点本身**不调用 `context.ErrorResult`**——要么返回 `NodeOutput`，要么 `throw NodeExecutionException`，由框架转换。节点超时统一由 `RetryExecutor` 处理，不自行创建 `CancellationTokenSource`。

### B.7 迁移策略补充

| 阶段 | 内容 | 影响 | 必要性 |
|------|------|------|--------|
| 阶段一：管线框架搭建 | 实现 `NodePipeline`/`IExecutionStage`/`NodePipelineContext`/`NodeBase`；定义 Core/Abstractions 占位 `NodeContext`；不改现有节点 | 新抽象存在但不影响运行 | 🟡 中 |
| 阶段二：NodeProcessor 拆解 | 将 `ProcessAsync` 按职责提取为阶段；保留 `INodeType` 作为 ExecutionStage 适配目标 | 行为不变，内部重构 | 🟡 中 |
| 阶段三：样板集中消除 | 添加 `RequiredAttribute`（新建），审核接续 `HintAttribute`/`CredentialAttribute`（现有）；提供 `NodeBase` 适配器；引入独立 DI 服务（含层归属） | 现有节点不变，新建节点用新写法 | 🔴 高 |
| 阶段四：逐节点迁移 | 从简单节点开始迁移到 `NodeBase`，修掉 `GetParameter`/`ResolvedParameters`/`HttpClientPool` 等违规；现有测试保持不变 | 旧 `INodeType` 保留期间可共存 | 🔴 高 |
| 阶段五：上帝对象分解 | 将 `NodeExecutionContext` 过剩属性分散或仅保留节点侧；接口隔离可暂缓（触发条件见 §5.2） | 只影响内部管线 | ⚪ 低/暂缓 |

**双路径治理（修订：建议强制 NodeBase） `[必要性:高（治理）]`**：阶段四后强制 `NodeBase`，旧 `INodeType` 退化为 `NodeBase` 适配层（而非并存执行路径）——双路径 = 双倍测试/维护；逃生口节点若绕过敏验/后处理，长期更易出 bug；`NodeBase` 已适配 `INodeType`（§A.4），旧接口无需作为"另一条执行路径"存在。少数节点确需脱离框架约束，应通过 §A.6 的 protected 通道获取能力，而非绕过管线。

**迁移优先级**：`WaitNode` → `MergeNode` → `IfNode`/`SwitchNode`/`FilterNode` → `LoopNode` → `JSNode`/`AgentNode`/`HttpRequestNode`（风险由低到高）。

**NodeExecutionContextFactory 逐步替换**：~181 行（`NodeExecutionContextFactory.cs:41-222`）职责与阶段高度重叠，但**不是一次性替换**：阶段一通过工厂适配器运行（风险最低）；阶段三随独立 DI 服务落地，从工厂产出物移除服务定位器字段；阶段四～五工厂精简为轻量适配器（仅 `ParameterHydrator` + 轻量 `NodeExecutionContext`），随后标记 deprecated。约束：每个迁移步骤保持 `dotnet build` + 现有测试全部通过。

---

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
|------|--------|----------|------------|
| 2026-07-25 | Agent | 由 `docs/designs/2026-07-25-execution-pipeline-refactoring.md` 提升为模块计划，管线共享上下文改名 `NodePipelineContext` | 设计→计划提升 |
| 2026-07-25 | Agent | 评审修订：移除 HintAttribute/CredentialAttribute 作为新建（改用现有）；明确 EvaluateAsync 已有 ResolvedValue 短路；GetParameter 禁令范围（扩展方法、仅适配层可用）；降级输出语义；ResolvedParameters 框架内定位；占位 NodeContext；DI 服务层归属；迁移测试策略；校验早于 Hydrator；PreEvaluator 双类；接口隔离触发条件；去掉开头元描述 blockquote | 用户评审 12 点 |
