# 自然语言转 DSL

## 1. 什么是 DSL

DSL（Domain Specific Language，领域特定语言）是面向特定领域的专用语言。在 Flow Engine 中，**工作流 JSON 就是系统的 DSL**：它有确定的结构、字段和语义，能够被引擎加载并执行。

一个典型的工作流 DSL 片段：

```json
{
  "id": "...",
  "name": "银行流水导入",
  "nodes": [
    { "id": "n1", "typeName": "scheduleTrigger", "name": "每日触发" },
    { "id": "n2", "typeName": "readExcel", "name": "读取 Excel" },
    { "id": "n3", "typeName": "llmTransform", "name": "LLM 归类" },
    { "id": "n4", "typeName": "postgres", "name": "写入数据库" }
  ],
  "connections": [
    { "sourceNodeId": "n1", "sourcePortName": "output", "targetNodeId": "n2", "targetPortName": "input" },
    { "sourceNodeId": "n2", "sourcePortName": "output", "targetNodeId": "n3", "targetPortName": "input" },
    { "sourceNodeId": "n3", "sourcePortName": "output", "targetNodeId": "n4", "targetPortName": "input" }
  ]
}
```

## 2. 语义解析层的位置

自然语言转 DSL 位于**设计阶段**，而不是**运行时执行层**。执行引擎只接受已经校验通过的 DSL，不直接执行自然语言。

```
用户自然语言
    ↓
┌─────────────────┐
│  语义解析层      │  ← 本章节描述的范围
│  (设计时)        │
└────────┬────────┘
         ↓ 输出 JSON DSL 草案
    Schema/规则校验
         ↓
    人工确认 / 版本化
         ↓
    保存为工作流版本
         ↓
┌─────────────────┐
│   执行引擎       │  ← 不感知自然语言
│  (运行时)        │
└─────────────────┘
```

这种分层的好处：

- **确定性**：执行层保持严格确定，可审计、可回放。
- **安全性**：自然语言的模糊性不会直接进入生产执行。
- **可回退**：生成的 DSL 草案可以先保存为版本，不满意可以回滚。

## 3. 语义解析层的工作流程

### 3.1 生成阶段

语义解析层构造一个结构化 Prompt，把以下信息提供给 LLM：

- 用户原始自然语言描述。
- 系统支持的节点类型列表（含参数、端口）。
- 输出格式要求（必须生成符合 schema 的 JSON）。
- 示例（Few-shot）。

```csharp
public class SemanticParser
{
    public async Task<string> GenerateDraftAsync(string userInput, NodeTypeCatalog catalog)
    {
        var prompt = $@"
你是一个工作流编排助手。请根据用户描述生成一个符合以下规则的工作流 JSON：
可用节点类型：{catalog.ToJson()}
输出要求：
1. 必须包含 'nodes' 和 'connections'。
2. 每个节点的 'typeName' 必须来自可用节点类型。
3. 端口方向必须合法。
4. 必填参数不能缺失。

用户描述：{userInput}
";
        return await llmClient.CompleteAsync(prompt);
    }
}
```

### 3.2 校验阶段

LLM 生成的 JSON 必须经过系统校验器检查：

- **Schema 校验**：是否为合法的工作流 JSON 结构。
- **节点类型校验**：引用的 `typeName` 是否已注册。
- **端口方向校验**：源端口必须是输出，目标端口必须是输入；Agent 工具端口、LLM 供应端口等特殊类型是否被正确使用。
- **连接完整性校验**：是否存在悬空连接、闭环、不可达节点。
  - 悬空连接：源端口或目标端口未指向有效节点。
  - 闭环：连接形成循环，破坏 DAG 拓扑，属于非法工作流。
  - 不可达节点：没有输入连接且未被显式标记为入口节点（`IsEntry = true`）的节点。
- **必填参数校验**：每个节点的必填参数是否已填充。

### 3.3 纠错循环（闭环纠错）

如果校验失败，把错误信息返回给 LLM，要求其修正：

```
生成 JSON → 校验 → 失败 → 错误信息反馈给 LLM → 重新生成 → 再次校验
                ↓
            达到最大重试次数 → 返回人工处理
```

纠错循环可以配置最大重试次数（如 3 次）。超过次数仍失败时，把草案和错误信息交给用户手动修改。

### 3.4 人工确认与版本化

即使自动校验通过，生成的 DSL 也必须经过人工确认后才能保存为工作流版本。确认后：

- 生成新的工作流版本号。
- 保存原始自然语言描述，便于后续追溯。
- 保存生成的 DSL，进入执行引擎可用的工作流库。

## 4. 与 AI Builder 的关系

[roadmap.md](roadmap.md) 中的 **AI Builder** 是语义解析层的产品形态之一。AI Builder 可以包括：

- 自然语言生成工作流。
- 自然语言修改现有工作流。
- AI 辅助编写表达式。
- 根据错误信息自动修正工作流。

语义解析层是 AI Builder 的后端核心，但 AI Builder 还包括前端交互界面（如对话式编辑器、差异对比、版本选择等）。

## 5. 关键设计决策

| 决策 | 说明 |
|------|------|
| 自然语言不直接驱动运行时 | 执行引擎只接受已校验的 DSL，保证确定性。 |
| LLM 只是草案生成器 | 系统校验器是信任边界，不是 LLM。 |
| 人工确认是必要环节 | 避免 LLM 幻觉导致错误流程进入生产。 |
| 版本化保存 | 每次生成都是一个工作流版本，可追溯、可回滚。 |
| 校验规则可扩展 | 新增节点类型时，校验器自动继承该节点的 schema 规则。 |

## 6. 安全与限制

- 生成的 DSL 在人工确认前不应被引擎执行。
- 不应让 LLM 直接生成凭据、密钥等敏感信息；凭据参数只允许引用已存在的凭据 ID。
- 对 LLM 输出进行消毒，防止提示注入污染工作流定义：
  - 使用 JSON Schema 强校验返回结果，拒绝包含未声明字段的结果。
  - 对字符串字段进行黑名单过滤，禁止出现 `{{ }}` 表达式以外的脚本片段。
  - Prompt 与外部输入隔离，避免用户输入覆盖系统指令。
- 校验阶段应同时校验表达式语法与上下文合法性（详见 [expression-system.md](expression-system.md)）。
- 记录每次自然语言生成的原始输入、LLM 输出、校验结果、确认人，满足审计要求。
