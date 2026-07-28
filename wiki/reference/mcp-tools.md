# MCP 工具参考（MCP Tools Reference）

> 本文档基于当前代码编写，以代码为准。工具清单与连接方式取自 `mcp-shim/skill/skill.json` 与 `mcp-shim/skill/claude.md`。

## 1. 概述

Flow Engine 通过 MCP 技能（`mcp-shim/`）向 AI Agent（Claude Code / Cursor / VS Code / Claude Desktop）暴露工具。需注意区分两层：

- **技能层（`mcp-shim/skill/skill.json`）**：面向 AI 的**核心 9 个工具**，覆盖「节点发现 → 工作流装配 / 修改 / 校验 / 确认 / 执行」全流程，AI 只需提供极简草稿（节点 id + 类型名 + 参数 + 连接），后端自动补全端口、坐标与入口节点，无需构造完整 DSL。
- **后端 MCP 服务（`FlowEngine.Host/Mcp/Tools/`）**：实际经 `/mcp` 注册的工具共 **15 个**，在 9 个核心工具之外另含凭据查询、草稿反馈、试运行、执行查询等。完整清单以运行期 `tools/list` 返回为准。

核心 9 个工具（`mcp-shim` 技能层）：

```
list_node_catalog   get_node_detail      assemble_workflow
modify_workflow     validate_workflow    confirm_workflow
execute_workflow    get_workflow         list_workflows
```

后端额外注册的 6 个工具（技能层未默认暴露，但 `/mcp` 实际可用）：

```
get_conventions     list_credentials     get_execution
dry_run_workflow    reject_draft         get_draft_feedback
```

## 2. 连接与鉴权

- **HTTP 传输（Claude Code / Cursor / VS Code）**：指向 `https://host:port/mcp`，请求头附加 `Authorization: Bearer <apiKey>`。
  - VS Code 的 `.vscode/mcp.json` 根键为 `servers`（非 `mcpServers`）。
- **stdio 传输（Claude Desktop）**：经 `mcp-shim/dist/index.js` 垫片转发，环境变量 `FLOWENGINE_URL` + `FLOWENGINE_API_KEY`。

> 后端端点 `/mcp` 经 `MapMcp("/mcp").RequireAuthorization()` 注册，必须携带 API Key（Bearer）方可调用。API Key 由 `POST /api/v1/auth/api-keys` 创建（见 [REST API 概览](rest-api.md)）。

> 本文列出的 15 个工具取自 `FlowEngine.Host/Mcp/Tools/` 源码（`[McpServerTool]` 标注）。技能层（`mcp-shim`）默认只暴露其中 9 个核心工具；**完整且最新的工具集合以运行期 MCP `tools/list` 返回为准**。

## 3. 工具详表

### 3.1 `list_node_catalog`
- **用途**：列出全部可用节点摘要，用于发现能力。
- **输入**：`category`（可选，按分类过滤）。
- **输出**：`AiNodeSummary[]`（节点名、分类、简述）。

### 3.2 `get_node_detail`
- **用途**：获取单个节点的完整定义。
- **输入**：`name`（节点类型名）。
- **输出**：`AiNodeDefinition`，含输入 schema、输出 schema、端口定义、示例。
- **常见错误**：传入不存在的 `name` → `NodeNotFound`。

### 3.3 `assemble_workflow`
- **用途**：装配工作流草稿（后端自动补全端口 / 坐标 / 入口节点）。
- **输入**：
  - `name`：工作流名称。
  - `nodes[]`：每项 `{ id, typeName, parameters }`（参数仅需提供业务字段，端口自动推导）。
  - `connections[]`：每项 `{ from, to, fromPort?, toPort? }`（端口缺省取第一 Output → 第一 Input）。
- **输出**：`{ draftId, workflow }`。
- **常见错误**：参数不符 schema → `AssembleFailed`；孤立节点 → `DisconnectedGraph`。

### 3.4 `modify_workflow`
- **用途**：在已有工作流上做增量修改，产出新草稿。
- **输入**：
  - `workflowId`：目标工作流 Guid。
  - `operations[]`：`add` / `remove` / `modify` / `connect` / `disconnect` / `move`。
- **输出**：`{ draftId, workflow, diff }`。
- **后续**：基于返回草稿继续 `validate_workflow` → `confirm_workflow` → `execute_workflow`。

### 3.5 `validate_workflow`
- **用途**：校验工作流结构合法性。
- **输入**：草稿的 `nodes` + `connections`，或已有的 `workflowId`。
- **输出**：`{ valid, errors[] }`，每条错误含 `nodeId` / `field` / `errorType` / `message` / `suggestedFix`，供 AI 自纠。
- **常见错误**：`ValidationFailed` + `suggestedFix`。

### 3.6 `confirm_workflow`
- **用途**：将校验通过的草稿激活为可执行工作流。
- **输入**：`draftId`（来自 `assemble_workflow` / `modify_workflow`）。
- **输出**：激活后的 `WorkflowDto`（状态变为可执行）。

### 3.7 `execute_workflow`
- **用途**：执行已激活工作流。
- **输入**：
  - `workflowId`：Guid。
  - `inputs`（可选）：执行输入。
  - `idempotencyKey`（可选）：幂等键。
- **输出**：执行结果；失败时返回结构化反馈 `{ executionContext, suggestedFix }` 供 AI 自纠。
- **常见错误**：`ExecutionFailed`、引用不存在凭据 → `CredentialNotFound`。

### 3.8 `get_workflow`
- **用途**：获取单个工作流定义。
- **输入**：`workflowId`（Guid）。
- **输出**：`WorkflowDto`（含节点与连接）。

### 3.9 `list_workflows`
- **用途**：列出工作流。
- **输入**：分页 / 过滤参数以 MCP 工具实现（skill 定义）为准。
- **输出**：工作流摘要列表。

### 3.10 `get_conventions`（后端额外工具）
- **用途**：获取项目编码规约 / 约定，供 AI 在生成或修改工作流时遵循。
- **输入**：无（或按实现约定的作用域参数）。
- **输出**：规约文本 / 结构化约定。

### 3.11 `list_credentials`（后端额外工具）
- **用途**：列出可用凭据（脱敏），便于在节点参数中引用凭据 ID。
- **输入**：过滤参数以实现为准。
- **输出**：凭据摘要列表（不含明文密钥）。

### 3.12 `get_execution`（后端额外工具）
- **用途**：查询某次执行的详情与上下文。
- **输入**：`executionId`（Guid）。
- **输出**：执行结果 / 上下文，失败时含 `suggestedFix` 供自纠。

### 3.13 `dry_run_workflow`（后端额外工具）
- **用途**：试运行工作流（不真正提交副作用 / 仅校验与预览）。
- **输入**：同 `execute_workflow`（`workflowId` / `inputs` 等）。
- **输出**：试运行结果。

### 3.14 `reject_draft`（后端额外工具）
- **用途**：拒绝（打回）一个待审查草稿。
- **输入**：`draftId`。
- **输出**：草稿状态更新结果。

### 3.15 `get_draft_feedback`（后端额外工具）
- **用途**：获取草稿的审查反馈（供 AI 按反馈修正）。
- **输入**：`draftId`。
- **输出**：反馈内容（问题点 / 建议）。

## 4. 推荐 AI 工作流

1. `list_node_catalog` → `get_node_detail` 发现并理解节点。
2. `assemble_workflow` 装配草稿。
3. `validate_workflow` 校验，按 `suggestedFix` 修正。
4. `confirm_workflow` 激活草稿。
5. `execute_workflow` 执行，失败按结构化反馈自纠。
6. 修改既有工作流：`get_workflow` / `list_workflows` → `modify_workflow` → 回到 3。

## 5. 常见错误与自纠

| 错误 | 原因 | 自纠 |
|------|------|------|
| `NodeNotFound` | `get_node_detail` 传入不存在的节点名 | 先用 `list_node_catalog` 确认 |
| `AssembleFailed` | 节点参数不符类型 schema | 用 `get_node_detail` 查 schema 后修正 |
| `DisconnectedGraph` | 孤立节点未连接（trigger 需有出口） | 补全连接 |
| `InvalidInput` | workflowId / draftId 非合法 Guid | 检查 UUID 格式 |
| `ValidationFailed` + `suggestedFix` | 结构问题 | 按建议修正后重新 `validate_workflow` |
| `ExecutionFailed` | 运行时出错 | 看 `executionContext` / `suggestedFix` 修正 |
| `CredentialNotFound` | 节点引用不存在的凭据 | 先在 UI / API 创建凭据 |

> 草稿（draft）经 `confirm_workflow` 前不可执行；`assemble` / `modify` 仅产出 `draftId`。
