# Flow Engine Wiki

面向开发者（未来的自己）与终端用户的系统文档。所有内容以代码为准、从代码编写；仅少数无法从代码核实的细节（多为 UI 文案/菜单路径或部署配置项）保留「待确认 / 以实际为准」标注。

## 索引

### 入门（getting-started/）
- [安装与依赖 installation.md](getting-started/installation.md)
- [快速上手 quick-start.md](getting-started/quick-start.md)

### 核心概念（concepts/）
- [工作流模型 workflow-model.md](concepts/workflow-model.md)
- [执行模型 execution.md](concepts/execution.md)
- [表达式与脚本 expressions.md](concepts/expressions.md)

### 架构（architecture/）
- [系统总览 overview.md](architecture/overview.md)

### 开发者操作指南（how-to/）
- [运行与调试 run-and-debug.md](how-to/run-and-debug.md)
- [测试 testing.md](how-to/testing.md)
- [编写一个节点插件 write-a-plugin.md](how-to/write-a-plugin.md)
- [通过 AI Agent（MCP）创建/修改/测试工作流 ai-agent-mcp.md](how-to/ai-agent-mcp.md)
- [凭据管理 credentials.md](how-to/credentials.md)
- [触发器 triggers.md](how-to/triggers.md)

### 部署与运维（deployment/）
- [单机部署 single-machine.md](deployment/single-machine.md)
- [横向扩展路径 scaling.md](deployment/scaling.md)

### 参考（reference/）
- [REST API 概览 rest-api.md](reference/rest-api.md)
- [MCP 工具参考 mcp-tools.md](reference/mcp-tools.md)

### 终端用户手册（user-guide/）
- [产品简介 introduction.md](user-guide/introduction.md)
- [画布编辑器 canvas-editor.md](user-guide/canvas-editor.md)
- [运行与监控 run-and-monitor.md](user-guide/run-and-monitor.md)
- [凭据管理 credentials.md](user-guide/credentials.md)
- [触发器配置 triggers.md](user-guide/triggers.md)
- [通过 AI Agent 自然语言编排 ai-agent.md](user-guide/ai-agent.md)

## 修订记录

所有修订均经代码核实。下列为一次集中评审后的修正（评审来源：Wiki 评审报告）。

### 评审修正（rest-api.md / mcp-tools.md / workflow-model.md / expressions.md / overview.md）
- **rest-api.md · 认证**：`POST /api/v1/auth/register` 实际返回 `410 Gone`（`RegistrationDisabled`），自助注册已关闭；该端点标 `IgnoreApi = true`，不出现在 Swagger。原文档将其当作可用端点，已更正。
- **mcp-tools.md · 工具数量**：区分两层 —— 技能层（`mcp-shim`）暴露**核心 9 个工具**；后端 `/mcp`（`FlowEngine.Host/Mcp/Tools/`）实际注册 **15 个**。补 `get_conventions` / `list_credentials` / `get_execution` / `dry_run_workflow` / `reject_draft` / `get_draft_feedback` 六项详表，并加注「完整清单以运行期 `tools/list` 返回为准」。
- **workflow-model.md · Connection**：补注其与 `NodeDefinition` 同为 `[NotMapped]`，是工作流 JSON 列的一部分、非独立表。
- **workflow-model.md · PortDefinition**：原文所列字段与 `PortDefinition.cs` 逐一核对一致，无需改动。
- **expressions.md / overview.md · `$items`**：纠正此前「`$items` 不存在」的错误删除。经核实 `$items(nodeName?)` 由 `ExecutionContextGlobalsBuilder.BuildFull` 注入，是**全局变量**（非逐项变量）：`$items()` 取当前输入批次 item 列表，`$items("节点名")` 取指定上游节点最新输出批次 item 列表。已加入变量表并修正误导性表述。
- **rest-api.md · 审计查询参数**：按 `AuditQueryParameters.cs` 补全 `GET /api/v1/audit-events` 的真实参数（`EventType` / `From` / `To` / `ResourceType` / `ResourceId` / `Offset` / `Limit`，默认 `Limit=50` 钳制 1–200）。注意：不含 `actor` 参数，分页为 `Offset`/`Limit` 而非 `page`/`pageSize`。

### 早期「待确认」清理（代码核实后转确定结论）
- `run-and-debug.md`：数据库经 `MigrateAsync()` 建表（非 `EnsureCreated()`）。
- `write-a-plugin.md`：`PluginLoader` 的哈希白名单在默认部署中**未启用**（构造默认 `null`，跳过校验）。
- `scaling.md` / `single-machine.md`：Quartz 集群未启用（无 `IsClustered`）；执行**无**断点续跑 / 恢复实现。
- `user-guide/triggers.md`：一个工作流可并存多种触发器类型（`Trigger.WorkflowDefinitionId` 关联，允许多条触发记录）。
- `user-guide/credentials.md`：凭据类型与 `CredentialTypeRegistry.CreateBuiltInTypes` 对齐，共 8 种（apiKey / database / basicAuth / oauth2 / smtp / s3 / redis / mongo），与代码完全一致。
