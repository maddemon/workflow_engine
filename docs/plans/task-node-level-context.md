# 任务：节点级持久化上下文（Node-Level Persistent Context）

## 目标

为 Flow Engine 运行时实现节点级持久化上下文机制，使 LoopNode 等有状态节点能跨迭代保持状态，并为未来有状态节点（PaginateNode / 聚合节点 / 批量处理节点）提供通用基础设施。同时支持在节点 body 表达式中通过 `$nodeContext.xxx` 读写上下文。

本任务依据 `docs/plans/plan-node-level-context-architecture.md`，并执行了针对该计划的两轮审查修正（见「主要修改记录」）。

## 待完成项

### 基础设施
- [x] **Task 1** — `ExecutionSession` 新增 `NodeContexts: ConcurrentDictionary<string, IDictionary<string, object?>>`（按 `node.Id` 隔离，OrdinalIgnoreCase）。
- [x] **Task 2** — `NodeExecutionContext` 新增 `NodeContext` 属性（默认空字典）。
- [x] **Task 3** — `INodeExecutionContextFactory.CreateAsync` 新增 `nodeContext` 参数（位于 `extraGlobals` 之后，单一签名；`NodeExecutionContextFactory` 同步），null 时创建空字典。
- [x] **Task 4 + Task 9（合并改动 `ProcessNodeAsync`）** — 内核从 `session.NodeContexts` `GetOrAdd` 取/建上下文并传入工厂；按回边判定重置（见下）。

### 工具与节点
- [x] **Task 5** — 新增 `NodeContextExtensions`：强类型 `Get/Set/TryGet/GetOrAdd<T>`（`where T : class`）+ **非泛型 `GetValue`/`SetValue`**（覆盖 int/double/bool 等值类型）。
- [x] **Task 6** — `LoopNode` 改造为真正迭代器：用 `initialized` 标志 + 存储 `allItems`/`position`/`processedItems`；Done 端口（`BranchIndex = 1`）输出累积处理结果，Loop 端口（`BranchIndex = 0`）输出当前窗口。「新上游输入即重新开始」由内核非回边激活清空上下文实现，无需节点内开关（`ResetIndex` 已废弃移除）。

### 表达式与重置（审查重点修正）
- [x] **Task 8（关键修正）** — `$nodeContext` 注入到 `context.GlobalVariables`（`BuildBase` 返回后、`return` 前，同一实例引用），**不是**工厂临时 `js`/`globals`；只要 `nodeContext != null` 即注入（不要求非空）。补充 Jint 数值回写为 `double` 的类型约定。
- [x] **Task 9（关键修正）** — 上下文重置改用**回边检测**：会话初始化时 `CycleDetector.ComputeBackEdges(workflow.Connections)` 计算回边集存入 `session.FeedbackEdgeKeys`；`NodeWorkItem` 增加 `bool IsFeedbackActivation`，由 `RouteOutputsAsync` 标记；`ProcessNodeAsync` 仅对非回边激活 `TryRemove` 旧上下文。**禁止使用 `SourceNodeId == node.Id`**（真实回环来源是下游节点，会误清空打断循环）。

### 测试
- [x] **Task 7** — 集成测试：Loop(batchSize=2) → Process → 回连 Loop，5 项输入，验证 Loop 共 4 次（1 次初始化 + 3 次回环）/ Process 3 次、Done 含全部 5 项处理结果。
- [x] **§5.4 测试策略全覆盖** — 含 `GetValue`/`SetValue` 值类型、节点 body 表达式可见性、JS→C# `double` 回写、回边复用/非回边重置、`SubWorkflowExecutor` 隔离。

## 完成标准

1. LoopNode 能正确迭代处理所有输入项目。
2. 节点上下文在同一工作流执行内跨调用保持。
3. 不同节点有独立的上下文，互不干扰。
4. 在**节点 body 表达式**中可通过 `$nodeContext.xxx` 读写节点自身上下文（验证点在运行期引擎，而非仅参数预求值）。
5. 新上游输入（非回边激活）进入节点时，内核清空并重建节点上下文实现「重新开始」；原 `ResetIndex` 开关已废弃移除（恒定 `true` 曾在回环中导致死循环）。
6. 非回边（新上游输入）路径进入节点时重置上下文，回边（环路继续）路径复用旧上下文（Task 9）。
7. `SuccessfulOutputs` 无条件累积节点每次成功输出（供下游 `$node.<name>` / `$items(<name>)` 读取）。BranchIndex 仅标识输出端口，曾误加的 `BranchIndex != 0` 守卫会静默丢弃 IfNode 的 true 分支 / SwitchNode 的 case 0 等合法输出，已移除（详见下方修改记录）。
8. 现有测试全部通过。
9. 新增测试覆盖所有场景（含上述审查修正点）。
10. 无性能回退。

## 完成状态

- [x] Task 1
- [x] Task 2
- [x] Task 3
- [x] Task 4 + Task 9
- [x] Task 5
- [x] Task 6
- [x] Task 7
- [x] Task 8
- [x] §5.4 测试策略

## 主要修改记录

- 依据 `plan-node-level-context-architecture.md` 实施；该计划经审查发现并修正以下问题（已回写计划文档）：
  - **C1 / S1**：原 `$nodeContext` 注入点（工厂临时 `js` + `globals`）不会流入节点运行期引擎，且 `Count > 0` 守卫导致首次迭代无法播种 → 改为注入 `context.GlobalVariables` 同一实例、非 null 即注入。
  - **C2**：原回环判定 `item.SourceNodeId == node.Id` 在真实回环中恒为 false，会清空上下文打断循环 → 改为回边检测（`CycleDetector` + `IsFeedbackActivation`）。
  - **S3 / S4**：补充值类型支持（非泛型 `GetValue`/`SetValue`）与 Jint `double` 回写约定。
  - **M1 / M2**：统一 `nodeContext` 参数位置；Task 4 与 Task 9 合并改动 `ProcessNodeAsync`。
  - **§1.1 / §5.2**：原 O(N?) 重复数据前提不成立（窗口互不相交、线性无重复，累积为 O(N)）；据此误加的 `BranchIndex != 0` 守卫（"避免暴露中间窗口"）会静默丢弃 BranchIndex = 0 的合法输出（IfNode true / SwitchNode case 0），已在第二轮审查中移除，恢复 `SuccessfulOutputs` 无条件累积（与计划文档「回环拓扑」的 O(N) 描述一致）。
- 测试编写阶段补充修正（第一轮）：
  - 集成测试 `LoopIntegrationTests` 参数键由 `BatchSize`（PascalCase）改为 `batchSize`（camelCase），以匹配 `ParameterDiscoverer` 下发的描述符参数名；否则 `MergeParameters` 回退到默认值 `BatchSize=1`，导致 Loop 执行 6 次而非预期的 4 次。
  - `INodeExecutionContextFactory.CreateAsync` 新增 `nodeContext` 参数后，`tests/FlowEngine.Core.Tests/ToolContextFactoryExtraTests.cs` 的 `FakeContextFactory` 实现同步补齐该参数，修复 CS0535 编译错误。
- 第二轮审查（用户提交，6 项）修正：
  - **Critical（死循环）**：`LoopNode` 的 `ResetIndex` 开关在真实回环中导致死循环——每次回环激活经 `ParameterHydrator` 从工作流定义重新水合为 `true`、节点自身从不清除，永远走不到 Done 端口。已移除 `|| ResetIndex` 分支（恒为 `true` 曾在回环中致命）。因内核「非回边激活即清空上下文」已提供"新上游输入即重新开始"，该开关冗余，故标记为已废弃（`[Obsolete]`/注释）保留以兼容既有定义，不再产生效果。
  - **position double 兜底**：`EmitNextWindow` 读取 `position` 改为 `switch { int / double / _ }`，兼容节点 body 表达式经 Jint 写回字典的 `double` 值（已通过 `NodeExecutionContextFactoryTests.BodyExpression_CanReadWrite_NodeContext_WithDoubleRoundTrip` 验证），避免静默归零导致重新迭代。新增 `LoopNodeTests.ExecuteAsync_PositionStoredAsDouble_ContinuesIteration` 回归测试。
  - **环路失控保护**：`WorkflowSchedulerKernel` 新增 `ExecutionSession.FeedbackActivationCounts` 计数 + `EngineDefaultsOptions.MaxCycleIterations`（默认 10000，0/负表示不限制）；反馈激活累计超限转 `Failed`（错误码 `CycleLimitExceeded`）。新增 `CycleLimitTests`（自环节点超限失败 + 正常 Loop 在限内完成）。
  - **拓扑约束与语义文档化**：`LoopNode` XML 文档明确「仅单反馈边」约束（多路扇出后各自回连会导致 `position` 推进与累积顺序错乱，需配合聚合节点 WaitingArea）与「Done 输出为下游回流窗口累积、未必等于原始输入全集」的语义。
  - **BranchIndex != 0 守卫移除（跨节点正确性）**：`WorkflowSchedulerKernel.ProcessNodeAsync` 写入 `SuccessfulOutputs` 的 `BranchIndex != 0` 守卫为上一轮错误 O(N?) 分析的产物，会静默丢弃 BranchIndex = 0 的合法输出（IfNode 的 true 分支、SwitchNode 的 case 0），已移除恢复无条件写入；同步更新 `LoopIntegrationTests` 断言（现 `SuccessfulOutputs["loop"]` 含 3 次中间窗口 + Done 共 10 项）。
- 全部测试通过：Core 651 / Infrastructure 98 / Runtime 656 / Application 473 / Host 322，0 失败；`dotnet build` 0 警告 0 错误。
- 按项目规范，本任务不主动提交代码，完成后发起 SubAgent Code Review（以本任务文档与计划文档为依据）。
