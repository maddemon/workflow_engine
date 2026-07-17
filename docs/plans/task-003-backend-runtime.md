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

- [ ] **3.1 SsrfGuard 测试**：`IsInternalTarget` 对 localhost / 内网地址 / 公网 URL 的判定；null/空字符串。
- [ ] **3.2 ErrorStrategyHandler 测试**：`Handle` 在不同 `ErrorStrategy` 下的结果；`CreateInputTimeoutResult` 产出结构。使用真实 `RetryPolicy`（注意 `BaseDelay` 非 `DelayMs`）。
- [ ] **3.3 参数水合与转换器测试**：经 `ParameterHydrator.HydrateAsync` 覆盖 `bool` / `numeric` / `DateTime` / `Uri` / `enum` / `Json` 等目标类型转换；`CodeParameterExtractor.Extract` 的 `Dictionary<string,object>` 入参版本。
- [ ] **3.4 ParameterResolver 测试**：表达式解析正常路径、`JsonElement` 转换、四类异常路径。

## 完成标准

- `dotnet test tests/FlowEngine.Runtime.Tests` 全绿。
- 不出现 `FluentAssertions`；`Moq` 仅当确须 mock 外部依赖时使用，否则用手写 fake / InMemory。
- 所有签名与上文核实一致；如启用 `[InternalsVisibleTo]` 仅限 Runtime 一处。

- 对应项目 `dotnet build` 通过（无编译错误，新增测试不得引入类型/签名错误）。

## 完成状态

- [ ] 3.1
- [ ] 3.2
- [ ] 3.3
- [ ] 3.4

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：修正 `IsSafeUrl(Uri)`→`IsInternalTarget(string?)`、`ShouldRetry/CalculateDelay`→`Handle/CreateInputTimeoutResult`、`Convert(string)→bool`→`ConvertAsync(object?,Type,ctx)→Task<object?>`、`DelayMs`→`BaseDelay`、虚构 `ExpressionEvaluator` 等。
