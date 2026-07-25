# 任务：Q-1 前端 CSS Modules 落地（task-audit-q1-css-modules）

## 目标
为 `frontend/src` 中确有局部样式需求的组件引入 `.module.css`（CSS Modules），贯彻样式隔离，消除全局样式污染风险。终审发现**零个 `.module.css`**。

## 待完成项
- [x] 扫描 `frontend/src` 找出使用 inline `style={{}}` 的组件并列出清单
- [x] 为有局部样式需求的组件创建 `ComponentName.module.css`，将内联样式移至类名
- [x] 颜色/主题令牌统一取自 `src/theme.ts`，不在组件中硬编码颜色
- [x] Mantine 可覆盖的场景优先用 Mantine 组件
- [x] `npm run build` / `npm run typecheck` / `npm run test` 全部通过

## 完成标准
- 原先使用内联样式的组件改用 CSS Modules。
- 前端构建、类型检查、测试均通过。
- 组件行为与 prop API 不变。

## 范围说明
全局结构性 CSS（ReactFlow 基础样式、reset）保留在 `App.css`/`index.css`，不在本次迁移范围。

## 主要修改记录

### 扫描结论
`frontend/src` 中大量 `.tsx` 使用内联 `style={{}}`。按前端规范，仅**原生元素**（`div`/`pre`/`input` 等）携带的、真实的局部视觉规则需要隔离为 CSS Modules；仅向 Mantine 组件（`Text`、`Box`、`Paper`、`Stack`、`Group`、`Table`、`ScrollArea`、`Center`、`ThemeIcon`、`MantineCode` 等）转发 `style` 的场景属可接受范围，本次未改动（遵循 `frontend-code-rules.md`）。`CustomEdge`/`CustomNode` 的内联样式均为状态驱动的动态 SVG 属性（stroke、transform、动态尺寸），保持内联以确保视觉效果不变。

### 已转换组件（12 个）与新建文件
| 组件 | 新建 CSS Module | 迁移内容 |
|------|----------------|----------|
| `components/Layout/MainLayout.tsx` | `MainLayout.module.css` | 整体布局骨架（列向 flex、100vh、sidebar/aside 边框与背景） |
| `components/Canvas/WorkflowCanvas.tsx` | `WorkflowCanvas.module.css` | 外层列向 flex 容器 |
| `components/ExecutionPanel/NodeOutputList.tsx` | `NodeOutputList.module.css` | 列表列向 flex 容器 |
| `components/ExecutionPanel/CodeViewer.tsx` | `CodeViewer.module.css` | 代码滚动容器 `overflow`（动态 `maxHeight` 仍保留内联） |
| `components/ExecutionView/ToolCallChain.tsx` | `ToolCallChain.module.css` | 行/图标列/状态圆点(结构)/连接线/内容列/按钮布局；hover 由内联 JS 改为 `:hover`；状态相关颜色仍为 `var(--mantine-color-*)` 内联 |
| `components/ExecutionPanel/StepItem.tsx` | `StepItem.module.css` | 同上模式（状态圆点为动态色）；chevron 颜色提取为类 |
| `components/ExecutionView/AgentExecutionView.tsx` | `AgentExecutionView.module.css` | `IterationGroup` 与 `SubRecordItem` 的原生 div/按钮布局；迭代图标颜色静态（indigo）已移入类；按钮 hover 改为 `:hover` |
| `components/common/RequireRole.tsx` | `RequireRole.module.css` | 无权限提示块 |
| `pages/HelpPage.tsx` | `HelpPage.module.css` | 页面根滚动容器 |
| `pages/SettingsPage.tsx` | `SettingsPage.module.css` | 页面根滚动容器 |
| `pages/ExecutionHistoryPage.tsx` | `ExecutionHistoryPage.module.css` | 输出 `<pre>` 文本块 |
| `pages/AdminFilesPage.tsx` | `AdminFilesPage.module.css` | 隐藏 file input、拖拽放置区（动态边框/背景按 `dragOver` 切换类）、拖拽遮罩层 |

### 验证结果
- `npm run build` ✅（exit 0）
- `npm run typecheck` ✅（exit 0）
- `npm run test` ✅（450 passed, 51 files）
- 组件行为与 prop API 未变；颜色/令牌均来自 `index.css` 的 `var(--...)` 主题变量或 `var(--mantine-color-*)`，无新增硬编码色值。
