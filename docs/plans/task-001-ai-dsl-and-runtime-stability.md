# 任务：AI DSL 生成 + 运行时稳定性 并行实施

> 关联计划：`plan-ai-dsl-generation.md`（Plan 1）、`plan-runtime-stability.md`（Plan 2）。
> 本任务文档跟踪两计划的并行实施，重点记录跨计划共享根因（S2 钉钉令牌）与冲突协调。

## 目标

- 实现 AI 驱动的工作流 DSL 生成（后端语义解析 + CLI generate），经校验-纠错循环后通过 CLI 创建/执行。
- 补齐"生成后能否跑通"的运行时稳定性短板，使钉钉场景端到端可稳定运行。

## 已固化结论（不再争议的假设）

- 触发命令为 CLI 根级 `execute [workflow-id]`；`execution` 子命令组仅查/取消，无 run。
- S8 后端已解决（`CredentialTypeRegistry` 存在，4 类型）；阶段二仅收口。
- S3（表达式引用）已解决；S9 基本解决。阻塞仅剩 S2。
- `WorkflowDraftValidator` 统一为 `WorkflowDraftValidator(INodeRegistry, ICredentialService)` 版。
- Plan 1 阶段五 `--create`：本期仅结构校验后创建，不强制 dry-run；dry-run 闭环待 Plan 2 阶段四。
- successWhen 优先级：①先判 HTTP 状态码（非 2xx 失败）；②HTTP 成功再判 successWhen；③失败=节点失败不自动重试（除非配 retryPolicy）；④配置在节点参数。
- OAuth2 扩展字段采用 provider 内置策略（standard/dingtalk），不暴露给用户逐项填。
- Plan 2 阶段五 5.1/5.2 必做；5.3/5.4/5.5 本期顺带实现（已确认）。
- OpenAiLlmClient 采用方案 A 移至 Infrastructure（已确认）。
- guide.ts 由 Plan 1 阶段六 + Plan 2 阶段三合并为单任务（已确认）。

## 待完成项（按执行顺序）

### 根因（最高优先级）
- [x] **P2-阶段零**：钉钉令牌请求策略适配
  - [x] 0.1 `OAuth2TokenRequest` 扩展 `HttpMethod`/`ParamLocation`/`ParamNameMap`/`ResponseErrorPath`/`ResponseSuccessValues`
  - [x] 0.1 重写 `RequestTokenAsync` 按策略拼装 GET/POST + query/body + 业务错误判定
  - [x] 0.1 oauth2 凭据 `provider`（standard/dingtalk）字段 + 内置模板映射（`OAuth2ProviderTemplates` + `OAuth2CredentialAccessor`）
  - [x] 0.1 单元测试：标准 client_credentials 仍通过；钉钉 GET+query(appkey/appsecret)+errcode 判定通过
  - [x] 0.2 HTTP/Paginate 节点 `successWhen` 表达式参数（缺省仅按状态码，向后兼容）
  - [x] 0.2 测试：errcode!=0 但 HTTP 200 → 节点失败（HttpRequestNode 已测；Paginate 待补）

### Plan 1 — AI DSL 生成
- [x] 阶段一 LLM 系统级集成（AiOptions + SystemLlmClientFactory 方案 A + DI + appsettings）含测试
- [x] 阶段二 `WorkflowDraftValidator(INodeRegistry, ICredentialService)` 含测试
- [x] 阶段三 `WorkflowGenerationService` + `PromptTemplates`（校验-纠错循环）含测试
- [x] 阶段四 `POST /api/v1/workflows/generate` 端点（RBAC/审计）含集成测试
- [x] 阶段五 CLI `workflow generate`（--description/--output/--create 基础版）含测试
- [x] 阶段六 skill/guide 更新（**与 P2 阶段三合并为单任务**）

### Plan 2 — 运行时稳定性
- [x] 阶段一 SetNode 表达式支持（S5）含测试
- [x] 阶段二 凭据类型注册表收口（S8）含测试 + 可选 GET /credential-types
  - [x] 类型清单已对齐（后端 4 种）；补 `provider` 可选字段 + 单元测试（12 passed）
  - [ ] （可选）`GET /api/v1/credential-types` 端点 —— 待阶段五 5.2 一并决定是否落地
- [x] 阶段三 CLI guide 变量参考（S9 收尾，**与 P1 阶段六合并**）
- [ ] 阶段四 Dry-Run 集成到 generate 流程（CLI --dry-run）含测试
- [ ] 阶段五 界面途径缺口：5.1 Dry-Run 按钮、5.2 connectionString、5.3 凭据内联新建+刷新、5.4 timeout 编辑、5.5 节点复制/粘贴

## 完成标准

- `dotnet build FlowEngine.sln` 通过，无新增警告；`dotnet test` 全部通过。
- CLI `npm run build` / `npm run test` 通过。
- 端到端：AI 生成"钉钉员工同步到数据库"DSL → `--dry-run` 试运行 → 创建 → `execute [workflow-id]` 执行成功。
- 旧 SetNode 纯字符串字段值仍可加载执行（向后兼容）。

## 主要修改记录

### 2026-07-11 阶段一 LLM 系统级集成（方案 A）
- 新增 `Core/Configuration/AiOptions.cs`：`Ai` 节点配置（ApiKey/Model/Temperature/MaxTokens/BaseEndpoint）。
- `OpenAiLlmClient` 由 `plugins/FlowEngine.Plugins.Standard/` 下沉至 `backend/FlowEngine.Infrastructure/Ai/`（命名空间 `FlowEngine.Infrastructure.Ai`）；`plugins` 项目改为引用 `Infrastructure`，`OpenAI` 包引用移至 `Infrastructure`。
  - 验证：`PluginLoadContext` 优先从默认 ALC 解析 `FlowEngine.Infrastructure`，插件层引用不破坏运行时加载。
- 新增 `Infrastructure/Ai/SystemLlmClientFactory.cs`：从 `AiOptions` 创建 `ILlmClient`，缺 ApiKey/非法 BaseEndpoint 抛友好 `InvalidOperationException`。
- `Host/ServiceCollectionExtensions.cs`：`Configure<AiOptions>` + 单例注册 `ILlmClient`（懒加载，配置缺失仅在使用时抛错，不阻塞宿主启动）。
- `Host/appsettings.json`：新增 `Ai` 节点（ApiKey 留空，运行时经环境变量 `Ai__ApiKey` 注入）。
- 测试：`tests/FlowEngine.Application.Tests/Ai/SystemLlmClientFactoryTests.cs`（6 项：合法/自定义端点/Null/空Key/空白Key/非法端点）；`LlmNodeTests` 补 `Infrastructure` 引用解析迁移后的 `OpenAiLlmClient`。
- 验证：`dotnet build FlowEngine.sln` 通过；`dotnet test FlowEngine.sln` 全绿（Core 149 / Application 265 / Runtime 259 / Host 96）。

### 2026-07-11 阶段二 WorkflowDraftValidator
- 新增 `Application/Workflows/WorkflowDraftValidator.cs`：`ValidateAsync(JsonNode draft)`，校验结构/节点类型/端口方向/连接完整性/必填参数，并异步校验 DSL 中引用的凭据存在性（via `ICredentialAccessor.GetCredentialByNameAsync`）。
- 构造依赖对齐固化结论 `(INodeRegistry, ICredentialService)`：代码库实际凭据契约为 `ICredentialAccessor`（按名称查询），故注入 `ICredentialAccessor`；`Validate` 因凭据查库为异步，命名为 `ValidateAsync`。
- 校验范围覆盖：name 非空、nodes 非空数组、connections 为数组、节点 id/typeName 合法、≥1 入口节点、未知类型、必填参数、连接悬空节点、源端口须 Output/目标端口须 Input、凭据参数（CredentialType）与 `$credentials.<name>` 表达式引用存在性。
- 测试：`tests/FlowEngine.Application.Tests/Workflows/WorkflowDraftValidatorTests.cs`（13 项：合法/缺名/空nodes/非对象/未知类型/缺必填/无入口/悬空连接/源端口方向错/目标端口非Input/凭据参数缺失/表达式凭据缺失/表达式凭据存在）。
- 验证：Application.Tests 278 passed。

### 2026-07-11 阶段三 WorkflowGenerationService + PromptTemplates
- 新增 `Application/Workflows/PromptTemplates.cs`：系统 Prompt（DSL 结构约束 + 实时节点类型清单 + 钉钉配方 Few-shot + 示例）、纠错 Prompt、节点类型紧凑序列化。
- 新增 `Application/Workflows/WorkflowGenerationService.cs`：`GenerateAsync` 构造 Prompt → 调 `ILlmClient.ChatAsync` → 解析 JSON（去 markdown 围栏）→ `WorkflowDraftValidator` 校验 → 纠错循环（最多 `AiOptions.MaxRetries` 次重试，默认 3）。
- `AiOptions` 新增 `MaxRetries`（默认 3），补全计划 §阶段一与 §阶段三 的不一致。
- `Host/ServiceCollectionExtensions.cs`：注册 `AiOptions` 为可解析单例；注册 `WorkflowDraftValidator`/`WorkflowGenerationService`（Scoped）。
- 测试：`WorkflowGenerationServiceTests.cs`（6 项：首次成功/纠错成功/重试耗尽/空描述/非 JSON/LLM 异常）。
- 验证：Application.Tests 284 passed；`dotnet test FlowEngine.sln` 全绿（Core 149 / Application 284 / Runtime 259 / Host 96）。

### 2026-07-11 阶段四 后端 generate 端点 + 阶段五 CLI generate（含回归修复）
- `Host/Controllers/WorkflowsController.cs`：注入 `WorkflowGenerationService`，新增 `[HttpPost("generate")]` + `[AuthorizePermission(Scope.Workflow, Operation.Write)]` 的 `Generate` action（复用 `WorkflowGenerationRequest` DTO）。
- 测试：`tests/FlowEngine.Host.Tests/Workflows/WorkflowGenerateEndpointTests.cs`（4 项：Editor→200+valid / 空描述→200+invalid / 未认证→401 / Viewer→403，含 FakeLlmClient/FakeNodeRegistry/FakeCredentialAccessor）。
- **回归根因与修复**：`WorkflowGenerationService` 原注入 `ILlmClient`，导致每次访问 `/api/v1/workflows/*`（构造控制器时）都会解析 LLM 客户端；测试环境 `Ai:ApiKey` 为空，`SystemLlmClientFactory.Create` 抛异常 → 全部端点 500。
  - 改为注入 `Func<ILlmClient>` 延迟解析，仅在 `GenerateAsync` 真正调用时解析，把"配置缺失"异常推迟到使用 generate 端点时（符合设计意图）。
  - MS DI 当前版本不自动支持 `Func<T>` 注入，故在 `ServiceCollectionExtensions` 显式注册 `services.AddSingleton<Func<ILlmClient>>(...)` 以满足构建期校验。
  - 修复后 `dotnet test FlowEngine.sln` 全绿（Core 149 / Host 100 / Application 284 / Runtime 259）。
- CLI（`cli/src`）：`types.ts` 新增 `WorkflowGenerationRequestDto`/`WorkflowGenerationResponseDto`；`commands/workflows.ts` 新增 `workflowGenerate`（--description 必填、--output、--create、--project-id，JSON 与交互模式，`--create` 时询问确认后创建工作流）；`index.ts` 注册 `workflow generate` 子命令。
- 测试：`cli/src/__tests__/workflows-commands.test.ts` 新增 4 项（`workflow generate` 描述：JSON 输出 / 输出文件 / --create 创建 / 无效草案报错不创建）。
- 验证：`npm run build` 通过；`npm run test` 147 passed。
