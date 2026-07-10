# 开发计划：清理全部 [Obsolete] 标记代码（plan-cleanup-01-obsolete-markers）

> 说明：本计划涉及的文件行号可能随开发漂移，下文一律以**代码内容**描述定位，不依赖硬编码行号。

## 1. 概述

系统处于开发阶段，无需向后兼容。本计划移除代码库中所有 `[Obsolete]` 标记及其背后的废弃实现，消除技术债与死代码。

**当前 `[Obsolete]` 分布：共 5 个逻辑分组，涉及 7 个文件。**

| # | 标记位置（文件） | 性质 | 处理 |
|---|----------|------|------|
| 1 | `plugins/FlowEngine.Plugins.Standard/JSNode.cs` — `InputHelper` 类 | 死代码（JSNode 已改用 `InputContainer`） | 直接删除 |
| 2 | `backend/FlowEngine.Core/Scripting/ScriptEngine.cs` — 整类 + `Evaluate*` 方法 | 重构残留，**无外部调用方**（全仓 `*.cs` 仅类自身定义 1 处引用） | 整文件删除 |
| 3 | `backend/FlowEngine.Core/Scripting/ScriptEvaluationExtensions.cs` — `GetScriptCache` 扩展方法 | 门面内部仍调用（被 `#pragma warning disable CS0618` 包裹） | 先改调用点再删 |
| 4 | `backend/FlowEngine.Core/Scripting/JsEngine.cs` — `ToClrValue` 方法 | 测试 `JsEngineSecurityTests.cs` 仍调用（被 `#pragma` 包裹） | 先迁测试再删 |
| 5 | `ProjectMember` 全套（见阶段四，跨 7 个文件） | 废弃功能，跨前后端 + 数据库 | 功能级移除，范围最大 |

**不覆盖范围：**
- 不改动仍在使用的 `JsEngine` 引擎本体（仅删 `ToClrValue` 方法）、`ScriptResult`、`PreparedScript`、`ScriptCache` 内部实现。
- 不改动与 `[Obsolete]` 无关的业务逻辑。

## 2. 交付物清单

| 交付物 | 类型 |
|--------|------|
| 删除 `InputHelper` 类（`JSNode.cs`） | 代码 |
| 删除 `ScriptEngine.cs` 整文件（含内部 `ExpressionCache`） | 代码 |
| 删除 `GetScriptCache` 扩展方法 + 门面内部改用 `context.ScriptCache` | 代码 |
| 迁移 `JsEngineSecurityTests` 至 `new ScriptResult(Script.Empty, ...).ToClr()` + 删除 `JsEngine.ToClrValue` | 代码/测试 |
| 移除 `ProjectMember` 废弃功能（后端/前端/CLI/迁移/测试） | 代码/数据/测试 |
| **移除所有 `#pragma warning disable CS0618` 抑制**（随上述 Obsolete 代码一并清除） | 代码 |
| **同步架构与索引文档**：`script-type.md` / `plan-000-overview.md` / `docs/index.md` | 文档 |
| `dotnet build` + `dotnet test` + 前端构建 + CLI 构建全绿 | 验证 |
| 全仓 `grep "Obsolete"` 与 `grep "CS0618"` 结果为 0（历史迁移文件除外） | 验证 |

## 3. 开发阶段

### 阶段一：删除死代码 `InputHelper`

**目标：** 移除已无引用的 `InputHelper` 辅助类。

- 删除 `JSNode.cs` 中 `[Obsolete]` 标记的 `InputHelper` 类（含其构造器 `InputHelper(List<object?>)` / `InputHelper(List<object?>, object?)`）。
- 确认 `JSNode.cs` 中 `$input` 注入仅依赖 `InputContainer`（构造处 `new InputContainer(...)`），无其他 `InputHelper` 引用；同时 `grep -rn "InputHelper"` 全仓应为 0（含 XML 注释/文档残留）。
- 验收：`dotnet build` 通过；`grep -rn "InputHelper" backend plugins` 结果为 0。

### 阶段二：删除 `ScriptEngine` 整类 + 同步架构文档

**目标：** 删除无外部调用方的废弃脚本引擎静态类，并消解与架构文档的矛盾。

- 验证：在 `backend/**/*.cs` 中搜索 `\bScriptEngine\b`，除 `ScriptEngine.cs` 自身外应为 0 条（已确认仅 1 处自引用，无 `typeof`/反射/配置字符串引用）。
- 删除 `backend/FlowEngine.Core/Scripting/ScriptEngine.cs` 整文件（含内部 `ExpressionCache` 旧缓存——已由 `IScriptCache`/`ScriptCache` 接管，无需保留）。
- **同步架构文档**（消除矛盾）：
  - `docs/architecture/script-type.md` 归位清单（`ScriptEngine 全部` 行）：将"标记 `[Obsolete]`…不物理删除（遵循项目规则）"改为"已由 `PreparedScript.RunAsync` + `ScriptResult.To*` 替代；`ScriptEngine` 已物理删除（见 plan-cleanup-01-obsolete-markers.md）"。
  - 同文档待定项"物理删除 ScriptEngine"：改为"已随 plan-cleanup-01-obsolete-markers.md 物理删除（用户明确授权：开发阶段无需向后兼容）"。
  - 在该文档末尾"变更记录"表格追加一行（日期 2026-07-10 / Agent / 内容：ScriptEngine 物理删除，同步清理计划）。
- 验收：`dotnet build` 通过；`ScriptEngine` 无残留引用；`script-type.md` 不再出现"不物理删除"。

### 阶段三：移除门面内部 Obsolete 包装

**目标：** 让 `ScriptEvaluationExtensions` 门面不再依赖被标记的 `GetScriptCache`，并清理测试对 `ToClrValue` 的依赖。

- **3.1 改造 `ScriptEvaluationExtensions.cs`：**
  - 将门面 `ExecuteAsync` 中被 `#pragma warning disable CS0618` 包裹的 `var scriptCache = context.GetScriptCache(); var prepared = scriptCache.GetOrPrepare(script);` 改为直接使用 `context.ScriptCache`：
    - **推荐方案**：`var prepared = context.ScriptCache!.GetOrPrepare(script);`（工厂保证 `NodeExecutionContext.ScriptCache` 非空；脱离工厂的单元测试应自行注入 `ScriptCache`）。
    - **兜底方案**（若现有测试构造了无 `ScriptCache` 的上下文导致失败）：将回退逻辑提取为 `ScriptEvaluationExtensions` 的 `private static IScriptCache GetOrCreateScriptCache(NodeExecutionContext)` 方法，保留 `ScriptCache is null` 时回退到默认 `IScriptCache` 的逻辑。
  - 删除 `ScriptCacheContextExtensions` 类及其 `[Obsolete] GetScriptCache` 扩展方法，移除该处 `#pragma`。
- **3.2 迁移 `tests/FlowEngine.Runtime.Tests/Scripting/JsEngineSecurityTests.cs`：**
  - 文件头部注释写有"直到阶段五迁移完成"——删除该行注释及顶部 `#pragma warning disable CS0618`。
  - 将全部 `JsEngine.ToClrValue(js.Evaluate(...))` 改为 `new ScriptResult(Script.Empty, js.Evaluate(...)).ToClr()`（与 `JsEngine.ToClrValue` 内部实现一致，见 `JsEngine.cs`）。确认测试文件已 `using FlowEngine.Core.Scripting;`（含 `ScriptResult`、`Script`）。
- **3.3 删除 `JsEngine.cs` 的 `ToClrValue` 方法及 `[Obsolete]` 标记。**
- 验收：`dotnet build` + `dotnet test` 通过；`GetScriptCache` / `ToClrValue` 无残留引用。

### 阶段四：移除 `ProjectMember` 废弃功能（范围最大，建议单独执行并二次确认）

**目标：** 彻底删除项目成员功能及其所有遗留代码、数据库表与前后端调用。

> 此阶段为功能级移除，横跨后端、数据库迁移、前端、CLI、测试。执行前建议二次确认交互/UI 无强依赖（见风险）。

**后端：**
- 删除 `backend/FlowEngine.Core/Entities/ProjectMember.cs` 实体（含 `[Obsolete]` 类标记）。
- 删除 `FlowEngineDbContext.cs` 的 `public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();` 及其上下 `#pragma warning disable/restore CS0618`。
- 删除 `ProjectService.cs` 中 `GetMembersAsync` / `AddMemberAsync` / `RemoveMemberAsync` / `UpdateMemberRoleAsync` 四个 `[Obsolete]` 方法及仅被其使用的私有 `MapToMemberDto`（连同其上下 `#pragma warning disable/restore CS0618`）。
- 删除 `ProjectsController.cs` 四个 `[Obsolete]` 成员端点（`GetMembers`/`AddMember`/`UpdateMemberRole`/`RemoveMember`）及其上下 `#pragma` 与 `[HttpGet("{id}/members")]` 等路由特性，确保路由代码一次性移除干净。
- 删除 `ProjectDtos.cs` 中 `ProjectMemberDto` / `AddProjectMemberDto` / `UpdateProjectMemberDto`。
- 清理 `AuditEventTypes.cs` 中 `MemberAdded` / `MemberRemoved` / `MemberRoleChanged`（三者均仅被成员功能使用，变为未引用）。

**数据库迁移：**
- 新增迁移 `DropProjectMembers`（或开发期重置 Sqlite 迁移）以 `DROP TABLE ProjectMembers`。
- 更新 `FlowEngineDbContextModelSnapshot.cs` 与最新 `*.Designer.cs`，移除 `ProjectMember` 实体配置。
- 验证本地数据库无 `ProjectMembers` 表残留。

**前端（已验证范围）：**
- 删除 `frontend/src/services/api.ts` 的 `getProjectMembers` / `addProjectMember` / `updateProjectMemberRole` / `removeProjectMember`（共 4 个函数）。
- 删除 `frontend/src/types/workflow.ts` 的 `ProjectMemberDto` / `AddProjectMemberDto` / `UpdateProjectMemberDto`。
- 已确认：前端 `.tsx` 组件无 `ProjectMember` 引用，无需改动 UI 页面/路由。

**CLI（已验证范围）：**
- 删除 `cli/src/types.ts` 中 `ProjectMemberDto` / `AddProjectMemberDto` / `UpdateProjectMemberDto`。
- 已确认：`cli/src/commands/**` 无任何成员接口引用，无需改动命令。

**测试：**
- 删除 `tests/FlowEngine.Application.Tests/Projects/ProjectServiceTests.cs` 中成员相关测试（`AddMemberAsync_*` / `RemoveMemberAsync_*` / `UpdateMemberRoleAsync_*` / `GetMembersAsync_*`）及 `ProjectMembers` 直接构造。

- 验收：`dotnet build` + `dotnet test` 全绿；`npm run build`（前端）通过；CLI 构建通过；`grep -rn "ProjectMember\|ProjectMembers" backend frontend cli tests` 结果为 0（迁移历史文件除外）。

## 4. 阶段依赖图

```
阶段一 (InputHelper)  ──┐
阶段二 (ScriptEngine) ──┼──> 阶段三 (GetScriptCache + ToClrValue) ──> 全局验证
阶段四 (ProjectMember) ─┘   （阶段四 独立于 一~三，建议单独执行并二次确认）
```

- 阶段一/二/四相互独立，可任意顺序。
- 阶段三依赖门面与方法调用点的已知位置（已用代码内容描述）。

## 5. 风险与待定项

| 风险 | 影响 | 应对 |
|------|------|------|
| 阶段四为功能级移除，前端可能存在成员管理 UI / 路由 | 删除 API 后前端编译或运行报错 | 已 `grep` 确认前端仅 `api.ts`+`workflow.ts` 引用，无 `.tsx` 消费；前端改完跑构建 |
| `ProjectMembers` 表删除需迁移；开发期若直接重置迁移可能丢失其他表数据 | 本地数据丢失 | 用"新增 Drop 迁移"而非重置；CI/生产暂不涉及 |
| `TreatWarningsAsErrors=true`：任何残留 `[Obsolete]` 引用或遗留 `#pragma` 会令 `dotnet build` 失败 | 编译阻断 | 每个阶段删完即 `dotnet build`；最终 `grep "Obsolete"` 与 `grep "CS0618"` 双验证 |
| `script-type.md` 与计划矛盾 | 文档不一致 | 阶段二显式同步架构文档（已列入交付物） |
| `ScriptEngine` 虽无 `*.cs` 外部引用，但可能存在反射/动态调用 | 运行时 MissingMethod | 阶段二前已确认 `typeof`/配置字符串引用为 0 |

## 6. 验收总标准

1. `grep -rn --fixed-strings "Obsolete" backend frontend cli plugins tests --include=*.cs --include=*.ts --include=*.tsx` 结果为 0（文档/历史迁移除外）。
2. `grep -rn --fixed-strings "CS0618" backend plugins tests --include=*.cs` 结果为 0（确认无残留 `#pragma` 抑制）。
3. `dotnet build` 全 solution 通过（含 `TreatWarningsAsErrors`）。
4. `dotnet test` 全部通过。
5. 前端 `tsc` / 构建通过；CLI 构建通过。
6. 阶段四完成后本地数据库无 `ProjectMembers` 表。
7. `script-type.md` / `plan-000-overview.md` / `docs/index.md` 已同步收录本清理计划。
