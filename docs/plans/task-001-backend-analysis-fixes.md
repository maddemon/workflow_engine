# 任务：后端分析报告问题修复（task-001-backend-analysis-fixes）

## 目标
修复对后端代码审查发现的 11 个问题（性能 / 正确性 / 并发安全），覆盖执行引擎、脚本引擎、子工作流、合并节点、数据访问层。

## 全局约束（所有子任务必须遵守）
- 后端代码规范见 `.agents/rules/backend-code-rules.md`；测试框架 **xUnit v3**。
- 采用 **TDD**：先写失败测试，再实现至通过；每个修复配套回归测试（正常路径 + 边界 + 异常）。
- 不得引入全局静态锁影响多流程并行（并发锁必须作用域在 JsEngine 实例内，见 Task 1）。
- 公共类/方法需 `///` XML 注释；不随意吞异常；禁止 `Console.WriteLine`（用 `ILogger<T>`）。
- 不主动提交（除非明确要求）；每个子任务独立 commit。
- 命名 `{方法名}_{场景}_{预期结果}`；覆盖 JsonElement↔string/枚举/null 类型转换与空值边界。

## 任务依赖与顺序
Task 1、2、4、9、10、11 互相独立；Task 3、5、7、8 为集成/架构类，按编号执行。

---

## Task 1 — Jint 引擎线程安全（高）
- 问题：`JsEngine` 封装的 Jint `Engine` 非线程安全。同一引擎实例被并发驱动（如节点 `OncePerItem` 对多个 item 用 `Task.WhenAll` 调 `EvaluatePrepared`/`RunForItemAsync`）会状态损坏、结果错乱。
- 位置：`backend/FlowEngine.Core/Scripting/JsEngine.cs`（所有 `_engine.Evaluate*` 方法）、`backend/FlowEngine.Core/Scripting/PreparedScriptSession.cs`。
- 修复：在 `JsEngine` 实例内加 `SemaphoreSlim(1,1)`，对 `Evaluate`/`Run`/`RunAsync`/`EvaluatePrepared` 做串行化（锁作用域仅本实例，绝不做静态全局锁，否则会拖垮多流程并行）。`Dispose` 时释放信号量。
- 验收：
  - 新增测试：两个线程并发调同一 `JsEngine` 的 `EvaluatePrepared`/`Run`，结果均正确且不抛异常（模拟竞态）。
  - 已有脚本/表达式测试全部通过。
  - 不同 `JsEngine` 实例并发执行互不影响（可附并发计数验证）。

## Task 2 — 脚本路径大整数精度丢失（高）
- 问题：`ScriptResult.ToClr()`/`ToJson()`/`To<T>` 对整数仅在 int 范围转 `(int)`，否则一律 `double`，导致大整数（如数据库 ID `9007199254740993`）精度丢失。`ParameterResolver.ResolveNumber` 已正确优先 `long`/`decimal`，脚本路径未对齐。
- 位置：`backend/FlowEngine.Core/Scripting/ScriptResult.cs`（搜索 `ToClr`/`ToJson`/`To<` 中整数分支）。
- 修复：整数分支先用 `long`（`TryGetInt64`），超出范围再 `decimal`；`ToJson` 用 `JsonValue.Create((long)...)` 而非 `AsNumber()` 的 double。
- 验收：
  - 测试：脚本返回 `9007199254740993` 经 `ScriptResult` 转换后，CLR 值为 `long` 且等于原值（非 double 近似）；`ToJson` 输出为整数而非 `9.007199254740993e15`。
  - 普通 int / double 行为不变。

## Task 3 — 子工作流参数水合与分支路由（高）
- 问题 A：`SubWorkflowExecutor` 用反射 `GetProperties`+`SetValue` 做参数水合，且 `catch {}` 静默吞掉所有转换错误；`ConvertParameterValue` 只处理 `JsonElement→bool`，数值参数落到 `Convert.ChangeType` 失败返回 `null`，与主运行时 `ParameterHydrator` 行为不一致。
- 问题 B：子工作流分支路由 `connectionsBySource` 用原始 `c.SourcePortName` 作键，但查询用 `ResolveSourcePortName`（解析为 true/false/case 名）；主运行时 `ExecutionSession` 会先把空 `SourcePortName` 解析为实际输出端口名，子工作流不解析，导致 If/Switch 空 `SourcePortName` 的连接命中不到、下游收不到数据。
- 位置：`plugins/FlowEngine.Plugins.Standard/.../SubWorkflowExecutor.cs`（参数水合 ~335-382，分支路由 ~43-44 与 ~275-276）。
- 修复：
  - A：注入复用主运行时的参数水合逻辑（如 `ParameterHydrator`）替换反射路径；移除 `catch {}` 改为记录并抛出/归一化错误。
  - B：构造 lookup 键时复用与主运行时一致的端口名解析（空 `SourcePortName` 解析为实际输出端口名）。
- 验收：
  - 测试：子工作流传入数值参数，水合后子节点收到正确 `long`/`int` 值（非 null）。
  - 测试：If/Switch 节点输出端口 `SourcePortName` 为空时，子工作流下游仍能收到数据。
  - 参数转换异常被记录（非静默吞掉）。

## Task 4 — MergeNode 透传分项 Success/Error（中）
- 问题：`MergeNode` 新建 `DataItem` 时写死 `Success = true`，丢弃输入项的 `Success`/`Error`，失败项被伪装成成功。
- 位置：`plugins/FlowEngine.Plugins.Standard/.../MergeNode.cs`（MergeAppend ~56-62、~75-95、~153-172）。注意 `MergeByPosition` ~132-151 已正确保留状态，参照它。
- 修复：透传源项的 `Success`/`Error`，而非写死 `true`。
- 验收：
  - 测试：输入含一个 `Success=false` 的项，合并后对应输出项 `Success=false` 且 `Error` 保留。
  - 全部成功时行为不变。

## Task 5 — 引擎复用与跨 item 全局泄漏（中）
- 问题 A（性能）：`NodeExecutionContextFactory.CreateAsync` 在 `OncePerItem` 循环中对每个输入项各调一次 `JsEngine.Create`，且每次重建完整 globals、把上游 `successfulOutputs` 逐个 `ToList()` 克隆，开销成倍放大（已确认 `GetOrCreateEngine` 按节点执行缓存单引擎，应复用而非每次新建）。
- 问题 B（正确性）：`PreparedScriptSession.RunForItemAsync` 复用同一引擎时，前序 item 经 `globalThis.x=1` 写入的全局状态残留污染后续 item（`strict` 模式只拦截 `foo=1`，拦不住 `globalThis.` 写入）。
- 位置：`backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs`、`backend/FlowEngine.Core/Scripting/PreparedScriptSession.cs`、`backend/FlowEngine.Core/Scripting/ExecutionScope.cs`。
- 修复：
  - A：确保 `OncePerItem` 全程复用 `GetOrCreateEngine()` 返回的同一引擎（不每次 `Create`）；如当前已复用则消除冗余的全局表全量克隆。
  - B：每次 item 求值前保存/恢复全局对象快照，或删除上次注入的额外全局键，避免跨 item 泄漏。
- 验收：
  - 测试：同一会话串行跑两个 item，item1 写入 `globalThis.__leak=1`，item2 不应读到该值（除非显式注入）。
  - 性能：对同一引擎多次 item 求值不再每次重建全局表（可用计数器验证 Create 调用次数）。

## Task 6 — SuccessfulOutputs/LatestBatches 按 node.Id 累积并设上限（中）
- 问题：按 `node.Name` 作键跨整个运行 `Concat` 累积所有成功输出，未配置上限时内存无界增长，且同名节点会串数据。
- 位置：`backend/FlowEngine.Runtime/Executor/ExecutionStage.cs`（~209/219-222/227）。
- 修复：改用 `node.Id` 作键；默认启用 `MaxRetainedOutputItems` 上限（截断旧数据）；下游只读快照。
- 验收：
  - 测试：两个同名不同 Id 的节点各自输出，互不被覆盖。
  - 测试：超过上限后旧输出被截断，内存有界。

## Task 7 — 跨 DbContext 作用域共享实体（中）
- 问题：`WorkflowExecutionWorker` 把请求作用域 `AsNoTracking` 加载的 `item.PreloadedWorkflow` 传入执行作用域的 `executor.ExecuteLoopAsync(..., dbContext, ...)`，实体与执行作用域 DbContext 分属不同 ChangeTracker，内核若 `SaveChanges` 会触发重复插入/Detached 异常。
- 位置：`backend/FlowEngine.Host/Executor/WorkflowExecutionWorker.cs`（~118-135）、`backend/FlowEngine.Application/.../ExecutionService.cs`（~41）。
- 修复：工作项只传 `WorkflowDefinitionId`，在执行作用域内统一重新加载（参照 else 分支已这样做）。
- 验收：
  - 测试/验证：执行路径不再跨作用域复用同一个实体实例；已有执行集成测试通过。

## Task 8 — 缺失乐观并发令牌（中-高，需谨慎）
- 问题：`Entity` 基类只有 `Id/CreatedAt/UpdatedAt/Deleted`，全仓无并发令牌；并发 `UpdateAsync` 会静默后写覆盖。
- 位置：`backend/FlowEngine.Core/Entities/Entity.cs`、`backend/FlowEngine.Migrations/`。
- 修复：在**高竞争实体**（`Workflow`/`Project`/`Credential`）加 `byte[] RowVersion` 配 `[Timestamp]`（不强行改动 `Entity` 基类以免影响全表迁移，除非评估后必要）；生成对应 EF 迁移。
- 验收：
  - 迁移可生成并 `dotnet build` 通过。
  - 测试：并发更新同一实体时，后写者收到 `DbUpdateConcurrencyException`（或等价乐观并发失败）。

## Task 9 — CredentialService 无分页（低）
- 问题：`GetAllAsync` 直接 `ToListAsync()` 返回全部凭据并逐条 `DecryptFields`，无分页上限，与已分页的 `WorkflowService` 不一致。
- 位置：`backend/FlowEngine.Application/Credentials/CredentialService.cs`（~152-165）。
- 修复：沿用 `PagedResult` + `Skip/Take` 分页；只读查询保持 `AsNoTracking`。
- 验收：
  - 测试：返回 `PagedResult`，分页参数生效；与 `WorkflowService` 行为一致。

## Task 10 — Poll 去重游标在部分失败时前移（低）
- 问题：`PollTriggerJob` 循环内单个 `engine.StartAsync` 失败被 per-item `catch` 吞掉，但 `LastPollId`/`LastPollTime` 仍基于全部 `newItems` 整体前移，导致失败项在下一轮被永久跳过。
- 位置：`backend/FlowEngine.Host/Jobs/PollTriggerJob.cs`（~238, ~247）。
- 修复：仅把成功触发项的游标写入 `updatedSettings`，或记录失败项以待重试。
- 验收：
  - 测试：部分 item 触发失败时，去重游标不跳过失败项（下一轮重试）。

## Task 11 — JsEngine.RunAsync 硬编码超时（低）
- 问题：`JsEngine.RunAsync` 硬编码 `CancelAfter(5000)`，忽略 `JsEngineOptions.ExecutionTimeoutMs`（其余路径用 `Options.TimeoutInterval` 一致）。
- 位置：`backend/FlowEngine.Core/Scripting/JsEngine.cs`（~154）。
- 修复：读取 `opts.ExecutionTimeoutMs`（需把 `JsEngineOptions` 存入实例字段，构造时传入）。
- 验收：
  - 测试：配置不同 `ExecutionTimeoutMs`，`RunAsync` 超时阈值随之变化（可用快超时 + 长脚本验证触发 `TimeoutException`）。

## 完成标准
- 每个 Task 有配套 xUnit 测试且通过；`dotnet build` 全仓库通过。
- 每个 Task 经 task reviewer 审查（spec + 质量）通过。
- 最终全分支 code review 通过。
