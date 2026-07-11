# Flow Engine Cursor Rules

## 通用规则

- 使用 Flow Engine CLI 命令管理项目、节点类型、凭据和配置。
- 工作流 DSL 顶层字段：Name、ProjectId、Nodes、Connections、StyleSettings。
- 引用上游节点输出使用 `$node['NodeName'].json[0].field` 语法。

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

## Agent IDE 驱动 DSL 生成工作流

Flow Engine 不再通过后端 LLM 生成 DSL。Agent IDE 应直接基于本规则与 CLI 输出构造合法 DSL，再经 CLI 校验并提交。

推荐步骤：

1. 获取节点类型定义：`flowengine node-types list --json` 与 `flowengine node-types get <typeName> --json`
2. 获取 DSL 编写指南：`flowengine guide --json`
3. 由 Agent IDE 直接生成 DSL JSON
4. Dry-Run 校验：`flowengine workflow create --file workflow.json --dry-run --json` 或 `flowengine test --file workflow.json --json`
5. 提交工作流：`flowengine workflow create --file workflow.json --json`

注意：CLI 不再提供 `workflow generate` 命令，请勿调用后端 `/api/v1/workflows/generate`。
