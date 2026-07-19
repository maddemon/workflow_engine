# 任务：后端 Core 模块测试补充

## 目标

将 `FlowEngine.Core` 行覆盖率从 **52.5%**（Task 008 实测，覆盖行 2137 / 4070）提升至 **65%+**。距目标 ~12.5pt，为后端冲 75% 的关键缺口之一；本模块实体/值对象/枚举众多，低成本属性往返测试可快速抬升，但须兼顾语义（领域规则/转换逻辑）高价值覆盖。

**行号说明**：文中 `:行号`（如 `:41`）取自 2026-07-17 版本源码，仅作辅助参考；执行时请以类名 / 方法名 / 签名为准确认当前源码，行号可能因后续改动偏移。

## 目标类与已核实 API

### 实体（命名空间 `FlowEngine.Core.Entities`）
- `Entity` 基类 `Entities/Entity.cs:19,30`：`public Guid Id { get; set; }`（UUIDv7）。**所有实体 id 为 `Guid`，非 string**。
- `Project : Entity` `Project.cs:15`：关键属性 `string Name` :24、`string? Description` :32、`Guid CreatedBy` :40。
- `Workflow : Entity` `Workflow.cs:14`：`Guid? ProjectId` :21、`string Name` :30、`int Version` :37。
- `NodeDefinition`（**[NotMapped]**，不继承 Entity）`NodeDefinition.cs:10`：**`public string Id { get; set; }`** :15、`string TypeName` :20、`string Name` :25、`Dictionary<string,object> Parameters` :30。（注意此处 Id 为 string，与实体基类不同）

### 枚举（命名空间 `FlowEngine.Core.Enums`）
- 真实名称：`ExecutionStatus`、`WorkflowSource`、`ErrorStrategy`、`ExecutionMode`、`PortDirection`、`PortType`、`ParameterType`、`TriggerType`、`BackoffStrategy`、`HttpMethodOption`、`HttpRequestAuthMode`、`DraftStatus`、`PresentationHint`。
- 多数带 `[Description("…")]`（如 `ExecutionStatus` 各值、`WorkflowSource`）。

### 值对象（命名空间 `FlowEngine.Core.ValueObjects`，均为 `readonly record struct`）
- `ExecutionId(Guid Value)` `ExecutionId.cs:7`：`New()` :13、`From(Guid)` :20。
- `WorkflowDefinitionId(Guid Value)` `WorkflowDefinitionId.cs:7`：`New()` :13、`From(Guid)` :20。
- `CredentialKey(Guid CredentialId, string FieldName)` `CredentialKey.cs:8`：无工厂，仅 `ToString()`。
- 注意：**无** `Create` / `FromString` 静态方法（原草稿臆测）。

## 待完成项

- [x] **2.1 实体属性往返测试**：`Project` / `Workflow` / `NodeDefinition` 构造 + 关键属性 get/set 往返；重点验证 `NodeDefinition.Id` 为 string、`Entity.Id` 为 Guid 的类型差异。
- [x] **2.2 枚举 [Description] 测试**：对带描述的枚举验证 `DescriptionAttribute` 取值（如 `ExecutionStatus.Completed` 的描述），覆盖无描述兜底。
- [x] **2.3 值对象测试**：`ExecutionId` / `WorkflowDefinitionId` 的 `New()` 与 `From(guid)` 等价性、`ToString()`；`CredentialKey(guid, field).ToString()` 格式。

## 完成标准

- `dotnet test tests/FlowEngine.Core.Tests` 全绿。
- 不使用 `FluentAssertions` / `Moq`（纯 `Assert.*`）。
- 所有签名与上文核实一致。

- 对应项目 `dotnet build` 通过（无编译错误，新增测试不得引入类型/签名错误）。

## 完成状态

- [x] 2.1
- [x] 2.2
- [x] 2.3

## 主要修改记录

- 重写自 `plan-unit-test-coverage.md`：修正原草稿"值对象不存在"的错误判断（实际存在但 API 不同），明确实体 Id 类型差异与枚举/值对象真实 API。
- 2026-07-19：基于已有未提交测试文件补充并验收，`FlowEngine.Core` 行覆盖率 52.5% → 65.01%（2646/4070），`dotnet test tests/FlowEngine.Core.Tests` 633 通过、0 失败。
