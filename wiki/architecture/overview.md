# 系统总览（Overview）

> 本文档基于当前代码编写，以代码为准。涉及接口签名与数据模型以源码为权威来源。

## 1. 一句话定位

Flow Engine 是一个节点可**热插拔**的工作流自动化引擎。前端用 React/TypeScript 做可视化编排，构建后由后端统一托管；后端用 .NET **确定性**地执行工作流，节点通过 DLL 插件扩展。默认以单机后台服务形态运行，并预留横向扩展能力。

## 2. 整体形态

- 单一 .NET 后台服务进程同时承载：HTTP API、前端静态资源（`wwwroot`）、执行引擎、Quartz 调度器、Webhook 路由、MCP 端点（`/mcp`）。
- 前端构建产物输出到 `backend/FlowEngine.Host/wwwroot`，由后端经 `UseStaticFiles` + `MapFallbackToFile("index.html")` 托管。
- 开发期前端用 Vite dev server（`:4000`），将 `/api` 代理到后端（`:8001`）。

## 3. 后端分层与依赖方向

6 个核心后端项目：

| 项目 | 职责 | 依赖 |
|------|------|------|
| `FlowEngine.Core` | 实体、抽象契约、值对象，及下沉的脚本/HTTP/Agent/Tools 类型；承载 `FlowEngineDbContext` | EF Core + Jint + Logging |
| `FlowEngine.Runtime` | 执行引擎、表达式/脚本求值、等待区、快照 | Core + Logging |
| `FlowEngine.Application` | 用例编排：`Workflows` / `Executions` / `Orchestrators` / `Tools` / `DTOs` | Core + Runtime |
| `FlowEngine.Infrastructure` | 持久化、调度（Quartz）、事件总线、凭据加密（AES-GCM）、文件存储 | Core + Application（实现其接口） |
| `FlowEngine.Migrations` | EF Core 迁移程序集 | Core + Infrastructure |
| `FlowEngine.Host` | 组合根：`Controllers` / `WebSocketHandlers` / `Middlewares` / `BackgroundServices` / `wwwroot` | 全部 |

另有辅助工程 `FlowEngine.Analyzers` 与 `FlowEngine.Resources`。

**依赖方向**：`Host → Application → Runtime → Core`；`Plugins → Core` 单向（插件**禁止**引用 `Application` 或 `Runtime`，否则产生循环依赖与执行死锁）。

## 4. 节点与插件（热插拔）

- 节点继承 `NodeBase`，使用 `[NodeMeta]` / `[Port]` / `[Required]` / `[Hint]` 等特性声明元信息与参数；公共属性即参数，`[Inject]` 标记运行时注入的依赖（如 `NodeExecutionContext`、`IExecutionLogger`）。
- 插件编译为 DLL 放入 `plugins/`，启动时经独立的 `AssemblyLoadContext` 加载并注册到节点注册中心；单个插件加载失败仅记警告，不影响主程序启动。
- 内置标准节点位于 `plugins/FlowEngine.Plugins.Standard`（HTTP、Code、If、Loop、Merge、Agent、LLM、DB 等）。

## 5. 表达式与脚本模型（重点）

- 表达式/脚本以 **JavaScript 语法**经 **Jint** 求值，运行在**受限沙箱**中：默认不调用 `AllowClr()`，并按白名单裁剪全局对象，封死借 `constructor` / 原型链访问 .NET 类型的路径。
- **不支持 `{{ }}` mustache 模板语法**——校验阶段直接拒绝含 `{{` / `}}` 的内容。
- 可用变量（由 `ExecutionScope` 注入）：
  - **逐项**：`$json`（当前 item）、`$input`（含 `.params` / `.context` / `.item()` / `.all()`）、`$itemIndex`、`$runIndex`。
  - **全局**（来自 `context.GlobalVariables`）：`$credentials`、`$env`、`$workflow`、`$execution`、`$vars`、`$now`、`$today`、`$node`、`$ctx`、`$items`（`$items()` 取当前输入批次 item 列表；`$items("节点名")` 取指定上游节点最新输出批次 item 列表）。
- 示例：`$json.status === 'active'`、`$input.item().count > 10`、`$credentials.x.accessToken`。

## 6. 执行引擎

- 按 DAG 拓扑顺序执行节点；支持多输入等待（`WaitingArea`）、分支、重试、取消与 Saga 补偿。
- 单个节点执行流程：解析参数 → 解密凭据 → 执行 → 输出给下游。
- 执行事件经 WebSocket 推送，前端实时高亮节点并展示输出。

## 7. 持久化

- 默认 **SQLite**（零配置启动）；EF Core 已接入 **SQLite 与 PostgreSQL** 提供器，可按部署切换数据库连接（DB 节点另可通过原始 ADO 驱动直连 MySQL / SQL Server）。
- 凭据加密存储（AES-GCM），运行时解密注入。

## 8. 触发器

- 定时（Quartz.NET）、Webhook、轮询触发器，用于启动工作流执行。

## 9. 凭据与安全

- 凭据静态加密（AES-GCM），运行时解密注入；明文不落日志、不返回前端。
- 节点插件经独立 `AssemblyLoadContext` 隔离加载。
- 表达式与代码执行节点运行在受限沙箱中。
- Webhook 入口支持签名验证或来源白名单。

## 10. AI 与 MCP

- 提供 MCP 技能（`mcp-shim/`），暴露 `list_node_catalog`、`get_node_detail`、`assemble_workflow`、`modify_workflow`、`validate_workflow`、`confirm_workflow`、`execute_workflow`、`get_workflow`、`list_workflows` 等工具。
- Agent IDE（Claude Code / Cursor / VS Code / Claude Desktop）经 MCP 发现节点并装配 / 修改 / 校验 / 执行工作流。
- 登录后于 **帮助与 MCP 配置** 页面获取完整 MCP 配置（含 API Key），复制交给 Agent 即可自行注册。

## 11. 前端

- 技术栈：React 19 + TypeScript（严格模式）+ Vite + Mantine + React Flow（`@xyflow/react`）+ ahooks。
- 主要模块：画布编辑器（拖拽 / 连线 / 撤销重做）、节点面板（拉取 `GET /api/node-types`）、参数面板（按节点描述自动渲染、条件显隐与校验）、执行实时视图（WebSocket）。

## 12. 默认端口

| 服务 | 端口 |
|------|------|
| 后端 HTTP | `:8001`（HTTPS `:8002`） |
| MCP 端点 | `/mcp`（同后端地址） |
| 前端 dev server | `:4000`（代理 `/api` → `:8001`） |
