# 任务：前端单元测试补充

## 目标

将前端行覆盖率从 **16.43%** 行（Task 008 实测：语句 15.29% / 分支 13.97% / 函数 9.9%，19 文件 118 用例全绿，覆盖行稀疏）提升至 65%+。原草稿"50.4%"为臆测值，实际缺口极大（~49pt），为本次剩余工作的主要投入。测试栈：Vitest + @testing-library/react + jsdom。**强制：禁止 `as any`**（违反 `frontend-code-rules §7`）。render 页面须按真实 provider 装配（AuthProvider / Router / i18n / Mantine）。低覆盖重灾区：services/api.ts 2.58%、stores/workflowStore.ts 21.85%、pages 多数 0%、ParameterPanel/fields 全 0%。

**行号说明**：文中 `:行号`（如 `:41`）取自 2026-07-17 版本源码，仅作辅助参考；执行时请以类名 / 方法名 / 签名为准确认当前源码，行号可能因后续改动偏移。

## 已核实的真实 API（对照源码）

### services/api.ts
- 导出 axios 实例名 **`api`**（**非 `apiClient`**），`baseURL: '/api/v1'`。
- 拦截器用公共 API **`api.interceptors.request.use(...)`** :41 与 **`.response.use(...)`** :61。
- **无 `apiClient.interceptors.request.handlers[0]?.fulfilled`**（那是 axios 内部结构，原草稿写法错误）。
- 其它导出：`ApiError` 类、`formatFileSize`、`ApiKeyDto`、`AuditQueryParams` / `AuditQueryResult`、`StoredFileDto`、`UploadFileResult`。

### stores/workflowStore.ts
- 真实选择状态字段：**`selectedNodeId: string | null`**（**非 `selectedNode` 对象**）。
- 真实动作：**`setSelectedNode: (nodeId: string | null) => void`**（收 string id，非 `{id,type}`）。
- 其它真实动作（节选）：`setNodes` / `setEdges` / `onNodesChange` / `onEdgesChange` / `addNode` / `removeNode` / `updateNodePosition` / `updateNodeParameters` / `updateNodeName` / `copyNode` / `pasteNode` / `addEdge` / `removeEdge` / `setWorkflowName` / `setIsActive` / `setProjectId` / `loadWorkflow` / `saveWorkflow` / `deleteWorkflow` / `newWorkflow` / `validateAllNodes` / `undo` / `redo` 等。
- **不存在**（原草稿虚构）：`setCurrentWorkflow` / `setExecutionHistory` / `clearSelectedNode` / `currentWorkflow` / `executionHistory` / `selectedNode`。

### utils
- **`src/utils/execution.ts` 不存在**；真实文件为 **`src/utils/execution.tsx`**（注意 `.tsx`）。导出 **`statusConfig`**（`Record<ExecutionStatus, {color; icon; labelKey}>`，**非 `getStatusColor`**）与 **`formatDuration(startedAt, completedAt)`** :12。
- `src/utils/tokenStore.ts`：导出对象 **`tokenStore`**，方法 `getToken` / `setToken` / **`clear`**（**非 `clearToken`**）。
- `src/utils/globalErrorHandler.ts`：导出 **`setupGlobalErrorHandlers`**（**非 `setupGlobalErrorHandler`**）。
- `src/utils/workflowSerializer.ts`：导出 **`serializeWorkflow`** / **`deserializeWorkflow`**。

### 页面装配要求（render 前必须提供对应 provider）
- `LoginPage`：需 **`<AuthProvider>`**（内部 `useAuth()`，缺省抛 "useAuth must be used within AuthProvider"）+ **Router**（`useNavigate`）+ **i18n**（`useTranslation('login')`）。仅包 `<MemoryRouter>` 会抛错。
- `HelpPage`：需 i18n（+ Mantine 组件）。
- `WorkflowEditorPage`：需 Router + i18n + `useWorkflowStore`（zustand，无需额外 provider）。
- `ExecutionHistoryPage`：需 Router + `useNavigate` + i18n。
- 全局：`vite.config.ts` 的 `test-setup.ts` 已提供 jest-dom / `ResizeObserver` mock / `matchMedia` mock；**无 Mantine setup**——页面含 Mantine 组件时需在测试内包裹 `<MantineProvider>`。

## 待完成项

- [ ] **7.1 api 拦截器与错误处理测试**：验证请求拦截器注入 `baseURL`/`Authorization` header；响应拦截器对 `ApiError` 的处理；`ApiError` 类行为。（测试文件 `src/services/__tests__/api.test.ts` 或等价路径）
- [ ] **7.2 workflowStore 测试**：用真实 action 名（`setSelectedNode(string)` / `addNode` / `removeNode` / `loadWorkflow` 等）与 `string` 类型 `selectedNodeId` 验证状态变更；**不得**使用 `selectedNode` / `setCurrentWorkflow` 等虚构 API。注意 zustand 单例需在测试间重置状态。
- [ ] **7.3 工具函数测试**：`tokenStore.getToken/setToken/clear`；`setupGlobalErrorHandlers` 调用；`serializeWorkflow` / `deserializeWorkflow` 往返；`execution.tsx` 的 `statusConfig[status].color` 与 `formatDuration`。
- [ ] **7.4 页面渲染测试**：`LoginPage` 必须包 `<AuthProvider>` + Router + i18n（必要时 `<MantineProvider>`）；`HelpPage` / `WorkflowEditorPage` / `ExecutionHistoryPage` 按上表装配后渲染不抛错、关键文案可见。

## 完成标准

- `cd frontend && npx vitest run` 全绿。
- **零 `as any`**（grep 校验）。
- 所有导入名/动作名/provider 与上文明示一致。

- 前端 `npm run build` 与 `npm run typecheck` 通过（TS 类型检查必过，新增页面测试易触发类型问题）。

## 完成状态

- [ ] 7.1
- [ ] 7.2
- [ ] 7.3
- [ ] 7.4

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：修正 `apiClient`→`api`、拦截器 `.handlers`→`.use`、store `selectedNode`/`setSelectedNode({})`→`selectedNodeId`/`setSelectedNode(string)`、`execution` 路径与 `getStatusColor`→`statusConfig`、页面缺 provider 必抛错等问题。
