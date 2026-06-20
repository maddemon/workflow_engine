---
description: 项目总规矩、AI 协作流程、硬性约束、安全规范、TDD 开发流程。所有新对话必须首先阅读。
globs: "*"
---
> 项目总规矩。新对话进入项目后，读完 `AGENTS.md` 首先读本文件。

# 项目规则总览

## 1. 通用原则

- **只实现需求内功能**，不额外新增特性。
- **逻辑合理为首要标准**，不回避问题、不做过度设计。
- **完整处理边界与异常场景**，精简代码不可遗留隐患。
- **有疑问立即沟通**：需求解读存在分歧、方案有隐患、内容模糊时，不擅自执行。
- **不主观臆断**：不替用户做未明确授权的重大决策。

## 2. AI 协作流程

1. **读规矩**：先读 `AGENTS.md`，再读本文件，然后按任务方向读前端/后端代码规范和文档规范。
2. **读计划**：查看 `docs/plans/` 中当前模块的开发计划。
3. **建任务文档**：多步骤任务先在 `docs/plans/` 或 `docs/chats/` 创建 `task-xxx-*.md`。
4. **实施**：按计划和规范写代码。
5. **编译验证**：后端 `dotnet build`，前端 `npm run build` / `npm run typecheck`，修复所有报错。
6. **Code Review**：发起 SubAgent Code Review，以任务文档和计划文档为依据。
7. **收尾**：更新任务文档，标记完成状态。

## 3. 硬性约束

| 约束 | 说明 |
|------|------|
| 代码规范优先 | 后端代码遵循 `backend-code-rules.md`，前端代码遵循 `frontend-code-rules.md`。 |
| 文档规范优先 | 计划文档、任务文档遵循 `docs-rules.md`。 |
| 设计先于实施 | 模块开发前必须有 `docs/plans/plan-xxx-*.md`，并经过 Code Review。 |
| 任务文档先行 | 多步骤任务必须有 `task-xxx-*.md`。 |
| 编译/构建必须通过 | 任何代码修改后必须修复所有编译错误。 |
| 先写测试再写实现 | 新增功能先写非法输入/异常复现用例，再写实现至用例通过。 |
| 不主动创建无关文件 | 不主动创建 README、空项目、示例代码、未明确需要的配置或文档。 |
| 不主动提交代码 | 除非用户明确要求。 |
| 不擅自删除历史代码 | 废弃代码标注 `// TODO: deprecated`，只移除本次改动后失效的导入/变量/函数。 |
| 不擅自修改注释/格式 | 沿用现有代码风格，不批量格式化无关文件。 |
| 敏感信息保护 | 凭据、Token、私钥禁止硬编码，不得落入日志或异常信息。 |

## 4. 安全与异常

- 用户输入、外部 API 返回在系统边界做校验与消毒。
- 表达式引擎与代码执行节点必须运行在隔离沙箱中。
- 节点插件 DLL 加载前须校验来源与完整性。
- Webhook 入口须做签名验证或来源白名单。
- 日志、异常、API 响应中不得输出明文密码、Token、私钥。

## 5. 编译与测试

- 后端修改后运行 `dotnet build`。
- 前端修改后运行 `npm run build` 和 `npm run typecheck`。
- 新增功能必须配套测试用例。
- Code Review 前必须完成编译和测试。

## 6. 开发工作流（TDD）

### 6.1 标准开发流程

```
1. 写测试（正常路径 + 边界条件）
2. 运行测试（确认失败）
3. 实现功能代码
4. 运行测试（确认通过）
5. 重构（如需要）
6. 运行全部测试（确认无回归）
```

### 6.2 新增节点插件流程

1. 在 `plugins/FlowEngine.Plugins.Standard/` 创建节点类
2. 在 `tests/FlowEngine.Runtime.Tests/Plugins/` 创建对应测试
3. 测试覆盖：正常执行、空参数、类型转换、异常处理
4. `dotnet build` + `dotnet test` 全部通过

### 6.3 新增 API 端点流程

1. 在 `tests/FlowEngine.Application.Tests/` 创建 DTO 转换测试
2. 实现 Service 和 Controller
3. 测试覆盖：正常创建/更新/查询、无效输入、ID 类型兼容
4. `dotnet build` + `dotnet test` 全部通过

### 6.4 修复 Bug 流程

1. **先写回归测试**：复现 Bug 的测试用例（确认失败）
2. 修复 Bug
3. 运行测试（确认通过）
4. 运行全部测试（确认无回归）

## 7. 规则文档索引

| 规则 | 文件 |
|------|------|
| 项目总规则 | `.agents/rules/project-rules.md` |
| 后端代码规范 | `.agents/rules/backend-code-rules.md` |
| 前端代码规范 | `.agents/rules/frontend-code-rules.md` |
| 文档规范与开发计划实施 | `.agents/rules/docs-rules.md` |

## 8. 文档目录索引

| 文档类型 | 目录 |
|----------|------|
| 开发计划 | `docs/plans/plan-xxx-*.md` |
| 任务记录 | `docs/plans/task-xxx-*.md` 或 `docs/chats/task-xxx-*.md` |
| 架构概念 | `docs/architecture/*.md`（待拆分） |
| 开发指南 | `docs/guides/*.md`（待补充） |
