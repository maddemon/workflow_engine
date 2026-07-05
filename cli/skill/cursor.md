# Flow Engine Cursor Rules

## 通用规则

- 使用 Flow Engine CLI 命令管理项目、节点类型、凭据和配置。
- 工作流 DSL 顶层字段：Name、ProjectId、Nodes、Connections、StyleSettings。
- 节点引用变量时使用 `${nodeId.output.field}` 语法。

## 认证命令

- `flowengine login [--url <url>] [--email <email>] [--password <password>]`：JWT 登录
- `flowengine login --api-key <key>`：API Key 登录
- `flowengine me`：查看当前用户
- `flowengine api-keys create --name <name>`：创建 API Key
- `flowengine api-keys list`：列出 API Key
- `flowengine api-keys revoke <id> --confirm`：吊销 API Key

## 资源管理命令

- `flowengine project list`
- `flowengine node-types list [--category <category>]`
- `flowengine credential list [--project-id <id>]`
- `flowengine workflow list [--project-id <id>]`
- `flowengine workflow create --file <file>`
- `flowengine execute <workflow-id> [--wait]`
- `flowengine execution list`
- `flowengine test --file <file>`
