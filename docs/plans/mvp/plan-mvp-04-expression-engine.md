# 开发计划：表达式引擎基础（plan-mvp-04-expression-engine）

## 1. 概述

实现 `{{ }}` 表达式求值引擎，支持变量引用、路径访问、基础运算、字符串替换与错误提示。MVP 阶段使用自研递归下降解析器，避免正则带来的技术债，完整沙箱强化推迟到 Alpha 阶段。

覆盖范围：
- 递归下降解析器（词法分析 + 语法分析）。
- 变量引用：`input`/`inputs`/`parameter`/`nodes`/`items`/`env` 白名单/`workflow`/`execution`/`runIndex`/`now`。
- 字符串替换与基础运算。
- 错误处理与友好提示。
- AST 缓存。

不覆盖范围：完整沙箱（Alpha 强化）、JMESPath 查询（Alpha）、前端表达式辅助 UI（见 plan-mvp-10）。

## 2. 交付物清单

- `src/FlowEngine.Runtime/Expressions/ExpressionParser.cs`（递归下降解析器）。
- `src/FlowEngine.Runtime/Expressions/ExpressionEvaluator.cs`（求值器）。
- `src/FlowEngine.Runtime/Expressions/ExpressionContext.cs`（求值上下文）。
- `src/FlowEngine.Runtime/Expressions/Ast/`（AST 节点：LiteralSegment/ExpressionSegment/MemberAccess/Indexer/FunctionCall/BinaryOperation）。
- `src/FlowEngine.Runtime/Expressions/ExpressionCacheKey.cs`（缓存键，签名见 [expression-system.md](../../architecture/expression-system.md) §6.1）。
- `src/FlowEngine.Runtime/Expressions/ExpressionError.cs`（错误类型与 availableFields）。
- 单元测试：变量引用、路径访问、字符串替换、错误场景、缓存命中。

## 3. 开发阶段

### 阶段一：词法与语法解析

- 目标：实现递归下降解析器，将模板字符串解析为 AST。
- 核心任务：
  - 词法分析：识别 `{{ }}` 包裹的表达式段与字面量段。
  - 语法分析：支持成员访问（`input.field`）、索引器（`inputs["portName"]`、`items[0]`）、函数调用（`length(...)`）、二元运算符（`+`/`-`/`*`/`/`/`%`/`==`/`!=`/`>`/`<`/`>=`/`<=`/`&&`/`||`/`!`）、括号分组、条件表达式（`? :`）。
  - 处理字符串内含 `}}` 的边界情况。
  - 生成 AST 节点序列（LiteralSegment + ExpressionSegment）。
- 输入：[expression-system.md](../../architecture/expression-system.md) §2.2 变量引用、§2.3 支持的运算、§3.1 伪代码。
- 输出：可解析模板字符串的解析器。
- 验收标准：
  - `{{ input.id }}` 解析为 ExpressionSegment（MemberAccess）。
  - 字面量与表达式混合字符串正确分段。
  - 语法错误抛出 `SyntaxError`，包含错误位置。
- 依赖：plan-mvp-02 Core 抽象。

### 阶段二：变量与路径求值

- 目标：实现表达式求值，支持所有变量引用。
- 核心任务：
  - 实现 `ExpressionContext`：封装 `Inputs`/`RawParameters`/`NodeOutputs`/`NodeBatches`/`env` 白名单/`workflow`/`execution`/`runIndex`/`now`。
  - 按前缀选择数据源：
    - `input` → `context.Inputs["input"]` 当前数据项。
    - `inputs["portName"]` → 指定端口当前数据项。
    - `parameter` → `context.RawParameters`。
    - `nodes["X"].data` / `nodes["X"].items` → `context.NodeOutputs`。
    - `items("X")[0]` → `context.NodeBatches`。
    - `env` → 白名单环境变量（仅配置允许的变量）。
    - `workflow`/`execution`/`runIndex`/`now` → `context.Metadata`。
  - 按路径取值（嵌套成员访问）。
  - 实现字符串替换：将求值结果转换为字符串拼接到最终输出。
- 输入：[expression-system.md](../../architecture/expression-system.md) §2.2 变量引用、§3 求值流程。
- 输出：可求值表达式的求值器。
- 验收标准：
  - `{{ input.id }}` 在 input 包含 `{id: "123"}` 时替换为 `123`。
  - `{{ parameter.method }}` 在参数为 `POST` 时替换为 `POST`。
  - `{{ nodes["GetUser"].data.name }}` 在节点输出包含 name 时正确取值。
  - `{{ env.API_BASE_URL }}` 在白名单中存在时返回值，不在白名单时返回 `SecurityViolation`。
- 依赖：阶段一。

### 阶段三：基础函数与运算

- 目标：实现基础函数调用与运算表达式。
- 核心任务：
  - 实现白名单函数：`length(...)`、`trim(...)`、`upper(...)`、`lower(...)`、`now`。
  - 实现算术与逻辑运算求值。
  - 实现条件表达式 `condition ? trueValue : falseValue`。
  - 类型不匹配时返回 `TypeMismatch` 错误。
- 输入：[expression-system.md](../../architecture/expression-system.md) §2.3 支持的运算。
- 输出：支持运算与函数的求值器。
- 验收标准：
  - `{{ length(input.items) }}` 返回数组长度。
  - `{{ input.age >= 18 }}` 返回布尔值。
  - `{{ input.active ? "on" : "off" }}` 返回条件结果。
- 依赖：阶段二。

### 阶段四：错误处理与缓存

- 目标：实现友好错误提示与 AST 缓存。
- 核心任务：
  - 实现错误类型：`FieldNotFound`/`NodeOutputNotFound`/`TypeMismatch`/`SyntaxError`/`SecurityViolation`。
  - 错误响应包含 `expression`/`reason`/`availableFields`（见 [expression-system.md](../../architecture/expression-system.md) §5）。
  - 实现 `ExpressionCacheKey`（Expression + InputSchemaHash + ParameterSchemaHash）。
  - 使用 `IMemoryCache` 缓存 AST，schema 变化时自动失效。
  - 缓存只保存 AST，不保存求值结果。
- 输入：[expression-system.md](../../architecture/expression-system.md) §5 错误处理、§6.1 表达式编译缓存。
- 输出：带错误提示与缓存的求值器。
- 验收标准：
  - 引用不存在的字段时返回 `FieldNotFound`，`availableFields` 列出可用字段。
  - 相同表达式第二次解析命中缓存。
  - schema 变化后缓存失效。
- 依赖：阶段二、阶段三。

## 4. 阶段依赖图

```mermaid
flowchart LR
    S1[阶段一 词法与语法解析] --> S2[阶段二 变量与路径求值]
    S2 --> S3[阶段三 基础函数与运算]
    S2 --> S4[阶段四 错误处理与缓存]
    S3 --> S4
```

## 5. 风险与待定项

| 风险/待定项 | 影响 | 应对策略 |
|------------|------|---------|
| 递归下降解析器实现复杂度高 | 开发周期延长 | MVP 仅支持必要语法，复杂查询推迟到 Alpha 的 JMESPath |
| `env` 白名单配置遗漏 | 节点无法读取必要环境变量 | 在 appsettings.json 中显式声明白名单，文档说明 |
| 缓存键 schema 哈希计算开销 | 性能下降 | MVP 阶段使用简单字符串拼接哈希，后续优化 |
| 完整沙箱缺失 | 安全风险 | MVP 仅限白名单变量与基础运算，禁止反射/文件/网络，Alpha 阶段强化 |

## 6. 验收总标准

- `{{ input.id }}` 可正确替换为实际值。
- `{{ parameter.method }}` 可正确引用节点参数。
- 错误返回 `availableFields`，帮助用户定位问题。
- AST 缓存命中后不重复解析。
- MVP 阶段不实现完整沙箱，仅限白名单变量与基础运算（Alpha 阶段强化）。
- 求值器签名与 [expression-system.md](../../architecture/expression-system.md) §3.1 一致。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务 |
|------|--------|----------|----------|
| 2026-06-18 | Agent | 创建表达式引擎计划 | MVP-0 |
