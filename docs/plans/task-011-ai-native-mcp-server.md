# 任务：AI-native MCP Server（task-011-ai-native-mcp-server）

## 目标

把 task-010 已完成、且经整体 review 验证的 AI-native 能力（Catalog API + Workflow 组装/修改/校验/确认/执行）通过 **MCP 协议**暴露给外部 AI（Claude Code / Cursor / VS Code / ChatGPT Connectors / Claude Desktop），让 AI 以"工具直调"方式生成与修改工作流。

本任务是设计文档 `docs/designs/2026-07-12-ai-native-workflow-engine.md` §10.1 **Phase 6（MCP Server）** 的精化实现，也是 `plan-enterprise-04-mcp.md`「阶段一：MCP Server 暴露」的本轮落地切片。**范围仅限 Catalog + AI-native Workflow 工具暴露**；MCP Client 节点、ToolCollection 注册属该计划其余阶段，不在本任务。

**本任务同时退役现有 `cli/` 的 AI 命令集**：`cli/` 当前仅服务于 AI（bash → REST 传输层），其存在理由已被 MCP 更好满足。退役后整个 `cli/` 目录删除，仅以独立仓库根 `mcp-shim/` 包（极简 stdio 垫片）形式留存（见 Phase 2）。

## 关键决策（经讨论确认）

| 决策           | 结论                                                                                          | 理由                                                                                                                                                                                      |
| -------------- | --------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 主传输         | **Streamable HTTP**（MCP 2025-03-26 规范，废弃旧 SSE）                                        | Flow Engine 是"按 IP+port 可达的服务"（本机/局域网/外网），远程 HTTP 才匹配该部署模型；stdio 只能本地子进程，做不到远程连                                                                 |
| Server 位置    | **C# `FlowEngine.Host` 内 `/mcp` 端点**                                                       | 单一可部署物、复用同一套鉴权与 DI、真正符合"IP:port 连 Flow Engine"；工具逻辑直接调 Application 服务，无需 HTTP 回环                                                                      |
| MCP SDK        | **`ModelContextProtocol.AspNetCore` 最新 preview 版**                                         | 官方 ASP.NET Core 扩展提供 `AddMcpServer()` + `MapMcp()`，直接支持远程 Streamable HTTP endpoint；preview 阶段接受破坏性变更，实现时锁定具体版本                                           |
| stdio 角色     | **仅 Claude Desktop 兜底垫片**（极简独立进程，代理到 `/mcp`）                                 | Claude Desktop 桌面版只支持 stdio；其余客户端均支持远程 HTTP                                                                                                                              |
| 鉴权           | 复用现有 **API Key（Bearer）**，与 REST 同源；依赖全局 `UseAuthentication`/`UseAuthorization` | 不引入新机制；`/mcp` 不单独加 `.RequireAuthorization()`，由 Host 全局鉴权中间件统一拦截；RBAC 鉴权（plan-enterprise-04 依赖项）本轮**不阻塞**，沿用 Catalog 现有未鉴权策略 + API Key 通道 |
| 工具逻辑归属   | 全部在 Host 实现；stdio 垫片只是 dumb proxy                                                   | 逻辑单点，避免 C#/TS 双份实现漂移                                                                                                                                                         |
| **CLI 命令集** | **本轮退役**                                                                                  | `cli/` 纯为 AI 服务的传输层，已被 MCP 取代；保留极简 stdio 垫片即可覆盖 Claude Desktop                                                                                                    |
| 旧 CLI 文档    | **删除 bash 驱动生成指引**                                                                    | `cli/skill/*` 中"AI 用 bash 调 `flowengine` 拼完整 DSL"的路径被 MCP 工具取代                                                                                                              |

## 待完成项

### Phase 1：Host 内 MCP Server（Streamable HTTP）

- [x] 引入 C# MCP SDK：`ModelContextProtocol.AspNetCore` 最新 preview 版（实现时锁定具体版本号）。
- [x] 在 `FlowEngine.Host` 注册 MCP 端点：
  - `builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly()`
  - `app.MapMcp("/mcp")`
  - 使用 Streamable HTTP 传输（处理 `GET` 建立 SSE 流 / `POST` 发请求 / `DELETE` 关闭会话，携带 `Mcp-Session-Id` 头）。
- [x] 复用 Host 现有鉴权中间件，使 `/mcp` 与 REST 共用 API Key Bearer 校验；未带凭证请求被拒。
- [x] 实现 9 个 MCP 工具，handler **直接解析 Host DI 中的 Application 服务**（不 HTTP 回环）：

  | MCP 工具            | 调用的服务/端点（来源控制器）                                                                     | 输入要点                                                                                                                                                                                                                                                     |
  | ------------------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
  | `list_node_catalog` | `CatalogService.ListAll()`（NodeCatalogController）                                               | 可选 `category` 过滤                                                                                                                                                                                                                                         |
  | `get_node_detail`   | `CatalogService.GetByName(name)`（NodeCatalogController）                                         | `name`；返回 `inputSchema`/`outputSchema`/`ports`/`examples`                                                                                                                                                                                                 |
  | `assemble_workflow` | `WorkflowAssemblyService.AssembleAsync(request)`（AiWorkflowsController）                         | 收 `AssembleWorkflowRequest`：顶层 `name`（**必需**）、`projectId`（可选 Guid）、`nodes`（每项 `id`/`typeName`/`parameters`）、`connections`（每项 `from`/`to`，可选 `fromPort`/`toPort`）。后端补全端口/坐标/入口，建未激活草稿，返回 `{draftId, workflow}` |
  | `modify_workflow`   | `WorkflowModificationService.ModifyAsync(id, ops)`（AiWorkflowsController）                       | 基于源工作流建新草稿，返回 `{draftId, workflow, diff}`                                                                                                                                                                                                       |
  | `validate_workflow` | `WorkflowValidationService.ValidateAsync(dsl)`（AiWorkflowsController）                           | 返回结构化错误（含 `nodeId`/`field`/`canAutoFix`/`suggestedFix`）                                                                                                                                                                                            |
  | `get_workflow`      | `WorkflowService.GetAsync(id)`（WorkflowsController）                                             | `workflowId`（**必需** Guid）；返回完整 DSL                                                                                                                                                                                                                  |
  | `list_workflows`    | `WorkflowService.GetAllAsync(projectId?, page=1, pageSize=20, ct)`（WorkflowsController）         | `projectId`（可选）、`page`（可选）、`pageSize`（可选，1–200）                                                                                                                                                                                               |
  | `confirm_workflow`  | `WorkflowService` 激活逻辑（AiWorkflowsController：`POST /api/v1/workflows/{id}/confirm`）        | `draftId` 即 `workflowId`（**必需** Guid）→ 激活部署                                                                                                                                                                                                         |
  | `execute_workflow`  | `ExecutionService.ExecuteAsync(workflowId, idempotencyKey?, ct, inputs?)`（ExecutionsController） | `workflowId`（**必需** Guid）、`inputs`（可选 JSON `Dictionary<string,object>`）；返回执行 ID/状态                                                                                                                                                           |

- [x] **结构化错误与 AI 自纠**：`validate_workflow` / `execute_workflow` 失败时不抛协议异常，而是把 `WorkflowValidationService` 的结构化错误与 `WorkflowExecutionFeedbackService` 的执行反馈作为**工具结果**返回（含 `canAutoFix`/`suggestedFix`/`executionContext`），供 AI 在 `maxRetries` 内自纠（设计文档 §5.4）。
- [x] 编写 handler→服务映射测试（xUnit），覆盖：draft 补全、默认端口、入口推导、拓扑校验、结构化错误回传、执行反馈回传。
- [x] **鉴权测试**：未带 API Key 请求 `/mcp` 初始化返回 **401**；带合法 Bearer 可建会话。
- [x] **会话生命周期测试**：首个 `POST` 初始化会话并返回 `Mcp-Session-Id`，后续 `POST`（带该头）与 `GET`（SSE 流）/ `DELETE`（关闭）按 Streamable HTTP 规范工作；非法/过期 `Mcp-Session-Id` 被拒。

### Phase 2：stdio 垫片（仅 Claude Desktop 兜底）+ CLI 退役

> 本阶段完成后，整个 `cli/` 目录删除，其 AI 命令全部退役；仅新建仓库根 `mcp-shim/` 包承载极简 stdio 垫片。

- [x] **退役 CLI 命令集**：删除 `cli/src/commands/` 下全部 AI 面向命令（`workflow`/`node-types`/`credential`/`trigger`/`api-keys`/`auth`/`config`/`executions`/`projects`/`guide`/`skill`/`test`/`builtInNodeTypes`）及其测试 `cli/src/__tests__/*`。
- [x] **抽极简 stdio 垫片（已定方案 A）**：在**仓库根新建 `mcp-shim/`** 独立最小包（删除整个 `cli/` 目录，不保留），仅含垫片 + 复用出的 HTTP 客户端（从原 `cli/src/api/client.ts` 提取）。
- [x] 垫片经 env（`FLOWENGINE_URL` + `FLOWENGINE_API_KEY`）指向 Host `/mcp`，把 stdio 上的 MCP 消息**代理转发**到 Host 的 Streamable HTTP 端点（dumb proxy，不含工具逻辑）。
- [x] 编写代理冒烟测试（Vitest，mock HTTP client）。

### Phase 3：skill 文档改为 MCP-only

- [x] **搬迁并改写 `mcp-shim/skill/`**：将原 `cli/skill/` 迁移至此，并删除其中 bash 驱动生成指引——`claude.md` / `skill.json` 里"AI 用 bash 调 `flowengine` 拼完整 DSL"的步骤整体移除（该路径随 CLI 命令集退役而消失）。
- [x] **重写 `mcp-shim/skill/mcp.json`**：改为声明上述 9 个 AI-native 工具的 `inputSchema`（删掉旧的 `list_node_types`/`get_node_type`/`test_workflow` 等，这些对应已被 task-010 取代的旧接口与"完整 DSL"要求）。
- [x] skill 文档（`mcp-shim/skill/`）新增**客户端连接配置示例**，覆盖两种模式：
  - 远程 HTTP：`claude mcp add --transport http flowengine https://host:port/mcp`（含 `Authorization: Bearer <apiKey>` header）；Cursor `.cursor/mcp.json` `url` 写法；VS Code `.vscode/mcp.json` 注意**根键是 `servers` 不是 `mcpServers`**。
  - stdio（Claude Desktop）：垫片启动配置（`command` + `env: {FLOWENGINE_URL, FLOWENGINE_API_KEY}`）。
- [x] 同步更新 `plan-enterprise-04-mcp.md` 变更记录，指向本任务（细化其「阶段一」）。

### Phase 4：端到端验证（设计文档 §10.1 Phase 7）

- [ ] 用 **Claude Code**（远程 HTTP）实测全链路：`list_node_catalog` → `get_node_detail` → `assemble_workflow` → `validate_workflow` → `confirm_workflow` → `execute_workflow`。
- [ ] 用 **Claude Desktop**（stdio 垫片）复测同一链路，确认垫片代理可达 Host `/mcp`。
- [ ] 失败自纠实测：故意提交非法 draft，确认结构化错误回传且 AI 可基于 `suggestedFix` 重试。
- [ ] 确认 `cli/` 旧命令已全部移除、构建无残留引用。

## 设计要点

### 与 task-010 的关系

- task-010 已落地全部后端能力（Catalog + Workflow 组装/修改/校验/反馈服务 + REST 端点 + 885 测试全绿）。**本任务不重写这些能力，只新增"MCP 协议外壳"**。
- MCP 工具 handler 在 Host 进程内直接调用 task-010 的 Application 服务；stdio 垫片通过 REST/HTTP 调同一 Host。两条路径共用同一套业务逻辑，保证行为一致。

### 传输与部署

- Host 在同一端口（REST 的 8001）新增 `/mcp` 路由，复用 Kestrel 与鉴权中间件，无需独立端口/进程。
- Streamable HTTP 会话管理：首个 `POST` 初始化会话并返回 `Mcp-Session-Id`，后续请求携带该头；`DELETE` 关闭。由 MCP SDK 的 HTTP 传输层处理，业务 handler 无感知。
- **CLI 不独立部署**：MCP Server 即 Host 的一部分；stdio 垫片是跑在 AI 客户端机器上的极简本地进程，不是 Flow Engine 的服务部署。

### CLI 退役边界

- **删**：`cli/src/commands/*` 全部命令 + 对应测试；`cli/skill/*` 中 bash 生成指引。
- **留**：一个极简 stdio 垫片（HTTP 客户端 + stdio 适配），承载 Claude Desktop 兜底。**已定方案 A**：新建仓库根 `mcp-shim/`，仅含垫片 + 复用出的 HTTP 客户端；**删除整个 `cli/` 目录**（不保留任何旧命令）。
- **不动**：REST API（前端/人类/curl 仍用）、Host `/mcp`、task-010 全部服务与测试。

### 工具结果形态

- 成功：返回结构化数据（如 assemble 返回 `{draftId, workflow}`）。
- 校验/执行失败：**不抛协议级错误**，返回工具结果对象，内含 `success:false` + 结构化 `errors[]`（`nodeId`/`field`/`errorType`/`message`/`canAutoFix`/`suggestedFix`）或执行反馈（`executionContext`/`suggestedFix`），供 AI 自纠。

### 鉴权

- 远程 HTTP：客户端在连接配置里带 `Authorization: Bearer <apiKey>`；Host 依赖全局 `UseAuthentication`/`UseAuthorization` 中间件统一校验，`/mcp` 不单独配置授权策略。
- stdio：垫片从 env 读取 apiKey，转发为 HTTP header。
- 本轮不引入 RBAC；Catalog 沿用现有未鉴权策略（与设计文档 §13 决策 1 一致）。
- **鉴权不对称（需显式知悉，非泄漏）**：`/mcp` 端点要求 API Key（Bearer），而 REST 的 `GET /api/v1/node-catalog` 当前**未鉴权**（设计 §13 决策 1 明确 Catalog 暴露的是节点能力元数据、不含敏感业务数据，故暂不鉴权）。二者共存属预期：MCP 端点因可被远程暴露而统一收口鉴权；Catalog REST 端点沿用现状。实现时不要在 MCP handler 内重复校验，由 Host 中间件统一拦截。

### 命名与文档

- 所有新增 C# 类/方法使用 `///` XML 文档注释；MCP 工具 `description` 用中文，明确"AI 填空、后端补全"语义，避免 AI 误以为需填端口/坐标。
- skill 文档保持与后端契约同步（draft 格式优先）。

## 完成标准

### MCP Server（Host）

- [ ] `FlowEngine.Host` 暴露 `/mcp`（Streamable HTTP），可被 Claude Code 经 `claude mcp add --transport http` 发现并调用。
- [ ] 9 个工具全部可用，行为等价于对应 REST 端点（handler 直调服务）。
- [ ] `/mcp` 受 API Key 鉴权保护；无凭证请求返回 **401**（有对应测试）。
- [ ] Streamable HTTP 会话生命周期有测试覆盖（初始化返回 `Mcp-Session-Id`；`POST`/`GET`SSE/`DELETE` 正常；非法 `Mcp-Session-Id` 被拒）。
- [ ] `validate_workflow` / `execute_workflow` 失败以工具结果返回结构化错误/反馈，含 `canAutoFix`/`suggestedFix`。

### CLI 退役 + stdio 垫片

- [ ] `cli/src/commands/*` 全部 AI 命令及测试已删除；无残留引用。
- [ ] `mcp-shim/` 仅含极简垫片 + HTTP 客户端；整个 `cli/` 已删除。
- [ ] Claude Desktop 经 stdio 配置可连上垫片，垫片代理到 Host `/mcp` 并完成同一全链路。
- [ ] 垫片不含工具逻辑，仅转发；Host 是唯一逻辑来源。

### 文档

- [ ] `mcp-shim/skill/mcp.json` 重写为 9 个 AI-native 工具清单。
- [ ] skill 文档改为 MCP-only，删除 bash 驱动生成指引；给出 HTTP 与 stdio 两种客户端配置示例，并标注 VS Code 根键 `servers` 差异。

### 测试与构建

- [ ] `dotnet build` 通过（0 错误 0 警告），`dotnet test` 全绿无回归（含新增 handler 映射测试）。
- [ ] 垫片 Vitest 冒烟测试通过。
- [ ] 端到端实测（Claude Code + Claude Desktop）全链路通过，含失败自纠。
- [ ] `cli/` 旧命令移除后前端/REST 构建不受影响。

## 主要修改记录（起草时占位）

- 新增 `FlowEngine.Host` MCP 端点 `/mcp`（Streamable HTTP）+ 9 个工具 handler（直调 task-010 服务）。
- 退役 `cli/src/commands/*` 全部 AI 命令及测试；抽极简 stdio 垫片（复用 HTTP 客户端）兜底 Claude Desktop。
- 重写 `mcp-shim/skill/mcp.json` 与 skill 文档为 MCP-only，删除 bash 驱动生成指引。

## 完成状态

| 阶段                           | 状态      | 说明                                     |
| ------------------------------ | --------- | ---------------------------------------- |
| Phase 1：Host MCP Server | ✅ 已完成 | SDK 引入 + `/mcp` 端点 + 9 工具 + 鉴权/会话测试 |
| Phase 2：stdio 垫片 + CLI 退役 | ✅ 已完成 | `cli/` 已删除 + `mcp-shim/` 垫片 + 8 冒烟测试 |
| Phase 3：skill 文档改 MCP-only | ✅ 已完成 | mcp.json/claude.md/skill.json MCP-only |
| Phase 4：端到端验证 | ⬜ 未开始 | 需实际服务运行 + AI 客户端连接 |

### 当前进度备注（2026-07-13）

- **Phase 1–3 已完成**。9 个 MCP 工具实现、CLI 退役、mcp-shim 垫片、skill 文档均已落地，全量测试通过（C# 980 + Vitest 8）。
- Phase 4（端到端验证）需要启动 Flow Engine 服务并用 AI 客户端连接实测，属于手动验证阶段。
- 全分支 Code Review 完成，无阻塞级发现。
- 后续改进项（Minor/Important 但不阻塞）：
  - 引入 `McpToolError` record 统一错误返回结构
  - 中间件顺序注释更精确 + 清理冗余 `UseEndpoints`
  - `validate_workflow` handler 层强制 workflowId vs nodes 互斥
