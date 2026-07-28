# 编写一个节点插件（Write a Plugin）

> 本文档基于当前代码编写，以代码为准。核心契约见 `backend/FlowEngine.Core/Abstractions/NodeBase.cs` 与 `backend/FlowEngine.Core/Attributes/`，加载逻辑见 `backend/FlowEngine.Runtime/Registry/PluginLoader.cs`。内置示例节点见 `plugins/FlowEngine.Plugins.Standard/`（如 `IfNode.cs`、`AgentNode.cs`）。

## 1. 插件模型总览

- 节点是一个继承 `NodeBase` 的类（`backend/FlowEngine.Core/Abstractions/NodeBase.cs`），`NodeBase` 同时实现 `INodeType`（框架契约）与 `INodeHandler`（业务契约）。
- 节点通过 **类级特性** 声明元信息与端口，通过 **公共属性** 声明参数，通过 **`[Inject]` 属性** 获取运行时注入的能力。
- 编译为 DLL 放入 `plugins/`，启动时经独立 `AssemblyLoadContext` 加载并注册到节点注册中心（`PluginLoader.LoadNodes`）。
- **硬性约束：插件只可引用 `FlowEngine.Core`，禁止引用 `FlowEngine.Application` 或 `FlowEngine.Runtime`**（否则产生循环依赖与执行死锁）。这就是插件放在 `plugins/` 而非 `backend/` 的原因。

端口与模型概念见 [工作流模型](concepts/workflow-model.md)；表达式语法见 [表达式](concepts/expressions.md)。

## 2. 第一步：建项目，只引用 Core

新建类库（也可直接加入已有的 `plugins/FlowEngine.Plugins.Standard` 项目）。`.csproj` 仅引用 Core：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputPath>../../plugins</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\backend\FlowEngine.Core\FlowEngine.Core.csproj" />
    <!-- ❌ 禁止引用 FlowEngine.Application / FlowEngine.Runtime -->
  </ItemGroup>
</Project>
```

> `PluginLoader` 会校验插件目标框架与宿主兼容：`.NETStandard` 始终兼容，同系列（`.NETCoreApp`）低版本向前兼容（RollForward）。宿主为 `net10.0`，插件按其编译即可。

## 3. 第二步：类级特性 `[NodeMeta]` 与 `[Port]`

`[NodeMeta]` 声明节点身份（定义见 `backend/FlowEngine.Core/Attributes/NodeMetaAttribute.cs`，其字段均为 `required`）：

| 参数 | 含义 |
|------|------|
| `TypeName`    | 唯一标识（如 `"uppercase"`），引擎据此路由 |
| `DisplayName` | 前端展示名 |
| `Category`    | 分类，枚举 `NodeCategory`（`Flow`/`AI`/`Data`/`Network`/`Trigger`/`Storage`/`Utility` 等） |
| `Icon`        | 图标名（Mantine 图标键，如 `"bot"`、`"shuffle"`） |
| `DefaultIsEntry` | 可选，是否默认作为入口节点 |
| `ExecutionMode`   | 可选，默认 `OnceForAll`；逐项处理用 `OncePerItem` |

`[Port(...)]` 可多次使用（定义见 `backend/FlowEngine.Core/Attributes/PortAttribute.cs`）：

```csharp
[Port(FlowConstants.PortNames.Input,  "Input",  PortDirection.Input)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
// 可选第四参 PortType：Main / AgentTool / LLM 等
```

> 端口名推荐使用 `FlowConstants.PortNames` 中的常量（如 `Input`/`Output`/`True`/`False`/`Tools`/`Llm`），保证与前端、引擎路由一致。

## 4. 第三步：公共属性即参数

- 节点的**公共可读写属性**即为参数，前端参数面板按属性自动渲染。
- 必填参数加 `[Required]`（`FlowEngine.Core.Metadata.RequiredAttribute`，置于独立命名空间以避开 `System.ComponentModel.DataAnnotations` 的同名类型）。
- 表达式型参数加 `[Hint(PresentationHint.Expression)]`（`FlowEngine.Core.Attributes.HintAttribute`，`PresentationHint.Expression` 位于 `FlowEngine.Core.Enums`）。表达式入参通常声明为 `Script` 类型，引擎在预求值阶段完成求值并写入 `Script.ResolvedValue`，节点执行期经 `Script.GetResolved<T>()` 读取强类型结果。
- 给属性加 `[Description("...")]` 作为前端提示文案。

```csharp
[Required]
[Hint(PresentationHint.Expression)]
[Description("要转为大写的文本，支持表达式，如 $json.name。")]
public Script Text { get; set; } = Script.Empty;
```

## 5. 第四步：`[Inject]` 获取运行时能力

运行时依赖（不来自前端参数）用 `[Inject]` 标记（`backend/FlowEngine.Core/Abstractions/InjectAttribute.cs`）。引擎在执行前经 `NodeCapabilityInjector` 注入：

```csharp
[Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
[Inject] public IExecutionLogger? Logger { get; private set; }
[Inject] public ILlmClient? LlmClient { get; private set; }
[Inject] public INodeRegistry? Registry { get; private set; }
[Inject] public ISubExecutionService? Sub { get; private set; }
```

`InjectAttribute` 还可设 `Required = true`（解析不到即抛 `NodeExecutionException`）或 `Name`（多来源时按 key 指定）。节点只读该属性，不关心来源。

## 6. 第五步：实现 `ExecuteAsync`

重写基类抽象方法：

```csharp
public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
```

- 输入 `NodeInput` 提供 `InputBatch`（主输入批次）、`GlobalVariables`、`RunIndex`、`Inputs`（各端口批次字典）。
- 返回 `NodeHandlerOutput`：单端口输出用 `NodeHandlerOutput.ToPort(portName, batch)`；多端口用 `NodeHandlerOutput.ToPorts(dict)`。
- 业务失败抛 `NodeExecutionException(errorCode, message)`，由基类捕获转为失败结果；若需“恢复”可重写 `OnErrorAsync` 返回非 null 输出。
- 数据项用 `DataItem { Data = JsonNode, Success = true }`，批次用 `DataBatch { Items = { ... } }`。

## 7. 完整示例：Uppercase 节点

以下示例与上文真实特性/类名一致，可直接放入 `plugins/FlowEngine.Plugins.Standard/` 编译：

```csharp
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>将输入文本转为大写并输出到 Output 端口。</summary>
[NodeMeta(
    TypeName = "uppercase",
    DisplayName = "Uppercase",
    Category = NodeCategory.Utility,
    Icon = "text-fields")]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
public sealed class UppercaseNode : NodeBase
{
    /// <summary>要转为大写的文本，支持表达式，如 $json.name。</summary>
    [Required]
    [Hint(PresentationHint.Expression)]
    [Description("要转为大写的文本，支持表达式，如 $json.name。")]
    public Script Text { get; set; } = Script.Empty;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (Text is null || string.IsNullOrWhiteSpace(Text.Source) || Text.ResolvedValue is null)
        {
            throw new NodeExecutionException("MissingText", "Text 参数不能为空或未被求值。");
        }

        var value = Text.GetResolved<string>();
        var result = value.ToUpperInvariant();

        var item = new DataItem
        {
            Data = JsonNode.Parse($"{{\"result\":\"{result}\"}}"),
            Success = true,
        };
        var batch = new DataBatch { Items = { item } };
        return NodeHandlerOutput.ToPort(FlowConstants.PortNames.Output, batch);
    }
}
```

## 8. 第六步：编译并部署 DLL

- 编译后 DLL 输出到 `plugins/`（`Plugins:Path` 默认 `"../../plugins"`，相对 Host 内容根解析，见 `ServiceCollectionExtensions.cs:388`）。加入 `FlowEngine.Plugins.Standard` 项目则随其自动输出；独立项目请确认 `OutputPath` 指向 `plugins/`。
- **重启后端**即触发扫描加载（开发期 `dotnet run` 重启即可）。

## 9. 加载与隔离

- `PluginLoader.LoadNodes` 遍历 `plugins/*.dll`，对每个 DLL 创建 `PluginLoadContext`（独立 `AssemblyLoadContext`）加载，反射出所有非抽象 `INodeType` 实现并实例化注册。
- **单个插件加载失败只记 `LogWarning`，不中断启动**：捕获 `ReflectionTypeLoadException` / `BadImageFormatException` / `FileLoadException` / `TypeLoadException` 及其他异常，逐 DLL 跳过。
- 可配置 DLL SHA256 哈希白名单（`PluginLoader` 构造参数 `hashWhitelist`）：不在白名单的 DLL 记警告并跳过（具体是否启用取决于宿主注册配置）。

## 10. 自测清单

- [ ] 类有 `[NodeMeta]` 且 `TypeName` 唯一、非空。
- [ ] 端口用 `[Port]` 声明，名称引用 `FlowConstants.PortNames`。
- [ ] 必填参数有 `[Required]`，表达式参数有 `[Hint(PresentationHint.Expression)]`。
- [ ] 项目仅引用 `FlowEngine.Core`，无 `Application`/`Runtime` 引用。
- [ ] `ExecuteAsync` 用 `NodeHandlerOutput.ToPort/ToPorts` 返回，业务失败抛 `NodeExecutionException`。
- [ ] 已写对应测试（正常输出、空/缺参错误、`Script` 类型转换、输出符合 `DataBatch`→`DataItem`）。
- [ ] DLL 放入 `plugins/`，重启后端，节点出现在 `GET /api/node-types`。

> 哈希白名单在默认部署中**未启用**：宿主注册处 `new PluginLoader(pluginsPath, logger)` 未传入 `hashWhitelist`，依 `PluginLoader` 构造默认值为 `null`（见 `PluginLoader.cs:31`），即加载时跳过哈希校验、仅做异常隔离。如需强制校验，需向宿主注入点显式传入白名单集合。
