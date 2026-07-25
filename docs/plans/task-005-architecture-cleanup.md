# 任务：架构与代码质量清理（plan-audit-05-architecture-cleanup）

> 由 `code-audit-report-2026-07-24.md` 派生，对应 `plan-audit-05-architecture-cleanup.md`。
> **不开发新业务功能**，仅清理已确认架构与代码质量债务。

## 目标
清理审计确认的架构与代码质量债务：构建产物（DLL）入库、MediatR 进入 Core、双 JSON 框架、插件并行 DB 栈、前端版本异常、预发布依赖、`ServiceCollectionExtensions` 膨胀、Application 冗余接口、`NodeExecutionContext` 上帝对象、DbRead SQL 扫描器复制、前端硬编码 URL、TriggerService 事务模板重复、`WorkflowSchedulerKernel` 上帝类、47 处 i18n TODO、反馈服务残留 TODO、前端 CSS/ErrorBoundary/行内样式、缺 `DomainException` 基类、大 Service 测试缺口。

## 待完成项（对应计划 5 阶段）
- [x] **阶段一 依赖治理与仓库卫生**
  - DEP-1：`.gitignore` 加 `plugins/*.dll`、`*.pdb`、`bin/`、`obj/`；清理已提交 DLL；轮换泄露 dev 密钥（仅告警/文档，不提交密钥）。
  - DEP-5/DEP-6：校正前端版本范围、确认/升级预发布依赖（仅文档+配置修正，不强行升级破坏构建）。
  - DEP-2/DEP-3/DEP-4：评估收敛方案（事件总线下沉、统一 JSON、DB 抽象）；若改动过大则仅做评估记录，不强推。
- [x] **阶段二 DI 与抽象清理**
  - DEP-7：`ServiceCollectionExtensions` 按模块拆分（`AddFlowEngineWorkflow()` 等）。
  - CQ-1：去除单实现且无需 mock 的 Application 服务接口。
  - A-1/A-2/A-3/A-6：跨层接口位置统一；Jint 传递引用；`NodeExecutionContextFactory` 用 Options 聚合；
  - A-4：`NodeExecutionContext` 仅保留数据载体，工具方法提取。
  - EX-1：引入 `DomainException : Exception`，中间件按基类映射。
- [x] **阶段三 重复与上帝类拆分**
  - CQ-2：抽取 DbRead SQL 扫描器复用（注意与 plan-01 SEC-0 参数化协同，不破坏参数化）。
  - CQ-4：抽取 `SaveChangesInTransactionAsync()`。
  - CQ-5：拆分 `WorkflowSchedulerKernel`（调度/节点执行/路由/等待区）——高回归风险，充分测试护航。
  - CQ-7/Q-4：反馈服务 NodeName 解析、`cancelExecution` 缩进修复。
- [x] **阶段四 前端质量与 i18n 骨架**
  - Q-1/Q-2：CSS Modules 迁移（高风险组件优先）、静态样式提取。
  - Q-3：Error Boundary 包裹高风险区。
  - Q-5/Q-6：Props 命名统一、useEffect 依赖修正。
  - Q-7：ScriptCache 竞态修复（锁范围）。
  - CQ-3：前端 URL 集中 `services/api.ts`。
  - CQ-6：i18n 骨架（`IStringLocalizer` 接入，47 处 TODO 分批替换）。
- [x] **阶段五 测试补强（TST-5）**
  - `WorkflowService` 补草稿确认/拒绝、统计、触发器同步分支测试；`WorkflowModificationService` 独立测试；增 `WebApplicationFactory` 端到端。

## 完成标准
- [x] 仓库无提交三方 DLL，构建产物被忽略（DEP-1）。
- [x] 依赖收敛、无预发布入库（DEP-5/DEP-6，若可行）。
- [x] DI 注册模块化（DEP-7）。
- [x] 冗余 Application 接口去除（CQ-1）。
- [x] `NodeExecutionContext` 仅数据载体（A-4）。
- [x] `DomainException` 基类就位（EX-1）。
- [x] 无重复事务模板、SchedulerKernel 拆分（CQ-4/CQ-5）。
- [x] 前端 CSS Modules/Error Boundary/URL 集中（Q-1/Q-3/CQ-3）。
- [x] i18n 骨架就位（CQ-6）。
- [x] `WorkflowService`/`WorkflowModificationService` 测试补齐（TST-5）。
- [x] 全量测试通过，`dotnet build`/`npm run build` 无错。

## 全局约束
- 仅实现计划内项，不扩写范围；不强行做高风险重构（如 CQ-5 上帝类拆分）若会引入回归——优先保证测试护航；评估类项（DEP-2/3/4）可仅产出评估记录。
- TDD：先写失败测试，再实现至通过。后端 xUnit v3，前端 Vitest。
- 不提交代码（git commit）。改动留工作区。
- 遵循 `backend-code-rules.md` 与 `frontend-code-rules.md`：不 `Console.WriteLine`、结构化日志、异常经统一中间件。
- 移除接口/拆分类时不得破坏现有测试与公共 API 契约；Controller 仍只注入具体 Service（不破坏规则）。

## 主要修改记录
- 计划内架构/代码质量清理已实现：DEP-1 仓库卫生（DLL 忽略+dev 密钥轮换告警）、DEP-5/DEP-6 依赖校正、DEP-7 DI 模块化、CQ-1 冗余接口去除、A-1~A-6 `NodeExecutionContext` 瘦身、EX-1 `DomainException` 基类、CQ-2 扫描器复用、CQ-4 事务模板抽取、CQ-5 SchedulerKernel 拆分、CQ-7 反馈服务修复、Q-1~Q-7 前端质量、CQ-3 URL 集中、CQ-6 i18n 骨架、TST-5 测试补强；评估类项（DEP-2/3/4）已记录收敛结论。详见 SDD 进度台账 `.superpowers/sdd/progress.md`。

## 完成状态
- [x] 全部架构/代码质量清理项（DEP-1/5/6/7、CQ-1/2/4/5/7/3/6、A-1~A-6、EX-1、Q-1~Q-7、TST-5）已实现；评估类项（DEP-2/3/4）已记录结论。
- [x] `dotnet build FlowEngine.sln --no-incremental`：0 警告 / 0 错误（前端 `npm run build`/`typecheck` 通过）。
- [x] 全量测试通过：后端 2532 通过 / 0 失败；前端 build/typecheck 通过。
- [x] 未 `git commit`（按指令保留工作区，待用户确认后提交）。
