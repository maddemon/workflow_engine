# 开发计划：前端项目分类管理 UI（plan-beta-13-frontend-project-classification）

> 配套后端计划：`beta/plan-beta-02-project-classification.md`
> 横向约定与推荐实施路线见 `plan-frontend-management-ui.md`
> 阶段归属：Beta（对应后端项目分类模块）

## 1. 概述

为「项目」资源提供前端管理界面，并让工作流列表/创建支持按项目筛选与归属。

覆盖范围：
- 项目 CRUD 页面（仅管理员可见）。
- 工作流列表接入项目筛选（含「全部 / 未分类」）。
- 创建工作流时绑定所属项目。

不覆盖范围：
- 后端项目 API（已存在 `ProjectsController` 全套 CRUD；`api.ts` 已封装 `getProjects/createProject/updateProject/deleteProject`）。
- 项目隔离逻辑（后端明确「仅筛选、不隔离」，见 `plan-beta-02`）。

## 2. 交付物清单

- `src/pages/AdminProjectsPage.tsx`：项目表格 + 新建/编辑/删除 Modal。
- `src/components/WorkflowList/ProjectFilter.tsx`：工作流列表的项目筛选下拉。
- 扩展现有创建工作流对话框：新增「所属项目」选择。
- `src/types/workflow.ts` 扩展：`WorkflowSummary` 已含 `projectId: string | null`（前端类型已定义）；`WorkflowSummary` 是否含项目名称字段（如 `projectName`）**待确认**——后端 `ProjectDto.name` 已存在，但 `WorkflowSummary` 可能未包含；若后端不返回，前端通过 `getProject(id)` 反查项目名称。

## 3. 现有改造点（需修改的既有文件）

| 文件 | 改造内容 |
|------|----------|
| `src/components/WorkflowList/WorkflowListPage.tsx` | 接入 `ProjectFilter`，按项目筛选 |
| 创建工作流相关组件/store | 新增「所属项目」字段 |
| `src/App.tsx` | 注册管理路由（受 RBAC 守卫保护） |

## 4. 开发阶段

### 阶段一：项目管理页

- 目标：管理员可管理项目。
- 核心任务：
  - `AdminProjectsPage` 用 `useRequest(getProjects)` 渲染表格，Modal 走 `createProject`/`updateProject`/`deleteProject`。
  - 删除前确认（`Mantine` 确认弹窗，含组件测试）。
- 输入：`ProjectsController`、`api.ts` 既有封装、`frontend-code-rules.md`。
- 输出：项目 CRUD 界面（admin 可见）。
- 验收标准：
  - 可新建/编辑/删除项目，列表正确展示。
  - 操作失败经统一错误处理反馈。

### 阶段二：工作流列表项目筛选

- 目标：按项目筛选工作流。
- 核心任务：
  - `ProjectFilter` 下拉含「全部 / 未分类 / 各项目」。
  - 选中后仅前端筛选展示；不选中时展示全部（**不做跨项目隐藏**）。
- 输入：阶段一、`plan-beta-02`「仅筛选不隔离」原则。
- 输出：带项目筛选的工作流列表。

### 阶段三：创建工作流绑定项目

- 目标：新建工作流归属项目。
- 核心任务：
  - 创建工作流对话框增加项目选择；提交时带 `projectId`。
  - 工作流详情/列表展示项目名称。
- 输入：阶段二、工作流创建 API。

## 5. 阶段依赖图

```mermaid
flowchart LR
    S1[阶段一 项目管理页] --> S2[阶段二 列表筛选]
    S2 --> S3[阶段三 创建绑定]
```

## 6. 风险与待定项

| 风险/待定项 | 影响 | 应对策略 |
|------------|------|---------|
| `WorkflowSummary.projectId` 已存在（`string | null`） | — | 无需补充，直接使用 |
| `WorkflowSummary` 是否含项目名展示字段待确认 | 列表/详情显示项目名称可能缺字段 | 确认 `WorkflowSummary` 是否含项目名（后端 `ProjectDto.name` 已存在，但 summary 可能未携带）；不含则前端按需调 `getProject(id)` 反查，或在创建工作流时存名称快照 |
| 误将筛选当隔离 | 数据被错误隐藏 | 代码审查强调「仅筛选」；测试带/不带项目筛选的列表 |

## 7. 验收总标准（含验证用例）

- 项目可 CRUD 且列表正确。
- 工作流列表可按项目筛选，不选中展示全部（无跨项目隐藏）。
- 新建工作流可指定项目，详情显示项目名称。
- 遵循前端代码规范，构建/类型检查通过。

**具体验证用例**：
1. 管理员进入 `/admin/projects`，点击「新建」填写名称提交，表格出现新项目；编辑/删除后状态同步。
2. 在工作流列表选择某项目筛选，仅显示该项目工作流；选择「全部」显示所有（含未分类），确认未分类工作流不被错误隐藏。
3. 删除项目时弹出确认弹窗，取消不删除、确认后从列表移除。
4. 新建工作流时选择项目并提交，进入编辑器后详情显示所属项目名称。

## 8. 测试要求

- 组件测试（RTL）：`ProjectFilter` 选项渲染与筛选回调；删除确认弹窗交互。
- 单元测试：`WorkflowListPage` 在「全部/指定项目」下的过滤逻辑（若有独立函数）。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
|------|--------|----------|------------|
| 2026-07-15 | Agent | 初版（根目录 plan-frontend-project-classification.md） | 前端功能缺口审计 |
| 2026-07-15 | Agent | 迁移至 beta/ 并按规范命名；补全测试/验证用例/改造点 | 计划评审 P0/P1/P2 |
| 2026-07-15 | Agent | P3：projectName→"项目名称"用词修正；对应风险表更新，注明后端 ProjectDto.name 已存在但 summary 可能未携带 | 源码评审 |
