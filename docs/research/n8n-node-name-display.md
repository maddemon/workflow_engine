# n8n Node Name Display 技术调研报告

## 概述

n8n 的画布节点名称显示采用**两行结构**：第一行为节点标签（label），第二行为可选的副标题（subtitle）。节点是否显示副标题取决于节点类型定义和参数配置。

## NodeType 结构对比

### n8n 的 NodeType (INodeTypeDescription)

| 属性 | 类型 | 说明 |
|------|------|------|
| `name` | string | 节点类型标识 |
| `displayName` | string | 显示名称 |
| `subtitle` | string | **画布副标题模板**，用 `={{...}}` 表达式 |
| `icon` | string | 图标 |
| `group` | string[] | 分组 |
| `description` | string | 描述 |
| `properties` | INodeProperties[] | 参数定义 |

### 我们的 NodeTypeDescriptor

| 属性 | 类型 | 说明 | 对应 n8n |
|------|------|------|----------|
| `TypeName` | string | 节点类型标识 | `name` |
| `DisplayName` | string | 显示名称 | `displayName` |
| `DisplayTemplate` | string | **画布副标题模板** | `subtitle` |
| `Icon` | string | 图标 | `icon` |
| `Category` | string | 分类 | `group` |
| `Parameters` | ParameterDefinition[] | 参数定义 | `properties` |
| `Ports` | PortDefinition[] | 端口定义 | `inputs/outputs` |
| `DefaultIsEntry` | bool | 是否默认入口 | - |
| `ExecutionMode` | enum | 执行模式 | - |

### 关键差异

| 方面 | n8n | 我们系统 |
|------|-----|----------|
| subtitle 语法 | `={{$parameter["operation"]}}` | `{{paramName}}` |
| subtitle 位置 | NodeType 层 | NodeType 层 |
| 渲染逻辑 | Vue 组件 | React 组件 |

## 架构总览

```
┌─────────────────────────────────────────────────────────┐
│                    CanvasNode.vue                        │
│  (画布节点容器，处理连接点、工具栏、事件)                   │
│                                                         │
│  ┌───────────────────────────────────────────────────┐  │
│  │              CanvasNodeRenderer.vue                │  │
│  │  (根据 renderType 选择具体渲染组件)                 │  │
│  │                                                   │  │
│  │  ┌─────────────────────────────────────────────┐  │  │
│  │  │           CanvasNodeDefault.vue              │  │  │
│  │  │  (默认节点渲染，处理名称和副标题显示)          │  │  │
│  │  │                                             │  │  │
│  │  │  ┌───────────────────────────────────────┐  │  │  │
│  │  │  │           .description                │  │  │  │
│  │  │  │  ┌─────────────────────────────────┐  │  │  │  │
│  │  │  │  │  .label (节点名称，1-2行)        │  │  │  │  │
│  │  │  │  └─────────────────────────────────┘  │  │  │  │
│  │  │  │  ┌─────────────────────────────────┐  │  │  │  │
│  │  │  │  │  .subtitle (副标题，可选，1行)   │  │  │  │  │
│  │  │  │  └─────────────────────────────────┘  │  │  │  │
│  │  │  └───────────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

## 核心文件

| 文件 | 职责 |
|------|------|
| `packages/frontend/editor-ui/src/features/workflows/canvas/components/elements/nodes/CanvasNode.vue` | 画布节点容器 |
| `packages/frontend/editor-ui/src/features/workflows/canvas/components/elements/nodes/render-types/CanvasNodeDefault.vue` | 默认节点渲染（名称+副标题） |
| `packages/frontend/editor-ui/src/features/workflows/canvas/composables/useCanvasNode.ts` | 节点数据 composable |
| `packages/frontend/editor-ui/src/features/workflows/canvas/composables/useCanvasMapping.ts` | 工作流节点到画布节点映射 |
| `packages/frontend/editor-ui/src/app/stores/workflowDocument/useWorkflowDocumentRenderData.ts` | 副标题计算逻辑 |
| `packages/frontend/editor-ui/src/app/utils/nodeTypesUtils.ts` | `getNodeSubtitle()` 核心函数 |

## 数据流

```
INodeTypeDescription.subtitle (节点类型定义)
          ↓
getNodeSubtitle() (纯函数，计算副标题)
          ↓
useWorkflowDocumentRenderData.subtitleByNodeId (Map<nodeId, ComputedRef<string>>)
          ↓
useCanvasMapping → data.subtitle
          ↓
CanvasNodeDefault.vue 渲染
```

## 副标题计算逻辑

核心函数：`getNodeSubtitle()` (`nodeTypesUtils.ts:77-132`)

```typescript
export function getNodeSubtitle(
  data: INode,
  nodeType: INodeTypeDescription,
  workflow: WorkflowObjectAccessors,
): string | undefined {
  // 优先级 1: 内联备注模式
  if (data.notesInFlow) {
    return data.notes;
  }

  // 优先级 2: 节点类型定义的 subtitle 表达式
  if (nodeType?.subtitle !== undefined) {
    return workflow.expression.getSimpleParameterValue(
      data,
      nodeType.subtitle,
      'internal',
      {},
      undefined,
      PLACEHOLDER_FILLED_AT_EXECUTION_TIME,
    ) as string | undefined;
  }

  // 优先级 3: 操作名称（operation parameter）
  if (data.parameters.operation !== undefined) {
    const operation = data.parameters.operation as string;
    // 查找 operation 属性的 options，返回对应 option 的 name
    const operationData = nodeType.properties.find(
      (property) => property.name === 'operation'
    );
    if (operationData?.options) {
      const optionData = operationData.options.find(
        (option) => option.value === data.parameters.operation
      );
      if (optionData) return optionData.name;
    }
    return operation;
  }

  // 无副标题
  return undefined;
}
```

### 计算优先级

| 优先级 | 条件 | 结果 |
|--------|------|------|
| 1 | `data.notesInFlow === true` | 返回 `data.notes`（用户备注） |
| 2 | `nodeType.subtitle` 有定义 | 计算表达式值 |
| 3 | `data.parameters.operation` 存在 | 返回操作的显示名称 |
| 4 | 以上都不满足 | 返回 `undefined`（不显示副标题） |

## 节点类型定义中的 subtitle

### 类型定义

```typescript
// packages/workflow/src/interfaces.ts:2368
interface INodeTypeDescription {
  // ...
  subtitle?: string;  // 表达式，以 '=' 开头
  // ...
}
```

### 常见 subtitle 表达式模式

#### 模式 1: operation + resource（最常见，365+ 个节点使用）

```typescript
subtitle: '={{$parameter["operation"] + ": " + $parameter["resource"]}}'
```

**显示效果**：`Create: Contact`、`Get: Message`、`Update: User`

**示例节点**：Slack、Salesforce、GitHub、Notion、HubSpot 等

#### 模式 2: 单一操作名称

```typescript
subtitle: '={{ $parameter["operation"] }}'
```

**显示效果**：`Create`、`Delete`、`Update`

**示例节点**：Postgres、MySQL、Totp

#### 模式 3: 触发事件

```typescript
subtitle: '={{$parameter["event"]}}'
```

**显示效果**：`New Message`、`File Created`

**示例节点**：Slack Trigger、GitHub Trigger、Shopify Trigger

#### 模式 4: 条件表达式

```typescript
subtitle: '={{ $parameter["mode"]==="jsonToxml" ? "JSON to XML" : "XML to JSON" }}'
```

**显示效果**：根据参数值显示不同文本

**示例节点**：XML

#### 模式 5: 列表拼接

```typescript
subtitle: '=Updates: {{$parameter["updates"].join(", ")}}'
```

**显示效果**：`Updates: message, reaction`

**示例节点**：Twilio Trigger、Telegram Trigger

#### 模式 6: 自定义字段

```typescript
subtitle: '={{$parameter["tool"]}}'
subtitle: '={{ $parameter["mode"] }}'
subtitle: '={{($parameter["triggerOn"])}}'
```

**示例节点**：UProc、Set V2、Salesforce Trigger

### 无 subtitle 的节点

以下节点类型**不定义 subtitle**，因此只显示一行名称：

- **NoOp 节点**（无操作参数）
- **简单节点**（只有默认行为，无 operation/resource）
- **Code 节点**（纯代码执行）
- **Function 节点**
- **Manual Trigger**

## 渲染实现

### CanvasNodeDefault.vue 模板结构

```vue
<div :class="$style.description">
  <!-- 节点名称（label）- 始终显示 -->
  <div v-if="label" :class="$style.label">
    {{ label }}
  </div>
  
  <!-- 禁用标记 -->
  <div v-if="isDisabled" :class="$style.disabledLabel">
    ({{ i18n.baseText('node.disabled') }})
  </div>
  
  <!-- 副标题 - 条件显示 -->
  <div v-if="subtitle && !isNotInstalledCommunityNode" :class="$style.subtitle">
    {{ subtitle }}
  </div>
</div>
```

### CSS 样式

```scss
/* 描述容器 - 位于节点下方 */
.description {
  top: 100%;                    /* 节点下方 */
  position: absolute;
  width: 100%;
  min-width: calc(var(--canvas-node--width) * 2);
  margin-top: var(--spacing--2xs);
  display: flex;
  flex-direction: column;
  gap: var(--spacing--4xs);
  pointer-events: none;
}

/* 节点名称 - 支持最多2行 */
.label {
  font-size: var(--font-size--md);
  text-align: center;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;        /* 最多显示2行 */
  overflow: hidden;
  overflow-wrap: anywhere;
  font-weight: var(--font-weight--medium);
  line-height: var(--line-height--sm);
}

/* 副标题 - 单行显示 */
.subtitle {
  width: 100%;
  text-align: center;
  color: var(--color--text--tint-1);  /* 较浅颜色 */
  font-size: var(--font-size--xs);    /* 较小字号 */
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: var(--line-height--sm);
  font-weight: var(--font-weight--regular);
}
```

## 显示场景总结

| 场景 | 第一行（label） | 第二行（subtitle） | 示例 |
|------|----------------|-------------------|------|
| 有 subtitle 定义的节点 | 节点名称 | operation: resource | Slack → `Send: Message` |
| 有 operation 但无 subtitle | 节点名称 | 操作名称 | Postgres → `Select` |
| 触发器节点 | 节点名称 | 事件名称 | GitHub Trigger → `Push` |
| 简单节点（无操作） | 节点名称 | 无 | No Operation → 只显示名称 |
| 禁用节点 | 节点名称 | (Deactivated) | 任意节点 |
| 内联备注模式 | 节点名称 | 备注内容 | 用户自定义备注 |

## 关键设计决策

1. **subtitle 是表达式**：使用 `=` 前缀的表达式语法，可以动态引用节点参数
2. **优先级链**：`notesInFlow > nodeType.subtitle > operation > 无`
3. **过滤噪音**：包含 `CUSTOM_API_CALL_KEY` 的 subtitle 会被隐藏
4. **社区节点**：未安装的社区节点不显示 subtitle
5. **行数限制**：label 最多 2 行，subtitle 固定 1 行，超出部分省略

## 对 Flow Engine 的启示

1. **名称和副标题分离**：节点显示名称和功能描述是两个独立概念
2. **表达式驱动**：subtitle 可以是动态表达式，而非静态文本
3. **渐进式信息**：简单节点只显示名称，复杂节点显示操作信息
4. **CSS 实现**：使用 `-webkit-line-clamp` 控制行数，`text-overflow: ellipsis` 处理溢出
5. **绝对定位**：描述区域使用 `position: absolute` 定位在节点下方，不占用节点空间
