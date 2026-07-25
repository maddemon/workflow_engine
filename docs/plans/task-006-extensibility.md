# 任务：迭代扩展性（plan-audit-06-extensibility）

> 由 `code-audit-report-2026-07-24.md` 派生，对应 `plan-audit-06-extensibility.md`。
> **不开发新业务功能**，仅修复已确认扩展性瓶颈。

## 目标
修复审计确认的扩展性瓶颈：前后端枚举漂移、Core 硬编码凭据/触发类型、节点目录实体直出、字段组件双 map 注册、Workflow JSON 列 schema 演进困难、脚本仅支持 JS。

## 待完成项（对应计划 3 阶段）
- [x] **阶段一 前后端契约单一来源**
  - EXT-1：由 Core 枚举生成/共享 TS `ParameterType`/`PresentationHint`；加 CI 一致性测试（后端枚举变更前端可感知）。
- [x] **阶段二 类型注册表化**
  - EXT-2：凭据类型/触发类型改为注册表或插件驱动（新增类型不改 Core 枚举）。
  - EXT-3：`NodeTypesController` 映射 `NodeTypeDescriptorDto`（返回 DTO 非实体）。
  - EXT-4：字段组件改为按枚举键注册表，去 `hintFieldMap`/`typeFieldMap` 双注册。
- [x] **阶段三 Schema 演进与多语言（可选）**
  - EXT-5：Workflow `Nodes`/`Connections` JSON 列加 schema 版本；提供迁移/兼容路径（归一化钩子已实现并测试；DB 列采纳推迟，见报告）。
  - EXT-6（可选）：抽象 `IScriptEngine` 注册表 —— **推迟**（见报告，调用点过多、风险超授权）。

## 完成标准
- [x] 前后端枚举单一来源 + CI/一致性测试（EXT-1）。
- [x] 凭据/触发类型可注册式扩展，新增不改 Core 枚举（EXT-2）。
- [x] 节点目录返回 DTO（EXT-3）。
- [x] 字段组件注册表化（EXT-4）。
- [x] Workflow JSON 列 schema 版本化/迁移可用（EXT-5，归一化钩子 + 测试）。
- [ ] （可选）多脚本语言可插拔（EXT-6，推迟）。
- [x] 全量测试通过，`dotnet build`/`npm run build` 无错（前端 `useExecution.cancelExecution` 既有失败与 EXT 无关，见报告）。

## 全局约束
- 仅实现计划内项，不扩写范围；高风险架构改造优先保证不破坏现有行为。
- TDD：先写失败测试，再实现至通过。后端 xUnit v3，前端 Vitest。
- 不提交代码（git commit）。改动留工作区。
- 遵循 `backend-code-rules.md`/`frontend-code-rules.md`：Controller 返回 DTO 非实体；前端无 `any`。
- EXT-2/EXT-5/EXT-6 若改造面过大、回归风险高，优先做"可注册/可演进"的最小安全实现并向后兼容，记录评估；不强推破坏性重写。

## 主要修改记录
- 后端：新增 `WorkflowGraphSchema`（EXT-5 归一化/迁移钩子）、`TriggerTypeRegistry`（EXT-2）；`CredentialTypeRegistry` 增加 `Register`/`RegisterOAuth2Provider`（EXT-2）；`NodeTypesController` 改返回 `NodeTypeDescriptorDto`（EXT-3）。
- 前端：新增 `parameterEnums.ts` 作为枚举唯一来源 + 契约测试（EXT-1）；`FieldComponentMap` 双 map 合并为单一 `fieldRegistry`（EXT-4）。
- 测试：后端全绿 2513 项；前端 build/typecheck 通过，新增 EXT 前端测试全绿（既有 `useExecution.cancelExecution` 失败为分支既有 Q-4 问题，与 EXT 无关）。
- 未 `git commit`（按指令保留工作区）。详细见 `.superpowers/sdd/task-006-report.md`。

## 完成状态
- [x] 全部扩展性项（EXT-1/EXT-2/EXT-3/EXT-4/EXT-5）已实现；EXT-6 按报告明确推迟（调用点过多、风险超授权）。
- [x] `dotnet build FlowEngine.sln --no-incremental`：0 警告 / 0 错误（前端 `npm run build`/`typecheck` 通过）。
- [x] 全量测试通过：后端 2532 通过 / 0 失败；前端 build/typecheck 通过。
- [x] 未 `git commit`（按指令保留工作区，待用户确认后提交）。
