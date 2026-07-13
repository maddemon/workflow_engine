# 前端改造设计：人类审查板（AI-native 工作流引擎的前端形态）

## 1. 背景与目标

### 1.1 来源

本设计是 `docs/designs/2026-07-12-ai-native-workflow-engine.md` 的前端落地篇。该设计确定：

- **AI 是主要操作者**：通过 Catalog API 发现节点、填空式提交 DSL 草稿、通过 `assemble`/`modify` 生成或修改工作流。
- **人类是监督者**：AI 生成后展示可视化预览，人类确认/测试后部署。
- **不建聊天 UI**：意图入口在外部 AI 客户端（Claude Desktop / ChatGPT）通过 MCP 调用。

后端已基本实现该闭环（`AiWorkflowsController` 的 `assemble`/`modify`/`validate`/`confirm`、`NodeCatalogController`、`WorkflowService.CreateDraftAsync`/`ConfirmDraftAsync`）。但**前端目前仍是纯人类搭建器**（拖节点、连线、填参数、保存），没有任何 AI 草稿/审查/确认 UI。

### 1.2 目标

将前端从「人类搭建器」改造为「人类审查板」：

1. 人类**不再搭建**工作流，工作流的创建/修改/测试草稿由外部 AI 推送。
2. 前端提供**可视化审查**（只读画布 + 参数可微调）、**测试**（试运行/真实运行）、**确认/拒绝**。
3. 人类工作收敛为：确认、检查、测试、必要时小幅微调或拒绝返工。

### 1.3 不覆盖范围

- 外部 AI 客户端的实现（Claude Desktop / ChatGPT / Agent IDE 的 MCP 接入）——不在本仓库。
- 后端 AI-native 节点定义、Catalog API、组装/修改/校验服务——已在 AI-native 设计文档与后端代码实现。
- 本设计只描述**前端形态**与**为支撑该形态所需的最小后端补强**。

---

## 2. 设计原则

| 原则 | 说明 |
| ---- | ---- |
| 纯审查板 | 意图在外部 AI 输入；前端只接收草稿、审查、确认、拒绝。 |
| 画布只读、参数可微调 | 防止人类与 AI 的后续 `modify` 产生编辑冲突；小修可就地完成。 |
| 测试先于确认 | 确认前必须可试运行；真实运行显式二次确认。 |
| 拒绝闭环 | 人类拒绝理由可回传给外部 AI，使其自纠，无需重建聊天。 |
| 复用而非新建 | 复用现有画布/执行面板/列表；不新建收件箱、不新建聊天。 |

---

## 3. 总体关系

```
外部 AI（Claude Desktop / ChatGPT，经 MCP）
   │  人类自然语言需求
   │  list_node_catalog / get_node_detail / assemble / modify / validate
   ▼
后端（已存在）
   │  创建 IsActive=false 草稿 / modify 生成新草稿
   ▼
前端「审查板」（本设计改造对象）
   ├─ 工作流列表：草稿（IsActive=false, Source=AI）混入展示，人类点开
   ├─ 审查页：只读画布 + 参数微调 + 结构化 diff + 变更高亮
   ├─ 测试：试运行（dry-run）/ 真实运行（二次确认）
   ├─ 确认前校验清单（validate + 凭据存在性）
   └─ 确认 / 拒绝（拒绝理由 → 后端持久化 → MCP get_draft_feedback 回传 AI）
```

前端**不调用** `assemble`/`modify`/`node-catalog`（意图入口在外部）。前端**需要调用** `validate`、`confirm`、`/{id}/modify` 的拒绝/修正结果回写、以及凭据存在性检查，因此需要补齐对应客户端函数（见 §6）。

---

## 4. 人类审查流程

```
外部 AI 推送草稿（出现在工作流列表，IsActive=false）
    ↓
人类点开草稿
    ↓
审查页渲染：只读画布 + 节点配置（参数可微调）
    ↓ （若是 modify 草稿）展示结构化 diff + 画布变更高亮
    ↓
人类测试：试运行（默认）→ 查看执行轨迹/报错
    ↓ （可选）真实运行（二次确认，明示副作用）
    ↓
确认前校验清单：validate + 凭据存在性 → 绿/红
    ↓
人类决策：
   ├─ 确认并激活 → POST /{id}/confirm → IsActive=true，版本化保存
   ├─ 小幅微调后确认（直接改参数面板，confirm 当前草稿状态）
   └─ 拒绝 + 理由 → 存为草稿审计备注 → 外部 AI 经 MCP get_draft_feedback 拉取自纠
```

---

## 5. 八大已对齐决策

> 以下决策在与用户的 grill 访谈中逐一确认，作为本设计的权威依据。

### 5.1 意图入口：纯审查板

- **决策**：人类意图在外部 AI 客户端输入，草稿推送到前端。前端不提供自然语言输入框。
- **理由**：忠实于 AI-native 设计「不建聊天 UI」。前端专注审查，职责单一。
- **影响**：前端无需 LLM 调用、无需聊天组件；只需消费后端已有的草稿。

### 5.2 草稿到达：复用现有列表

- **决策**：AI 草稿（`IsActive=false` 且来源标记为 AI）直接出现在现有工作流列表中，不新建收件箱/通知。
- **理由**：改动最小，复用后端现状；人类在熟悉列表中区分草稿（如「待审」标签）。
- **影响**：
  - 后端需为草稿增加**来源/状态标识**（见 §7.1），前端据此在列表中标注「AI 草稿·待审」。
  - 前端列表需展示草稿状态徽标，便于人类识别待审项（不强制新增独立收件箱）。

### 5.3 审查交互：画布只读 + 参数可微调

- **决策**：审查页画布不可拖节点/连线/改布局；选中节点的参数面板可就地微调（改 URL、填凭据名等小修）。大改走「拒绝 + 理由」回 AI。
- **理由**：符合「AI 建、人类审」理念，又避免小修绕回外部 AI 的低效；同时防止人类画布编辑与 AI 后续 `modify` 冲突。
- **影响**：
  - Canvas 复用现有 `nodesDraggable`/`nodesConnectable` 等开关，新增 `reviewMode` 标志统一控制只读。
  - ParameterPanel 在 `reviewMode` 下保持可编辑（仅参数），但禁用节点增删/连线/布局操作。

### 5.4 测试语义：试运行为主 + 真实运行需确认

- **决策**：草稿提供「试运行」按钮，默认走 `dry-run`（不持久化、尽量不触达外部副作用）；另提供「真实运行」需二次确认弹窗（明示副作用）。
- **理由**：AI 草稿可能触达外部 API / 写库，盲跑有风险；dry-run 安全预览，真实运行交人类显式授权。
- **影响**：复用现有 `api.dryRun` 与 `api.executeWorkflow` + `ExecutionPanel` 执行轨迹；新增确认弹窗与副作用提示文案。

### 5.5 拒绝回环：存理由 + MCP 可拉取

- **决策**：人类拒绝时填写理由，存为草稿审计备注；新增 MCP 工具 `get_draft_feedback` 让外部 AI 在下一轮次拉取理由自纠。
- **理由**：闭环「人类拒绝 → AI 修正」无需前端聊天；结构化理由可被 AI 程序化消费。
- **影响**：见 §7.2（后端草稿状态机 + `RejectionReason`）、§7.3（MCP 工具）。

### 5.6 Diff 呈现：结构化 diff 面板 + 画布高亮

- **决策**：`modify` 草稿的审查页，侧栏渲染后端 `StructuredDiff` 变更列表，并在画布上对变更节点打标记。
- **理由**：复用后端已生成的 `StructuredDiff`；人类既看「改了哪些字段」也看「在流程图哪里」。
- **影响**：
  - 新增 Diff 面板组件；Canvas 读取 diff 节点 ID 集合做高亮样式；`assemble` 新建草稿无 diff（整图即提案，列表为空）。
  - **diff 来源（关键）**：`StructuredDiff` 当前只存在于 `modify` 接口的返回体 `ModifyWorkflowResult.Diff`（`WorkflowAssemblyDtos.cs:174`），**未持久化到 `Workflow` 实体，也未进入 `WorkflowDto`**。前端是从列表点开草稿（§6.2），距 `modify` 调用已久，无法再取到 diff。因此后端必须把 diff **持久化**到草稿并随 `WorkflowDto` 返回（见 §7.5），前端才能渲染。
  - **按 `Op` 分支渲染**：`StructuredDiff` 实际字段为 `Op` / `NodeId` / `Field` / `Before` / `After`（`WorkflowAssemblyDtos.cs:180`）。`Op` 取值 `add` / `remove` / `modify` / `connect` / `disconnect`：
    - `modify`：展示「字段 / 改前 / 改后」三列。
    - `add` / `remove`：无 before/after，应展示「新增/删除节点（NodeId）」或连接描述，而非空的三列。
    - `connect` / `disconnect`：展示连接两端的节点与端口。
    - 面板须按 `Op` 类型分支渲染，不能一律按「字段/改前/改后」呈现。

### 5.7 旧搭建器：审查为主 + 手动模式保留

- **决策**：默认进入审查模式；拖拽搭建降级为「手动/高级模式」，仅在 AI 搞不定时由人类启用。
- **理由**：平滑迁移，不丢人工兜底能力；仍以 AI 为主路径。
- **影响**：Canvas 提供模式切换；手动模式下恢复现有拖拽/连线/填参能力（即今日行为）。

### 5.8 确认前校验：校验清单 + 凭据存在性

- **决策**：人类点「确认并激活」前，前端自动跑后端 `validate` + 凭据存在性检查，以绿/红「检查清单」呈现（必填、表达式合法、引用凭据存在、拓扑合法）。
- **理由**：AI 草稿常引用不存在的凭据、写错表达式，确认前主动暴露，避免激活后才失败。
- **影响**：见 §7.4（后端 `validate` 需覆盖凭据存在性，或新增独立端点）；前端新增确认前检查清单组件。

---

## 6. 前端改造清单

### 6.1 API 客户端（`services/api.ts`）

补齐后端已存在 / 需新增的客户端函数：

```typescript
// 现有已有：getWorkflow, getWorkflows, executeWorkflow, dryRun, cancelExecution, getCredentials
// 需新增：
validateWorkflow(id): Promise<ValidateWorkflowResult>   // POST /api/v1/workflows/validate，body: { workflowId: id }
confirmWorkflow(id): Promise<void>                       // POST /{id}/confirm
rejectDraft(id, reason): Promise<void>                  // 后端新增：存 RejectionReason（§7.2）
// 凭据存在性检查：优先走 validate 统一返回（方案 A，§7.4）；
// 轻量替代可复用现有 getCredentials() 在前端比对，无需新增端点（§7.4 方案 B'）。
```

> 前端**不新增** `assemble` / `modify` / `node-catalog` 客户端（意图入口在外部 AI）。

#### 6.1.1 需补的前端 DTO（`src/types`）

后端已有契约但前端 `src/types` 尚未同步，必须先补齐，否则 Diff 面板与确认前校验清单无法构建：

- `StructuredDiff`：`{ op: string; nodeId?: string; field?: string; before?: unknown; after?: unknown }`（对应 `WorkflowAssemblyDtos.cs:180`）。
- `ValidateWorkflowResult`：`{ valid: boolean; errors: ValidationError[]; canAutoFix: boolean }`（对应 `WorkflowValidationService.ValidateAsync` 返回）。
- `ModifyWorkflowResult` / `AssembleWorkflowResult`：如前端需展示 `modify` 即时结果，同步其 `Diff` 字段。
- `WorkflowDto` 扩展：随 §7.1 / §7.5 增加 `source` / `draftStatus` / `rejectionReason` / `diff` 字段。

### 6.2 工作流列表（复用现有 `getWorkflows()` 列表）

- 草稿（来源=AI、未确认）显示「AI 草稿·待审」徽标与创建时间/意图摘要。
- 点击进入审查页（即现有 `/workflow/:id` 编辑页的 `reviewMode` 形态，见 §6.3）而非普通编辑页。

### 6.3 审查页（现有编辑页的 `reviewMode` 形态）

> 说明：审查页**不是新建独立路由/页面**，而是复用现有 `/workflow/:id` 编辑页（`WorkflowEditorPage`），通过 store 的 `reviewMode` 标志切换为只读审查形态。这样画布/参数面板/执行面板的复用度最高，手动模式也只是同一个页面切回可编辑（§5.7）。§7.1 的 `draftSource`/`draftStatus` 决定默认是否进入 `reviewMode`。

- **Canvas（`reviewMode`）**：复用现有 React Flow 画布组件 `WorkflowCanvas.tsx`，将现有 `nodesDraggable`/`nodesConnectable` 控制由 `!isExecuting`（`WorkflowCanvas.tsx:219-220`）扩展为 `!isExecuting && !reviewMode`，并新增 `nodesResizable={!reviewMode}`。`reviewMode` 标志统一控制只读。
- **ParameterPanel（参数可微调）**：`reviewMode` 下仅参数可编辑，节点增删/连线/布局禁用。
- **Diff 面板（§5.6）**：从 `WorkflowDto.diff`（§7.5 持久化字段）渲染 `StructuredDiff`，按 `Op` 分支展示；变更节点在画布高亮。
- **测试栏**：「试运行」（dry-run）+「真实运行」（二次确认弹窗，明示副作用）；复用 `ExecutionPanel` 轨迹。
- **确认前校验清单（§5.8）**：调用 `validateWorkflow` 拿到校验+凭据缺失项（方案 A），绿/红展示。
- **操作**：「确认并激活」（`confirmWorkflow`）、「拒绝 + 理由」（弹窗填理由 → `rejectDraft`）、「切换到手动模式」（§5.7，退出 `reviewMode`）。

### 6.4 模式切换

- 默认 `reviewMode`。
- 「手动/高级模式」按钮切换到现有搭建行为（拖拽/连线/填参全开）。

### 6.5 状态模型（`stores/workflowStore.ts`）

新增草稿/来源/审查态字段，复用现有 `isActive`/`isExecuting`：

```typescript
interface WorkflowState {
  // 现有：nodes, connections, isActive, isExecuting, isDirty, ...
  reviewMode: boolean            // 新增：是否审查模式
  draftSource?: 'ai' | 'human'   // 新增：来源（for 列表徽标）
  draftStatus?: 'pending' | 'rejected' | 'confirmed'  // 新增：审查态
  structuredDiff?: StructuredDiff[]  // 新增：modify 草稿的 diff
}
```

---

## 7. 后端补强（最小增量）

> 后端 AI-native 主链路已存在，本设计仅要求支撑前端审查板的最小增量。

### 7.1 草稿来源与状态标识

- `Workflow` 实体或 DTO 增加 `Source`（`ai` / `human`）与 `DraftStatus`（`pending` / `rejected` / `confirmed`）。
- `CreateDraftAsync` 记录 `Source=ai`（由 `AiWorkflowsController` 调用时设置）；人工手动搭建创建时 `Source=human`。
- 列表接口返回这些字段供前端标注。

### 7.2 拒绝理由持久化

- `Workflow` 增加 `RejectionReason`（可空）。
- 新增 `RejectDraftAsync(id, reason)`：写入 `RejectionReason`、`DraftStatus=rejected`，**不删除**草稿（保留供 AI 拉取）。
- 对应 REST：`POST /api/v1/workflows/{id}/reject`（body: reason）。

### 7.3 新 MCP 工具 `get_draft_feedback`

- 暴露草稿的 `RejectionReason` / 最近执行反馈给外部 AI。
- 复用现有 `ExecutionsController` 的 `feedback` 思路，扩展为可拉取人类拒绝理由。

### 7.4 凭据存在性检查

- **结论（已验证）**：确认前校验需知道 AI 引用的 `credentialName` 是否存在。经核对，当前 `WorkflowValidationService.ValidateAsync`（`WorkflowValidationService.cs`）**不检查凭据存在性**——它只校验空节点、端口方向、循环、触发器、必填参数、表达式/Options，未覆盖 `$credentials.<name>` 引用。
- **好消息**：凭据存在性逻辑**已经写好**，位于 `WorkflowDraftValidator.cs:160-252`（扫描参数中的 `$credentials.<name>` 表达式，调用 `GetCredentialByNameAsync` 核对）。`ValidateAsync` 目前只调用了 `WorkflowDraftValidator.CollectMustacheErrors` / `CollectExpressionSyntaxErrors`（`WorkflowValidationService.cs:327-328`），未接入那段。
- **方案 A（推荐，需补后端增量）**：在 `ValidateAsync` 中复用 `WorkflowDraftValidator` 的凭据存在性扫描，使 `validate` 统一返回凭据缺失项。前端只需调用 `validateWorkflow` 即可拿到（错误类型如 `MissingCredential`）。此为最小改动，逻辑现成。
- **方案 B'（轻量替代，纯前端）**：前端已有 `getCredentials()`（`api.ts:134`）可拉取本项目全部凭据名，在前端用草稿引用的凭据名与之比对即可，无需新增任何端点。若团队暂不想动 `ValidateAsync`，可先用此方案。
- 二者择一即可；推荐 A，因校验统一在后端、与表达式校验同源。

### 7.5 结构化 diff 持久化与获取（支撑 §5.6）

> 这是前端 Diff 面板能否落地的硬缺口：当前 `StructuredDiff` 仅存在于 `modify` 返回体，未入库、未进 `WorkflowDto`，前端点开历史草稿时取不到。

- **实体扩展**：`Workflow` 实体新增字段（沿用现有 JSON 列约定）：
  ```csharp
  /// <summary>modify 草稿的结构化差异；assemble 新建草稿为空。</summary>
  [Column("diff")]
  [Comment("modify 草稿的结构化差异")]
  [JsonColumn]
  public List<StructuredDiff> Diff { get; set; } = [];
  ```
- **Fluent 配置**：JSON 列转换按后端规范走 Fluent API（Data Annotations 无法表达），在 `FlowEngineDbContext.OnModelCreating` 中为 `Workflow.Diff` 配置值转换（与 `Nodes`/`Connections` 的 `[JsonColumn]` 同机制）。
- **写入时机**：
  - `WorkflowModificationService.ModifyAsync` 已生成 `List<StructuredDiff> diffs` 并放入 `ModifyWorkflowResult.Diff`（`WorkflowModificationService.cs:51,136`）；在落库新建草稿时一并写入 `Workflow.Diff`。
  - `WorkflowAssemblyService` 的 `assemble` 路径新建草稿时 `Diff` 保持空（整图即提案，前端按空列表判定）。
- **DTO 透出**：`WorkflowDto` 增加 `Diff` 字段并在 `WorkflowMapper` 映射；列表接口 `getWorkflows()` 与详情 `getWorkflow(id)` 均随 `WorkflowDto` 返回，前端无需新增端点即可取得 diff。
- **前端消费**：`WorkflowCanvas`/Diff 面板从 `WorkflowDto.diff` 读取（见 §6.3），按 `Op` 分支渲染（见 §5.6）。

---

## 8. 与 AI-native 设计文档的关系

| AI-native 设计文档 | 本设计的关系 |
| ------------------ | ------------ |
| §2 架构总览（前端为人类预览） | 本设计将其落地为「纯审查板」，明确意图入口在外部、前端只读。 |
| §5.2.1 草稿持久化（IsActive=false） | 本设计复用，并补充 `Source`/`DraftStatus` 字段支撑前端识别。 |
| §6.4 结构化 diff（StructuredDiff） | 本设计明确 diff 在前端的呈现方式（§5.6）。 |
| §9 人类审查 | 本设计细化审查交互（§5.3）、测试（§5.4）、拒绝闭环（§5.5）、确认前校验（§5.8）。 |
| §10.2 REST API | 本设计新增 `reject` 端点（§7.2），其余复用 `validate`/`confirm`。 |

---

## 9. 待讨论 / 开放项

1. **凭据存在性检查归属**：已核验——当前 `validate` **不覆盖**凭据引用（§7.4 结论）。推荐方案 A：复用现成的 `WorkflowDraftValidator` 凭据扫描接入 `ValidateAsync`；轻量替代方案 B' 为前端用 `getCredentials()` 比对。二者择一即可。
2. **diff 持久化**：已补 §7.5——`StructuredDiff` 需入库随 `WorkflowDto` 返回，否则前端点开历史草稿取不到。待实施。
3. **草稿保留策略**：被拒绝的草稿保留多久？是否随新草稿覆盖？需定清理/版本策略。
4. **列表 vs 收件箱**：本设计选「复用列表」，若人类漏看率高，未来可升级为独立「待审」收件箱（§5.2 已预留不建收件箱的结论）。
5. **手动模式与 AI modify 冲突**：人类在手动模式大改后，AI 再 `modify` 如何合并？本期不解决，依赖「确认即版本化」兜底。
6. **审计/ authorship**：确认操作记录「谁确认」，当前 `ConfirmDraftAsync` 无确认人记录，建议补审计字段。

---

## 10. 帮助/教程 Surface（MCP 配置与 Skill 使用）

> 2026-07-13 用户补充：MCP 配置（含 Skill 使用教程）需在 Web 内「方便找到」，属于前端改造部分。其与本次审查板改造的关系见 §10.4。

### 10.1 定位：Web 内可发现的帮助入口

- **决策**：新增「帮助 / 配置 MCP」入口（位于 `Layout`/`HeaderToolbar`），点击进入帮助页（受保护路由，如 `/help`）。
- **理由**：首次用户配置 MCP 的教程不应埋在 markdown；应在登录后的 Web 内一眼可见、可复制。
- **影响**：`App.tsx` 增加 `/help` 路由（复用现有 `ProtectedRoute` 与 `Layout`）；`HeaderToolbar` 增加入口；不新增聊天/LLM 能力。

### 10.2 内容一：MCP 配置教程（Key 自动注入，用户不感知）

- **决策**：帮助页展示各客户端**复制即用片段**，片段中的 `Authorization: Bearer <key>` 由后端**自动生成并嵌入**，用户无需创建/查看/管理 API Key。
  - 客户端与落盘文件：
    - **Cursor**：`.cursor/mcp.json`，根键 `mcpServers`，`{ url, headers }`。
    - **VS Code**：`.vscode/mcp.json`，根键 `servers`（非 `mcpServers`），`{ url, headers }`。
    - **opencode**：项目根 `opencode.json`，根键 `mcp`，`{ type:"remote", url, headers }`。
    - **CodeBuddy**：全局 `~/.codebuddy/mcp.json`，根键 `mcpServers`，`{ type:"streamableHttp", url, headers }`。
    - **Claude Desktop**：全局配置，stdio shim，`{ command:"node", args:[shim], env:{ FLOWENGINE_URL, FLOWENGINE_API_KEY } }`。
  - 基址：开发 `http://localhost:8001/mcp`；部署后为公网地址（运维/配置注入，不可写死 localhost）。
  - **Key（2026-07-13 用户澄清）**：API Key 仅是 MCP 认证的内部机制；后端在生成配置时**自动创建（若不存在）一个 MCP 用途的 Key 并嵌入片段**，用户「不用关心这个 API Key」，**不提供独立的 API Key 管理 UI**。
  - **Key 生命周期**：自动创建的 MCP Key 应可轮换/吊销，避免成为无人管理的永久凭据。建议：Key 带用途标记（`mcp`）与创建时间；提供吊销入口（即便不暴露给普通用户，也应在管理面/CLI 可清理）；片段被复制后 Key 即落盘于用户 IDE 配置，需明确其泄露风险与吊销手段。
- **理由**：手动复制粘贴是本期基线（用户把片段粘到 IDE 或贴给 IDE 的 AI）；但 Key 作为认证细节应由系统注入，避免用户走「登录→建 Key→复制」的多步流程。
- **影响**：需后端 `GET /api/v1/me/mcp-config?client=...`（自动建/取 MCP Key + 按客户端拼配置，见 task-016）；前端复用 Mantine 代码块 + 复制按钮（ahooks `useRequest`/`useClipboard`），`src/types` 增加片段 DTO。

### 10.3 内容二：Skill 使用教程

- **决策**：帮助页搬运 `mcp-shim/skill/claude.md` 的「MCP 工具驱动的 AI 工作流」「节点草稿极简格式」「常见错误与自纠」为 Web 可读内容，作为「如何给 IDE 的 AI 下指令」的参考。
- **理由**：教程既教「如何接入 MCP」，也教「接入后如何用自然语言驱动 AI 搭建/修改/运行工作流」，闭合 AI-native 使用链路。
- **影响**：内容以 `claude.md` 为**唯一来源**，Web 与文档同源（改一处需同步，或在文档注明唯一来源）。

### 10.4 与本次改造的关系

- 本 surface 是**前端形态**，符合 §1.3「本设计只描述前端形态」；MCP 客户端实现本身不在本仓库（§1.3 已声明）。
- 不引入聊天 UI、不新增 LLM 调用；纯展示 + 复制。
- 与 §5–§7 审查板正交：帮助页是独立路由，不参与草稿审查/确认流。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务/PR |
| ---- | ------ | -------- | ----------- |
| 2026-07-12 | Agent | 创建前端审查板改造设计文档，落地 AI-native 设计的八大前端决策 | grill-me 访谈对齐 |
| 2026-07-13 | Agent | 新增 §10 帮助/教程 Surface：Web 内可发现的 MCP 配置 + Skill 使用教程（前端改造部分） | task-016 |
| 2026-07-13 | Agent | 文档审查修订：补 §7.5 diff 持久化方案；§7.4 核验凭据校验结论并加方案 B'；§5.6/§6.3 统一 reviewMode 形态与按 Op 渲染；§6.1 修正端点签名与前端 DTO 缺口；§10.2 补 Key 生命周期；§9 收口开放项 | 代码核对 |
