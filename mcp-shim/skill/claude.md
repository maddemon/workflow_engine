# Flow Engine AI Agent Skill (MCP)

你是 Flow Engine AI Agent 的助手，通过 MCP 工具帮助用户发现节点、组装/修改/校验/确认/执行工作流。

## 快速开始

### 客户端连接配置

#### 远程 HTTP（Claude Code / Cursor / VS Code）

**Claude Code:**

```bash
claude mcp add --transport http flowengine https://host:port/mcp
# 需在请求头中附加 Authorization: Bearer <apiKey>
```

**Cursor** — 项目根目录 `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "flowengine": {
      "url": "https://host:port/mcp",
      "headers": { "Authorization": "Bearer <apiKey>" }
    }
  }
}
```

**VS Code** — 项目根目录 `.vscode/mcp.json`（注意根键是 `servers` 不是 `mcpServers`）:

```json
{
  "servers": {
    "flowengine": {
      "url": "https://host:port/mcp",
      "headers": { "Authorization": "Bearer <apiKey>" }
    }
  }
}
```

#### stdio（Claude Desktop）

使用 MCP Shim 垫片将 stdin JSON-RPC 转发到 HTTP 端点：

```json
{
  "mcpServers": {
    "flowengine": {
      "command": "node",
      "args": ["path/to/mcp-shim/dist/index.js"],
      "env": {
        "FLOWENGINE_URL": "https://host:port",
        "FLOWENGINE_API_KEY": "<apiKey>"
      }
    }
  }
}
```

## MCP 工具驱动的 AI 工作流

AI 不需要构造完整 DSL，只需提供极简草稿（节点 ID + 类型名 + 参数 + 连接），后端自动补全端口、坐标、入口节点。

### 推荐步骤

1. **发现节点**

   调用 `list_node_catalog` 获取全部节点摘要，可选按 `category` 过滤。

   对感兴趣的节点调用 `get_node_detail`，确认其输入 schema、输出 schema、端口定义和示例。

2. **装配工作流**

   调用 `assemble_workflow`，传入：
   - `name`（工作流名称）
   - `nodes`（每项含 `id`、`typeName`、`parameters`）
   - `connections`（每项含 `from`、`to`，可选 `fromPort`/`toPort`）

   后端自动补全端口、坐标、入口节点，返回 `{ draftId, workflow }`。

3. **校验工作流**

   调用 `validate_workflow`，传入草稿的 nodes + connections 或已有 workflowId。

   返回 `{ valid, errors[] }`，每条错误含 `nodeId`、`field`、`errorType`、`message`、`suggestedFix`，供 AI 自纠。

4. **确认草稿**

   校验通过后调用 `confirm_workflow`，传入 `draftId`。

   草稿激活后即可执行。

5. **执行工作流**

   调用 `execute_workflow`，传入 `workflowId`，可选 `inputs` 和 `idempotencyKey`。

   执行失败时返回结构化反馈（含 `executionContext`、`suggestedFix`），供 AI 自纠。

### 修改已有工作流

1. 调用 `get_workflow` 或 `list_workflows` 找到目标工作流。
2. 调用 `modify_workflow`，传入 `workflowId` + `operations`（add/remove/modify/connect/disconnect/move）。
3. 返回 `{ draftId, workflow, diff }`，基于新草稿继续校验→确认→执行。

## 节点草稿极简格式

`assemble_workflow` 的 nodes 项只需：

```json
{ "id": "fetch", "typeName": "httpRequest", "parameters": { "url": "https://api.example.com/data" } }
```

connections 项只需：

```json
{ "from": "trigger", "to": "fetch" }
```

后端自动推导端口名（默认取第一个 Output → 第一个 Input）、自动补全坐标、自动标记入口节点。

## 常见错误与自纠

| 错误 | 原因 | 自纠方法 |
|------|------|---------|
| `NodeNotFound` | `get_node_detail` 传入了不存在的节点名 | 先用 `list_node_catalog` 确认可用节点 |
| `AssembleFailed` | 节点参数不符合类型 schema | 用 `get_node_detail` 查看完整参数 schema，修正 parameters |
| `DisconnectedGraph` | 孤立节点未连接 | 确保所有节点通过连接可达，trigger 节点需有出口连接 |
| `InvalidInput` | workflowId / draftId 格式不是合法 Guid | 检查 ID 是否为标准 UUID 格式 |
| `ExecutionFailed` | 工作流运行时出错 | 查看返回的 `executionContext` 和 `suggestedFix`，修正参数后重试 |
| `ValidationFailed` + `suggestedFix` | 校验发现结构问题 | 按 `suggestedFix` 修正后重新 `validate_workflow`，通过后再 `confirm_workflow` |
| `CredentialNotFound` | 节点引用了不存在的凭据 | 需在 Flow Engine UI 或 API 中先创建凭据，再在节点参数中引用 |
