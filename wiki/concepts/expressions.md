# 表达式与脚本模型（Expressions & Scripting）

> 本文档基于当前代码编写，以代码为准。核心实现位于 `FlowEngine.Core/Scripting/`，校验在 `FlowEngine.Application/Workflows/WorkflowDraftValidator.cs`。
> 总览见 [系统总览](architecture/overview.md)；运行时数据作用域见 [工作流模型](workflow-model.md)。

## 1. 引擎与沙箱

- 表达式/脚本以 **JavaScript 语法** 求值，由 **Jint** 引擎执行（封装于 `JsEngine`，`Core/Scripting/JsEngine.cs`）。
- **受限沙箱**（`JsEngine.Create`）：
  - **默认不调用 `AllowClr()`**，脚本无法访问 CLR 类型/对象，封死借 `constructor` 逃逸执行 .NET 代码的路径。
  - 先按白名单 `AllowedGlobals` **裁剪全局对象**（`ApplySandboxWhitelist`），再显式删除 `ForbiddenIdentifiers`（如 `fetch` / `XMLHttpRequest` / `WebSocket` / `eval` 等，见 `JsEngineOptions.cs`）。
  - 同时从 `Object.prototype` 移除 `constructor`，切断经原型链的逃逸。
  - 限制：语句数、递归深度、内存、数组大小、正则超时、执行超时；`DisableStringCompilation()` 启用。
- 注入的安全辅助：`console`（转 `ILogger`）、`now` / `nowIso`、`jmespath`（基础 JSON 路径查询）、`length`、`trim`。**无网络/文件系统/进程 API**。

## 2. mustache 语法不支持

- **`{{ }}` 模板语法不被支持**（本引擎表达式是 JavaScript，非 n8n 风格）。
- `WorkflowDraftValidator.CollectMustacheErrors` 递归扫描参数字符串，命中 `{{` 或 `}}` 即报错：

```text
节点 "xxx" 含 n8n 风格的 {{ }} mustache 模板语法，本引擎不支持。
请改用 JavaScript 表达式，例如：'https://api.com/path?token=' + $json.token
```

- 该词法扫描是首要防线（带/不带引号都命中），JS 编译校验（`CollectExpressionSyntaxErrors`，`ScriptCompiler.TryCompile`）作为通用语法网补充。

## 3. 可用变量

变量由 `ExecutionScope`（`Core/Scripting/ExecutionScope.cs`）注入，与 `NodeExecutionContextFactory` 构造方式完全一致，避免各节点变量集漂移。

**逐项变量（per-item，每次求值前覆盖注入）**：

| 变量 | 含义 |
|------|------|
| `$json` | 当前 item 的 `JsonNode` 数据 |
| `$input` | 输入容器（见下表方法） |
| `$itemIndex` | 当前 item 在批次中的索引 |
| `$runIndex` | 运行索引（逐项路径与 `$itemIndex` 同值） |

`$input` 的方法/属性（`Core/Scripting/InputContainer.cs`）：

| 成员 | 含义 |
|------|------|
| `$input.item()` | 当前 item（等价于 `$json`） |
| `$input.all()` | 全部输入 item 数组 |
| `$input.first()` | 首个 input item |
| `$input.last()` | 末个 input item |
| `$input.count()` | 输入 item 数量 |
| `$input.params` | 当前节点参数字典 |
| `$input.context` | 执行上下文（含 `executionId` / `runIndex` / `nodeName` / `nodeType` / `workflowId`） |

**全局变量（来自 `ExecutionContextGlobalsBuilder`，单次执行内恒定）**：

| 变量 | 含义 |
|------|------|
| `$credentials` | 凭据字典（按名称取用，`$credentials.x.accessToken`） |
| `$env` | 环境变量（`EnvironmentAccessor`，按白名单） |
| `$workflow` | 当前工作流信息 |
| `$execution` | 当前执行信息 |
| `$vars` | 工作流级可变变量字典 |
| `$now` | 当前 UTC 时间（`DateTime.UtcNow`） |
| `$today` | 当前 UTC 日期 |
| `$node` | 当前节点信息 |
| `$ctx` | 上下文字典 |
| `$items` | 取数据批次 item 的函数（见下） |

`$items`（全局函数，由 `ExecutionContextGlobalsBuilder.BuildFull` 注入，见 `ExecutionContextGlobalsBuilder.cs:34`）：

| 调用 | 返回 |
|------|------|
| `$items()` | 当前输入批次的全部 item 数据列表（`inputItems`） |
| `$items("nodeName")` | 指定上游节点最新输出批次的 item 数据列表；节点不存在时返回 `null` |

> 注：保留名清单（`ParameterResolver.cs` 中的 `$json/$input/$items/$node/...`）部分用于防碰撞。**逐项变量**（`ExecutionScope` 实际注入）仅有 `$json` / `$input` / `$itemIndex` / `$runIndex`；`$items` 属**全局变量**（随 `BuildFull` 注入），并非逐项变量。

## 4. 正确示例

```js
// 逐项条件判断（If / 连接 Condition）
$json.status === 'active'

// 聚合判断：当前节点全部输入 item 数 > 10
$input.all().count > 10
// 等价写法
$input.item().count > 10

// 取凭据中的访问令牌
$credentials.x.accessToken

// 取节点参数
$input.params.apiUrl

// 字符串拼接（替代 mustache）
'https://api.com/path?token=' + $json.token

// 路径查询
jmespath($json, 'user.address.city')
```

## 5. 求值 API

`JsEngine`（`Core/Scripting/JsEngine.cs`）提供：

| 方法 | 用途 |
|------|------|
| `Evaluate(expr)` | 纯表达式求值，自动包装 `return (expr)` |
| `Run(script)` | 完整脚本（IIFE 包装，需 `return`） |
| `RunAsync(script, ct)` | 支持 `await` 的异步脚本，带强制超时 |
| `PrepareExpression` / `EvaluatePrepared` | 预编译 AST 缓存，逐 item 复用同一引擎 |

`ExecutionScope` 注入分两层以优化性能：
- `ApplyGlobalVariables`：全局变量，循环外调用一次；
- `ApplyItemScope`：逐项变量，每个 item 求值前覆盖调用；
- `ApplyNodeScope`：一次性注入（无循环简短求值用）。

求值结果经 `ToDataItem` 转回 `DataItem`（布尔/数字/字符串直接映射，对象/数组序列化为 JSON）。

## 备注

- `$credentials` 的取值字段结构随凭据类型变化（如令牌类凭据暴露 `accessToken` 等字段），以凭据定义为准，勿假定固定字段名。
- 注入的**逐项变量**仅有 `$json` / `$input` / `$itemIndex` / `$runIndex`；`$items` 为**全局变量**（经 `ExecutionContextGlobalsBuilder.BuildFull` 注入），不属于逐项作用域。
