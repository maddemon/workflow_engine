# MVP-1 前端未完成工作

> 更新时间：2026-06-20
> 状态：进行中
> 参考：`docs/architecture/frontend-ui-design.md`

---

## 1. 工作流列表页（最高优先级 — MVP 必须）

### 问题

用户可以创建和保存工作流，但无法浏览、加载、删除已保存的工作流。这是 MVP 核心功能缺口。

### 参考 n8n 设计

n8n 首页是**卡片式工作流列表**，不是画布。每张卡片显示名称、更新时间、激活状态、操作菜单。

### 实现方案

**页面路由**：
- `/` → 工作流列表页
- `/workflow/:id` → 画布编辑页

**列表页内容**：
- 顶部："New Workflow" 按钮
- 卡片网格：每个工作流一张卡片（名称、版本、激活状态、节点数）
- 卡片操作：点击进入画布、三点菜单（重命名/删除）
- 空状态：引导创建第一个工作流

### 涉及文件

| 文件 | 操作 |
|------|------|
| 新增 `pages/WorkflowListPage.tsx` | 工作流列表页 |
| 新增 `components/WorkflowCard.tsx` | 工作流卡片组件 |
| 修改 `App.tsx` | 添加路由 |
| 修改 `stores/workflowStore.ts` | 新增 `listWorkflows`、`deleteWorkflow` |
| 修改 `services/api.ts` | 已有，无需改动 |

### 验收标准

- [ ] 默认进入工作流列表页
- [ ] 卡片显示名称、版本、激活状态
- [ ] 点击卡片加载工作流到画布
- [ ] 可删除工作流（有确认提示）
- [ ] "New Workflow" 创建并跳转画布
- [ ] 空列表显示引导文字

---

## 2. 节点 Settings 标签页（高优先级）

### 问题

节点的 errorStrategy、retryPolicy 等公共配置没有 UI 入口，用户无法配置。

### 参考 n8n 设计

n8n 节点详情面板有两个标签：**Parameters**（业务参数）和 **Settings**（公共配置）。

### 需要暴露的配置

| 配置项 | 类型 | 字段 | 默认值 |
|--------|------|------|--------|
| On Error | Select | `errorStrategy` | Terminate |
| Retry On Fail | Switch | `retryPolicy != null` | false |
| Max Retries | Number | `retryPolicy.maxRetries` | 2 |
| Delay (ms) | Number | `retryPolicy.baseDelayMs` | 1000 |
| Notes | TextArea | 新增字段 | '' |

### 不需要用户配置的

| 配置项 | 原因 |
|--------|------|
| isEntry | 自动推断（无入边的节点） |
| executionMode | 由节点类型定义 |
| icon | 由节点类型定义 |

### 涉及文件

| 文件 | 操作 |
|------|------|
| 修改 `ParameterPanel.tsx` | 增加 Parameters/Settings 标签切换 |
| 新增 `NodeSettingsTab.tsx` | Settings 标签页组件 |
| 修改后端 `NodeInstance` | 新增 `Notes` 字段 |
| 修改 `workflowSerializer.ts` | 序列化 Notes 字段 |

### 验收标准

- [ ] 节点面板显示 Parameters / Settings 两个标签
- [ ] Settings 标签可配置 errorStrategy、retryPolicy
- [ ] On Error 下拉：Stop / Continue
- [ ] Retry 开关控制重试参数的显示/隐藏
- [ ] Notes 文本框可输入节点备注

---

## 3. 执行错误反馈（高优先级）

### 问题

`useExecution` hook 捕获了错误但从未展示给用户。

### 涉及文件

- `hooks/useExecution.ts` — `error` 状态未被消费
- `App.tsx` — 未解构 `error`

### 验收标准

- [ ] 执行失败时显示红色 toast 通知
- [ ] 网络错误、超时、500 错误均有反馈

---

## 4. 凭据管理页面（高优先级）

### 问题

只有下拉选择，无法创建/编辑/删除凭据。

### 涉及文件

- `services/api.ts` — CRUD API 已实现
- `components/ParameterPanel/fields/CredentialField.tsx` — 只有选择功能

### 验收标准

- [ ] 可创建新凭据（名称 + 类型 + 字段）
- [ ] 可编辑已有凭据
- [ ] 可删除凭据

---

## 5. 前端单元测试（高优先级）

### 验收标准

- [ ] `npm run test` 可执行
- [ ] 核心工具函数覆盖率 ≥ 80%

---

## 6. 节点输出显示节点名称（中优先级）

### 验收标准

- [ ] 执行结果中每个节点显示用户自定义名称

---

## 7. 执行历史（中优先级）

### 验收标准

- [ ] 可查看当前工作流的历史执行记录

---

## 优先级排序

| 优先级 | 工作项 | 预估工时 |
|--------|--------|----------|
| **P0** | **工作流列表页（首页 + 加载 + 删除）** | **2 天** |
| **P0** | **节点 Settings 标签页** | **1.5 天** |
| P0 | 执行错误反馈 | 0.5 天 |
| P0 | 凭据管理页面 | 2 天 |
| P0 | 前端单元测试 | 1 天 |
| P1 | 节点输出显示名称 | 0.5 天 |
| P1 | 执行历史 | 1 天 |
| P2 | 节点执行耗时 | 0.5 天 |
