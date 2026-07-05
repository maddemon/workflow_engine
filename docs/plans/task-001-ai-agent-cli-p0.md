# 任务：AI Agent CLI P0 后端与 CLI 基础

## 目标

为 Flow Engine 实现 AI Agent CLI 的 P0（MVP 必需）能力：

1. 后端支持 API Key / Personal Access Token 长期认证，解决 JWT 60 分钟过期对 AI Agent 长会话的阻塞。
2. 后端支持 Dry-Run 执行 API，让 CLI `test` 命令能在不持久化工作流、不依赖 Admin 权限的情况下验证 DSL。
3. 搭建 Node.js CLI 项目基础架构（package、tsconfig、配置、API 客户端、错误处理、输出）。
4. 实现 CLI 认证与配置相关命令（login、logout、profile、config）。

计划文档：[.agents/documents/plan-ai-agent-cli-for-flow-engine.md](../../../../.agents/documents/plan-ai-agent-cli-for-flow-engine.md)

## 待完成项

- [ ] Task 1: 后端 API Key（实体、迁移、Service、Controller、认证中间件扩展）
- [ ] Task 2: 后端 Dry-Run 执行 API
- [ ] Task 3: CLI 项目基础架构
- [ ] Task 4: CLI 认证与配置命令
- [ ] 编译/测试通过
- [ ] SubAgent Code Review

## 完成标准

- `dotnet build FlowEngine.sln` 通过，无新增编译警告。
- `dotnet test` 相关测试通过（新增测试覆盖正常路径与边界异常）。
- CLI 项目 `npm run build` / `npm run test` 通过。
- 后端新增端点符合现有 RBAC、审计、EF Core LINQ 约束。
- 不保存密码、Token 不泄露到日志/响应。

## 主要修改记录

- 待定
