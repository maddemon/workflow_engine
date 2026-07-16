# Dogfooding - AI Workflow Testing System

AI 自动生成工作流需求、通过 MCP 构建/执行/修改、分析失败模式、自动提 PR 优化 Flow Engine 的 AI 体验。

## 给主 Agent 的触发词

| 场景 | 提示词 |
|------|--------|
| 启动一轮 | `启动 dogfooding` 或 `Run the dogfooding pipeline` |
| 指定场景数 | `启动 dogfooding，一轮 5 个场景` |
| 单次排查 | `Dogfooding: build scenario X` （跳过分析/改进，直接构建一个场景看结果） |

主 Agent 收到触发词后会：
1. 自动加载本 skill
2. 确保 Flow Engine Host 在 :8001 运行
3. 执行 `run.ps1`（或 `run.sh`）
4. 汇报报告摘要

## 一键启动（终端里自己跑）

```bash
# Windows (PowerShell 7+)
.\.agents\skills\dogfooding\run.ps1

# macOS / Linux
./.agents/skills/dogfooding/run.sh
```

脚本自动：
- 检查 Flow Engine Host（:8001），未运行则后台启动并等待就绪
- 设置 `FLOWENGINE_API_KEY`
- 运行 orchestrator
- 输出最新报告路径

## 安装依赖

```bash
cd .agents/skills/dogfooding && npm install
```

## 跨 IDE 配置

- **OpenCode**: 已有 `.agents/` 映射，自动发现
- **Claude Code**: 在 `CLAUDE.md` 添加 `读取 .agents/skills/dogfooding/SKILL.md 并按指引执行`
- **Cursor**: 创建 `.cursor/rules/dogfooding.mdc` 引用本文件
- **VS Code Copilot**: 在 `.github/copilot-instructions.md` 引用
