# 任务：Phase 2 稳定性、功能正确性与性能加固

## 目标

修复 High 级别的执行引擎功能缺陷、前端正确性/性能、并发安全与认证安全问题。

## 待完成项

### 执行引擎功能缺陷
- [ ] 1. 取消执行是空操作 - `ExecutionsController.cs:59-62`、`ExecutionService.cs:120-145`、`WorkflowSchedulerKernel.cs:106`
- [ ] 2. UpdateAsync/ModifyAsync 从不递增 Version - `WorkflowService.cs:138-175`、`WorkflowModificationService.cs:49-116`
- [ ] 3. SwitchNode 端口缓存按 TypeName 错位 - `WorkflowSchedulerKernel.cs:645-665`、`SwitchNode.cs:62-73`
- [ ] 4. LoopNode 迭代机制从未接入执行器 - `LoopNode.cs:54-142`
- [ ] 5. OncePerItem 覆盖 session 输出 - `WorkflowSchedulerKernel.cs:256-260`

### 前后端契约对齐
- [ ] 6. 枚举 DraftStatus/WorkflowSource 大小写错位 - 后端 `DraftStatus.cs`、`WorkflowSource.cs`；前端 `types/workflow.ts`
- [ ] 7. RetryPolicy 后端对象/前端字符串错位 - 后端 `WorkflowDtos.cs:61`；前端 `types/workflow.ts:114`
- [ ] 8. CredentialFieldDefinition 字段名错位 - 后端 `CredentialFieldDefinition.cs:21,26`；前端 `types/workflow.ts:262-268`
- [ ] 9. validationRules 后端对象列表/前端字符串数组错位 - 后端 `ParameterDefinition.cs:38`；前端 `types/workflow.ts:63`

### 并发安全与性能
- [ ] 10. 静态缓存无界增长修复 - `WorkflowSchedulerKernel.cs:645-647`
- [ ] 11. ScriptCache 竞态条件修复 - `ScriptCache.cs:91-128`
- [ ] 12. CredentialService 性能优化 - `CredentialService.cs:297-310`
- [ ] 13. JsEngine 异步超时强制 - `JsEngine.cs:92-97`

### 认证安全
- [ ] 14. AuthenticationService 时序攻击防护 - `AuthenticationService.cs:107-145`

### RESTful 规范
- [ ] 15. RESTful 状态码规范化 - `ExecutionsController.cs:48` 等
- [ ] 16. 文件上传大小限制 - `FilesController.cs:30-36`
- [ ] 17. 节点目录授权 - `NodeCatalogController.cs`、`NodeTypesController.cs`

## 完成标准

- 所有 High 问题修复
- 执行引擎功能正确（取消/版本/路由/迭代/输出）
- 前后端契约对齐（枚举/RetryPolicy/凭据字段/校验规则）
- 相关单元测试通过
- 编译通过，无报错
- Code Review 通过

## 完成状态

> 千问已提交（未 commit）的实现：#10 仅加 MemoryCache（内存有界，但**端口碰撞 bug 未修**，见下）、#11 ScriptCache 竞态、#12 CredentialService 脱敏跳过解密、#13 JsEngine 超时、#14 AuthenticationService 时序攻击、#15 Execute 返回 201、#16 Files 上传大小限制、#17 节点目录 [Authorize]。这些项代码已改、待构建/测试验证与 Code Review。以下为逐项勾选：

- [x] 10. 静态缓存无界增长修复（仅内存有界；**TypeName 碰撞未修，需补端口签名维度**）
- [x] 11. ScriptCache 竞态条件修复
- [x] 12. CredentialService 性能优化（脱敏跳过解密）
- [x] 13. JsEngine 异步超时强制
- [x] 14. AuthenticationService 时序攻击防护
- [x] 15. RESTful 状态码规范化（Execute 返回 201）
- [x] 16. 文件上传大小限制
- [x] 17. 节点目录授权
- [ ] 1. 取消执行是空操作（未开始）
- [ ] 2. UpdateAsync/ModifyAsync 不递增 Version（未开始）
- [ ] 3. SwitchNode 端口缓存碰撞（千问加了 MemoryCache 但仍按 TypeName 缓存，bug 未修）
- [ ] 4. LoopNode 迭代机制未接入（未开始）
- [ ] 5. OncePerItem 覆盖 session 输出（未开始）
- [ ] 6. 枚举 DraftStatus/WorkflowSource 大小写错位（未开始）
- [ ] 7. RetryPolicy 对象/字符串错位（未开始）
- [ ] 8. CredentialFieldDefinition 字段名错位（未开始）
- [ ] 9. validationRules 对象/字符串错位（未开始）

## 主要修改记录

### 千问已落地（待验证）
- #10-17：见 git 工作区未提交改动（`git diff`）。其中 #10 仅解决内存有界，端口缓存碰撞（TypeName 维度）需在本次继续修复，详见任务 #3。
- #3 SwitchNode 端口缓存：千问将 `ConcurrentDictionary` 改为 `MemoryCache(SizeLimit=1000)`，但缓存键仍为 `input:{TypeName}` / `output:{TypeName}`，未纳入端口签名。`SwitchNode.Ports` 依赖每实例 `Cases`，不同 case 数的 switch 节点仍会碰撞 → 需改为按 `(TypeName, 端口签名)` 维度缓存（或转发 `nodeType.Ports` 直读）。
