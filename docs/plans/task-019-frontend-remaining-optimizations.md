# 任务：前端剩余优化（task-018 遗留项）

## 目标
收拢 `task-018-frontend-bugfixes.md` 中**有意暂缓 / 超出范围**的剩余项，作为后续优化清单。
这些项不影响 `fix/frontend-task-018` 分支的正确性（该分支已合并至 main，commit `792bc6f`），
可独立安排，不阻塞当前合并。

本任务文档同时承载「修复 task-019 之前的前后端对齐调研结论」。先调研、后实施：
在动手改前端之前，先确认哪些项需要后端配合、是否存在破坏性修改，结论见文末「前后端对齐调研」。

## 待完成项

### F6 — 版本轮询改轻量接口（性能，需后端新增接口）
- [ ] 后端新增轻量版本查询接口（仅返回 `id` / `version` / `updatedAt`，不返回整份工作流文档）。
- [ ] 前端 `src/hooks/useWorkflowVersionPolling.ts` 改调该接口替代每 30s 全量 `getWorkflow`。
- 后端改动性质：**新增（additive），非破坏性**。不修改现有 `GET /api/v1/workflows/{id}` 的响应结构。
- 建议接口签名见「前后端对齐调研 · F6 提案」。

### F7 — 工作流列表虚拟化 / 预格式化（性能，纯前端）
- [ ] `src/components/WorkflowList/WorkflowListPage.tsx` 行数据 `useMemo` 预格式化 `formatDateTime` 结果。
- [ ] 列表行虚拟化（如 `@tanstack/react-virtual`）或分页，避免大列表全量渲染与每帧重格式化。
- 后端改动性质：**无**。后端 `GET /api/v1/workflows` 已分页（`page` / `pageSize` 参数 + `PagedResult<WorkflowSummaryDto>.TotalCount`），前端只需消费分页元数据。
- 范围评估：改动较大，建议单独走 TDD 小计划。

### F8（运行时收尾）— 彻底消除 store 循环依赖
- [ ] `src/components/Canvas/stores/canvasStore.ts` 仍 `import { useWorkflowStore}`（仅 `markWorkflowDirty()` 内 `useWorkflowStore.setState({ isDirty: true })`）。
- [ ] 方案：在 `CanvasState` 增加本地 `isDirty` 布尔并在同处置位，移除对 `useWorkflowStore` 的导入，彻底断开运行时循环（类型耦合已在 task-018 F8 解除）。
- 注意：现有测试断言「画布变更会置 `workflowStore.isDirty`」，改完需同步测试断言（改断言为画布本地 `isDirty` 或保留桥接）。
- 后端改动性质：**无**。纯前端重构。

### Minor — CredentialField 直调整改（一致性，纯前端，几分钟）
- [ ] `src/components/ParameterPanel/fields/CredentialField.tsx` 仍 `await createCredential(...)` 直调，未走 `useRequest(manual)`。
- [ ] 与 task-018 F9 精神一致，改为 `useRequest(createCredential, { manual: true })`，保留 `bumpCredentialRevision()` 与错误通知。
- 后端改动性质：**无**。纯前端一致性调整。

## 完成标准
- [ ] 每项实施时独立补 TDD 测试，`npm run build` / `npm run typecheck` / `npm test` 通过。
- [ ] 完成后发起 SubAgent Code Review。
- [ ] 更新本任务文档「完成状态」。

## 完成状态
- [ ] F6（需后端先合并轻量版本接口）
- [ ] F7（纯前端，后端已分页）
- [ ] F8（运行时收尾，纯前端）
- [ ] Minor: CredentialField 直调整改（纯前端）

## 前后端对齐调研

> 调研时间：task-018 合并后（main @ `792bc6f`）。
> 目的：确定 task-019 各项是否需要后端配合、是否存在破坏性修改。

### 调研方法
- 直接阅读后端源码：`WorkflowsController.cs`、`WorkflowService.cs`、`WorkflowDtos.cs`、`Workflow.cs` 实体。
- 核对前端实际使用：`useWorkflowVersionPolling.ts`、`WorkflowListPage.tsx`、各 store。

### 后端现状事实

**1. 工作流详情接口（F6 相关）**
- `GET /api/v1/workflows/{id:guid}` → 返回完整 `WorkflowDto`（含 `Version:int`、`UpdatedAt:DateTime?`）。
- 现有 `WorkflowDto` 顶层已带 `Version` 与 `UpdatedAt`，前端轮询当前就是全量拉这个 DTO 再取这两个字段。
- 另有 `GET /api/v1/workflows/{id:guid}/versions` → 返回 `IReadOnlyCollection<int>`（仅版本号列表，**不含 `UpdatedAt`**），无法直接支撑「按 UpdatedAt 判断是否有更新」的轮询语义。
- 另有 `GET /api/v1/workflows/{id:guid}/versions/{version:int}` → 返回完整 `WorkflowDto`（历史版本，非轻量）。
- **结论：不存在「仅返回 id/version/updatedAt」的轻量接口**，F6 需后端新增。

**2. 工作流列表接口（F7 相关）**
- `GET /api/v1/workflows` → `[FromQuery] int page = 1`、`[FromQuery] [Range(1,200)] int pageSize = 20`。
- 返回 `PagedResult<WorkflowSummaryDto>`，结构为 `{ Items, TotalCount, Page, PageSize }`。
- `WorkflowService.GetAllAsync` 内部已 `Skip/Take` 分页，且只投影列表所需标量字段（`Nodes`/`Connections` 等大 JSON 列不参与物化）。
- **结论：后端已完整支持分页，F7 纯前端消费 `page`/`pageSize`/`TotalCount` 即可，零后端改动。**

**3. 工作流实体字段**
- `Workflow.cs` 含 `Version` 列与来自 `Entity` 基类的 `UpdatedAt`。轻量接口所需字段均已落库，无需迁移。

### F6 提案（新增，非破坏性）

新增接口，不改动任何既有接口契约：

```
GET /api/v1/workflows/{id:guid}/version
→ WorkflowVersionDto { Guid Id; int Version; DateTime? UpdatedAt }
```

配套后端改动（均 additive，不影响现有契约）：
- `WorkflowApplication/Dtos/WorkflowDtos.cs`：新增 `WorkflowVersionDto` 记录（init-only）。
- `WorkflowService`：新增 `Task<WorkflowVersionDto?> GetVersionInfoAsync(Guid id, CancellationToken)`，
  经 `AsNoTracking()` 仅投影 `Id/Version/UpdatedAt` 三字段（比现有 `GetAsync` 更轻）。
- `WorkflowsController`：新增 `[HttpGet("{id:guid}/version")]` Action，返回 `ActionResult<WorkflowVersionDto>`。
- 前端 `useWorkflowVersionPolling.ts`：将 `getWorkflow(id)` 全量调用替换为 `getWorkflowVersion(id)`，
  仅比对 `version` / `updatedAt`；有变化再走现有全量拉取或提示用户刷新。

破坏性评估：
- 新增路由 `.../{id}/version` 与既有 `.../{id}/versions`（复数）路径不冲突。
- 不修改 `GET /api/v1/workflows/{id}` 响应体，现有前端代码不受影响。
- **非破坏性（additive）**。

### 破坏性修改总览表

| 项 | 需后端配合 | 修改类型 | 破坏性 | 说明 |
|----|-----------|----------|--------|------|
| F6 | 是 | 新增接口 + DTO + Service 方法 | **否（additive）** | 建议 `GET .../{id}/version`，不改动既有契约 |
| F7 | 否 | 无 | 否 | 后端已分页，前端消费即可 |
| F8 | 否 | 无 | 否 | 纯前端重构 |
| Minor | 否 | 无 | 否 | 纯前端一致性调整 |

### 实施顺序建议
1. **后端先行**：F6 的 `GET .../{id}/version` 接口单独立项、评审、合并（非破坏性，可先于前端上线，向后兼容）。
2. **前端跟进**：后端接口可用后，F6 前端改造 + F7 + F8 + Minor 统一在 `fix/frontend-task-019` 分支按 TDD 实施。
3. 若后端接口尚未就绪，F7/F8/Minor 可先行（均不依赖后端），F6 前端侧留好切换点。

## 主要修改记录
- 调研结论：F6 需后端新增 `GET /api/v1/workflows/{id}/version`（additive，非破坏）；F7 后端已分页，纯前端；F8/Minor 纯前端。（见「前后端对齐调研」）

## 说明
- 本任务与 `fix/frontend-task-018` 解耦：018 已合并不影响本任务；本任务另开分支实施。
- F6 需后端先行立项轻量版本接口，但该接口为 additive，不阻塞现有前端运行。
