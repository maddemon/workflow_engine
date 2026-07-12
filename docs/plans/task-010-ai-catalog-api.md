# 任务：AI-native Catalog API（task-010-ai-catalog-api）

## 目标

为「AI 创建工作流、人类只审查」的新思路落地第一步：让 AI 能通过 Catalog API 发现节点、通过 Workflow API 生成/修改工作流。

- **改造 Workflow DSL 模型**：`NodeDefinition.Id` 改为 `string`；`PositionX/Y` 改为可选；`Connection` 端口默认化；`IsEntry` 自动推导。**本次改造不向后兼容旧数据、旧逻辑**。
- **不修改 `INodeType` 执行契约**，31 个生产节点的执行逻辑不受影响。
- 通过**自动适配器**（`NodeTypeDescriptor` → AI-native 定义）让现有节点零改动即可被 AI 发现。
- 预留可选覆盖接口 `IAiDefinitionProvider`，节点可声明更精确的 AI 元数据（description、tags、examples、ports）。

## 待完成项

### Phase 1：DSL 模型重构（不兼容改造）

> 对应设计文档 §10.1 Phase 1。`NodeDefinition` 与 `Connection` 均标 `[NotMapped]`，作为 JSON 内嵌在 `Workflow` 的 `nodes`/`connections` 列中。改动 C# 类型只改变序列化后的 JSON 形状，不会触发 EF 迁移；真正的兼容风险是**已落库的旧 JSON 无法反序列化**。

- [x] 修改 `NodeDefinition`：`Id` 从 `Guid` 改为 `string`；`PositionX`/`PositionY` 改为 `int?`
- [x] 修改 `Connection`：`SourcePortName`/`TargetPortName` 改为可空；增加 `Condition` 字段（分支条件）
- [x] 更新执行引擎：连接读取时处理默认端口（缺省取第一个 Input/Output）
- [x] 更新 `WorkflowValidator`：适配 string ID、默认端口推断、入口节点自动推导、多 Trigger 语义
- [x] 更新 DTO/Mappers：`WorkflowDtos`、`WorkflowImportExportDtos`、`WorkflowMapper`
- [x] 删除旧 DSL 兼容代码（如 Guid 解析、强制端口名校验等）
- [x] **旧数据策略**：清空或迁移 `Workflow` 表中已有的 `nodes`/`connections` JSON；本次不保留旧数据反序列化兼容

### Phase 2：Catalog 模型与适配器

> 对应设计文档 §10.1 Phase 2–3。

- [x] 新增 AI-native 只读模型（`AiNodeSummary` / `AiNodeDefinition` / `AiPortSchema` / `AiExample`）
- [x] 新增可选接口 `IAiDefinitionProvider`

  ```csharp
  public interface IAiDefinitionProvider
  {
      /// <summary>
      /// 返回该节点类型的 AI-native 定义，覆盖适配器自动推导。
      /// </summary>
      /// <param name="descriptor">由 ParameterDiscoverer 生成的节点描述，供参考。</param>
      AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor);
  }
  ```
- [x] 实现 `NodeDefinitionAdapter`：`ParameterDefinition` → JSON Schema，`DataSchema` → JSON Schema，`PortDefinition` → `AiPortSchema`，优先覆盖
- [x] 实现 `CatalogService`（依赖 `INodeRegistry`）
- [x] 新增 `NodeCatalogController`：`GET /api/v1/node-catalog`、`GET /api/v1/node-catalog/{name}`
- [x] 编写适配器 + Service 测试，遵循 TDD（先红后绿）

### Phase 3：Workflow 生成/修改/校验 API

> 对应设计文档 §10.1 Phase 4–5。

- [x] 实现 `WorkflowAssemblyService`：接收 AI DSL 草稿，补全为完整 DSL（id、ports、position、isEntry）。明确区别于旧版 LLM 生成，只做后端组装。
- [x] 实现 `WorkflowModificationService`：应用操作列表（自定义 operations，非 RFC 6902 JSON Patch）
- [x] 实现 `WorkflowValidationService`：校验 schema、拓扑、表达式；返回结构化错误（含 `canAutoFix`/`errorType`/`suggestedFix`）
- [x] 实现 `WorkflowExecutionFeedbackService`：捕获执行失败并转换为结构化反馈，供 AI 自纠
- [x] 新增 `WorkflowsController` 端点：`POST /assemble`、`POST /{id}/modify`、`POST /validate`、`POST /{id}/confirm`
- [x] 编写生成/修改/校验测试

### Phase 4：消费方同步改造（不兼容变更必须全部完成，否则系统半坏）

> 落实设计文档 §13「不向后兼容」策略。设计文档 Phase 6–7（MCP Server、外部 AI 集成测试）不在本任务范围。

| 消费方 | 影响点 | 建议 Owner |
| ------ | ------ | ---------- |
| 前端工作流编辑器 | 保存格式改为 string ID、可选坐标、默认端口 | 前端 |
| 前端工作流详情/预览 | 读取新 DSL 渲染节点图 | 前端 |
| CLI `workflow create` | 输入改为新 DSL 草稿格式 | CLI |
| CLI `workflow import/export` | 导入导出 JSON 改为新 DSL | CLI |
| 后端导入导出服务 | `WorkflowImportService` / `WorkflowExportService` 适配新 DSL | 后端 |
| 后端测试数据/种子 | 所有测试用例中的旧 DSL JSON 更新 | 后端 |

- [x] 前端工作流编辑器保存/读取改为新 DSL
- [x] CLI `workflow create` / `import` / `export` 改为新 DSL
- [x] 后端导入导出服务适配新 DSL（已自动兼容；删除废弃的 Guid-based `NodeDefinitionId` 值对象）
- [x] 所有测试用例中的旧 DSL 数据更新（测试数据 JSON 已使用 string ID，无旧格式数据）
- [x] 旧 `Workflow` 表数据清空或迁移完成（新增 EF Core 迁移 `ClearIncompatibleWorkflowData`，将 `nodes`/`connections` 列置为 `[]`）

### Phase 5：验证

- [x] `dotnet build` + `dotnet test` 全绿（878 pass, 0 fail）
- [x] 前端 `npm run build` 通过（tsc + vite build）
- [x] CLI `npx tsc --noEmit` 通过（0 errors）
- [x] 端到端验证：新增 `AiWorkflowEndToEndTests`，覆盖 Catalog → assemble → confirm → execute 全链路（878 测试中含此 1 例）

## 设计要点

### 模块划分

- AI 模型与适配器放 `FlowEngine.Core`（依赖根，未来 MCP Server 可复用）；`CatalogService` 放 `FlowEngine.Application`；Controller 放 `FlowEngine.Host`。
- 模型非 EF 实体：只作内存只读投影，不进 `FlowEngineDbContext`，不触发迁移。
- JSON 约定：沿用全局 camelCase + `JsonStringEnumConverter`；输入/输出 schema 用 `System.Text.Json.JsonNode` 动态构建。

### DSL 重构要点

- `NodeDefinition.Id` 改为 `string`，AI 可用自然名称（如 `fetch`）；后端保证工作流内唯一。
- `PositionX`/`PositionY` 改为 `int?`，AI 不填时后端自动布局。
- `Connection.SourcePortName`/`TargetPortName` 改为可空；缺省时后端取源节点第一个 Output、目标节点第一个 Input。
- 分支节点（If/Switch）由 AI 显式填写 `fromPort`（如 `true`/`false`）选择出口。
- `Connection.Condition` 是执行引擎运行时求值字段，**不由 AI 填写**。
- `NodeDefinition.IsEntry` 自动推导：第一个 Trigger 节点为入口；AI 可显式覆盖。
- `NodeDefinition.Ports` 由后端根据节点类型 `PortDefinition` 自动填充，AI 不填。

### 类型映射（`ParameterType` → JSON Schema）

- String → `string`；Number → `number`；Boolean → `boolean`
- Options → `string` + `enum:[values]`
- Json（有 Fields）→ `object` + `properties`；否则 `object`
- Array → `array` + `items`（来自 `ItemDefinition`，或 `Fields` 推断）
- Code / Script / Credential / Resource / File → `string`
- `supportsExpression`：类型属于 String / Json / Code / Script 时置 `true`。AI 生成参数值时使用运行时 `$node['xxx'].json[0].field` 等裸 `$` 表达式，不使用 `{{ }}` 包裹。
- `required`：来自 `ParameterDefinition.Required`
- `default`：来自 `DefaultValue`（有值时）

### Catalog 详情必须包含

- `inputSchema`：节点参数 JSON Schema
- `outputSchema`：节点输出 JSON Schema。**注意**：自动推导通常很薄；有意义的 `outputSchema` 需节点实现 `IAiDefinitionProvider` 手工编写。
- `ports`：每个端口的 `name`/`direction`/`description`，AI 必须知道才能正确连接
- `examples`：1-2 个填参示例
- `description`/`tags`：自动推导时不能为空；推荐关键节点实现 `IAiDefinitionProvider` 提供人工编写内容

### 其他

- **IsTrigger**：`Category == "Trigger"` 或 `DefaultIsEntry` 推导。
- **敏感默认值**：`Credential`/`File` 类型或名称含 `secret`/`token`/`password`/`apiKey` 的参数不输出 `default`。
- **鉴权**：沿用 `NodeTypesController` 现状（未鉴权）。Catalog 暴露的是节点能力元数据，不包含敏感业务数据；后续按角色隐藏节点时再引入 RBAC。
- **不向后兼容**：旧 DSL JSON、旧导入导出格式、旧前端保存格式、旧 CLI 命令格式均不再保留。实施前需清空或迁移旧数据。

## 完成标准

### DSL 重构

- [x] `NodeDefinition.Id` 为 `string`，`PositionX/Y` 为 `int?`。
- [x] `Connection.SourcePortName`/`TargetPortName` 可空，支持 `Condition`。
- [x] 执行引擎能处理缺省端口名，自动匹配 Input/Output。
- [x] `WorkflowValidator` 能自动推导入口节点并校验多 Trigger 场景。
- [x] 旧 Guid 解析、强制端口名校验等兼容代码已删除。

### Catalog API

- [x] `GET /api/v1/node-catalog` 返回现有全部节点的紧凑列表（name/displayName/description/category/tags/isTrigger）。
- [x] `GET /api/v1/node-catalog/{name}` 返回该节点的 `inputSchema`、`outputSchema`、`ports`、`examples`。
- [x] 现有一个节点实现 `IAiDefinitionProvider` 时，详情优先使用该覆盖定义。
- [x] `description` 不为空；敏感参数默认值已被过滤。

### Workflow API

- [x] `POST /api/v1/workflows/assemble` 接收 AI DSL 草稿，创建未激活草稿并返回 `{ draftId, workflow }`。
- [x] `POST /api/v1/workflows/{id}/modify` 接收操作列表，基于源工作流创建新草稿，返回 `{ draftId, workflow, diff }`。
- [x] `POST /api/v1/workflows/validate` 返回结构化错误（含 `nodeId`/`field`/`message`/`canAutoFix`/`suggestedFix`）。
- [x] 执行失败反馈包含 `nodeId`/`errorType`/`executionContext`/`suggestedFix`，支持 AI 自纠。
- [x] `POST /api/v1/workflows/{id}/confirm` 将草稿转正并激活。

### 测试与构建

- [x] 适配器测试覆盖类型映射、递归、required、supportsExpression、覆盖优先级、ports 转换。
- [x] Workflow 生成/修改/校验测试覆盖 id 补全、默认端口、入口推导、拓扑校验。
- [x] `dotnet build` 通过，`dotnet test` 全绿，无回归。

## 主要修改记录

- 重构 Workflow DSL：`NodeDefinition.Id` → `string`；`PositionX/Y` → `int?`；`Connection` 端口默认化 + `Condition`；`IsEntry` 自动推导；删除旧 Guid/强制端口兼容代码。
- 新增 AI-native 模型：`AiNodeSummary`、`AiNodeDefinition`、`AiPortSchema`、`AiExample`。
- 新增 `IAiDefinitionProvider` 接口，支持节点覆盖自动推导的 AI 元数据。
- 实现 `NodeDefinitionAdapter`：完成 `ParameterDefinition` → JSON Schema、`DataSchema` → JSON Schema、`PortDefinition` → `AiPortSchema` 的转换。
- 实现 `CatalogService` 与 `NodeCatalogController`，提供 `GET /api/v1/node-catalog` 和 `GET /api/v1/node-catalog/{name}`。
- 实现 Workflow 组装/修改/校验/执行反馈服务与 Controller 端点；`POST /workflows/assemble` 创建未激活草稿；`modify` 返回结构化 diff；校验/执行错误支持 AI 自动修复。
- 明确不向后兼容旧 DSL/导入导出/前端保存格式；列出前端/CLI/后端导入导出/测试数据等消费方同步改造清单。

## 完成状态

| 阶段 | 状态 | 说明 |
| ---- | ---- | ---- |
| Phase 1：DSL 模型重构 | ✅ 完成 | 类型改造、执行引擎、Validator、Mapper、旧兼容代码删除均完成；`dotnet test` 全绿 |
| Phase 2：Catalog 模型与适配器 | ✅ 完成 | `AiNode*` 模型、`IAiDefinitionProvider`、`NodeDefinitionAdapter`、`CatalogService`、`NodeCatalogController`、测试完成 |
| Phase 3：Workflow 生成/修改/校验 API | ✅ 完成 | `WorkflowAssemblyService`/`WorkflowModificationService`/`WorkflowValidationService`/`WorkflowExecutionFeedbackService`、`AiWorkflowsController`、测试完成 |
| Phase 4：消费方同步改造 | ✅ 完成 | 前端类型 + 序列化器适配新 DSL；CLI 类型/校验/文档全链路更新；后端废弃 `NodeDefinitionId` 值对象删除；旧数据迁移创建 |
| Phase 5：验证 | ✅ 完成 | `dotnet build` 0 错误 0 警告；`dotnet test` 885 pass 0 fail（含整体 review 新增 7 例）；前端 `npm run build` 通过；CLI `tsc --noEmit` 0 errors；新增端到端测试 `AiWorkflowEndToEndTests` 覆盖 Catalog→assemble→confirm→execute 全链路 |

### 当前进度备注（2026-07-12）

- 后端三阶段（Phase 1–3）实现完毕，全部 878 个后端测试通过（`dotnet test` 0 失败），`dotnet build` 0 警告 0 错误。
- 本会话修复的回归：触发节点校验从 `WorkflowValidator.Validate()` 移出（仅 AI 组装/修改路径要求 Trigger），Runtime 反射测试 `typeof(Guid)`→`typeof(string)`，`AiWorkflowsController` 与 `ExecutionsController` 的 `POST .../{id}/execute` 路由冲突已移除重复端点。
- Phase 4（消费方同步）为不兼容变更收尾，必须全部完成否则系统半坏；详见下方 Phase 4 清单。
- 代码评审（@oracle）已完成。4 个 Blocker 已修复：① `AiWorkflowsController` 与 `WorkflowsController` 的 `GET {id:guid}` 路由冲突已移除重复端点；② `WorkflowModificationService.ApplyModify` 节点属性修改死代码已修复（`pathParts.Length == 2` → `>= 3`，支持 `name`/`isEntry`）；③ `NodeDefinitionAdapter.GetDescription` 从取首个参数描述改为使用 `DisplayName`；④ `WorkflowModificationService.ModifyAsync` 缺失审计事件已通过注入 `IEventBus`+`AuditEventFactory` 修复。⬜ 非阻塞改进：`NodeCatalogController` 返回类型强类型化、`Details` 字段移除、`ConnectionDto.Condition` 加 `[Description]`、`WorkflowExecutionFeedbackService.NodeName` 加 TODO 注释。全部 878 测试构建后再通过。
- **Phase 4 完成（2026-07-12）**：前端 `NodeDefinition.positionX/Y` 改为 `number | null`，`Connection.sourcePortName/targetPortName` 改为 `string | null` 并新增 `condition?`；序列化器零值安全处理（`?? 0` 位置、`?` 端口名）。CLI 类型同步更改；`entryCount` 校验移除（后端自动推导入口节点）；端口查找在缺省名时跳过；guide/skill 文档更新。后端删除废弃的 `NodeDefinitionId` 值对象。新增 EF Core SQLite 迁移 `ClearIncompatibleWorkflowData`。`dotnet build` 0 错误 0 警告，`dotnet test` 877 pass 0 fail，前端 `npm run build` 通过，CLI `npx tsc --noEmit` 0 errors。
- **Phase 5 完成（2026-07-12）**：新增端到端集成测试 `tests/FlowEngine.Host.Tests/Workflows/AiWorkflowEndToEndTests.cs`，覆盖 AI 全链路：Catalog 取节点（`manualTrigger`/`set`）→ assemble 建草稿（含 Trigger + Set 节点与连接）→ confirm 激活 → execute 触发执行（验证执行记录创建且状态合法）。`dotnet test` 878 pass 0 fail（含此新增 1 例）。节点实际执行已由 dry-run 与运行时测试覆盖。

### 整体 Review 修复（2026-07-12 后续）

对 Phase 1–5 做整体 review（5 个并行 @oracle 评审 + 1 份独立 AI 评审交叉核对），修复发现的设计违背与 Blocker 缺口。`dotnet build` 0 错误 0 警告；`dotnet test` 885 pass 0 fail（878 + 新增 7 例）。

**已修复（设计违背 / Blocker）：**

- **Phase 2 覆盖优先级（设计文档 §3.4 / §10.4）**：`NodeDefinitionAdapter.ToAiDefinition` 原仅覆盖 `Description`/`Tags`/`Examples`，违背「`IAiDefinitionProvider` 优先级 > 自动推导、全字段覆盖」。已改为覆盖 `DisplayName`/`Category`/`IsTrigger`/`InputSchema`/`Ports` 等全部字段；新增 `HasValue` 辅助区分「未提供」与「显式空」；`SensitiveNamePatterns` 补 `api_key`/`api-key`。新增测试 `Override_Provider_Adopts_All_Fields_Over_AutoDerivation`。
- **Phase 3 执行反馈 `executionContext`（验收 §5.4）**：`ExecutionFeedbackNode` 原缺 `executionContext` 字段。已新增 `ExecutionContext`（`object?`，含 `RawParameters` + `Inputs`），`WorkflowExecutionFeedbackService.BuildExecutionContext` 填充。新增 `WorkflowExecutionFeedbackServiceTests`（2 例）。
- **Phase 3 草稿补全（设计文档 §5.2 步骤 1/6）**：`WorkflowAssemblyService` 原对缺 `id` 节点直接抛 `NodeIdEmpty`、缺 `position` 不布局。已新增 `GenerateNodeId`（缺 id 按 `typeName` 生成 + 唯一后缀）、`ApplyAutoLayout`（按依赖层级最长路径布局补全 `PositionX/Y`）。替换/新增 `AutoGeneratedFromTypeName`、`DuplicateGeneratedIds_GetUniqueSuffix`、`AutoLayout_SetsPositionsByDependencyLayer`、`FirstTrigger_MarkedAsEntry`、`MultipleTriggers_AllowsAndMarksFirstAsEntry`。
- **Phase 1 入口推导（设计文档 §7.3）**：`WorkflowValidator.DeriveEntryNodes` 原强制第一个 Trigger 为入口，忽略 AI 显式 `isEntry:true`。已改为已有显式入口时尊重覆盖。
- **Phase 4 draft 校验**：`WorkflowDraftValidator` 原 `entryCount==0` 即报错，与后端自动推导入口决策不一致。已放宽为 `entryCount==0 && triggerCount==0`。
- **测试断言收紧**：`WorkflowValidationServiceTests` 补齐 `CanAutoFix`/`SuggestedFix` 断言；`AiWorkflowEndToEndTests` 执行状态断言收窄为 `ExecutionStatus.Pending`（避免误判 `Completed`）。

**已补充实现：**

- **Phase 1 B1/B2**：`ExecutionSession` 新增 `INodeRegistry?` 参数，`ConnectionsBySource` 端口解析时当 `NodeDefinition.Ports` 为空时降级到注册中心 `PortDefinition`；更新 `WorkflowExecutor` / `WorkflowDryRunService` / `WorkflowSchedulerKernelTests` 三处构造调用。
- **参数 schema 校验**：`WorkflowValidationService.ValidateAsync` 新增步骤 7，校验 Options 类型参数值是否在允许枚举范围内；新增 `ExtractParameterString` 辅助。
- **N1–N18 非阻塞项**：N8 `JsonDefaults` 添加 `JsonStringEnumConverter`（枚举序列化为字符串）；N1 Catalog 强类型返回、N5 Details 字段清理、ConnectionDto.Condition `[Description]` 均已就绪。
