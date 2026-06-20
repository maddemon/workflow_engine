> 前端代码规范。所有参与本项目的 AI Agent 与协作者在修改前端代码前必须阅读。

# 前端代码规范

## 1. 技术栈

- React 18+（当前 React 19）
- TypeScript（严格模式）
- Vite（构建工具）
- ahooks（通用 hooks 库，优先替代手写 useState/useEffect；当前未引入，新增请求/防抖等场景时引入）
- Zustand（状态管理）
- React Flow（画布，`@xyflow/react`）
- Mantine（UI 组件库，`@mantine/core` + `@mantine/hooks` + `@mantine/notifications` + `@mantine/code-highlight`）
- CSS Modules（Mantine 不满足时的样式补充，避免全局样式污染）

## 2. 目录结构

前端源码统一放在 `frontend/src/` 下，按功能模块扁平组织：

```
frontend/
├── public/                       # 静态资源
├── src/
│   ├── main.tsx                  # 应用入口（注入 MantineProvider）
│   ├── App.tsx                   # 根组件
│   ├── App.css                   # 全局样式（仅保留 ReactFlow 等必要样式）
│   ├── index.css                 # 基础 reset
│   ├── theme.ts                  # Mantine 主题定义
│   ├── components/               # 组件按功能模块扁平组织
│   │   ├── Canvas/               # 画布相关（CustomNode/CustomEdge/WorkflowCanvas）
│   │   ├── NodePanel/            # 左侧节点面板（NodePanel/NodeCard）
│   │   ├── ParameterPanel/       # 右侧参数面板
│   │   │   ├── ParameterPanel.tsx
│   │   │   ├── FieldResolver.tsx # 字段分发组件
│   │   │   └── fields/           # 各类型字段子组件
│   │   ├── ExecutionPanel/       # 执行结果面板
│   │   ├── Layout/               # 布局组件（AppShell/HeaderToolbar）
│   │   └── common/               # 跨模块通用组件（NodeIcon 等）
│   ├── hooks/                    # 全局通用 hooks
│   ├── services/                 # API 请求封装
│   │   └── api.ts                # axios 封装与请求函数
│   ├── stores/                   # 全局状态（Zustand）
│   ├── types/                    # 全局类型定义
│   └── utils/                    # 纯工具函数
├── package.json
├── tsconfig.json
└── vite.config.ts
```

### 2.1 各目录职责

| 目录                           | 放什么                                  | 不放什么                       |
| ------------------------------ | --------------------------------------- | ------------------------------ |
| `components/common/`           | 跨模块复用的无业务逻辑通用 UI           | 业务逻辑、API 调用             |
| `components/{Module}/`         | 某个功能模块的组件                      | 其他模块的具体实现             |
| `components/{Module}/fields/`  | 该模块内部复用的字段子组件              | 通用组件、页面级组件           |
| `hooks/`                       | 全局通用 hooks（如 useDebounce）        | 只在某个 module 内使用的 hooks |
| `stores/`                      | 跨 module 的全局状态                    | 局部 module 状态               |
| `services/`                    | API 请求封装、请求拦截、错误处理        | 组件 UI、状态管理              |
| `types/`                       | 前后端共享的 DTO、全局 TS 类型          | 组件 props 类型（放在组件旁）  |
| `utils/`                       | 纯函数工具（日期、深拷贝等）            | 与业务相关的逻辑               |

## 3. 命名规范

| 类型            | 命名                        | 示例                   |
| --------------- | --------------------------- | ---------------------- |
| 组件文件        | PascalCase.tsx              | `NodePanel.tsx`        |
| 组件 props 接口 | `I` + 组件名 + `Props`      | `INodePanelProps`      |
| hooks 文件      | camelCase.ts，以 `use` 开头 | `useWorkflow.ts`       |
| 工具函数文件    | camelCase.ts                | `dateUtils.ts`         |
| 常量            | UPPER_SNAKE_CASE            | `MAX_RETRY_COUNT`      |
| 变量/函数       | camelCase                   | `handleSave`           |
| 类型/枚举       | PascalCase                  | `NodeTypeCategory`     |
| 样式模块        | `ComponentName.module.css`  | `NodePanel.module.css` |

## 4. 组件设计

### 4.1 函数组件 + Hooks

所有组件使用函数组件，配合 hooks 管理状态和副作用。

### 4.2 Props 必须定义类型

```tsx
interface INodePanelProps {
  nodeTypes: NodeTypeDescriptor[]
  onDragStart: (nodeType: NodeTypeDescriptor) => void
}

export function NodePanel({ nodeTypes, onDragStart }: INodePanelProps) {
  // ...
}
```

### 4.3 优先使用 ahooks 替代手写 useState/useEffect

- 数据请求、防抖节流、轮询、生命周期等场景优先使用 ahooks 提供的 hooks。
- 减少手写 `useEffect` 做数据请求、事件订阅、定时器等操作。

正确：

```tsx
import { useRequest } from "ahooks"
import { listWorkflows } from "@/services/workflowService"

export function WorkflowList() {
  const { data, loading } = useRequest(listWorkflows)

  if (loading) return <Spinner />

  return (
    <ul>
      {data?.map((w) => (
        <li key={w.id}>{w.name}</li>
      ))}
    </ul>
  )
}
```

错误：

```tsx
export function WorkflowList() {
  const [workflows, setWorkflows] = useState([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    setLoading(true)
    listWorkflows()
      .then(setWorkflows)
      .finally(() => setLoading(false))
  }, []) // ❌ 手写 useState + useEffect 处理请求

  if (loading) return <Spinner />

  return (
    <ul>
      {workflows.map((w) => (
        <li key={w.id}>{w.name}</li>
      ))}
    </ul>
  )
}
```

### 4.4 禁止在组件中直接写 API 调用

组件只负责 UI 和事件转发，API 调用统一放在 `services/` 或 module 内的 hooks 中。

正确：

```tsx
export function WorkflowList() {
  const { data, isLoading } = useWorkflows() // 封装在 hook 中

  if (isLoading) return <Spinner />

  return (
    <ul>
      {data?.map((w) => (
        <li key={w.id}>{w.name}</li>
      ))}
    </ul>
  )
}
```

错误：

```tsx
export function WorkflowList() {
  const [workflows, setWorkflows] = useState([])

  useEffect(() => {
    fetch("/api/workflows") // ❌ 组件内直接调用 API
      .then((r) => r.json())
      .then(setWorkflows)
  }, [])

  return (
    <ul>
      {workflows.map((w) => (
        <li key={w.id}>{w.name}</li>
      ))}
    </ul>
  )
}
```

## 5. 状态管理

### 5.1 状态就近原则

- 只在本组件使用的状态用 `useState`。
- 在 module 内多个组件共享的状态用 module 内的 store。
- 跨 module 共享的状态才用全局 store。

### 5.2 禁止把画布状态全局化

画布状态庞大且高频更新，应放在 `modules/canvas/stores/` 内，避免影响全局渲染。

## 6. API 请求

- 所有请求通过 `services/apiClient.ts` 封装。
- 服务端错误统一处理，组件只消费最终状态。
- 请求 URL 集中管理，禁止在组件中硬编码路径。

## 7. 类型

- 全局类型放在 `src/types/`。
- 组件 props 类型放在组件文件内或同目录 `types.ts`。
- 禁止用 `any`，与外部库交互时可用 `unknown` 后断言。
- 前后端共享的 DTO 优先从后端契约同步，手动同步时前后端保持一致。

## 8. 样式与 UI 组件库

- 优先使用 Mantine 提供的组件（Button、TextInput、Select、Switch、Paper、Stack、Group 等），避免手写 HTML + style。
- Mantine 不满足需求时，用 CSS Modules 补充，避免全局样式污染。
- 主题色、节点分类色等统一在 `src/theme.ts` 定义，不在组件中硬编码颜色。
- 不使用行内样式处理复杂布局。
- 业务组件中避免直接写 `<div>` + 样式，优先使用 Mantine 高层组件（Paper、Card、Stack、Group 等）。

## 9. 测试

### 9.1 测试策略

| 层级 | 覆盖目标 | 工具 |
|------|----------|------|
| 单元测试 | 工具函数、hooks、store 逻辑 | Vitest |
| 组件测试 | 组件渲染、用户交互 | Vitest + React Testing Library |

### 9.2 必须测试的场景

1. **工具函数**：`validateParameters`、`computeDynamicPorts`、序列化/反序列化
2. **Store 操作**：`addNode`、`saveWorkflow`、`loadWorkflow` 的状态变更
3. **类型兼容**：前后端 DTO 字段类型一致（`string` ID vs `Guid`）

### 9.3 测试命名规范

```
{函数名/组件名} - {场景} - {预期结果}
```

示例：
- `validateParameters - required field empty - returns error`
- `computeDynamicPorts - Switch with cases - generates correct output ports`

## 10. 错误示范速查

| 错误                                   | 正确                                                |
| -------------------------------------- | --------------------------------------------------- |
| 组件内直接 `fetch`/`axios`             | 通过 `services/` 或 module hooks 调用               |
| 手写 `useState` + `useEffect` 处理请求 | 使用 `ahooks` 的 `useRequest` 等                    |
| 所有状态都用全局 store                 | 按作用域选择 useState / module store / global store |
| 通用组件里写业务逻辑                   | 通用组件只处理 UI                                   |
| 手写 `<div>` + 样式实现表单/表格       | 优先使用 UI 组件库组件                              |
| 使用 `any`                             | 使用具体类型或 `unknown`                            |
| 把画布状态放全局                       | 画布状态放 `modules/canvas/stores/`                 |
| 在组件中硬编码 API URL                 | 通过 `services/` 集中管理                           |
| 跨 module 直接引用内部文件             | 通过 module 暴露的公共入口引用                      |

## 11. React Hooks 规范

### 11.1 useEffect / useLayoutEffect 必须有依赖数组

无依赖数组的 effect 会在每次渲染执行，导致性能问题和潜在无限循环。

```tsx
// ✅ 正确：有依赖数组
useLayoutEffect(() => {
  updateNodeInternals(id);
}, [id, ports.length]);

// ❌ 错误：无依赖数组，每次渲染都执行
useLayoutEffect(() => {
  updateNodeInternals(id);
});
```

### 11.2 Zustand 选择器尽量精确

避免订阅整个大数组。使用 `.length` 或 `.filter()` 等派生值减少不必要的重渲染。

```tsx
// ✅ 正确：只订阅长度
const edgeCount = useWorkflowStore((s) => s.edges.length);

// ❌ 错误：订阅整个数组
const edges = useWorkflowStore((s) => s.edges);
```

## 12. 错误处理

### 12.1 async 操作必须有错误处理

Store 中的 async 操作（`loadWorkflow`、`saveWorkflow` 等）必须有 `try/catch`，至少记录错误日志。静默失败会导致用户无法感知问题。
