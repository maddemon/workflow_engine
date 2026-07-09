# 表达式系统

> **阅读须知（现状与历史）**：本文档 §1 / §3 / §4 / §5.1 / §6 保留了 MVP 阶段基于 `{{ }}` 双大括号 + 手写递归下降解析器的**历史设计**，仅作演进记录。**当前引擎的真实行为以 §2.2 与 §2.5 为准**：不使用 `{{ }}` 包裹，节点参数由 `ParameterResolver`（字符启发式识别）+ Jint（`return (expr)` 包裹求值）处理。凡历史章节与 §2.5 冲突处，以 §2.5 为准。

## 1. 表达式的用途

> 以下 `{{ }}` 写法为历史示例，当前不再使用（见 §2.5）。

用户在节点参数中填写的值常常需要动态引用其他数据，例如：

- URL：`https://api.example.com/users/{{ input.id }}/orders`
- 请求体：`{"name": "{{ parameter.name }}", "status": "{{ nodes["GetUser"].data.status }}"}`
- 条件判断：`{{ input.age }} >= 18 && {{ parameter.includeVip }}`

表达式系统负责在运行时将动态引用求值并替换到最终字符串中。

## 2. 表达式语法

### 2.1 基本格式

表达式用双大括号包裹：

```
{{ 变量路径 }}
{{ 函数调用 }}
{{ 运算表达式 }}
```

### 2.2 变量引用

执行上下文中的 `Inputs` 是按端口名组织的 `DataBatch` 字典。plan-004 之后采用统一的 `$` 前缀内建变量模型：**所有 `$` 开头的变量都是引擎内建**，裸写的任何名字都视为用户数据或自定义变量。这样既兼容 n8n 习惯，也避免 `input`/`json` 等通用词与用户数据字段冲突。

旧式裸名变量（`input`、`nodes`、`workflow` 等）仍作为迁移期兼容保留，但新流程推荐统一使用 `$` 前缀变量。

节点通过 `ExecutionMode` 决定批次处理方式：

- `OncePerItem`：引擎对 `DataBatch` 中每一条 `DataItem` 分别调用一次节点执行，`$json`/`$input.item()` 指向当前数据项，`$runIndex` 与该数据项在批次中的索引一致。
- `OnceForAll`：引擎将整个 `DataBatch` 一次性传入节点，`$json`/`$input.item()` 指向批次中的第一条数据项（或节点自行处理整个批次）。

#### 2.2.1 通用核心内建变量（第 1 层）

所有节点都会注入以下 `$` 前缀变量：

| 变量 | 含义 | 示例 |
| ---- | ---- | ---- |
| `$json` | 当前 item 的 `Data`（JsonNode） | `$json.userid` |
| `$input` | n8n 式输入容器 | `$input.item().userid` |
| `$items(name?)` | 获取指定/当前节点全部 item 数据数组 | `$items('GetUser')` / `$items()` |
| `$node['NodeName']` | 指定节点输出对象（含 `.json`） | `$node['GetUser'].json[0].name` |
| `$workflow` | 工作流元数据（id/name/projectId/version） | `$workflow.name` |
| `$execution` | 执行元数据（id） | `$execution.id` |
| `$env.VAR_NAME` | 白名单环境变量 | `$env.API_BASE_URL` |
| `$vars` | 工作流级可写状态（当前为空对象占位） | `$vars.flag` |
| `$now` | 当前 UTC 时间 | `$now` |
| `$today` | 当日 UTC 00:00 | `$today` |
| `$runIndex` | 当前运行索引 | `$runIndex` |
| `$itemIndex` | 当前 item 索引（与 `$runIndex` 一致） | `$itemIndex` |
| `$credentials.<name>.<field>` | 多字段凭据值 | `$credentials.db.connectionString` |
| `$ctx` | 上下文 bundle（函数式 `ctx => ...` 的参数等价物） | `$ctx.$json` |

`$input` 容器方法（camelCase）：

| 方法/属性 | 说明 |
| --------- | ---- |
| `item()` | 当前 item 数据，等价 `$json` |
| `all()` | 当前节点全部输入 item 数组 |
| `first()` | 第一个输入 item |
| `last()` | 最后一个输入 item |
| `count()` | 输入 item 数量 |
| `Params` | 当前节点参数字典 |
| `Context` | 执行上下文信息（executionId、runIndex、nodeName 等） |

#### 2.2.2 节点/场景特有变量（第 2 层）

这些变量**不注册到工厂顶层**，由具体节点在执行时通过 `INodeExecutionContextFactory.CreateAsync` 的 `extraGlobals` 参数本地注入。工厂只负责把它们设到当前 JS 引擎，不感知变量语义。

| 变量 | 注入节点 | 含义 |
| ---- | -------- | ---- |
| `$cursor` | `PaginateNode` | 当前请求游标 |
| `$nextCursor` | `PaginateNode` | 下一页游标（用于 `terminateWhen`） |
| `$page` | `PaginateNode` | 当前页码（从 0 开始） |
| `$response` | `PaginateNode` | 上一页 HTTP 响应体 |
| `$payload` | Webhook/触发器入口 | 触发载荷 |
| `$tool` | Agent 工具 | 工具入参 |

`LoopNode` 当前项复用 `$json`，序号复用 `$itemIndex`，不另设 `$item`/`$index`。

**`env` 命名空间安全**：只允许访问在系统配置中显式声明的环境变量（白名单），禁止读取 `DATABASE_PASSWORD`、`JWT_SECRET` 等敏感变量。白名单通过配置管理，不在工作流中暴露。

### 2.3 支持的运算

- 算术运算：`+`、`-`、`*`、`/`、`%`
- 比较运算：`==`、`!=`、`>`、`<`、`>=`、`<=`
- 逻辑运算：`&&`、`||`、`!`
- 条件表达式：`condition ? trueValue : falseValue`
- 函数调用：`jmespath(...)`、`length(...)`、`trim(...)` 等

### 2.4 JMESPath 查询

对于复杂 JSON 查询，支持 JMESPath：

```
{{ jmespath(input.data, "users[?age > `18`].name") }}
```

### 2.5 表达式识别与求值（当前实现）

> 以下描述**当前代码实际行为**（`ParameterResolver` + `JsEngine`，基于 Jint）。§2.5.1 的「函数式 / 省略式双写法」为 plan-004 的**目标设计，尚未实现**，勿按其编写参数。

节点参数值默认按字面量处理；仅当 `ParameterResolver.IsExpression` 判定为表达式时，才交给 JS 引擎求值。识别规则（字符启发式，见 `ParameterResolver.cs:300`）：

1. 字面 `true` / `false` / `null` → 视为表达式。
2. 可解析为 `int` / `long` / `double` 的纯数字 → 视为表达式。
3. 首个单词命中 `s_knownIdentifiers`（含全部 `$` 前缀内建变量）→ 视为表达式。
4. 串中含运算/分组字符 `= + - * / % > < ! ? : ( ) [ ] & |` 之一 → 视为表达式。
5. 其余（如纯字符串、普通 URL 片段）→ 按字面量原样返回。

被判为表达式的串统一用 `return (<expr>)` 包裹后由 Jint 求值（`JsEngine.PrepareExpression`，`JsEngine.cs:133`）。**没有** Acornima AST 顶层类型分类，也**不做** Function/Expression/Script 三分；不支持 `{{ }}` 双大括号包裹（判定与求值均不识别 `{{ }}`）。

安全由 `JsEngine.DisableStringCompilation()`（封死 `eval`/`new Function`）+ 求值超时 + 禁用 CLR 互操作保证，而非自研解析器。

> 因规则 4 只做**字符级**扫描，凡纯引用（如 `$credentials.x`、`$json`）若不含上述字符，必须依赖规则 3 命中 `s_knownIdentifiers` 才会被当作表达式——所以所有 `$` 前缀内建变量都要在 `s_knownIdentifiers` 登记（`ParameterResolver.cs:21-34`），否则会被当普通字符串。

#### 2.5.1 计划中：函数式 / 省略式双写法（未实现）

plan-004 评审提出的目标写法，**当前引擎不支持**，记录于此仅作后续实现参考：

- **函数式**：`ctx => <expr>` 或 `({ $json }) => <expr>`，`ctx` 为上下文 bundle。
- **省略式**：直接写表达式或语句，如 `({ dept_id: 1 })`、`$json.status === 'active'`。

设想引擎按顶层 AST 类型分类包裹（函数式以 `(__fn)(ctx)` 调用、多语句包 IIFE 等）。⚠️ 在实现前，参数里写 `ctx => (...)` 会被当函数值返回、不会自动以 `ctx` 调用；多语句脚本会因 `return (<expr>)` 包裹而语法出错。请只用单表达式写法。

## 3. 求值流程

```
用户在 URL 参数里填: {{ input.id }}/details
                                 ↓
执行引擎读取参数原始值        → "{{ input.id }}/details"
                                 ↓
正则匹配 {{ ... }}             → 提取 "input.id"
                                 ↓
解析表达式链                   → 主输入端口 → 当前数据项 → id → "123"
                                 ↓
字符串替换                     → "123/details"
                                 ↓
返回最终值                     → "123/details"
```

### 3.1 伪代码

MVP 直接使用手写递归下降解析器，避免正则带来的字符串内含 `}}`、嵌套表达式等技术债。解析器只支持必要的语法：`{{ }}` 包裹、成员访问、索引器、函数调用、二元运算符、括号分组。

```csharp
public string Evaluate(string template, ExpressionContext context)
{
    var parser = new ExpressionParser(template);
    var segments = parser.Parse();

    var sb = new StringBuilder();
    foreach (var segment in segments)
    {
        if (segment is LiteralSegment literal)
            sb.Append(literal.Text);
        else if (segment is ExpressionSegment expr)
            sb.Append(ConvertToString(EvaluateExpression(expr, context)));
    }
    return sb.ToString();
}

private object EvaluateExpression(ExpressionNode node, ExpressionContext context)
{
    // 1. 根据前缀选择数据源
    //    - input -> context.Inputs["input"].CurrentItem
    //    - inputs["portName"] -> context.Inputs["portName"].CurrentItem
    //    - parameter -> context.Parameters
    //    - nodes["X"].data / nodes["X"].items -> context.NodeOutputs
    //    - items("X")[0] -> context.NodeBatches
    //    - env -> WhitelistEnvironmentVariables（白名单）
    //    - workflow/execution/runIndex -> context.Metadata
    // 2. 按路径取值
    // 3. 如有函数调用，调用安全函数
    // 4. 返回结果
}
```

## 4. 安全限制

表达式引擎**不是代码执行引擎**，必须严格限制能力：

| 禁止行为             | 说明                                     |
| -------------------- | ---------------------------------------- |
| 访问文件系统         | 不允许读取/写入文件                      |
| 访问网络             | 不允许发起 HTTP 请求                     |
| 访问进程             | 不允许启动进程                           |
| 访问反射             | 不允许调用任意 .NET 类型                 |
| 访问非白名单环境变量 | `env` 命名空间只能读取配置允许的环境变量 |
| 无限递归             | 表达式求值深度限制                       |
| 超时                 | 单次求值超时限制                         |

**当前实现选型：Jint**（`JsEngine` 封装）。通过 `DisableStringCompilation()` 封死 `eval`/`new Function`、禁用 CLR 互操作、设置求值超时来实现沙箱隔离；未采用自研解析器。

以下为 MVP 阶段的历史选型讨论，保留作演进记录：

1. **自研递归下降解析器 + 表达式树**：只支持必要运算符和白名单函数，无额外依赖，隔离最彻底。
2. **DynamicExpresso**：轻量，适合简单表达式，但需限制可用类型和函数。
3. **Jint / ClearScript**：完整 JS 语义。**最终选用 Jint**——以完整 JS 表达式能力换取用户表达力，靠上述沙箱手段收敛风险。

## 5. 错误处理与友好提示

当表达式求值失败时，引擎应返回清晰的错误信息：

```json
{
  "success": false,
  "errorCode": "ExpressionEvaluationFailed",
  "message": "表达式求值失败",
  "details": {
    "expression": "{{ input.user.name }}",
    "reason": "input 中不存在 'user' 字段",
    "availableFields": ["id", "email", "status"]
  }
}
```

### 5.1 常见错误类型

> 当前实现以 **C# 异常**表达求值错误，非上例 JSON 错误码。`ParameterResolver` 主要抛出：
>
> | 异常 | 触发场景 |
> | ---- | -------- |
> | `SecurityViolationException` | 表达式命中 `s_forbiddenIdentifiers`（`eval`/`require`/`process` 等）或尝试访问禁止资源 |
> | `ExpressionEvaluationException` | Jint 求值失败（语法错误、字段访问越界、运行时抛错等），消息尽量携带缺失字段名 |
>
> 下表为历史设计中规划的细分错误码，尚未在运行时区分：

| 错误                 | 说明                   |
| -------------------- | ---------------------- |
| `FieldNotFound`      | 引用的字段不存在       |
| `NodeOutputNotFound` | 引用的节点输出不存在   |
| `TypeMismatch`       | 运算类型不匹配         |
| `SyntaxError`        | 表达式语法错误         |
| `SecurityViolation`  | 表达式尝试访问禁止资源 |

## 6. 性能考虑

### 6.1 表达式编译缓存

解析后的表达式抽象语法树（AST）可缓存，避免重复解析。缓存键定义：

```csharp
public record ExpressionCacheKey(
    string Expression,       // 原始表达式文本
    string InputSchemaHash,  // 当前输入端口 OutputSchema 的哈希
    string ParameterSchemaHash // 当前节点参数结构的哈希
);
```

- `Expression` 相同的表达式，若输入/参数 schema 发生变化，缓存自动失效。
- 缓存只保存 AST，不保存求值结果（因为上下文每次不同）。
- 使用内存 `IMemoryCache`，可配置过期时间。

### 6.2 其他优化

- 避免在循环中重复解析同一表达式。
- 大数据量场景下，JMESPath 查询应支持流式或分页。

## 7. 前端辅助

- 参数输入框应提供表达式提示，列出可用的 `$json`、`$input`、`$items`、`$node`、`$workflow`、`$execution`、`$env`、`$vars`、`$now`、`$today`、`$runIndex`、`$itemIndex`、`$credentials` 等内建变量，以及本节点可能注入的节点私有变量（如 `$cursor`）。
- 表达式高亮显示，方便用户识别 `$` 前缀内建变量与用户数据字段。
- 提供表达式测试工具，输入模拟数据即可预览结果。
- 参数类型为 `Credential` 时，按 `CredentialType` 限制可选择的凭据类型。
