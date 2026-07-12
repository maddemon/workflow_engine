# 开发计划：收敛插件节点中越界的职责（plan-cleanup-02-plugin-node-concerns）

> 说明：本计划涉及的文件行号可能随开发漂移，下文一律以**代码内容**描述定位，不依赖硬编码行号。所有定位均来自对 `plugins/FlowEngine.Plugins.Standard/**` 与 `backend/FlowEngine.Core/Entities/NodeExecutionContext.cs` 的静态核查。

## 1. 概述

插件节点当前承担了三类本不属于"节点"的职责：

1. **通用样板**：每个节点重复实现的输入获取、结果构造、JSON 解析/深拷贝、取消与异常包装。这些应下沉到 `NodeExecutionContext` 或 `Core` 公共方法。
2. **安全 / 基础设施横切关注点**：节点直接拉取并解密凭据、自行做 SSRF 预检、自行管理 HTTP 客户端池与鉴权头。这些应由上层（Runtime/Application）注入或统一拦截，节点只消费已解析好的能力。
3. **跨节点重复的领域工具 / 执行管道**：DB 连接/事务管理、HTTP 执行管道，应抽成共享服务，节点只描述"做什么"（子工作流加载已在 Core 正确抽象 `IWorkflowLoader`，不属此列）。

**当前越界点分布（共 11 项，按风险从低到高）：**

| # | 越界职责 | 当前位置（代表） | 应归属 | 风险 |
|---|----------|------------------|--------|------|
| 1 | 输入批次获取样板（重复 21 处） | `FilterNode`/`SortNode`/`SetNode`/`LoopNode`/`DbUpsertNode`/`DataQualityNode`/`JSNode`/`IfNode` 等 | `NodeExecutionContext.GetInputBatch(portName)` | 低 |
| 2 | 错误码目录散落、消息不统一 | `JSNode`/`HttpNodeExecution`/`PaginateNode`/`WebSearchToolNode`/`DbUpsertNode` 等 | `FlowConstants.ErrorCodes` 集中常量 | 低 |
| 3 | JSON 深拷贝是通用工具 | `DataQualityNode.DeepCopy` | `Core` 的 `JsonNodeExtensions.DeepClone()` | 低 |
| 4 | JSON 解析 + 错误包装重复 | `DataQualityNode`/`SubWorkflowToolNode` | `NodeExecutionContext.TryParseJson(...)` 含泛型重载 | 低 |
| 5 | 结果构造样板（手写 `DataBatch`/`DataItem`） | 多数节点 | `context.Ok(data)` / `context.Ok(batch)` | 低 |
| 6 | 取消/异常 → ErrorResult 包装重复 | `JSNode`/`CodeSnippetToolNode`/`WaitNode`/`WebSearchToolNode`/`ShellToolNode`/`PaginateNode`/`HttpNodeExecution` | `context.CatchToResult(...)` 或 `ToErrorResult(Exception)` 轻量映射 | 低 |
| 7 | 节点直接拉取并解密凭据 | `LlmNode`/`OAuth2Node`/`PaginateNode`/`WebSearchToolNode` | 统一 `ResolveCredentialAsync` 返回完整 `CredentialValue?`；`ResolveApiKeyAsync` 为便捷重载；删除 `LlmNode` 私有 `ResolveApiKeyAsync` 重复方法 | 中 |
| 8 | SSRF 预检重复 | `LlmNode`/`HttpNodeExecution`/`PaginateNode`/`WebSearchToolNode` | `context.GuardSsrf(url)` 或统一 HTTP 预检 | 中 |
| 9 | HTTP 客户端池 + 鉴权头重复 | `HttpNodeExecution`（中心）/`PaginateNode`/`WebSearchToolNode` | 共享 HTTP 执行服务 | 中 |
| 10 | 子工作流 Inline JSON 反序列化 + EmptyWorkflow 校验留在节点内 | `SubWorkflowToolNode`（Inline 路径仅 20 行） | 复用现有 `IWorkflowLoader`（已在 Core）；仅将 Inline 解析错误码与 `EmptyWorkflow` 校验下沉 | 低 |
| 11 | DB 连接/事务管理（节点自管） | `DbUpsertNode` | 共享 `DbExecutor`（plugins 内，复用现有 `Data/` 设施） | 高 |

**不覆盖范围：**

- 不改动节点真正的业务逻辑：数据质量规则、过滤/排序/去重/聚合、If/Switch 条件求值、LLM prompt 组装、表达式求值、SQL 方言生成（已在 `Data/` 正确抽离）。
- 不改动节点专属参数校验的*规则*（如 `Mode must be upsert/insert/update`），仅统一其*返回机制*。
- 阶段三（#9/#11）为执行管道级重构，建议单独排期、单独 Code Review；#10（子工作流 Inline 下沉）范围已缩小，可在阶段二后提前做。
- 本计划不关注已经在 Core 层正确实现的 `IWorkflowLoader` 与 `SubWorkflowExecutor` 职责边界——DB 加载/执行分离已成立。

## 2. 交付物清单

| 交付物 | 类型 |
|--------|------|
| `NodeExecutionContext.GetInputBatch(portName = Input)` 扩展输入获取 | 代码（Core） |
| `FlowConstants.ErrorCodes` 共享错误码集中常量；节点私有 code 保持原样 | 代码（Core + plugins） |
| `JsonNodeExtensions.DeepClone()`，删除 `DataQualityNode.DeepCopy` | 代码（Core + plugins） |
| `NodeExecutionContext.TryParseJson(...)` + 泛型 `TryParseJson<T>(...)` 重载 | 代码（Core） |
| `NodeExecutionContext.Ok(data)` / `Ok(batch)` 结果构造 | 代码（Core） |
| `NodeExecutionContext.CatchToResult(...)` 简单节点用；`ToErrorResult(this Exception)` 轻量映射供 DbUpsertNode 等有事务的节点用 | 代码（Core） |
| 凭据解析统一入口：强化 `ResolveApiKeyAsync`，新增 `ResolveCredentialAsync` 返回 `CredentialValue?`（完整值对象）；节点只能访问已解析的字段，不接触 `ICredentialAccessor`；删除 `LlmNode` 私有 `ResolveApiKeyAsync` 重复方法 | 代码（Core + plugins） |
| `NodeExecutionContext.GuardSsrf(url)` 或统一 HTTP 预检，节点不再直接 `SsrfGuard.IsInternalTarget` | 代码（Core + plugins） |
| 共享 HTTP 执行服务（客户端池 + 鉴权头），收敛 `PaginateNode`/`WebSearchToolNode` 重复 | 代码（Core/Runtime + plugins） |
| 子工作流 Inline JSON 反序列化与 `EmptyWorkflow` 校验下沉（复用现有 `IWorkflowLoader`；`SubWorkflowExecutor` 不移） | 代码（Core + plugins） |
| `DbExecutor` 共享执行管道（放在 `plugins/FlowEngine.Plugins.Standard/`，复用现有 `Data/` 下的 `DbConnectionFactory` 与 dialects），收敛 `DbUpsertNode` 连接/事务管理 | 代码（plugins） |
| `dotnet build` + `dotnet test` 全绿；相关节点测试补充"使用公共方法"的回归用例 | 验证 |

## 3. 开发阶段

### 阶段一：通用样板下沉到 Context / Core（低风险，高收益）

**目标：** 消除节点内重复的输入获取、结果构造、JSON 工具、异常包装样板。

- **1.1 `GetInputBatch`**：在 `NodeExecutionContext` 新增
  `public DataBatch GetInputBatch(string portName = FlowConstants.PortNames.Input) => Inputs.TryGetValue(portName, out var b) ? b : new DataBatch();`
  将 21 处 `context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) ? batch : new DataBatch()` 改为 `context.GetInputBatch()`；多端口节点（`MergeNode` 的 `Input1`/`Input2`）改用带参重载。
  **⚠ 保留 `Items.Count` 判断语义**：部分节点用 `batch.Items.Count > 0` / `== 0` 做"有/无输入早返回"分支（如 `CalculatorToolNode`/`WebSearchToolNode`/`ThinkToolNode`/`SubWorkflowToolNode`/`AgentNode` 等）。迁移后 `context.GetInputBatch().Items.Count == 0` 显式保留该判断逻辑。grep 验收只保证 `TryGetValue(FlowConstants.PortNames.Input` 消灭，不消灭 `Items.Count`。
- **1.2 `Ok` / `Ok(batch)`**：在 `NodeExecutionContext` 新增成功结果构造（复用现有 `CreateSingleResult` 思路，并补充批量重载），将节点内手写的 `new NodeExecutionResult { Output = new DataBatch { Items = [new DataItem {...}] } }` 收敛为 `context.Ok(data)` / `context.Ok(batch)`。
- **1.3 `JsonNodeExtensions.DeepClone()`**：在 `Core` 新增静态扩展（替代 `DataQualityNode.DeepCopy` 的序列化 round-trip 实现），删除 `DataQualityNode.DeepCopy` 私有方法并改用公共扩展。
- **1.4 `TryParseJson`**：在 `NodeExecutionContext` 新增两个重载：
  - `bool TryParseJson(string raw, out JsonDocument doc, out string? errorCode)` — 支持 `DataQualityNode` 的 `JsonDocument.Parse` 场景（注意 `JsonDocument` 须调用方 Dispose）。
  - `bool TryParseJson<T>(string raw, out T? result, out string? errorCode, JsonSerializerOptions? opts = null)` — 支持 `SubWorkflowToolNode` 以 `JsonSerializer.Deserialize<Workflow>` 解析强类型。
  内部统一包 try/catch 返回标准错误码。注意 `SubWorkflowToolNode` 的 Inline 路径是否使用该泛型重载**由实施者按实际情况决定**——若 `Deserialize<Workflow>` 与标准错误码差距很小也可保持原样，不作为硬性验收约束。
- **1.5 `CatchToResult` / `ToErrorResult`**：
  - 在 `NodeExecutionContext` 新增 `public NodeExecutionResult CatchToResult(Func<CancellationToken, Task<NodeExecutionResult>> exec, CancellationToken ct)`，统一捕获 `OperationCanceledException`→`"Cancelled"`、`ScriptErrorException`/`Exception`→对应 error code。用于**无资源清理**的简单节点（`JSNode`/`CodeSnippetToolNode`/`WaitNode`/`WebSearchToolNode`/`ShellToolNode`/`PaginateNode`/`HttpNodeExecution`）。
  - 新增 `public static NodeError ToErrorResult(this Exception ex, Guid nodeDefinitionId)` 轻量异常→错误映射（不包裹执行函数），供**有事务/资源清理**的节点（如 `DbUpsertNode`）在自己的 `catch` 体内直接调用，避免与 `using`/`finally` 生命周期冲突。
- **验收：** `dotnet build` 通过；`grep -rn "TryGetValue(FlowConstants.PortNames.Input" plugins` 结果为 0（多端口节点改用带参重载）；`grep -rn "new DataBatch" plugins` 仅剩必要的批量构造；`dotnet test` 通过。

### 阶段二：错误码集中化 + 安全横切关注点统一（中风险）

**目标：** 错误码成为常量；凭据解析与 SSRF 预检不再由节点直接执行。

- **2.1 `FlowConstants.ErrorCodes`（务实方案）**：
  - 不追求枚举全部 ~40+ 个错误码。只集中**明确跨节点共享**的高频 code（如 `Cancelled`/`ScriptError`/`SsrfBlocked`/`HttpClientUnavailable`/`MissingUrl`/`UnexpectedError`/`Timeout`/`CodeError`/`MissingCode`/`MissingConnection` 等）。
  - 节点私有 code（如 `InvalidMode`/`DbError`/`MissingCredentialName`/`MissingAccessToken`/`MissingThought`/`MissingWorkflowId`/`SearchFailed`/`CalculationError` 等）保持原样，留在各自节点中。
  - 判定原则：出现在 2+ 节点中的 code → 集中；仅 1 个节点出现 → 私有。
  - 实施前用 `grep -rho 'ErrorResult("\([A-Za-z]*\)"' plugins/FlowEngine.Plugins.Standard/ | Sort-Object -Unique` 导出全量清单辅助判定。
- **2.2 凭据解析统一入口**：
  - 强化现有 `NodeExecutionContext.ResolveApiKeyAsync` 作为便捷重载（专取 apiKey secret）。
  - 新增 `ResolveCredentialAsync(string? idOrName, CancellationToken ct)`，返回完整 `CredentialValue?`（Core 层值对象）。`CredentialValue` 包含 `Type`/`Fields` 等字段，节点可读 `tokenType`/`expiresAt`/`username`/`baseUrl` 等非 secret 元数据，但**不接触** `ICredentialAccessor` 访问器。
  - `LlmNode`/`PaginateNode`/`WebSearchToolNode` 改为调用 `ResolveCredentialAsync` 或 `ResolveApiKeyAsync`，不再直接 `context.Credentials.GetCredentialAsync(...)`。
  - `OAuth2Node` 改为调用 `ResolveCredentialAsync` 取完整 `CredentialValue?`，继续读 `Fields["accessToken"]`/`["tokenType"]`/`["expiresAt"]` 构造输出（`OAuth2Node.cs:57-83`）。
  - **删除 `LlmNode` 私有 `ResolveApiKeyAsync` 方法（`LlmNode.cs:143-172`）**——该方法与 `NodeExecutionContext.ResolveApiKeyAsync` 完全重复。
- **2.3 SSRF 预检统一**：
  - 在 `NodeExecutionContext` 新增 `public NodeExecutionResult? GuardSsrf(string? url, string code = ErrorCodes.SsrfBlocked)`，内部调用 `SsrfGuard.IsInternalTarget` 并在命中时返回标准 `ErrorResult`。
  - `LlmNode`/`HttpNodeExecution`/`PaginateNode`/`WebSearchToolNode` 的 `if (SsrfGuard.IsInternalTarget(...)) return context.ErrorResult("SsrfBlocked", ...)` 改为 `var g = context.GuardSsrf(url); if (g is not null) return g;`。
  - **可选加固**（验收后可做）：SSRF 收敛后收紧 `SsrfGuard.IsInternalTarget` 的可见性（`public → internal`，或限制为仅通过 `GuardSsrf` 暴露），防止新节点绕过。
- **验收：** `dotnet build` + `dotnet test` 通过；`grep -rn "SsrfGuard.IsInternalTarget" plugins` 结果为 0；`grep -rn "Credentials.GetCredential" plugins` 结果为 0；错误码字面量仅出现在 `FlowConstants.ErrorCodes` 定义处。

### 阶段三：执行管道抽离（高风险，建议单独排期）

> 此阶段为执行管道级重构，横跨 Core/Runtime/Application 与多个节点，建议单独执行并二次 Code Review。

- **3.1 共享 HTTP 执行服务**：将 `HttpNodeExecution` 中"获取 HTTP 客户端池 → 应用鉴权头 → SSRF 预检 → 发送 → 统一错误"的能力抽为 `Core`/`Runtime` 的 `IHttpExecutionService`（复用现有 `SsrfGuard` 与客户端池）。`PaginateNode`/`WebSearchToolNode` 改为调用该服务，删除各自重复的客户端池检查与 `ApplyAuthHeaderAsync` 拼装。
- **3.2 Inline JSON 解析与 EmptyWorkflow 校验轻量下沉**：
  `IWorkflowLoader` 已在 `Core/Abstractions` 定义（`Task<Workflow?> LoadAsync(Guid, CancellationToken)`），`NodeExecutionContext.WorkflowLoader` 属性已存在，`SubWorkflowToolNode` DB 路径已使用 `context.WorkflowLoader.LoadAsync(...)`。**加载/执行已分离，大方向无需改动。**
  仅剩余的两处越界：
  1. **Inline 路径 JSON 解析**（`SubWorkflowToolNode.cs:118-128`）——`JsonSerializer.Deserialize<Workflow>` + try/catch。可考虑泛型 `TryParseJson<T>`，但该路径与节点语义高度绑定（解析后即传给 `SubWorkflowExecutor`），也可保留原样。
  2. **EmptyWorkflow 校验**（`SubWorkflowToolNode.cs:131-134`）——`workflow is null || workflow.Nodes.Count == 0`。可抽为 `WorkflowValidator.EnsureNonEmpty(Workflow?)` 公用方法（`Core` 层），供 SubWorkflowToolNode 和其他潜在加载点使用。
  3. `SubWorkflowToolNode` 的 `GetInputPayload`（`cs:164-172`）——是输入获取样板的重复，改用 1.1 的 `GetInputBatch`。
  **此子项工作量大降，可提前至阶段二后实施，不需等阶段三整体排期。**
- **3.3 `DbExecutor` 共享管道（放在 plugins 内）**：
  - `DbConnectionFactory`/`DbDialect`/`SqlGeneratorFactory` 等当前在 `plugins/FlowEngine.Plugins.Standard/Data/`，**不上移**。
  - 在 `plugins/FlowEngine.Plugins.Standard/Execution/`（或 `Data/` 同级）新增 `DbExecutor`，封装 `CreateConnection` → `BeginTransaction` → 参数化命令 → `RowExistsAsync` → `Commit`/`Rollback`，复用现有 `Data/` 设施。
  - `DbUpsertNode` 只描述表/列/模式与逐行值计算，连接与事务生命周期交给 `DbExecutor`。`Data/` 下 SQL 生成器保持不变。
- **验收：** `dotnet build` + `dotnet test` 全绿；`PaginateNode`/`WebSearchToolNode` 无独立客户端池/鉴权头逻辑；`SubWorkflowToolNode` 无直接 DB/JSON 解析；`DbUpsertNode` 无 `BeginTransaction`/`Rollback` 调用。

## 4. 阶段依赖图

```
阶段一 (通用样板)  ──> 阶段二 (错误码 + 凭据/SSRF 统一)
                                                │
                     ┌──────────────────────────┤
                     ▼                          ▼
              3.2 子工作流 Inline 下沉      3.1/3.3 HTTP/DB 执行管道
              (低风险，可提前)              (高风险，单独排期)
```

- 阶段一、二顺序执行（二依赖一提供的 `ErrorResult`/Context 方法）。
- 3.2（Inline 解析+EmptyWorkflow 校验下沉）范围已大幅缩小，可在阶段二后随时实施。
- 3.1（HTTP 执行服务）+ 3.3（DbExecutor）为执行管道级重构，建议单独排期、独立 Code Review。

## 5. 风险与待定项

| 风险 | 影响 | 应对 |
|------|------|------|
| `TreatWarningsAsErrors=true`：任何残留字面量/未用 using 会令 `dotnet build` 失败 | 编译阻断 | 每阶段改完即 `dotnet build`；最终 `grep` 双验证 |
| `GetInputBatch` 默认端口参数改动影响多端口节点 | `MergeNode` 等编译/行为错误 | 多端口节点显式传 `Input1`/`Input2`；改完跑 `MergeNode` 测试 |
| `GetInputBatch` 不覆盖 `Items.Count` 判断语义（`CalculatorToolNode`/`WebSearchToolNode`/`ThinkToolNode`/`SubWorkflowToolNode`/`AgentNode` 等有用空输入做早返回的分支） | 行为改变或重复判断被误删 | 1.1 已显式注明保留 `Items.Count` 判断；验收 grep 只消灭 `TryGetValue` 不消灭 `Items.Count` |
| 凭据解析统一入口改变 secret 获取路径 | LLM/WebSearch/OAuth2 节点运行期取不到 key | 阶段二前确认 `ResolveApiKeyAsync` 已覆盖 oauth2/apiKey 两种类型；补充凭据解析测试 |
| `CatchToResult` 包裹含有 `BeginTransaction`/`Rollback` 的 `DbUpsertNode` | `using`/`finally` 事务生命周期与 Func 包装冲突 | 1.5 已显式排除 DbUpsertNode 使用 `CatchToResult`，改用轻量 `ToErrorResult(this Exception)` helper |
| 阶段三 `DbExecutor` 改变事务边界 | upsert 原子性/回滚行为变化 | 保留 `RowExistsAsync`+事务语义；`DbUpsertNode` 测试覆盖 upsert/insert/update 与回滚 |
| 阶段三子工作流重构：Inline JSON 解析与 EmptyWorkflow 校验抽离后节点可读性下降 | 代码碎片化 | 保持 `Inline` 分支整体可读；若抽离得不偿失则保留原样（已注明非硬性验收约束） |
| 阶段三 HTTP 服务抽离可能改变超时/重试行为 | Paginate/WebSearch 行为漂移 | 保留现有超时与 `SuccessWhen` 表达式语义；补充端到端测试 |

## 6. 验收总标准

1. `grep -rn "TryGetValue(FlowConstants.PortNames.Input" plugins` 结果为 0（多端口节点改用带参 `GetInputBatch`；`Items.Count` 判断保留）。
2. `grep -rn "SsrfGuard.IsInternalTarget" plugins` 结果为 0（统一走 `GuardSsrf`）。
3. `grep -rn "Credentials.GetCredential" plugins` 结果为 0（统一走 `ResolveApiKeyAsync`/`ResolveCredentialAsync`）。
4. `grep -rn "new DataBatch" plugins` 仅剩必要的批量构造（单条结果改用 `context.Ok`）。
5. 集中化的共享错误码（`ErrorCodes.Cancelled`/`ErrorCodes.ScriptError`/`ErrorCodes.SsrfBlocked` 等）在 `plugins/**` 中**无字面量**；节点私有 code 可保留字面量（验收时 `grep` 跳过私有 code 清单，仅验证共享 code 确认已收敛）。
6. `dotnet build` 全 solution 通过（含 `TreatWarningsAsErrors`）。
7. `dotnet test` 全部通过；阶段一/二补充"使用公共方法"的回归用例。
8. 阶段三完成后：
   - `PaginateNode`/`WebSearchToolNode` 无独立客户端池/鉴权头逻辑。
   - `SubWorkflowToolNode` DB 路径已通过 `IWorkflowLoader` 加载（不变）；Inline 路径与 `EmptyWorkflow` 校验若选择下沉则已复用公共方法（非硬性验收 #8 约束，放宽为"不恶化"）。
   - `DbUpsertNode` 无 `BeginTransaction`/`Rollback` 直接调用。
