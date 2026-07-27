# 任务：SuccessfulOutputs/LatestBatches 按 node.Id 累积并设上限（task-6）

## 目标
- 将 `SuccessfulOutputs` / `LatestBatches` 的累积键由 `node.Name` 改为 `node.Id`，避免同名不同节点互相覆盖/串数据。
- 保留输出数始终受 `MaxRetainedOutputItems` 上限约束（0/负值回退为默认上限，内存有界）。
- 保持下游表达式 `$node['Name']` / `$items('Name')` 按名读取的契约不变。

## 待完成项
- [x] 新增 RED 测试：同名不同 Id 节点互不覆盖；超上限仅保留 cap。
- [x] `ExecutionStage` 累积改为按 `node.Id` 写键，且始终应用 retention cap。
- [x] `NodeExecutionHelpers.CapRetainedOutput` 按 `node.Id` 键截断。
- [x] `EngineDefaultsOptions` 新增 `DefaultMaxRetainedOutputItems`，0/负值回退默认上限。
- [x] `NodeExecutionContextFactory` 经 `workflow.Nodes` 做 Id→Name 映射，重建下游按名只读视图。
- [x] `dotnet build` 全仓通过；Runtime 测试 946 全过，全仓无回归。

## 完成标准
- 新测试通过；既有 `OncePerItemAccumulationTests` / `LoopIntegrationTests` / `WorkflowExecutorTests` cap 反射测试仍通过。
- `dotnet build` 0 警告 0 错误。

## 完成状态
- [x] 全部完成。

## 主要修改记录
- `backend/FlowEngine.Runtime/Execution/Stages/ExecutionStage.cs`：写键 `node.Name`→`node.Id`；cap 始终应用（effective cap = `MaxRetainedOutputItems > 0 ? 配置值 : DefaultMaxRetainedOutputItems`）。
- `backend/FlowEngine.Runtime/Execution/Stages/NodeExecutionHelpers.cs`：`CapRetainedOutput` 按 `nodeId` 截断。
- `backend/FlowEngine.Core/Configuration/EngineDefaultsOptions.cs`：新增 `const int DefaultMaxRetainedOutputItems = 1000`；文档说明 0/负值=默认上限。
- `backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs`：下游 `$node`/`$items` 经 Id→Name 映射重建按名视图。
- `backend/FlowEngine.Runtime/Executor/NodeProcessor.cs`：`CapRetainedOutput` 入参语义对齐为 nodeId（保留反射测试兼容性）。
- 新增 `tests/FlowEngine.Runtime.Tests/Executor/OutputAccumulationByIdTests.cs`。
