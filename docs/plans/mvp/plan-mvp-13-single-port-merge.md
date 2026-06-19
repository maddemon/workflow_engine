# 开发计划：单端口多来源自动合并（plan-mvp-13-single-port-merge）

## 1. 概述

解决"多个上游节点连到同一个下游节点的 input 端口，下游应只执行一次，拿到合并后的所有数据"这个场景。

### 核心约定

- **单 input 端口**是推荐做法，多数节点只需声明一个 `input` 端口
- **命名端口**（`a`/`b` 等）保留给语义上需要区分来源的场景
- 路由层自动判断：单端口有多来源时走合并路径，单来源时走直接入队路径（与现有行为一致）
- **合并语义**：多个上游的 `DataBatch.Items` 拼接为一个 List

### 覆盖范围

- `WorkflowExecutor.RouteOutputsAsync` 路由逻辑改造
- 新增 `PendingMerge` 或复用 WaitingArea 实现合并等待
- 现有多端口节点的 WaitingArea 逻辑不变

### 不覆盖范围

- 端口级超时策略（沿用现有全局超时）
- 前端画布多连线校验（已支持同一端口多连线）
- Merge/Split 等高级路由模式

## 2. 交付物清单

### 修改文件

| 文件 | 变更 |
|------|------|
| `Runtime/Executor/WorkflowExecutor.cs` | `RouteOutputsAsync` 增加多来源检测与合并等待逻辑 |
| `Runtime/WaitingArea/WaitingArea.cs` | 可选：新增 `ReceiveWithExpectedCount` 或 `SetExpectedSourceCount` 方法 |

### 新增文件

无（逻辑集中在 WorkflowExecutor 内改造）

## 3. 开发阶段

### 阶段一：上游连接数检测

- 目标：在路由前构建反向索引，知道每个目标节点有多少上游
- 核心任务：
  - `ProcessNodeAsync` 或 `ExecuteLoopAsync` 中构建 `connectionsByTarget`：

```csharp
var connectionsByTarget = workflow.Connections
    .ToLookup(c => c.TargetNodeId);
```

  - 每条 `Connection` 已知 `SourceNodeId + SourcePortName → TargetNodeId + TargetPortName`
  - 通过 `connectionsByTarget` 可查 `GetTargetUpstreamCount(targetNodeId, targetPortName)`
- 验收标准：
  - `connectionsByTarget` 能正确返回每个目标节点的上游数量
  - 单上游节点 count = 1，双上游节点 count = 2

### 阶段二：单端口多来源合并路由

- 目标：改造 `RouteOutputsAsync`，对单端口多来源走合并路径
- 核心任务：
  - 在 `WorkflowExecutor` 中新增 `_pendingBatches` 字典，跟踪等待合并的批次：

```csharp
// key: (executionId, targetNodeId, targetPortName)
// value: 已到达的 batches 列表
private readonly ConcurrentDictionary<
    (Guid ExecutionId, Guid NodeInstanceId, string PortName),
    List<DataBatch>> _pendingBatches = new();
```

  - 改造 `RouteOutputsAsync` 的单端口分支：

```
for each outgoing connection:
    targetNode = ...
    targetInputPorts = GetInputPortNames(targetNodeType)

    if targetInputPorts.Count == 1:
        upstreamCount = CountUpstream(targetNode.Id, connection.TargetPortName)
        if upstreamCount > 1:
            → 走合并路径
                pendingBatches[(executionId, targetNodeId, port)].Add(outputBatch)
                if pendingBatches[].Count == upstreamCount:
                    merged = MergeDataBatches(pendingBatches[])  // 拼接 Items
                    pendingBatches.Remove(...)
                    enqueue (targetNodeId, { port: merged })
                else:
                    // 等待更多上游送达，不入队
        else:
            → 走直接入队路径（和现在一样）
    else:
        → 走 WaitingArea 路径（和现在一样，不变）
```

  - 实现 `MergeDataBatches`：将多个 `DataBatch` 的 `Items` 拼接为一个 List
  - 清理：`CleanupExecution` 时清理对应的 `_pendingBatches` 条目
- 验收标准：
  - 节点 A + 节点 B 都连到 JS Node 的 input → JS Node 执行一次，Items 包含 A 和 B 的数据
  - 节点 A 单独连到 JS Node 的 input → 直接入队，行为与改造前一致
  - 多端口节点（MergeNode 的 a/b 端口）行为不受影响
  - 执行取消/清理时 `_pendingBatches` 对应条目被清除

### 合并路径时序示意

```
         上游 A 完成          上游 B 完成
RouteOutputsAsync           RouteOutputsAsync
    ↓                           ↓
pendingBatches[JS.input]    pendingBatches[JS.input]
    .Add(batchA)                .Add(batchB)
    count=1 of 2               count=2 of 2
    → 等待                    → 数量达标
                                → Merge([batchA, batchB])
                                → Enqueue JS → 执行一次
                                → JS.Inputs["input"].Items
                                  = [...batchA.items, ...batchB.items]
```

### 与现有 WaitingArea 的关系

- WaitingArea 保留，专门服务**多端口节点**
- 单端口多来源不经过 WaitingArea，走独立的 `_pendingBatches` 合并
- 二者互不干扰

### 错误策略

- 如果某个上游执行失败（`result.Success == false` 且 `ErrorStrategy != Continue`），执行提前终止，`_pendingBatches` 中等待该节点的条目在 `CleanupExecution` 中被清理
- 如果 `ErrorStrategy == Continue`，失败的上游仍然路由其 Output（可能为空的 `DataBatch`），合并后传给下游

## 4. 阶段依赖图

```mermaid
flowchart LR
    S1[阶段一 上游连接数检测] --> S2[阶段二 单端口合并路由]
```

依赖：plan-mvp-05 执行引擎（`WorkflowExecutor` 和 `RouteOutputsAsync` 已就绪）。

## 5. 风险与待定项

| 风险/待定项 | 影响 | 应对 |
|------------|------|------|
| `OncePerItem` 模式下合并后的 Items 数量 = 各源之和，runCount 放大 | 下游节点执行次数多于预期 | 合并后的 DataBatch.Items.Count 即为 runCount，这是正确的——每个 item 应执行一次 |
| 同一端口多来源 + DisplayCondition | 前端用户需要知道此端口接受多连线 | 前端画布已支持多连线；端口 hover 时提示上游数量 |
| 超时：某个上游一直不完成，_pendingBatches 泄露 | 内存泄漏 | 执行完成/取消时清理；可加定时扫描 |
| 合并路径与现有 WaitingArea 路径重复 | 维护两个等待机制 | 若后续发现可统一，重构为单一 WaitingArea 支持"预期到达次数" |

## 6. 验收总标准

- 两个独立节点连到同一 JS Node 的 input → JS Node 执行一次，输入数据合并
- 单节点连到 JS Node → 行为与改造前一致（直接入队）
- 多端口 MergeNode 不受影响，a/b 端口各自等待
- 清理流程覆盖 `_pendingBatches`
- 所有现有引擎测试回归通过
