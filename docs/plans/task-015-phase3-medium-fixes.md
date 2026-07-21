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
- [ ] #2 热查询列补索引（`FlowEngineDbContext`）—— 待批次 B2（需迁移）
- [ ] #4 / #6 单次执行重复加载同一工作流（透传 Workflow/编译定义）—— 待批次 B2
- [ ] #20 `WorkflowExecutionWorker` scope 优化（per-item scope 解析 `WorkflowExecutor`）—— 待批次 B2（与 #27 合并）
- [ ] #23 统计/授权批量查询优化（防 N+1）—— 待批次 B2
- [ ] #26 前端其他类型/选择器优化 —— 待批次 B2（与批次 C #11/#12 前端部分合并）

### 批次 B2（剩余中等项，待派发）
- [ ] #2 热查询列补索引（EF 迁移）
- [ ] #4 / #6 单次执行重复加载同一工作流（透传）
- [ ] #20 `WorkflowExecutionWorker` scope 优化（与 #27 合并）
- [ ] #23 统计/授权批量查询优化（防 N+1）
- [ ] #26 前端其他类型/选择器优化（与 C #11/#12 合并）

### 批次 C（高风险、需迁移/大改）
- [ ] #1 执行器每节点 JSON 写放大（子表/批量持久化）
- [ ] #3 `FindReferencingCredentialAsync` 全表加载（归一化关联表）
- [ ] #8 `SubWorkflowExecutor` 多输入丢数据（复用 `WaitingArea`）
- [ ] #9 凭据解密忽略 `KeyVersion`（密钥环）
- [ ] #11 前端重渲染与状态管理（画布状态抽 `canvasStore` 等）
- [ ] #12 前后端契约补充（`ExecutionStatus`/`PortInstance`/`error`/`inputs`/`幂等键`）
- [ ] #27 `WorkflowExecutionWorker` scope（与 #20 合并）

## 完成标准
- 每批次 items 修复，先写失败测试（TDD）再实现
- 后端 `dotnet build` + `dotnet test` 通过；前端涉及项 `npm run build` + `npm run typecheck` + 单测通过
- 以计划文档与任务文档为依据自审，无回归
- 代码遵循 `backend-code-rules.md` / `frontend-code-rules.md`
- 不主动创建无关文件；不擅自修改无关代码

## 完成状态
- [x] 批次 A（8/8）
- [x] 批次 B（6/11，剩余 5 项归入批次 B2）
- [ ] 批次 B2（待派发）
- [ ] 批次 C

## 主要修改记录
- 批次 A（8 项）全部完成，遵循 TDD：先写/调整失败测试再实现。commit `9d72754`。
- 验证：`dotnet build FlowEngine.sln` 通过（0 警告 0 错误）；`FlowEngine.Host.Tests` 54 通过、`FlowEngine.Runtime.Tests`(ParameterResolver) 23 通过、`FlowEngine.Application.Tests` 458 通过。
- 实现差异说明：#13 包络 `details` 取 `null`（与计划一致，区别于全局异常的 `traceId` 对象）；#19 因 `JsonNode.FromElement` 非 STJ 公共 API，改用等价的 `JsonNode.Parse(GetRawText())`；#22 的 `s_guidRegex`/`s_functionCallRegex` 此前已是静态只读，本次将 `TryExtractMissingName` 内两处 `Regex.Match` 字面量也提取为静态字段。
- 批次 B（6 项）全部完成，遵循 TDD：先写失败测试再实现。`ExecutionIdempotencyService` 已有 `TryGetExistingAsync`/`TryGetOrRegisterAsync` 可直接复用。验证：`dotnet build` 0 警告 0 错误；`FlowEngine.Core.Tests` 647、`FlowEngine.Runtime.Tests` 626、`FlowEngine.Application.Tests` 460，全部通过、0 失败。差异说明：#5/#24 的删除/清理在 InMemory 提供程序下退化为按 ID 加载后删除（仅测试路径），关系型下用 `ExecuteDeleteAsync`；#21 `IsInternalTarget` 保留为尽力预检，权威防护移至 `CreateConnectCallback` 连接瞬间校验。
- B1（#10 幂等并发重复执行窗口）按评审修复：原 P3 为修「假成功」移除抢占，引入并发重复执行窗口。现于 `StartAsync` 前用 `TryGetOrRegisterAsync` 以唯一约束抢占幂等键（夺胜者启动并把键更新为真实执行 id；落败者不启动，经 `WaitForRealExecutionAsync` 轮询复用胜者真实结果，绝不返回合成对象）。并补关系型并发测试（`ExecutionServiceConcurrencyTests`：文件型 SQLite + 真实 `ExecutionIdempotencyService` + 引擎侧信号解耦抢占/启动两阶段，断言 `engine.StartAsync` 恰好 1 次且两请求返回同一真实结果；另含 InMemory 控制流锁定测试）。修复中发现并修正 `ExecutionIdempotencyService.TryGetExistingAsync` 缺少 `AsNoTracking` 导致落败者上下文被旧 tracked 实体遮蔽、无法观察到胜者将键更新为真实执行 id 而误判超时重复启动，已加 `AsNoTracking`。验证：`dotnet build` 0 警告 0 错误；`FlowEngine.Application.Tests` 462 全绿（含新增 2 例）。
