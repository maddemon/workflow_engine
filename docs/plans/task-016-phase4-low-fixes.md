# 任务：Phase 4 低优先级代码质量修复（代码审查）

## 目标
执行 `docs/plans/plan-code-review-fixes.md` 的 Phase 4（Low 级别）修复，覆盖控制器语义/RESTful 规范、错误响应统一、配置外置、并发安全与前端小幅性能优化。属代码审查收尾的最后一批。

## 待完成项（按 plan Phase 4）

### 后端
- [x] #2 `CredentialsController.Ensure` 由 `HttpPost("ensure")` 改为 `HttpPut("ensure")`（RESTful upsert；已确认前端无 `/credentials/ensure` 调用方）
- [x] #3 `NodeTypesController` 删除对注入依赖 `nodeRegistry` 的多余 `ArgumentNullException.ThrowIfNull`（ASP.NET 构造注入保证非 null，属冗余守卫）
- [x] #4 `AiWorkflowsController` 两处 `BadRequest(new { success, errorCode, message })` 改用 `ControllerExtensions.BadRequestError` 统一错误包络
- [x] #5 `WebhookHandler` 轮询间隔 `Task.Delay(100, …)` 提取为 `IOptions<WebhookOptions>.PollingIntervalMs`（默认 100，可配置）
- [x] #6 `NodeRegistry` 描述符创建改为 `Lazy<T>` 缓存，确保每种类型只创建一次、并发注册安全
- [x] #7 `SecretMasker.MaskDataBatch` 增加可选 `bool deepCopy = true` 参数，调用方已独占批次时可跳过 `DeepClone`
- [x] #1 `AuthController.Register` 状态码 `403 Forbidden` → `410 Gone`（注册按设计永久关闭，语义更准确）
- [x] 额外项 `LocalFileStorage.SanitizeFileName` 改用硬编码非法字符集合 `<>:"/\|?*\0`，替代 `Path.GetInvalidFileNameChars()`（该 API 在 Linux/macOS 上遗漏 `<>:"` 等字符，导致跨平台文件名消毒不一致）
- [ ] #8 中间件三元表达式空操作清理 —— **撤销**：现状已是正确条件分支（见 Phase 1 #2），无此问题

### 前端（#9 其余项）
- [x] `CustomNode` `computePortLayouts(...)` 用 `useMemo` 包裹（避免每渲染重算）
- [x] `useWorkflowVersionPolling` `dismiss` 用 `useCallback` 包裹
- [ ] `ParameterPanel` `retryPolicy` 解析用 `useMemo` —— 暂缓：解析位于 JSX IIFE 内，包 `useMemo` 价值极低且易引入 churn，跳过
- [ ] `NodeExecutionRecordDto.startedAt` 收紧为 `string` —— **跳过**：`startedAt` 节点未运行时确为 `null`，收紧会破坏契约
- [x] `CreateWorkflowDto.styleSettings` / `CreateApiKeyResult.id` —— 已在 #12 契约对齐中补齐，无需改动
- [ ] `displayTemplate` / `StoredFileDto.projectId` 等可选项 —— 非致命、后端无对应成员，跳过

## 完成标准
- 后端 `dotnet build FlowEngine.sln` 0 警告 0 错误；相关测试（Host/Runtime/Application）通过
- 前端 `npm run typecheck` 0 错误、`npm run build` 通过、`vitest` 通过
- 改动符合 `backend-code-rules.md` / `frontend-code-rules.md`，不扩大范围
- 仅实现需求内功能，不引入过度设计

## 完成状态
- [x] 后端 7 项（#1/#2/#3/#4/#5/#6/#7）+ #8 撤销 + LocalFileStorage 跨平台修复
- [x] 前端 #9 明确项（computePortLayouts/dismiss），其余按上表跳过

## 主要修改记录
- 见各 commit。验证：`dotnet build` 0 警告；前端 typecheck/build/vitest 通过。
