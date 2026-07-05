# Task 4 报告：移动 AgentDtos 到 Core 并增强 InlineResolver 记录迭代

## 完成状态

DONE

## 变更摘要

1. **移动 AgentDtos**
   - 新建 `backend/FlowEngine.Core/Dtos/AgentDtos.cs`，命名空间改为 `FlowEngine.Core.Dtos`。
   - 删除原 `backend/FlowEngine.Application/Dtos/AgentDtos.cs`。
   - 为使 Step 3 中的 `AgentIterationDto.StartedAt` / `CompletedAt` 能直接赋 ISO 8601 字符串，将这两个属性从 `DateTime?` 调整为 `string?`（与前端 `AgentIteration.startedAt/completedAt: string | null` 对齐）。

2. **扩展 InlineResolverResult**
   - 在 `backend/FlowEngine.Runtime/Agent/InlineResolverResult.cs` 中添加 `using FlowEngine.Core.Dtos;`。
   - 将 `Iterations` 从 `int` 改为 `List<AgentIterationDto>`。

3. **增强 InlineResolver**
   - 在 `backend/FlowEngine.Runtime/Agent/InlineResolver.cs` 中：
     - 每次循环记录 `iterationStartedAt`，LLM 调用与工具调用完成后构造 `AgentIterationDto`。
     - 新增内部 `ToolResult` 记录，改造 `ExecuteToolAsync` 返回结构化结果（含 `ToolCallId`、`ToolName`、`Input`、`Output`、`Success`、`Error`），再映射为 `ToolCallRecordDto`。
     - 循环结束后返回包含完整 `Iterations` 列表的 `InlineResolverResult`。

4. **更新测试**
   - 调整 `tests/FlowEngine.Runtime.Tests/Plugins/AgentEnhanceTests.cs` 中断言 `result.Iterations` 的用法：改为 `result.Iterations.Count` 或 `Assert.Single`。

## 验证结果

- `dotnet build FlowEngine.sln`：通过。
- `dotnet test tests/FlowEngine.Runtime.Tests/FlowEngine.Runtime.Tests.csproj --filter "FullyQualifiedName~InlineResolver"`：5 个测试全部通过。
- `dotnet test --no-build`：全量 414 个测试全部通过。

## 提交记录

- `6cb228b` Task 4: 移动 AgentDtos 到 Core 并增强 InlineResolver 记录迭代

## 注意事项

- 当前没有其他代码引用 `AgentExecutionResultDto` 等类型，因此移动命名空间后无需大量修改 `using`。
- `InlineResolverResult.Iterations` 的类型由 `int` 变为 `List<AgentIterationDto>`，调用方需相应调整；测试中已完成适配。
- 取消令牌在循环顶部或工具调用期间触发时，会记录已完成的迭代/工具调用并以 `Cancelled` 结束；取消发生在 LLM 流式调用期间时通过 `OperationCanceledException` 以 `Cancelled` 结束且不记录该次迭代。
