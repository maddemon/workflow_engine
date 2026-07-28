# 工作流模型（Workflow Model）

> 本文档基于当前代码编写，以代码为准。涉及实体字段以 `FlowEngine.Core/Entities/` 源码为权威来源。
> 相关引擎概念见 [系统总览](architecture/overview.md)。

工作流是一张有向图，由 **工作流（Workflow）— 节点（Node）— 端口（Port）— 连接（Connection）** 构成，运行时数据以 **数据批次（DataBatch）/ 数据项（DataItem）** 沿连接流动。

## 1. 工作流（Workflow）

表 `flow.workflows`，对应实体 `Workflow`（`FlowEngine.Core/Entities/Workflow.cs`）：

| 字段 | 类型 | 含义 |
|------|------|------|
| `Id` | `Guid`（UUIDv7） | 主标识 |
| `Name` | `string` | 工作流名称 |
| `Version` | `int` | 版本号 |
| `Nodes` | `List<NodeDefinition>` | 节点实例列表（JSON 列） |
| `Connections` | `List<Connection>` | 连接列表（JSON 列） |
| `IsActive` | `bool` | 是否激活 |
| `Source` | `WorkflowSource` | 来源：人工创建 / AI 生成 |
| `DraftStatus` | `DraftStatus?` | 草稿审查状态 |
| `RowVersion` | `long` | 乐观并发行版本（防更新丢失） |

**版本管理**：`Version` 为单调递增的整型。仅在**内容实质变更**时递增（`WorkflowService.cs:292`、`WorkflowModificationService.cs:117` 的 `existing.Version += 1`），避免无意义的版本膨胀；同一 `Id` 可存在多版本，由 `GetVersionAsync(id, version)` 按版本取值。

## 2. 节点（NodeDefinition）

对应 `NodeDefinition`（`FlowEngine.Core/Entities/NodeDefinition.cs`，标注 `[NotMapped]`，作为工作流 JSON 的一部分存储）：

| 字段 | 含义 |
|------|------|
| `Id` | 节点实例 ID，工作流内唯一（AI DSL 用自然名如 `"fetch"`） |
| `TypeName` | 节点类型名（如 `Http`、`Code`、`If`、`Agent`、`Llm`） |
| `Name` | 显示名称 |
| `Parameters` | 参数字典 `Dictionary<string, object>` |
| `Ports` | 端口实例列表 `List<PortInstance>` |
| `PositionX/Y` | 画布坐标（AI 不填时后端自动布局，`int?`） |
| `IsEntry` | 是否入口节点 |
| `Disabled` | 是否禁用 |
| `RetryPolicy` | 重试策略（见 `RetryPolicy`） |
| `ErrorStrategy` | 错误处理策略（`ErrorStrategy` 枚举） |
| `Timeout` | 节点超时 |

**入口节点**：`IsEntry = true` 标记入口；若无显式入口，后端自动推导首个 **Trigger** 节点为入口（`WorkflowDraftValidator` 校验：既无 `isEntry` 也无触发器时报错）。

## 3. 端口（Port）

端口分两层定义：

- **类型端口 `PortDefinition`**（`FlowEngine.Core/Entities/PortDefinition.cs`）：节点类型的端口模板，含 `Name` / `DisplayName` / `Direction` / `Type` / `Required` / `Condition` / `AllowedTypes` / `OutputSchema` / `ExpectedSchema`。
- **实例端口 `PortInstance`**（`PortInstance.cs`）：运行期节点上的端口实例，仅含 `Name` / `Direction` / `Type`。

**方向** `PortDirection`（`Enums/PortDirection.cs`）：

| 值 | 含义 |
|----|------|
| `Input` | 输入端口 |
| `Output` | 输出端口 |

**类型** `PortType`（`Enums/PortType.cs`）：

| 值 | 含义 | 典型用途 |
|----|------|----------|
| `Main` | 主数据端口 | 普通数据流 |
| `AgentTool` | Agent 工具端口 | Agent 节点挂载子节点为工具 |
| `LLM` | LLM 供应端口 | 向 Agent/LLM 节点注入模型 |
| `Memory` | 记忆端口 | 对话记忆 |

标准端口名常量见 `FlowConstants.PortNames`（`FlowConstants.cs`）：`Input` / `Output` / `Tools` / `LLM` / `Loop` / `Done` / `Default` / `Kept` / `Discarded` / `Input 1` / `Input 2` / `True` / `False` 等。

> **端口兼容性**：`AgentTool` / `LLM` / `Memory` 非 `Main` 端口只能连**同类型**端口（`WorkflowDraftValidator` 的端口类型检查），防止错误接线。

## 4. 连接（Connection）

对应 `Connection`（`FlowEngine.Core/Entities/Connection.cs`，继承 `Entity` 并标注 `[NotMapped]`）：

> 与 `NodeDefinition` 相同，`Connection` 并非独立数据库表，而是作为工作流 JSON 列的一部分存储（随 `Workflow` 持久化）。其 `Id` 仅在工作流内部用于引用，不代表跨表外键。

| 字段 | 含义 |
|------|------|
| `SourceNodeId` | 源节点实例 ID |
| `SourcePortName` | 源端口名（缺省取源节点首个 Output 端口） |
| `TargetNodeId` | 目标节点实例 ID |
| `TargetPortName` | 目标端口名（缺省取目标节点首个 Input 端口） |
| `Condition` | 连接条件表达式（用于 If/Switch 分支） |

约束（由 `WorkflowDraftValidator` 校验）：
- 源端口必须是 `Output`、目标端口必须是 `Input`；
- 端口类型兼容；
- 非入口节点必须至少有一条入边（禁止孤立节点）。

## 5. 数据项与数据批次

数据沿连接以 **批次（Batch）** 为单位传递。

**数据项 `DataItem`**（`DataItem.cs`）：

| 字段 | 含义 |
|------|------|
| `Data` | `JsonNode?` 节点载荷 |
| `Success` | 是否成功 |
| `Error` | `NodeError?` 错误信息 |
| `SourceIndex` | 来源索引（合并后重新编号） |
| `AttachmentId` | 关联已存储文件 ID（避免大二进制写入执行记录） |

**数据批次 `DataBatch`**（`DataBatch.cs`）：包装 `List<DataItem>`。`DataBatch.Merge(a, b)` 将两个批次按顺序合并为新批次并重新索引 `SourceIndex`（不修改入参）。

## 6. 数据流示意

```text
┌──────────┐  Output       ┌──────────┐  Output        ┌──────────┐
│ Trigger  │ ───────────▶  │  Http    │ ─────────────▶ │  If      │
│ (Entry)  │   DataBatch   │          │  DataBatch      │          │
└──────────┘               └──────────┘                └──────────┘
                                │                           │ True / False
                                ▼                           ▼
                          (下游节点获得 Input 端口的 DataBatch)
```

运行时，上游节点输出 `DataBatch` 经 `Connection` 路由到下游节点的 `Input` 端口；下游节点从 `Input` 端口读取 `DataBatch`（见 `NodeExecutionContext.GetInputBatch(portName)`，默认端口 `Input`）。`$json` / `$input` 等表达式变量即以当前 `DataItem` 与整批 `DataBatch` 为作用域（见 [表达式与脚本模型](expressions.md)）。

## Memory 端口说明

- `PortType.Memory` 端口的端到端接线与持久化行为当前集中于 Agent 链路，详见 Agent 节点相关文档。
