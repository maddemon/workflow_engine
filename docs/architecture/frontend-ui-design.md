# FlowEngine 前端 UI 架构设计

> 参考 n8n 设计模式，规划 FlowEngine 前端页面结构
> 更新时间：2026-06-20

---

## 1. 页面结构

### 1.1 当前结构（画布直入）

```
App
├── HeaderToolbar（New / Undo / Redo / Name / Save / Execute）
├── NodePanel（左侧节点列表）
├── WorkflowCanvas（中央画布）
└── ParameterPanel（右侧参数面板）
```

### 1.2 目标结构（列表 → 画布）

```
App
├── 路由：/ → WorkflowListPage
├── 路由：/workflow/:id → WorkflowEditorPage
│   ├── HeaderToolbar
│   ├── NodePanel
│   ├── WorkflowCanvas
│   └── AsidePanel（ParameterPanel / ExecutionPanel / WorkflowSettings）
```

---

## 2. 工作流列表页（首页）

### 2.1 参考 n8n 设计

n8n 首页是**卡片式工作流列表**，不是画布。

### 2.2 页面元素

| 元素 | 说明 |
|------|------|
| 顶部工具栏 | "New Workflow" 按钮 + 搜索框 + 筛选器 |
| 卡片网格 | 每个工作流一张卡片 |
| 空状态 | 引导用户创建第一个工作流 |

### 2.3 卡片信息

每张工作流卡片显示：

| 字段 | 来源 | 说明 |
|------|------|------|
| 名称 | `WorkflowSummary.name` | 加粗标题 |
| 版本号 | `WorkflowSummary.version` | v1, v2, ... |
| 激活状态 | `WorkflowSummary.isActive` | Badge 标签 |
| 节点数 | 需新增或从 WorkflowDto 获取 | 统计信息 |
| 更新时间 | `WorkflowDto.updatedAt` | 相对时间 |

### 2.4 操作

| 操作 | 说明 |
|------|------|
| 点击卡片 | 加载工作流进入画布 |
| 三点菜单 → 重命名 | 编辑名称 |
| 三点菜单 → 删除 | 确认后删除 |
| "New Workflow" | 创建空白工作流并跳转画布 |

### 2.5 API 对应

```
GET    /api/v1/workflows           → WorkflowSummary[]
GET    /api/v1/workflows/:id       → WorkflowDto
PUT    /api/v1/workflows/:id       → 更新（含名称）
DELETE /api/v1/workflows/:id       → 删除
```

全部 API 已实现，只需前端 UI。

---

## 3. 节点 Settings 标签页

### 3.1 参考 n8n 设计

n8n 的节点详情面板有两个标签：**Parameters**（业务参数）和 **Settings**（公共配置）。

### 3.2 需要暴露的配置

| 配置项 | 类型 | 对应字段 | 默认值 | 说明 |
|--------|------|----------|--------|------|
| On Error | Select | `errorStrategy` | Terminate | StopWorkflow / Continue |
| Retry On Fail | Switch | `retryPolicy` != null | false | 是否启用重试 |
| Max Retries | Number | `retryPolicy.maxRetries` | 2 | 2-10 |
| Delay Between Retries | Number | `retryPolicy.baseDelayMs` | 1000 | 毫秒 |
| Notes | TextArea | 新增字段 | '' | 节点备注 |

### 3.3 不需要用户配置的

| 配置项 | 原因 |
|--------|------|
| isEntry | 自动推断（无入边的节点） |
| executionMode | 由节点类型定义，用户不感知 |
| icon | 由节点类型定义 |
| timeout | 保留后端配置，暂不暴露 |

### 3.4 UI 实现

在 `ParameterPanel` 中增加标签切换：

```
┌─────────────────────────────┐
│ [Parameters] [Settings]     │  ← 标签切换
├─────────────────────────────┤
│                             │
│  Parameters 标签：           │
│  - 业务参数（method, url...）│
│                             │
│  Settings 标签：             │
│  - On Error: [Stop ▾]       │
│  - Retry: [OFF]             │
│    - Max Retries: [2]       │
│    - Delay (ms): [1000]     │
│  - Notes: [___________]     │
│                             │
└─────────────────────────────┘
```

---

## 4. 节点数据模型更新

### 4.1 NodeInstance 需要新增字段

```csharp
// 后端 NodeInstance
public string? Notes { get; set; }        // 节点备注
```

`errorStrategy`、`retryPolicy` 已有，不需要新增。

### 4.2 前端类型更新

```typescript
// NodeInstance 已有字段（无需新增）
interface NodeInstance {
  // ... 已有字段
  errorStrategy: string;      // 'Terminate' | 'Continue'
  retryPolicy: RetryPolicy | null;
  timeout: number | null;
}
```

只需在 UI 中暴露这些字段的编辑入口。

---

## 5. 实施优先级

| 优先级 | 工作项 | 依赖 |
|--------|--------|------|
| **P0** | 工作流列表页（首页） | 路由系统 |
| **P0** | 工作流加载/删除 | 列表页 |
| **P1** | 节点 Settings 标签页 | 无 |
| **P1** | 节点备注字段 | 后端 + 前端 |
| **P2** | 工作流搜索/筛选 | 列表页 |
| **P2** | 工作流卡片样式美化 | 列表页 |
