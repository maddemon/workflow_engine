# 开发计划：表达式沙箱强化（plan-alpha-03-expression-sandbox）

## 1. 概述

本模块在 MVP 表达式引擎基础上强化安全沙箱，确保用户编写的 `{{ }}` 表达式无法访问文件系统、网络、进程、反射等危险资源，同时补齐 JMESPath 查询与 AST 缓存失效机制。

覆盖范围：

- 安全限制（禁止文件/网络/进程/反射/非白名单 env）。
- 深度与超时限制。
- 白名单函数。
- JMESPath 查询。
- 错误类型定义（SecurityViolation 等）。
- AST 缓存失效（InputSchemaHash / ParameterSchemaHash）。

不覆盖：表达式测试工具前端（Beta 增强）、tokenizer 精确计数（GA）。

安全限制、错误类型、缓存键定义详见 [expression-system.md](../../architecture/expression-system.md)。

## 2. 交付物清单

- 沙箱限制实现：禁止文件系统/网络/进程/反射/非白名单环境变量访问。
- 深度限制：表达式求值递归深度上限（可配置）。
- 超时限制：单次求值超时（CancellationToken 传入）。
- 白名单函数集：`jmespath`、`length`、`trim`、`now` 等安全函数。
- 白名单环境变量：`env` 命名空间只允许读取配置中声明的变量。
- JMESPath 查询支持：`jmespath(data, query)` 函数。
- 错误类型：`FieldNotFound`、`NodeOutputNotFound`、`TypeMismatch`、`SyntaxError`、`SecurityViolation`（见 [expression-system.md §5.1](../../architecture/expression-system.md#51-常见错误类型)）。
- AST 缓存：基于 `ExpressionCacheKey`（Expression + InputSchemaHash + ParameterSchemaHash），schema 变化时失效。
- 单元测试（含安全违规用例）。

## 3. 开发阶段

### 阶段一：沙箱限制

- **目标**：表达式无法访问危险资源。
- **核心任务**：
  - 禁止文件系统访问：解析器不提供文件相关函数与类型。
  - 禁止网络访问：不提供 HTTP/Socket 相关函数。
  - 禁止进程访问：不提供进程启动相关函数。
  - 禁止反射：不允许调用任意 .NET 类型，仅支持预定义数据源访问。
  - 深度限制：递归下降解析器加入深度计数器，超限抛 `SecurityViolation`。
  - 超时限制：求值时传入 CancellationToken，超时终止并抛错。
- **输入**：MVP 表达式引擎（plan-mvp-04）。
- **输出**：受限沙箱，危险操作被拒绝。
- **验收标准**：
  - 表达式无法读取文件。
  - 表达式无法发起网络请求。
  - 表达式无法启动进程。
  - 表达式无法调用任意 .NET 类型。
  - 深度超限抛 `SecurityViolation`。
  - 超时终止求值。
- **依赖**：plan-mvp-04 表达式引擎。

### 阶段二：白名单函数与 env

- **目标**：提供安全函数集，env 命名空间只读白名单。
- **核心任务**：
  - 实现白名单函数：`jmespath`、`length`、`trim`、`now`、算术/比较/逻辑运算符。
  - 非白名单函数调用被拒绝并抛 `SecurityViolation`。
  - `env` 命名空间只允许读取配置中显式声明的环境变量（白名单见 [expression-system.md §2.2](../../architecture/expression-system.md#22-变量引用)）。
  - 敏感变量（`DATABASE_PASSWORD`、`JWT_SECRET` 等）不在白名单中。
- **输入**：沙箱限制（阶段一）、环境变量白名单配置。
- **输出**：安全函数集与受控 env 访问。
- **验收标准**：
  - 白名单函数可正常调用。
  - 非白名单函数调用抛 `SecurityViolation`。
  - `env.API_BASE_URL`（白名单内）可读取。
  - `env.DATABASE_PASSWORD`（白名单外）抛 `SecurityViolation`。
- **依赖**：阶段一。

### 阶段三：JMESPath 查询

- **目标**：支持复杂 JSON 查询。
- **核心任务**：
  - 实现 `jmespath(data, query)` 函数，支持 JMESPath 语法。
  - 查询失败时返回友好错误（如路径不存在）。
  - 大数据量场景下避免性能问题。
- **输入**：白名单函数框架（阶段二）。
- **输出**：JMESPath 查询可用。
- **验收标准**：
  - `{{ jmespath(input.data, "users[?age > `18`].name") }}` 返回正确结果。
  - 查询路径不存在时返回友好错误。
- **依赖**：阶段二。

### 阶段四：错误处理与 AST 缓存

- **目标**：求值失败时返回清晰错误，AST 缓存 schema 变化时失效。
- **核心任务**：
  - 实现错误类型：`FieldNotFound`、`NodeOutputNotFound`、`TypeMismatch`、`SyntaxError`、`SecurityViolation`。
  - 错误信息包含表达式文本、失败原因、可用字段列表（见 [expression-system.md §5](../../architecture/expression-system.md#5-错误处理与友好提示)）。
  - 实现 AST 缓存：缓存键为 `ExpressionCacheKey`（Expression + InputSchemaHash + ParameterSchemaHash，见 [expression-system.md §6.1](../../architecture/expression-system.md#61-表达式编译缓存)）。
  - schema 变化时缓存自动失效。
  - 缓存只存 AST，不存求值结果。
- **输入**：阶段一至三。
- **输出**：友好错误与缓存机制。
- **验收标准**：
  - 字段不存在时返回 `FieldNotFound` 并列出可用字段。
  - 语法错误返回 `SyntaxError` 并定位错误位置。
  - 安全违规返回 `SecurityViolation`。
  - 相同表达式 + 相同 schema 命中缓存。
  - schema 变化后缓存失效，重新解析。
- **依赖**：阶段一至三。

## 4. 阶段依赖图

```mermaid
flowchart LR
    S1[阶段一：沙箱限制] --> S2[阶段二：白名单函数与 env]
    S2 --> S3[阶段三：JMESPath]
    S3 --> S4[阶段四：错误处理与缓存]
    MVP[MVP 表达式引擎<br/>plan-mvp-04] --> S1
```

## 5. 风险与待定项

| 风险/待定项 | 影响 | 应对/说明 |
|-------------|------|-----------|
| 沙箱绕过漏洞 | 安全风险 | 编写非法输入测试用例覆盖；仅支持预定义数据源，不暴露 .NET 类型系统 |
| JMESPath 性能 | 大数据查询慢 | 当前按需实现；GA 阶段可引入流式/分页 |
| 缓存失效判断不准 | 表达式求值结果错误 | schema 哈希计算需覆盖完整结构，单元测试覆盖 |
| 超时精度 | CancellationToken 检查点不足 | 在递归下降每层检查 token |

## 6. 验收总标准

- 表达式无法访问文件系统、网络、进程、反射。
- 超时与深度超限时终止求值并抛 `SecurityViolation`。
- `env` 命名空间只读白名单变量，敏感变量被拒绝。
- JMESPath 查询可用。
- 错误信息清晰，包含表达式、原因、可用字段。
- AST 缓存命中正确，schema 变化时失效。

## 变更记录

| 日期 | 修改人 | 修改内容 | 关联任务 |
|------|--------|----------|----------|
| 2026-06-18 | Agent | 创建表达式沙箱强化开发计划 | Alpha 计划编写 |
