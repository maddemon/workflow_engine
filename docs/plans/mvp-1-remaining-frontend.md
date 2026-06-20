# MVP-1 前端未完成工作

> 更新时间：2026-06-20
> 状态：进行中

---

## 1. 工作流管理页（最高优先级 — MVP 必须）

### 问题

用户可以创建和保存工作流，但无法浏览、加载、删除已保存的工作流。这是 MVP 核心功能缺口。

### 现状

| 组件 | 状态 |
|------|------|
| API `getWorkflows()` | ✅ 已实现 |
| API `getWorkflow(id)` | ✅ 已实现 |
| API `deleteWorkflow(id)` | ✅ 已实现 |
| Store `loadWorkflow(id)` | ✅ 已实现 |
| Store `newWorkflow()` | ✅ 已实现 |
| **UI — Open 按钮** | ❌ 缺失 |
| **UI — 工作流列表** | ❌ 缺失 |
| **UI — 删除功能** | ❌ 缺失 |

### 涉及文件

- `frontend/src/components/Layout/HeaderToolbar.tsx` — 新增 "Open" 按钮
- 新增 `frontend/src/components/WorkflowList/WorkflowListModal.tsx` — 模态框
- `frontend/src/stores/workflowStore.ts` — 新增 `listWorkflows`、`deleteWorkflow`

### 实现方案

**方案：顶部工具栏 + 模态框（最简）**

1. HeaderToolbar 新增 "Open" 按钮（与 New 按钮相邻）
2. 点击弹出 Mantine `Modal`，调用 `getWorkflows()` 获取列表
3. 列表显示：名称、版本、状态、最后修改时间
4. 点击某行调用 `loadWorkflow(id)` 加载到画布
5. 每行右侧有删除按钮（确认后调用 `deleteWorkflow(id)`）

```
┌─────────────────────────────────────┐
│ Open Workflows                  [X] │
├─────────────────────────────────────┤
│ Name              Version  Updated  │
│ ─────────────────────────────────── │
│ HTTP → If → Code     v3   2min ago │
│ 测试工作流            v1   1hr ago  │
│                                     │
│ [+ New Workflow]                    │
└─────────────────────────────────────┘
```

### 验收标准

- [ ] 点击 "Open" 弹出工作流列表
- [ ] 列表显示名称、版本号、更新时间
- [ ] 点击工作流加载到画布（节点、连线、参数全部恢复）
- [ ] 可删除工作流（有确认提示）
- [ ] 列表为空时显示引导文字

---

## 2. 执行错误反馈（高优先级）

### 问题

`useExecution` hook 捕获了错误但从未展示给用户。执行失败时无任何 UI 反馈。

### 涉及文件

- `frontend/src/hooks/useExecution.ts` — `error` 状态已定义但未被消费
- `frontend/src/App.tsx:13` — 未解构 `error`
- `frontend/src/components/ExecutionPanel/ExecutionButton.tsx:9` — 未解构 `error`

### 实现方案

1. 在 `App.tsx` 中引入 `@mantine/notifications` 的 `notifications` API
2. 在 `useExecution` 的 `execute` catch 块中调用 `notifications.show()`
3. 或在 `ExecutionButton` 中监听 `error` 状态并展示 toast

```tsx
// App.tsx
const { execution, error, clearExecution } = useExecution();

useEffect(() => {
  if (error) {
    notifications.show({ title: 'Execution Failed', message: error, color: 'red' });
  }
}, [error]);
```

### 验收标准

- [ ] 执行失败时显示红色 toast 通知
- [ ] 网络错误、超时、500 错误均有反馈
- [ ] 用户可关闭通知

---

## 2. 凭据管理页面（高优先级）

### 问题

当前只有 `CredentialField` 下拉选择器，无法创建/编辑/删除凭据。

### 涉及文件

- `frontend/src/services/api.ts` — CRUD API 已实现
- `frontend/src/types/workflow.ts` — 类型已定义
- `frontend/src/components/ParameterPanel/fields/CredentialField.tsx` — 只有选择功能

### 实现方案

新增 `CredentialPanel` 组件，作为独立的侧边栏面板或模态框：

1. **列表视图**：显示所有凭据（名称、类型、创建时间）
2. **创建/编辑表单**：名称、类型（apiKey/oauth2/basicAuth）、字段键值对
3. **删除确认**：调用 `deleteCredential` API

位置选项：
- 方案 A：顶部工具栏新增 "Credentials" 按钮，点击打开侧边面板
- 方案 B：在 ParameterPanel 的 Workflow Settings 区域增加凭据管理入口

### 涉及的新文件

- `frontend/src/components/CredentialPanel/CredentialPanel.tsx`
- `frontend/src/components/CredentialPanel/CredentialForm.tsx`

### 验收标准

- [ ] 可创建新凭据（名称 + 类型 + 字段）
- [ ] 可编辑已有凭据
- [ ] 可删除凭据（被工作流引用时提示）
- [ ] 凭据列表显示类型和创建时间

---

## 3. 前端单元测试（高优先级）

### 问题

零测试文件，无测试框架配置。

### 实现方案

1. 安装测试依赖：`vitest` + `@testing-library/react` + `@testing-library/jest-dom`
2. 配置 `vite.config.ts` 添加 test 配置
3. 添加 `test` script 到 `package.json`

### 必须测试的模块

| 模块 | 测试文件 | 测试内容 |
|------|----------|----------|
| `validateParameters` | `utils/validateParameters.test.ts` | 必填、minlength、maxlength、pattern |
| `computeDynamicPorts` | `utils/computeDynamicPorts.test.ts` | Switch 动态端口生成 |
| `workflowSerializer` | `utils/workflowSerializer.test.ts` | 序列化/反序列化一致性 |
| `useParameterValidation` | `hooks/useParameterValidation.test.ts` | hook 返回值正确性 |
| `FieldResolver` | `components/ParameterPanel/FieldResolver.test.ts` | 类型分发正确性 |

### 验收标准

- [ ] `npm run test` 可执行
- [ ] 核心工具函数覆盖率 ≥ 80%
- [ ] CI 可集成

---

## 4. 节点输出显示节点名称（中优先级）

### 问题

`NodeOutputList` 使用 `record.nodeDefinitionId`（GUID）标识节点，用户无法识别。

### 涉及文件

- `frontend/src/components/ExecutionPanel/NodeOutputList.tsx`

### 实现方案

1. 后端 `NodeExecutionRecordDto` 增加 `nodeName` 字段
2. 或前端根据 `nodeDefinitionId` 查找对应的 `NodeInstance.name`
3. 显示格式：`HTTP Request (Completed)` / `If Status OK (Failed)`

### 验收标准

- [ ] 执行结果中每个节点显示用户自定义名称
- [ ] 名称相同时追加类型后缀区分

---

## 5. 执行历史（中优先级）

### 问题

API `getWorkflowExecutions` 已实现但无 UI。

### 涉及文件

- `frontend/src/services/api.ts:59` — `getWorkflowExecutions` 已定义
- `frontend/src/components/ExecutionPanel/` — 需新增历史列表

### 实现方案

在 ExecutionPanel 中增加历史记录标签页：
1. 显示当前执行的详细结果（已有）
2. 标签切换到历史列表（新增）
3. 点击历史项加载该次执行的详情

### 验收标准

- [ ] 可查看当前工作流的历史执行记录
- [ ] 可点击加载某次执行的详细结果

---

## 6. 节点执行耗时（低优先级）

### 涉及文件

- `frontend/src/components/ExecutionPanel/NodeOutputList.tsx`

### 实现方案

在每个节点记录旁显示耗时：`Completed (236ms)`

```tsx
const duration = record.completedAt && record.startedAt
  ? new Date(record.completedAt).getTime() - new Date(record.startedAt).getTime()
  : null;
```

---

## 7. ResourceField 动态加载（低优先级）

### 涉及文件

- `frontend/src/components/ParameterPanel/fields/ResourceField.tsx:16` — TODO 注释

### 实现方案

根据 `definition.resourceType` 调用对应 API 拉取动态选项。当前只支持静态选项。

---

## 优先级排序

| 优先级 | 工作项 | 预估工时 |
|--------|--------|----------|
| **P0** | **工作流管理页（Open + 列表 + 删除）** | **1.5 天** |
| P0 | 执行错误反馈 | 0.5 天 |
| P0 | 凭据管理页面 | 2 天 |
| P0 | 前端单元测试 | 1 天 |
| P1 | 节点输出显示名称 | 0.5 天 |
| P1 | 执行历史 | 1 天 |
| P2 | 节点执行耗时 | 0.5 天 |
| P2 | ResourceField 动态加载 | 0.5 天 |
