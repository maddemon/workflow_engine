> AI Agent 路由文档。先读本文件，再按指引阅读对应规则文档。

# Flow Engine —— 工作流自动化引擎

规则文件已按标准放在 `.agents/rules/`，通过 `opencode.json` 自动加载。规则文件含 `globs` 前 matter，编辑对应文件类型时会自动引入。

**重要：`.agents/` 是唯一的规则和技能来源。** 其他 AI 工具（Cursor、Claude Code、Windsurf 等）的配置目录（`.cursor/`、`.claude/` 等）已被 `.gitignore` 禁止提交，不应在其中放置规则或技能文件。如需追加规则，一律放入 `.agents/rules/`。

## 1. 快速定位

| 你想了解什么 | 读哪个文件 |
|--------------|-----------|
| 项目规矩总览、AI 协作流程 | `.agents/rules/project-rules.md` |
| 后端代码规范、目录结构、错误示范 | `.agents/rules/backend-code-rules.md` |
| 前端代码规范、目录结构、错误示范 | `.agents/rules/frontend-code-rules.md` |
| 文档怎么写、开发计划怎么实施 | `.agents/rules/docs-rules.md` |
| 架构概念、模块关系 | `docs/index.md` → `docs/architecture/*.md` |
| 模块开发计划 | `docs/plans/plan-xxx-*.md` |
| 任务记录 | `docs/plans/task-*.md` 或 `docs/chats/task-*.md` |

## 2. 新对话承接流程

详见 `.agents/rules/project-rules.md` 第 2 节。

## 3. 当前重点

- 架构概念文档已拆分为 `docs/architecture/` 系列文档，索引入口为 `docs/index.md`。
- 后续需补齐 `docs/getting-started.md` 快速开始文档和 `docs/api/` API 文档。
