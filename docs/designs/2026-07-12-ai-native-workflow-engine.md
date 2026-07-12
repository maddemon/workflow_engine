# AI-native 工作流引擎设计

## 1. 背景与目标

### 1.1 当前系统的问题

Flow Engine 当前的设计是**为人服务的**：

- 前端拖拽 UI、属性面板、可视化编辑——这些是给人用的交互
- 节点定义（`INodeType` + `ParameterDefinition`）是给人看的元数据
- DSL 是人机之间的"翻译层"——人通过 UI 生成 DSL

但 AI 正在成为主要的操作者。用户不想写代码，也不想拖拽节点，只想用自然语言描述需求，让 AI 自动生成工作流。

### 1.2 设计目标

重新设计节点系统和工作流生成流程，使 AI 成为主要操作者，人类成为监督者：

1. **AI-native 节点定义**：节点元数据对齐 AI 已有知识（MCP Tool、JSON Schema）
2. **Catalog API**：AI 通过 API 发现和理解可用节点
3. **AI 填空，后端组装**：AI 不生成 schema，只选择节点并填入参数
4. **MCP 暴露**：通过 MCP 协议暴露能力，让 Claude Desktop / ChatGPT 等外部 AI 调用
5. **人类审查**：AI 生成后展示可视化预览，人类确认后部署

### 1.3 核心理念

> **不建聊天 UI，建 AI 接口。**

聊天 UI 是别人的事（Claude Desktop、ChatGPT、Agent IDE）。你的事是：

- 暴露 Catalog API（AI 发现可用节点）
- 暴露工作流 API（AI 生成/修改工作流）
- 通过 MCP 协议让外部 AI 调用

> **节点目录的目标不是"告诉 AI 所有事"，而是"给 AI 一个获取精确 schema 的高效途径"。**

AI 会主动查询 Catalog API 获取精确 schema，即使它从训练数据中"知道" HTTP 怎么调用。因此设计重点是：

- Catalog API 高效（紧凑列表 + 按需详情）
- 使用标准格式（JSON Schema，AI 已理解）
- 最小化 token 消耗

---

## 2. 架构总览

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                      用户（通过 Claude Desktop / ChatGPT）        │
│              "每天早上9点从银行API拉取昨日流水"                    │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    外部 AI（Claude / GPT）                       │
│                                                                 │
│  1. 读取 MCP 工具定义 → 理解可用能力
  2. 调用 list_node_catalog → 获取节点列表
  3. 调用 get_node_detail → 获取节点 schema
  4. 调用 assemble_workflow → 提交 DSL 草稿，后端补全为完整工作流配置
  5. 调用 validate_workflow → 校验工作流
  6. 调用 confirm_workflow → 人类确认后部署                        │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    MCP Server（Flow Engine）                     │
│                                                                 │
│  暴露的 MCP 工具：                                              │
│  - list_node_catalog     获取可用节点列表                        │
│  - get_node_detail       获取节点详细 schema                     │
│  - assemble_workflow     组装 AI 提交的 DSL 草稿为完整工作流配置   │
│  - modify_workflow       修改现有工作流                           │
│  - validate_workflow     校验工作流合法性                        │
│  - get_workflow          获取工作流详情                           │
│  - confirm_workflow      确认并部署工作流                         │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                      后端服务                                    │
│                                                                 │
│  - Catalog Service      节点目录管理                            │
│  - Workflow Service     工作流 CRUD + 校验                       │
│  - Execution Engine     工作流执行                               │
│  - Version Service      版本管理                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 生成流程（从零创建）

```
用户: "每天早上9点从银行API拉取昨日流水"
    ↓
外部 AI 调用 MCP 工具:
  1. list_node_catalog → 看到 httpRequest, scheduleTrigger, postgres 等
  2. get_node_detail("httpRequest") → 获取精确 schema
  3. assemble_workflow({ nodes: [...], connections: [...] }) → 提交 DSL 草稿，后端组装为完整配置
    ↓
后端组装 + 校验
    ↓
返回工作流预览给人类
    ↓
人类确认 → confirm_workflow → 部署执行
```

### 2.3 修改流程（在现有基础上变更）

```
用户: "把 HTTP 请求的方法从 GET 改成 POST"
    ↓
外部 AI 调用 MCP 工具:
  1. get_workflow("wf-001") → 获取现有工作流
  2. modify_workflow({
       workflowId: "wf-001",
       operations: [{ op: "modify", path: "/nodes/n2/parameters/method", value: "POST" }]
     }) → 生成修改
    ↓
后端应用修改 + 校验
    ↓
返回 diff 预览给人类
    ↓
人类确认 → confirm_workflow → 部署执行
```

---

## 3. 节点定义重设计

### 3.1 当前设计（UI-oriented）

```csharp
public interface INodeType
{
    string TypeName { get; }
    string DisplayName { get; }
    string Category { get; }
    string Icon { get; }
    ExecutionMode ExecutionMode { get; }
    IReadOnlyList<PortDefinition> Ports { get; }
    bool DefaultIsEntry { get; }
    Task<NodeExecutionResult> ExecuteAsync(...);
}
```

> 注：当前 `INodeType` **没有** `Parameters` 属性。参数定义由 `ParameterDiscoverer` 反射节点 C# 属性生成，存储在 `NodeTypeDescriptor` 中。AI 适配器应从 `NodeTypeDescriptor` 读取参数，而不是从 `INodeType`。

问题：

- `DisplayName`、`Icon` 是给人看的
- `ParameterDefinition` 为 UI 渲染设计（`DisplayRule`、`Options`）
- AI 需要的是 `inputSchema`（JSON Schema），不是 UI 元数据

### 3.2 新设计（AI-native）

节点定义分离为两层：

**1. 引擎层（保持不变）**

`INodeType` 接口和执行逻辑保持不变，继续用 C# 实现。

**2. AI 层（新增）**

每个节点类型暴露一个 AI-native 的元数据描述，用于 Catalog API：

```json
{
  "name": "httpRequest",
  "displayName": "HTTP Request",
  "description": "发起 HTTP 请求。支持 GET/POST/PUT/DELETE/PATCH，可配置认证方式、请求头、请求体、超时。返回状态码和响应体。",
  "category": "Core",
  "tags": ["http", "api", "rest"],

  "inputSchema": {
    "type": "object",
    "properties": {
      "method": {
        "type": "string",
        "enum": ["GET", "POST", "PUT", "DELETE", "PATCH"],
        "default": "GET"
      },
      "url": {
        "type": "string",
        "description": "请求 URL，支持 $node['xxx'].json[0].field 表达式引用上游数据"
      },
      "headers": {
        "type": "object",
        "additionalProperties": { "type": "string" }
      },
      "body": {
        "type": ["object", "string", "null"]
      },
      "authentication": {
        "type": "string",
        "enum": ["none", "bearer", "basic", "apiKey"],
        "default": "none"
      },
      "credentialName": {
        "type": "string",
        "description": "Flow Engine 凭据系统中配置的凭据名称"
      },
      "timeoutMs": {
        "type": "number",
        "default": 30000
      }
    },
    "required": ["method", "url"]
  },

  "outputSchema": {
    "type": "object",
    "properties": {
      "statusCode": { "type": "number" },
      "headers": { "type": "object" },
      "body": { "description": "响应体，自动解析 JSON" }
    }
  },

  "ports": [
    { "name": "default", "direction": "Output", "description": "请求成功时的默认输出" },
    { "name": "error", "direction": "Output", "description": "请求失败或状态码非 2xx 时的输出" }
  ],

  "examples": [
    {
      "description": "GET 请求",
      "input": { "method": "GET", "url": "https://api.example.com/users" },
      "output": { "statusCode": 200, "body": [...] }
    }
  ]
}
```

> **关于 `outputSchema` 的说明**：当前 `PortDefinition.OutputSchema` 多数节点未填充或仅描述端口拓扑，自动适配器推导出的 `outputSchema` 通常很薄甚至为空。AI 要获得可用的上游数据结构，需要节点实现 `IAiDefinitionProvider` 手工编写 `outputSchema`。本阶段自动适配节点的 `outputSchema` 仅作占位。

### 3.3 关键设计决策

| 决策                   | 说明                                                                |
| ---------------------- | ------------------------------------------------------------------- |
| 使用 JSON Schema       | AI 已理解的标准格式，不需要额外学习                                 |
| Trigger 保留为独立类型 | Trigger 有特殊语义（工作流入口），AI 需要学习这个概念               |
| 参数分层暴露           | 标准参数只列类型，自定义参数详细描述                                |
| examples 字段          | 提供 few-shot 示例，帮助 AI 理解如何填参数                          |
| ports 字段必须完整 | AI 需要知道每个节点的入口/出口名称，才能正确描述连接关系 |
| DSL 使用 string ID | 节点 ID 用自然名称（如 `fetch`），AI 引用节点更直观；后端保证唯一性 |

### 3.4 可选覆盖接口 `IAiDefinitionProvider`

节点可通过实现该接口提供精确的 AI 元数据，覆盖适配器的自动推导：

```csharp
public interface IAiDefinitionProvider
{
    /// <summary>
    /// 返回该节点类型的 AI-native 定义。
    /// </summary>
    /// <param name="descriptor">由 ParameterDiscoverer 生成的节点描述，供参考。</param>
    AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor);
}
```

适配器优先级：`IAiDefinitionProvider.GetAiDefinition()` > 自动推导。未实现接口的节点仍可通过适配器被 AI 发现。

---

## 4. Catalog API

### 4.1 API 设计

```
GET /api/v1/node-catalog
→ 返回所有节点列表（紧凑格式）

GET /api/v1/node-catalog/{nodeName}
→ 返回该节点的完整 AI-native 定义
```

### 4.2 列表 API 响应

```json
{
  "nodes": [
    {
      "name": "scheduleTrigger",
      "displayName": "定时触发器",
      "description": "按 Cron 表达式定时触发工作流",
      "category": "Trigger",
      "tags": ["trigger", "schedule", "cron"],
      "isTrigger": true
    },
    {
      "name": "webhookTrigger",
      "displayName": "Webhook 触发器",
      "description": "通过 HTTP Webhook 触发工作流",
      "category": "Trigger",
      "tags": ["trigger", "webhook", "http"],
      "isTrigger": true
    },
    {
      "name": "manualTrigger",
      "displayName": "手动触发器",
      "description": "人工手动触发工作流",
      "category": "Trigger",
      "tags": ["trigger", "manual"],
      "isTrigger": true
    },
    {
      "name": "httpRequest",
      "displayName": "HTTP 请求",
      "description": "发起 HTTP 请求",
      "category": "Core",
      "tags": ["http", "api", "rest"],
      "isTrigger": false
    },
    {
      "name": "postgres",
      "displayName": "PostgreSQL",
      "description": "PostgreSQL 数据库操作",
      "category": "Data",
      "tags": ["database", "sql", "postgres"],
      "isTrigger": false
    },
    {
      "name": "llmTransform",
      "displayName": "LLM 转换",
      "description": "LLM 单次调用处理数据",
      "category": "AI",
      "tags": ["ai", "llm"],
      "isTrigger": false
    },
    {
      "name": "agent",
      "displayName": "Agent",
      "description": "Agent 多轮决策循环",
      "category": "AI",
      "tags": ["ai", "agent"],
      "isTrigger": false
    }
  ]
}
```

### 4.3 详情 API 响应

返回该节点的完整 JSON Schema（见 §3.2 示例）。

### 4.4 Token 效率策略

| 策略          | 说明                                                  |
| ------------- | ----------------------------------------------------- |
| 列表 API 紧凑 | 只返回 name + description，不返回 schema              |
| 按需查询      | AI 只查询它需要使用的节点详情                         |
| 标准格式      | JSON Schema 是 AI 已理解的格式，不需要额外解释        |
| examples      | 提供 1-2 个示例，帮助 AI 理解填参模式                 |
| 端口显式声明  | 详情中返回 ports，避免 AI guessing 端口名导致连接错误 |

---

## 5. AI 生成流程

### 5.1 AI 的任务

AI 不生成完整的 workflow DSL，而是：

1. **选择节点类型**（从 Catalog 中选择）
2. **填入参数**（根据节点的 inputSchema）
3. **定义连接**（指定节点之间的数据流向）

AI 的输出是一个**可直接执行的 DSL 草稿**。节点使用自然语言 string ID，连接只描述数据流向，端口、坐标、入口标记等执行细节由后端补全。

```json
{
  "name": "每日银行流水同步",
  "projectId": "optional-project-guid",
  "intent": "每天早上9点从银行API拉取昨日流水，用LLM归类交易类型，写入PostgreSQL",
  "nodes": [
    {
      "id": "trigger",
      "typeName": "scheduleTrigger",
      "parameters": { "cron": "0 9 * * *" }
    },
    {
      "id": "fetch",
      "typeName": "httpRequest",
      "parameters": {
        "method": "GET",
        "url": "https://bank-api.com/transactions?date=$json.yesterday",
        "authentication": "bearer",
        "credentialName": "bankApiToken"
      }
    },
    {
      "id": "classify",
      "typeName": "llmTransform",
      "parameters": {
        "prompt": "将以下银行流水按交易类型归类：工资、转账、消费、其他",
        "outputSchema": { "type": "array", "items": { "type": "object" } }
      }
    },
    {
      "id": "save",
      "typeName": "postgres",
      "parameters": {
        "connection": "$credentials.postgres.connectionString",
        "table": "transactions",
        "operation": "upsert"
      }
    }
  ],
  "connections": [
    { "from": "trigger", "to": "fetch" },
    { "from": "fetch", "to": "classify" },
    { "from": "classify", "to": "save" }
  ]
}
```

> **为什么用 string ID 而不是 Guid？**
> AI 引用节点时使用自然名称（如 `fetch`）最直观；后端负责保证 ID 在工作流内唯一，并映射到内部标识。
>
> **为什么连接不写端口名？**
> 大多数节点只有一个默认输入/输出端口。AI 只在分支节点（If/Switch）需要显式选择出口时才写 `fromPort` / `toPort`。后端根据节点类型的端口定义自动补全默认值。

### 5.2 后端组装与补全流程

```
AI 输出（DSL 草稿）
    ↓
遍历节点列表
    ↓
对每个节点：
    1. 校验 id 在工作流内唯一；若缺失则按 typeName 生成
    2. 校验 typeName 是否存在于 Catalog
    3. 校验 parameters 是否符合 inputSchema
    4. 根据节点类型 PortDefinition 补全 Ports 实例
    5. 补全 Name（缺省时使用 typeName 或 displayName）
    6. 补全 PositionX/Y（自动布局）
    ↓
根据 connections 生成连接
    ↓
对每个连接：
    1. 校验 from/to 节点存在
    2. 缺省 fromPort 时自动取源节点的第一个 Output 端口
    3. 缺省 toPort 时自动取目标节点的第一个 Input 端口
    4. AI 在分支节点（If/Switch）上显式填写 `fromPort`（如 `true`/`false`）选择出口
    5. `Condition` 由执行引擎根据节点参数运行时求值，**不由 AI 填写**
    ↓
校验拓扑（无环、无悬空连接、至少一个 Trigger）
    ↓
标记入口节点（第一个 Trigger 节点的 IsEntry = true）
    ↓
输出完整 workflow DSL
```

> **设计原则**：AI 只写业务关心的字段（id、typeName、parameters、connections），其余执行细节由后端统一补全。这样 AI 看到真实 DSL 结构，却不必生成无意义的 Guid 或坐标。

### 5.2.1 草稿持久化

`assemble_workflow` 不是纯计算接口。后端收到 AI DSL 草稿后：

1. 组装补全为完整 DSL
2. **创建一条未激活的 Workflow 记录**（`IsActive = false`），即草稿
3. 返回 `{ draftId, workflow }`

人类确认通过后，调用 `confirm_workflow(draftId)` 将草稿激活（`IsActive = true`）并部署。

`modify_workflow` 基于已有工作流生成**新的草稿记录**（新的 `draftId`），不影响已激活版本。`modify` 返回 `{ draftId, workflow, diff }`，人类确认时应对新的 `draftId` 调用 `confirm_workflow`。

### 5.3 校验与重试

如果校验失败，返回具体错误给 AI：

```json
{
  "valid": false,
  "errors": [
    {
      "nodeId": "fetch",
      "field": "url",
      "message": "url 必须是合法的 HTTP/HTTPS 地址"
    },
    {
      "nodeId": "classify",
      "field": "prompt",
      "message": "必填参数缺失"
    }
  ]
}
```

AI 根据错误修正参数，重新提交（最多重试 3 次）。

### 5.4 AI 自动修复

失败时后端返回结构化反馈，外部 AI 可在 `maxRetries` 内自纠，无需人类介入。

**适用场景**

1. DSL 组装/校验失败：参数类型错、必填缺失、表达式字段不存在、拓扑错误、端口不匹配
2. 工作流执行失败：HTTP 非 2xx、数据库连接失败、表达式求值失败
3. 人类审查反馈：人类拒绝并给出原因，AI 据此修改
4. 结果不符合预期：输出结构与 `outputSchema` 不符，AI 调整下游节点或 prompt

**不可自纠场景（转人工确认）**

- 业务逻辑错误
- 权限不足 / 凭据确实不存在
- 外部 API 行为变更需人工判断

**错误反馈结构**

```json
{
  "canAutoFix": true,
  "retryCount": 1,
  "maxRetries": 3,
  "errors": [
    {
      "nodeId": "fetch",
      "field": "parameters.url",
      "errorType": "InvalidExpression",
      "message": "表达式 $node['trigger'].yesterday 不存在",
      "schema": { "type": "string" },
      "suggestedFix": "可用上游字段：$node['trigger'].json.date"
    }
  ]
}
```

**自动修复流程**

```
AI 提交 DSL 草稿
    ↓
后端 assemble / validate / execute
    ↓
失败？
    ↓ 是
判断 canAutoFix
    ↓ 是
返回结构化错误 + schema + suggestedFix
    ↓
AI 修正后重新提交
    ↓
retryCount < maxRetries？
    ↓ 否
转人工确认
```

---

## 6. AI 修改流程

### 6.1 核心挑战

修改现有工作流比从零生成更复杂：

| 挑战         | 说明                                                 |
| ------------ | ---------------------------------------------------- |
| 理解现有结构 | AI 需要读懂当前工作流的节点、连接、参数              |
| 精准定位变更 | 用户说"把 HTTP 请求改成 POST"，AI 需要找到正确的节点 |
| 最小化改动   | 只改需要改的，不要重新生成整个工作流                 |
| 保持合法性   | 改完之后工作流仍然是合法的                           |
| 可审查       | 人类能看到"改了什么"再确认                           |

### 6.2 修改流程

**Step 1：AI 读取现有工作流**

```
GET /api/v1/workflows/{id}
→ 返回完整 DSL（节点 + 连接 + 参数）
```

AI 需要看到：

- 每个节点的 ID、类型、当前参数
- 节点之间的连接关系
- 工作流的整体结构

**Step 2：AI 生成修改操作**

AI 根据自然语言指令，生成一个"操作列表"。路径使用节点 string ID：

```json
{
  "instruction": "把 HTTP 请求的方法从 GET 改成 POST，URL 改成 https://api.example.com/create",
  "operations": [
    {
      "op": "modify",
      "path": "/nodes/fetch/parameters",
      "value": {
        "method": "POST",
        "url": "https://api.example.com/create"
      }
    }
  ]
}
```

**Step 3：后端验证与应用**

```
AI 输出（操作列表）
    ↓
遍历操作列表
    ↓
对每个操作：
    1. 校验操作是否合法（节点存在？参数符合 schema？）
    2. 应用操作
    ↓
校验最终工作流（拓扑、连接、参数）
    ↓
创建新的草稿记录
    ↓
返回 `{ draftId, workflow, diff }`
```

**Step 4：人类审查 Diff**

展示修改前后的对比：

```
修改前：
[Schedule] → [HTTP GET] → [LLM] → [DB]
                ↓
            url: https://old-api.com

修改后：
[Schedule] → [HTTP POST] → [LLM] → [DB]
                ↓
            url: https://api.example.com/create
            method: POST

变更内容：
- 节点 fetch: method GET → POST
- 节点 fetch: url old-api.com → api.example.com/create
```

人类确认后应用修改。

返回的 diff 必须同时提供**结构化格式**，供前端渲染和 AI 消费：

```json
{
  "diff": [
    {
      "op": "modify",
      "nodeId": "fetch",
      "field": "parameters.method",
      "before": "GET",
      "after": "POST"
    },
    {
      "op": "modify",
      "nodeId": "fetch",
      "field": "parameters.url",
      "before": "https://old-api.com",
      "after": "https://api.example.com/create"
    }
  ]
}
```

### 6.3 操作类型

| 操作         | 说明         | 示例                                  |
| ------------ | ------------ | ------------------------------------- |
| `add`        | 添加节点     | 在 `fetch` 后面加一个 HTTP 请求节点   |
| `remove`     | 删除节点     | 删除 `classify` 节点                  |
| `modify`     | 修改节点参数 | 把 `fetch` 的 method 从 GET 改成 POST |
| `connect`    | 添加连接     | 把 `fetch` 连到 `save`                |
| `disconnect` | 删除连接     | 断开 `fetch` 和 `classify` 的连接     |
| `move`       | 移动节点位置 | 把 `classify` 移到 `fetch` 后面       |

### 6.4 操作格式（操作列表）

```json
{
  "operations": [
    {
      "op": "modify",
      "path": "/nodes/fetch/parameters/method",
      "value": "POST"
    },
    {
      "op": "add",
      "node": {
        "id": "transform",
        "typeName": "code",
        "parameters": { "code": "return $input.first();" }
      },
      "after": "fetch"
    },
    {
      "op": "connect",
      "from": "fetch",
      "to": "transform"
    }
  ]
}
```

### 6.5 关键设计决策

| 决策                            | 说明                                       |
| ------------------------------- | ------------------------------------------ |
| AI 输出操作列表，不输出完整 DSL | 变更明确、Token 节省、审查友好             |
| 后端校验每个操作                | 确保操作合法，防止破坏工作流               |
| 支持批量操作                    | 一次修改可以包含多个操作                   |
| 原子执行                        | 要么全部成功，要么全部回滚                 |
| Diff 可视化                     | 人类只需要看"改了什么"，不需要看完整工作流 |

### 6.6 与生成流程的关系

| 场景     | 流程                                  |
| -------- | ------------------------------------- |
| 从零生成 | AI 调用 Catalog → 填空 → 后端组装     |
| 修改现有 | AI 读取现有 → 生成操作列表 → 后端应用 |

两者共用：

- Catalog API（AI 了解可用节点）
- 后端校验（确保合法性）
- 人类审查（确认变更）

---

## 7. Trigger 处理

### 7.1 保留 Trigger 概念

Trigger 保留为独立的节点类别，原因：

1. Trigger 有特殊语义：它们是工作流的"起点"
2. 引擎需要知道从哪个节点开始执行
3. AI 需要学习这个概念（简单且必要）

### 7.2 Trigger 类型

| Trigger 类型      | 说明         | 典型用途        |
| ----------------- | ------------ | --------------- |
| `scheduleTrigger` | 定时触发     | 每天/每小时执行 |
| `webhookTrigger`  | Webhook 触发 | 外部系统调用    |
| `manualTrigger`   | 手动触发     | 人工点击执行    |

### 7.3 入口节点标记

工作流 DSL 中，入口节点由后端自动推导。AI 无需显式声明 `isEntry`：

```json
{
  "nodes": [
    { "id": "trigger", "typeName": "scheduleTrigger", ... },
    { "id": "fetch", "typeName": "httpRequest", ... }
  ]
}
```

后端组装规则：

- 若工作流只有一个 Trigger 节点，将其 `IsEntry` 置为 `true`
- 若存在多个 Trigger 节点，默认取第一个作为入口；AI 可通过在节点上显式设置 `"isEntry": true` 覆盖
- 非 Trigger 节点不允许作为入口
- **多 Trigger 语义**：每个 Trigger 独立触发一次完整工作流执行。不同 Trigger 的 payload 进入同一个入口后的执行路径；若业务需要按 Trigger 分支，应在入口后接 If/Switch 节点根据 `$execution.triggerType` 等上下文判断

---

## 8. 表达式系统

### 8.1 表达式语法

Flow Engine 表达式是 JavaScript 子集，直接以 `$` 前缀内建变量开头，无需 `{{ }}` 包裹：

```
$node['NodeName'].json[0].field   // 引用上游节点输出
$credentials.name.key             // 引用凭据
$input.first().body               // 引用当前节点输入
$json.fieldName                   // 引用当前数据上下文字段
```

> 注：这是运行时实际支持的语法，与 CLI skill 中 `$node['NodeName'].json[0].field` 保持一致。AI 生成参数时按此语法填写。

### 8.2 节点定义中的表达式支持

`supportsExpression` 不是节点 C# 属性，而是由适配器根据 `ParameterType` 派生标注在 JSON Schema 上的提示：

```json
{
  "name": "url",
  "type": "string",
  "supportsExpression": true,
  "description": "支持 $node['xxx'].json[0].field 表达式引用上游数据"
}
```

> 派生规则：当 `ParameterType` 为 `String`、`Json`、`Code`、`Script` 时，`supportsExpression` 置为 `true`。AI 看到该标记后，即可在参数值中使用 `$` 前缀表达式。

### 8.3 AI 需要知道的

AI 需要知道：

1. 哪些参数支持表达式（`supportsExpression: true`）
2. 上游节点的输出结构（`outputSchema`）
3. 表达式语法：以 `$` 前缀内建变量开头的 JavaScript 子集，无需 `{{ }}` 包裹

这些信息在节点详情 API 中提供。

---

## 9. 人类审查

### 9.1 可视化预览

AI 生成的工作流在提交给人类审查时，展示为可视化图表：

```
[Schedule Trigger] → [HTTP Request] → [LLM Transform] → [PostgreSQL]
     每天9点           拉取银行流水        LLM 归类          写入数据库
```

### 9.2 审查流程

1. AI 生成工作流配置
2. 后端组装并校验
3. 展示可视化预览给人类
4. 人类确认或修改
5. 确认后版本化保存
6. 部署执行

### 9.3 人类可以做的

- 查看工作流图
- 查看每个节点的配置
- 修改节点参数
- 删除或添加节点
- 确认或拒绝

---

## 10. 迁移策略

### 10.1 迁移策略

本次改造**不向后兼容旧数据、旧逻辑**。工作流 DSL、导入/导出格式、前端保存格式统一采用新结构。

| Phase   | 内容                               | 说明                                                                                                   |
| ------- | ---------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Phase 1 | 重构 Workflow DSL 模型             | `NodeDefinition.Id` 改为 `string`；`PositionX/Y` 改为可选；`Connection` 端口默认化；`IsEntry` 自动推导 |
| Phase 2 | 定义 AI-native NodeDefinition 格式 | 纯新增，不破坏现有功能                                                                                 |
| Phase 3 | 实现 Catalog API                   | 现有节点通过适配器自动转换；详情必须返回 `ports` + `inputSchema` + `outputSchema`                      |
| Phase 4 | 实现后端组装服务                   | 将 AI 输出的 DSL 草稿补全为完整 DSL；校验 schema、拓扑、表达式                                         |
| Phase 5 | 实现 REST 工作流 API               | `assemble_workflow` / `modify_workflow` / `validate_workflow` / `confirm_workflow`                     |
| Phase 6 | 实现 MCP Server                    | 暴露 Catalog + 工作流 API 为 MCP 工具                                                                  |
| Phase 7 | 外部 AI 集成测试                   | 验证 Claude Desktop / ChatGPT / Agent IDE 可以通过 MCP 生成工作流                                      |

### 10.2 API 形态

**第一阶段先实现 REST API**，Agent IDE 可直接通过 HTTP 调用。Catalog API 与 Workflow API 稳定后，**第二阶段再包一层 MCP Server**，把相同能力暴露为 MCP Tools。

```
REST API:
├── GET    /api/v1/node-catalog            获取可用节点列表
├── GET    /api/v1/node-catalog/{name}     获取节点详细 schema
├── POST   /api/v1/workflows/assemble      接收 AI DSL 草稿，后端补全为完整工作流
├── POST   /api/v1/workflows/{id}/modify   修改现有工作流
├── POST   /api/v1/workflows/validate      校验工作流
├── GET    /api/v1/workflows/{id}          获取工作流详情
├── GET    /api/v1/workflows               列出所有工作流
├── POST   /api/v1/workflows/{id}/confirm  确认并部署工作流
└── POST   /api/v1/workflows/{id}/execute  执行工作流
```

> `assemble_workflow` 明确区别于旧版 LLM 生成：它只接收 AI 已填好的 DSL 草稿，由后端补全并校验，不再调用 LLM。

```
MCP Tools（REST 稳定后包装）：
├── list_node_catalog        获取可用节点列表
├── get_node_detail          获取节点详细 schema
├── assemble_workflow        组装 AI 提交的 DSL 草稿为完整工作流配置
├── modify_workflow          修改现有工作流
├── validate_workflow        校验工作流合法性
├── get_workflow             获取工作流详情
├── list_workflows           列出所有工作流
├── confirm_workflow         确认并部署工作流
└── execute_workflow         执行工作流
```

### 10.3 适配器模式

现有 `INodeType` 通过适配器自动转换为 AI-native 格式：

```csharp
public class NodeTypeAdapter
{
    public AiNodeDefinition ToAiDefinition(INodeType nodeType, NodeTypeDescriptor descriptor)
    {
        return new AiNodeDefinition
        {
            Name = nodeType.TypeName,
            DisplayName = nodeType.DisplayName,
            Description = GenerateDescription(nodeType),
            Category = nodeType.Category,
            IsTrigger = nodeType.Category == "Trigger" || nodeType.DefaultIsEntry,
            InputSchema = ConvertParameters(descriptor.Parameters),
            // outputSchema 描述节点主输出（default 端口）的数据结构。
            // 注意：自动推导通常很薄；有意义的 outputSchema 需节点实现 IAiDefinitionProvider 手工编写。
            OutputSchema = ConvertDefaultOutputSchema(descriptor.Ports),
            // ports 描述节点有哪些输入/输出端口，供 AI 建立连接
            Ports = ConvertPortDefinitions(descriptor.Ports),
            Examples = GetExamples(nodeType)
        };
    }
}
```

### 10.4 新节点开发

新节点可以直接用 AI-native 格式定义。推荐实现 `IAiDefinitionProvider` 覆盖自动推导：

```csharp
public class HttpRequestNode : INodeType, IAiDefinitionProvider
{
    public AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor)
    {
        return new AiNodeDefinition
        {
            Name = "httpRequest",
            Description = "发起 HTTP 请求。支持 GET/POST/PUT/DELETE/PATCH，可配置认证、请求头、请求体、超时。",
            Category = "Core",
            Tags = ["http", "api", "rest"],
            IsTrigger = false,
            InputSchema = new JsonSchema { ... },
            OutputSchema = new JsonSchema { ... },
            Ports =
            [
                new AiPortSchema { Name = "default", Direction = "Output", Description = "请求成功输出" },
                new AiPortSchema { Name = "error", Direction = "Output", Description = "请求失败输出" }
            ],
            Examples = [ ... ]
        };
    }
}
```

---

## 11. 与现有计划的关系

| 现有计划                      | 关系                                                                                       |
| ----------------------------- | ------------------------------------------------------------------------------------------ |
| plan-enterprise-04-mcp        | **本设计的最终形态**。MCP Server 暴露 Catalog + 工作流 API，让外部 AI 调用；先 REST 后 MCP |
| plan-enterprise-05-ai-builder | 本设计简化了 AI Builder：不需要建聊天 UI，通过 MCP 复用外部 AI 的聊天界面                  |
| natural-language-to-dsl.md    | 本设计替代了"语义解析层"的概念，改为 Catalog/Workflow API 调用                             |
| task-007-agent-ide-driven-dsl | CLI skill 可复用 Catalog API；本次改造不向后兼容旧 DSL，前端/CLI/导入导出需同步更新        |

> **重要**：本次改造对工作流 DSL 做不兼容重构（string ID、可选坐标、默认端口）。旧数据/旧逻辑不再保留，前端保存、CLI `workflow create`、导入导出格式统一采用新 DSL。

---

## 12. 待讨论

1. **节点版本**：节点定义变更时是否对外暴露版本号？AI 生成时是否需要指定节点版本？
2. **修改冲突**：多人同时修改同一个工作流时如何处理冲突？是否需要乐观锁/版本号机制？
3. **自动布局算法**：`PositionX/Y` 后端自动计算，采用何种布局策略（层次布局、网格布局）？

## 13. 已确定决策

1. **Catalog API 鉴权**：当前沿用 `NodeTypesController` 未鉴权策略。Catalog 暴露的是节点能力元数据，不包含敏感业务数据；后续若需按角色隐藏部分节点，再引入 RBAC。
2. **多 Trigger 语义**：允许多个 Trigger，每个 Trigger 独立触发一次完整执行；默认取第一个 Trigger 作为入口。
3. **不向后兼容**：旧 DSL JSON、旧导入导出格式、旧前端保存格式、旧 CLI 命令格式均不再保留。实施前需清空或迁移旧数据。
4. **AI 自动修复**：`assemble`/`modify`/`validate`/`execute` 失败时，后端返回结构化错误（含 schema、建议修复、可自纠标记），允许外部 AI 在 `maxRetries` 内自动修正并重新提交；不可自纠的错误转人工确认。

---

## 变更记录

| 日期       | 修改人 | 修改内容                                                                                 |
| ---------- | ------ | ---------------------------------------------------------------------------------------- |
| 2026-07-12 | Agent  | 创建 AI-native 工作流引擎设计文档                                                        |
| 2026-07-12 | Agent  | 新增 §6 AI 修改流程（Diff-based 修改、操作列表）                                         |
| 2026-07-12 | Agent  | 决策：不建聊天 UI，通过 MCP 暴露能力给外部 AI（Claude Desktop / ChatGPT）                |
| 2026-07-12 | Agent  | 重构 DSL：string ID、可选坐标、连接端口默认化、`IsEntry` 自动推导；明确不向后兼容        |
| 2026-07-12 | Agent | 根据 review 修正：表达式语法统一为裸 `$`；Catalog 列表统一字段；`generate` 改为 `assemble`；明确草稿持久化、diff 结构化、多 Trigger 规则、`IAiDefinitionProvider` 签名、鉴权结论、不兼容波及范围、AI 自动修复 |
