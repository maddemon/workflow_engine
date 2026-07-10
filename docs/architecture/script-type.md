# Script 类型设计

本设计解决"脚本（JS 表达式 / 多语句脚本）在节点中以 `string` 存储"带来的类型无语义、编译/缓存散落、求值样板重复、通用逻辑错位四类问题。它是 `expression-system.md` 在"类型与求值抽象"层面的补强：**表达式系统讲语法与变量，本文讲承载脚本的类型与求值封装**。

按需求，本设计**不考虑向后兼容**：节点属性从 `string` 改为 `Script`，工作流 JSON 中脚本字段的线格式由本设计重新定义，历史字符串字段的迁移不在范围内（旧工作流需重新保存或离线迁移）。

> **快照说明**：§1 证据表的行号锚点基于当前代码快照，实施重构后会漂移，仅供参考定位。

## 1. 现状问题

当前所有"脚本/表达式"节点属性都是裸 `string`，运行逻辑散落：

| 问题                | 证据                                                                                                                                                                                                                                                                                                                                                           | 后果                                                                                                    |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| 类型无语义          | `FilterNode.Condition` 是 `string`（[FilterNode.cs:41](../plugins/FlowEngine.Plugins.Standard/FilterNode.cs#L41)），`JSNode.Code` 是 `string`（[JSNode.cs:48](../plugins/FlowEngine.Plugins.Standard/JSNode.cs#L48)）                                                                                                                                          | 调用点必须靠 `JsEngine.PrepareExpression` vs `Run` 的不同包裹猜"这是表达式还是多语句脚本"，无法静态区分 |
| 编译/缓存散落调用点 | 每个节点在 `ExecuteAsync` 里 `JsEngine.PrepareExpression(Condition)`（[FilterNode.cs:80](../plugins/FlowEngine.Plugins.Standard/FilterNode.cs#L80)）                                                                                                                                                                                                           | AST 无法跨节点、跨求值复用；每次执行都重编译                                                            |
| 求值样板重复        | `FilterNode.EvaluateExpressionForItem`（[FilterNode.cs:156](../plugins/FlowEngine.Plugins.Standard/FilterNode.cs#L156)）手工拼：`ApplyItemScope` + `EvaluatePrepared` + `JsEngine.ToClrValue` + `ToBoolean`                                                                                                                                                    | 同样的 4 步组合在 FilterNode / DbUpsertNode 各抄一遍，且 `$input.params/context` 注入方式易漂移         |
| 通用逻辑错位        | `ToBoolean` 在 [FilterNode.cs:181](../plugins/FlowEngine.Plugins.Standard/FilterNode.cs#L181) 与 [IfNode.cs:84](../plugins/FlowEngine.Plugins.Standard/IfNode.cs#L84) 各写一份；`GetJsonValue` 写在 `FilterNode` 但属于通用 JSON 路径访问；`ToClrValue` 挂在 `JsEngine` 上却是"脚本结果转换"                                                                   | 节点类承担了本属于脚本子系统的通用能力，复制即负债                                                      |
| 三套执行入口        | `js.Run(Code)`（[JSNode.cs:111](../plugins/FlowEngine.Plugins.Standard/JSNode.cs#L111)）、`ScriptEngine.EvaluateAsString`（[HttpRequestNode.cs:157](../plugins/FlowEngine.Plugins.Standard/HttpRequestNode.cs#L157)）、`ParameterResolver.EvaluateExpression`（[ParameterResolver.cs:94](../backend/FlowEngine.Runtime/Expressions/ParameterResolver.cs#L94)） | 行为略有差异，安全策略、缓存、错误处理各搞一套                                                          |
| Hint 标记不一致     | `HttpToolNode.Url` 只传 Properties 未指定 Hint（[HttpToolNode.cs:46](../plugins/FlowEngine.Plugins.Standard/HttpToolNode.cs#L46)）；`DataQualityNode.Rules` 标了 Expression 但实际是 JSON 数据（[DataQualityNode.cs:31](../plugins/FlowEngine.Plugins.Standard/DataQualityNode.cs#L31)）                                                                       | 表达式字段识别不可靠，依赖字符启发式                                                                    |

## 2. 设计目标

- 用一个**有语义的类型** `Script` 取代 `string`，内嵌「源码 + 语言 + 返回类型」。ReturnType 的主要作用是**驱动前端渲染**和**指导运行时结果转换**，不是编译期强类型契约：ReturnType=Dictionary → key-value 编辑器，ReturnType=Bool → switch + script 切换按钮，ReturnType=String → 单行/多行文本框。运行时 `ScriptResult.To<T>()` 会按 ReturnType 做最佳努力转换，但不对 JS 动态类型做硬性保证。
- 通过 `WithResolvedValue` 工厂方法，让框架对表达式参数预求值后写入 `ResolvedValue`，节点通过 `Url.GetResult<string>()` 直接取值，同时保持 Script 实例不可变（工厂创建新实例，不是 setter 变异）。
- 把"编译、求值、结果转换、类型归一"收敛进 `PreparedScript` / `PreparedScriptSession` / `ScriptResult`，节点只写一行求值调用。
- 把 `ToBoolean` / `ToClrValue` / `GetJsonValue` 等通用逻辑从节点类迁到 Core 脚本子系统，消除复制。
- **统一编译与执行管线**：三套执行入口归并为同一套 `PreparedScript` → `ScriptResult` 管线。**框架预求值**与**节点显式求值**是同一管线的两种调用时机，不是两套机制。
- 保留 Jint 沙箱边界不变：安全策略来自服务端配置，不随脚本载荷传输。
- **节点只依赖 `Script` 与 `ScriptResult`**：主取值路径是泛型 `script.EvaluateAsync<T>(context[, scope])`，直接拿强类型返回值；少数需判定成败或多次取值的节点用 `script.ExecuteAsync(...)` 拿 `ScriptResult`。`IScriptCache` / `PreparedScript` / `PreparedScriptSession` / `JsEngine`，以及缓存、引擎复用、逐项作用域、引擎释放，全部由 Core 门面与运行时透明承担，节点代码不得直接依赖（详见 §4.4）。

## 3. 核心类型定义

### 3.1 Script（定义类型，源码 + 语言 + 返回类型）

```csharp
namespace FlowEngine.Core.Scripting;

public enum ScriptLanguage { JavaScript }           // 预留多语言扩展点
public enum ScriptReturnType { String, Object, Bool, Number, Dictionary }

public sealed class Script
{
    // 持久化属性：Script 是定义，承载源码、语言和返回类型
    public string Source { get; init; }
    public ScriptLanguage Language { get; init; }
    public ScriptReturnType ReturnType { get; init; }

    // 运行时属性：框架通过 WithResolvedValue 工厂注入，不持久化
    [JsonIgnore]
    private readonly JsonNode? _resolvedValue;

    // 表达式参数：框架预求值后的结果（JsonNode）
    // 代码参数：null（应使用 RunAsync / Session 取运行结果）
    public JsonNode? ResolvedValue => _resolvedValue;

    public Script() { }

    internal Script(string source, ScriptLanguage language, ScriptReturnType returnType, JsonNode? resolvedValue = null)
    {
        Source = source;
        Language = language;
        ReturnType = returnType;
        _resolvedValue = resolvedValue;
    }

    // 强类型取值：基于 JsonNode 解析，T=string 时强制转换（数值/布尔亦可用）
    public T? GetResult<T>();

    // 工厂：创建"已解析版"实例（新对象，不是修改原对象）
    public Script WithResolvedValue(JsonNode? value) => new(Source, Language, ReturnType, value);

    public static Script Empty { get; }
        = new() { Source = "", Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.Object };

    /// <summary>
    /// 隐式转换：string → Script(Source=str, Language=JavaScript, ReturnType=Object)。
    /// 仅用于测试代码保持简洁；生产代码应显式指定 ReturnType。
    /// </summary>
    public static implicit operator Script(string source)
        => new() { Source = source, Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.Object };

    public override int GetHashCode() => HashCode.Combine(Source, Language, ReturnType);
    public override bool Equals(object? obj) => obj is Script s && Source == s.Source && Language == s.Language && ReturnType == s.ReturnType;
}
```

设计要点：

- **ReturnType 是渲染与转换提示**：一段脚本在定义时声明 ReturnType，主要供前端决定渲染组件，并指导 `ScriptResult.To<T>()` 的转换方向。由于 JS 是动态类型，不对源码做编译期类型保证；运行时若实际返回类型与 ReturnType 不符，按 `ScriptResult` 的转换规则最佳努力处理。
- **WithResolvedValue 是工厂不是变异**：框架调用 `script.WithResolvedValue(result)` 创建新实例，原 Script 不变。这不是 `ResolvedValue { set; }`，不存在"值对象被框架写入"的矛盾。
- **Source 与 ResolvedValue 分离**：
  - `Source` 永远表示用户编写的源码文本。
  - `ResolvedValue` 仅对 `Hint=Expression` 且框架预求值成功的参数非 null。
  - 代码参数（Hint=CodeEditor/Script）的 `ResolvedValue` 始终为 null，节点应通过 `PreparedScript.RunAsync` 取结果。
- **`.GetResult<T>()` 强类型取值**：对表达式参数，直接取已解析的 CLR 对象，不损失精度。门面收敛后（§4.4）此方法降为 `internal`，仅供 `ScriptResult.FromResolved` 复用其 JsonNode→T 转换逻辑；节点统一改用 `ScriptResult.To<T>()`。
- **Script.Empty 定义明确**：`Source = ""` 时 `IScriptCache.GetOrPrepare(Script.Empty)` 返回 no-op `PreparedScript`，其 `RunAsync` / `RunForItemAsync` 直接返回 `ScriptResult.Success(JsValue.Undefined)`。
- **隐式转换仅用于测试**：生产代码应显式构造 `Script` 并指定 `ReturnType`，避免类型推断隐藏错误。

### 3.2 PreparedScript（预编译产物，可跨节点复用）

```csharp
public sealed class PreparedScript
{
    public Script Original { get; }
    public string CacheKey { get; }            // SHA256(Source)

    // 单次执行（自建 JsEngine）：创建引擎，通过 ExecutionScope 注入全部变量，执行，返回结果
    public Task<ScriptResult> RunAsync(ScriptContext ctx, CancellationToken cancellationToken = default);

    // 单次执行（复用已存在的 JsEngine）：引擎须已通过 ExecutionScope 注入变量，仅执行求值
    public Task<ScriptResult> RunAsync(ScriptContext ctx, JsEngine engine, CancellationToken cancellationToken = default);

    // 逐项执行：返回 Session，引擎复用，全局变量注入一次，逐项覆盖 item 级变量
    public PreparedScriptSession CreateSession(JsEngine engine);
}
```

- **两阶段分离**：`IScriptCache.GetOrPrepare(script)` 负责编译（解析 AST、判定包裹、生成 `Jint.Prepared<Script>`），`PreparedScript` 负责执行。编译产物可跨节点、跨执行复用。
- **CacheKey**：`SHA256(Source)`，不含 Security/ReturnType 维度（安全策略不由 Script 携带，ReturnType 不同不影响编译产物复用）。
- **RunAsync 每次创建 JsEngine**：一次性场景天然只需一次执行。若节点需多次单次执行（如 HttpRequestNode 多脚本），应使用 `CreateSession`。

### 3.3 PreparedScriptSession（逐项 / 多脚本执行会话）

```csharp
public sealed class PreparedScriptSession : IDisposable
{
    // 绑定 JsEngine，全局变量已通过 ExecutionScope.ApplyGlobalVariables 注入

    // 在 Session 引擎上执行指定 PreparedScript（单次，不切换 item 作用域）
    public Task<ScriptResult> RunAsync(PreparedScript script, ScriptContext ctx, CancellationToken cancellationToken = default);

    // 在 Session 引擎上执行指定 PreparedScript，先切换 item 作用域再执行
    public Task<ScriptResult> RunForItemAsync(PreparedScript script, ScriptContext ctx, JsonNode? itemData, int itemIndex, CancellationToken cancellationToken = default);

    public void Dispose();
}
```

- **Session 接受 PreparedScript 参数**：每列有各自的 `PreparedScript`（AST 已缓存），传入 Session 共享引擎执行，AST 正确复用。
- **RunForItemAsync 全异步**：与 `RunAsync` 一致。
- **CreateSession 来源**：可从任意 `PreparedScript.CreateSession(engine)` 创建，Session 与特定 PreparedScript 不绑定；`ownsEngine=false` 时由调用方释放引擎（逐项复用同一引擎），`ownsEngine=true` 时 Session 释放时一并释放。

### 3.4 ScriptResult（统一结果模型）

```csharp
public sealed class ScriptResult
{
    public bool Success { get; }
    public JsValue Raw { get; }              // Jint 原始值
    public ScriptErrorException? Error { get; } // 结构化错误（消息 + 源码位置）

    // 失败时调用以下方法抛出 ScriptErrorException
    public object? ToClr();               // 原 JsEngine.ToClrValue 语义
    public bool ToBoolean();              // 共享的 ToBoolean
    public JsonNode? ToJson();
    public T? To<T>();                    // 强类型转换，参考 Original.ReturnType
}
```

- **失败时抛异常**：`Success == false` 时调用任何 `To*` 方法抛出 `ScriptErrorException`，不会静默返回默认值。
- `ToBoolean()` 取代 [FilterNode.cs:181](../plugins/FlowEngine.Plugins.Standard/FilterNode.cs#L181) 与 [IfNode.cs:84](../plugins/FlowEngine.Plugins.Standard/IfNode.cs#L84) 的两份私有实现。
- `ToClr()` 取代 `JsEngine.ToClrValue`（[JsEngine.cs:102](../backend/FlowEngine.Core/Scripting/JsEngine.cs#L102)）。
- `FromResolved(Script)`（internal）：把框架预求值写入的 `ResolvedValue`（`JsonNode`）包装为成功结果，使门面 `EvaluateAsync` 命中快路径时 `To<T>()` 的语义与执行路径完全一致（见 §4.4）。

### 3.5 ScriptContext（执行上下文，对齐 ExecutionScope）

```csharp
public sealed class ScriptContext
{
    public NodeExecutionContext NodeContext { get; init; }
    public IReadOnlyDictionary<string, object?>? ExtraGlobals { get; init; }

    public static ScriptContext From(NodeExecutionContext ctx);
}
```

- **不重新发明作用域注入**：`ScriptContext` 只持有 `NodeExecutionContext` 引用和可选额外全局变量。变量注入完全由已有的 `ExecutionScope`（[ExecutionScope.cs](../backend/FlowEngine.Core/Scripting/ExecutionScope.cs)）负责：
  - `ApplyGlobalVariables`：注入 `$credentials/$env/$workflow/$execution/$vars/$now/$today/$node/$ctx` 等
  - `ApplyItemScope`：注入 `$json/$input/$itemIndex/$runIndex`，构造 `InputContainer`
  - `ApplyNodeScope`：组合上述两者
- **变量名与现状一致**：`$json`、`$input`（InputContainer），不是裸 `input`。

### 3.6 安全策略（服务端配置，不随脚本载荷传输）

**核心原则：安全策略由服务端持有，不进入用户可控的 Script JSON。**

- **Script 不含 ScriptSecurity 字段**：当前安全策略来自全局 `JsEngineOptions`，不可被用户 JSON 覆盖。
- **Hint 级约定**：`Hint(CodeEditor)` 的参数自动使用 `JsEngineOptions.CodePreset`（较长超时等），`Hint(Expression)` 使用 `ExpressionPreset`。两个预设的字段取值范围在代码中硬编码为常量。
- **编译时校验**：`IScriptCache.GetOrPrepare(script)` 在编译 Source 时做全局黑名单校验，黑名单来自 `JsEngineOptions.ForbiddenIdentifiers`，与当前 `ParameterResolver.ValidateSecurity` 逻辑等价。
- **收紧校验强制**：`CodePreset` 的各字段值硬编码上限（如 `TimeoutMs` 上限 60000），构造函数校验并抛 `ArgumentOutOfRangeException`。

> **新增要求**：`JsEngineOptions` 需要新增 `IReadOnlySet<string> ForbiddenIdentifiers` 属性，把当前硬编码在 `ParameterResolver.cs:36-43` 的黑名单迁移到配置层，并通过 DI 注入到 `IScriptCache`（其内部编译入口使用 `IOptions<JsEngineOptions>`）。

## 4. 执行模型

### 4.1 自动包裹判定

`IScriptCache` 内部编译阶段通过 Acornima AST 分析源码：

```
if Source 为空:
    返回 no-op PreparedScript（执行时直接返回 undefined）
if 源码顶层是单个表达式（无语句）:
    包裹为 return (source);
else if 源码含 return 语句:
    包裹为 (function() { source })()
else:
    包裹为 (function() { source; return undefined; })()
```

### 4.2 两种求值时机

表达式参数与代码参数有不同的求值需求。本设计**统一底层管线**，**明确两种调用时机**。

| 维度     | 框架预求值（Hint=Expression）                             | 节点显式求值（Hint=CodeEditor/Script） |
| -------- | --------------------------------------------------------- | -------------------------------------- |
| 触发者   | NodeExecutionContextFactory                               | 节点 ExecuteAsync                      |
| 时机     | 节点执行前                                                | 节点执行中                             |
| 频率     | 每参数一次                                                | 可逐 item / 按需                       |
| 取值方式 | `await Url.EvaluateAsync<string>(ctx)`（命中快路径）      | `await script.EvaluateAsync<T>(ctx[, scope])` |
| 底层管线 | `IScriptCache.GetOrPrepare → RunAsync`（门面内部）        | 同左（门面内部）                       |

**统一点**：两者走同一条 `PreparedScript → ScriptResult` 管线，安全策略来自同一个 `JsEngineOptions`，缓存来自同一个 `ScriptCache`。

### 4.3 框架预求值

框架在 `NodeExecutionContextFactory` 阶段，对 `Hint(Expression)` 的 Script 参数预求值。框架已创建并复用单个 JsEngine（注入全部全局变量和当前 item 变量），避免每参数创建引擎的开销：

```
遍历节点参数：
├─ 参数是 Script 类型 且 Hint == Expression
│   ├─ _scriptCache.GetOrPrepare(script)
│   ├─ prepared.RunAsync(ctx, sharedEngine)        // 复用工厂已有的 JsEngine
│   ├─ result.ToClr() 得 CLR 对象
│   ├─ 通过 WithResolvedValue 创建新 Script 实例，替换 resolvedParameters[paramName]
├─ 参数是 Script 类型 且 Hint == CodeEditor/Script
│   └─ 跳过求值，Script 原样传递
└─ 参数是其他类型 → 现有逻辑
```

IIFE 包裹 + 严格模式隔离：表达式参数的 `return (expr)` 包裹无法声明变量，裸赋值 `a = 1` 在严格模式下抛 ReferenceError，避免脚本间全局污染。

**预求值失败处理**：框架捕获异常后，直接终止节点执行并返回结构化错误（错误码 + 源码位置 + 异常消息），**不再回退到 Source**。这样避免把错误处理责任推给每个节点，也避免静默错误。

### 4.4 节点求值门面与 API 示例

节点不应感知缓存、预编译产物、引擎复用与逐项作用域。这些全部收敛到 Core 门面。节点只做两件事：**声明 `Script` 属性**、**调用一次求值直接拿返回值**。

**关键取舍**：节点绝大多数场景要的是"脚本算出来的那个值"（`bool` / `string` / `JsonNode` / `Dictionary` / 原生 `object`），极少需要检查执行成败或对同一次结果做多种取值。因此**主入口是泛型的 `EvaluateAsync<T>`，直接返回 `T?`**；返回 `ScriptResult` 的 `ExecuteAsync` 仅作次要入口，供需要 `Success`/`Error` 判定或一次结果多次取值的少数节点使用。

#### 门面签名

```csharp
public static class ScriptEvaluationExtensions
{
    // 主入口（逐项求值）：传 JsonNode item 即按当前 item 逐项求值，框架注入标准 $json / $itemIndex；
    // 需要额外全局变量时通过 globals 传入（与上下文全局变量合并）。覆盖绝大多数节点。
    // - Expression 参数已被框架预求值：命中 Script.ResolvedValue，走零成本快路径（不建引擎、不执行）。
    // 内部等价于 (await script.ExecuteAsync(...)).To<T>()——取值逻辑仍集中在 ScriptResult，不产生第二套。
    public static Task<T?> EvaluateAsync<T>(
        this Script script,
        NodeExecutionContext context,
        JsonNode? item,
        int itemIndex = 0,
        (string Key, object? Value)[]? globals = null,
        CancellationToken cancellationToken = default);

    // 主入口（额外全局）：第二个参数直接传键值对即可，框架自动作为额外全局变量注入（无需任何作用域类型）。
    // 与逐项重载按第二个参数类型（JsonNode vs 键值对）自动区分。
    public static Task<T?> EvaluateAsync<T>(
        this Script script,
        NodeExecutionContext context,
        CancellationToken cancellationToken,
        params (string Key, object? Value)[] globals);

    // 次要入口（逐项 / 额外全局）：语义同上，返回原始 ScriptResult（判定 Success/Error 或一次结果多种取值）。
    public static Task<ScriptResult> ExecuteAsync(
        this Script script, NodeExecutionContext context, JsonNode? item,
        int itemIndex = 0, (string Key, object? Value)[]? globals = null, CancellationToken cancellationToken = default);
    public static Task<ScriptResult> ExecuteAsync(
        this Script script, NodeExecutionContext context, CancellationToken cancellationToken,
        params (string Key, object? Value)[] globals);
}
```

**节点无需感知任何作用域类型**：逐项求值传 `JsonNode`、额外全局直接传键值对，二者由第二个参数类型自动区分。节点侧示例：

```csharp
// 1) 普通参数表达式（无 item、无额外全局）——最常见
var url = await Url.EvaluateAsync<string>(context, cancellationToken);

// 2) 逐条 item 循环（自动注入 $json / $itemIndex）
foreach (var (item, index) in inputBatch.Items.WithIndex())
    if (await Condition.EvaluateAsync<bool>(context, item.Data, index, cancellationToken: cancellationToken))

// 3) 额外全局变量（第二个参数直接传键值对，ct 在前）
stop = await terminateScript.EvaluateAsync<bool>(context, cancellationToken,
    ("$cursor", cursor), ("$nextCursor", nextCursor), ("$page", page), ("$response", httpBody));

// 4) item + 额外全局（item 自动注入 $json，仅需补额外变量）
var result = await Code.EvaluateAsync<JsonNode>(context, currentItem,
    globals: new (string, object?)[] { ("$input", inputContainer) },
    cancellationToken: cancellationToken);
```

**取值收敛**：`EvaluateAsync<T>` 覆盖节点全部取值场景，因为 `ScriptResult.To<T>()` 内部已归一——`<bool>` 走 `ToBoolean` 真值语义、`<JsonNode>` 走 `ToJson`、`<object>` 走 `ToClr` 原生对象、`<string>` 走字符串转换。只有确需检查成败或复用同一结果时才用 `ExecuteAsync` 拿 `ScriptResult` 再自选 `To<T>()`/`ToClr()`/`ToJson()`/`ToBoolean()`。`EvaluateExpressionAsync<T>()` 删除（其职责由 `EvaluateAsync<T>` 承接），`Script.GetResult<T>()` 降为 `internal`（仅 `ScriptResult.FromResolved` 内部使用）。

#### 上下文托管引擎

两个入口内部均通过 `NodeExecutionContext` 懒创建并复用单个 `JsEngine`（逐项时复用同一会话作用域）。`NodeExecutionContext` 将该引擎登记为可释放资源，运行时（`WorkflowSchedulerKernel`）在节点执行结束（含重试循环结束）后统一释放。节点无需 `using`、无需 `CreateSession`、无需持有 `IScriptCache`。

#### 表达式参数（框架预求值，命中快路径）

```csharp
public sealed class IfNode : INodeType
{
    [Hint(PresentationHint.Expression)]
    public Script Condition { get; set; } = Script.Empty;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct = default)
    {
        bool ok = await Condition.EvaluateAsync<bool>(context, cancellationToken: ct);
        // ...
    }
}
```

`ShellToolNode.Command` / `SwitchNode.Expression` / `HttpRequestNode.Url` 等 Expression 字段同理：
`await Field.EvaluateAsync<string>(context, cancellationToken: ct)`。

#### 代码参数 + 多脚本

```csharp
public sealed class HttpRequestNode : INodeType
{
    [Hint(PresentationHint.Expression)]
    public Script Url { get; set; } = Script.Empty;

    [Hint(PresentationHint.Script)]
    public Script? HeadersExpression { get; set; }

    [Hint(PresentationHint.Script)]
    public Script? BodyExpression { get; set; }

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct = default)
    {
        var url = await Url.EvaluateAsync<string>(context, cancellationToken: ct);

        var headers = HeadersExpression is null ? null
            : await HeadersExpression.EvaluateAsync<Dictionary<string, string>>(context, cancellationToken: ct);
        var body = BodyExpression is null ? null
            : (await BodyExpression.EvaluateAsync<JsonNode>(context, cancellationToken: ct))?.ToJsonString();
        // 多脚本自动复用上下文托管的同一引擎，节点无需感知
    }
}
```

节点不再注入 `IScriptCache`，不再 `JsEngine.Create()` / `CreateSession()`。

#### 逐项执行

```csharp
public sealed class FilterNode : INodeType
{
    [Hint(PresentationHint.Script)]
    public Script Condition { get; set; } = Script.Empty;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct = default)
    {
        foreach (var (item, index) in inputBatch.Items.WithIndex())
        {
            if (await Condition.EvaluateAsync<bool>(context, item.Data, index, cancellationToken: ct))
                keptItems.Add(item);
        }
    }
}
```

> **破坏性变更（沿用旧设计）**：脚本错误经 `EvaluateAsync<bool>`（内部 `ToBoolean`）抛 `ScriptErrorException`，不再静默返回 `false`。

#### 多脚本逐项执行（按 key 直接用 Script）

```csharp
public sealed class DbUpsertNode : INodeType
{
    [Hint(PresentationHint.Expression)]
    public Script Connection { get; set; } = Script.Empty;

    [Hint(PresentationHint.Script)]
    public Dictionary<string, Script> Columns { get; set; } = [];

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct = default)
    {
        var connection = await Connection.EvaluateAsync<string>(context, cancellationToken: ct);

        foreach (var (item, index) in inputBatch.Items.WithIndex())
        {
            var values = new Dictionary<string, object?>();
            foreach (var (colName, script) in Columns)
                values[colName] = await script.EvaluateAsync<object>(context, item.Data, index, cancellationToken: ct);
            // <object> 内部走 ToClr，得到原生 int/double/string/bool/JsonNode
            // ...
        }
    }
}
```

不再有 `preparedColumns` 字典、`GetOrPrepare` 循环、`CreateSession`——按 key 直接用每个 `Script`。

### 4.5 缓存策略

```csharp
public interface IScriptCache
{
    PreparedScript GetOrPrepare(Script script);
    void TrimIfNeeded(int maxItems);
}

public sealed class ScriptCache : IScriptCache
{
    public const int DefaultMaxCapacity = 4096;

    private readonly ConcurrentDictionary<string, PreparedScript> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScriptErrorException> _compileErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _order = new();               // 加入顺序（LRU）
    private readonly Dictionary<string, LinkedListNode<string>> _orderIndex = new(StringComparer.OrdinalIgnoreCase); // O(1) 成员判定
    private readonly JsEngineOptions _options;

    public ScriptCache(IOptions<JsEngineOptions> options)
    {
        _options = options.Value;
    }

    public PreparedScript GetOrPrepare(Script script)
    {
        var key = ComputeCacheKey(script.Source);
        if (_compileErrors.TryGetValue(key, out var error))           // 编译失败已缓存：直接返回，避免重复编译
            return new PreparedScript(script, key, NoOp, error);

        if (!_cache.ContainsKey(key))
            ValidateSecurity(script);                                 // 安全校验仅首次编译执行（编译时一次）

        var prepared = _cache.GetOrAdd(key, _ => ScriptCompiler.Compile(script));
        RecordAccess(key);                                            // 容量超 DefaultMaxCapacity 时按加入顺序淘汰最旧条目
        return new PreparedScript(script, key, prepared);
    }

    public void TrimIfNeeded(int maxItems)
    {
        // maxItems<=0：清空全部；否则按加入顺序淘汰最旧条目至 maxItems
    }
}
```

- **缓存键**：`SHA256(Source)`（不含 ReturnType——不同 ReturnType 不影响编译产物复用）。
- **生命周期**：进程级，无过期。容量超 `DefaultMaxCapacity`(4096) 时按加入顺序**（LRU）淘汰最旧条目**，避免 `ConcurrentDictionary` 无界增长；安全校验仅在首次编译执行一次。
- **替代关系**：取代 `ScriptEngine.ExpressionCache` 和 `ParameterResolver` 本地缓存。
- **依赖注入**：`IScriptCache` 以单例注入，避免静态字典在测试/多租户场景下互相污染；`IScriptCache` 内部编译入口使用 `IOptions<JsEngineOptions>` 获取安全策略。

## 5. 持久化与序列化

### 5.1 JSON 线格式

```json
{
  "url": { "source": "'https://api.com/' + $json.path", "language": "JavaScript", "returnType": "String" },
  "code": { "source": "return $input.all().map(x => x.id)", "returnType": "Object" },
  "condition": { "source": "$json.status === 200", "returnType": "Bool" },
  "headers": { "source": "return { 'Content-Type': 'application/json' }", "returnType": "Dictionary" }
}
```

- **source + language + returnType**：无 security 字段。
- `language` 与默认一致时可省略：`{ "source": "$json.id", "returnType": "String" }`。
- `returnType` 与默认（String）一致时也可省略：`{ "source": "$json.id" }`。
- 纯字符串简写：`"url": "https://api.com"` → 自动反序列化为 `Script { Source = "https://api.com", ReturnType = String }`。

### 5.2 JsonConverter

```csharp
public sealed class ScriptJsonConverter : JsonConverter<Script>
{
    // 序列化：Script → { source, language?, returnType? }
    // 反序列化：JSON 对象 → Script；纯字符串 → Script(source, ReturnType=String) 简写
}
```

> **向后兼容**：旧工作流中脚本字段是纯字符串。本次设计不自动迁移；加载旧工作流时，纯字符串会被 `ScriptJsonConverter` 反序列化为 `ReturnType=String` 的 Script。但字段语义可能变化（例如原本 Bool 表达式会被当 String 处理），因此旧工作流必须重新保存或离线迁移。

## 6. 反射注册调整

### 6.1 ParameterDiscoverer.InferParameterType

```csharp
if (effectiveType == typeof(Script))
{
    var hint = hintAttr?.Component ?? PresentationHint.Expression;
    return (ParameterType.Script, hint);
}

if (effectiveType.IsGenericType
    && effectiveType.GetGenericTypeDefinition() == typeof(Dictionary<>)
    && effectiveType.GetGenericArguments() is [typeof(string), typeof(Script)])
{
    return (ParameterType.Json, PresentationHint.KeyValueEditor);
}
```

- `Script` 类型对应 `ParameterType.Script`。
- `Dictionary<string, Script>` 仍映射为 `ParameterType.Json` + `KeyValueEditor`，由 `ParameterHydrator` 在反序列化时把值转为 `Script`。
- 不再需要从 Hint Properties 读取 returnType——returnType 已内嵌在 Script 自身。但 Hint Properties 中仍保留 `language` 等扩展信息。

### 6.2 ParameterHydrator.ConvertValueAsync

新增 `Script` 与 `Dictionary<string, Script>` 分支：

```csharp
if (underlying == typeof(Script))
{
    return value switch
    {
        Script s => s,
        string str => new Script { Source = str, Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String },
        JsonElement element => element.Deserialize<Script>(JsonDefaults.Options),
        JsonNode node => node.Deserialize<Script>(JsonDefaults.Options),
        _ => null
    };
}

if (underlying.IsGenericType
    && underlying.GetGenericTypeDefinition() == typeof(Dictionary<,>)
    && underlying.GetGenericArguments() is [typeof(string), typeof(Script)])
{
    // 遍历 JSON 对象的每个属性值，递归转换为 Script
    var dict = value switch
    {
        JsonElement element => element.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options),
        JsonNode node => node.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options),
        string str => JsonSerializer.Deserialize<Dictionary<string, Script>>(str, JsonDefaults.Options),
        _ => null
    };
    return dict;
}
```

### 6.3 NodeExecutionContextFactory 调整

当前工厂（[NodeExecutionContextFactory.cs:54-74](file:///d:/Repos/flow_engine/backend/FlowEngine.Runtime/Executor/NodeExecutionContextFactory.cs#L54-L74)）按参数名把 `CodeEditor`/`Script` 的 string 参数跳过，再由 `ParameterResolver` 对所有 string 做启发式求值。改为 Script 类型后，流程调整为：

```
遍历节点参数：
├─ 参数是 Script 类型 且 Hint == Expression
│   ├─ _scriptCache.GetOrPrepare(script).RunAsync(ctx)
│   ├─ result.ToClr() 得 CLR 对象
│   ├─ 通过 WithResolvedValue 创建新 Script 实例，替换 resolvedParameters[paramName]
├─ 参数是 Script 类型 且 Hint == CodeEditor/Script
│   └─ 跳过求值，Script 原样传递
├─ 参数是 Dictionary<string, Script>
│   └─ 递归处理每个值（Expression 预求值，Script/CodeEditor 跳过）
└─ 参数是其他类型 → 现有逻辑
```

`ParameterHydrator` 随后将 `resolvedParameters` 绑定到节点属性时，表达式参数的 Script 已经是 `WithResolvedValue` 后的实例，节点读 `.GetResult<T>()` 即可。

## 7. 前端 DTO 与编辑器

### 7.1 TypeScript 类型

```typescript
export interface Script {
  source: string
  language: "JavaScript"
  returnType: "String" | "Object" | "Bool" | "Number" | "Dictionary"
}
```

后端 `ParameterType` 枚举中的 `Script` 需要同步到前端：

```typescript
export type ParameterType =
  | "String"
  | "Number"
  | "Boolean"
  | "Options"
  | "Json"
  | "Code"
  | "Credential"
  | "Resource"
  | "Array"
  | "File"
  | "Expression"
  | "Script" // 新增
```

### 7.2 编辑器组件

Script 类型参数统一走 `ExpressionField` 组件（后续可改名为 `ScriptField`），由 `returnType` 和 `hint` 共同驱动渲染模式：

```
Script 类型参数 → 统一走 ScriptField 组件：
├─ hint == Expression 且 returnType == String → 单行表达式输入框
├─ hint == Script / CodeEditor → 多行代码编辑器
├─ returnType == Bool → switch 开关 + script 切换按钮（默认 switch，点 script 图标展开代码编辑器）
├─ returnType == Dictionary → key-value 编辑器 + script 切换按钮（默认 key-value 模式，点 script 图标切换为代码编辑器）
├─ returnType == Number → 数字输入框 + script 切换按钮
└─ returnType == Object → 多行 CodeMirror 编辑器

Hint 仍保留，用于框架决定预求值行为（Expression → 预求值，CodeEditor/Script → 不预求值）。
```

`ExpressionField` 的 `value` 需要从 `string` 改为 `Script | string`（兼容旧数据），`onChange` 输出完整的 `Script` 对象。

## 8. 现有节点影响清单

| 节点                | 字段              | 原类型                     | 新类型                     | ReturnType | Hint       | 求值方式                                  | 备注                                             |
| ------------------- | ----------------- | -------------------------- | -------------------------- | ---------- | ---------- | ----------------------------------------- | ------------------------------------------------ |
| JSNode              | Code              | string                     | Script                     | Object     | CodeEditor | 显式 Session                              |                                                  |
| CodeSnippetToolNode | Code              | string                     | Script                     | Object     | CodeEditor | 显式 RunAsync                             |                                                  |
| HttpRequestNode     | Url               | string                     | Script                     | String     | Expression | 框架预求值，`Url.GetResult<string>()`     |                                                  |
| HttpRequestNode     | HeadersExpression | string?                    | Script?                    | Dictionary | Script     | 显式 Session，`.To<Dictionary>()`         | 前端：key-value + script 切换                    |
| HttpRequestNode     | BodyExpression    | string?                    | Script?                    | Object     | Script     | 显式 Session                              |                                                  |
| HttpToolNode        | Url               | string                     | Script                     | String     | Expression | 框架预求值                                | 补 Hint=Expression                               |
| HttpToolNode        | HeadersExpression | string?                    | Script?                    | Dictionary | Script     | 显式 Session                              | 修正 Hint=Script                                 |
| HttpToolNode        | BodyExpression    | string?                    | Script?                    | Object     | Script     | 显式 Session                              | 修正 Hint=Script                                 |
| ShellToolNode       | Command           | string                     | Script                     | String     | Expression | 框架预求值，`Command.GetResult<string>()` |                                                  |
| IfNode              | Condition         | string                     | Script                     | Bool       | Expression | 框架预求值，`Condition.GetResult<bool>()` | 前端：switch + script 切换                       |
| FilterNode          | Condition         | string                     | Script                     | Bool       | Script     | 显式 Session 逐项                         | 前端：switch + script 切换；脚本错误不再静默吞掉 |
| SwitchNode          | Expression        | string                     | Script                     | String     | Expression | 框架预求值                                | **修复未求值 bug**                               |
| DbUpsertNode        | Connection        | string                     | Script                     | String     | Expression | 框架预求值                                | 移除 ResolveConnection                           |
| DbUpsertNode        | Columns           | Dictionary<string, string> | Dictionary<string, Script> | String     | Script     | 显式 Session 逐项逐列                     |                                                  |
| DataQualityNode     | Rules             | string                     | JsonNode                   | -          | JsonEditor | -                                         | **改类型**，非脚本，作为独立任务                 |
| SetNode             | Fields            | List<SetField>             | 不变                       | -          | -          | -                                         | **保留**，不在本次删除范围内                     |

## 9. 通用逻辑归位清单

| 当前位置            | 逻辑                                  | 迁往                                                                                                  |
| ------------------- | ------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| FilterNode + IfNode | `ToBoolean`                           | `ScriptResult.ToBoolean()`                                                                            |
| JsEngine            | `ToClrValue`                          | `ScriptResult.ToClr()`                                                                                |
| FilterNode          | `GetJsonValue`                        | Core 新增 `JsonPath`                                                                                  |
| ScriptEngine 全部   | `Evaluate*` 系列                      | 已由 `PreparedScript.RunAsync` + `ScriptResult.To*` 替代；`ScriptEngine` 已物理删除（见 plan-cleanup-01-obsolete-markers.md） |
| ParameterResolver   | `EvaluateExpression` + `IsExpression` | 简化为"Script 参数 + ScriptCache"；保留处理非 Script 字符串的旧逻辑作为兼容层                         |
| ScriptEvaluationExtensions | `EvaluateExpressionAsync<T>`   | 删除，由主入口 `Script.EvaluateAsync<T>(...)`（直接返回 `T?`）取代；`ExecuteAsync` 作次要入口返回 `ScriptResult` |
| Script              | `GetResult<T>`（public）              | 降为 `internal`，仅供 `ScriptResult.FromResolved` 复用；节点改用 `EvaluateAsync<T>`                    |
| 各节点              | `IScriptCache` / `PreparedScript` / `PreparedScriptSession` / `JsEngine.Create` 直接依赖 | 全部收敛进门面 `EvaluateAsync` + `NodeExecutionContext` 托管引擎，运行时统一释放                       |

## 10. 安全边界

- **安全策略由服务端持有**：`JsEngineOptions` 是唯一的安全配置来源，需新增 `ForbiddenIdentifiers`。
- **Hint 级约定**：`CodeEditor` → `CodePreset`，`Expression` → `ExpressionPreset`，硬编码上限。
- **编译时校验**：`IScriptCache` 在编译 Source 时做全局黑名单校验，黑名单来自 `JsEngineOptions.ForbiddenIdentifiers`。
- **JsEngine 沙箱不变**。

## 11. 风险与待定项

| #   | 风险                              | 应对                                                                               |
| --- | --------------------------------- | ---------------------------------------------------------------------------------- |
| 1   | 预求值时机                        | 保持 NodeExecutionContextFactory 变量注入顺序，预求值在注入之后                    |
| 2   | Dictionary<string, Script> 反射   | §6.1 / §6.2 已给出具体分支代码                                                     |
| 3   | SwitchNode.Expression 未求值      | 改为 Script + Hint(Expression)，框架预求值修复                                     |
| 4   | DataQualityNode.Rules 类型变更    | 独立小改动，不纳入 Script 改造主阶段                                               |
| 5   | Session 非线程安全                | 限单次执行内、单线程                                                               |
| 6   | ScriptCache 容量                  | TrimIfNeeded，上限 4096；改为注入式 `IScriptCache` 便于测试                        |
| 7   | InputHelper 分歧                  | Out of Scope                                                                       |
| 8   | FilterNode 脚本错误行为变更       | 从静默返回 false 改为抛异常；需前端/文档说明                                       |
| 9   | IfNode 从 ResolvedParameters 迁移 | 改为 Script 属性 + GetResult<bool>()                                               |
| 10  | 旧工作流兼容性                    | 本次不自动迁移；旧字符串格式会被反序列化为 ReturnType=String，需重新保存或离线迁移 |
| 11  | 连接条件 ConnectionDto.Condition  | 是否纳入 Script 改造需单独决策；本次默认不纳入                                     |

## 12. 范围与边界（Out of Scope）

- **向后兼容 / 自动迁移**：迁移脚本为独立任务。
- **InputHelper 分歧**：统一走 ExecutionScope（InputContainer）是单独任务。
- **函数式写法**：`ctx => expr` 目标写法不在本期范围。
- **SetNode 删除**：保留 SetNode，如需移除需先设计等价替代节点。
- **物理删除 ScriptEngine**：已随 plan-cleanup-01-obsolete-markers.md 物理删除（用户明确授权：开发阶段无需向后兼容）。

## 13. 落地阶段

| 阶段 | 目标                           | 关键交付                                                                                                                           |
| ---- | ------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------- |
| 一   | Core 类型与序列化              | `Script` / `PreparedScript` / `PreparedScriptSession` / `ScriptResult` / `ScriptContext` + `JsonConverter` + `IScriptCache` + 单测 |
| 二   | 单节点试点                     | IfNode + FilterNode 迁移；验证 ParameterHydrator、NodeExecutionContextFactory 预求值、ScriptCache、错误处理                        |
| 三   | 通用逻辑归位                   | `ToBoolean` / `ToClrValue` → `ScriptResult`；`GetJsonValue` → Core `JsonPath`；`ScriptEngine` 标记 `[Obsolete]`                    |
| 四   | 中间层完善                     | `ParameterResolver` 简化；`JsEngineOptions.ForbiddenIdentifiers`；`NodeExecutionContextFactory` 完整适配                           |
| 五   | 全量节点迁移                   | HttpRequestNode / HttpToolNode / ShellToolNode / JSNode / CodeSnippetToolNode / SwitchNode / DbUpsertNode                          |
| 六   | DataQualityNode.Rules 类型变更 | string → JsonNode，独立交付                                                                                                        |
| 七   | 前端 DTO 与编辑器              | TypeScript 类型；ExpressionField 支持 Script 对象                                                                                  |

## 14. 变更记录

| 日期       | 修改人 | 修改内容                                                                                                                                                                                                                                                                                                                              |
| ---------- | ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-07-10 | Agent  | 初版：定义 Script / ScriptResult 抽象、节点属性改造                                                                                                                                                                                                                                                                                   |
| 2026-07-10 | Agent  | v2：移除 ScriptKind；新增 PreparedScript 两阶段 + Session                                                                                                                                                                                                                                                                             |
| 2026-07-10 | Agent  | v3：基于 16 项评审重构——Script 纯不可变、安全回归服务端、对齐 ExecutionScope、Session 接受 PreparedScript、全异步、缓存容量保护                                                                                                                                                                                                       |
| 2026-07-10 | Agent  | v4：新增 WithResolvedValue 工厂 + .Value / .As<T>() API；HeadersExpression/BodyExpression 改为节点显式执行（Hint=Script）而非框架预求值                                                                                                                                                                                               |
| 2026-07-10 | Agent  | v5：恢复 ScriptReturnType（定义时返回类型即定死；编译时验证 + 驱动前端渲染：Bool→switch+script 切换，Dictionary→key-value+script 切换）                                                                                                                                                                                               |
| 2026-07-10 | Agent  | v6：Grill-me 评审后更新——CacheKey 改为 SHA256(Source) 不含 ReturnType；新增 RunAsync(ctx, engine?) 引擎复用重载；合并阶段四-六-七为全量节点迁移；移除 SetNode；添加 implicit operator string→Script                                                                                                                                   |
| 2026-07-10 | Agent  | v7：源码调研后修订——弱化 ReturnType 为渲染/转换提示；删除移除 SetNode 提议；明确 Source/ResolvedValue 分离；预求值失败直接失败；补充 ParameterHydrator/ParameterDiscoverer/NodeExecutionContextFactory/JsEngineOptions 改造细节；前端 ParameterType 扩展；阶段划分为基础设施→单节点试点→全量迁移；ScriptEngine 标记 Obsolete 而非删除 |
| 2026-07-10 | Agent  | v8：新增节点求值门面 `Script.EvaluateAsync`/`ExecuteAsync`，收敛节点对 `IScriptCache`/`PreparedScript`/`PreparedScriptSession`/`JsEngine` 的直接依赖；新增 `ScriptResult.FromResolved` 统一取值语义；`GetResult<T>` 降为 internal、删除 `EvaluateExpressionAsync<T>`；引擎复用改由 `NodeExecutionContext` 托管、运行时执行后释放；节点无需感知任何作用域类型（逐项传 `JsonNode`、额外全局直接传键值对，按第二个参数类型自动区分）；§4.4 示例与 §4.2 取值方式、§9 归位清单同步更新 |
| 2026-07-10 | Agent  | v9：门面主入口改为泛型 `EvaluateAsync<T>` 直接返回 `T?`（节点绝大多数只要返回值），`ScriptResult` 经次要入口 `ExecuteAsync` 返回（仅判定成败/多次取值时用）；§2、§4.2、§4.4、§9 全部示例统一为 `await script.EvaluateAsync<T>(...)` |
| 2026-07-10 | Agent  | v10：物理删除 `ScriptEngine` 整类（用户明确授权，开发阶段无需向后兼容）；归位清单与待定项同步更新为"已删除"，详见 plan-cleanup-01-obsolete-markers.md |
