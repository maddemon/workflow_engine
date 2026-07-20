# 测试覆盖率达标后质量加固计划

## 目标

在 `plan-unit-test-coverage.md` 已达成覆盖率目标的基础上，解决两份独立评审报告（`.superpowers/sdd/kimi.md`、`.superpowers/sdd/hy3.md）指出的稳定性、覆盖盲区和可维护性问题，把“数字达标”转化为“结果可信”。

## 合并后的核心问题

### 高优先级（影响稳定性/正确性）

1. **前端全局 mock 未清理**
   - `WorkflowEditorPage.test.tsx` 中 `window.confirm` 被覆盖后未在 `afterEach` 恢复。
   - `api.test.ts` 中 `window.location`、`window.localStorage` 被覆盖后未在 `afterEach` 恢复。
   - 存在测试间污染风险。
2. **后端审计落盘关键用例被 Skip**
   - `AuditLogFileSinkTests.OnEventAsync_CriticalEvent_WritesToFile` 在完整解决方案运行时因后台通道时序不稳定被跳过。
3. **网络依赖用例非确定性**
   - `HttpToolNodeTests.Execute_WithResolvedUrl_DoesNotReturnMissingUrl` 真实请求 `httpbin.org`，断言退化。
4. **3 个已知生产缺陷未修复**
   - `WorkflowRepository.FindReferencingCredentialAsync` 的 EF JsonElement 问题。
   - `NumericConverter` 返回 boxed `Double`。
   - `FallbackConverter` 的 string→Guid 失败。

### 中优先级（提升测试质量/覆盖盲区）

5. **前端组件/页面测试偏浅**
   - `ParameterPanel` 仅 5 个测试，缺少错误状态、边界输入、多节点切换。
   - `WorkflowEditorPage` validation modal 完整确认流程未测完。
   - `AuthContext.tsx` 行覆盖率 0%。
   - `components/admin` 行覆盖率仅 36%，Admin 页面平均 25–35%。
6. **前端 Provider 装配不统一**
   - `renderWithProvider` 仅包含 MantineProvider + i18n，缺少 AuthProvider/Router 的统一封装。
7. **前端 DOM 依赖测试脆弱**
   - `WorkflowCanvas` 测试依赖 ReactFlow 内部 CSS 类名和 DOM 结构。
8. **后端断言偏弱/覆盖偏窄**
   - `ScriptCompilerTests` 部分测试仅验证输出非空，未验证产物结构。
   - `WorkflowValidatorTests` 仅覆盖 `EnsureNonEmpty`。
   - `AuthControllerTests` 缺少登录失败、账号锁定等边界场景。
9. **Host 层测试重复与隔离不足**
   - `CreateAuthenticatedClientAsync`、`SeedUserAsync`、`MakeHttpContext`、`MakeLogger` 等辅助方法在多个测试文件中重复。
   - 同一测试类内共享 SQLite 数据库，测试间存在隐性依赖。

### 低优先级（流程/维护）

10. **分支覆盖率未设门槛**
    - 计划仅考核 line-rate；Core 行 65% 但分支仅 50%，存在逻辑分支盲区。
11. **无 CI 覆盖率门禁**
    - 75%/65% 仅为测量值，未配置流水线失败条件。
12. **测试产物堆积**
    - `tests/**/TestResults/` 下积累大量历史 `coverage.cobertura.xml`。
13. **注水测试占比**
    - Core 中约 11% 为实体/值对象往返测试，长期应替换为行为断言。

## 实施阶段

### Phase 1：稳定性止血（先执行，避免 flaky 测试污染后续结果）

- [x] 1.1 修复 `WorkflowEditorPage.test.tsx` 中 `window.confirm` / `window.location` 的 mock 清理。
- [x] 1.2 修复 `AuditLogFileSinkTests.OnEventAsync_CriticalEvent_WritesToFile` 的时序问题，移除 `[Skip]` 并恢复 CI 验证。
- [x] 1.3 隔离/替换 `HttpToolNodeTests` 中对 `httpbin.org` 的真实请求，改为本地测试服务器或注入 fake HttpClient。

### Phase 2：生产缺陷修复与回归测试（需用户决策）

- [x] 2.1 修复 `WorkflowRepository.FindReferencingCredentialAsync` 的 JsonElement 处理。
- [x] 2.2 修复 `NumericConverter` 的类型保持问题。
- [x] 2.3 修复 `FallbackConverter` 的 string→Guid 转换。
- [x] 2.4 为上述 3 处缺陷各补充 1 个回归测试。

> 注：若用户决定暂不修复生产逻辑，则改为补充“记录当前（含缺陷）行为”的测试，并在文档中明确标注 TODO。

### Phase 3：前端覆盖盲区补齐

- [x] 3.1 扩展 `renderWithProvider`，统一加入 `AuthProvider` 和可选 `MemoryRouter`。
- [x] 3.2 补充 `AuthContext.tsx` 测试（登录/登出、token 刷新、权限校验）。
- [x] 3.3 补充 `ParameterPanel` 边界与错误场景测试。
- [x] 3.4 完成 `WorkflowEditorPage` validation modal 完整确认流程测试。
- [x] 3.5 补充 Admin 页面（`AdminFilesPage`、`AdminProjectsPage`、`AdminAuditPage`）基础渲染/交互测试。
- [x] 3.6 重构 `WorkflowCanvas` 测试，减少对 ReactFlow 内部 CSS 类名的依赖。

### Phase 4：后端测试质量加固

- [x] 4.1 强化 `ScriptCompilerTests` 断言，验证 `Script.Globals` 等产物结构。
- [x] 4.2 扩展 `WorkflowValidatorTests`，覆盖连接验证、节点类型验证。
- [x] 4.3 扩展 `AuthControllerTests`，覆盖登录失败、空输入、重复注册、账户禁用等场景。
- [x] 4.4 抽取 Host 测试共享辅助方法到 `HostTestHelpers` 基类或 helper 类。
- [x] 4.5 改善 Host 测试数据隔离（每测试独立 DB 或显式清理）。

### Phase 5：流程与维护

- [x] 5.1 清理/忽略 `tests/**/TestResults/` 历史 coverage 产物。
- [x] 5.2 评估并引入分支覆盖率 secondary gate（建议后端 ≥55%）。
- [x] 5.3 配置 CI 覆盖率门禁（后端整体 ≥75%，前端 Lines ≥65%）。
- [ ] 5.4 逐步将 Core 中低价值往返测试替换为行为断言（长期，本次未执行）。

## 验收标准

- 每个 Phase 完成后，`dotnet test FlowEngine.sln` 与 `cd frontend && npm run build && npm run typecheck && npx vitest run` 全绿。
- Phase 1 完成后，全量运行不再出现被 Skip 的关键路径用例，网络依赖用例不再请求外部域名。
- Phase 2 完成后，3 个生产缺陷要么已修复并带回归测试，要么已有记录当前行为的 TODO 测试。
- Phase 3 完成后，前端 `AuthContext` 与 Admin 页面覆盖率提升至 60%+，`ParameterPanel` 与 `WorkflowEditorPage` 关键分支覆盖完整。
- Phase 4 完成后，Host 测试辅助方法重复率下降，同一测试类内数据库隔离明确。
- Phase 5 完成后，TestResults 历史产物不再出现在 git status，CI 配置包含覆盖率门禁。

## 风险与待定项

- Phase 2 涉及生产代码修改，必须等待用户明确决策后再动手。
- Phase 1.2 的审计时序问题若根因较深（如后台通道设计），可能需要较大改动；优先尝试同步刷新/确定性等待的最小修复。
- Phase 3 页面测试需保持 provider 装配统一，避免引入新的全局状态污染。
