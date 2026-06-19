namespace FlowEngine.Core.Attributes;

/// <summary>
/// 指定数组/列表属性的子项类型，用于生成嵌套的 <c>ItemDefinition</c>。
/// </summary>
/// <example>
/// <code>
/// [Item(typeof(SwitchCase))]
/// public List&lt;SwitchCase&gt; Cases { get; set; } = [];
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ItemAttribute : Attribute
{
    /// <summary>
    /// 子项 CLR 类型。
    /// </summary>
    public Type ItemType { get; }

    /// <summary>
    /// 指定子项类型。
    /// </summary>
    /// <param name="itemType">子项 CLR 类型。</param>
    public ItemAttribute(Type itemType)
    {
        ItemType = itemType ?? throw new ArgumentNullException(nameof(itemType));
    }
}
