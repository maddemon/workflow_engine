# 任务：后端代码质量整改（重复代码 / 健壮性）

## 目标
消除代码审查发现的后端重复代码与健壮性隐患，范围限定为低风险重构与已确认的问题，不新增功能、不改变对外行为（除 C 组显式抛异常外）。

## 待完成项
- [ ] A1 用 `JsonPath.GetValue` 替换 5 个节点的私有 `GetFieldValue`
  - `MergeNode.cs`、`AggregateNode.cs`、`SortNode.cs`、`DeduplicateNode.cs`、`DataQualityNode.cs`
- [ ] A2 `MergeNode.cs` 顶部加 `using System.Text.Json.Nodes;`，消除 `System.Text.Json.Nodes.JsonNode` 全限定名
- [ ] A3 为 `NodeExecutionContext` 增加 `GetInputBatch(portName)` 扩展方法，替换约 21 处 `Inputs.TryGetValue(... ? : new DataBatch())` 样板
- [ ] B1 `DbUpsertNode.Mode` 魔法字符串改为枚举 `UpsertMode`，入口处一次归一化（消除 6 处 `Equals` 重复）
- [ ] B2 `WebSearchToolNode` / `PaginateNode` 的 `catch { return null; }` 改为记录日志后返回
- [ ] B3 `JSNode` 超时判定从 `ex.GetType().Name.Contains("Timeout")` 改为捕获具体异常 / `OperationCanceledException`
- [ ] C1 将 `MergeNode` / `AggregateNode` / `AgentNode` / `OpenAiLlmClient` / `ShellToolNode` 的 `switch` 静默 `default` 兜底改为显式抛 `ArgumentOutOfRangeException`

## 完成标准
- `dotnet build` 全部通过
- `dotnet test` 全绿（重点 `FlowEngine.Runtime.Tests` 的 `JsonPathTests` 与各节点 Plugins 测试）
- 项目中不再存在重复的私有 `GetFieldValue` 实现（仅 `Core/Scripting/JsonPath.cs` 保留）
- 不再有 `System.Text.Json.Nodes` 全限定名散落
- 无静默吞掉未知枚举值的 `default` 分支

## 完成状态
- [x] A1 `GetFieldValue` → `JsonPath.GetValue`（5 个节点）
- [x] A2 `MergeNode` FQN 清理
- [x] B2 吞异常补日志（WebSearchToolNode / PaginateNode）
- [x] B3 `JSNode` 超时判定改 `TimeoutException`
- [x] C1 `switch` 静默兜底改抛异常（MergeNode / AggregateNode）
- [ ] A3 `GetInputBatch` 扩展（需全量替换约 21 处，建议单独一轮）
- [ ] B1 `DbUpsertNode` 枚举化（需向后兼容转换器，待确认）
- [ ] C1 余下（AgentNode / OpenAiLlmClient / ShellToolNode）按各自语义修复（待确认）

## 主要修改记录
- A1：5 个节点的 `GetFieldValue(JsonNode?, string)` 逐个删除，调用点改为 `JsonPath.GetValue(...)`；语义等价（`JsonPath` 为超集，额外支持 `items[0]` 数组索引）。
- A2：`MergeNode` 增加 `using System.Text.Json.Nodes;` 与 `using FlowEngine.Core.Scripting;`，`MergeJsonNodes` 与 `GetFieldValue` 中全限定名改短名（`GetFieldValue` 随 A1 一并删除）。
- B2：`WebSearchToolNode.GetApiKeyAsync` 与 `PaginateNode.ApplyAuthHeaderAsync` 的 `catch { return null; }` 改为 `catch (Exception ex) { context.Logger?.LogWarning("...: {Error}", ex.Message); ... }`，不再静默吞异常。
- B3：`JSNode` 超时判定由脆弱的 `ex.GetType().Name.Contains("Timeout")` 改为捕获标准 `TimeoutException`。
- C1（MergeNode / AggregateNode）：`Mode` / `CombineOperation` switch 的静默 `default` 兜底改为 `throw new ArgumentOutOfRangeException(...)`。已确认 `WorkflowSchedulerKernel` 有通用 `catch (Exception)` 将异常转为失败结果并记日志，故抛异常不会冲垮调度器。
