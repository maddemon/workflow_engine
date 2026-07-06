# Flow Engine AI Agent Skill

你是 Flow Engine AI Agent CLI 的助手，帮助用户编写、调试和执行工作流。

## 快速开始

### 1. 认证

推荐使用 API Key 认证（长期有效，无需刷新）：

```bash
flowengine login --url <server-url> --api-key <key> --json
```

密码认证（JWT token 会过期，不推荐 AI 使用）：

```bash
flowengine login --url <server-url> --email <email> --password <password> --json
```

开发环境默认管理员：`admin@flowengine.local` / `admin123`（首次登录后请立即创建 API Key）

创建 API Key：

```bash
flowengine api-keys create --name "ai-agent" --json
```

### 2. 验证连接

```bash
flowengine me --json
flowengine node-types list --json
```

## CLI 命令参考

所有命令支持 `--json` 输出结构化数据，支持 `--verbose` 查看请求详情。

- `login [--url <url>] [--email <email>] [--password <password>] [--api-key <key>]`：登录并保存认证信息
- `logout`：登出当前会话
- `me`：获取当前用户信息
- `profile`：显示当前 profile 认证信息
- `api-keys create --name <name> [--expires-at <date>]`：创建 API Key
- `api-keys list`：列出 API Key
- `api-keys revoke <id> --confirm`：吊销 API Key
- `config get`：获取当前配置
- `config set <key> <value>`：设置配置项（baseUrl、email）
- `config use-profile <name>`：切换默认 profile
- `config list-profiles`：列出所有已保存的 profile
- `node-types list [--category <category>]`：列出节点类型
- `node-types get <typeName>`：查看单个节点类型详情（含完整参数和端口定义）
- `project list`：列出项目
- `project get <id>`：查看项目详情
- `credential list [--project-id <id>]`：列出凭据
- `credential create --name <name> --type <type> --fields <json>`：创建凭据
- `credential ensure --name <name> --type <type> --fields <json>`：幂等创建/更新凭据
- `workflow create --file <file> [--name <name>] [--project-id <id>] [--dry-run]`：创建工作流
- `workflow list [--project-id <id>]`：列出工作流
- `workflow get <id> [--version <N>]`：查看工作流详情
- `workflow update <id> [--file <file>] [--name <name>] [--active <bool>]`：更新工作流
- `workflow delete <id> --confirm`：删除工作流
- `workflow export <id> [--output <file>]`：导出工作流
- `workflow import <file> [--project-id <id>]`：导入工作流
- `execute <workflow-id> [--wait] [--timeout <seconds>] [--input <json>]`：执行工作流
- `execution get <id>`：查看执行详情
- `execution list --workflow <id>`：列出执行记录
- `execution cancel <id>`：取消执行
- `test --file <file> [--expect <file>] [--credentials <json>]`：Dry-Run 测试工作流
- `trigger list [--workflow <id>]`：列出触发器
- `trigger create --workflow <id> --type <type> [--name <name>]`：创建触发器
- `guide [--output <file>]`：生成 DSL 编写指南（含节点类型清单）

## 工作流 DSL 规范

所有字段名为 camelCase。

### 顶层结构

```json
{
  "name": "string (required)",
  "projectId": "string (optional)",
  "nodes": [],
  "connections": [],
  "styleSettings": { "layoutDirection": "vertical" | "horizontal" }
}
```

### 节点对象

```json
{
  "id": "string (required, 任意唯一标识)",
  "typeName": "string (required, 如 manualTrigger、set、script、httpRequest、if、llm、agent)",
  "name": "string (required, 显示名称)",
  "parameters": {},
  "ports": [
    { "name": "string", "direction": "Input" | "Output" (PascalCase), "type": "Main" | "AgentTool" | "LLM" | "Memory" }
  ],
  "positionX": 100,
  "positionY": 200,
  "isEntry": true,
  "errorStrategy": "Terminate" | "Continue" | "Retry",
  "timeout": "00:05:00"
}
```

**重要**：
- `ports` 是必需字段，必须与节点类型定义的端口一致
- 使用 `flowengine node-types get <typeName> --json` 查看节点类型的完整端口和参数定义
- `direction` 枚举值：`"Input"` 或 `"Output"`（字符串，不是数字）
- `type` 枚举值：`"Main"`、`"AgentTool"`、`"LLM"`、`"Memory"`

### 连接对象

```json
{
  "id": "string (required, 任意唯一标识)",
  "sourceNodeId": "string (required)",
  "sourcePortName": "string (required, 必须是 Output 端口)",
  "targetNodeId": "string (required)",
  "targetPortName": "string (required, 必须是 Input 端口)",
  "condition": "string (optional)"
}
```

### 常见节点端口参考

| typeName | 入口端口 | 出口端口 |
|----------|---------|---------|
| manualTrigger | — | Output (Main) |
| set | Input (Main) | Output (Main) |
| script | Input (Main) | Output (Main) |
| httpRequest | Input (Main) | Output (Main) |
| if | Input (Main) | True (Main), False (Main) |
| llm | — | Output (Main) |
| agent | Input (Main) | Output (Main), Input (AgentTool), Input (AgentTool) |
| loop | Input (Main) | Output (Main), Done (Main) |
| merge | Input (Main), Input (Main) | Output (Main) |

> 以上为简化参考，完整端口定义请用 `flowengine node-types get <typeName> --json` 获取。

### 凭据引用

对于 Credential 类型参数，需在 parameters 中传入已创建凭据的 Guid：

```bash
flowengine credential create --name "my-api" --type "httpBasicAuth" --fields '{"username":"xxx","password":"yyy"}' --json
# 返回 id，在节点 parameters 中使用
```

### 创建前验证

使用 `--dry-run` 或 `test` 命令验证工作流定义：

```bash
flowengine workflow create --file workflow.json --dry-run --json
flowengine test --file workflow.json --json
```

## 常见错误

| 错误 | 原因 | 解决方法 |
|------|------|---------|
| 端口方向枚举转换失败 | direction 写成了 Out/In | 使用 `"Input"` / `"Output"` 完整枚举值 |
| 端口类型枚举转换失败 | type 写成了 Flow | 使用 `"Main"` / `"AgentTool"` / `"LLM"` / `"Memory"` |
| 工作流验证失败 | 节点缺少 ports | 每个节点必须包含 ports 数组 |
| CredentialNotFound | 凭据不存在 | 先用 `credential create` 创建凭据 |
| DisconnectedGraph | 孤立节点 | 确保所有节点通过连接可达 |
