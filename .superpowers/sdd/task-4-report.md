# Task 4: Agent 节点基础 — 完成报告

## 状态

**完成** — 全部 4 个阶段已实现，构建通过，180 个测试全部通过（含 14 个新增 Agent 测试）。

## 交付物

### 阶段一：Agent 节点与端口定义

- **AgentNode** (`plugins/FlowEngine.Plugins.Standard/AgentNode.cs`) — 实现 `INodeType`，`TypeName = "agent"`
- 4 个端口定义：
  - `input` (Main, Input) — 主数据输入
  - `output` (Main, Output) — 主数据输出
  - `tools` (AgentTool, Output) — 工具连接端口
  - `llmSupply` (LLMSupply, Input) — LLM 供应端口
- 节点参数：`MaxIterations`（默认 10）、`TimeoutSeconds`（可选）、`PromptTemplate`（系统提示词）
- 注册到 NodeRegistry 后可在前端节点面板可见

### 阶段二：工具收集

- **CollectTools** 方法扫描 `tools` 端口的下游连接，为每个工具节点生成 `ToolDefinition`
- 从 `NodeTypeDescriptor.Parameters` 推导 JSON Schema（`ParametersSchema`）
- 无连接时返回空列表

### 阶段三：执行循环与迭代限制

- Agent 循环：调用 LLM → 解析 tool_calls → 执行工具 → 结果回填 → 继续循环
- 无 tool_calls 时返回最终文本结果
- 最大迭代次数限制（可配置）
- LLM 调用超时控制（通过 `CancellationTokenSource.CancelAfter`）

### 阶段四：工具调用映射

- LLM tool_calls 解析为引擎内部执行请求
- 通过 `INodeRegistry` 查找工具节点类型，创建简化 `NodeExecutionContext` 执行
- 工具执行结果格式化后回填 LLM
- 工具执行异常不导致 Agent 崩溃（返回错误消息继续循环）

## 核心接口

### ILlmClient (`backend/FlowEngine.Core/Abstractions/ILlmClient.cs`)

```csharp
public interface ILlmClient
{
    Task<LlmResponse> ChatAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default);
}
```

由 plan-alpha-07 LLM 供应节点实现。当前 Agent 节点通过 `NodeExecutionContext.LlmClient` 获取。

### 数据模型 (`backend/FlowEngine.Core/Entities/`)

- `LlmMessage` — 对话消息（role, content, toolCallId, toolCalls）
- `LlmToolCall` — 工具调用（id, name, arguments）
- `LlmResponse` — LLM 响应（content, toolCalls, HasToolCalls）

## 修改的现有文件

- `backend/FlowEngine.Core/Entities/NodeExecutionContext.cs` — 新增 `ILlmClient?` 和 `INodeRegistry?` 属性
- `backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs` — 接受可选 `ILlmClient?` 参数并传递到上下文

## 测试覆盖

| 测试 | 覆盖内容 |
|------|----------|
| `AgentNode_Has_Correct_TypeName` | TypeName 属性 |
| `AgentNode_Has_Correct_Ports` | 4 端口定义正确 |
| `AgentNode_Default_Parameters` | 默认参数值 |
| `ExecuteAsync_Returns_Error_When_No_LlmClient` | 缺少 LLM 客户端时返回错误 |
| `ExecuteAsync_Calls_LLM_With_No_Tools` | 无工具时直接调用 LLM |
| `ExecuteAsync_Collects_Tools_From_Connections` | 从工作流连接收集工具 |
| `ExecuteAsync_Returns_Empty_Tools_When_No_Connections` | 无连接时返回空工具列表 |
| `ExecuteAsync_Executes_Tool_And_Feeds_Back_To_LLM` | 完整 Agent 循环 |
| `ExecuteAsync_Stops_After_MaxIterations` | 最大迭代次数限制 |
| `ExecuteAsync_Handles_LLM_Error` | LLM 异常处理 |
| `ExecuteAsync_Tool_Not_Found_Returns_Error_Message` | 未知工具错误处理 |
| `ExecuteAsync_Passes_Input_To_LLM` | 输入数据传递给 LLM |
| `ExecuteAsync_Uses_PromptTemplate_As_System_Message` | 系统提示词模板 |
| `ExecuteAsync_Tool_Result_Fed_Back_To_LLM` | 工具结果回填 LLM |

## Fix Round 1

**日期**: 2026-06-21

### 修复内容

#### 1. [Critical] Missing NodeExecutionRecord for tool execution

`AgentNode.ExecuteToolAsync` 现在在每次工具执行后创建 `NodeExecutionRecord` 并通过 `parentContext.ExecutionStore.AddNodeRecordAsync` 写入执行存储，提供工具调用的审计追踪。异常路径同样记录失败的执行记录。

**变更文件**:
- `plugins/FlowEngine.Plugins.Standard/AgentNode.cs` — `ExecuteToolAsync` 方法增加审计记录
- `backend/FlowEngine.Core/Entities/NodeExecutionContext.cs` — 新增 `IExecutionStore? ExecutionStore` 属性
- `backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs` — 接受并传递 `IExecutionStore?`
- `backend/FlowEngine.Host/Program.cs` — DI 注册传递 `IExecutionStore`

#### 2. [Important] No test for LLM timeout path

新增 `ExecuteAsync_Returns_Timeout_When_LLM_Calls_Timed_Out` 测试：通过 `CancellationTokenSource.CancelAfter(50ms)` 模拟超时，MockLlmClient 异步等待 10 秒触发取消，验证返回 `AgentTimeout` 错误码。

#### 3. [Important] Tool execution bypasses NodeExecutionContextFactory

在 `ExecuteToolAsync` 方法头部添加 TODO 注释，说明当前手动构造 `NodeExecutionContext` 的限制及后续全量工厂集成计划。基本参数拷贝已确保工作正常。

### 新增测试

| 测试 | 覆盖内容 |
|------|----------|
| `ExecuteAsync_Returns_Timeout_When_LLM_Calls_Timed_Out` | LLM 超时路径，CancellationToken 触发 AgentTimeout |
| `ExecuteAsync_Creates_NodeExecutionRecord_For_Tool_Execution` | 工具执行生成 NodeExecutionRecord 写入 ExecutionStore |

### 构建与测试

- `dotnet build` — 0 错误，0 警告
- `dotnet test` — 182 测试全部通过（含 16 个 Agent 测试）
