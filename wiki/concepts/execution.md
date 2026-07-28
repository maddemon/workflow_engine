# 执行模型（Execution Model）

> 本文档基于当前代码编写，以代码为准。执行引擎位于 `FlowEngine.Runtime`，节点流程见 [工作流模型](workflow-model.md)，表达式见 [表达式与脚本模型](expressions.md)。
> 总览见 [系统总览](architecture/overview.md)。

工作流按 **DAG 拓扑顺序** 执行；支持多输入等待、分支、重试、取消与 Saga 风格补偿。执行事件经 **WebSocket** 实时推送前端。

## 1. 执行入口与调度

- 主执行器 `WorkflowExecutor`（`Runtime/Executor/WorkflowExecutor.cs`，实现 `IEngine`）统筹一次工作流执行。
- `WorkflowSchedulerKernel`（`Runtime/Executor/WorkflowSchedulerKernel.cs`）驱动节点出队、执行、超时与取消；支持空闲等待与取消感知。
- 环检测：`CycleDetector.ComputeBackEdges(...)` 在 `ExecutionSession` 构造时基于连接图计算一次回边（`ExecutionSession.cs:133`），使含环图（如 Loop）可按 DAG 调度。
- 入口推导：无显式 `IsEntry` 时取首个 Trigger 节点。

## 2. 单节点执行流水线

每个节点经固定阶段的管道（`Runtime/Execution/Pipeline` + `Runtime/Execution/Stages`）处理，顺序为：

| 阶段 | 职责 |
|------|------|
| `ValidationStage` | 参数与端口校验 |
| `InitializeStage` | 节点上下文初始化 |
| `ResolutionStage` | **解析参数 → 解密凭据**（`ResolutionStage.cs`，参数经表达式求值，凭据引用经凭据系统注入） |
| `ExecutionStage` | **执行节点**（`NodeProcessor` / 节点 `ExecuteAsync`） |
| `RoutingStage` | 按输出端口与连接条件**路由输出给下游**（`OutputRouter.cs`） |
| `PostProcessStage` | 后处理（错误策略、统计） |
| `PersistenceStage` | 执行记录落库 |

即：**解析参数 → 解密凭据 → 执行 → 输出到下游**。凭据静态加密（AES-GCM），运行时仅解密注入本次执行，明文不落库、不返回前端、不进日志。

## 3. 数据路由与等待区

- 节点输出为 `DataBatch`（见 [工作流模型](workflow-model.md)），经 `OutputRouter` 写入下游节点的对应 `Input` 端口。
- **多输入等待**：`WaitingArea`（`Runtime/WaitingArea/WaitingArea.cs`，注意：**不存在名为 `MultiInputBarrier` 的类**）按 `(ExecutionId, NodeInstanceId)` 收集各输入端口的批次；`IsReady(...)` 判断全部必需端口到齐后 `TryTake(...)` 取出并触发节点执行。
  - 默认输入等待超时 **5 分钟**（`TimeSpan.FromMinutes(5)`），超时项经 `GetTimeoutKeys()` 暴露供上层处理。
  - `CancelWaiting(...)` 在取消时清理等待状态。

## 4. 重试

- `RetryExecutor`（`Runtime/Executor/RetryExecutor.cs`）包裹单节点执行，处理超时、取消与异常后按退避策略重试。
- `RetryPolicy`（`Core/Entities/RetryPolicy.cs`）字段：

| 字段 | 含义 |
|------|------|
| `MaxRetries` | 最大重试次数 |
| `BaseDelay` / `MaxDelay` | 基础 / 最大延迟 |
| `UseJitter` | 是否抖动 |
| `BackoffStrategy` | 退避策略（默认 `Exponential`） |
| `RetryableErrorCodes` | 可重试错误码（空 = 全部可重试） |

- 节点级 `ErrorStrategy` 决定失败走向（`Retry` 走重试；`Continue` 由 `ErrorStrategyHandler` 做安全包装后继续）；**节点自身超时与外都取消不重试**。

## 5. 取消

- 取消登记：`ExecutionCancellationRegistry`（`Runtime/Executor/ExecutionCancellationRegistry.cs`）使运行中的执行可被真正取消并进入 `Cancelled` 终态。
- 状态机 `Pending/Running --Cancel--> Cancelled`（`ExecutionStateMachine`，见下）；`WorkflowSchedulerKernel` 在节点处理/空闲等待期间检测到取消后立即统一转为 `Cancelled` 终态并落库，不向上抛。
- `WorkflowExecutor` 在出队前即检查是否已被 `CancelAsync` 置为 `Cancelled`，避免覆写终态。

## 6. 状态机与 Saga 补偿

执行状态由 `ExecutionStateMachine`（`Runtime/Executor/ExecutionStateMachine.cs`，基于 Stateless 库）驱动，合法转换：

```text
Pending ──Start──▶ Running
Pending/Running ──Cancel──▶ Cancelled
Running ──Complete──▶ Completed
Running ──Fail──▶ Failed
Running ──DryRunComplete──▶ DryRunCompleted
Completed ──Compensate──▶ Compensating
Compensating ──CompensationSucceed──▶ Compensated
Compensating ──CompensationFail──▶ CompensationFailed
```

- **Saga 风格补偿**：已完成（`Completed`）的工作流可触发补偿（`Compensate`），进入 `Compensating` 状态，由补偿逻辑反向撤销已产生的副作用；最终落为 `Compensated`（成功）或 `CompensationFailed`（失败）。
- 非法转换（当前状态下无对应 `Permit`）被静默忽略，等价于重构前的 `if` 守卫语义。

## 7. 清理与终态持久化

- `ExecutionCleanupService`（`Application/ExecutionCleanup/ExecutionCleanupService.cs`，由 `ExecutionCleanupHostedService` 后台定时驱动）负责执行记录/残留状态的清理。
- 终态（Completed / Failed / Cancelled / Compensated / CompensationFailed / DryRunCompleted）无论如何都在执行结束后持久化，确保前端可查最终状态。

## 8. 实时事件推送

- 执行过程（节点开始/完成/失败、输出、LLM 流式 token 等）经事件总线（`MediatrEventBus`）广播到 **WebSocket** 通道（`FlowEngine.Host/WebSocketHandlers/`）。
- 前端订阅执行后实时高亮节点并展示输出；断线重连经 `WebSocketReplayService` 补发。

## 备注

- 重试次数：`EngineDefaultsOptions.DefaultMaxRetries` 默认为 `0`；仅当节点 `ErrorStrategy = Retry` 且未显式设置 `RetryPolicy` 时，实际重试次数取 `max(DefaultMaxRetries, 1)`（即默认 1 次），显式 `RetryPolicy.MaxRetries` 优先（`RetryExecutor`）。
- `WaitingArea` 按 `(ExecutionId, NodeInstanceId)` 收集各输入端口批次，全部必需端口到齐后触发执行；超时后的下游处理由执行引擎实现决定。
