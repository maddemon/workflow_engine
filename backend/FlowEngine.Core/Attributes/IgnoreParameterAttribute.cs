namespace FlowEngine.Core.Attributes;

/// <summary>
/// 排除属性，标记该属性不作为节点参数。
/// </summary>
/// <example>
/// <code>
/// [IgnoreParameter]
/// public HttpClient SharedClient { get; set; } = new();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IgnoreParameterAttribute : Attribute
{
}
