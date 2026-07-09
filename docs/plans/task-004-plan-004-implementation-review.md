# 任务：plan-004 实施复核——新发现问题记录

> 关联：[plan-004-integration-foundations.md](plan-004-integration-foundations.md)、[task-003-dingtalk-sync-via-cli.md](task-003-dingtalk-sync-via-cli.md)

## 目标

逐条核查 task-003 S1–S9 在 plan-004 中的落地质量，记录落地过程中引入的新问题或遗留问题，供后续跟进。

## 核查结论总览

| 短板               | 状态        | 新发现问题                                                                                                       |
| ------------------ | ----------- | ---------------------------------------------------------------------------------------------------------------- |
| S1 DbUpsertNode    | ✅ 已落地   | **A1**：Columns 表达式无 `$credentials` 作用域                                                                   |
| S2 OAuth2 令牌管理 | ✅ 已落地   | **A5**：令牌缓存仅内存，跨进程/多实例无法复用                                                                    |
| S3 凭据多字段引用  | ✅ 已落地   | 见 A1（同源问题）                                                                                                |
| S4 游标分页        | ✅ 已落地   | **A2**：Page 端口空悬                                                                                            |
| S5 SetNode 表达式  | ⏭️ 计划延迟 | —                                                                                                                |
| S6 CLI 离线校验    | ✅ 已落地   | —                                                                                                                |
| S7 CLI 缺口暴露    | ✅ 已落地   | **A3**：knownGaps 未反映已补齐能力                                                                               |
| S8 凭据类型校验    | ✅ 已落地   | —                                                                                                                |
| S9 表达式变量模型  | 🟡 部分落地 | **A4**：统一表达式引擎（AST 三分类/函数式双写法）未实现；IfNode/FilterNode 自写解析器未删除；plan-004 与代码不符 |

---

## 发现 A1 — DbUpsertNode Columns 表达式缺少 `$credentials` 作用域

- **文件：** `plugins/FlowEngine.Plugins.Standard/DbUpsertNode.cs:334-354`
- **归属：** 插件
- **现象：** `EvaluateRowValues()` 为逐行列映射表达式创建独立的 `JsEngine`，仅注入 `$json`、`$input`、`$itemIndex`、`$runIndex`。若用户在 `Columns` 的表达式值中引用 `$credentials.<name>.<field>`，会因变量未定义而运行时失败。
- **严重度：** Low（当前场景无影响——列映射只需 `$json.userid`）
- **建议方向：** 将工厂上下文的全局变量（`$credentials`、`$env`、`$workflow` 等）传播到 `EvaluateRowValues` 的 JsEngine 中，方式可以是 `NodeExecutionContext` 携带已注入的 JsEngine 引用，或显式传变量字典。
- **注：** `Connection` 参数已由 ParameterResolver 在工厂中预求值，不受此限。

## 发现 A2 — PaginateNode Page 端口空悬 + 测试覆盖不足

- **文件：** `plugins/FlowEngine.Plugins.Standard/PaginateNode.cs:62-67`
- **归属：** 插件
- **现象 1（端口空悬）：** `Ports` 声明了 `"Page"` 输出端口（第 66 行），但 `ExecuteAsync` 仅输出 `"Output"`（跨页打平的单一 `DataBatch`）。`"Page"` 端口从未被写入。
- **现象 2（测试偏薄）：** `tests/.../Plugins/PaginateNodeTests.cs` 仅 1 条测试方法 `ExecuteAsync_IteratesWithCursor_AndAggregatesItems`（3 页 × 2 条 = 6 条 + 游标推进），缺少终止条件专项测试、HTTP 错误场景、认证头、`bodyExpression`（POST 请求体）等场景覆盖。对于游标分页这种关键集成节点，1 条测试不足以建立信心。
- **严重度：** Low（端口空悬） / Medium（测试偏薄——未来修改分页逻辑容易引入回归）
- **建议方向：**
  - 端口：如果后续需要逐页处理，在每轮迭代末尾向 `"Page"` 端口发一次数据；若不需要则删除该端口定义避免迷惑。
  - 测试：补充至少 3 条——`terminateWhen` 提前终止、HTTP 请求失败错误传递、`bodyExpression`（POST + `$cursor` 引用）。
- **文档连带（见 A4）：** `plan-004` §3 阶段四声称 `Page` 端口「每页一次」可用，与代码矛盾，需在 A4 一并订正 `plan-004`。

## 发现 A3 — Guide knownGaps 未反映已补齐能力

- **文件：** `cli/src/commands/guide.ts:122-126`
- **归属：** CLI + 文档
- **现象：** `knownGaps` 数组内容仍为 plan-004 前的原始缺口描述：
  ```
  - 平台专用 SDK 节点（钉钉 / 企业微信 / 飞书）未提供…
  - 部分高级数据库功能（存储过程、复杂迁移）需自行扩展。
  - authorization_code 等交互式授权、外部凭据保险库（Vault/KMS）对接尚未实现。
  ```
  但 `dbUpsert`、`paginate`、`oauth2` 等通用能力已落地，用户从 `guide` 中无法感知已有进展。
- **严重度：** Low（虽然 `BUILT_IN_NODE_TYPES` 的节点清单会包含这些新节点，但用户不一定会注意到清单变化）
- **建议方向：** 在 `knownGaps` 前添加一段说明，列举最近补齐的能力，例如：
  ```
  ## 近期已补齐能力
  - DbUpsertNode（通用数据库 upsert）
  - PaginateNode（游标分页拉取）
  - OAuth2 凭据类型 + 令牌自动托管
  ```
  也可在每条 gap 后标注「当前进展」。

## 发现 A4 — 计划/架构文档与代码严重不符（核查基准失真）

- **文件：** `docs/plans/plan-004-integration-foundations.md`（§1.5 / §1.6 / §3 阶段一）、`docs/architecture/expression-system.md`、`credentials.md`、`node-system.md`
- **归属：** 文档（计划 + 架构）
- **现象：** 本次核查以 `plan-004` 为验收基准，但 `plan-004` 自身就与代码不符——这是比"三篇架构文档落后"更根本的问题：
  1. `plan-004` §1.5 / §3 阶段一反复声称已实现「统一表达式模型：函数式 `ctx =>` + 省略式双写法，**Acornima AST 分类**，无 `{{ }}` 包裹前缀」，§1.6 更把「阶段一·核心任务3 统一表达式变量模型」标记为 `[x]` 已落地。但代码现实是：`ParameterResolver.IsExpression` 为**字符启发式**（纯字面 / `s_knownIdentifiers` 命中 / 含运算字符），`JsEngine` 统一以 `return (expr)` 包裹求值，**没有** Acornima AST 顶层类型分类，也**不支持**函数式 `ctx =>` 调用。所谓"统一表达式引擎（AST 三分类 + 双写法）"并未实现——仅"变量注入清单 + 字符启发式识别"落地。这直接导致本总览把 **S9 错判为「已落地」**（实为部分落地）。
  2. **IfNode / FilterNode 自写解析器未删除**（plan-004 §3 阶段一"统一落点"明确承诺删除，但未执行）：
     - **IfNode**（`plugins/.../IfNode.cs:81-134`）：`ToBoolean()` → `Compare()` 仍为手写字符串切分比较器，不支持 `$json`/`$input`/`$credentials` 等 `$` 变量。用户写 `$json.status == 'active'` 会被当作原始字符串 `"status"` 与 `"'active'"` 比较，结果必然为 `false`。`Condition` 参数也未标注 `[Hint(Expression)]`，不经 `ParameterResolver` 解析。
     - **FilterNode**（`plugins/.../FilterNode.cs:149-177`）：`EvaluateExpression()` 仍解析 `{{ $json.field }}` mustache 语法（`StartsWith("{{")` + `GetJsonValue` 路径提取），不支持新表达式模型（`$json.field` 裸式）。`Condition` / `Conditions` 参数也未标注 `[Hint(Expression)]`。
     - 这是 A4 最直接的用户可见后果：两个最常用的逻辑节点（条件/过滤）**无法使用新 `$` 变量**，统一表达式模型的"统一"名存实亡。
  3. 关联 **A2**：`plan-004` §3 阶段四写道 `PaginateNode` 可选 `Page` 端口「每页一次，供逐页处理」，但代码（见 A2）`Page` 端口从未被写入。计划文档声称的能力与实现矛盾，应同步订正。
  4. 原核查清单写「`OAuth2CredentialAccessor` 接口」——代码**无此接口**，实际为 `ICredentialAccessor.GetCredentialAsync` / `GetCredentialByNameAsync`（异步）。原清单系照 plan-004 文字抄录，未真读代码。
     > 注：`expression-system.md` 已在本轮文档同步中改回如实描述（明确"无 Acornima AST 三分类、不支持 `{{ }}`"），但 `plan-004` 这篇**计划/设计文档未同步**，至今声称已落地的虚构引擎。
- **严重度：** High（以失真的计划文档为基准，会使 S9 等落地判定整体失真；文档与代码一致的约束见 `project-rules.md` §3 & `docs-rules.md` §9）
- **建议方向（先对齐计划文档，再核对架构文档，且一律以代码为基准）：**
  1. **`plan-004` 先订正**：§1.5 / §3 中"Acornima AST 分类、函数式 `ctx =>` 双写法"改为「规划中未实现」（或删除"已实现"断言）；§1.6「核心任务3」改为「部分落地：变量注入已落地，AST 三分类/双写法未实现」；§3 阶段四 `Page` 端口描述按 A2 结论改为「端口已声明但未发数据，待引擎支持多端口输出」。
  2. **架构文档交叉核对**（以代码为准，非 plan-004）：
     - `expression-system.md`：确认 `$` 前缀变量清单、`$input` 容器方法、`return (expr)` 包裹、无 AST 三分类、安全修正说明一致（本轮已部分修订）。
     - `credentials.md`：oauth2 凭据类型字段 schema、实际 `ICredentialAccessor` 异步接口（`GetCredentialAsync` / `GetCredentialByNameAsync`），**非** `OAuth2CredentialAccessor`。
     - `node-system.md`：DbUpsertNode / PaginateNode / OAuth2Node 参数与端口签名；`Page` 端口按 A2 如实标注未发数据。

## 发现 A5 — OAuth2TokenService 令牌缓存仅内存

- **文件：** `backend/FlowEngine.Runtime/Credentials/OAuth2TokenService.cs:16-34`
- **归属：** 运行时
- **现象：** `OAuth2TokenService` 使用 `ConcurrentDictionary<string, CacheEntry>` 做令牌缓存，进程重启后缓存丢失、需重新请求令牌。多实例部署时各实例独立缓存，无法共享，可能导致令牌端点被重复请求。
- **严重度：** Low（plan-004 §5 已标注为风险："先内存+可选持久化，多实例用共享缓存"；功能无缺陷，仅效率与部署形态受限）
- **建议方向：** 后续可选持久化（文件 / Redis / DB），当前阶段按 plan-004 §5 风险记录跟踪即可。

## 完成状态

| 事项                                                     | 状态                                                 |
| -------------------------------------------------------- | ---------------------------------------------------- |
| A1 DbUpsertNode Columns 缺少 `$credentials`              | [x] 已修复——EvaluateRowValues 注入 GlobalVariables |
| A2 PaginateNode Page 端口空悬 + 测试覆盖不足             | [x] 已处理——删除空悬 Page 端口；补充 2 条测试（terminateWhen 提前终止、HTTP 500 错误） |
| A3 Guide knownGaps 未更新                                | [x] 已修复——新增 recentProgress 段（DbUpsert/Paginate/OAuth2/IfNode-FilterNode 统一表达式） |
| A4 计划/架构文档与代码对齐 + IfNode/FilterNode 解析器统一 | [x] 已执行——plan-004 文档订正；IfNode 删自写解析器改走 ParameterResolver；FilterNode 删 mustache 解析器改走逐项 JsEngine + GlobalVariables；NodeExecutionContext 新增 GlobalVariables；**新增 IfNodeTests / FilterNodeTests 单元测试覆盖 `$json` 表达式路径** |
| A5 OAuth2TokenService 缓存仅内存                         | [ ] 跟踪（plan-004 §5 已标注风险，后续按需补持久化） |

## 主要修改记录

| 日期       | 修改人 | 修改内容                                                                                                                                                                                         |
| ---------- | ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 2026-07-09 | Agent  | 基于 plan-004 代码复核创建本任务文档，记录 A1–A4                                                                                                                                                 |
| 2026-07-09 | Agent  | 复核修正：总览 S9 改「部分落地」；A4 重写为「计划/架构文档与代码严重不符」，关联 A2（Page 端口）、修正接口名（`OAuth2CredentialAccessor`→`ICredentialAccessor`），严重度升 High                  |
| 2026-07-10 | Agent  | 补充 A4 第4项：IfNode `ToBoolean`/`Compare` 与 FilterNode `{{ $json }}` mustache 解析器未删除的具体代码证据；A2 补充测试覆盖不足（仅1条测试）；新增 A5（OAuth2 缓存仅内存）；总览 S2/S9 同步更新 |
| 2026-07-10 | Agent  | A1 修复：DbUpsertNode EvaluateRowValues 注入 GlobalVariables；A2 处理：删除空悬 Page 端口 + 补充 2 条测试；A3 修复：guide.ts 新增 recentProgress；A4 执行：IfNode 删自写解析器改走 ParameterResolver，FilterNode 删 mustache 改走逐项 JsEngine，NodeExecutionContext 新增 GlobalVariables，plan-004 文档订正（AST 三分类/双写法标为未实现，统一落点标为已完成） |
| 2026-07-09 | Agent  | 补充单元测试：新增 IfNodeTests（Condition 真/假路由 True/False 分支、缺失 condition 报错）、FilterNodeTests（逐项 `$json` 表达式过滤、空条件保留全部）；review 文档 A4 完成状态补注新增单测 |
