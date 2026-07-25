# 审计发现与遗留问题（2026-07-24）

对应分支 `audit-hardening-2026-07-24` 终审中识别的偏离项和待改进项。

---

## 0. 终审验证网关结果（通过项，供上下文）

| 网关 | 命令 | 结果 |
|------|------|------|
| 后端编译 | `dotnet build FlowEngine.sln --no-incremental` | **0 错误 / 0 警告**（清掉残留 `testhost` 后；首次出现的 21 条 `MSB` 文件锁告警为环境使然，非代码告警） |
| 后端测试 | `dotnet test FlowEngine.sln` | **2533 通过 / 0 失败**（Core 693 · Infra 99 · Runtime 881 · App 502 · Host 358） |
| 前端 build | `npm run build` | 通过 |
| 前端 typecheck | `npm run typecheck`（`tsc -b`） | 通过（无错误） |
| 前端测试 | `npm run test`（vitest） | **450 通过 / 0 失败** |

> 以下 #1–#15 为在质量闸门通过前提下，仍存在的**偏离项 / 测试覆盖缺口 / 虚假完成勾选**，须处理后方可判分支"完成"。注意：task-006 所述"`useExecution.cancelExecution` 既有失败"经核实为陈旧记录——该测试实际通过。

---

## 1. CQ-5 — WorkflowSchedulerKernel 仍为大类

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-05-architecture-cleanup.md` |
| **状态** | ⚠️ 部分未达标 |
| **描述** | SchedulerKernel 计划行数应从 887 显著下降。子组件已提取（`ExecutionStateMachine`、`ExecutionSession`、`ExecutionContextGlobalsBuilder`、`CycleDetector`、`NodeWorkItem` 等），但主类增长至 **967 行**（+80 行）。 |
| **根因** | 新功能引入（LLM 流式、补偿逻辑等）抵消了提取效果。 |
| **建议** | 在后续计划中专设拆分任务：提取 `ExecutionOrchestrator`（编排逻辑）、`PendingQueue`（等待队列）、`Runner`（驱动循环），使主类 ≤300 行。 |

---

## 2. Q-1 — CSS Modules 未实施

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-05-architecture-cleanup.md` |
| **状态** | ❌ 未通过 |
| **描述** | 计划要求为有局部样式需求的组件添加 `.module.css`（CSS Modules），贯彻样式隔离。终审发现 **零个 `.module.css` 文件**，所有组件仍使用全局 `App.css` / `index.css` 或行内样式。 |
| **根因** | 前端改造工作在本次分支中未获得充分执行资源。 |
| **影响** | 全局样式污染风险持续存在；多组件共用类名可能导致非预期覆盖。 |
| **建议** | 纳入前端改造专项计划，逐一为有局部样式需求的组件添加 `.module.css`。 |

---

## 3. ~~TST-5 — WorkflowModificationService 缺单元测试~~ （已纠正）

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-05-architecture-cleanup.md` |
| **状态** | ✅ 已达标 |
| **描述** | 2026-07-25 终审重新核查发现 `WorkflowModificationServiceTests` 已存在于 `tests/FlowEngine.Application.Tests/Workflows/WorkflowModificationServiceTests.cs`。原审计发现（2026-07-24）系误判。 |
| **结论** | TST-5 全部测试类就位：`WorkflowServiceCrudTests`、`WorkflowServiceProjectionTests`、`WorkflowServiceActivationCompensationTests`、`WorkflowServiceDeactivationCompensationTests`、`WorkflowServiceAuthorizationTests`、`WorkflowServiceDtoTests`、`WorkflowModificationServiceTests`。 |

---

## 4. SEC-3 — WebhookReplayCache / WebhookRateLimiter 缺单元测试

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-01-security-hardening.md` |
| **状态** | ⚠️ 推荐补充 |
| **描述** | `WebhookReplayCache` 和 `WebhookRateLimiter` 是 SEC-3 的核心新组件（防重放+限速），但未发现独立的单元测试文件。 |
| **影响** | 重放防护和限速逻辑无自动化回归保障。 |
| **建议** | 补充单元测试覆盖：重复 nonce 拒绝、过期 nonce 接受、时钟偏差处理、限速阈值拒绝/恢复、并发安全。 |

---

## 5. 一个测试因平台限制失败

| 字段 | 内容 |
|------|------|
| **测试** | `ShellToolNodeTests.Execute_RunInShellFalse_NotGated` |
| **状态** | ⚠️ 平台限定，非代码 Bug |
| **描述** | 该测试调用 `echo` 作为 shell 命令验证 `RunInShell=false` 时绕过门禁。在 **Windows 上 `echo` 是 `cmd.exe` 内置命令，非独立可执行文件**，导致测试失败。Unix/Linux/macOS 下 `echo` 是独立 exe，测试通过。 |
| **建议** | 添加 `[Fact(Skip="Platform-specific: echo is not a standalone executable on Windows")]` 或改用独立可执行文件（如 `where`、`ping -n 1 127.0.0.1`）作为跨平台替代。 |

---

## 6. Plugins.Storage 被 Defender 锁定（环境问题）

| 字段 | 内容 |
|------|------|
| **项目** | `FlowEngine.Plugins.Storage` |
| **状态** | ℹ️ 非代码问题，已确认无影响 |
| **描述** | 编译期间 `FlowEngine.Plugins.Storage.dll` 被 Microsoft Defender 防病毒服务（PID 5544）锁定，无法覆盖或删除，导致 `dotnet build` 失败。 |
| **根因** | 本分支对该项目无任何代码改动。 |
| **影响** | 无。 |
| **解决** | 关闭实时防护或添加排除路径后可编译。 |

---

## 7. SEC-2 — SameSite=Lax 而非 Strict（有意识取舍）

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-01-security-hardening.md` |
| **状态** | ℹ️ 已评估，可接受 |
| **描述** | 计划文档指定 `SameSite=Strict`，实际实现使用 `SameSiteMode.Lax`。`Lax` 是行业标准配置（阻断跨站 POST/PUT/DELETE，允许同站导航链接），结合自定义反伪造请求头中间件提供主要 CSRF 防御。 |
| **影响** | 非偏离——属于合理的安全-可用性权衡。 |

---

## 8. CON-5 — 大批次输出内存上限缺专用测试

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-04-concurrency-perf.md` |
| **状态** | ⚠️ 推荐补充 |
| **描述** | `MaxRetainedOutputItems` 配置项（`EngineDefaultsOptions.cs:41-45`）和 `CapRetainedOutput` 实现（`WorkflowSchedulerKernel.cs:436-441,868-886`）均已就位，但 **无专用单元测试验证上限行为**。OncePerItem 累积逻辑（`WorkflowSchedulerKernelTests:200-222`）已覆盖，但未设置 `MaxRetainedOutputItems > 0` 并断言输出被截断。 |
| **影响** | 内存限流机制无自动化回归保障；若 `CapRetainedOutput` 逻辑回归（如 Skip 截断方向错误），测试无法捕获。 |
| **建议** | 补充测试：设置 `MaxRetainedOutputItems=5`，执行 10 个 item 的 OncePerItem 工作流，断言 `SuccessfulOutputs` 只保留最新 5 个。 |
| **补充（2026-07-25 终审）** | 除"缺专用测试"外，更严重的缺陷是**上限在默认配置下根本不生效**。`MaxRetainedOutputItems`（`EngineDefaultsOptions.cs:45`）无初始化，默认值为 `0`；`WorkflowSchedulerKernel.cs:438` 的 `if (_defaults.MaxRetainedOutputItems > 0)` 因此在默认生产配置下跳过截断。即机制与测试（显式设值）存在，但验收标准"大批次输出内存有上限"在默认环境下**未达成**。建议：给该配置项设合理非零默认（如 1000），或在文档/配置中明确"需运维显式启用"，并补截断断言测试。状态由"推荐补充"上调为 **🟠 部分未达标（默认值缺陷）**。 |

---

## 9. CQ-1 — 冗余 Application 接口未删除（虚假勾选 + 违反规范）

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-05-architecture-cleanup.md`（CQ-1） |
| **状态** | ❌ 未通过（task-005 勾选"已完成"，实际未做；且违反 `backend-code-rules.md` §5） |
| **描述** | 计划要求去除单实现且无需 mock 的 Application 服务接口。终审发现 `IWorkflowService` 仍定义于 `backend/FlowEngine.Application/Workflows/IWorkflowService.cs:8`，并在 `backend/FlowEngine.Host/ServiceCollectionExtensions.cs:317` 以 `AddScoped<IWorkflowService>(sp => sp.GetRequiredService<WorkflowService>())` 注册，仍被 `AiWorkflowsController`、`Mcp/Tools/WorkflowLifecycleTools`、`WorkflowQueryTools`、`DraftFeedbackTools` 注入。其余单实现接口（`IWorkflowValidationService`、`IWorkflowModificationService`、`IExecutionService`、`IWorkflowAssemblyService` 等）亦保留。 |
| **影响** | 与项目后端规范 §5（"只有一个实现时直接写具体类，不必先定义 `IXxxService` 接口"）直接冲突；属虚假完成勾选。 |
| **建议** | 若确为单实现且无 mock 需求，移除冗余接口并改直接注入具体类；或如实将 task-005 对应项状态改回"未做"。保留需 mock 的 Core `Abstractions` 接口。 |

---

## 10. DEP-5 — 前端依赖版本未校正（虚假勾选）

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-05-architecture-cleanup.md`（DEP-5） |
| **状态** | ❌ 未通过（task-005 勾选"依赖校正"完成，实际未做） |
| **描述** | 计划/任务要求校正前端依赖版本异常：审计指出 `lucide-react ^1.21.0` 为非可解析版本线，`@mantine/core ^9.3.2` 与 `@mantine/form ^9.4.1` 存在次版本漂移。终审确认 `frontend/package.json` **不在本分支改动列表内**，版本字符串维持原样（仍含 `lucide-react ^1.21.0`、`@mantine/core ^9.3.2`、`@mantine/form ^9.4.1`）。 |
| **影响** | 审计点名的前端依赖风险持续存在；属虚假完成勾选。 |
| **建议** | 校正 `package.json` 版本范围（`lucide-react` 改为可解析线、`@mantine/*` 对齐次版本）并执行 `npm audit`；或将任务状态如实改回"未做/评估中"。 |

---

## 11. CQ-2 — SqlStatementScanner 未纳入版本控制（🔴 阻断）

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-05-architecture-cleanup.md`（CQ-2）+ `plan-audit-01-security-hardening.md`（SEC-0） |
| **状态** | 🔴 阻断（必须处理，否则干净克隆 / CI 编译失败） |
| **描述** | DbRead SQL 扫描器抽取为 `plugins/FlowEngine.Plugins.Standard/Data/SqlStatementScanner.cs`，`DbReadNode.cs` 已引用之，重复 tokenizer（`HasTrailingStatement`/`ExtractFirstKeyword`/`ContainsKeyword`）已消除——**CQ-2 合并本身真实完成**。但 `git status` 显示该文件为 **untracked**（`?? plugins/FlowEngine.Plugins.Standard/Data/SqlStatementScanner.cs`），未纳入版本控制。在干净克隆或 CI 构建时，`DbReadNode` 引用的类型不存在 → 插件项目编译失败。 |
| **影响** | VCS 完整性缺陷：本机可编译（文件在磁盘），但提交遗漏该文件后任何干净环境无法编译。 |
| **建议** | 将其 `git add` 纳入版本控制（本分支未提交，待提交时必须带上）。 |

---

## 12. EX-4 — Webhook 同步完成服务/通知器缺测试

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-01-security-hardening.md`（EX-4） |
| **状态** | ⚠️ 推荐补充（已实现，缺端到端测试） |
| **描述** | Webhook 同步模式已改事件驱动：`WebhookHandler.cs:178-203` 调用 `_syncCompletion.WaitAsync(...)` 替代 DB 轮询；`WebhookSyncCompletionService`（`TaskCompletionSource`）与 `WebhookCompletionNotifier`（`INotificationHandler<WorkflowCompletedEvent>`）构成事件唤醒。但二者**无独立单元测试**，`WebhookHandlerTests` 仅以 `FakeSyncCompletion` 桩覆盖 handler 分支，未验证"事件真正唤醒等待者"的往返。 |
| **影响** | "以事件通知替换 DB 轮询"的核心改造缺少自动化回归保障。 |
| **建议** | 补充 `WebhookSyncCompletionService` 单测（发布完成事件后 waiter 收到结果）与 `WebhookCompletionNotifier` 集成测试。 |

---

## 13. OBS-2 — WorkflowExecutor 事件发布侧缺测试

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-02-observability-hardening.md`（OBS-2） |
| **状态** | ⚠️ 推荐补充（已实现，缺发布侧断言） |
| **描述** | `WorkflowExecutor` 经 `ExecutorSideEffects` 调用 `_eventBus.PublishAsync` 发布 `WorkflowStartedEvent`/`NodeExecutedEvent`/`NodeErrorEvent`（`WorkflowExecutor.cs:264-289`），且由 `WorkflowSchedulerKernel` 在真实执行路径调用。但无测试断言"执行器确实发布了这三类事件"（现有 `WorkflowExecutor` 构造未注入 `IEventBus`，测试未捕获发布）。 |
| **影响** | 审计链"执行开始/节点完成/节点错误"的发布行为无回归保障。 |
| **建议** | 注入 Fake `IEventBus` 执行工作流，断言三类事件按序发布。 |

---

## 14. OBS-7 — WS 广播计数日志 / Meter 缺断言

| 字段 | 内容 |
|------|------|
| **计划** | `plan-audit-02-observability-hardening.md`（OBS-7） |
| **状态** | ⚠️ 推荐补充（已实现，缺计数/指标断言） |
| **描述** | `WebSocketEventPushService.cs:339-355` 已加结构化日志（成功/失败计数）+ `FlowEngineMetrics.WebSocketBroadcastSuccess/Failure` 自增。但现有 `WebSocketEventPushServiceTests` 仅覆盖广播/发送/重连行为，**未断言成功/失败计数日志与 Meter 自增**。 |
| **影响** | 可观测性指标无回归保障。 |
| **建议** | 补充断言广播成功/失败时计数日志与 Meter 增量。 |

---

## 15. 次要测试覆盖缺口（合并：已实现但行为未被守护）

| 字段 | 内容 |
|------|------|
| **计划** | 多计划 |
| **状态** | ⚠️ 推荐补充 |
| **描述** | - **SEC-7**：`JsEngineSandboxWhitelistTests` 未含 Unicode 同形异义逃逸用例（白名单设计先天防御，但缺断言）。<br>- **CON-1**：`ExecutionWebSocketHandlerTests.HandleAsync_LargeMessageWithMidClose_DoesNotThrow` 仅断言"无异常 + 连接清理"，**未断言 ArrayPool buffer 仅归还一次**，双重归还回归无守护。<br>- **CON-3**：无"两个并发 SwitchNode 执行互不篡改 `Cases`/`Ports`"的端到端测试，仅依赖 `NotSame` 实例隔离的结构性保证。<br>- **EXT-3**：`NodeTypesControllerTests` 将响应反序列化为 **Core 实体 `NodeTypeDescriptor`** 而非 `NodeTypeDescriptorDto`，未真正断言返回的是 DTO（实现正确，测试质量弱）。 |
| **建议** | 针对上述逐项补测试，使验收行为被自动化守护。 |

---

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
|------|--------|----------|------------|
| 2026-07-25 | Agent | 创建审计发现文档（1-7 项） | `audit-hardening-2026-07-24` 分支终审 |
| 2026-07-25 | Agent | 纠正 #3 TST-5（WorkflowModificationServiceTests 已存在）；新增 #8 CON-5 内存上限测试缺口 | `audit-hardening-2026-07-24` 终审复查 |
| 2026-07-25 | Agent | 补充终审验证网关结果（#0）；新增 #9 CQ-1、#10 DEP-5、#11 CQ-2 VCS 阻断、#12 EX-4、#13 OBS-2、#14 OBS-7、#15 次要测试缺口；更新 #8 CON-5 默认值缺陷 | `audit-hardening-2026-07-24` 完成度终审 |
