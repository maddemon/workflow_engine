# 开发计划：前端审计日志查看器（plan-alpha-10-frontend-audit-log）

> 配套后端计划：`alpha/plan-alpha-02-audit-log.md`
> 横向约定与推荐实施路线见 `plan-frontend-management-ui.md`
> 阶段归属：Alpha（对应后端审计日志模块）

## 1. 概述

为后端审计事件提供前端查询与查看界面。

覆盖范围：
- 审计事件查询（筛选 + 分页）。
- 事件详情抽屉（JSON 视图）。
- 敏感字段不展示明文。

不覆盖范围：
- 后端审计采集/存储（已实现，`AuditEventsController`）。
- 审计事件写操作（由系统产生，非用户录入）。

## 2. 交付物清单

- `src/services/api.ts` 新增：`queryAuditEvents(params)` 及类型：
  - `AuditQueryParams`：对应后端 `AuditQueryParameters`（实际字段，无 actor/keyword/timeRange）：
    - `eventType?: string`（事件类型）
    - `from?: string`（起始时间，ISO 8601）
    - `to?: string`（结束时间，ISO 8601）
    - `resourceType?: string`（资源类型）
    - `resourceId?: string`（资源 ID）
    - `offset: number`、`limit: number`
    - 若产品需「按操作人/关键词」筛选，须先补后端契约，不在本期前端范围。
  - `AuditQueryResult`：`{ total: number; offset: number; limit: number; events: Record<string, unknown>[] }`（**后端返回 `{total, offset, limit, events}`，events 为动态 JSON 对象，字段不固定**）。
- `src/pages/AdminAuditPage.tsx`：筛选表单 + 表格 + 分页。
- `src/components/admin/AuditDetailDrawer.tsx`：事件详情抽屉（复用 `ExecutionPanel/CodeViewer.tsx` 渲染 JSON）。
- `src/utils/`：时间格式化、审计动作/类型中文映射（可选）。

## 3. 现有改造点（需修改的既有文件）

| 文件 | 改造内容 |
|------|----------|
| `src/services/api.ts` | 新增审计查询封装与类型 |
| `src/App.tsx` | 注册 `/admin/audit` 路由（受 RBAC 守卫） |

## 4. 开发阶段

### 阶段一：API 封装与动态结构处理

- 目标：前端可查询审计事件并正确处理动态字段。
- 核心任务：
  - 确认 `AuditQueryParameters` 实际字段（EventType/From/To/ResourceType/ResourceId/Offset/Limit）→ 定义 `AuditQueryParams`。
  - `queryAuditEvents` 封装 `GET /api/v1/audit-events`，返回 `{ total, offset, limit, events }`。
  - **动态结构处理**：`events` 每项字段不固定；前端定义 `events: Record<string, unknown>[]`，表格仅提取关键字段（`eventType`/`resourceType`/`resourceId`/时间等，缺失时显示「—」），其余字段在详情抽屉以完整 JSON 展示。
- 输入：`AuditEventsController`、`alpha/plan-alpha-02-audit-log.md`。
- 输出：可用的审计查询 API 与类型。
- 验收标准：
  - 封装正确映射查询参数；分页结构（`total/offset/limit`）与后端一致。
  - 动态字段不因缺字段报错。

### 阶段二：列表与筛选

- 目标：可筛选、分页查看审计事件。
- 核心任务：
  - `AdminAuditPage` 筛选表单（事件类型 `eventType`、资源类型 `resourceType`、资源 ID `resourceId`、起止时间 `from`/`to`）+ `Table` + `Pagination`，用 `useRequest` 拉取。
  - 时间格式化、事件类型中文展示；缺字段显示占位。
- 输入：阶段一。
- 输出：审计列表界面。

### 阶段三：详情抽屉

- 目标：查看单条事件完整内容。
- 核心任务：
  - 点击行打开 `AuditDetailDrawer`，以 JSON 展示完整事件（含非固定字段）。
- 输入：阶段二、`CodeViewer.tsx`。
- 输出：审计详情查看能力。

## 5. 阶段依赖图

```mermaid
flowchart LR
    S1[阶段一 API 封装] --> S2[阶段二 列表筛选]
    S2 --> S3[阶段三 详情抽屉]
```

## 6. 风险与待定项

| 风险/待定项 | 影响 | 应对策略 |
|------------|------|---------|
| 审计量大分页/性能 | 列表卡顿 | 默认时间倒序 + 服务端分页 |
| 动态字段结构 | 表格渲染复杂 | 仅提取关键字段，余下走详情 JSON（已在阶段一处理）。若表格因缺字段大面积显示「—」而稀疏，应降级为紧凑摘要卡片视图，每行展示事件摘要而非固定列 |

## 7. 验收总标准（含验证用例）

- 审计事件可筛选/分页/查看详情。
- 敏感字段不展示明文。
- 动态结构事件（缺部分字段）不报错，缺字段显示占位。
- 遵循前端代码规范，构建/类型检查通过。

**具体验证用例**：
1. 进入 `/admin/audit`，默认加载最近审计事件，表格显示事件类型/资源类型/资源 ID/时间；切换页码，列表与总数（`total`）对应。
2. 按事件类型 + 起止时间（`from`/`to`）筛选，结果集随之变化；清空筛选恢复全部。
3. 点击某行打开详情抽屉，展示该事件完整 JSON（含非固定字段）；某事件缺 `resource` 字段时表格该列显示「—」而非崩溃。
4. 审计事件中不含凭据明文值（如 `apiKey` 字段被脱敏或仅显示 ID）。

## 8. 测试要求

- 单元测试：审计查询参数序列化；关键字段提取函数在缺字段时返回占位。
- 组件测试（RTL）：筛选表单交互、分页切换、详情抽屉打开渲染 JSON。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
|------|--------|----------|------------|
| 2026-07-15 | Agent | 初版（根目录 plan-frontend-audit-log.md） | 前端功能缺口审计 |
| 2026-07-15 | Agent | 迁移至 alpha/ 并按规范命名；明确动态 events 结构处理；补全测试/验证用例/改造点 | 计划评审 P0/P1/P2 |
| 2026-07-15 | Agent | P1：动态字段表格稀疏时降级为紧凑卡片视图的后备方案提示 | 源码评审 |
