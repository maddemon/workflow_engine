# Dogfooding - AI Workflow Testing System

AI 自动生成工作流需求、通过 MCP 构建/执行/修改、分析失败模式、自动提 PR 优化 Flow Engine 的 AI 体验。

## 使用方式

```bash
# 安装依赖
cd .agents/skills/dogfooding && npm install

# 确保 Flow Engine Host 在运行（:8001）
# 配置环境变量
export FLOWENGINE_API_KEY="your-api-key"

# 运行一轮
npx tsx src/orchestrator.ts
```

## 跨 IDE 配置

- **OpenCode**: 已有 `.agents/` 映射，自动发现
- **Claude Code**: 在 `CLAUDE.md` 添加 `读取 .agents/skills/dogfooding/SKILL.md 并按指引执行`
- **Cursor**: 创建 `.cursor/rules/dogfooding.mdc` 引用本文件
- **VS Code Copilot**: 在 `.github/copilot-instructions.md` 引用

详情见 `docs/superpowers/specs/2026-07-16-dogfooding-system-design.md`
