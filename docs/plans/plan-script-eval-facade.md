# 开发计划：Script 求值 Facade（plan-script-eval-facade）

## 1. 概述

消除 Plugin Node 对 `IScriptCache` / `GetOrPrepare` / `PreparedScript` / `PreparedScriptSession` / `JsEngine` 的直接依赖。

**当前问题：** 7 个 plugin node 在 `ExecuteAsync` 中重复以下样板代码：

```csharp
var scriptCache = context.GetScriptCache();
var prepared = scriptCache.GetOrPrepare(script);
var result = await prepared.RunAsync(ScriptContext.From(context), ct);
```

节点不应该关心"脚本如何编译缓存"这个基础设施问题。

**目标：** 节点只需要：
1. 声明哪些属性是 `Script` 类型（已在参数描述符中定义）
2. 调用 `script.EvaluateAsync<T>(context[, scope], ct)` 直接获取强类型返回值

> 设计真相源见架构文档 [script-type.md §4.4](../architecture/script-type.md)。本计划只描述"做什么/怎么验收"，签名与语义以架构文档为准。

**不覆盖范围：**
- 不改动 `ScriptCache` / `PreparedScript` / `PreparedScriptSession` 内部实现
- 不改动 `NodeExecutionContextFactory` 的预求值流程
- 不涉及前端、测试框架变更

## 2. 交付物清单

| 交付物 | 类型 |
|--------|------|
| `Core/Scripting/ScriptEvaluationExtensions.cs` 改造 — 主入口 `EvaluateAsync<T>` 返回 `T?`、次要入口 `ExecuteAsync` 返回 `ScriptResult`；按第二参数类型区分逐项（`JsonNode item`）/ 额外全局（`params` 键值对），节点无需感知作用域类型 | 代码 |
| `ScriptResult.FromResolved(Script)`（internal）+ `Script.GetResult<T>()` 降为 internal + 删除 `EvaluateExpressionAsync<T>` | 代码 |
| `NodeExecutionContext` 托管单个 `JsEngine`（懒建、复用、登记释放），运行时执行后统一释放 | 代码 |
| 7 个 plugin node 的 ScriptCache / JsEngine / Session 引用消除 | 代码 |
| `IScriptCache.GetScriptCache()` 扩展标记 `[Obsolete]` | 代码 |
| 对应测试覆盖新扩展方法 | 测试 |
| `dotnet build` + `dotnet test` 通过 | 验证 |

## 3. 开发阶段

### 阶段一：Core 扩展方法

**目标：** 提供消除样板代码的扩展方法，且节点无需知道任何作用域类型。

新增两个入口到 `FlowEngine.Core/Scripting/`（完整签名见架构文档 §4.4），均提供「逐项」与「额外全局」两种重载，由第二个参数类型自动区分：

```csharp
// 主入口（逐项）：传 JsonNode item 即按 item 求值，注入标准 $json / $itemIndex
Task<T?> EvaluateAsync<T>(this Script script, NodeExecutionContext context, JsonNode? item,
    int itemIndex = 0, (string Key, object? Value)[]? globals = null, CancellationToken ct = default);

// 主入口（额外全局）：第二个参数直接传键值对，ct 在前
Task<T?> EvaluateAsync<T>(this Script script, NodeExecutionContext context,
    CancellationToken ct, params (string Key, object? Value)[] globals);

// 次要入口 ExecuteAsync 同形态双重载，返回 ScriptResult
```

- 节点零作用域认知：逐项传 `JsonNode`、额外全局直接传键值对，无需 `ScriptScope` 或字典。
- 不引入 `EvaluateAllAsync`：`Dictionary<string, Script>` 由节点按 key 循环调用 `EvaluateAsync<T>`。
- `EvaluateAsync<T>` 内部 = `(await ExecuteAsync(...)).To<T>()`，取值逻辑集中在 `ScriptResult`。
- 删除 `EvaluateExpressionAsync<T>`（职责由 `EvaluateAsync<T>` 承接）；`Script.GetResult<T>()` 降 internal，仅 `ScriptResult.FromResolved` 复用。

`ExecuteAsync` 内部逻辑：

```
1. script.ResolvedValue 非空 → ScriptResult.FromResolved(script) 短路返回（不建引擎、不执行）
2. 否则复用 NodeExecutionContext 托管的单个 JsEngine：GetOrPrepare → RunAsync(ScriptContext.From(context, scope), ct)
3. 返回 ScriptResult
```

**验收标准：**
- 扩展方法有单元测试覆盖：正常执行、空 Script、编译错误、`ResolvedValue` 短路、`ScriptScope.ForItem` 逐项注入、`ScriptScope.With` 额外全局注入、`EvaluateAsync<bool>`/`<JsonNode>`/`<object>`/`<string>` 取值语义
- `EvaluateExpressionAsync<T>` 已删除、`GetResult<T>` 不再 public（编译期确认无外部调用）
- `dotnet build` + `dotnet test` 通过

### 阶段二：迁移 Plugin Node

**目标：** 消除 7 个 node 中的 ScriptCache 直接引用。

逐节点替换模式：

#### CalculatorToolNode（低）
```
- var scriptCache = context.GetScriptCache();
- var prepared = scriptCache.GetOrPrepare(script);
- var result = await prepared.RunAsync(ScriptContext.From(context), ct);
+ var result = await script.EvaluateAsync<JsonNode>(context, cancellationToken: ct);
```

#### CodeSnippetToolNode（低）
```
- var scriptCache = context.GetScriptCache();
- var prepared = scriptCache.GetOrPrepare(Code);
- var result = await prepared.RunAsync(scriptContext, ct);
+ var result = await Code.ExecuteAsync(context, ct, ("input", inputData));  // 需 ScriptResult 多次取值
```

#### JSNode（低）
```
- var scriptCache = context.GetScriptCache();
- var prepared = scriptCache.GetOrPrepare(Code);
- delegate to ExecuteForEachItem / ExecuteForAllItems with prepared
+ var result = await Code.EvaluateAsync<JsonNode>(context, item.Data, index, cancellationToken: ct);  // 循环内
```

#### HttpNodeExecution（低）
```
- var scriptCache = context.GetScriptCache();
- var prepared = scriptCache.GetOrPrepare(headersExpression);
- session.RunAsync(...)
+ var headersResult = await headersExpression.EvaluateAsync<Dictionary<string,string>>(context, cancellationToken: ct);
```

#### PaginateNode（低）
```
- var scriptCache = context.ScriptCache ?? new ScriptCache(Options.Create(...));
- var preparedTerminate = scriptCache.GetOrPrepare(terminateScript);
- session
+ var stop = await terminateScript.EvaluateAsync<bool>(context, ct, ("$cursor", cursor), ("$nextCursor", nextCursor), ("$page", page), ("$response", httpBody));
```

#### FilterNode（中）
```
- var scriptCache = context.GetScriptCache();
- var preparedCondition = scriptCache.GetOrPrepare(Condition);
- var conditionSession = preparedCondition.CreateSession(engine);
- per-item: conditionSession.RunAsync(preparedCondition, ...)
+ per-item: await Condition.EvaluateAsync<bool>(context, item.Data, index, cancellationToken: ct);
```

去掉外部 engine 和 session 管理。

#### DbUpsertNode（中）
```
- var scriptCache = context.GetScriptCache();
- var preparedColumns = Columns.ToDictionary(c => c.Key, c => scriptCache.GetOrPrepare(c.Value));
- engine + session + per-item eval
+ per-item per-column: await colScript.EvaluateAsync<object>(context, item.Data, index, cancellationToken: ct);
```

**验收标准：**
- 每个节点替换后行为不变（执行结果等价）
- 所有涉及节点的测试通过
- 代码中不再出现 `GetScriptCache()` 调用
- ScriptCache 相关 using 从节点文件中移除

### 阶段三：收尾

1. 将 `ScriptCacheContextExtensions.GetScriptCache()` 标记为 `[Obsolete]`（保留门面内部使用）
2. 删除 `EvaluateExpressionAsync<T>`（调用点已在阶段二全部迁移到 `EvaluateAsync<T>`）；`Script.GetResult<T>()` 降为 `internal`
3. 确认 `NodeExecutionContext.ScriptCache` 与托管 `JsEngine` 从节点感知中消失（仅由门面内部使用）
4. `dotnet build` + `dotnet test` 全量通过

## 4. 阶段依赖图

```mermaid
graph LR
    A[阶段一: Core 扩展方法] --> B[阶段二: 迁移 Plugin Node]
    B --> C[阶段三: 收尾 Obsolete 标记]
```

阶段二内部的 7 个节点可以并行迁移。

## 5. 风险与待定项

| 风险 | 影响 | 应对 |
|------|------|------|
| 逐项场景引擎复用 | FilterNode/DbUpsertNode 若每项新建引擎会变慢 | 引擎由 `NodeExecutionContext` 托管，节点执行期间复用同一实例；逐项复用同一会话作用域，仅换 `$json`/`$itemIndex` |
| 额外全局变量合并到 ScriptContext 而非冲突 | 变量覆盖行为可能变化 | `ScriptContext.From(context, globals)` 先注入 base globals，再叠加 `globals`，与当前 `ScriptContext(context, extraGlobals)` 行为一致 |
| `EvaluateExpressionAsync<T>` 已有使用者 | 直接删除会破坏编译 | 阶段二逐调用点替换为 `EvaluateAsync<T>` 后，阶段三再物理删除，保证任一提交都可编译 |
| 托管 `JsEngine` 释放时机 | 释放过早/过晚导致逐项失败或泄漏 | 运行时（`WorkflowSchedulerKernel`）在节点执行结束（含重试循环结束）后统一释放，`NodeExecutionContext` 登记为可释放资源 |

## 6. 验收总标准

- [ ] 所有扩展方法有测试覆盖
- [ ] 7 个 plugin node 不再出现 `GetScriptCache()` / `GetOrPrepare` / `PreparedScript` / `PreparedScriptSession` / `JsEngine` 的直接引用
- [ ] `GetScriptCache()` 扩展已标记 `[Obsolete]`
- [ ] `dotnet build` 全量通过
- [ ] `dotnet test` 全量通过（无回归）
- [ ] 节点代码量平均每节点减少 4-8 行样板代码

## 修改记录

| 日期 | 修改人 | 修改内容 |
|------|--------|----------|
| 2026-07-10 | Agent | 初版 |
| 2026-07-10 | Agent | 对齐架构文档 v9：主入口改泛型 `EvaluateAsync<T>` 直接返回 `T?`，`ExecuteAsync` 返回 `ScriptResult` 为次要入口；用「逐项传 JsonNode / 额外全局传键值对（按第二参数类型区分）」取代 `ScriptScope`/`extraGlobals` 重载与 `EvaluateAllAsync`；`EvaluateExpressionAsync<T>` 由委托改为阶段二迁移+阶段三删除；引擎复用改为 `NodeExecutionContext` 托管 |
