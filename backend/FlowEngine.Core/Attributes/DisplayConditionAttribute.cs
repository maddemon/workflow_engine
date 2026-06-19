namespace FlowEngine.Core.Attributes;

/// <summary>
/// 条件显隐：当指定属性的值匹配时，该参数才在前端显示。
/// 多个 <see cref="DisplayConditionAttribute"/> 之间为 OR 语义。
/// </summary>
/// <example>
/// <code>
/// [DisplayCondition(nameof(Method), "POST")]
/// [DisplayCondition(nameof(Method), "PUT")]
/// public JsonObject? Body { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class DisplayConditionAttribute : Attribute
{
    /// <summary>
    /// 依赖的属性名（PascalCase，Discoverer 会转为 camelCase）。
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// 匹配值。
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// 声明条件显隐规则。
    /// </summary>
    /// <param name="propertyName">依赖的属性名（使用 <c>nameof</c>）。</param>
    /// <param name="value">匹配值。</param>
    public DisplayConditionAttribute(string propertyName, object value)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}
