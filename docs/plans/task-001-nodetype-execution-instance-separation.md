# 任务：INodeType 类型实例与执行实例分离 + 执行能力注入实施

## 目标

按设计文档 `docs/designs/2026-07-26-nodetype-execution-instance-separation.md` 落地：
1. 消除 SubExecutionService 复用调用方实例的潜伏竞争（主路径已由 Get→TryGet→Activator.CreateInstance 每调用克隆隔离，无需改）。
2. ExecutionMode 可声明配置。
3. 统一移除 NodeBase 上的能力成员与方法 helper，改用单 `[Inject]` 特性按类型从 DI 容器 + 运行上下文注入。
4. 参数模型绑定：typed property 作为已解析+已强转+已校验唯一直源。
5. 不重写现有插件节点逻辑；节点只加 `[Inject]` 声明与读 typed property。

## 待完成项（对应设计文档 §5 步骤）

- [x] 步骤 1：`_metaCache` 静态缓存（NodeBase 构造函数反射去重）
- [x] 步骤 2：`SubExecutionService` 改每运行实例 + 过渡 `BindServices` + `GetAll()` 合规分析器规则
- [x] 步骤 3：`[NodeMeta]` 增加 `ExecutionMode`，`NodeBase` 改读 `_meta.ExecutionMode`
- [x] 步骤 4：`[Inject]` 特性 + 共享 `NodeCapabilityInjector`（DI + 运行上下文两源）
- [x] 步骤 5：方法 helper 下沉（EvaluateItemAsync/GetCredentialAsync/GuardSsrf/TryParseJson/CreateChildContextAsync）
- [x] 步骤 6：逐节点迁移 `[Inject]` 声明（随步骤 4 一并完成）
- [x] 步骤 7：参数模型绑定（`[Range]`/`[Required]`，移除 `GetResolvedParameter`/`GetRawParameter`/`CoerceInt`，保留 `ReadResolvedParameter` 窄 API）
- [x] 步骤 8：并发回归测试
- [x] 步骤 9：并发反向护栏测试（先于改动落地）

## 完成标准

- `dotnet build` 通过；`dotnet test` 全量通过。
- 步骤 9 护栏测试在任何改动前即应通过，作为回归护栏。
- `NodeBase` 上无节点面向能力成员、无方法 helper（仅类型级元数据 + 抽象 `ExecuteAsync` + 生命周期钩子 + 私有注入例程）。
- `[Inject]` 按属性类型声明；`ILlmClient`/`IExecutionLogger`/`ICredentialAccessor` **必须来自 `NodeExecutionContext`**（每运行/每节点），不能走 DI。
- `NodeApiComplianceAnalyzer` 拦截对 `GetAll()` 返回值调用 `ExecuteAsync`。
- 现有插件节点改动仅限于加 `[Inject]` 声明与读 typed property。

## 关键约束（来自评审，已代码核实）

- `ILlmClient`：`ExecutionStage.cs:97` 按节点解析写入 `ctx.LlmClient`；DI 仅注册 `ILlmClientFactory`，故 `sp.GetService<ILlmClient>()` 为 null。
- `IExecutionLogger`：`ctx.Logger` 是 `FlowEngine.IExecutionLogger`，≠ DI 的 `ILogger<NodeBase>`。
- `ICredentialAccessor`：`ctx.Credentials` 已被 `CredentialAuditAccessor` 包裹，走 DI 基础访问器会绕过凭据审计。
- `Get()` 返回每调用新克隆（并发安全），`GetAll()` 返回非克隆共享单例（真竞争点）。

## 主要修改记录

- 步骤 1（DONE）：`NodeBase` 构造函数改读 `_metaCache`（按 `Type` 缓存 `[NodeMeta]`+端口），新增静态 `BuildMeta`/`BuildPortsFromAttributes`；新增 `tests/FlowEngine.Core.Tests/NodeBaseMetadataCacheTests.cs`（5 测试通过）。评审 Approved。
- 步骤 2（DONE）：`SubExecutionService.ExecuteSubAsync` 改经 `nodeRegistry.CreateInstance(nodeType.TypeName)` 取每运行实例，对 `NodeBase` 实例过渡调用 `BindServices(GetService<IHttpExecutionService>(), this, GetService<IToolResolver>())`；`Get()`/`GetAll()` 加"禁止用于执行"注释；`NodeApiComplianceAnalyzer` 新增 FE0002 拦截 `GetAll().ExecuteAsync(...)`（抽出 `DetectsGetAllExecuteAsync`）；新增 `tests/FlowEngine.Infrastructure.Tests/SubExecutionServiceTests.cs`（3 测试）+ 合规测试（8 测试）。全量 `dotnet build` 通过，零警告。评审 Approved。
- 步骤 3（DONE）：`NodeMetaAttribute` 新增 `ExecutionMode` 字段（默认 OnceForAll）；`NodeBase.INodeType.ExecutionMode` 改读 `_meta.ExecutionMode`；新增 `OncePerItemTestNode` 与 2 个测试（声明 OncePerItem → InitializeStage 算 RunCount==2）。评审 Approved。
- 步骤 4+6（DONE）：新增 `[Inject]` 特性与静态 `NodeCapabilityInjector`（ctx 六型 + DI 两源解析，sp 为 null 时安全跳过）；`ExecutionStage`/`SubExecutionService` 调 `Inject` 取代 `BindServices`，`ResolutionStage` 移除 `BindServices`；`NodeBase` 剥离全部能力属性（`Ctx`/`Logger`/`LlmClient`/`Registry`/`NodeContext`/`Http`/`Sub`/`Tools` 等），改为各节点自声明 `[Inject]`（约 29 个插件节点）；适配器新增 `InjectCapabilities(context)` 兜底供直接执行/测试路径等价注入。全量 `dotnet build` 0 错误，`dotnet test` 全绿（Core 710/Runtime 931/Infra 102/App 502/Host 374）。评审 Approved（遗留：适配器双重注入开销小；Required-DI 能力在适配器路径为潜伏点，当前无节点使用）。
- 步骤 5（DONE）：从 `NodeBase` 移除全部 11 个方法 helper（`EvaluateItemAsync<T>`/`EvaluateItemAsync`(ScriptResult)/`EvaluateContextAsync<T>`/`GetCredentialAsync`/`GuardSsrf`/`TryParseJson<T>`/`CreateChildContextAsync`/`IsInvokedByAgent`/`ShellExecutionEnabled`/`NestingLevel`/`LoadWorkflowAsync`），仅保留参数读取三件套（`GetRawParameter`/`GetResolvedParameter`/`ReadResolvedParameter`）。各插件节点按映射改经 `[Inject]` 能力：脚本求值→`Ctx`+`Script.EvaluateAsync<T>(Ctx, item:, itemIndex:, cancellationToken:)`；凭据→`[Inject] ICredentialAccessor Creds`+新增默认接口方法 `Creds.ResolveAsync(idOrName, ct)`；SSRF→`Ctx.GuardSsrf(url)`；JSON→新增 `FlowEngine.Core.JsonHelper.TryParse<T>`；子上下文→`[Inject] INodeExecutionContextFactory Factory`+`Factory.CreateAsync(...)`；上下文访问器→`Ctx.IsAgentInvocation`/`Ctx.AllowShellExecution`/`Ctx.NestingDepth`/`Ctx.WorkflowLoader.LoadAsync`。测试 SP `NodeTestContextFactory` 补 `.AddSingleton<INodeExecutionContextFactory>(factory)`。配套修复（编译/测试通过后落地）：① `NodeApiComplianceAnalyzer` 与 `NodeApiComplianceTests` 的禁用标识符集合移除 `IsAgentInvocation`/`AllowShellExecution`/`NestingDepth`/`WorkflowLoader`（经 `[Inject] NodeExecutionContext Ctx` 显式 opt-in 后已合法）；② `NodeCapabilityInjector.ContextProviders` 新增 `INodeExecutionContextFactory → ctx.ContextFactory`，使 DI 缺失时（直接执行/测试路径）仍能从 `context.ContextFactory` 取得工厂，与迁移前 `_rawContext.ContextFactory` 行为一致。`dotnet build` 0 错误 0 警告；`dotnet test` 全绿（Core 710/Infra 102/Runtime 931/App 502/Host 374，与迁移前基线完全一致）。

### 步骤 5 两处有意偏离设计文档（已记录）

- **偏离 a**：`ScriptEvaluationExtensions` 扩展方法签名保持基于 `NodeExecutionContext`（未改为 `JsEngine`）。脚本求值调用统一为 `Script.EvaluateAsync<T>(Ctx, item:, itemIndex:, cancellationToken:)`，沿用既有重载（逐项重载 item 必填 + 额外全局重载）以保证无 item 调用无歧义。
- **偏离 b**：SSRF 防护使用 `Ctx.GuardSsrf(url)`（扩展方法），未新增 `ISsrfGuard` 抽象；`Ctx.GuardSsrf` 原本即不在合规分析器禁用标识符集合内，无合规冲突。

### 步骤 5 编译期修复（非设计文档显式列出，但为修复全部编译错误所必需）

- **FE0001 禁用列表收敛**：设计目标 `Ctx.IsAgentInvocation`/`Ctx.AllowShellExecution`/`Ctx.NestingDepth`/`Ctx.WorkflowLoader` 在 `[Inject] NodeExecutionContext Ctx` opt-in 后属合法访问，从分析器与单测的禁用标识符集合移除；其余索取式 API（`GetParameter`/`ErrorResult`/`HttpClientPool`/`NodeRegistry`/`ContextFactory`/`LlmClientFactory`/`ScriptCache`/`ResolveCredentialAsync`/`ResolvedParameters`/`RawParameters`）保留。
- **`INodeExecutionContextFactory` 上下文源注入**：`NodeCapabilityInjector` 新增该类型→`ctx.ContextFactory` 映射，确保测试（仅设 `context.ContextFactory`）与直接执行路径不再因 DI 容器缺工厂而 NRE；生产路径 `NodeExecutionContextFactory.CreateAsync` 本就置 `ContextFactory = this`，行为不变。
- **15 处逐项求值调用改具名实参**：`ScriptEvaluationExtensions.EvaluateAsync<T>` 第 5 参为 `globals`、第 6 参为 `cancellationToken`；原先 `script.EvaluateAsync<T>(Ctx, item, itemIndex, ct)` 把 `ct` 误绑到 `globals`，全部改为 `item:`/`itemIndex:`/`cancellationToken:` 具名实参，唯一绑定逐项重载。
- **`SubWorkflowToolNode` 空引用保护**：`Ctx.WorkflowLoader` 为可空，按迁移前 `LoadWorkflowAsync` 语义（loader 为空则返回 null，随后抛 `WorkflowNotFound`），改为 `Ctx.WorkflowLoader is null ? null : await Ctx.WorkflowLoader.LoadAsync(...)`。

### 步骤 7（DONE）

- `ParameterHydrator.HydrateAsync` 在写入 typed property 前增加绑定期校验：数值类型（int/long/double/float 及可空）按 `[Range]` clamp 到 [Minimum, Maximum]；`[Required]` 仅记 warning 不抛异常（保持现有节点行为优先）。新增 `ClampToRange`/`TryConvertNumeric`/`IsMissingForRequired` 辅助方法（`using System.ComponentModel.DataAnnotations`）。
- `NodeBase` 删除 `GetRawParameter`/`GetResolvedParameter` 两个 protected 方法，并连带删除已无引用的 `_rawContext` 字段与赋值（适配层 `INodeType.ExecuteAsync` 现仅 `_rawContext` 置值去掉，直接调用 `InjectCapabilities(context)`）。**保留** 静态 `ReadResolvedParameter(ctx, key)` 作为跨上下文窄 API（§4.3 允许，偏离 §5 步骤 7 的"一并移除"）。
- 节点改造：`SubAgentToolNode` 新增 `MaxIterations` 属性并给 `MaxNestingDepth`/`MaxIterations`/`MemoryWindowSize` 加 `[Range]`，删除 `ResolveMaxNestingDepth`/`ResolveMaxIterations`/`ResolveMemoryEnabled`/`ResolveMemoryWindowSize`/`CoerceInt`，调用点内联为直接读属性；`SubWorkflowToolNode` 给 `MaxNestingDepth` 加 `[Range]` 并删除 `ResolveMaxNestingDepth`/`CoerceInt`；`WebSearchToolNode` 新增 `Query` 属性并改读 `Query`；`PaginateNode` 新增 `NextCursorPath`/`ItemsPath`/`TerminateWhen`/`CredentialName` 属性，将 `GetConfig(...)` 全部改为读 typed property 并删除 `GetConfig`，保留三处 `ReadResolvedParameter(iterContext, ...)`。
- 测试：`PaginateNodeTests` 四个用例改为直接设置 typed property（测试不经 hydrator）；新增 `ParameterHydratorTests` 的 2 个 `[Range]` clamp 单测（`RangeNode` 测试类）。`dotnet build` 0 错误 0 警告；`dotnet test` 全绿（Core 710/Infra 102/Runtime 933/App 502/Host 374；Runtime 较基线 +2 为 clamp 测试）。

### 步骤 7 偏离设计文档（已记录）

- **偏离 c**：§5 步骤 7 要求移除 `ReadResolvedParameter`，但 §4.3 明确其为跨上下文窄口径 API（`PaginateNode` 从 `iterContext` 读 `url`/`bodyExpression`/`method` 合法），故保留 `ReadResolvedParameter`，仅移除 `GetResolvedParameter`/`GetRawParameter`。

### 步骤 9（DONE，先于步骤 8 落地的反向护栏）

- **InitializeStage 反向护栏**（新增 `tests/FlowEngine.Runtime.Tests/Execution/Stages/InitializeStageTests.cs::RunAsync_ParallelSameType_ReturnsDistinctNodeTypeInstances`）：并行对同一注册表类型执行 `InitializeStage`，断言两次 `context.NodeType` 引用相异。该测试专门防止有人基于"Get 返回共享单例"的错误前提把 `Get()` 改成返回 `_instances` 单例——一旦退化即失败。
- **SubExecutionService 反向护栏补充**（新增 `tests/FlowEngine.Infrastructure.Tests/SubExecutionServiceTests.cs::ExecuteSubAsync_ParallelCalls_InstancesDistinctFromCaller`）：并行两次 `ExecuteSubAsync` 传入同一调用方实例，断言两次子执行实例彼此相异、且均 ≠ 调用方实例（`callerNode.WasExecuted` 为 false）。与步骤 2 已有的 `UsesDistinctInstance_NotCallerInstance`/`ParallelCalls_UseIndependentInstances` 共筑"子执行不复用调用方实例"护栏。

### 步骤 8（DONE，并发回归）

- 新增 `tests/FlowEngine.Runtime.Tests/Execution/ConcurrencyIsolationTests.cs`，复用真实 `NodeExecutionContextFactory` + `NodeCapabilityInjector`（即 `ExecutionStage` 在 `contextFactory.CreateAsync` 之后建立的注入契约），对同类型节点并行构造两独立运行：
  - `ParallelSameType_IsolatedContextEngineCredentialsParameters`：断言实例/上下文/JsEngine/凭据访问器（`[Inject] ICredentialAccessor` 各自指向本运行的 `credentialAccessorOverride`）/类型化参数（`Label` "A" vs "B" 不串改）均按运行独立。
  - `ParallelSameType_EnginesIndependentlyDisposable`：释放 A 上下文的 JsEngine 不影响 B 的引擎，重新创建的 A 引擎与 B 互异，证每上下文引擎生命周期独立。
- 两项测试均真正并行启动两次 `CreateAsync`（`Task.WhenAll` 前发起），暴露任何共享静态状态缺陷。`dotnet test` 全绿（Runtime 8 + Infra 4 相关用例通过；整仓基线未受影响）。

## 全分支 Code Review（requesting-code-review）

- 复审结果（只读，未提交）：无 Critical 阻断项；规格 1–6 全部满足，`NodeBase` 仅余静态 `ReadResolvedParameter`，`[Inject]` 双源注入、`ExecutionMode` 可配、`SubExecutionService` 每运行实例、`GetAll()` 的 FE0002 拦截、参数模型绑定均到位。
- **Important 修复（PaginateNode.maxPages 回归）**：旧 `GetConfig("maxPages", …)` 带 `mp > 0 ? mp : MaxPages` 兜底，迁移后 `var maxPages = MaxPages` 丢失该兜底——非正值（如 0）会直接产生 0 页空结果。按步骤 7「typed property + `[Range]` 校验」原则，给 `PaginateNode.MaxPages` 加 `[Range(1, int.MaxValue)]`（补 `using System.ComponentModel.DataAnnotations`），由 `ParameterHydrator` 钳制到 ≥1。已核对全插件 diff 仅此一处丢弃 `>0` 兜底；`dotnet build` 0 警告；`PaginateNodeTests` 4 例全过。
- **复核误报（NodeContext 复制）**：`NodeCapabilityInjector` 的 `new NodeContext(ctx.NodeContext)` 包裹的是**同一 `State` 字典引用**，`BatchSplitNode`/`LoopNode` 经 `.State` 写入均回写共享字典，无丢失；非回归。
- **已知非阻断项（维持原设计）**：① FE0002 仅做内联式语法拦截（中间变量形式不覆盖），设计文档已定性为软护栏；② `NodeBase.INodeType.ExecuteAsync` 适配器保留 `InjectCapabilities` 兜底供直接执行/测试路径，无害。`dotnet test` 全量复跑通过（Runtime 936 / Infra 103）。
