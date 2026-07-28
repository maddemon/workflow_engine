# 任务：修复 main 分支前端预置 tsc 类型错误

## 目标
修复 main 分支 `npm run typecheck`（`tsc -b`）报出的 15 个预置类型错误，全部集中在 5 个测试文件中，不涉及任何运行时逻辑改动。

## 待完成项
- [x] 修正 `CustomNode.test.tsx` / `WorkflowCanvas.test.tsx` 中错误的相对导入路径 `../../types/canvas.ts` → `../../../types/canvas.ts`
- [x] `CustomNode.test.tsx`：直接渲染 `<CustomNode>` 需补齐 `NodeProps` 必填项（`type`/`deletable`/`selectable`/`draggable`/`dragging`/`zIndex`/`isConnectable`/`positionAbsoluteX`/`positionAbsoluteY`）
- [x] `CustomNode.test.tsx`：`switchDescriptor` 的 `cases` 参数与 `itemDefinition` 补齐 `ParameterDefinition` 必填字段（`defaultValue`/`displayRule`/`credentialType`/`options` 等）
- [x] `FileField.test.tsx`：移除未使用的 `render` 导入（TS6133）
- [x] `useWebSocketConnection.test.ts`：为 4 个 `vi.fn()` mock 变量补充分的 `Mock<...>` 泛型签名，使其满足 hook 配置类型
- [x] `workflowSerializer.test.ts`：将非法的 `type: 'Object'`（前后端均不支持）改为合法 `'Array'`，与该用例 Switch 子项既有 fixture 模式一致

## 完成标准
- `npm run typecheck` 零错误
- `npx vitest run` 全量前端 486 测试通过（无回归）

## 完成状态
- [x] 类型检查通过
- [x] 前端全量测试 486/486 通过

## 主要修改记录
- 5 个测试文件均为类型层面的 fixture 修正，未改动任何被测源码；运行时行为保持不变。
- `'Object'` 非法类型：已核对 `backend/.../Enums/ParameterType.cs`，枚举中确实无 `Object`，前端 `parameterEnums.ts` 单一来源亦无此值，故改为 `'Array'`（与 `computeDynamicPorts.test.ts:73` 中 Switch 子项写法一致）。
