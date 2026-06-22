> AI Agent 路由文档。先读本文件，再按指引阅读对应规则文档。

# Flow Engine —— 工作流自动化引擎

规则文件已按标准放在 `.agents/rules/`，通过 `opencode.json` 自动加载。规则文件含 `globs` 前 matter，编辑对应文件类型时会自动引入。

## 1. 快速定位

| 你想了解什么                     | 读哪个文件                                       |
| -------------------------------- | ------------------------------------------------ |
| 项目规矩总览、AI 协作流程        | `.agents/rules/project-rules.md`                 |
| 后端代码规范、目录结构、错误示范 | `.agents/rules/backend-code-rules.md`            |
| 前端代码规范、目录结构、错误示范 | `.agents/rules/frontend-code-rules.md`           |
| 文档怎么写、开发计划怎么实施     | `.agents/rules/docs-rules.md`                    |
| 架构概念、模块关系               | `docs/index.md` → `docs/architecture/*.md`       |
| 模块开发计划                     | `docs/plans/plan-xxx-*.md`                       |
| 任务记录                         | `docs/plans/task-*.md` 或 `docs/chats/task-*.md` |

## 2. 新对话承接流程

详见 `.agents/rules/project-rules.md` 第 2 节。

## 3. 核心工作流

| 步骤             | Skill                            | 用途                                                                        |
| ---------------- | -------------------------------- | --------------------------------------------------------------------------- |
| 执行计划         | `subagent-driven-development`    | **首选**。每任务分派独立子 agent + spec 审查 + 代码质量审查，持续执行不中断 |
| 执行计划（备选） | `executing-plans`                | 无 subagent 支持时使用，人-in-loop，适合简单计划                            |
| 子 agent TDD     | `test-driven-development`        | subagent 内部自动使用，垂直切片 + 行为测试                                  |
| Code Review      | `requesting-code-review`         | 最终全分支 review，spec 合规 + 代码质量                                     |
| 验证约束         | `verification-before-completion` | 必须在声称完成前运行验证命令（构建、测试等）                                |
| 收尾合并         | `finishing-a-development-branch` | 全部任务完成后收尾                                                          |

## 4. 多用 Codegraph
