# 通过 AI Agent IDE（MCP）创建/修改/测试工作流

> 本文档基于当前代码编写，以代码为准。涉及工具签名与连接配置以 `mcp-shim/skill/skill.json`、`mcp-shim/skill/claude.md` 及前端 `HelpPage` 为权威来源。

Flow Engine 提供 MCP（Model Context Protocol）技能，让 Claude Code / Cursor / VS Code / Claude Desktop 等 AI 客户端通过自然语言发现节点、装配 / 修改 / 校验 / 确认 / 执行工作流，无需直接编辑 JSON 或 DSL。相关工具见 [系统总览](architecture/overview.md) 第 10 节。

## 1. MCP 工具清单（9 个）

技能暴露以下工具（`mcp-shim/skill/skill.json`）：

| 工具 | 用途 |
|------|------|
| `list_node_catalog` | 列出全部可用节点摘要（可按 `category` 过滤） |
| `get_node_detail` | 查询节点完整参数 schema、输入 / 输出端口、示例 |
| `assemble_workflow` | 用极简草稿（节点 ID + 类型名 + 参数 + 连接）装配工作流，后端自动补全端口 / 坐标 / 入口节点 |
| `modify_workflow` | 对已有工作流执行 add/remove/modify/connect/disconnect/move 操作，返回新草稿 |
| `validate_workflow` | 校验草稿或已有工作流结构，返回 `{ valid, errors[] }`，每条错误含 `nodeId` / `field` / `errorType` / `message` / `suggestedFix` |
| `confirm_workflow` | 校验通过后激活草稿，使其可被执行 |
| `execute_workflow` | 执行工作流，可选 `inputs` 与 `idempotencyKey`；失败时返回 `executionContext` 与 `suggestedFix` 供 AI 自纠 |
| `get_workflow` | 按 ID 获取工作流详情 |
| `list_workflows` | 列出工作流 |

## 2. 获取 MCP 连接配置（含 API Key）

登录 Flow Engine 后，打开 **帮助与 MCP 配置** 页面（前端路由对应 `HelpPage`，标题 `帮助与 MCP 配置`）。该页面会基于你账户下的真实 API Key 动态生成**可直接复制**的客户端配置，API Key 已自动填入。复制整段配置交给你的 Agent（Claude Code / Cursor / VS Code / Claude Desktop），客户端会自行注册 MCP 服务器。

> MCP 鉴权用的 API Key 即来源于此页面；该 Key 仅用于 MCP / API 鉴权，与后文 [凭据管理](credentials.md) 中的业务凭据（如第三方访问令牌）是两套独立体系。

## 3. 客户端连接配置

MCP 端点固定为后端 `/mcp`（默认后端 HTTP 端口 `:8001`，见 [系统总览](architecture/overview.md) 第 12 节）。提供两种连接模式。

### 3.1 远程 HTTP 模式（Claude Code / Cursor / VS Code）

**Cursor** — 项目根目录 `.cursor/mcp.json`（根键为 `mcpServers`）：

```json
{
  "mcpServers": {
    "flowengine": {
      "url": "http://localhost:8001/mcp",
      "headers": { "Authorization": "Bearer <apiKey>" }
    }
  }
}
```

**VS Code** — 项目根目录 `.vscode/mcp.json`（注意根键是 `servers` 不是 `mcpServers`）：

```json
{
  "servers": {
    "flowengine": {
      "url": "http://localhost:8001/mcp",
      "headers": { "Authorization": "Bearer <apiKey>" }
    }
  }
}
```

**Claude Code**：可用 `claude mcp add --transport http flowengine http://localhost:8001/mcp` 注册，并自行在请求头附加 `Authorization: Bearer <apiKey>`。

> `<apiKey>` 直接取自 **帮助与 MCP 配置** 页面生成的配置，无需手动拼装。

### 3.2 stdio 模式（Claude Desktop）

Claude Desktop 不支持远程 HTTP，需将 `mcp-shim/` 垫片注册为 `stdio` 服务器，由垫片把 stdin JSON-RPC 转发到 HTTP 端点。在 Claude Desktop 配置中加入：

```json
{
  "mcpServers": {
    "flowengine": {
      "command": "node",
      "args": ["path/to/mcp-shim/dist/index.js"],
      "env": {
        "FLOWENGINE_URL": "http://localhost:8001",
        "FLOWENGINE_API_KEY": "<apiKey>"
      }
    }
  }
}
```

`FLOWENGINE_URL` 为后端基础地址（**不要**带 `/mcp` 后缀），`FLOWENGINE_API_KEY` 同样来自 **帮助与 MCP 配置** 页面。

## 4. 典型工作流（推荐步骤）

AI 无需构造完整 DSL，只需提供极简草稿，后端自动补全端口、坐标与入口节点。

1. **发现节点** — 调用 `list_node_catalog` 获取节点摘要；对感兴趣的节点调 `get_node_detail` 确认参数与端口 schema。
2. **装配** — 调 `assemble_workflow`，传入 `name`、`nodes`（每项含 `id` / `typeName` / `parameters`）、`connections`（每项含 `from` / `to`，可选 `fromPort` / `toPort`）。返回 `{ draftId, workflow }`。
3. **校验** — 调 `validate_workflow`，返回 `{ valid, errors[] }`；若有错误按 `suggestedFix` 修正后重试。
4. **确认** — 校验通过后调 `confirm_workflow`，传入 `draftId`，草稿激活。
5. **执行** — 调 `execute_workflow`，传入 `workflowId`（可选 `inputs` / `idempotencyKey`）。失败后依据 `executionContext` 与 `suggestedFix` 修正重跑。

即：**list nodes → `assemble_workflow` → `validate_workflow` → 修正 → `confirm_workflow` → `execute_workflow`**。

### 修改已有工作流

1. 调 `get_workflow` 或 `list_workflows` 找到目标。
2. 调 `modify_workflow`，传入 `workflowId` + `operations`（add / remove / modify / connect / disconnect / move）。
3. 返回 `{ draftId, workflow, diff }`，基于新草稿继续 校验 → 确认 → 执行。

## 5. 节点草稿极简格式

`assemble_workflow` 的 `nodes` 项只需：

```json
{ "id": "fetch", "typeName": "httpRequest", "parameters": { "url": "https://api.example.com/data" } }
```

`connections` 项只需：

```json
{ "from": "trigger", "to": "fetch" }
```

后端自动推导端口名（默认取第一个 Output → 第一个 Input）、补全坐标、标记入口节点。节点 / 端口模型见 [工作流模型](concepts/workflow-model.md)；参数中表达式写法见 [表达式](concepts/expressions.md)。

## 6. 凭据引用

节点参数中如需第三方凭据（如 API Token、数据库密码），**只引用凭据 ID，绝不内嵌明文密钥**。凭据在 Flow Engine 中加密存储，运行时由引擎解密注入（详见 [凭据管理](credentials.md)）。若节点引用了不存在的凭据，工具会返回 `CredentialNotFound`，需先在 UI 或 API 创建对应凭据。

## 7. 常见错误与自纠

| 错误 | 原因 | 自纠方法 |
|------|------|---------|
| `NodeNotFound` | `get_node_detail` 传入了不存在的节点名 | 先用 `list_node_catalog` 确认可用节点 |
| `AssembleFailed` | 节点参数不符合类型 schema | 用 `get_node_detail` 查看完整参数 schema，修正 `parameters` |
| `DisconnectedGraph` | 孤立节点未连接 | 确保所有节点通过连接可达，`trigger` 节点需有出口连接 |
| `InvalidInput` | `workflowId` / `draftId` 不是合法 Guid | 检查 ID 是否为标准 UUID 格式 |
| `ExecutionFailed` | 工作流运行时出错 | 查看返回的 `executionContext` 与 `suggestedFix`，修正后重试 |
| `ValidationFailed` + `suggestedFix` | 校验发现结构问题 | 按 `suggestedFix` 修正后重新 `validate_workflow` |
| `CredentialNotFound` | 节点引用了不存在的凭据 | 先在 Flow Engine UI 或 API 创建凭据，再在节点参数引用其 ID |

## 8. 待确认 / 备注

- 各 AI 客户端的 MCP 注册命令细节（如 VS Code / Cursor 的具体菜单路径）随客户端版本变化，以客户端官方文档为准；配置 JSON 的**键名与结构**以上文为准。
- `mcp-shim` 需先构建：在 `mcp-shim/` 下执行 `npm install && npm run build`（即 `tsc`，输出 `dist/index.js`）。
