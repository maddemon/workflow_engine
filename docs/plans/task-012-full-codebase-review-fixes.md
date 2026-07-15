# 任务：全项目代码审查问题整改（task-012-full-codebase-review-fixes）

## 目标
落实 2026-07-15 全项目代码审查发现的问题，按 P0→P3 分阶段整改，消除安全漏洞、修复核心功能缺陷、补齐硬约束审计事件、提升性能与规范一致性。不新增功能，仅修复已确认问题。

## 审查来源
2026-07-15 全项目代码审查会话（5 个子 agent 并行扫描后端 Core/Runtime/Application/Host/Infrastructure、Plugins.Standard、前端、Tests，关键 Critical 已通过源码二次确认）。原始审查结论存于该会话历史，本任务文档为唯一整改依据。

## 整改范围
后端 Core/Runtime/Application/Host/Infrastructure/Migrations、Plugins.Standard、前端 React/TS、Tests 共 5 大模块，约 100 个问题（13 Critical / 48 Major / 39 Minor）。

## 编号体系
全文统一扁平编号：P0a-* / P0b-* / P0c-* / P0d-*（Critical，按子任务分组）/ P1-1 ~ P1-30（Major）/ P2-1 ~ P2-21（Major 性能与测试）/ P3-1 ~ P3-25（Minor）。

## 与 task-006 的执行顺序
[task-006-code-quality-refactor.md](task-006-code-quality-refactor.md) 剩余未完成项（A3 GetInputBatch 扩展、B1 DbUpsertNode 枚举化、C1 余下 AgentNode/OpenAiLlmClient/ShellToolNode）与本任务有文件重叠：
- task-006 B1（DbUpsertNode 枚举化）↔ 本任务 P1-18（DbUpsertNode TimeoutSeconds）：同文件不同问题，**本任务 P1-18 应在 task-006 B1 完成后执行**
- task-006 A1 已完成（5 节点 GetFieldValue 替换），与本任务 P1-19（MergeNode 重复键）无直接重叠
- 其余无重叠项可独立执行

---

## 实施规范（所有 P0 项通用，弱 AI 必须遵守）

### 1. 行号失效处理
**行号会随修改变化，实施时不要死磕旧行号。** 每项的「定位锚点」给出**方法名 + 关键代码片段**，用 Grep 或 IDE 搜索定位当前实际位置。

### 2. TDD 顺序
项目规则要求 TDD（先写测试再改实现）。每项标注「TDD 顺序」字段：
- **先测试**：先写/改测试用例（确认失败）→ 改源码 → 跑测试（确认通过）
- **先源码**：先改源码 → 后补回归测试（用于 SSRF 等难以单测的场景）

### 3. 每项改完立即 commit
**改完一项立即 commit**，commit message 规范：`fix(task-012): P0a-1 SSRF 攻击链修复`。失败时用 `git restore <具体文件>` 回滚该项，不要批量回滚。

### 4. 验证命令
每项给「验证」字段。后端统一用 `dotnet build FlowEngine.sln`；前端用 `npm run build` + `npm run typecheck`；测试用 `dotnet test --filter "FullyQualifiedName~XxxTests"` 精确跑某测试类。

### 5. 风险红线
每项「风险红线」标注遇到何种情况立即停止并询问用户，不要擅自决策。

### 6. 参照定位
每项「参照」给出参照文件的具体行号 + 关键代码片段。**先读参照，再动手**。

### 7. 完成判定
每项「完成判定」是 3-6 条可勾选项，全部满足才算完成。

### 8. 不擅自扩散
只改该项列出的文件。若发现关联问题，记录到「主要修改记录」但不擅自修复，留给对应编号项处理。

---

## 待完成项

# P0 — 立即修复（Critical，13 项，拆为 4 个子任务）

## P0a — 后端安全与调度（2 项）

### P0a-1 SSRF 攻击链修复（3 处合并）

**前置依赖**：无
**TDD 顺序**：先源码，后补集成测试（SSRF 难以单测，靠代码审查 + 集成测试覆盖）

**定位锚点**：
- `backend/FlowEngine.Core/Http/SsrfGuard.cs` 的 `IsInternalTarget(string host)` 方法，关键代码 `Dns.GetHostAddresses(host)`
- `backend/FlowEngine.Runtime/Credentials/OAuth2TokenService.cs` 的 `RequestTokenAsync` 方法，关键代码 `var url = request.TokenUrl;` 后紧跟 `using var httpRequest = new HttpRequestMessage(httpMethod, url);`
- `backend/FlowEngine.Runtime/Http/HttpClientPool.cs` 的 `GetClient` 方法，关键代码 `return _httpClientFactory.CreateClient()`

**修复步骤**：
1. **SsrfGuard.cs**：保留 `IsInternalTarget` 作为公开校验入口；新增 `internal static HttpClientHandler CreateSsrfSafeHandler()` 或在 `SsrfGuard` 上提供 `ConnectCallback` 工厂方法，内部一次性解析 IP 并 pin 住。`SocketsHttpHandler.ConnectCallback` 签名：`Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>`，在 callback 内 `Dns.GetHostAddresses` 后校验每个 IP 是否 internal，通过则用 `new Socket(...)` 连接该 IP。
2. **HttpClientPool.cs**：`GetClient` 内用 `_httpClientFactory.CreateClient()` 后，配置 handler 禁用自动重定向（或在 DI 注册时 `services.ConfigureHttpClientDefaults(c => c.ConfigurePrimaryHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = SsrfGuard.CreateConnectCallback() }))`）。若手动处理重定向，每跳重新调 `SsrfGuard.IsInternalTarget`。
3. **OAuth2TokenService.cs**：在 `using var httpRequest = new HttpRequestMessage(httpMethod, url);` **之前** 加 `if (SsrfGuard.IsInternalTarget(new Uri(request.TokenUrl).Host)) throw new BusinessException("OAuth2 token URL 指向内部地址，已被 SSRF 防护拦截");`；同时把 L155/L161 的 `$"...：{Truncate(responseBody)}"` 改为 `$"...：HTTP {(int)response.StatusCode}"`（仅留状态码，移除响应体）。

**参照**：无（已是源头修复）

**验证命令**：
- `dotnet build FlowEngine.sln`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~OAuth2TokenServiceTests"`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~HttpClientPool"`

**完成判定**：
- [ ] SsrfGuard 提供 ConnectCallback 方式 pin IP
- [ ] HttpClientPool 禁用 AllowAutoRedirect
- [ ] OAuth2TokenService 入口校验 TokenUrl
- [ ] OAuth2TokenService 异常消息不含 responseBody
- [ ] dotnet build 通过
- [ ] OAuth2TokenServiceTests 全绿

**风险红线**：
- 若 `SocketsHttpHandler.ConnectCallback` 在 net10.0 不可用（应可用，.NET 5+ 支持），立即停止询问
- 若现有 OAuth2 测试因 SSRF 校验失败（如测试用了 `localhost`），改测试用 `httpbin.org` 或 mock SsrfGuard，**不要移除 SSRF 校验**

**回滚**：`git restore backend/FlowEngine.Core/Http/SsrfGuard.cs backend/FlowEngine.Runtime/Credentials/OAuth2TokenService.cs backend/FlowEngine.Runtime/Http/HttpClientPool.cs`

---

### P0a-2 调度系统失效修复

**前置依赖**：无
**TDD 顺序**：先源码，后补集成测试

**定位锚点**：
- `backend/FlowEngine.Host/ServiceCollectionExtensions.cs` 第 220 行 `services.AddSingleton<IScheduleManager, QuartzScheduleManager>();`
- `backend/FlowEngine.Host/Scheduling/QuartzScheduleManager.cs`：
  - L12 `public sealed class QuartzScheduleManager : IScheduleManager, IHostedService, IDisposable`
  - L16 `private IScheduler? _scheduler;`
  - L30-35 `public async Task StartAsync(...)` 内 `_scheduler = await _schedulerFactory.GetScheduler(...); await _scheduler.Start(...)`
  - L57-61 `RegisterScheduleAsync` 内 `if (_scheduler is null) { _logger.LogWarning("调度器未启动..."); return; }`
  - L109/142/182 同样的 null 检查

**症状**：`AddSingleton<IScheduleManager, QuartzScheduleManager>()` 不触发 IHostedService 生命周期，`StartAsync` 永不被调用 → `_scheduler` 永为 null → 所有 Register 方法走 warning return 分支，触发器静默不注册。`AddQuartzHostedService` 启动的是 `QuartzHostedService` 自己获取的 scheduler，不会注入到 `QuartzScheduleManager._scheduler` 字段。

**修复步骤**：
1. 删除 `QuartzScheduleManager` 的 `: IHostedService, IDisposable` 实现，仅保留 `: IScheduleManager`
2. 删除 `_scheduler` 字段、`StartAsync`、`StopAsync`、`Dispose` 方法
3. 在每个使用 `_scheduler` 的方法（`RegisterScheduleAsync`/`UnregisterScheduleAsync`/`GetNextFireTimeAsync`/`RegisterPollTriggerAsync`/`UnregisterPollTriggerAsync`）开头改为 `var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);`（Quartz 的 `GetScheduler` 幂等返回同一实例，已由 `QuartzHostedService` 启动）
4. 删除原 `if (_scheduler is null)` 的 warning return 分支
5. **不要** 改 `ServiceCollectionExtensions.cs#L220` 的注册方式（保持 `AddSingleton<IScheduleManager, QuartzScheduleManager>()`，`AddQuartzHostedService` 仍负责 scheduler 生命周期）

**参照**：无

**验证命令**：
- `dotnet build FlowEngine.sln`
- `dotnet test tests/FlowEngine.Host.Tests`
- 启动应用，注册一个 Schedule 触发器，确认日志出现 "已注册定时触发器" 而非 "调度器未启动"

**完成判定**：
- [ ] QuartzScheduleManager 仅实现 IScheduleManager
- [ ] 删除 _scheduler 字段与 StartAsync/StopAsync/Dispose
- [ ] 每个 Register 方法改为懒加载 `await _schedulerFactory.GetScheduler(ct)`
- [ ] dotnet build 通过
- [ ] 现有 Host.Tests 全绿
- [ ] 手动验证 trigger 注册成功

**风险红线**：
- 若 `GetScheduler` 在 scheduler 未启动时返回未启动的实例（需查 Quartz 文档确认），改为 `var scheduler = await _schedulerFactory.GetScheduler(ct); if (!scheduler.IsStarted) await scheduler.Start(ct);`
- 若删除 IHostedService 后有其他代码依赖 `QuartzScheduleManager.StartAsync`（grep 确认），立即停止询问

**回滚**：`git restore backend/FlowEngine.Host/Scheduling/QuartzScheduleManager.cs`

---

## P0b — 插件节点（4 项）

### P0b-1 SubAgent DTO 一致性

**前置依赖**：无
**TDD 顺序**：先改测试（P0d-1 已覆盖 SubAgentToolNode，先补 DTO 断言）→ 后改源码

**定位锚点**：
- `plugins/FlowEngine.Plugins.Standard/SubAgentToolNode.cs` 的 `ExecuteAsync` 方法，关键代码（在 `case Core.Agent.InlineResolverStopReason.Completed:` 分支内）：
  ```
  return new NodeExecutionResult
  {
      Success = true,
      Output = new DataBatch { Items = [ new DataItem { Data = result.Content, ... } ] }
  };
  ```

**修复步骤**：
1. 读参照文件 `plugins/FlowEngine.Plugins.Standard/AgentNode.cs#L244-274` 的 `CreateSuccessResult` 方法，复制其 DTO 构造逻辑
2. 在 SubAgentToolNode 的 Completed 分支，把 `Data = result.Content` 改为构造 `AgentExecutionResultDto` 后序列化：
   ```csharp
   var dto = new AgentExecutionResultDto
   {
       AgentInfo = new AgentExecutionInfoDto
       {
           Model = context.LlmClient?.ModelName ?? "unknown",
           IterationCount = result.Iterations.Count,
           Status = "Completed",
           CompletedAt = DateTime.UtcNow,
       },
       Iterations = result.Iterations,
       SubRecords = new List<SubRecordDto>(),
   };
   // Data = JsonSerializer.SerializeToNode(dto, JsonDefaults.Options)
   ```
3. 添加必要的 using：`FlowEngine.Core.Dtos`、`System.Text.Json`、`FlowEngine.Core`

**参照**：
- [AgentNode.cs#L244-274](file:///d:/Repos/flow_engine/plugins/FlowEngine.Plugins.Standard/AgentNode.cs#L244-L274) 的 `CreateSuccessResult` 方法
- [AgentDtos.cs#L188-209](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Dtos/AgentDtos.cs#L188-L209) 的 `AgentExecutionResultDto` 定义

**验证命令**：
- `dotnet build FlowEngine.sln`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~SubAgentToolNodeTests"`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~AgentNodeDtoTests"`

**完成判定**：
- [ ] SubAgentToolNode Completed 分支返回 AgentExecutionResultDto 序列化结果
- [ ] 引入 using 正确
- [ ] dotnet build 通过
- [ ] SubAgentToolNodeTests 全绿（含 P0d-1 补的 parentRecordId 断言）
- [ ] 前端 NodeOutputList.tsx 仍能正确渲染（手动验证）

**风险红线**：
- 若前端 NodeOutputList.tsx 期望 `result.Content` 而非 DTO，需同步改前端（但 P0d-1 已说明前端已直接消费 AgentExecutionData，应兼容）

**回滚**：`git restore plugins/FlowEngine.Plugins.Standard/SubAgentToolNode.cs`

---

### P0b-2 SubWorkflowExecutor 并发隐患

**前置依赖**：先读 `backend/FlowEngine.Runtime/Registry/NodeRegistry.cs` 确认节点注册方式
**TDD 顺序**：先写并发测试（确认失败）→ 改源码 → 跑测试通过

**定位锚点**：
- `plugins/FlowEngine.Plugins.Standard/SubWorkflowExecutor.cs` 的 `HydrateParameters` 方法（约 L67），关键代码：反射设置 `nodeType` 的属性
- `backend/FlowEngine.Runtime/Registry/NodeRegistry.cs`（需先读确认注册方式）

**修复步骤**：
1. 先读 `NodeRegistry.cs`，找 `Register` 或 `TryGet` 方法，确认节点实例是 Singleton 还是每次新建
2. **若 Singleton**（最可能）：
   - 方案 A（推荐）：将 `HydrateParameters` 的参数注入 `NodeExecutionContext.ResolvedParameters`（已有字典），节点 ExecuteAsync 从 `context.ResolvedParameters` 读参数（已是大部分节点做法）。删除反射 `SetValue` 调用
   - 方案 B（备选）：`NodeRegistry` 改为 `CreateInstance(typeName)` 工厂方法，每次返回新实例
3. **若 Transient**（每次新建）：方案 A 仍推荐做（语义清晰），但可暂缓
4. 写并发测试：两个 SubWorkflow 同时执行同一类型节点、参数不同，断言互不影响

**参照**：
- [SubAgentToolNode.cs#L175-195](file:///d:/Repos/flow_engine/plugins/FlowEngine.Plugins.Standard/SubAgentToolNode.cs#L175-L195) 的 `ResolveMaxNestingDepth` 方法（从 `context.ResolvedParameters` 读参数的范例）

**验证命令**：
- `dotnet build FlowEngine.sln`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~SubWorkflow"`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~SubWorkflowExecutor"`

**完成判定**：
- [ ] 已确认 NodeRegistry 节点生命周期
- [ ] HydrateParameters 不再通过反射改节点实例属性
- [ ] 参数改为注入 context.ResolvedParameters（方案 A）或 NodeRegistry 改工厂（方案 B）
- [ ] 新增并发测试覆盖（两 SubWorkflow 同时执行同类型节点）
- [ ] dotnet build + 测试全绿

**风险红线**：
- 若方案 A 涉及大量节点改动（grep 发现很多节点直接读实例属性而非 context），立即停止询问，改用方案 B
- 若 NodeRegistry 是 Singleton 且无 CreateInstance 接口，需先改 NodeRegistry，影响面大，立即询问

**回滚**：`git restore plugins/FlowEngine.Plugins.Standard/SubWorkflowExecutor.cs backend/FlowEngine.Runtime/Registry/NodeRegistry.cs`

---

### P0b-3 WebSearch 功能损坏

**前置依赖**：无
**TDD 顺序**：先改测试（断言 cx != apiKey）→ 后改源码

**定位锚点**：
- `plugins/FlowEngine.Plugins.Standard/WebSearchToolNode.cs#L202`（Google 分支），关键代码 `key={apiKey}&cx={apiKey}`

**修复步骤**：
1. 在 WebSearchToolNode 类增加 `[Description("Google Programmable Search Engine ID (cx)")] public string SearchEngineId { get; set; } = string.Empty;`
2. L202 的 URL 改为 `key={Uri.EscapeDataString(apiKey)}&cx={Uri.EscapeDataString(SearchEngineId)}`
3. 修复测试：断言 cx 字段等于 SearchEngineId 而非 apiKey

**参照**：无

**验证命令**：
- `dotnet build FlowEngine.sln`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~WebSearchToolNode"`

**完成判定**：
- [ ] 新增 SearchEngineId 属性
- [ ] URL cx 使用 SearchEngineId 而非 apiKey
- [ ] apiKey 经 Uri.EscapeDataString
- [ ] 测试断言 cx 正确
- [ ] dotnet build + 测试全绿

**风险红线**：无

**回滚**：`git restore plugins/FlowEngine.Plugins.Standard/WebSearchToolNode.cs tests/FlowEngine.Runtime.Tests/Plugins/WebSearchToolNodeTests.cs`

---

### P0b-4 SubWorkflow 嵌套深度限制

**前置依赖**：无
**TDD 顺序**：先写测试（构造深度超过限制的调用，断言返回 MaxNestingDepthExceeded 错误）→ 后改源码

**定位锚点**：
- `plugins/FlowEngine.Plugins.Standard/SubWorkflowToolNode.cs#L84-157` 的 `ExecuteAsync` 方法

**修复步骤**：
1. 读参照 [SubAgentToolNode.cs#L16-19,49,78-84,175-195](file:///d:/Repos/flow_engine/plugins/FlowEngine.Plugins.Standard/SubAgentToolNode.cs#L16-L195) 完整 NestingDepth 机制
2. 在 SubWorkflowToolNode 类加常量：`private const int DefaultMaxNestingDepth = 5;` `private const int MinMaxNestingDepth = 1;` `private const int MaxMaxNestingDepth = 20;`
3. 加属性：`[Description("Maximum nesting depth to prevent infinite recursion.")] public int MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;`
4. 在 ExecuteAsync 入口加深度检查：
   ```csharp
   var effectiveMaxNestingDepth = ResolveMaxNestingDepth(context);
   if (context.NestingDepth >= effectiveMaxNestingDepth)
   {
       return context.ErrorResult("MaxNestingDepthExceeded", $"SubWorkflow nesting depth {context.NestingDepth} exceeds maximum allowed depth of {effectiveMaxNestingDepth}.");
   }
   ```
5. 复制 SubAgentToolNode 的 `ResolveMaxNestingDepth` 私有方法到 SubWorkflowToolNode
6. 确认 `NodeExecutionContext.NestingDepth` 属性存在（应已存在，SubAgentToolNode 在用）
7. 确认调用 SubWorkflowExecutor 时传入的 context 已正确设置 NestingDepth（递增）

**参照**：
- [SubAgentToolNode.cs#L16-19,49,78-84,175-195](file:///d:/Repos/flow_engine/plugins/FlowEngine.Plugins.Standard/SubAgentToolNode.cs#L16-L195) 完整 NestingDepth 机制（常量/属性/入口检查/解析方法）

**验证命令**：
- `dotnet build FlowEngine.sln`
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~SubWorkflowToolNode"`

**完成判定**：
- [ ] SubWorkflowToolNode 加 MaxNestingDepth 属性与入口检查
- [ ] ResolveMaxNestingDepth 方法实现（含范围校验 [1, 20]）
- [ ] 新增测试覆盖：深度超过限制返回 MaxNestingDepthExceeded
- [ ] dotnet build + 测试全绿

**风险红线**：
- 若 `NodeExecutionContext.NestingDepth` 不存在，需先在 NodeExecutionContext 加该属性并在 SubWorkflowExecutor 内部递增，立即询问
- 若 SubWorkflowExecutor 未把 NestingDepth 透传给子节点 context，需改 SubWorkflowExecutor，立即询问

**回滚**：`git restore plugins/FlowEngine.Plugins.Standard/SubWorkflowToolNode.cs tests/FlowEngine.Runtime.Tests/Plugins/SubWorkflowToolNodeTests.cs`

---

## P0c — 前端实时数据（4 项）

### P0c-1 AuthContext 启动崩溃

**前置依赖**：无
**TDD 顺序**：先改源码（简单修复）→ 手动验证

**定位锚点**：
- `frontend/src/hooks/AuthContext.tsx#L25-28`，关键代码 `JSON.parse(stored)` 在 useState 初始化函数内

**修复步骤**：
1. 在 useState 初始化函数内包 try/catch：
   ```tsx
   const [authUser, setAuthUser] = useState<AuthUser | null>(() => {
     try {
       const stored = localStorage.getItem('auth_user');
       return stored ? JSON.parse(stored) as AuthUser : null;
     } catch {
       localStorage.removeItem('auth_user');
       return null;
     }
   });
   ```

**参照**：无

**验证命令**：
- `cd frontend; npm run build; npm run typecheck`
- 手动：在浏览器 DevTools 把 `localStorage.auth_user` 设为损坏 JSON，刷新页面应正常进入登录页

**完成判定**：
- [ ] JSON.parse 包 try/catch
- [ ] 失败时 removeItem 清理 + 返回 null
- [ ] npm run build + typecheck 通过
- [ ] 手动验证损坏 JSON 不崩溃

**风险红线**：无

**回滚**：`git restore frontend/src/hooks/AuthContext.tsx`

---

### P0c-2 executionMeta 不更新

**前置依赖**：无
**TDD 顺序**：先改源码 → 后补单测

**定位锚点**：
- `frontend/src/hooks/websocket/messageHandlers.ts#L126-156`，关键代码三个 handler：`execution_completed`/`execution_failed`/`execution_cancelled`，每个内仅 `setIsExecuting(false)`

**修复步骤**：
1. 找到这三个 handler
2. 每个 handler 在 `setIsExecuting(false)` 之外，额外调用 `setExecutionMeta` 或对应 store action，更新 finalStatus（completed/failed/cancelled）、error（如有）、completedAt
3. 先读 messageHandlers.ts 找到 `setExecutionMeta` 的来源（应在 useWebSocketExecution 注入）；若无此 setter，需在 useExecution store 加 `setFinalStatus` action

**参照**：无

**验证命令**：
- `cd frontend; npm run build; npm run typecheck`
- `cd frontend; npm test -- useWebSocketExecution`
- 手动：执行工作流，观察 ExecutionPanel 的 status badge 在完成后变为 Completed/Failed 而非停留 Running

**完成判定**：
- [ ] 三个 handler 均更新 executionMeta
- [ ] finalStatus/error/completedAt 字段正确写入
- [ ] npm run build + typecheck 通过
- [ ] 现有 useWebSocketExecution 测试全绿
- [ ] 手动验证 status badge 更新

**风险红线**：
- 若 `setExecutionMeta` 不存在且需在 store 加 action，影响面扩大，立即询问

**回滚**：`git restore frontend/src/hooks/websocket/messageHandlers.ts frontend/src/hooks/useWebSocketExecution.ts`

---

### P0c-3 SSE 无重连

**前置依赖**：无
**TDD 顺序**：先改源码 → 后补单测

**定位锚点**：
- `frontend/src/hooks/websocket/useSseFallback.ts#L35-39`，关键代码 SSE `onerror` 仅 `close()` + `setStatus('error')`

**修复步骤**：
1. 读参照 [useWebSocketConnection.ts#L28-32,70-74,97-99,110](file:///d:/Repos/flow_engine/frontend/src/hooks/websocket/useWebSocketConnection.ts#L28-L110) 完整重连模式（reconnectAttemptsRef、maxReconnectAttempts=5、指数退避 `reconnectInterval * Math.pow(2, attempts)`）
2. 在 useSseFallback 内复制同样模式：`reconnectAttemptsRef`、`maxReconnectAttempts`、`reconnectInterval`
3. `onerror` 内：若 attempts < max，setTimeout 重连并 attempts++；否则 setStatus('error') 并 close()
4. 成功连接时重置 attempts=0
5. 组件卸载或显式关闭时 clearTimeout 清理

**参照**：
- [useWebSocketConnection.ts#L28-32,70-74,97-99,110](file:///d:/Repos/flow_engine/frontend/src/hooks/websocket/useWebSocketConnection.ts#L28-L110) 重连模式实现

**验证命令**：
- `cd frontend; npm run build; npm run typecheck`
- `cd frontend; npm test -- useSseFallback`
- 手动：断网后恢复，SSE 应自动重连而非永久中断

**完成判定**：
- [ ] useSseFallback 实现指数退避重连
- [ ] 成功连接重置 attempts
- [ ] 卸载时 clearTimeout
- [ ] npm run build + typecheck 通过
- [ ] 手动验证断网重连

**风险红线**：无

**回滚**：`git restore frontend/src/hooks/websocket/useSseFallback.ts`

---

### P0c-4 SSE 多订阅丢失

**前置依赖**：P0c-3 完成
**TDD 顺序**：先改源码 → 后补单测

**定位锚点**：
- `frontend/src/hooks/websocket/useWebSocketConnection.ts#L75-78`，关键代码 `subscribedExecutionsRef.current.values().next().value`

**修复步骤**：
1. 改为遍历所有 subscribedExecutions：`for (const executionId of subscribedExecutionsRef.current) { trySseFallback(executionId); }`
2. 或维护一个 `Map<Guid, EventSource>`，每个 execution 一个 EventSource
3. 取消订阅时关闭对应 EventSource

**参照**：无

**验证命令**：
- `cd frontend; npm run build; npm run typecheck`
- `cd frontend; npm test -- useWebSocketExecution`
- 手动：同时订阅多个 execution，所有 execution 实时事件都收到

**完成判定**：
- [ ] 不再仅取第一个 executionId
- [ ] 多 execution 各自建立 EventSource
- [ ] 取消订阅时关闭对应 EventSource
- [ ] npm run build + typecheck 通过
- [ ] 手动验证多订阅

**风险红线**：
- 若同时多 EventSource 引发性能问题或浏览器连接数限制（HTTP/1.1 同源 6 个），立即询问是否改为单 WebSocket 复用

**回滚**：`git restore frontend/src/hooks/websocket/useWebSocketConnection.ts`

---

## P0d — 测试虚假覆盖修复（3 项，应在 P0a/P0b/P0c 完成后作为回归验证）

### P0d-1 SubAgentToolNode parentRecordId 测试断言

**前置依赖**：P0b-1 完成（SubAgentToolNode 已正确输出 DTO）
**TDD 顺序**：先改测试断言 → 跑测试确认通过

**定位锚点**：
- `tests/FlowEngine.Runtime.Tests/Agent/SubAgentToolNodeTests.cs#L27-105`，关键代码 `ExecuteAsync_Passes_NodeExecutionRecordId_As_ParentRecordId_When_Available` 测试方法，仅断言 `result.Success`/`callCount`/输出内容

**修复步骤**：
1. 在测试中捕获 InlineResolver 或 ToolExecutionRecorder 的调用（可用 mock 捕获 `Record` 调用参数，或读 `result.ToolExecutionRecords`）
2. 断言生成记录的 `ParentRecordId == 传入的 nodeExecutionRecordId`
3. 补一个用例：`NodeExecutionRecordId == Guid.Empty` 时回退到 `ExecutionId`，断言 `ParentRecordId == context.ExecutionId`

**参照**：
- [SubAgentToolNode.cs#L99-101](file:///d:/Repos/flow_engine/plugins/FlowEngine.Plugins.Standard/SubAgentToolNode.cs#L99-L101) 的 parentRecordId 解析逻辑
- [InlineResolverResult.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Agent/InlineResolverResult.cs) 的 ToolExecutionRecords 字段

**验证命令**：
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~SubAgentToolNodeTests"`

**完成判定**：
- [ ] 测试断言 ParentRecordId == 传入值
- [ ] 补 Guid.Empty 回退分支用例
- [ ] 测试全绿

**风险红线**：
- 若 InlineResolver 内 parentRecordId 未透传到 ToolExecutionRecords（需读 InlineResolver.cs 确认），需先改 InlineResolver，立即询问

**回滚**：`git restore tests/FlowEngine.Runtime.Tests/Agent/SubAgentToolNodeTests.cs`

---

### P0d-2 AgentEnhance parentRecordId 测试断言

**前置依赖**：P0b-1 完成
**TDD 顺序**：先改测试断言 → 跑测试确认通过

**定位锚点**：
- `tests/FlowEngine.Runtime.Tests/Plugins/AgentEnhanceTests.cs#L149-181`，关键代码 `RunAsync_Creates_NodeExecutionRecord_With_ParentRecordId` 测试方法

**修复步骤**：
1. 断言 `result.ToolExecutionRecords` 中至少一条记录的 `ParentRecordId == 传入的 parentRecordId`
2. 补一个 `parentRecordId=null` 或 `Guid.Empty` 的对比用例

**参照**：
- [AgentEnhanceTests.cs#L149-181](file:///d:/Repos/flow_engine/tests/FlowEngine.Runtime.Tests/Plugins/AgentEnhanceTests.cs#L149-L181) 现有测试
- [InlineResolverResult.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Agent/InlineResolverResult.cs)

**验证命令**：
- `dotnet test tests/FlowEngine.Runtime.Tests --filter "FullyQualifiedName~AgentEnhanceTests"`

**完成判定**：
- [ ] 测试断言 records 中至少一条 ParentRecordId 匹配
- [ ] 补 null/Empty 对比用例
- [ ] 测试全绿

**风险红线**：无

**回滚**：`git restore tests/FlowEngine.Runtime.Tests/Plugins/AgentEnhanceTests.cs`

---

### P0d-3 JsEngine 沙箱边界测试

**前置依赖**：无
**TDD 顺序**：先写测试 → 跑测试确认通过（实现已存在，仅测覆盖）

**定位锚点**：
- `tests/FlowEngine.Runtime.Tests/Scripting/JsEngineSecurityTests.cs#L1-141`，当前 13 个测试全为基础功能
- `backend/FlowEngine.Core/Scripting/JsEngineOptions.cs` 的 `ForbiddenIdentifiers` 集合（约 26 项：require/process/fs/fetch/eval/setTimeout/__proto__/constructor 等）

**修复步骤**：
1. 读 `JsEngineOptions.cs` 确认 ForbiddenIdentifiers 完整列表
2. 对每个关键标识符（require/process/eval/__proto__/constructor）写测试：脚本内访问该标识符，断言抛 `ScriptErrorException` 或 `ScriptSecurityException`
3. 写超时测试：构造死循环脚本，设置 `ExecutionTimeoutMs`，断言超时抛异常
4. 写递归深度测试：构造深递归脚本，断言抛异常
5. 写内存超限测试：构造大数组分配，断言抛异常

**参照**：
- [JsEngineOptions.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Scripting/JsEngineOptions.cs) 沙箱配置
- [JsEngine.cs](file:///d:/Repos/flow_engine/backend/FlowEngine.Core/Scripting/JsEngine.cs) 沙箱实现

**验证命令**：
- `dotnet test tests/FlowEngine.Core.Tests --filter "FullyQualifiedName~JsEngineSecurityTests"`

**完成判定**：
- [ ] require/process/eval/__proto__/constructor 各一条断言抛异常
- [ ] 超时测试通过
- [ ] 递归深度测试通过
- [ ] 内存超限测试通过
- [ ] 全部测试绿

**风险红线**：
- 若某标识符未被拦截（如 `__proto__` 实际可用），说明沙箱有漏洞，需先修 JsEngine，立即询问

**回滚**：`git restore tests/FlowEngine.Core.Tests/Scripting/JsEngineSecurityTests.cs`

---

# P1 — 近期修复（Major，30 项）

> P1 项的格式与 P0 相同但简化，每项含：定位锚点、修复要点、验证命令、完成判定。

#### P1-1 ~ P1-6 RBAC 系统性缺口（硬约束违反）

- [ ] **P1-1** `backend/FlowEngine.Application/Workflows/WorkflowImportService.cs#L132-199` 的 `ImportSingleAsync` 方法 — 入口加 `await authGuard.RequireAccessAsync(ResourceKind.Project, dto.ProjectId.Value, Operation.Write, ct)`。验证：`dotnet test --filter "FullyQualifiedName~ImportExportTests"`。完成判定：[ ] Import 前 RBAC 校验 [ ] 测试覆盖跨项目导入被拒
- [ ] **P1-2** `backend/FlowEngine.Application/Workflows/WorkflowExportService.cs#L40-94` 的 `ExportAsync`/`ExportBatchAsync` — 注入 `IAuthorizationGuard`，对每个 workflowId 调 `RequireAccessAsync(ResourceKind.Workflow, id, Operation.Read, ct)`。验证：`dotnet test --filter "FullyQualifiedName~WorkflowExportServiceTests"`。完成判定：[ ] 每个 id 单独 RBAC [ ] 测试覆盖导出他人工作流被拒
- [ ] **P1-3** `backend/FlowEngine.Application/Workflows/WorkflowModificationService.cs#L32-145` 的 `ModifyAsync`/`ConfirmDraftAsync`/`RejectDraftAsync` — 入口加 `RequireAccessAsync(ResourceKind.Workflow, workflowId, Operation.Write, ct)`。完成判定：[ ] 三个方法均加 RBAC [ ] 测试覆盖
- [ ] **P1-4** `backend/FlowEngine.Host/Controllers/AuditEventsController.cs#L13-22` — `[Authorize]` 改 `[Authorize(Roles = "Admin")]`。完成判定：[ ] 仅 Admin 可访问 [ ] 测试覆盖非 Admin 403
- [ ] **P1-5** `backend/FlowEngine.Application/Executions/ExecutionService.cs#L161-180` 的 `GetByWorkflowAsync` — 入口加 `RequireAccessAsync(ResourceKind.Workflow, workflowId, Operation.Read, ct)`。完成判定：[ ] RBAC 加 [ ] 测试覆盖
- [ ] **P1-6** `backend/FlowEngine.Application/Triggers/TriggerService.cs#L105-114` 的 `GetByWorkflowDefinitionIdAsync` — 入口加 `RequireAccessAsync(ResourceKind.Workflow, workflowDefinitionId, Operation.Read, ct)`。完成判定：[ ] RBAC 加 [ ] 测试覆盖

#### P1-7 ~ P1-12 审计事件系统性补齐（硬约束违反）

- [ ] **P1-7** `backend/FlowEngine.Host/Middlewares/RbacAuthorizationMiddleware.cs#L27-37` — `!HasPermission` 分支发布 `AuditEventTypes.PermissionDenied` 后返回 403。注入 `IEventBus`+`AuditEventFactory`。完成判定：[ ] 403 写审计 [ ] 测试断言审计事件
- [ ] **P1-8** `backend/FlowEngine.Application/Identity/UserRoleService.cs#L46-80` + `backend/FlowEngine.Core/Events/AuditEventTypes.cs` — 先在 `AuditEventTypes` 加 `MemberAdded`/`MemberRoleChanged` 常量；Assign/Revoke 成功后 `eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(..., new { userId, role }))`；先校验 userId 存在。完成判定：[ ] 常量已加 [ ] Assign/Revoke 写审计 [ ] 测试断言
- [ ] **P1-9** `backend/FlowEngine.Host/Controllers/FilesController.cs#L50-101` — Upload 的 `catch(UnauthorizedAccessException)` 改为 `catch (PermissionDeniedException)` 写 `FileAccessDenied` 审计后 rethrow；删除 UnauthorizedAccessException 分支。完成判定：[ ] 死代码删除 [ ] FileAccessDenied 触发 [ ] 测试覆盖
- [ ] **P1-10** `backend/FlowEngine.Host/Jobs/PollTriggerJob.cs#L61-107` — 每个 skip return 前发布 `PollSkipped`，payload 含 `reason` 字段（inactive/missing_settings/skip_if_running/node_not_registered/node_failed）。完成判定：[ ] 5 个 skip 路径均写 [ ] payload 含 reason [ ] 测试覆盖
- [ ] **P1-11** `backend/FlowEngine.Runtime/Registry/ParameterDiscoverer.cs#L116-119` — `taskOptions.GetAwaiter().GetResult()` 改 `DiscoverAsync` 异步传播，或契约改同步返回 `IEnumerable<Option>`。完成判定：[ ] 无阻塞调用 [ ] 测试全绿
- [ ] **P1-12** `backend/FlowEngine.Runtime/Registry/PluginLoader.cs#L52-85` — 加载前校验 DLL 签名或哈希白名单，未通过记录警告并跳过。完成判定：[ ] 校验逻辑加 [ ] 未通过跳过 [ ] 警告日志

#### P1-13 ~ P1-16 事务一致性

- [ ] **P1-13** `backend/FlowEngine.Application/Workflows/WorkflowService.cs#L168-279` — UpdateAsync/DeleteAsync 用 `BeginTransactionAsync` 包裹；triggerSync 采用"先注销→SaveChanges→注册新调度→失败补偿日志"模式。完成判定：[ ] 事务包裹 [ ] 失败有补偿日志 [ ] 测试覆盖失败路径
- [ ] **P1-14** `backend/FlowEngine.Application/Triggers/TriggerService.cs#L157-216` — 同 P1-13 模式。完成判定同上
- [ ] **P1-15** `backend/FlowEngine.Runtime/WaitingArea/WaitingArea.cs#L118-125` — `AddOrMerge` 合并时创建 DataItem 副本：`new DataItem { Data = item.Data, Success = item.Success, Error = item.Error, SourceIndex = i, AttachmentId = item.AttachmentId }`。完成判定：[ ] 不修改原 item [ ] 测试覆盖
- [ ] **P1-16** `backend/FlowEngine.Host/WebSocketHandlers/ExecutionWebSocketHandler.cs#L90-117` — 用 `ArrayPool<byte>`+动态扩容 `MemoryStream` 拼接直到 `result.EndOfMessage`。完成判定：[ ] 大消息不截断 [ ] 测试覆盖 >4KB 消息

#### P1-17 ~ P1-25 插件节点正确性

- [ ] **P1-17** `plugins/FlowEngine.Plugins.Standard/FilterNode.cs#L70-92` — Discarded 端口路由 discardedItems。**风险红线**：若运行时不支持多输出，需先确认 BranchIndex 机制。完成判定：[ ] Discarded 端口收到数据 [ ] 测试覆盖
- [ ] **P1-18** `plugins/FlowEngine.Plugins.Standard/DbUpsertNode.cs#L41-72` — 加 `TimeoutSeconds` 属性，`DbExecutor.CreateCommand` 时设 `CommandTimeout`。**依赖 task-006 B1 完成后执行**。完成判定：[ ] 属性加 [ ] CommandTimeout 设 [ ] 测试覆盖
- [ ] **P1-19** `plugins/FlowEngine.Plugins.Standard/MergeNode.cs#L129-131` — `ToDictionary` 改 `GroupBy`/`ToLookup` 处理重复键。完成判定：[ ] 重复键不崩 [ ] 测试覆盖
- [ ] **P1-20** `plugins/FlowEngine.Plugins.Standard/SortNode.cs#L94-117` — `GetSortKey` 统一返回类型（全 string 或全 double），或比较器处理异构。完成判定：[ ] 异构类型不崩 [ ] 测试覆盖
- [ ] **P1-21** `plugins/FlowEngine.Plugins.Standard/DataQualityNode.cs#L155-168` — `report.DeepClone()`。完成判定：[ ] 不共享引用 [ ] 测试覆盖
- [ ] **P1-22** `plugins/FlowEngine.Plugins.Standard/DataQualityNode.cs#L278-290` — 集成 Jint 实际求值，或返回 `(false, "customExpression 暂未支持")`。完成判定：[ ] 不恒返回 true [ ] 测试覆盖
- [ ] **P1-23** `plugins/FlowEngine.Plugins.Standard/LoopNode.cs#L34-103` — 入口 `BatchSize = Math.Max(1, BatchSize)`。完成判定：[ ] 边界校验 [ ] 测试覆盖 <=0
- [ ] **P1-24** `plugins/FlowEngine.Plugins.Standard/SubWorkflowExecutor.cs#L80` — `nodeOutputs[node.Name]` 改 `nodeOutputs[node.Id]`。完成判定：[ ] 用 Id 为键 [ ] CollectInputs 同步调整
- [ ] **P1-25** 见 P0b-4

#### P1-26 ~ P1-30 前端正确性 / 规范

- [ ] **P1-26** `frontend/src/components/ParameterPanel/ParameterPanel.tsx#L122` — `useWorkflowStore.getState().workflowId` 改 `useWorkflowStore((s) => s.workflowId ?? '')`。完成判定：[ ] 响应式订阅 [ ] 切换 workflow 后 TriggerConfig 重渲染
- [ ] **P1-27** `frontend/src/hooks/useNodeTypes.ts#L5-26` — 手写 useState+useEffect 改 ahooks `useRequest`。完成判定：[ ] 用 useRequest [ ] onSuccess 写 store
- [ ] **P1-28** `frontend/src/components/ParameterPanel/ValidationChecklistModal.tsx#L14-37` — 改 `useRequest(..., { ready: opened })`。完成判定：[ ] 用 useRequest [ ] 无闭包陷阱
- [ ] **P1-29** `frontend/src/components/ExecutionPanel/ExecutionPanel.tsx#L56-72` — 组件内直接调 API 改 props 接收 `onCancel` 回调。完成判定：[ ] 不直接调 API [ ] 经 useExecution 提供
- [ ] **P1-30** 三处合并：`frontend/src/stores/workflowStore.ts#L448-450` deleteWorkflow 加 try/catch；`StepItem.tsx#L39` 用 `isAgentOutput` 类型守卫后再断言；`KeyValueField.tsx#L64-82` 选一为单一数据源。完成判定：[ ] 三处全改 [ ] 测试全绿

---

# P2 — 迭代修复（Major 性能与测试覆盖，21 项）

#### P2-1 ~ P2-9 性能：AsNoTracking 与查询优化（按文件 + 方法级 checklist）

分类规则：**纯只读查询** → 加 AsNoTracking；**写操作前读取**（后续 SaveChanges 修改该实体）→ **不加** AsNoTracking（需 tracking）。

- [ ] **P2-1** `backend/FlowEngine.Infrastructure/Audit/AuditLogReader.cs#L32-91` — 全量加载内存分页改流式过滤 + 最小堆 Top-N，或迁移到数据库表。**风险红线**：迁移到数据库表涉及 schema 变更，立即询问。完成判定：[ ] 不全量加载 [ ] 大数据量测试通过
- [ ] **P2-2** `backend/FlowEngine.Application/Workflows/WorkflowRepository.cs#L15-29` 的 `FindReferencingCredentialAsync` — 用 EF `Like` 在数据库侧过滤，加 AsNoTracking（纯只读）。完成判定：[ ] 不全表扫描 [ ] AsNoTracking
- [ ] **P2-3** `backend/FlowEngine.Application/Workflows/WorkflowService.cs` — 逐方法：
  - `GetAsync`(L75-77) `GetAllAsync`(L101-110) `GetVersionAsync`(L149-151) `GetAsync`(L301-303) 纯只读 → 加
  - `UpdateAsync`(L168-171) `DeleteAsync`(L223-225) `ConfirmDraftAsync`(L244-246) `RejectDraftAsync`(L267-269) 写前 → **不加**
  - 完成判定：[ ] 4 个只读方法加 [ ] 4 个写前方法不加 [ ] *ServiceTests 全绿
- [ ] **P2-4** `backend/FlowEngine.Application/Credentials/CredentialService.cs` — `EnsureAsync`(L69-75) `GetAsync`(L135-137) `GetAllAsync`(L154-160) `ValidateNameNotInUseAsync`(L242-248) 加；`UpdateAsync`(L174-176) `DeleteAsync`(L205-207) 不加。完成判定：同上
- [ ] **P2-5** `backend/FlowEngine.Application/Triggers/TriggerService.cs` — `GetByIdAsync`(L91-93) `GetByWorkflowDefinitionIdAsync`(L108-111) `GetAllForUserAsync`(L123-130) `RegisterWorkflowSchedulesAsync`(L256-259) `UnregisterWorkflowSchedulesAsync`(L287-290) `GetActiveAsync`(L306-309) 加；`UpdateAsync`(L144-146) `DeleteAsync`(L196-198) `UpdateTriggerTimestampsAsync`(L319-321) 不加；`DeleteByWorkflowDefinitionIdAsync`(L240-243) 批量删除不加。完成判定：同上
- [ ] **P2-6** `backend/FlowEngine.Application/Executions/ExecutionService.cs` — `GetAsync`(L93-95) `GetByWorkflowAsync`(L117-119) dedup 读取(L166-177) 加；`ExecuteAsync`(L58-60) `CancelAsync`(L147-149) 不加。完成判定：同上
- [ ] **P2-7** `backend/FlowEngine.Runtime/Credentials/CredentialAccessor.cs#L33-35` — 两处 `FirstOrDefaultAsync` 加 AsNoTracking；`WorkflowExecutor.StartAsync` 中只读 ProjectId 查询加。完成判定：[ ] 加 AsNoTracking [ ] 测试全绿
- [ ] **P2-8** `frontend/src/components/Canvas/CustomNode.tsx#L149` — `useWorkflowStore((s) => s.edges)` 改 `useWorkflowStore(useShallow((s) => s.edges.filter(e => e.source === id || e.target === id)))`。完成判定：[ ] 只订阅相关 edges [ ] 画布性能改善
- [ ] **P2-9** 验证策略：每个加 AsNoTracking 的方法跑对应 *ServiceTests，确保返回数据正确；写操作测试确保 SaveChanges 仍生效

#### P2-10 ~ P2-17 测试覆盖补齐

- [ ] **P2-10** 全 tests/ RBAC 测试仅断言抛异常 — 扩展失败路径用例，断言 `_eventBus.PublishedEvents` 含 `AuditEventTypes.PermissionDenied`。完成判定：[ ] 失败路径断言审计事件 [ ] payload 校验
- [ ] **P2-11** `tests/FlowEngine.Host.Tests/PollTriggerJobTests.cs` — 类名实为 `PollDeduplicationTests`，未覆盖 `PollTriggerJob.ExecuteAsync`：新建 `PollTriggerJobTests`，覆盖空结果/去重命中/错误时发布 PollSkipped。完成判定：[ ] 新建测试 [ ] 覆盖 PollSkipped 各 reason
- [ ] **P2-12** `tests/FlowEngine.Application.Tests/Files/FileServiceTests.cs` — `FakeResourceAuthorizationService` 全返回 true：补失败用例（返回 false，断言抛 PermissionDeniedException）。完成判定：[ ] 失败用例 [ ] 断言异常
- [ ] **P2-13** 全 tests/ 无 SSE 降级测试 — 新建 `ExecutionSseFallbackTests`，覆盖 SSE Content-Type、事件帧格式、断连重连。完成判定：[ ] 新建测试 [ ] 覆盖降级路径
- [ ] **P2-14** 4 份 *AuthorizationTests.cs — 测试 double 的 IsAllowed 表与真实 AuthorizationService 策略不一致（Editor 的 Execute 权限被错误剥夺）：复用真实 AuthorizationService + ResourceAuthorizationService，或修正 switch 表对齐（参照 [AuthorizationServiceTests.cs#L36-42](file:///d:/Repos/flow_engine/tests/FlowEngine.Application.Tests/Authorization/AuthorizationServiceTests.cs#L36-L42)）。**风险红线**：修正前必须先读真实策略，避免反向破坏。完成判定：[ ] 测试 double 对齐 [ ] Editor.Execute 被验证
- [ ] **P2-15** `tests/FlowEngine.Runtime.Tests/OpenAiLlmClientTests.cs#L1-59` — 仅 5 个测试全测静态 helper：用 `HttpMessageHandler` stub 补 `ChatAsync`/`ChatStreamAsync`/4xx/5xx/网络异常/工具调用映射。完成判定：[ ] 真实 HTTP 调用测试 [ ] 错误路径覆盖
- [ ] **P2-16** `tests/FlowEngine.Application.Tests/Credentials/CredentialServiceTests.cs#L159-179` — StubEncryptionService 不测真实算法：新建 `CredentialEncryptionServiceTests`，测 AesGcm 往返一致、空串、错误 tag/nonce 应抛。完成判定：[ ] 新建测试 [ ] 真实算法覆盖
- [ ] **P2-17** `tests/FlowEngine.Runtime.Tests/Credentials/OAuth2TokenServiceTests.cs#L82-102` — 断言墙钟时间 flaky：用可注入 `TimeProvider`/`IRetryDelayStrategy` 替换 `Task.Delay`。**风险红线**：改 OAuth2TokenService 注入 TimeProvider 需改构造函数签名，立即询问。完成判定：[ ] 不依赖墙钟 [ ] 测试快

#### P2-18 ~ P2-21 其他 Major

- [ ] **P2-18** `backend/FlowEngine.Application/Projects/ProjectServiceTests.cs#L66-74` — `CreateAsync_PublishesAuditEvent` 仅断言 `Count > 0` 改 `Assert.Single` + `AuditEventTypes.ProjectCreated` + payload。完成判定：[ ] 严格断言
- [ ] **P2-19** `backend/FlowEngine.Host/Controllers/FilesController.cs#L88-101` — Download 两次 DB 查询合并为 `GetDownloadAsync(fileId)` 返回 `(Stream, StoredFileDto?)`。完成判定：[ ] 单次查询
- [ ] **P2-20** `backend/FlowEngine.Host/WebSocketHandlers/WebSocketReplayService.cs#L18-50` — executionId 数量无上限内存泄漏：加全局上限（最多 100 个 execution）+ LRU 淘汰。完成判定：[ ] 上限加 [ ] LRU 实现
- [ ] **P2-21** `backend/FlowEngine.Infrastructure/Identity/PasswordHasher.cs#L22-26` — `SuccessRehashNeeded` 视为成功并触发重哈希。完成判定：[ ] 算法升级用户可登录

---

# P3 — 清账（Minor，25 项）

#### P3-1 ~ P3-5 React key 用数组索引统一整改

- [ ] **P3-1** `frontend/src/components/ParameterPanel/DiffPanel.tsx#L74-76` — `key={idx}` 改 `key={\`${entry.op}-${entry.nodeId ?? entry.field}-${idx}\`}`
- [ ] **P3-2** `frontend/src/components/CredentialPanel/CredentialListModal.tsx#L135-136` — `key={index}` 改稳定唯一 id（如 `crypto.randomUUID()`）
- [ ] **P3-3** `frontend/src/pages/ExecutionHistoryPage.tsx#L227-294` — Fragment 无 key 改 `<Fragment key={record.id}>`
- [ ] **P3-4** `frontend/src/components/WorkflowList/WorkflowListPage.tsx#L546-547` — `key={idx}` 改 `key={\`${err.errorType}-${err.nodeId}-${idx}\`}`
- [ ] **P3-5** `frontend/src/hooks/useExecution.ts#L81-129` — `useWorkflowStore.getState().workflowId` 改 `useWorkflowStore((s) => s.workflowId)` 并加入 deps

#### P3-6 ~ P3-8 死代码清理

- [ ] **P3-6** `plugins/FlowEngine.Plugins.Standard/PaginateNode.cs#L52-60` — CursorInitial/CursorType/MaxPages 声明未用：统一属性驱动或移除
- [ ] **P3-7** `plugins/FlowEngine.Plugins.Standard/LoopNode.cs#L145-161` — `LoopIterationResult` 类未引用：删除
- [ ] **P3-8** `plugins/FlowEngine.Plugins.Standard/JSNode.cs#L21` + `CodeSnippetToolNode.cs#L21` — `DefaultTimeoutMs` 常量未用：删除或实际应用

#### P3-9 ~ P3-13 插件节点规范

- [ ] **P3-9** `plugins/FlowEngine.Plugins.Standard/Data/DbDialect.cs#L6-12` — 枚举值缺 `[Description]` 中文标注
- [ ] **P3-10** `plugins/FlowEngine.Plugins.Standard/DataQualityNode.cs#L19-23` — 公共属性缺 `///` XML 注释
- [ ] **P3-11** `plugins/FlowEngine.Plugins.Standard/WebSearchToolNode.cs#L202,251` — apiKey 未 `Uri.EscapeDataString`；Bing/SerpAPI 优先用 Header
- [ ] **P3-12** `plugins/FlowEngine.Plugins.Standard/ShellToolNode.cs#L218-224` — `Kill()` 改 `Kill(entireProcessTree: true)`
- [ ] **P3-13** `plugins/FlowEngine.Plugins.Standard/OAuth2Node.cs#L65-68` — 加 `IsNullOrWhiteSpace` 校验；`WaitNode.cs#L33,82-91` — Amount 加 `Math.Max(0, Amount)`；`SubWorkflowToolNode.cs#L118-121` — TryParseJson 错误消息含 parseError

#### P3-14 ~ P3-17 Host/Infrastructure 规范

- [ ] **P3-14** `backend/FlowEngine.Host/WebSocketHandlers/WebSocketConnectionManager.cs#L139-161` — fire-and-forget CloseAsync 改同步等待或仅 Dispose
- [ ] **P3-15** `backend/FlowEngine.Infrastructure/Storage/LocalFileStorage.cs#L113-133` — FindFile 全盘扫描：SaveAsync 返回相对路径，ReadAsync 直接拼接读取
- [ ] **P3-16** `backend/FlowEngine.Host/Middlewares/GlobalExceptionHandlerMiddleware.cs#L82-83` — UnauthorizedAccessException 映射 401 改统一 PermissionDeniedException => 403
- [ ] **P3-17** `backend/FlowEngine.Host/ApplicationBuilderExtensions.cs#L117-118` — `CreateScope()` 改 `CreateAsyncScope()` + `await using`

#### P3-18 ~ P3-21 配置/Application

- [ ] **P3-18** `backend/FlowEngine.Host/appsettings.Development.json#L2-4` — 硬编码 JWT Secret 改 user-secrets 或环境变量
- [ ] **P3-19** `backend/FlowEngine.Application/Identity/AuthenticationService.cs#L26-99` — `_clientIp` 实例字段改参数传参
- [ ] **P3-20** `backend/FlowEngine.Host/Jobs/PollTriggerJob.cs#L224-245` — CreateNodeExecutionContext 改注入 `NodeExecutionContextFactory`
- [ ] **P3-21** `backend/FlowEngine.Runtime/Credentials/CredentialAccessor.cs#L37-49` — 未找到时两方法行为统一（null 或抛 NotFoundException）

#### P3-22 ~ P3-25 测试规范

- [ ] **P3-22** `tests/FlowEngine.Runtime.Tests/Plugins/AgentNodeTests.cs#L32-57` — 测试命名改 `{方法}_{场景}_{预期}`
- [ ] **P3-23** `tests/FlowEngine.Runtime.Tests/Plugins/AgentEnhanceTests.cs#L659` — 合并两份 `SubAgentToolNodeTests`；拆分 `AgentEnhanceTests.cs` 内 `InlineResolverTests`/`AgentMemoryTests` 到独立文件
- [ ] **P3-24** 测试 Fake 类重复：抽取到 `tests/FlowEngine.Application.Tests/TestSupport/Fakes/` 共享（`FakeUserContext`/`InMemoryEventBus`/`FakeNodeRegistry`/`FakeScheduleManager`/`StubEncryptionService`/`StubKeyProvider`/`RoleBasedResourceAuthorizationService`）
- [ ] **P3-25** `tests/FlowEngine.Runtime.Tests/Plugins/AgentEnhanceTests.cs#L713-774` — 补 ToolExecutionRecord 各字段校验；`tests/FlowEngine.Runtime.Tests/Agent/AgentNodeDtoTests.cs#L1-226` — 补含 ToolCalls 的 Iterations/SubRecords/流式 LlmChunks 场景

---

## 完成标准
- **P0 全部完成**：`dotnet build` + `npm run build` + `npm run typecheck` 通过；新增/修复的对应测试全绿；Critical 攻击链与核心功能缺陷消除
- **P1 全部完成**：硬约束 RBAC 与审计事件全部补齐（PermissionDenied/RateLimited/PollSkipped/ExportPerformed/ImportPerformed/FileAccessDenied/MemberAdded/MemberRoleChanged）；事务边界统一；节点边界缺陷修复；前端实时数据正确性恢复
- **P2 全部完成**：AsNoTracking 按方法级 checklist 补齐且写操作未误加；测试覆盖硬约束场景全绿（资源权限/项目过滤/审计事件/Agent DTO 序列化/SSE 降级/parentRecordId）；性能瓶颈消除
- **P3 全部完成**：React key 规范化；死代码清理；命名/注释/规范统一
- 全程不新增功能，仅修复已确认问题；每个 P 阶段完成后单独发起 SubAgent Code Review

## 完成状态
- [x] P0a 后端安全与调度（2 项）
- [x] P0b 插件节点（4 项）
- [x] P0c 前端实时数据（4 项）
- [x] P0d 测试虚假覆盖（3 项）
- [x] P1（30 项，其中 P1-17/P1-18 跳过：P1-17 需运行时多输出端口支持，P1-18 依赖 task-006 B1 未完成）
- [x] P2（21 项）
- [x] P3（25 项）
- [x] **复审第二轮（2026-07-15）**：对原计划标记"已完成"的 9 项做独立 SubAgent 复审，发现 8 项为部分实现/虚假覆盖、1 项（P2-2）需跨库可移植决策；全部修复/定案。详见下方「主要修改记录 - 复审第二轮」。

## 主要修改记录
- 2026-07-15 | SubAgent-A | P0a-1 | SsrfGuard 新增 CreateConnectCallback；HttpClientPool 改用 SocketsHttpHandler+ConnectCallback；OAuth2TokenService 入口 SSRF 校验+异常消息移除 responseBody | dotnet build 0 错误，OAuth2TokenServiceTests 9/9 通过
- 2026-07-15 | SubAgent-A | P0a-2 | QuartzScheduleManager 移除 IHostedService/IDisposable/_scheduler，所有方法懒加载 GetScheduler | dotnet build 0 错误，Host.Tests 187/187 通过
- 2026-07-15 | SubAgent-B | P0b-1 | SubAgentToolNode Completed 分支返回 AgentExecutionResultDto 序列化 | dotnet build 0 错误，SubAgentToolNodeTests 9/9 通过
- 2026-07-15 | SubAgent-B | P0b-2 | SubWorkflowExecutor 改用 NodeRegistry.CreateInstance 替代反射；补 NestingDepth 透传 | dotnet build 0 错误，SubWorkflowToolNodeTests 7/7 通过
- 2026-07-15 | SubAgent-B | P0b-3 | WebSearchToolNode 新增 SearchEngineId 属性；cx 用 SearchEngineId；Uri.EscapeDataString | dotnet build 0 错误，WebSearchToolNodeTests 4/4 通过
- 2026-07-15 | SubAgent-B | P0b-4 | SubWorkflowToolNode 加 MaxNestingDepth 属性+入口检查+ResolveMaxNestingDepth | dotnet build 0 错误，SubWorkflowToolNodeTests 7/7 通过
- 2026-07-15 | SubAgent-C | P0c-1 | AuthContext useState 初始化包 try/catch + removeItem | npm build+typecheck 通过
- 2026-07-15 | SubAgent-C | P0c-2 | 三个 handler 调用 updateExecutionMeta 更新 status/completedAt | npm build+typecheck 通过，95 tests 通过
- 2026-07-15 | SubAgent-C | P0c-3 | useSseFallback 实现指数退避重连(5次/2s基数) | npm build+typecheck 通过，97 tests 通过
- 2026-07-15 | SubAgent-C | P0c-4 | SSE 多订阅：Map<string, SseConnection> 管理多 EventSource | npm build+typecheck 通过，97 tests 通过
- 2026-07-15 | SubAgent-D | P0d-1 | SubAgentToolNodeTests 补 parentRecordId 断言 + Guid.Empty 回退用例 | 10 tests 通过
- 2026-07-15 | SubAgent-D | P0d-2 | AgentEnhanceTests 补 parentRecordId 匹配断言 + null 对比用例 | 12 tests 通过
- 2026-07-15 | SubAgent-D | P0d-3 | JsEngineSecurityTests 新增 8 个沙箱边界测试（require/process/eval/__proto__/constructor/超时/递归/内存） | 21 tests 通过
- 2026-07-15 | SubAgent-E | P1-1~6 | Import/Export/Modification/AuditEvents/Execution/Trigger 增加 RBAC 校验 | Application.Tests 348/348 通过
- 2026-07-15 | SubAgent-E | P1-7~12 | 补齐 PermissionDenied/MemberAdded/MemberRoleChanged/FileAccessDenied/PollSkipped 审计事件；ParameterDiscoverer 异步化；PluginLoader DLL 校验 | Application+Host.Tests 通过
- 2026-07-15 | SubAgent-E | P1-13~16 | Workflow/Trigger Service 事务包裹；WaitingArea 副本；WebSocketHandler ArrayPool | Application+Host.Tests 通过
- 2026-07-15 | SubAgent-F | P1-19~24 | MergeNode ToLookup、SortNode 异构排序、DataQualityNode DeepClone+customExpression、LoopNode BatchSize 边界、SubWorkflowExecutor 用 Id 为键 | Runtime.Tests 通过（P1-17/P1-18 跳过）
- 2026-07-15 | SubAgent-G | P1-26~30 | 前端响应式订阅、useRequest 重构、onCancel 回调、deleteWorkflow try/catch、类型守卫、KeyValueField 单一数据源 | 前端 97 tests 通过
- 2026-07-15 | SubAgent-H | P2-1~9 | AuditLogReader 流式 TopN、多 Service AsNoTracking 分类整改、CustomNode edges 选择器优化 | 后端测试通过
- 2026-07-15 | SubAgent-I | P2-10~17 | RBAC 审计断言、PollTriggerJob 测试、FileService 失败用例、SSE 降级测试、权限表对齐、OpenAiLlmClient HTTP 测试、CredentialEncryptionService AesGcm 测试、OAuth2 重试墙钟 flaky 修复 | 后端测试通过
- 2026-07-15 | SubAgent-J | P2-18~21 | ProjectCreated 审计严格断言、FilesController 单次查询、WebSocketReplayService LRU 上限、PasswordHasher SuccessRehashNeeded | 后端测试通过
- 2026-07-15 | SubAgent-K | P3-1~8 | React key 稳定化、useExecution 选择器、PaginateNode/LoopNode/JSNode/CodeSnippetToolNode 死代码清理 | 前端 97 tests 通过
- 2026-07-15 | SubAgent-L | P3-9~17 | 插件节点 Description/XML 注释、WebSearch Header/SSRF、Shell KillProcessTree、OAuth2/Wait/SubWorkflow 边界、WebSocket 清理、LocalFileStorage 单次查询、ExceptionHandler 403、CreateAsyncScope | 后端测试通过
- 2026-07-15 | SubAgent-M | P3-18~25 | JWT Secret 占位符、AuthenticationService clientIp 参数化、PollTriggerJob 工厂注入、CredentialAccessor 行为统一、测试命名规范、AgentEnhanceTests 拆分、共享 Fakes、AgentNodeDto 补齐 | Application+Runtime 测试通过

### 复审第二轮（2026-07-15，独立 SubAgent 复审后补丁）
- 2026-07-15 | Review | P1-14 | TriggerService.UpdateAsync/DeleteAsync 的 SaveChanges 用 `Database.IsRelational()` 包裹 BeginTransactionAsync（InMemory 测试提供程序不支持事务，否则 2 个测试 BEGIN TRANSACTION 报错）；Quartz 调度作为外部状态保留在事务外 | dotnet build 0 错误，TriggerServiceTests 20/20 通过
- 2026-07-15 | Review | P2-5 | TriggerService 其余只读查询补齐 AsNoTracking：GetByIdAsync/GetByWorkflowDefinitionIdAsync/GetAllForUserAsync/RegisterWorkflowSchedulesAsync/UnregisterWorkflowSchedulesAsync | TriggerServiceTests 20/20 通过
- 2026-07-15 | Review | P2-7 | CredentialAccessor 两处 FirstOrDefaultAsync 补齐 AsNoTracking | Application.Tests 349/349 通过
- 2026-07-15 | Review | P1-1 | WorkflowImportExportTests 新增 Import_CrossProjectAccessDenied_ThrowsPermissionDenied，断言跨项目导入抛 PermissionDeniedException（DenyingProjectAuthorizationGuard） | Application.Tests 349/349 通过
- 2026-07-15 | Review | P0c-2 | ExecutionDto 新增 error 字段；messageHandlers execution_failed 分支写入 error；SSE/WebSocket 失败态前端正确展示错误 | npm run build 通过（tsc -b 0 错误）
- 2026-07-15 | Review | P0d-1 | NodeExecutionResult 新增 ToolExecutionRecords 属性（非破坏性）；SubAgentToolNode Completed 分支回填；SubAgentToolNodeTests 由手工 InlineResolver 改为断言 result.ToolExecutionRecords 的 ParentRecordId 端到端匹配 | Runtime.Tests 343/343 通过
- 2026-07-15 | Review | P2-13 | ExecutionSseFallbackTests 重写：真实驱动 SseController.Stream，断言 Content-Type=text/event-stream、含 event: connected 帧、订阅全部 8 个 ExecutionDomainEvents（原为仅构造 DTO 的虚假覆盖） | Host.Tests 195/195 通过
- 2026-07-15 | Review | P0b-2 | 经复审确认：SubWorkflowExecutor 的 property.SetValue 为参数绑定循环（将字典参数拷回节点属性），属良性反射；真正缺陷（Activator.CreateInstance 不安全实例化）已在首轮经 NodeRegistry.CreateInstance 修复。维持现状，计划"移除反射"表述过强 | 无需改动
- 2026-07-15 | Review | P2-2 | 调研结论：EF.Functions.Like 对 JSON 列仅 SQLite/SQL Server 可翻译，PostgreSQL(jsonb)/MySQL(JSON)/Dameng 会运行时报 SQL 错，不可移植。按项目"避免方言/可移植 SQL"规则，决议保持 AsNoTracking 内存过滤（已加 AsNoTracking），不做非可移植 Like；真正服务端过滤需新增派生 CredentialIds 索引列（schema 变更，按红线另批） | 维持现状，Application.Tests 349/349 通过
- 2026-07-15 | Review | P2-21 | 补充 SuccessRehashNeeded 回归测试（之前仅实现无测试）：新增 `PasswordHasher_VerifyPassword_LegacyV2Hash_ReturnsSuccessRehashNeeded`（V2 哈希映射）与 `LoginAsync_LegacyV2HashAlgorithm_SucceedsAndUpgradesHash`（旧算法用户可登录且哈希升级为 V3），覆盖 AuthenticationService 的 SuccessRehashNeeded 分支 | Application.Tests 351/351 通过

## 风险与待定项
- **P0a-1 SSRF**：`SocketsHttpHandler.ConnectCallback` 在 net10.0 可用（.NET 5+ 支持）；OAuth2 凭据测试需 mock SsrfGuard 避免影响真实 token 端点
- **P0a-2 调度**：删除 IHostedService 实现后，`AddQuartzHostedService` 仍负责 scheduler 生命周期；`GetScheduler` 在 scheduler 未启动时是否自动启动需查 Quartz 文档（默认会）
- **P0b-1 SubAgent DTO 变更**：前端 NodeOutputList.tsx 已直接消费 AgentExecutionData，应兼容；若不兼容需同步改前端
- **P0b-2 SubWorkflowExecutor**：方案 A 涉及节点参数读取模式统一，需先读 NodeRegistry 确认节点生命周期模型再定方案
- **P0b-4 SubWorkflow 嵌套**：依赖 `NodeExecutionContext.NestingDepth` 属性存在；若不存在需先加
- **P0c-2 executionMeta**：若 `setExecutionMeta` 不存在需在 store 加 action，影响面扩大
- **P0c-4 SSE 多订阅**：多 EventSource 可能触发浏览器连接数限制（HTTP/1.1 同源 6 个）
- **P1-13/14 事务**：triggerSync 涉及 Quartz 外部状态无法纳入 DB 事务，采用"先注销→SaveChanges→注册新调度→失败补偿日志"模式
- **P1-18 DbUpsertNode 超时**：依赖 task-006 B1 完成后执行
- **P2-2 FindReferencingCredentialAsync 服务端过滤（可移植性）**：JSON 列 `Nodes` 经 `JsonColumnAttribute`+`JsonValueConverter` 映射为 jsonb/json 文本列；`EF.Functions.Like` 仅 SQLite/SQL Server 可翻译，PostgreSQL/MySQL/Dameng 运行时会报 SQL 错（违反可移植规则）。复审决议：保持 AsNoTracking 内存过滤（已加 AsNoTracking），真正服务端过滤需新增派生 CredentialIds 索引列（schema 变更，触发红线，需另批计划+迁移）
- **P2-1 AsNoTracking**：已按方法级分类（纯只读 vs 写前读取），执行时严格按 checklist
- **P2-14 测试 double 对齐**：修正 IsAllowed 表需对照 [AuthorizationServiceTests.cs#L36-42](file:///d:/Repos/flow_engine/tests/FlowEngine.Application.Tests/Authorization/AuthorizationServiceTests.cs#L36-L42) 真实策略
- **P2-17 OAuth2 重试测试**：改 OAuth2TokenService 注入 TimeProvider 需改构造函数签名
