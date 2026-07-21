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

### 批次 B（中等风险、无迁移）
- [ ] #2 热查询列补索引（`FlowEngineDbContext`）
- [ ] #4 / #6 单次执行重复加载同一工作流（透传 Workflow/编译定义）
- [ ] #5 清理/删除改用 `ExecuteDeleteAsync`
- [ ] #7 `ParameterResolver` 数值精度（`long`/金额不丢精度）
- [ ] #10 幂等可能返回"假成功"（真实 executionId 原子注册）
- [ ] #20 `WorkflowExecutionWorker` scope 优化（per-item scope 解析 `WorkflowExecutor`）
- [ ] #21 SSRF 防护增强（`SsrfGuard` 移除预检查，依赖 `ConnectCallback`）
- [ ] #23 统计/授权批量查询优化（防 N+1）
- [ ] #24 `TriggerService` 事务补充
- [ ] #25 `WaitingArea` 锁粒度（`ConcurrentDictionary`）
- [ ] #26 前端其他类型/选择器优化

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
- [x] 批次 A
- [ ] 批次 B
- [ ] 批次 C

## 主要修改记录
- 批次 A（8 项）全部完成，遵循 TDD：先写/调整失败测试再实现。
- 验证：`dotnet build FlowEngine.sln` 通过（0 警告 0 错误）；`FlowEngine.Host.Tests` 54 通过、`FlowEngine.Runtime.Tests`(ParameterResolver) 23 通过、`FlowEngine.Application.Tests` 458 通过。
- 实现差异说明：#13 包络 `details` 取 `null`（与计划一致，区别于全局异常的 `traceId` 对象）；#19 因 `JsonNode.FromElement` 非 STJ 公共 API，改用等价的 `JsonNode.Parse(GetRawText())`；#22 的 `s_guidRegex`/`s_functionCallRegex` 此前已是静态只读，本次将 `TryExtractMissingName` 内两处 `Regex.Match` 字面量也提取为静态字段。
