namespace FlowEngine.Core.ValueObjects;

/// <summary>
/// 节点定义 ID 值对象。
/// </summary>
/// <param name="Value">Guid 值。</param>
public readonly record struct NodeDefinitionId(Guid Value)
{
    /// <summary>
    /// 创建新的节点定义 ID。
    /// </summary>
    /// <returns>新的节点定义 ID。</returns>
    public static NodeDefinitionId New() => new(Guid.NewGuid());

    /// <summary>
    /// 从 Guid 创建节点定义 ID。
    /// </summary>
    /// <param name="value">Guid 值。</param>
    /// <returns>节点定义 ID。</returns>
    public static NodeDefinitionId From(Guid value) => new(value);

    /// <summary>
    /// 返回节点定义 ID 的字符串表示。
    /// </summary>
    /// <returns>字符串表示。</returns>
    public override string ToString() => Value.ToString();
}
