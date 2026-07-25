using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Dtos;

/// <summary>
/// 节点类型描述 DTO（EXT-3）。
/// 用于 <see cref="FlowEngine.Host.Controllers.NodeTypesController"/> 返回，避免直接暴露 Core 实体
/// <c>NodeTypeDescriptor</c>。JSON 形状与实体保持一致，前端无需改动。
/// </summary>
public sealed class NodeTypeDescriptorDto
{
    /// <summary>节点类型唯一标识。</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>节点分类（本地化显示名）。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>节点分类原始键（英文，用于颜色映射等）。</summary>
    public string CategoryKey { get; init; } = string.Empty;

    /// <summary>节点图标。</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>执行模式。</summary>
    public ExecutionMode ExecutionMode { get; init; }

    /// <summary>参数定义列表。</summary>
    public IReadOnlyList<ParameterDefinitionDto> Parameters { get; init; } = [];

    /// <summary>端口定义列表。</summary>
    public IReadOnlyList<PortDefinitionDto> Ports { get; init; } = [];

    /// <summary>是否默认作为入口节点。</summary>
    public bool DefaultIsEntry { get; init; }
}

/// <summary>
/// 参数定义 DTO（对应 Core 实体 <c>ParameterDefinition</c>）。
/// 嵌套的值类型 <see cref="ValidationRule"/>/<see cref="DisplayRule"/>/<see cref="DataSchema"/> 直接复用，
/// 它们为纯数据结构，序列化形状与实体一致。
/// </summary>
public sealed class ParameterDefinitionDto
{
    /// <summary>参数名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>参数类型。</summary>
    public ParameterType Type { get; init; }

    /// <summary>默认值。</summary>
    public object? DefaultValue { get; init; }

    /// <summary>是否必填。</summary>
    public bool Required { get; init; }

    /// <summary>验证规则列表。</summary>
    public List<ValidationRule> ValidationRules { get; init; } = [];

    /// <summary>显示规则。</summary>
    public DisplayRule? DisplayRule { get; init; }

    /// <summary>凭据类型。</summary>
    public string? CredentialType { get; init; }

    /// <summary>选项列表。</summary>
    public List<OptionDto> Options { get; init; } = [];

    /// <summary>渲染提示。</summary>
    public PresentationHint? Hint { get; init; }

    /// <summary>Hint 组件的扩展属性。</summary>
    public IReadOnlyDictionary<string, object>? HintProperties { get; init; }

    /// <summary>字段描述。</summary>
    public string? Description { get; init; }

    /// <summary>资源类型。</summary>
    public string? ResourceType { get; init; }

    /// <summary>子项定义（Array 类型）。</summary>
    public ParameterDefinitionDto? ItemDefinition { get; init; }

    /// <summary>子字段列表（结构化数组子项）。</summary>
    public List<ParameterDefinitionDto> Fields { get; init; } = [];
}

/// <summary>
/// 端口定义 DTO（对应 Core 实体 <c>PortDefinition</c>）。
/// </summary>
public sealed class PortDefinitionDto
{
    /// <summary>端口名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>端口方向。</summary>
    public PortDirection Direction { get; init; }

    /// <summary>端口类型。</summary>
    public PortType Type { get; init; }

    /// <summary>是否必填。</summary>
    public bool Required { get; init; }

    /// <summary>端口条件表达式。</summary>
    public string? Condition { get; init; }

    /// <summary>允许的数据类型列表。</summary>
    public List<string> AllowedTypes { get; init; } = [];

    /// <summary>输出数据模式。</summary>
    public DataSchema? OutputSchema { get; init; }

    /// <summary>期望输入数据模式。</summary>
    public DataSchema? ExpectedSchema { get; init; }
}

/// <summary>
/// 选项 DTO（对应 Core 实体 <c>Option</c>）。
/// </summary>
public sealed class OptionDto
{
    /// <summary>显示标签。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>选项值。</summary>
    public object? Value { get; init; }
}
