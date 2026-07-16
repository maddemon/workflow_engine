# 任务 9b：前端 — 迁移 NodePanel + ParameterPanel + 字段组件

## 目标

将 NodePanel、ParameterPanel 及其字段组件中的硬编码英文（及少量中文）文本迁移到 react-i18next 的 `t()` 调用，扩展 `nodePanel.json`、`parameterPanel.json` 与 `common.json` 翻译文件，确保中英 key 一致并通过前端类型检查。

## 待完成项

- [ ] 重构并扩展 `frontend/public/locales/en/nodePanel.json` 与 `zh-CN/nodePanel.json`
- [ ] 重构并扩展 `frontend/public/locales/en/parameterPanel.json` 与 `zh-CN/parameterPanel.json`
- [ ] 迁移 `frontend/src/components/NodePanel/NodePanel.tsx`
- [ ] 迁移 `frontend/src/components/NodePanel/NodeCard.tsx`
- [ ] 迁移 `frontend/src/components/ParameterPanel/ParameterPanel.tsx`
- [ ] 迁移 `frontend/src/components/ParameterPanel/FieldResolver.tsx`
- [ ] 迁移 `frontend/src/components/ParameterPanel/DiffPanel.tsx`
- [ ] 迁移 `frontend/src/components/ParameterPanel/TriggerConfig.tsx`
- [ ] 迁移 `frontend/src/components/ParameterPanel/ValidationChecklistModal.tsx`
- [ ] 迁移 `frontend/src/components/ParameterPanel/fields/*.tsx`（19 个字段组件）
- [ ] 运行 `cd frontend && npm run typecheck` 并修复错误

## 完成标准

- 所有目标文件中的用户可见静态文本均使用 `t()` 调用
- `en` 与 `zh-CN` 翻译文件 key 完全一致
- `nodePanel.json` 与 `parameterPanel.json` 使用嵌套 key（参照 `workflow.json`）
- `npm run typecheck` 无错误
- 不翻译用户创建的名称、节点标签、项目名、日志/文件内容
- API errorCode、运行时引擎错误保持英文

## 完成状态

- [ ] 扩展翻译文件
- [ ] 迁移组件
- [ ] 类型检查通过
