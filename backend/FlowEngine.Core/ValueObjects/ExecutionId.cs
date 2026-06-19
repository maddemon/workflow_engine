namespace FlowEngine.Core.ValueObjects;

/// <summary>
/// 执行 ID 值对象。
/// </summary>
/// <param name="Value">Guid 值。</param>
public readonly record struct ExecutionId(Guid Value)
{
    /// <summary>
    /// 创建新的执行 ID。
    /// </summary>
    /// <returns>新的执行 ID。</returns>
    public static ExecutionId New() => new(Guid.NewGuid());

    /// <summary>
    /// 从 Guid 创建执行 ID。
    /// </summary>
    /// <param name="value">Guid 值。</param>
    /// <returns>执行 ID。</returns>
    public static ExecutionId From(Guid value) => new(value);

    /// <summary>
    /// 返回执行 ID 的字符串表示。
    /// </summary>
    /// <returns>字符串表示。</returns>
    public override string ToString() => Value.ToString();
}
