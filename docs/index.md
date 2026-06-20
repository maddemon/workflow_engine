# Flow Engine 文档索引

## 项目定位

Flow Engine 是一个节点可热插拔的工作流自动化引擎。后端用 C# 承载 API 与前端静态资源，前端 React/TypeScript，节点通过 DLL 插件扩展。

## 建议阅读顺序

1. [系统模块总览](architecture/overview.md) — 理解整体架构、分层职责、核心数据流。
2. [核心概念与术语](architecture/terminology.md) — 理解节点、工作流、数据项、执行栈等概念。
3. 按兴趣阅读各子系统：
   - [节点系统](architecture/node-system.md)
   - [执行引擎](architecture/execution-engine.md)
   - [表达式系统](architecture/expression-system.md)
   - [凭据系统](architecture/credentials.md)
   - [触发器系统](architecture/trigger-system.md)
   - [Webhook 系统](architecture/webhook.md)
   - [审计日志系统](architecture/audit-log.md)
   - [AI Agent 与工作流即工具](architecture/agent-and-tool.md)
   - [自然语言转 DSL](architecture/natural-language-to-dsl.md) — AI 辅助生成工作流定义的语义解析层
4. [部署架构](architecture/deployment.md) — 理解单机部署、运行方式与横向扩展路径。
5. [项目路线图](architecture/roadmap.md) — 理解各阶段目标与模块依赖。

## 文档目录

### 架构概念

| 文档                                                                     | 内容                        |
| ------------------------------------------------------------------------ | --------------------------- |
| [overview.md](architecture/overview.md)                               | 系统模块总览、分层职责、核心数据流、持久化设计             |
| [terminology.md](architecture/terminology.md)                         | 核心概念词汇表、术语关系图、名词对照                  |
| [node-system.md](architecture/node-system.md)                         | 节点接口、注册流程、参数驱动 UI、节点分类、开发规范         |
| [execution-engine.md](architecture/execution-engine.md)               | 执行循环、多输入等待、错误处理、执行状态机、执行记录          |
| [expression-system.md](architecture/expression-system.md)             | 表达式语法、变量、求值流程、安全限制、错误处理             |
| [credentials.md](architecture/credentials.md)                         | 凭据模型、加密方案、运行时注入、安全红线                |
| [trigger-system.md](architecture/trigger-system.md)                   | 触发器类型、调度、轮询去重、生命周期、状态持久化            |
| [webhook.md](architecture/webhook.md)                                 | Webhook 类型、生命周期、路由注册、请求校验、响应模式      |
| [audit-log.md](architecture/audit-log.md)                             | 事件模型、EventBus、日志消费端、回放机制            |
| [agent-and-tool.md](architecture/agent-and-tool.md)                   | Agent 执行流程、工具收集、子工作流工具、子 Agent 嵌套   |
| [natural-language-to-dsl.md](architecture/natural-language-to-dsl.md) | 自然语言生成工作流 DSL、校验循环、人工确认与版本化         |
| [deployment.md](architecture/deployment.md)                           | 单机后台服务部署、运行方式、默认技术栈、横向扩展路径          |
| [roadmap.md](architecture/roadmap.md)                                 | MVP 到 Enterprise 各阶段目标、功能清单、验收标准、风险 |

### 开发计划

开发计划位于 `plans/`，按阶段（MVP/Alpha/Beta/GA/Enterprise）分目录组织，入口为总览文档：

| 文档 | 内容 |
|------|------|
| [plans/plan-000-overview.md](plans/plan-000-overview.md) | 开发计划总览：阶段划分、编号方案、依赖图、完整文件清单 |
| [plans/mvp/plan-mvp-00-readme.md](plans/mvp/plan-mvp-00-readme.md) | MVP 阶段说明（核心可运行 + 前端编排，12 个模块计划） |
| [plans/alpha/plan-alpha-00-readme.md](plans/alpha/plan-alpha-00-readme.md) | Alpha 阶段说明（触发器/审计/Agent 基础/用户系统，10 个模块计划） |
| [plans/beta/plan-beta-00-readme.md](plans/beta/plan-beta-00-readme.md) | Beta 阶段说明（RBAC/多租户/Agent 增强，10 个模块计划） |
| [plans/ga/plan-ga-00-readme.md](plans/ga/plan-ga-00-readme.md) | GA 阶段说明（队列/Worker/监控/SSO，8 个模块计划） |
| [plans/enterprise/plan-enterprise-00-readme.md](plans/enterprise/plan-enterprise-00-readme.md) | Enterprise 阶段说明（Git/协作/MCP/AI Builder，7 个模块计划） |

### 项目规则

| 文档                                                                              | 内容               |
| ------------------------------------------------------------------------------- | ---------------- |
| [AGENTS.md](../AGENTS.md)                                                       | AI Agent 路由文档    |
| [.agents/rules/project-rules.md](../.agents/rules/project-rules.md)              | 项目总规则与协作流程       |
| [.agents/rules/backend-code-rules.md](../.agents/rules/backend-code-rules.md)    | 后端代码规范、目录结构、错误示范 |
| [.agents/rules/frontend-code-rules.md](../.agents/rules/frontend-code-rules.md)  | 前端代码规范、目录结构、错误示范 |
| [.agents/rules/docs-rules.md](../.agents/rules/docs-rules.md)                    | 文档规范与开发计划实施指南    |

## 待补充文档

| 缺口 | 说明 | 建议位置 |
|------|------|----------|
| 快速开始 | 5 分钟跑通 Hello World 的向导 | `docs/getting-started.md` |
| API 文档 | HTTP 端点列表、请求/响应示例 | `docs/api/` 或 OpenAPI 描述 |
