using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 参数定义。
/// </summary>
public class ParameterDefinition
{
    /// <summary>
    /// 参数名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 参数类型。
    /// </summary>
    public ParameterType Type { get; set; }

    /// <summary>
    /// 默认值。
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// 是否必填。
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// 验证规则列表。
    /// </summary>
    public List<ValidationRule> ValidationRules { get; set; } = [];

    /// <summary>
    /// 显示规则。
    /// </summary>
    public DisplayRule? DisplayRule { get; set; }

    /// <summary>
    /// 凭据类型。
    /// </summary>
    public string? CredentialType { get; set; }

    /// <summary>
    /// 选项列表。
    /// </summary>
    public List<Option> Options { get; set; } = [];

    /// <summary>
    /// 渲染提示，指导前端使用何种组件渲染。未指定时由前端按 <see cref="Type"/> 自动推断。
    /// </summary>
    public PresentationHint? Hint { get; set; }

    /// <summary>
    /// 字段描述，展示在参数下方帮助用户理解用途。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 资源类型，用于 <see cref="ParameterType.Resource"/> 指定资源来源（如部门、应用、标签）。
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// 子项定义，用于 <see cref="ParameterType.Array"/> 定义列表每一行的结构。
    /// </summary>
    public ParameterDefinition? ItemDefinition { get; set; }

    /// <summary>
    /// 子字段列表，用于结构化数组子项（如 SwitchCase 的 Name/Label/Value）。
    /// 当 <see cref="ItemDefinition"/> 的 <see cref="Type"/> 为 <see cref="ParameterType.Json"/> 时，
    /// 此列表描述该 JSON 对象的各个字段。
    /// </summary>
    public List<ParameterDefinition> Fields { get; set; } = [];
}
