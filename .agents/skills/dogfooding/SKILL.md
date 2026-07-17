# Dogfooding — Flow Engine MCP 可用性自动测试

主 agent 设计场景 → spawn sub agent 仅靠 MCP 工具创建工作流 → sub agent 踩坑并汇报 → 主 agent 分析报告改进 MCP 接口。N 轮后 sub agent 创建工作流的成本降低。

## 核心原则

**MCP 接口必须自足。** Sub agent **不应读取任何源代码**——仅靠 `list_node_catalog`、`get_node_detail`、`get_conventions` 等 MCP 工具提供的信息，就应该能正确构建和执行工作流。

**失败了不是 AI 的问题，是 MCP 接口的问题。** Sub agent 踩的每一个坑都说明 MCP 接口对 AI 不友好——schema 不足、错误信息难懂、工具设计反直觉。

## 运行方式

主 agent 在对话中执行：

```
/dogfooding
```

或手动按以下步骤：

### 1. 生成场景

```bash
cd .agents/skills/dogfooding
npx tsx src/scenario-generator.ts --count 1
```

输出一个 JSON 场景描述（含节点组合和任务提示）。

### 2. Spawn sub agent

用 Actor tool spawn 一个 general sub agent，prompt 包含：
- 场景描述（要创建工作流的意图）
- MCP 工具使用指引（见下方 prompt 模板）
- 要求输出结构化报告

### 3. 收集报告

Sub agent 完成后返回结构化报告（JSON），包含：
- 工具调用序列
- 遇到的错误
- 自纠过程
- 缺失的信息
- 最终结果

### 4. 分析与改进

主 agent 读取报告，判断：
- A 类（惯例污染）→ 改 conventions
- B 类（Schema 不足）→ 改节点 inputSchema
- C 类（引擎缺陷）→ 改 backend 代码
- D 类（环境假设）→ 补充说明文档

### 5. 持久化

```bash
npx tsx src/save-report.ts --round <roundId> --report '<json>'
```

## Sub Agent Prompt 模板

spawn sub agent 时使用以下 prompt（将 `{SCENARIO}` 替换为场景描述）：

```
你是一个 AI 工作流构建 agent。你的任务是仅通过 MCP 工具创建并执行一个工作流。

## 任务
{SCENARIO}

## 工作流程
1. 调用 get_conventions 了解表达式语法和连接规则
2. 调用 list_node_catalog 查看可用节点
3. 对每个候选节点调用 get_node_detail 获取端口和参数 schema
4. 设计工作流（节点 + 连接）
5. 调用 assemble_workflow 创建草稿
6. 调用 validate_workflow 检查，如有错误则调用 modify_workflow 修复，循环直到通过
7. 调用 confirm_workflow 确认
8. 调用 execute_workflow 执行
9. 等待执行完成（轮询 execution 状态）

## 重要规则
- 不要读取任何源代码文件
- 不要猜测参数格式，从 get_node_detail 的 inputSchema 获取
- 连接端口时注意端口类型（Main/AgentTool/LLM/Memory）
- 如果验证失败，仔细阅读错误信息并修复，不要跳过

## 输出格式
完成后，输出一个 JSON 报告（用 ```json 包裹）：

{
  "success": true/false,
  "scenario": "场景描述",
  "toolCalls": [
    {"tool": "工具名", "args": {...}, "result": "...(摘要)", "error": null}
  ],
  "errors": [
    {"tool": "工具名", "error": "错误信息", "resolution": "如何修复的"}
  ],
  "missingInfo": [
    "get_node_detail 没有返回端口类型信息"
  ],
  "suggestions": [
    "建议改进: ..."
  ]
}
```

## 知识库

| 文件 | 内容 |
|------|------|
| `docs/superpowers/dogfooding/runs/{roundId}.json` | 本轮 sub agent 报告 |
| `docs/superpowers/dogfooding/metrics.md` | 跨轮趋势（成功率、踩坑类型分布） |
| `docs/superpowers/dogfooding/coverage.json` | 节点覆盖记录 |
| `docs/superpowers/dogfooding/error-patterns.json` | 已发现的错误模式库 |

## 场景来源

场景可来自：
1. **自动组合**：`scenario-generator.ts` 从 catalog 按分类组合
2. **手动指定**：主 agent 根据需要测试的节点手动创建
3. **历史复现**：从 `error-patterns.json` 中选择已知问题重新测试
