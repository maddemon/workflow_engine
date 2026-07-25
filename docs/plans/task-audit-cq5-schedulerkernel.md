# 任务：CQ-5 WorkflowSchedulerKernel 拆分（task-audit-cq5-schedulerkernel）

## 目标
将 `backend/FlowEngine.Runtime/Executor/WorkflowSchedulerKernel.cs`（967 行）拆分为协作类，使内核自身代码降至 ≤300 行，且**完全保留现有行为**。

## 待完成项
- [x] 提取 `RetryExecutor`（ExecuteNodeWithRetryAsync + CalculateBackoff）
- [x] 提取 `OutputRouter`（RouteOutputsAsync + ResolveSourcePortName + 端口名辅助）
- [x] 提取 `NodeProcessor`（ProcessAsync + ResolveLlmClientForNode + BuildNodeExecutionRecord 重载 + CapRetainedOutput/Cap + BuildRunInputs）
- [x] 提取 `TimeoutProcessor`（ProcessAsync = ProcessTimeoutsAsync）
- [x] 提取 `SchedulerHelpers`（SafeError + CreateDataBatch 静态共享）
- [x] 保留 `WorkflowSchedulerKernel` 公共构造签名不变，内部 `new` 协作者并委派
- [x] 新增 `RetryExecutorTests` / `OutputRouterTests` 单元测试
- [x] `dotnet build FlowEngine.sln --no-incremental` 0 警告/0 错误
- [x] `dotnet test tests/FlowEngine.Runtime.Tests` 全绿

## 完成标准
- 内核自身代码 ≤300 行（其余在协作者类）。
- 全部 Runtime 测试通过（行为未回归）。
- 构建无警告。

## 主要修改记录

### 新增文件（`backend/FlowEngine.Runtime/Executor/`）
- `SchedulerHelpers.cs`（76 行）：`internal static` 共享静态 `SafeError(NodeError?)` 与 `CreateDataBatch(object?)`，供内核及各协作者复用。
- `RetryExecutor.cs`（223 行）：`ExecuteNodeWithRetryAsync`（重试/超时/取消/异常 + 退避）与 `internal static CalculateBackoff`。
- `OutputRouter.cs`（160 行）：`RouteOutputsAsync`（单/多端口路由，内联 `EnqueueAsync + PulseScheduler` 替代内核静态方法）+ `ResolveSourcePortName/GetInputPortNames/GetOutputPortNames`。
- `NodeProcessor.cs`（497 行）：`ProcessAsync`（原 `ProcessNodeAsync`，返回 `shouldStop`）+ `ResolveLlmClientForNode` + `BuildNodeExecutionRecord` 两个重载 + `CapRetainedOutput/Cap` + `BuildRunInputs`。
- `TimeoutProcessor.cs`（136 行）：`ProcessAsync`（原 `ProcessTimeoutsAsync`）。
- `tests/FlowEngine.Runtime.Tests/Executor/RetryExecutorTests.cs`：重试成功 / 不可重试错误码停止 / 超时返回 `Code=="Timeout"` / Fixed 退避延迟 / `CalculateBackoff` 三策略与上限。
- `tests/FlowEngine.Runtime.Tests/Executor/OutputRouterTests.cs`：单端口目标直接入队并脉冲；多端口目标经等待区聚合未就绪不入队。

### 修改文件
- `WorkflowSchedulerKernel.cs`（**233 行**，原 967 行）：仅保留 ctor（在构造函数体内 `new` 出四个协作者并持有为私有只读字段）、`RunAsync`（委派 `_nodeProcessor.ProcessAsync` / `_timeoutProcessor.ProcessAsync`）、`EnqueueEntryNodesAsync`、`EnqueueWorkAsync`（static），以及 `SchedulerHelpers.CreateDataBatch` 调用。公共构造签名**完全不变**。
- `tests/FlowEngine.Runtime.Tests/Executor/WorkflowSchedulerKernelTests.cs` 与 `WorkflowExecutorTests.cs`：原反射调用 `WorkflowSchedulerKernel.BuildNodeExecutionRecord` / `CapRetainedOutput` 的测试，改为构造 `NodeProcessor` 并对其实例反射（方法已下沉至 `NodeProcessor`）。

### 行为保留要点 / 需说明的偏离
- **日志类型**：内核构造签名被冻结（不能增删参数），其 `ILogger<WorkflowSchedulerKernel>` 无法直接传给协作者期望的 `ILogger<T>`；故四个协作者统一接受非泛型 `ILogger`，由内核直接传入自身 logger，真实日志通道得以保留（仅日志分类名变为 `WorkflowSchedulerKernel`），不丢失可观测性。
- **TimeoutProcessor 构造**：原计划构造签名未含 `OutputRouter`，但 `ProcessTimeoutsAsync` 在 Continue 策略下需 `RouteOutputsAsync` 路由下游，故为其构造增加 `OutputRouter outputRouter` 参数（行为必须项）；`BuildNodeExecutionRecord` 的裸参数重载在 `TimeoutProcessor` 内以 `_secretMasker` 复刻一份（因 `SchedulerHelpers` 仅承载 `SafeError`/`CreateDataBatch`，不含需 `SecretMasker` 的建记录逻辑）。
- **`_defaults` 字段**：原 `_defaults` 字段在抽离后内核不再读取，改为在构造体内计算并传给 `RetryExecutor`/`NodeProcessor`（避免未使用字段警告），行为等价。
- 退避、超时、可重试错误码过滤、等待区聚合、零端口跳过、环路失控保护、OncePerItem 累积、脱敏建记录等全部分支逻辑逐字符保留。

### 验证结果
- `dotnet build FlowEngine.sln --no-incremental`：**0 警告 / 0 错误**。
- `dotnet test FlowEngine.sln`：**全部通过**（Runtime 902 / Core 693 / Infrastructure 99 / Application 502 / Host 374，共 2570，0 失败）。
- 拆分后 `WorkflowSchedulerKernel.cs` 行数：**233 行**（≤300 达标）。

