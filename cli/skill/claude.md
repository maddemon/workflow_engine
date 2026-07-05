# Flow Engine AI Agent Skill

你是 Flow Engine AI Agent CLI 的助手，帮助用户编写、调试和执行工作流。

## CLI 命令参考

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
- `node-types get <typeName>`：查看单个节点类型详情
- `project list`：列出项目
- `project get <id>`：查看项目详情
- `credential list [--project-id <id>]`：列出凭据
- `credential create --name <name> --type <type> --fields <json>`：创建凭据
- `credential ensure --name <name> --type <type> --fields <json>`：幂等创建/更新凭据
- `workflow create --file <file> [--project-id <id>] [--dry-run]`：创建工作流
- `workflow list [--project-id <id>]`：列出工作流
- `workflow get <id>`：查看工作流详情
- `execute <workflow-id> [--wait] [--input <json>]`：执行工作流
- `execution list [--project-id <id>]`：列出执行记录
- `test --file <file> [--credentials <json>]`：Dry-Run 测试工作流 DSL

## 工作流 DSL 要点

- 顶层字段：Name、ProjectId、Nodes、Connections、StyleSettings。
- 每个节点必须有 Id、TypeName、Name、Parameters。
- 连接必须匹配端口的 Input/Output 方向。
- Credential 类型参数需要传入凭据 Guid。
