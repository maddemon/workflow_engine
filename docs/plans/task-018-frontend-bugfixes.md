# 任务：前端缺陷修复（数据丢失 / 功能 bug / 性能 / 设计）

## 目标
修复前端代码评审发现的一组缺陷，按严重程度分步处理：
- 消除保存工作流时的**数据丢失**（动态端口连线被丢弃）。
- 修复**功能 bug**（误报新版本、主动关闭后 WebSocket 重连泄漏、dismiss 不推进基线）。
- 缓解**性能问题**（CustomNode memo 失效、版本轮询拉全量、列表无虚拟化）。
- 清理**设计/规范问题**（store 循环依赖、组件内直接调 API、原生 div、硬编码颜色、重复常量）。

## 待完成项

### 阶段一：严重数据丢失（必须最先修，且须有回归测试）
- [ ] **F1** `src/utils/workflowSerializer.ts`：`serializeWorkflow` 的节点 `ports` 字段（`workflowSerializer.ts:24`）与 `nodePortMap` 构建（`workflowSerializer.ts:39`）改用 `computeDynamicPorts(data)`，使 Switch 等动态端口节点在保存时保留分支输出端口与连线。**须补 `import { computeDynamicPorts } from './computeDynamicPorts.ts';`**（评审指出计划遗漏此 import）。
- [ ] **F1-测试**：在 `src/utils/__tests__/workflowSerializer.test.ts` 增加用例——含 2 个 case 输出的 Switch 节点，断言序列化后 `nodeDefinitions[].ports` 含 `case_0/case_1` 且 `connections` 保留对应分支连线（TDD：先写失败用例再改实现）。另增 **duplicate-name 用例**：两个 case 同名时 `computeDynamicPorts` 会改名 `name_i`，断言序列化结果不出现重复端口名（旧连线 handle 不跟随改名属预存问题，仅记录不修）。
- [x] **F1-后端契约（已验证，解除阻塞）**：后端 `WorkflowValidator.cs:72-89` 与 `WorkflowValidationService.cs:131-185` 均按 `GetNodeDescriptor(node.TypeName)`（**注册静态描述符**）校验连接；当端口名未命中时 `sourcePort/targetPort` 为 `null`，**不会报错**（仅当端口命中但方向/类型不符才报错）。连接按名称原样存储。因此前端发送完整动态端口（`case_0` 等）**不会触发校验失败、不会回归**，且更正确。✅ 已解除阻塞，可直接实施 F1。

### 阶段二：功能 bug
- [ ] **F2** `src/stores/workflowStore.ts:141,143`：保存后更新 `workflowVersion`（`updateWorkflow` 取返回值 `version`；`createWorkflow` 取 `created.version`），避免刚保存就误报「有更新版本」。
- [ ] **F3** `src/hooks/websocket/useWebSocketConnection.ts:97-111`：新增 `manualCloseRef` 标志（用 `useRef(false)`）。`closeConnection` 中置 `manualCloseRef.current = true`；**提前 return 须放在 `wsRef.current = null` 之后**（评审细化：否则已关闭的 socket 残留在 ref 中），且 intentional close 仍要 `setStatus('disconnected')`；`doConnect` 开头重置 `manualCloseRef.current = false`。意外掉线（绕过 `closeConnection` 直接 `onclose`）仍正常重连。
- [ ] **F4** `src/hooks/useWorkflowVersionPolling.ts:57`：`dismiss` 内推进 `latestVersionRef.current = newVersion ?? latestVersionRef.current`。

### 阶段三：性能
- [ ] **F5** `src/components/Canvas/CustomNode.tsx:149-171`：将 `inputPorts`/`outputPorts` 的 `.filter()` 移入 `useMemo` 或单独 `useMemo` 包端口列表，使 `computePortLayouts` 仅在端口真正变化时重算。
- [ ] **F6** `src/hooks/useWorkflowVersionPolling.ts:39`：若后端提供轻量版本接口则用其替代全量 `getWorkflow`；否则保持并备注（需后端配合，本期可暂不实现，仅记录待定）。
- [ ] **F7** `src/components/WorkflowList/WorkflowListPage.tsx:364`：对 `filteredWorkflows` 行做 `useMemo` 预格式化（`formatDateTime` 结果缓存）；虚拟化/分页作为后续优化（标注待定，不在本期阻塞）。

### 阶段四：设计 / 规范（低风险，分批）
- [ ] **F8** 循环依赖：`workflowStore.ts:7` ↔ `canvasStore.ts:14`。方案：将 Canvas 节点/边类型抽到 `src/types/`，两 store 改从 `types/` 导入（不在 `workflowStore` re-export Canvas 类型）。
- [ ] **F9** 组件内直接 API 调用（**仅改 mutation，GET 已正确用 `useRequest` 不动**）：`WorkflowEditorPage.tsx:129/140`（`confirmWorkflow`/`rejectDraft`）、`CredentialMenu.tsx`/`CredentialListModal.tsx` 的凭据增删改、`FileField.tsx:37`（`uploadFile`）改为 `useRequest(fn, { manual: true })`，统一 loading/error（`RoleAssignModal.tsx` 为参照样板）。GET 类（`WorkflowListPage`/`CredentialMenu` 等列表加载）保持现状。
- [ ] **F10** 原生 `<div>` 改 Mantine：`ParameterPanel.tsx:180`、`CredentialField.tsx:102,178`、`FileField.tsx:70,78` → `Box/Stack/Group`；`ExecutionPanel.tsx:103-106,164-167` 的 `--exec-err-*` 颜色移入 `theme.ts`。
- [ ] **F11** 重复常量（**注意两处并不相同——是潜在不一致 bug**）：`CredentialMenu.tsx:11` = `[apiKey, oauth2, basicAuth, connectionString]`，而 `CredentialField.tsx:48` = `[apiKey, oauth2, basicAuth, database]`（`connectionString` vs `database` 不一致）。抽取为共享常量 `defaultCredentialTypeOptions`（建议 `types/workflow.ts` 或 credentials util），并**先对齐这个差异**（以哪边为准须确认，优先复用后端 `getCredentialTypes` 结果作为主源）。

## 完成标准
- [ ] `dotnet` 无关；前端：`npm run build` 与 `npm run typecheck` 通过。
- [ ] `npm test`（Vitest）全部通过，新增 F1 回归用例覆盖动态端口保存。
- [ ] 阶段一/二（F1–F4）必须完成；阶段三 F5 完成；F6/F7 至少完成可落地的部分（F6 标注待后端配合）。
- [ ] 发起 SubAgent Code Review，以本任务文档为依据，按意见修正。
- [ ] 更新本任务文档「完成状态」。

## 完成状态
- [x] 阶段一 F1（commit 2d4bbfd）
- [x] 阶段二 F2（557a912）/ F3（24ac28a）/ F4（d87ba36）
- [x] 阶段三 F5（6c2a7a7）；F6/F7 按计划暂缓（待后端轻量版本接口 / 后续优化）
- [x] 阶段四 F8（9818cd6）/ F9（9736b14）/ F10（6095d37）/ F11（d5eea13）

## 待定项处理记录
- F6（版本轮询拉全量→轻量接口）：后端未提供轻量版本接口，本期暂缓，列入后续优化。
- F7（工作流列表虚拟化/预格式化）：仅做可落地部分（F5 已含 memo 优化），列表虚拟化列为后续优化。
- F8 运行时 `useWorkflowStore` 导入保留（消除需改运行时行为，超出类型解耦范围）。
- F9 confirm 按钮 loading 在范围外文件（ValidationChecklistModal），per-row delete loading 有意不接。

## 主要修改记录
（实施中填写：每步改了哪些文件/函数，对应 Fx）

## 待定项 / 风险
- **F1（阻塞）**：轻量版本接口需后端新增，本期若后端未就绪则暂缓并记录。
- **F1-后端契约（阻塞）**：须确认后端按完整动态端口存储/校验 `connections`（见 F1 项内说明），否则 F1 需前后端协同。
- **F6**：轻量版本接口需后端新增，本期若后端未就绪则暂缓并记录。
- **F8**：移动 store 类型为跨模块改动，需同步检查所有 `useWorkflowStore`/`useCanvasStore` 的导入路径；用 `useShallow` 处不受影响。
- **F9**：范围较大，按文件逐个改造，保持与 `RoleAssignModal` 一致风格，避免一次大改引入回归；只改 mutation。
- **F11**：`connectionString` vs `database` 不一致须先与用户/后端确认以哪边为准，再抽取共享常量。
- **评审记录**：本计划经 SubAgent 评审（task-018 review），结论全部 11 项覆盖、F1/F2/F3/F4/F5/F6/F7/F8/F9/F10 通过，F11 需修正不一致；上述修正已并入本文件。

## 实施顺序（标准 TDD 流程）
1. 阶段一 F1（含失败用例 → 实现 → 测试通过）。
2. 阶段二 F2 → F3 → F4。
3. 阶段三 F5（F6/F7 视后端就绪情况）。
4. 阶段四 F8 → F9 → F10 → F11（F11 先做差异确认）。
5. 每阶段：实现后 `npm run build` + `npm run typecheck` + `npm test` 通过。
6. 全部完成后发起 SubAgent Code Review（以本计划 + 任务文档为依据），按意见修正。
7. 更新本文件「完成状态」，不主动提交（除非用户要求）。
