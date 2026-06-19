# 表达式系统

## 1. 表达式的用途

用户在节点参数中填写的值常常需要动态引用其他数据，例如：

- URL：`https://api.example.com/users/{{ input.id }}/orders`
- 请求体：`{"name": "{{ parameter.name }}", "status": "{{ nodes["GetUser"].data.status }}"}`
- 条件判断：`{{ input.age }} >= 18 && {{ parameter.includeVip }}`

表达式系统负责在运行时将 `{{ ... }}` 中的内容求值并替换到最终字符串中。

## 2. 表达式语法

### 2.1 基本格式

表达式用双大括号包裹：

```
{{ 变量路径 }}
{{ 函数调用 }}
{{ 运算表达式 }}
```

### 2.2 变量引用

执行上下文中的 `Inputs` 是按端口名组织的 `DataBatch` 字典。表达式中的 `input` 是简化引用，默认指向名称为 `input` 的主输入端口当前数据项（即该端口当前批次中正在处理的那一条）。

节点通过 `ExecutionMode` 决定批次处理方式：

- `OncePerItem`：引擎对 `DataBatch` 中每一条 `DataItem` 分别调用一次节点执行，`input` 指向当前数据项，`runIndex` 与该数据项在批次中的索引一致。
- `OnceForAll`：引擎将整个 `DataBatch` 一次性传入节点，`input` 指向批次中的第一条数据项（或节点自行处理整个批次）。

| 变量                            | 含义                                               | 示例                                        |
| ------------------------------- | -------------------------------------------------- | ------------------------------------------- |
| `input.field`                   | 主输入端口（`input`）当前数据项的字段              | `{{ input.id }}`                            |
| `input`                         | 主输入端口当前数据项本身                           | `{{ input }}`                               |
| `inputs["portName"].field`      | 指定输入端口当前数据项的字段                       | `{{ inputs["secondary"].id }}`              |
| `parameter.name`                | 本节点的参数值                                     | `{{ parameter.method }}`                    |
| `nodes["NodeName"].data`        | 指定节点首次输出批次的第一条数据项的 `Data` 字段   | `{{ nodes["GetUser"].data.name }}`          |
| `nodes["NodeName"].items`       | 指定节点首次输出批次的所有数据项列表               | `{{ nodes["GetUser"].items[1].data.name }}` |
| `items("NodeName")[0].data`     | 指定节点的指定批次输出的第一条数据项的 `Data` 字段 | `{{ items("GetUser")[0].data }}`            |
| `env.VAR_NAME`                  | 白名单环境变量                                     | `{{ env.API_BASE_URL }}`                    |
| `workflow.id` / `workflow.name` | 工作流 ID/名称                                     | `{{ workflow.name }}`                       |
| `execution.id`                  | 当前执行 ID                                        | `{{ execution.id }}`                        |
| `runIndex`                      | 当前运行索引                                       | `{{ runIndex }}`                            |
| `now`                           | 当前时间                                           | `{{ now }}`                                 |

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

实现方式选型（按优先级）：

1. **自研递归下降解析器 + 表达式树**（推荐长期）：只支持必要运算符和白名单函数，无额外依赖，隔离最彻底。
2. **DynamicExpresso**：轻量，适合简单表达式，但需限制可用类型和函数。
3. **Jint / ClearScript**：仅在需要完整 JS 语义时考虑，体积大、沙箱隔离复杂，不建议默认使用。

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

- 参数输入框应提供表达式提示，列出可用的 `input`、`parameter`、`nodes` 字段。
- 表达式高亮显示，方便用户识别。
- 提供表达式测试工具，输入模拟数据即可预览结果。
