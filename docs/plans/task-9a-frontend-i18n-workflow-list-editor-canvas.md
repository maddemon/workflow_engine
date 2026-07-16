# 任务 9a：前端 — 迁移 WorkflowList + WorkflowEditorPage + Canvas

## 目标

将 WorkflowList、WorkflowEditorPage、Canvas 相关组件中的硬编码英文（及少量中文）文本迁移到 react-i18next 的 `t()` 调用，扩展 `workflow.json` 与 `common.json` 翻译文件，确保中英 key 一致并通过前端类型检查。

## 待完成项

- [x] 扩展 `frontend/public/locales/en/workflow.json` 与 `zh-CN/workflow.json`
- [x] 扩展 `frontend/public/locales/en/common.json` 与 `zh-CN/common.json`
- [x] 迁移 `frontend/src/components/WorkflowList/WorkflowListPage.tsx`
- [x] 迁移 `frontend/src/components/WorkflowList/ProjectFilter.tsx`
- [x] 迁移 `frontend/src/pages/WorkflowEditorPage.tsx`
- [x] 迁移 `frontend/src/components/Canvas/WorkflowCanvas.tsx`
- [x] 迁移 `frontend/src/components/Canvas/CanvasToolbar.tsx`
- [x] 检查 `frontend/src/components/Canvas/CustomNode.tsx` 与 `CustomEdge.tsx` 是否有静态标签
- [x] 运行 `cd frontend && npm run typecheck` 并修复错误

## 完成标准

- 所有目标文件中的用户可见静态文本均使用 `t()` 调用
- `en` 与 `zh-CN` 翻译文件 key 完全一致
- `npm run typecheck` 无错误
- 不翻译用户创建的名称、节点标签、项目名、日志/文件内容
- API errorCode、运行时引擎错误保持英文

## 完成状态

- [x] 扩展翻译文件
- [x] 迁移组件
- [x] 类型检查通过

## 主要修改记录

- 在 `workflow.json` 中新增 WorkflowList、WorkflowEditor、Canvas、CanvasToolbar、ProjectFilter 所需 key，中英版本 key 完全一致。
- 在 `common.json` 中新增 `refresh`、`copied`。
- 将所有硬编码字符串替换为 `useTranslation` 的 `t()` 调用，保留动态数据（工作流名、项目名、端口名、文件名等）不翻译。
- `CustomNode.tsx` 与 `CustomEdge.tsx` 无静态用户可见标签，未作改动。
