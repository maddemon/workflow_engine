# 任务：清理全部 [Obsolete] 标记代码

## 目标
按 `plan-cleanup-01-obsolete-markers.md` 移除全仓 `[Obsolete]` 标记及其废弃实现（InputHelper / ScriptEngine / GetScriptCache / ToClrValue / ProjectMember），并同步相关文档。

## 待完成项
- [ ] 阶段一：删除 `JSNode.cs` 的 `InputHelper` 类（确认全仓无 `InputHelper` 引用）
- [ ] 阶段二：删除 `ScriptEngine.cs` 整文件（含 `ExpressionCache`）；同步 `script-type.md` 归位清单与待定项
- [ ] 阶段三：门面 `ExecuteAsync` 改用 `context.ScriptCache!` 并删除 `ScriptCacheContextExtensions.GetScriptCache`；迁移 `JsEngineSecurityTests` 至 `new ScriptResult(Script.Empty, ...).ToClr()` 后删除 `JsEngine.ToClrValue`
- [ ] 阶段四：移除 `ProjectMember` 全套（实体/DbSet/迁移/Service/Controller/DTO/AuditEventTypes/MemberRoleChanged/前端 api+types/CLI types/测试），含 `DROP TABLE ProjectMembers` 迁移
- [ ] 移除所有残留 `#pragma warning disable CS0618`
- [ ] 同步 `plan-000-overview.md` §4.6 与 `docs/index.md`（已提前完成，复核即可）

## 完成标准
- `grep --fixed-strings "Obsolete"` 与 `grep --fixed-strings "CS0618"` 在 `backend/frontend/cli/plugins/tests` 的 `*.cs/*.ts/*.tsx` 中结果为 0（文档/历史迁移除外）
- `dotnet build` + `dotnet test` 全绿；前端 `tsc`/构建通过；CLI 构建通过
- 阶段四完成后本地数据库无 `ProjectMembers` 表
- `script-type.md` 不再出现"不物理删除"字样

## 完成状态
- [x] 阶段一：删除 `JSNode.cs` 的 `InputHelper` 类
- [x] 阶段二：删除 `ScriptEngine.cs` 整文件（含 `ExpressionCache`）；同步 `script-type.md`
- [x] 阶段三：门面改用 `context.ScriptCache` 回退 helper 并删除 `ScriptCacheContextExtensions`；迁移 `JsEngineSecurityTests` 至 `ScriptResult.ToClr()` 后删除 `JsEngine.ToClrValue`
- [x] 阶段四：移除 `ProjectMember` 全套（实体/DbSet/迁移/Service/Controller/DTO/AuditEventTypes/前端 api+types/CLI types/测试），新增 `DropProjectMembers` 迁移
- [x] 验证：`dotnet build` 0 警告 0 错误；`dotnet test` Application 247 + Runtime 240 全绿；前端 `tsc` 0 错误；CLI `tsc` 0 错误；全仓 `Obsolete`/`CS0618`/`ProjectMember` 代码引用为 0

## 主要修改记录
- 文档先行：已修订 `plan-cleanup-01-obsolete-markers.md`（修正行号硬编码、迁移路径、补 `MemberRoleChanged`/`#pragma` 清理点、补文档同步项），并同步 `script-type.md`、`plan-000-overview.md`、`docs/index.md`。
- 阶段一~三执行中曾误用 `context.ScriptCache!` 导致 Runtime 测试 NRE（测试上下文未注入 ScriptCache），已改用回退 helper `GetOrCreateScriptCache` 修复（评审2-6 预测场景）。
- 阶段四通过 `dotnet ef migrations add DropProjectMembers` 生成迁移（Up 删 `project_members` 表，Down 重建），快照已同步移除 `ProjectMember`。该迁移随应用启动自动应用（开发库若存在需经正常迁移路径）。
