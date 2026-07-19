# 任务：后端 Runtime 模块测试补充

## 目标

将 `FlowEngine.Runtime` 行覆盖率从 **65.0%**（Task 008 实测，覆盖行 2051 / 3153）拉升至 **75%+**，并括及同测试工程的 `Plugins.Standard`（实测 58.1% → 70%+）。因用户决策后端整体冲 75%+，本模块需实质性深补：错误策略、参数提取/水合、表达式解析、以及标准插件节点逻辑。

**行号说明**：文中 `:行号`（如 `:41`）取自 2026-07-17 版本源码，仅作辅助参考；执行时请以类名 / 方法名 / 签名为准确认当前源码，行号可能因后续改动偏移。

## 目标类与已核实 API

### SsrfGuard
- 命名空间 `FlowEngine.Core.Http`，`Http/SsrfGuard.cs:13`，`public static class`
- 真实签名：**`public static bool IsInternalTarget(string? url)`** :19（**无 `IsSafeUrl(Uri)`**，原草稿臆测）。
- 另有 `public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback()` :74。

### ErrorStrategyHandler
- 命名空间 `FlowEngine.Runtime.Executor`，`Executor/ErrorStrategyHandler.cs:9`，`public sealed class`（无参构造）
- 真实公共方法：
  - **`public NodeExecutionResult Handle(NodeExecutionResult result, string nodeDefinitionId, ErrorStrategy strategy)`** :18
  - **`public NodeExecutionResult CreateInputTimeoutResult(string nodeDefinitionId)`** :36
- 注意：**无** `ShouldRetry(RetryPolicy,int)` / `CalculateDelay(RetryPolicy,int)`（原草稿臆测）。
- `ErrorStrategy` 枚举在 `FlowEngine.Core.Enums`（`Core/Enums/ErrorStrategy.cs:8`）。
- `RetryPolicy` 在 `FlowEngine.Core.Entities`（`Core/Entities/RetryPolicy.cs:10`）：属性 `int MaxRetries`、`TimeSpan BaseDelay`、`TimeSpan MaxDelay`、`bool UseJitter`、`BackoffStrategy BackoffStrategy`（默认 `Exponential`）、`List<string>? RetryableErrorCodes`。**无 `DelayMs`**（原草稿臆测为 `DelayMs`）。

### CodeParameterExtractor / ParameterHydrator / Converters
- `CodeParameterExtractor` `Executor/CodeParameterExtractor.cs:9`，**`internal static class`**；签名正确：`public static Dictionary<string,object> Extract(Dictionary<string,object> rawParameters, NodeTypeDescriptor descriptor)` :15。
- `ParameterHydrator` `Registry/ParameterHydrator.cs:19`，`public sealed class`；**公共入口**：`public async Task HydrateAsync(INodeType instance, IReadOnlyDictionary<string,object> resolvedValues)` :55。
- `IValueConverter` `Registry/IValueConverter.cs:6`，**`internal interface`**：`Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)` :16 + `bool CanConvert(Type targetType)`。
- 转换器（`internal sealed`，共 12 个）：`StringConverter`、`BoolConverter`、`NumericConverter`、`DateTimeConverter`、`UriConverter`、`CredentialConverter`、`JsonConverter`、`ScriptConverter`、`DictionaryConverter`、`ListConverter`、`EnumConverter`、`FallbackConverter`。
- **测试策略**：转换器与 `CodeParameterExtractor` 均为 `internal`。优先经公共入口 `ParameterHydrator.HydrateAsync` 间接覆盖各转换器；若需在测试程序集直接调用，须在 `FlowEngine.Runtime` 程序集加 `[InternalsVisibleTo("FlowEngine.Runtime.Tests")]`（唯一允许的非逻辑改动）。**禁止**原草稿 `new BoolConverter().Convert("true")` 写法（方法名/参数/返回值均错）。

### ParameterResolver（表达式引擎）
- 命名空间 `FlowEngine.Runtime.Expressions`，`Expressions/ParameterResolver.cs:22`，`public sealed class`
- 公共方法：`public async Task<Dictionary<string,object>> ResolveAsync(IReadOnlyDictionary<string,object> rawParameters, JsEngine jsEngine, CancellationToken ct = default)` :75
- 覆盖：表达式识别（`IsExpression`）、`JsonElement` 转换、异常映射（`SyntaxErrorException` / `FieldNotFoundException` / `TypeMismatchException` / `SecurityViolationException`）。（**无 `ExpressionEvaluator` 类**，原草稿臆测）

### Executor / Factory
- `NodeExecutionContextFactory` `Executor/NodeExecutionContextFactory.cs:20`，`public sealed class : INodeExecutionContextFactory`；`public async Task<NodeExecutionContext> CreateAsync(Workflow, ExecutionRecord, NodeDefinition, INodeType, IReadOnlyDictionary<string,DataBatch> inputs, IReadOnlyDictionary<string,DataBatch> successfulOutputs, IReadOnlyDictionary<string,DataBatch> latestBatches, int runIndex, CancellationToken, ICredentialAccessor? credentialAccessorOverride = null, IReadOnlyDictionary<string,object?>? extraGlobals = null)` :38。**无 `Build` / `Create` 简写**。
- `WorkflowExecutor` `Executor/WorkflowExecutor.cs`：`public async Task<ExecutionId> StartAsync(...)` :53、`public async Task ExecuteLoopAsync(...)` :89。

## 待完成项

### Runtime 部分（已完成）
- [x] **3.1 SsrfGuard 测试**：`IsInternalTarget` 对 localhost / 内网地址 / 公网 URL 的判定；null/空字符串。→ `tests/FlowEngine.Runtime.Tests/Security/SsrfGuardTests.cs`
- [x] **3.2 ErrorStrategyHandler 测试**：`Handle` 在不同 `ErrorStrategy` 下的结果；`CreateInputTimeoutResult` 产出结构。使用真实 `RetryPolicy`（注意 `BaseDelay` 非 `DelayMs`）。→ `tests/FlowEngine.Runtime.Tests/Executor/ErrorStrategyHandlerTests.cs`
- [x] **3.3 参数水合与转换器测试**：`ParameterHydratorCoverageTests`（经 `HydrateAsync` 覆盖大部分）+ `Registry/Converters/ConverterUnitTests`（经 `[InternalsVisibleTo]` 直接覆盖 12 个 internal 转换器）；`CodeParameterExtractor.Extract` 的 `Dictionary<string,object>` 入参版本 → `tests/FlowEngine.Runtime.Tests/Executor/CodeParameterExtractorTests.cs`。
- [x] **3.4 ParameterResolver 测试**（表达式解析正常路径、`JsonElement` 转换、四类异常路径）+ 表达式异常类（`NodeOutputNotFoundException` / `FieldNotFoundException` / `ExpressionEvaluationException`）→ `tests/FlowEngine.Runtime.Tests/Expressions/ParameterResolverExceptionsTests.cs` + `Expressions/Exceptions/ExpressionExceptionTests.cs`。
- [x] **3.5 执行队列 / HTTP 池**：`ExecutionQueue.Enqueue/Dequeue/Reader` + `NodeWorkItem` 记录 → `tests/FlowEngine.Runtime.Tests/Executor/ExecutionQueueTests.cs`；`HttpClientPool.GetClient/Dispose` → `tests/FlowEngine.Runtime.Tests/Http/HttpClientPoolTests.cs`。

### Plugins.Standard 部分（进行中，58.1% → 70%）
- [ ] **3.6 简单纯逻辑节点**（低投入、高行覆盖）：`LimitNode`（Skip/Take）、`MemoryNode`（Read/Write/Clear + JSON 字面量回退）、`ManualTriggerNode`（DefaultIsEntry 输出 triggeredAt）、`WaitNode`（Amount/Unit/LimitWaitTime 计算 WaitTime）、`DeduplicateNode`（CompareField/KeepFirst 全项与字段键）、`CalculatorToolNode`（expression/query/math 取值 + 脚本求值 + 错误路径）。
  - 测试上下文复用模式见 `tests/FlowEngine.Runtime.Tests/Plugins/FilterNodeTests.cs`：`NodeRegistry` + `NodeExecutionContextFactory(ScriptCache, ParameterResolver, NullCredentialAccessor, whitelist)` + `factory.CreateAsync(...)` + 内部 `NullCredentialAccessor`。
  - 节点通过 `new XxxNode { Prop = ... }.ExecuteAsync(context, ct)` 直接执行；输入经 `FlowConstants.PortNames.Input` 的 `DataBatch`。
- [ ] **3.7 较大缺口节点**（按需）：`AggregateNode`(~90 未覆盖)、`DataQualityNode`(~79)、`ThinkToolNode`(~74)、`SubWorkflowExecutor`(~72)、`MergeNode`(~64)、`WebhookNode`(~55)、`ScheduleTriggerNode`(~46)。

## 完成标准

- `dotnet test tests/FlowEngine.Runtime.Tests` 全绿（Runtime 实测 75.2% / 499 用例通过）。
- 不出现 `FluentAssertions`；`Moq` 仅限 `FlowEngine.Host.Tests`，Runtime 测试用手写 fake / InMemory（`NullCredentialAccessor` 等）。
- 所有签名与上文核实一致；`[InternalsVisibleTo("FlowEngine.Runtime.Tests")]` 仅 Runtime 一处（已加）。
- 对应项目 `dotnet build` 通过。

## 完成状态

- [x] 3.1 SsrfGuard
- [x] 3.2 ErrorStrategyHandler
- [x] 3.3 参数水合与转换器
- [x] 3.4 ParameterResolver 与表达式异常
- [x] 3.5 执行队列 / HTTP 池
- [ ] 3.6 简单节点（Limit/Memory/ManualTrigger/Wait/Deduplicate/Calculator）
- [ ] 3.7 较大缺口节点（Aggregate/DataQuality/ThinkTool/SubWorkflow/Merge/Webhook/ScheduleTrigger）

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：修正 `IsSafeUrl(Uri)`→`IsInternalTarget(string?)`、`ShouldRetry/CalculateDelay`→`Handle/CreateInputTimeoutResult`、`Convert(string)→bool`→`ConvertAsync(object?,Type,ctx)→Task<object?>`、`DelayMs`→`BaseDelay`、虚构 `ExpressionEvaluator` 等。
- 2026-07-19 进度：Runtime 部分（3.1–3.5）全部完成，新增 9 个测试文件、共 ~83 用例，Runtime 覆盖率 65.0% → **75.2%**（499 用例全绿）。Plugins.Standard 部分（3.6–3.7）尚未开始写测试，仍 58.1%；已核实 6 个简单节点源码与 `FilterNodeTests` 上下文模板，可直接开工。
- 已发现但未修复的 3 个生产缺陷（计划禁止修改生产逻辑，仅记录）：① `WorkflowRepository.FindReferencingCredentialAsync` 的 EF JsonElement 问题；② `NumericConverter` 返回 boxed `Double`（int/long/float 分支被三元提升）；③ `FallbackConverter` 的 string→Guid 失败。
