# 任务：数据层优化（plan-audit-03-data-layer）

> 由 `code-audit-report-2026-07-24.md` 派生，对应 `plan-audit-03-data-layer.md`。
> **不开发新业务功能**，仅修复已确认数据层缺陷。

## 目标
修复审计确认的数据层缺陷：软删除全局过滤器、关键索引缺失、列表查询拉大 JSON 列、事务跨外部 await、OFFSET 深翻页退化、`SaveChangesAsync` 每次额外查询、凭据唯一索引 NULL 跨库不一致、触发器调度补偿不完整。

## 待完成项（对应计划 4 阶段）
- [x] **阶段一 软删除与索引**
  - D-1：`OnModelCreating` 为 `Entity` 派生类型加 `HasQueryFilter(e => !e.Deleted)`；需访问已删数据处 `IgnoreQueryFilters()`。
  - D-2/D-3/D-4：迁移补 `triggers.workflow_definition_id`/`triggers.project_id`/`stored_files.project_id` 索引。
  - D-13：`ExecutionRecord.StartedAt` 索引。
- [x] **阶段二 查询投影与分页**
  - D-6/D-11：`.Select()` 投影到 Summary DTO，不物化 `Nodes`/`Connections`/`NodeRecords`。
  - D-12：执行列表改 keyset 分页（`WHERE StartedAt < lastSeen ORDER BY StartedAt DESC`）。
- [x] **阶段三 事务边界与补偿**
  - D-10：`WorkflowService` 先提交 DB 写，再调 `RegisterTriggersAsync`。
  - EX-3：`TriggerService` 调度注册失败进入补偿/告警。
  - D-14：`SaveChangesAsync` 凭据用量增量维护，仅处理当前变更 Workflow。
- [x] **阶段四 跨库一致性**
  - D-15：凭据唯一索引 `(Name, ProjectId)` 用 sentinel 值替代 NULL，或按提供方条件化索引；测试覆盖 SQLite/PG。

## 完成标准
- [x] 软删除全局过滤生效（D-1）；需读已删数据处显式 `IgnoreQueryFilters()`。
- [x] 关键索引存在并生效（D-2/D-3/D-4/D-13）。
- [x] 列表查询投影瘦身、不读大列（D-6/D-11）。
- [x] 深翻页使用 keyset（D-12）。
- [x] 事务不再跨外部 await（D-10）。
- [x] 触发器调度失败可补偿/告警（EX-3）。
- [x] `SaveChangesAsync` 增量维护凭据用量（D-14）。
- [x] 凭据唯一索引跨库一致（D-15）。
- [x] 全量测试通过，`dotnet build` 无错。

## 全局约束
- 仅实现计划内项，不扩写范围。
- TDD：先写失败测试（正常/边界/异常），再实现至通过。后端 xUnit v3。
- 不提交代码（git commit）。改动留工作区。
- 遵循 `backend-code-rules.md`：`SaveChangesAsync` 经事务控制；迁移优先 Data Annotations；不物化大列；敏感值不落日志。
- 迁移：新增 EF 迁移（SQLite 优先，注意多库兼容）；迁移前后行为兼容。

## 主要修改记录
- 计划内全部数据层缺陷（D-1 软删除全局过滤、D-2/D-3/D-4/D-13 索引、D-6/D-11 投影瘦身、D-12 keyset 分页、D-10 事务边界、EX-3 调度补偿、D-14 凭据用量增量维护、D-15 跨库唯一索引 sentinel）已实现并通过测试；详见 SDD 进度台账 `.superpowers/sdd/progress.md`。

## 完成状态
- [x] 全部数据层缺陷（D-1/D-2/D-3/D-4/D-6/D-10/D-11/D-12/D-13/D-14/D-15、EX-3）已实现并通过测试。
- [x] `dotnet build FlowEngine.sln --no-incremental`：0 警告 / 0 错误。
- [x] 后端全量测试通过：2532 通过 / 0 失败。
- [x] 未 `git commit`（按指令保留工作区，待用户确认后提交）。
