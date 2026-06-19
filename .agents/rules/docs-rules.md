# 文档规范与开发计划实施指南

## 1. 文档目录

```
docs/
├── plans/                          # 开发计划与任务文档
│   ├── plan-000-overview.md        # 计划总览与索引
│   ├── mvp/                        # MVP 阶段（12 个模块计划）
│   ├── alpha/                      # Alpha 阶段（9 个模块计划）
│   ├── beta/                       # Beta 阶段（10 个模块计划）
│   ├── ga/                         # GA 阶段（8 个模块计划）
│   ├── enterprise/                 # Enterprise 阶段（7 个模块计划）
│   └── task-*.md                   # 任务记录
├── architecture/                   # 架构概念文档
│   ├── overview.md
│   ├── terminology.md
│   ├── node-system.md
│   ├── execution-engine.md
│   ├── expression-system.md
│   ├── credentials.md
│   ├── trigger-system.md
│   ├── webhook.md
│   ├── audit-log.md
│   ├── agent-and-tool.md
│   ├── natural-language-to-dsl.md
│   ├── deployment.md
│   └── roadmap.md
└── chats/                          # 任务/讨论记录
    └── task-*.md
```

## 2. 文档命名规范

### 2.1 计划文档

文件名：`plan-{stage}-NN-module-name.md`

- `{stage}`：阶段标识，如 `mvp`、`alpha`、`beta`、`ga`、`enterprise`；总览文件为 `plan-000-overview.md`。
- `NN`：两位序号，阶段说明文件固定为 `00`，模块计划从 `01` 起。
- `module-name`：模块英文小写，用连字符分隔。
- 各阶段计划存放在 `docs/plans/{stage}/` 子目录下。
- 示例：`plan-mvp-03-node-system.md`、`plan-alpha-04-triggers.md`、`plan-ga-02-worker.md`。

### 2.2 任务文档

文件名：`task-xxx-descriptive-name.md`

- `xxx`：三位序号，与计划文档序号独立编排。
- `descriptive-name`：简短描述任务内容。
- 示例：`task-001-setup-project-rules.md`、`task-002-rewrite-node-system-plan.md`。

### 2.3 架构文档

文件名：`descriptive-name.md`，小写，连字符分隔。
- 示例：`overview.md`、`terminology.md`、`node-system.md`。

## 3. 计划文档结构

每个计划文档必须包含以下章节（可根据模块复杂度裁剪）：

```markdown
# 开发计划：模块名（plan-xxx-module-name）

## 1. 概述
- 本模块解决什么问题、覆盖范围、不覆盖范围

## 2. 交付物清单
- 代码、配置、测试、文档等

## 3. 开发阶段
### 阶段一：...
- 目标、核心任务、输入、输出、验收标准、依赖

### 阶段二：...
...

## 4. 阶段依赖图
- Mermaid 或文字描述的依赖关系

## 5. 风险与待定项
- 风险点、影响、应对策略

## 6. 验收总标准
```

**约束**：计划文档只写"要做什么"和"怎么验收"，不重复架构文档中已有的设计内容。架构设计、接口签名、数据模型统一在 `docs/architecture/` 中维护。

## 4. 任务文档结构

```markdown
# 任务：任务标题

## 目标
- 本任务要达成什么

## 待完成项
- [ ] 事项一
- [ ] 事项二

## 完成标准
- 明确的验收条件

## 完成状态
- [x] 事项一
- [ ] 事项二

## 主要修改记录
- 修改点 A
- 修改点 B
```

## 5. 新对话如何实施开发计划

### 5.1 实施前

1. 阅读 `AGENTS.md` 找到对应规则文档。
2. 阅读相关代码规范（前端/后端）和本文档。
3. 阅读要实施的 `docs/plans/plan-xxx-*.md`。
4. 创建 `docs/plans/task-xxx-*.md` 或 `docs/chats/task-xxx-*.md`，记录本任务的目标、范围、完成标准。
5. 如有设计疑问，先沟通再实施。

### 5.2 实施中

1. 按阶段顺序开发，每阶段完成后自检验收标准。
2. 代码修改必须遵循前端/后端代码规范。
3. 重要的接口或数据结构变更，同步更新计划文档。
4. 遇到边界问题或设计调整，在任务文档中记录。

### 5.3 实施后

1. 执行编译/构建，修复所有报错。
2. 运行相关测试，确保通过。
3. 发起 SubAgent Code Review，以任务文档和计划文档为依据。
4. 根据 Review 意见修改。
5. 更新任务文档，标记完成状态。
6. 不主动提交代码，除非用户明确要求。

## 6. 代码样例与伪代码规范

- 计划文档中的重要接口必须包含伪代码或 C# / TypeScript 签名。
- 伪代码只表达设计意图，不必可编译。
- 关键流程必须用文字或图表说明，不能只列接口。
- 错误示范用 `// ❌` 标记，正确示范用 `// ✅` 或直接写正确代码。

## 7. 禁止事项

- 不在文档中引用外部产品专属术语。
- 不照搬任何第三方产品的类名、函数名、变量名。
- 不在任务文档中写大段实现代码样例，只记录目标、设计、验收。
- 不创建未明确需要的文档。

## 8. 架构文档编写规范

### 8.1 禁止嵌入完整实现代码

架构文档（`docs/architecture/`）只应包含接口签名和设计意图说明，**不得贴完整类实现**（含完整方法体、循环、分支逻辑）。完整实现应在源代码中，文档中引用接口签名后使用 `{ ... }` 或 `...` 表示省略，并在文末注明"完整实现见源码"。

✅ 正确写法：

```csharp
public interface INodeType
{
    string TypeName { get; }
    Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct = default);
}
```

❌ 错误写法：贴 `WaitingArea` 完整类（含所有方法体）、贴 `InMemoryEventBus` 完整实现。

### 8.2 禁止元描述前置行

每篇文档首行不得出现 `> 本文说明...` 或 `> 阅读本文件可理解...` 这类元描述。文档标题已经承担了说明作用。元描述若有必要，应作为正文第一段的自然叙述，而非独立的 blockquote。

✅ 正确：直接用 `# 标题` 开篇。
❌ 错误：`> xxx 说明。阅读本文件可理解...` 空一行后 `# 标题`。

### 8.3 架构与计划内容分离

- `docs/architecture/`：维护"是什么"——设计决策、接口契约、数据流。
- `docs/plans/`：维护"要做什么"——交付物、阶段划分、验收标准。
- 计划文档不得重复架构文档中的接口签名、数据模型、设计思路，应通过链接引用架构文档。

### 8.4 文档目录与实际文件一致

`docs/` 目录树必须在 `docs-rules.md` 和 `docs/index.md` 中保持最新。删除目录分支前，确保对应文件确实不存在。新增文档时同步更新目录树。

## 9. 文档版本与变更管理

- 架构文档变更须经过 Code Review，不得无审批修改。
- 每次修改架构文档时，在文件末尾的"变更记录"表格中追加一行。
- 变更记录格式：

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
|------|--------|----------|------------|
| 2026-06-18 | Agent | 裁剪完整实现代码，保留接口签名 | 文档评审 |

- 代码实现与架构文档不一致时，以代码为准，同时更新架构文档。
