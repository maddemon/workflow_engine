# 任务记录：Script 求值 Facade 实施

- 计划文档：`docs/plans/plan-script-eval-facade.md`
- 架构真相源：`docs/architecture/script-type.md` §4.4（v9）
- 状态：✅ 阶段一~三全部完成，构建与测试通过

## 实施内容

### 阶段一：Core 扩展方法
- 重写 `Core/Scripting/ScriptEvaluationExtensions.cs`：
  - 主入口 `EvaluateAsync<T>` 双重载（按第二个参数类型自动区分）：传 `JsonNode item` = 逐项求值（注入 $json/$itemIndex）；传 `cancellationToken, params (string,object?)[] globals` = 额外全局变量。节点无需感知任何作用域类型。
  - 次要入口 `ExecuteAsync` 同形态双重载，返回 `ScriptResult`（需 Success/Error 或多次取值时）。
  - 预求值路径（命中 `Script.ResolvedValue`）走零引擎零执行快路径。
  - 删除旧 `EvaluateExpressionAsync<T>`。
- （曾引入 `ScriptScope` 值对象，后据评审移除：节点不必知道作用域类型，改由参数类型区分。）
- `ScriptResult` 新增内部 JsonNode 构造 + `FromResolved(Script)` + 惰性 `ResolveRaw()`（线程内复用单引擎把 JsonNode 转为 JsValue，复用统一取值语义）。
- `NodeExecutionContext` 托管单个 `JsEngine`：`GetOrCreateEngine()` / `ReleaseEngine()` + 注入 `EngineOptions` / `EngineLogger`。
- 工厂 `NodeExecutionContextFactory` 注入 `EngineOptions` / `EngineLogger`；运行时 `WorkflowSchedulerKernel` 在节点执行（含重试）结束后统一 `ReleaseEngine()`。

### 阶段二：迁移 Plugin Node（10 个，计划 7 + 同模式 3）
- 计划内 7 个：`CalculatorToolNode`、`CodeSnippetToolNode`、`JSNode`、`HttpNodeExecution`、`PaginateNode`、`FilterNode`、`DbUpsertNode`。
- 计划外同模式 3 个（同样调用 `GetResult<T>`，为阶段三 internal 化铺路）：`IfNode`、`SwitchNode`、`ShellToolNode`。
- 全部改为 `await script.EvaluateAsync<T>(context, item, index, ct)` / `EvaluateAsync<T>(context, ct, ("k", v)…)` / `ExecuteAsync(...)`，逐项传 `JsonNode`、额外全局直接传键值对，移除 `GetScriptCache` / `GetOrPrepare` / `PreparedScript` / `PreparedScriptSession` / `JsEngine` 直接引用。

### 阶段三：收尾
- `Script.GetResult<T>()` 降为 `internal`（无生产调用者；测试经 `InternalsVisibleTo` 仍可用）。
- `ScriptCacheContextExtensions.GetScriptCache()` 标记 `[Obsolete]`（门面内部调用以 `#pragma warning disable CS0618` 抑制，因 `TreatWarningsAsErrors=true`）。
- `FlowEngine.Core.csproj` 增加 `InternalsVisibleTo`：`FlowEngine.Core.Tests`（原有）、`FlowEngine.Runtime.Tests`（新增，供 `ScriptIntegrationTests`）。

## 验证

- `dotnet build FlowEngine.sln`：**0 警告 / 0 错误**（`TreatWarningsAsErrors=true`）。
- `dotnet test FlowEngine.sln`：**738 通过 / 0 失败**（Core 147 + Application 255 + Runtime 240 + Host 96）。
- 新增 `tests/FlowEngine.Core.Tests/Scripting/ScriptEvaluationExtensionsTests.cs`：11 个用例覆盖正常执行、空脚本、编译错误、ResolvedValue 短路、ForItem 逐项注入、With 额外全局注入、bool/JsonNode/object/string 取值语义、托管引擎复用。

## 备注
- `DbUpsertNode` 在连接字符串求空前增加空源守卫，保留「缺失连接 → MissingConnection」语义（空脚本经求值会返回 `"undefined"` 字符串，与原 `GetResult` 返回 `null` 不同）。
