# 任务：Agent IDE 驱动 DSL 生成改造

## 目标

将 Flow Engine 的 DSL 生成职责从后端 LLM 转移到 Agent IDE，解决 [PromptTemplates.cs](../backend/FlowEngine.Application/Workflows/PromptTemplates.cs) 写死钉钉同步等业务知识的问题，同时让 LLM 配置更贴近运行时凭据化模型。

## 背景

当前 CLI `workflow generate` 调用后端 `POST /api/v1/workflows/generate`，由 [WorkflowGenerationService](../backend/FlowEngine.Application/Workflows/WorkflowGenerationService.cs) 构造 Prompt 并调 LLM 生成 DSL。该模式导致：

- 平台代码里出现具体业务场景知识（钉钉同步配方）。
- 后端需要全局 `AiOptions` 配置，与多模型、多租户需求不符。
- 生成质量依赖后端 Prompt 工程，平台却难以控制。

改造后：

- Agent IDE（Cursor/Claude Code/Claude Desktop）通过读取 CLI skill 文件掌握 DSL schema 与节点类型定义，直接生成 DSL JSON。
- CLI 仅负责校验（`workflow validate` / `workflow create --dry-run`）和提交（`workflow create`）。
- 后端删除 `PromptTemplates`、`WorkflowGenerationService`、`/workflows/generate` 端点及全局 `AiOptions`。
- 运行时 LLM/Agent 节点所需 LLM 配置改为通过凭据系统引用，支持多模型、多项目隔离。

## 待完成项

### 阶段一：后端清理

- [ ] 删除 [PromptTemplates.cs](../backend/FlowEngine.Application/Workflows/PromptTemplates.cs)
- [ ] 删除 [WorkflowGenerationService.cs](../backend/FlowEngine.Application/Workflows/WorkflowGenerationService.cs)（含内部 `WorkflowGenerationRequest` / `WorkflowGenerationResponse` records）
- [ ] 删除 [WorkflowsController.cs](../backend/FlowEngine.Host/Controllers/WorkflowsController.cs) 的 `Generate` 方法与 `generationService` 构造函数参数
- [ ] 删除 [ServiceCollectionExtensions.cs](../backend/FlowEngine.Host/ServiceCollectionExtensions.cs) 中以下注册：
  - `WorkflowGenerationService`
  - `AiOptions` 配置绑定
  - 系统级 `ILlmClient` 单例
  - `Func<ILlmClient>` 延迟解析委托
- [ ] 删除 [SystemLlmClientFactory.cs](../backend/FlowEngine.Infrastructure/Ai/SystemLlmClientFactory.cs)
- [ ] 删除 [AiOptions.cs](../backend/FlowEngine.Core/Configuration/AiOptions.cs)
- [ ] 删除 [appsettings.json](../backend/FlowEngine.Host/appsettings.json) 的 `"Ai"` 节点

### 阶段二：确认运行时 LLM 节点已凭据化（无需新增凭据类型）

> 经核实，运行时 `LlmNode` / `AgentNode` 早已基于凭据系统工作，与待删除的全局 `AiOptions` 无关：
> - `LlmNode` 通过 `CredentialId`（凭据，字段 `apiKey`）+ 节点参数 `Model` / `Temperature` / `MaxTokens` / `BaseEndpoint` 取配置，并以 `context.LlmClientFactory.Create(...)` 创建客户端；
> - `AgentNode` 不持有 LLM 配置，而是从上游 `llm` 节点的 `context.LlmClient` 获取；
> - 全局 `AiOptions` 与系统级 `ILlmClient` 单例**仅**被（待删除的）`WorkflowGenerationService` 使用，运行时节点用的是已注册的 `ILlmClientFactory` 单例。
>
> 因此本阶段**不做**"新增 `openAiApiKey` 凭据类型"或"改用 `credentialName`"之类的改造，以免与现有通用 `apiKey` 凭据类型重复、并避免破坏 `LlmNode` 现有的 Guid 解析逻辑。

- [ ] 删除 `AiOptions` / 系统级 `ILlmClient` 单例 / `Func<ILlmClient>` 后，跑一遍含 `llm` / `agent` 节点的工作流，确认运行时仍正常（它们本就不依赖 `AiOptions`）。
- [ ] 确认 `OpenAiLlmClientFactory`（`ILlmClientFactory`）仍按单例注册，供运行时节点解析 LLM 客户端。
- [ ] （独立决策项，不在本任务范围）统一凭据解析方式：`LlmNode` 当前按 `Guid`（`CredentialId`）解析、`PaginateNode` 等按名称解析，二者不一致；如需统一为 `credentialName`，应作为单独任务评估，不应与本次 DSL 生成迁移耦合。

### 阶段三：CLI 改造

- [ ] 删除 [cli/src/types.ts](../cli/src/types.ts) 的 `WorkflowGenerationRequestDto` / `WorkflowGenerationResponseDto`
- [ ] 删除 [cli/src/commands/workflows.ts](../cli/src/commands/workflows.ts) 的 `WorkflowGenerateOptions` 与 `workflowGenerate` 函数
- [ ] 删除 [cli/src/index.ts](../cli/src/index.ts) 的 `workflow generate` 命令注册
- [ ] 删除 [cli/src/__tests__/workflows-commands.test.ts](../cli/src/__tests__/workflows-commands.test.ts) 的 `workflow generate` 测试块
- [ ] 更新 [cli/src/commands/guide.ts](../cli/src/commands/guide.ts)：
  - 删除 `aiGeneration` 章节
  - 将"钉钉员工同步"示例改为通用"第三方 API 分页同步到数据库"示例
- [ ] 更新 [cli/skill/claude.md](../cli/skill/claude.md)：
  - 删除 `workflow generate` 命令说明
  - 删除钉钉相关示例
  - 增加 Agent IDE 工作流：组合 `node-types list --json` → `guide --json` → `workflow create --file xxx.json --dry-run` → `workflow create`
- [ ] 更新 [cli/skill/cursor.md](../cli/skill/cursor.md) 与 [cli/skill/skill.json](../cli/skill/skill.json)，同步删除 generate 相关内容
- [ ] （可选）新增 `flowengine schema` 命令，输出机器友好的 DSL JSON Schema

### 阶段四：文档同步

- [ ] 标记或重写 [plan-ai-dsl-generation.md](./plan-ai-dsl-generation.md)
- [ ] 更新 [natural-language-to-dsl.md](../architecture/natural-language-to-dsl.md) 架构描述
- [ ] 复核 [docs/index.md](../index.md) 等文档中关于 AI 生成的描述

### 阶段五：验证

- [ ] 后端 `dotnet build FlowEngine.sln` 无错误无新增警告
- [ ] 后端 `dotnet test` 全绿
- [ ] CLI `npm run build` / `npm run test` 全绿
- [ ] 确认 `POST /api/v1/workflows/generate` 已不存在
- [ ] 确认包含 `llm` / `agent` 节点的工作流仍能正常执行（AiOptions 删除后，运行时本就不依赖它）
- [ ] 在 Agent IDE 中完成一次端到端验证：自然语言描述 → DSL 生成 → `--dry-run` 通过 → `workflow create` 成功

## 完成标准

- 后端不再包含任何与 DSL 生成相关的 Prompt 模板或 LLM 系统级配置。
- CLI 不再提供 `workflow generate` 命令。
- 运行时 LLM/Agent 节点保持现有凭据化行为不变（删除 AiOptions 不影响其多模型 / 多凭据能力）。
- 全量编译与测试通过。
- Agent IDE 可直接基于 CLI skill 生成合法 DSL 并通过 CLI 提交。

## 主要修改记录（待执行后填写）

- 后端：删除 PromptTemplates、WorkflowGenerationService、AiOptions、SystemLlmClientFactory、`/workflows/generate` 端点。
- 后端：新增 LLM API Key 凭据类型，改造 LlmNode / AgentNode 参数与解析逻辑。
- CLI：删除 generate 命令、类型、测试；更新 guide 与 skill 文件。
- 文档：更新 AI DSL 生成相关计划与架构文档。
