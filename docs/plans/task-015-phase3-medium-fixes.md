# 任务：Phase 3 中优先级缺陷修复（代码审查）

## 目标
执行 `docs/plans/plan-code-review-fixes.md` 的 Phase 3（Medium 级别）修复，覆盖 API 一致性、性能瓶颈与前后端对齐问题。本任务持续进行，按批次派发子 agent 实现。

## 待完成项（按批次）

### 批次 A（低风险、无需迁移、高确定性）—— 已完成
- [x] #13 错误响应格式统一：`RbacAuthorizationMiddleware` 返回统一包络 `{ success:false, errorCode:"Forbidden", message, details:null }`，HTTP 403 不变
- [x] #14 HSTS 安全头：`SecurityHeadersMiddleware` 非开发环境（`!IsDevelopment()`）添加 `Strict-Transport-Security: max-age=31536000; includeSubDomains`
- [x] #15 删除操作权限统一：`FilesController` 删除端点 `[AuthorizePermission(Scope.File, Operation.Delete)]`
- [x] #16 分页参数校验：`WorkflowsController.GetAll` 对 `page` 加 `[Range(1, int.MaxValue)]`、`pageSize` 加 `[Range(1, 200)]`，非法值经 `[ApiController]` 自动返回 400
- [x] #17 ConfigureAwait 补充：`GlobalExceptionHandlerMiddleware` `WriteAsync(...)` 后追加 `.ConfigureAwait(false)`
- [x] #18 SSE 心跳异常观察：`SseController.RunHeartbeatAsync` 新增 `catch (Exception ex)` 经 `ILogger` 记录心跳异常，不再静默忽略
- [x] #19 AuditEvents JSON 转换优化：`AuditEventsController` 用 `JsonNode.Parse(doc.RootElement.GetRawText())` 替代手写递归转换，输出结构语义不变
- [x] #22 正则静态缓存：`ParameterResolver` 将 `TryExtractMissingName` 中两处字面量正则提取为 `private static readonly Regex`（带 `Compiled`）

### 批次 B（中等风险、无迁移）—— 已实现 6/11 项（commit 见主要修改记录）
- [x] #5 清理/删除改用 `ExecuteDeleteAsync`：`ExecutionCleanupService` 关系型路径批量删除（InMemory 退化为按 ID 加载），不再物化整行
- [x] #7 `ParameterResolver` 数值精度（`long`/金额不丢精度）：新增 `ResolveNumber`，优先 `TryGetInt64`→`TryGetDecimal`→`GetDouble` 兜底
- [x] #10 幂等可能返回"假成功"：`ExecutionService` 改用 `TryGetExistingAsync` 查真实执行；命中但记录缺失视为未完成、继续真实执行；以真实 executionId 接管幂等键
- [x] #21 SSRF 防护增强：`SsrfGuard.CreateConnectCallback` 在连接瞬间再次校验实际 IP（防 DNS 重绑定），`IsInternalTarget` 降为尽力预检
- [x] #24 `TriggerService` 事务补充：关系型路径下 `CreateAsync` 显式事务；`RemoveTriggersByWorkflowIdAsync` 改 `ExecuteDeleteAsync` 且不开启嵌套事务
- [x] #25 `WaitingArea` 锁粒度：`PortState` 改 `ConcurrentDictionary` + `AddOrUpdate` 纯函数 `Merge`，移除手动 `lock`
- [x] #2 热查询列补索引（`FlowEngineDbContext`），已生成 EF 迁移 `AddHotQueryIndexes` —— 批次 B2 第一部分完成
- [x] #4 / #6 单次执行重复加载同一工作流（透传 Workflow/编译定义）—— 批次 B2 第二部分完成
- [x] #20 `WorkflowExecutionWorker` scope 优化（per-item scope 解析 `WorkflowExecutor`）—— 批次 B2 第一部分完成
- [x] #23 统计/授权批量查询优化（防 N+1）—— 批次 B2 第一部分完成（经核实已批量，仅补锁定测试）
- [x] #26 前端其他类型/选择器优化 —— 批次 B2 第一部分完成（经核实类型已对齐，仅 `ParameterPanel` 选择器/回调稳定性优化）

### 批次 B2（剩余中等项，已派发第一部分）
- [x] #2 热查询列补索引（EF 迁移 `AddHotQueryIndexes`）
- [x] #4 / #6 单次执行重复加载同一工作流（透传）
- [x] #20 `WorkflowExecutionWorker` scope 优化（与 #27 合并）
- [x] #23 统计/授权批量查询优化（防 N+1）
- [x] #26 前端其他类型/选择器优化（与 C #11/#12 前端部分合并）

### 批次 C（高风险、需迁移/大改）—— 已确认方案，开始实施
- [x] #27 `WorkflowExecutionWorker` scope（与 #20 合并，已在 B2 第一部分完成）
- [x] #1 执行器每节点 JSON 写放大 —— **已确认：批量化（不迁表）**。commit `80f105c`。节点记录仍存 `NodeRecords` JSON 列，仅在终态或每约 25 个节点 flush 一次 `SaveChangesAsync`，写库由 O(N²) 降到约 O(N)；`PersistFailedStateAsync` 与内核调用传播真实 `CancellationToken`（修正 `CancellationToken.None`）。无 EF 迁移、低风险。
- [x] #3 `FindReferencingCredentialAsync` 全表加载 —— **已确认：归一化关联表**。commit `147035f`。新建 `workflow_credential_usages` 表 + `credential_id` 索引，在 `DbContext.SaveChangesAsync` 中集中维护（覆盖创建/更新/导入/删除），按 `credential_id` 在 SQL 内查询；需一次迁移回填。可移植、符合规则。
- [x] #8 `SubWorkflowExecutor` 多输入丢数据 —— **已确认：进程内合并 + Core 预求值**。commit `dab86a4`。抽 Core 级 `Merge`/`ScriptParameterPreEvaluator` 助手，子流程执行器合并多入边输入（所有必需输入就绪才执行）、执行前预求值 `Script` 参数，保持进程内执行（轻量）。不引用 Runtime。
- [x] #9 凭据解密忽略 `KeyVersion` —— **低风险**。commit `eee81b4`。
- [x] #11 前端重渲染与状态管理 —— **已确认：抽 `canvasStore`（治本）**。commit `ab96d7a`。新建 Canvas 模块 store 承载 nodes/edges/positions/选中/执行覆盖层等高频状态，全局 `workflowStore` 仅留元数据（符合 frontend-code-rules §5.1）；`CustomNode` 移除每节点 O(N×E) 的 `edges.filter`，改由 `WorkflowCanvas` 一次性 O(E) 计算 `nodeId→Set<handleId>` 经 `ConnectedHandlesContext` 下发；`ExecutionPanel` 用 `useShallow` 稳定派生 id→name 映射。改动较大但有新增单测保障。
- [x] #12 前后端契约补充 —— **低风险类型对齐**。commit `64dfbe1`。`ExecutionStatus` 前端联合类型补齐 9 个枚举值；`executeWorkflow` 支持传入 `inputs` 与 `idempotencyKey`（POST body）；前端 `ExecutionDto.error` 为死字段（后端无此字段、错误经 WS 下发），删除之；`PortInstance` 保持前端权威（后端仅 Name/Direction/Type）。后端 `ExecutionsController.Execute` 已消费 `dto?.Inputs` 与 `dto?.IdempotencyKey`，端到端契约对齐。

## 完成标准
- 每批次 items 修复，先写失败测试（TDD）再实现
- 后端 `dotnet build` + `dotnet test` 通过；前端涉及项 `npm run build` + `npm run typecheck` + 单测通过
- 以计划文档与任务文档为依据自审，无回归
- 代码遵循 `backend-code-rules.md` / `frontend-code-rules.md`
- 不主动创建无关文件；不擅自修改无关代码

## 完成状态
- [x] 批次 A（8/8）
- [x] 批次 B（6/11，剩余 5 项归入批次 B2）
- [x] 批次 B2（第一部分 4/4 完成，第二部分 #4/#6 完成）
- [x] 批次 C（12/12：#27/#1/#8/#3/#9/#11/#12 全部完成）

## 主要修改记录
- 批次 A（8 项）全部完成，遵循 TDD：先写/调整失败测试再实现。commit `9d72754`。
- 验证：`dotnet build FlowEngine.sln` 通过（0 警告 0 错误）；`FlowEngine.Host.Tests` 54 通过、`FlowEngine.Runtime.Tests`(ParameterResolver) 23 通过、`FlowEngine.Application.Tests` 458 通过。
- 实现差异说明：#13 包络 `details` 取 `null`（与计划一致，区别于全局异常的 `traceId` 对象）；#19 因 `JsonNode.FromElement` 非 STJ 公共 API，改用等价的 `JsonNode.Parse(GetRawText())`；#22 的 `s_guidRegex`/`s_functionCallRegex` 此前已是静态只读，本次将 `TryExtractMissingName` 内两处 `Regex.Match` 字面量也提取为静态字段。
- 批次 B（6 项）全部完成，遵循 TDD：先写失败测试再实现。`ExecutionIdempotencyService` 已有 `TryGetExistingAsync`/`TryGetOrRegisterAsync` 可直接复用。验证：`dotnet build` 0 警告 0 错误；`FlowEngine.Core.Tests` 647、`FlowEngine.Runtime.Tests` 626、`FlowEngine.Application.Tests` 460，全部通过、0 失败。差异说明：#5/#24 的删除/清理在 InMemory 提供程序下退化为按 ID 加载后删除（仅测试路径），关系型下用 `ExecuteDeleteAsync`；#21 `IsInternalTarget` 保留为尽力预检，权威防护移至 `CreateConnectCallback` 连接瞬间校验。
- B1（#10 幂等并发重复执行窗口）按评审修复：原 P3 为修「假成功」移除抢占，引入并发重复执行窗口。现于 `StartAsync` 前用 `TryGetOrRegisterAsync` 以唯一约束抢占幂等键（夺胜者启动并把键更新为真实执行 id；落败者不启动，经 `WaitForRealExecutionAsync` 轮询复用胜者真实结果，绝不返回合成对象）。并补关系型并发测试（`ExecutionServiceConcurrencyTests`：文件型 SQLite + 真实 `ExecutionIdempotencyService` + 引擎侧信号解耦抢占/启动两阶段，断言 `engine.StartAsync` 恰好 1 次且两请求返回同一真实结果；另含 InMemory 控制流锁定测试）。修复中发现并修正 `ExecutionIdempotencyService.TryGetExistingAsync` 缺少 `AsNoTracking` 导致落败者上下文被旧 tracked 实体遮蔽、无法观察到胜者将键更新为真实执行 id 而误判超时重复启动，已加 `AsNoTracking`。验证：`dotnet build` 0 警告 0 错误；`FlowEngine.Application.Tests` 462 全绿（含新增 2 例）。
- 批次 B2 第一部分（#23、#26、#2、#20）完成，遵循 TDD：先写/调整失败测试再实现。详情：
  - **#23 统计/授权批量查询防 N+1**：经核实 `WorkflowService.GetAllAsync` 已通过 `WorkflowStatisticsLoader.LoadAsync(workflowIds)` 批量 `Where(e => workflowIds.Contains(e.WorkflowDefinitionId)).GroupBy(...)` 加载，`ResourceAuthorizationService` 仅 `ResolveProjectAsync`（单资源，无 N+1 批量入口），即原计划引用的 `ResolveProjectsAsync` 并不存在。结论：代码无需改动，仅补锁定测试 `WorkflowServiceCrudTests.GetAllAsync_BatchesStatistics_PopulatesPerWorkflowStats` 断言每工作流的触发数/末次执行时间/下次触发时间正确。
  - **#26 前端类型/选择器优化**：经核实 `NodeDefinition.retryPolicy/timeout`、`ProjectDto.createdBy` 已与后端对齐（`Project.CreatedBy` 为 `string` 非 `Guid`），`LayoutContext` 仅 `{navbar, aside}` 无需改造。实际改动：移除 `ParameterPanel` 中对 `selectedNodeId` 的冗余订阅，将 `handleParameterChange` 改为从 `useWorkflowStore.getState()` 读取最新 `selectedNodeId/nodes/updateNodeParameters`，配 `useCallback([],)`，引用稳定，避免 `selectedNode` 每次渲染变化导致的非必要重渲染。验证：`npm run typecheck` 0 错误、`npm run build` 成功。
  - **#2 热查询列补索引**：为 `ExecutionRecord` 增加 `[Index(nameof(WorkflowDefinitionId))]`、`[Index(nameof(ProjectId))]`、`[Index(nameof(Status), nameof(CompletedAt))]`（复合），为 `Workflow` 增加 `[Index(nameof(ProjectId))]`；均用 Data Annotations（符合规则）。已通过 `dotnet ef migrations add AddHotQueryIndexes` 生成迁移（命名空间 `FlowEngine.Migrations.Migrations.Sqlite`，与既有布局一致），无需 fallback 任务文档注记。
  - **#20 `WorkflowExecutionWorker` scope 优化**：将 `WorkflowExecutor` 与 `FlowEngineDbContext` 的解析从外层长生命周期 scope 移入每个执行项内部的 `using var executionScope = _scopeFactory.CreateScope()`，使其 scoped `DbContext` 随 scope 释放，避免长生命周期 scope 捕获 `DbContext` 导致跨执行数据污染。新增回归测试 `WorkflowExecutionWorkerScopeTests.Execute_SequentialItems_ResolveIndependentScopedDbContexts`，断言两个连续执行项各自解析出独立 `DbContext` 实例（非同一引用）。运行路径行为不变。
  - 验证：`dotnet build FlowEngine.Host` 0 警告 0 错误；`dotnet test FlowEngine.Application.Tests` 463 通过、`dotnet test FlowEngine.Host.Tests` 321 通过（均 0 失败）；前端 `typecheck`/`build` 通过。偏差：#23、#26 经核实无需按原计划改代码（已对齐/已批量），仅以测试锁定现状与做一处安全的渲染稳定性优化，未扩大范围。
- 批次 B2 第二部分（#6 重复加载透传、#4 执行列表分页）完成，遵循 TDD：
  - **#6 单次执行重复加载同一工作流**：单次执行请求此前对 `Workflow` 实体读取 3 次（`ExecutionService` 鉴权读、`WorkflowExecutor.StartAsync` 重载读、`WorkflowExecutionWorker` 后台读）。新增 `IEngine.StartAsync(Guid, Workflow, object?, CancellationToken)` 重载与 `WorkflowExecutionWorkItem.PreloadedWorkflow` 字段（4 参带默认值，旧 3 参调用与 Moq 测试均不受影响），将 `ExecutionService` 已加载的 `Workflow` 透传至 `StartAsync`→队列→`Worker`，消除后两次 DB 往返；两者在 `Id` 不匹配时回退内部加载。队列为内存 `System.Threading.Channels`，按引用持有工作项，携带实体引用安全。新增测试 `WorkflowExecutorTests.StartAsync_WithPreloadedWorkflow_EnqueuesItemCarryingWorkflow`（含 `Id` 不匹配回退用例）、`WorkflowExecutionWorkerScopeTests.Execute_WithPreloadedWorkflow_ReusesInstanceWithoutRequery`。
  - **#4 执行列表分页 + 独立运行中端点**：`ExecutionService.GetByWorkflowAsync` 改为服务端分页，返回 `PagedResult<ExecutionSummaryDto>`，支持 `status`/`page`/`pageSize`（`page≥1`、`pageSize` 收敛 1–200，`AsNoTracking`）；新增 `GetActiveAsync` 仅返回 `Pending`/`Running` 活跃执行。`ExecutionsController` 对应更新 `GetByWorkflow` 查询参数并新增 `GetActive` 端点（`workflows/{id}/executions/active`，无路由冲突）。前端 `getWorkflowExecutions` 改为接受 `ExecutionQuery` 并返回 `PagedResult`，新增 `getActiveExecutions`；`ExecutionHistoryPage` 由客户端分页切换为服务端分页（`useRequest` + `refreshDeps:[id,page,statusFilter]`，移除客户端过滤/`slice` memo）；`useExecution` 实时跟踪改用 `getActiveExecutions`。补充 `ExecutionServicePagingTests`（分页数、TotalPages、次页、状态过滤、GetActiveAsync）。验证：`dotnet build FlowEngine.sln` 0 警告 0 错误；`FlowEngine.Application.Tests` 465、`FlowEngine.Host.Tests` 322 全绿；前端 `typecheck`/`build` 成功、`vitest run` 440 全绿。偏差：用户经 AskUserQuestion 选择「全分页+独立运行中端点」方案，故未采用「后端封顶不改契约」的简化方案。
- 批次 C（#27/#1/#8/#3/#9/#11/#12）完成，遵循 TDD（先写失败测试再实现），方案经 AskUserQuestion 全部确认采用推荐低风险的治本路线：
  - **#1 执行器每节点 JSON 写放大**：commit `80f105c`。节点记录仍存 `NodeRecords` JSON 列（不迁表），引入 `ExecutorSideEffects._pendingNodeWrites` 计数器与 `NodeFlushThreshold=25`；`PersistNodeRecordAsync` 仅当计数每约 25 个或终态才真正 `SaveChangesAsync`，写库由 O(N²) 降到约 O(N)；终态 flush 保证不丢数据。`PersistFailedStateAsync` 与内核调用传播真实 `CancellationToken`。重要修正：终态 113/115 处的 `SaveChangesAsync` 必须保持 `CancellationToken.None`——到彼时执行令牌已被取消，若传真实令牌会抛异常导致 `Cancelled` 终态丢失（实现者发现并修正了原 spec 的 `CancellationToken.None`→真实令牌的错误建议，避免回归两例取消测试）。`FlowEngineDbContext` 改为可继承以便 Spy 测试。
  - **#8 `SubWorkflowExecutor` 多输入丢数据**：commit `dab86a4`。抽 Core 级 `DataBatch.Merge`（重索引 `SourceIndex`）+ `ScriptParameterPreEvaluatorCore.PreEvaluateAsync`（仅 Core 引用，供 Plugin 安全调用），`WaitingArea.PortState.Merge` 改为复用 `DataBatch.Merge`（去重约 33 行）。子流程执行器为入边建 `pendingInputs` 字典、按端口 `Merge` 多入边批次，所有必需输入就绪才执行（`continue` 而非加入 `executed`，确保第二父节点可再次合并而非被跳过）；执行前经 `ScriptParameterPreEvaluatorCore` 预求值 `Script` 参数。保持进程内执行（轻量）。新增 `SubWorkflowExecutorTests`（多入边合并、缺参挂起、预求值）。
  - **#3 `FindReferencingCredentialAsync` 全表加载**：commit `147035f`。新建 `workflow_credential_usages` 表（`[Table("flow.workflow_credential_usages")]`、复合主键 `(WorkflowId,CredentialId,NodeId)`、`CredentialId` 索引），在 `FlowEngineDbContext.SaveChangesAsync` 中集中维护——变更 `Workflow`（增/改/删）前收集 ID，删除旧行并依 `CredentialReferenceScanner.Scan` 重插，单事务原子。`WorkflowRepository.FindReferencingCredentialAsync` 改为 `WorkflowCredentialUsages.AsNoTracking().Where(u=>u.CredentialId==id).Select(WorkflowName).Distinct()`（SQL `WHERE`，不再物化 `Workflows`）。新增启动 `WorkflowCredentialUsageBackfillHostedService` 幂等回填存量。已通过 `dotnet ef migrations add AddWorkflowCredentialUsage` 生成迁移。偏差：`NodeId` 用非可空 `string`（默认 `""`）而非 `string?`——可空列参与复合主键会触发模型校验告警、破坏 0 警告构建规则（实现者修正）。
  - **#9 凭据解密忽略 `KeyVersion`**：commit `eee81b4`。`ICryptoKeyProvider` 增 `CurrentVersion` 与 `GetKey(string keyVersion)`（空/当前→当前密钥；未知版本→`CryptographicException`），`CryptoKeyProvider` 以 `v1` 为当前版、保留旧版回退；`CredentialService` 与 `CredentialAccessor` 解密均改传 `credential.KeyVersion`。`GetKey()` 旧签名保留兼容。新增 `CryptoKeyProviderTests`（版本解析、未知版本抛异常、旧版回退）。
  - **#11 前端重渲染与状态管理**：commit `ab96d7a`。见批次 C 列表 #11 条目。新增 `canvasStore.test.ts`（322 行）覆盖节点/连线/选区/撤销重做/自动布局动作。验证：`npm run typecheck` 0 错误、`npm run build` 成功、`npx vitest run` 443 全绿。偏差：经核实 `workflowStore` 由全局收为纯元数据，load/save/new 经 canvasStore 编排；为避免循环导入在模块求值期绑定，`useWorkflowStore.setState` 经闭包运行时访问标记脏。
  - **#12 前后端契约补充**：commit `64dfbe1`。见批次 C 列表 #12 条目。前端 `ExecutionStatus` 联合类型补 9 枚举值、`ExecutionDto.error` 死字段删除、`executeWorkflow` POST `{inputs,idempotencyKey}`；后端 `ExecutionsController.Execute` 已消费 `dto?.Inputs` 与 `dto?.IdempotencyKey`，端到端对齐。新增 `executionStatus.contract.test.ts` 锁定前后端值一致。
  - **合并态验证（全部 C 提交合入后）**：`dotnet build FlowEngine.sln -c Debug` 0 警告 0 错误；前端 `npm run typecheck` 0 错误、`npm run build` 成功（仅既有 chunk 体积提示）、`npx vitest run` 443 全绿。后端各套件此前各自全绿（Core 651 / Runtime 632 / Application 471 / Infrastructure 98 / Host 322 ≈ 2174）。

### 收尾加固（非阻塞遗留项，已补齐）
- [x] #5/#24 关系型 `ExecuteDeleteAsync` 路径集成测试：commit `31a2ea1`。既有 `ExecutionCleanupService`/`TriggerService` 的删除路径虽已改 `ExecuteDeleteAsync`，但全部测试用 InMemory 提供程序（`IsRelational()` 为 false），永远走不到关系型分支。新增 `ExecutionCleanupServiceSqliteTests` 与 `TriggerServiceSqliteTests`，借 `Identity/UserStoreTests` 的内存 SQLite 夹具（`DataSource=:memory:` + 保持连接 + `EnsureCreated`）将 `IsRelational()` 翻为 true，并以 EF Core 命令日志断言「仅生成一条 `DELETE FROM` 语句」证明走的是单语句批量删除（InMemory 退化路径根本不产生 SQL DELETE，即便退化跑在关系型上也会逐行生成 N 条）。验证：`dotnet test ... --filter "SqliteTests"` 2/2 通过；`FlowEngine.Application.Tests` 全量 473/473 通过。
- [x] #21 OAuth2 令牌获取接入 SSRF 连接时防护：commit `19100d6`。`OAuth2TokenService` 原仅依赖 `SsrfGuard.IsInternalTarget` 同步预检 + 默认 `IHttpClientFactory.CreateClient()`，DNS 重绑定可绕过。改为注入 `IHttpClientPool`、令牌请求经 `httpClientPool.GetClient()`（底层 `SocketsHttpHandler` 带 `ConnectCallback = SsrfGuard.CreateConnectCallback()`，连接瞬间校验实际解析 IP），与 `HttpClientPool` 通用 HTTP 路径一致；保留 `IsInternalTarget` 预检。测试 `StubHttpClientFactory` 全部替换为 `StubHttpClientPool`，新增 `GetTokenAsync_UsesSsrfSafeHttpClientPool` 断言令牌获取确实经 `GetClient`（连接时拦截有效性已在 `SsrfGuardConnectCallbackTests.CreateConnectCallback_HostnameResolvingToLoopback_BlockedAtConnectTime` 证明）。验证：`dotnet test ... --filter "OAuth2TokenService"` 18/18 通过；`dotnet build FlowEngine.sln` 0 警告 0 错误。
