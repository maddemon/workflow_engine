# Task 4 报告：CLI workflow validate 离线结构校验 / guide 缺口提示

## 完成状态

DONE

## 变更摘要

1. **内置节点 schema 包**
   - 新增 `cli/src/commands/builtInNodeTypes.ts`，导出 `BUILT_IN_NODE_TYPES` 常量与描述符接口。
   - 覆盖 Task 1-3 新增/涉及节点：`httpRequest`、`paginate`、`oauth2`、`dbUpsert`、`manualTrigger`、`scheduleTrigger`、`webhook`、`if`、`switch`、`filter`、`loop`、`set`、`script`、`merge`、`aggregate`、`wait` 等。
   - 每个节点包含 `typeName`、`category`、`ports`（含 `name`/`direction`/`type`）与 `parameters`（含 `name`/`required`）。

2. **`workflow validate <file>` 命令**
   - 在 `cli/src/commands/workflows.ts` 新增 `WorkflowValidateOptions` 接口与 `workflowValidate` 函数。
   - 校验内容：
     - 基础结构：`name` 非空、`nodes` 非空数组、`connections` 为数组。
     - 节点类型：优先调用后端 `/node-types`，后端不可用时回退到 `BUILT_IN_NODE_TYPES`。
     - 端口方向：`connection` 的源端口须为 Output，目标端口须为 Input；节点必须存在。
     - 必填参数：节点类型 `required = true` 的参数必须在 `node.parameters` 中存在且非空。
     - 入口节点：至少有一个 `isEntry === true`。
   - 输出：JSON 模式返回 `{ valid, errors, warnings }`；文本模式打印错误/警告，通过时输出 `工作流校验通过`。
   - 退出码：`valid` 为 `ExitCode.Success`（0），否则 `ExitCode.InvocationError`（1）。

3. **`workflow create --dry-run` 增加结构校验**
   - 在 `workflowCreate` 的 `--dry-run` 分支中，先执行与 `workflowValidate` 相同的校验逻辑。
   - 校验失败时以非零退出码抛出 `CLIError`，不再仅打印请求体。

4. **`guide` 缺口提示**
   - 修改 `cli/src/commands/guide.ts`：
     - 后端不可用时提示：`未连接后端，节点类型清单不可用。以下为基础模板与已知内置能力。`
     - `## 支持的节点类型` 后列出内置节点（按 category 分组）。
     - 新增 `## 已知能力缺口` 章节，列出 plan-004 中尚未实现的能力：
       - 平台专用 SDK 节点（钉钉 / 企业微信 / 飞书）未提供，需用通用 OAuth2 + HTTP 节点自行组装。
       - 部分高级数据库功能（存储过程、复杂迁移）需自行扩展。
       - authorization_code 等交互式授权、外部凭据保险库（Vault/KMS）对接尚未实现。
     - 保持现有示例工作流与常见错误说明不变。

5. **注册命令**
   - 在 `cli/src/index.ts` 注册 `workflow validate <file>` 子命令，支持 `--profile` 全局选项与 `--json` 输出模式。

6. **测试**
   - 更新 `cli/src/__tests__/workflows-commands.test.ts`：
     - 新增 `workflow validate` 测试：有效工作流通过、未知节点类型、端口方向错误、缺少必填参数、无入口节点。
     - 更新 `--dry-run` 测试：非法节点类型时现在报错而非仅打印。
   - 更新 `cli/src/__tests__/guide-command.test.ts`：
     - 后端不可用场景下验证输出包含 `未连接后端` 和 `已知能力缺口`。

## 验证结果

- `npm run build`（`cli` 目录）：通过，无 TypeScript 错误。
- `npm test`（`cli` 目录）：15 个测试文件、143 个测试全部通过。
- 关键命令路径已通过测试覆盖：
  - `flowengine workflow validate valid-workflow.json` → 输出 `工作流校验通过`。
  - `flowengine workflow validate invalid-node.json` → 输出未知节点类型错误并退出码 1。
  - `flowengine workflow create --dry-run --file invalid-node.json` → 非零退出码报错。
  - `flowengine guide` 在后端不可用时 → 输出离线提示与能力缺口。

## 提交记录

- `2f8b71c` feat(cli): workflow validate 离线结构校验与 guide 缺口提示

## 注意事项

- `builtInNodeTypes.ts` 中的端口/参数列表为离线兜底数据，不要求与后端实时完全一致；后续可扩展为从后端拉取后缓存到本地 schema 文件。
- `workflowValidate` 与 `workflowCreate --dry-run` 共用同一 `validateWorkflow` 实现，确保校验行为一致。
- `guide` 命令在后端失败时降级为内置节点列表，错误信息会通过 `error()` 输出到 stderr，不影响正文输出。
