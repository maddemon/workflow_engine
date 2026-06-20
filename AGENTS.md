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

## 3. 可用 Skill（按需加载）

项目已安装以下 skill（`obra/superpowers` 全家桶），OpenCode 自动发现于 `.agents/skills/`。AI 根据任务匹配加载，**执行开发计划时需显式告知 AI 使用哪个 skill**。

### 核心工作流

| 步骤 | Skill | 用途 |
|------|-------|------|
| 执行计划 | `subagent-driven-development` | **首选**。每任务分派独立子 agent + spec 审查 + 代码质量审查，持续执行不中断 |
| 执行计划（备选） | `executing-plans` | 无 subagent 支持时使用，人-in-loop，适合简单计划 |
| 子 agent TDD | `test-driven-development` | subagent 内部自动使用，垂直切片 + 行为测试 |
| Code Review | `requesting-code-review` | 最终全分支 review，spec 合规 + 代码质量 |
| 验证约束 | `verification-before-completion` | 必须在声称完成前运行验证命令（构建、测试等） |
| 收尾合并 | `finishing-a-development-branch` | 全部任务完成后收尾 |

### 辅助 skill

| Skill | 用途 |
|-------|------|
| `grill-me` | 计划评审，帮你捋清设计 |
| `writing-plans` | 创建开发计划文档 |
| `receiving-code-review` | 接收 review 反馈 |

### 使用方式

开始执行计划时，对 AI 说一句即可：

> "使用 subagent-driven-development 执行这个计划：`docs/plans/plan-xxx-*.md`"

AI 会自动加载 skill，按流程：读计划 → 创建任务 brief → 逐任务派子 agent 实现 → task review → 全部完成后 final review。

## 4. 当前重点

- 架构概念文档已拆分为 `docs/architecture/` 系列文档，索引入口为 `docs/index.md`。
- 后续需补齐 `docs/getting-started.md` 快速开始文档和 `docs/api/` API 文档。
