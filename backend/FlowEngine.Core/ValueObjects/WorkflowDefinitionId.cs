namespace FlowEngine.Core.ValueObjects;

/// <summary>
/// 工作流定义 ID 值对象。
/// </summary>
/// <param name="Value">Guid 值。</param>
public readonly record struct WorkflowDefinitionId(Guid Value)
{
    /// <summary>
    /// 创建新的工作流定义 ID。
    /// </summary>
    /// <returns>新的工作流定义 ID。</returns>
    public static WorkflowDefinitionId New() => new(Guid.NewGuid());

    /// <summary>
    /// 从 Guid 创建工作流定义 ID。
    /// </summary>
    /// <param name="value">Guid 值。</param>
    /// <returns>工作流定义 ID。</returns>
    public static WorkflowDefinitionId From(Guid value) => new(value);

    /// <summary>
    /// 返回工作流定义 ID 的字符串表示。
    /// </summary>
    /// <returns>字符串表示。</returns>
    public override string ToString() => Value.ToString();
}
